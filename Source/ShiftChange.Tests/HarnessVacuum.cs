using System;
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
    /// <summary>
    /// The scenes of the tests on a vacuum map (<see cref="HeadingIntoVacuum"/>,
    /// <see cref="KeepingTheirVacsuitOn"/>). The second map
    /// (<see cref="HarnessFixtures.SecondMap"/>) is Odyssey's <c>SpacePocket</c>, whose biome is
    /// <c>Orbit</c>, so the game treats it as space: a room on it holds a vacuum value, and open
    /// floor reads 1.
    ///
    /// <para>A test sets every room it reads, the open ones too: a doorway averages its
    /// neighbours' stored values (<c>Room.UnsanitizedVacuum</c>), and an open room's stored value
    /// is brought to 1 only when something reads or exchanges it. Odyssey's vacuum simulation
    /// (<c>VacuumComponent</c>) exchanges air between neighbouring rooms every 250 ticks, so a
    /// value a test sets would drift once a swap steps the clock. The ship has no oxygen pump to
    /// keep its air in, so the test holds its values instead (<see cref="HoldVacuum"/>).</para>
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
        /// The ship the gate's tests use, laid out from the second map's centre: the fixture's room
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

            HoldVacuum(fix, away, ship.Bench, 0f);
            HoldVacuum(fix, away, ship.InFarRoom, 0f);
            HoldVacuum(fix, away, ship.OnFloor, connected ? 0f : 1f);
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
        /// Sets the vacuum of the room at <paramref name="cell"/> and holds it there while the
        /// test steps the clock (<see cref="Fixture.Hold"/>). A later hold on the same room wins.
        /// </summary>
        internal static void HoldVacuum(Fixture fix, Map map, IntVec3 cell, float vacuum)
        {
            SetVacuum(map, cell, vacuum);
            fix.Hold += () => SetVacuum(map, cell, vacuum);
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

        /// <summary>A crafter in an enclosed room on DevTools' map.</summary>
        internal static Fixture StageEnclosedCrafter(Map map, CellRect pad)
        {
            return Stage(map, pad, StageKit.NonDisplacing, enclose: true,
                         capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting"));
        }

        // ------------------------------------------------- the other arms of the gate

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
