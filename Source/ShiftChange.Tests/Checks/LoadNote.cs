using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DevTools;
using DevTools.Testing;
using Verse;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Checks how <see cref="SaveRoundTrip.Load"/> notes an abort: which log entries
    /// it counts as its load's own, and that the note applies only to the game that load made.
    /// It needs no game. The two tests that touch the note put it back as they found it.
    /// </summary>
    [TestFixture(TestType.MainMenu)]
    internal class LoadNote : ShiftChangeFixture
    {
        internal const string FirstLine = "[ShiftChange] LoadNote: the load's first line";
        internal const string LastLine = "[ShiftChange] LoadNote: the load's last line";

        [Test]
        internal void CountsOnlyTheLoadsOwnEntries()
        {
            LogMessage abort = new LogMessage(LogMessageType.Error,
                SaveRoundTrip.FinalizeAbort + ": System.NullReferenceException", null);
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
            SaveRoundTrip.LoadedGame.TryGetTarget(out Game keptGame);
            string keptCulprit = SaveRoundTrip.loadAbortCulprit;
            try
            {
                // Two lines, so the first differs from the queue's last entry even on a rerun and
                // never folds into it.
                List<LogMessage> read = SaveRoundTrip.NoteLoad(() =>
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
                SaveRoundTrip.LoadedGame.SetTarget(keptGame);
                SaveRoundTrip.loadAbortCulprit = keptCulprit;
            }
        }

        [Test]
        internal void IgnoresANoteWithNoGameBehindIt()
        {
            SaveRoundTrip.LoadedGame.TryGetTarget(out Game keptGame);
            string keptCulprit = SaveRoundTrip.loadAbortCulprit;
            try
            {
                SaveRoundTrip.LoadedGame.SetTarget(null);
                SaveRoundTrip.loadAbortCulprit = "a culprit";
                if (SaveRoundTrip.LoadAbortCulprit() != null)
                {
                    Test.Fail("a note whose game is gone applied at the main menu");
                }
            }
            finally
            {
                SaveRoundTrip.LoadedGame.SetTarget(keptGame);
                SaveRoundTrip.loadAbortCulprit = keptCulprit;
            }
        }

        internal static void Check(List<string> problems, string what, List<LogMessage> queue,
                                   LogMessage before, bool aborted)
        {
            string culprit = SaveRoundTrip.FinalizeAbortCulprit(
                SaveRoundTrip.LinesSince(queue, before));
            if ((culprit != null) != aborted)
            {
                problems.Add(what + (aborted ? " was missed" : " was counted"));
            }
        }
    }
}
