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
    ///
    /// The class is <c>partial</c>, across seven files in Interception/. This
    /// one holds the kill switch, the fault latch, <see cref="TryInsertSwap"/>
    /// with every step it calls in the order they run, and
    /// <see cref="Insert"/>. The other six hold the change-back latch, the
    /// mid-job catch-up, deliveries, job kinds, the stand search and places;
    /// docs/DEVELOPMENT.md maps them.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static partial class Patch_JobInterception
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
        // These four TweakValues SHIP, deliberately (decided 2026-08-17), as
        // do DressMidJob (Patch_JobInterception.CatchUp.cs) and
        // Patch_OutfitStandsPlusUseButton.Enabled, six in all. The sweep of
        // the debug menu does not extend to them: the bar is destructiveness,
        // not reachability, and they are how a player is walked through
        // diagnosing a report. docs/DESIGN.md, "Development tooling".
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
        /// Called by SessionGuard when the loaded game changes. A stale
        /// <see cref="LastBlockedTick"/> entry is WRONG in the new game, not
        /// merely leaked: docs/DESIGN.md, "State across save, load and
        /// uninstall".
        /// </summary>
        internal static void ResetSessionState()
        {
            LastBlockedTick.Clear();
            ChangedBackAt.Clear();
            DressedFor.Clear();
            // Re-arm after a fault. Deliberately NOT `Enabled = true`: that
            // flag also carries the hot-reload quarantine, and the wedging
            // twin JobDriver is still loaded after a save load, so re-arming
            // it here would resurrect the 2026-08-08 tracker wedge.
            faultCount = 0;
            faulted = false;
        }

        // ReSharper disable once InconsistentNaming — Harmony field/instance injection.
        public static bool Prefix(Job newJob, JobTag? tag, Pawn ___pawn, Pawn_JobTracker __instance)
        {
            // BEFORE the gate, not after: the gate reads the fault latch, and
            // until SessionGuard sees a new game the latch is the last game's.
            // Other entry points call Ensure too (OnShiftStandFor, a stand's
            // spawn, the optimizer patches), so this is not the only way a
            // load lifts the latch, but it is the one that guarantees this
            // job is decided on this game's state. One reference comparison.
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
            // Every step below reads this, and none classifies the job again.
            JobClass c = Classify(pawn, job, tag);

            // Sampled here, above every gate below, so the latch is tested at
            // every job boundary this mod decides on, including jobs that
            // return below (danger, player-forced, cooldown). Leaving the
            // stand's catchment, or waking, is what ends it, and the pawn must
            // not have to take a dressable job to be noticed doing either.
            // MapToDecideOn has already passed over a pawn who is downed, in
            // a mental state or off the map; drafting drops the latch
            // outright (Patch_DraftClearsDressBlock).
            UpdateChangeBackLatch(pawn, job, c);

            // The next four gates hold in BOTH directions, which is why they
            // sit above the return trip. Each lets the job start untouched.
            // A delivery and an ignored giver's job are not among the jobs the
            // first lets through, and are decided once the other three have
            // run.
            bool delivery = !c.PlayerForced && c.Kind == JobKind.Delivery;
            bool ignored = !c.PlayerForced && (c.Kind == JobKind.MealRun || c.Kind == JobKind.IgnoredGiver);
            if (!delivery && !ignored && ExemptInEitherDirection(c))
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
            // Taken below the gates, so the job tracker's pause between a swap
            // and the job deferred behind it rides along above without
            // spending it, and above the deliveries below, so one kept on
            // there spends a record that is not its own.
            bool dressedForThis = TakeDressedFor(pawn, job);

            // A DELIVERY STARTS AT THE STACK, and a meal for a patient or a
            // prisoner wherever the meal is stored, so both are judged by
            // where they go (HasSites). A bare colonist is dressed for a
            // delivery only at an area stand holding the whole trip
            // (DeliveryArm), and for an ignored giver's job, meal runs
            // included, never. One in uniform keeps it on when their stand
            // serves the work and holds every site the trip supplies
            // (KeepsTheUniformFor): the builder fetching steel in a hardhat,
            // wherever the steel lies, and the doctor carrying a meal from the
            // freezer to a patient in the hospital. Anyone else takes the
            // return trip below (SiteJobStaysDressed), so a builder bound for
            // a site outside changes back now, while the stand is near, and
            // pyjamas come off before a fetch from a stockpile outside.
            // docs/DESIGN.md, "Mod compatibility".
            if (delivery || ignored)
            {
                CompShiftStand dressed = CompShiftStand.OnShiftStandFor(pawn);
                if (dressed == null)
                {
                    return delivery && DeliveryArm(job, tag, pawn, tracker, map);
                }
                if (KeepsTheUniformFor(job, c, dressed, map))
                {
                    return false;
                }
            }

            JobPlace target = SharedTargetPlace(job, c, map);

            // The return trip. Checked first: a pawn already in uniform who is
            // leaving should change back whatever the new job is.
            CompShiftStand onShift = CompShiftStand.OnShiftStandFor(pawn);
            if (onShift != null)
            {
                // Never for the job they were just dressed for, though its
                // target may have left the catchment while they changed.
                if (dressedForThis)
                {
                    if (Verbose)
                    {
                        Log.Message($"[ShiftChange] {pawn.LabelShort} keeps the outfit on for " +
                                    $"{job.def.defName}: the job they were dressed for");
                    }
                    return false;
                }
                return ReturnTrip(job, tag, c, pawn, tracker, onShift, target, map);
            }

            // BELOW the return trip and above every dress arm: under threat,
            // nobody changes in, but changing back still happens.
            if (MapUnderThreat(map))
            {
                return false;
            }

            // One arm per kind, and the kinds are tested in the arms' order
            // (Classify): the work arm first, so VisitSickPawn, Doctor work
            // that carries a joyKind, is work. The sleep arm turns medical
            // lay-downs away because the work arm has already declined them.
            // Deliveries, ignored givers and the jobs that ride along were
            // settled above.
            switch (c.Kind)
            {
                case JobKind.Work:
                    return WorkArm(job, tag, pawn, tracker, c.Work, target, map);
                case JobKind.Rest:
                    return SleepArm(job, tag, c, pawn, tracker, target, map);
                case JobKind.Recreation:
                    return RecreationArm(job, tag, pawn, tracker, target, map);
                default:
                    return false;
            }
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
        internal static bool ExemptInEitherDirection(JobClass c)
        {
            // Never divert a direct order or an emergency response, in EITHER
            // direction: the uniform rides along and the next ordinary job
            // settles it. The giver-less follow-ups a haul leaves and the job
            // tracker's pause between jobs (RidesAlong) pass the same way.
            // Givers this mod ignores (JobRoomTargets.Ignored) are decided by
            // TryInsertSwap instead, below the other gates. One opt-in
            // carve-out: with the medical emergency setting on, MEDICAL
            // emergencies stop being exempt, in both directions, and
            // firefighting never does. Why this sits above the return trip:
            // docs/DESIGN.md, "The gates"; the setting: "The sleep branch".
            return c.PlayerForced || c.ExemptEmergency || c.Kind == JobKind.RidesAlong;
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
        internal static JobPlace SharedTargetPlace(Job job, JobClass c, Map map)
        {
            // ONE room resolver per job class, consumed by BOTH directions:
            // with split reads the two arms disagreed about where a job
            // happens, and a pawn ping-ponged between stand and work forever
            // (docs/DESIGN.md, "The recreation branch"). By the recreation
            // class, not the kind, so VisitSickPawn is read B-first too.
            return c.RecreationJob ? JoyTargetPlace(job, map) : TargetPlace(job, map);
        }

        /// <summary>
        /// The pawn is on shift at <paramref name="onShift"/>: change them back
        /// before this job, or let the uniform ride along. True if a swap
        /// started.
        /// </summary>
        internal static bool ReturnTrip(Job job, JobTag? tag, JobClass c, Pawn pawn, Pawn_JobTracker tracker,
                                        CompShiftStand onShift, JobPlace target, Map map)
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
            // AN URGENT PATIENT GOES STRAIGHT TO BED. The work arm will not
            // dress one unless the medical emergency setting is on
            // (MedicalRestWorkType), and the same refusal holds this way
            // round, or a colonist bleeding out walks to the stand to change
            // before lying down. The same goes for an errand vanilla slips in
            // ahead of that lay-down, which it puts back in the queue untagged.
            if (c.UrgentPatient || UrgentPatientNext(pawn, tracker))
            {
                return false;
            }
            // Eating cannot use the room test below: an ingest job's targetA
            // is the FOOD, and the chair is chosen during the job. Food on the
            // pawn means eat as-is; anything else is a sit-down break, so
            // change out first (decided 2026-08-08; docs/DESIGN.md, "Meal
            // breaks").
            if (c.Ingest)
            {
                return MealBreak(job, tag, pawn, tracker, onShift, target, map);
            }
            // A delivery or a meal run the stand did not keep on above goes by
            // its sites and its stack, not by one place (SiteJobStaysDressed).
            if (c.HasSites)
            {
                if (SiteJobStaysDressed(job, c, pawn, onShift, target, map))
                {
                    return false;
                }
            }
            // Room mode keeps its shipped rule, anything in the room, because
            // a room limits what can happen inside it. A radius or an area
            // limits nothing, so there it goes by what the stand is for
            // (StaysDressed).
            else if (onShift.serves == CatchmentKind.Room)
            {
                if (!target.IsValid || onShift.Covers(target, map))
                {
                    // Still inside the stand's room, or the job's location is
                    // unreadable (queue-based jobs — hauling, harvesting).
                    // Staying dressed is the safe answer either way.
                    return false;
                }
            }
            else if (StaysDressed(job, c, pawn, onShift, target, map))
            {
                return false;
            }
            // An errand slotted in AHEAD of this stand's own work: they are
            // coming straight back to it, so changing out first is a round
            // trip to the stand for nothing. Below the meal branch on
            // purpose, which keeps its own policy. See QueuedAheadOfServedWork.
            if (QueuedAheadOfServedWork(job, c, pawn, tracker, onShift, map))
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
                                       CompShiftStand onShift, JobPlace target, Map map)
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
            if (onShift.HandlesRecreation() && onShift.Covers(target, map))
            {
                return false;
            }
            return Insert(pawn, tracker, onShift, job, tag, "return");
        }

        /// <summary>
        /// For a stand serving a radius or an area: the pawn on shift at it
        /// keeps the uniform on for this job. What the stand would dress them
        /// for, inside its catchment, always does. Past that it goes by what
        /// the stand is for: a work stand also lets through pass-through work
        /// with every place inside, and the incidental jobs; a recreation stand
        /// the incidental jobs alone; a sleep stand nothing, since staying in
        /// bed was settled above (docs/DESIGN.md, "Catchments").
        /// </summary>
        internal static bool StaysDressed(Job job, JobClass c, Pawn pawn, CompShiftStand onShift,
                                          JobPlace target, Map map)
        {
            // The dress arms' own classification, in their order. A job this
            // stand would dress the pawn for must not change them back, or the
            // next boundary dresses them again. By the work type rather than
            // the kind, because a delivery's or a meal run's colonist reaches
            // this too (SiteJobStaysDressed), and is judged by its giver's work.
            WorkTypeDef work = c.Work;
            bool served = work != null
                ? onShift.HandlesWork(work)
                : c.RestJob
                    ? c.OrdinarySleep && onShift.HandlesRest()
                    : c.RecreationJob && onShift.HandlesRecreation();
            if (served)
            {
                // An unreadable location stays dressed, as in room mode.
                return !target.IsValid || onShift.Covers(target, map);
            }
            // The first job out of bed changes a sleep stand's pawn back,
            // wherever it is.
            if (onShift.HandlesRest())
            {
                return false;
            }
            if (work != null)
            {
                return !onShift.HandlesRecreation() && JobRoomTargets.PassesThrough(work)
                       && EveryPlaceCovered(job, pawn, onShift, map);
            }
            // Idling and the walks are judged where they go, and a wait, which
            // goes nowhere, where the pawn stands.
            return c.Incidental
                   && onShift.Covers(target.IsValid ? target : JobPlace.At(pawn.Position), map);
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
            // (docs/DESIGN.md, "The sleep branch"). Fishing is the same shape
            // (JobRoomTargets.NonScanGivers).
            return job.workGiverDef?.workType
                ?? MedicalRestWorkType(pawn, job, tag)
                ?? JobRoomTargets.NonScanGiver(job, tag)?.workType;
        }

        /// <summary>
        /// Dress for <paramref name="work"/> at a stand whose catchment covers
        /// the job (<see cref="FindAvailableStand(JobPlace, Map, Pawn, WorkTypeDef, StandTrigger)"/>). True
        /// if a swap started.
        /// </summary>
        internal static bool WorkArm(Job job, JobTag? tag, Pawn pawn, Pawn_JobTracker tracker,
                                     WorkTypeDef work, JobPlace target, Map map)
        {
            if (!target.IsValid)
            {
                return false;
            }

            // The search holds the change-back latch, per candidate.
            CompShiftStand stand = FindAvailableStand(target, map, pawn, work);
            if (stand == null)
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] no free {work.defName} stand for {pawn.LabelShort} " +
                                $"({job.def.defName}) at {target}, {RoomName(target.RoomCell.GetRoom(map))}");
                }
                return false;
            }

            return Insert(pawn, tracker, stand, job, tag, "dress");
        }

        /// <summary>
        /// Dress for bed at a sleep stand whose catchment covers the bed. True
        /// if a swap started.
        /// </summary>
        internal static bool SleepArm(Job job, JobTag? tag, JobClass c, Pawn pawn, Pawn_JobTracker tracker,
                                      JobPlace target, Map map)
        {
            // The SLEEP arm, for a rest job the work arm did not take. The tag
            // test in IsOrdinarySleep keeps MEDICAL bed rest out: reaching
            // here means the work arm declined it (MedicalRestWorkType
            // resolved no work type), and a pyjama stand must not then take it
            // as ordinary sleep. Its workGiverDef limb is a conservative catch
            // for a modded giver with a null workType. Do not add
            // HealthAIUtility.ShouldSeekMedicalRest here: it cannot tell "hurt"
            // from "night", and once killed bedtime for every wounded colonist.
            // Why each: docs/DESIGN.md, "The sleep branch".
            if (!c.OrdinarySleep)
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
            // The outdoor guard lives in the search now: an outdoor cell
            // resolves the one map-spanning, edge-touching room rather than
            // null, so a room stand in open ground would serve every bedroll
            // on the map, and the search skips such a room for sleep. It holds
            // the change-back latch too, per candidate.
            CompShiftStand restStand = FindAvailableStand(target, map, pawn, null, StandTrigger.Rest);
            if (restStand == null)
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] no free sleep stand for {pawn.LabelShort} " +
                                $"({job.def.defName}) at {target}, {RoomName(target.RoomCell.GetRoom(map))}");
                }
                return false;
            }
            return Insert(pawn, tracker, restStand, job, tag, "dress-rest");
        }

        /// <summary>
        /// Dress for recreation at a stand whose catchment covers where it
        /// happens. True if a swap started.
        /// </summary>
        internal static bool RecreationArm(Job job, JobTag? tag, Pawn pawn, Pawn_JobTracker tracker,
                                           JobPlace target, Map map)
        {
            // The RECREATION arm, for a recreation job the work arm did not
            // take. Joy jobs carry no workGiverDef, so a joy giver's job never
            // reaches the work arm: only JobGiver_Work stamps one
            // (JobGiver_Work.cs:240, :334, :345), and a prioritized order
            // (Pawn_JobTracker.cs:881). The shared resolver has already read
            // the room B-first (JoyTargetCell), where the pawn will sit.
            //
            // Vanilla hands joy to pawns lying in bed so they stay there, so
            // in-bed joy never pulls a patient out to a wardrobe. OnABed, not
            // pawn.InBed() (docs/DESIGN.md, "The recreation branch").
            if (OnABed(pawn))
            {
                return false;
            }
            if (!target.IsValid)
            {
                return false;
            }
            // Outdoor cells resolve the one map-spanning room, never null, so a
            // room stand in open ground would serve every outdoor joy job on the
            // map; the search skips such a room for recreation. A walled roofless
            // yard is its own room and stays eligible, and a radius needs no
            // guard (docs/DESIGN.md, "The recreation branch"). The search holds
            // the change-back latch too, per candidate.
            CompShiftStand recStand = FindAvailableStand(target, map, pawn, null, StandTrigger.Recreation);
            if (recStand == null)
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] no free recreation stand for {pawn.LabelShort} " +
                                $"({job.def.defName}) at {target}, {RoomName(target.RoomCell.GetRoom(map))}");
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
                // Count the fault (NoteFault latches interception off at
                // FaultLimit), release both jobs' claims, and hand the
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

            // Behind a dressing swap, the deferred job is let through on the
            // return trip while it starts or waits at the head of the queue
            // (DressedFor, TakeDressedFor). A change back records nothing: cut
            // short, its job must be judged again. Nobody is on shift until
            // the swap completes, so this still reads the state before it.
            if (CompShiftStand.OnShiftStandFor(pawn) == null)
            {
                DressedFor[pawn] = new DeferredJob(originalJob);
            }

            if (Verbose)
            {
                Log.Message($"[ShiftChange] {direction}: {pawn.LabelShort} → {stand.parent.LabelShort} " +
                            $"(deferring {originalJob.def.defName})");
            }
            return true;
        }
    }
}
