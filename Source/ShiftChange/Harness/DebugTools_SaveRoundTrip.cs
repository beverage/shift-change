// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
#if HARNESS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Save/load round trips for the state an outfit stand scribes: the assignable comp's owner
    /// lists and <see cref="CompShiftStand"/>'s ledger and catchment.
    ///
    /// <para>The load is synchronous. <c>GameDataSaveLoader.LoadGame</c> queues an asynchronous
    /// long event and disposes the current game, so control never returns to an assertion.
    /// <c>SavedGameLoaderNow.LoadGameFromSaveFileNow</c>, the primitive beneath it, runs the
    /// whole load inline, both scribe passes included, so everything has finished before the
    /// next statement.</para>
    ///
    /// <para>A load replaces <c>Current.Game</c>, so the map the harness was handed and every
    /// reference held across the call belong to a disposed game. Hence these cases run last
    /// among the map cases, as registered in <see cref="DebugTools_LifecycleHarness.Run"/>, and
    /// through the fixture-less <c>Case</c> overload, since <c>Teardown</c> would clear a pad on
    /// the disposed map. The cases are numbered legs in run order: <c>RoundTrip</c> (1),
    /// <c>LegacyMigration</c> (2), <c>ForeignAssignable</c> (3), <c>AreaRoundTrip</c> (4). Each
    /// runs on the game the one before left: leg 2 rewrites leg 1's file, and legs 3 and 4
    /// stage on whatever map is current.</para>
    ///
    /// <para>Assertions read the written file as well as the loaded state: comp state alone
    /// cannot tell a value that scribed from one that never left the object.</para>
    /// </summary>
    internal static class DebugTools_SaveRoundTrip
    {
        /// <summary>
        /// The five save files, left in place so that a failed assertion about file contents
        /// leaves the file to inspect. <c>run-harness.sh</c> wipes its isolated save folder at
        /// the start of each run; a run from the debug menu writes them to that profile's own
        /// Saves folder.
        /// </summary>
        internal const string SaveName = "shiftchange-harness-roundtrip";
        internal const string LegacySaveName = "shiftchange-harness-legacy";
        internal const string ReSaveName = "shiftchange-harness-resave";
        internal const string ForeignSaveName = "shiftchange-harness-foreign";
        internal const string AreaSaveName = "shiftchange-harness-area";

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
        /// retired, so no save carries both, and <see cref="LegacyMigration"/> strips it.
        /// </summary>
        internal const string OurMarkerKey = "shiftChangeOwnersUnified";
        internal const string OurMarkerNode = "<" + OurMarkerKey + ">[^<]*</" + OurMarkerKey + ">";

        /// <summary>
        /// The catchment keys' prefix (<see cref="CompShiftStand.PostExposeData"/>), and every
        /// node under it, written out or null. They too arrived after the generic keys were
        /// retired, so <see cref="LegacyMigration"/> strips them.
        /// </summary>
        internal const string CatchmentKeyPrefix = "shiftChangeServe";
        internal const string CatchmentNodes =
            "<(" + CatchmentKeyPrefix + @"\w*)>[^<]*</\1>|<" + CatchmentKeyPrefix + @"\w*\b[^>]*/>";

        /// <summary>
        /// Logged by <c>LoadIDsWantedBank.ConfirmClear</c> when a reference list registers load
        /// ids during LoadingVars and never collects them during ResolvingCrossRefs. The
        /// reference is dropped with no other error, so the absence of this line is what makes
        /// the migration leg meaningful.
        /// </summary>
        internal const string UnconsumedWarning = "Not all loadIDs which were read were consumed";

        /// <summary>
        /// Logged when two comps read the same node of one thing: <c>LoadIDsWantedBank</c>
        /// refuses the second registration, and the matching take then finds nothing
        /// (<see cref="FailedTake"/>). <see cref="DeclinedAndReconciled"/> and
        /// <see cref="ForeignAssignable"/> assert both are absent.
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
        /// leg 1 logs this string and asserts <see cref="Logged"/> finds it.
        /// </summary>
        internal const string Probe = "[ShiftChange] harness log-scanner probe";

        // ------------------------------------------------------------ leg 1

        /// <summary>
        /// Saves a stand carrying an owner, a populated ledger and a radius catchment, loads it
        /// back, and asserts every scribed field survived, the forced flag included. The stand is
        /// saved with vanilla's removal flag on, and the load must sweep it off. Logs
        /// <see cref="Probe"/>, the control for the file's absence assertions.
        /// </summary>
        internal static bool RoundTrip(Map map, CellRect pad)
        {
            DebugTools_LifecycleHarness.Fixture fix = HarnessFixtures.Build(map, pad);
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
            // Through the comp's own API, not its backing list: the copy into a foreign
            // assignable happens there.
            owned.TryAssignPawn(fix.Pawn);

            // Build leaves storedForcedApparel empty, and the forced flag is under test: record
            // again through NotifyDressed with one stored garment flagged.
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

            // The catchment rides in the same save, at a radius other than the default so a
            // value that never left the object cannot pass for one that scribed. A radius needs
            // a work type ticked first.
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (doctor == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine("      the Doctor work type does not resolve");
                return false;
            }
            fix.Comp.ToggleWork(doctor);
            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(9);

            // Captured before the load: afterwards these objects belong to a disposed game.
            IntVec3 standCell = fix.Stand.Position;
            string ownerLoadID = fix.Pawn.GetUniqueLoadID();
            int ownerID = fix.Pawn.thingIDNumber;
            int storedCount = stored.Count;
            int issuedCount = issued.Count;
            string forcedLoadID = stored[0].GetUniqueLoadID();

            // A stand saved with the removal flag on must load with it off. The toggle's disable
            // only stops the off-to-on change, so the flag can already be on in a save, and
            // CompShiftStand.PostSpawnSetup clears it on every spawn, a load included. Staged in
            // this leg because it already pays for a save/load cycle.
            if (Patch_AllowRemovingToggle.AllowRemovingItemsRef != null)
            {
                Patch_AllowRemovingToggle.AllowRemovingItemsRef(fix.Stand) = true;
            }

            if (!TrySave(SaveName, out string savePath))
            {
                return false;
            }
            string saved = File.ReadAllText(savePath);

            // The generic key is not ours to write, but another mod's assignable on the same stand
            // (Outfit Stands Plus gives the vanilla stand one) saves under it with a copy of the
            // owner. So the owner appears there once per foreign comp, and one more means our comp
            // wrote it; with no foreign assignable the expected count is zero.
            int foreignCopies = fix.Stand.AllComps.OfType<CompAssignableToPawn>()
                .Count(c => !(c is CompAssignableToPawn_ShiftStand));
            int genericHits = NodeBodies(saved, LegacyKey).Count(body => body.Contains(ownerLoadID));
            bool ok = DebugTools_LifecycleHarness.Expect(
                    NodeContains(saved, OurKey, ownerLoadID),
                    "the save carries the owner under " + OurKey)
                & DebugTools_LifecycleHarness.Expect(
                    genericHits == foreignCopies,
                    foreignCopies == 0
                        ? "and not under the generic " + LegacyKey
                        : "and under the generic " + LegacyKey
                          + " only as the foreign comp's copy (" + genericHits
                          + " found, " + foreignCopies + " expected)")
                & DebugTools_LifecycleHarness.Expect(
                    saved.Contains("<shiftChangeServes>Radius</shiftChangeServes>")
                        && saved.Contains("<shiftChangeServeRadius>9</shiftChangeServeRadius>"),
                    "the save carries the catchment under its prefixed keys");

            SavedGameLoaderNow.LoadGameFromSaveFileNow(SaveName);

            // Positive control for the absence assertions, logged after the load so that a load
            // which cleared the queue could not hide it.
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
                return ok & MissingStand("no stand at " + standCell + " after loading");
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
                    ledger.serves == CatchmentKind.Radius && ledger.serveRadius == 9,
                    "the stand still serves a radius of 9")
                & DebugTools_LifecycleHarness.Expect(
                    CompShiftStand.CatchmentStands.Contains(ledger),
                    "and the load put it back in the registry")
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
        /// Rewrites the file leg 1 wrote into the shape of a save from before v1.0.2, loads it,
        /// and asserts the owner comes back and re-saves under the prefixed key. The rewrite
        /// runs in-process, so the assertion and the artifact it reads cannot drift apart.
        ///
        /// <para>Two routes bring the owner back, and whether the loaded stand carries a foreign
        /// assignable picks the one under test. Without one, the key migration in
        /// <see cref="CompAssignableToPawn_ShiftStand.PostExposeData"/> reads the generic keys
        /// (<see cref="KeyMigration"/>). With one, those keys are that comp's: the migration
        /// declines them, and the one-time reconcile adopts the owner from the foreign list on
        /// first spawn (<see cref="DeclinedAndReconciled"/>). Outfit Stands Plus puts a foreign
        /// assignable on every vanilla stand, so the four-mod list only takes the first route
        /// and a list carrying that mod only the second.</para>
        ///
        /// <para>The rewrite also strips the reconciled marker (<see cref="OurMarkerKey"/>) and
        /// the catchment keys, since no save old enough to carry the generic keys has either.
        /// With the marker left in, a stand beside a foreign assignable loads as already
        /// reconciled with an empty list, skips the reconcile, and the copy then empties the
        /// foreign list too, losing the owner.</para>
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
            string source = File.ReadAllText(sourcePath);

            // Read the owner off our key before the rewrite: afterwards the stand can carry the
            // generic key twice, and beside a foreign assignable the first is that comp's copy.
            string ownerLoadID = FirstListEntry(source, OurKey);
            if (ownerLoadID == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      the round-trip save has no owner under " + OurKey);
                return false;
            }

            string legacy = Regex.Replace(Regex.Replace(source
                    .Replace(OurUninstalledKey, LegacyUninstalledKey)
                    .Replace(OurKey, LegacyKey),
                OurMarkerNode, ""), CatchmentNodes, "");
            if (!NodeContains(legacy, LegacyKey, ownerLoadID))
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      the rewritten save has no owner under " + LegacyKey);
                return false;
            }
            File.WriteAllText(GenFilePaths.FilePathForSavedGame(LegacySaveName), legacy);

            bool ok = DebugTools_LifecycleHarness.Expect(
                !legacy.Contains(OurMarkerKey),
                "the rewritten save carries no reconciled marker, as no save that old does")
                & DebugTools_LifecycleHarness.Expect(
                source.Contains("<" + CatchmentKeyPrefix) && !legacy.Contains("<" + CatchmentKeyPrefix),
                "nor the catchment keys the round-trip save wrote, as no save that old does");

            SavedGameLoaderNow.LoadGameFromSaveFileNow(LegacySaveName);

            ok &= DebugTools_LifecycleHarness.Expect(
                !Logged(UnconsumedWarning),
                "the legacy load consumed every load-id it registered");

            Building_OutfitStand loaded = FindStandAnywhere();
            if (loaded == null)
            {
                return ok & MissingStand("no stand on the migrated map");
            }
            CompAssignableToPawn_ShiftStand assign =
                loaded.TryGetComp<CompAssignableToPawn_ShiftStand>();
            if (assign == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      the migrated stand lost its assignable comp");
                return false;
            }

            List<CompAssignableToPawn> foreigners = loaded.AllComps
                .OfType<CompAssignableToPawn>()
                .Where(c => !(c is CompAssignableToPawn_ShiftStand))
                .ToList();
            return ok & (foreigners.Count == 0
                ? KeyMigration(assign, ownerLoadID)
                : DeclinedAndReconciled(assign, foreigners, ownerLoadID));
        }

        /// <summary>
        /// Leg 2 with no foreign assignable: the key migration. The owner must arrive, and a
        /// re-save must write it under the prefixed key.
        /// </summary>
        internal static bool KeyMigration(CompAssignableToPawn_ShiftStand assign, string ownerLoadID)
        {
            bool ok = DebugTools_LifecycleHarness.Expect(
                assign.AssignedPawnsForReading.Any(p => p != null
                    && p.GetUniqueLoadID() == ownerLoadID),
                "the legacy owner migrated onto the comp");

            // Only a migration that collected the reference writes the owner back out under the
            // prefixed key; an in-memory owner that does not scribe passes the checks above.
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

        /// <summary>
        /// Leg 2 beside a foreign assignable: the route
        /// <see cref="CompAssignableToPawn_ShiftStand.PostExposeData"/> promises a legacy save
        /// that carries both mods.
        ///
        /// <para>The decline leaves no trace. The stand's node carries the generic key twice, the
        /// foreign comp's and our rewritten one, and the loader answers every read of it with the
        /// first (<c>ScribeLoader.EnterNode</c> looks the child up by name). Had ours read it
        /// too, the second registration would be refused and its take would find nothing: the
        /// two log lines asserted absent here.</para>
        ///
        /// <para>The owner comes back by the other road. Ours loads empty, the foreign list holds
        /// the owner (leg 1's copy put it there), and the reconcile adopts it. The copy writes it
        /// back, so a re-save carries it under our key and under the generic key once per
        /// foreign comp, the count leg 1 makes.</para>
        /// </summary>
        internal static bool DeclinedAndReconciled(CompAssignableToPawn_ShiftStand assign,
                                                   List<CompAssignableToPawn> foreigners,
                                                   string ownerLoadID)
        {
            CompAssignableToPawn foreign = foreigners[0];
            bool ok = DebugTools_LifecycleHarness.Expect(
                    foreigners.Count == 1,
                    "exactly one foreign assignable, so the contest under test "
                    + "is ours against theirs")
                & DebugTools_LifecycleHarness.Expect(
                    !Logged(DuplicateRegistration),
                    "ours left the generic key to theirs: nothing registered it twice")
                & DebugTools_LifecycleHarness.Expect(
                    !Logged(FailedTake),
                    "no comp asked for load-ids it never registered")
                & DebugTools_LifecycleHarness.Expect(
                    foreign.AssignedPawnsForReading.Any(p => p != null
                        && p.GetUniqueLoadID() == ownerLoadID),
                    "their owner survived intact")
                & DebugTools_LifecycleHarness.Expect(
                    assign.AssignedPawnsForReading.Any(p => p != null
                        && p.GetUniqueLoadID() == ownerLoadID),
                    "and ours took it back through the reconcile, the only route "
                    + "open beside a foreign assignable")
                & DebugTools_LifecycleHarness.Expect(
                    assign.ownersUnified,
                    "and the stand is marked reconciled, so it never adopts again");

            if (!TrySave(ReSaveName, out string resavePath))
            {
                return false;
            }
            string resaved = File.ReadAllText(resavePath);
            int genericHits = NodeBodies(resaved, LegacyKey).Count(body => body.Contains(ownerLoadID));

            return ok
                & DebugTools_LifecycleHarness.Expect(
                    NodeContains(resaved, OurKey, ownerLoadID),
                    "re-saving writes the recovered owner under " + OurKey)
                & DebugTools_LifecycleHarness.Expect(
                    genericHits == foreigners.Count,
                    "and under the generic " + LegacyKey + " only as the foreign comp's copy ("
                    + genericHits + " found, " + foreigners.Count + " expected)");
        }

        // ------------------------------------------------------------ leg 3

        /// <summary>
        /// Asserts that a stand from an older save, carrying another mod's assignable comp with
        /// an owner of its own, round-trips that owner without a contest and adopts it into ours.
        ///
        /// <para>Comps scribe flat into the thing's node, so every <c>CompAssignableToPawn</c>
        /// using the generic key reads and writes the same node. The load must leave no trace of
        /// a contest over it: no duplicate registration, no failed take, no unconsumed load
        /// ids.</para>
        ///
        /// <para>It does not reach the decline in
        /// <see cref="CompAssignableToPawn_ShiftStand.PostExposeData"/>: this save carries our
        /// prefixed keys, empty, and a present key turns the migration off before the contest is
        /// consulted. <see cref="LegacyMigration"/> beside a foreign assignable is the only case
        /// that reaches it.</para>
        ///
        /// <para>A plain <c>CompAssignableToPawn</c> serves as the foreign comp, since
        /// <c>ForeignAssignableBesideUs</c> excludes only our subclass. It is added to the def,
        /// not the instance: <c>ThingWithComps.InitializeComps</c> rebuilds comps from
        /// <c>def.comps</c> on load, so an instance-level comp would be gone after the load and
        /// the case would pass without testing anything. The def edit is reverted in a
        /// <c>finally</c>.</para>
        /// </summary>
        internal static bool ForeignAssignable()
        {
            Map map = Find.CurrentMap;
            if (map == null || !Patch_HarnessAutoRun.TryFindPad(map, out IntVec3 origin))
            {
                return CannotStage("no usable pad on the current map");
            }
            // A fresh pad on the current map: the map and pad the harness was handed belong to a
            // game an earlier leg replaced.
            CellRect pad = new CellRect(origin.x, origin.z, DebugTools_LifecycleHarness.PadSize,
                                        DebugTools_LifecycleHarness.PadSize);

            ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
            if (standDef == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine("      no outfit stand def");
                return false;
            }

            // Reuse a foreign assignable when the mod list already supplies one. A second would
            // make two wherever a mod put one on this def, and the case would test theirs against
            // theirs: Outfit Stands Plus' base comp derives from CompAssignableToPawn without
            // overriding PostExposeData, so it scribes the generic keys exactly as the plain one
            // added here does, and the load logs a duplicate registration. The finders exclude
            // only our subclass and cannot tell the added comp from a supplied one, and the
            // four-mod list has no foreign assignable, so only a larger list shows it.
            CompProperties present = standDef.comps.FirstOrDefault(
                cp => cp != null && cp.compClass != null
                      && typeof(CompAssignableToPawn).IsAssignableFrom(cp.compClass)
                      && !typeof(CompAssignableToPawn_ShiftStand).IsAssignableFrom(cp.compClass));
            CompProperties_AssignableToPawn foreignProps = null;
            if (present == null)
            {
                foreignProps = new CompProperties_AssignableToPawn();
                standDef.comps.Add(foreignProps);
            }
            try
            {
                DebugTools_LifecycleHarness.Fixture fix =
                    HarnessFixtures.Stage(map, pad,
                        DebugTools_LifecycleHarness.StageKit.Displacing);
                if (fix == null)
                {
                    DebugTools_LifecycleHarness.Report.AppendLine(
                        "      fixture could not be staged with the foreign comp");
                    return false;
                }

                List<CompAssignableToPawn> foreigners = fix.Stand.AllComps
                    .OfType<CompAssignableToPawn>()
                    .Where(c => !(c is CompAssignableToPawn_ShiftStand))
                    .ToList();
                CompAssignableToPawn foreign = foreigners.FirstOrDefault();
                CompAssignableToPawn_ShiftStand ours =
                    fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                if (foreign == null || ours == null)
                {
                    DebugTools_LifecycleHarness.Report.AppendLine(
                        "      the staged stand does not carry both assignables");
                    return false;
                }

                // An owner set through the foreign comp's own call, which none of our overrides
                // see, and the reconciled marker off: the shape a stand from before the copy loads
                // with.
                foreign.TryAssignPawn(fix.Pawn);
                ours.ownersUnified = false;
                string ownerLoadID = fix.Pawn.GetUniqueLoadID();
                IntVec3 standCell = fix.Stand.Position;

                // Asserted, so a list that grows a second foreign comp says so: the case is ours
                // against theirs, and a second turns it into theirs against theirs while it still
                // goes through the motions.
                bool ok = DebugTools_LifecycleHarness.Expect(
                        foreigners.Count == 1,
                        "exactly one foreign assignable, so the contest under test "
                        + "is ours against theirs")
                    & DebugTools_LifecycleHarness.Expect(
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
                    return ok & MissingStand("no stand after the contested load");
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
                        loadedOurs.AssignedPawnsForReading.Any(p => p != null
                            && p.GetUniqueLoadID() == ownerLoadID),
                        "and ours adopted it, the only owner either list had")
                    & DebugTools_LifecycleHarness.Expect(
                        loadedOurs.ownersUnified,
                        "and the stand is marked reconciled, so it never adopts again");
            }
            finally
            {
                // Revert only what this case added. A def edit outliving its case would change
                // what later cases test, and a list that supplied its own foreign assignable added
                // nothing here, so this must not remove somebody else's comp.
                if (foreignProps != null)
                {
                    standDef.comps.Remove(foreignProps);
                }
            }
        }

        // ------------------------------------------------------------ leg 4

        /// <summary>
        /// Saves two stands serving allowed areas, one whose area was renamed and one whose area
        /// was deleted, loads them back, and asserts the first came back bound to its area under
        /// the new name, and the second in area mode with no area, under the deleted one's name.
        /// A third, boxed and carried by a world pawn so it is on no map, keeps its binding to
        /// the live area through the save, as a stand carried by a caravan must to serve that
        /// area again at home.
        ///
        /// <para>The file is asserted first. A rename changes an allowed area's load ID, which
        /// embeds the name, so the reference has to carry the name the area had when saved. The
        /// deleted area's reference must be dropped rather than left for the loader to fail on,
        /// and a stand with no binding writes no reference node at all.</para>
        ///
        /// <para>A leg of its own on a fresh pad: leg 2 loads leg 1's file and takes the first
        /// stand it finds, so more stands in that file would make leg 2 test whichever came
        /// first.</para>
        /// </summary>
        internal static bool AreaRoundTrip()
        {
            Map map = Find.CurrentMap;
            if (map == null || !Patch_HarnessAutoRun.TryFindPad(map, out IntVec3 origin))
            {
                return CannotStage("no usable pad on the current map");
            }
            CellRect pad = new CellRect(origin.x, origin.z, DebugTools_LifecycleHarness.PadSize,
                                        DebugTools_LifecycleHarness.PadSize);
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            DebugTools_LifecycleHarness.Fixture fix = doctor == null
                ? null
                : HarnessFixtures.Stage(map, pad, DebugTools_LifecycleHarness.StageKit.Displacing,
                                        enclose: true);
            if (fix == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      the Doctor work type or the fixture could not be had");
                return false;
            }

            IntVec3 boundCell = fix.Stand.Position;
            IntVec3 paintedCell = boundCell + new IntVec3(2, 0, 2);
            IntVec3 lostCell = boundCell + new IntVec3(4, 0, 2);
            CompShiftStand lost = HarnessCatchments.StageStand(fix, lostCell);
            Area_Allowed area = HarnessCatchments.NewArea(fix, "Shift harness area", boundCell, paintedCell);
            Area_Allowed doomed = HarnessCatchments.NewArea(fix, "Shift harness gone", lostCell);
            if (lost == null || area == null || doomed == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      the second stand or the two areas could not be staged");
                return false;
            }
            // A third stand, boxed and carried by a colonist off every map, as a caravan carries
            // one. Its area lives on, so the save keeps the binding for when the stand is set
            // down at home again.
            CompShiftStand boxed = HarnessCatchments.StageStand(fix, boundCell + new IntVec3(3, 0, 3));
            if (boxed == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine("      the boxed stand could not be staged");
                return false;
            }
            fix.Comp.ToggleWork(doctor);
            fix.Comp.SetServeArea(area);
            area.SetLabel("Shift harness renamed");
            lost.ToggleWork(doctor);
            lost.SetServeArea(doomed);
            doomed.Delete();
            boxed.ToggleWork(doctor);
            boxed.SetServeArea(area);
            MinifiedThing box = boxed.parent.MakeMinified();
            Pawn carrier = DebugTools_Fixtures.AveragePawn(Gender.Male, "Carrier");
            bool carried = box != null && carrier.inventory.innerContainer.TryAdd(box);
            Find.WorldPawns.PassToWorld(carrier, RimWorld.Planet.PawnDiscardDecideMode.KeepForever);
            string carrierID = carrier.ThingID;
            string areaLoadID = area.GetUniqueLoadID();
            string doomedLoadID = doomed.GetUniqueLoadID();

            bool ok = DebugTools_LifecycleHarness.Expect(
                fix.Comp.serves == CatchmentKind.Area && lost.serves == CatchmentKind.Area,
                "both stands serve an area before the save (control)")
                & DebugTools_LifecycleHarness.Expect(
                    carried && boxed.parent.MapHeld == null && boxed.serveArea == area
                        && boxed.serves == CatchmentKind.Area,
                    "a third stand serving the area is boxed and carried off every map (control)");

            if (!TrySave(AreaSaveName, out string savePath))
            {
                return false;
            }
            string saved = File.ReadAllText(savePath);
            ok &= DebugTools_LifecycleHarness.Expect(
                    NodeContains(saved, AreaKey, areaLoadID),
                    "the save carries the binding under the renamed area's load ID")
                & DebugTools_LifecycleHarness.Expect(
                    NodeContains(saved, AreaLabelKey, "Shift harness renamed"),
                    "and the new name as its label")
                & DebugTools_LifecycleHarness.Expect(
                    !NodeContains(saved, AreaKey, doomedLoadID)
                        && NodeContains(saved, AreaLabelKey, "Shift harness gone"),
                    "the deleted area's stand keeps its name but no reference to it")
                & DebugTools_LifecycleHarness.Expect(
                    NodeBodies(saved, AreaKey).Count() == 2
                        && NodeBodies(saved, AreaKey).All(body => body.Contains(areaLoadID)),
                    "and only the bound stand and the boxed one write a reference node, both to the live area");

            SavedGameLoaderNow.LoadGameFromSaveFileNow(AreaSaveName);
            ok &= DebugTools_LifecycleHarness.Expect(
                !Logged(UnconsumedWarning),
                "the load consumed every load-id it registered");

            Building_OutfitStand loadedBound = FindStand(boundCell);
            Building_OutfitStand loadedLost = FindStand(lostCell);
            if (loadedBound == null || loadedLost == null)
            {
                return ok & MissingStand("no area stands at " + boundCell + " and " + lostCell
                                         + " after loading");
            }
            CompShiftStand bound = loadedBound.TryGetComp<CompShiftStand>();
            CompShiftStand unbound = loadedLost.TryGetComp<CompShiftStand>();
            if (bound == null || unbound == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      a loaded stand lost its comp — cannot assert");
                return false;
            }
            Area live = bound.LiveServeArea();
            Pawn loadedCarrier = Find.WorldPawns.AllPawnsAliveOrDead.FirstOrDefault(p => p.ThingID == carrierID);
            CompShiftStand loadedBoxed = (loadedCarrier?.inventory?.innerContainer
                    .OfType<MinifiedThing>().FirstOrDefault()?.InnerThing)?.TryGetComp<CompShiftStand>();
            ok &= DebugTools_LifecycleHarness.Expect(
                loadedBoxed != null && loadedBoxed.serves == CatchmentKind.Area && live != null
                    && loadedBoxed.serveArea == live,
                "the boxed stand came back still bound to its area, off every map");
            return ok
                & DebugTools_LifecycleHarness.Expect(
                    bound.serves == CatchmentKind.Area && live != null
                        && live.Label == "Shift harness renamed",
                    "the bound stand came back serving its area, under the new name")
                & DebugTools_LifecycleHarness.Expect(
                    bound.Covers(paintedCell, loadedBound.Map),
                    "which covers the cell painted in it")
                & DebugTools_LifecycleHarness.Expect(
                    CompShiftStand.CatchmentStands.Contains(bound),
                    "and the load put it back in the registry")
                & DebugTools_LifecycleHarness.Expect(
                    unbound.serves == CatchmentKind.Area && unbound.serveArea == null
                        && unbound.serveAreaLabel == "Shift harness gone",
                    "the other came back in area mode with no area, under the deleted one's name")
                & DebugTools_LifecycleHarness.Expect(
                    !unbound.Covers(lostCell, loadedLost.Map),
                    "covering nothing");
        }

        // ---------------------------------------------------------- plumbing

        /// <summary>
        /// Saves under <paramref name="name"/>. <c>GameDataSaveLoader.SaveGame</c> returns void
        /// and catches and logs its own exceptions, so success is the file existing.
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
                // A failed delete is not itself a failure; the existence check below decides.
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
        /// not the one the fixture was built on.
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
        /// Any stand on any loaded map. The migration leg stages no fixture and is not handed
        /// leg 1's cell, and the foreign leg falls back to it when its stand is not at the cell
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
        /// The stand is gone after a load: decides whether that is our failure or the
        /// environment's. A known gap when <see cref="FinalizeAbort"/> is in the log, since the
        /// load never reached the pass that spawns things and the case exercised nothing;
        /// calling that a failure would blame our scribing for a third party's static cache.
        ///
        /// <para>It excuses nothing else. Every caller ands in its own running result, so
        /// assertions that failed earlier in the case still fail it, and with no abort logged
        /// the stand is missing for a reason nobody understands, which stays a failure.</para>
        /// </summary>
        internal static bool MissingStand(string where)
        {
            string culprit = FinalizeAbortCulprit();
            if (culprit == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      " + where + " — cannot assert");
                return false;
            }
            return DebugTools_LifecycleHarness.ExpectKnownGap(
                false, where,
                "Map.FinalizeLoading aborted in " + culprit
                + ", so the map spawned nothing. Not ours: a third-party static "
                + "cache that is not cleared between in-process loads");
        }

        /// <summary>
        /// There is nowhere to stage: the same fork as <see cref="MissingStand"/>, one step
        /// earlier. When the load before aborted in <c>Map.FinalizeLoading</c>, the region and
        /// room grids were never built, <c>TryFindPad</c> requires a room on every cell of the
        /// pad and its margin, and legs 3 and 4 find no origin anywhere on the map.
        ///
        /// <para>That is a known gap only when <see cref="FinalizeAbort"/> is in the log. A map
        /// that was never usable is a broken harness invocation and stays a failure, as it does
        /// in <see cref="Patch_HarnessAutoRun"/> before the run starts. Nothing past staging is
        /// excused: no stand def, no fixture, an assignable missing off the staged stand all
        /// still fail.</para>
        /// </summary>
        internal static bool CannotStage(string where)
        {
            string culprit = FinalizeAbortCulprit();
            if (culprit == null)
            {
                DebugTools_LifecycleHarness.Report.AppendLine(
                    "      " + where + " — cannot stage");
                return false;
            }
            return DebugTools_LifecycleHarness.ExpectKnownGap(
                false, where,
                "Map.FinalizeLoading aborted in " + culprit
                + ", so the map an earlier leg left behind has no region grid "
                + "to stage on. Not ours: a third-party static cache that is "
                + "not cleared between in-process loads");
        }

        /// <summary>
        /// The first mod frame inside a FinalizeLoading abort, or null when no abort was logged.
        /// It names whose cache broke the load, so the reader need not open Player.log.
        /// </summary>
        internal static string FinalizeAbortCulprit()
        {
            bool aborted = false;
            foreach (LogMessage message in Log.Messages)
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
            return aborted ? "a frame the harness could not name" : null;
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
        /// The cost is a duplicate failure, an earlier leg's warning failing the later legs too;
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
#endif
