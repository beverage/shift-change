using System;
using System.Collections;
using System.Runtime.CompilerServices;
using DevTools.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// What the tests written as stories share: the frame each runs in (<see cref="Tell"/>), which
    /// opens its <see cref="Story"/> with the test's sentence and always cleans up after it; the
    /// ways to set a test up (<see cref="Staged(Action{Fixture, Story}, StageKit, Gender, string)"/>
    /// and its siblings, <see cref="OnThePad"/>, <see cref="Told"/>); and <see cref="Swap"/>, one
    /// change at the stand with the game's clock stepped until it ends (<see cref="GameClock"/>).
    /// </summary>
    internal abstract class StoryFixture : HarnessFixture
    {
        /// <summary>
        /// The most ticks one change may take: a walk across the pad, the garments' equip
        /// delays, and room to spare. A change that outlasts it fails with where it got stuck.
        /// </summary>
        internal const int MostTicks = 2500;

        /// <summary>What the running test set up on the pad, for <see cref="Tell"/> to clear.</summary>
        internal Fixture Scene;

        /// <summary>
        /// Runs <paramref name="body"/> on a stand holding a duster and a colonist dressed per
        /// <paramref name="kit"/> on its interaction cell (<see cref="Stage"/>), for a test that
        /// steps the game's clock.
        /// </summary>
        internal IEnumerator Staged(Func<Fixture, Story, IEnumerator> body,
                                    StageKit kit = StageKit.Displacing, Gender gender = Gender.Male,
                                    [CallerMemberName] string test = null)
        {
            return Tell(test, true, story => Within(StageFor(story, kit, gender), story, body));
        }

        /// <summary>
        /// Runs <paramref name="body"/> on a stand holding a duster and a colonist dressed per
        /// <paramref name="kit"/> on its interaction cell (<see cref="Stage"/>), within one frame.
        /// </summary>
        internal IEnumerator Staged(Action<Fixture, Story> body,
                                    StageKit kit = StageKit.Displacing, Gender gender = Gender.Male,
                                    [CallerMemberName] string test = null)
        {
            return Tell(test, true, story => Within(StageFor(story, kit, gender), story, Now(body)));
        }

        /// <summary>
        /// Runs <paramref name="body"/>, within one frame, on what <paramref name="build"/> sets up
        /// on the pad, which <paramref name="scene"/> says in the game's words. A build that throws
        /// is retried (<see cref="BuildAttempts"/>), each try noted; one that returns null cannot
        /// set the scene up, which fails the test.
        /// </summary>
        internal IEnumerator Staged(Func<Map, CellRect, Fixture> build, string scene,
                                    Action<Fixture, Story> body, [CallerMemberName] string test = null)
        {
            return Tell(test, true, story => Within(BuildFor(story, build, scene), story, Now(body)));
        }

        /// <summary>
        /// Runs <paramref name="body"/> on the current map with the pad found, for a test that sets
        /// up what it needs itself and leaves nothing on the pad to clear.
        /// </summary>
        internal IEnumerator OnThePad(Action<Story> body, [CallerMemberName] string test = null)
        {
            return Tell(test, true, story => Now(body, story));
        }

        /// <summary>
        /// Runs <paramref name="body"/> with no pad and no camera move, for a test that reads the
        /// game's tables, or finds a place of its own.
        /// </summary>
        internal IEnumerator Told(Action<Story> body, [CallerMemberName] string test = null)
        {
            return Tell(test, false, story => Now(body, story));
        }

        /// <summary>
        /// The frame every story test runs in. Opens the story with the test's sentence, finds the
        /// pad when <paramref name="onPad"/>, and runs <paramref name="run"/>. Then, whatever
        /// happened: puts Keep colonists decent back as it was, clears what the test set up on the
        /// pad (<see cref="Scene"/>), skips a test whose only trouble was a known gap, and writes
        /// the story to Test.log.
        /// </summary>
        internal IEnumerator Tell(string test, bool onPad, Func<Story, IEnumerator> run)
        {
            Story story = new Story(Sentence(test) ?? test);
            Scene = null;
            bool decent = ShiftChangeMod.Settings != null && ShiftChangeMod.Settings.keepColonistsDecent;
            try
            {
                if (Sentence(test) == null)
                {
                    story.Check(false, test + " has a TestDescription, the sentence its story opens with");
                    yield break;
                }
                if (onPad)
                {
                    yield return ReadyPad();
                    if (Map == null)
                    {
                        yield break;
                    }
                }
                else
                {
                    Map = Find.CurrentMap;
                    Pad = CellRect.Empty;
                }
                yield return run(story);
            }
            finally
            {
                if (ShiftChangeMod.Settings != null)
                {
                    ShiftChangeMod.Settings.keepColonistsDecent = decent;
                }
                if (Scene != null)
                {
                    Teardown(Scene, Map, Pad);
                    Scene = null;
                }
                if (story.Gap && !story.Failing)
                {
                    Test.Skip("a known gap; the story is in Test.log");
                }
                story.Tell();
            }
        }

        /// <summary>Runs <paramref name="body"/> on <paramref name="fix"/>, which a failed set-up leaves null.</summary>
        internal IEnumerator Within(Fixture fix, Story story, Func<Fixture, Story, IEnumerator> body)
        {
            if (fix == null)
            {
                yield break;
            }
            Scene = fix;
            yield return body(fix, story);
        }

        /// <summary>A body that runs within one frame, as a step of the test's routine.</summary>
        internal static Func<Fixture, Story, IEnumerator> Now(Action<Fixture, Story> body)
        {
            return (fix, story) => Now(s => body(fix, s), story);
        }

        /// <summary>
        /// Runs <paramref name="body"/> now. What it throws fails the test with the story so far,
        /// the exception its last line.
        /// </summary>
        internal static IEnumerator Now(Action<Story> body, Story story)
        {
            try
            {
                body(story);
            }
            catch (Exception e)
            {
                story.Threw(e);
            }
            yield break;
        }

        /// <summary>
        /// Sets a scene up with <paramref name="build"/>, retrying a build that throws as
        /// <see cref="StageFor"/> does. Null, the test failed, when none could be set up.
        /// </summary>
        internal Fixture BuildFor(Story story, Func<Map, CellRect, Fixture> build, string scene)
        {
            Exception last = null;
            for (int attempt = 1; attempt <= BuildAttempts; attempt++)
            {
                try
                {
                    Fixture fix = build(Map, Pad);
                    if (fix != null)
                    {
                        Scene = fix;
                        story.Check(true, scene);
                        return fix;
                    }
                    break;
                }
                catch (Exception e)
                {
                    last = e;
                    story.Note("setting up threw on try " + attempt + ": " + e.GetType().Name + ": " + e.Message);
                    Teardown(null, Map, Pad);
                }
            }
            story.Check(false, last == null
                ? scene + ": could not be set up"
                : scene + ": could not be set up in " + BuildAttempts + " tries, last: " + last);
            return null;
        }

        /// <summary>
        /// Stages a stand and a colonist on the pad, retrying a build that throws as the harness
        /// cases do (<see cref="BuildAttempts"/>), each retry noted. Null, the test failed, when
        /// none could be built.
        /// </summary>
        internal Fixture StageFor(Story story, StageKit kit, Gender gender)
        {
            Exception last = null;
            for (int attempt = 1; attempt <= BuildAttempts; attempt++)
            {
                try
                {
                    Fixture fix = Stage(Map, Pad, kit, gender: gender);
                    if (fix != null)
                    {
                        Scene = fix;
                        story.Check(true, "a stand holding a duster, and " + fix.Pawn.LabelShort
                                          + " beside it in a shirt, trousers"
                                          + (kit == StageKit.Displacing ? " and a parka" : ""));
                        return fix;
                    }
                }
                catch (Exception e)
                {
                    last = e;
                    story.Note("setting the stand up threw on try " + attempt + ": "
                               + e.GetType().Name + ": " + e.Message);
                    Teardown(null, Map, Pad);
                }
            }
            story.Check(false, last == null
                ? "a stand and a colonist could be set up"
                : "a stand and a colonist could be set up in " + BuildAttempts + " tries, last: " + last);
            return null;
        }

        /// <summary>
        /// Turns Keep colonists decent on or off for this test; <see cref="Staged"/> puts it
        /// back. False, the test failed, when the mod's settings cannot be read.
        /// </summary>
        internal static bool SetDecent(Story story, bool on)
        {
            if (!story.Check(ShiftChangeMod.Settings != null, "Shift Change's settings can be read"))
            {
                return false;
            }
            ShiftChangeMod.Settings.keepColonistsDecent = on;
            story.Note("Keep colonists decent is " + (on ? "on" : "off"));
            return true;
        }

        /// <summary>Changes what the stand holds to the one garment named, or fails the test.</summary>
        internal static bool StockOnly(Story story, Fixture fix, string defName, string what)
        {
            ClearStand(fix.Stand);
            return story.Check(StockOne(fix.Stand, defName), "the stand holds " + what + " and nothing else");
        }

        internal static Apparel Worn(Pawn pawn, string defName)
        {
            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                if (apparel.def.defName == defName)
                {
                    return apparel;
                }
            }
            return null;
        }

        internal static void MoveTo(Pawn pawn, IntVec3 cell)
        {
            pawn.pather.StopDead();
            pawn.Position = cell;
            pawn.Notify_Teleported();
        }

        internal static int Distance(Fixture fix, IntVec3 from)
        {
            return (int)Math.Round(from.DistanceTo(fix.Stand.InteractionCell));
        }

        /// <summary>
        /// One change at the stand, through the real driver on the colonist's own job tracker,
        /// with the game's clock stepped until that driver ends. Watches the driver, not the job,
        /// since jobs are pooled, and records how it ended, since a driver also ends when its job
        /// fails (docs/TESTING.md, "Four engine traps").
        /// </summary>
        internal static IEnumerator Swap(Fixture fix, SwapLeg leg)
        {
            Job swap = JobMaker.MakeJob(ShiftChangeDefOf.ShiftChange_SwapAtStand, fix.Stand);
            fix.Pawn.jobs.StartJob(swap, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null,
                JobTag.ChangingApparel);
            JobDriver started = fix.Pawn.jobs.curDriver;
            if (started == null)
            {
                leg.Why = "the change never started";
                yield break;
            }
            started.AddFinishAction(c => leg.Ended = c);

            yield return GameClock.Run(() => fix.Pawn.jobs?.curDriver != started, MostTicks, leg.Clock);

            if (!leg.Clock.Done)
            {
                leg.Why = "still at it after " + MostTicks + " ticks, at step " + (started.CurToilIndex + 1)
                          + (fix.Pawn.pather.Moving ? ", walking" : "")
                          + ", " + Distance(fix, fix.Pawn.Position) + " cells from the stand";
            }
            else if (leg.Ended != JobCondition.Succeeded)
            {
                // What the colonist did instead, in the words the game shows under their portrait.
                JobDriver next = fix.Pawn.jobs?.curDriver;
                leg.Why = "it " + Said(leg.Ended) + " at step " + (started.CurToilIndex + 1) + ", "
                          + Distance(fix, fix.Pawn.Position) + " cells from the stand; then "
                          + (next == null ? "nothing" : next.GetReport().TrimEnd('.').UncapitalizeFirst())
                          + (next?.job?.jobGiver == null ? "" : " (" + next.job.jobGiver.GetType().Name + ")");
            }
        }

        /// <summary>One change, stepped to its end, with a line in the story for how it went.</summary>
        internal static IEnumerator Swap(Fixture fix, Story story, string what, SwapLeg leg = null)
        {
            leg = leg ?? new SwapLeg();
            yield return Swap(fix, leg);
            story.Check(leg.Succeeded, leg.Told(what));
        }

        internal static string Said(JobCondition ended)
        {
            switch (ended)
            {
                case JobCondition.Incompletable:
                    return "could not be finished (" + ended + ")";
                case JobCondition.InterruptForced:
                case JobCondition.InterruptOptional:
                    return "was interrupted (" + ended + ")";
                case JobCondition.Errored:
                case JobCondition.ErroredPather:
                    return "hit an error (" + ended + ")";
                default:
                    return "ended " + ended;
            }
        }
    }

    /// <summary>How one change went: the ticks it took, how its driver ended, and why not, if not.</summary>
    internal sealed class SwapLeg
    {
        internal readonly ClockRun Clock = new ClockRun();
        internal JobCondition Ended = JobCondition.None;
        internal string Why;

        internal bool Succeeded => Why == null && Clock.Done && Ended == JobCondition.Succeeded;

        /// <summary><paramref name="what"/>, with the ticks it took, or why it did not happen.</summary>
        internal string Told(string what)
        {
            return Succeeded
                ? what + " (" + Clock.Ticks + " ticks)"
                : what + ": " + Why;
        }
    }
}
