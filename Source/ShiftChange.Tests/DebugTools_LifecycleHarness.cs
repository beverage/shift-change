using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
// The fixture builders and their teardown.
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// What the tests play on: the pad, and the <see cref="Fixture"/> a test sets up on it. Most
    /// tests build a throwaway scene, drive it, check the outcome and tear it down
    /// (<see cref="StoryFixture"/>); a lifecycle test fires one real engine event at a checked-out
    /// stand and checks where the ledger lands.
    ///
    /// <para>Each lifecycle test splits in two. The engine's half is read from the game's code and
    /// taken as given: a gravship flight, say, despawns what is aboard with <c>WillReplace</c>. The
    /// test drives our half: given that call, where the ledger lands.</para>
    ///
    /// <para>Fire lifecycle events through the engine's own entry points, never our comp
    /// handlers directly, so the engine's comp dispatch, its ordering and its side effects run
    /// for real and only the orchestration around them is simulated. docs/TESTING.md, "Rules the
    /// suite follows".</para>
    ///
    /// <para>Two kinds of fixture. Lifecycle tests hand-assemble a checked-out state
    /// (<see cref="Build"/>) to test what happens to a ledger afterwards. Driver tests stage an
    /// undressed pawn (<see cref="Stage"/>) and run <see cref="JobDriver_SwapAtStand"/> for real
    /// through the pawn's own tracker (<see cref="RunSwap"/>), to test whether the driver builds
    /// that ledger.</para>
    ///
    /// <para>Engine traps shape the fixtures (docs/TESTING.md, "Five engine traps"). Jobs are pooled,
    /// so <see cref="RunSwap"/> watches the driver, not the job. A generated colonist is not
    /// tended, so the fixture removes what it arrives needing tended. And the swap steps the
    /// game's own clock, so everything on the map moves while it runs: a test holds still what it
    /// arranged (<see cref="Fixture.Hold"/>, <see cref="HarnessFixtures.StayPut"/>).</para>
    /// </summary>
    internal static class DebugTools_LifecycleHarness
    {
        /// <summary>Side of the pad every test is handed, cleared and rebuilt per test.</summary>
        internal const int PadSize = 7;

        /// <summary>
        /// Somewhere the tests can stand: the map centre if usable, so runs are comparable, and a
        /// random cell otherwise. The pad is cleared before
        /// use, so the bar is a pad in bounds whose origin is standable and not water, not an
        /// empty one.
        ///
        /// <para>Every cell of the pad and its one-cell margin must be in a room that touches the
        /// map edge. Tests reach past their pad: the exterior-wall test counts the ring around a
        /// wall on the pad's edge, and some tests need a stand in the open, which the mod tests
        /// with <c>Room.TouchesMapEdge</c>, on the margin outside a door or on the pad left
        /// unwalled.
        /// Natural rock has no room, and a valley ringed by mountains, an island in deep water or a
        /// ruin's walls give the margin a room that does not reach the edge. The margin is a closed
        /// ring around the pad, so it still reaches the edge once the tests build walls on the pad.
        /// A map whose region updater a failed load left off is refused without a search, which
        /// <see cref="DebugTools_SaveRoundTrip.CannotStage"/> relies on.</para>
        /// </summary>
        internal static bool TryFindPad(Map map, out IntVec3 origin)
        {
            // When a prefix on Map.FinalizeLoading throws, the body that turns the map's
            // RegionAndRoomUpdater back on never runs, so its rooms are never built and every
            // room lookup on it logs a warning.
            if (!map.regionAndRoomUpdater.Enabled)
            {
                origin = IntVec3.Invalid;
                return false;
            }
            Predicate<IntVec3> usable = c =>
            {
                CellRect pad = new CellRect(c.x, c.z, PadSize, PadSize);
                if (!pad.ExpandedBy(1).InBounds(map)
                    || !c.Standable(map)
                    || c.GetTerrain(map).IsWater)
                {
                    return false;
                }
                foreach (IntVec3 cell in pad.ExpandedBy(1))
                {
                    Room room = cell.GetRoom(map);
                    if (room == null || !room.TouchesMapEdge)
                    {
                        return false;
                    }
                }
                return true;
            };

            IntVec3 centre = map.Center - new IntVec3(PadSize / 2, 0, PadSize / 2);
            if (usable(centre))
            {
                origin = centre;
                return true;
            }
            return CellFinderLoose.TryGetRandomCellWith(usable, map, 500, out origin);
        }

        /// <summary>
        /// One test's throwaway world: a stand, the colonist it is staged for, and what a test adds
        /// beyond them. <see cref="Teardown"/> sweeps all of it.
        /// </summary>
        internal class Fixture
        {
            internal Map Map;
            internal Building_OutfitStand Stand;
            internal Pawn Pawn;
            internal CompShiftStand Comp;
            internal int StoredCount;

            internal List<Pawn> Extras = new List<Pawn>();

            /// <summary>Allowed areas a test made; a map holds ten at most.</summary>
            internal List<Area> Areas = new List<Area>();

            /// <summary>
            /// The strip outside the west door that
            /// <see cref="HarnessFixtures.StageWithDoorway(Map, CellRect)"/> cuts, past the pad;
            /// empty for every other fixture.
            /// </summary>
            internal CellRect Outside = CellRect.Empty;

            /// <summary>Cells past the pad that tests put things on (<c>OpenGround</c>).</summary>
            internal List<IntVec3> Claimed = new List<IntVec3>();

            /// <summary>
            /// A second map (<see cref="HarnessFixtures.SecondMap"/>), so a flight can leave the
            /// borrower and their stand apart.
            /// </summary>
            internal Map Pocket;

            /// <summary>
            /// What the test holds against the game's own simulation while the clock steps, such
            /// as the air a room was set to: run before every tick and after the last
            /// (<see cref="GameClock.Step"/>). Null when the test holds nothing.
            /// </summary>
            internal Action Hold;
        }

        /// <summary>
        /// What a staged fixture wears and what its stand holds. Core defs only, never the demo
        /// stage's: the default list is four mods and <c>--full</c> copies the runner's own, and a
        /// fixture built from Vanilla Apparel Expanded defs would be a different test on each.
        /// </summary>
        internal enum StageKit
        {
            /// <summary>
            /// Stand: a duster (Shell). Pawn: shirt and trousers (OnSkin) plus a parka (Shell), so
            /// the duster displaces the parka and the ledger has something in it.
            /// </summary>
            Displacing,

            /// <summary>
            /// The same without the parka, so the duster (Shell) shares no layer with shirt and
            /// trousers (OnSkin) and displaces nothing. <see cref="Build"/> cannot produce this
            /// shape: it returns null when nothing is displaced.
            /// </summary>
            NonDisplacing,
        }

        /// <summary>
        /// Fixture builds are retried because spawning a freshly generated pawn is unreliable on a
        /// large mod list and the failure is not ours: <c>Pawn.SpawnSetup</c> calls
        /// <c>Pawn_HealthTracker.Notify_Spawned</c>, which walks the pawn's hediffs, and a hediff
        /// that adds or removes another there throws <c>Collection was modified</c>. Every retry
        /// is printed, so an intermittent bug of ours still shows.
        /// </summary>
        internal const int BuildAttempts = 3;

    }
}
