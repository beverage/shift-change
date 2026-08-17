// SCENES only — see the config table in ShiftChange.csproj. This file BUILDS
// A SCENE and must never reach a player: it clears a 13x16 footprint (which
// destroys any pawn standing in it), then leaves permanent player-faction
// colonists, buildings and terrain behind. The fixture primitives it is built
// from live in DebugTools_Fixtures, which always compiles because the harness
// needs them in Release.
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
    /// One-click film set and test fixture: dev mode → Shift Change → Build
    /// demo stage, then click a map cell. Builds a 2×2 complex — hospital and
    /// lab on top, kitchen and dining room below, a corridor between the rows,
    /// kitchen and dining directly connected — roofed, lit, colour-coded
    /// carpet per work room (red/green/blue), stands stocked with Vanilla
    /// Apparel Expanded work gear when that mod is loaded (tinted vanilla
    /// clothes otherwise), three specialists in plain tunics, a surgery
    /// patient, an active
    /// research project, one bulk kitchen bill and exactly one batch of
    /// ingredients.
    ///
    /// The intended take, from unpause: all three change and start work; the
    /// chef cooks 4 meals and delivers them to the dining shelf IN UNIFORM
    /// (the carry is the tail of the same bill job — no new job, no return
    /// trip), then walks back, changes out and goes to eat; the doctor works
    /// through three queued surgeries, changes out, eats; the researcher
    /// stays. Ordering between doctor and chef is approximate — the knob is
    /// the surgery-bill count and the starting food levels below.
    ///
    /// Ships in Release ON PURPOSE: footage is filmed on live builds (see the
    /// live/lab discipline in docs/DEVELOPMENT.md). Dev mode gates it, and
    /// requiresOdyssey hides it exactly where the mod itself is inert.
    /// </summary>
    internal static class DebugTools_DemoStage
    {
        internal const int RoomInterior = 5;
        internal const int CorridorHeight = 2;
        internal const int Width = RoomInterior * 2 + 3;
        internal const int Height = RoomInterior * 2 + CorridorHeight + 4;

        // The script's timing knobs. Food sits above the hungry threshold so
        // nobody eats before working, and low enough that the doctor and chef
        // seek the cooked meals soon after finishing. The researcher starts
        // full and simply keeps working.
        internal const float DoctorFood = 0.30f;
        internal const float ChefFood = 0.33f;
        internal const int SurgeryBills = 3;

        /// <summary>
        /// Reached from the "Dev tools..." submenu
        /// (<see cref="DebugTools_Menu"/>), which carries the game-state and
        /// Odyssey gating this method used to declare itself.
        /// </summary>
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
        /// Walls, doors, floors, carpets, roof. Layout, in relative rows from
        /// the south edge: wall / bottom interiors / wall / corridor ×2 /
        /// wall / top interiors / wall. A middle wall column splits each room
        /// row; the corridor runs the full width. Doors: each room onto the
        /// corridor, plus kitchen↔dining through the middle wall.
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
        /// The stand goes on the room's DOOR side, so changing is the first
        /// and last thing that happens in the room: top rooms' doors face the
        /// corridor to their south, the kitchen's faces the corridor to its
        /// north.
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

            // Each anesthetize consumes one medicine, and the fetch trips are
            // what keep the doctor visibly working while the chef cooks.
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
            // One row in from the north wall — the bench's art overdraws
            // north past its footprint, so flush against the wall it draws
            // ON the wall. Interaction cell lands one further south.
            Spawn(map, ThingDefOf.SimpleResearchBench, ThingDefOf.WoodLog,
                new IntVec3(interior.minX + 2, 0, interior.maxZ - 1), Rot4.North);
            Stock(stand, new[] { "VAE_Apparel_LabCoat" }, new Color(0.62f, 0.9f, 0.7f));
        }

        /// <summary>
        /// <paramref name="stoveNorth"/> flips the stove to the room's north
        /// end, for a kitchen whose door is on the SOUTH wall. The rule is
        /// that the stove sits opposite the door, so the stand meets the chef
        /// as they arrive and the walk to the work is visible rather than
        /// happening in one corner. The demo stage's kitchen is a bottom room
        /// with a north door and takes the default; the preview stage's is
        /// standalone with a south door and passes true.
        /// </summary>
        internal static void BuildKitchen(Map map, CellRect interior, Building_OutfitStand stand,
            bool stoveNorth = false)
        {
            Carpet(map, interior, "CarpetBlue");
            SpawnTorch(map, new IntVec3(interior.maxX, 0, interior.minZ));

            // The stove is 3x1 with drawSize (3.5,1.5), so it overdraws its
            // footprint on every side. At the north end that means one row in
            // from the wall, exactly as the research bench needs — flush, it
            // draws onto the wall. Rotation follows: interactionCellOffset is
            // (0,0,-1), so North puts the chef south of the stove and South
            // puts them north of it. Either way they stand inside the room.
            ThingDef stoveDef = DefDatabase<ThingDef>.GetNamedSilentFail("FueledStove");
            if (stoveDef != null)
            {
                Thing stove = Spawn(map, stoveDef, ThingDefOf.WoodLog,
                    new IntVec3(interior.minX + 2, 0, stoveNorth ? interior.maxZ - 1 : interior.minZ),
                    stoveNorth ? Rot4.North : Rot4.South);
                CompRefuelable fuel = stove.TryGetComp<CompRefuelable>();
                fuel?.Refuel(fuel.Props.fuelCapacity);

                // ONE bulk batch (vanilla CookMealSimpleBulk, ×4 meals):
                // repeatCount defaults to 1, so the bill completes and the
                // chef's shift genuinely ends.
                RecipeDef cook = DefDatabase<RecipeDef>.GetNamedSilentFail("CookMealSimpleBulk");
                if (cook != null && stove is Building_WorkTable table)
                {
                    table.billStack.AddBill(cook.MakeNewBill());
                }
            }

            // Exactly one batch of ingredients: the bulk recipe wants 2.0
            // nutrition of raw food and a potato is 0.05 — forty, no spares.
            ThingDef potatoes = DefDatabase<ThingDef>.GetNamedSilentFail("RawPotatoes");
            if (potatoes != null)
            {
                Thing stack = ThingMaker.MakeThing(potatoes);
                stack.stackCount = 40;
                // Beside the stove, wherever the stove ended up: the chef
                // fetches them as part of the bill, and a fetch across the
                // whole room is dead time in the middle of a take.
                GenPlace.TryPlaceThing(stack,
                    new IntVec3(interior.maxX, 0, stoveNorth ? interior.maxZ - 1 : interior.minZ + 1),
                    map, ThingPlaceMode.Near);
            }

            Stock(stand,
                new[] { "VAE_Apparel_ChefsUniform", "VAE_Headgear_ChefsToque" },
                new Color(0.95f, 0.8f, 0.35f));
        }

        /// <summary>
        /// The anchor for the script's ending: a shelf that accepts only
        /// meals (so the chef's delivery lands here), a table and chairs (so
        /// "go eat" has a destination). No stand — the room's role is
        /// DiningRoom and the mod is inert in it.
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
                // West side seats one chair only: the kitchen↔dining door
                // opens into (minX, minZ + 2), and a chair there blocks it.
                Spawn(map, chairDef, ThingDefOf.WoodLog, new IntVec3(interior.minX, 0, interior.minZ + 1), Rot4.East);
                Spawn(map, chairDef, ThingDefOf.WoodLog, new IntVec3(interior.minX + 3, 0, interior.minZ + 1), Rot4.West);
                Spawn(map, chairDef, ThingDefOf.WoodLog, new IntVec3(interior.minX + 3, 0, interior.minZ + 2), Rot4.West);
            }
        }

        /// <summary>
        /// Three pure specialists spawned in the corridor: only their
        /// specialty enabled, at priority 1, so nothing (hauling included)
        /// competes with the script. Manual priorities are forced ON — with
        /// the vanilla toggle off, all priorities collapse to checkboxes and
        /// the beeline disappears, which needs no mod to fix.
        /// </summary>
        internal static void SpawnStaff(Map map, IntVec3 origin)
        {
            Current.Game.playSettings.useWorkPriorities = true;

            // Each specialist starts in the corridor directly outside their
            // room's door, so the take opens with three simultaneous entries.
            // Skill 20 in the specialty keeps the take moving — a bulk meal
            // at low Cooking takes forever, and a level-20 Medicine doctor
            // will not botch a surgery into an on-camera bleeding emergency.
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
        /// A colonist downed WITHOUT bleeding wounds, laid in the medical bed,
        /// with several anesthetize surgeries queued. Non-bleeding matters: a
        /// bleeding patient arrives as DoctorTendEmergency, which the mod
        /// deliberately never diverts for — the doctor would sprint over in
        /// civvies. Queued surgeries are ordinary Doctor work: they wait
        /// indefinitely and are exactly the case that scrubs in on camera.
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
        /// Picks the first startable research project if none is active, so
        /// the researcher has a bench job waiting.
        ///
        /// The "if none is active" guard was described here but never written
        /// (found 2026-08-17): this used to call SetCurrentProject
        /// unconditionally, silently swapping whatever the colony was already
        /// researching for the first startable project in def order. Progress
        /// survives — ResearchManager keeps a separate progress table and
        /// SetCurrentProject only reassigns currentProj — but the player's
        /// choice did not. Now it matches its own contract.
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
        /// Torch lamps, and they are the RIGHT choice for filming — which is
        /// worth stating, because the obvious reasoning says otherwise and we
        /// followed it once already.
        ///
        /// <c>TorchLamp</c> carries a <c>CompProperties_FireOverlay</c>, so
        /// each flame animates every frame; a gif encodes frame-to-frame
        /// deltas, so swapping to steady <c>StandingLamp</c>s looks like an
        /// obvious win. It was tried on 2026-08-12 and measured, same 10s
        /// window, same encoder settings, same 200 frames:
        ///
        ///   torch take 1.6 MB   standing-lamp take 4.1 MB
        ///
        /// Mean inter-frame delta was IDENTICAL (34.0) — the motion is the
        /// same and the flames cost almost nothing. What costs is
        /// QUANTISATION: a torch throws a small warm pool, a standing lamp
        /// (<c>glowRadius</c> 12 against the torch's 9) throws a large smooth
        /// GRADIENT, and a palette has to band a gradient and then re-band it
        /// every frame as encoder noise pushes pixels over band boundaries.
        /// Corroborated both ways — fewer colours helped (48 → 3.1 MB),
        /// dithering hurt (bayer → 4.5 MB), which is what band churn predicts
        /// and the opposite of what flame animation would.
        ///
        /// So: do not swap these for lamps to "help compression". If a future
        /// take wants steady light for a LOOK reason, that is a different
        /// argument and it costs roughly 2.5x the gif.
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
