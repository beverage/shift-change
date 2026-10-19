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
    /// A prefix on <see cref="Pawn_JobTracker.StartJob"/>: before a pawn starts a job a stand
    /// dresses for, it defers the job and sends them to change first, and it changes a pawn in
    /// uniform back when a job takes them away. Why StartJob rather than TryOpportunisticJob,
    /// and the insertion pattern: docs/DESIGN.md, "Interception".
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static partial class Patch_JobInterception
    {
        /// <summary>
        /// The standing kill switch, the player's or the hot-reload quarantine's, for everything
        /// the mod does on its own, the return trip and the vacuum change-back included; throws
        /// go to <see cref="faulted"/>. Guards keyed on the ledger (the optimizer pause, the
        /// recolor guard) and the player's Change back button read neither, since a pawn in
        /// uniform still needs it protected and a way out of it; a new hook of that kind must not.
        /// </summary>
        // These TweakValues ship on purpose: they are how a player is walked through a report.
        [TweakValue("ShiftChange")]
        public static bool Enabled = true;

        /// <summary>
        /// Interception threw <see cref="FaultLimit"/> times and is off for the rest of this
        /// game. Kept apart from <see cref="Enabled"/>, which a load must leave alone.
        /// </summary>
        internal static bool faulted;

        /// <summary>Throws counted this game.</summary>
        internal static int faultCount;

        /// <summary>Harness test seam: while positive, TryInsertSwap throws.</summary>
        internal static int injectFaults;

        /// <summary>
        /// Throws tolerated before <see cref="faulted"/> latches. Not one: the catches also see
        /// other mods' exceptions thrown through this frame.
        /// </summary>
        [TweakValue("ShiftChange", 1f, 20f)]
        public static int FaultLimit = 3;

        /// <summary>
        /// Counts a throw, and at <see cref="FaultLimit"/> latches <see cref="faulted"/> and tells
        /// the player in a message: <c>Log.Error</c> opens no log window outside dev mode.
        /// Callers pass the pawn whenever they have one, so the log names it.
        /// </summary>
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

        /// <summary>Ticks before a pawn whose swap could not start is considered again.</summary>
        [TweakValue("ShiftChange", 0f, 5000f)]
        public static int RetryCooldownTicks = 600;

        /// <summary>Set while <see cref="Insert"/> starts a swap, re-entering the prefix.</summary>
        internal static bool inserting;

        internal static readonly Dictionary<int, int> LastBlockedTick = new Dictionary<int, int>();

        /// <summary>Called by SessionGuard when the loaded game changes. The tick records are
        /// keyed by thing id and stamped with TicksGame, both of which restart per save, so a
        /// stale entry is wrong there, not merely leaked.</summary>
        internal static void ResetSessionState()
        {
            LastBlockedTick.Clear();
            LastVacuumWarningTick.Clear();
            ChangedBackAt.Clear();
            DressedFor.Clear();
            // The fault latch only. Enabled also carries the hot-reload quarantine, and the twin
            // JobDriver that wedges a tracker is still loaded after a save load.
            faultCount = 0;
            faulted = false;
        }

        // ReSharper disable once InconsistentNaming — Harmony field/instance injection.
        public static bool Prefix(Job newJob, JobTag? tag, Pawn ___pawn, Pawn_JobTracker __instance)
        {
            // Before the gate, which reads the fault latch: until now it may be the last game's.
            SessionGuard.Ensure();

            if (!Enabled || faulted || inserting)
            {
                return true;
            }

            // A throw here would stop job assignment for every pawn on the map. Fail open.
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
        /// The prefix's decision, one call per step; it and each arm return true when the
        /// original StartJob must not run: a swap started, or threw.
        /// The order of the calls is behaviour; the steps here are declared below in that order.
        /// </summary>
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
                // A drafted colonist's moves are orders that never reach the gates below; the
                // vacuum warning still applies to them.
                WarnIfOrderedIntoVacuum(job, pawn);
                return false;
            }
            // Every step below reads this; none classifies the job again.
            JobClass c = Classify(pawn, job, tag);

            // Above every gate, so the latch sees each job boundary decided here, including those
            // a gate turns back: leaving the catchment or waking ends it, whatever the job.
            UpdateChangeBackLatch(pawn, job, c);

            // These gates hold in both directions, so they sit above the return trip, and each
            // lets the job start untouched. Deliveries and ignored givers' jobs skip the first and
            // are decided after the rest. Of the jobs these gates let through, only an order, a
            // lord's job and one handed over mid-swap pass the vacuum gate below unasked; the
            // other exempt jobs are let through after it.
            bool delivery = !c.PlayerForced && c.Kind == JobKind.Delivery;
            bool ignored = !c.PlayerForced && (c.Kind == JobKind.MealRun || c.Kind == JobKind.IgnoredGiver);
            bool exempt = !delivery && !ignored && ExemptInEitherDirection(c);
            if (exempt && c.PlayerForced)
            {
                WarnIfOrderedIntoVacuum(job, pawn);
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

            // No uniform into vacuum: a colonist on shift whose job goes through vacuum, in a
            // uniform that protects less than their own clothes, changes back first if the stand
            // can be reached through air; if not, the walk to change would be the crossing itself,
            // so the player is told instead. Only on a vacuum map (docs/DESIGN.md, "Vacuum").
            // Above every exemption but an order's, emergencies and errands included, above the
            // cooldown and above every step below that would keep the uniform on. Below the lord
            // and mid-swap gates, under which no swap can start, and the return trip's refusals
            // hold here too: nobody is pulled out of bed, and an urgent patient goes to bed.
            CompShiftStand worn = CompShiftStand.OnShiftStandFor(pawn);
            if (worn != null && VacuumMap(map) && !StaysInBed(pawn, job)
                && !c.UrgentPatient && !UrgentPatientNext(pawn, tracker))
            {
                UniformInVacuum outcome = UniformIntoVacuum(job, pawn, worn, map);
                if (outcome == UniformInVacuum.ChangeBack)
                {
                    if (Verbose)
                    {
                        Log.Message($"[ShiftChange] {pawn.LabelShort} changes back before {job.def.defName}: " +
                                    "it goes through vacuum, and their own clothes protect them better");
                    }
                    return Insert(pawn, tracker, worn, job, tag, "return (vacuum)");
                }
                if (outcome != UniformInVacuum.Fine && !WarnedLately(pawn))
                {
                    WarnOfVacuum(pawn, outcome, ordered: false);
                }
            }
            if (CoolingDown(pawn))
            {
                return false;
            }
            if (exempt)
            {
                return false;
            }
            // Below the gates, so the tracker's pause before the deferred job keeps the record;
            // above the deliveries, so a delivery kept on there drops another job's record.
            bool dressedForThis = TakeDressedFor(pawn, job);

            // A delivery starts at the stack and a meal run wherever the meal is stored, so both go
            // by the sites they supply. Of these and the ignored givers' jobs, only a delivery
            // dresses a bare colonist (DeliveryArm).
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

            CompShiftStand onShift = CompShiftStand.OnShiftStandFor(pawn);
            if (onShift != null)
            {
                // The job they were dressed for keeps them dressed, wherever its target went.
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

            // Below the return trip and above every dress arm: under threat nobody changes in,
            // but changing back still happens.
            if (MapUnderThreat(map))
            {
                return false;
            }

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

        /// <summary>The map to decide this job on, or null if this mod leaves it alone.</summary>
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
            // A drafted pawn is never sent to a wardrobe, in either direction.
            if (pawn.Drafted || pawn.Downed || pawn.InMentalState)
            {
                return null;
            }
            return pawn.MapHeld;
        }

        /// <summary>Orders, emergencies and ride-alongs start as they are, both ways; medical
        /// emergencies only while the player has not allowed dressing for them.</summary>
        internal static bool ExemptInEitherDirection(JobClass c)
        {
            return c.PlayerForced || c.ExemptEmergency || c.Kind == JobKind.RidesAlong;
        }

        /// <summary>A lord or a duty holds the pawn, so neither direction diverts them.</summary>
        internal static bool UnderLordDuty(Pawn pawn, Job job)
        {
            // A lord reissues the pawn's job on its own clock, pre-empting any swap and deferring
            // each reissued job again. The duty too, since a mod may assign one with no Lord. The
            // lord's cleanup clears both together, so this never latches a colonist out.
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
            // Let the new job through: deferring it would start a second swap and push the first
            // one's deferred job deeper into the queue.
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

        /// <summary>A swap for this pawn failed to start within the retry cooldown.</summary>
        internal static bool CoolingDown(Pawn pawn)
        {
            int now = Find.TickManager.TicksGame;
            int blocked;
            return LastBlockedTick.TryGetValue(pawn.thingIDNumber, out blocked)
                   && now - blocked < RetryCooldownTicks;
        }

        /// <summary>Where the job happens, by job class, the same for both directions.</summary>
        internal static JobPlace SharedTargetPlace(Job job, JobClass c, Map map)
        {
            // If the directions disagree, a pawn walks between stand and work forever. By the
            // recreation class, not the kind, so VisitSickPawn, which is Work, is read B-first.
            return c.RecreationJob ? JoyTargetPlace(job, map) : TargetPlace(job, map);
        }

        /// <summary>Change the on-shift pawn back first, or let the uniform ride along.</summary>
        internal static bool ReturnTrip(Job job, JobTag? tag, JobClass c, Pawn pawn, Pawn_JobTracker tracker,
                                        CompShiftStand onShift, JobPlace target, Map map)
        {
            Room standRoom = onShift.parent.GetRoom();
            if (standRoom == null)
            {
                return false;
            }
            // Never pull a pawn out of bed to change back: in-bed joy and re-queued lay-downs
            // arrive as fresh jobs. StaysInBed, not pawn.InBed(), which answers wrongly here.
            if (StaysInBed(pawn, job))
            {
                return false;
            }
            // No change out before an urgent patient's lay-down, nor before an errand vanilla slips
            // in ahead of it; UrgentPatient is false while medical emergency dressing is on.
            if (c.UrgentPatient || UrgentPatientNext(pawn, tracker))
            {
                return false;
            }
            // Not the room test below: an ingest job's targetA is the food, and the chair is
            // chosen during the job.
            if (c.Ingest)
            {
                return MealBreak(job, tag, pawn, tracker, onShift, target, map);
            }
            // A delivery or a meal run not kept on earlier goes by its sites and its stack.
            if (c.HasSites)
            {
                if (SiteJobStaysDressed(job, c, pawn, onShift, target, map))
                {
                    return false;
                }
            }
            // A room limits what can happen in it, so room mode keeps the uniform on for anything
            // there. A radius or an area goes by what the stand is for (StaysDressed).
            else if (onShift.serves == CatchmentKind.Room)
            {
                if (!target.IsValid || onShift.Covers(target, map))
                {
                    // Inside the room, or neither the job's targets nor its queue name a cell on
                    // this map: stay dressed.
                    return false;
                }
            }
            else if (StaysDressed(job, c, pawn, onShift, target, map))
            {
                return false;
            }
            // An errand slotted in ahead of this stand's own work, which they come straight back
            // to. Below the meal branch, which keeps its own policy.
            if (QueuedAheadOfServedWork(job, c, pawn, tracker, onShift, map))
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] {pawn.LabelShort} keeps the outfit on for " +
                                $"{job.def.defName}: {onShift.parent.LabelShort}'s work is queued next");
                }
                return false;
            }
            // Never across vacuum to change, whatever the return is for.
            if (ReturnCrossesVacuum(pawn, onShift, map))
            {
                return false;
            }
            return Insert(pawn, tracker, onShift, job, tag, "return");
        }

        /// <summary>Return trip for an ingest job: eat in uniform, or change out first. Both
        /// exceptions read the stand's setup now, not why the pawn was dressed.</summary>
        internal static bool MealBreak(Job job, JobTag? tag, Pawn pawn, Pawn_JobTracker tracker,
                                       CompShiftStand onShift, JobPlace target, Map map)
        {
            // Eating as-is is a work and recreation rule. A sleep stand's pawn wakes beside the
            // stand, and would only be sent out in sleepwear to eat and back.
            if (FoodSourceIsOnPawn(job, pawn) && !onShift.HandlesRest())
            {
                return false;
            }
            // A recreation stand's room stocks its own drinks, so food inside its catchment is
            // part of the break.
            if (onShift.HandlesRecreation() && onShift.Covers(target, map))
            {
                return false;
            }
            if (ReturnCrossesVacuum(pawn, onShift, map))
            {
                return false;
            }
            return Insert(pawn, tracker, onShift, job, tag, "return");
        }

        /// <summary>Radius or area stand: the pawn on shift stays dressed for this job.</summary>
        internal static bool StaysDressed(Job job, JobClass c, Pawn pawn, CompShiftStand onShift,
                                          JobPlace target, Map map)
        {
            // Changing back for a job this stand dresses for would only dress them again next job.
            // By work type, not kind: a delivery or meal run goes by its giver's work.
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
            // The first job out of bed changes a sleep stand's pawn back, wherever it is.
            if (onShift.HandlesRest())
            {
                return false;
            }
            if (work != null)
            {
                return !onShift.HandlesRecreation() && JobRoomTargets.PassesThrough(work)
                       && EveryPlaceCovered(job, pawn, onShift, map);
            }
            // Idling and the walks are judged where they go, and a wait, which goes nowhere,
            // where the pawn stands.
            return c.Incidental
                   && onShift.Covers(target.IsValid ? target : JobPlace.At(pawn.Position), map);
        }

        /// <summary>The map is under threat, so no arm dresses anyone.</summary>
        internal static bool MapUnderThreat(Map map)
        {
            // Vanilla keeps apparel changes out of danger by their place in the think tree, which
            // this hook does not have. It only holds dressing back; nothing reacts to danger
            // starting.
            return map.dangerWatcher.DangerRating != StoryDanger.None;
        }

        /// <summary>The work type the work arm dresses this job for, or null.</summary>
        internal static WorkTypeDef WorkTypeFor(Pawn pawn, Job job, JobTag? tag)
        {
            // JobGiver_Work stamps workGiverDef only on its scanner paths, so NonScanJob jobs
            // arrive with none: a medical lay-down is charged to PatientBedRest by its tag,
            // whatever its giver (MedicalRestWorkType), and fishing to its giver's work type
            // (NonScanGiver).
            return job.workGiverDef?.workType
                ?? MedicalRestWorkType(pawn, job, tag)
                ?? JobRoomTargets.NonScanGiver(job, tag)?.workType;
        }

        /// <summary>Dress for <paramref name="work"/> at a stand covering the job.</summary>
        internal static bool WorkArm(Job job, JobTag? tag, Pawn pawn, Pawn_JobTracker tracker,
                                     WorkTypeDef work, JobPlace target, Map map)
        {
            if (!target.IsValid)
            {
                return false;
            }

            // The search holds the change-back latch per candidate, and the vacuum gate: given the
            // job, it passes over a stand whose outfit would strip protection the trip needs.
            CompShiftStand stand = FindAvailableStand(target, map, pawn, work, StandTrigger.Work, job);
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

        /// <summary>Dress for bed at a sleep stand covering the bed.</summary>
        internal static bool SleepArm(Job job, JobTag? tag, JobClass c, Pawn pawn, Pawn_JobTracker tracker,
                                      JobPlace target, Map map)
        {
            // Medical bed rest the work arm declined is not sleep. Do not test
            // HealthAIUtility.ShouldSeekMedicalRest: it is true at a wounded colonist's bedtime.
            if (!c.OrdinarySleep)
            {
                return false;
            }
            // Never pull a pawn out of bed to dress for bed: a sleeper who stirs starts a fresh
            // lay-down in the same bed. OnABed, not pawn.InBed().
            if (OnABed(pawn))
            {
                return false;
            }
            if (!target.IsValid)
            {
                return false;
            }
            // An outdoor cell resolves the one map-spanning room, not null, so a room stand in open
            // ground would serve every bedroll on the map; the search skips such a room for sleep.
            // It holds the change-back latch per candidate, and the vacuum gate.
            CompShiftStand restStand = FindAvailableStand(target, map, pawn, null, StandTrigger.Rest, job);
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

        /// <summary>Dress for recreation at a stand covering where it happens.</summary>
        internal static bool RecreationArm(Job job, JobTag? tag, Pawn pawn, Pawn_JobTracker tracker,
                                           JobPlace target, Map map)
        {
            // Joy jobs carry no workGiverDef, so a joy giver's job never reaches the work arm: only
            // JobGiver_Work's scanner paths and a prioritized order stamp one. Vanilla hands joy
            // to a pawn lying in bed so they stay there, so in-bed joy never sends a patient to a
            // wardrobe. OnABed, not pawn.InBed().
            if (OnABed(pawn))
            {
                return false;
            }
            if (!target.IsValid)
            {
                return false;
            }
            // As for sleep, the search skips a room stand in open ground; a walled roofless yard is
            // its own room and stays eligible (docs/DESIGN.md, "The recreation branch"). It holds
            // the change-back latch per candidate, and the vacuum gate.
            CompShiftStand recStand = FindAvailableStand(target, map, pawn, null, StandTrigger.Recreation, job);
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

        /// <summary>
        /// Start a swap at <paramref name="stand"/> with <paramref name="originalJob"/> queued
        /// behind it. True when the original StartJob must not run: the swap started, or threw.
        /// </summary>
        internal static bool Insert(Pawn pawn, Pawn_JobTracker tracker, CompShiftStand stand,
                                   Job originalJob, JobTag? tag, string direction)
        {
            // The swap would fail on its first toil at a stand outside the pawn's allowed area,
            // and CanReserveAndReach below ignores allowed areas.
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

            // Reserve the deferred job's targets now, since the skipped StartJob would have; every
            // queue-clearing path releases them. curJob is the deferred job for the dry run, as in
            // StartJob, because drivers such as JobDriver_SocialRelax reserve against pawn.CurJob.
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
                // Lost the target this tick: drop partial claims; vanilla starts and fails the job.
                pawn.ClearReservationsForJob(originalJob);
                return false;
            }

            Job swap = JobMaker.MakeJob(ShiftChangeDefOf.ShiftChange_SwapAtStand, stand.parent);

            // Start the swap before enqueueing the deferred job, the reverse of vanilla's order:
            // a throw after an enqueue would leave one Job in two places. InterruptForced, not
            // None: for a pawn with a job, StartJob warns and substitutes it anyway.
            inserting = true;
            try
            {
                tracker.StartJob(swap, JobCondition.InterruptForced, null,
                    resumeCurJobAfterwards: false,
                    cancelBusyStances: true, null, JobTag.ChangingApparel);
            }
            catch (Exception e)
            {
                // A throw inside StartJob can leave curJob set with no curDriver, and that
                // tracker then throws every tick. Release both jobs' claims,
                // hand the pawn to vanilla's error recovery, and return true so the original
                // StartJob does not run on top of it. The original job is not enqueued.
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
            tracker.jobQueue.EnqueueFirst(originalJob, tag);

            // Behind a dressing swap, record the deferred job for the return trip to let through
            // (TakeDressedFor); a change back records nothing, so its job is judged again. Nobody
            // is on shift until the swap completes, so this still reads the state before it.
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
