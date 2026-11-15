using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ShiftChange.Tests
{
    /// <summary>
    /// What the save/load tests (<see cref="SavingAndLoading"/>) share, for the state an outfit
    /// stand scribes: the assignable comp's owner lists and <see cref="CompShiftStand"/>'s ledger
    /// and catchment. The save files and keys, the load, and readers of the written file.
    ///
    /// <para>The load is synchronous. <c>GameDataSaveLoader.LoadGame</c> queues an asynchronous
    /// long event and disposes the current game, so control never returns to a check.
    /// <c>SavedGameLoaderNow.LoadGameFromSaveFileNow</c>, the primitive beneath it, runs the
    /// whole load inline, both scribe passes included, so everything has finished before the
    /// next statement.</para>
    ///
    /// <para>A load replaces <c>Current.Game</c>, so the map a test was handed and every
    /// reference held across the call belong to a disposed game. Hence these tests run last of
    /// all, in their own fixture, and set up no scene, since <c>Teardown</c> would clear a pad on
    /// the disposed map. Each runs on the game the one before left:
    /// <see cref="SavingAndLoading.ASaveFromAnOlderShiftChangeGetsItsOwnerBack"/> rewrites the
    /// file <see cref="SavingAndLoading.AStandInUseComesBackFromASave"/> saved, and the two after
    /// it stage on whatever map is current.</para>
    ///
    /// <para>The checks read the written file as well as the loaded state: comp state alone
    /// cannot tell a value that scribed from one that never left the object.</para>
    /// </summary>
    internal static class SaveRoundTrip
    {
        /// <summary>
        /// The five save files. They are deleted once the tests have all run, except those a
        /// failed test used, which stay to inspect (<see cref="SavingAndLoading.RemoveSaves"/>).
        /// </summary>
        internal const string SaveName = "shiftchange-test-roundtrip";
        internal const string LegacySaveName = "shiftchange-test-legacy";
        internal const string ReSaveName = "shiftchange-test-resave";
        internal const string ForeignSaveName = "shiftchange-test-foreign";
        internal const string AreaSaveName = "shiftchange-test-area";

        /// <summary>The keys an area binding scribes under.</summary>
        internal const string AreaKey = "shiftChangeServeArea";
        internal const string AreaLabelKey = "shiftChangeServeAreaLabel";

        /// <summary>
        /// Our prefixed keys, and the generic keys <c>CompAssignableToPawn</c> writes, which
        /// older saves and any foreign assignable also carry.
        /// </summary>
        internal const string OurKey = "shiftChangeAssignedPawns";
        internal const string OurUninstalledKey = "shiftChangeUninstalledAssignedPawns";
        internal const string LegacyKey = "assignedPawns";
        internal const string LegacyUninstalledKey = "uninstalledAssignedPawns";

        /// <summary>
        /// The reconciled marker (<see cref="CompAssignableToPawn_ShiftStand.ownersUnified"/>)
        /// and its whole node, whatever it holds. It arrived after the generic keys were
        /// retired, so no save carries both, and
        /// <see cref="SavingAndLoading.ASaveFromAnOlderShiftChangeGetsItsOwnerBack"/> strips it.
        /// </summary>
        internal const string OurMarkerKey = "shiftChangeOwnersUnified";
        internal const string OurMarkerNode = "<" + OurMarkerKey + ">[^<]*</" + OurMarkerKey + ">";

        /// <summary>
        /// The catchment keys' prefix (<see cref="CompShiftStand.PostExposeData"/>), and every
        /// node under it, written out or null. They too arrived after the generic keys were
        /// retired, so <see cref="SavingAndLoading.ASaveFromAnOlderShiftChangeGetsItsOwnerBack"/>
        /// strips them.
        /// </summary>
        internal const string CatchmentKeyPrefix = "shiftChangeServe";
        internal const string CatchmentNodes =
            "<(" + CatchmentKeyPrefix + @"\w*)>[^<]*</\1>|<" + CatchmentKeyPrefix + @"\w*\b[^>]*/>";

        /// <summary>
        /// Logged by <c>LoadIDsWantedBank.ConfirmClear</c> when a reference list registers load
        /// ids during LoadingVars and never collects them during ResolvingCrossRefs. The
        /// reference is dropped with no other error, so the absence of this line is what makes
        /// the migration test meaningful.
        /// </summary>
        internal const string UnconsumedWarning = "Not all loadIDs which were read were consumed";

        /// <summary>
        /// Logged when two comps read the same node of one thing: <c>LoadIDsWantedBank</c>
        /// refuses the second registration, and the matching take then finds nothing
        /// (<see cref="FailedTake"/>). <see cref="SavingAndLoading.DeclinedAndReconciled"/> and
        /// <see cref="SavingAndLoading.AnotherModsOwnerComesThroughALoad"/> assert both are absent.
        /// </summary>
        internal const string DuplicateRegistration = "Tried to register the same list of load IDs twice";
        internal const string FailedTake = "Could not get load IDs list";

        /// <summary>
        /// Logged by <c>Game.LoadGame</c> when a map's <c>FinalizeLoading</c> throws; the load
        /// carries on. That pass spawns a loaded map's contents, so after an abort the map is
        /// empty and every stand lookup returns null. A mod can cause it: Vehicle Framework keys
        /// a static map-component cache by map index and never clears it between in-process
        /// loads, so the second and later loads collide on key 0. Nothing of ours is on that
        /// stack.
        /// </summary>
        internal const string FinalizeAbort = "Error in Map.FinalizeLoading()";

        /// <summary>
        /// Several assertions in this file test for absence: no unconsumed load ids, no duplicate
        /// registration, no failed take. A scanner that never matches satisfies all of them, so
        /// the first save/load test logs this string and checks <see cref="Logged"/> finds it.
        /// </summary>
        internal const string Probe = "[ShiftChange] log-scanner probe";





        // ---------------------------------------------------------- plumbing

        /// <summary>
        /// The game the last <see cref="Load"/> made, held weakly so a replaced game is not kept
        /// alive.
        /// </summary>
        internal static readonly System.WeakReference<Game> LoadedGame =
            new System.WeakReference<Game>(null);

        /// <summary>The last <see cref="Load"/>'s <see cref="FinalizeAbort"/> culprit.</summary>
        internal static string loadAbortCulprit;

        /// <summary>
        /// Loads a save in-process and notes whether this load logged <see cref="FinalizeAbort"/>,
        /// as it returns: the log queue keeps only its last 1000 entries.
        /// </summary>
        internal static void Load(string name)
        {
            NoteLoad(() => SavedGameLoaderNow.LoadGameFromSaveFileNow(name));
        }

        /// <summary>Runs a load and notes its abort culprit; returns the entries it read.</summary>
        internal static List<LogMessage> NoteLoad(Action load)
        {
            // LoadGameFromSaveFileNow logs a line of its own first, so nothing the load logs
            // folds into this entry's repeats.
            LogMessage before = Log.Messages.LastOrDefault();
            load();
            List<LogMessage> own = LinesSince(Log.Messages.ToList(), before).ToList();
            LoadedGame.SetTarget(Current.Game);
            loadAbortCulprit = FinalizeAbortCulprit(own);
            return own;
        }

        /// <summary>The entries after <paramref name="before"/>, or all once it has left.</summary>
        internal static IEnumerable<LogMessage> LinesSince(List<LogMessage> messages,
                                                           LogMessage before)
        {
            return messages.Skip(messages.FindLastIndex(m => ReferenceEquals(m, before)) + 1);
        }

        /// <summary>
        /// The culprit <see cref="Load"/> noted, or null unless the current game is that load's.
        /// </summary>
        internal static string LoadAbortCulprit()
        {
            return LoadedGame.TryGetTarget(out Game game) && game == Current.Game
                ? loadAbortCulprit
                : null;
        }

        /// <summary>
        /// Whether <paramref name="needle"/> appears inside the named element rather than
        /// anywhere in the document: a save repeats each pawn's load id in many unrelated nodes,
        /// so a document-wide <c>Contains</c> would match a file that lost the owner.
        /// </summary>
        internal static bool NodeContains(string xml, string node, string needle)
        {
            foreach (string body in NodeBodies(xml, node))
            {
                if (body.Contains(needle))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The first <c>&lt;li&gt;</c> value in the first such node.</summary>
        internal static string FirstListEntry(string xml, string node)
        {
            foreach (string body in NodeBodies(xml, node))
            {
                int open = body.IndexOf("<li>", StringComparison.Ordinal);
                int close = body.IndexOf("</li>", StringComparison.Ordinal);
                if (open >= 0 && close > open)
                {
                    return body.Substring(open + 4, close - open - 4);
                }
            }
            return null;
        }

        /// <summary>
        /// Every <c>&lt;node&gt;…&lt;/node&gt;</c> body in the document. A map carries one per
        /// stand; the assertions match on any of them.
        /// </summary>
        internal static IEnumerable<string> NodeBodies(string xml, string node)
        {
            string open = "<" + node + ">";
            string close = "</" + node + ">";
            int from = 0;
            while (true)
            {
                int start = xml.IndexOf(open, from, StringComparison.Ordinal);
                if (start < 0)
                {
                    yield break;
                }
                int end = xml.IndexOf(close, start, StringComparison.Ordinal);
                if (end < 0)
                {
                    yield break;
                }
                yield return xml.Substring(start + open.Length, end - start - open.Length);
                from = end + close.Length;
            }
        }

        /// <summary>
        /// The stand at a known cell on the current map, which after a load is the loaded map,
        /// not the one the scene was built on.
        /// </summary>
        internal static Building_OutfitStand FindStand(IntVec3 cell)
        {
            Map map = Find.CurrentMap;
            if (map == null || !cell.InBounds(map))
            {
                return null;
            }
            return cell.GetThingList(map).OfType<Building_OutfitStand>().FirstOrDefault();
        }

        /// <summary>
        /// Any stand on any loaded map. The migration test stages no scene and is not handed
        /// the first test's cell, and the foreign test falls back to it when its stand is not at the cell
        /// it saved.
        /// </summary>
        internal static Building_OutfitStand FindStandAnywhere()
        {
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Building_OutfitStand stand = maps[i].listerBuildings
                    .AllBuildingsColonistOfClass<Building_OutfitStand>().FirstOrDefault();
                if (stand != null)
                {
                    return stand;
                }
            }
            return null;
        }

        /// <summary>
        /// The first mod frame inside a FinalizeLoading abort among <paramref name="messages"/>,
        /// or null when none of them is one. It names whose cache broke the load, so the reader
        /// need not open Player.log.
        /// </summary>
        internal static string FinalizeAbortCulprit(IEnumerable<LogMessage> messages)
        {
            bool aborted = false;
            foreach (LogMessage message in messages)
            {
                if (message.text == null || !message.text.Contains(FinalizeAbort))
                {
                    continue;
                }
                aborted = true;
                string frame = FirstModFrame(message.text);
                if (frame != null)
                {
                    return frame;
                }
            }
            return aborted ? "a frame the tests could not name" : null;
        }

        /// <summary>
        /// How a frame of the game, the runtime or Harmony begins; one opening with a parenthesis
        /// is a runtime wrapper. A mod that declares its types in Verse or RimWorld is skipped too.
        /// </summary>
        internal static readonly string[] EngineFrames =
        {
            "Verse.", "RimWorld.", "LudeonTK.", "System.", "Microsoft.", "UnityEngine.", "Unity.",
            "Mono.", "MonoMod.", "HarmonyLib.", "HarmonyMod.", "Waylith.", "RimWorldWaylith.",
            "Brutal.", "InvokeMessageMethod_", "(",
        };

        /// <summary>
        /// What some Harmony versions write before the original method's full name when they name
        /// the replacement for a patched method.
        /// </summary>
        internal static readonly string[] PatchedFrames =
        {
            "HarmonyGenerated.", "MonoMod.Utils.DynamicMethodDefinition.",
        };

        /// <summary>
        /// The first stack frame in an exception's text that no <see cref="EngineFrames"/> entry
        /// begins, cut after its argument list, or null when every frame is the engine's.
        /// </summary>
        internal static string FirstModFrame(string text)
        {
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("at ", StringComparison.Ordinal))
                {
                    continue;
                }
                // Only the type name marks a mod's frame: a source path follows only when the mod
                // ships its symbols, and names wherever it was built.
                string frame = trimmed.Substring(3);
                foreach (string patched in PatchedFrames)
                {
                    if (frame.StartsWith(patched, StringComparison.Ordinal))
                    {
                        frame = frame.Substring(patched.Length);
                    }
                }
                // A line with no argument list is message text, not a frame.
                int close = frame.IndexOf(')');
                if (close < 0
                    || EngineFrames.Any(e => frame.StartsWith(e, StringComparison.Ordinal)))
                {
                    continue;
                }
                return frame.Substring(0, close + 1);
            }
            return null;
        }

        /// <summary>
        /// Whether the log contains <paramref name="needle"/>. Scans the whole queue, not a range
        /// past a saved index, which gives false passes two ways: the queue caps at 1000 entries,
        /// so a noisy load pushes earlier entries out from under the index; and a message equal to
        /// the last one does not enqueue but bumps that entry's <c>repeats</c>, behind the index.
        /// The cost is a duplicate failure, an earlier test's warning failing the later tests too;
        /// these strings appear only when a load went wrong.
        /// </summary>
        internal static bool Logged(string needle)
        {
            foreach (LogMessage message in Log.Messages)
            {
                if (message.text != null && message.text.Contains(needle))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
