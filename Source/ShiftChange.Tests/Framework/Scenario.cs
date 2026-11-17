using System.Text;
using DevTools;
using DevTools.Testing;

namespace ShiftChange.Tests
{
    /// <summary>
    /// A test as a BDD scenario, in the words of the game: its title, the test's sentence, then a
    /// step for each check and note, in order. The checks are soft assertions: the first that
    /// fails fails the test with the steps so far, its own marked, so whoever reads the failure
    /// sees what had happened when it went wrong: a player in the Test Explorer, a developer in
    /// Test.log or in an IDE. Every step, those after the failure included, goes in Test.log when
    /// the test ends.
    ///
    /// <para>Plain text with a marker in the margin, never colour: the same text reaches the
    /// Test Explorer, Test.log and the test adapter, and each would show colour codes as
    /// junk.</para>
    /// </summary>
    internal sealed class Scenario
    {
        internal const string Passed = "    PASS  ";
        internal const string Failed = ">>  FAIL  ";
        internal const string Noted = "          ";

        internal readonly StringBuilder Steps = new StringBuilder();

        internal Scenario(string sentence)
        {
            Steps.AppendLine(sentence);
        }

        internal const string Gapped = "    GAP   ";

        internal const string Fixed = "    FIXED ";

        /// <summary>True once a known gap has been noted (<see cref="KnownGap"/>).</summary>
        internal bool Gap;

        /// <summary>True once a check has failed.</summary>
        internal bool Failing;

        /// <summary>A line of context, which passes or fails nothing.</summary>
        internal void Note(string line)
        {
            Steps.Append(Noted).AppendLine(line);
        }

        /// <summary>
        /// A check: a step in the scenario, and a signal to DevTools. A failure does not stop the
        /// test, so the scenario shows every check; the first failure names the test's failure.
        /// </summary>
        /// <returns>The condition, so a test can stop where the rest would be moot.</returns>
        internal bool Check(bool condition, string what)
        {
            Steps.Append(condition ? Passed : Failed).AppendLine(what);
            if (condition)
            {
                Expect.IsTrue(true, what);
            }
            else if (!Failing)
            {
                Failing = true;
                Test.Fail(what, Steps.ToString().TrimEnd());
            }
            return condition;
        }

        /// <summary>
        /// What a test checks but the mod does not do yet, with the reason: a line marked GAP,
        /// and the test is skipped rather than failed when it ends, unless a check failed, so a
        /// green run stays meaningful. Should it start to hold, the line is marked FIXED, a sign
        /// to make it a check.
        /// </summary>
        internal void KnownGap(bool condition, string what, string why)
        {
            if (condition)
            {
                Steps.Append(Fixed).Append(what).AppendLine(" (a known gap that now holds: make it a check)");
                return;
            }
            Steps.Append(Gapped).Append(what).Append(": ").AppendLine(why);
            Gap = true;
        }

        /// <summary>
        /// What a test threw, as its last step, failing it with the steps so far if nothing has
        /// yet.
        /// </summary>
        internal void Threw(System.Exception e)
        {
            Steps.Append(Failed).Append("the test threw ").AppendLine(e.ToString());
            if (!Failing)
            {
                Failing = true;
                Test.Fail("the test threw " + e.GetType().Name + ": " + e.Message, Steps.ToString().TrimEnd());
            }
        }

        /// <summary>Writes the whole scenario to Test.log.</summary>
        internal void WriteToLog()
        {
            DevLog.Write(Steps.ToString().TrimEnd());
        }

        public override string ToString()
        {
            return Steps.ToString().TrimEnd();
        }
    }
}
