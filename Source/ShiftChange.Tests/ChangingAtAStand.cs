using System;
using System.Collections;
using DevTools.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// A colonist changing at a stand, on the game's own clock (<see cref="GameClock"/>): the
    /// first tests that tick the whole game instead of one pawn, and the first written to be read
    /// by a player (<see cref="Story"/>).
    ///
    /// <para><see cref="IntoUniformAndBack"/> repeats the pumped
    /// <see cref="HarnessDriverAndOwners.DriverRoundTrip"/> check for check, so one run gives
    /// both verdicts side by side. <see cref="WalksToTheStand"/> is what the pump cannot do: a
    /// pawn ticked alone never gets a path, so the pumped cases stage their colonist on the
    /// stand's interaction cell.</para>
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.ChangingAtAStand)]
    internal class ChangingAtAStand : HarnessFixture
    {
        /// <summary>
        /// The most ticks one change may take: a walk across the pad, the garments' equip
        /// delays, and room to spare. A change that outlasts it fails with where it got stuck.
        /// </summary>
        internal const int MostTicks = 2500;

        [Test]
        [TestDescription("on the game's clock, a colonist changes into a stand's duster and back, "
                         + "and their forced parka comes back still forced")]
        internal IEnumerator IntoUniformAndBack()
        {
            Story story = new Story(Sentence(nameof(IntoUniformAndBack)));
            Fixture fix = null;
            try
            {
                yield return ReadyPad();
                if (Map == null)
                {
                    yield break;
                }
                fix = StageFor(story, StageKit.Displacing);
                if (fix == null)
                {
                    yield break;
                }
                string name = fix.Pawn.LabelShort;
                Apparel parka = Worn(fix.Pawn, "Apparel_Parka");
                if (!story.Check(parka != null, name + " wears a parka the duster displaces"))
                {
                    yield break;
                }
                // The player's choice, which a change must not downgrade to policy-managed.
                fix.Pawn.outfits.forcedHandler.SetForced(parka, forced: true);
                story.Note(name + "'s parka is forced, as a player sets it in the gear tab");

                SwapLeg on = new SwapLeg();
                yield return Swap(fix, on);
                story.Check(on.Succeeded, on.Told(name + " changes into the duster"));
                story.Check(fix.Comp.Borrower == fix.Pawn, "the stand lends its duster to " + name);
                story.Check(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                            name + " is on shift at this stand");
                story.Check(parka.ParentHolder == fix.Stand, "the parka hangs on the stand");
                story.Check(fix.Comp.StoredOwnerApparelForReading.Contains(parka),
                            "which keeps it for " + name);
                story.Check(fix.Comp.WasForcedWhenStored(parka), "and remembers it was forced");

                SwapLeg off = new SwapLeg();
                yield return Swap(fix, off);
                story.Check(off.Succeeded, off.Told(name + " changes back"));
                story.Check(fix.Pawn.apparel.WornApparel.Contains(parka), "the parka is back on");
                story.Check(fix.Pawn.outfits.forcedHandler.IsForced(parka),
                            "still forced, as the player left it");
                story.Check(!fix.Comp.OnShift, "the stand is free again");
                story.Check(CompShiftStand.OnShiftStandFor(fix.Pawn) == null, name + " is off shift");
            }
            finally
            {
                Teardown(fix, Map, Pad);
                story.Tell();
            }
        }

        [Test]
        [TestDescription("on the game's clock, a colonist walks over to a stand to change, "
                         + "and walks back to it to change back")]
        internal IEnumerator WalksToTheStand()
        {
            Story story = new Story(Sentence(nameof(WalksToTheStand)));
            Fixture fix = null;
            try
            {
                yield return ReadyPad();
                if (Map == null)
                {
                    yield break;
                }
                fix = StageFor(story, StageKit.Displacing);
                if (fix == null)
                {
                    yield break;
                }
                string name = fix.Pawn.LabelShort;
                Apparel parka = Worn(fix.Pawn, "Apparel_Parka");
                IntVec3 across = new IntVec3(Pad.maxX - 1, 0, Pad.maxZ - 1);
                if (!story.Check(parka != null && across.Standable(Map),
                                 name + " wears a parka, and there is room to stand across the pad"))
                {
                    yield break;
                }

                MoveTo(fix.Pawn, across);
                story.Note(name + " stands " + Distance(fix, across) + " cells from the stand");
                SwapLeg on = new SwapLeg();
                yield return Swap(fix, on);
                story.Check(on.Succeeded, on.Told(name + " walks over and changes into the duster"));
                story.Check(fix.Pawn.Position == fix.Stand.InteractionCell, name + " is at the stand");
                story.Check(parka.ParentHolder == fix.Stand, "the parka hangs on the stand");

                MoveTo(fix.Pawn, across);
                SwapLeg off = new SwapLeg();
                yield return Swap(fix, off);
                story.Check(off.Succeeded, off.Told(name + " walks back and changes out of it"));
                story.Check(fix.Pawn.apparel.WornApparel.Contains(parka), "the parka is back on");
                story.Check(fix.Stand.HeldItems.Count == 1 && fix.Stand.HeldItems[0].def.defName == "Apparel_Duster",
                            "and the duster is back on the stand");
            }
            finally
            {
                Teardown(fix, Map, Pad);
                story.Tell();
            }
        }

        /// <summary>
        /// Stages a stand and a colonist on the pad (<see cref="Stage"/>), retrying a build that
        /// throws as the pumped cases do (<see cref="BuildAttempts"/>), each retry noted. Null,
        /// the test failed, when none could be built.
        /// </summary>
        internal Fixture StageFor(Story story, StageKit kit)
        {
            Exception last = null;
            for (int attempt = 1; attempt <= BuildAttempts; attempt++)
            {
                try
                {
                    Fixture fix = Stage(Map, Pad, kit);
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
