// SCENES only — see the config table in ShiftChange.csproj. This file BUILDS
// A SCENE and must never reach a player: it clears a 10x8 footprint (which
// destroys any pawn standing in it), lays 24 cells of shallow water, and
// leaves a permanent player-faction colonist and buildings behind. The
// fixture primitives it is built from live in DebugTools_Fixtures, which
// always compiles because the harness needs them in Release; the three it
// still takes from DebugTools_DemoStage (SpawnTorch, SpawnStand, TopUpNeeds)
// resolve because that file is SCENES-gated too.
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
    /// The recreation test fixture: dev mode → Shift Change → Build pool room
    /// stage, then click a map cell. One roofed natatorium — a shallow-water
    /// pool ringed by a plank deck, torch-lit and heat-pushed warm — with an
    /// outfit stand by the door holding one white robe, and a swimmer with no
    /// work at all and a drained joy need.
    ///
    /// TODAY this stages the BASELINE: the swimmer takes vanilla Odyssey
    /// GoSwimming breaks in their own clothes and the stand does nothing,
    /// because the interception matches work jobs only. The same
    /// stage unchanged becomes the acceptance fixture for the recreation
    /// branch — identical take, but the swimmer dresses at the stand first.
    /// The comparison is the point: keep the stage stable across that change.
    ///
    /// The pool is sized for <c>SwimPathFinder</c>, which is all-or-nothing:
    /// it builds exactly 12 hops of 1–3 cells each and FAILS OUTRIGHT if any
    /// hop has no candidate (SwimPathFinder.cs:27-76) — there are no shorter
    /// paths. A 6×4 pool keeps in-pool candidates at radius ≤3 from every
    /// node, so a path always resolves; ~3×3 still paths but the hops fold on
    /// the spot and read as wading rather than swimming.
    ///
    /// GoSwimming's other gates (JoyGiver_GoSwimming.cs:58-81): indoors the
    /// ROOM temperature must exceed 10 °C — hence the heat push and torches —
    /// and every pool cell must be Standable, IsWater, non-toxic and
    /// unfogged. That means vanilla <c>WaterShallow</c> and never deep water:
    /// deep water is Impassable, so SwimPathFinder rejects it as
    /// non-Standable and vanilla swimmers never enter it.
    ///
    /// Ships in Release like its siblings: footage is filmed on live builds.
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
        /// Below the 0.35 threshold where the Anything-schedule think node
        /// starts offering joy (ThinkNode_Priority_GetJoy), with headroom so
        /// several consecutive swims fit in one observation session before
        /// the need refills.
        /// </summary>
        internal const float DrainedJoy = 0.15f;

        /// <summary>
        /// One shot of warmth so the room passes GoSwimming's indoor gate
        /// (&gt; 10 °C) on the first joy roll instead of waiting for the
        /// torches to catch up. Torches sustain it afterwards.
        /// </summary>
        internal const float HeatPush = 3000f;

        /// <summary>
        /// Registered from <see cref="DebugTools_Menu"/>, not by its own
        /// attribute — one collapsing entry, never a category of loose ones.
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

            // Deck corners away from the stand's SW spot. Light matters for
            // the eventual footage, heat for the giver's indoor gate.
            DebugTools_DemoStage.SpawnTorch(map, new IntVec3(interior.maxX, 0, interior.minZ));
            DebugTools_DemoStage.SpawnTorch(map, new IntVec3(interior.maxX, 0, interior.maxZ));

            Building_OutfitStand stand =
                DebugTools_DemoStage.SpawnStand(map, standDef, interior, doorNorth: false);
            ThingDef robe = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Robe");
            if (robe != null)
            {
                // White-dyed, so the swap reads at a glance on camera. One robe only: the
                // stand holds one outfit, and a second robe would just be
                // evicted by the first swap.
                stand.AddApparel(MakeGarment(robe, Color.white));
            }

            // Arm the trigger. A pure pool room is ROLELESS — GoSwimming is
            // terrain-driven, so RoomRoleWorker_RecRoom counts nothing here —
            // and the automatic path can never light up. The manual toggle is
            // the normal player flow for pool rooms; the stage does it for
            // the take.
            CompShiftStand comp = stand.TryGetComp<CompShiftStand>();
            if (comp != null && !comp.HandlesRecreation())
            {
                comp.ToggleRecreation();
            }

            SpawnSwimmer(map, origin, interior);

            // After the shell exists, so the push lands in the enclosed room
            // rather than dissipating outdoors.
            GenTemperature.PushHeat(new IntVec3(origin.x + DoorX, 0, interior.minZ + 1), map, HeatPush);

            Messages.Message("Pool room stage built; the stand dresses for recreation. Use "
                + "Drain recreation on a pawn (or wait for a joy roll): they should change into "
                + "the robe at the stand FIRST, take their recreation, and change back on their "
                + "first job outside the room.",
                MessageTypeDefOf.TaskCompletion, historical: false);
        }

        /// <summary>
        /// Re-arm the recreation trigger on one pawn: drop the joy need to a
        /// level that makes them seek recreation, and clear the joy TOLERANCES
        /// that would otherwise decide which kind they pick.
        ///
        /// <para>The tolerance half is the part that matters, and it replaced a
        /// "force a swim" button that could not do this job. Joy selection is a
        /// weighted roll over every giver, and a kind whose tolerance passed 0.5
        /// is not down-weighted but SKIPPED outright
        /// (<c>JobGiver_GetJoy</c> tests <c>BoredOf</c> and continues), with
        /// boredom only lifting once tolerance decays below 0.3. A psycaster
        /// meditating for psyfocus banks Meditative tolerance through the same
        /// joy path (<c>JobDriver_Meditate</c> ticks
        /// <c>JoyUtility.JoyTickCheckEnd</c>), which silently excludes every
        /// Meditative giver — hot springs, swimming, skygazing, art. Draining
        /// the need alone does nothing about that, which is why a hundred
        /// draft/undraft cycles could not produce a pool take.</para>
        ///
        /// <para>Deliberately does NOT start a job. Forcing one specific joy
        /// def only ever tested that def — it was Odyssey <c>GoSwimming</c>
        /// against water TERRAIN, so it could not reach a modded hot spring,
        /// which is a building with its own giver. Re-arming and letting the
        /// real roll happen tests the path a player actually walks, and works
        /// for any joy source from any mod.</para>
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

            // JoyToleranceSet keeps both maps private and exposes only "add"
            // and "decay one interval" — there is no reset. Reflection is the
            // honest way to get one, and this file is SCENES-only, so it never
            // reaches a player.
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
                // DefMap<T,V> exposes an indexer and Count; reset through them
                // rather than reaching further into its own internals.
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

        /// <summary>
        /// Walls, one south door, plank floor, roof. Rows from the south
        /// edge: wall with door / deck / pool rows / deck / wall.
        /// </summary>
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
        /// One colonist with EVERY work type at priority zero, so joy and
        /// idling are all the think tree can offer — the stage measures the
        /// joy pipeline, and a stray hauling job would muddy the take. Needs
        /// are full except joy, drained below the Anything-schedule threshold
        /// so the first break comes quickly.
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
