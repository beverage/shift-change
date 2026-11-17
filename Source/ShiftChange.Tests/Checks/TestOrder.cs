using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DevTools;
using DevTools.Testing;
using Verse;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Checks the tests DevTools registered from this assembly, which it runs in registry order:
    /// each fixture runs exactly the tests it declares, in declaration order, which DevTools keeps
    /// for up to 16 tests to a fixture; every fixture that plays the game runs at a priority of
    /// its own, <see cref="SavingAndLoading"/> at the lowest; and every test has a sentence of its
    /// own. It reads DevTools' registry and runs no test of its own, so it needs no game.
    /// </summary>
    [TestFixture(TestType.MainMenu)]
    internal class TestOrder : ShiftChangeFixture
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
                if (fixture.Type.Assembly != typeof(ShiftChangeFixture).Assembly)
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
                    string sentence = FeatureFixture.SentenceOf(test);
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
}
