using System;
using System.Collections;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// What the tests on the game's clock share: <see cref="Staged"/>, which sets a stand and a
    /// colonist up on the pad, runs a test's body and always cleans up, and <see cref="Swap"/>,
    /// one change at the stand with the clock stepped until it ends (<see cref="GameClock"/>).
    /// Every check goes in the test's <see cref="Story"/>.
    /// </summary>
    internal abstract class ClockFixture : HarnessFixture
    {
        /// <summary>
        /// The most ticks one change may take: a walk across the pad, the garments' equip
        /// delays, and room to spare. A change that outlasts it fails with where it got stuck.
        /// </summary>
        internal const int MostTicks = 2500;

        /// <summary>
        /// Runs <paramref name="body"/> on a stand holding a duster and a colonist dressed per
        /// <paramref name="kit"/> on its interaction cell (<see cref="Stage"/>), then clears the
        /// pad, puts Keep colonists decent back as it was, and writes the story to Test.log,
        /// whatever happened. The story opens with the test's <c>TestDescription</c>.
        /// </summary>
        internal IEnumerator Staged(Func<Fixture, Story, IEnumerator> body,
                                    StageKit kit = StageKit.Displacing, Gender gender = Gender.Male,
                                    [CallerMemberName] string test = null)
        {
            Story story = new Story(Sentence(test) ?? test);
            Fixture fix = null;
            bool decent = ShiftChangeMod.Settings != null && ShiftChangeMod.Settings.keepColonistsDecent;
            try
            {
                if (Sentence(test) == null)
                {
                    story.Check(false, test + " has a TestDescription, the sentence its story opens with");
                    yield break;
                }
                yield return ReadyPad();
                if (Map == null)
                {
                    yield break;
                }
                fix = StageFor(story, kit, gender);
                if (fix != null)
                {
                    yield return body(fix, story);
                }
            }
            finally
            {
                if (ShiftChangeMod.Settings != null)
                {
                    ShiftChangeMod.Settings.keepColonistsDecent = decent;
                }
                Teardown(fix, Map, Pad);
                story.Tell();
            }
        }

        /// <summary>
        /// Stages a stand and a colonist on the pad, retrying a build that throws as the pumped
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
