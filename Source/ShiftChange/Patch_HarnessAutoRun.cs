// HARNESS only — see the configuration table in ShiftChange.csproj. This file
// IS the -shiftchange-harness launch flag, and it does not ship: a Release
// build compiles it out, so the flag does not exist in a player's install at
// all. devtools/run-harness.sh builds with -p:Harness=true to get it back.
#if HARNESS
using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Runs the lifecycle harness without a human, when the game is launched
    /// with <c>-shiftchange-harness</c>, then quits.
    ///
    /// <code>
    /// devtools/rimworld-profile.sh minimal
    /// open ".../RimWorldMac.app" --args -quicktest -shiftchange-harness
    /// #   ... wait for the process to exit, then read Player.log ...
    /// devtools/rimworld-profile.sh restore
    /// </code>
    ///
    /// <para><b>Why the flag exists.</b> Driving the harness by hand costs a
    /// menu hunt and a map click, and the menu's layout moves with the mod
    /// list, so the coordinates that work on one profile are wrong on the
    /// other. That is a small cost per run and a large one over a release, and
    /// it is exactly the sort of friction that stops a test being run at all.
    /// With this the whole loop is one command and a log read.</para>
    ///
    /// <para><b>What it costs a player: nothing, because it is not there.</b>
    /// Release compiles this file out, so a shipped assembly carries no
    /// postfix on <c>FinalizeInit</c> and no flag to pass. It is a Harmony
    /// postfix rather than a <c>GameComponent</c> for a second reason that
    /// still governs the harness build: components are scribed into every save
    /// (<c>Game.ExposeData</c>, <c>LookMode.Deep</c>), and a dev-only feature
    /// has no business appearing in a save file at all.</para>
    ///
    /// <para><b>It quits when it is done</b>, pass or fail. The point is a
    /// loop that terminates on its own so a caller can wait on the process and
    /// then read the log, rather than polling a screen.</para>
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    public static class Patch_HarnessAutoRun
    {
        internal const string Arg = "shiftchange-harness";

        /// <summary>
        /// One run per process.
        ///
        /// <c>FinalizeInit</c> is not once-per-launch: the save/load round-trip
        /// cases load a game in-process through
        /// <c>SavedGameLoaderNow.LoadGameFromSaveFileNow</c>, and
        /// <c>Game.LoadGame</c> calls <c>FinalizeInit</c> at the end of it
        /// (<c>Game.cs:636</c>). Without this latch the harness starts a nested
        /// run from inside its own round-trip case, and the process quits on
        /// whichever finishes first.
        /// </summary>
        internal static bool started;

        public static void Postfix()
        {
            if (!GenCommandLine.CommandLineArgPassed(Arg) || started)
            {
                return;
            }
            started = true;
            // FinalizeInit runs inside map generation's long event. Deferring
            // means the harness spawns into a map that has finished setting
            // itself up, which is the state a human clicking the debug action
            // would find.
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
                // Never let a throw here leave the process alive: an automated
                // caller waiting on exit would hang forever on a game sitting
                // at a map nobody is looking at.
                Log.Error("[ShiftChange] -" + Arg + " threw: " + e);
            }

            // One line, one shape, greppable. The caller reads this rather
            // than parsing the per-assertion report above it.
            Log.Message("[ShiftChange] harness auto-run: " + (passed ? "PASSED" : "FAILED"));
            Root.Shutdown();
        }

        /// <summary>
        /// Somewhere the fixture can stand. Prefers the map centre so runs are
        /// comparable, and only wanders if the centre is unusable — the pad is
        /// cleared before use, so the bar is "in bounds and not water", not
        /// "empty".
        ///
        /// <para><b>The one-cell MARGIN has to have rooms in it too</b>
        /// (2026-09-18). Cases reach past the pad they were given: the
        /// exterior-wall case counts the ring around a wall built on the pad's
        /// edge, and that ring includes cells the clear never touched. Natural
        /// rock there carries no room, <c>GetRoom</c> returns null, and the
        /// case's geometry silently stops being the geometry it was written
        /// for. Rejecting such an origin costs nothing on an ordinary map —
        /// outdoor cells all belong to one enormous room — and it keeps runs
        /// comparable, which is what the map-centre preference was already
        /// reaching for. The case clears its own margin as well; this is the
        /// half that also covers a pad chosen by hand from the debug menu
        /// landing somewhere silly.</para>
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
