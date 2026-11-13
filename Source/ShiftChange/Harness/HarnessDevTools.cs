// HARNESS and DEVTOOLS only. DEVTOOLS is defined only by a harness build handed a DevTools dll,
// so every other build, CI's included, compiles this file away. The guard is whole-file, as for
// all harness code (check-invariants.py enforces it).
#if HARNESS && DEVTOOLS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DevTools;
using DevTools.Testing;
using RimWorld;
using Verse;
using static ShiftChange.DebugTools_LifecycleHarness;

namespace ShiftChange
{
    /// <summary>
    /// Every harness case, run by SmashPhil's DevTools test runner instead of the
    /// <c>-shiftchange-harness</c> auto-run. Each fixture below is one <see cref="CaseGroup"/>
    /// and has one test per case, which DevTools generates from the group and names after the
    /// case. A test runs its case's own <c>Case</c> call, so the fixture, its build retries and
    /// its teardown are the auto-run's.
    ///
    /// <para>DevTools runs a fixture with a higher <c>ExecutionPriority</c> first, so each takes
    /// its group's place in <see cref="CaseGroup"/> from <see cref="First"/>. That holds for the
    /// unfiltered run <c>run-harness.sh</c> starts; older DevTools builds order a run filtered
    /// with <c>-w</c> by test type alone.</para>
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

        /// <summary>Runs a case as the auto-run hands it: on the pad if it needs one.</summary>
        internal static IEnumerator Run(HarnessCase harnessCase)
        {
            return Run(harnessCase, harnessCase.Run);
        }

        internal static IEnumerator Run(HarnessCase harnessCase, Action<Map, CellRect> runCase)
        {
            if (!harnessCase.NeedsPad)
            {
                return RunWithoutPad(() => runCase(Find.CurrentMap, CellRect.Empty));
            }
            return RunOnPad(runCase);
        }

        /// <summary>
        /// Runs a case on the current map's pad, unseen by the camera. The pad is found once per
        /// map, as the auto-run finds one per run, so a case that leaves the pad unusable fails
        /// the cases after it instead of moving them to another pad.
        /// </summary>
        internal static IEnumerator RunOnPad(Action<Map, CellRect> runCase)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Test.Fail("no current map");
                yield break;
            }
            if (!PadMap.TryGetTarget(out Map padMap) || padMap != map)
            {
                if (!Patch_HarnessAutoRun.TryFindPad(map, out IntVec3 origin))
                {
                    Test.Fail("no clear " + PadSize + "×" + PadSize + " pad on DevTools' map");
                    yield break;
                }
                PadMap.SetTarget(map);
                Pad = new CellRect(origin.x, origin.z, PadSize, PadSize);
                IntVec3 centre = map.Center - new IntVec3(PadSize / 2, 0, PadSize / 2);
                DevLog.Write("[ShiftChange] pad at (" + origin.x + ", " + origin.z
                             + (origin == centre ? "), the map centre" : "), not the map centre"));
            }
            CellRect pad = Pad;

            // Cases pump pawns by hand under a stopped clock, and Thing.DoTick gives a pawn the
            // camera can see its interval tick every one to five pumps instead of every fifteen,
            // which changes whose needs fire and how far a pumped job gets. DevTools centres the
            // camera on the map, where the pad is, so look away first. The camera's view rect is
            // cached per frame, so the move shows only after one.
            Find.CameraDriver.JumpToCurrentMapLoc(new IntVec3(1, 0, 1));
            yield return null;
            yield return null;
            // Cases reach past the pad (the fixture check eight cells out), so keep a margin.
            if (Find.CameraDriver.CurrentViewRect.ExpandedBy(1).Overlaps(pad.ExpandedBy(10)))
            {
                Test.Fail("the camera still sees the pad at " + pad + " after looking away");
                yield break;
            }

            Judge(() => runCase(map, pad));
        }

        /// <summary>
        /// Runs a case that is handed no map or pad as the auto-run does: no pad search, no camera
        /// move. Two save/load legs find a pad of their own, wherever the camera is, as they do in
        /// the auto-run.
        /// </summary>
        internal static IEnumerator RunWithoutPad(Action runCase)
        {
            Judge(runCase);
            yield break;
        }

        /// <summary>
        /// Runs a case and turns its tally into the test's status: a failure fails it, a known gap
        /// skips it. The case's transcript, one line per assertion, goes to Test.log either way.
        /// </summary>
        internal static void Judge(Action runCase)
        {
            Report.Length = 0;
            int failedBefore = Failed;
            int gapsBefore = KnownGaps;
            runCase();
            string transcript = Report.ToString().TrimEnd();
            DevLog.Write(transcript);

            if (transcript.Length == 0)
            {
                Test.Fail("the case never ran");
            }
            else if (Failed > failedBefore)
            {
                Test.Fail("the case failed; its transcript is above");
            }
            else if (KnownGaps > gapsBefore)
            {
                Test.Skip("a known gap; its transcript is above");
            }
        }
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Flights)]
    internal class HarnessFlights
    {
        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.Flights);

        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.VacuumGate)]
    internal class HarnessVacuumGate
    {
        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.VacuumGate);

        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.VacuumRoutes)]
    internal class HarnessVacuumRoutes
    {
        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.VacuumRoutes);

        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Lifecycle)]
    internal class HarnessLifecycleEvents
    {
        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.Lifecycle);

        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase);
    }

    /// <summary>
    /// The case throws three faults through interception on purpose. Each logs an error, and the
    /// third also logs the latch closing. Only those are allowed: a throw of any other kind logs
    /// its own error, which fails the test.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.FaultLatch)]
    [ErrorsAllowed(@"\[ShiftChange\] injected test fault",
                   @"\[ShiftChange\] interception disabled for this game after \d+ faults")]
    internal class HarnessFaultLatch
    {
        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.FaultLatch);

        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.DriverAndOwners)]
    internal class HarnessDriverAndOwners
    {
        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.DriverAndOwners);

        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Arms)]
    internal class HarnessArms
    {
        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.Arms);

        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Gates)]
    internal class HarnessGateCases
    {
        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.Gates);

        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Radius)]
    internal class HarnessRadius
    {
        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.Radius);

        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.BillProducts)]
    internal class HarnessBillProducts
    {
        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.BillProducts);

        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Latches)]
    internal class HarnessLatches
    {
        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.Latches);

        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Areas)]
    internal class HarnessAreas
    {
        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.Areas);

        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.RideAlong)]
    internal class HarnessRideAlongCases
    {
        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.RideAlong);

        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Assorted)]
    internal class HarnessAssorted
    {
        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.Assorted);

        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Tables)]
    internal class HarnessTableCases
    {
        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.Tables);

        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase);
    }

    /// <summary>
    /// The four save and load legs of <see cref="DebugTools_SaveRoundTrip"/>, in run order, each
    /// on the game the one before left.
    ///
    /// <para>A load replaces the game DevTools generated, and DevTools goes on tracking that
    /// game's map. Asking for a world of its own makes DevTools generate a fresh one for this
    /// fixture and again for whichever fixture follows. It runs last, out of its group's place,
    /// so none follows.</para>
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.SaveLoadPriority)]
    internal class HarnessDevToolsSaveLoad : IWorldGeneration
    {
        WorldGenerationSettings IWorldGeneration.WorldGenerationSettings
        {
            get { return new WorldGenerationSettings(); }
        }

        internal static IEnumerable<HarnessCase> Group() => CasesIn(CaseGroup.SaveLoad);

        /// <summary>
        /// Only the first leg reads the map and pad. The others run on the game the leg before
        /// loaded, which a mod that breaks a load can leave with no clear pad, and they reach their
        /// own known gap for that.
        /// </summary>
        [Test]
        internal IEnumerator Run([ParametersSource(nameof(Group))] HarnessCase harnessCase)
            => HarnessDevTools.Run(harnessCase, (map, pad) => Loading(harnessCase, map, pad));

        /// <summary>
        /// Runs a case as the game runs a load: inside LongEventHandler's finishing pass, so what
        /// the load defers to that pass runs after the load. With no long event running, a
        /// deferred action runs on the spot, halfway through the load, before the world it reads
        /// has been loaded.
        /// </summary>
        internal static void Loading(HarnessCase harnessCase, Map map, CellRect pad)
        {
            LongEventHandler.ExecuteWhenFinished(() => harnessCase.Run(map, pad));
        }
    }

    /// <summary>
    /// Checks the tests DevTools registered against <see cref="Cases"/>: the list follows
    /// <see cref="CaseGroup"/> order, so DevTools runs the groups in the auto-run's order apart
    /// from save/load, which it runs last; nothing listed after save/load needs a pad, since the
    /// auto-run would hand it the game those legs replaced; each group runs in one fixture, at its
    /// group's priority, as exactly its cases in list order; a fixture whose test takes a case
    /// has a group with cases; no two cases share a name; and no group passes the 16 cases
    /// DevTools keeps in order. It reads DevTools' own registry, where each fixture's tests are
    /// already sorted into run order, and runs no case, so it needs no game.
    /// </summary>
    [TestFixture(TestType.MainMenu)]
    internal class HarnessCaseList
    {
        internal const int MostInOrder = 16;

        [Test]
        internal void EveryCaseRunsOnceInOrder()
        {
            List<HarnessCase> all = Cases();
            List<string> problems = new List<string>();
            foreach (IGrouping<CaseGroup, HarnessCase> group in all.GroupBy(c => c.Group))
            {
                if (group.Count() > MostInOrder)
                {
                    problems.Add(group.Key + " holds " + group.Count() + " cases, past the "
                                 + MostInOrder + " DevTools keeps in order; split it");
                }
            }
            foreach (IGrouping<string, HarnessCase> name in all.GroupBy(c => c.Name))
            {
                if (name.Count() > 1)
                {
                    problems.Add("\"" + name.Key + "\" is listed " + name.Count() + " times");
                }
            }
            for (int i = 1; i < all.Count; i++)
            {
                if (all[i].Group < all[i - 1].Group)
                {
                    problems.Add("\"" + all[i].Name + "\" is listed under " + all[i].Group
                                 + " after " + all[i - 1].Group + "'s cases, out of CaseGroup"
                                 + " order, so the two runners reach it at different points");
                }
            }
            bool pastSaveLoad = false;
            foreach (HarnessCase harnessCase in all)
            {
                if (harnessCase.Group == CaseGroup.SaveLoad)
                {
                    pastSaveLoad = true;
                }
                else if (pastSaveLoad && harnessCase.NeedsPad)
                {
                    problems.Add("\"" + harnessCase.Name + "\" is listed after the save/load legs"
                                 + " but needs a pad, which the auto-run would take from the game"
                                 + " they replaced");
                }
            }

            if (TestRunner.Current == null)
            {
                problems.Insert(0, "no DevTools test runner to read the registered tests from");
                Test.Fail(string.Join("; ", problems));
                return;
            }
            Dictionary<CaseGroup, List<string>> registered = new Dictionary<CaseGroup, List<string>>();
            foreach (ITestFixture fixture in TestRunner.Current.testManager.TestFixtures)
            {
                if (fixture.Type.Assembly != typeof(HarnessDevTools).Assembly)
                {
                    continue;
                }
                List<HarnessCase> generated = fixture.TestFunctions
                    .Select(test => test.Args != null && test.Args.Length == 1
                                        ? test.Args[0] as HarnessCase
                                        : null)
                    .Where(harnessCase => harnessCase != null)
                    .ToList();
                if (generated.Count == 0)
                {
                    // An empty [ParametersSource] still makes one test, with no argument.
                    bool takesCase = fixture.Type
                        .GetMethods(BindingFlags.Instance | BindingFlags.Static
                                    | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(method => method.IsDefined(typeof(TestAttribute), true))
                        .SelectMany(method => method.GetParameters())
                        .Any(parameter => parameter.ParameterType == typeof(HarnessCase));
                    if (takesCase)
                    {
                        problems.Add(fixture.Type.Name + "'s test takes a case, but DevTools"
                                     + " generated none: its group is empty, or the test did not"
                                     + " load");
                    }
                    continue;
                }
                CaseGroup group = generated[0].Group;
                string name = fixture.Type.Name;
                if (generated.Any(harnessCase => harnessCase.Group != group))
                {
                    problems.Add(name + " runs cases from more than one group");
                }
                if (registered.ContainsKey(group))
                {
                    problems.Add(name + " runs " + group + ", which another fixture runs too");
                }
                else
                {
                    registered[group] = generated.Select(harnessCase => harnessCase.Name).ToList();
                }
                int priority = fixture.MetaData.Get<int>("ExecutionPriority");
                int expected = group == CaseGroup.SaveLoad
                    ? HarnessDevTools.SaveLoadPriority
                    : HarnessDevTools.First - (int)group;
                if (priority != expected)
                {
                    problems.Add(name + " runs at priority " + priority + ", not " + group + "'s "
                                 + expected);
                }
            }
            foreach (IGrouping<CaseGroup, HarnessCase> group in all.GroupBy(c => c.Group))
            {
                List<string> listed = group.Select(harnessCase => harnessCase.Name).ToList();
                if (!registered.TryGetValue(group.Key, out List<string> run))
                {
                    problems.Add(group.Key + " has no fixture, so its " + listed.Count
                                 + " cases never run");
                }
                else if (!run.SequenceEqual(listed))
                {
                    problems.Add(group.Key + "'s fixture runs " + run.Count + " tests, not its "
                                 + listed.Count + " cases in list order");
                }
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
    internal class HarnessLoadNote
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
#endif
