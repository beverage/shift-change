// SCENES only, see the config table in ShiftChange.csproj. This file builds a scene and must
// never reach a player: it clears a 10x8 footprint (destroying any pawn in it), rewrites its
// terrain and roof, and leaves a permanent player-faction colonist and buildings behind. It
// calls DebugTools_DemoStage (SpawnTorch, SpawnStand, TopUpNeeds), which must stay under SCENES.
#if SCENES
using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using System.Reflection;
using Verse.AI;
using static ShiftChange.DebugTools_Fixtures;

namespace ShiftChange
{
    /// <summary>
    /// Fixture for the recreation arm: a roofed shallow-water pool ringed by a plank deck,
    /// torch-lit and heated, with a stand by the door holding one white robe and set to
    /// recreation, and a swimmer with no work and a drained joy need. The swimmer should dress
    /// first, swim, and change back on their first job outside. The recreation footage is shot
    /// here, so keep the layout stable.
    ///
    /// <para>The pool is sized for <c>SwimPathFinder</c>, which makes exactly twelve hops out
    /// from the start cell and then returns to it (a fourteen-cell path), each hop to a water
    /// cell from just over one to just over three cells away, and fails outright if any hop
    /// finds none. A 6×4 pool keeps a candidate in reach of every node; a 3×3 one resolves,
    /// but the hops fold back on the spot and read as wading rather than swimming.</para>
    ///
    /// <para>The GoSwimming giver also needs, indoors, a room temperature above 10 °C (hence
    /// the heat push and torches) and pool cells that are standable, water, non-toxic and
    /// unfogged. So the pool is vanilla <c>WaterShallow</c>: deep water is impassable, which
    /// SwimPathFinder rejects as not standable, so vanilla swimmers never enter it.</para>
    /// </summary>
    internal static class DebugTools_PoolStage
    {
        internal const int PoolWidth = 6;
        internal const int PoolHeight = 4;

        /// <summary>Interior = the pool plus a one-tile deck ring.</summary>
        internal const int InteriorWidth = PoolWidth + 2;
        internal const int InteriorHeight = PoolHeight + 2;
        internal const int Width = InteriorWidth + 2;
        internal const int Height = InteriorHeight + 2;

        /// <summary>Door column, relative to the block origin.</summary>
        internal const int DoorX = 4;

        /// <summary>
        /// Below 0.35, where the Anything-schedule think node (<c>ThinkNode_Priority_GetJoy</c>)
        /// starts offering joy, with headroom for several swims before the need refills.
        /// </summary>
        internal const float DrainedJoy = 0.15f;

        /// <summary>
        /// One shot of warmth so the room passes GoSwimming's indoor gate (above 10 °C) on the
        /// first joy roll instead of waiting for the torches, which sustain it afterwards.
        /// </summary>
        internal const float HeatPush = 3000f;

        /// <summary>
        /// Registered from <see cref="DebugTools_Menu"/>, not by its own attribute.
        /// </summary>
        internal static void BuildPoolStage()
        {
            Map map = Find.CurrentMap;
            IntVec3 origin = UI.MouseCell();
            CellRect footprint = new CellRect(origin.x, origin.z, Width, Height);
            if (!footprint.InBounds(map))
            {
                Messages.Message("Pool stage does not fit here — click further from the map edge.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
            TerrainDef water = DefDatabase<TerrainDef>.GetNamedSilentFail("WaterShallow");
            if (standDef == null || water == null)
            {
                Messages.Message("Outfit stand or WaterShallow def not found — cannot build the pool stage.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            GenDebug.ClearArea(footprint, map);
            Current.Game.playSettings.useWorkPriorities = true;

            BuildShell(map, origin);

            CellRect interior = new CellRect(origin.x + 1, origin.z + 1, InteriorWidth, InteriorHeight);
            CellRect pool = new CellRect(interior.minX + 1, interior.minZ + 1, PoolWidth, PoolHeight);
            foreach (IntVec3 cell in pool)
            {
                map.terrainGrid.SetTerrain(cell, water);
            }

            // Torches at the deck's east corners, away from the stand's south-west spot: light
            // for the footage, warmth for the giver's indoor gate.
            DebugTools_DemoStage.SpawnTorch(map, new IntVec3(interior.maxX, 0, interior.minZ));
            DebugTools_DemoStage.SpawnTorch(map, new IntVec3(interior.maxX, 0, interior.maxZ));

            Building_OutfitStand stand =
                DebugTools_DemoStage.SpawnStand(map, standDef, interior, doorNorth: false);
            ThingDef robe = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Robe");
            if (robe != null)
            {
                // White, so the swap reads at a glance on camera. One robe only: the stand holds
                // one outfit, and a second robe would be dropped when the first is put back.
                stand.AddApparel(MakeGarment(robe, Color.white));
            }

            // A pure pool room scores only the generic Room role (swimming is terrain-driven, so
            // RoomRoleWorker_RecRoom counts nothing in it), which suggests no trigger, so the
            // stand never lights up on its own. The manual recreation toggle is the player's flow
            // for pool rooms.
            CompShiftStand comp = stand.TryGetComp<CompShiftStand>();
            if (comp != null && !comp.HandlesRecreation())
            {
                comp.ToggleRecreation();
            }

            SpawnSwimmer(map, origin, interior);

            // After the shell exists, so the heat lands in the enclosed room instead of
            // dissipating outdoors.
            GenTemperature.PushHeat(new IntVec3(origin.x + DoorX, 0, interior.minZ + 1), map, HeatPush);

            Messages.Message("Pool room stage built; the stand dresses for recreation. Use "
                + "Drain recreation on a pawn (or wait for a joy roll): they should change into "
                + "the robe at the stand FIRST, take their recreation, and change back on their "
                + "first job outside the room.",
                MessageTypeDefOf.TaskCompletion, historical: false);
        }

        /// <summary>
        /// Re-arms the recreation trigger on one pawn: drains joy to <see cref="DrainedJoy"/> and
        /// clears every joy tolerance, so any giver can win the next roll. It starts no job, so
        /// the real roll runs and any joy source from any mod is exercised.
        ///
        /// <para>The tolerance reset is the part that matters. A joy kind whose tolerance has
        /// passed 0.5 is skipped outright, not down-weighted (<c>JobGiver_GetJoy</c> tests
        /// <c>BoredOf</c> and continues), until tolerance decays below 0.3. Meditating for
        /// psyfocus banks Meditative tolerance (<c>JobDriver_Meditate</c> ticks
        /// <c>JoyUtility.JoyTickCheckEnd</c>), which excludes every Meditative giver, swimming
        /// included; draining the need alone cannot undo that.</para>
        /// </summary>
        internal static void DrainRecreation(Pawn pawn)
        {
            if (pawn?.needs?.joy == null || !pawn.RaceProps.Humanlike)
            {
                Messages.Message("That pawn has no joy need.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            pawn.needs.joy.CurLevel = DrainedJoy;

            // JoyToleranceSet keeps both maps (tolerances and bored) private and exposes only
            // add and decay-one-interval, with no reset, so they are reset by reflection.
            int cleared = 0;
            JoyToleranceSet tolerances = pawn.needs.joy.tolerances;
            foreach (string field in new[] { "tolerances", "bored" })
            {
                FieldInfo info = typeof(JoyToleranceSet).GetField(
                    field, BindingFlags.Instance | BindingFlags.NonPublic);
                object map = info?.GetValue(tolerances);
                if (map == null)
                {
                    continue;
                }
                // A DefMap<T,V> exposes an indexer and Count; reset through those.
                PropertyInfo count = map.GetType().GetProperty("Count");
                PropertyInfo item = map.GetType().GetProperty("Item", new[] { typeof(int) });
                if (count == null || item == null)
                {
                    continue;
                }
                int n = (int)count.GetValue(map);
                for (int i = 0; i < n; i++)
                {
                    item.SetValue(map, field == "bored" ? (object)false : 0f, new object[] { i });
                }
                cleared++;
            }

            Messages.Message(pawn.LabelShort + ": joy drained to "
                + DrainedJoy.ToStringPercent() + (cleared == 2
                    ? " and every joy tolerance cleared — any giver can win the next roll."
                    : " (tolerances could NOT be cleared — boredom may still skip a kind)."),
                cleared == 2 ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput,
                historical: false);
        }

        /// <summary>Walls with one south door, plank floor and roof over the footprint.</summary>
        internal static void BuildShell(Map map, IntVec3 origin)
        {
            IntVec3 door = origin + new IntVec3(DoorX, 0, 0);
            TerrainDef plank = DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor");

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

                    bool wall = rx == 0 || rx == Width - 1 || rz == 0 || rz == Height - 1;
                    if (!wall)
                    {
                        continue;
                    }
                    ThingDef built = cell == door ? ThingDefOf.Door : ThingDefOf.Wall;
                    Spawn(map, built, ThingDefOf.WoodLog, cell, Rot4.North);
                }
            }
        }

        /// <summary>
        /// One colonist inside the door with every work type at priority zero, so only joy and
        /// idling are on offer and a stray job cannot muddy the take. Needs are full except joy,
        /// drained so the first break comes quickly.
        /// </summary>
        internal static void SpawnSwimmer(Map map, IntVec3 origin, CellRect interior)
        {
            IntVec3 cell = new IntVec3(origin.x + DoorX, 0, interior.minZ);
            if (!cell.InBounds(map))
            {
                return;
            }

            Pawn pawn = AveragePawn(Gender.Female, "Swim");
            DressInStartingKit(pawn, researcher: false);
            GenSpawn.Spawn(pawn, cell, map);

            pawn.workSettings.EnableAndInitialize();
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (work.visible && !pawn.WorkTypeIsDisabled(work))
                {
                    pawn.workSettings.SetPriority(work, 0);
                }
            }

            DebugTools_DemoStage.TopUpNeeds(pawn, 1f);
            if (pawn.needs?.joy != null)
            {
                pawn.needs.joy.CurLevel = DrainedJoy;
            }
        }
    }
}
#endif
