using System;
using System.Collections.Generic;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShiftChange
{
    /// <summary>
    /// Where Shift Change actually happens: a prefix on
    /// <see cref="Pawn_JobTracker.StartJob"/> that, when a pawn is about to
    /// start automatic work in a room whose stand they own, pushes that job
    /// back onto the queue and sends them to change clothes first. The same
    /// hook runs the return trip when a job takes them out of the room.
    ///
    /// <b>Why here.</b> Spiked in play 2026-08-07: <c>workGiverDef</c> is
    /// populated at this point, <c>targetA</c> resolves to the work's room, and
    /// <c>playerForced</c> is readable — everything the decision needs. The
    /// obvious alternative, <c>TryOpportunisticJob</c>, is a trap: vanilla
    /// bails out of it for drafted pawns (<c>Pawn_JobTracker.cs:628</c>) and
    /// unless the job def sets <c>allowOpportunisticPrefix</c> (<c>:657</c>),
    /// which <c>TendPatient</c> does not — so the headline doctoring case could
    /// never fire through it.
    ///
    /// <b>The insertion is vanilla's own.</b> <c>Pawn_JobTracker.cs:338-347</c>
    /// enqueues the incoming job first and starts a different one instead; we
    /// do the same, without going through the gated method. Because we start a
    /// job from inside a StartJob prefix, the call re-enters this patch —
    /// hence <see cref="inserting"/>.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_JobInterception
    {
        /// <summary>
        /// The INTERCEPTION kill switch, and nothing more. Read by
        /// <see cref="Prefix"/> and <see cref="Notify_StandFreed"/> only.
        ///
        /// It latches false whenever anything throws inside the nested
        /// <c>StartJob</c> — which includes a throw from another mod's patch,
        /// so it is not a reliable signal that WE are broken. The on-shift
        /// protections (the optimizer pause, the recolor execution guard) and
        /// the Change back button therefore do NOT consult it: a pawn standing
        /// in a uniform still needs their uniform protected and still needs a
        /// way out of it, and gating those here meant a neighbour's exception
        /// re-opened the dye path on staged kit while simultaneously removing
        /// the player's only manual remedy. Those three key off the ledger
        /// instead. Keep it that way when adding hooks: ask "does this ACT on
        /// its own, or protect something already in progress?"
        /// </summary>
        [TweakValue("ShiftChange")]
        public static bool Enabled = true;

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
        /// It is positional rather than a countdown because the thing that
        /// must not cycle is the CHANGING, not the work (principal,
        /// 2026-08-09): counting jobs either spends the block on an
        /// unrelated errand — leaving the next room job free to re-dress
        /// them a second after the player pulled them out — or holds it
        /// through a long trip that plainly should have ended it. "Have they
        /// left the room yet" is the question the player is actually asking,
        /// and it answers itself from position.
        ///
        /// The STAND is stored, not the Room, so the room is re-derived live
        /// on both sides of every comparison and a rebuilt wall cannot leave
        /// a stale Room object behind. Keyed by Pawn reference rather than
        /// thingIDNumber on purpose: an object reference cannot collide
        /// across a save load the way a recycled ID can, so a stale entry is
        /// inert rather than wrong. SessionGuard still clears it so old-game
        /// pawns don't leak.
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
        /// Called by SessionGuard when the loaded game changes. This map is
        /// keyed by thingIDNumber and stamped with TicksGame — both restart
        /// per save, so stale entries are not merely leaked but WRONG: an
        /// old entry can sit in the future and block a same-ID pawn from
        /// swapping until the new game's clock catches up.
        /// </summary>
        internal static void ResetSessionState()
        {
            LastBlockedTick.Clear();
            ChangedBackAt.Clear();
        }

        /// <summary>Toggle for the mid-job catch-up below.</summary>
        [TweakValue("ShiftChange")]
        public static bool DressMidJob = true;

        /// <summary>
        /// A stand just returned to availability. If a colonist is ALREADY
        /// working bare in its room — because they took the job while every
        /// stand was checked out — interrupt them to change now, resuming the
        /// job afterwards (found in play, 2026-08-08: two benches, two pawns,
        /// two stands; the second pawn starts seconds before a stand frees).
        ///
        /// The interrupt is vanilla's own detour shape (the vomit pattern):
        /// StartJob with resumeCurJobAfterwards suspends the current job when
        /// its def allows (Pawn_JobTracker.cs:293-296) and resumes it from
        /// the queue. Gated on suspendable AND casualInterruptible — both
        /// default true (JobDef.cs:24-26) and both are false on TendPatient,
        /// so bills and research are caught up while a doctor mid-treatment
        /// is never yanked away from a patient to fetch scrubs.
        /// </summary>
        public static void Notify_StandFreed(CompShiftStand stand, Pawn except)
        {
            if (!Enabled || !DressMidJob)
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
                Log.Error("[ShiftChange] stand-freed catch-up threw: " + e);
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
                if (giver?.workType == null || giver.emergency || !stand.HandlesWork(giver.workType))
                {
                    continue;
                }
                IntVec3 target = TargetCell(job, map);
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

        internal static List<ThingDef> StandDefs
        {
            get
            {
                if (standDefs == null)
                {
                    standDefs = new List<ThingDef>();
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
                    if (def != null)
                    {
                        standDefs.Add(def);
                    }
                }
                return standDefs;
            }
        }

        // ReSharper disable once InconsistentNaming — Harmony field/instance injection.
        public static bool Prefix(Job newJob, JobTag? tag, Pawn ___pawn, Pawn_JobTracker __instance)
        {
            if (!Enabled || inserting)
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
                Enabled = false;
                Log.Error("[ShiftChange] job interception threw, disabling itself: " + e);
                return true;
            }
        }

        /// <returns>true if a swap job was started in place of the incoming one.</returns>
        internal static bool TryInsertSwap(Job job, JobTag? tag, Pawn pawn, Pawn_JobTracker tracker)
        {
            SessionGuard.Ensure();
            if (job == null || pawn == null || Current.ProgramState != ProgramState.Playing)
            {
                return false;
            }
            if (!pawn.Spawned || pawn.Faction != Faction.OfPlayer || !pawn.RaceProps.Humanlike)
            {
                return false;
            }
            if (job.def == ShiftChangeDefOf.ShiftChange_SwapAtStand)
            {
                return false;
            }
            // A drafted pawn is being told where to be. Never send them to a
            // wardrobe, in either direction.
            if (pawn.Drafted || pawn.Downed || pawn.InMentalState)
            {
                return false;
            }

            Map map = pawn.MapHeld;
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

            // No changing while the map is under threat. Vanilla has no
            // precedent to copy here: JobGiver_OptimizeApparel carries no
            // danger check at all, because think-tree position does the work
            // for it (Humanlike.xml:302-306) — and we sit downstream of the
            // think tree, so the gate has to be ours.
            if (map.dangerWatcher.DangerRating != StoryDanger.None)
            {
                return false;
            }

            // Never divert a direct order or an emergency response — in
            // EITHER direction. A right-click order means "now", and
            // emergency work givers (DoctorTendEmergency) exist precisely
            // because something cannot wait: a pawn bleeding out must not
            // wait while the doctor changes out of scrubs. The uniform rides
            // along instead, and the return happens on the next ordinary
            // automatic job. This must sit ABOVE the return-trip block —
            // it originally gated only the dressing path, which meant an
            // emergency in another room was delayed by an undress detour.
            if (job.playerForced || job.workGiverDef?.emergency == true)
            {
                return false;
            }

            int now = Find.TickManager.TicksGame;
            int blocked;
            if (LastBlockedTick.TryGetValue(pawn.thingIDNumber, out blocked)
                && now - blocked < RetryCooldownTicks)
            {
                return false;
            }

            IntVec3 target = TargetCell(job, map);

            // The return trip. Checked first: a pawn already in uniform who is
            // leaving should change back whatever the new job is.
            CompShiftStand onShift = CompShiftStand.OnShiftStandFor(pawn);
            if (onShift != null)
            {
                Room standRoom = onShift.parent.GetRoom();
                if (standRoom == null)
                {
                    return false;
                }
                // Eating cannot use the room test below, because an ingest
                // job's targetA is the FOOD, while the place the eating
                // happens — the dining chair — is chosen DURING the job by
                // Toils_Ingest.CarryIngestibleToChewSpot
                // (JobDriver_Ingest.cs:165, Toils_Ingest.cs:115) and is not
                // knowable here. The food's cell misreads the break in both
                // directions: a packed lunch reads as the pawn's own
                // position, and a colony's meal stock usually sits in or
                // near the kitchen, so a cook's meal reads as "in the room"
                // — and they then carry it across the base to a chair in
                // uniform, the exact walk this mod exists to prevent.
                // Policy (principal, 2026-08-08): food already on the pawn
                // means eat as-is; anything else is a sit-down break —
                // change out first, wherever the meal happens to be stored.
                if (IsIngestJob(job))
                {
                    if (FoodSourceIsOnPawn(job, pawn))
                    {
                        return false;
                    }
                    return Insert(pawn, tracker, onShift, job, tag, "return");
                }
                if (!target.IsValid || target.GetRoom(map) == standRoom)
                {
                    // Still working in the room, or the job's location is
                    // unreadable (queue-based jobs — hauling, harvesting).
                    // Staying dressed is the safe answer either way.
                    return false;
                }
                return Insert(pawn, tracker, onShift, job, tag, "return");
            }

            WorkTypeDef work = job.workGiverDef?.workType;
            if (work == null || !target.IsValid)
            {
                return false;
            }

            Room room = target.GetRoom(map);
            if (room == null)
            {
                return false;
            }

            // The change-back latch: they were pulled out of this very room
            // and have not left it since, so dressing again here is the cycle
            // the player just stopped.
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

        internal static bool Insert(Pawn pawn, Pawn_JobTracker tracker, CompShiftStand stand,
                                   Job originalJob, JobTag? tag, string direction)
        {
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

            // Reserve the deferred job's targets NOW — this is vanilla
            // parity, not an extra. Vanilla reserves at StartJob
            // (TryMakePreToilReservations), and its own opportunistic
            // deferral reserves FIRST, then enqueues the job and starts the
            // other one (Pawn_JobTracker.cs:331-347; ClearDriver never
            // releases). Our prefix skips the original StartJob entirely, so
            // without this the patient or bench sits unreserved for the
            // whole walk-and-change and any other pawn can take it — found
            // in play 2026-08-08 as a distant doctor dressing for a patient
            // a nearer doctor had already tended, then changing straight
            // back. Safe to hold while queued: every queue-clearing path
            // releases via QueuedJob.Cleanup → ClearReservationsForJob
            // (QueuedJob.cs:26), and the queued start re-reserves its own
            // claims idempotently.
            // The dry-run must reproduce vanilla's calling context: StartJob
            // assigns curJob BEFORE calling TryMakePreToilReservations, and
            // drivers rely on it — vanilla's JobDriver_SocialRelax reserves
            // its seat against pawn.CurJob, not its own job field
            // (JobDriver_SocialRelax.cs:30), and FE's served social driver
            // mirrors it. With curJob unset, that reserve sees a null job and
            // returns false (ReservationManager.cs:306-309, one "without a
            // valid job" warning per attempt), so the divert silently
            // degraded to ride-along — found in play 2026-08-09 as a crafter
            // drinking at the bar in uniform. MakeDriver sets the driver's
            // own job field (Job.cs:606), which is why only CurJob-reading
            // drivers ever noticed the difference.
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

            // Start the swap BEFORE enqueueing the displaced job. Vanilla's
            // own pattern (Pawn_JobTracker.cs:338-347) enqueues first, but it
            // runs inside StartJob where nothing can fail between the two
            // calls. Out here, if StartJob threw after the enqueue, the
            // prefix's fail-open catch would let the original StartJob
            // proceed while the same Job object also sat in the queue — one
            // job, two places, and the tracker chokes on it. StartJob never
            // consults the queue, so enqueueing after is equivalent on
            // success and strictly safer on failure.
            inserting = true;
            try
            {
                tracker.StartJob(swap, JobCondition.None, null, resumeCurJobAfterwards: false,
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
                Enabled = false;
                Log.Error("[ShiftChange] swap StartJob threw — disabling and recovering "
                          + pawn.LabelShort + ": " + e);
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
        internal static CompShiftStand FindAvailableStand(Room room, Pawn pawn, WorkTypeDef work)
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
                    if (comp == null || comp.OnShift || !comp.HandlesWork(work)
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
                    // four gates because it is much the most expensive, and
                    // it is the same call TryDressMidJob already makes.
                    if (!pawn.CanReserveAndReach(thing, PathEndMode.InteractionCell, Danger.Deadly))
                    {
                        continue;
                    }

                    bool personal = comp.AssignedOwner == pawn;
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
        /// Where the job happens. <c>targetA</c> covers the work types room
        /// mode cares about, but queue-based jobs (hauling, harvesting) leave
        /// it empty — verified in the spike — so fall back to the queue rather
        /// than treating those as "no location".
        /// </summary>
        internal static IntVec3 TargetCell(Job job, Map map)
        {
            IntVec3 cell = job.targetA.HasThing ? job.targetA.Thing.PositionHeld : job.targetA.Cell;
            if (cell.IsValid && cell.InBounds(map))
            {
                return cell;
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
    }
}
