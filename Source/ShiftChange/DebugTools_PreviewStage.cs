// SCENES only — see the config table in ShiftChange.csproj. Like the demo
// stage this BUILDS A SCENE and must never reach a player: it clears a 32x10
// footprint (destroying any pawn in it) and leaves three permanent
// player-faction colonists and three roomfuls of owned buildings behind.
#if SCENES
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// The title-card stage: dev mode → Shift Change → Build preview stage,
    /// then click a map cell. Three standalone rooms — hospital, laboratory,
    /// kitchen — each with its own corridor and its own worker, sized so a
    /// screenshot of any one of them crops to half a Workshop preview without
    /// distortion.
    ///
    /// <para>The geometry is the whole point. A Workshop preview is 640×360,
    /// and a card is a before/after pair side by side, so each panel is
    /// 320×360 — a ratio of 8:9. Each block's capture rectangle is therefore 8
    /// tiles across by 9 down:</para>
    ///
    /// <code>
    ///   wall + 6 interior + wall              = 8 across
    ///   wall + 5 interior + wall + 2 corridor = 9 down
    /// </code>
    ///
    /// <para>At 320 px that is exactly 40 px per tile, twice what the demo
    /// stage gives on the same canvas, so a pawn standing in a corridor reads
    /// on its own rather than leaning on its name label.</para>
    ///
    /// <para><b>Each block's far corridor wall sits one row BELOW its capture
    /// rectangle</b>, deliberately out of shot. The corridor still has to be
    /// enclosed and roofed, but excluding that row puts the panel's bottom
    /// edge on the floor-to-wall line rather than cutting through anything.
    /// </para>
    ///
    /// <para><b>The blocks are separated by <see cref="Gap"/> tiles of open
    /// ground</b>, which is what makes them standalone. An 8-tile crop of one
    /// block cannot catch a neighbour's wall, and a crop that is a few pixels
    /// off catches grass rather than another room — the failure that made the
    /// four-room demo stage uncroppable for this purpose.</para>
    ///
    /// <para>All three run at once, so one locked-off recording yields all six
    /// stills. That matters more than it sounds: framing is then identical by
    /// construction across every card, and the crop is measured once for the
    /// whole take instead of once per screenshot.</para>
    ///
    /// <para><b>Why this is not the demo stage with different numbers.</b>
    /// That one is a test fixture as much as a film set, and every play
    /// observation to date was made in its layout. Resizing it to suit a
    /// screenshot would make those observations incomparable. This stage
    /// borrows all of its room dressing and changes only the shell.</para>
    ///
    /// <para>Ships in Release for the same reason the demo stage does: footage
    /// is filmed on live builds.</para>
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

        /// <summary>
        /// Open ground between blocks. Its only job is crop tolerance — miss
        /// the edge by a few pixels and you catch grass instead of the next
        /// room's wall.
        /// </summary>
        internal const int Gap = 4;

        internal const int Rooms = 3;
        internal const int Pitch = BlockWidth + Gap;
        internal const int Width = Rooms * BlockWidth + (Rooms - 1) * Gap;

        /// <summary>
        /// Door column, relative to a block's origin, so interior column
        /// <c>minX + 3</c>.
        /// </summary>
        /// <remarks>
        /// Not centred, and not negotiable: the three rooms have to share a
        /// door column or the cards stop looking like a set, and the kitchen
        /// is the room that constrains it. <c>BuildKitchen</c> puts the stove
        /// on the south wall at <c>interior.minX + 2</c> and the stand sits at
        /// <c>minX</c>, with a torch at <c>maxX</c> — so a door at <c>minX +
        /// 2</c> opens straight into the stove. <c>minX + 3</c> is clear in
        /// all three rooms.
        /// </remarks>
        internal const int DoorX = 4;

        /// <summary>
        /// Reached from the "Dev tools..." submenu
        /// (<see cref="DebugTools_Menu"/>), which carries the game-state and
        /// Odyssey gating this method used to declare itself.
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

                // The corridor gets its own light. Unlit, an arriving pawn is
                // dark on dark and including the corridor buys nothing. East
                // end, so it never stands where the worker does.
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
        /// Room dressing, borrowed wholesale from the demo stage so the two
        /// stages can never drift on what a hospital or a kitchen contains.
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
                // stoveNorth: this kitchen's door is on the SOUTH wall, so the
                // stove goes opposite it. Left at the demo stage's default the
                // stove lands on the south wall across the doorway, and the
                // chef changes and cooks in the same two tiles — no visible
                // walk, and a before/after pair that looks like one photo.
                DebugTools_DemoStage.BuildKitchen(map, interior, stand, stoveNorth: true);
            }
        }

        /// <summary>
        /// Walls, door, floor, roof for one block. Rows from the south edge:
        /// wall / corridor ×2 / wall with the door / interiors ×5 / wall. The
        /// southmost row is the one the capture crops away.
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
        /// One specialist per room, in their own clothes, standing in the
        /// corridor at the door — the "before" half of that room's card.
        /// </summary>
        /// <remarks>
        /// <para>They stand in the corridor row ADJACENT to the door rather
        /// than the far one. That is a framing decision: a title overlaid on
        /// the bottom of a panel covers the far row, and a worker standing
        /// there gets their feet clipped by the type.</para>
        /// <para>Food is full, unlike the demo stage's deliberately hungry
        /// doctor and chef. This take ends when the work does — nobody should
        /// break for a meal mid-shot, and there is nothing to eat out here
        /// anyway. Driving the change-back beat needs a reason to leave the
        /// room, which is a different staging problem.</para>
        /// </remarks>
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

            // Their specialty and nothing else, so each stays in their own
            // building for the whole take instead of wandering to another
            // room's job and dressing for the wrong room on camera.
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
