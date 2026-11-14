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
    internal static partial class HarnessCatchments
    {
        /// <summary>
        /// The alert lists a stand that serves nothing. Vanilla creates the alert from the subclass
        /// alone. It leaves out an area stand that serves (control), lists the stand while it
        /// stands outside its area and once the area is deleted, naming the area each time, and
        /// drops it when the stand serves its room again. A colonist in the stand's uniform, kept
        /// in it by work inside the area, changes back for the same work once the area is deleted.
        /// </summary>
        internal static bool ServesNothingAlertListsTheStand(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            if (doctor == null || tend == null)
            {
                return Expect(false, "the doctor work defs resolve");
            }
            Alert alert = VanillaAlert(typeof(Alert_ShiftStandServesNothing));
            bool ok = Expect(alert != null, "vanilla's alert readout made the alert from the subclass alone");
            if (alert == null)
            {
                return false;
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 inRoom = at + new IntVec3(2, 0, 2);
            Area_Allowed area = NewArea(fix, "Shift harness area", at, inRoom);
            if (area == null)
            {
                return ok & Expect(false, "the map has room for another allowed area");
            }
            fix.Comp.SetServeArea(area);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Area && !Lists(alert, fix.Stand),
                         "a stand serving its area is not listed (control)");

            area[at] = false;
            ok &= Expect(Lists(alert, fix.Stand) && Explains(alert, "Shift harness area"),
                         "standing outside its area, it is listed, under the area's name");
            area[at] = true;
            ok &= Expect(!Lists(alert, fix.Stand), "painted back in, it is not (control)");

            ok &= Expect(RunSwap(fix), "dressed at the area stand")
                & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(!Probe(fix, WorkAt(tend, inRoom)),
                         "Doctor work in the area keeps the uniform on (control)");

            area.Delete();
            ok &= Expect(Lists(alert, fix.Stand) && Explains(alert, "Shift harness area"),
                         "with the area deleted, the stand is listed, under the area's name")
                & Expect(Probe(fix, WorkAt(tend, inRoom)),
                         "and the colonist in its uniform changes back for the same work");

            fix.Comp.SetServes(CatchmentKind.Room);
            return ok & Expect(!Lists(alert, fix.Stand),
                               "serving its room again, it is not listed (control)");
        }

        /// <summary>The icon texture of a float menu row, which vanilla keeps private.</summary>
        internal static object Swatch(FloatMenuOption option)
        {
            return AccessTools.Field(typeof(FloatMenuOption), "iconTex")?.GetValue(option);
        }

        /// <summary>The Serves menu's New allowed area option for the stand, or null.</summary>
        internal static FloatMenuOption NewAreaOption(CompShiftStand comp)
        {
            string label = "ShiftChange.ServesOptionNewArea".Translate();
            return new Dialog_SetStandWorkTypes(comp).ServesMenuOptions().Find(o => o.Label == label);
        }

        /// <summary>
        /// Picks New allowed area from <paramref name="comp"/>'s Serves menu and returns the area
        /// it made, or null. Recorded on the fixture, whose teardown deletes it.
        /// </summary>
        internal static Area_Allowed MakeFromMenu(Fixture fix, CompShiftStand comp)
        {
            List<Area> before = new List<Area>(fix.Map.areaManager.AllAreas);
            NewAreaOption(comp)?.action?.Invoke();
            Area_Allowed made = fix.Map.areaManager.AllAreas.Find(a => !before.Contains(a)) as Area_Allowed;
            if (made != null)
            {
                fix.Areas.Add(made);
            }
            return made;
        }

        /// <summary>
        /// The explanation <paramref name="alert"/> gives with a fresh report contains
        /// <paramref name="text"/>. Vanilla asks for the report first too: the explanation is built
        /// from what the report found.
        /// </summary>
        internal static bool Explains(Alert alert, string text)
        {
            alert.GetReport();
            return alert.GetExplanation().Resolve().Contains(text);
        }
    }
}
