// HARNESS only: dev tooling, compiled out of a plain Release build (the
// configuration table is in ShiftChange.csproj). The guard is whole-file,
// always, and check-invariants.py enforces it; why: docs/DESIGN.md,
// "Development tooling".
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
        /// THE ALERT LISTS A STAND THAT SERVES NOTHING. Vanilla made the alert
        /// from the subclass alone. It leaves out an area stand that serves
        /// (control), lists the stand while it stands outside its area and once
        /// the area is deleted, naming the area each time, and drops it when
        /// the stand serves its room again. A colonist in the stand's uniform,
        /// kept in it by work inside the area, changes back for the same work
        /// once the area is deleted.
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

        /// <summary>
        /// THE SERVES MENU OFFERS THE MAP'S ALLOWED AREAS, Home excepted, in
        /// three sections: this room and a radius, the areas, then New allowed
        /// area and Manage areas, with a gap opening each section after the
        /// first. Each area carries its own colour. One that leaves the stand
        /// out is faded, says why in red, and binds all the same when picked,
        /// leaving the stand serving nothing, which is why it is marked. Read
        /// off the window's own builder, since no case draws the window.
        /// </summary>
        internal static bool ServesMenuOffersTheAllowedAreas(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (doctor == null)
            {
                return Expect(false, "the Doctor work type resolves");
            }
            fix.Comp.ToggleWork(doctor);
            IntVec3 at = fix.Stand.Position;
            IntVec3 inRoom = at + new IntVec3(2, 0, 2);
            Area_Allowed with = NewArea(fix, "Shift harness with", at, inRoom);
            Area_Allowed without = NewArea(fix, "Shift harness without", inRoom);
            if (with == null || without == null)
            {
                return Expect(false, "the map has room for two more allowed areas");
            }

            List<FloatMenuOption> options = new Dialog_SetStandWorkTypes(fix.Comp).ServesMenuOptions();
            int areas = fix.Map.areaManager.AllAreas.Count(a => a.AssignableAsAllowed() && !(a is Area_Home));
            string withoutLabel = Dialog_SetStandWorkTypes.AreaLeavingStandOut(without.Label);
            string redTag = string.Empty.Colorize(ColorLibrary.RedReadable).Replace("</color>", string.Empty);
            string home = fix.Map.areaManager.Home?.Label;
            ServesMenuOption pickWith = options.Find(o => o.Label == with.Label) as ServesMenuOption;
            ServesMenuOption pickWithout = options.Find(o => o.Label == withoutLabel) as ServesMenuOption;
            List<int> sections = options.OfType<ServesMenuOption>().Select(o => o.section).ToList();
            bool ok = Expect(options.Count == areas + 4,
                             "one option per allowed area, and four more (" + options.Count + " for " + areas + ")")
                    & Expect(options.Count > 1
                             && options[0].Label == (string)"ShiftChange.ServesOptionRoom".Translate()
                             && options[1].Label == (string)"ShiftChange.ServesOptionRadius".Translate(),
                             "this room and a radius come first")
                    & Expect(options.Count > 1
                             && options[options.Count - 2].Label == (string)"ShiftChange.ServesOptionNewArea".Translate()
                             && options[options.Count - 1].Label == (string)"ManageAreas".Translate(),
                             "New allowed area and Manage areas come last")
                    & Expect(sections.Count == options.Count
                             && sections.Take(2).All(s => s == ServesMenuOption.SectionWhere)
                             && sections.Skip(2).Take(areas).All(s => s == ServesMenuOption.SectionAreas)
                             && sections.Skip(2 + areas).All(s => s == ServesMenuOption.SectionManage),
                             "every row sits in its section: where, the areas, then making and managing")
                    & Expect(pickWith != null && pickWith.mouseoverGuiAction != null,
                             "an area holding the stand goes by its name, and hovering draws it")
                    & Expect(pickWith != null && ReferenceEquals(Swatch(pickWith), with.ColorTexture)
                             && pickWith.iconColor.a == 1f,
                             "with its own colour beside it")
                    & Expect(pickWithout != null && ReferenceEquals(Swatch(pickWithout), without.ColorTexture)
                             && pickWithout.iconColor.a < 1f,
                             "one leaving the stand out has its colour faded")
                    & Expect(withoutLabel.StartsWith(redTag)
                             && withoutLabel.Contains(without.Label.Colorize(ColoredText.SubtleGrayColor)),
                             "and its name faded, inside a red label saying why")
                    & Expect(home != null && !options.Any(o => o.Label == home), "Home is not offered");
            if (pickWith == null || pickWithout == null)
            {
                return false;
            }

            // The gaps, as the menu would get them once it has sized the rows.
            Dictionary<FloatMenuOption, float> heights = options.ToDictionary(o => o, o => o.RequiredHeight);
            ServesMenuOption.MarkSections(options);
            List<ServesMenuOption> gapped = options.OfType<ServesMenuOption>().Where(o => o.gapAbove > 0f).ToList();
            ok &= Expect(ServesMenuOption.RequiredHeightRef != null,
                         "the row height vanilla's menu lays out by is still reachable (precondition)")
                & Expect(gapped.Count == 2 && gapped[0] == options[2] && gapped[1] == options[options.Count - 2],
                         "a gap opens the areas and the last section, and nothing else")
                & Expect(gapped.All(o => o.RequiredHeight == heights[o] + ServesMenuOption.SectionGap),
                         "each row opening a section is taller by the gap");

            pickWith.action();
            ok &= Expect(fix.Comp.serves == CatchmentKind.Area && fix.Comp.serveArea == with
                         && fix.Comp.ServeFault == CatchmentFault.None,
                         "picking an area binds it, and the stand serves it");
            pickWithout.action();
            ok &= Expect(fix.Comp.serveArea == without && fix.Comp.ServeFault == CatchmentFault.OutsideArea,
                         "picking the marked one binds it too, and the stand serves nothing");
            options[0].action();
            return ok & Expect(fix.Comp.serves == CatchmentKind.Room, "This room puts it back in its room (control)");
        }

        /// <summary>
        /// NEW ALLOWED AREA COPIES THE STAND'S ROOM and binds it, named for the
        /// stand's work and numbered when that name is taken. In the open it
        /// holds the stand's own cell alone. At vanilla's cap of ten allowed
        /// areas it is greyed, with vanilla's reason, and makes nothing.
        /// </summary>
        internal static bool NewAllowedAreaCopiesTheRoom(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (doctor == null)
            {
                return Expect(false, "the Doctor work type resolves");
            }
            if (fix.Outside.Area == 0)
            {
                return Expect(false, "the fixture has a door and open ground outside it");
            }
            fix.Comp.ToggleWork(doctor);
            string doctoring = WorkTypeLabels.Of(doctor).CapitalizeFirst();
            Room room = fix.Stand.GetRoom();

            Area_Allowed first = MakeFromMenu(fix, fix.Comp);
            if (first == null || room == null)
            {
                return Expect(false, "New allowed area made an area for a stand in a room");
            }
            bool ok = Expect(first.Label == (string)"ShiftChange.NewAreaLabel".Translate(doctoring),
                             "it is named for the stand's work (" + first.Label + ")")
                    & Expect(room.Cells.All(c => first[c]) && first.TrueCount == room.CellCount,
                             "it is a copy of the stand's room, no more (" + first.TrueCount + " of " + room.CellCount + ")")
                    & Expect(fix.Comp.serves == CatchmentKind.Area && fix.Comp.serveArea == first
                             && fix.Comp.ServeFault == CatchmentFault.None,
                             "and the stand serves it at once");

            Area_Allowed second = MakeFromMenu(fix, fix.Comp);
            ok &= Expect(second != null
                         && second.Label == (string)"ShiftChange.NewAreaLabelNumbered".Translate(doctoring, 2)
                         && fix.Comp.serveArea == second,
                         "a second takes the next number, and the stand moves to it");

            CompShiftStand open = StageStand(fix, new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ));
            if (open == null)
            {
                return ok & Expect(false, "a stand could be staged in the open");
            }
            open.ToggleWork(doctor);
            Area_Allowed outdoors = MakeFromMenu(fix, open);
            ok &= Expect(open.StandsInTheOpen && outdoors != null && outdoors.TrueCount == 1
                         && outdoors[open.parent.Position] && open.ServeFault == CatchmentFault.None,
                         "in the open it holds the stand's own cell alone, and the stand serves it");

            for (int n = 1; fix.Map.areaManager.CanMakeNewAllowed(); n++)
            {
                if (NewArea(fix, "Shift harness filler " + n) == null)
                {
                    break;
                }
            }
            List<FloatMenuOption> cappedMenu = new Dialog_SetStandWorkTypes(fix.Comp).ServesMenuOptions();
            string newLabel = "ShiftChange.ServesOptionNewArea".Translate();
            ServesMenuOption capped = cappedMenu.Find(o => o.Label == newLabel) as ServesMenuOption;
            ServesMenuOption manage = cappedMenu.LastOrDefault() as ServesMenuOption;
            ServesMenuOption.MarkSections(cappedMenu);
            int count = fix.Map.areaManager.AllAreas.Count;
            return ok & Expect(capped != null && capped.Disabled && capped.tooltip.HasValue
                               && capped.tooltip.Value.text
                                  == (string)"MaxAreasReached".Translate(AreaManager.MaxAllowedAreas),
                               "at ten allowed areas it is greyed, with vanilla's reason")
                      & Expect(capped != null && manage != null && manage != capped
                               && manage.gapAbove > 0f && capped.gapAbove == 0f,
                               "greyed, it sorts below Manage areas, so the gap opens the section there instead")
                      & Expect(fix.Comp.MakeServeArea() == null && fix.Map.areaManager.AllAreas.Count == count
                               && fix.Comp.serveArea == second,
                               "and makes nothing, the stand keeping its area");
        }

        /// <summary>The texture a float menu row draws as its icon, which vanilla keeps private.</summary>
        internal static object Swatch(FloatMenuOption option)
        {
            return AccessTools.Field(typeof(FloatMenuOption), "iconTex")?.GetValue(option);
        }

        /// <summary>The Serves menu's New allowed area option for <paramref name="comp"/>, or null.</summary>
        internal static FloatMenuOption NewAreaOption(CompShiftStand comp)
        {
            string label = "ShiftChange.ServesOptionNewArea".Translate();
            return new Dialog_SetStandWorkTypes(comp).ServesMenuOptions().Find(o => o.Label == label);
        }

        /// <summary>
        /// Picks New allowed area from <paramref name="comp"/>'s Serves menu and
        /// returns the area it made, or null. Recorded on the fixture, whose
        /// teardown deletes it.
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
        /// PAINT AND MANAGE AREAS CLOSE THE SETTINGS WINDOW FIRST, since it
        /// absorbs clicks around itself and would swallow the brush. Paint
        /// leaves vanilla's Expand allowed area tool selected on the stand's
        /// area; Manage areas opens vanilla's dialog. Both windows really open
        /// on the stack here, and everything is closed and put down after.
        /// </summary>
        internal static bool PaintAndManageAreasCloseTheWindowFirst(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (doctor == null)
            {
                return Expect(false, "the Doctor work type resolves");
            }
            fix.Comp.ToggleWork(doctor);
            Area_Allowed area = NewArea(fix, "Shift harness area", fix.Stand.Position);
            if (area == null)
            {
                return Expect(false, "the map has room for another allowed area");
            }
            fix.Comp.SetServeArea(area);

            bool ok = false;
            Dialog_SetStandWorkTypes dialog = new Dialog_SetStandWorkTypes(fix.Comp);
            try
            {
                Find.WindowStack.Add(dialog);
                ok = Expect(Find.WindowStack.IsOpen(dialog), "the settings window is open (control)");
                dialog.Paint(area);
                ok &= Expect(!Find.WindowStack.IsOpen(dialog), "Paint closed it")
                    & Expect(Find.DesignatorManager.SelectedDesignator is Designator_AreaAllowedExpand
                             && Designator_AreaAllowed.SelectedArea == area,
                             "and left Expand allowed area selected, on the stand's area");
                Find.DesignatorManager.Deselect();
                Designator_AreaAllowed.ClearSelectedArea();

                dialog = new Dialog_SetStandWorkTypes(fix.Comp);
                Find.WindowStack.Add(dialog);
                string manage = "ManageAreas".Translate();
                FloatMenuOption option = dialog.ServesMenuOptions().Find(o => o.Label == manage);
                option?.action?.Invoke();
                ok &= Expect(option != null && !Find.WindowStack.IsOpen(dialog)
                             && Find.WindowStack.IsOpen<Dialog_ManageAreas>(),
                             "Manage areas closed it and opened vanilla's dialog");
            }
            finally
            {
                Find.WindowStack.TryRemove(dialog, doCloseSound: false);
                Find.WindowStack.TryRemove(typeof(Dialog_ManageAreas), doCloseSound: false);
                Find.DesignatorManager.Deselect();
                Designator_AreaAllowed.ClearSelectedArea();
            }
            return ok;
        }

        /// <summary>
        /// AN AREA STAND'S WORDS NAME ITS AREA, and say when it serves nothing.
        /// The Serves row, the inspect pane and the switch's tooltip name the
        /// area; with the stand outside it, or the area gone, the row and the
        /// pane say which and the switch reads "serves nothing", as it does for
        /// a room stand in the open. The serving state of each is the control.
        /// </summary>
        internal static bool AreaWordsNameTheArea(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (doctor == null)
            {
                return Expect(false, "the Doctor work type resolves");
            }
            if (fix.Outside.Area == 0)
            {
                return Expect(false, "the fixture has a door and open ground outside it");
            }
            fix.Comp.ToggleWork(doctor);
            IntVec3 at = fix.Stand.Position;
            const string name = "Shift harness area";
            Area_Allowed area = NewArea(fix, name, at, at + new IntVec3(2, 0, 2));
            if (area == null)
            {
                return Expect(false, "the map has room for another allowed area");
            }
            fix.Comp.SetServeArea(area);
            string manual = "ShiftChange.Manual".Translate();
            string doctoring = WorkTypeLabels.Of(doctor);
            string servesNothing = "ShiftChange.RegimeServesNothing".Translate().RawText;

            bool ok = Expect(fix.Comp.ServesLabel() == name, "the Serves row names the area")
                    & Expect(fix.Comp.ByCatchment("ShiftChange.RestRow") == "ShiftChange.RestRowArea",
                             "its wording is the area variant")
                    & Expect(HarnessGizmos.Switch(fix.Comp)?.defaultDesc
                             == (string)"ShiftChange.SetWorkTypeDescArea".Translate(
                                 fix.Comp.WorkTypesLabel(), manual, fix.Comp.serveRadius, name),
                             "so is its switch's tooltip, naming the area")
                    & Expect(HarnessGizmos.Switch(fix.Comp)?.defaultLabel == HarnessGizmos.Shift(doctoring),
                             "whose label names the work while it serves (control)")
                    & Expect(fix.Comp.CompInspectStringExtra()
                                 .Contains((string)"ShiftChange.InspectServesArea".Translate(name)),
                             "and the inspect pane names the area");

            area[at] = false;
            ok &= Expect(fix.Comp.ServesLabel() == (string)"ShiftChange.ServesAreaStandOutside".Translate(name),
                         "outside its area, the row says the stand is outside it")
                & Expect(fix.Comp.CompInspectStringExtra()
                             .Contains((string)"ShiftChange.InspectStandOutsideArea".Translate(name)),
                         "so does the inspect pane")
                & Expect(HarnessGizmos.Switch(fix.Comp)?.defaultLabel == servesNothing,
                         "and the switch reads serves nothing");

            area[at] = true;
            area.Delete();
            ok &= Expect(fix.Comp.ServesLabel() == (string)"ShiftChange.ServesAreaGone".Translate(name),
                         "with the area gone, the row says so")
                & Expect(fix.Comp.CompInspectStringExtra()
                             .Contains((string)"ShiftChange.InspectAreaGone".Translate(name)),
                         "so does the inspect pane")
                & Expect(HarnessGizmos.Switch(fix.Comp)?.defaultLabel == servesNothing,
                         "and the switch reads serves nothing");

            CompShiftStand open = StageStand(fix, new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ));
            if (open == null)
            {
                return ok & Expect(false, "a stand could be staged in the open");
            }
            open.ToggleWork(doctor);
            ok &= Expect(HarnessGizmos.Switch(open)?.defaultLabel == servesNothing,
                         "a room stand in the open reads serves nothing too");
            open.SetServes(CatchmentKind.Radius);
            return ok & Expect(HarnessGizmos.Switch(open)?.defaultLabel == HarnessGizmos.Shift(doctoring),
                               "until it serves a radius (control)");
        }

        /// <summary>
        /// The explanation <paramref name="alert"/> gives with a fresh report
        /// contains <paramref name="text"/>. Vanilla asks for the report first
        /// too: the explanation is built from what the report found.
        /// </summary>
        internal static bool Explains(Alert alert, string text)
        {
            alert.GetReport();
            return alert.GetExplanation().Resolve().Contains(text);
        }
    }
}
#endif
