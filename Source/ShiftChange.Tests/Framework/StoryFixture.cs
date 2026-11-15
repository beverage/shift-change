using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DevTools;
using DevTools.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// What the tests written as stories share: the frame each runs in (<see cref="Tell"/>), which
    /// opens its <see cref="Story"/> with the test's sentence and always cleans up after it; the
    /// ways to set a test up (<see cref="Staged(Action{Scene, Story}, StageKit, Gender, string)"/>
    /// and its siblings, <see cref="OnThePad"/>, <see cref="Unstaged"/>); <see cref="Swap"/>, one
    /// change at the stand with the game's clock stepped until it ends (<see cref="GameClock"/>),
    /// and <see cref="SwapNow"/>, one within a frame; and the checks and set-up many tests share.
    /// </summary>
    internal abstract class StoryFixture : ShiftChangeFixture
    {
        /// <summary>
        /// The most ticks one change may take: a walk across the pad, the garments' equip
        /// delays, and room to spare. A change that outlasts it fails with where it got stuck.
        /// </summary>
        internal const int MostTicks = 2500;

        /// <summary>What the running test set up on the pad, for <see cref="Tell"/> to clear.</summary>
        internal Scene CurrentScene;

        /// <summary>
        /// The map the pad was found on, held weakly so that a replaced game is not kept alive,
        /// and the pad, reused until the map changes.
        /// </summary>
        internal static readonly System.WeakReference<Map> PadFoundOn =
            new System.WeakReference<Map>(null);
        internal static CellRect FoundPad;

        /// <summary>
        /// The map and pad the running test was handed: DevTools' map and the pad on it, or, for a
        /// test that needs neither, the current map and an empty pad. Test bodies read them once
        /// they are set.
        /// </summary>
        internal Map Map;

        internal CellRect Pad;

        /// <summary>
        /// Finds the current map's pad, looks the camera away from it and sends away any pawn the
        /// game brought in (<see cref="SendAwayStrays"/>), then sets <see cref="Map"/> and
        /// <see cref="Pad"/>; leaves <see cref="Map"/> null, the test failed with the reason, when
        /// there is no map, no pad, or the camera still sees it.
        /// </summary>
        internal IEnumerator ReadyPad()
        {
            Map = null;
            Pad = CellRect.Empty;
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Test.Fail("no current map");
                yield break;
            }
            if (!PadFoundOn.TryGetTarget(out Map padMap) || padMap != map)
            {
                if (!TryFindPad(map, out IntVec3 origin))
                {
                    Test.Fail("no clear " + PadSize + "×" + PadSize + " pad on DevTools' map");
                    yield break;
                }
                PadFoundOn.SetTarget(map);
                FoundPad = new CellRect(origin.x, origin.z, PadSize, PadSize);
                IntVec3 centre = map.Center - new IntVec3(PadSize / 2, 0, PadSize / 2);
                DevLog.Write("[ShiftChange] pad at (" + origin.x + ", " + origin.z
                             + (origin == centre ? "), the map centre" : "), not the map centre"));
            }
            CellRect pad = FoundPad;

            // Thing.DoTick gives a pawn the camera can see its interval tick every one to five
            // ticks instead of every fifteen (GenTicks.GetCameraUpdateRate), on the game's clock as
            // in play, which changes whose needs fire and how far a job gets. DevTools centres the
            // camera on the map, where the pad is, so look away first. The camera's view rect is
            // cached per frame, so the move shows only after one.
            Find.CameraDriver.JumpToCurrentMapLoc(new IntVec3(1, 0, 1));
            yield return null;
            yield return null;
            // Tests reach past the pad (the fixture check eight cells out), so keep a margin.
            if (Find.CameraDriver.CurrentViewRect.ExpandedBy(1).Overlaps(pad.ExpandedBy(10)))
            {
                Test.Fail("the camera still sees the pad at " + pad + " after looking away");
                yield break;
            }
            // After the frames above, in which the game runs and the storyteller with it.
            SendAwayStrays(map);

            Map = map;
            Pad = pad;
        }

        /// <summary>
        /// Removes every pawn on <paramref name="map"/>, before the test stages its own. The last
        /// test's tear-down removed that test's pawns, so any still here came from the game, and
        /// left in, they get to what a test set out for its own colonist first
        /// (docs/TESTING.md, "Five engine traps"):
        /// - The stranger in black. When a test kills or kidnaps its colonist, the colony has
        ///   lost its last one, and the storyteller sends the stranger 180 ticks later
        ///   (<c>StorytellerComp_Triggered</c>, <c>StrangerInBlackJoin</c>), who arrives while the
        ///   game runs between tests and stays until some test's clearing happens to remove
        ///   them. The stranger hauled the potatoes put out for
        ///   <c>HaulOnTheWayToABillKeepsTheUniformOn</c>'s crafter before the crafter was asked.
        /// - Wild animals, which the map spawns as its clock runs and which eat food left
        ///   outside. A pawn eating holds a claim on its food that no other pawn of any faction
        ///   can reserve past (<c>PhysicalInteractionReservationManager</c>), so the crafter could
        ///   not reserve the potatoes while sparrows ate them.
        /// Test.log names what was sent away.
        /// </summary>
        internal static void SendAwayStrays(Map map)
        {
            List<Pawn> strays = map.mapPawns.AllPawnsSpawned.ToList();
            if (strays.Count == 0)
            {
                return;
            }
            List<string> names = new List<string>();
            for (int i = 0; i < strays.Count; i++)
            {
                Pawn stray = strays[i];
                names.Add(stray.RaceProps.Humanlike ? stray.LabelShort + " (" + stray.KindLabel + ")"
                                                    : stray.KindLabel);
                CompShiftStand.OnShiftStands.Remove(stray);
                stray.Destroy();
            }
            DevLog.Write("[ShiftChange] sent away what the game brought onto DevTools' map: "
                         + string.Join(", ", names));
        }

        /// <summary>Runs a test's body; the save/load fixture runs it as a load runs.</summary>
        protected virtual void Wrap(Action runCase)
        {
            runCase();
        }

        /// <summary>The sentence a test's <c>TestDescription</c> opens its story with.</summary>
        internal static string SentenceOf(MethodInfo test)
        {
            return test?.GetCustomAttribute<TestDescriptionAttribute>()?.Value as string;
        }

        internal string Sentence(string test)
        {
            return test == null
                ? null
                : SentenceOf(GetType().GetMethod(test, BindingFlags.Instance | BindingFlags.Public
                                                       | BindingFlags.NonPublic));
        }

        /// <summary>
        /// Runs <paramref name="body"/> on a stand holding a duster and a colonist dressed per
        /// <paramref name="kit"/> on its interaction cell (<see cref="Stage"/>), for a test that
        /// steps the game's clock.
        /// </summary>
        internal IEnumerator Staged(Func<Scene, Story, IEnumerator> body,
                                    StageKit kit = StageKit.Displacing, Gender gender = Gender.Male,
                                    [CallerMemberName] string test = null)
        {
            return Tell(test, true, story => Within(StageFor(story, kit, gender), story, body));
        }

        /// <summary>
        /// Runs <paramref name="body"/> on a stand holding a duster and a colonist dressed per
        /// <paramref name="kit"/> on its interaction cell (<see cref="Stage"/>), within one frame.
        /// </summary>
        internal IEnumerator Staged(Action<Scene, Story> body,
                                    StageKit kit = StageKit.Displacing, Gender gender = Gender.Male,
                                    [CallerMemberName] string test = null)
        {
            return Tell(test, true, story => Within(StageFor(story, kit, gender), story, Now(body)));
        }

        /// <summary>
        /// Runs <paramref name="body"/>, within one frame, on what <paramref name="build"/> sets up
        /// on the pad, which <paramref name="description"/> says in the game's words. A build that
        /// throws is retried (<see cref="BuildAttempts"/>), each try noted; one that returns null
        /// cannot set the scene up, which fails the test.
        /// </summary>
        internal IEnumerator Staged(Func<Map, CellRect, Scene> build, string description,
                                    Action<Scene, Story> body, [CallerMemberName] string test = null)
        {
            return Tell(test, true, story => Within(BuildFor(story, build, description), story, Now(body)));
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
        internal IEnumerator Unstaged(Action<Story> body, [CallerMemberName] string test = null)
        {
            return Tell(test, false, story => Now(body, story));
        }

        /// <summary>
        /// The frame every story test runs in. Opens the story with the test's sentence, finds the
        /// pad when <paramref name="onPad"/>, and runs <paramref name="run"/>. Then, whatever
        /// happened: puts Keep colonists decent back as it was, clears what the test set up on the
        /// pad (<see cref="CurrentScene"/>), skips a test whose only trouble was a known gap, and
        /// writes the story to Test.log.
        /// </summary>
        internal IEnumerator Tell(string test, bool onPad, Func<Story, IEnumerator> run)
        {
            Story story = new Story(Sentence(test) ?? test);
            CurrentScene = null;
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
                if (CurrentScene != null)
                {
                    Teardown(CurrentScene, Map, Pad);
                    CurrentScene = null;
                }
                if (story.Gap && !story.Failing)
                {
                    Test.Skip("a known gap; the story is in Test.log");
                }
                story.Tell();
            }
        }

        /// <summary>
        /// Runs <paramref name="body"/> on <paramref name="scene"/>, null after a failed set-up.
        /// </summary>
        internal IEnumerator Within(Scene scene, Story story, Func<Scene, Story, IEnumerator> body)
        {
            if (scene == null)
            {
                yield break;
            }
            CurrentScene = scene;
            yield return body(scene, story);
        }

        /// <summary>A body that runs within one frame, as a step of the test's routine.</summary>
        internal Func<Scene, Story, IEnumerator> Now(Action<Scene, Story> body)
        {
            return (scene, story) => Now(s => body(scene, s), story);
        }

        /// <summary>
        /// Runs <paramref name="body"/> now, the way this fixture runs a test
        /// (<see cref="Wrap"/>: the save/load fixture runs it as the game runs a
        /// load). What it throws fails the test with the story so far, the exception its last
        /// line. A body the wrap put off, which would run after the test had ended, fails it.
        /// </summary>
        internal IEnumerator Now(Action<Story> body, Story story)
        {
            bool ran = false;
            Wrap(() =>
            {
                ran = true;
                try
                {
                    body(story);
                }
                catch (Exception e)
                {
                    story.Threw(e);
                }
            });
            if (!ran)
            {
                story.Check(false, "the test ran when it was started, not later");
            }
            yield break;
        }

        /// <summary>
        /// Sets a scene up with <paramref name="build"/>, retrying a build that throws as
        /// <see cref="StageFor"/> does. Null, the test failed, when none could be set up.
        /// </summary>
        internal Scene BuildFor(Story story, Func<Map, CellRect, Scene> build, string description)
        {
            Exception last = null;
            for (int attempt = 1; attempt <= BuildAttempts; attempt++)
            {
                try
                {
                    Scene scene = build(Map, Pad);
                    if (scene != null)
                    {
                        CurrentScene = scene;
                        story.Check(true, description);
                        return scene;
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
                ? description + ": could not be set up"
                : description + ": could not be set up in " + BuildAttempts + " tries, last: " + last);
            return null;
        }

        /// <summary>
        /// Stages a stand and a colonist on the pad, retrying a build that throws
        /// (<see cref="BuildAttempts"/>), each retry noted. Null, the test failed, when none could
        /// be built.
        /// </summary>
        internal Scene StageFor(Story story, StageKit kit, Gender gender)
        {
            Exception last = null;
            for (int attempt = 1; attempt <= BuildAttempts; attempt++)
            {
                try
                {
                    Scene scene = Stage(Map, Pad, kit, gender: gender);
                    if (scene != null)
                    {
                        CurrentScene = scene;
                        story.Check(true, "a stand holding a duster, and " + scene.Colonist.LabelShort
                                          + " beside it in a shirt, trousers"
                                          + (kit == StageKit.Displacing ? " and a parka" : ""));
                        return scene;
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
        internal static bool StockOnly(Story story, Scene scene, string defName, string what)
        {
            ClearStand(scene.OutfitStand);
            return story.Check(StockOne(scene.OutfitStand, defName), "the stand holds " + what + " and nothing else");
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

        internal static int Distance(Scene scene, IntVec3 from)
        {
            return (int)Math.Round(from.DistanceTo(scene.OutfitStand.InteractionCell));
        }

        /// <summary>
        /// One change at the stand, through the real driver on the colonist's own job tracker,
        /// with the game's clock stepped until that driver ends. Watches the driver, not the job,
        /// since jobs are pooled, and records how it ended, since a driver also ends when its job
        /// fails (docs/TESTING.md, "Five engine traps").
        /// </summary>
        internal static IEnumerator Swap(Scene scene, SwapLeg leg)
        {
            Job swap = JobMaker.MakeJob(ShiftChangeDefOf.ShiftChange_SwapAtStand, scene.OutfitStand);
            scene.Colonist.jobs.StartJob(swap, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null,
                JobTag.ChangingApparel);
            JobDriver started = scene.Colonist.jobs.curDriver;
            if (started == null)
            {
                leg.Why = "the change never started";
                yield break;
            }
            started.AddFinishAction(c => leg.Ended = c);

            yield return GameClock.Run(() => scene.Colonist.jobs?.curDriver != started, MostTicks, leg.Clock);

            if (!leg.Clock.Done)
            {
                leg.Why = "still at it after " + MostTicks + " ticks, at step " + (started.CurToilIndex + 1)
                          + (scene.Colonist.pather.Moving ? ", walking" : "")
                          + ", " + Distance(scene, scene.Colonist.Position) + " cells from the stand";
            }
            else if (leg.Ended != JobCondition.Succeeded)
            {
                // What the colonist did instead, in the words the game shows under their portrait.
                JobDriver next = scene.Colonist.jobs?.curDriver;
                leg.Why = "it " + Said(leg.Ended) + " at step " + (started.CurToilIndex + 1) + ", "
                          + Distance(scene, scene.Colonist.Position) + " cells from the stand; then "
                          + (next == null ? "nothing" : next.GetReport().TrimEnd('.').UncapitalizeFirst())
                          + (next?.job?.jobGiver == null ? "" : " (" + next.job.jobGiver.GetType().Name + ")");
            }
        }

        /// <summary>One change, stepped to its end, with a line in the story for how it went.</summary>
        internal static IEnumerator Swap(Scene scene, Story story, string what, SwapLeg leg = null)
        {
            leg = leg ?? new SwapLeg();
            yield return Swap(scene, leg);
            story.Check(leg.Succeeded, leg.Outcome(what));
        }

        /// <summary>
        /// One change at the stand within this frame, the game's clock stepped until it ends
        /// (<see cref="SceneKit.RunSwap"/>), for a test whose checks follow
        /// in the same frame. False when it gave up, with notes on where it got stuck or what took
        /// the colonist instead; the caller's check says what failed.
        /// </summary>
        internal static bool SwapNow(Story story, Scene scene, int maxTicks = 5000)
        {
            return RunSwap(scene, story.Note, maxTicks);
        }

        /// <summary>
        /// Checks that the job a colonist takes after a change does not send them straight back to
        /// the stand, then clears what the change left (<see cref="ClearSwapDecision"/>). As a
        /// change succeeds the job tracker starts a one-tick pause (<c>Wait_MaintainPosture</c>)
        /// before the queue gets a turn, so the pause falls between every change and the job
        /// deferred behind it. Judged as a job of its own it would send a radius sleep stand's
        /// colonist straight back to change, so it has to ride along. Clearing ends a change that
        /// did start, which would answer every later ask with <c>MidSwap</c>.
        /// </summary>
        internal static bool NotSentStraightBack(Story story, Scene scene)
        {
            JobDef next = scene.Colonist.CurJobDef;
            bool ok = story.Check(next != ShiftChangeDefOf.ShiftChange_SwapAtStand,
                                  scene.Colonist.LabelShort + "'s next job, " + (next == null ? "none" : next.label)
                                  + ", does not send them straight back to the stand");
            ClearSwapDecision(scene.Colonist);
            return ok;
        }

        /// <summary>
        /// Another colonist, beside the stand, cleared with the scene. Spawning is retried as a
        /// scene's set-up is (<see cref="BuildAttempts"/>): on a large mod list it can throw
        /// <c>Collection was modified</c> in <c>Pawn_HealthTracker.Notify_Spawned</c>, under
        /// other mods' postfixes on <c>Pawn.SpawnSetup</c>. Each retry is noted, and the last
        /// try's exception fails the test.
        /// </summary>
        internal static Pawn AnotherColonist(Story story, Scene scene, Gender gender, string nick)
        {
            return AnotherColonist(story, scene, gender, nick,
                                   scene.OutfitStand.Position + new IntVec3(2, 0, scene.Extras.Count + 1),
                                   "beside the stand");
        }

        /// <summary>
        /// <see cref="AnotherColonist(Story, Scene, Gender, string)"/> on a given cell, which
        /// <paramref name="where"/> says in the game's words.
        /// </summary>
        internal static Pawn AnotherColonist(Story story, Scene scene, Gender gender, string nick,
                                             IntVec3 cell, string where)
        {
            for (int attempt = 1; ; attempt++)
            {
                Pawn pawn = DebugTools_Fixtures.AveragePawn(gender, nick);
                pawn.workSettings?.EnableAndInitialize();
                try
                {
                    GenSpawn.Spawn(pawn, cell, scene.Map, Rot4.North);
                    scene.Extras.Add(pawn);
                    story.Note(pawn.LabelShort + (gender == Gender.Female ? ", a woman," : ", a man,")
                               + " joins the colony " + where);
                    return pawn;
                }
                catch (Exception e) when (attempt < BuildAttempts)
                {
                    story.Note("spawning " + nick + " threw on try " + attempt + ": "
                               + e.GetType().Name + ": " + e.Message);
                    try
                    {
                        // Likely half spawned: clear it before the retry lands on the same cell.
                        if (!pawn.Destroyed)
                        {
                            pawn.Destroy();
                        }
                    }
                    catch (Exception)
                    {
                        // The scene's teardown clears the pad whatever is left on it.
                    }
                }
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
        internal string Outcome(string what)
        {
            return Succeeded
                ? what + " (" + Clock.Ticks + " ticks)"
                : what + ": " + Why;
        }
    }
}
