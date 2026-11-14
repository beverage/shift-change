using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DevTools.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// A stand serving its room or a ring around it: what it covers, whom it dresses, and how it says so.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.ServingARoomOrARing)]
    internal class ServingARoomOrARing : StoryFixture
    {
        /// <summary>What <see cref="HarnessFixtures.Stage"/> sets up with the pad enclosed.</summary>
        internal const string InAWalledRoom =
            "a stand holding a duster in a walled, roofed room, and a colonist beside it "
            + "in a shirt, trousers and a parka";

        /// <summary>The same, with a colonist who can do Doctor work.</summary>
        internal const string DoctorInAWalledRoom =
            "a stand holding a duster in a walled, roofed room, and a colonist able to doctor beside it "
            + "in a shirt, trousers and a parka";

        /// <summary>What <see cref="HarnessFixtures.StageWithDoorway(Map, CellRect)"/> sets up.</summary>
        internal const string BesideADoor =
            "a stand holding a duster in a walled, roofed room, a door beside it to open ground, "
            + "and a colonist beside the stand in a shirt, trousers and a parka";

        /// <summary>The same, with a colonist who can do Doctor work.</summary>
        internal const string DoctorBesideADoor =
            "a stand holding a duster in a walled, roofed room, a door beside it to open ground, "
            + "and a colonist able to doctor beside the stand in a shirt, trousers and a parka";

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
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), InAWalledRoom, Body);

            void Body(Fixture fix, Story story)
            {
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                if (doctor == null)
                {
                    story.Check(false, "the game has the Doctor work type");
                    return;
                }
                fix.Comp.ToggleWork(doctor);
                story.Note("Doctor is ticked on the stand");
                IntVec3 at = fix.Stand.Position;
                // The stand stands one cell in from the pad's south-west corner, so two cells west of
                // it is the pad's margin, outside the walls.
                IntVec3 inRoom = at + new IntVec3(2, 0, 2);
                IntVec3 pastWall = at + new IntVec3(-2, 0, 2);
                story.Check(fix.Comp.serves == CatchmentKind.Room, "a new stand serves its own room");
                story.Check(fix.Comp.Covers(inRoom, fix.Map), "which covers a cell inside it");
                story.Check(!fix.Comp.Covers(pastWall, fix.Map), "and not one just past the wall");

                const int radius = 5;
                fix.Comp.SetServes(CatchmentKind.Radius);
                fix.Comp.SetServeRadius(radius);
                story.Note("Within a radius is chosen under Serves, and the radius set to " + radius + " cells");
                story.Check(fix.Comp.serves == CatchmentKind.Radius && fix.Comp.serveRadius == radius,
                            "the stand now serves within " + radius + " cells");
                story.Check(fix.Comp.Covers(pastWall, fix.Map), "which reaches past the wall, as a radius does");

                int ringCells = GenRadial.NumCellsInRadius(radius);
                int checkedCells = 0;
                bool allIn = true;
                for (int i = 0; i < ringCells; i++)
                {
                    IntVec3 cell = at + GenRadial.RadialPattern[i];
                    if (!cell.InBounds(fix.Map))
                    {
                        continue;
                    }
                    checkedCells++;
                    if (!fix.Comp.Covers(cell, fix.Map))
                    {
                        allIn = false;
                        story.Note("not covered: " + cell);
                    }
                }
                IntVec3 next = at + GenRadial.RadialPattern[ringCells];
                story.Check(checkedCells > 0 && allIn,
                            "every cell the ring outlines is covered (" + checkedCells + " checked)");
                story.Check(next.InBounds(fix.Map) && !fix.Comp.Covers(next, fix.Map),
                            "and the first cell past the ring is not");
                story.Check(fix.Comp.Covers(at + new IntVec3(radius, 0, 0), fix.Map),
                            "a cell exactly " + radius + " due east is in");
                story.Check(!fix.Comp.Covers(at + new IntVec3(4, 0, 4), fix.Map),
                            "one 4 east and 4 north, past the ring, is out");
                story.Check(!fix.Comp.Covers(at, null), "and asked about no map at all, it covers nothing");

                // NumCellsInRadius refuses the pattern's edge itself, which can be a whole number.
                float edge = GenRadial.MaxRadialPatternRadius;
                int top = CompShiftStand.MaxServeRadius;
                story.Check(top < edge && top + 1 >= edge,
                            "the radius slider stops at the largest whole radius below the edge of the "
                            + "game's radial pattern at " + edge + ": " + top + " cells");
                story.Check(GenRadial.RadialPattern.Length != HarnessCatchments.GameRadialCells || top == 79,
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
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), InAWalledRoom, Body);

            void Body(Fixture fix, Story story)
            {
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                if (doctor == null || bedDef == null)
                {
                    story.Check(false, "the game has the Doctor work type and the bed");
                    return;
                }

                story.Check(fix.Comp.IsAutomatic && fix.Comp.WorkTypes.Count == 0
                            && !fix.Comp.HandlesRecreation() && !fix.Comp.HandlesRest(),
                            "an automatic stand in a room with no role serves nothing");
                fix.Comp.SetServes(CatchmentKind.Radius);
                story.Note("Within a radius is chosen under Serves");
                story.Check(fix.Comp.serves == CatchmentKind.Room,
                            "so it stays on its room: there is nothing to tick");
                story.Check(!CompShiftStand.CatchmentStands.Contains(fix.Comp),
                            "and it is not on the list of stands serving a radius or an area");
                story.Check(!fix.Comp.ServesSomething,
                            "and its settings window greys its Serves row to match");

                // A medical bed makes the pad a hospital, so automatic reads Doctor.
                Building_Bed bed = DebugTools_Fixtures.Spawn(
                    fix.Map, bedDef, ThingDefOf.WoodLog,
                    new IntVec3(fix.Stand.Position.x + 2, 0, fix.Stand.Position.z + 2),
                    Rot4.North) as Building_Bed;
                if (bed == null)
                {
                    story.Check(false, "a bed spawns in the stand's room");
                    return;
                }
                bed.Medical = true;
                story.Note("a bed set for medical use goes in the stand's room");
                // Doctor alone in vanilla; the Complex Jobs compatibility rows add Nurse and Surgeon.
                List<WorkTypeDef> hospital = new List<WorkTypeDef>(fix.Comp.WorkTypes);
                story.Check(fix.Comp.IsAutomatic && fix.Comp.HandlesWork(doctor)
                            && hospital.All(w => w == doctor || HarnessTables.IsModWork(w)),
                            "the medical bed makes it an automatic Doctor stand, plus any hospital work "
                            + "mods add");
                story.Check(fix.Comp.ServesSomething, "so its Serves row is live");

                fix.Comp.SetServes(CatchmentKind.Radius);
                story.Note("Within a radius is chosen under Serves");
                story.Check(fix.Comp.serves == CatchmentKind.Radius,
                            "with something to tick, it serves a radius");
                story.Check(!fix.Comp.IsAutomatic && fix.Comp.HasCustomSet
                            && hospital.All(fix.Comp.HandlesWork)
                            && fix.Comp.WorkTypes.Count == hospital.Count,
                            "its automatic work became ticks, so nothing visibly changed");
                story.Check(CompShiftStand.CatchmentStands.Contains(fix.Comp),
                            "and it is on the list of stands serving a radius or an area");
                story.Check(fix.Comp.ServesSomething,
                            "and its Serves row stays live, so it can switch back");

                fix.Comp.SetServes(CatchmentKind.Room);
                story.Note("This room is chosen under Serves");
                story.Check(fix.Comp.serves == CatchmentKind.Room && fix.Comp.IsAutomatic
                            && fix.Comp.HandlesWork(doctor),
                            "back on This room with its ticks untouched, it is automatic again, "
                            + "reading the hospital");
                WorkTypeDef research = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Research");
                if (research != null)
                {
                    fix.Comp.SetServes(CatchmentKind.Radius);
                    fix.Comp.ToggleWork(research);
                    fix.Comp.SetServes(CatchmentKind.Room);
                    story.Note("Research is ticked while it serves a radius, then This room is chosen again");
                    story.Check(fix.Comp.serves == CatchmentKind.Room && !fix.Comp.IsAutomatic
                                && fix.Comp.HandlesWork(doctor) && fix.Comp.HandlesWork(research),
                                "but ticks changed while it served a radius stay ticks when it comes back");
                    fix.Comp.ToggleWork(research);
                    story.Note("Research is unticked again");
                }
                fix.Comp.SetServes(CatchmentKind.Radius);
                story.Note("Within a radius is chosen under Serves");
                story.Check(fix.Comp.serves == CatchmentKind.Radius && !fix.Comp.IsAutomatic
                            && fix.Comp.WorkTypes.Count == hospital.Count,
                            "and it serves a radius with the hospital's work ticked again, for what follows");

                fix.Comp.SetServeRadius(7);
                ToggleEach(fix, hospital);
                story.Note("its radius is set to 7 cells, and each of its work types unticked");
                story.Check(fix.Comp.IsExcluded,
                            "unticking the last work type makes it Not used for shift changes");
                story.Check(fix.Comp.serves == CatchmentKind.Room, "and puts it back on its room");
                story.Check(!CompShiftStand.CatchmentStands.Contains(fix.Comp),
                            "off the list of stands serving a radius or an area");
                story.Check(fix.Comp.serveRadius == 7, "with its radius of 7 kept for when it comes back");

                fix.Comp.SetServes(CatchmentKind.Radius);
                story.Note("Within a radius is chosen under Serves");
                story.Check(fix.Comp.serves == CatchmentKind.Room,
                            "a stand not used for shift changes cannot serve a radius");

                fix.Comp.SetAutomatic();
                story.Note("Automatic is chosen on its switch");
                story.Check(fix.Comp.IsAutomatic && fix.Comp.serves == CatchmentKind.Room,
                            "Automatic puts it back on its room, reading the hospital again");
                story.Check(fix.Comp.ServesSomething, "with its Serves row live again");
                fix.Comp.SetServes(CatchmentKind.Radius);
                story.Note("Within a radius is chosen under Serves");
                story.Check(fix.Comp.serves == CatchmentKind.Radius, "and now it serves a radius again");
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
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), InAWalledRoom, Body);

            void Body(Fixture fix, Story story)
            {
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
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
                    Room room = fix.Stand.GetRoom();
                    story.Check(room != null && RoomContentsWork.Any && fix.Comp.IsAutomatic
                                && !fix.Comp.HandlesWork(doctor),
                                "with the contents read, the automatic stand reads its room, which has no role, "
                                + "as no Doctor work");
                    story.Check(room != null && fix.Comp.automaticCache != null
                                && fix.Comp.automaticCacheRoomId == room.ID
                                && fix.Comp.automaticCacheRole == room.Role
                                && fix.Comp.automaticCacheTick == Find.TickManager.TicksGame,
                                "and keeps that reading for this room, its role and this tick");
                    Building_Bed bed = DebugTools_Fixtures.Spawn(
                        fix.Map, bedDef, ThingDefOf.WoodLog,
                        new IntVec3(fix.Stand.Position.x + 2, 0, fix.Stand.Position.z + 2),
                        Rot4.North) as Building_Bed;
                    if (bed == null)
                    {
                        story.Check(false, "a bed spawns in the stand's room");
                        return;
                    }
                    bed.Medical = true;
                    story.Note("a bed set for medical use goes in the room, on the same tick");
                    story.Check(fix.Stand.GetRoom() == room,
                                "the medical bed leaves the room, and its ID, as they were");
                    story.Check(fix.Comp.HandlesWork(doctor),
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
            return Staged((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                          DoctorBesideADoor, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    story.Check(false, "the game has Doctor work and its tending of colonists");
                    return;
                }
                if (fix.Outside.Area == 0)
                {
                    story.Check(false, "the stand's room has a door with open ground outside it");
                    return;
                }
                fix.Comp.ToggleWork(doctor);
                MakeCalm(fix.Map);
                story.Note("Doctor is ticked on the stand, and the map holds no enemies");

                IntVec3 at = fix.Stand.Position;
                IntVec3 nearOutside = new IntVec3(fix.Outside.minX, 0, at.z);
                IntVec3 farOutside = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ);
                IntVec3 farCorner = at + new IntVec3(4, 0, 4);
                story.Check(nearOutside.GetRoom(fix.Map) != fix.Stand.GetRoom(),
                            "the ground outside the door is another room");
                story.Check(!Diverts(fix, HarnessGates.DoctoringAt(tend, nearOutside)),
                            "serving its room, the stand does not dress " + name
                            + " for doctoring just outside the door");
                story.Check(Diverts(fix, HarnessGates.DoctoringAt(tend, farCorner)),
                            "but does for doctoring in the room's far corner");

                fix.Comp.SetServes(CatchmentKind.Radius);
                fix.Comp.SetServeRadius(5);
                story.Note("Within a radius is chosen under Serves, and the radius set to 5 cells");
                story.Check(Diverts(fix, HarnessGates.DoctoringAt(tend, nearOutside)),
                            "within 5 cells, doctoring outside the room but inside the ring dresses " + name);
                story.Check(!Diverts(fix, HarnessGates.DoctoringAt(tend, farOutside)),
                            "doctoring past the ring does not");
                story.Check(!Diverts(fix, HarnessGates.DoctoringAt(tend, farCorner)),
                            "and nor does the room's far corner, past the ring: the radius replaces the room");

                fix.Comp.SetServeRadius(6);
                story.Note("the radius is widened to 6 cells");
                story.Check(Diverts(fix, HarnessGates.DoctoringAt(tend, farCorner)),
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

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
                if (chess == null)
                {
                    story.Check(false, "the game has chess, a joy activity");
                    return;
                }
                if (fix.Outside.Area == 0)
                {
                    story.Check(false, "the stand's room has a door with open ground outside it");
                    return;
                }
                // The pad's own stand stays out of it: not used for shift changes.
                fix.Comp.SetExcluded();
                story.Note("the stand in the room is set to Not used for shift changes");

                IntVec3 standCell = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 1);
                IntVec3 joyCell = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 2);
                Building_OutfitStand outdoor = DebugTools_Fixtures.Spawn(
                    fix.Map, fix.Stand.def, ThingDefOf.WoodLog, standCell, Rot4.North) as Building_OutfitStand;
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
                story.Check(!Diverts(fix, JobMaker.MakeJob(chess, joyCell, joyCell)),
                            "so chess beside it does not dress " + name
                            + ": serving its room, a stand in the open serves nothing");

                comp.SetServes(CatchmentKind.Radius);
                comp.SetServeRadius(3);
                story.Note("the stand outside is set to serve within 3 cells");
                story.Check(Diverts(fix, JobMaker.MakeJob(chess, joyCell, joyCell)),
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
            return Staged((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                          DoctorBesideADoor, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    story.Check(false, "the game has Doctor work and its tending of colonists");
                    return;
                }
                if (fix.Outside.Area == 0)
                {
                    story.Check(false, "the stand's room has a door with open ground outside it");
                    return;
                }
                fix.Comp.ToggleWork(doctor);
                MakeCalm(fix.Map);
                story.Note("Doctor is ticked on the stand, and the map holds no enemies");

                IntVec3 standCell = new IntVec3(fix.Outside.minX, 0, fix.Stand.Position.z + 2);
                Building_OutfitStand mine = DebugTools_Fixtures.Spawn(
                    fix.Map, fix.Stand.def, ThingDefOf.WoodLog, standCell, Rot4.North) as Building_OutfitStand;
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

                IntVec3 workAt = fix.Stand.Position + new IntVec3(1, 0, 0);
                story.Check(mineComp.Covers(workAt, fix.Map),
                            "the outside stand's ring takes in doctoring in the room");
                story.Check(Patch_JobInterception.FindAvailableStand(workAt, fix.Map, fix.Pawn, doctor)
                            == fix.Comp,
                            "with neither stand owned, the nearer shared stand in the room is the one chosen");

                owner.TryAssignPawn(fix.Pawn);
                story.Note(name + " is made the owner of the stand outside");
                story.Check(Patch_JobInterception.FindAvailableStand(workAt, fix.Map, fix.Pawn, doctor)
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
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                          capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                          DoctorInAWalledRoom, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    story.Check(false, "the game has Doctor work and its tending of colonists");
                    return;
                }
                fix.Comp.ToggleWork(doctor);
                MakeCalm(fix.Map);
                story.Note("Doctor is ticked on the stand, and the map holds no enemies");

                Map map = fix.Map;
                IntVec3 at = fix.Stand.Position;
                IntVec3 beside = at + new IntVec3(0, 0, 1);
                IntVec3 field = at + new IntVec3(2, 0, 3);
                IntVec3 edge = at + new IntVec3(2, 0, 2);
                IntVec3 away = at + new IntVec3(4, 0, 1);
                Area_Allowed area = HarnessCatchments.NewArea(fix, "Shift harness field", at, field,
                                                              at + new IntVec3(3, 0, 3));
                if (area == null)
                {
                    story.Check(false, "the map has room for another allowed area");
                    return;
                }
                fix.Comp.SetServeArea(area);
                story.Note("the stand serves a new allowed area, painted on the stand and a small field");
                story.Check(fix.Comp.serves == CatchmentKind.Area && fix.Comp.Covers(field, map),
                            "the stand serves an allowed area holding it and a field");
                story.Check(!fix.Comp.Covers(beside, map) && !fix.Comp.Covers(edge, map)
                            && !fix.Comp.Covers(away, map),
                            "and not the cell beside the stand, the field's edge or the ground away from both");

                IntVec3 wasAt = fix.Pawn.Position;
                Patch_JobInterception.ChangedBackAt[fix.Pawn] = new Patch_JobInterception.ChangeBackLatch(fix.Stand);
                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = beside;
                story.Note(name + " has just changed back at the stand, which now holds off dressing them, "
                           + "and stands on the unpainted cell beside it");
                Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1));
                story.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "the pause after changing back, on the unpainted cell beside the stand, "
                            + "keeps the stand holding off");

                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = edge;
                story.Note(name + " moves to the field's unpainted edge");
                story.Check(!Diverts(fix, HarnessGates.DoctoringAt(tend, field)),
                            "held off, doctoring in the field, from the unpainted edge beside it, "
                            + "does not dress " + name);
                story.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "and the stand keeps holding off");
                Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1));
                story.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "and so it does through the pause after that work, which names no place, "
                            + "on the same edge");
                Patch_JobInterception.ChangedBackAt.Remove(fix.Pawn);
                story.Note("the hold is lifted by hand");
                story.Check(Diverts(fix, HarnessGates.DoctoringAt(tend, field)),
                            "with the hold lifted, the same doctoring dresses " + name);
                Settle(fix.Pawn);

                Patch_JobInterception.ChangedBackAt[fix.Pawn] = new Patch_JobInterception.ChangeBackLatch(fix.Stand);
                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = away;
                story.Note("held off again, " + name + " waits on unpainted ground away from the stand");
                Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, away));
                story.Check(!Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "a job on unpainted ground away from the stand ends the hold");
                Patch_JobInterception.ChangedBackAt.Remove(fix.Pawn);
                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = wasAt;
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
            return Staged((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                          DoctorBesideADoor, Body);

            void Body(Fixture fix, Story story)
            {
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    story.Check(false, "the game has Doctor work and its tending of colonists");
                    return;
                }
                if (fix.Outside.Area == 0)
                {
                    story.Check(false, "the stand's room has a door with open ground outside it");
                    return;
                }
                fix.Comp.ToggleWork(doctor);
                MakeCalm(fix.Map);
                story.Note("Doctor is ticked on the stand, and the map holds no enemies");

                Map map = fix.Map;
                IntVec3 at = fix.Stand.Position;
                IntVec3 door = at + new IntVec3(-1, 0, 0);
                CompShiftStand outdoor = HarnessCatchments.StageStand(fix, at + new IntVec3(-2, 0, 0));
                if (outdoor == null)
                {
                    story.Check(false, "a second stand holding a duster can be put outside the door");
                    return;
                }
                outdoor.ToggleWork(doctor);
                story.Note("a second stand holding a duster stands just outside the door, Doctor ticked");

                Room doorway = door.GetRoom(map);
                List<Thing> listed = doorway != null
                    ? doorway.ContainedThings(fix.Stand.def).ToList()
                    : new List<Thing>();
                story.Check(door.GetDoor(map) != null && doorway != null && doorway.IsDoorway,
                            "the cell beside the stand is a door, a room of its own");
                story.Check(listed.Contains(fix.Stand) && listed.Contains(outdoor.parent),
                            "and the game counts both stands, the one inside and the one outside, "
                            + "as in the doorway's room");
                story.Check(outdoor.ServeFault == CatchmentFault.InTheOpen,
                            "the stand outside stands in the open and serves nothing");
                story.Check(!fix.Comp.Covers(door, map) && !outdoor.Covers(door, map),
                            "neither stand covers the doorway");
                story.Check(!Probe(fix, HarnessGates.DoctoringAt(tend, door)),
                            "so doctoring in the doorway dresses nobody");
                story.Check(Probe(fix, HarnessGates.DoctoringAt(tend, at + new IntVec3(2, 0, 2))),
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
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), InAWalledRoom, Body);

            void Body(Fixture fix, Story story)
            {
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                if (doctor == null)
                {
                    story.Check(false, "the game has the Doctor work type");
                    return;
                }
                fix.Comp.ToggleWork(doctor);
                fix.Comp.SetServes(CatchmentKind.Radius);
                story.Note("Doctor is ticked on the stand, and it serves a radius");
                ThingWithComps loose = ThingMaker.MakeThing(fix.Stand.def, ThingDefOf.WoodLog) as ThingWithComps;
                CompShiftStand stale = loose?.TryGetComp<CompShiftStand>();
                if (stale == null)
                {
                    story.Check(false, "a stand can be made that is on no map");
                    return;
                }
                CompShiftStand.CatchmentStands.Add(stale);
                story.Note("a stand on no map is put on the list, as one left on an abandoned map stays on it");
                story.Check(CompShiftStand.CatchmentStands.Contains(stale)
                            && CompShiftStand.CatchmentStands.Contains(fix.Comp),
                            "the list of stands serving a radius or an area holds the stand on the map "
                            + "and the one on no map");
                Patch_JobInterception.FindAvailableStand(fix.Stand.Position, fix.Map, fix.Pawn, doctor);
                story.Note("a search for a stand to dress for doctoring runs");
                story.Check(!CompShiftStand.CatchmentStands.Contains(stale), "the search drops the one on no map");
                story.Check(CompShiftStand.CatchmentStands.Contains(fix.Comp), "and keeps the stand on the map");
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
            return Staged((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                          DoctorBesideADoor, Body);

            void Body(Fixture fix, Story story)
            {
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                if (doctor == null)
                {
                    story.Check(false, "the game has the Doctor work type");
                    return;
                }
                if (fix.Outside.Area == 0)
                {
                    story.Check(false, "the stand's room has a door with open ground outside it");
                    return;
                }
                fix.Comp.ToggleWork(doctor);
                story.Note("Doctor is ticked on the stand");
                string manual = "ShiftChange.Manual".Translate();
                string radiusLine = "ShiftChange.InspectServesRadius".Translate(7);

                story.Check(fix.Comp.ServesLabel() == (string)"ShiftChange.ServesRoom".Translate(),
                            "serving its room, its Serves row reads this room");
                story.Check(fix.Comp.ByCatchment("ShiftChange.RestRow") == "ShiftChange.RestRow",
                            "its Sleeping row speaks of this room");
                story.Check(HarnessGizmos.Switch(fix.Comp)?.defaultDesc
                            == (string)"ShiftChange.SetWorkTypeDesc".Translate(fix.Comp.WorkTypesLabel(), manual),
                            "so does its switch's tooltip");
                story.Check(!fix.Comp.CompInspectStringExtra().Contains(radiusLine),
                            "and the inspect pane has no radius line");

                fix.Comp.SetServes(CatchmentKind.Radius);
                fix.Comp.SetServeRadius(7);
                story.Note("Within a radius is chosen under Serves, and the radius set to 7 cells");
                story.Check(fix.Comp.ServesLabel() == (string)"ShiftChange.ServesRadius".Translate(7),
                            "serving a radius, its Serves row reads within 7 cells");
                story.Check(fix.Comp.ByCatchment("ShiftChange.RestRow") == "ShiftChange.RestRowRadius",
                            "its Sleeping row speaks of its radius");
                story.Check(HarnessGizmos.Switch(fix.Comp)?.defaultDesc
                            == (string)"ShiftChange.SetWorkTypeDescRadius".Translate(fix.Comp.WorkTypesLabel(),
                                                                                       manual, 7),
                            "so does its switch's tooltip, naming 7 cells");
                story.Check(fix.Comp.CompInspectStringExtra().Contains(radiusLine),
                            "and the inspect pane says it serves a radius of 7 cells");

                CompShiftStand open = HarnessCatchments.StageStand(
                    fix, new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ));
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
    }
}
