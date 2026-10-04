// HARNESS only — see the configuration table in ShiftChange.csproj. The
// harness is dev tooling and does not ship: a Release build compiles this
// file out entirely, and devtools/run-harness.sh asks for it back with
// -p:Harness=true on top of Release codegen.
//
// The guard is whole-file, always. Never put an #if HARNESS inside a file
// that ships — a shipping build and a harness build must differ by the
// presence of these types and by nothing else, or a harness run stops saying
// anything about the assembly that goes out. check-invariants.py enforces it.
#if HARNESS
using System.Linq;
using RimWorld;
using Verse;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Harness cases for the stand's own buttons: what the switch says. Built
    /// the way the gizmo bar builds them, by asking the comps for their gizmos,
    /// and never drawn.
    /// </summary>
    internal static class HarnessGizmos
    {
        /// <summary>The stand's switch: the one command <see cref="CompShiftStand"/> yields.</summary>
        internal static Command_Action Switch(CompShiftStand comp)
        {
            return comp.CompGetGizmosExtra().OfType<Command_Action>().FirstOrDefault();
        }

        internal static string Shift(string what)
        {
            return "ShiftChange.RegimeShift".Translate(what).RawText;
        }

        /// <summary>
        /// THE SWITCH NAMES WHAT THE STAND SERVES, recreation and sleep
        /// included.
        ///
        /// <para>Its face was built from the work types alone, so a working
        /// recreation or sleep stand read "Shift stand (no work here yet)" while
        /// its inspect pane, built from all three triggers, said what it served.
        /// The README already promised "Shift stand: recreation".</para>
        /// </summary>
        internal static bool SwitchNamesWhatTheStandServes(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (doctor == null)
            {
                return Expect(false, "the doctor work type resolves");
            }
            string recreation = "ShiftChange.Recreation".Translate().RawText;
            string sleep = "ShiftChange.Rest".Translate().RawText;

            bool ok = Expect(Switch(fix.Comp)?.defaultLabel == "ShiftChange.RegimeIdle".Translate().RawText,
                             "an automatic stand in a room with no role reads idle (control)");

            fix.Comp.ToggleWork(doctor);
            ok &= Expect(Switch(fix.Comp)?.defaultLabel == Shift(WorkTypeLabels.Of(doctor)),
                         "a doctoring stand names doctoring (control)");

            fix.Comp.ToggleRecreation();
            ok &= Expect(fix.Comp.HandlesRecreation(), "the stand is on recreation (control)")
                & Expect(Switch(fix.Comp)?.defaultLabel == Shift(recreation),
                         "a recreation stand names recreation (read: \""
                         + Switch(fix.Comp)?.defaultLabel + "\")");

            fix.Comp.ToggleRest();
            ok &= Expect(fix.Comp.HandlesRest(), "the stand is on sleep (control)")
                & Expect(Switch(fix.Comp)?.defaultLabel == Shift(sleep),
                         "a sleep stand names sleep (read: \"" + Switch(fix.Comp)?.defaultLabel + "\")");

            fix.Comp.SetExcluded();
            return ok & Expect(Switch(fix.Comp)?.defaultLabel == "ShiftChange.RegimeOff".Translate().RawText,
                               "and an excluded stand still reads as not used (control)");
        }
    }
}
#endif
