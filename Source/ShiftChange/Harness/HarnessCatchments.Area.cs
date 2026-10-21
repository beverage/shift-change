// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
#if HARNESS
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    internal static partial class HarnessCatchments
    {
        /// <summary>
        /// A bedroom stand moved to an area keeps its work list. Leaving room mode ticks the room's
        /// Sleeping for real; since the bedroom suggests it anyway, the settings window keeps the
        /// work list in reach, and ticking a work type swaps Sleeping out with the stand still on
        /// its area. Controls: the automatic bedroom stand, and Sleeping ticked once the room is no
        /// bedroom, which hides the list.
        /// </summary>
        internal static bool BedroomStandMovedToAnAreaKeepsItsWorkList(Fixture fix)
        {
            WorkTypeDef growing = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Growing");
            if (growing == null)
            {
                return Expect(false, "the Growing work type resolves");
            }
            IntVec3 at = fix.Stand.Position;
            Building_Bed bed = DebugTools_Fixtures.Spawn(fix.Map, ThingDefOf.Bed, ThingDefOf.WoodLog,
                new IntVec3(at.x + 2, 0, at.z + 2), Rot4.North) as Building_Bed;
            if (bed == null)
            {
                return Expect(false, "a bed spawns in the stand's room");
            }
            bool ok = Expect(fix.Comp.IsAutomatic && fix.Comp.HandlesRest(),
                             "the bed makes this a bedroom, and the automatic stand serves sleep (control)")
                    & Expect(!fix.Comp.HidesWorkList, "with its work list in reach (control)");

            Area_Allowed area = NewArea(fix, "Shift harness farm", at, at + new IntVec3(1, 0, 1));
            if (area == null)
            {
                return ok & Expect(false, "the map has room for another allowed area");
            }
            fix.Comp.SetServeArea(area);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Area && fix.Comp.IsTriggerOverridden && fix.Comp.HandlesRest(),
                         "serving an area, the stand has its Sleeping ticked for real")
                & Expect(fix.Comp.TriggerIsRoomsOwn && !fix.Comp.HidesWorkList,
                         "and, the bedroom suggesting it anyway, keeps its work list in reach");

            fix.Comp.ToggleWork(growing);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Area && fix.Comp.serveArea == area,
                         "ticking Growing keeps the stand on its area")
                & Expect(fix.Comp.HandlesWork(growing) && !fix.Comp.HandlesRest() && !fix.Comp.IsTriggerOverridden,
                         "and swaps Sleeping out for Growing");

            fix.Comp.ToggleRest();
            ok &= Expect(fix.Comp.HandlesRest() && !fix.Comp.HidesWorkList,
                         "Sleeping ticked by hand in the bedroom keeps the list as well");
            bed.Destroy(DestroyMode.Vanish);
            return ok & Expect(!fix.Comp.TriggerIsRoomsOwn && fix.Comp.HidesWorkList,
                               "with the bed gone the room is no bedroom, and Sleeping hides the list (control)");
        }

        /// <summary>
        /// An area covers its painted cells while the stand stands in one: a painted cell past the
        /// walls, not an unpainted cell of the stand's own room, since the area replaces the room,
        /// and nothing once the stand's own cell is cleared. Room mode's answers on the same cells
        /// are the control. Binding needs something ticked and an area a colonist could be
        /// restricted to, and the radius served before comes back on switching to a radius.
        /// </summary>
        internal static bool AreaCoversItsCells(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (doctor == null)
            {
                return Expect(false, "the Doctor work type resolves");
            }
            IntVec3 at = fix.Stand.Position;
            IntVec3 inRoom = at + new IntVec3(2, 0, 2);
            IntVec3 farCorner = at + new IntVec3(4, 0, 4);
            IntVec3 pastWall = at + new IntVec3(-2, 0, 2);
            Area_Allowed area = NewArea(fix, "Shift harness area", at, inRoom, pastWall);
            if (area == null)
            {
                return Expect(false, "the map has room for another allowed area");
            }

            bool ok = Expect(fix.Comp.Covers(farCorner, fix.Map) && !fix.Comp.Covers(pastWall, fix.Map),
                             "room mode covers the room's far corner and nothing past the wall (control)");
            fix.Comp.SetServeArea(area);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Room && fix.Comp.serveArea == null,
                         "with nothing ticked, the stand cannot serve an area, and the refusal binds nothing");

            fix.Comp.ToggleWork(doctor);
            fix.Comp.SetServeArea(fix.Map.areaManager.BuildRoof);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Room && fix.Comp.serveArea == null,
                         "nor the build-roof area, which no colonist can be restricted to");

            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(9);
            fix.Comp.SetServeArea(area);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Area && fix.Comp.serveArea == area,
                         "with Doctoring ticked, it serves the area")
                & Expect(CompShiftStand.CatchmentStands.Contains(fix.Comp),
                         "and is in the registry")
                & Expect(fix.Comp.Covers(inRoom, fix.Map),
                         "which covers a painted cell of its room")
                & Expect(fix.Comp.Covers(pastWall, fix.Map),
                         "and a painted cell past the wall")
                & Expect(!fix.Comp.Covers(farCorner, fix.Map),
                         "but not the room's unpainted far corner: the area replaces the room")
                & Expect(!fix.Comp.Covers(inRoom, null),
                         "and nothing on no map");

            area[at] = false;
            ok &= Expect(!fix.Comp.Covers(inRoom, fix.Map) && !fix.Comp.Covers(pastWall, fix.Map),
                         "with its own cell cleared, the stand stands outside its area and covers nothing");
            area[at] = true;
            ok &= Expect(fix.Comp.Covers(inRoom, fix.Map),
                         "painted back in, it covers again (control)");

            fix.Comp.SetServes(CatchmentKind.Radius);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Radius && fix.Comp.serveRadius == 9,
                         "switched to a radius, it serves the radius it had before");
            fix.Comp.SetServes(CatchmentKind.Area);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Area && fix.Comp.serveArea == area,
                         "and switched back, the area it was bound to");

            fix.Comp.ToggleWork(doctor);
            return ok & Expect(fix.Comp.IsExcluded && fix.Comp.serves == CatchmentKind.Room,
                               "unticking the last work type takes it back to its room (control)")
                      & Expect(fix.Comp.serveArea == area,
                               "with the area binding kept while the area lives");
        }

        /// <summary>
        /// An area replaces the room, through the real dressing decision. Work on a painted cell
        /// outside the stand's room dresses; work on an unpainted cell does not, the stand's own
        /// room included. Room mode's answers on the same cells are the control, and painting the
        /// far corner in brings it back.
        /// </summary>
        internal static bool AreaStandDressesWithinItsArea(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            if (doctor == null || tend == null)
            {
                return Expect(false, "the doctor work defs resolve");
            }
            if (fix.Outside.Area == 0)
            {
                return Expect(false, "the fixture has a door and open ground outside it");
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 nearOutside = new IntVec3(fix.Outside.minX, 0, at.z);
            IntVec3 farOutside = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ);
            IntVec3 farCorner = at + new IntVec3(4, 0, 4);
            Area_Allowed area = NewArea(fix, "Shift harness area", at, nearOutside);
            if (area == null)
            {
                return Expect(false, "the map has room for another allowed area");
            }

            bool ok = Expect(!Diverts(fix, HarnessGates.DoctoringAt(tend, nearOutside)),
                             "room mode: work outside the stand's room does not dress (control)")
                    & Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, farCorner)),
                             "room mode: work in the room's far corner does (control)");

            fix.Comp.SetServeArea(area);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Area,
                         "the stand now serves the area (control)")
                & Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, nearOutside)),
                         "work on a painted cell outside the room dresses")
                & Expect(!Diverts(fix, HarnessGates.DoctoringAt(tend, farOutside)),
                         "work on an unpainted cell outside does not")
                & Expect(!Diverts(fix, HarnessGates.DoctoringAt(tend, farCorner)),
                         "and nor does the room's unpainted far corner: the area replaces the room");

            area[farCorner] = true;
            return ok & Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, farCorner)),
                               "painted in, the far corner dresses again (control)");
        }

        /// <summary>
        /// A deleted area leaves its stand serving nothing. The stand stays in area mode under the
        /// area's last name, a rename included, and covers no cell and dresses nobody, rather than
        /// falling back to its room. A new area under the same name is not adopted, since default
        /// area names repeat; picking it binds it, which is the paired positive.
        /// </summary>
        internal static bool DeletedAreaServesNobody(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            if (doctor == null || tend == null)
            {
                return Expect(false, "the doctor work defs resolve");
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 inRoom = at + new IntVec3(2, 0, 2);
            Area_Allowed area = NewArea(fix, "Shift harness area", at, inRoom);
            if (area == null)
            {
                return Expect(false, "the map has room for another allowed area");
            }
            fix.Comp.SetServeArea(area);
            area.SetLabel("Shift harness renamed");
            bool ok = Expect(fix.Comp.serves == CatchmentKind.Area && fix.Comp.Covers(inRoom, fix.Map),
                             "the stand serves its area (control)")
                    & Expect(fix.Comp.LiveServeArea() == area
                             && fix.Comp.serveAreaLabel == "Shift harness renamed",
                             "and, read again after a rename, goes by the area's new name")
                    & Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, inRoom)),
                             "work in the area dresses (control)");

            area.Delete();
            ok &= Expect(fix.Comp.LiveServeArea() == null && !fix.Comp.Covers(inRoom, fix.Map),
                         "with the area deleted, the stand covers nothing")
                & Expect(!Diverts(fix, HarnessGates.DoctoringAt(tend, inRoom)),
                         "and dresses nobody for the same work")
                & Expect(fix.Comp.serves == CatchmentKind.Area
                         && fix.Comp.serveAreaLabel == "Shift harness renamed",
                         "it stays in area mode under the area's last name, rather than falling back to its room");

            Area_Allowed namesake = NewArea(fix, "Shift harness renamed", at, inRoom);
            if (namesake == null)
            {
                return ok & Expect(false, "the map has room for another allowed area");
            }
            ok &= Expect(!fix.Comp.Covers(inRoom, fix.Map) && fix.Comp.serveArea != namesake,
                         "a new area under the same name is not adopted");
            fix.Comp.SetServeArea(namesake);
            return ok & Expect(fix.Comp.serveArea == namesake && fix.Comp.Covers(inRoom, fix.Map),
                               "picking it binds it, and it covers again (control)");
        }

        /// <summary>
        /// A gravship landing rebinds the area it carried. A landing copies the ship's areas under
        /// their labels, so the stand's own area object stays behind on the old map. The case
        /// deletes the old area and makes a new one under its label, which is what the stand finds
        /// on landing. Without a flight the stand does not adopt it (control: the namesake rule).
        /// After a flight on the same map (<see cref="Land"/>) it does, with nobody on shift, so
        /// the rebind has to run ahead of the early return in <c>PostSwapMap</c>. A namesake that
        /// leaves out the stand's own cell, which a carried area never does, is not adopted on
        /// landing either.
        /// </summary>
        internal static bool GravshipLandingRebindsTheArea(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (doctor == null)
            {
                return Expect(false, "the doctor work type resolves");
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 inRoom = at + new IntVec3(2, 0, 2);
            Area_Allowed area = NewArea(fix, "Shift harness deck", at, inRoom);
            if (area == null)
            {
                return Expect(false, "the map has room for another allowed area");
            }
            fix.Comp.SetServeArea(area);
            bool ok = Expect(fix.Comp.Covers(inRoom, fix.Map), "the stand serves its area (control)");

            area.Delete();
            Area_Allowed carried = NewArea(fix, "Shift harness deck", at, inRoom);
            if (carried == null)
            {
                return ok & Expect(false, "the map has room for another allowed area");
            }
            ok &= Expect(fix.Comp.serveArea != carried && !fix.Comp.Covers(inRoom, fix.Map),
                         "with no flight, an area under the same name is not adopted (control)");

            Land(fix);
            ok &= Expect(!fix.Comp.OnShift, "nobody is on shift at the stand, so PostSwapMap's early return applies")
                & Expect(fix.Comp.serveArea == carried && fix.Comp.serves == CatchmentKind.Area
                         && fix.Comp.Covers(inRoom, fix.Map),
                         "after landing, the stand is bound to the area carried under its label, and serves it");

            carried.Delete();
            Area_Allowed namesake = NewArea(fix, "Shift harness deck", inRoom);
            if (namesake == null)
            {
                return ok & Expect(false, "the map has room for another allowed area");
            }
            Land(fix);
            return ok & Expect(fix.Comp.serveArea != namesake && !fix.Comp.Covers(inRoom, fix.Map),
                               "a namesake that leaves out the stand's own cell is not adopted on landing");
        }

        /// <summary>
        /// A gravship landing staged on the fixture's own map, in the order
        /// <see cref="HarnessLifecycle.GravshipFlight"/> uses: the stand alone is despawned for
        /// replacement, set down on the same cell, then <c>PostSwapMap</c> runs. A real flight
        /// also runs <c>PreSwapMap</c> first and copies the areas between the spawn and
        /// <c>PostSwapMap</c>; the caller stages the copied area beforehand.
        /// </summary>
        internal static void Land(Fixture fix)
        {
            IntVec3 cell = fix.Stand.Position;
            Rot4 rot = fix.Stand.Rotation;
            fix.Stand.DeSpawn(DestroyMode.WillReplace);
            GenSpawn.Spawn(fix.Stand, cell, fix.Map, rot);
            fix.Stand.PostSwapMap();
        }
    }
}
#endif
