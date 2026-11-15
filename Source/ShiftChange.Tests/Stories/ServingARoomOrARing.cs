using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DevTools.Testing;
using RimWorld;
using Verse;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// A stand serving its room or a ring around it: what it covers, whom it dresses, and how it says so.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.ServingARoomOrARing)]
    internal class ServingARoomOrARing : StoryFixture
    {
        /// <summary>What <see cref="SceneKit.Stage"/> sets up with the pad enclosed.</summary>
        internal const string InAWalledRoom =
            "a stand holding a duster in a walled, roofed room, and a colonist beside it "
            + "in a shirt, trousers and a parka";

        /// <summary>The same, with a colonist who can do Doctor work.</summary>
        internal const string DoctorInAWalledRoom =
            "a stand holding a duster in a walled, roofed room, and a colonist able to doctor beside it "
            + "in a shirt, trousers and a parka";

        /// <summary>What <see cref="SceneKit.StageWithDoorway(Map, CellRect)"/> sets up.</summary>
        internal const string BesideADoor =
            "a stand holding a duster in a walled, roofed room, a door beside it to open ground, "
            + "and a colonist beside the stand in a shirt, trousers and a parka";

        /// <summary>The same, with a colonist who can do Doctor work.</summary>
        internal const string DoctorBesideADoor =
            "a stand holding a duster in a walled, roofed room, a door beside it to open ground, "
            + "and a colonist able to doctor beside the stand in a shirt, trousers and a parka";

        /// <summary>
        /// What <see cref="SceneKit.Stage"/> sets up on the open pad, with a colonist who can
        /// do Doctor work.
        /// </summary>
        internal const string DoctorInTheOpen =
            "a stand holding a duster out in the open, and a colonist able to doctor beside it "
            + "in a shirt, trousers and a parka";

        /// <summary>
        /// A radius covers its ring, inclusive: every cell <c>GenRadial.NumCellsInRadius</c>
        /// counts, which is what <c>DrawRadiusRing</c> outlines, and not the next cell of the
        /// pattern. Checked against the ring's own cell list rather than a distance formula,
        /// since the ring is what the player reads. Room mode's answer, the room, is the control.
        /// </summary>
        [Test]
        [TestDescription("a stand serving a radius covers every cell its ring outlines and none past it, "
                         + "where serving its room it stops at the walls")]
        internal IEnumerator TheRingShowsWhatARadiusCovers()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InAWalledRoom, Body);

            void Body(Scene scene, Story story)
            {
                WorkTypeDef doctor = WorkType("Doctor");
                if (doctor == null)
                {
                    story.Check(false, "the game has the Doctor work type");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                story.Note("Doctor is ticked on the stand");
                IntVec3 at = scene.OutfitStand.Position;
                // The stand stands one cell in from the pad's south-west corner, so two cells west of
                // it is the pad's margin, outside the walls.
                IntVec3 inRoom = at + new IntVec3(2, 0, 2);
                IntVec3 pastWall = at + new IntVec3(-2, 0, 2);
                story.Check(scene.Comp.serves == CatchmentKind.Room, "a new stand serves its own room");
                story.Check(scene.Comp.Covers(inRoom, scene.Map), "which covers a cell inside it");
                story.Check(!scene.Comp.Covers(pastWall, scene.Map), "and not one just past the wall");

                const int radius = 5;
                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(radius);
                story.Note("Within a radius is chosen under Serves, and the radius set to " + radius + " cells");
                story.Check(scene.Comp.serves == CatchmentKind.Radius && scene.Comp.serveRadius == radius,
                            "the stand now serves within " + radius + " cells");
                story.Check(scene.Comp.Covers(pastWall, scene.Map), "which reaches past the wall, as a radius does");

                int ringCells = GenRadial.NumCellsInRadius(radius);
                CellRect reach = Pad.ExpandedBy(PadReach);
                bool inReach = true;
                for (int i = 0; i <= ringCells; i++)
                {
                    inReach &= reach.Contains(at + GenRadial.RadialPattern[i]);
                }
                story.Check(inReach, "the ring and the first cell past it lie within the "
                                     + PadReach + " cells past the pad that the map is sure to have");
                int checkedCells = 0;
                bool allIn = true;
                for (int i = 0; i < ringCells; i++)
                {
                    IntVec3 cell = at + GenRadial.RadialPattern[i];
                    if (!cell.InBounds(scene.Map))
                    {
                        continue;
                    }
                    checkedCells++;
                    if (!scene.Comp.Covers(cell, scene.Map))
                    {
                        allIn = false;
                        story.Note("not covered: " + cell);
                    }
                }
                IntVec3 next = at + GenRadial.RadialPattern[ringCells];
                story.Check(checkedCells == ringCells && allIn,
                            "every cell the ring outlines is covered (" + checkedCells + " of " + ringCells
                            + " checked)");
                story.Check(next.InBounds(scene.Map) && !scene.Comp.Covers(next, scene.Map),
                            "and the first cell past the ring is not");
                story.Check(scene.Comp.Covers(at + new IntVec3(radius, 0, 0), scene.Map),
                            "a cell exactly " + radius + " due east is in");
                story.Check(!scene.Comp.Covers(at + new IntVec3(4, 0, 4), scene.Map),
                            "one 4 east and 4 north, past the ring, is out");
                story.Check(!scene.Comp.Covers(at, null), "and asked about no map at all, it covers nothing");

                // NumCellsInRadius refuses the pattern's edge itself, which can be a whole number.
                float edge = GenRadial.MaxRadialPatternRadius;
                int top = CompShiftStand.MaxServeRadius;
                story.Check(top < edge && top + 1 >= edge,
                            "the radius slider stops at the largest whole radius below the edge of the "
                            + "game's radial pattern at " + edge + ": " + top + " cells");
                story.Check(GenRadial.RadialPattern.Length != CatchmentKit.GameRadialCells || top == 79,
                            "which on the game's own pattern is 79, the number the README gives");
            }
        }

        /// <summary>
        /// A stand leaves room mode only with something ticked, and on leaving shift use returns to
        /// its room with its radius kept. Automatic is a room's own reading, so an automatic
        /// stand's set becomes ticks on the way out, and a stand with nothing to tick stays in its
        /// room. The window greys its Serves row on <see cref="CompShiftStand.ServesSomething"/>, a
        /// test separate from the setter's refusal, so the two are compared in every state that
        /// draws the row; a stand out of shift use draws none, so there only the setter is asked.
        /// </summary>
        [Test]
        [TestDescription("a stand serves a radius only once something is ticked on it, "
                         + "and goes back to its room when nothing is")]
        internal IEnumerator ARadiusNeedsSomethingTicked()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InAWalledRoom, Body);

            void Body(Scene scene, Story story)
            {
                WorkTypeDef doctor = WorkType("Doctor");
                ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                if (doctor == null || bedDef == null)
                {
                    story.Check(false, "the game has the Doctor work type and the bed");
                    return;
                }

                story.Check(scene.Comp.IsAutomatic && scene.Comp.WorkTypes.Count == 0
                            && !scene.Comp.HandlesRecreation() && !scene.Comp.HandlesRest(),
                            "an automatic stand in a room with no role serves nothing");
                scene.Comp.SetServes(CatchmentKind.Radius);
                story.Note("Within a radius is chosen under Serves");
                story.Check(scene.Comp.serves == CatchmentKind.Room,
                            "so it stays on its room: there is nothing to tick");
                story.Check(!CompShiftStand.CatchmentStands.Contains(scene.Comp),
                            "and it is not on the list of stands serving a radius or an area");
                story.Check(!scene.Comp.ServesSomething,
                            "and its settings window greys its Serves row to match");

                // A medical bed makes the pad a hospital, so automatic reads Doctor.
                Building_Bed bed = DebugTools_Fixtures.Spawn(
                    scene.Map, bedDef, ThingDefOf.WoodLog,
                    new IntVec3(scene.OutfitStand.Position.x + 2, 0, scene.OutfitStand.Position.z + 2),
                    Rot4.North) as Building_Bed;
                if (bed == null)
                {
                    story.Check(false, "a bed spawns in the stand's room");
                    return;
                }
                bed.Medical = true;
                story.Note("a bed set for medical use goes in the stand's room");
                // Doctor alone in vanilla; the Complex Jobs compatibility rows add Nurse and Surgeon.
                List<WorkTypeDef> hospital = new List<WorkTypeDef>(scene.Comp.WorkTypes);
                story.Check(scene.Comp.IsAutomatic && scene.Comp.HandlesWork(doctor)
                            && hospital.All(w => w == doctor || GiverTables.IsModWork(w)),
                            "the medical bed makes it an automatic Doctor stand, plus any hospital work "
                            + "mods add");
                story.Check(scene.Comp.ServesSomething, "so its Serves row is live");

                scene.Comp.SetServes(CatchmentKind.Radius);
                story.Note("Within a radius is chosen under Serves");
                story.Check(scene.Comp.serves == CatchmentKind.Radius,
                            "with something to tick, it serves a radius");
                story.Check(!scene.Comp.IsAutomatic && scene.Comp.HasCustomSet
                            && hospital.All(scene.Comp.HandlesWork)
                            && scene.Comp.WorkTypes.Count == hospital.Count,
                            "its automatic work became ticks, so nothing visibly changed");
                story.Check(CompShiftStand.CatchmentStands.Contains(scene.Comp),
                            "and it is on the list of stands serving a radius or an area");
                story.Check(scene.Comp.ServesSomething,
                            "and its Serves row stays live, so it can switch back");

                scene.Comp.SetServes(CatchmentKind.Room);
                story.Note("This room is chosen under Serves");
                story.Check(scene.Comp.serves == CatchmentKind.Room && scene.Comp.IsAutomatic
                            && scene.Comp.HandlesWork(doctor),
                            "back on This room with its ticks untouched, it is automatic again, "
                            + "reading the hospital");
                WorkTypeDef research = WorkType("Research");
                if (research != null)
                {
                    scene.Comp.SetServes(CatchmentKind.Radius);
                    scene.Comp.ToggleWork(research);
                    scene.Comp.SetServes(CatchmentKind.Room);
                    story.Note("Research is ticked while it serves a radius, then This room is chosen again");
                    story.Check(scene.Comp.serves == CatchmentKind.Room && !scene.Comp.IsAutomatic
                                && scene.Comp.HandlesWork(doctor) && scene.Comp.HandlesWork(research),
                                "but ticks changed while it served a radius stay ticks when it comes back");
                    scene.Comp.ToggleWork(research);
                    story.Note("Research is unticked again");
                }
                scene.Comp.SetServes(CatchmentKind.Radius);
                story.Note("Within a radius is chosen under Serves");
                story.Check(scene.Comp.serves == CatchmentKind.Radius && !scene.Comp.IsAutomatic
                            && scene.Comp.WorkTypes.Count == hospital.Count,
                            "and it serves a radius with the hospital's work ticked again, for what follows");

                scene.Comp.SetServeRadius(7);
                ToggleEach(scene, hospital);
                story.Note("its radius is set to 7 cells, and each of its work types unticked");
                story.Check(scene.Comp.IsExcluded,
                            "unticking the last work type makes it Not used for shift changes");
                story.Check(scene.Comp.serves == CatchmentKind.Room, "and puts it back on its room");
                story.Check(!CompShiftStand.CatchmentStands.Contains(scene.Comp),
                            "off the list of stands serving a radius or an area");
                story.Check(scene.Comp.serveRadius == 7, "with its radius of 7 kept for when it comes back");

                scene.Comp.SetServes(CatchmentKind.Radius);
                story.Note("Within a radius is chosen under Serves");
                story.Check(scene.Comp.serves == CatchmentKind.Room,
                            "a stand not used for shift changes cannot serve a radius");

                scene.Comp.SetAutomatic();
                story.Note("Automatic is chosen on its switch");
                story.Check(scene.Comp.IsAutomatic && scene.Comp.serves == CatchmentKind.Room,
                            "Automatic puts it back on its room, reading the hospital again");
                story.Check(scene.Comp.ServesSomething, "with its Serves row live again");
                scene.Comp.SetServes(CatchmentKind.Radius);
                story.Note("Within a radius is chosen under Serves");
                story.Check(scene.Comp.serves == CatchmentKind.Radius, "and now it serves a radius again");
            }
        }

        /// <summary>
        /// An automatic stand reads its room's new role on the tick the role changes, with the
        /// contents scan on, which is what makes the stand cache its reading. A bed set to medical
        /// keeps the room's ID, so only the role tells the old reading from the new. A stand-in
        /// marker that matches nothing turns the scan on without the mod it is for.
        /// </summary>
        [Test]
        [TestDescription("an automatic stand reading its room's contents sees the room become a hospital "
                         + "on the tick a medical bed goes in")]
        internal IEnumerator AnAutomaticStandSeesItsRoomBecomeAHospital()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InAWalledRoom, Body);

            void Body(Scene scene, Story story)
            {
                WorkTypeDef doctor = WorkType("Doctor");
                ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                if (doctor == null || bedDef == null)
                {
                    story.Check(false, "the game has the Doctor work type and the bed");
                    return;
                }
                List<RoomContentsWork.Marker> saved = RoomContentsWork.active;
                RoomContentsWork.active = new List<RoomContentsWork.Marker>
                {
                    new RoomContentsWork.Marker("RimWorld.Building_CommsConsole", false, new string[0])
                    {
                        type = typeof(Building_CommsConsole),
                        works = new List<WorkTypeDef>(),
                    },
                };
                story.Note("Shift Change reads work from what stands in a room, through one row, "
                           + "for a comms console, that earns no work");
                try
                {
                    Room room = scene.OutfitStand.GetRoom();
                    story.Check(room != null && RoomContentsWork.Any && scene.Comp.IsAutomatic
                                && !scene.Comp.HandlesWork(doctor),
                                "with the contents read, the automatic stand reads its room, which has no role, "
                                + "as no Doctor work");
                    story.Check(room != null && scene.Comp.automaticCache != null
                                && scene.Comp.automaticCacheRoomId == room.ID
                                && scene.Comp.automaticCacheRole == room.Role
                                && scene.Comp.automaticCacheTick == Find.TickManager.TicksGame,
                                "and keeps that reading for this room, its role and this tick");
                    Building_Bed bed = DebugTools_Fixtures.Spawn(
                        scene.Map, bedDef, ThingDefOf.WoodLog,
                        new IntVec3(scene.OutfitStand.Position.x + 2, 0, scene.OutfitStand.Position.z + 2),
                        Rot4.North) as Building_Bed;
                    if (bed == null)
                    {
                        story.Check(false, "a bed spawns in the stand's room");
                        return;
                    }
                    bed.Medical = true;
                    story.Note("a bed set for medical use goes in the room, on the same tick");
                    story.Check(scene.OutfitStand.GetRoom() == room,
                                "the medical bed leaves the room, and its ID, as they were");
                    story.Check(scene.Comp.HandlesWork(doctor),
                                "and on the same tick the stand reads it as a hospital, for Doctor work");
                }
                finally
                {
                    RoomContentsWork.active = saved;
                }
            }
        }

        /// <summary>
        /// A radius replaces the room. Work outside the stand's room but inside its ring dresses;
        /// work past the ring does not, even in the stand's own room. Room mode's answers on the
        /// same cells are the control.
        /// </summary>
        [Test]
        [TestDescription("a stand serving within 5 cells dresses a doctor out past its door, "
                         + "and not past its ring, even in its own room")]
        internal IEnumerator TheRingReplacesTheRoom()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, WorkType("Doctor")),
                          DoctorBesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    story.Check(false, "the game has Doctor work and its tending of colonists");
                    return;
                }
                if (scene.Outside.Area == 0)
                {
                    story.Check(false, "the stand's room has a door with open ground outside it");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                story.Note("Doctor is ticked on the stand, and the map holds no enemies");

                IntVec3 at = scene.OutfitStand.Position;
                IntVec3 nearOutside = new IntVec3(scene.Outside.minX, 0, at.z);
                IntVec3 farOutside = new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ);
                IntVec3 farCorner = at + new IntVec3(4, 0, 4);
                story.Check(nearOutside.GetRoom(scene.Map) != scene.OutfitStand.GetRoom(),
                            "the ground outside the door is another room");
                story.Check(!SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, nearOutside)),
                            "serving its room, the stand does not dress " + name
                            + " for doctoring just outside the door");
                story.Check(SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, farCorner)),
                            "but does for doctoring in the room's far corner");

                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(5);
                story.Note("Within a radius is chosen under Serves, and the radius set to 5 cells");
                story.Check(SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, nearOutside)),
                            "within 5 cells, doctoring outside the room but inside the ring dresses " + name);
                story.Check(!SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, farOutside)),
                            "doctoring past the ring does not");
                story.Check(!SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, farCorner)),
                            "and nor does the room's far corner, past the ring: the radius replaces the room");

                scene.Comp.SetServeRadius(6);
                story.Note("the radius is widened to 6 cells");
                story.Check(SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, farCorner)),
                            "with the ring widened to reach it, the far corner dresses again");
            }
        }

        /// <summary>
        /// The outdoor guard is for room stands. A room stand standing in the open serves no
        /// outdoor recreation, because the outdoor room spans the whole map; the same stand given
        /// a radius does, because a radius is bounded. The radius half is the control.
        /// </summary>
        [Test]
        [TestDescription("a stand out in the open dresses nobody for chess while it serves its room, "
                         + "and does once it serves a radius")]
        internal IEnumerator ARadiusServesRecreationInTheOpen()
        {
            return Staged(StageWithDoorway, BesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
                if (chess == null)
                {
                    story.Check(false, "the game has chess, a joy activity");
                    return;
                }
                if (scene.Outside.Area == 0)
                {
                    story.Check(false, "the stand's room has a door with open ground outside it");
                    return;
                }
                // The pad's own stand stays out of it: not used for shift changes.
                scene.Comp.SetExcluded();
                story.Note("the stand in the room is set to Not used for shift changes");

                IntVec3 standCell = new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ - 1);
                IntVec3 joyCell = new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ - 2);
                Building_OutfitStand outdoor = DebugTools_Fixtures.Spawn(
                    scene.Map, scene.OutfitStand.def, ThingDefOf.WoodLog, standCell, Rot4.North) as Building_OutfitStand;
                CompShiftStand comp = outdoor?.TryGetComp<CompShiftStand>();
                if (comp == null || !StockOne(outdoor, "Apparel_Duster"))
                {
                    story.Check(false, "a second stand holding a duster can be put outside the door");
                    return;
                }
                comp.ToggleRecreation();
                story.Note("a second stand holding a duster stands outside the door, Recreation ticked");

                Room open = outdoor.GetRoom();
                story.Check(open != null && open.TouchesMapEdge,
                            "the stand outside stands in the open, in the room that touches the map edge");
                story.Check(comp.HandlesRecreation() && comp.serves == CatchmentKind.Room,
                            "and serves recreation from its room");
                story.Check(!SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(chess, joyCell, joyCell)),
                            "so chess beside it does not dress " + name
                            + ": serving its room, a stand in the open serves nothing");

                comp.SetServes(CatchmentKind.Radius);
                comp.SetServeRadius(3);
                story.Note("the stand outside is set to serve within 3 cells");
                story.Check(SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(chess, joyCell, joyCell)),
                            "the same stand, serving within 3 cells, dresses " + name + " for it");
            }
        }

        /// <summary>
        /// Precedence is unchanged: the colonist's own stand first, then the nearest. An owned
        /// radius stand outside the room beats a nearer shared room stand inside it, and without
        /// the owner the nearer one wins, which is the control.
        /// </summary>
        [Test]
        [TestDescription("a colonist's own stand, serving a radius from outside the room, "
                         + "wins over a nearer shared stand inside it")]
        internal IEnumerator TheirOwnRadiusStandBeatsANearerSharedOne()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, WorkType("Doctor")),
                          DoctorBesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    story.Check(false, "the game has Doctor work and its tending of colonists");
                    return;
                }
                if (scene.Outside.Area == 0)
                {
                    story.Check(false, "the stand's room has a door with open ground outside it");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                story.Note("Doctor is ticked on the stand, and the map holds no enemies");

                IntVec3 standCell = new IntVec3(scene.Outside.minX, 0, scene.OutfitStand.Position.z + 2);
                Building_OutfitStand mine = DebugTools_Fixtures.Spawn(
                    scene.Map, scene.OutfitStand.def, ThingDefOf.WoodLog, standCell, Rot4.North) as Building_OutfitStand;
                CompShiftStand mineComp = mine?.TryGetComp<CompShiftStand>();
                CompAssignableToPawn_ShiftStand owner = mine?.TryGetComp<CompAssignableToPawn_ShiftStand>();
                if (mineComp == null || owner == null || !StockOne(mine, "Apparel_Duster"))
                {
                    story.Check(false, "a second stand holding a duster can be put outside the door");
                    return;
                }
                mineComp.ToggleWork(doctor);
                mineComp.SetServes(CatchmentKind.Radius);
                mineComp.SetServeRadius(6);
                story.Note("a second stand holding a duster stands outside the door, "
                           + "Doctor ticked, serving within 6 cells");

                IntVec3 workAt = scene.OutfitStand.Position + new IntVec3(1, 0, 0);
                story.Check(mineComp.Covers(workAt, scene.Map),
                            "the outside stand's ring takes in doctoring in the room");
                story.Check(Patch_JobInterception.FindAvailableStand(workAt, scene.Map, scene.Colonist, doctor)
                            == scene.Comp,
                            "with neither stand owned, the nearer shared stand in the room is the one chosen");

                owner.TryAssignPawn(scene.Colonist);
                story.Note(name + " is made the owner of the stand outside");
                story.Check(Patch_JobInterception.FindAvailableStand(workAt, scene.Map, scene.Colonist, doctor)
                            == mineComp,
                            "with the radius stand " + name + "'s own, it is chosen over the nearer shared one");
            }
        }

        /// <summary>
        /// An area stand's latch, Change back's hold on dressing the colonist again, holds beside
        /// the stand. Changing back leaves the colonist on a cell next to the stand, which the area
        /// need not hold, and a painted field or rock face is worked from the unpainted cell beside
        /// it. Neither is leaving the catchment: the pause after the swap, work in the field and the
        /// pause after that work all keep the latch, and the latched stand still refuses the work.
        /// Lifting the latch by hand is the control for the refusal, and a job on unpainted ground
        /// away from the stand releases it.
        /// </summary>
        [Test]
        [TestDescription("after Change back, a stand serving an allowed area does not dress them again "
                         + "beside it or in its field, until a job takes them away")]
        internal IEnumerator ChangeBackHoldsBesideAnAreaStand()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true,
                                          capableOf: WorkType("Doctor")),
                          DoctorInAWalledRoom, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    story.Check(false, "the game has Doctor work and its tending of colonists");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                story.Note("Doctor is ticked on the stand, and the map holds no enemies");

                Map map = scene.Map;
                IntVec3 at = scene.OutfitStand.Position;
                IntVec3 beside = at + new IntVec3(0, 0, 1);
                IntVec3 field = at + new IntVec3(2, 0, 3);
                IntVec3 edge = at + new IntVec3(2, 0, 2);
                IntVec3 away = at + new IntVec3(4, 0, 1);
                Area_Allowed area = CatchmentKit.NewArea(scene, "Shift test field", at, field,
                                                              at + new IntVec3(3, 0, 3));
                if (area == null)
                {
                    story.Check(false, "the map has room for another allowed area");
                    return;
                }
                scene.Comp.SetServeArea(area);
                story.Note("the stand serves a new allowed area, painted on the stand and a small field");
                story.Check(scene.Comp.serves == CatchmentKind.Area && scene.Comp.Covers(field, map),
                            "the stand serves an allowed area holding it and a field");
                story.Check(!scene.Comp.Covers(beside, map) && !scene.Comp.Covers(edge, map)
                            && !scene.Comp.Covers(away, map),
                            "and not the cell beside the stand, the field's edge or the ground away from both");

                IntVec3 wasAt = scene.Colonist.Position;
                Patch_JobInterception.ChangedBackAt[scene.Colonist] =
                    new Patch_JobInterception.ChangeBackLatch(scene.OutfitStand);
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = beside;
                story.Note(name + " has just changed back at the stand, which now holds off dressing them, "
                           + "and stands on the unpainted cell beside it");
                SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1));
                story.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "the pause after changing back, on the unpainted cell beside the stand, "
                            + "keeps the stand holding off");

                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = edge;
                story.Note(name + " moves to the field's unpainted edge");
                story.Check(!SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, field)),
                            "held off, doctoring in the field, from the unpainted edge beside it, "
                            + "does not dress " + name);
                story.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "and the stand keeps holding off");
                SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1));
                story.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "and so it does through the pause after that work, which names no place, "
                            + "on the same edge");
                Patch_JobInterception.ChangedBackAt.Remove(scene.Colonist);
                story.Note("the hold is lifted by hand");
                story.Check(SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, field)),
                            "with the hold lifted, the same doctoring dresses " + name);
                ClearSwapDecision(scene.Colonist);

                Patch_JobInterception.ChangedBackAt[scene.Colonist] =
                    new Patch_JobInterception.ChangeBackLatch(scene.OutfitStand);
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = away;
                story.Note("held off again, " + name + " waits on unpainted ground away from the stand");
                SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(JobDefOf.Wait, away));
                story.Check(!Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "a job on unpainted ground away from the stand ends the hold");
                Patch_JobInterception.ChangedBackAt.Remove(scene.Colonist);
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = wasAt;
            }
        }

        /// <summary>
        /// A room stand beside a door serves its own room only. The region lister files a thing
        /// under every region it can be touched from, so the doorway's room lists the stand just
        /// inside the door and a room stand just outside it (control). Neither covers work in the
        /// doorway, and the dress search agrees: that work dresses nobody, where the same work
        /// inside the room does (control). The stand outside stands in the open and serves nothing.
        /// </summary>
        [Test]
        [TestDescription("a stand just inside a door serves only its own room: "
                         + "doctoring in the doorway dresses nobody")]
        internal IEnumerator AStandBesideADoorServesItsOwnRoom()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, WorkType("Doctor")),
                          DoctorBesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    story.Check(false, "the game has Doctor work and its tending of colonists");
                    return;
                }
                if (scene.Outside.Area == 0)
                {
                    story.Check(false, "the stand's room has a door with open ground outside it");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                story.Note("Doctor is ticked on the stand, and the map holds no enemies");

                Map map = scene.Map;
                IntVec3 at = scene.OutfitStand.Position;
                IntVec3 door = at + new IntVec3(-1, 0, 0);
                CompShiftStand outdoor = CatchmentKit.StageStand(scene, at + new IntVec3(-2, 0, 0));
                if (outdoor == null)
                {
                    story.Check(false, "a second stand holding a duster can be put outside the door");
                    return;
                }
                outdoor.ToggleWork(doctor);
                story.Note("a second stand holding a duster stands just outside the door, Doctor ticked");

                Room doorway = door.GetRoom(map);
                List<Thing> listed = doorway != null
                    ? doorway.ContainedThings(scene.OutfitStand.def).ToList()
                    : new List<Thing>();
                story.Check(door.GetDoor(map) != null && doorway != null && doorway.IsDoorway,
                            "the cell beside the stand is a door, a room of its own");
                story.Check(listed.Contains(scene.OutfitStand) && listed.Contains(outdoor.parent),
                            "and the game counts both stands, the one inside and the one outside, "
                            + "as in the doorway's room");
                story.Check(outdoor.ServeFault == CatchmentFault.InTheOpen,
                            "the stand outside stands in the open and serves nothing");
                story.Check(!scene.Comp.Covers(door, map) && !outdoor.Covers(door, map),
                            "neither stand covers the doorway");
                story.Check(!SendsToChange(scene, GateKit.DoctoringAt(tend, door)),
                            "so doctoring in the doorway dresses nobody");
                story.Check(SendsToChange(scene, GateKit.DoctoringAt(tend, at + new IntVec3(2, 0, 2))),
                            "where the same doctoring inside the room does");
            }
        }

        /// <summary>
        /// The registry forgets a stand that is no longer spawned. Removing a map despawns none of
        /// its buildings, so a catchment stand left on an abandoned map would stay listed. An
        /// unspawned stand stands in for one here: a stand search drops it, and keeps the spawned
        /// stand on the pad (control).
        /// </summary>
        [Test]
        [TestDescription("a radius stand that is no longer on its map drops out of the stand search")]
        internal IEnumerator AStandGoneFromItsMapIsForgotten()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InAWalledRoom, Body);

            void Body(Scene scene, Story story)
            {
                WorkTypeDef doctor = WorkType("Doctor");
                if (doctor == null)
                {
                    story.Check(false, "the game has the Doctor work type");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                scene.Comp.SetServes(CatchmentKind.Radius);
                story.Note("Doctor is ticked on the stand, and it serves a radius");
                ThingWithComps loose =
                    ThingMaker.MakeThing(scene.OutfitStand.def, ThingDefOf.WoodLog) as ThingWithComps;
                CompShiftStand stale = loose?.TryGetComp<CompShiftStand>();
                if (stale == null)
                {
                    story.Check(false, "a stand can be made that is on no map");
                    return;
                }
                CompShiftStand.CatchmentStands.Add(stale);
                story.Note("a stand on no map is put on the list, as one left on an abandoned map stays on it");
                story.Check(CompShiftStand.CatchmentStands.Contains(stale)
                            && CompShiftStand.CatchmentStands.Contains(scene.Comp),
                            "the list of stands serving a radius or an area holds the stand on the map "
                            + "and the one on no map");
                Patch_JobInterception.FindAvailableStand(scene.OutfitStand.Position, scene.Map, scene.Colonist, doctor);
                story.Note("a search for a stand to dress for doctoring runs");
                story.Check(!CompShiftStand.CatchmentStands.Contains(stale), "the search drops the one on no map");
                story.Check(CompShiftStand.CatchmentStands.Contains(scene.Comp), "and keeps the stand on the map");
                CompShiftStand.CatchmentStands.Remove(stale);
            }
        }

        /// <summary>
        /// The Serves row, the inspect pane and the switch's tooltip all read the stand's mode: its
        /// room, the open ground (nothing, whatever the stand is for), or its radius. Checked on
        /// the strings those three build, since no test draws a window or a gizmo; the room's
        /// wording is the control for each radius one.
        /// </summary>
        [Test]
        [TestDescription("a stand's Serves row, inspect pane and switch tooltip say whether it serves "
                         + "its room, a radius or nothing in the open")]
        internal IEnumerator ItsWordsFollowWhatItServes()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, WorkType("Doctor")),
                          DoctorBesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                WorkTypeDef doctor = WorkType("Doctor");
                if (doctor == null)
                {
                    story.Check(false, "the game has the Doctor work type");
                    return;
                }
                if (scene.Outside.Area == 0)
                {
                    story.Check(false, "the stand's room has a door with open ground outside it");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                story.Note("Doctor is ticked on the stand");
                string manual = "ShiftChange.Manual".Translate();
                string radiusLine = "ShiftChange.InspectServesRadius".Translate(7);

                story.Check(scene.Comp.ServesLabel() == (string)"ShiftChange.ServesRoom".Translate(),
                            "serving its room, its Serves row reads this room");
                story.Check(scene.Comp.ByCatchment("ShiftChange.RestRow") == "ShiftChange.RestRow",
                            "its Sleeping row speaks of this room");
                story.Check(GizmoKit.Switch(scene.Comp)?.defaultDesc
                            == (string)"ShiftChange.SetWorkTypeDesc".Translate(scene.Comp.WorkTypesLabel(), manual),
                            "so does its switch's tooltip");
                story.Check(!scene.Comp.CompInspectStringExtra().Contains(radiusLine),
                            "and the inspect pane has no radius line");

                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(7);
                story.Note("Within a radius is chosen under Serves, and the radius set to 7 cells");
                story.Check(scene.Comp.ServesLabel() == (string)"ShiftChange.ServesRadius".Translate(7),
                            "serving a radius, its Serves row reads within 7 cells");
                story.Check(scene.Comp.ByCatchment("ShiftChange.RestRow") == "ShiftChange.RestRowRadius",
                            "its Sleeping row speaks of its radius");
                story.Check(GizmoKit.Switch(scene.Comp)?.defaultDesc
                            == (string)"ShiftChange.SetWorkTypeDescRadius".Translate(scene.Comp.WorkTypesLabel(),
                                                                                       manual, 7),
                            "so does its switch's tooltip, naming 7 cells");
                story.Check(scene.Comp.CompInspectStringExtra().Contains(radiusLine),
                            "and the inspect pane says it serves a radius of 7 cells");

                CompShiftStand open = CatchmentKit.StageStand(
                    scene, new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ));
                if (open == null)
                {
                    story.Check(false, "a second stand holding a duster can be put in the open");
                    return;
                }
                string nothingLine = "ShiftChange.InspectServesNothingOutdoors".Translate();
                open.ToggleWork(doctor);
                story.Note("a second stand holding a duster stands out in the open, Doctor ticked");
                story.Check(open.StandsInTheOpen
                            && open.ServesLabel() == (string)"ShiftChange.ServesNothingOutdoors".Translate(),
                            "a work stand in the open, serving its room, reads nothing out in the open");
                story.Check(open.CompInspectStringExtra().Contains(nothingLine),
                            "and its inspect pane says so");
                open.ToggleRecreation();
                story.Note("Recreation is ticked on the stand in the open");
                story.Check(open.ServesLabel() == (string)"ShiftChange.ServesNothingOutdoors".Translate(),
                            "a recreation stand there reads the same");
                open.SetServes(CatchmentKind.Radius);
                story.Note("Within a radius is chosen under Serves on the stand in the open");
                story.Check(open.ServesLabel() == (string)"ShiftChange.ServesRadius".Translate(open.serveRadius)
                            && !open.CompInspectStringExtra().Contains(nothingLine),
                            "until it serves a radius, which its row and inspect pane then say");
            }
        }

        /// <summary>
        /// A stand serving its room serves nothing in the open. On open ground its room is all the
        /// open ground on the map, so serving it takes in no cell, not even the stand's own: Doctor
        /// work beside it dresses nobody, and the Shift stand serves nothing alert lists the stand.
        /// Given a radius, the same work dresses and the alert drops it. A colonist already in its
        /// uniform, as an older save can hold, changes back at their next job once the stand
        /// serves its room again.
        /// </summary>
        [Test]
        [TestDescription("a stand out in the open, serving its room, dresses nobody for doctoring beside it "
                         + "and shows in the alert, until it serves a radius")]
        internal IEnumerator AStandInTheOpenServesNoWork()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing,
                                          capableOf: WorkType("Doctor")),
                          DoctorInTheOpen, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    story.Check(false, "the game has Doctor work and its tending of colonists");
                    return;
                }
                Alert alert = CatchmentKit.VanillaAlert(typeof(Alert_ShiftStandServesNothing));
                if (alert == null)
                {
                    story.Check(false, "the game's alerts include Shift stand serves nothing");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                story.Note("Doctor is ticked on the stand, and the map holds no enemies");

                IntVec3 near = scene.OutfitStand.Position + new IntVec3(2, 0, 2);
                story.Check(scene.Comp.StandsInTheOpen && scene.Comp.serves == CatchmentKind.Room,
                            "the stand stands in the open, serving its room");
                story.Check(!scene.Comp.Covers(near, scene.Map)
                            && !scene.Comp.Covers(scene.OutfitStand.Position, scene.Map),
                            "which takes in no cell, not even its own");
                story.Check(!SendsToChange(scene, CatchmentKit.WorkAt(tend, near)),
                            "so doctoring right beside it dresses nobody");
                story.Check(CatchmentKit.Lists(alert, scene.OutfitStand),
                            "and the Shift stand serves nothing alert lists it");

                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(5);
                story.Note("Within a radius is chosen under Serves, and the radius set to 5 cells");
                story.Check(SendsToChange(scene, CatchmentKit.WorkAt(tend, near)),
                            "within 5 cells, the same doctoring dresses " + name);
                story.Check(!CatchmentKit.Lists(alert, scene.OutfitStand), "and the alert drops the stand");

                // A colonist in the uniform of a stand in the open serving its room, as an older
                // save can hold: dressed while the stand serves a radius, then the stand put back
                // on its room. The doctoring asked about above was recorded as the job just dressed
                // for, which on shift would keep the uniform on whatever the stand takes in, so
                // that record goes first.
                Patch_JobInterception.DressedFor.Remove(scene.Colonist);
                story.Check(SwapNow(story, scene), name + " changes into the duster while the stand serves a radius");
                if (!story.Check(scene.Comp.OnShift, "and is on shift at it"))
                {
                    return;
                }
                NotSentStraightBack(story, scene);
                story.Check(!SendsToChange(scene, CatchmentKit.WorkAt(tend, near)),
                            "in uniform, doctoring inside the ring keeps it on");
                scene.Comp.SetServes(CatchmentKind.Room);
                story.Note("the stand is set back to serve its room");
                story.Check(SendsToChange(scene, CatchmentKit.WorkAt(tend, near)),
                            "serving its room again, the same doctoring sends " + name
                            + " to change back: the stand takes in nothing");
            }
        }
    }
}
