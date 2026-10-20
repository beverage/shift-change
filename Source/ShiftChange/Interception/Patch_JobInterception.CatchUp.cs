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
    public static partial class Patch_JobInterception
    {
        /// <summary>Switch for the mid-job catch-up.</summary>
        [TweakValue("ShiftChange")]
        public static bool DressMidJob = true;

        /// <summary>
        /// A stand just returned to availability. If a colonist is already working bare in its
        /// room, because they took the job while every stand was checked out, interrupt them to
        /// change now and resume the job afterwards. Only a job whose def is both suspendable and
        /// casually interruptible, which leaves a doctor mid-treatment alone: vanilla's
        /// <c>TendPatient</c> is neither. docs/DESIGN.md, "The mid-job catch-up".
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
                // Runs inside another pawn's job cleanup, which a throw here must not break.
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
            // Room stands only: a freed radius or area stand would pull a bare colonist off work
            // anywhere in its catchment, and the next job boundary catches them anyway.
            if (stand.serves != CatchmentKind.Room)
            {
                return;
            }
            Map map = parent.Map;
            if (map == null || map.dangerWatcher.DangerRating != StoryDanger.None)
            {
                return;
            }
            // A room stand in the open serves nothing, so it catches nobody up.
            Room room = parent.GetRoom();
            if (room == null || room.TouchesMapEdge)
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
                // A lord or duty holds the pawn, as in StartJob. A duty job need not declare itself
                // not casually interruptible, so the job filter below would not stop a freed
                // stand pulling a ritual participant off their duty.
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
                // No tag: only a giver-less job's work type depends on one, and neither test below
                // reads it (the work test needs a stamped giver, the recreation class ignores it).
                JobClass c = Classify(pawn, job, null);
                // Fewer kinds than StartJob dresses for, on purpose: work from a stamped giver
                // that is not an emergency, and recreation. Giver-less work (medical bed rest,
                // unstamped fishing) and every emergency, whatever the medical-emergency setting
                // says, are never caught up. Deliveries and ignored givers' jobs carry a work type
                // but are never Work (Classify), which is what keeps them out here, or a freed
                // stand would pull a colonist off a mid-haul. docs/DESIGN.md, "The mid-job
                // catch-up".
                bool forWork = giver?.workType != null && !giver.emergency
                    && c.Kind == JobKind.Work && stand.HandlesWork(c.Work);
                // Joy jobs carry no workGiverDef (RecreationArm says who stamps one), so the
                // work and recreation arms are disjoint.
                bool forRecreation = !forWork && giver == null
                    && stand.HandlesRecreation() && c.RecreationJob
                    // Never pull a pawn out of bed, as in RecreationArm.
                    && !OnABed(pawn);
                // No sleep arm: vanilla's LayDown is not casually interruptible, so the job
                // filter above already turns a sleeper away.
                if (!forWork && !forRecreation)
                {
                    continue;
                }
                // The shared resolver, not one picked by the matching arm. VisitSickPawn is both
                // Doctor work and Social joy, so two resolvers ordering it differently disagree
                // about where it happens and the pawn churns between the stand and the patient.
                if (!stand.Covers(SharedTargetPlace(job, c, map), map))
                {
                    continue;
                }
                if (!stand.CanBeClaimedBy(pawn) || !SwapPlan.WouldDress(pawn, stand.Stand))
                {
                    continue;
                }
                // Read straight after WouldDress, before anything builds another plan; the walk
                // it may lead to is asked last.
                bool lessProtected = PlanLowersVacuumProtection(pawn, map);
                // The latch holds on this dress path too, or a stand returning to the pool would
                // re-dress the pawn the player just pulled out of this room.
                if (LatchBlocks(pawn, stand, forWork ? StandTrigger.Work : StandTrigger.Recreation))
                {
                    continue;
                }
                // Outside this pawn's allowed area, so the swap would die on its first toil (see
                // StandForbiddenTo).
                if (StandForbiddenTo(parent, pawn))
                {
                    continue;
                }
                if (!pawn.CanReserveAndReach(parent, PathEndMode.InteractionCell, Danger.Deadly))
                {
                    continue;
                }
                int distance = pawn.Position.DistanceToSquared(parent.Position);
                if (distance >= bestDistance)
                {
                    continue;
                }
                // What is left of the job may still go through vacuum. A region walk, so asked
                // only of a colonist who would otherwise be the one caught up.
                if (lessProtected && DressingTripGoesThroughVacuum(job, pawn, stand, map))
                {
                    continue;
                }
                best = pawn;
                bestDistance = distance;
            }

            if (best == null)
            {
                return;
            }

            Job resumed = best.CurJob;
            Job swap = JobMaker.MakeJob(ShiftChangeDefOf.ShiftChange_SwapAtStand, parent);
            best.jobs.StartJob(swap, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: true, cancelBusyStances: true, null, JobTag.ChangingApparel);
            // StartJob puts the suspended job back on the queue as the same object; it is the job
            // they were dressed for, as Insert records it.
            JobQueue queue = best.jobs.jobQueue;
            if (resumed != null && queue.Count > 0 && queue.Peek()?.job == resumed)
            {
                DressedFor[best] = new DeferredJob(resumed);
            }
            if (Verbose)
            {
                Log.Message($"[ShiftChange] catch-up: {best.LabelShort} interrupted to dress at {parent.LabelShort}");
            }
        }

        internal static List<ThingDef> standDefs;

        /// <summary>
        /// Every def in the <see cref="Building_OutfitStand"/> class family that carries our
        /// comp. The class alone is not enough: a stand mod the XML does not patch has no comp or
        /// ledger and stays out of the pool, and other mods' stands, Outfit Stands Plus' powered
        /// ones among them, are further defs of the family, so no defName is listed. The XML
        /// decides coverage; this only reads it. Resolved once: the def set is fixed for the
        /// process lifetime.
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
    }
}
