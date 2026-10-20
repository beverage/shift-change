// SCENES only (see the config table in ShiftChange.csproj). This file builds a scene and must
// never reach a player: it clears a 13x16 footprint, destroying every building and item in it
// and vanishing any pawn, then leaves permanent player-faction colonists, buildings and terrain
// behind. Its fixture primitives live in DebugTools_Fixtures, under SCENES || HARNESS.
#if SCENES
using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using static ShiftChange.DebugTools_Fixtures;

namespace ShiftChange
{
    /// <summary>
    /// A one-click film set and test fixture: dev mode → Shift Change → Dev tools... → Build
    /// demo stage, then click a map cell, which becomes the stage's south-west corner. It builds
    /// a 2x2 complex (hospital and laboratory on top, kitchen and dining room below, a corridor
    /// between the rows) with three specialists, a surgery patient, an active research project,
    /// one bulk kitchen bill and exactly one batch of ingredients.
    ///
    /// The intended take, from unpause: all three change and start work. The chef cooks four
    /// meals and carries them to the dining shelf in uniform. The carry is a new HaulToCell job
    /// (Toils_Recipe.FinishRecipeAndStartStoringProduct starts it as the bill ends), but its
    /// target is the meal in the chef's hands, still in the kitchen, so the return trip keeps
    /// the uniform on. The chef then walks back, changes out and goes to eat. The doctor works
    /// through three queued surgeries, changes out and eats. The researcher stays. Ordering
    /// between doctor and chef is approximate; tune it with SurgeryBills and the food constants.
    ///
    /// Footage is filmed on the Media configuration (Release codegen with the stages present;
    /// see docs/DESIGN.md, "Development tooling").
    ///
    /// devtools/bridge/shoot-controls-card.py selects this stage by menu label and hard-codes
    /// the hospital stand's cell from RoomRect and SpawnStand, so a change to either has to
    /// reach that script by hand.
    /// </summary>
    internal static class DebugTools_DemoStage
    {
        internal const int RoomInterior = 5;
        internal const int CorridorHeight = 2;
        internal const int Width = RoomInterior * 2 + 3;
        internal const int Height = RoomInterior * 2 + CorridorHeight + 4;

        // The take's tuning knobs; starting food is a fraction of the maximum. A human looks
        // for food below 0.3 (RaceProperties.FoodLevelPercentageWantEat; the Hungry category
        // starts lower, at 0.24), so at 0.30 and 0.33 nobody eats before working, yet the doctor
        // and chef want the cooked meals soon after finishing. The researcher starts full.
        internal const float DoctorFood = 0.30f;
        internal const float ChefFood = 0.33f;
        internal const int SurgeryBills = 3;

        internal static void BuildDemoStage()
        {
            Map map = Find.CurrentMap;
            IntVec3 origin = UI.MouseCell();
            CellRect footprint = new CellRect(origin.x, origin.z, Width, Height);
            if (!footprint.InBounds(map))
            {
                Messages.Message("Demo stage does not fit here — click further from the map edge.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
            if (standDef == null)
            {
                Messages.Message("Odyssey outfit stand def not found — cannot build the demo stage.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            GenDebug.ClearArea(footprint, map);
            BuildShell(map, origin);

            CellRect kitchen = RoomRect(origin, left: true, top: false);
            CellRect dining = RoomRect(origin, left: false, top: false);
            CellRect hospital = RoomRect(origin, left: true, top: true);
            CellRect lab = RoomRect(origin, left: false, top: true);

            Building_Bed bed = BuildHospital(map, hospital, SpawnStand(map, standDef, hospital, doorNorth: false));
            BuildLaboratory(map, lab, SpawnStand(map, standDef, lab, doorNorth: false));
            BuildKitchen(map, kitchen, SpawnStand(map, standDef, kitchen, doorNorth: true));
            BuildDining(map, dining);

            SpawnStaff(map, origin);
            SpawnPatient(map, bed);
            StartResearch();

            Messages.Message("Shift Change demo stage built — unpause and the take starts: everyone "
                + "changes and works, the chef delivers to the dining shelf in uniform, then doctor "
                + "and chef change out and go eat.", MessageTypeDefOf.TaskCompletion, historical: false);
        }

        internal static CellRect RoomRect(IntVec3 origin, bool left, bool top)
        {
            int x = origin.x + (left ? 1 : RoomInterior + 2);
            int z = origin.z + (top ? RoomInterior + CorridorHeight + 3 : 1);
            return new CellRect(x, z, RoomInterior, RoomInterior);
        }

        /// <summary>
        /// Plank floor, roof, walls and doors. Rows from the south edge: wall, bottom interiors,
        /// wall, corridor (two rows), wall, top interiors, wall. A middle wall column splits each
        /// room row and the corridor runs the full width. Doors lead from each room onto the
        /// corridor, and one joins kitchen and dining through the middle wall.
        /// </summary>
        internal static void BuildShell(Map map, IntVec3 origin)
        {
            var doors = new List<IntVec3>
            {
                origin + new IntVec3(3, 0, RoomInterior + CorridorHeight + 2),  // hospital → corridor
                origin + new IntVec3(9, 0, RoomInterior + CorridorHeight + 2),  // lab → corridor
                origin + new IntVec3(3, 0, RoomInterior + 1),                   // kitchen → corridor
                origin + new IntVec3(9, 0, RoomInterior + 1),                   // dining → corridor
                origin + new IntVec3(RoomInterior + 1, 0, 3),                   // kitchen ↔ dining
            };

            TerrainDef plank = DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor");
            int corridorLow = RoomInterior + 2;
            int corridorHigh = corridorLow + CorridorHeight - 1;

            for (int rx = 0; rx < Width; rx++)
            {
                for (int rz = 0; rz < Height; rz++)
                {
                    IntVec3 cell = origin + new IntVec3(rx, 0, rz);
                    if (plank != null)
                    {
                        map.terrainGrid.SetTerrain(cell, plank);
                    }
                    map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);

                    bool corridorRow = rz >= corridorLow && rz <= corridorHigh;
                    bool wall = rx == 0 || rx == Width - 1 || rz == 0 || rz == Height - 1
                        || rz == RoomInterior + 1 || rz == RoomInterior + CorridorHeight + 2
                        || (rx == RoomInterior + 1 && !corridorRow);
                    if (!wall)
                    {
                        continue;
                    }
                    if (doors.Contains(cell))
                    {
                        Spawn(map, ThingDefOf.Door, ThingDefOf.WoodLog, cell, Rot4.North);
                    }
                    else
                    {
                        Spawn(map, ThingDefOf.Wall, ThingDefOf.WoodLog, cell, Rot4.North);
                    }
                }
            }
        }

        internal static void Carpet(Map map, CellRect interior, string carpetDefName)
        {
            TerrainDef carpet = DefDatabase<TerrainDef>.GetNamedSilentFail(carpetDefName);
            if (carpet == null)
            {
                return;
            }
            foreach (IntVec3 cell in interior)
            {
                map.terrainGrid.SetTerrain(cell, carpet);
            }
        }

        /// <summary>
        /// The stand goes in the interior's door-side corner (minX), so changing is the first and
        /// last thing that happens in the room: the south row for the top rooms, whose doors are
        /// on their south side, and the north row (<paramref name="doorNorth"/>) for the kitchen.
        /// </summary>
        internal static Building_OutfitStand SpawnStand(Map map, ThingDef standDef, CellRect interior, bool doorNorth)
        {
            int z = doorNorth ? interior.maxZ : interior.minZ;
            return (Building_OutfitStand)Spawn(map, standDef, ThingDefOf.WoodLog,
                new IntVec3(interior.minX, 0, z), Rot4.South);
        }

        internal static Building_Bed BuildHospital(Map map, CellRect interior, Building_OutfitStand stand)
        {
            Carpet(map, interior, "CarpetRed");
            SpawnTorch(map, new IntVec3(interior.maxX, 0, interior.minZ));

            Building_Bed first = null;
            for (int i = 0; i < 2; i++)
            {
                Building_Bed bedBuilding = (Building_Bed)Spawn(map, ThingDefOf.Bed, ThingDefOf.WoodLog,
                    new IntVec3(interior.minX + 1 + i * 2, 0, interior.minZ + 3), Rot4.South);
                bedBuilding.Medical = true;
                first = first ?? bedBuilding;
            }

            // Each anesthetize consumes one medicine, and the fetch trips are what keep the
            // doctor visibly working while the chef cooks.
            ThingDef medicine = DefDatabase<ThingDef>.GetNamedSilentFail("MedicineHerbal");
            if (medicine != null)
            {
                Thing stack = ThingMaker.MakeThing(medicine);
                stack.stackCount = 10;
                GenPlace.TryPlaceThing(stack, new IntVec3(interior.minX, 0, interior.maxZ), map, ThingPlaceMode.Near);
            }

            Stock(stand,
                new[] { "VAE_Apparel_DoctorScrubs", "VAE_Headgear_SurgicalMask" },
                new Color(0.92f, 0.96f, 1f));
            return first;
        }

        internal static void BuildLaboratory(Map map, CellRect interior, Building_OutfitStand stand)
        {
            Carpet(map, interior, "CarpetGreen");
            SpawnTorch(map, new IntVec3(interior.maxX, 0, interior.minZ));
            // A simple research bench is 3x2; facing north it covers its Position and the row
            // north of it, so Position one row in from the north wall puts its back against the
            // wall, and Position on the last row would push it into the wall. Its interaction
            // cell is one row south of Position.
            Spawn(map, ThingDefOf.SimpleResearchBench, ThingDefOf.WoodLog,
                new IntVec3(interior.minX + 2, 0, interior.maxZ - 1), Rot4.North);
            Stock(stand, new[] { "VAE_Apparel_LabCoat" }, new Color(0.62f, 0.9f, 0.7f));
        }

        /// <summary>
        /// The stove sits opposite the door, so the chef meets the stand on arrival and the walk
        /// to the work is visible. <paramref name="stoveNorth"/> puts it at the room's north end,
        /// for a kitchen with a south door. The demo stage's kitchen has a north door and takes
        /// the default; <see cref="DebugTools_PreviewStage"/>'s has a south door and passes true.
        /// </summary>
        internal static void BuildKitchen(Map map, CellRect interior, Building_OutfitStand stand,
            bool stoveNorth = false)
        {
            Carpet(map, interior, "CarpetBlue");
            SpawnTorch(map, new IntVec3(interior.maxX, 0, interior.minZ));

            // The stove is 3x1 with drawSize (3.5,1.5), so its art overdraws the footprint on
            // every side; at the north end it sits a row in, leaving a free row between it and
            // the wall, to stay off it. Its interactionCellOffset is (0,0,-1), so rotation North
            // puts the chef south of it and South puts them north, inside the room.
            ThingDef stoveDef = DefDatabase<ThingDef>.GetNamedSilentFail("FueledStove");
            if (stoveDef != null)
            {
                Thing stove = Spawn(map, stoveDef, ThingDefOf.WoodLog,
                    new IntVec3(interior.minX + 2, 0, stoveNorth ? interior.maxZ - 1 : interior.minZ),
                    stoveNorth ? Rot4.North : Rot4.South);
                CompRefuelable fuel = stove.TryGetComp<CompRefuelable>();
                fuel?.Refuel(fuel.Props.fuelCapacity);

                // One bulk batch (CookMealSimpleBulk, four meals). repeatCount defaults to 1, so
                // the bill completes and the chef's shift ends.
                RecipeDef cook = DefDatabase<RecipeDef>.GetNamedSilentFail("CookMealSimpleBulk");
                if (cook != null && stove is Building_WorkTable table)
                {
                    table.billStack.AddBill(cook.MakeNewBill());
                }
            }

            // Exactly one batch of ingredients: the bulk recipe wants 2.0 nutrition of raw food
            // and a potato is 0.05, so forty, none spare.
            ThingDef potatoes = DefDatabase<ThingDef>.GetNamedSilentFail("RawPotatoes");
            if (potatoes != null)
            {
                Thing stack = ThingMaker.MakeThing(potatoes);
                stack.stackCount = 40;
                // Beside the stove, wherever it ended up: the chef fetches them as part of the
                // bill, and a fetch across the room is dead time in a take.
                GenPlace.TryPlaceThing(stack,
                    new IntVec3(interior.maxX, 0, stoveNorth ? interior.maxZ - 1 : interior.minZ + 1),
                    map, ThingPlaceMode.Near);
            }

            Stock(stand,
                new[] { "VAE_Apparel_ChefsUniform", "VAE_Headgear_ChefsToque" },
                new Color(0.95f, 0.8f, 0.35f));
        }

        /// <summary>
        /// The anchor for the script's ending: a shelf that accepts only meals, so the chef's
        /// delivery lands here, and a table and chairs, so going to eat has a destination. No
        /// stand: the room's role is DiningRoom, where the mod is inert.
        /// </summary>
        internal static void BuildDining(Map map, CellRect interior)
        {
            SpawnTorch(map, new IntVec3(interior.maxX, 0, interior.minZ));

            ThingDef shelfDef = DefDatabase<ThingDef>.GetNamedSilentFail("ShelfSmall");
            if (shelfDef != null)
            {
                Thing shelf = Spawn(map, shelfDef, ThingDefOf.WoodLog,
                    new IntVec3(interior.maxX, 0, interior.maxZ), Rot4.South);
                ThingCategoryDef meals = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("FoodMeals");
                if (shelf is Building_Storage storage && meals != null)
                {
                    StorageSettings settings = storage.GetStoreSettings();
                    settings.filter.SetDisallowAll();
                    settings.filter.SetAllow(meals, allow: true);
                    settings.Priority = StoragePriority.Important;
                }
            }

            ThingDef tableDef = DefDatabase<ThingDef>.GetNamedSilentFail("Table2x2c");
            if (tableDef != null)
            {
                Spawn(map, tableDef, ThingDefOf.WoodLog,
                    new IntVec3(interior.minX + 1, 0, interior.minZ + 1), Rot4.North);
            }
            ThingDef chairDef = DefDatabase<ThingDef>.GetNamedSilentFail("DiningChair");
            if (chairDef != null)
            {
                // One chair on the west side: the kitchen-dining door opens into
                // (minX, minZ + 2), and a chair there would block it.
                Spawn(map, chairDef, ThingDefOf.WoodLog, new IntVec3(interior.minX, 0, interior.minZ + 1), Rot4.East);
                Spawn(map, chairDef, ThingDefOf.WoodLog, new IntVec3(interior.minX + 3, 0, interior.minZ + 1), Rot4.West);
                Spawn(map, chairDef, ThingDefOf.WoodLog, new IntVec3(interior.minX + 3, 0, interior.minZ + 2), Rot4.West);
            }
        }

        /// <summary>
        /// Three pure specialists spawned in the corridor, each with only their specialty at
        /// priority 1 and all other work at 0, so nothing (hauling included) competes with the
        /// script. Manual priorities are forced on in the play settings: with vanilla's toggle
        /// off, priorities collapse to checkboxes and pawns stop beelining for their specialty.
        /// </summary>
        internal static void SpawnStaff(Map map, IntVec3 origin)
        {
            Current.Game.playSettings.useWorkPriorities = true;

            // Each specialist starts in the corridor just outside their room's door, so the take
            // opens with three simultaneous entries. Skill 20 in the specialty keeps it moving: a
            // bulk meal at low Cooking takes forever, and a level-20 doctor will not botch a
            // surgery into an on-camera bleeding emergency.
            (string workDefName, string skillDefName, string nick, Gender gender, float food, IntVec3 offset)[] staff =
            {
                ("Doctor", "Medicine", "Doc", Gender.Male, DoctorFood, new IntVec3(3, 0, RoomInterior + CorridorHeight + 1)),
                ("Research", "Intellectual", "Lab", Gender.Female, 1f, new IntVec3(9, 0, RoomInterior + CorridorHeight + 1)),
                ("Cooking", "Cooking", "Chef", Gender.Male, ChefFood, new IntVec3(3, 0, RoomInterior + 2)),
            };

            for (int i = 0; i < staff.Length; i++)
            {
                IntVec3 cell = origin + staff[i].offset;
                if (!cell.InBounds(map))
                {
                    continue;
                }
                WorkTypeDef specialty = DefDatabase<WorkTypeDef>.GetNamedSilentFail(staff[i].workDefName);
                Pawn pawn = AveragePawn(staff[i].gender, staff[i].nick, specialty);
                DressInStartingKit(pawn, staff[i].workDefName == "Research");
                GenSpawn.Spawn(pawn, cell, map);
                pawn.workSettings.EnableAndInitialize();
                foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                {
                    if (work.visible && !pawn.WorkTypeIsDisabled(work))
                    {
                        pawn.workSettings.SetPriority(work, work == specialty ? 1 : 0);
                    }
                }
                SkillDef skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(staff[i].skillDefName);
                if (skillDef != null)
                {
                    SkillRecord record = pawn.skills?.GetSkill(skillDef);
                    if (record != null)
                    {
                        record.Level = 20;
                    }
                }
                TopUpNeeds(pawn, staff[i].food);
            }
        }

        /// <summary>
        /// A colonist downed without bleeding wounds, laid in the medical bed, with several
        /// anesthetize surgeries queued. Non-bleeding matters: a bleeding patient arrives as
        /// DoctorTendEmergency, which the mod does not change first for by default, and the
        /// doctor would sprint over in civvies. Queued surgeries are ordinary Doctor work, which
        /// waits indefinitely, so they are the case that scrubs in on camera.
        /// </summary>
        internal static void SpawnPatient(Map map, Building_Bed bed)
        {
            if (bed == null)
            {
                return;
            }
            Pawn patient = AveragePawn(Gender.Female, "Patient");
            GenSpawn.Spawn(patient, bed.Position, map);
            HealthUtility.DamageUntilDowned(patient, allowBleedingWounds: false);

            RecipeDef surgery = DefDatabase<RecipeDef>.GetNamedSilentFail("Anesthetize");
            if (surgery != null)
            {
                for (int i = 0; i < SurgeryBills; i++)
                {
                    patient.health.surgeryBills.AddBill(new Bill_Medical(surgery, null));
                }
            }
        }

        /// <summary>
        /// Starts the first startable research project when none is active, so the researcher
        /// has a bench job waiting and a colony's own choice is left alone.
        /// </summary>
        internal static void StartResearch()
        {
            if (Find.ResearchManager.GetProject() != null)
            {
                return;
            }
            List<ResearchProjectDef> all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].CanStartNow)
                {
                    Find.ResearchManager.SetCurrentProject(all[i]);
                    return;
                }
            }
        }

        internal static void TopUpNeeds(Pawn pawn, float foodLevel)
        {
            List<Need> needs = pawn.needs?.AllNeeds;
            if (needs == null)
            {
                return;
            }
            for (int i = 0; i < needs.Count; i++)
            {
                needs[i].CurLevel = needs[i].MaxLevel;
            }
            if (pawn.needs.food != null)
            {
                pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel * foodLevel;
            }
        }

        /// <summary>
        /// Torch lamps, deliberately: swapping in standing lamps to shrink the footage gif
        /// backfires. The torch's <c>CompProperties_FireOverlay</c> animates its flame every
        /// frame, which looks costly but is not: over the same ten seconds at the same encoder
        /// settings a torch take came to 1.6 MB and a standing-lamp take to 4.1 MB, with the same
        /// mean inter-frame delta. What costs is the lamp's larger, smoother glow (glowRadius 12
        /// against the torch's 10), a gradient the gif palette bands and then re-bands every
        /// frame. Steady light for a look reason is a different argument, and costs about 2.5
        /// times the gif.
        /// </summary>
        internal static void SpawnTorch(Map map, IntVec3 cell)
        {
            Thing torch = Spawn(map, ThingDefOf.TorchLamp, null, cell, Rot4.North);
            CompRefuelable fuel = torch.TryGetComp<CompRefuelable>();
            fuel?.Refuel(fuel.Props.fuelCapacity);
        }
    }
}
#endif
