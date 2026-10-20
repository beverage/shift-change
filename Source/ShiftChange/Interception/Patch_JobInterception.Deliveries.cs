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
        /// <summary>
        /// A colonist on shift at <paramref name="stand"/> keeps its uniform on for this delivery
        /// or ignored giver's job when the stand serves its work (<see cref="ServesSiteWork"/>)
        /// and, for a delivery or a meal run, every site it supplies is inside the catchment
        /// (<see cref="SitesCovered"/>): a site outside sends them back now, while the stand is
        /// near, not after the walk out. Rimatomics' chemfuel run, the one ignored giver that is
        /// not a meal run, goes by its work alone. docs/DESIGN.md, "Mod compatibility".
        /// </summary>
        internal static bool KeepsTheUniformFor(Job job, JobClass c, CompShiftStand stand, Map map)
        {
            return ServesSiteWork(job, stand) && (!c.HasSites || SitesCovered(job, stand, map));
        }

        /// <summary>
        /// The stand serves the work type of the giver that handed out this job, or construction
        /// for a <c>HaulToContainer</c> (building materials or an item to install) filed under a
        /// hauling work type. Every job asked about here has a giver, so this work type is
        /// <see cref="JobClass.Work"/>. Vanilla files a builder's fetch under Hauling when hauling
        /// outranks construction for that colonist or the blueprint needs more skill than they
        /// have, and Complex Jobs moves vanilla's two Hauling delivery givers to its own work type,
        /// FSFDeliver, also tagged Hauling. A fetch filed under another work type, such as VEF's
        /// art-skill deliveries under Art, is that work's, and a hardhat is not kept on for it.
        /// </summary>
        internal static bool ServesSiteWork(Job job, CompShiftStand stand)
        {
            WorkTypeDef work = job.workGiverDef?.workType;
            if (work == null)
            {
                return false;
            }
            return stand.HandlesWork(work)
                   || (job.def == JobDefOf.HaulToContainer && (work.workTags & WorkTags.Hauling) != 0
                       && stand.HandlesWork(WorkTypeDefOf.Construction));
        }

        /// <summary>
        /// Every site a delivery or a meal run supplies is inside the stand's catchment: targetB
        /// (the first drop, or the patient or prisoner), targetB's queue (a delivery's other
        /// sites), and targetC where it is a site (<see cref="SiteInTargetC"/>), each judged by
        /// <see cref="PlaceCovered"/>. The stack, in targetA and its queue, is not a site.
        /// </summary>
        internal static bool SitesCovered(Job job, CompShiftStand stand, Map map)
        {
            bool named = false;
            return PlaceCovered(job.targetB, stand, map, ref named)
                   && (!SiteInTargetC(job) || PlaceCovered(job.targetC, stand, map, ref named))
                   && QueueCovered(job.targetQueueB, stand, map, ref named);
        }

        /// <summary>
        /// This job's targetC is one of its sites: the site a construction delivery's giver
        /// chose (<c>HaulToContainer</c>; targetB is the site nearest the stack, and can be
        /// another one), or the spot a prisoner's meal is left at (<c>DeliverFood</c>). In
        /// <c>PlantSeed</c> it is the stack the seeds were taken from, left where it lay, which
        /// the driver writes as the pawn picks up part of it, so a planting resumed after an
        /// interruption carries the stack's end of the trip there; in <c>FeedPatient</c> it is a
        /// colonist carrying the meal. Neither is a site.
        /// </summary>
        internal static bool SiteInTargetC(Job job)
        {
            return job.def == JobDefOf.HaulToContainer || job.def == JobDefOf.DeliverFood;
        }

        /// <summary>
        /// The return trip's answer for a delivery or a meal run the stand did not keep on
        /// (<see cref="KeepsTheUniformFor"/>): true to stay dressed. A stand serving the work
        /// changes them back, since a site is outside. Any other stand needs the whole trip
        /// inside, stack included (<see cref="EveryPlaceCovered"/>): its room in room mode, and
        /// at a radius or an area what <see cref="StaysDressed"/> says, which applies that test
        /// to pass-through work at a work stand and refuses anything else.
        /// docs/DESIGN.md, "Mod compatibility".
        /// </summary>
        internal static bool SiteJobStaysDressed(Job job, JobClass c, Pawn pawn, CompShiftStand onShift,
                                                 JobPlace target, Map map)
        {
            if (ServesSiteWork(job, onShift))
            {
                return false;
            }
            if (onShift.serves == CatchmentKind.Room)
            {
                return EveryPlaceCovered(job, pawn, onShift, map);
            }
            return StaysDressed(job, c, pawn, onShift, target, map);
        }

        /// <summary>
        /// The one way a delivery (<see cref="JobRoomTargets.IsDelivery"/>) dresses anyone: a
        /// bare colonist is dressed for it at an area stand whose area holds the stack and every
        /// site the trip supplies (<see cref="EveryPlaceCovered"/>). A room or a radius stand
        /// dresses for no delivery. The stand must serve the giver's own work type, not the
        /// construction <see cref="ServesSiteWork"/> adds: vanilla files a builder's fetch under
        /// Hauling for a colonist who ranks hauling above construction or lacks the blueprint's
        /// skill, whose next job is seldom the frame. Keeping a uniform on costs nothing and
        /// changing costs a trip, so dressing is the stricter. True if a swap started.
        /// docs/DESIGN.md, "Catchments".
        /// </summary>
        internal static bool DeliveryArm(Job job, JobTag? tag, Pawn pawn, Pawn_JobTracker tracker, Map map)
        {
            WorkTypeDef work = job.workGiverDef?.workType;
            if (work == null || CompShiftStand.OnShiftStandFor(pawn) != null || MapUnderThreat(map))
            {
                return false;
            }
            // Area stands alone, through the stand search's own gates (Consider).
            CompShiftStand best = null;
            bool bestIsPersonal = false;
            int bestDistance = int.MaxValue;
            CompShiftStand.PruneCatchmentStands();
            foreach (CompShiftStand comp in CompShiftStand.CatchmentStands)
            {
                Thing thing = comp.parent;
                if (comp.serves == CatchmentKind.Area && thing != null && thing.Spawned && thing.Map == map
                    && EveryPlaceCovered(job, pawn, comp, map))
                {
                    // With the job, so the vacuum gate runs: an area can be painted across vacuum.
                    Consider(comp, pawn, work, StandTrigger.Work, ref best, ref bestIsPersonal, ref bestDistance,
                             job);
                }
            }
            if (best == null)
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] no {work.defName} area stand whose area holds all of " +
                                $"{pawn.LabelShort}'s delivery ({job.workGiverDef.defName})");
                }
                return false;
            }
            return Insert(pawn, tracker, best, job, tag, "dress-delivery");
        }

        /// <summary>
        /// This job carries no giver, and the next job in the queue keeps this on-shift stand's
        /// uniform on: it is an errand slotted in ahead of that job, which the pawn comes
        /// straight back to. Recreation and sleep are never such an errand, and the caller decides
        /// a meal before asking. Vanilla's opportunistic haul and Common Sense's ingredient haul
        /// both build that shape, and vanilla's puts the job back with no tag. The next job
        /// qualifies when it is the job they were just dressed for (<see cref="DressedFor"/>),
        /// wherever its target went; a delivery or an ignored giver's job that
        /// <see cref="KeepsTheUniformFor"/>, but not one that only
        /// <see cref="SiteJobStaysDressed"/> keeps on, which is no work this stand serves; or
        /// other work the stand serves inside its catchment, classified with the queued job's own
        /// tag (<see cref="Classify"/>), so fishing counts with no giver stamped, as does an
        /// urgent patient's bed rest with the medical-emergency setting on. Asked on the return
        /// trip, so it only keeps a uniform on. docs/DESIGN.md, "Errands queued ahead of work".
        /// </summary>
        internal static bool QueuedAheadOfServedWork(Job job, JobClass c, Pawn pawn, Pawn_JobTracker tracker,
                                                     CompShiftStand onShift, Map map)
        {
            if (job.workGiverDef != null || c.RecreationJob || c.RestJob)
            {
                return false;
            }
            JobQueue queue = tracker?.jobQueue;
            if (queue == null || queue.Count == 0)
            {
                return false;
            }
            QueuedJob head = queue.Peek();
            Job next = head?.job;
            if (next == null)
            {
                return false;
            }
            DeferredJob deferred;
            if (DressedFor.TryGetValue(pawn, out deferred) && deferred.Is(next))
            {
                return true;
            }
            JobClass n = Classify(pawn, next, head.tag);
            if (n.DeliveryOrIgnored)
            {
                return KeepsTheUniformFor(next, n, onShift, map);
            }
            if (n.Work == null || !onShift.HandlesWork(n.Work))
            {
                return false;
            }
            return onShift.Covers(SharedTargetPlace(next, n, map), map);
        }
    }
}
