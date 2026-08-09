using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

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
    /// Ships in Release ON PURPOSE: footage is filmed on live builds (see
    /// CLAUDE.md's live/lab discipline). Dev mode gates its visibility, and
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

        [DebugAction("Shift Change", "Build demo stage",
            actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap,
            requiresOdyssey = true)]
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

        internal static void BuildKitchen(Map map, CellRect interior, Building_OutfitStand stand)
        {
            Carpet(map, interior, "CarpetBlue");
            SpawnTorch(map, new IntVec3(interior.maxX, 0, interior.minZ));

            // South wall, facing north into the room (the kitchen's door is
            // on its NORTH wall, so the stand meets the chef at the door and
            // the stove sits deep in the room).
            ThingDef stoveDef = DefDatabase<ThingDef>.GetNamedSilentFail("FueledStove");
            if (stoveDef != null)
            {
                Thing stove = Spawn(map, stoveDef, ThingDefOf.WoodLog,
                    new IntVec3(interior.minX + 2, 0, interior.minZ), Rot4.South);
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
                GenPlace.TryPlaceThing(stack, new IntVec3(interior.maxX, 0, interior.minZ + 1), map, ThingPlaceMode.Near);
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
        /// The room's work gear from Vanilla Apparel Expanded when its defs
        /// are loaded; a colour-tinted vanilla outfit otherwise, so the swap
        /// still reads on camera without the apparel mod.
        /// </summary>
        internal static void Stock(Building_OutfitStand stand, string[] preferredDefNames, Color fallbackTint)
        {
            bool stockedAny = false;
            foreach (string defName in preferredDefNames)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null)
                {
                    continue;
                }
                stand.AddApparel(MakeGarment(def, null));
                stockedAny = true;
            }
            if (stockedAny)
            {
                return;
            }
            foreach (string defName in new[] { "Apparel_BasicShirt", "Apparel_Pants" })
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null)
                {
                    continue;
                }
                stand.AddApparel(MakeGarment(def, fallbackTint));
            }
        }

        /// <summary>
        /// Cloth for anything stuffable, default sprite always — staged
        /// garments should read identically take after take. The explicit
        /// SetColor matters: CompColorable.Initialize rolls the def's
        /// colorGenerator (random clothing colours — the pink chef's
        /// uniform), so "default" has to be imposed.
        /// </summary>
        internal static Apparel MakeGarment(ThingDef def, Color? tint)
        {
            ThingDef stuff = def.MadeFromStuff ? ThingDefOf.Cloth : null;
            Apparel garment = (Apparel)ThingMaker.MakeThing(def, stuff);
            garment.SetStyleDef(null);
            garment.overrideGraphicIndex = 0;
            garment.TryGetComp<CompColorable>()?.SetColor(
                tint ?? (stuff != null ? stuff.stuffProps.color : Color.white));
            return garment;
        }

        /// <summary>
        /// Strips generated apparel and dresses the pawn in one cloth tunic.
        /// PawnGenerator rolls random colonist clothing, which both muddies
        /// the take (a chef can spawn already wearing a toque in whatever
        /// fabric rolled) and pads the change-in with one removal delay per
        /// displaced garment — shirt plus pants cost 3.5s on top of the
        /// uniform. A tunic is a single OnSkin torso piece: the lab coat
        /// (Shell layer) goes straight over it, so the researcher changes in
        /// 1.0s, while scrubs and chef's whites displace exactly this one
        /// 1.5s garment (doctor 3.3s, chef 4.8s).
        /// </summary>
        internal static void DressInTunic(Pawn pawn)
        {
            if (pawn.apparel == null)
            {
                return;
            }
            pawn.apparel.DestroyAll();
            ThingDef tunic = DefDatabase<ThingDef>.GetNamedSilentFail("VAE_Apparel_Tunic")
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_TribalA");
            if (tunic != null)
            {
                pawn.apparel.Wear(MakeGarment(tunic, null));
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
                DressInTunic(pawn);
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
        /// </summary>
        internal static void StartResearch()
        {
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

        /// <summary>
        /// John and Jane Q. Pawn: a 30-year-old baseliner with a standard
        /// body, natural hair, no beard, no tattoos, NO TRAITS (a randomly
        /// rolled Nudist or Pyromaniac hijacks a take), and a role nickname
        /// so the on-screen label reads Doc / Lab / Chef / Patient.
        ///
        /// Traits are cleared, but BACKSTORIES are rolled and kept — and a
        /// backstory can disable a work type outright. A chef whose roll
        /// disabled Cooking gets priority 0 in everything (SpawnStaff zeroes
        /// all non-specialty work) and wanders the set for the whole take
        /// (found in play, 2026-08-08). So reroll until the specialty is
        /// enabled; relations are off so the discards leave nothing behind.
        /// </summary>
        internal static Pawn AveragePawn(Gender gender, string nick, WorkTypeDef mustBeCapableOf = null)
        {
            PawnGenerationRequest request = new PawnGenerationRequest(
                PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                fixedBiologicalAge: 30f, fixedChronologicalAge: 30f,
                fixedGender: gender);
            XenotypeDef baseliner = DefDatabase<XenotypeDef>.GetNamedSilentFail("Baseliner");
            if (baseliner != null)
            {
                request.ForcedXenotype = baseliner;
            }
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            for (int tries = 0;
                 mustBeCapableOf != null && pawn.WorkTypeIsDisabled(mustBeCapableOf) && tries < 30;
                 tries++)
            {
                pawn.Destroy();
                pawn = PawnGenerator.GeneratePawn(request);
            }

            pawn.Name = new NameTriple(gender == Gender.Female ? "Jane" : "John", nick, "Doe");
            pawn.story.traits.allTraits.Clear();
            pawn.story.HairColor = new Color(0.35f, 0.24f, 0.15f);
            pawn.story.bodyType = gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male;
            if (pawn.style != null)
            {
                pawn.style.beardDef = DefDatabase<BeardDef>.GetNamedSilentFail("NoBeard");
                pawn.style.FaceTattoo = DefDatabase<TattooDef>.GetNamedSilentFail("NoTattoo_Face");
                pawn.style.BodyTattoo = DefDatabase<TattooDef>.GetNamedSilentFail("NoTattoo_Body");
            }
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            return pawn;
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

        internal static void SpawnTorch(Map map, IntVec3 cell)
        {
            Thing torch = Spawn(map, ThingDefOf.TorchLamp, null, cell, Rot4.North);
            CompRefuelable fuel = torch.TryGetComp<CompRefuelable>();
            fuel?.Refuel(fuel.Props.fuelCapacity);
        }

        internal static Thing Spawn(Map map, ThingDef def, ThingDef stuff, IntVec3 cell, Rot4 rot)
        {
            Thing thing = ThingMaker.MakeThing(def, stuff);
            thing.SetFactionDirect(Faction.OfPlayer);
            // A consistent set: no ideology styling, no random graphic
            // variants — every torch, chair and table is the default sprite.
            thing.SetStyleDef(null);
            thing.overrideGraphicIndex = 0;
            Thing spawned = GenSpawn.Spawn(thing, cell, map, rot);
            // …and clear AGAIN after spawning: vanilla's torch is a
            // Graphic_Single with exactly one graphic, so the mixed torches
            // in play could only come from a restyling mod on the modlist
            // applying at SpawnSetup — after the pre-spawn clear. Post-spawn
            // wins, and Notify_ColorChanged drops the cached graphic.
            spawned.SetStyleDef(null);
            spawned.overrideGraphicIndex = 0;
            spawned.Notify_ColorChanged();
            return spawned;
        }
    }
}
