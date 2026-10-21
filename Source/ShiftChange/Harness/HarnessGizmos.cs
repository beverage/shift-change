// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
#if HARNESS
using System.Linq;
using RimWorld;
using Verse;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Cases for the stand's buttons: what the switch says, and that selecting several stands keeps
    /// each stand's buttons its own. Read from the comps' gizmos, never drawn.
    /// </summary>
    internal static class HarnessGizmos
    {
        /// <summary>The stand's switch: the first <c>Command_Action</c> its comp yields.</summary>
        internal static Command_Action Switch(CompShiftStand comp)
        {
            return comp.CompGetGizmosExtra().OfType<Command_Action>().FirstOrDefault();
        }

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
        /// The switch names what the stand serves, recreation and sleep included; the idle,
        /// doctoring and excluded stands are controls. Guards a past bug where the switch listed
        /// work types alone, so a working recreation or sleep stand read as having no work yet
        /// (docs/DESIGN.md, "Rooms to work types").
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
        /// Selecting several stands shows each stand's own buttons, the switch and Set owner alike.
        /// Why a merged button misled: docs/DESIGN.md, "Rooms to work types". The control is two
        /// plain commands with the same face, which do merge (<c>Command.GroupsWith</c> compares
        /// label, icon, hotkey and group key), so it is the <c>groupable</c> flag on our commands
        /// that keeps the stands' buttons apart.
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
