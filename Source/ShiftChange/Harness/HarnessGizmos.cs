// HARNESS only: dev tooling, compiled out of a plain Release build (the
// configuration table is in ShiftChange.csproj). The guard is whole-file,
// always, and check-invariants.py enforces it; why: docs/DESIGN.md,
// "Development tooling".
#if HARNESS
using System.Linq;
using RimWorld;
using Verse;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Harness cases for the stand's own buttons: what the switch says, and
    /// that each stand's buttons stay its own when several are selected. Built
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

        /// <summary>The stand's Set owner button.</summary>
        internal static Command_Action OwnerButton(Building_OutfitStand stand)
        {
            CompAssignableToPawn_ShiftStand comp = stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
            return comp?.CompGetGizmosExtra().OfType<Command_Action>().FirstOrDefault();
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

        /// <summary>
        /// SELECTING SEVERAL STANDS SHOWS EACH STAND'S OWN BUTTONS.
        ///
        /// <para>Commands with the same label and icon merge into one button
        /// when their owners are selected together, and a click on it runs
        /// every one of them (<c>GizmoGridDrawer</c>). Each of ours opens a
        /// dialog, and a window of a type already open closes the earlier one
        /// (<c>Window.onlyOneOfTypeAllowed</c>, <c>WindowStack.Add</c>), so the
        /// merged button configured one stand while looking like it configured
        /// them all. The Set owner button did the same for shared stands.</para>
        ///
        /// <para>The control is two plain commands built to the same face,
        /// which do merge: the fixture's two stands are alike enough to have
        /// merged, and it is our flag that keeps them apart.</para>
        /// </summary>
        internal static bool StandButtonsStaySeparate(Fixture fix)
        {
            ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
            if (standDef == null)
            {
                return Expect(false, "the stand def resolves");
            }
            Building_OutfitStand other = DebugTools_Fixtures.Spawn(fix.Map, standDef, ThingDefOf.WoodLog,
                fix.Stand.Position + new IntVec3(2, 0, 0), Rot4.North) as Building_OutfitStand;
            CompShiftStand otherComp = other?.TryGetComp<CompShiftStand>();
            if (otherComp == null)
            {
                return Expect(false, "a second stand could be staged");
            }

            Command_Action mine = Switch(fix.Comp);
            Command_Action theirs = Switch(otherComp);
            if (!Expect(mine != null && theirs != null, "both stands have a switch (control)"))
            {
                return false;
            }
            Command_Action twinA = new Command_Action { defaultLabel = mine.defaultLabel, icon = mine.icon };
            Command_Action twinB = new Command_Action { defaultLabel = mine.defaultLabel, icon = mine.icon };
            bool ok = Expect(mine.defaultLabel == theirs.defaultLabel && mine.icon == theirs.icon,
                             "two stands set up alike show identical switches (control)")
                    & Expect(twinA.GroupsWith(twinB),
                             "two plain commands with that face merge into one button (control)")
                    & Expect(!mine.GroupsWith(theirs) && !theirs.GroupsWith(mine),
                             "but the two stands' switches stay separate buttons");

            Command_Action myOwner = OwnerButton(fix.Stand);
            Command_Action theirOwner = OwnerButton(other);
            return ok
                & Expect(myOwner != null && theirOwner != null && myOwner.Label == theirOwner.Label,
                         "both shared stands show the same Set owner button (control)")
                & Expect(myOwner != null && theirOwner != null
                         && !myOwner.GroupsWith(theirOwner) && !theirOwner.GroupsWith(myOwner),
                         "and those stay separate too");
        }
    }
}
#endif
