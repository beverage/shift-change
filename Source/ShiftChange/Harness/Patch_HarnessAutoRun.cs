// HARNESS only: this file is the -shiftchange-harness launch flag, and a plain Release build
// compiles it out, so the flag does not exist in a player's install. The guard is whole-file,
// always (docs/DESIGN.md, "Development tooling"); run-harness.sh builds with -p:Harness=true.
#if HARNESS
using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Runs the lifecycle harness without a human when the game is launched with
    /// <c>-shiftchange-harness</c>, then quits, pass or fail, so a caller can wait on the process
    /// and read the log. run-harness.sh adds <c>-quicktest</c> for a throwaway map; nothing here
    /// checks for it, so the flag fires on any game that finishes initializing, a loaded save
    /// included, clears a pad there and loads its round-trip saves over it. It is a Harmony
    /// postfix rather than a <c>GameComponent</c>, because components are scribed into every
    /// save and a dev-only feature has no business appearing in one.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    public static class Patch_HarnessAutoRun
    {
        internal const string Arg = "shiftchange-harness";

        /// <summary>
        /// One run per process. <c>FinalizeInit</c> is not once per launch: the save/load
        /// round-trip cases load a game in-process through
        /// <c>SavedGameLoaderNow.LoadGameFromSaveFileNow</c>, and <c>Game.LoadGame</c> calls
        /// <c>FinalizeInit</c> at its end. Without this latch the harness starts a nested run
        /// inside its own round-trip case, and the process quits on whichever finishes first.
        /// </summary>
        internal static bool started;

        public static void Postfix()
        {
            if (!GenCommandLine.CommandLineArgPassed(Arg) || started)
            {
                return;
            }
            started = true;
            // FinalizeInit runs inside the long event that generates the map; deferring spawns the
            // harness into a finished map, as a human clicking the debug action would find it.
            LongEventHandler.ExecuteWhenFinished(RunAndQuit);
        }

        internal static void RunAndQuit()
        {
            bool passed = false;
            try
            {
                Map map = Find.CurrentMap;
                IntVec3 origin;
                if (map == null)
                {
                    Log.Error("[ShiftChange] -" + Arg + ": no current map to run on.");
                }
                else if (!TryFindPad(map, out origin))
                {
                    Log.Error("[ShiftChange] -" + Arg + ": no clear "
                              + DebugTools_LifecycleHarness.PadSize + "×"
                              + DebugTools_LifecycleHarness.PadSize + " area on this map.");
                }
                else
                {
                    passed = DebugTools_LifecycleHarness.Run(map, origin, toast: false);
                }
            }
            catch (Exception e)
            {
                // A throw must not leave the process alive: a caller waiting on exit would hang
                // forever on a game sitting at a map nobody watches.
                Log.Error("[ShiftChange] -" + Arg + " threw: " + e);
            }

            // One greppable verdict line; run-harness.sh matches it exactly rather than parsing the
            // report above, so change the two together.
            Log.Message("[ShiftChange] harness auto-run: " + (passed ? "PASSED" : "FAILED"));
            Root.Shutdown();
        }

        /// <summary>
        /// Somewhere the fixture can stand: the map centre if usable, so runs are comparable. The
        /// pad is cleared before use, so the bar is a pad in bounds whose origin is standable and
        /// not water, not an empty one.
        ///
        /// <para>The one-cell margin must have rooms too, because cases reach past their pad (the
        /// exterior-wall case counts the ring around a wall on the pad's edge). Natural rock there
        /// carries no room, <c>GetRoom</c> returns null, and the case silently stops running the
        /// geometry it was written for. Open ground is one enormous room, so on an ordinary map
        /// this rejects only origins near rock or walls. On a map whose load aborted before its
        /// region grid was built it rejects every origin, which
        /// <see cref="DebugTools_SaveRoundTrip.CannotStage"/> relies on. A pad picked by hand from
        /// the debug menu bypasses this finder.</para>
        /// </summary>
        internal static bool TryFindPad(Map map, out IntVec3 origin)
        {
            int size = DebugTools_LifecycleHarness.PadSize;
            Predicate<IntVec3> usable = c =>
            {
                CellRect pad = new CellRect(c.x, c.z, size, size);
                if (!pad.ExpandedBy(1).InBounds(map)
                    || !c.Standable(map)
                    || c.GetTerrain(map).IsWater)
                {
                    return false;
                }
                foreach (IntVec3 cell in pad.ExpandedBy(1))
                {
                    if (cell.GetRoom(map) == null)
                    {
                        return false;
                    }
                }
                return true;
            };

            IntVec3 centre = map.Center - new IntVec3(size / 2, 0, size / 2);
            if (usable(centre))
            {
                origin = centre;
                return true;
            }
            return CellFinderLoose.TryGetRandomCellWith(usable, map, 500, out origin);
        }
    }
}
#endif
