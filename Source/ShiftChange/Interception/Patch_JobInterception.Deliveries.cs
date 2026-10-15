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
        /// A colonist on shift at <paramref name="stand"/> keeps its uniform on
        /// for this delivery or ignored giver's job: the stand serves its work
        /// (<see cref="ServesSiteWork"/>), and for a delivery or a meal run
        /// every site it supplies is inside the catchment
        /// (<see cref="SitesCovered"/>). Rimatomics' chemfuel run, the one
        /// ignored giver that is not a meal run, goes by its work alone: a
        /// stand serving its work keeps the suit on whichever processor the
        /// chemfuel goes to, inside the catchment or not. Kept on for a site
        /// outside, a delivery's or a meal run's colonist
        /// changed back at the next job there, after the walk out, rather than
        /// now, while the stand is near. docs/DESIGN.md, "Mod compatibility".
        /// </summary>
        internal static bool KeepsTheUniformFor(Job job, JobClass c, CompShiftStand stand, Map map)
        {
            return ServesSiteWork(job, stand) && (!c.HasSites || SitesCovered(job, stand, map));
        }

        /// <summary>
        /// The stand serves the work type of the giver that handed out this
        /// job (every job asked about here has one, so this is
        /// <see cref="JobClass.Work"/> wherever the giver names a work type),
        /// or construction for building materials or an item to install
        /// (<c>HaulToContainer</c>) filed under a hauling work type: vanilla
        /// files the builder's fetch under Hauling when hauling outranks
        /// construction or the blueprint needs more skill, and Complex Jobs
        /// under its own Deliver, which carries the same tag. A fetch filed
        /// under any other work type, VEF's art-skill deliveries under Art, is
        /// that work's, and a hardhat is not kept on for it.
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
        /// Every site a delivery or a meal run supplies is inside the stand's
        /// catchment, judged as <see cref="PlaceCovered"/> judges them: targetB
        /// (the first drop, or the patient or prisoner), targetB's queue (a
        /// delivery's other sites), and targetC where it is a site
        /// (<see cref="SiteInTargetC"/>). The stack, in targetA and its queue,
        /// is left out: it is where the trip starts, not where its work is.
        /// </summary>
        internal static bool SitesCovered(Job job, CompShiftStand stand, Map map)
        {
            bool named = false;
            return PlaceCovered(job.targetB, stand, map, ref named)
                   && (!SiteInTargetC(job) || PlaceCovered(job.targetC, stand, map, ref named))
                   && QueueCovered(job.targetQueueB, stand, map, ref named);
        }

        /// <summary>
        /// This job's targetC is one of its sites: the site a construction
        /// delivery's giver chose (<c>HaulToContainer</c>; targetB is the site
        /// nearest the stack, and can be another one), or the spot a
        /// prisoner's meal is left at (<c>DeliverFood</c>). The other jobs
        /// judged by their sites keep the stack's end of the trip there:
        /// <c>PlantSeed</c> writes the seed stack it split mid-job, which a
        /// planting resumed after an interruption carries, and
        /// <c>FeedPatient</c> names a colonist carrying the meal.
        /// </summary>
        internal static bool SiteInTargetC(Job job)
        {
            return job.def == JobDefOf.HaulToContainer || job.def == JobDefOf.DeliverFood;
        }

        /// <summary>
        /// The return trip's answer for a delivery or a meal run the stand did
        /// not keep on (<see cref="KeepsTheUniformFor"/>): true to stay
        /// dressed. A stand serving the work changes them back, since a site
        /// is outside. Any other stand needs the whole trip inside, the stack
        /// included (<see cref="EveryPlaceCovered"/>): in room mode the
        /// stand's room, so pyjamas and kitchen whites come off before a fetch
        /// from a stockpile outside, and at a radius or an area what
        /// <see cref="StaysDressed"/> says, which is that same test for
        /// pass-through work at a work stand and nothing anywhere else.
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
        /// The one way a delivery (<see cref="JobRoomTargets.IsDelivery"/>)
        /// dresses anyone: a bare colonist is dressed for it at an area stand
        /// whose area holds the stack and every site the trip supplies, targetA
        /// to targetC and both queues (<see cref="EveryPlaceCovered"/>). The
        /// whole trip then happens inside the area, so the wardrobe is on the
        /// way. The stand serves the giver's own work type and nothing else: a
        /// builder's fetch vanilla files under Hauling is kept on for at a
        /// construction stand (<see cref="ServesSiteWork"/>) but dressed for
        /// at none, because vanilla files it there for a colonist who ranks
        /// hauling above construction or lacks the skill for the blueprint,
        /// and either way the frame is seldom their next job. Changing costs a
        /// trip to the stand and keeping the uniform on costs nothing, so the
        /// two directions may differ. A room or a radius never dresses for a
        /// delivery, nor does an area missing part of the trip. A colonist
        /// already in uniform is decided by <see cref="KeepsTheUniformFor"/>.
        /// True if a swap started. docs/DESIGN.md, "Catchments".
        /// </summary>
        internal static bool DeliveryArm(Job job, JobTag? tag, Pawn pawn, Pawn_JobTracker tracker, Map map)
        {
            WorkTypeDef work = job.workGiverDef?.workType;
            if (work == null || CompShiftStand.OnShiftStandFor(pawn) != null || MapUnderThreat(map))
            {
                return false;
            }
            // The stand search's own gates, through Consider, over area stands
            // alone: room and radius stands dress for no delivery.
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
                    Consider(comp, pawn, work, StandTrigger.Work, ref best, ref bestIsPersonal, ref bestDistance);
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
        /// This job carries no giver, and the next job in the queue keeps this
        /// on-shift stand's uniform on: an errand slotted in ahead of it, which
        /// the pawn is coming straight back to. The next job qualifies when it
        /// is the job they were just dressed for (<see cref="DressedFor"/>),
        /// wherever its target went; a delivery or an ignored giver's job, a
        /// meal run among them, whose work the stand serves and that it keeps
        /// the uniform on for (<see cref="KeepsTheUniformFor"/>; one the return
        /// trip keeps on for another reason, <see cref="SiteJobStaysDressed"/>,
        /// does not count, as no work the stand does not serve does); or other work the stand
        /// serves, inside its catchment, classified as the dress arms classify
        /// it (<see cref="Classify"/>, with the queued job's own tag), so fishing counts with no giver
        /// stamped and no tag, as does an urgent patient's bed rest with the
        /// medical-emergency setting on (with it off,
        /// <see cref="UrgentPatientNext"/> keeps the uniform on). Vanilla's
        /// opportunistic haul and Common Sense's ingredient haul both build
        /// that shape, and vanilla's puts the job back with no tag. Asked on the return trip only, so it
        /// keeps a uniform on and never dresses anyone; recreation and sleep
        /// are excluded here, and a meal is decided before this is reached.
        /// The signal, exactly, and both sources: docs/DESIGN.md, "Errands
        /// queued ahead of work".
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
