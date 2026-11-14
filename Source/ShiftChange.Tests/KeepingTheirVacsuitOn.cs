using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DevTools.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// On a ship in space, a stand never takes a vacsuit off a colonist bound through vacuum.
    ///
    /// <para>The ship stands on a second map (<see cref="HarnessFixtures.SecondMap"/>), Odyssey's
    /// <c>SpacePocket</c>, whose biome is <c>Orbit</c>, so the game treats it as space: a room on
    /// it holds a vacuum value, and open floor reads 1. Its builders
    /// (<see cref="HarnessVacuum.StageShip(Map, CellRect)"/>,
    /// <see cref="HarnessVacuum.StageConnectedShip"/>) set every room the tests read, the open
    /// ones too, since a doorway averages its neighbours' stored values
    /// (<c>Room.UnsanitizedVacuum</c>). None of these tests steps the clock, so Odyssey's air
    /// exchange between rooms never runs during one.</para>
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.KeepingTheirVacsuitOn)]
    internal class KeepingTheirVacsuitOn : StoryFixture
    {
        /// <summary>What <see cref="HarnessVacuum.StageShip(Map, CellRect)"/> sets up.</summary>
        internal const string OpenShip =
            "a ship in space: a crafter in a vacsuit and its helmet over a shirt and trousers, beside a "
            + "stand that holds a duster and serves crafting, in a room with a crafting spot and a bill; "
            + "past the room's door, three cells of floor open to vacuum, then a second room with air";

        /// <summary>What <see cref="HarnessVacuum.StageConnectedShip"/> sets up.</summary>
        internal const string ConnectedShip =
            "a ship in space: a crafter in a vacsuit and its helmet over a shirt and trousers, beside a "
            + "stand that holds a duster and serves crafting, in a room with a crafting spot and a bill; "
            + "past the room's door, a walled, roofed corridor with air, then a second room with air";

        /// <summary>
        /// The ground every test here stands on, checked before any of them relies on it: the
        /// second map is in space; a walled, roofed room on it is a room of its own and holds air;
        /// the open floor past its door and the doorway read at or over the line where vacuum
        /// harms; and a region walk that may only step into air stays in the room, where a walk
        /// from the same start that may step anywhere reaches the floor, so the negative means
        /// something.
        /// </summary>
        [Test]
        [TestDescription("in space, a walled, roofed room holds air, while the floor past its door "
                         + "is open to vacuum")]
        internal IEnumerator ARoomInSpaceHoldsAir()
        {
            return OnThePad(Body);

            void Body(Story story)
            {
                Map away = SecondMap(Map);
                if (away == null)
                {
                    story.Check(false, "a second map, out in space, could be made");
                    return;
                }
                try
                {
                    HarnessVacuum.VacuumScene scene = HarnessVacuum.StageVacuumScene(away);
                    story.Note("on it, a walled, roofed room with a door onto three cells of open floor");
                    float harm = VacuumUtility.MinVacuumForDamage;
                    story.Check(away.Biome.inVacuum,
                                "the second map is out in space (its biome: " + away.Biome.label + ")");
                    story.Check(scene.Inside != null && !scene.Inside.TouchesMapEdge
                                && !scene.Inside.ExposedToSpace,
                                "the walled, roofed room is a room of its own, sealed from space");
                    story.Check(scene.Outside != null && scene.Outside.ExposedToSpace,
                                "the floor past its door is open to space");
                    story.Check(scene.InsideCell.GetVacuum(away) < harm, "the room holds air");
                    story.Check(scene.OutsideCell.GetVacuum(away) >= harm, "the open floor is vacuum");
                    story.Check(scene.DoorCell.GetRoom(away)?.Vacuum >= harm,
                                "and the doorway between them is vacuum enough to harm");

                    Region start = scene.InsideCell.GetRegion(away);
                    Region strip = scene.OutsideCell.GetRegion(away);
                    HashSet<Region> anywhere = HarnessVacuum.Walk(start, to => true);
                    HashSet<Region> airOnly = HarnessVacuum.Walk(start,
                        to => to.Room != null && to.Room.Vacuum < harm);
                    story.Check(strip != null && anywhere.Contains(strip),
                                "a walk from the room that may go anywhere reaches the open floor");
                    story.Check(start != null && airOnly.Contains(start) && !airOnly.Contains(strip),
                                "while one that may only step into air stays in the room");
                }
                finally
                {
                    RemoveSecondMap(away);
                }
            }
        }

        /// <summary>
        /// A crafter in a vacsuit, at a stand whose duster would take it off, is not dressed for a
        /// bill fetching from open floor outside: the fetch goes through vacuum, so they keep
        /// their own gear. The same bill fetching from the room dresses them (the control).
        /// </summary>
        [Test]
        [TestDescription("a crafter keeps their vacsuit on for a bill whose steel lies out in vacuum, "
                         + "and changes for one whose steel is in the room")]
        internal IEnumerator SteelOutInVacuumKeepsTheVacsuitOn()
        {
            return Staged(HarnessVacuum.StageShip, OpenShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                HarnessVacuum.Ship ship = HarnessVacuum.ShipOn(fix.Map);
                Job outside = HarnessVacuum.BillFetching(fix, ship.OnFloor);
                Job inside = HarnessVacuum.BillFetching(fix, ship.InRoom);
                if (outside == null || inside == null)
                {
                    story.Check(false, "a bill fetching steel from the open floor, and one fetching it "
                                       + "from the room, could be set up");
                    return;
                }
                story.Note("ten steel lie on the open floor, and ten more in the room");
                story.Check(!Probe(fix, outside),
                            name + " is not sent to change into the duster for a bill fetching steel "
                            + "from the open floor");
                story.Check(Probe(fix, inside), "but is for the same bill fetching steel from the room");
            }
        }

        /// <summary>
        /// A bill keeps them in their own gear just the same when its ingredient lies safe in air
        /// but only vacuum leads to it: a second room past the open floor. The control is
        /// <see cref="ACorridorWithAirChangesNothing"/>.
        /// </summary>
        [Test]
        [TestDescription("steel in a room with air still keeps a crafter's vacsuit on, "
                         + "when only open floor in vacuum leads there")]
        internal IEnumerator ARoomOnlyVacuumLeadsToKeepsItOnToo()
        {
            return Staged(HarnessVacuum.StageShip, OpenShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                Job across = HarnessVacuum.BillFetching(fix, HarnessVacuum.ShipOn(fix.Map).InFarRoom);
                if (across == null)
                {
                    story.Check(false, "a bill fetching steel from the second room could be set up");
                    return;
                }
                story.Check(!Probe(fix, across),
                            name + " is not sent to change into the duster for a bill fetching steel "
                            + "from the second room, which only the open floor leads to");
            }
        }

        /// <summary>
        /// The control for <see cref="ARoomOnlyVacuumLeadsToKeepsItOnToo"/>, on the connected
        /// ship: its floor is a roofed corridor holding air, so the same bill dresses them. Where
        /// everything connects through air nothing changes, as the README promises. Should the
        /// way read as anything but air, the story notes what each cell along it reads
        /// (<see cref="NoteRow"/>).
        /// </summary>
        [Test]
        [TestDescription("where a corridor with air joins the rooms, a crafter changes as usual "
                         + "for a bill fetching from the far room")]
        internal IEnumerator ACorridorWithAirChangesNothing()
        {
            return Staged(HarnessVacuum.StageConnectedShip, ConnectedShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                HarnessVacuum.Ship ship = HarnessVacuum.ShipOn(fix.Map);
                Job across = HarnessVacuum.BillFetching(fix, ship.InFarRoom);
                if (across == null)
                {
                    story.Check(false, "a bill fetching steel from the second room could be set up");
                    return;
                }
                bool through = Patch_JobInterception.AirRouteFor(across, fix.Pawn, fix.Map,
                    Patch_JobInterception.StandCell(fix.Comp)) != Patch_JobInterception.AirRoute.Air;
                story.Check(!through, "the way from the stand to the second room is air all along");
                story.Check(Probe(fix, across),
                            "so " + name + " is sent to change into the duster for a bill fetching "
                            + "steel from there, as anywhere else");
                if (through)
                {
                    NoteRow(story, fix.Map, ship.Room.maxX - 1, ship.FarRoom.minX + 1, ship.OnFloor.z);
                }
            }
        }

        /// <summary>
        /// A crafter with no vacuum protection to lose is dressed as before, even for a bill whose
        /// fetch goes through vacuum: the change takes nothing away, so the vacuum rule does not
        /// apply.
        /// </summary>
        [Test]
        [TestDescription("a crafter with no vacsuit to lose changes for a bill through vacuum as usual")]
        internal IEnumerator WithoutAVacsuitTheyChangeAsUsual()
        {
            return Staged(HarnessVacuum.StageShip, OpenShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                HarnessVacuum.StripVacsuit(fix.Pawn);
                story.Note(name + "'s vacsuit and helmet are taken away");
                Job outside = HarnessVacuum.BillFetching(fix, HarnessVacuum.ShipOn(fix.Map).OnFloor);
                if (outside == null)
                {
                    story.Check(false, "a bill fetching steel from the open floor could be set up");
                    return;
                }
                story.Check(SwapPlan.UnclampedVacuumResistance(fix.Pawn) < 0.01f,
                            name + " has no protection from vacuum left");
                story.Check(Probe(fix, outside),
                            "and is sent to change into the duster for a bill fetching steel from the "
                            + "open floor all the same");
            }
        }

        /// <summary>
        /// Off a vacuum map nothing changes, since the README limits the vacuum rule to a ship in
        /// space: on the colony's own map, the crafter in the same vacsuit is dressed for a bill
        /// whose ingredient lies outside the room.
        /// </summary>
        [Test]
        [TestDescription("on a planet, a crafter in a vacsuit still changes for a bill "
                         + "whose steel lies outside the room")]
        internal IEnumerator OnAPlanetNothingChanges()
        {
            return Staged(HarnessVacuum.StageEnclosedCrafter,
                          "a stand holding a duster in a walled, roofed room on the colony's map, and beside "
                          + "it a crafter in a shirt and trousers", Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef crafting = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting");
                ThingDef spotDef = DefDatabase<ThingDef>.GetNamedSilentFail("CraftingSpot");
                WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsUseCraftingSpot");
                RecipeDef recipe = spotDef?.AllRecipes?.FirstOrDefault();
                if (crafting == null || spotDef == null || giver == null || recipe == null)
                {
                    story.Check(false, "the game has crafting, the crafting spot, its bills and a recipe for it");
                    return;
                }
                if (!story.Check(!fix.Map.Biome.inVacuum, "the colony's own map is not in space")
                    || !story.Check(WearOne(fix.Pawn, "Apparel_Vacsuit") && WearOne(fix.Pawn, "Apparel_VacsuitHelmet"),
                                    name + " puts on a vacsuit and its helmet"))
                {
                    return;
                }
                fix.Comp.ToggleWork(crafting);
                story.Note("the stand serves crafting");
                // Where the ship puts its bench: the stand stands one cell in from the room's
                // south-west corner.
                IntVec3 benchAt = fix.Stand.Position + new IntVec3(3, 0, 2);
                Building_WorkTable spot = DebugTools_Fixtures.Spawn(fix.Map, spotDef, null, benchAt, Rot4.South)
                    as Building_WorkTable;
                if (spot == null)
                {
                    story.Check(false, "a crafting spot could be put in the room");
                    return;
                }
                spot.BillStack.AddBill(new Bill_Production(recipe));
                IntVec3 outsideAt = OpenGround(fix, new IntVec3(fix.Stand.Position.x - 3, 0, fix.Stand.Position.z));
                Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
                steel.stackCount = 10;
                GenSpawn.Spawn(steel, outsideAt, fix.Map);
                Job job = JobMaker.MakeJob(JobDefOf.DoBill, spot);
                job.bill = spot.BillStack[0];
                job.workGiverDef = giver;
                job.targetQueueB = new List<LocalTargetInfo> { steel };
                job.countQueue = new List<int> { 10 };
                story.Note("a crafting spot in the room has a bill, and ten steel lie on the ground outside");
                story.Check(steel.GetRoom() != spot.GetRoom(), "the steel lies outside the crafting spot's room");
                story.Check(Probe(fix, job),
                            "and " + name + " is sent to change into the duster for the bill, as always");
            }
        }

        /// <summary>
        /// The stand search passes over a stand whose outfit would take the suit off and goes on
        /// to the next: for a bill fetching through vacuum, the nearer duster stand loses to a
        /// vacsuit stand across the room. Without the job, the nearer one wins (the control).
        /// </summary>
        [Test]
        [TestDescription("for a bill through vacuum, a crafter passes a nearer duster stand by "
                         + "for a vacsuit stand across the room")]
        internal IEnumerator TheVacsuitStandWinsOverANearerOne()
        {
            return Staged(HarnessVacuum.StageShip, OpenShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef crafting = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting");
                ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
                if (crafting == null || standDef == null)
                {
                    story.Check(false, "the game has crafting and the outfit stand");
                    return;
                }
                Map map = fix.Map;
                HarnessVacuum.Ship ship = HarnessVacuum.ShipOn(map);
                Building_OutfitStand suits = DebugTools_Fixtures.Spawn(map, standDef, ThingDefOf.WoodLog,
                    new IntVec3(ship.Room.maxX - 2, 0, ship.Room.minZ + 1), Rot4.North) as Building_OutfitStand;
                CompShiftStand suitsComp = suits?.TryGetComp<CompShiftStand>();
                if (suitsComp == null || !StockOne(suits, "Apparel_Vacsuit") || !StockOne(suits, "Apparel_VacsuitHelmet"))
                {
                    story.Check(false, "a second stand, holding a vacsuit and its helmet, could be set up "
                                       + "across the room");
                    return;
                }
                suitsComp.ToggleWork(crafting);
                story.Note("a second stand across the room holds a vacsuit and its helmet, and serves crafting too");
                Job outside = HarnessVacuum.BillFetching(fix, ship.OnFloor);
                if (outside == null)
                {
                    story.Check(false, "a bill fetching steel from the open floor could be set up");
                    return;
                }
                JobPlace target = Patch_JobInterception.TargetPlace(outside, map);
                story.Check(SwapPlan.WouldDress(fix.Pawn, suits), name + " could wear what the vacsuit stand holds");
                story.Check(Patch_JobInterception.FindAvailableStand(target, map, fix.Pawn, crafting) == fix.Comp,
                            "for crafting there in general, the nearer duster stand is the one " + name + " would use");
                story.Check(Patch_JobInterception.FindAvailableStand(target, map, fix.Pawn, crafting,
                                Patch_JobInterception.StandTrigger.Work, outside) == suitsComp,
                            "for a bill fetching steel from the open floor, the vacsuit stand is, "
                            + "since the duster would take the suit off");
                bool inserted = Patch_JobInterception.TryInsertSwap(outside, null, fix.Pawn, fix.Pawn.jobs);
                Thing sentTo = fix.Pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand
                    ? fix.Pawn.CurJob.targetA.Thing
                    : null;
                Settle(fix.Pawn);
                story.Check(inserted && sentTo == suits,
                            "and " + name + " is sent to the vacsuit stand to change");
            }
        }

        /// <summary>
        /// Deliveries ask too: a stand serving an area painted across the open floor does not
        /// dress a builder in a vacsuit for a delivery whose steel lies out there, where steel
        /// inside the room dresses them (the control).
        /// </summary>
        [Test]
        [TestDescription("a stand serving an area painted over open floor leaves a colonist in their vacsuit "
                         + "to bring steel in from out there")]
        internal IEnumerator AnAreaOverOpenFloorLeavesTheVacsuitOn()
        {
            return Staged(HarnessVacuum.StageShip, OpenShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef construction = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Construction");
                WorkGiverDef deliver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToBlueprints");
                if (construction == null || deliver == null)
                {
                    story.Check(false, "the game has construction and its deliveries to blueprints");
                    return;
                }
                Map map = fix.Map;
                HarnessVacuum.Ship ship = HarnessVacuum.ShipOn(map);
                fix.Comp.ToggleWork(construction);
                story.Note("the stand serves construction too");
                MakeCalm(map);
                Thing steelOut = HarnessCatchments.Steel(map, ship.OnFloor);
                Thing steelIn = HarnessCatchments.Steel(map, ship.InRoom);
                Thing site = HarnessCatchments.WallBlueprint(map, new IntVec3(ship.Room.minX + 2, 0, ship.Room.maxZ - 1));
                if (site == null)
                {
                    story.Check(false, "a wall could be planned in the room");
                    return;
                }
                story.Note("a wall is planned in the room, with steel for it in the room and on the open floor");
                Area_Allowed area = HarnessCatchments.NewArea(fix, "Shift harness deck",
                    ship.Room.Cells.Concat(new[] { ship.OnFloor }).ToArray());
                if (area == null)
                {
                    story.Check(false, "the map has room for another allowed area");
                    return;
                }
                fix.Comp.SetServeArea(area);
                story.Check(fix.Comp.serves == CatchmentKind.Area && fix.Comp.Covers(ship.OnFloor, map),
                            "the stand serves an allowed area painted over the room and the open floor");
                story.Check(Probe(fix, HarnessCatchments.Delivery(deliver, steelIn, site)),
                            name + " is sent to change into the duster to bring the wall steel from the room");
                story.Check(!Probe(fix, HarnessCatchments.Delivery(deliver, steelOut, site)),
                            "but not to bring it from the open floor, and keeps the vacsuit on");
            }
        }

        /// <summary>
        /// The catch-up while a job is under way asks too: a stand freed while a crafter in a
        /// vacsuit is already on a bill fetching through vacuum does not pull them in to change,
        /// where the same job fetching from the room does (the control). The stand serves nothing
        /// while the job starts, so the job's own start does not dress them first.
        /// </summary>
        [Test]
        [TestDescription("a stand that comes free does not call over a crafter already on a bill "
                         + "fetching through vacuum")]
        internal IEnumerator AStandComingFreeLeavesThemBe()
        {
            return Staged(HarnessVacuum.StageShip, OpenShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef crafting = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting");
                WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsUseCraftingSpot");
                if (crafting == null || giver == null)
                {
                    story.Check(false, "the game has crafting and its bills at a crafting spot");
                    return;
                }
                Map map = fix.Map;
                HarnessVacuum.Ship ship = HarnessVacuum.ShipOn(map);
                MakeCalm(map);
                Thing steelOut = HarnessCatchments.Steel(map, ship.OnFloor);
                Thing steelIn = HarnessCatchments.Steel(map, ship.InRoom);

                fix.Comp.ToggleWork(crafting);
                story.Note("the stand stops serving crafting while " + name + " starts on a bill "
                           + "fetching steel from the open floor");
                // A Goto, because the catch-up may interrupt it (Core's Wait is not suspendable),
                // aimed at another cell so it stays open (a Goto to the cell the pawn stands on
                // completes inside StartJob), and carrying the bill's ingredient queue.
                Job working = JobMaker.MakeJob(JobDefOf.Goto, ship.InRoom);
                working.workGiverDef = giver;
                working.targetQueueB = new List<LocalTargetInfo> { steelOut };
                fix.Pawn.jobs.StartJob(working, JobCondition.InterruptForced, null,
                    resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);
                fix.Comp.ToggleWork(crafting);
                if (!story.Check(fix.Pawn.CurJobDef == JobDefOf.Goto && fix.Comp.HandlesWork(crafting),
                                 name + " is on the bill in the vacsuit, and the stand serves crafting again"))
                {
                    return;
                }
                Patch_JobInterception.TryDressMidJob(fix.Comp, null);
                story.Check(fix.Pawn.CurJobDef == JobDefOf.Goto,
                            "the stand, come free, does not call " + name + " over to change");
                if (fix.Pawn.CurJobDef == JobDefOf.Goto)
                {
                    fix.Pawn.CurJob.targetQueueB = new List<LocalTargetInfo> { steelIn };
                    story.Note("the bill now fetches its steel from the room instead");
                    Patch_JobInterception.TryDressMidJob(fix.Comp, null);
                    story.Check(fix.Pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                                "and with the steel in the room, it does");
                }
                Settle(fix.Pawn);
            }
        }

        /// <summary>
        /// The walk through air goes only where the colonist may pass. On the connected ship a
        /// bill fetching from the far room dresses them (the control); with the far room's door
        /// forbidden the walk finds no way there through air and the bill keeps them in their
        /// vacsuit. That door is the only way in, so vanilla would hand out no such bill; the test
        /// checks that the walk obeys the door, which matters where another way in crosses vacuum.
        /// </summary>
        [Test]
        [TestDescription("a forbidden door is no way through air, so a bill beyond it "
                         + "keeps a crafter's vacsuit on")]
        internal IEnumerator AForbiddenDoorIsNoWayThrough()
        {
            return Staged(HarnessVacuum.StageConnectedShip, ConnectedShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                Map map = fix.Map;
                HarnessVacuum.Ship ship = HarnessVacuum.ShipOn(map);
                Job across = HarnessVacuum.BillFetching(fix, ship.InFarRoom);
                Building_Door door = new IntVec3(ship.FarRoom.minX, 0, ship.OnFloor.z).GetDoor(map);
                if (across == null || door == null)
                {
                    story.Check(false, "a bill fetching steel from the second room, and that room's door, "
                                       + "could be set up");
                    return;
                }
                story.Check(Probe(fix, across),
                            name + " is sent to change into the duster for a bill fetching steel from "
                            + "the second room, down the corridor");
                door.SetForbidden(true, warnOnFail: false);
                story.Note("the second room's door is forbidden");
                Job again = HarnessVacuum.BillFetching(fix, ship.InFarRoom + IntVec3.North);
                story.Check(Patch_JobInterception.AirRouteFor(again, fix.Pawn, map,
                                Patch_JobInterception.StandCell(fix.Comp))
                            == Patch_JobInterception.AirRoute.Vacuum,
                            "now there is no way there through air");
                story.Check(!Probe(fix, again), "so " + name + " keeps the vacsuit on for the same bill");
            }
        }

        /// <summary>
        /// What each cell of a row reads, as notes, for a test that failed on it. For a cell in a
        /// room open to space, its district is also told the roof changed, and the note says
        /// whether the room still reads open after that.
        /// </summary>
        internal static void NoteRow(Story story, Map map, int fromX, int toX, int z)
        {
            for (int x = fromX; x <= toX; x++)
            {
                IntVec3 cell = new IntVec3(x, 0, z);
                Region region = cell.GetRegion(map);
                Room room = region?.Room;
                StringBuilder line = new StringBuilder();
                line.Append(cell).Append(": ")
                    .Append(region == null ? "no region" : "region " + region.id)
                    .Append(room == null ? "" : ", room " + room.ID + " vacuum " + room.Vacuum.ToString("F2")
                                                + (room.ExposedToSpace ? " open" : " closed")
                                                + (room.TouchesMapEdge ? " edge" : "")
                                                + (room.IsDoorway ? " doorway" : "")
                                                + ", " + room.CellCount + " cells")
                    .Append(map.roofGrid.Roofed(cell) ? ", roofed" : "")
                    .Append(", ").Append(cell.GetTerrain(map)?.defName ?? "no terrain");
                if (room != null && room.ExposedToSpace)
                {
                    cell.GetDistrict(map)?.Notify_RoofChanged();
                    line.Append(room.ExposedToSpace ? ", still open after a reset" : ", closed after a reset");
                }
                story.Note(line.ToString());
            }
        }
    }
}
