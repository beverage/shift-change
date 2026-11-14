using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Set-up and jobs for the tests of catchments: a stand serving a radius around itself, or an
    /// allowed area, instead of its room (docs/DESIGN.md, "Catchments").
    /// </summary>
    internal static partial class HarnessCatchments
    {
        /// <summary>The cells in the game's own radial pattern, before a mod enlarges it.</summary>
        internal const int GameRadialCells = 20000;

        /// <summary>
        /// A job of this giver's work type at a cell, reserving nothing:
        /// <see cref="HarnessGates.DoctoringAt"/> for any giver.
        /// </summary>
        internal static Job WorkAt(WorkGiverDef giver, IntVec3 at)
        {
            return HarnessGates.DoctoringAt(giver, at);
        }

        /// <summary>A haul to a cell, from a hauling giver.</summary>
        internal static Job Haul(WorkGiverDef giver, Thing thing, IntVec3 to)
        {
            Job job = HarnessRideAlong.GiverlessHaul(thing, to);
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

        /// <summary>Another stand on the fixture's map, stocked with a duster, or null.</summary>
        internal static CompShiftStand StageStand(Fixture fix, IntVec3 at)
        {
            Building_OutfitStand stand = DebugTools_Fixtures.Spawn(
                fix.Map, fix.Stand.def, ThingDefOf.WoodLog, at, Rot4.North) as Building_OutfitStand;
            return stand != null && StockOne(stand, "Apparel_Duster")
                ? stand.TryGetComp<CompShiftStand>()
                : null;
        }

        /// <summary>
        /// A new allowed area on the fixture's map, named and painted on <paramref name="cells"/>,
        /// or null when the map already holds ten, vanilla's cap. Recorded on the fixture, whose
        /// teardown deletes it, since an area outlives the pad.
        /// </summary>
        internal static Area_Allowed NewArea(Fixture fix, string label, params IntVec3[] cells)
        {
            Area_Allowed area;
            if (!fix.Map.areaManager.TryMakeNewAllowed(out area))
            {
                return null;
            }
            fix.Areas.Add(area);
            area.SetLabel(label);
            for (int i = 0; i < cells.Length; i++)
            {
                area[cells[i]] = true;
            }
            return area;
        }

        /// <summary>
        /// A fresh job of <paramref name="def"/> on <paramref name="target"/>, as
        /// <paramref name="giver"/> hands it out. One per probe: jobs are pooled, and one a probe
        /// queued can be handed out again once the queue is cleared.
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
