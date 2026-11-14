using System;
using System.Collections;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange.Tests
{
    /// <summary>
    /// The game's own clock, stepped by a test: <c>TickManager.DoSingleTick</c>, one tick at a
    /// time, so every map, pawn, comp and the pathfinder tick as they do in play. A pawn ticked on
    /// its own (<c>Pawn.DoTick</c>) gets none of that: path requests are served in
    /// <c>Map.MapPreTick</c>, and needs read <c>TicksGame</c>, which only the clock moves
    /// (docs/TESTING.md, "Four engine traps").
    ///
    /// <para>Stepped rather than let run at speed, for an exact count: at speed a frame runs as
    /// many ticks as it likes. The game's own speed is set to paused while it steps, so no frame
    /// adds ticks of its own, and put back after. A frame passes every
    /// <see cref="TicksPerFrame"/> ticks, so the window and DevTools stay alive.</para>
    ///
    /// <para>While it steps, the storyteller and random mental breaks are off, so no raid or
    /// tantrum lands in a test, and the autosave counter is held at zero, so no autosave is
    /// written into the player's Saves folder. All three are put back as they were, the autosave
    /// counter too, so the player's next autosave comes when it would have.</para>
    /// </summary>
    internal static class GameClock
    {
        internal const int TicksPerFrame = 250;

        // Set through the field: the speed setter and Pause refuse, without a word, while a new
        // game fades in, which DevTools' game may still do (Fine Wine's Clock, the same finding).
        internal static readonly AccessTools.FieldRef<TickManager, TimeSpeed> Speed =
            AccessTools.FieldRefAccess<TickManager, TimeSpeed>("curTimeSpeed");

        internal static readonly AccessTools.FieldRef<Autosaver, int> TicksSinceSave =
            AccessTools.FieldRefAccess<Autosaver, int>("ticksSinceSave");

        /// <summary>
        /// Steps the clock until <paramref name="done"/> holds after a tick, or
        /// <paramref name="most"/> ticks have run, and records which in <paramref name="run"/>.
        /// </summary>
        internal static IEnumerator Run(Func<bool> done, int most, ClockRun run)
        {
            TickManager clock = Find.TickManager;
            TimeSpeed speed = Speed(clock);
            int sinceSave = TicksSinceSave(Find.Autosaver);
            bool storyteller = DebugSettings.enableStoryteller;
            bool mentalBreaks = DebugSettings.enableRandomMentalStates;
            DebugSettings.enableStoryteller = false;
            DebugSettings.enableRandomMentalStates = false;
            Speed(clock) = TimeSpeed.Paused;
            try
            {
                while (!run.Done && run.Ticks < most)
                {
                    TicksSinceSave(Find.Autosaver) = 0;
                    for (int i = 0; i < TicksPerFrame && run.Ticks < most; i++)
                    {
                        clock.DoSingleTick();
                        run.Ticks++;
                        if (done())
                        {
                            run.Done = true;
                            break;
                        }
                    }
                    if (!run.Done)
                    {
                        yield return null;
                    }
                }
            }
            finally
            {
                DebugSettings.enableStoryteller = storyteller;
                DebugSettings.enableRandomMentalStates = mentalBreaks;
                if (Find.TickManager == clock)
                {
                    Speed(clock) = speed;
                    TicksSinceSave(Find.Autosaver) = sinceSave;
                }
            }
        }
    }

    /// <summary>How a <see cref="GameClock.Run"/> went: ticks stepped, and whether it got there.</summary>
    internal sealed class ClockRun
    {
        internal int Ticks;
        internal bool Done;
    }
}
