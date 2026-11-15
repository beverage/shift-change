using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Set-up and jobs for the tests of catchments: a stand serving a radius around itself, or an
    /// allowed area, instead of its room (docs/DESIGN.md, "Catchments").
    /// </summary>
    internal static partial class CatchmentKit
    {
        /// <summary>The cells in the game's own radial pattern, before a mod enlarges it.</summary>
        internal const int GameRadialCells = 20000;

        /// <summary>
        /// A job of this giver's work type at a cell, reserving nothing:
        /// <see cref="GateKit.DoctoringAt"/> for any giver.
        /// </summary>
        internal static Job WorkAt(WorkGiverDef giver, IntVec3 at)
        {
            return GateKit.DoctoringAt(giver, at);
        }

        /// <summary>A haul to a cell, from a hauling giver.</summary>
        internal static Job Haul(WorkGiverDef giver, Thing thing, IntVec3 to)
        {
            Job job = RideAlongKit.GiverlessHaul(thing, to);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>Twenty steel on a cell, for a delivery.</summary>
        internal static Thing Steel(Map map, IntVec3 at)
        {
            Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
            steel.stackCount = 20;
            return GenSpawn.Spawn(steel, at, map);
        }

        /// <summary>Another stand on the scene's map, stocked with a duster, or null.</summary>
        internal static CompShiftStand StageStand(Scene scene, IntVec3 at)
        {
            Building_OutfitStand stand = DebugTools_Fixtures.Spawn(
                scene.Map, scene.OutfitStand.def, ThingDefOf.WoodLog, at, Rot4.North) as Building_OutfitStand;
            return stand != null && StockOne(stand, "Apparel_Duster")
                ? stand.TryGetComp<CompShiftStand>()
                : null;
        }

        /// <summary>
        /// A new allowed area on the scene's map, named and painted on <paramref name="cells"/>,
        /// or null when the map already holds ten, vanilla's cap. Recorded on the scene, whose
        /// teardown deletes it, since an area outlives the pad.
        /// </summary>
        internal static Area_Allowed NewArea(Scene scene, string label, params IntVec3[] cells)
        {
            Area_Allowed area;
            if (!scene.Map.areaManager.TryMakeNewAllowed(out area))
            {
                return null;
            }
            scene.Areas.Add(area);
            area.SetLabel(label);
            for (int i = 0; i < cells.Length; i++)
            {
                area[cells[i]] = true;
            }
            return area;
        }

        /// <summary>
        /// A fresh job of <paramref name="def"/> on <paramref name="target"/>, as
        /// <paramref name="giver"/> hands it out. One per ask
        /// (<see cref="SceneKit.SendsToChange"/>): jobs are pooled, and one an ask queued
        /// can be handed out again once the queue is cleared.
        /// </summary>
        internal static Job JobOn(JobDef def, WorkGiverDef giver, Thing target)
        {
            Job job = JobMaker.MakeJob(def, target);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>Paints or clears the on-map cells of <paramref name="cells"/>.</summary>
        internal static void SetCells(Area area, List<IntVec3> cells, bool painted)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].InBounds(area.Map))
                {
                    area[cells[i]] = painted;
                }
            }
        }

        /// <summary>Whether a fresh report from the alert lists <paramref name="stand"/>.</summary>
        internal static bool Lists(Alert alert, Thing stand)
        {
            AlertReport report = alert.GetReport();
            return report.active && report.culpritsThings != null && report.culpritsThings.Contains(stand);
        }

        /// <summary>
        /// The instance of <paramref name="type"/> that vanilla's alert readout made for this game,
        /// or null. Read off the readout's private list, since what is under test is that vanilla
        /// made it from the subclass alone.
        /// </summary>
        internal static Alert VanillaAlert(System.Type type)
        {
            AlertsReadout readout = (Find.UIRoot as UIRoot_Play)?.alerts;
            List<Alert> all = readout == null
                ? null
                : AccessTools.Field(typeof(AlertsReadout), "AllAlerts").GetValue(readout) as List<Alert>;
            return all?.Find(a => a != null && a.GetType() == type);
        }

    }
}
