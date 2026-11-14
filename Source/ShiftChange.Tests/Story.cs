using System.Text;
using DevTools;
using DevTools.Testing;

namespace ShiftChange.Tests
{
    /// <summary>
    /// What a test did, in the words of the game: its sentence, then a line for each check and
    /// note, in order. The first check that fails fails the test with the story so far, its own
    /// line marked, so whoever reads the failure sees what had happened when it went wrong: a
    /// player in the Test Explorer, a developer in Test.log or in an IDE. The whole story, checks
    /// after the failure included, goes in Test.log when the test ends.
    ///
    /// <para>Plain text with a marker in the margin, never colour: the same text reaches the
    /// Test Explorer, Test.log and the test adapter, and each would show colour codes as
    /// junk.</para>
    /// </summary>
    internal sealed class Story
    {
        internal const string Passed = "    PASS  ";
        internal const string Failed = ">>  FAIL  ";
        internal const string Noted = "          ";

        internal readonly StringBuilder Lines = new StringBuilder();

        internal Story(string sentence)
        {
            Lines.AppendLine(sentence);
        }

        /// <summary>True once a check has failed.</summary>
        internal bool Failing;

        /// <summary>A line of context, which passes or fails nothing.</summary>
        internal void Note(string line)
        {
            Lines.Append(Noted).AppendLine(line);
        }

        /// <summary>
        /// A check: a line in the story, and a signal to DevTools. A failure does not stop the
        /// test, so the story shows every check; the first failure names the test's failure.
        /// </summary>
        /// <returns>The condition, so a test can stop where the rest would be moot.</returns>
        internal bool Check(bool condition, string what)
        {
            Lines.Append(condition ? Passed : Failed).AppendLine(what);
            if (condition)
            {
                Expect.IsTrue(true, what);
            }
            else if (!Failing)
            {
                Failing = true;
                Test.Fail(what, Lines.ToString().TrimEnd());
            }
            return condition;
        }

        /// <summary>Writes the whole story to Test.log.</summary>
        internal void Tell()
        {
            DevLog.Write(Lines.ToString().TrimEnd());
        }

        public override string ToString()
        {
            return Lines.ToString().TrimEnd();
        }
    }
}
