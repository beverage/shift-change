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
    /// The suite's case machinery. Most cases build a throwaway fixture, drive it, check the
    /// outcome and tear it down; a lifecycle case fires one real engine event at a checked-out
    /// stand and checks where the ledger lands. Each case is one DevTools test (HarnessCases.cs),
    /// run through <c>Case</c>: its assertions signal DevTools as they go and write the case's
    /// transcript, which <see cref="HarnessFixture"/> puts in Test.log.
    ///
    /// <para>Each lifecycle case splits in two. The engine's half is read from the game's code and
    /// taken as given: a gravship flight, say, despawns what is aboard with <c>WillReplace</c>. The
    /// harness drives our half: given that call, where the ledger lands.</para>
    ///
    /// <para>Fire lifecycle events through the engine's own entry points, never our comp
    /// handlers directly, so the engine's comp dispatch, its ordering and its side effects run
    /// for real and only the orchestration around them is simulated. docs/TESTING.md, "Rules the
    /// suite follows".</para>
    ///
    /// <para>Two kinds of fixture. Lifecycle cases hand-assemble a checked-out state
    /// (<see cref="Build"/>) to test what happens to a ledger afterwards. Driver cases stage an
    /// undressed pawn (<see cref="Stage"/>) and run <see cref="JobDriver_SwapAtStand"/> for real
    /// through the pawn's own tracker (<see cref="RunSwap"/>), to test whether the driver builds
    /// that ledger.</para>
    ///
    /// <para>Engine traps shape the fixtures (docs/TESTING.md, "Four engine traps"). Jobs are pooled,
    /// so <see cref="RunSwap"/> watches the driver, not the job. A generated colonist is not
    /// tended, so the fixture removes what it arrives needing tended. And the swap steps the
    /// game's own clock, so everything on the map moves while it runs: a case holds still what it
    /// arranged (<see cref="Fixture.Hold"/>, <see cref="HarnessFixtures.StayPut"/>).</para>
    /// </summary>
    internal static class DebugTools_LifecycleHarness
    {
        /// <summary>Side of the pad every case is handed, cleared and rebuilt per case.</summary>
        internal const int PadSize = 7;

        /// <summary>
        /// The running case's transcript: its sentence, then one line per assertion.
        /// </summary>
        internal static readonly StringBuilder Report = new StringBuilder();

        /// <summary>
        /// Somewhere the cases can stand: the map centre if usable, so runs are comparable, and a
        /// random cell otherwise. The pad is cleared before
        /// use, so the bar is a pad in bounds whose origin is standable and not water, not an
        /// empty one.
        ///
        /// <para>Every cell of the pad and its one-cell margin must be in a room that touches the
        /// map edge. Cases reach past their pad: the exterior-wall case counts the ring around a
        /// wall on the pad's edge, and some cases need a stand in the open, which the mod tests
        /// with <c>Room.TouchesMapEdge</c>, on the margin outside a door or on the pad left
        /// unwalled.
        /// Natural rock has no room, and a valley ringed by mountains, an island in deep water or a
        /// ruin's walls give the margin a room that does not reach the edge. The margin is a closed
        /// ring around the pad, so it still reaches the edge once the cases build walls on the pad.
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
        /// One case's throwaway world: a stand, the colonist it is staged for, and what a case adds
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

            /// <summary>Allowed areas a case made; a map holds ten at most.</summary>
            internal List<Area> Areas = new List<Area>();

            /// <summary>
            /// The strip outside the west door that
            /// <see cref="HarnessFixtures.StageWithDoorway(Map, CellRect)"/> cuts, past the pad;
            /// empty for every other fixture.
            /// </summary>
            internal CellRect Outside = CellRect.Empty;

            /// <summary>Cells past the pad that cases put things on (<c>OpenGround</c>).</summary>
            internal List<IntVec3> Claimed = new List<IntVec3>();

            /// <summary>
            /// A second map (<see cref="HarnessFixtures.SecondMap"/>), so a flight can leave the
            /// borrower and their stand apart.
            /// </summary>
            internal Map Pocket;

            /// <summary>
            /// What the case holds against the game's own simulation while the clock steps, such
            /// as the air a room was set to: run before every tick and after the last
            /// (<see cref="GameClock.Step"/>). Null when the case holds nothing.
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

        /// <summary>
        /// Set by <see cref="ExpectKnownGap"/>, read once the case has run: a case that carries a
        /// known gap is skipped, unless it failed.
        /// </summary>
        internal static bool GapThisCase;

        /// <summary>A case on a checked-out fixture (<see cref="Build"/>).</summary>
        internal static void Case(Map map, CellRect pad, string name, Func<Fixture, bool> body)
        {
            Case(map, pad, name, Build, body);
        }

        /// <summary>
        /// A case with no fixture, for anything that asserts about static tables, or that reads a
        /// map and pad of its own and stages nothing on the pad <c>Teardown</c> would clear.
        /// </summary>
        internal static void Case(string name, Func<bool> body)
        {
            Report.Append("  ").AppendLine(name);
            GapThisCase = false;
            // Mod settings are global and cases run in sequence. A case that needs the decency
            // guard in a given state sets it and does not clean up; this restores it, so a leak
            // cannot decide the cases after it. Snapshot and restore rather than reset to the
            // default, so a run leaves the setting as it found it.
            bool decencyBefore = ShiftChangeMod.Settings != null
                                 && ShiftChangeMod.Settings.keepColonistsDecent;
            try
            {
                if (!body())
                {
                    ReturnedFalse();
                }
            }
            catch (Exception e)
            {
                Fail("threw: " + e);
            }
            finally
            {
                if (ShiftChangeMod.Settings != null)
                {
                    ShiftChangeMod.Settings.keepColonistsDecent = decencyBefore;
                }
            }
        }

        /// <summary>
        /// A case that stages its own fixture. <paramref name="stage"/> returns null for "this
        /// fixture cannot be built", which fails the case. The retry loop is for throws (see
        /// <see cref="BuildAttempts"/>).
        /// </summary>
        internal static void Case(Map map, CellRect pad, string name,
                                  Func<Map, CellRect, Fixture> stage, Func<Fixture, bool> body)
        {
            Report.Append("  ").AppendLine(name);
            GapThisCase = false;
            Fixture fix = null;
            // Restores the decency setting, as the fixture-less overload does.
            bool decencyBefore = ShiftChangeMod.Settings != null
                                 && ShiftChangeMod.Settings.keepColonistsDecent;
            try
            {
                Exception lastBuildError = null;
                for (int attempt = 1; attempt <= BuildAttempts && fix == null; attempt++)
                {
                    try
                    {
                        fix = stage(map, pad);
                    }
                    catch (Exception e)
                    {
                        lastBuildError = e;
                        Report.Append("    RETRY fixture build threw on attempt ").Append(attempt)
                              .Append(" — ").Append(e.GetType().Name).Append(": ")
                              .AppendLine(e.Message);
                        Teardown(null, map, pad);
                    }
                }
                if (fix == null)
                {
                    Fail(lastBuildError != null
                        ? "fixture build failed " + BuildAttempts + " times, last: " + lastBuildError
                        : "fixture could not be staged");
                    return;
                }

                if (!body(fix))
                {
                    ReturnedFalse();
                }
            }
            catch (Exception e)
            {
                Fail("threw: " + e);
            }
            finally
            {
                if (ShiftChangeMod.Settings != null)
                {
                    ShiftChangeMod.Settings.keepColonistsDecent = decencyBefore;
                }
                Teardown(fix, map, pad);
            }
        }

        /// <summary>
        /// An assertion: a line in the transcript, and a DevTools signal, which fails the test
        /// when the condition does not hold and does not stop it. The case goes on, so its
        /// transcript shows every assertion; the first that failed labels the test in Test.log.
        /// </summary>
        /// <returns>The condition, so a case can stop at a failure that makes the rest moot.</returns>
        internal static bool Expect(bool condition, string what)
        {
            Report.Append(condition ? "    PASS  " : "    FAIL  ").AppendLine(what);
            DevTools.Testing.Expect.IsTrue(condition, what);
            return condition;
        }

        /// <summary>
        /// An assertion expected to fail, with the reason recorded. It prints GAP and skips the
        /// test, not fails it, so a green run stays meaningful. When the condition starts to hold
        /// it prints FIXED instead, so the case gets updated.
        /// </summary>
        internal static bool ExpectKnownGap(bool condition, string what, string why)
        {
            if (condition)
            {
                Report.Append("    FIXED ").Append(what)
                      .AppendLine(" — known gap now passes, update the harness");
                return true;
            }
            Report.Append("    GAP   ").Append(what).Append(" — ").AppendLine(why);
            GapThisCase = true;
            return true;
        }

        /// <summary>
        /// Fails the test with a reason, which goes in the transcript in full and in Test.log's
        /// result line up to its first line break, so a thrown exception's trace stays above.
        /// </summary>
        internal static void Fail(string why)
        {
            Report.Append("    FAIL  ").AppendLine(why);
            int lineBreak = why.IndexOf('\n');
            DevTools.Testing.Test.Fail(lineBreak < 0 ? why : why.Substring(0, lineBreak).TrimEnd());
        }

        /// <summary>
        /// A case body that returns false has failed. One that failed an assertion has said why
        /// already, and the first reason given labels the test.
        /// </summary>
        internal static void ReturnedFalse()
        {
            DevTools.Testing.Test.Fail("the case returned false");
        }
    }
}
