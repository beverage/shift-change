using System;
using System.Collections.Generic;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ShiftChange
{
    /// <summary>
    /// Where Shift Change actually happens: a prefix on
    /// <see cref="Pawn_JobTracker.StartJob"/> that, when a pawn is about to
    /// start automatic work in a room whose stand they own, pushes that job
    /// back onto the queue and sends them to change clothes first. The same
    /// hook runs the return trip when a job takes them out of the room.
    ///
    /// Starting the swap from inside a StartJob prefix re-enters this patch,
    /// hence <see cref="inserting"/>. Why StartJob rather than
    /// <c>TryOpportunisticJob</c>, and why the insertion copies vanilla's own:
    /// docs/DESIGN.md, "Interception".
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_JobInterception
    {
        /// <summary>
        /// The INTERCEPTION kill switch, and nothing more — a STANDING
        /// decision, held deliberately by a player at the tweak panel or by
        /// the hot-reload quarantine. Accidents live in
        /// <see cref="faulted"/> instead; this one never latches on a throw.
        /// Read by <see cref="Prefix"/> and <see cref="Notify_StandFreed"/>
        /// only.
        ///
        /// The on-shift protections (the optimizer pause, the recolor
        /// execution guard) and the Change back button do NOT consult either
        /// flag: a pawn standing in a uniform still needs their uniform
        /// protected and still needs a way out of it, and gating those here
        /// meant a neighbour's exception re-opened the dye path on staged kit
        /// while simultaneously removing the player's only manual remedy.
        /// Those three key off the ledger instead. Keep it that way when
        /// adding hooks: ask "does this ACT on its own, or protect something
        /// already in progress?"
        /// </summary>
        // These five TweakValues SHIP, deliberately (decided 2026-08-17), and
        // the sweep of the debug menu does not extend to them: the bar is
        // destructiveness, not reachability, and they are how a player is
        // walked through diagnosing a report. docs/DESIGN.md, "Development
        // tooling".
        [TweakValue("ShiftChange")]
        public static bool Enabled = true;

        /// <summary>
        /// Interception has thrown too often and has taken itself out of
        /// service FOR THIS GAME. Distinct from <see cref="Enabled"/> on
        /// purpose, and the distinction is the whole point:
        ///
        /// <list type="bullet">
        /// <item><see cref="Enabled"/> is a standing decision — the player's
        /// or the developer's — and includes the hot-reload quarantine
        /// (<c>HarmonyInit.OnEditCompileReload</c>), which MUST survive a save
        /// load, because the twin JobDriver that wedges a tracker is still
        /// loaded.</item>
        /// <item>This is an accident, and accidents should not outlive the
        /// game they happened in.</item>
        /// </list>
        ///
        /// They were one flag until 2026-08-14, and a single throw therefore
        /// disabled the mod for the whole process — through a save load, a new
        /// colony, everything, until RimWorld was restarted. With
        /// <c>Log.Error</c> not even opening the log window outside dev mode
        /// (<c>Log.cs:151</c>), what a player saw was colonists quietly never
        /// changing again, with nothing to connect it to.
        ///
        /// Cleared by <see cref="ResetSessionState"/>, so loading a save
        /// re-arms.
        /// </summary>
        internal static bool faulted;

        /// <summary>How many throws have been counted this game.</summary>
        internal static int faultCount;

        /// <summary>
        /// TEST SEAM. While positive, the next N interceptions throw from
        /// inside <see cref="TryInsertSwap"/> and decrement it. Set only by
        /// <see cref="DebugTools_LifecycleHarness"/>; zero in every other
        /// circumstance, so the cost in play is one int comparison per
        /// interception.
        ///
        /// It exists because the alternative was worse. The fault latch is
        /// the mod's response to something going wrong, and there is no honest
        /// way to test a response to a throw without a throw — calling
        /// <see cref="NoteFault"/> directly would exercise the counter while
        /// proving nothing about whether a real exception in interception
        /// actually reaches it. This makes the harness drive the same
        /// <see cref="Prefix"/> catch block a live failure would.
        /// </summary>
        internal static int injectFaults;

        /// <summary>
        /// Throws tolerated before <see cref="faulted"/> latches. Not one:
        /// these catch blocks also fire for a NEIGHBOUR's exception thrown
        /// through our frame, and taking a mod out of service for the rest of
        /// the colony over one transient throw somewhere else is a worse
        /// failure than the throw. Repeated throws are a different claim, and
        /// that is what this counts.
        /// </summary>
        [TweakValue("ShiftChange", 1f, 20f)]
        public static int FaultLimit = 3;

        /// <summary>
        /// Record a throw, and latch off if they are piling up. Always tells
        /// the PLAYER when it latches — a dev-log line is invisible without
        /// dev mode, and "my colonists stopped changing" with no visible cause
        /// is the worst version of this bug.
        /// </summary>
        /// <param name="pawn">
        /// Whoever this happened to. Always pass it if you have it: a throw
        /// with no name in it tells a player their colonists stopped changing
        /// and nothing about which one, and tells us nothing about whether the
        /// fault is one pawn's odd state or everyone's.
        /// </param>
        internal static void NoteFault(string where, Exception e, Pawn pawn = null)
        {
            faultCount++;
            string who = pawn != null ? " for " + pawn.LabelShort : "";
            Log.Error("[ShiftChange] " + where + who + " threw (" + faultCount + " of "
                      + FaultLimit + " before disabling): " + e);
            if (faultCount < FaultLimit || faulted)
            {
                return;
            }
            faulted = true;
            Log.Error("[ShiftChange] interception disabled for this game after " + faultCount
                      + " faults. Load a save or start a new game to re-arm it.");
            Messages.Message("ShiftChange.Faulted".Translate(),
                MessageTypeDefOf.NegativeEvent, historical: true);
        }

        /// <summary>Log every decision, not just the swaps. Noisy.</summary>
        [TweakValue("ShiftChange")]
        public static bool Verbose = false;

        /// <summary>
        /// Minimum ticks before a pawn may be considered again after a swap
        /// was wanted but could not be started (stand unreachable, reserved,
        /// forbidden). Without it an unreachable stand re-triggers on every
        /// job assignment, which is a tight loop rather than a slow one.
        /// </summary>
        [TweakValue("ShiftChange", 0f, 5000f)]
        public static int RetryCooldownTicks = 600;

        internal static bool inserting;

        internal static readonly Dictionary<int, int> LastBlockedTick = new Dictionary<int, int>();

        /// <summary>
        /// Pawn → the stand they were changed back at by the change-back
        /// button (<see cref="Patch_ChangeBackGizmo"/>): the ROOM-EXIT LATCH.
        /// While it holds, that pawn will not dress again in that stand's
        /// room; it drops the moment they are seen starting a job anywhere
        /// else, and drafting drops it outright.
        ///
        /// The STAND is stored, not the Room, so the room is re-derived live
        /// on both sides of every comparison and a rebuilt wall cannot leave
        /// a stale Room object behind. Keyed by Pawn reference rather than
        /// thingIDNumber on purpose: an object reference cannot collide
        /// across a save load the way a recycled ID can, so a stale entry is
        /// inert rather than wrong. SessionGuard still clears it so old-game
        /// pawns don't leak. Why positional rather than a countdown:
        /// docs/DESIGN.md, "Change back".
        /// </summary>
        internal static readonly Dictionary<Pawn, Thing> ChangedBackAt = new Dictionary<Pawn, Thing>();

        /// <summary>
        /// Drops the latch as soon as the pawn is starting a job while
        /// standing outside the room they were changed out of. Called at the
        /// top of every interception, so it samples at every job boundary
        /// for every pawn — no tick hook needed. (A doorway is its own Room,
        /// <c>Room.IsDoorway</c>, so a pawn caught mid-door reads as "left":
        /// a hair early, and they are in fact leaving.)
        /// </summary>
        internal static void UpdateChangeBackLatch(Pawn pawn)
        {
            Thing stand;
            if (!ChangedBackAt.TryGetValue(pawn, out stand))
            {
                return;
            }
            if (stand == null || !stand.Spawned)
            {
                ChangedBackAt.Remove(pawn);
                return;
            }
            Room standRoom = stand.GetRoom();
            if (standRoom == null || pawn.GetRoom() != standRoom)
            {
                ChangedBackAt.Remove(pawn);
            }
        }

        /// <summary>
        /// Would dressing in <paramref name="room"/> right now be the very
        /// re-dress the player just cancelled? Scoped to the latched room on
        /// purpose: a latched pawn who takes a job in a DIFFERENT room is
        /// leaving, and the uniform waiting for them there is a different
        /// uniform — blocking that would strand the feature at one room.
        /// </summary>
        internal static bool IsLatchedIn(Pawn pawn, Room room)
        {
            Thing stand;
            if (!ChangedBackAt.TryGetValue(pawn, out stand) || stand == null || !stand.Spawned)
            {
                return false;
            }
            return stand.GetRoom() == room;
        }

        /// <summary>
        /// Called by SessionGuard when the loaded game changes. A stale
        /// <see cref="LastBlockedTick"/> entry is WRONG in the new game, not
        /// merely leaked: docs/DESIGN.md, "State across save, load and
        /// uninstall".
        /// </summary>
        internal static void ResetSessionState()
        {
            LastBlockedTick.Clear();
            ChangedBackAt.Clear();
            // Re-arm after a fault. Deliberately NOT `Enabled = true`: that
            // flag also carries the hot-reload quarantine, and the wedging
            // twin JobDriver is still loaded after a save load, so re-arming
            // it here would resurrect the 2026-08-08 tracker wedge.
            faultCount = 0;
            faulted = false;
        }

        /// <summary>Toggle for the mid-job catch-up below.</summary>
        [TweakValue("ShiftChange")]
        public static bool DressMidJob = true;

        /// <summary>
        /// A stand just returned to availability. If a colonist is ALREADY
        /// working bare in its room, because they took the job while every
        /// stand was checked out, interrupt them to change now and resume the
        /// job afterwards. Only a job whose def is both suspendable and
        /// casually interruptible, which leaves a doctor mid-treatment alone.
        /// docs/DESIGN.md, "The mid-job catch-up".
        /// </summary>
        public static void Notify_StandFreed(CompShiftStand stand, Pawn except)
        {
            if (!Enabled || faulted || !DressMidJob)
            {
                return;
            }
            try
            {
                TryDressMidJob(stand, except);
            }
            catch (Exception e)
            {
                // Called from inside another pawn's job cleanup — breaking
                // THAT would turn a convenience into a job-system fault.
                Log.Error("[ShiftChange] stand-freed catch-up threw at "
                          + (stand?.parent?.LabelShort ?? "an unknown stand")
                          + ", freed by "
                          + (except?.LabelShort ?? "nobody") + ": " + e);
            }
        }

        internal static void TryDressMidJob(CompShiftStand stand, Pawn except)
        {
            Thing parent = stand?.parent;
            if (parent == null || !parent.Spawned || stand.OnShift)
            {
                return;
            }
            Map map = parent.Map;
            if (map == null || map.dangerWatcher.DangerRating != StoryDanger.None)
            {
                return;
            }
            Room room = parent.GetRoom();
            if (room == null)
            {
                return;
            }

            Pawn best = null;
            int bestDistance = int.MaxValue;
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (pawn == except || !pawn.RaceProps.Humanlike
                    || pawn.Drafted || pawn.Downed || pawn.InMentalState)
                {
                    continue;
                }
                // The duty guard applies here too, and this path needs it
                // said out loud: the job filter below rejects a job that is
                // not casually interruptible, but a duty job is not required
                // to say so, and a freed stand reaching across the room to
                // pull a ritual participant off their duty is the same
                // stranding by another route.
                if (pawn.GetLord() != null || pawn.mindState?.duty != null)
                {
                    continue;
                }
                if (CompShiftStand.OnShiftStandFor(pawn) != null)
                {
                    continue;
                }
                Job job = pawn.CurJob;
                if (job == null || job.def == ShiftChangeDefOf.ShiftChange_SwapAtStand
                    || job.playerForced
                    || !job.def.suspendable || !job.def.casualInterruptible)
                {
                    continue;
                }
                WorkGiverDef giver = job.workGiverDef;
                // The ignored-giver gate applies here too. StartJob's copy
                // only stops a pawn being dressed as they TAKE such a job; a
                // freed stand would otherwise reach across and pull them off
                // one mid-haul, which is the same wardrobe detour by another
                // route.
                bool forWork = giver?.workType != null && !giver.emergency
                    && !JobRoomTargets.Ignored(giver)
                    && stand.HandlesWork(giver.workType);
                // The joy twin: a colonist already at recreation bare when
                // this stand freed — the pool-party case — is caught up the
                // same way. Joy jobs carry no workGiverDef, so the two arms
                // are disjoint by construction.
                bool forRecreation = !forWork && giver == null
                    && stand.HandlesRecreation() && IsRecreationJob(job)
                    // The dress arm's two guards apply here too: never pull a
                    // pawn out of bed, and never treat the map-spanning
                    // outdoor room as this stand's room.
                    && !OnABed(pawn) && !room.TouchesMapEdge;
                // NO SLEEP ARM HERE, and it is not an omission. Catch-up
                // interrupts a pawn already doing the thing the freed stand
                // dresses for, and the sleep equivalent is a colonist asleep
                // in bed — who is excluded twice over before the arms are even
                // consulted: LayDown sets casualInterruptible false, which the
                // job filter above rejects, and the OnABed guard would reject
                // them again. Waking someone to put pyjamas on is the one
                // catch-up nobody wants.
                if (!forWork && !forRecreation)
                {
                    continue;
                }
                // Resolve by job CLASS, not by which arm matched — the same
                // rule as TryInsertSwap's shared resolver, and it must be:
                // VisitSickPawn is the one vanilla job that is BOTH (Doctor
                // work with joyKind Social), and resolving it A-first here
                // while StartJob resolved it B-first re-opened a bounded
                // dress/undress churn per stand-freed event (fix-verify,
                // 2026-08-15). Every site answers "where does this job
                // happen" identically or the two answers fight.
                IntVec3 target = IsRecreationJob(job) ? JoyTargetCell(job, map) : TargetCell(job, map);
                if (!target.IsValid || target.GetRoom(map) != room)
                {
                    continue;
                }
                if (!stand.CanBeClaimedBy(pawn) || !SwapPlan.WouldDress(pawn, stand.Stand))
                {
                    continue;
                }
                // The second dress path, and it needs the same latch: without
                // it, a stand returning to the pool re-dresses the very pawn
                // the player just pulled out of this room.
                if (IsLatchedIn(pawn, room))
                {
                    continue;
                }
                // Outside this pawn's allowed area, so the swap would die on
                // its first toil (see StandForbiddenTo). Interrupting them for
                // it is a detour that ends where it started.
                if (StandForbiddenTo(parent, pawn))
                {
                    continue;
                }
                if (!pawn.CanReserveAndReach(parent, PathEndMode.InteractionCell, Danger.Deadly))
                {
                    continue;
                }
                int distance = pawn.Position.DistanceToSquared(parent.Position);
                if (distance < bestDistance)
                {
                    best = pawn;
                    bestDistance = distance;
                }
            }

            if (best == null)
            {
                return;
            }

            Job swap = JobMaker.MakeJob(ShiftChangeDefOf.ShiftChange_SwapAtStand, parent);
            best.jobs.StartJob(swap, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: true, cancelBusyStances: true, null, JobTag.ChangingApparel);
            if (Verbose)
            {
                Log.Message($"[ShiftChange] catch-up: {best.LabelShort} interrupted to dress at {parent.LabelShort}");
            }
        }

        internal static List<ThingDef> standDefs;

        /// <summary>
        /// Every def the XML patched into the shift system: the
        /// <see cref="Building_OutfitStand"/> class family, filtered to defs
        /// actually carrying our comp. Class alone is not enough — a stand
        /// mod we did not patch has no comp, no ledger, and no business in
        /// the pool — and a hardcoded defName stopped being true when
        /// Outfit Stands Plus' powered stands joined. The XML decides
        /// coverage; this list only reads it. Resolved once: the def set is
        /// fixed for the process lifetime.
        /// </summary>
        internal static List<ThingDef> StandDefs
        {
            get
            {
                if (standDefs == null)
                {
                    standDefs = new List<ThingDef>();
                    List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
                    for (int i = 0; i < all.Count; i++)
                    {
                        ThingDef def = all[i];
                        if (def.thingClass != null
                            && typeof(Building_OutfitStand).IsAssignableFrom(def.thingClass)
                            && def.GetCompProperties<CompProperties_ShiftStand>() != null)
                        {
                            standDefs.Add(def);
                        }
                    }
                }
                return standDefs;
            }
        }

        // ReSharper disable once InconsistentNaming — Harmony field/instance injection.
        public static bool Prefix(Job newJob, JobTag? tag, Pawn ___pawn, Pawn_JobTracker __instance)
        {
            // BEFORE the gate, not after. The fault latch is cleared by
            // ResetSessionState, which only runs from here — gate first and a
            // faulted mod could never notice the game had changed, so the
            // latch would never lift and "cleared on save load" would be a
            // comment rather than a behaviour. One reference comparison.
            SessionGuard.Ensure();

            if (!Enabled || faulted || inserting)
            {
                return true;
            }

            // A throw here would break job assignment for every pawn on the
            // map — a bricked colony, not a bad log line. Fail open, always.
            try
            {
                return !TryInsertSwap(newJob, tag, ___pawn, __instance);
            }
            catch (Exception e)
            {
                NoteFault("job interception", e, ___pawn);
                return true;
            }
        }

        /// <summary>
        /// The prefix's whole decision, one call per step. The ORDER of the
        /// calls is behaviour: where a position matters, the note beside the
        /// call says why. Each step is declared below in the order it runs, so
        /// "above" and "below" in the steps' comments mean earlier and later.
        /// </summary>
        /// <returns>true if a swap job was started in place of the incoming one.</returns>
        internal static bool TryInsertSwap(Job job, JobTag? tag, Pawn pawn, Pawn_JobTracker tracker)
        {
            if (injectFaults > 0)
            {
                injectFaults--;
                throw new Exception("[ShiftChange] injected test fault");
            }
            SessionGuard.Ensure();
            Map map = MapToDecideOn(job, pawn);
            if (map == null)
            {
                return false;
            }

            // Sampled here, above every other gate, so the latch is tested at
            // EVERY job boundary — including jobs that return below (danger,
            // player-forced, cooldown). Leaving the room is what ends it, and
            // the pawn must not have to take a dressable job to be noticed
            // leaving.
            UpdateChangeBackLatch(pawn);

            // The next four gates hold in BOTH directions, which is why they
            // sit above the return trip. Each lets the job start untouched.
            if (ExemptInEitherDirection(job))
            {
                return false;
            }
            if (UnderLordDuty(pawn, job))
            {
                return false;
            }
            if (MidSwap(pawn, tracker, job))
            {
                return false;
            }
            if (CoolingDown(pawn))
            {
                return false;
            }

            IntVec3 target = SharedTargetCell(job, map);

            // The return trip. Checked first: a pawn already in uniform who is
            // leaving should change back whatever the new job is.
            CompShiftStand onShift = CompShiftStand.OnShiftStandFor(pawn);
            if (onShift != null)
            {
                return ReturnTrip(job, tag, pawn, tracker, onShift, target, map);
            }

            // BELOW the return trip and above every dress arm: under threat,
            // nobody changes in, but changing back still happens.
            if (MapUnderThreat(map))
            {
                return false;
            }

            // The work arm runs first. The sleep arm turns medical lay-downs
            // away because the work arm has already declined them, and
            // VisitSickPawn, Doctor work that carries a joyKind, would
            // otherwise go to the recreation arm.
            WorkTypeDef work = WorkTypeFor(pawn, job, tag);
            if (work != null)
            {
                return WorkArm(job, tag, pawn, tracker, work, target, map);
            }
            if (IsRestJob(job))
            {
                return SleepArm(job, tag, pawn, tracker, target, map);
            }
            return RecreationArm(job, tag, pawn, tracker, map);
        }

        /// <summary>
        /// The map this job boundary is decided on, or null when the job or the
        /// pawn is not one this mod acts on.
        /// </summary>
        internal static Map MapToDecideOn(Job job, Pawn pawn)
        {
            if (job == null || pawn == null || Current.ProgramState != ProgramState.Playing)
            {
                return null;
            }
            if (!pawn.Spawned || pawn.Faction != Faction.OfPlayer || !pawn.RaceProps.Humanlike)
            {
                return null;
            }
            if (job.def == ShiftChangeDefOf.ShiftChange_SwapAtStand)
            {
                return null;
            }
            // A drafted pawn is being told where to be. Never send them to a
            // wardrobe, in either direction.
            if (pawn.Drafted || pawn.Downed || pawn.InMentalState)
            {
                return null;
            }
            return pawn.MapHeld;
        }

        /// <summary>
        /// The job starts as it is: no dressing for it, and no changing back
        /// first.
        /// </summary>
        internal static bool ExemptInEitherDirection(Job job)
        {
            // Never divert a direct order or an emergency response, in EITHER
            // direction: the uniform rides along and the next ordinary job
            // settles it. Givers this mod ignores (JobRoomTargets.Ignored) and
            // the giver-less follow-ups a haul leaves (RidesAlong) pass the
            // same way. One opt-in carve-out: with the medical emergency
            // setting on, MEDICAL emergencies stop being exempt, in both
            // directions, and firefighting never does. Why this sits above the
            // return trip: docs/DESIGN.md, "The gates"; the setting: "The
            // sleep branch".
            return job.playerForced
                   || (job.workGiverDef?.emergency == true
                       && !EmergencyDressingAllowed(job.workGiverDef))
                   || JobRoomTargets.Ignored(job.workGiverDef)
                   || JobRoomTargets.RidesAlong(job);
        }

        /// <summary>
        /// A lord or a duty holds the pawn, so neither direction may divert
        /// them.
        /// </summary>
        internal static bool UnderLordDuty(Pawn pawn, Job job)
        {
            // A PAWN UNDER A LORD DUTY IS SPOKEN FOR, in both directions: the
            // lord reissues their job on its own clock, so a swap started here
            // is pre-empted rather than finished, and its replacement deferred
            // again. Declining is broader than vanilla by one case, on purpose.
            // It cannot latch a colonist out, because the lord's cleanup clears
            // duty and lord together. The duty is tested beside the lord
            // because a mod may assign one with no Lord to own it. Why, and
            // the stranding that taught it: docs/DESIGN.md, "The gates".
            Lord lord = pawn.GetLord();
            if (lord != null || pawn.mindState?.duty != null)
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] {pawn.LabelShort} is under a duty " +
                                $"({lord?.LordJob?.GetType().Name ?? "no lord"}) — " +
                                $"not diverted for {job.def.defName}");
                }
                return true;
            }
            return false;
        }

        /// <summary>The pawn is already on their way to a stand.</summary>
        internal static bool MidSwap(Pawn pawn, Pawn_JobTracker tracker, Job job)
        {
            // NEVER STACK A SWAP ON A SWAP. MapToDecideOn catches our own
            // re-entry; this catches the other direction, a pawn already
            // walking to a stand who is handed a new job by anything at all.
            // Why it is let through rather than deferred: docs/DESIGN.md,
            // "The gates".
            if (tracker.curJob?.def == ShiftChangeDefOf.ShiftChange_SwapAtStand)
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] {pawn.LabelShort} is mid-swap — " +
                                $"{job.def.defName} is not deferred on top of it");
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// A swap was wanted for this pawn and could not start, less than
        /// <see cref="RetryCooldownTicks"/> ago.
        /// </summary>
        internal static bool CoolingDown(Pawn pawn)
        {
            int now = Find.TickManager.TicksGame;
            int blocked;
            return LastBlockedTick.TryGetValue(pawn.thingIDNumber, out blocked)
                   && now - blocked < RetryCooldownTicks;
        }

        /// <summary>
        /// Where the job happens, read by job class, so both directions get
        /// the same answer.
        /// </summary>
        internal static IntVec3 SharedTargetCell(Job job, Map map)
        {
            // ONE room resolver per job class, consumed by BOTH directions:
            // with split reads the two arms disagreed about where a job
            // happens, and a pawn ping-ponged between stand and work forever
            // (docs/DESIGN.md, "The recreation branch").
            return IsRecreationJob(job) ? JoyTargetCell(job, map) : TargetCell(job, map);
        }

        /// <summary>
        /// The pawn is on shift at <paramref name="onShift"/>: change them back
        /// before this job, or let the uniform ride along. True if a swap
        /// started.
        /// </summary>
        internal static bool ReturnTrip(Job job, JobTag? tag, Pawn pawn, Pawn_JobTracker tracker,
                                        CompShiftStand onShift, IntVec3 target, Map map)
        {
            Room standRoom = onShift.parent.GetRoom();
            if (standRoom == null)
            {
                return false;
            }
            // NEVER PULL A PAWN OUT OF BED TO CHANGE BACK, where "out of bed"
            // means the job would take them out of it: since sleepwear, an
            // on-shift pawn in bed is the ordinary state all night, and in-bed
            // joy and re-queued lay-downs arrive here as fresh jobs.
            // StaysInBed, not pawn.InBed(), which answers wrongly at exactly
            // these boundaries (docs/DESIGN.md, "The sleep branch").
            if (StaysInBed(pawn, job))
            {
                return false;
            }
            // Eating cannot use the room test below: an ingest job's targetA
            // is the FOOD, and the chair is chosen during the job. Food on the
            // pawn means eat as-is; anything else is a sit-down break, so
            // change out first (decided 2026-08-08; docs/DESIGN.md, "Meal
            // breaks").
            if (IsIngestJob(job))
            {
                return MealBreak(job, tag, pawn, tracker, onShift, standRoom, target, map);
            }
            if (!target.IsValid || target.GetRoom(map) == standRoom)
            {
                // Still working in the room, or the job's location is
                // unreadable (queue-based jobs — hauling, harvesting).
                // Staying dressed is the safe answer either way.
                return false;
            }
            // An errand slotted in AHEAD of this stand's own work: they are
            // coming straight back to it, so changing out first is a round
            // trip to the stand for nothing. Below the meal branch on
            // purpose, which keeps its own policy. See QueuedAheadOfServedWork.
            if (QueuedAheadOfServedWork(job, tracker, onShift, standRoom, map))
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] {pawn.LabelShort} keeps the outfit on for " +
                                $"{job.def.defName}: {onShift.parent.LabelShort}'s work is queued next");
                }
                return false;
            }
            return Insert(pawn, tracker, onShift, job, tag, "return");
        }

        /// <summary>
        /// The return trip for an ingest job: eat in uniform, or change out
        /// first. True if a swap started.
        /// </summary>
        internal static bool MealBreak(Job job, JobTag? tag, Pawn pawn, Pawn_JobTracker tracker,
                                       CompShiftStand onShift, Room standRoom, IntVec3 target, Map map)
        {
            // Eat-as-is is a WORK and RECREATION rule, and a sleep stand must
            // not inherit it: at the wake-up the pawn is standing at the
            // stand, so it would only send them out in sleepwear and back
            // (docs/DESIGN.md, "Meal breaks").
            if (FoodSourceIsOnPawn(job, pawn) && !onShift.HandlesRest())
            {
                return false;
            }
            // The sit-down-break policy is a WORK-room rule. A recreation
            // stand's room stocks its own drinks, so same-room food on a stand
            // that serves recreation is part of the break: stay dressed. Both
            // carve-outs read the stand's CURRENT configuration, not why the
            // pawn was dressed (docs/DESIGN.md, "Meal breaks").
            if (onShift.HandlesRecreation()
                && target.IsValid && target.GetRoom(map) == standRoom)
            {
                return false;
            }
            return Insert(pawn, tracker, onShift, job, tag, "return");
        }

        /// <summary>The map is under threat, so no arm dresses anyone.</summary>
        internal static bool MapUnderThreat(Map map)
        {
            // No DRESSING while the map is under threat. Vanilla gets this from
            // think-tree position (Humanlike.xml:302-306), and this hook sits
            // downstream of the tree, so the gate has to be ours. One-
            // directional and BELOW the return trip on purpose (decided
            // 2026-08-31), and never a push: nothing reacts to danger
            // starting. Why, and the raid that taught it: docs/DESIGN.md,
            // "The gates".
            return map.dangerWatcher.DangerRating != StoryDanger.None;
        }

        /// <summary>
        /// The work type the work arm dresses this job for, or null when the
        /// work arm does not take the job.
        /// </summary>
        internal static WorkTypeDef WorkTypeFor(Pawn pawn, Job job, JobTag? tag)
        {
            // MEDICAL BED REST ENTERS THE WORK ARM HERE, and has to be HANDED
            // its work type: the patient givers are NonScanJob overrides, and
            // JobGiver_Work stamps workGiverDef only on its scanner paths, so a
            // medical lay-down arrives with none. Reading the giver alone left
            // both patient rows dead from the day the sleep trigger shipped
            // (docs/DESIGN.md, "The sleep branch").
            return job.workGiverDef?.workType ?? MedicalRestWorkType(pawn, job, tag);
        }

        /// <summary>
        /// Dress for <paramref name="work"/> at a stand in the job's room. True
        /// if a swap started.
        /// </summary>
        internal static bool WorkArm(Job job, JobTag? tag, Pawn pawn, Pawn_JobTracker tracker,
                                     WorkTypeDef work, IntVec3 target, Map map)
        {
            if (!target.IsValid)
            {
                return false;
            }

            Room room = target.GetRoom(map);
            if (room == null)
            {
                return false;
            }

            // The change-back latch: they were pulled out of this very
            // room and have not left it since, so dressing again here is
            // the cycle the player just stopped.
            if (IsLatchedIn(pawn, room))
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] {pawn.LabelShort} stays changed out — " +
                                "not left the room since the change-back order");
                }
                return false;
            }

            CompShiftStand stand = FindAvailableStand(room, pawn, work);
            if (stand == null)
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] no free {work.defName} stand in {room.Role?.defName ?? "unroled"} " +
                                $"room for {pawn.LabelShort} ({job.def.defName})");
                }
                return false;
            }

            return Insert(pawn, tracker, stand, job, tag, "dress");
        }

        /// <summary>
        /// Dress for bed at a sleep stand in the bed's room. True if a swap
        /// started.
        /// </summary>
        internal static bool SleepArm(Job job, JobTag? tag, Pawn pawn, Pawn_JobTracker tracker,
                                      IntVec3 target, Map map)
        {
            // The SLEEP arm. Disjoint from the other two by construction: a
            // lay-down job carries no joyKind, and the tag test below keeps
            // MEDICAL bed rest out. Reaching it means the work arm declined
            // the job (MedicalRestWorkType resolved no work type), and a
            // pyjama stand must not then take it as ordinary sleep. The
            // workGiverDef limb is a conservative catch for a modded giver
            // with a null workType. Do not add
            // HealthAIUtility.ShouldSeekMedicalRest here: it cannot tell "hurt"
            // from "night", and once killed bedtime for every wounded colonist.
            // Why each: docs/DESIGN.md, "The sleep branch".
            if (job.workGiverDef != null || tag == JobTag.RestingForMedicalReasons)
            {
                return false;
            }
            // Never pull a pawn OUT of bed to dress for bed: a sleeper who
            // stirs into LayDownAwake, or whose in-bed job re-queues LayDown,
            // starts a fresh lay-down in the same bed. OnABed, not
            // pawn.InBed() (docs/DESIGN.md, "The sleep branch").
            if (OnABed(pawn))
            {
                return false;
            }
            if (!target.IsValid)
            {
                return false;
            }
            Room bedRoom = target.GetRoom(map);
            // The recreation arm's outdoor guard, needed for the same
            // reason: an outdoor cell resolves the one map-spanning,
            // edge-touching room rather than null, so a sleep stand in
            // open ground would serve every bedroll on the map.
            if (bedRoom == null || bedRoom.TouchesMapEdge)
            {
                return false;
            }
            if (IsLatchedIn(pawn, bedRoom))
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] {pawn.LabelShort} stays changed out — " +
                                "not left the room since the change-back order");
                }
                return false;
            }
            CompShiftStand restStand = FindAvailableStand(bedRoom, pawn, null, StandTrigger.Rest);
            if (restStand == null)
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] no free sleep stand in {bedRoom.Role?.defName ?? "unroled"} " +
                                $"room for {pawn.LabelShort} ({job.def.defName})");
                }
                return false;
            }
            return Insert(pawn, tracker, restStand, job, tag, "dress-rest");
        }

        /// <summary>
        /// Dress for recreation at a stand in the room where it happens, if the
        /// job is recreation at all. True if a swap started.
        /// </summary>
        internal static bool RecreationArm(Job job, JobTag? tag, Pawn pawn, Pawn_JobTracker tracker,
                                           Map map)
        {
            // The RECREATION arm. Joy jobs carry no workGiverDef, so it is
            // disjoint from the work arm by construction; the room is read
            // B-first (JoyTargetCell), where the pawn will sit.
            if (!IsRecreationJob(job))
            {
                return false;
            }
            // Vanilla hands joy to pawns lying in bed so they stay there, so
            // in-bed joy never pulls a patient out to a wardrobe. OnABed, not
            // pawn.InBed() (docs/DESIGN.md, "The recreation branch").
            if (OnABed(pawn))
            {
                return false;
            }
            IntVec3 joyTarget = JoyTargetCell(job, map);
            if (!joyTarget.IsValid)
            {
                return false;
            }
            Room joyRoom = joyTarget.GetRoom(map);
            // Outdoor cells resolve the one map-spanning room, never null, so
            // without this a stand in open ground would serve every outdoor
            // joy job on the map. A walled roofless yard is its own room and
            // stays eligible (docs/DESIGN.md, "The recreation branch").
            if (joyRoom == null || joyRoom.TouchesMapEdge)
            {
                return false;
            }
            if (IsLatchedIn(pawn, joyRoom))
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] {pawn.LabelShort} stays changed out — " +
                                "not left the room since the change-back order");
                }
                return false;
            }
            CompShiftStand recStand = FindAvailableStand(joyRoom, pawn, null, StandTrigger.Recreation);
            if (recStand == null)
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] no free recreation stand in {joyRoom.Role?.defName ?? "unroled"} " +
                                $"room for {pawn.LabelShort} ({job.def.defName})");
                }
                return false;
            }
            return Insert(pawn, tracker, recStand, job, tag, "dress-rec");
        }

        internal static bool Insert(Pawn pawn, Pawn_JobTracker tracker, CompShiftStand stand,
                                   Job originalJob, JobTag? tag, string direction)
        {
            // NEVER WALK A PAWN OUT OF THEIR ALLOWED AREA TO CHANGE. The swap
            // would not get there anyway: it fails on its first toil, inside
            // StartJob (see StandForbiddenTo). The dress paths already pass such
            // a stand over, so in practice this is the RETURN TRIP's gate, meal
            // breaks included, and the uniform rides along until the stand is
            // back inside the area. The cooldown is what stops the next job
            // boundary asking again; the stand's inspect pane says why.
            if (StandForbiddenTo(stand.parent, pawn))
            {
                LastBlockedTick[pawn.thingIDNumber] = Find.TickManager.TicksGame;
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] {direction} wanted for {pawn.LabelShort} but " +
                                $"{stand.parent.LabelShort} is outside their allowed area");
                }
                return false;
            }
            if (!pawn.CanReserveAndReach(stand.parent, PathEndMode.InteractionCell, Danger.Deadly))
            {
                LastBlockedTick[pawn.thingIDNumber] = Find.TickManager.TicksGame;
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] {direction} wanted for {pawn.LabelShort} but " +
                                $"{stand.parent.LabelShort} is unreachable or reserved");
                }
                return false;
            }

            // Reserve the deferred job's targets NOW, as vanilla's own
            // opportunistic deferral does: our prefix skips the original
            // StartJob, so nothing else would, and another pawn could take
            // the patient or bench during the walk-and-change. Safe to hold
            // while queued, since every queue-clearing path releases them.
            // The dry run must run with curJob set to the deferred job, as
            // StartJob would have it: drivers such as JobDriver_SocialRelax
            // reserve against pawn.CurJob, and with it unset the reserve fails
            // and the divert silently degrades to ride-along. Both found in
            // play; docs/DESIGN.md, "Deferred jobs carry their reservations".
            JobDriver reservationDriver = originalJob.MakeDriver(pawn);
            Job prevCurJob = tracker.curJob;
            bool reserved;
            try
            {
                tracker.curJob = originalJob;
                reserved = reservationDriver.TryMakePreToilReservations(errorOnFailed: false);
            }
            finally
            {
                tracker.curJob = prevCurJob;
            }
            if (!reserved)
            {
                // Lost the race for the target within this very tick. Do not
                // detour for a job that can no longer run — drop any partial
                // claims and let vanilla start and fail it the ordinary way.
                pawn.ClearReservationsForJob(originalJob);
                return false;
            }

            Job swap = JobMaker.MakeJob(ShiftChangeDefOf.ShiftChange_SwapAtStand, stand.parent);

            // Start the swap BEFORE enqueueing the displaced job, the reverse
            // of vanilla's order: if StartJob threw after an enqueue, the
            // fail-open catch would leave one Job object in two places.
            // docs/DESIGN.md, "Inserting a job ahead of another".
            // InterruptForced, not None. When a pawn already has a job — which
            // is every override path, and most of them in play —
            // Pawn_JobTracker warns "starting job ... without a specific job
            // end condition" and then substitutes InterruptForced itself
            // (:288-292). Passing None was therefore never a behaviour
            // difference, only a warning per swap in the player's log, which
            // is noise they cannot act on and which buries the lines that
            // matter. Says exactly what the sibling call at TryDressMidJob
            // already says.
            inserting = true;
            try
            {
                tracker.StartJob(swap, JobCondition.InterruptForced, null,
                    resumeCurJobAfterwards: false,
                    cancelBusyStances: true, null, JobTag.ChangingApparel);
            }
            catch (Exception e)
            {
                // A throw from inside StartJob — another mod's prefix, or
                // (2026-08-08, in play) a hot-reload twin failing JobDriver's
                // protected base ctor — unwinds AFTER vanilla has ended the
                // pawn's current job and set curJob but BEFORE curDriver
                // exists. Left alone, that tracker NREs every tick forever.
                // Disable ourselves, release both jobs' claims, and hand the
                // pawn to vanilla's own error recovery. Return true so the
                // original StartJob body does not run on top of the corrupt
                // state — the original job is deliberately NOT enqueued.
                NoteFault("swap StartJob", e, pawn);
                pawn.ClearReservationsForJob(swap);
                pawn.ClearReservationsForJob(originalJob);
                try
                {
                    JobUtility.TryStartErrorRecoverJob(pawn, "[ShiftChange] recovering from failed swap start");
                }
                catch (Exception recovery)
                {
                    Log.Error("[ShiftChange] recovery also failed for " + pawn.LabelShort + ": " + recovery);
                }
                return true;
            }
            finally
            {
                inserting = false;
            }
            // The displaced job resumes the moment the pawn finishes changing.
            tracker.jobQueue.EnqueueFirst(originalJob, tag);

            if (Verbose)
            {
                Log.Message($"[ShiftChange] {direction}: {pawn.LabelShort} → {stand.parent.LabelShort} " +
                            $"(deferring {originalJob.def.defName})");
            }
            return true;
        }

        /// <summary>
        /// An ingest-family job — meals, drinks, drugs; anything driven by
        /// <see cref="JobDriver_Ingest"/> or a subclass. By driver class, not
        /// JobDefOf.Ingest, so modded ingest defs are covered too.
        /// </summary>
        internal static bool IsIngestJob(Job job)
        {
            Type driver = job.def?.driverClass;
            return driver != null && typeof(JobDriver_Ingest).IsAssignableFrom(driver);
        }

        /// <summary>
        /// A recreation job the room trigger can honestly serve: it carries a
        /// <c>joyKind</c>, and its driver is not a reading driver, which
        /// picks its spot mid-job. This classifies the JOB CLASS, not the arm
        /// that serves it, so VisitSickPawn (Doctor work with joyKind Social)
        /// is in class and every room-resolver site reads it B-first. Why
        /// joyKind, and why consumption and reading stay outside:
        /// docs/DESIGN.md, "The recreation branch".
        ///
        /// <para><b>The reading exclusion is NARROWER than the principle behind
        /// it, and knowingly so.</b> What disqualifies reading is not that it
        /// is reading — it is that the job CHOOSES ITS SPOT MID-JOB, so the
        /// target it carries at StartJob is not where the activity ends up
        /// happening. Nothing in the engine flags that behaviour, so there is
        /// no way to ask for it directly; the driver class is the only handle,
        /// and it only catches vanilla's own.
        ///
        /// A modded reading job with its own driver would therefore slip
        /// through, and dress a pawn for whichever room the book is shelved
        /// in. As of 1.6 none in practice does: Alpha Books' own read job
        /// (<c>ABooks_Reading</c>, driver <c>JobDriver_ReadAlphaBook</c>)
        /// carries NO joyKind, so it fails the first test and never reaches
        /// this one — excluded a step earlier, by luck rather than by design.
        ///
        /// If a report ever arrives of a stand firing while somebody reads,
        /// this is the shape of it, and the fix is to widen the test — either
        /// by naming the offending driver too, or by deferring the room
        /// decision for any job whose target moves after it starts.</para>
        /// </summary>
        internal static bool IsRecreationJob(Job job)
        {
            JobDef def = job.def;
            if (def?.joyKind == null)
            {
                return false;
            }
            Type driver = def.driverClass;
            return driver == null || !typeof(JobDriver_Reading).IsAssignableFrom(driver);
        }

        /// <summary>
        /// A sleep job the room trigger can honestly serve: a lay-down job
        /// whose target is an actual BED. The bed test is what keeps ground
        /// sleep (no bed, and usually no valid target at all), mech dormancy
        /// and Odyssey's deactivation out, since all three run the same
        /// <see cref="JobDriver_LayDown"/> family: docs/DESIGN.md, "The sleep
        /// branch".
        ///
        /// <para>By driver class rather than by JobDef, matching
        /// <see cref="IsIngestJob"/>, so a modded sleep job that reuses
        /// vanilla's driver is covered. One that does not is not — the same
        /// known limit the reading exclusion carries, and the same shape of
        /// fix if a report ever arrives.</para>
        /// </summary>
        internal static bool IsRestJob(Job job)
        {
            Type driver = job.def?.driverClass;
            return driver != null
                   && typeof(JobDriver_LayDown).IsAssignableFrom(driver)
                   && job.targetA.Thing is Building_Bed;
        }

        /// <summary>
        /// The work type a medical lay-down should be charged to, or null when
        /// this job is not one or must not divert. This is the whole of the
        /// hospital-gown trigger: the patient givers' jobs arrive with no
        /// giver, so the <c>RestingForMedicalReasons</c> tag is what it
        /// resolves from.
        ///
        /// <para>Two refusals, both load-bearing. An urgent case (vanilla's
        /// own split, <see cref="HealthAIUtility.ShouldSeekMedicalRestUrgent"/>)
        /// never diverts unless the medical-emergency setting is on. And a
        /// pawn already on a bed never diverts: the work arm has no bed guard
        /// of its own, because no other work job can start from a bed. Only
        /// <c>PatientBedRest</c> is ever returned, so the <c>Patient</c> row in
        /// the stand dialog stays inert. The argument, and the known cost of
        /// the urgent refusal: docs/DESIGN.md, "The sleep branch".</para>
        /// </summary>
        internal static WorkTypeDef MedicalRestWorkType(Pawn pawn, Job job, JobTag? tag)
        {
            if (tag != JobTag.RestingForMedicalReasons || !IsRestJob(job))
            {
                return null;
            }
            // The bed guard is UNCONDITIONAL and stays that way. The setting
            // below is about whether an emergency may be delayed; nothing in
            // it argues for hauling a patient off a bed they are already lying
            // on, which is a different behaviour with a different failure.
            if (OnABed(pawn))
            {
                return null;
            }
            if (!ShiftChangeMod.MedicalEmergencyDressingEnabled
                && HealthAIUtility.ShouldSeekMedicalRestUrgent(pawn))
            {
                return null;
            }
            return ShiftChangeDefOf.PatientBedRest;
        }

        /// <summary>
        /// Work types whose emergencies the medical-emergency setting covers.
        /// Named, because <c>emergency</c> alone would include firefighting and
        /// walk colonists to a wardrobe while the base burns; the [FSF] Complex
        /// Jobs names are belt and braces against a future repoint. Same
        /// silent-fail contract as <see cref="RoomWorkTypes.CompatDefaults"/>:
        /// an absent name is a mod that is not installed. docs/DESIGN.md,
        /// "The sleep branch".
        /// </summary>
        internal static readonly string[] MedicalWorkTypeNames =
        {
            "Doctor", "Patient", "PatientBedRest", "FSFNurse", "FSFSurgeon",
        };

        internal static HashSet<WorkTypeDef> medicalWorkTypes;

        internal static HashSet<WorkTypeDef> MedicalWorkTypes
        {
            get
            {
                if (medicalWorkTypes == null)
                {
                    medicalWorkTypes = new HashSet<WorkTypeDef>();
                    for (int i = 0; i < MedicalWorkTypeNames.Length; i++)
                    {
                        WorkTypeDef work =
                            DefDatabase<WorkTypeDef>.GetNamedSilentFail(MedicalWorkTypeNames[i]);
                        if (work != null)
                        {
                            medicalWorkTypes.Add(work);
                        }
                    }
                }
                return medicalWorkTypes;
            }
        }

        /// <summary>
        /// May this emergency giver's job be delayed for a change, because the
        /// player asked for that and this is medical work?
        /// </summary>
        internal static bool EmergencyDressingAllowed(WorkGiverDef giver)
        {
            return ShiftChangeMod.MedicalEmergencyDressingEnabled
                   && giver?.workType != null
                   && MedicalWorkTypes.Contains(giver.workType);
        }

        /// <summary>
        /// Is this pawn physically on a bed right now?
        ///
        /// <para><b>Not <c>pawn.InBed()</c>, and the difference is the whole
        /// point.</b> At an ordinary job boundary it reads FALSE for a colonist
        /// lying in their own bed, and true only at the wake-up, the opposite
        /// of what every guard here means (<c>RestUtility.cs:505</c>,
        /// <c>Pawn_JobTracker.cs:501</c>, <c>Toils_LayDown.cs:74</c>).
        /// docs/DESIGN.md, "The sleep branch".</para>
        ///
        /// <para>Position answers the question <c>curJob</c> cannot. A pawn
        /// merely walking across a bed tile also passes, which is why the
        /// return trip pairs this with <see cref="StaysInBed"/> rather than
        /// using it alone.</para>
        /// </summary>
        internal static bool OnABed(Pawn pawn)
        {
            Map map = pawn.MapHeld;
            if (!pawn.Spawned || map == null)
            {
                return false;
            }
            List<Thing> here = pawn.Position.GetThingList(map);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i] is Building_Bed)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The pawn is on a bed AND this incoming job is one they can do
        /// without getting off it: <c>Job.CanBeginNow(pawn, whileLyingDown:
        /// true)</c> (false on the base driver, <c>JobDriver.cs:303-306</c>),
        /// or a job whose own target is the bed under them
        /// (<see cref="TargetsBedUnder"/>), because the drivers' own list is
        /// incomplete. Staying in bed means stay dressed; getting up means
        /// change back while still beside the stand. docs/DESIGN.md, "The
        /// sleep branch". (Eating still reaches the ingest branch further
        /// down, which has its own deliberate policy; this only stops the
        /// return trip firing first.)
        ///
        /// <para>It caches a driver on the job (<c>Job.GetCachedDriver</c>),
        /// which is what vanilla's own callers do and what <see cref="Insert"/>
        /// already does for its reservation dry run. <c>StartJob</c> builds the
        /// real driver with <c>MakeDriver</c>, never the cached one, and pooled
        /// jobs are <c>Clear()</c>ed before reuse, so the throwaway cannot leak
        /// into a live job. A throwing driver constructor is caught by the
        /// prefix's fail-open handler. Short-circuited behind
        /// <see cref="OnABed"/>, so nothing is constructed at the ordinary job
        /// boundary where the pawn is nowhere near a bed.</para>
        /// </summary>
        internal static bool StaysInBed(Pawn pawn, Job job)
        {
            return OnABed(pawn)
                   && (job.CanBeginNow(pawn, whileLyingDown: true) || TargetsBedUnder(pawn, job));
        }

        /// <summary>
        /// This job's own target is the bed the pawn is lying on — so whatever
        /// its driver claims, doing it does not mean getting up.
        ///
        /// <para>Both A and B, because the in-bed job givers disagree about
        /// which they use: <c>JobGiver_MeditateInBed</c> puts the pawn's cell in
        /// A and the bed in B, while the lay-down family puts the bed in A.
        /// <c>OccupiedRect</c> rather than <c>Position</c> so a double bed's
        /// second slot counts.</para>
        /// </summary>
        internal static bool TargetsBedUnder(Pawn pawn, Job job)
        {
            IntVec3 at = pawn.Position;
            Building_Bed a = job.targetA.Thing as Building_Bed;
            if (a != null && a.Spawned && a.OccupiedRect().Contains(at))
            {
                return true;
            }
            Building_Bed b = job.targetB.Thing as Building_Bed;
            return b != null && b.Spawned && b.OccupiedRect().Contains(at);
        }

        /// <summary>
        /// Where a recreation job happens: <c>targetB</c> FIRST, the cell the
        /// pawn occupies while the joy ticks, then the work-style
        /// <see cref="TargetCell"/>, since where B is unset A is already the
        /// venue. docs/DESIGN.md, "The recreation branch".
        /// </summary>
        internal static IntVec3 JoyTargetCell(Job job, Map map)
        {
            IntVec3 cell = job.targetB.HasThing ? job.targetB.Thing.PositionHeld : job.targetB.Cell;
            if (cell.IsValid && cell.InBounds(map))
            {
                return cell;
            }
            return TargetCell(job, map);
        }

        /// <summary>
        /// The job's food source is already on the pawn — in inventory (the
        /// same test the driver itself uses for eatingFromInventory,
        /// JobDriver_Ingest.cs:86) or in their hands.
        /// </summary>
        internal static bool FoodSourceIsOnPawn(Job job, Pawn pawn)
        {
            Thing food = job.targetA.Thing;
            if (food == null)
            {
                return false;
            }
            if (pawn.inventory != null && pawn.inventory.Contains(food))
            {
                return true;
            }
            return pawn.carryTracker?.CarriedThing == food;
        }

        /// <summary>
        /// Which of the three triggers a stand is being asked to serve. One
        /// value rather than a row of bools, because "recreation and sleep
        /// both true" is not a state any caller is allowed to hold — the
        /// exclusivity rule lives on the comp, and this keeps the selector
        /// from being the place it could be broken.
        /// </summary>
        internal enum StandTrigger
        {
            Work,
            Recreation,
            Rest,
        }

        internal static bool Handles(CompShiftStand comp, StandTrigger trigger, WorkTypeDef work)
        {
            switch (trigger)
            {
                case StandTrigger.Recreation:
                    return comp.HandlesRecreation();
                case StandTrigger.Rest:
                    return comp.HandlesRest();
                default:
                    return comp.HandlesWork(work);
            }
        }

        /// <summary>
        /// The stand this pawn should use, or null if there isn't one.
        ///
        /// An unassigned stand is a POOL stand: any capable pawn may claim it,
        /// so a kitchen needs one stand per CONCURRENT cook rather than one per
        /// cook who might ever cook. A stand assigned to this pawn always wins
        /// over a pool stand — a personal kit is personal — and among equals
        /// the nearest is taken, or two cooks walk past a closer one to reach
        /// the same far one.
        ///
        /// No free stand simply means no swap. Never queue for one: this is a
        /// nicety and must not become a bottleneck on the work itself.
        /// </summary>
        internal static CompShiftStand FindAvailableStand(Room room, Pawn pawn, WorkTypeDef work,
            StandTrigger trigger = StandTrigger.Work)
        {
            CompShiftStand best = null;
            bool bestIsPersonal = false;
            int bestDistance = int.MaxValue;

            List<ThingDef> defs = StandDefs;
            for (int i = 0; i < defs.Count; i++)
            {
                foreach (Thing thing in room.ContainedThings(defs[i]))
                {
                    CompShiftStand comp = thing.TryGetComp<CompShiftStand>();
                    if (comp == null || comp.OnShift
                        || !Handles(comp, trigger, work)
                        || !comp.CanBeClaimedBy(pawn))
                    {
                        continue;
                    }
                    // Ask the same question the driver will ask on arrival, not
                    // a looser one. "Holds some apparel" is not the same as
                    // "holds something THIS pawn can put on", and the gap sent
                    // pawns on wasted trips that looked like swapping at an
                    // empty rack.
                    if (!SwapPlan.WouldDress(pawn, comp.Stand))
                    {
                        continue;
                    }
                    // Outside this pawn's allowed area. The swap could not get
                    // there (see StandForbiddenTo), and because a personal stand
                    // outranks a shared one below, leaving it in would let a
                    // forbidden personal stand beat an allowed one beside it.
                    if (StandForbiddenTo(thing, pawn))
                    {
                        continue;
                    }
                    // Reservation is part of "available", not a formality to
                    // check after choosing. OnShift only becomes true when a
                    // swap COMPLETES, so a stand someone is currently walking
                    // to still reads as free here — it would win the distance
                    // tiebreak, fail to reserve back in Insert, and put the
                    // pawn on cooldown instead of falling through to the stand
                    // next to it that was genuinely free. On a burst arrival —
                    // the exact concurrency pooling exists to serve — that
                    // degraded pooling to roughly one dress per completed
                    // swap however many stands the room had. Asked last of the
                    // gates because it is much the most expensive, and it is
                    // the same call TryDressMidJob already makes.
                    if (!pawn.CanReserveAndReach(thing, PathEndMode.InteractionCell, Danger.Deadly))
                    {
                        continue;
                    }

                    bool personal = comp.IsAssignedTo(pawn);
                    int distance = pawn.Position.DistanceToSquared(thing.Position);

                    if (best == null
                        || (personal && !bestIsPersonal)
                        || (personal == bestIsPersonal && distance < bestDistance))
                    {
                        best = comp;
                        bestIsPersonal = personal;
                        bestDistance = distance;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// The stand is off limits to this pawn: outside their allowed area, in
        /// practice, since an outfit stand has no forbid toggle of its own.
        /// It is the swap driver's own predicate, so the two cannot disagree;
        /// <c>CanReserveAndReach</c> ignores allowed areas, and the driver
        /// does not. Same map first: another map's area grid throws out of
        /// range and reads the wrong bit inside it. docs/DESIGN.md, "Allowed
        /// areas".
        /// </summary>
        internal static bool StandForbiddenTo(Thing stand, Pawn pawn)
        {
            return stand != null && pawn != null
                   && stand.MapHeld != null && stand.MapHeld == pawn.MapHeld
                   && stand.IsForbidden(pawn);
        }

        /// <summary>
        /// This job carries no giver, and the next job in the queue is work this
        /// on-shift stand serves, in its own room: an errand slotted in ahead of
        /// the stand's own work, which the pawn is coming straight back to.
        /// Vanilla's opportunistic haul and Common Sense's ingredient haul both
        /// build that shape. Asked on the return trip only, so it keeps a
        /// uniform on and never dresses anyone; recreation and sleep are
        /// excluded here, and a meal is decided before this is reached. The
        /// signal, exactly, and both sources: docs/DESIGN.md, "Errands queued
        /// ahead of work".
        /// </summary>
        internal static bool QueuedAheadOfServedWork(Job job, Pawn_JobTracker tracker,
                                                     CompShiftStand onShift, Room standRoom, Map map)
        {
            if (job.workGiverDef != null || IsRecreationJob(job) || IsRestJob(job))
            {
                return false;
            }
            JobQueue queue = tracker?.jobQueue;
            if (queue == null || queue.Count == 0)
            {
                return false;
            }
            Job next = queue.Peek()?.job;
            WorkGiverDef giver = next?.workGiverDef;
            if (giver?.workType == null || JobRoomTargets.Ignored(giver)
                || !onShift.HandlesWork(giver.workType))
            {
                return false;
            }
            IntVec3 at = IsRecreationJob(next) ? JoyTargetCell(next, map) : TargetCell(next, map);
            return at.IsValid && at.GetRoom(map) == standRoom;
        }

        /// <summary>
        /// Where the job happens. <c>targetA</c> covers the work types room
        /// mode cares about, but queue-based jobs (hauling, harvesting) leave
        /// it empty — verified in the spike — so fall back to the queue rather
        /// than treating those as "no location".
        ///
        /// <para>A short list of jobs answers from <c>targetB</c> instead,
        /// because they name their destination in targetA and the thing being
        /// carried in targetB; see <see cref="JobRoomTargets"/>.</para>
        /// </summary>
        internal static IntVec3 TargetCell(Job job, Map map)
        {
            // A few jobs name their DESTINATION in targetA and the thing being
            // carried in targetB, which is the reverse of both vanilla shapes
            // — see JobRoomTargets for the list and why it is a list. Tried
            // first, and it falls through to targetA when targetB is unreadable
            // so a malformed job still resolves somewhere.
            //
            // targetB's thing is read exactly like targetA's, PositionHeld and
            // all. That means once the pawn has PICKED THE ROD UP this answers
            // "wherever the carrier is standing" rather than the pool. At a job
            // boundary, which is where both arms ask, the rod is still on the
            // ground and the answer is the pool. The catch-up sweep can ask
            // mid-carry, and there it degrades to the room the pawn is walking
            // through — which is the room a freed stand would have to be in to
            // match anyway, so it stays harmless.
            if (JobRoomTargets.UsesTargetB(job.def))
            {
                IntVec3 carried = job.targetB.HasThing
                    ? job.targetB.Thing.PositionHeld
                    : job.targetB.Cell;
                if (carried.IsValid && carried.InBounds(map))
                {
                    return RoomBearing(carried, job.targetB, map);
                }
            }

            IntVec3 cell = job.targetA.HasThing ? job.targetA.Thing.PositionHeld : job.targetA.Cell;
            if (cell.IsValid && cell.InBounds(map))
            {
                return RoomBearing(cell, job.targetA, map);
            }

            List<LocalTargetInfo> queue = job.targetQueueA;
            if (queue != null)
            {
                for (int i = 0; i < queue.Count; i++)
                {
                    IntVec3 queued = queue[i].HasThing ? queue[i].Thing.PositionHeld : queue[i].Cell;
                    if (queued.IsValid && queued.InBounds(map))
                    {
                        return queued;
                    }
                }
            }
            return IntVec3.Invalid;
        }

        /// <summary>
        /// A cell that HAS a room, for a work target that does not.
        ///
        /// <para>A building's own cells stop belonging to any room once it is
        /// both impassable and full-fillage:
        /// <c>RegionTypeUtility.GetExpectedRegionType</c> returns
        /// <c>RegionType.None</c> for a non-walkable cell holding anything
        /// whose <c>Fillage</c> is <c>Full</c>, so no region covers it and
        /// <c>GetRoom</c> answers null. Every arm here reads the job's room
        /// from the target cell and gives up when it is null, so a work target
        /// built like that is invisible to this mod: pawns work at it and
        /// never change. Rimatomics' plutonium processor is one (impassable,
        /// fillPercent 1, 4x4), which is how two colonists supervised research
        /// at it in a room with two stands set to Research and nothing
        /// happened.</para>
        ///
        /// <para>The engine has no helper for this. <c>Thing.GetRoom</c> and
        /// <c>GetRoomOrAdjacent</c> both work from the thing's Position, and on
        /// a 4x4 solid building that centre cell's eight neighbours are still
        /// inside the building, so both answer null too. The ring around the
        /// whole occupied rect is what has to be walked.</para>
        ///
        /// <para>Resolved by COUNT first: the room owning most of the ring
        /// wins. At an equal count an enclosed room beats an outdoor one,
        /// because the outdoor room is one map-wide object and every stand
        /// this mod looks for lives in a room. Two enclosed rooms that still
        /// tie get no answer at all: this returns the roomless cell rather
        /// than guess. The engine has no opinion on such a building either,
        /// since one wedged into a wall carries no interaction cell.</para>
        ///
        /// <para>Nothing here reads a stand, and iteration order changes
        /// nothing, so both arms read the same room for the same job. That is
        /// required rather than tidy: make the answer depend on where a free
        /// stand happens to be and the two arms disagree, which is a pawn
        /// walking between the stand and the work forever.</para>
        ///
        /// <para>Only runs when the cheap answer is null, so the ordinary case
        /// costs one region lookup.</para>
        /// </summary>
        internal static IntVec3 RoomBearing(IntVec3 cell, LocalTargetInfo target, Map map)
        {
            if (cell.GetRoom(map) != null)
            {
                return cell;
            }
            Thing thing = target.HasThing ? target.Thing : null;
            if (thing == null || !thing.Spawned || thing.Map != map)
            {
                return cell;
            }

            // Count how much of the ring each room owns, keeping the first
            // cell seen for each. Iteration order decides nothing below: the
            // winner is settled by count and by indoor-ness, and a tie that
            // survives both refuses rather than picks.
            Dictionary<Room, int> counts = new Dictionary<Room, int>();
            Dictionary<Room, IntVec3> firstCell = new Dictionary<Room, IntVec3>();
            foreach (IntVec3 ring in GenAdj.CellsAdjacent8Way(thing))
            {
                if (!ring.InBounds(map))
                {
                    continue;
                }
                Room room = ring.GetRoom(map);
                if (room == null)
                {
                    continue;
                }
                int seen;
                if (!counts.TryGetValue(room, out seen))
                {
                    firstCell[room] = ring;
                }
                counts[room] = seen + 1;
            }

            int topCount = 0;
            int atTop = 0;
            Room leader = null;
            foreach (KeyValuePair<Room, int> entry in counts)
            {
                if (entry.Value > topCount)
                {
                    topCount = entry.Value;
                    atTop = 1;
                    leader = entry.Key;
                }
                else if (entry.Value == topCount)
                {
                    atTop++;
                }
            }

            if (atTop == 0)
            {
                return cell;
            }

            // The ordinary case, and the reason enclosure is not consulted
            // here: one room owns more of the ring than any other, so whether
            // it is enclosed cannot change the answer. Mining is the hot
            // caller — every mineable rock is impassable and full-fillage, so
            // every Mine job reaches this — and its ring is rock plus one
            // room. Testing enclosure anyway would spend a
            // PsychologicallyOutdoors on each of those for nothing.
            if (atTop == 1)
            {
                return firstCell[leader];
            }

            // A genuine tie, so now it is worth asking. An enclosed room beats
            // an outdoor one: the outdoor room is a single map-wide object,
            // and letting it take a tie hands the job to a "room" spanning the
            // whole colony. PsychologicallyOutdoors reads a per-room cached
            // count after its first call (Room.cs:557), and the count check
            // short-circuits ahead of it, so only the tied rooms are asked.
            Room pick = null;
            int enclosed = 0;
            foreach (KeyValuePair<Room, int> entry in counts)
            {
                if (entry.Value != topCount || entry.Key.PsychologicallyOutdoors)
                {
                    continue;
                }
                pick = entry.Key;
                enclosed++;
            }

            // Exactly one enclosed room among the tied ones wins. Otherwise
            // there is nothing left to separate them — two enclosed rooms, or
            // none at all — so refuse rather than guess. Both arms then read
            // null and agree, which is the conservative pair: no dressing, and
            // a pawn already dressed returns their gear.
            if (enclosed != 1)
            {
                return cell;
            }
            return firstCell[pick];
        }
    }
}
