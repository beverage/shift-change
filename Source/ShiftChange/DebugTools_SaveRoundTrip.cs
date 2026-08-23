using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Save/load round-trip coverage for the state an outfit stand scribes:
    /// the assignable comp's owner lists, and <see cref="CompShiftStand"/>'s
    /// ledger.
    ///
    /// <para>The load runs synchronously. <c>GameDataSaveLoader.LoadGame</c>
    /// queues an asynchronous long event and disposes the current game
    /// (<c>GameDataSaveLoader.cs:153-166</c>), so control never returns to an
    /// assertion. <c>SavedGameLoaderNow.LoadGameFromSaveFileNow</c> is the
    /// synchronous primitive beneath it, and the one <c>Root_Play</c> uses for
    /// an autostart save (<c>Root_Play.cs:32</c>). It runs <c>Game.LoadGame()</c>
    /// inline — world, maps, <c>Scribe.loader.FinalizeLoading()</c> and
    /// <c>FinalizeInit</c> (<c>Game.cs:560-636</c>) — so both scribe passes
    /// complete before the next statement.</para>
    ///
    /// <para>These cases replace <c>Current.Game</c>. Once one has run, the map
    /// the harness was handed and every reference held across the call belong
    /// to a disposed game. Two constraints follow, both applied where the cases
    /// are registered in <see cref="DebugTools_LifecycleHarness.Run"/>: they run
    /// last among the map cases, and they register through the fixture-less
    /// <c>Case</c> overload, since <c>Teardown</c> would clear a pad on the
    /// disposed map.</para>
    ///
    /// <para><c>Game.LoadGame</c> calls <c>FinalizeInit</c>, which
    /// <see cref="Patch_HarnessAutoRun"/> postfixes. A one-shot latch there
    /// prevents a nested harness run.</para>
    ///
    /// <para>Assertions read the written file as well as the loaded comp state.
    /// Comp state alone cannot distinguish a value that scribed correctly from
    /// one that never left the object. Mechanisms:
    /// <c>rimworld-docs/gamedata/scribe-system.md</c>.</para>
    ///
    /// <para>This body ships in every configuration, like the rest of the
    /// harness: <c>-shiftchange-harness</c> is the release gate and asserts
    /// against the shipped dll.</para>
    /// </summary>
    internal static class DebugTools_SaveRoundTrip
    {
        /// <summary>
        /// Four save files, not cleaned up.
        ///
        /// <para><c>run-harness.sh</c> gives the game an isolated
        /// <c>-savedatafolder</c> and wipes it at the start of every run, so the
        /// normal path leaves nothing behind. Driving the harness from the debug
        /// menu writes them into that profile's <c>Saves</c> folder instead;
        /// that entry point is SCENES-only.</para>
        ///
        /// <para>They are kept because the assertions are about file contents.
        /// When one fails, the file is the artifact to inspect.</para>
        /// </summary>
        internal const string SaveName = "shiftchange-harness-roundtrip";
        internal const string LegacySaveName = "shiftchange-harness-legacy";
        internal const string ReSaveName = "shiftchange-harness-resave";
        internal const string ForeignSaveName = "shiftchange-harness-foreign";

        /// <summary>
        /// Our mod-prefixed keys, and the generic keys <c>CompAssignableToPawn</c>
        /// writes — which older saves and any foreign assignable also carry.
        /// </summary>
        internal const string OurKey = "shiftChangeAssignedPawns";
        internal const string OurUninstalledKey = "shiftChangeUninstalledAssignedPawns";
        internal const string LegacyKey = "assignedPawns";
        internal const string LegacyUninstalledKey = "uninstalledAssignedPawns";

        /// <summary>
        /// Logged when a reference list registers load-ids during LoadingVars
        /// and does not collect them during ResolvingCrossRefs
        /// (<c>LoadIDsWantedBank.ConfirmClear</c>, <c>:53</c>). A half-consumed
        /// bank drops the reference without erroring, so absence of this line
        /// is what makes the migration leg meaningful.
        /// </summary>
        internal const string UnconsumedWarning = "Not all loadIDs which were read were consumed";

        /// <summary>
        /// Logged when two comps read the same node on one thing: the second
        /// registration is refused (<c>LoadIDsWantedBank.cs:108</c>) and the
        /// matching take finds nothing (<c>:149</c>).
        /// <see cref="ForeignAssignable"/> asserts both are absent.
        /// </summary>
        internal const string DuplicateRegistration = "Tried to register the same list of load IDs twice";
        internal const string FailedTake = "Could not get load IDs list";

        /// <summary>
        /// Six assertions in this file test for absence — no unconsumed
        /// load-ids, no duplicate registration, no failed take. A scanner that
        /// never matches anything satisfies all six, so one leg logs this
        /// string and asserts <see cref="Logged"/> finds it.
        /// </summary>
        internal const string Probe = "[ShiftChange] harness log-scanner probe";

        // ------------------------------------------------------------ leg 1

        /// <summary>
        /// Saves a stand carrying an owner and a populated ledger, loads it
        /// back, and asserts every scribed field survived.
        /// </summary>
        internal static bool RoundTrip(Map map, CellRect pad)
        {
            DebugTools_LifecycleHarness.Fixture fix = DebugTools_LifecycleHarness.Build(map, pad);
            if (fix == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine("      fixture could not be staged");
                return false;
            }

            CompAssignableToPawn_ShiftStand owned =
                fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
            if (owned == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      the stand has no CompAssignableToPawn_ShiftStand");
                return false;
            }
            // Through the comp's own API rather than the backing list: the
            // harness simulates orchestration only, never engine calls.
            owned.TryAssignPawn(fix.Pawn);

            // Build leaves storedForcedApparel empty; the forced flag is one
            // of the fields under test. Re-record through NotifyDressed with
            // one stored garment flagged.
            List<Apparel> stored = new List<Apparel>(fix.Comp.storedOwnerApparel);
            List<Apparel> issued = new List<Apparel>(fix.Comp.issuedUniform);
            if (stored.Count == 0 || issued.Count == 0)
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      the fixture's ledger is empty — nothing to round-trip");
                return false;
            }
            fix.Comp.NotifyDressed(fix.Pawn, stored, issued,
                                   new List<Apparel> { stored[0] });

            // Captured before the load: afterwards these objects belong to a
            // disposed game.
            IntVec3 standCell = fix.Stand.Position;
            string ownerLoadID = fix.Pawn.GetUniqueLoadID();
            int ownerID = fix.Pawn.thingIDNumber;
            int storedCount = stored.Count;
            int issuedCount = issued.Count;
            string forcedLoadID = stored[0].GetUniqueLoadID();

            // The invariant's migration half: a stand saved EXPOSED must come
            // back clean. Refusing the toggle's OFF->ON transition never
            // emptied the stands already sitting in ON, and this is the leg
            // that proves they get swept. Staged here rather than in its own
            // case because this is the only case that already pays for a
            // save/load cycle, and the sweep runs in PostSpawnSetup, which
            // only a real load exercises.
            if (Patch_AllowRemovingToggle.AllowRemovingItemsRef != null)
            {
                Patch_AllowRemovingToggle.AllowRemovingItemsRef(fix.Stand) = true;
            }

            if (!TrySave(SaveName, out string savePath))
            {
                return false;
            }
            string saved = File.ReadAllText(savePath);

            // Asserted on the file before anything is read back: comp state
            // cannot distinguish a value that scribed correctly from one that
            // never left the object.
            bool ok = DebugTools_LifecycleHarness.Expect(
                    NodeContains(saved, OurKey, ownerLoadID),
                    "the save carries the owner under " + OurKey)
                & DebugTools_LifecycleHarness.Expect(
                    !NodeContains(saved, LegacyKey, ownerLoadID),
                    "and not under the generic " + LegacyKey);

            SavedGameLoaderNow.LoadGameFromSaveFileNow(SaveName);

            // Positive control for the absence assertions. Logged after the
            // load, so it also covers the queue being cleared by it.
            Log.Message(Probe);
            ok &= DebugTools_LifecycleHarness.Expect(
                    Logged(Probe),
                    "the log scanner finds a line that is there (positive control)")
                & DebugTools_LifecycleHarness.Expect(
                    !Logged(UnconsumedWarning),
                    "the load consumed every load-id it registered");

            Building_OutfitStand loaded = FindStand(standCell);
            if (loaded == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      no stand at " + standCell + " after loading — cannot assert");
                return false;
            }

            CompShiftStand ledger = loaded.TryGetComp<CompShiftStand>();
            CompAssignableToPawn_ShiftStand assign =
                loaded.TryGetComp<CompAssignableToPawn_ShiftStand>();
            if (ledger == null || assign == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      the loaded stand lost a comp — cannot assert");
                return false;
            }

            return ok
                & DebugTools_LifecycleHarness.Expect(
                    assign.AssignedPawnsForReading.Any(p => p != null && p.thingIDNumber == ownerID),
                    "the owner survived the round trip")
                & DebugTools_LifecycleHarness.Expect(
                    ledger.Borrower != null && ledger.Borrower.thingIDNumber == ownerID,
                    "the borrower survived")
                & DebugTools_LifecycleHarness.Expect(ledger.OnShift, "the stand still reads on-shift")
                & DebugTools_LifecycleHarness.Expect(
                    ledger.storedOwnerApparel.Count == storedCount,
                    "the stored list kept all " + storedCount + " garments")
                & DebugTools_LifecycleHarness.Expect(
                    ledger.issuedUniform.Count == issuedCount,
                    "the issued list kept all " + issuedCount + " garments")
                & DebugTools_LifecycleHarness.Expect(
                    ledger.storedForcedApparel.Any(a => a != null
                        && a.GetUniqueLoadID() == forcedLoadID),
                    "the force-worn flag came back on the same garment")
                & DebugTools_LifecycleHarness.Expect(
                    !ledger.IsExcluded,
                    "the loaded stand is in service (control for the sweep below)")
                & DebugTools_LifecycleHarness.Expect(
                    Patch_AllowRemovingToggle.AllowRemovingItemsRef != null
                        && !Patch_AllowRemovingToggle.AllowRemovingItemsRef(loaded),
                    "and a stand saved with the removal flag ON came back with it off");
        }

        // ------------------------------------------------------------ leg 2

        /// <summary>
        /// Rewrites the file leg 1 wrote so the prefixed keys become the
        /// generic ones an older save carries, loads it, and asserts the owner
        /// migrates onto the comp.
        ///
        /// <para>The rewrite runs in-process rather than in the shell runner so
        /// the assertion and the artifact it reads cannot drift apart.</para>
        ///
        /// <para>Three assertions: the owner arrives, the loader reports no
        /// unconsumed load-ids, and a re-save writes the owner back under the
        /// prefixed key. The third is what shows the migration collected the
        /// reference rather than only registering it — an in-memory owner that
        /// does not scribe satisfies the first two.</para>
        /// </summary>
        internal static bool LegacyMigration()
        {
            string sourcePath = GenFilePaths.FilePathForSavedGame(SaveName);
            if (!File.Exists(sourcePath))
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      no round-trip save to rewrite — did the previous case fail?");
                return false;
            }

            // Longest key first: rewriting the short one first would corrupt
            // the long one into "shiftChangeUninstalledassignedPawns".
            string legacy = File.ReadAllText(sourcePath)
                .Replace(OurUninstalledKey, LegacyUninstalledKey)
                .Replace(OurKey, LegacyKey);

            string ownerLoadID = FirstListEntry(legacy, LegacyKey);
            if (ownerLoadID == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      the rewritten save has no owner under " + LegacyKey);
                return false;
            }
            File.WriteAllText(GenFilePaths.FilePathForSavedGame(LegacySaveName), legacy);

            SavedGameLoaderNow.LoadGameFromSaveFileNow(LegacySaveName);

            bool ok = DebugTools_LifecycleHarness.Expect(
                !Logged(UnconsumedWarning),
                "the migration consumed every load-id it registered");

            Building_OutfitStand loaded = FindStandAnywhere();
            if (loaded == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      no stand on the migrated map — cannot assert");
                return false;
            }
            CompAssignableToPawn_ShiftStand assign =
                loaded.TryGetComp<CompAssignableToPawn_ShiftStand>();
            if (assign == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      the migrated stand lost its assignable comp");
                return false;
            }

            ok &= DebugTools_LifecycleHarness.Expect(
                assign.AssignedPawnsForReading.Any(p => p != null
                    && p.GetUniqueLoadID() == ownerLoadID),
                "the legacy owner migrated onto the comp");

            // Re-save: the owner must come back out under the prefixed key,
            // which only a migration that collected the reference produces.
            if (!TrySave(ReSaveName, out string resavePath))
            {
                return false;
            }
            string resaved = File.ReadAllText(resavePath);

            return ok
                & DebugTools_LifecycleHarness.Expect(
                    NodeContains(resaved, OurKey, ownerLoadID),
                    "re-saving writes the migrated owner under " + OurKey)
                & DebugTools_LifecycleHarness.Expect(
                    !NodeContains(resaved, LegacyKey, ownerLoadID),
                    "and stops writing the generic " + LegacyKey);
        }

        // ------------------------------------------------------------ leg 3

        /// <summary>
        /// Asserts a stand carrying another mod's assignable comp round-trips
        /// that mod's owner untouched, and that ours stays empty.
        ///
        /// <para>Comps scribe flat into the thing's node, so two
        /// <c>CompAssignableToPawn</c> instances on one building write the same
        /// generic key.
        /// <see cref="CompAssignableToPawn_ShiftStand.PostExposeData"/> declines
        /// to read that key when a foreign assignable is present; this case
        /// asserts declining leaves no trace — no duplicate registration, no
        /// failed take, no unconsumed load-ids.</para>
        ///
        /// <para><c>ForeignAssignableBesideUs</c> excludes only our own
        /// subclass, so a plain <c>CompAssignableToPawn</c> serves as the
        /// foreign comp and no test-only type is needed. It is added to the DEF
        /// rather than the instance: comps are rebuilt from <c>def.comps</c> by
        /// <c>ThingWithComps.InitializeComps</c> (<c>:200-208</c>), so an
        /// instance-level comp would be absent after the load and the case would
        /// pass without testing anything. The def edit is reverted in a
        /// <c>finally</c>.</para>
        /// </summary>
        internal static bool ForeignAssignable()
        {
            Map map = Find.CurrentMap;
            if (map == null || !Patch_HarnessAutoRun.TryFindPad(map, out IntVec3 origin))
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      no usable pad on the current map — cannot stage");
                return false;
            }
            // A fresh pad on the current map: the map and pad the harness was
            // handed belong to a game the earlier legs replaced.
            CellRect pad = new CellRect(origin.x, origin.z, DebugTools_LifecycleHarness.PadSize,
                                        DebugTools_LifecycleHarness.PadSize);

            ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
            if (standDef == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine("      no outfit stand def");
                return false;
            }

            CompProperties_AssignableToPawn foreignProps = new CompProperties_AssignableToPawn();
            standDef.comps.Add(foreignProps);
            try
            {
                DebugTools_LifecycleHarness.Fixture fix =
                    DebugTools_LifecycleHarness.Stage(map, pad,
                        DebugTools_LifecycleHarness.StageKit.Displacing);
                if (fix == null)
                {
                    DebugTools_LifecycleHarness.Report.AppendLine(
                        "      fixture could not be staged with the foreign comp");
                    return false;
                }

                CompAssignableToPawn foreign = fix.Stand.AllComps
                    .OfType<CompAssignableToPawn>()
                    .FirstOrDefault(c => !(c is CompAssignableToPawn_ShiftStand));
                CompAssignableToPawn_ShiftStand ours =
                    fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                if (foreign == null || ours == null)
                {
                    DebugTools_LifecycleHarness.Report.AppendLine(
                        "      the staged stand does not carry both assignables");
                    return false;
                }

                foreign.TryAssignPawn(fix.Pawn);
                string ownerLoadID = fix.Pawn.GetUniqueLoadID();
                IntVec3 standCell = fix.Stand.Position;

                bool ok = DebugTools_LifecycleHarness.Expect(
                    ours.AssignedPawnsForReading.Count == 0,
                    "our comp starts empty — the foreign owner is not ours (control)");

                if (!TrySave(ForeignSaveName, out string savePath))
                {
                    return false;
                }
                string saved = File.ReadAllText(savePath);
                ok &= DebugTools_LifecycleHarness.Expect(
                        NodeContains(saved, LegacyKey, ownerLoadID),
                        "the save carries their owner under the generic " + LegacyKey)
                    & DebugTools_LifecycleHarness.Expect(
                        !NodeContains(saved, OurKey, ownerLoadID),
                        "and never under " + OurKey);

                SavedGameLoaderNow.LoadGameFromSaveFileNow(ForeignSaveName);

                ok &= DebugTools_LifecycleHarness.Expect(
                        !Logged(DuplicateRegistration),
                        "no comp registered the contested key twice")
                    & DebugTools_LifecycleHarness.Expect(
                        !Logged(FailedTake),
                        "no comp asked for load-ids it never registered")
                    & DebugTools_LifecycleHarness.Expect(
                        !Logged(UnconsumedWarning),
                        "the contested load consumed every load-id it registered");

                Building_OutfitStand loaded = FindStand(standCell) ?? FindStandAnywhere();
                if (loaded == null)
                {
                    DebugTools_LifecycleHarness.Report.AppendLine(
                        "      no stand after the contested load — cannot assert");
                    return false;
                }
                CompAssignableToPawn loadedForeign = loaded.AllComps
                    .OfType<CompAssignableToPawn>()
                    .FirstOrDefault(c => !(c is CompAssignableToPawn_ShiftStand));
                CompAssignableToPawn_ShiftStand loadedOurs =
                    loaded.TryGetComp<CompAssignableToPawn_ShiftStand>();
                if (loadedForeign == null || loadedOurs == null)
                {
                    DebugTools_LifecycleHarness.Report.AppendLine(
                        "      the loaded stand lost one of the two assignables");
                    return false;
                }

                return ok
                    & DebugTools_LifecycleHarness.Expect(
                        loadedForeign.AssignedPawnsForReading.Any(p => p != null
                            && p.GetUniqueLoadID() == ownerLoadID),
                        "their owner survived intact")
                    & DebugTools_LifecycleHarness.Expect(
                        loadedOurs.AssignedPawnsForReading.Count == 0,
                        "and ours did not adopt it");
            }
            finally
            {
                // Revert the def. This case runs last and the process exits
                // shortly after, but a def edit outliving its case would
                // silently change what any later case tests.
                standDef.comps.Remove(foreignProps);
            }
        }

        // ---------------------------------------------------------- plumbing

        /// <summary>
        /// <c>GameDataSaveLoader.SaveGame</c> catches and logs its own
        /// exceptions (<c>:139-142</c>) and returns void, so success is
        /// determined by the file existing.
        /// </summary>
        internal static bool TrySave(string name, out string path)
        {
            path = GenFilePaths.FilePathForSavedGame(name);
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // A delete that fails is not itself interesting — the
                // existence check below is what decides.
            }
            GameDataSaveLoader.SaveGame(name);
            if (File.Exists(path))
            {
                return true;
            }
            DebugTools_LifecycleHarness.Report.AppendLine(
                "      SaveGame wrote nothing to " + path);
            return false;
        }

        /// <summary>
        /// Whether <paramref name="needle"/> appears inside the named element
        /// rather than anywhere in the document. A save repeats each pawn's load
        /// id in many unrelated nodes, so a document-wide <c>Contains</c> would
        /// match a file that lost the owner.
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
        /// Every <c>&lt;node&gt;…&lt;/node&gt;</c> body in the document. A map
        /// carries one per stand; the assertions match on any of them.
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
        /// The stand at a known cell on the current map, which after a load is
        /// the loaded map rather than the one the fixture was built on.
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
        /// Any stand on any loaded map. The migration leg cannot key on a
        /// cell: it stages no fixture of its own, and the save it loads came
        /// from a game two loads back.
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
        /// Whether the log contains <paramref name="needle"/>.
        ///
        /// <para>Scans the whole queue rather than a range past a saved index.
        /// Positional scanning is unsound in two ways, both producing false
        /// passes: the queue caps at 1000 entries and dequeues past that
        /// (<c>LogMessageQueue.cs:8</c>), so a noisy load pushes earlier entries
        /// out from under the index; and an identical message does not enqueue
        /// but increments <c>repeats</c> on the existing entry (<c>:24-34</c>),
        /// placing a repeat of an earlier line behind the index.</para>
        ///
        /// <para>Scanning everything costs a duplicate failure instead — an
        /// earlier leg's warning fails the later legs too. These strings appear
        /// only when a load went wrong.</para>
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
