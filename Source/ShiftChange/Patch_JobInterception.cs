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

        private static bool inserting;

        private static readonly Dictionary<int, int> LastBlockedTick = new Dictionary<int, int>();

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

        private static void TryDressMidJob(CompShiftStand stand, Pawn except)
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

        private static List<ThingDef> standDefs;

        private static List<ThingDef> StandDefs
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
        private static bool TryInsertSwap(Job job, JobTag? tag, Pawn pawn, Pawn_JobTracker tracker)
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
                if (!target.IsValid || standRoom == null || target.GetRoom(map) == standRoom)
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

        private static bool Insert(Pawn pawn, Pawn_JobTracker tracker, CompShiftStand stand,
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
            JobDriver reservationDriver = originalJob.MakeDriver(pawn);
            if (!reservationDriver.TryMakePreToilReservations(errorOnFailed: false))
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
        private static CompShiftStand FindAvailableStand(Room room, Pawn pawn, WorkTypeDef work)
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
        private static IntVec3 TargetCell(Job job, Map map)
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
