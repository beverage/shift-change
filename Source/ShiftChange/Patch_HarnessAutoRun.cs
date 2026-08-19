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
    /// <para><b>Why this ships.</b> Driving the harness by hand costs a menu
    /// hunt and a map click, and the menu's layout moves with the mod list, so
    /// the coordinates that work on one profile are wrong on the other. That
    /// is a small cost per run and a large one over a release, and it is
    /// exactly the sort of friction that stops a test being run at all. With
    /// this the whole loop is one command and a log read.</para>
    ///
    /// <para><b>What it costs a player.</b> One
    /// <c>CommandLineArgPassed</c> string comparison, once, when a game
    /// finishes loading. Nothing else in here runs without the flag, and the
    /// flag is not something anyone sets by accident. It is a Harmony postfix
    /// rather than a <c>GameComponent</c> deliberately: components are scribed
    /// into every save (<c>Game.ExposeData</c>, <c>LookMode.Deep</c>), and a
    /// dev-only feature has no business appearing in a player's save file.</para>
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
        /// </summary>
        internal static bool TryFindPad(Map map, out IntVec3 origin)
        {
            int size = DebugTools_LifecycleHarness.PadSize;
            Predicate<IntVec3> usable = c =>
                new CellRect(c.x, c.z, size, size).InBounds(map)
                && c.Standable(map)
                && !c.GetTerrain(map).IsWater;

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
