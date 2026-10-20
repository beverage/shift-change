// SCENES only, see the config table in ShiftChange.csproj. Like the demo stage, this file builds
// a scene and must never reach a player: it clears a 32x10 footprint (destroying any pawn in
// it), rewrites the terrain and roof under the three blocks, and leaves three permanent
// player-faction colonists and three roomfuls of owned buildings behind.
#if SCENES
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// The title-card stage: three standalone rooms (hospital, laboratory, kitchen), each with
    /// its own corridor and worker, sized so a screenshot of any one crops to half a Workshop
    /// preview. A preview is 640×360 and a card is a before/after pair, so a panel is 320×360
    /// (8:9) and each block's capture rectangle is 8 tiles across (wall, 6 interior, wall) by 9
    /// down (2 corridor, wall, 5 interior, wall).
    ///
    /// <para>The far corridor wall sits one row below the capture rectangle, out of shot. It
    /// still has to be enclosed and roofed, but leaving it out puts the panel's bottom edge on
    /// the floor-to-wall line instead of cutting through anything. Blocks are
    /// <see cref="Gap"/> tiles of open ground apart, so a crop of one cannot catch a
    /// neighbour's wall. All three run at once, so one locked-off recording yields all six
    /// stills with identical framing.</para>
    ///
    /// <para>This is not the demo stage with different numbers: that layout is a test fixture
    /// whose play observations have to stay comparable, so it is not resized for screenshots.
    /// This stage reuses its room dressing and changes only the shell.</para>
    ///
    /// <para>The sleep card stage (<see cref="DebugTools_SleepStage"/>) reuses
    /// <see cref="BuildShell"/>, <see cref="InteriorOf"/>, <see cref="BlockWidth"/>,
    /// <see cref="BlockHeight"/> and <see cref="DoorX"/>, and
    /// <c>devtools/bridge/shoot-sleep-card.py</c> hard-codes the block size, so a geometry
    /// change has to reach that script by hand.</para>
    /// </summary>
    internal static class DebugTools_PreviewStage
    {
        internal const int RoomWidth = 6;
        internal const int RoomHeight = 5;
        internal const int CorridorHeight = 2;

        /// <summary>One block's capture rectangle: 8 × 9 tiles, a 320×360 panel.</summary>
        internal const int BlockWidth = RoomWidth + 2;
        internal const int ShotHeight = RoomHeight + CorridorHeight + 2;

        /// <summary>One row taller than the shot: the corridor's far wall.</summary>
        internal const int BlockHeight = ShotHeight + 1;

        /// <summary>Open ground between blocks, so a crop slightly off catches grass.</summary>
        internal const int Gap = 4;

        internal const int Rooms = 3;
        internal const int Pitch = BlockWidth + Gap;
        internal const int Width = Rooms * BlockWidth + (Rooms - 1) * Gap;

        /// <summary>
        /// Door column, relative to a block's origin: interior column <c>minX + 3</c>. All three
        /// rooms share it or the cards stop looking like a set, so the interior cell just inside
        /// the door has to stay free in each (the stand is at <c>minX</c>, a torch at
        /// <c>maxX</c>, and the kitchen's stove goes to the north end, see <see cref="Dress"/>).
        /// </summary>
        internal const int DoorX = 4;

        /// <summary>
        /// Registered from <see cref="DebugTools_Menu"/>, which gates it to Odyssey on a map.
        /// </summary>
        internal static void BuildPreviewStage()
        {
            Map map = Find.CurrentMap;
            IntVec3 origin = UI.MouseCell();
            CellRect footprint = new CellRect(origin.x, origin.z, Width, BlockHeight);
            if (!footprint.InBounds(map))
            {
                Messages.Message("Preview stage does not fit here — it is "
                    + Width + " tiles wide; click further from the map edge.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
            if (standDef == null)
            {
                Messages.Message("Odyssey outfit stand def not found — cannot build the preview stage.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            GenDebug.ClearArea(footprint, map);
            Current.Game.playSettings.useWorkPriorities = true;

            for (int room = 0; room < Rooms; room++)
            {
                IntVec3 block = origin + new IntVec3(room * Pitch, 0, 0);
                BuildShell(map, block);

                CellRect interior = InteriorOf(block);
                Building_OutfitStand stand =
                    DebugTools_DemoStage.SpawnStand(map, standDef, interior, doorNorth: false);

                // Lights the corridor, where an arriving pawn is dark on dark otherwise. East
                // end, clear of the worker.
                DebugTools_DemoStage.SpawnTorch(map,
                    block + new IntVec3(BlockWidth - 2, 0, CorridorHeight));

                Dress(map, room, interior, stand);
                SpawnWorker(map, block, room);
            }

            DebugTools_DemoStage.StartResearch();

            Messages.Message("Shift Change preview stage built — three rooms, three workers, one take. "
                + "Unpause and record: each walks in, changes, and works. Crop 8×9 tiles per room from "
                + "its corridor floor upward (the bottom wall row is out of shot) and resize to 320×360.",
                MessageTypeDefOf.TaskCompletion, historical: false);
        }

        internal static CellRect InteriorOf(IntVec3 block)
        {
            return new CellRect(block.x + 1, block.z + CorridorHeight + 2, RoomWidth, RoomHeight);
        }

        /// <summary>
        /// Dresses a room with the demo stage's own builders, so the two cannot drift apart.
        /// </summary>
        internal static void Dress(Map map, int room, CellRect interior, Building_OutfitStand stand)
        {
            if (room == 0)
            {
                Building_Bed bed = DebugTools_DemoStage.BuildHospital(map, interior, stand);
                DebugTools_DemoStage.SpawnPatient(map, bed);
            }
            else if (room == 1)
            {
                DebugTools_DemoStage.BuildLaboratory(map, interior, stand);
            }
            else
            {
                // stoveNorth: this kitchen's door is on the south wall, so the stove goes
                // opposite it. By default it lands across the doorway and the chef changes
                // and cooks in the same two tiles: no visible walk, and a before/after pair
                // that looks like one photo.
                DebugTools_DemoStage.BuildKitchen(map, interior, stand, stoveNorth: true);
            }
        }

        /// <summary>
        /// Walls, door, floor, roof for one block. Rows from the south edge: wall (cropped
        /// away) / corridor ×2 / wall with the door / interior ×5 / wall.
        /// </summary>
        internal static void BuildShell(Map map, IntVec3 block)
        {
            IntVec3 door = block + new IntVec3(DoorX, 0, CorridorHeight + 1);
            TerrainDef plank = DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor");

            for (int rx = 0; rx < BlockWidth; rx++)
            {
                for (int rz = 0; rz < BlockHeight; rz++)
                {
                    IntVec3 cell = block + new IntVec3(rx, 0, rz);
                    if (plank != null)
                    {
                        map.terrainGrid.SetTerrain(cell, plank);
                    }
                    map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);

                    bool wall = rx == 0 || rx == BlockWidth - 1 || rz == 0 || rz == BlockHeight - 1
                        || rz == CorridorHeight + 1;
                    if (!wall)
                    {
                        continue;
                    }
                    ThingDef built = cell == door ? ThingDefOf.Door : ThingDefOf.Wall;
                    DebugTools_Fixtures.Spawn(map, built, ThingDefOf.WoodLog, cell, Rot4.North);
                }
            }
        }

        /// <summary>
        /// One specialist per room, in their own clothes, in the corridor next to the door: the
        /// "before" half of that room's card. Not on the far row, so their feet and name label
        /// clear the panel's bottom edge. Food is full, unlike the demo stage's hungry doctor
        /// and chef, so nobody leaves for a meal mid-shot.
        /// </summary>
        internal static void SpawnWorker(Map map, IntVec3 block, int room)
        {
            IntVec3 cell = block + new IntVec3(DoorX, 0, CorridorHeight);
            if (!cell.InBounds(map))
            {
                return;
            }

            string workDefName = "Doctor";
            string skillDefName = "Medicine";
            string nick = "Doc";
            Gender gender = Gender.Male;
            if (room == 1)
            {
                workDefName = "Research";
                skillDefName = "Intellectual";
                nick = "Lab";
                gender = Gender.Female;
            }
            else if (room == 2)
            {
                workDefName = "Cooking";
                skillDefName = "Cooking";
                nick = "Chef";
                gender = Gender.Male;
            }

            WorkTypeDef specialty = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workDefName);
            Pawn pawn = DebugTools_Fixtures.AveragePawn(gender, nick, specialty);
            DebugTools_Fixtures.DressInStartingKit(pawn, workDefName == "Research");
            GenSpawn.Spawn(pawn, cell, map);

            // Their specialty and nothing else, so each stays in their own building instead
            // of wandering to another room's job and dressing for the wrong room on camera.
            pawn.workSettings.EnableAndInitialize();
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (work.visible && !pawn.WorkTypeIsDisabled(work))
                {
                    pawn.workSettings.SetPriority(work, work == specialty ? 1 : 0);
                }
            }

            SkillDef skill = DefDatabase<SkillDef>.GetNamedSilentFail(skillDefName);
            if (skill != null)
            {
                SkillRecord record = pawn.skills?.GetSkill(skill);
                if (record != null)
                {
                    record.Level = 20;
                }
            }

            DebugTools_DemoStage.TopUpNeeds(pawn, 1f);
        }
    }
}
#endif
