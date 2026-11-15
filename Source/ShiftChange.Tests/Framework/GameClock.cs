using System;
using System.Collections;
using HarmonyLib;
using Verse;

namespace ShiftChange.Tests
{
    /// <summary>
    /// The game's own clock, stepped by a test: <c>TickManager.DoSingleTick</c>, one tick at a
    /// time, so every map, pawn, comp and the pathfinder tick as they do in play. A pawn ticked on
    /// its own (<c>Pawn.DoTick</c>) gets none of that: path requests are served in
    /// <c>Map.MapPreTick</c>, and needs read <c>TicksGame</c>, which only the clock moves
    /// (docs/TESTING.md, "Five engine traps").
    ///
    /// <para>Stepped rather than let run at speed, for an exact count: at speed a frame runs as
    /// many ticks as it likes. The game's own speed is set to paused while it steps, so no frame
    /// adds ticks of its own, and put back after. <see cref="Run"/> lets a frame pass every
    /// <see cref="TicksPerFrame"/> ticks, so the window and DevTools stay alive;
    /// <see cref="Step"/> runs within the caller's frame, as the game itself runs many ticks in
    /// one frame at its higher speeds, for the tests that run inside one frame.</para>
    ///
    /// <para>While it steps, the storyteller and random mental breaks are off, so no raid or
    /// tantrum lands in a test; both are put back as they were. Autosave is DevTools' to hold
    /// (SmashPhil/DevTools#18 skips the game's autosave tick while tests run), so that no test
    /// world is written into the player's Saves folder.</para>
    /// </summary>
    internal static class GameClock
    {
        internal const int TicksPerFrame = 250;

        // Set through the field: the speed setter and Pause refuse, without a word, while a new
        // game fades in, which DevTools' game may still do (Fine Wine's Clock, the same finding).
        internal static readonly AccessTools.FieldRef<TickManager, TimeSpeed> Speed =
            AccessTools.FieldRefAccess<TickManager, TimeSpeed>("curTimeSpeed");

        /// <summary>
        /// Steps the clock until <paramref name="done"/> holds after a tick, or
        /// <paramref name="most"/> ticks have run, and records which in <paramref name="run"/>,
        /// letting a frame pass every <see cref="TicksPerFrame"/> ticks.
        /// </summary>
        internal static IEnumerator Run(Func<bool> done, int most, ClockRun run)
        {
            using (Held held = new Held())
            {
                while (!run.Done && run.Ticks < most)
                {
                    for (int i = 0; i < TicksPerFrame && run.Ticks < most; i++)
                    {
                        held.Tick();
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
        }

        /// <summary>
        /// Steps the clock within this frame until <paramref name="done"/> holds after a tick, or
        /// <paramref name="most"/> ticks have run. <paramref name="hold"/> runs before every tick
        /// and after the last, so what it puts back is as the caller arranged it whenever anything
        /// outside the tick reads it.
        /// </summary>
        internal static ClockRun Step(Func<bool> done, int most, Action hold = null)
        {
            ClockRun run = new ClockRun();
            using (Held held = new Held())
            {
                while (run.Ticks < most)
                {
                    hold?.Invoke();
                    held.Tick();
                    run.Ticks++;
                    if (done())
                    {
                        run.Done = true;
                        break;
                    }
                }
                hold?.Invoke();
            }
            return run;
        }

        /// <summary>
        /// The game paused, and the storyteller and random mental breaks off, while a test steps
        /// the clock, all put back as they were on <see cref="Dispose"/>.
        /// </summary>
        internal sealed class Held : IDisposable
        {
            internal readonly TickManager Clock = Find.TickManager;
            internal readonly TimeSpeed Speed = GameClock.Speed(Find.TickManager);
            internal readonly bool Storyteller = DebugSettings.enableStoryteller;
            internal readonly bool MentalBreaks = DebugSettings.enableRandomMentalStates;

            internal Held()
            {
                DebugSettings.enableStoryteller = false;
                DebugSettings.enableRandomMentalStates = false;
                GameClock.Speed(Clock) = TimeSpeed.Paused;
            }

            internal void Tick()
            {
                Clock.DoSingleTick();
            }

            public void Dispose()
            {
                DebugSettings.enableStoryteller = Storyteller;
                DebugSettings.enableRandomMentalStates = MentalBreaks;
                if (Find.TickManager == Clock)
                {
                    GameClock.Speed(Clock) = Speed;
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
