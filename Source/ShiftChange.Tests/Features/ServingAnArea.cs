using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DevTools.Testing;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// A stand serving an allowed area: what it covers, whom it dresses and keeps dressed, the alert
    /// for a stand that serves nothing, and the menus and words for it.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.ServingAnArea)]
    internal class ServingAnArea : FeatureFixture
    {
        /// <summary>What <see cref="SceneKit.Stage"/> sets up with the pad enclosed.</summary>
        internal const string InARoom =
            "a stand holding a duster in the corner of a walled room, "
            + "and a colonist beside it in a shirt, trousers and a parka";

        /// <summary>The same, with a colonist able to doctor.</summary>
        internal const string InARoomWithADoctor =
            "a stand holding a duster in the corner of a walled room, "
            + "and a colonist who can doctor beside it in a shirt, trousers and a parka";

        /// <summary>The same, with a colonist able to mine.</summary>
        internal const string InARoomWithAMiner =
            "a stand holding a duster in the corner of a walled room, "
            + "and a colonist who can mine beside it in a shirt, trousers and a parka";

        /// <summary>
        /// What <see cref="SceneKit.StageWithDoorway(Map, CellRect, WorkTypeDef)"/> sets up.
        /// </summary>
        internal const string InARoomWithADoor =
            "a stand holding a duster in the corner of a walled room, a door beside it onto open ground, "
            + "and a colonist who can doctor beside it in a shirt, trousers and a parka";

        /// <summary>
        /// An area covers its painted cells while the stand stands in one: a painted cell past the
        /// walls, not an unpainted cell of the stand's own room, since the area replaces the room,
        /// and nothing once the stand's own cell is cleared. Room mode's answers on the same cells
        /// come first, for comparison. Binding needs something ticked and an area a colonist could
        /// be restricted to, and the radius served before comes back on switching to a radius.
        /// </summary>
        [Test]
        [TestDescription("a stand serving an allowed area covers the cells painted in it, past its walls "
                         + "too, for as long as it stands on one of them")]
        internal IEnumerator CoversWhatIsPaintedInTheArea()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Scene scene, Scenario scenario)
            {
                WorkTypeDef doctor = WorkType("Doctor");
                if (doctor == null)
                {
                    scenario.Check(false, "the game has the Doctor work type");
                    return;
                }
                IntVec3 at = scene.OutfitStand.Position;
                IntVec3 inRoom = at + new IntVec3(2, 0, 2);
                IntVec3 farCorner = at + new IntVec3(4, 0, 4);
                IntVec3 pastWall = at + new IntVec3(-2, 0, 2);
                Area_Allowed area = CatchmentKit.NewArea(scene, "Shift test area", at, inRoom, pastWall);
                if (area == null)
                {
                    scenario.Check(false, "the map has room for another allowed area");
                    return;
                }
                scenario.Note("an allowed area is painted on the stand's cell, a cell of its room "
                           + "and a cell past the west wall");

                scenario.Check(scene.Comp.Covers(farCorner, scene.Map) && !scene.Comp.Covers(pastWall, scene.Map),
                            "serving its room, the stand covers the room's far corner and nothing past the wall");
                scene.Comp.SetServeArea(area);
                scenario.Check(scene.Comp.serves == CatchmentKind.Room && scene.Comp.serveArea == null,
                            "with nothing ticked, picking the area changes nothing: the stand still serves its room");

                scene.Comp.ToggleWork(doctor);
                scenario.Note("Doctoring is ticked on the stand");
                scene.Comp.SetServeArea(scene.Map.areaManager.BuildRoof);
                scenario.Check(scene.Comp.serves == CatchmentKind.Room && scene.Comp.serveArea == null,
                            "nor can it serve the Build roof area, which no colonist can be restricted to");

                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(9);
                scenario.Note("the stand is set to serve within 9 cells, then the area is picked");
                scene.Comp.SetServeArea(area);
                scenario.Check(scene.Comp.serves == CatchmentKind.Area && scene.Comp.serveArea == area,
                            "with Doctoring ticked, it serves the area");
                scenario.Check(CompShiftStand.CatchmentStands.Contains(scene.Comp),
                            "and the stand search now looks for it beyond its room");
                scenario.Check(scene.Comp.Covers(inRoom, scene.Map), "it covers a painted cell of its room");
                scenario.Check(scene.Comp.Covers(pastWall, scene.Map), "and a painted cell past the wall");
                scenario.Check(!scene.Comp.Covers(farCorner, scene.Map),
                            "but not the room's unpainted far corner: the area replaces the room");
                scenario.Check(!scene.Comp.Covers(inRoom, null), "and nothing when asked of no map");

                area[at] = false;
                scenario.Check(!scene.Comp.Covers(inRoom, scene.Map) && !scene.Comp.Covers(pastWall, scene.Map),
                            "with its own cell cleared from the area, the stand stands outside it and covers nothing");
                area[at] = true;
                scenario.Check(scene.Comp.Covers(inRoom, scene.Map), "painted back in, it covers again");

                scene.Comp.SetServes(CatchmentKind.Radius);
                scenario.Check(scene.Comp.serves == CatchmentKind.Radius && scene.Comp.serveRadius == 9,
                            "switched to Within a radius, it serves the 9 cells it had before");
                scene.Comp.SetServes(CatchmentKind.Area);
                scenario.Check(scene.Comp.serves == CatchmentKind.Area && scene.Comp.serveArea == area,
                            "and switched back, the area it was bound to");

                scene.Comp.ToggleWork(doctor);
                scenario.Check(scene.Comp.IsExcluded && scene.Comp.serves == CatchmentKind.Room,
                            "unticking Doctoring, the last thing ticked, leaves the stand not used for "
                            + "shift changes and back on its room");
                scenario.Check(scene.Comp.serveArea == area, "still bound to the area, which is still there");
            }
        }

        /// <summary>
        /// An area replaces the room, through the real dressing decision. Work on a painted cell
        /// outside the stand's room dresses; work on an unpainted cell does not, the stand's own
        /// room included. Room mode's answers on the same cells come first, for comparison, and
        /// painting the far corner in brings it back.
        /// </summary>
        [Test]
        [TestDescription("doctoring sends a colonist to an area stand where the area is painted, "
                         + "out past the door too, and nowhere else, not even in the stand's own room")]
        internal IEnumerator DressesForWorkInTheAreaOnly()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, WorkType("Doctor")),
                          InARoomWithADoor, Body);

            void Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    scenario.Check(false, "the game has the Doctor work type and its tend to patients work");
                    return;
                }
                if (scene.Outside.Area == 0)
                {
                    scenario.Check(false, "the room has a door and open ground outside it");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                scenario.Note("Doctoring is ticked on the stand, and the map is calm");

                IntVec3 at = scene.OutfitStand.Position;
                IntVec3 nearOutside = new IntVec3(scene.Outside.minX, 0, at.z);
                IntVec3 farOutside = new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ);
                IntVec3 farCorner = at + new IntVec3(4, 0, 4);
                Area_Allowed area = CatchmentKit.NewArea(scene, "Shift test area", at, nearOutside);
                if (area == null)
                {
                    scenario.Check(false, "the map has room for another allowed area");
                    return;
                }
                scenario.Note("an allowed area is painted on the stand's cell and the cell just outside the door");

                scenario.Check(!SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, nearOutside)),
                            "serving its room, the stand does not send " + name
                            + " to change for doctoring just outside the door");
                scenario.Check(SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, farCorner)),
                            "but does for doctoring in the room's far corner");

                scene.Comp.SetServeArea(area);
                scenario.Check(scene.Comp.serves == CatchmentKind.Area, "the area picked, the stand serves it");
                scenario.Check(SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, nearOutside)),
                            "doctoring on the painted cell outside the door sends " + name + " to change");
                scenario.Check(!SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, farOutside)),
                            "doctoring on an unpainted cell further out does not");
                scenario.Check(!SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, farCorner)),
                            "and nor does doctoring in the room's unpainted far corner: the area replaces the room");

                area[farCorner] = true;
                scenario.Check(SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, farCorner)),
                            "painted in, the far corner sends " + name + " to change again");
            }
        }

        /// <summary>
        /// A bedroom stand moved to an area keeps its work list. Leaving room mode ticks the room's
        /// Sleeping for real; since the bedroom suggests it anyway, the settings window keeps the
        /// work list in reach, and ticking a work type swaps Sleeping out with the stand still on
        /// its area. The automatic bedroom stand comes first, for comparison, and Sleeping ticked
        /// once the room is no bedroom hides the list.
        /// </summary>
        [Test]
        [TestDescription("a bedroom's stand set to serve an allowed area still shows its work list, "
                         + "and ticking Growing there takes the place of Sleeping")]
        internal IEnumerator BedroomStandKeepsItsWorkListOnAnArea()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Scene scene, Scenario scenario)
            {
                WorkTypeDef growing = WorkType("Growing");
                if (growing == null)
                {
                    scenario.Check(false, "the game has the Growing work type");
                    return;
                }
                IntVec3 at = scene.OutfitStand.Position;
                Building_Bed bed = DebugTools_Fixtures.Spawn(scene.Map, ThingDefOf.Bed, ThingDefOf.WoodLog,
                    new IntVec3(at.x + 2, 0, at.z + 2), Rot4.North) as Building_Bed;
                if (bed == null)
                {
                    scenario.Check(false, "a bed is built in the stand's room");
                    return;
                }
                scenario.Note("a bed is built in the stand's room");
                scenario.Check(scene.Comp.IsAutomatic && scene.Comp.HandlesRest(),
                            "the bed makes the room a bedroom, and the stand, left on Automatic, serves sleep");
                scenario.Check(!scene.Comp.HidesWorkList, "with its work list in reach");

                Area_Allowed area = CatchmentKit.NewArea(scene, "Shift test farm", at, at + new IntVec3(1, 0, 1));
                if (area == null)
                {
                    scenario.Check(false, "the map has room for another allowed area");
                    return;
                }
                scene.Comp.SetServeArea(area);
                scenario.Check(scene.Comp.serves == CatchmentKind.Area && scene.Comp.IsTriggerOverridden
                            && scene.Comp.HandlesRest(),
                            "set to serve an allowed area, the stand has Sleeping ticked for real");
                scenario.Check(scene.Comp.TriggerIsRoomsOwn && !scene.Comp.HidesWorkList,
                            "and, since the bedroom suggests it anyway, keeps its work list in reach");

                scene.Comp.ToggleWork(growing);
                scenario.Check(scene.Comp.serves == CatchmentKind.Area && scene.Comp.serveArea == area,
                            "ticking Growing keeps the stand on its area");
                scenario.Check(scene.Comp.HandlesWork(growing) && !scene.Comp.HandlesRest()
                            && !scene.Comp.IsTriggerOverridden,
                            "and swaps Sleeping out for Growing");

                scene.Comp.ToggleRest();
                scenario.Check(scene.Comp.HandlesRest() && !scene.Comp.HidesWorkList,
                            "Sleeping ticked by hand in the bedroom keeps the work list in reach as well");
                bed.Destroy(DestroyMode.Vanish);
                scenario.Check(!scene.Comp.TriggerIsRoomsOwn && scene.Comp.HidesWorkList,
                            "with the bed gone the room is no bedroom, and Sleeping hides the work list");
            }
        }

        /// <summary>
        /// A deleted area leaves its stand serving nothing. The stand stays in area mode under the
        /// area's last name, a rename included, and covers no cell and dresses nobody, rather than
        /// falling back to its room. A new area under the same name is not adopted, since default
        /// area names repeat; picking it binds it, which is the paired positive.
        /// </summary>
        [Test]
        [TestDescription("a stand whose allowed area is deleted serves nothing rather than its room, "
                         + "and takes up a new area of the same name only when it is picked")]
        internal IEnumerator DeletedAreaLeavesTheStandServingNothing()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true,
                                          capableOf: WorkType("Doctor")),
                          InARoomWithADoctor, Body);

            void Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    scenario.Check(false, "the game has the Doctor work type and its tend to patients work");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                scenario.Note("Doctoring is ticked on the stand, and the map is calm");

                IntVec3 at = scene.OutfitStand.Position;
                IntVec3 inRoom = at + new IntVec3(2, 0, 2);
                Area_Allowed area = CatchmentKit.NewArea(scene, "Shift test area", at, inRoom);
                if (area == null)
                {
                    scenario.Check(false, "the map has room for another allowed area");
                    return;
                }
                scene.Comp.SetServeArea(area);
                area.SetLabel("Shift test renamed");
                scenario.Note("the stand is set to serve an allowed area, which is then renamed");
                scenario.Check(scene.Comp.serves == CatchmentKind.Area && scene.Comp.Covers(inRoom, scene.Map),
                            "the stand serves its area");
                scenario.Check(scene.Comp.LiveServeArea() == area
                            && scene.Comp.serveAreaLabel == "Shift test renamed",
                            "and, looked at again after the rename, goes by the area's new name");
                scenario.Check(SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, inRoom)),
                            "doctoring in the area sends " + name + " to change");

                area.Delete();
                scenario.Check(scene.Comp.LiveServeArea() == null && !scene.Comp.Covers(inRoom, scene.Map),
                            "with the area deleted, the stand covers nothing");
                scenario.Check(!SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, inRoom)),
                            "and doctoring in the same spot sends nobody to change");
                scenario.Check(scene.Comp.serves == CatchmentKind.Area
                            && scene.Comp.serveAreaLabel == "Shift test renamed",
                            "it stays set to the area, under the area's last name, rather than falling back to its room");

                Area_Allowed namesake = CatchmentKit.NewArea(scene, "Shift test renamed", at, inRoom);
                if (namesake == null)
                {
                    scenario.Check(false, "the map has room for another allowed area");
                    return;
                }
                scenario.Check(!scene.Comp.Covers(inRoom, scene.Map) && scene.Comp.serveArea != namesake,
                            "a new area painted under the same name is not taken up");
                scene.Comp.SetServeArea(namesake);
                scenario.Check(scene.Comp.serveArea == namesake && scene.Comp.Covers(inRoom, scene.Map),
                            "picking it binds it, and the stand covers again");
            }
        }

        /// <summary>
        /// A gravship landing rebinds the area it carried. A landing copies the ship's areas under
        /// their labels, so the stand's own area object stays behind on the old map. The test
        /// deletes the old area and makes a new one under its label, which is what the stand finds
        /// on landing. Without a flight the stand does not adopt it: the namesake rule. After a
        /// flight on the same map (<see cref="Land"/>) it does, with nobody on
        /// shift, so the rebind has to run ahead of the early return in <c>PostSwapMap</c>. A
        /// namesake that leaves out the stand's own cell, which a carried area never does, is not
        /// adopted on landing either.
        /// </summary>
        [Test]
        [TestDescription("after a gravship lands, a stand on its deck serves the area that flew with it, "
                         + "though nobody was wearing its outfit")]
        internal IEnumerator GravshipLandingFindsTheAreaAgain()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true,
                                          capableOf: WorkType("Doctor")),
                          InARoomWithADoctor, Body);

            void Body(Scene scene, Scenario scenario)
            {
                WorkTypeDef doctor = WorkType("Doctor");
                if (doctor == null)
                {
                    scenario.Check(false, "the game has the Doctor work type");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                scenario.Note("Doctoring is ticked on the stand, and the map is calm");

                IntVec3 at = scene.OutfitStand.Position;
                IntVec3 inRoom = at + new IntVec3(2, 0, 2);
                Area_Allowed area = CatchmentKit.NewArea(scene, "Shift test deck", at, inRoom);
                if (area == null)
                {
                    scenario.Check(false, "the map has room for another allowed area");
                    return;
                }
                scene.Comp.SetServeArea(area);
                scenario.Check(scene.Comp.Covers(inRoom, scene.Map), "the stand serves its area");

                area.Delete();
                Area_Allowed carried = CatchmentKit.NewArea(scene, "Shift test deck", at, inRoom);
                if (carried == null)
                {
                    scenario.Check(false, "the map has room for another allowed area");
                    return;
                }
                scenario.Note("the area is deleted and painted again under its name, as a landing copies "
                           + "the ship's areas");
                scenario.Check(scene.Comp.serveArea != carried && !scene.Comp.Covers(inRoom, scene.Map),
                            "with no flight, the area under the same name is not taken up");

                Land(scene);
                scenario.Note("the stand is lifted and set down on the same cell, as a gravship lands");
                scenario.Check(!scene.Comp.OnShift, "nobody is wearing the stand's outfit as it lands");
                scenario.Check(scene.Comp.serveArea == carried && scene.Comp.serves == CatchmentKind.Area
                            && scene.Comp.Covers(inRoom, scene.Map),
                            "after landing, the stand is bound to the area carried under its name, and serves it");

                carried.Delete();
                Area_Allowed namesake = CatchmentKit.NewArea(scene, "Shift test deck", inRoom);
                if (namesake == null)
                {
                    scenario.Check(false, "the map has room for another allowed area");
                    return;
                }
                Land(scene);
                scenario.Check(scene.Comp.serveArea != namesake && !scene.Comp.Covers(inRoom, scene.Map),
                            "an area of the same name that leaves out the stand's own cell is not taken up on landing");
            }
        }

        /// <summary>
        /// The Serves menu offers the map's allowed areas, Home excepted, in three sections: this
        /// room and a radius, the areas, then New allowed area and Manage areas, with a gap opening
        /// each section after the first. Each area carries its own colour. One that leaves the
        /// stand out is faded, says why in red, and binds all the same when picked, leaving the
        /// stand serving nothing, which is why it is marked. Read off the window's own builder,
        /// since no test draws the window.
        /// </summary>
        [Test]
        [TestDescription("the Serves menu lists every allowed area but Home, each in its own colour, "
                         + "fades one the stand is outside, and serves the one picked")]
        internal IEnumerator ServesMenuListsTheAllowedAreas()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Scene scene, Scenario scenario)
            {
                WorkTypeDef doctor = WorkType("Doctor");
                if (doctor == null)
                {
                    scenario.Check(false, "the game has the Doctor work type");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                scenario.Note("Doctoring is ticked on the stand");
                IntVec3 at = scene.OutfitStand.Position;
                IntVec3 inRoom = at + new IntVec3(2, 0, 2);
                Area_Allowed with = CatchmentKit.NewArea(scene, "Shift test with", at, inRoom);
                Area_Allowed without = CatchmentKit.NewArea(scene, "Shift test without", inRoom);
                if (with == null || without == null)
                {
                    scenario.Check(false, "the map has room for two more allowed areas");
                    return;
                }
                scenario.Note("two allowed areas: one painted over the stand, one beside it that leaves it out");

                List<FloatMenuOption> options = new Dialog_SetStandWorkTypes(scene.Comp).ServesMenuOptions();
                int areas = scene.Map.areaManager.AllAreas.Count(a => a.AssignableAsAllowed() && !(a is Area_Home));
                string withoutLabel = Dialog_SetStandWorkTypes.AreaLeavingStandOut(without.Label);
                // Colorize wraps its text in a tag pair, so colorizing nothing and dropping the closing
                // tag leaves the opening tag the marked label should start with.
                string redTag = string.Empty.Colorize(ColorLibrary.RedReadable).Replace("</color>", string.Empty);
                string home = scene.Map.areaManager.Home?.Label;
                ServesMenuOption pickWith = options.Find(o => o.Label == with.Label) as ServesMenuOption;
                ServesMenuOption pickWithout = options.Find(o => o.Label == withoutLabel) as ServesMenuOption;
                List<int> sections = options.OfType<ServesMenuOption>().Select(o => o.section).ToList();
                scenario.Check(options.Count == areas + 4,
                            "the Serves menu has a row per allowed area, and four more ("
                            + options.Count + " for " + areas + ")");
                scenario.Check(options.Count > 1
                            && options[0].Label == (string)"ShiftChange.ServesOptionRoom".Translate()
                            && options[1].Label == (string)"ShiftChange.ServesOptionRadius".Translate(),
                            "This room and Within a radius come first");
                scenario.Check(options.Count > 1
                            && options[options.Count - 2].Label == (string)"ShiftChange.ServesOptionNewArea".Translate()
                            && options[options.Count - 1].Label == (string)"ManageAreas".Translate(),
                            "New allowed area and Manage areas come last");
                scenario.Check(sections.Count == options.Count
                            && sections.Take(2).All(s => s == ServesMenuOption.SectionWhere)
                            && sections.Skip(2).Take(areas).All(s => s == ServesMenuOption.SectionAreas)
                            && sections.Skip(2 + areas).All(s => s == ServesMenuOption.SectionManage),
                            "every row sits in its section: where, the areas, then making and managing");
                scenario.Check(pickWith != null && pickWith.mouseoverGuiAction != null,
                            "the area painted over the stand goes by its name, and hovering over it draws it on the map");
                scenario.Check(pickWith != null && ReferenceEquals(Swatch(pickWith), with.ColorTexture)
                            && pickWith.iconColor.a == 1f,
                            "with its own colour beside it");
                scenario.Check(pickWithout != null
                            && ReferenceEquals(Swatch(pickWithout), without.ColorTexture)
                            && pickWithout.iconColor.a < 1f,
                            "the one leaving the stand out has its colour faded");
                scenario.Check(withoutLabel.StartsWith(redTag)
                            && withoutLabel.Contains(without.Label.Colorize(ColoredText.SubtleGrayColor)),
                            "and its name faded, inside a red label saying the stand is outside it");
                scenario.Check(home != null && !options.Any(o => o.Label == home), "Home is not offered");
                if (pickWith == null || pickWithout == null)
                {
                    return;
                }

                // Read the heights first, then add the gaps as the menu does, after its constructor has
                // sized the rows.
                Dictionary<FloatMenuOption, float> heights = options.ToDictionary(o => o, o => o.RequiredHeight);
                ServesMenuOption.MarkSections(options);
                List<ServesMenuOption> gapped = options.OfType<ServesMenuOption>().Where(o => o.gapAbove > 0f).ToList();
                scenario.Check(ServesMenuOption.RequiredHeightRef != null,
                            "the row height vanilla's menus lay out by can still be reached, to add the gaps to");
                scenario.Check(gapped.Count == 2 && gapped[0] == options[2] && gapped[1] == options[options.Count - 2],
                            "a gap opens the areas and the last section, and nothing else");
                scenario.Check(gapped.All(o => o.RequiredHeight == heights[o] + ServesMenuOption.SectionGap),
                            "each row opening a section is taller by the gap");

                pickWith.action();
                scenario.Check(scene.Comp.serves == CatchmentKind.Area && scene.Comp.serveArea == with
                            && scene.Comp.ServeFault == CatchmentFault.None,
                            "picking the area painted over the stand binds it, and the stand serves it");
                pickWithout.action();
                scenario.Check(scene.Comp.serveArea == without && scene.Comp.ServeFault == CatchmentFault.OutsideArea,
                            "picking the faded one binds it too, and the stand serves nothing");
                options[0].action();
                scenario.Check(scene.Comp.serves == CatchmentKind.Room, "This room puts it back on its room");
            }
        }

        /// <summary>
        /// New allowed area copies the stand's room and binds it, named for the stand's work and
        /// numbered when that name is taken. In the open it holds the stand's own cell alone. At
        /// vanilla's cap of ten allowed areas it is greyed, with vanilla's reason, and makes
        /// nothing; greyed, it sorts below Manage areas, so the section's gap opens there instead.
        /// </summary>
        [Test]
        [TestDescription("New allowed area makes a copy of the stand's room named for its work, "
                         + "and is greyed once the map has ten areas")]
        internal IEnumerator NewAllowedAreaStartsAsTheRoom()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, WorkType("Doctor")),
                          InARoomWithADoor, Body);

            void Body(Scene scene, Scenario scenario)
            {
                WorkTypeDef doctor = WorkType("Doctor");
                if (doctor == null)
                {
                    scenario.Check(false, "the game has the Doctor work type");
                    return;
                }
                if (scene.Outside.Area == 0)
                {
                    scenario.Check(false, "the room has a door and open ground outside it");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                scenario.Note("Doctoring is ticked on the stand");
                string doctoring = WorkTypeLabels.Of(doctor).CapitalizeFirst();
                Room room = scene.OutfitStand.GetRoom();

                Area_Allowed first = MakeFromMenu(scene, scene.Comp);
                if (first == null || room == null)
                {
                    scenario.Check(false, "New allowed area made an area for a stand in a room");
                    return;
                }
                scenario.Note("New allowed area is picked from the Serves menu");
                scenario.Check(first.Label == (string)"ShiftChange.NewAreaLabel".Translate(doctoring),
                            "it is named for the stand's work (" + first.Label + ")");
                scenario.Check(room.Cells.All(c => first[c]) && first.TrueCount == room.CellCount,
                            "it is a copy of the stand's room, no more ("
                            + first.TrueCount + " of " + room.CellCount + ")");
                scenario.Check(scene.Comp.serves == CatchmentKind.Area && scene.Comp.serveArea == first
                            && scene.Comp.ServeFault == CatchmentFault.None,
                            "and the stand serves it at once");

                Area_Allowed second = MakeFromMenu(scene, scene.Comp);
                scenario.Check(second != null
                            && second.Label == (string)"ShiftChange.NewAreaLabelNumbered".Translate(doctoring, 2)
                            && scene.Comp.serveArea == second,
                            "picked again, it makes a second with the next number, and the stand moves to it");

                CompShiftStand open =
                    CatchmentKit.StageStand(scene, new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ));
                if (open == null)
                {
                    scenario.Check(false, "a second stand could be built out in the open");
                    return;
                }
                open.ToggleWork(doctor);
                scenario.Note("a second stand out in the open past the door, with Doctoring ticked");
                Area_Allowed outdoors = MakeFromMenu(scene, open);
                scenario.Check(open.StandsInTheOpen && outdoors != null && outdoors.TrueCount == 1
                            && outdoors[open.parent.Position] && open.ServeFault == CatchmentFault.None,
                            "in the open, the new area holds the stand's own cell alone, and the stand serves it");

                for (int n = 1; scene.Map.areaManager.CanMakeNewAllowed(); n++)
                {
                    if (CatchmentKit.NewArea(scene, "Shift test filler " + n) == null)
                    {
                        break;
                    }
                }
                scenario.Note("allowed areas are added until the map can take no more");
                List<FloatMenuOption> cappedMenu = new Dialog_SetStandWorkTypes(scene.Comp).ServesMenuOptions();
                string newLabel = "ShiftChange.ServesOptionNewArea".Translate();
                ServesMenuOption capped = cappedMenu.Find(o => o.Label == newLabel) as ServesMenuOption;
                ServesMenuOption manage = cappedMenu.LastOrDefault() as ServesMenuOption;
                ServesMenuOption.MarkSections(cappedMenu);
                int count = scene.Map.areaManager.AllAreas.Count;
                scenario.Check(capped != null && capped.Disabled && capped.tooltip.HasValue
                            && capped.tooltip.Value.text
                               == (string)"MaxAreasReached".Translate(AreaManager.MaxAllowedAreas),
                            "at ten allowed areas, New allowed area is greyed, with vanilla's reason");
                scenario.Check(capped != null && manage != null && manage != capped
                            && manage.gapAbove > 0f && capped.gapAbove == 0f,
                            "greyed, it sorts below Manage areas, so the gap opens the section there instead");
                scenario.Check(scene.Comp.MakeServeArea() == null && scene.Map.areaManager.AllAreas.Count == count
                            && scene.Comp.serveArea == second,
                            "and it makes nothing, the stand keeping its area");
            }
        }

        /// <summary>
        /// Paint and Manage areas close the settings window first, since it absorbs clicks around
        /// itself and would swallow the brush. Paint leaves vanilla's Expand allowed area tool
        /// selected on the stand's area; Manage areas opens vanilla's dialog. Both windows really
        /// open on the stack here, and everything is closed and put down after.
        /// </summary>
        [Test]
        [TestDescription("Paint and Manage areas shut the stand's settings before vanilla's area tools open, "
                         + "so the brush is not swallowed")]
        internal IEnumerator PaintAndManageAreasCloseTheSettingsFirst()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Scene scene, Scenario scenario)
            {
                WorkTypeDef doctor = WorkType("Doctor");
                if (doctor == null)
                {
                    scenario.Check(false, "the game has the Doctor work type");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                Area_Allowed area = CatchmentKit.NewArea(scene, "Shift test area", scene.OutfitStand.Position);
                if (area == null)
                {
                    scenario.Check(false, "the map has room for another allowed area");
                    return;
                }
                scene.Comp.SetServeArea(area);
                scenario.Note("Doctoring is ticked on the stand, which serves an allowed area painted on its cell");

                Dialog_SetStandWorkTypes dialog = new Dialog_SetStandWorkTypes(scene.Comp);
                try
                {
                    Find.WindowStack.Add(dialog);
                    scenario.Check(Find.WindowStack.IsOpen(dialog), "the stand's settings window is open");
                    dialog.Paint(area);
                    scenario.Check(!Find.WindowStack.IsOpen(dialog), "Paint closed it");
                    scenario.Check(Find.DesignatorManager.SelectedDesignator is Designator_AreaAllowedExpand
                                && Designator_AreaAllowed.SelectedArea == area,
                                "and left vanilla's Expand allowed area tool in hand, on the stand's area");
                    Find.DesignatorManager.Deselect();
                    Designator_AreaAllowed.ClearSelectedArea();

                    dialog = new Dialog_SetStandWorkTypes(scene.Comp);
                    Find.WindowStack.Add(dialog);
                    scenario.Note("the settings window is opened again");
                    string manage = "ManageAreas".Translate();
                    FloatMenuOption option = dialog.ServesMenuOptions().Find(o => o.Label == manage);
                    option?.action?.Invoke();
                    scenario.Check(option != null && !Find.WindowStack.IsOpen(dialog)
                                && Find.WindowStack.IsOpen<Dialog_ManageAreas>(),
                                "Manage areas, in the Serves menu, closed it and opened vanilla's own Manage areas");
                }
                finally
                {
                    Find.WindowStack.TryRemove(dialog, doCloseSound: false);
                    Find.WindowStack.TryRemove(typeof(Dialog_ManageAreas), doCloseSound: false);
                    Find.DesignatorManager.Deselect();
                    Designator_AreaAllowed.ClearSelectedArea();
                }
            }
        }

        /// <summary>
        /// An area stand's words name its area, and say when it serves nothing. The Serves row, the
        /// inspect pane and the switch's tooltip name the area; with the stand outside it, or the
        /// area gone, the row and the pane say which and the switch reads "serves nothing", as it
        /// does for a room stand in the open. The serving state of each comes first, for comparison.
        /// </summary>
        [Test]
        [TestDescription("an area stand's Serves row, inspect pane and button name its area, "
                         + "and say so when the stand is outside it or the area is gone")]
        internal IEnumerator RowPaneAndButtonNameTheArea()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, WorkType("Doctor")),
                          InARoomWithADoor, Body);

            void Body(Scene scene, Scenario scenario)
            {
                WorkTypeDef doctor = WorkType("Doctor");
                if (doctor == null)
                {
                    scenario.Check(false, "the game has the Doctor work type");
                    return;
                }
                if (scene.Outside.Area == 0)
                {
                    scenario.Check(false, "the room has a door and open ground outside it");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                IntVec3 at = scene.OutfitStand.Position;
                const string name = "Shift test area";
                Area_Allowed area = CatchmentKit.NewArea(scene, name, at, at + new IntVec3(2, 0, 2));
                if (area == null)
                {
                    scenario.Check(false, "the map has room for another allowed area");
                    return;
                }
                scene.Comp.SetServeArea(area);
                scenario.Note("Doctoring is ticked on the stand, which serves the allowed area " + name);
                string manual = "ShiftChange.Manual".Translate();
                string doctoring = WorkTypeLabels.Of(doctor);
                string servesNothing = "ShiftChange.RegimeServesNothing".Translate().RawText;

                scenario.Check(scene.Comp.ServesLabel() == name, "the Serves row names the area");
                scenario.Check(scene.Comp.ByCatchment("ShiftChange.RestRow") == "ShiftChange.RestRowArea",
                            "the settings rows are worded for an allowed area, as in \"going to bed in its allowed area\"");
                scenario.Check(GizmoKit.Switch(scene.Comp)?.defaultDesc
                            == (string)"ShiftChange.SetWorkTypeDescArea".Translate(
                                scene.Comp.WorkTypesLabel(), manual, scene.Comp.serveRadius, name),
                            "and so is its button's tooltip, which names the area");
                scenario.Check(GizmoKit.Switch(scene.Comp)?.defaultLabel == GizmoKit.Shift(doctoring),
                            "the button reads " + GizmoKit.Shift(doctoring) + " while the stand serves");
                scenario.Check(scene.Comp.CompInspectStringExtra()
                                .Contains((string)"ShiftChange.InspectServesArea".Translate(name)),
                            "and the inspect pane names the area");

                area[at] = false;
                scenario.Check(scene.Comp.ServesLabel() == (string)"ShiftChange.ServesAreaStandOutside".Translate(name),
                            "with its own cell cleared from the area, the Serves row says the stand is outside it");
                scenario.Check(scene.Comp.CompInspectStringExtra()
                                .Contains((string)"ShiftChange.InspectStandOutsideArea".Translate(name)),
                            "so does the inspect pane");
                scenario.Check(GizmoKit.Switch(scene.Comp)?.defaultLabel == servesNothing,
                            "and the button reads " + servesNothing);

                area[at] = true;
                area.Delete();
                scenario.Check(scene.Comp.ServesLabel() == (string)"ShiftChange.ServesAreaGone".Translate(name),
                            "with the area painted back and then deleted, the Serves row says it is gone");
                scenario.Check(scene.Comp.CompInspectStringExtra()
                                .Contains((string)"ShiftChange.InspectAreaGone".Translate(name)),
                            "so does the inspect pane");
                scenario.Check(GizmoKit.Switch(scene.Comp)?.defaultLabel == servesNothing,
                            "and the button reads " + servesNothing);

                CompShiftStand open =
                    CatchmentKit.StageStand(scene, new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ));
                if (open == null)
                {
                    scenario.Check(false, "a second stand could be built out in the open");
                    return;
                }
                open.ToggleWork(doctor);
                scenario.Note("a second stand out in the open past the door, with Doctoring ticked");
                scenario.Check(GizmoKit.Switch(open)?.defaultLabel == servesNothing,
                            "serving its room out in the open, its button reads " + servesNothing + " too");
                open.SetServes(CatchmentKind.Radius);
                scenario.Check(GizmoKit.Switch(open)?.defaultLabel == GizmoKit.Shift(doctoring),
                            "until it serves a radius");
            }
        }

        /// <summary>
        /// A stand outside the colonist's allowed area dresses nobody, and an allowed one is chosen
        /// instead (docs/DESIGN.md, "Allowed areas"). Each of the three paths that dress is driven:
        /// the work arm, the selector's ranking (a colonist's own stand outranks a shared one, but
        /// not when the own stand is forbidden to them), and the mid-job catch-up. Every refusal
        /// has the same setup with the stand allowed beside it, for comparison.
        /// </summary>
        [Test]
        [TestDescription("a colonist restricted to an allowed area that leaves their stand out is not sent "
                         + "to it, and changes at a shared stand inside the area instead")]
        internal IEnumerator StandOutsideTheirAreaDressesNobody()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true,
                                          capableOf: WorkType("Doctor")),
                          InARoomWithADoctor, Body);

            void Body(Scene scene, Scenario scenario)
            {
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
                if (doctor == null || tend == null || standDef == null)
                {
                    scenario.Check(false,
                                "the game has the Doctor work type, its tend to patients work and the outfit stand");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                scenario.Note("Doctoring is ticked on the stand, and the map is calm");

                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                IntVec3 workAt = scene.OutfitStand.Position + new IntVec3(3, 0, 3);
                scenario.Check(SendsToChange(scene, GateKit.DoctoringAt(tend, workAt)),
                            "with no allowed area set, doctoring in the room sends " + name + " to change");

                Area_Allowed area = GateKit.AreaWithoutTheStand(scene);
                if (area == null)
                {
                    scenario.Check(false, "an allowed area could be made");
                    return;
                }
                Pawn bare = null;
                try
                {
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;
                    scenario.Note(name + " is restricted to an allowed area holding the whole room "
                               + "but the stand's own cell");
                    scenario.Check(scene.OutfitStand.IsForbidden(pawn),
                                "the area leaves the stand out, so it is forbidden to " + name);
                    scenario.Check(!workAt.IsForbidden(pawn), "while the work is inside it");

                    bool inserted = Patch_JobInterception.TryInsertSwap(GateKit.DoctoringAt(tend, workAt), null,
                                                                        pawn, pawn.jobs);
                    scenario.Check(!inserted, "a stand outside " + name + "'s allowed area does not send them to change");
                    if (inserted)
                    {
                        scenario.Note("a change was started anyway: still under way "
                                   + (pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand ? "yes" : "no")
                                   + ", retry cooldown set "
                                   + (Patch_JobInterception.LastBlockedTick.ContainsKey(pawn.thingIDNumber) ? "yes" : "no"));
                    }
                    ClearSwapDecision(pawn);

                    // The catch-up: a colonist already doctoring bare in the room, under the same area,
                    // built as the freed-stand catch-up's test builds one.
                    bare = DebugTools_Fixtures.AveragePawn(Gender.Female, "Bare", doctor);
                    bare.apparel?.DestroyAll();
                    bare.workSettings?.EnableAndInitialize();
                    GenSpawn.Spawn(bare, scene.OutfitStand.Position + new IntVec3(2, 0, 2), scene.Map, Rot4.North);
                    scene.Extras.Add(bare);
                    WearOne(bare, "Apparel_BasicShirt");
                    WearOne(bare, "Apparel_Pants");
                    bare.playerSettings.AreaRestrictionInPawnCurrentMap = area;
                    string other = bare.LabelShort;
                    Job working = WorkingAt(tend, scene.OutfitStand.Position + new IntVec3(3, 0, 2));
                    // Started with interception off: the stand is free, so starting this job with it on
                    // would already make the decision under test and leave the catch-up nothing to
                    // catch up.
                    bool armed = Patch_JobInterception.Enabled;
                    try
                    {
                        Patch_JobInterception.Enabled = false;
                        bare.jobs.StartJob(working, JobCondition.InterruptForced, null,
                            resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);
                    }
                    finally
                    {
                        Patch_JobInterception.Enabled = armed;
                    }
                    scenario.Note(other + ", in a shirt and trousers and restricted to the same area, "
                               + "starts doctoring in the room");
                    JobDriver before = bare.jobs.curDriver;
                    scenario.Check(StillWorking(bare, tend) && SwapPlan.WouldDress(bare, scene.OutfitStand),
                                other + " is doctoring in the room and could wear the stand's outfit");

                    Patch_JobInterception.TryDressMidJob(scene.Comp, null);
                    scenario.Check(bare.jobs.curDriver == before,
                                "a freed stand outside " + other + "'s area does not interrupt them to change");

                    bare.playerSettings.AreaRestrictionInPawnCurrentMap = null;
                    Patch_JobInterception.TryDressMidJob(scene.Comp, null);
                    scenario.Check(bare.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                                "with " + other + "'s area lifted, the same freed stand does");
                    ClearSwapDecision(bare);

                    // The ranking: their own stand outside the area, a shared one inside it. A personal
                    // stand wins any tie on everything else, so only the area can decide this.
                    Building_OutfitStand shared = DebugTools_Fixtures.Spawn(scene.Map, standDef, ThingDefOf.WoodLog,
                        scene.OutfitStand.Position + new IntVec3(2, 0, 0), Rot4.North) as Building_OutfitStand;
                    CompShiftStand sharedComp = shared?.TryGetComp<CompShiftStand>();
                    CompAssignableToPawn_ShiftStand mine =
                        scene.OutfitStand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                    if (sharedComp == null || mine == null || !StockOne(shared, "Apparel_Duster"))
                    {
                        scenario.Check(false, "a second, shared stand could be built in the room");
                        return;
                    }
                    sharedComp.ToggleWork(doctor);
                    mine.TryAssignPawn(pawn);
                    scenario.Note("the first stand is assigned to " + name
                               + "; a second stand holding a duster, inside the area, also serves doctoring");
                    scenario.Check(scene.Comp.IsAssignedTo(pawn) && sharedComp.IsPool && !shared.IsForbidden(pawn),
                                name + "'s own stand is outside the area, a shared one inside it");

                    CompShiftStand chosen = Patch_JobInterception.FindAvailableStand(workAt, scene.Map, pawn, doctor);
                    scenario.Check(chosen == sharedComp,
                                "so the shared stand inside the area is chosen over their own (chose "
                                + (chosen == null ? "nothing" : chosen == scene.Comp ? "their own" : "another")
                                + ")");
                    bool went = Patch_JobInterception.TryInsertSwap(GateKit.DoctoringAt(tend, workAt), null,
                                                                    pawn, pawn.jobs)
                                && pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand
                                && pawn.CurJob.targetA.Thing == shared;
                    scenario.Check(went, "and " + name + " goes to it to change");
                    ClearSwapDecision(pawn);
                }
                finally
                {
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = null;
                    if (bare != null && bare.playerSettings != null)
                    {
                        bare.playerSettings.AreaRestrictionInPawnCurrentMap = null;
                    }
                    area.Delete();
                }
            }
        }

        /// <summary>
        /// Recreation and sleep pass over a stand outside the area too. Neither has a gate of its
        /// own: both find their stand through <c>Patch_JobInterception.FindAvailableStand</c>,
        /// which is where the work arm's gate sits, and this pins that they keep sharing it. Each
        /// is asked three times of the same stand: with no area (dresses), with the stand left out
        /// (does not), and with it painted back in (dresses again).
        ///
        /// <para>Recreation goes first, while the room has no role: the bed the sleep half needs
        /// would make it a bedroom. Both are ticked explicitly, so the room's role decides nothing
        /// here.</para>
        /// </summary>
        [Test]
        [TestDescription("nor is a colonist sent to a stand outside their allowed area to change "
                         + "for recreation or for bed")]
        internal IEnumerator StandOutsideTheirAreaServesNoRecreationOrSleep()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Scene scene, Scenario scenario)
            {
                JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
                JobDef laydown = DefDatabase<JobDef>.GetNamedSilentFail("LayDown");
                ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                if (chess == null || laydown == null || bedDef == null)
                {
                    scenario.Check(false, "the game has playing chess, lying down and the bed");
                    return;
                }
                MakeCalm(scene.Map);
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;

                scene.Comp.ToggleRecreation();
                scenario.Note("Recreation is ticked on the stand, and the map is calm");
                scenario.Check(scene.Comp.HandlesRecreation(), "the stand serves recreation");
                scenario.Check(SendsToChange(scene, JoyJob(scene, chess)),
                            "with no allowed area set, playing chess in the room sends " + name + " to change");

                Area_Allowed area = GateKit.AreaWithoutTheStand(scene);
                if (area == null)
                {
                    scenario.Check(false, "an allowed area could be made");
                    return;
                }
                try
                {
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;
                    scenario.Note(name + " is restricted to an allowed area holding the whole room "
                               + "but the stand's own cell");
                    scenario.Check(scene.OutfitStand.IsForbidden(pawn),
                                "the area leaves the stand out, so it is forbidden to " + name);
                    scenario.Check(Patch_JobInterception.FindAvailableStand(pawn.Position, scene.Map, pawn, null,
                                    Patch_JobInterception.StandTrigger.Recreation) == null,
                                "the search for a stand to change at for recreation passes it over");
                    scenario.Check(!SendsToChange(scene, JoyJob(scene, chess)),
                                "so playing chess in the room does not send " + name + " to it");
                    area[scene.OutfitStand.Position] = true;
                    scenario.Check(SendsToChange(scene, JoyJob(scene, chess)),
                                "with the stand painted back into the area, the same game of chess does");
                    area[scene.OutfitStand.Position] = false;

                    Building_Bed bed = DebugTools_Fixtures.Spawn(scene.Map, bedDef, ThingDefOf.WoodLog,
                        scene.OutfitStand.Position + new IntVec3(2, 0, 2), Rot4.North) as Building_Bed;
                    if (bed == null)
                    {
                        scenario.Check(false, "a bed is built in the stand's room");
                        return;
                    }
                    scene.Comp.ToggleRest();
                    scenario.Note("the stand is left out of the area again, a bed is built in the room, "
                               + "and Sleeping is ticked on the stand");
                    scenario.Check(scene.Comp.HandlesRest() && !scene.Comp.HandlesRecreation(),
                                "the stand serves sleep instead of recreation");
                    scenario.Check(!bed.IsForbidden(pawn), "and the bed is inside " + name + "'s area");
                    scenario.Check(Patch_JobInterception.FindAvailableStand(bed.Position, scene.Map, pawn, null,
                                    Patch_JobInterception.StandTrigger.Rest) == null,
                                "the search for a stand to change at for bed passes the stand over");
                    scenario.Check(!SendsToChange(scene, JobMaker.MakeJob(laydown, bed)),
                                "so going to bed in the room does not send " + name + " to it");
                    area[scene.OutfitStand.Position] = true;
                    scenario.Check(SendsToChange(scene, JobMaker.MakeJob(laydown, bed)),
                                "with the stand painted back into the area, going to bed does");
                }
                finally
                {
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = null;
                    area.Delete();
                }
            }
        }

        /// <summary>
        /// The Shift stand serves nothing alert lists a stand that serves nothing. The game makes
        /// its alerts from their types alone. The alert leaves out an area stand that serves, lists
        /// the stand while it stands outside its area and once the area is deleted, naming the area
        /// each time, and drops it when the stand serves its room again. A colonist in the stand's
        /// uniform, kept in it by work inside the area, changes back for the same work once the
        /// area is deleted.
        /// </summary>
        [Test]
        [TestDescription("the \"Shift stand serves nothing\" alert lists a stand outside its allowed area or "
                         + "whose area was deleted, naming the area, and a colonist in its outfit then changes back")]
        internal IEnumerator ServesNothingAlertNamesTheArea()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true,
                                          capableOf: WorkType("Doctor")),
                          InARoomWithADoctor, Body);

            void Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    scenario.Check(false, "the game has the Doctor work type and its tend to patients work");
                    return;
                }
                Alert alert = CatchmentKit.VanillaAlert(typeof(Alert_ShiftStandServesNothing));
                if (!scenario.Check(alert != null,
                                 "the game made the \"Shift stand serves nothing\" alert from its type alone"))
                {
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                scenario.Note("Doctoring is ticked on the stand, and the map is calm");

                IntVec3 at = scene.OutfitStand.Position;
                IntVec3 inRoom = at + new IntVec3(2, 0, 2);
                Area_Allowed area = CatchmentKit.NewArea(scene, "Shift test area", at, inRoom);
                if (area == null)
                {
                    scenario.Check(false, "the map has room for another allowed area");
                    return;
                }
                scene.Comp.SetServeArea(area);
                scenario.Note("the stand is set to serve an allowed area painted on its own cell and a cell of its room");
                scenario.Check(scene.Comp.serves == CatchmentKind.Area && !CatchmentKit.Lists(alert, scene.OutfitStand),
                            "serving the area, the stand is not on the alert");

                area[at] = false;
                scenario.Check(CatchmentKit.Lists(alert, scene.OutfitStand)
                            && Explains(alert, "Shift test area"),
                            "with its own cell cleared from the area, the alert lists it, naming the area");
                area[at] = true;
                scenario.Check(!CatchmentKit.Lists(alert, scene.OutfitStand), "painted back in, it is off the alert");

                scenario.Check(SwapNow(scenario, scene), name + " changes into the area stand's outfit");
                if (!scenario.Check(scene.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                NotSentStraightBack(scenario, scene);
                scenario.Check(!SendsToChange(scene, CatchmentKit.WorkAt(tend, inRoom)),
                            "doctoring in the area keeps the uniform on");

                area.Delete();
                scenario.Check(CatchmentKit.Lists(alert, scene.OutfitStand)
                            && Explains(alert, "Shift test area"),
                            "with the area deleted, the alert lists the stand, still naming the area");
                scenario.Check(SendsToChange(scene, CatchmentKit.WorkAt(tend, inRoom)),
                            "and " + name + " changes back for the same doctoring");

                scene.Comp.SetServes(CatchmentKind.Room);
                scenario.Check(!CatchmentKit.Lists(alert, scene.OutfitStand),
                            "set to serve its room again, the stand is off the alert");
            }
        }

        /// <summary>
        /// Mining a rock is judged at the rock first, then the floor beside it. A rock has no room
        /// on its own cell, so a mining job's room cell is the floor beside it, which room mode
        /// dresses by. An area dresses a miner for the rock painted, or for only the floor around
        /// it, and not for neither; a miner in uniform keeps it for the rock alone. A wall's
        /// repair, judged as pass-through work at every place it names, keeps the uniform on with
        /// only the floor around the wall painted, and changes them back with none.
        /// </summary>
        [Test]
        [TestDescription("mining a rock in an allowed area counts where the rock is painted, then where the "
                         + "floor beside it is, and repairing a wall counts the floor around it")]
        internal IEnumerator MiningARockCountsTheRockThenTheFloor()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true,
                                          capableOf: WorkType("Mining")),
                          InARoomWithAMiner, Body);

            void Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef mining = WorkType("Mining");
                WorkGiverDef mine = Giver("Mine");
                WorkGiverDef repair = Giver("Repair");
                ThingDef granite = DefDatabase<ThingDef>.GetNamedSilentFail("Granite");
                if (mining == null || mine == null || repair == null || granite == null)
                {
                    scenario.Check(false, "the game has mining, repairing and granite");
                    return;
                }
                scene.Comp.ToggleWork(mining);
                MakeCalm(scene.Map);
                scenario.Note("Mining is ticked on the stand, and the map is calm");

                IntVec3 at = scene.OutfitStand.Position;
                Thing rock = GenSpawn.Spawn(ThingMaker.MakeThing(granite), at + new IntVec3(3, 0, 3), scene.Map);
                if (rock == null || !rock.Spawned)
                {
                    scenario.Check(false, "a granite rock could be put in the stand's room");
                    return;
                }
                scenario.Note("a granite rock stands in the room");
                Job mineRock = CatchmentKit.JobOn(JobDefOf.Mine, mine, rock);
                JobPlace place = Patch_JobInterception.SharedTargetPlace(
                    mineRock, Patch_JobInterception.Classify(scene.Colonist, mineRock, null), scene.Map);
                scenario.Check(rock.Position.GetRoom(scene.Map) == null && place.Cell == rock.Position
                            && place.RoomCell != rock.Position && place.RoomCell.GetRoom(scene.Map) != null,
                            "the rock's own cell is in no room, so mining it counts as work in the room "
                            + "by a floor cell beside it");
                scenario.Check(SendsToChange(scene, CatchmentKit.JobOn(JobDefOf.Mine, mine, rock)),
                            "serving its room, the stand sends " + name + " to change to mine it, by that floor cell");

                Area_Allowed area = CatchmentKit.NewArea(scene, "Shift test quarry", at, rock.Position);
                if (area == null)
                {
                    scenario.Check(false, "the map has room for another allowed area");
                    return;
                }
                scene.Comp.SetServeArea(area);
                scenario.Check(scene.Comp.serves == CatchmentKind.Area && !area[place.RoomCell],
                            "the stand serves an allowed area painted on the rock and not on that floor cell");
                scenario.Check(SendsToChange(scene, CatchmentKit.JobOn(JobDefOf.Mine, mine, rock)),
                            "which sends " + name + " to change to mine the rock face");

                area[rock.Position] = false;
                scenario.Check(!SendsToChange(scene, CatchmentKit.JobOn(JobDefOf.Mine, mine, rock)),
                            "with the rock cleared from the area too, it does not");

                List<IntVec3> aroundRock = new List<IntVec3>(GenAdj.CellsAdjacent8Way(rock));
                CatchmentKit.SetCells(area, aroundRock, true);
                scenario.Check(SendsToChange(scene, CatchmentKit.JobOn(JobDefOf.Mine, mine, rock)),
                            "with only the floor around the rock painted, it does");

                CatchmentKit.SetCells(area, aroundRock, false);
                area[rock.Position] = true;
                scenario.Note("the floor around the rock is cleared and the rock painted back in");
                // Asking whether a job sends them to dress records that job as the one just
                // dressed for, and on shift that record keeps the uniform on for the same job
                // whatever the catchment says, so the ask below would pass for the wrong reason.
                // Clear it.
                Patch_JobInterception.DressedFor.Remove(scene.Colonist);
                scenario.Check(SwapNow(scenario, scene), name + " changes into the stand's outfit to mine");
                if (!scenario.Check(scene.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                NotSentStraightBack(scenario, scene);
                scenario.Check(!SendsToChange(scene, CatchmentKit.JobOn(JobDefOf.Mine, mine, rock)),
                            "in uniform, " + name + " keeps it on to mine the rock, painted alone");

                // Clear of the rock's ring, so the two sets of painted cells never meet. One wall
                // inside a room does not split it.
                Thing wall = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel),
                                            at + new IntVec3(0, 0, 3), scene.Map);
                if (wall == null || !wall.Spawned)
                {
                    scenario.Check(false, "a steel wall could be built in the stand's room");
                    return;
                }
                List<IntVec3> aroundWall = new List<IntVec3>(GenAdj.CellsAdjacent8Way(wall));
                CatchmentKit.SetCells(area, aroundWall, true);
                scenario.Note("a steel wall stands in the room, and the floor around it is painted into the area");
                scenario.Check(!area[wall.Position]
                            && !SendsToChange(scene, CatchmentKit.JobOn(JobDefOf.Repair, repair, wall)),
                            "with only the floor around it painted, repairing the wall keeps the uniform on: "
                            + "work on a wall counts by the same rule");
                CatchmentKit.SetCells(area, aroundWall, false);
                scenario.Check(SendsToChange(scene, CatchmentKit.JobOn(JobDefOf.Repair, repair, wall)),
                            "with nothing around it painted, repairing it changes " + name + " back");
            }
        }

        /// <summary>
        /// A colonist whose stand is outside their allowed area keeps the outfit on, and the stand
        /// says why. This is changing back: an area painted after dressing, or switched by Better
        /// Pawn Control's alerts, leaves a colonist in uniform whose stand they may no longer walk
        /// to. The uniform rides along, Shift Change waits before trying again, and the inspect
        /// pane names the cause. The Change back button is unaffected: it is an order, and orders
        /// ignore allowed areas in the game too.
        ///
        /// <para>Beside it, the same out-of-room job with the stand inside their area, asked before
        /// the area is set and again once the stand is back in it, changes them back as usual and
        /// the inspect line is absent.</para>
        /// </summary>
        [Test]
        [TestDescription("a colonist in uniform whose allowed area comes to leave their stand out keeps the "
                         + "outfit on, and the stand's inspect pane tells why")]
        internal IEnumerator OutfitStaysOnWhenTheirAreaLeavesTheStandOut()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true,
                                          capableOf: WorkType("Doctor")),
                          InARoomWithADoctor, Body);

            void Body(Scene scene, Scenario scenario)
            {
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
                if (doctor == null || tend == null || mealDef == null)
                {
                    scenario.Check(false, "the game has the Doctor work type, its tend to patients work "
                                       + "and the simple meal");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                scenario.Note("Doctoring is ticked on the stand, and the map is calm");

                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                Map map = scene.Map;
                IntVec3 outside = OpenGround(scene, scene.OutfitStand.Position + new IntVec3(8, 0, 0));
                IntVec3 mealCell = OpenGround(scene, outside + new IntVec3(0, 0, 1));
                if (!outside.InBounds(map) || outside.GetRoom(map) == scene.OutfitStand.GetRoom())
                {
                    scenario.Check(false, "there is open ground outside the stand's room for doctoring and a meal");
                    return;
                }
                Thing meal = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), mealCell, map);
                string line = "ShiftChange.InspectOutsideArea".Translate(pawn.LabelShort);
                scenario.Note("a simple meal lies outside the room");

                scenario.Check(SwapNow(scenario, scene), name + " changes into the stand's outfit");
                if (!scenario.Check(scene.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                scenario.Check(SendsToChange(scene, GateKit.DoctoringAt(tend, outside)),
                            "with no allowed area set, doctoring outside the room changes " + name + " back");
                scenario.Check(!scene.Comp.CompInspectStringExtra().Contains(line),
                            "and the stand's inspect pane says nothing about areas");

                Area_Allowed area = GateKit.AreaWithoutTheStand(scene);
                if (area == null)
                {
                    scenario.Check(false, "an allowed area could be made");
                    return;
                }
                try
                {
                    area[outside] = true;
                    area[meal.Position] = true;
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;
                    scenario.Note(name + " is restricted to an allowed area holding the whole room but the stand's "
                               + "own cell, and the cells outside where the doctoring and the meal are");
                    scenario.Check(scene.OutfitStand.IsForbidden(pawn),
                                "the area leaves the stand out, so it is forbidden to " + name);

                    int now = Find.TickManager.TicksGame;
                    bool inserted = Patch_JobInterception.TryInsertSwap(GateKit.DoctoringAt(tend, outside), null,
                                                                        pawn, pawn.jobs);
                    int blocked;
                    bool cooled = Patch_JobInterception.LastBlockedTick.TryGetValue(pawn.thingIDNumber, out blocked)
                                  && blocked == now;
                    scenario.Check(!inserted, "with the stand outside their allowed area, " + name
                                           + " keeps the outfit on to doctor outside the room");
                    scenario.Check(cooled, "and Shift Change holds off, so their next job does not try again at once");
                    scenario.Check(CompShiftStand.OnShiftStandFor(pawn) == scene.Comp, name + " is still on shift");
                    if (inserted)
                    {
                        scenario.Note("a change back was started anyway: still under way "
                                   + (pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand ? "yes" : "no"));
                    }
                    string inspect = scene.Comp.CompInspectStringExtra() ?? "";
                    scenario.Check(inspect.Contains(line), "the stand's inspect pane says why: \"" + line + "\"");
                    ClearSwapDecision(pawn);

                    scenario.Check(!SendsToChange(scene, IngestJob(meal)),
                                "a meal break does not walk " + name + " out of their area to change either");

                    area[scene.OutfitStand.Position] = true;
                    scenario.Check(!scene.OutfitStand.IsForbidden(pawn),
                                "with the stand painted into the area, it is allowed again");
                    scenario.Check(SendsToChange(scene, GateKit.DoctoringAt(tend, outside)),
                                "so doctoring outside the room changes " + name + " back as usual");
                    scenario.Check(!scene.Comp.CompInspectStringExtra().Contains(line), "and the inspect line is gone");
                }
                finally
                {
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = null;
                    area.Delete();
                }
            }
        }

        /// <summary>
        /// A gravship landing staged on the scene's own map, in the order the game lands one:
        /// the stand alone is despawned for replacement, set down on the same cell, then
        /// <c>PostSwapMap</c> runs. A real flight also runs <c>PreSwapMap</c> first and copies the
        /// areas between the spawn and <c>PostSwapMap</c>; the caller stages the copied area
        /// beforehand.
        /// </summary>
        internal static void Land(Scene scene)
        {
            IntVec3 cell = scene.OutfitStand.Position;
            Rot4 rot = scene.OutfitStand.Rotation;
            scene.OutfitStand.DeSpawn(DestroyMode.WillReplace);
            GenSpawn.Spawn(scene.OutfitStand, cell, scene.Map, rot);
            scene.OutfitStand.PostSwapMap();
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
        /// it made, or null. Recorded on the scene, whose teardown deletes it.
        /// </summary>
        internal static Area_Allowed MakeFromMenu(Scene scene, CompShiftStand comp)
        {
            List<Area> before = new List<Area>(scene.Map.areaManager.AllAreas);
            NewAreaOption(comp)?.action?.Invoke();
            Area_Allowed made = scene.Map.areaManager.AllAreas.Find(a => !before.Contains(a)) as Area_Allowed;
            if (made != null)
            {
                scene.Areas.Add(made);
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
