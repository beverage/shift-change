using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShiftChange.Tests
{
    internal static partial class CatchmentKit
    {
        /// <summary>
        /// A construction delivery whose first drop and site are the same needer, the one nearest
        /// the stack. <see cref="DeliveryVia"/> sets them apart and gives the giver's full shape.
        /// </summary>
        internal static Job Delivery(WorkGiverDef giver, Thing stack, Thing site, params Thing[] others)
        {
            return DeliveryVia(giver, stack, site, site, others);
        }

        /// <summary>
        /// A construction delivery shaped as the giver shapes it
        /// (<c>WorkGiver_ConstructDeliverResources.ResourceDeliverJobFor</c>): the stack in
        /// targetA, the first drop (the needer nearest the stack) in targetB, the site asked
        /// about in targetC, and the other sites the trip supplies in targetB's queue.
        /// </summary>
        internal static Job DeliveryVia(WorkGiverDef giver, Thing stack, Thing firstDrop, Thing site,
                                        params Thing[] others)
        {
            Job job = JobMaker.MakeJob(JobDefOf.HaulToContainer, stack, firstDrop, site);
            for (int i = 0; i < others.Length; i++)
            {
                job.AddQueuedTarget(TargetIndex.B, others[i]);
            }
            job.count = stack.stackCount;
            job.haulMode = HaulMode.ToContainer;
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>
        /// A delivery under its giver's job def (<see cref="GiverTables.DeliveryDefFor"/>), the
        /// stack's cell in A and the site's in B and no targetC: the shape of an install, a seed
        /// planting or a replanting, not of a construction delivery. Reserves nothing.
        /// </summary>
        internal static Job DeliveryAt(WorkGiverDef giver, IntVec3 stack, IntVec3 site)
        {
            Job job = JobMaker.MakeJob(GiverTables.DeliveryDefFor(giver), stack, site);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>
        /// Removing an old floor under a blueprint, as the blueprint delivery giver hands it out
        /// (<c>RemoveExistingFloorJob</c>): a site job.
        /// </summary>
        internal static Job FloorRemovalAt(WorkGiverDef giver, IntVec3 at)
        {
            Job job = JobMaker.MakeJob(JobDefOf.RemoveFloor, at);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>A steel wall blueprint on a cell, owned by the colony.</summary>
        internal static Thing WallBlueprint(Map map, IntVec3 at)
        {
            return GenConstruct.PlaceBlueprintForBuild(ThingDefOf.Wall, at, map, Rot4.North, Faction.OfPlayer,
                ThingDefOf.Steel);
        }
    }
}
