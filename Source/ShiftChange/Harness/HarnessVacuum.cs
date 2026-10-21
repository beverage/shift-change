// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
#if HARNESS
using System;
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
    /// <summary>
    /// Harness cases on a vacuum map. The second map (<see cref="HarnessFixtures.SecondMap"/>) is
    /// Odyssey's <c>SpacePocket</c>, whose biome is <c>Orbit</c>, so the game treats it as space: a
    /// room on it holds a vacuum value, and open floor reads 1.
    ///
    /// <para>The clock stands still while the harness runs and the vacuum simulation exchanges only
    /// on map ticks, so nothing moves a value a case sets. A case sets every room it reads, the
    /// open ones too: a doorway averages its neighbours' stored values
    /// (<c>Room.UnsanitizedVacuum</c>), and an open room's stored value is brought to 1 only when
    /// something reads or exchanges it.</para>
    /// </summary>
    internal static class HarnessVacuum
    {
        /// <summary>
        /// A walled, roofed room on the second map with a door onto a strip of open floor, the room
        /// set to air and the strip to vacuum.
        /// </summary>
        internal struct VacuumScene
        {
            internal Room Inside;
            internal Room Outside;
            internal IntVec3 InsideCell;
            internal IntVec3 OutsideCell;
            internal IntVec3 DoorCell;
        }

        /// <summary>
        /// Build a <see cref="VacuumScene"/> at the centre of <paramref name="away"/>. The space
        /// map's own terrain is impassable, so the strip is laid as floor: walkable, unroofed, and
        /// so open to space.
        /// </summary>
        internal static VacuumScene StageVacuumScene(Map away)
        {
            VacuumScene scene = new VacuumScene();
            TerrainDef floor = DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor");
            if (floor == null)
            {
                return scene;
            }
            CellRect room = CellRect.CenteredOn(away.Center, 3);
            EnclosePad(away, room);

            scene.DoorCell = new IntVec3(room.maxX, 0, room.CenterCell.z);
            scene.DoorCell.GetEdifice(away)?.Destroy();
            GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Door, ThingDefOf.WoodLog), scene.DoorCell, away);
            for (int x = room.maxX + 1; x <= room.maxX + 3; x++)
            {
                away.terrainGrid.SetTerrain(new IntVec3(x, 0, scene.DoorCell.z), floor);
            }

            scene.InsideCell = room.CenterCell;
            scene.OutsideCell = new IntVec3(room.maxX + 2, 0, scene.DoorCell.z);
            scene.Inside = scene.InsideCell.GetRoom(away);
            scene.Outside = scene.OutsideCell.GetRoom(away);
            if (scene.Inside != null)
            {
                scene.Inside.Vacuum = 0f;
            }
            if (scene.Outside != null)
            {
                scene.Outside.Vacuum = 1f;
            }
            return scene;
        }

        /// <summary>
        /// The ground every vacuum case stands on, checked before any of them relies on it: the
        /// second map is a vacuum map; the staged room is a room of its own and holds air; the
        /// strip and the doorway read at or over the harm line; and a region walk that may only
        /// step into air stays in the room, where an unrestricted walk from the same start reaches
        /// the strip (the control, so the negative means something).
        /// </summary>
        internal static bool VacuumMapStages(Map home)
        {
            Map away = SecondMap(home);
            if (away == null)
            {
                return Expect(false, "a second map was made");
            }
            try
            {
                VacuumScene scene = StageVacuumScene(away);
                float harm = VacuumUtility.MinVacuumForDamage;
                bool ok = Expect(away.Biome.inVacuum,
                                 "the second map's biome (" + away.Biome.defName + ") is in vacuum")
                        & Expect(scene.Inside != null && !scene.Inside.TouchesMapEdge
                                 && !scene.Inside.ExposedToSpace,
                                 "the walled, roofed room is a room of its own, closed to space")
                        & Expect(scene.Outside != null && scene.Outside.ExposedToSpace,
                                 "the open strip beyond its door is open to space")
                        & Expect(scene.InsideCell.GetVacuum(away) < harm, "the room reads as air")
                        & Expect(scene.OutsideCell.GetVacuum(away) >= harm, "the strip reads as vacuum")
                        & Expect(scene.DoorCell.GetRoom(away)?.Vacuum >= harm,
                                 "and the doorway between them reads at the harm line");

                Region start = scene.InsideCell.GetRegion(away);
                Region strip = scene.OutsideCell.GetRegion(away);
                HashSet<Region> anywhere = Walk(start, to => true);
                HashSet<Region> airOnly = Walk(start, to => to.Room != null && to.Room.Vacuum < harm);
                ok &= Expect(strip != null && anywhere.Contains(strip),
                             "a walk from the room that may step anywhere reaches the strip (the control)")
                    & Expect(start != null && airOnly.Contains(start) && !airOnly.Contains(strip),
                             "and one that may only step into air stays in the room");
                return ok;
            }
            finally
            {
                RemoveSecondMap(away);
            }
        }

        /// <summary>Every region a walk from <paramref name="start"/> reaches.</summary>
        internal static HashSet<Region> Walk(Region start, Predicate<Region> mayEnter)
        {
            HashSet<Region> reached = new HashSet<Region>();
            if (start == null)
            {
                return reached;
            }
            RegionTraverser.BreadthFirstTraverse(start, (from, to) => mayEnter(to),
                region =>
                {
                    reached.Add(region);
                    return false;
                }, 9999);
            return reached;
        }

        // ------------------------------------------------- the gate's ship

        /// <summary>
        /// The ship the gate's cases use, laid out from the second map's centre: the fixture's room
        /// (stand, crafter, crafting spot), a door in its east wall onto three cells of floor, and
        /// a second room past them with a door of its own. The floor is open to space, or roofed
        /// into a corridor holding air when <c>connected</c>.
        /// </summary>
        internal struct Ship
        {
            internal CellRect Room;
            internal CellRect FarRoom;
            internal IntVec3 Bench;
            internal IntVec3 InRoom;
            internal IntVec3 OnFloor;
            internal IntVec3 InFarRoom;
        }

        /// <summary>The ship's cells on <paramref name="map"/>, staged or not.</summary>
        internal static Ship ShipOn(Map map)
        {
            CellRect room = CellRect.CenteredOn(map.Center, PadSize / 2);
            int z = room.CenterCell.z;
            return new Ship
            {
                Room = room,
                FarRoom = new CellRect(room.maxX + 4, z - 2, 5, 5),
                Bench = new IntVec3(room.minX + 4, 0, room.minZ + 3),
                InRoom = new IntVec3(room.minX + 3, 0, room.maxZ - 1),
                OnFloor = new IntVec3(room.maxX + 2, 0, z),
                InFarRoom = new IntVec3(room.maxX + 6, 0, z),
            };
        }

        /// <summary>The ship with its floor open to space.</summary>
        internal static Fixture StageShip(Map home, CellRect homePad)
        {
            return StageShip(home, connected: false);
        }

        /// <summary>The ship with its floor roofed into a corridor holding air.</summary>
        internal static Fixture StageConnectedShip(Map home, CellRect homePad)
        {
            return StageShip(home, connected: true);
        }

        /// <summary>
        /// Stage the ship on a second map: the crafter wears shirt, trousers, a vacsuit and its
        /// helmet, which together reach a resistance of 1, and the stand holds a duster, which
        /// displaces the vacsuit. Everything is built before any room is read, so no room caches a
        /// layout that later changed. Null, with the map removed, if any of it cannot be staged.
        /// </summary>
        internal static Fixture StageShip(Map home, bool connected)
        {
            Map away = SecondMap(home);
            if (away == null)
            {
                return null;
            }
            Fixture fix = null;
            try
            {
                fix = BuildShip(away, connected);
            }
            finally
            {
                if (fix == null)
                {
                    RemoveSecondMap(away);
                }
            }
            if (fix != null)
            {
                fix.Pocket = away;
            }
            return fix;
        }

        internal static Fixture BuildShip(Map away, bool connected)
        {
            WorkTypeDef crafting = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting");
            ThingDef spotDef = DefDatabase<ThingDef>.GetNamedSilentFail("CraftingSpot");
            RecipeDef recipe = spotDef?.AllRecipes?.FirstOrDefault();
            TerrainDef floor = DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor");
            if (crafting == null || spotDef == null || recipe == null || floor == null)
            {
                return null;
            }
            Ship ship = ShipOn(away);
            Fixture fix = Stage(away, ship.Room, StageKit.NonDisplacing, enclose: true, capableOf: crafting);
            if (fix == null || !WearOne(fix.Pawn, "Apparel_Vacsuit") || !WearOne(fix.Pawn, "Apparel_VacsuitHelmet"))
            {
                return null;
            }
            fix.Comp.ToggleWork(crafting);

            EnclosePad(away, ship.FarRoom);
            Door(away, new IntVec3(ship.Room.maxX, 0, ship.OnFloor.z));
            Door(away, new IntVec3(ship.FarRoom.minX, 0, ship.OnFloor.z));
            // A connected corridor needs walls down both sides as well as a roof: space terrain
            // cannot be walked but still carries air-exchange regions in the map's outdoor room, so
            // a roofed strip with open sides joins that room and reads as vacuum 1.
            for (int x = ship.Room.maxX + 1; x < ship.FarRoom.minX; x++)
            {
                IntVec3 cell = new IntVec3(x, 0, ship.OnFloor.z);
                away.terrainGrid.SetTerrain(cell, floor);
                if (connected)
                {
                    away.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                    foreach (IntVec3 side in new[] { cell + IntVec3.North, cell + IntVec3.South })
                    {
                        away.roofGrid.SetRoof(side, RoofDefOf.RoofConstructed);
                        GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog), side, away);
                    }
                }
            }
            Building_WorkTable spot = DebugTools_Fixtures.Spawn(away, spotDef, null, ship.Bench, Rot4.South)
                as Building_WorkTable;
            if (spot == null)
            {
                return null;
            }
            spot.BillStack.AddBill(new Bill_Production(recipe));

            SetVacuum(away, ship.Bench, 0f);
            SetVacuum(away, ship.InFarRoom, 0f);
            SetVacuum(away, ship.OnFloor, connected ? 0f : 1f);
            return fix;
        }

        internal static void Door(Map map, IntVec3 cell)
        {
            cell.GetEdifice(map)?.Destroy();
            GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Door, ThingDefOf.WoodLog), cell, map);
        }

        internal static void SetVacuum(Map map, IntVec3 cell, float vacuum)
        {
            Room room = cell.GetRoom(map);
            if (room != null)
            {
                room.Vacuum = vacuum;
            }
        }

        /// <summary>
        /// A bill at the ship's crafting spot whose one ingredient, ten steel, lies at
        /// <paramref name="ingredientAt"/>: the job as <c>WorkGiver_DoBill</c> builds it, the
        /// ingredients in targetQueueB.
        /// </summary>
        internal static Job BillFetching(Fixture fix, IntVec3 ingredientAt)
        {
            Map map = fix.Map;
            Building_WorkTable spot = ShipOn(map).Bench.GetFirstThing<Building_WorkTable>(map);
            WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsUseCraftingSpot");
            if (spot == null || giver == null || spot.BillStack.Count == 0)
            {
                return null;
            }
            Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
            steel.stackCount = 10;
            GenSpawn.Spawn(steel, ingredientAt, map);
            Job job = JobMaker.MakeJob(JobDefOf.DoBill, spot);
            job.bill = spot.BillStack[0];
            job.workGiverDef = giver;
            job.targetQueueB = new List<LocalTargetInfo> { steel };
            job.countQueue = new List<int> { 10 };
            return job;
        }

        /// <summary>
        /// A crafter in a vacsuit, at a stand whose duster would take it off, is not dressed for a
        /// bill fetching from open floor outside: the fetch goes through vacuum, so they keep their
        /// own gear. The same bill fetching from the room dresses them (the control).
        /// </summary>
        internal static bool IngredientInVacuumKeepsTheirGear(Fixture fix)
        {
            Ship ship = ShipOn(fix.Map);
            Job outside = BillFetching(fix, ship.OnFloor);
            Job inside = BillFetching(fix, ship.InRoom);
            if (outside == null || inside == null)
            {
                return Expect(false, "the bills were staged");
            }
            return Expect(!Probe(fix, outside), "a bill fetching from open floor does not dress the crafter")
                   & Expect(Probe(fix, inside), "the same bill fetching from inside the room does (the control)");
        }

        /// <summary>
        /// The fetch fails just the same when the ingredient is safe but only vacuum leads to it: a
        /// second room past the open floor. The control is
        /// <see cref="ConnectedShipChangesNothing"/>.
        /// </summary>
        internal static bool SafeRoomAcrossVacuumKeepsTheirGear(Fixture fix)
        {
            Job across = BillFetching(fix, ShipOn(fix.Map).InFarRoom);
            if (across == null)
            {
                return Expect(false, "the bill was staged");
            }
            return Expect(!Probe(fix, across),
                          "a bill fetching from a room only vacuum leads to does not dress the crafter");
        }

        /// <summary>
        /// The control for <see cref="SafeRoomAcrossVacuumKeepsTheirGear"/>, on the connected ship:
        /// its floor is a roofed corridor holding air, so the same bill dresses them. Where
        /// everything connects through air nothing changes, as the README promises.
        /// </summary>
        internal static bool ConnectedShipChangesNothing(Fixture fix)
        {
            Ship ship = ShipOn(fix.Map);
            Job across = BillFetching(fix, ship.InFarRoom);
            if (across == null)
            {
                return Expect(false, "the bill was staged");
            }
            bool through = Patch_JobInterception.AirRouteFor(across, fix.Pawn, fix.Map,
                Patch_JobInterception.StandCell(fix.Comp)) != Patch_JobInterception.AirRoute.Air;
            bool ok = Expect(!through, "the trip from the stand to the far room reads as all air")
                      & Expect(Probe(fix, across),
                               "on a ship whose rooms all connect through air, the same bill dresses them");
            if (through)
            {
                ReportRow(fix.Map, ship.Room.maxX - 1, ship.FarRoom.minX + 1, ship.OnFloor.z);
            }
            return ok;
        }

        /// <summary>
        /// What each cell of a row reads, for a case that failed on it. For a cell in a room open
        /// to space, its district is also told the roof changed, and the report says whether the
        /// room still reads open after that.
        /// </summary>
        internal static void ReportRow(Map map, int fromX, int toX, int z)
        {
            for (int x = fromX; x <= toX; x++)
            {
                IntVec3 cell = new IntVec3(x, 0, z);
                Region region = cell.GetRegion(map);
                Room room = region?.Room;
                Report.Append("      ").Append(cell).Append(": ")
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
                    Report.Append(room.ExposedToSpace ? ", still open after a reset" : ", closed after a reset");
                }
                Report.AppendLine();
            }
        }

        /// <summary>
        /// A crafter already in the duster, their vacsuit parked, changes back before a bill whose
        /// fetch goes through vacuum and keeps the duster on for one that stays inside (the
        /// control). Dressing asked the same question, so the two directions agree.
        /// </summary>
        internal static bool OnShiftChangesBackBeforeVacuum(Fixture fix)
        {
            if (!Expect(RunSwap(fix), "the crafter dressed") || !Expect(fix.Comp.OnShift, "and is on shift"))
            {
                return false;
            }
            Ship ship = ShipOn(fix.Map);
            Job outside = BillFetching(fix, ship.OnFloor);
            Job inside = BillFetching(fix, ship.InRoom);
            if (outside == null || inside == null)
            {
                return Expect(false, "the bills were staged");
            }
            return Expect(Probe(fix, outside), "a bill fetching from open floor sends them back to change first")
                   & Expect(!Probe(fix, inside), "one fetching from inside keeps the duster on (the control)");
        }

        /// <summary>
        /// A crafter with no vacuum protection to lose is dressed as before, even for a bill whose
        /// fetch goes through vacuum: the swap takes nothing away, so the gate does not apply.
        /// </summary>
        internal static bool UnprotectedCrafterIsUnaffected(Fixture fix)
        {
            StripVacsuit(fix.Pawn);
            Job outside = BillFetching(fix, ShipOn(fix.Map).OnFloor);
            if (outside == null)
            {
                return Expect(false, "the bill was staged");
            }
            return Expect(SwapPlan.UnclampedVacuumResistance(fix.Pawn) < 0.01f,
                          "the crafter has no vacuum resistance")
                   & Expect(Probe(fix, outside), "and is dressed for the bill all the same");
        }

        /// <summary>Take the vacsuit and its helmet off a pawn, and destroy them.</summary>
        internal static void StripVacsuit(Pawn pawn)
        {
            foreach (Apparel worn in pawn.apparel.WornApparel.ToList())
            {
                if (worn.def.defName == "Apparel_Vacsuit" || worn.def.defName == "Apparel_VacsuitHelmet")
                {
                    pawn.apparel.Remove(worn);
                    worn.Destroy();
                }
            }
        }

        /// <summary>The messages on screen now, read off vanilla's private list.</summary>
        internal static List<Message> LiveMessages()
        {
            List<Message> live = AccessTools.Field(typeof(Messages), "liveMessages")?.GetValue(null) as List<Message>;
            return live == null ? new List<Message>() : new List<Message>(live);
        }

        /// <summary>A message with this text appeared since <paramref name="before"/>.</summary>
        internal static bool PostedSince(List<Message> before, string text)
        {
            return LiveMessages().Any(m => m.text == text && !before.Contains(m));
        }

        /// <summary>
        /// An order runs as given, in uniform; only a message says the uniform protects less, and
        /// it goes up on screen. A second order within the hour says nothing more, since vanilla
        /// answers a repeated message by flashing it and playing its sound again, and with the
        /// stamp cleared it speaks again (the control). A drafted move, which never reaches the
        /// gates, is warned about the same way.
        /// </summary>
        internal static bool OrderIntoVacuumRunsAsGiven(Fixture fix)
        {
            if (!Expect(RunSwap(fix), "the crafter dressed") || !Expect(fix.Comp.OnShift, "and is on shift"))
            {
                return false;
            }
            Ship ship = ShipOn(fix.Map);
            // Two cells of the strip: an item spawned onto another pushes it aside.
            Job ordered = BillFetching(fix, ship.OnFloor);
            Job again = BillFetching(fix, ship.OnFloor + IntVec3.East);
            if (ordered == null || again == null)
            {
                return Expect(false, "the bills were staged");
            }
            ordered.playerForced = true;
            again.playerForced = true;
            int id = fix.Pawn.thingIDNumber;
            Patch_JobInterception.LastVacuumWarningTick.Remove(id);
            string text = "ShiftChange.OrderIntoVacuum".Translate(fix.Pawn.LabelShort);
            List<Message> before = LiveMessages();
            bool ok = Expect(!Probe(fix, ordered), "an ordered bill through vacuum is not diverted")
                      & Expect(PostedSince(before, text), "and a message saying the uniform protects less goes up")
                      & Expect(!Patch_JobInterception.WarnIfOrderedIntoVacuum(again, fix.Pawn),
                               "a second order within the hour says nothing more");
            Patch_JobInterception.LastVacuumWarningTick.Remove(id);
            ok &= Expect(Patch_JobInterception.WarnIfOrderedIntoVacuum(again, fix.Pawn),
                         "and with the stamp cleared it speaks again (control)");

            Patch_JobInterception.LastVacuumWarningTick.Remove(id);
            Job move = JobMaker.MakeJob(JobDefOf.Goto, ship.OnFloor);
            move.playerForced = true;
            fix.Pawn.drafter.Drafted = true;
            try
            {
                ok &= Expect(Patch_JobInterception.MapToDecideOn(move, fix.Pawn) == null,
                             "drafted, the interception leaves them alone (control)")
                    & Expect(!Probe(fix, move), "a drafted move onto open floor is not diverted")
                    & Expect(Patch_JobInterception.WarnedLately(fix.Pawn), "and is warned about all the same");
            }
            finally
            {
                fix.Pawn.drafter.Drafted = false;
            }
            return ok;
        }

        /// <summary>
        /// The gate overrides the exemption emergencies have: an emergency job through vacuum sends
        /// a colonist in uniform back to change first, where the same job inside the room still
        /// goes straight ahead (the control). The emergency is firefighting's giver, because the
        /// medical-emergency setting never opens it.
        /// </summary>
        internal static bool EmergencyIntoVacuumChangesBackFirst(Fixture fix)
        {
            WorkGiverDef fires = DefDatabase<WorkGiverDef>.GetNamedSilentFail("FightFires");
            if (!Expect(fires != null && fires.emergency, "firefighting's giver is an emergency giver"))
            {
                return false;
            }
            if (!Expect(RunSwap(fix), "the colonist dressed") || !Expect(fix.Comp.OnShift, "and is on shift"))
            {
                return false;
            }
            Ship ship = ShipOn(fix.Map);
            Job outside = JobMaker.MakeJob(JobDefOf.Wait, ship.OnFloor);
            outside.workGiverDef = fires;
            Job inside = JobMaker.MakeJob(JobDefOf.Wait, ship.InRoom);
            inside.workGiverDef = fires;
            return Expect(Probe(fix, outside), "an emergency out on open floor sends them back to change first")
                   & Expect(!Probe(fix, inside), "the same emergency inside goes straight ahead (the control)");
        }

        /// <summary>A crafter in an enclosed room on the harness's own map.</summary>
        internal static Fixture StageEnclosedCrafter(Map map, CellRect pad)
        {
            return Stage(map, pad, StageKit.NonDisplacing, enclose: true,
                         capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting"));
        }

        /// <summary>
        /// Off a vacuum map nothing changes, since the README scopes the vacuum rule to a ship in
        /// space: on the harness's own map, the crafter in the same vacsuit is dressed for a bill
        /// whose ingredient lies outside the room.
        /// </summary>
        internal static bool PlanetMapChangesNothing(Fixture fix)
        {
            WorkTypeDef crafting = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting");
            ThingDef spotDef = DefDatabase<ThingDef>.GetNamedSilentFail("CraftingSpot");
            WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsUseCraftingSpot");
            RecipeDef recipe = spotDef?.AllRecipes?.FirstOrDefault();
            if (crafting == null || spotDef == null || giver == null || recipe == null)
            {
                return Expect(false, "the crafting defs resolve");
            }
            if (!Expect(!fix.Map.Biome.inVacuum, "the harness's own map is not a vacuum map")
                || !Expect(WearOne(fix.Pawn, "Apparel_Vacsuit") && WearOne(fix.Pawn, "Apparel_VacsuitHelmet"),
                           "the colonist wears a vacsuit and its helmet"))
            {
                return false;
            }
            fix.Comp.ToggleWork(crafting);
            // Where the ship puts its bench: Stage puts the stand one cell in from the pad's
            // south-west corner.
            IntVec3 benchAt = fix.Stand.Position + new IntVec3(3, 0, 2);
            Building_WorkTable spot = DebugTools_Fixtures.Spawn(fix.Map, spotDef, null, benchAt, Rot4.South)
                as Building_WorkTable;
            if (spot == null)
            {
                return Expect(false, "a crafting spot was staged");
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
            return Expect(steel.GetRoom() != spot.GetRoom(), "the ingredient lies outside the bench's room")
                   & Expect(Probe(fix, job), "and the bill dresses them, as it always has");
        }

        // ------------------------------------------------- the other arms of the gate

        /// <summary>
        /// A stand whose outfit raises protection still dresses, for a bill fetching through
        /// vacuum: the crafter has no suit of their own, and the stand holds one. Once they wear
        /// it, the gate leaves them be, since the uniform protects better than what they parked.
        /// </summary>
        internal static bool VacsuitStandStillDresses(Fixture fix)
        {
            StripVacsuit(fix.Pawn);
            ClearStand(fix.Stand);
            if (!Expect(StockOne(fix.Stand, "Apparel_Vacsuit") && StockOne(fix.Stand, "Apparel_VacsuitHelmet"),
                        "the stand holds a vacsuit and its helmet"))
            {
                return false;
            }
            Ship ship = ShipOn(fix.Map);
            Job outside = BillFetching(fix, ship.OnFloor);
            if (outside == null)
            {
                return Expect(false, "the bill was staged");
            }
            bool ok = Expect(Probe(fix, outside),
                             "the vacsuit stand dresses an unsuited crafter for a bill fetching through vacuum")
                      & Expect(RunSwap(fix), "the crafter dressed")
                      & Expect(fix.Comp.OnShift, "and is on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            Job again = BillFetching(fix, ship.OnFloor + IntVec3.East);
            return ok
                   & Expect(Patch_JobInterception.UniformIntoVacuum(again, fix.Pawn, fix.Comp, fix.Map)
                            == Patch_JobInterception.UniformInVacuum.Fine,
                            "in the vacsuit, the gate leaves them be: it protects better than their own clothes")
                   & Expect(!Probe(fix, again), "and the next bill through vacuum keeps them in it");
        }

        /// <summary>
        /// The stand search passes over a stand whose outfit would take the suit off and goes on to
        /// the next: for a bill fetching through vacuum, the nearer duster stand loses to a vacsuit
        /// stand across the room. Without the job, the nearer one wins (the control).
        /// </summary>
        internal static bool ProtectiveStandWinsOverANearerOne(Fixture fix)
        {
            WorkTypeDef crafting = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting");
            ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
            if (crafting == null || standDef == null)
            {
                return Expect(false, "the crafting and stand defs resolve");
            }
            Map map = fix.Map;
            Ship ship = ShipOn(map);
            Building_OutfitStand suits = DebugTools_Fixtures.Spawn(map, standDef, ThingDefOf.WoodLog,
                new IntVec3(ship.Room.maxX - 2, 0, ship.Room.minZ + 1), Rot4.North) as Building_OutfitStand;
            CompShiftStand suitsComp = suits?.TryGetComp<CompShiftStand>();
            if (suitsComp == null || !StockOne(suits, "Apparel_Vacsuit") || !StockOne(suits, "Apparel_VacsuitHelmet"))
            {
                return Expect(false, "a second stand holding a vacsuit and its helmet was staged");
            }
            suitsComp.ToggleWork(crafting);
            Job outside = BillFetching(fix, ship.OnFloor);
            if (outside == null)
            {
                return Expect(false, "the bill was staged");
            }
            JobPlace target = Patch_JobInterception.TargetPlace(outside, map);
            bool ok = Expect(SwapPlan.WouldDress(fix.Pawn, suits), "the crafter could wear the vacsuit stand's kit")
                      & Expect(Patch_JobInterception.FindAvailableStand(target, map, fix.Pawn, crafting) == fix.Comp,
                               "without the job, the nearer duster stand wins (control)")
                      & Expect(Patch_JobInterception.FindAvailableStand(target, map, fix.Pawn, crafting,
                                   Patch_JobInterception.StandTrigger.Work, outside) == suitsComp,
                               "with it, the vacsuit stand does: the duster would take the suit off");
            bool inserted = Patch_JobInterception.TryInsertSwap(outside, null, fix.Pawn, fix.Pawn.jobs);
            Thing sentTo = fix.Pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand
                ? fix.Pawn.CurJob.targetA.Thing
                : null;
            Settle(fix.Pawn);
            return ok & Expect(inserted && sentTo == suits, "and the crafter is sent to the vacsuit stand to dress");
        }

        /// <summary>
        /// The delivery arm asks too: an area stand whose area is painted across the open floor
        /// does not dress a builder in a vacsuit for a delivery whose steel lies out there, where
        /// steel inside the room dresses them (the control).
        /// </summary>
        internal static bool AreaDeliveryAcrossVacuumKeepsTheirGear(Fixture fix)
        {
            WorkTypeDef construction = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Construction");
            WorkGiverDef deliver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToBlueprints");
            if (construction == null || deliver == null)
            {
                return Expect(false, "the construction defs resolve");
            }
            Map map = fix.Map;
            Ship ship = ShipOn(map);
            fix.Comp.ToggleWork(construction);
            MakeCalm(map);
            Thing steelOut = HarnessCatchments.Steel(map, ship.OnFloor);
            Thing steelIn = HarnessCatchments.Steel(map, ship.InRoom);
            Thing site = HarnessCatchments.WallBlueprint(map, new IntVec3(ship.Room.minX + 2, 0, ship.Room.maxZ - 1));
            if (site == null)
            {
                return Expect(false, "the wall blueprint places");
            }
            Area_Allowed area = HarnessCatchments.NewArea(fix, "Shift harness deck",
                ship.Room.Cells.Concat(new[] { ship.OnFloor }).ToArray());
            if (area == null)
            {
                return Expect(false, "the map has room for another allowed area");
            }
            fix.Comp.SetServeArea(area);
            return Expect(fix.Comp.serves == CatchmentKind.Area && fix.Comp.Covers(ship.OnFloor, map),
                          "the stand serves an area painted over the room and the open floor (control)")
                   & Expect(Probe(fix, HarnessCatchments.Delivery(deliver, steelIn, site)),
                            "a delivery from steel in the room dresses the builder (control)")
                   & Expect(!Probe(fix, HarnessCatchments.Delivery(deliver, steelOut, site)),
                            "one from steel on the open floor keeps them in their vacsuit");
        }

        /// <summary>
        /// The mid-job catch-up asks too: a stand freed while a crafter in a vacsuit is already on
        /// a bill fetching through vacuum does not pull them in to change, where the same job
        /// fetching from the room does (the control). The stand serves nothing while the job
        /// starts, so the job's own start does not dress them first.
        /// </summary>
        internal static bool FreedStandSkipsAJobThroughVacuum(Fixture fix)
        {
            WorkTypeDef crafting = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting");
            WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsUseCraftingSpot");
            if (crafting == null || giver == null)
            {
                return Expect(false, "the crafting defs resolve");
            }
            Map map = fix.Map;
            Ship ship = ShipOn(map);
            MakeCalm(map);
            Thing steelOut = HarnessCatchments.Steel(map, ship.OnFloor);
            Thing steelIn = HarnessCatchments.Steel(map, ship.InRoom);

            fix.Comp.ToggleWork(crafting);
            // A Goto, because the catch-up may interrupt it (Core's Wait is not suspendable), aimed
            // at another cell so it stays open (a Goto to the cell the pawn stands on completes
            // inside StartJob), and carrying the bill's ingredient queue.
            Job working = JobMaker.MakeJob(JobDefOf.Goto, ship.InRoom);
            working.workGiverDef = giver;
            working.targetQueueB = new List<LocalTargetInfo> { steelOut };
            fix.Pawn.jobs.StartJob(working, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);
            fix.Comp.ToggleWork(crafting);
            bool ok = Expect(fix.Pawn.CurJobDef == JobDefOf.Goto && fix.Comp.HandlesWork(crafting),
                             "the crafter is on the job in their vacsuit, and the stand serves crafting again (control)");
            if (!ok)
            {
                return false;
            }
            Patch_JobInterception.TryDressMidJob(fix.Comp, null);
            ok &= Expect(fix.Pawn.CurJobDef == JobDefOf.Goto,
                         "a freed stand does not catch them up for a job fetching through vacuum");
            if (fix.Pawn.CurJobDef == JobDefOf.Goto)
            {
                fix.Pawn.CurJob.targetQueueB = new List<LocalTargetInfo> { steelIn };
                Patch_JobInterception.TryDressMidJob(fix.Comp, null);
                ok &= Expect(fix.Pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                             "the same job fetching from the room, it does (control)");
            }
            Settle(fix.Pawn);
            return ok;
        }

        /// <summary>
        /// An errand queued ahead of the stand's own work rides along in uniform, but not into
        /// vacuum: on the connected ship, a haul from the far room with a bill queued behind it
        /// keeps the duster on (the control), and with the corridor open to vacuum it sends them
        /// back to change first.
        /// </summary>
        internal static bool ErrandAheadOfWorkChangesBackBeforeVacuum(Fixture fix)
        {
            WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsUseCraftingSpot");
            if (giver == null)
            {
                return Expect(false, "the crafting giver resolves");
            }
            if (!Expect(RunSwap(fix), "the crafter dressed") || !Expect(fix.Comp.OnShift, "and is on shift"))
            {
                return false;
            }
            Map map = fix.Map;
            Ship ship = ShipOn(map);
            Thing steelFar = HarnessCatchments.Steel(map, ship.InFarRoom);
            JobQueue queue = fix.Pawn.jobs.jobQueue;
            queue.EnqueueFirst(HarnessRideAlong.QueuedWork(giver, ship.Bench));
            bool ok = Expect(!Probe(fix, HarnessRideAlong.GiverlessHaul(steelFar, ship.InRoom)),
                             "connected: a haul from the far room, ahead of a bill the stand serves, keeps the duster on (control)");
            SetVacuum(map, ship.OnFloor, 1f);
            queue.EnqueueFirst(HarnessRideAlong.QueuedWork(giver, ship.Bench));
            return ok & Expect(Probe(fix, HarnessRideAlong.GiverlessHaul(steelFar, ship.InRoom)),
                               "with the corridor open to vacuum, the same errand sends them back to change first");
        }

        /// <summary>
        /// The air walk goes only where the colonist may pass. On the connected ship a bill
        /// fetching from the far room dresses them (the control); with the far room's door
        /// forbidden the walk finds no way there through air and the bill keeps them in their
        /// vacsuit. That door is the only way in, so vanilla would hand out no such bill; the case
        /// tests that the walk obeys the door, which matters where another way in crosses vacuum.
        /// </summary>
        internal static bool ForbiddenDoorIsNoWayThrough(Fixture fix)
        {
            Map map = fix.Map;
            Ship ship = ShipOn(map);
            Job across = BillFetching(fix, ship.InFarRoom);
            Building_Door door = new IntVec3(ship.FarRoom.minX, 0, ship.OnFloor.z).GetDoor(map);
            if (across == null || door == null)
            {
                return Expect(false, "the bill and the far room's door were staged");
            }
            bool ok = Expect(Probe(fix, across), "connected: a bill fetching from the far room dresses them (control)");
            door.SetForbidden(true, warnOnFail: false);
            Job again = BillFetching(fix, ship.InFarRoom + IntVec3.North);
            return ok
                   & Expect(Patch_JobInterception.AirRouteFor(again, fix.Pawn, map, Patch_JobInterception.StandCell(fix.Comp))
                            == Patch_JobInterception.AirRoute.Vacuum,
                            "with the far room's door forbidden, the walk finds no way there through air")
                   & Expect(!Probe(fix, again), "and the bill keeps them in their vacsuit");
        }

        /// <summary>
        /// A colonist in uniform cut off from their stand by vacuum is not sent back across it to
        /// change: a job in the far room keeps the duster on, and so does one out on the open
        /// floor, which a message reports instead. From the stand's room, the far-room job changes
        /// them back first (the control).
        /// </summary>
        internal static bool StrandedColonistIsNotSentAcrossToChange(Fixture fix)
        {
            WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsUseCraftingSpot");
            if (giver == null)
            {
                return Expect(false, "the crafting giver resolves");
            }
            if (!Expect(RunSwap(fix), "the crafter dressed") || !Expect(fix.Comp.OnShift, "and is on shift"))
            {
                return false;
            }
            Map map = fix.Map;
            Ship ship = ShipOn(map);
            int z = ship.OnFloor.z;
            Job local = HarnessCatchments.WorkAt(giver, new IntVec3(ship.FarRoom.minX + 3, 0, z + 1));
            Job outOnFloor = HarnessCatchments.WorkAt(giver, ship.OnFloor);
            Patch_JobInterception.LastVacuumWarningTick.Remove(fix.Pawn.thingIDNumber);
            string text = "ShiftChange.VacuumStandAcrossVacuum".Translate(fix.Pawn.LabelShort);

            fix.Pawn.Position = new IntVec3(ship.FarRoom.minX + 1, 0, z);
            List<Message> before = LiveMessages();
            bool ok = Expect(fix.Pawn.GetRoom() == ship.InFarRoom.GetRoom(map),
                             "the crafter stands in the far room (precondition)")
                      & Expect(!Probe(fix, local),
                               "cut off by vacuum, a job in the far room does not send them back across it to change")
                      & Expect(!Probe(fix, outOnFloor), "nor does one out on the open floor")
                      & Expect(PostedSince(before, text), "which a message reports instead");

            fix.Pawn.Position = fix.Stand.InteractionCell;
            return ok & Expect(Probe(fix, HarnessCatchments.WorkAt(giver, new IntVec3(ship.FarRoom.minX + 3, 0, z + 1))),
                               "from the stand's room, the same far-room job changes them back first (control)");
        }

        /// <summary>
        /// The cooldown a failed swap leaves does not wave the next job into vacuum: with it in
        /// force, a meal break that would change them back does not (the control), and a bill
        /// through vacuum still does.
        /// </summary>
        internal static bool CooldownDoesNotWaveThemIntoVacuum(Fixture fix)
        {
            if (!Expect(RunSwap(fix), "the crafter dressed") || !Expect(fix.Comp.OnShift, "and is on shift"))
            {
                return false;
            }
            int id = fix.Pawn.thingIDNumber;
            Patch_JobInterception.LastBlockedTick[id] = Find.TickManager.TicksGame;
            bool ok = Expect(Patch_JobInterception.CoolingDown(fix.Pawn),
                             "a swap that could not start a moment ago has them cooling down (precondition)")
                      & Expect(!HarnessLifecycle.MealBreakSendsThemBack(fix),
                               "and the cooldown holds an ordinary change back, a meal break's (control)");
            Patch_JobInterception.LastBlockedTick[id] = Find.TickManager.TicksGame;
            Job outside = BillFetching(fix, ShipOn(fix.Map).OnFloor);
            return ok & Expect(outside != null && Probe(fix, outside),
                               "a bill through vacuum still changes them back first")
                      & Expect(HarnessLifecycle.MealBreakSendsThemBack(fix),
                               "and with no cooldown the meal break does too (control)");
        }

        /// <summary>
        /// The return trip's refusal holds in vacuum too: an urgent patient goes straight to a
        /// medical bed across vacuum in uniform, rather than to the stand to change, where
        /// recuperating there with no such wound changes them back first (the control).
        /// </summary>
        internal static bool UrgentPatientGoesStraightToBedAcrossVacuum(Fixture fix)
        {
            ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
            if (bedDef == null)
            {
                return Expect(false, "the bed def resolves");
            }
            if (!Expect(RunSwap(fix), "the crafter dressed") || !Expect(fix.Comp.OnShift, "and is on shift"))
            {
                return false;
            }
            Map map = fix.Map;
            Ship ship = ShipOn(map);
            Building_Bed bed = DebugTools_Fixtures.Spawn(map, bedDef, ThingDefOf.WoodLog,
                new IntVec3(ship.FarRoom.minX + 2, 0, ship.OnFloor.z), Rot4.South) as Building_Bed;
            if (bed == null)
            {
                return Expect(false, "a bed spawns in the far room");
            }
            bed.Medical = true;
            bool was = ShiftChangeMod.Settings.medicalEmergenciesChangeFirst;
            Hediff wound = null;
            try
            {
                ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = false;
                bool ok = Expect(!HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn),
                                 "the colonist needs no doctor to begin with (precondition)")
                          & Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed), JobTag.RestingForMedicalReasons),
                                   "recuperating in the bed across vacuum changes them back first (control)");
                BodyPartRecord part = fix.Pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault();
                wound = HediffMaker.MakeHediff(HediffDefOf.Cut, fix.Pawn, part);
                wound.Severity = 6f;
                fix.Pawn.health.AddHediff(wound, part);
                return ok
                       & Expect(HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn) && !fix.Pawn.Downed,
                                "an untended wound makes them an urgent case, still on their feet (precondition)")
                       & Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed), JobTag.RestingForMedicalReasons),
                                "so they go straight to the bed across vacuum");
            }
            finally
            {
                ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = was;
                if (wound != null)
                {
                    fix.Pawn.health.RemoveHediff(wound);
                }
            }
        }

        /// <summary>
        /// A colonist whose stand is on another map cannot change back, and is not left silent
        /// about it: flown to a vacuum map in a duster, their vacsuit parked at a stand on the
        /// harness's map, a job through vacuum puts up a message that their own clothes are not on
        /// this map, and so does an order.
        /// </summary>
        internal static bool StandOnAnotherMapStillWarns(Fixture fix)
        {
            WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsUseCraftingSpot");
            if (giver == null)
            {
                return Expect(false, "the crafting giver resolves");
            }
            if (!Expect(WearOne(fix.Pawn, "Apparel_Vacsuit") && WearOne(fix.Pawn, "Apparel_VacsuitHelmet"),
                        "the crafter wears a vacsuit and its helmet")
                || !Expect(RunSwap(fix), "and dressed at a stand on the harness's map")
                || !Expect(fix.Comp.OnShift, "and is on shift"))
            {
                return false;
            }
            Map home = fix.Map;
            Map away = SecondMap(home);
            if (away == null)
            {
                return Expect(false, "a second map was made");
            }
            fix.Pocket = away;
            VacuumScene scene = StageVacuumScene(away);
            int id = fix.Pawn.thingIDNumber;
            string text = "ShiftChange.VacuumStandElsewhere".Translate(fix.Pawn.LabelShort);
            HarnessLifecycle.Fly(fix.Pawn, away, scene.InsideCell);
            try
            {
                Patch_JobInterception.LastVacuumWarningTick.Remove(id);
                List<Message> before = LiveMessages();
                bool ok = Expect(fix.Pawn.Map == away && CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                                 "the crafter is on the vacuum map, still on shift at the stand at home (precondition)")
                          & Expect(!Probe(fix, HarnessCatchments.WorkAt(giver, scene.OutsideCell)),
                                   "a job out on the open floor sends nobody after a stand on another map")
                          & Expect(PostedSince(before, text), "and a message says their own clothes are not on this map");

                Patch_JobInterception.LastVacuumWarningTick.Remove(id);
                Job ordered = HarnessCatchments.WorkAt(giver, scene.OutsideCell);
                ordered.playerForced = true;
                return ok & Expect(Patch_JobInterception.WarnIfOrderedIntoVacuum(ordered, fix.Pawn),
                                   "and an order out there says so too");
            }
            finally
            {
                HarnessLifecycle.Fly(fix.Pawn, home, fix.Stand.InteractionCell);
            }
        }

        /// <summary><see cref="StageEnclosedCrafter"/> with its stand serving crafting.</summary>
        internal static Fixture StageCrafterAtHome(Map map, CellRect pad)
        {
            WorkTypeDef crafting = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting");
            Fixture fix = Stage(map, pad, StageKit.NonDisplacing, enclose: true, capableOf: crafting);
            fix?.Comp.ToggleWork(crafting);
            return fix;
        }
    }
}
#endif
