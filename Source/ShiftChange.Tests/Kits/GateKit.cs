using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Jobs and areas for the tests of the gates on interception (<see cref="MealsDangerAndDuties"/>,
    /// <see cref="AfterChangingBack"/>): doctoring at a cell, and an allowed area that leaves the
    /// stand out.
    /// </summary>
    internal static class GateKit
    {
        /// <summary>An automatic doctoring job at a cell, as the work arm sees one.</summary>
        internal static Job DoctoringAt(WorkGiverDef giver, IntVec3 at)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Wait, at);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>
        /// An allowed area holding every cell of the stand's room except the stand's own: the work
        /// is inside it and the stand is not. Better Pawn Control's alert areas, or any
        /// hand-painted area, can leave a stand out the same way.
        /// </summary>
        internal static Area_Allowed AreaWithoutTheStand(Scene scene)
        {
            Area_Allowed area;
            if (!scene.Map.areaManager.TryMakeNewAllowed(out area))
            {
                return null;
            }
            foreach (IntVec3 cell in scene.OutfitStand.GetRoom().Cells)
            {
                if (cell != scene.OutfitStand.Position)
                {
                    area[cell] = true;
                }
            }
            return area;
        }

    }
}
