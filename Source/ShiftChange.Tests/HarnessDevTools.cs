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
using static ShiftChange.Tests.DebugTools_LifecycleHarness;

namespace ShiftChange.Tests
{
    /// <summary>
    /// The fixtures that play the game, in run order: DevTools runs a fixture with a higher
    /// <c>ExecutionPriority</c> first, and each runs at <see cref="HarnessDevTools.First"/> less its
    /// place here. <see cref="SavingAndLoading"/> runs after all of them
    /// (<see cref="HarnessDevTools.SaveLoadPriority"/>), so it has no place here.
    /// </summary>
    internal enum CaseGroup
    {
        /// <summary>What every test takes for granted about the colony it runs in, so first.</summary>
        TheTestColony,
        ChangingAtAStand,
        KeepingThemDecent,
        WhatSendsThemToChange,
        WhoUsesAStand,
        ServingARoomOrARing,
        ServingAnArea,
        KeepingTheirVacsuitOn,
        LosingAStandOrAColonist,
        ButtonsAndOrders,
        ReadingJobsAndRooms,
        FlyingTheGravship,
        HeadingIntoVacuum,
        MealsDangerAndDuties,
        AfterChangingBack,
        StayingInUniform,
        StoringWhatTheyMade,
        RidingAlong,
    }

    /// <summary>
    /// The priorities the fixtures that play the game run at, and the pad their tests share.
    /// </summary>
    internal static class HarnessDevTools
    {
        internal const int First = 100;

        /// <summary>
        /// The save/load fixture's priority: DevTools' lowest, so it runs after every fixture of
        /// its test type at a higher one, the default included. A tie keeps registration order.
        /// </summary>
        internal const int SaveLoadPriority = (int)DevTools.Testing.Priority.Last;

        /// <summary>
        /// The map the pad was found on, held weakly so that a replaced game is not kept alive,
        /// and the pad, reused until the map changes.
        /// </summary>
        internal static readonly System.WeakReference<Map> PadMap =
            new System.WeakReference<Map>(null);
        internal static CellRect Pad;
    }

    /// <summary>
    /// What every fixture shares: the pad its tests play on (<see cref="ReadyPad"/>), a test's
    /// sentence, and what every fixture allows, lines other mods log by themselves on a full
    /// list, which land in whichever test is running. Each passes only with that mod's own code
    /// on its stack, so the same text from this mod still fails. How a story test runs:
    /// <see cref="StoryFixture"/>.
    /// </summary>
    [WarningsAllowedFrom(HarnessFixture.MeleeAnimationFrame, HarnessFixture.MeleeAnimationLine)]
    [WarningsAllowedFrom(HarnessFixture.MapPreviewFrame, HarnessFixture.MapPreviewRandomness)]
    [WarningsAllowedFrom(HarnessFixture.ImmersiveFilterFrame, HarnessFixture.EngineStub)]
    [ErrorsAllowedFrom(HarnessFixture.EngineCallFrame, HarnessFixture.ImmersiveFilterNoWorld)]
    [WarningsAllowedFrom(HarnessFixture.MemeTraitFrame, HarnessFixture.TraitAlreadyHeld)]
    internal abstract class HarnessFixture
    {
        /// <summary>
        /// Melee Animation's late-load checks, which land in the first test: other mods' patches
        /// on the methods it patches, and weapons with no tweak data of its own.
        /// </summary>
        internal const string MeleeAnimationFrame =
            @"AM\.Core\.(LogPotentialConflicts|LoadAllTweakData)\(";

        /// <summary>Any warning with Melee Animation's tag, which it puts in colour.</summary>
        internal const string MeleeAnimationLine = @"^(<color=#\w+>)?\[MeleeAnim\]";

        /// <summary>Map Preview's check of each map component as a test's map is built.</summary>
        internal const string MapPreviewFrame =
            @"MapPreview\.Patches\.Patch_Verse_Map\.FillComponents_CheckRand";

        /// <summary>Its note that another mod's map component draws random numbers.</summary>
        internal const string MapPreviewRandomness =
            @"^\[Map Preview v[\d.]+\] Detected RNG usage in constructor of map component ";

        /// <summary>Immersive Filter's set-up of its screen filter on the first map.</summary>
        internal const string ImmersiveFilterFrame =
            @"Og_ImmersiveFilter\.OgImmersiveFilter\.(Start|EnsureFilterBuffer)\(";

        /// <summary>The engine's warning for a Unity call it leaves unimplemented.</summary>
        internal const string EngineStub =
            @"^\w+ \([^()]*\): Stub! You may not get expected results!";

        /// <summary>
        /// The engine running a component's frame method, which logs what that method throws under
        /// the component's class name, its own trace in the message.
        /// </summary>
        internal const string EngineCallFrame =
            @"Waylith\.UnityCompat\.UnityWorld\.InvokeMessage\(";

        /// <summary>Immersive Filter's update reading the world view between games.</summary>
        internal const string ImmersiveFilterNoWorld =
            @"^\[Og_ImmersiveFilter\.OgImmersiveFilter\] LateUpdate threw: "
            + @"System\.NullReferenceException[\s\S]*RimWorld\.Planet\.WorldRendererUtility\.";

        /// <summary>
        /// VE Framework giving a new colonist the traits its ideoligion's memes require, as a test
        /// generates one, whatever traits the colonist already rolled.
        /// </summary>
        internal const string MemeTraitFrame =
            @"VEF\.Memes\.VanillaExpandedFramework_Pawn_IdeoTracker_SetIdeo_Patch\."
            + @"ForceTraitAndAbilities\(";

        /// <summary>The game's warning when a pawn is given a trait it has.</summary>
        internal const string TraitAlreadyHeld = @"^[^\n]* already has trait [\w-]+$";

        /// <summary>
        /// The map and pad the running test was handed: DevTools' map and the pad on it, or, for a
        /// test that needs neither, the current map and an empty pad. Test bodies read them once
        /// they are set.
        /// </summary>
        internal Map Map;

        internal CellRect Pad;

        /// <summary>
        /// Finds the current map's pad and looks the camera away from it, then sets
        /// <see cref="Map"/> and <see cref="Pad"/>; leaves <see cref="Map"/> null, the test
        /// failed with the reason, when there is no map, no pad, or the camera still sees it.
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
            if (!HarnessDevTools.PadMap.TryGetTarget(out Map padMap) || padMap != map)
            {
                if (!TryFindPad(map, out IntVec3 origin))
                {
                    Test.Fail("no clear " + PadSize + "×" + PadSize + " pad on DevTools' map");
                    yield break;
                }
                HarnessDevTools.PadMap.SetTarget(map);
                HarnessDevTools.Pad = new CellRect(origin.x, origin.z, PadSize, PadSize);
                IntVec3 centre = map.Center - new IntVec3(PadSize / 2, 0, PadSize / 2);
                DevLog.Write("[ShiftChange] pad at (" + origin.x + ", " + origin.z
                             + (origin == centre ? "), the map centre" : "), not the map centre"));
            }
            CellRect pad = HarnessDevTools.Pad;

            // Thing.DoTick gives a pawn the camera can see its interval tick every one to five
            // ticks instead of every fifteen (GenTicks.GetCameraUpdateRate), on the game's clock
            // as in play, which changes whose needs fire and how far a job gets. DevTools centres the
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

            Map = map;
            Pad = pad;
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
    }

    /// <summary>
    /// Checks the tests DevTools registered from this assembly, which it runs in registry order:
    /// each fixture runs exactly the tests it declares, in declaration order, which DevTools keeps
    /// for up to 16 tests to a fixture; every fixture that plays the game runs at a priority of
    /// its own, <see cref="SavingAndLoading"/> at the lowest; and every test has a sentence of its
    /// own. It reads DevTools' registry and runs no test of its own, so it needs no game.
    /// </summary>
    [TestFixture(TestType.MainMenu)]
    internal class HarnessTestOrder : HarnessFixture
    {
        internal const int MostInOrder = 16;

        [Test]
        internal void EveryCaseRunsOnceInOrder()
        {
            if (TestRunner.Current == null)
            {
                Test.Fail("no DevTools test runner to read the registered tests from");
                return;
            }
            List<string> problems = new List<string>();
            Dictionary<int, string> priorities = new Dictionary<int, string>();
            Dictionary<string, string> sentences = new Dictionary<string, string>();
            // Nullable, since the save/load fixture's priority, DevTools' lowest, is int.MinValue.
            int? saveLoadPriority = null;
            int lowestOther = int.MaxValue;
            int caseFixtures = 0;
            foreach (ITestFixture fixture in TestRunner.Current.testManager.TestFixtures)
            {
                if (fixture.Type.Assembly != typeof(HarnessFixture).Assembly)
                {
                    continue;
                }
                string name = fixture.Type.Name;
                List<MethodInfo> run = fixture.TestFunctions
                    .Where(function => function.MethodType == MethodType.Test)
                    .Select(function => function.MethodInfo)
                    .ToList();
                List<MethodInfo> declared = fixture.Type
                    .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(method => method.IsDefined(typeof(TestAttribute), true))
                    .OrderBy(method => method.MetadataToken)
                    .ToList();
                if (!run.SequenceEqual(declared))
                {
                    problems.Add(name + " runs " + run.Count + " tests, not its " + declared.Count
                                 + " in declaration order"
                                 + (declared.Count > MostInOrder
                                     ? "; it has more than the " + MostInOrder
                                       + " DevTools keeps in order, so split it"
                                     : ""));
                }
                if (fixture.TestType != TestType.Playing)
                {
                    continue;
                }

                caseFixtures++;
                int priority = fixture.MetaData.Get<int>("ExecutionPriority");
                if (fixture.Type == typeof(SavingAndLoading))
                {
                    saveLoadPriority = priority;
                }
                else
                {
                    lowestOther = Math.Min(lowestOther, priority);
                }
                if (priorities.TryGetValue(priority, out string other))
                {
                    problems.Add(name + " and " + other + " share priority " + priority
                                 + ", so DevTools may run them in either order");
                }
                else
                {
                    priorities[priority] = name;
                }
                foreach (MethodInfo test in declared)
                {
                    string sentence = SentenceOf(test);
                    if (sentence == null)
                    {
                        problems.Add(name + "." + test.Name + " has no TestDescription");
                    }
                    else if (sentences.TryGetValue(sentence, out string first))
                    {
                        problems.Add(name + "." + test.Name + " has the sentence of " + first);
                    }
                    else
                    {
                        sentences[sentence] = name + "." + test.Name;
                    }
                }
            }
            if (caseFixtures != Enum.GetValues(typeof(CaseGroup)).Length + 1)
            {
                problems.Add(caseFixtures + " fixtures that play the game registered, not one for each"
                             + " CaseGroup and SavingAndLoading");
            }
            if (saveLoadPriority == null)
            {
                problems.Add("SavingAndLoading is not registered");
            }
            else if (saveLoadPriority >= lowestOther)
            {
                problems.Add("SavingAndLoading runs at priority " + saveLoadPriority
                             + ", not below every other fixture that plays the game");
            }

            if (problems.Count > 0)
            {
                Test.Fail(string.Join("; ", problems));
            }
        }
    }

    /// <summary>
    /// Checks how <see cref="DebugTools_SaveRoundTrip.Load"/> notes an abort: which log entries
    /// it counts as its load's own, and that the note applies only to the game that load made.
    /// It needs no game. The two tests that touch the note put it back as they found it.
    /// </summary>
    [TestFixture(TestType.MainMenu)]
    internal class HarnessLoadNote : HarnessFixture
    {
        internal const string FirstLine = "[ShiftChange] HarnessLoadNote: the load's first line";
        internal const string LastLine = "[ShiftChange] HarnessLoadNote: the load's last line";

        [Test]
        internal void CountsOnlyTheLoadsOwnEntries()
        {
            LogMessage abort = new LogMessage(LogMessageType.Error,
                DebugTools_SaveRoundTrip.FinalizeAbort + ": System.NullReferenceException", null);
            LogMessage before = new LogMessage("before the load");
            LogMessage loaded = new LogMessage("loaded");
            List<string> problems = new List<string>();
            Check(problems, "an abort before the load",
                  new List<LogMessage> { abort, before, loaded }, before, false);
            Check(problems, "an abort in the load",
                  new List<LogMessage> { before, abort }, before, true);
            Check(problems, "an abort once the entry before the load has left the queue",
                  new List<LogMessage> { abort, loaded }, before, true);
            if (problems.Count > 0)
            {
                Test.Fail(string.Join("; ", problems));
            }
        }

        [Test]
        internal void ReadsFromTheLoadsFirstLine()
        {
            DebugTools_SaveRoundTrip.LoadedGame.TryGetTarget(out Game keptGame);
            string keptCulprit = DebugTools_SaveRoundTrip.loadAbortCulprit;
            try
            {
                // Two lines, so the first differs from the queue's last entry even on a rerun and
                // never folds into it.
                List<LogMessage> read = DebugTools_SaveRoundTrip.NoteLoad(() =>
                {
                    Log.Message(FirstLine);
                    Log.Message(LastLine);
                });
                if (read.FirstOrDefault()?.text != FirstLine || read.All(m => m.text != LastLine))
                {
                    Test.Fail("the note read " + read.Count + " entries, not the load's two from"
                              + " its first: " + string.Join(" | ", read.Select(m => m.text)));
                }
            }
            finally
            {
                DebugTools_SaveRoundTrip.LoadedGame.SetTarget(keptGame);
                DebugTools_SaveRoundTrip.loadAbortCulprit = keptCulprit;
            }
        }

        [Test]
        internal void IgnoresANoteWithNoGameBehindIt()
        {
            DebugTools_SaveRoundTrip.LoadedGame.TryGetTarget(out Game keptGame);
            string keptCulprit = DebugTools_SaveRoundTrip.loadAbortCulprit;
            try
            {
                DebugTools_SaveRoundTrip.LoadedGame.SetTarget(null);
                DebugTools_SaveRoundTrip.loadAbortCulprit = "a culprit";
                if (DebugTools_SaveRoundTrip.LoadAbortCulprit() != null)
                {
                    Test.Fail("a note whose game is gone applied at the main menu");
                }
            }
            finally
            {
                DebugTools_SaveRoundTrip.LoadedGame.SetTarget(keptGame);
                DebugTools_SaveRoundTrip.loadAbortCulprit = keptCulprit;
            }
        }

        internal static void Check(List<string> problems, string what, List<LogMessage> queue,
                                   LogMessage before, bool aborted)
        {
            string culprit = DebugTools_SaveRoundTrip.FinalizeAbortCulprit(
                DebugTools_SaveRoundTrip.LinesSince(queue, before));
            if ((culprit != null) != aborted)
            {
                problems.Add(what + (aborted ? " was missed" : " was counted"));
            }
        }
    }
}
