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
            // Room stands only. A freed radius or area stand would pull a bare
            // colonist off work anywhere in its catchment, and the next job
            // boundary catches them anyway (docs/DESIGN.md, "Catchments").
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
                // Classified with no tag. Only a giver-less job's work type
                // depends on one, and neither test below reads it: the work
                // test needs a stamped giver, and the recreation class does
                // not look at the tag.
                JobClass c = Classify(pawn, job, null);
                // FEWER KINDS THAN StartJob, on purpose: work from a stamped
                // giver that is not an emergency, and recreation. A giver-less
                // job the work arm takes, medical bed rest or unstamped
                // fishing, is never caught up, and neither is any emergency,
                // which the medical-emergency setting does not change.
                // Deliveries and ignored givers are not Work, so they stay out:
                // StartJob only stops a pawn being dressed as they TAKE such a
                // job, and a freed stand would otherwise reach across and pull
                // them off one mid-haul, the same wardrobe detour by another
                // route. docs/DESIGN.md, "The mid-job catch-up".
                bool forWork = giver?.workType != null && !giver.emergency
                    && c.Kind == JobKind.Work && stand.HandlesWork(c.Work);
                // The joy twin: a colonist already at recreation bare when
                // this stand freed — the pool-party case — is caught up the
                // same way. Joy jobs carry no workGiverDef (RecreationArm says
                // who stamps one), so the two arms are disjoint by
                // construction.
                bool forRecreation = !forWork && giver == null
                    && stand.HandlesRecreation() && c.RecreationJob
                    // The dress arm's bed guard applies here too: never pull a
                    // pawn out of bed. Its outdoor guard returned above.
                    && !OnABed(pawn);
                // NO SLEEP ARM HERE, and it is not an omission. Catch-up
                // interrupts a pawn already doing the thing the freed stand
                // dresses for, and the sleep equivalent is a colonist asleep
                // in bed — who is excluded twice over before the arms are even
                // consulted: LayDown sets casualInterruptible false
                // (Core/Defs/JobDefs/Jobs_Misc.xml), which the job filter above
                // rejects, and the OnABed guard would reject them again.
                // Waking someone to put pyjamas on is the one catch-up nobody
                // wants.
                if (!forWork && !forRecreation)
                {
                    continue;
                }
                // The shared resolver, not one picked by which arm matched:
                // VisitSickPawn is the one vanilla job that is BOTH (Doctor
                // work with joyKind Social), and resolving it A-first here
                // while StartJob resolved it B-first re-opened a bounded
                // dress/undress churn per stand-freed event (fix-verify,
                // 2026-08-15). Every site answers "where does this job
                // happen" identically or the two answers fight.
                if (!stand.Covers(SharedTargetPlace(job, c, map), map))
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
                if (LatchBlocks(pawn, stand, forWork ? StandTrigger.Work : StandTrigger.Recreation))
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

            Job resumed = best.CurJob;
            Job swap = JobMaker.MakeJob(ShiftChangeDefOf.ShiftChange_SwapAtStand, parent);
            best.jobs.StartJob(swap, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: true, cancelBusyStances: true, null, JobTag.ChangingApparel);
            // The suspended job goes back on the queue as the same object, and
            // is the job they were dressed for, as Insert records it.
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
    }
}
