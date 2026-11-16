using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DevTools;
using DevTools.Testing;
using RimWorld;
using Verse;
using static ShiftChange.Tests.SaveRoundTrip;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Saving a game with stands in use and loading it again, and loading saves from older
    /// versions of the mod: each test on the game the one before it loaded. Only the first reads
    /// the pad. The others run on the game the test before loaded, which a mod that breaks a load
    /// can leave with no clear pad, and they reach their own known gap for that.
    ///
    /// <para>A load replaces the game DevTools generated, and DevTools goes on tracking that
    /// game's map. Asking for a world of its own makes DevTools generate a fresh one for this
    /// fixture and again for whichever fixture follows. It runs after every other
    /// (<see cref="RunOrder.SaveLoadPriority"/>), so none follows.</para>
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.SaveLoadPriority)]
    [WarningsAllowedFrom(SavingAndLoading.VarietyFrame, SavingAndLoading.StaleReference)]
    [ErrorsAllowedFrom(SavingAndLoading.VarietyFrame, SavingAndLoading.WrongReference)]
    [ErrorsAllowedFrom(SavingAndLoading.OwnLoadFrame, SavingAndLoading.VehicleCacheAbort)]
    internal class SavingAndLoading : StoryFixture, IWorldGeneration
    {
        WorldGenerationSettings IWorldGeneration.WorldGenerationSettings
        {
            get { return new WorldGenerationSettings(); }
        }

        /// <summary>
        /// The save code of Vanilla Food Variety Expanded's per-colonist diet record. The mod keeps
        /// one for every colonist it has seen in a static table that no game end clears, and
        /// DevTools plays several games in one run, so an earlier game's colonists reach this
        /// fixture's saves. Their load logs a reference that resolves to nothing or to some other
        /// thing. A stand owner saved wrongly logs the same lines from this mod's code, so they
        /// pass only with this frame on their stack.
        /// </summary>
        internal const string VarietyFrame =
            @"VanillaCookingExpandedVariety\.PawnDiet\.ExposeData";

        /// <summary>
        /// The warnings for a stale pawn or corpse id: it matches nothing, or falls back to
        /// whatever thing now has its number.
        /// </summary>
        internal const string StaleReference =
            @"^Could not resolve reference to (Thing|object) with loadID Thing_";

        /// <summary>The error for a stale id whose number now belongs to a thing of another type.</summary>
        internal const string WrongReference =
            @"^Exception getting object with thing id \d+ of type Verse\.(Pawn|Corpse)\.";

        /// <summary>
        /// This mod's load call. Vehicle Framework keeps a static map-component cache keyed by map
        /// index that no in-process load clears, so a test's load can collide with an earlier
        /// game's entry and abort FinalizeLoading; the test already records that as a known gap.
        /// The abort's stack holds only engine frames and this call, and its message names the
        /// cache, which no code of this mod can.
        /// </summary>
        internal const string OwnLoadFrame = @"ShiftChange\.Tests\.DebugTools_SaveRoundTrip\.NoteLoad\(";

        /// <summary>The FinalizeLoading abort that names Vehicle Framework's cache.</summary>
        internal const string VehicleCacheAbort =
            @"^Error in Map\.FinalizeLoading\(\): [\s\S]*SmashTools\.DetachedMapComponentCache";

        /// <summary>
        /// Runs a test as the game runs a load: inside LongEventHandler's finishing pass, so what
        /// the load defers to that pass runs after the load. With no long event running, a
        /// deferred action runs on the spot, halfway through the load, before the world it reads
        /// has been loaded.
        /// </summary>
        protected override void Wrap(Action runCase)
        {
            LongEventHandler.ExecuteWhenFinished(runCase);
        }

        /// <summary>
        /// The saves the running test has written or read. They go into the Saves folder of the
        /// game the tests run in, which is the player's own unless the game was started with a
        /// save folder of its own, as the lab's runs are.
        /// </summary>
        internal static readonly SortedSet<string> SavesInUse = new SortedSet<string>();

        /// <summary>Each test this fixture has run, with the saves it used.</summary>
        internal readonly List<KeyValuePair<ITestGroup, string[]>> SavesUsed =
            new List<KeyValuePair<ITestGroup, string[]>>();

        /// <summary>Starts the fixture's record of saves empty.</summary>
        [OneTimeSetUp]
        internal void ForgetSaves()
        {
            SavesInUse.Clear();
            SavesUsed.Clear();
        }

        /// <summary>Notes the saves the test that just ran used, for <see cref="RemoveSaves"/>.</summary>
        [TearDown]
        internal void NoteSavesUsed()
        {
            SavesUsed.Add(new KeyValuePair<ITestGroup, string[]>(Test.Current, SavesInUse.ToArray()));
            SavesInUse.Clear();
        }

        /// <summary>
        /// Deletes the saves these tests wrote once they have all run, except those a failed test
        /// used, which stay for a bug report; Test.log names them and their folder. It waits for
        /// the end because a test's result is settled only after its own tear-down, when DevTools
        /// checks the log for errors, and because a test reads the save the one before it wrote.
        /// </summary>
        [OneTimeTearDown]
        internal void RemoveSaves()
        {
            SortedSet<string> used = new SortedSet<string>();
            SortedSet<string> kept = new SortedSet<string>();
            foreach (KeyValuePair<ITestGroup, string[]> test in SavesUsed)
            {
                used.UnionWith(test.Value);
                if (test.Key?.Status == Status.Failed)
                {
                    kept.UnionWith(test.Value);
                }
            }
            List<string> removed = new List<string>();
            foreach (string name in used.Where(n => !kept.Contains(n)))
            {
                string path = GenFilePaths.FilePathForSavedGame(name);
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        removed.Add(name);
                    }
                }
                catch (Exception e)
                {
                    DevLog.Write("[ShiftChange] could not delete the save " + name + ": " + e.Message);
                }
            }
            if (removed.Count > 0)
            {
                DevLog.Write("[ShiftChange] deleted the saves the save/load tests wrote: "
                             + string.Join(", ", removed));
            }
            if (kept.Count > 0)
            {
                DevLog.Write("[ShiftChange] kept for a bug report, since a test that used them failed: "
                             + string.Join(", ", kept) + ", in " + GenFilePaths.SavedGamesFolderPath
                             + ". Send them with this log, then delete them.");
            }
        }

        // These replace Current.Game, so DevTools' map, the pad and every scene set up before
        // them belong to a disposed game once they have run. They stage no scene for the same
        // reason, since the frame's teardown would clear a pad on the disposed map. See
        // SaveRoundTrip.

        /// <summary>
        /// Saves a stand carrying an owner, a colonist on shift with the clothes it keeps for him,
        /// and a radius it serves, loads it back, and checks that everything it saved survived,
        /// the forced mark on a kept garment included. The stand is saved with the game's Allow
        /// removing items on, and the load must turn it off. Logs
        /// <see cref="SaveRoundTrip.Probe"/>, which shows the log search works, for the
        /// checks in these tests that a line is absent.
        ///
        /// <para>The checks read the saved file as well as the loaded game: the loaded stand alone
        /// cannot tell a value that was saved from one that never left it.</para>
        /// </summary>
        [Test]
        [TestDescription("a stand in use comes back from a save with its owner, the clothes it keeps "
                         + "and its radius, and with Allow removing items off")]
        internal IEnumerator AStandInUseComesBackFromASave()
        {
            return OnThePad(Body);

            void Body(Story story)
            {
                Scene scene = Build(Map, Pad);
                if (scene == null)
                {
                    story.Check(false, "a colonist on shift at a stand can be set up on the pad");
                    return;
                }
                string name = scene.Colonist.LabelShort;
                story.Note(name + " is on shift at a stand: he wears what it held, and it keeps the "
                           + "clothes of his that made way");

                CompAssignableToPawn_ShiftStand owned =
                    scene.OutfitStand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                if (owned == null)
                {
                    story.Check(false, "the stand has Shift Change's owner list");
                    return;
                }
                // Through the comp's own call, not its backing list: the copy into another mod's
                // owner list happens there.
                owned.TryAssignPawn(scene.Colonist);
                story.Note(name + " is made the stand's owner");

                // Build leaves the forced marks empty, and they are under test: record the change
                // again through NotifyDressed with one kept garment marked.
                List<Apparel> stored = new List<Apparel>(scene.Comp.storedOwnerApparel);
                List<Apparel> issued = new List<Apparel>(scene.Comp.issuedUniform);
                if (stored.Count == 0 || issued.Count == 0)
                {
                    story.Check(false, "the stand keeps clothes of " + name + "'s, and has lent him its own");
                    return;
                }
                scene.Comp.NotifyDressed(scene.Colonist, stored, issued,
                                       new List<Apparel> { stored[0] });
                story.Note("the change is recorded again, with " + name + "'s " + stored[0].def.label
                           + " remembered as forced, as clothes he was told to wear are");

                // What the stand serves rides in the same save, at a radius other than the default
                // so a value that never left the stand cannot pass for one that was saved. A
                // radius needs a work type ticked first.
                WorkTypeDef doctor = WorkType("Doctor");
                if (doctor == null)
                {
                    story.Check(false, "the game has its Doctor work");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(9);
                story.Note(doctor.labelShort + " is ticked, and the stand serves within 9 cells");

                // Captured before the load: afterwards these objects belong to a disposed game.
                IntVec3 standCell = scene.OutfitStand.Position;
                string ownerLoadID = scene.Colonist.GetUniqueLoadID();
                int ownerID = scene.Colonist.thingIDNumber;
                int storedCount = stored.Count;
                int issuedCount = issued.Count;
                string forcedLoadID = stored[0].GetUniqueLoadID();
                string forcedLabel = stored[0].def.label;

                // A stand saved with Allow removing items on must load with it off. The switch's
                // greying only stops turning it on, so it can already be on in a save, and
                // CompShiftStand.PostSpawnSetup turns it off on every spawn, a load included. Set
                // up in this test because it already pays for a save and a load.
                if (Patch_AllowRemovingToggle.AllowRemovingItemsRef != null)
                {
                    Patch_AllowRemovingToggle.AllowRemovingItemsRef(scene.OutfitStand) = true;
                    story.Note("Allow removing items is switched on, as a save can carry it");
                }

                if (!TrySave(story, SaveName, out string savePath))
                {
                    return;
                }
                string saved = File.ReadAllText(savePath);

                // The game's key is not Shift Change's to write, but another mod's owner list on
                // the same stand (Outfit Stands Plus gives the vanilla stand one) saves under it
                // with a copy of the owner. So the owner appears there once per other list, and one
                // more means Shift Change's list wrote it; with no other list the count is zero.
                int foreignCopies = scene.OutfitStand.AllComps.OfType<CompAssignableToPawn>()
                    .Count(c => !(c is CompAssignableToPawn_ShiftStand));
                int genericHits = NodeBodies(saved, LegacyKey).Count(body => body.Contains(ownerLoadID));
                story.Note("Shift Change saves a stand's owners under a key of its own, " + OurKey
                           + "; the game's owner lists, and older versions of Shift Change, under " + LegacyKey);
                story.Check(NodeContains(saved, OurKey, ownerLoadID),
                            "the save names " + name + " as the stand's owner under Shift Change's own key");
                story.Check(genericHits == foreignCopies,
                            foreignCopies == 0
                                ? "and not under the game's"
                                : "and under the game's only as the other mod's list's copy (" + genericHits
                                  + " found, " + foreignCopies + " expected)");
                story.Check(saved.Contains("<shiftChangeServes>Radius</shiftChangeServes>")
                            && saved.Contains("<shiftChangeServeRadius>9</shiftChangeServeRadius>"),
                            "the save says the stand serves within 9 cells, under Shift Change's own keys");

                // Loaded with New stands are used for shift changes off, which must not reach a
                // stand that already exists: the setting is read only as a stand is made.
                bool newStandsWas = ShiftChangeMod.Settings.newStandsInService;
                ShiftChangeMod.Settings.newStandsInService = false;
                story.Note("New stands are used for shift changes is switched off for the load");
                try
                {
                    Load(SaveName);
                }
                finally
                {
                    ShiftChangeMod.Settings.newStandsInService = newStandsWas;
                }
                story.Note("the game is loaded from " + SaveName + ", in place of the one it was saved from");

                // A line known to be there, for the checks that a line is absent: logged after the
                // load, so that a load which cleared the log could not hide it.
                Log.Message(SaveRoundTrip.Probe);
                story.Check(Logged(SaveRoundTrip.Probe),
                            "a line just written to the log is found there, so searching the log works");
                story.Check(!Logged(UnconsumedWarning),
                            "the load resolved every reference it read: the log has no warning of any left over");

                Building_OutfitStand loaded = FindStand(standCell);
                if (loaded == null)
                {
                    MissingStand(story, "the stand is back at " + standCell + " after loading");
                    return;
                }

                CompShiftStand ledger = loaded.TryGetComp<CompShiftStand>();
                CompAssignableToPawn_ShiftStand assign =
                    loaded.TryGetComp<CompAssignableToPawn_ShiftStand>();
                if (ledger == null || assign == null)
                {
                    story.Check(false, "the loaded stand still has Shift Change's settings and owner list");
                    return;
                }

                story.Check(assign.AssignedPawnsForReading.Any(p => p != null && p.thingIDNumber == ownerID),
                            name + " is still the stand's owner");
                story.Check(ledger.Borrower != null && ledger.Borrower.thingIDNumber == ownerID,
                            "and still the one it lends its outfit to");
                story.Check(ledger.OnShift, "the stand's shift is still on");
                story.Check(ledger.storedOwnerApparel.Count == storedCount,
                            "it still keeps all " + storedCount + " of " + name + "'s own garments");
                story.Check(ledger.issuedUniform.Count == issuedCount,
                            "and still has all " + issuedCount + " of its own lent to him");
                story.Check(ledger.storedForcedApparel.Any(a => a != null
                                && a.GetUniqueLoadID() == forcedLoadID),
                            "his " + forcedLabel + " is still remembered as forced, the same garment");
                story.Check(ledger.serves == CatchmentKind.Radius && ledger.serveRadius == 9,
                            "the stand still serves within 9 cells");
                story.Check(CompShiftStand.CatchmentStands.Contains(ledger),
                            "and the load listed it again among the stands that serve beyond their room");
                story.Check(!ledger.IsExcluded,
                            "the loaded stand is still used for shift changes, though New stands are used "
                            + "for shift changes was off: that setting reaches only stands built after it");
                story.Check(Patch_AllowRemovingToggle.AllowRemovingItemsRef != null
                                && !Patch_AllowRemovingToggle.AllowRemovingItemsRef(loaded),
                            "and Allow removing items, saved on, came back off, as for any stand in service");
            }
        }

        /// <summary>
        /// Rewrites the file the test before wrote into the shape of a save from before Shift
        /// Change kept owners under keys of its own, loads it, and checks the owner comes back and
        /// saves again under Shift Change's key. The rewrite runs in the test, so the check and the
        /// file it reads cannot drift apart.
        ///
        /// <para>Two routes bring the owner back, and whether the loaded stand carries another
        /// mod's owner list picks the one under test. Without one, the key migration in
        /// <see cref="CompAssignableToPawn_ShiftStand.PostExposeData"/> reads the game's keys
        /// (<see cref="KeyMigration"/>). With one, those keys are that list's: the migration
        /// declines them, and the one-time reconcile adopts the owner from the other list on first
        /// spawn (<see cref="DeclinedAndReconciled"/>). Outfit Stands Plus puts an owner list of
        /// its own on every vanilla stand, so the four-mod list only takes the first route and a
        /// list carrying that mod only the second.</para>
        ///
        /// <para>The rewrite also strips the reconciled mark
        /// (<see cref="SaveRoundTrip.OurMarkerKey"/>) and what the stand serves, since
        /// no save old enough to carry the game's keys has either. With the mark left in, a stand
        /// beside another mod's list loads as already reconciled with an empty list, skips the
        /// reconcile, and the copy then empties the other list too, losing the owner.</para>
        /// </summary>
        [Test]
        [TestDescription("a save from an older Shift Change gives the stand its owner back, "
                         + "and saving again writes the owner the new way")]
        internal IEnumerator ASaveFromAnOlderShiftChangeGetsItsOwnerBack()
        {
            return Unstaged(Body);

            void Body(Story story)
            {
                SavesInUse.Add(SaveName);
                string sourcePath = GenFilePaths.FilePathForSavedGame(SaveName);
                if (!File.Exists(sourcePath))
                {
                    story.Check(false, "the save the test before wrote, " + SaveName + ", is there to rewrite");
                    return;
                }
                string source = File.ReadAllText(sourcePath);

                // Read the owner off Shift Change's key before the rewrite: afterwards the stand can
                // carry the game's key twice, and beside another mod's owner list the first is that
                // list's copy.
                string ownerLoadID = FirstListEntry(source, OurKey);
                if (ownerLoadID == null)
                {
                    story.Check(false, "that save names the stand's owner under Shift Change's own key");
                    return;
                }

                string legacy = Regex.Replace(Regex.Replace(source
                        .Replace(OurUninstalledKey, LegacyUninstalledKey)
                        .Replace(OurKey, LegacyKey),
                    OurMarkerNode, ""), CatchmentNodes, "");
                if (!NodeContains(legacy, LegacyKey, ownerLoadID))
                {
                    story.Check(false, "rewritten, it names the owner under the game's key instead");
                    return;
                }
                SavesInUse.Add(LegacySaveName);
                File.WriteAllText(GenFilePaths.FilePathForSavedGame(LegacySaveName), legacy);
                story.Note(SaveName + " is rewritten as " + LegacySaveName + ", the way an older Shift "
                           + "Change saved: the stand's owners under the game's key, " + LegacyKey
                           + ", not its own");

                story.Check(!legacy.Contains(OurMarkerKey),
                            "the rewritten save has no mark that the stand's owner lists were reconciled, "
                            + "as no save that old does");
                story.Check(source.Contains("<" + CatchmentKeyPrefix) && !legacy.Contains("<" + CatchmentKeyPrefix),
                            "nor what the stand serves, which the save it came from had, as no save that old does");

                Load(LegacySaveName);
                story.Note("the game is loaded from " + LegacySaveName);

                story.Check(!Logged(UnconsumedWarning),
                            "loading it resolved every reference it read: the log has no warning of any left over");

                Building_OutfitStand loaded = FindStandAnywhere();
                if (loaded == null)
                {
                    MissingStand(story, "the stand is on the loaded map");
                    return;
                }
                CompAssignableToPawn_ShiftStand assign =
                    loaded.TryGetComp<CompAssignableToPawn_ShiftStand>();
                if (assign == null)
                {
                    story.Check(false, "the loaded stand still has Shift Change's owner list");
                    return;
                }

                List<CompAssignableToPawn> foreigners = loaded.AllComps
                    .OfType<CompAssignableToPawn>()
                    .Where(c => !(c is CompAssignableToPawn_ShiftStand))
                    .ToList();
                if (foreigners.Count == 0)
                {
                    KeyMigration(story, assign, ownerLoadID);
                }
                else
                {
                    DeclinedAndReconciled(story, assign, foreigners, ownerLoadID);
                }
            }
        }

        /// <summary>
        /// Checks that a stand from an older save, carrying another mod's owner list with an owner
        /// of its own, saves and loads that owner with no contest over it, and that Shift Change's
        /// list adopts it.
        ///
        /// <para>Owner lists save flat into the stand's own entry, so every owner list using the
        /// game's key reads and writes the same entry. The load must leave no trace of a contest
        /// over it: no list reading it twice, none asking for references it never read, none left
        /// over.</para>
        ///
        /// <para>It does not reach the decline in
        /// <see cref="CompAssignableToPawn_ShiftStand.PostExposeData"/>: this save carries Shift
        /// Change's own keys, empty, and a key that is there turns the migration off before the
        /// contest is consulted. <see cref="ASaveFromAnOlderShiftChangeGetsItsOwnerBack"/> beside
        /// another mod's list is the only test that reaches it.</para>
        ///
        /// <para>The game's plain owner list (<c>CompAssignableToPawn</c>) stands in for the other
        /// mod's, since <c>ForeignAssignableBesideUs</c> excludes only Shift Change's subclass. It
        /// is added to the stand's def, not the stand: <c>ThingWithComps.InitializeComps</c>
        /// rebuilds comps from <c>def.comps</c> on load, so a comp added to the stand alone would
        /// be gone after the load and the test would pass without testing anything. The def edit is
        /// undone in a <c>finally</c>.</para>
        /// </summary>
        [Test]
        [TestDescription("a stand from an older save, owned through another mod's owner list, loads "
                         + "with no contest over its owner, and Shift Change adopts that owner")]
        internal IEnumerator AnotherModsOwnerComesThroughALoad()
        {
            return Unstaged(Body);

            void Body(Story story)
            {
                Map map = Find.CurrentMap;
                if (map == null || !TryFindPad(map, out IntVec3 origin))
                {
                    CannotStage(story, "the loaded map has a clear pad to set a stand up on");
                    return;
                }
                // A fresh pad on the current map: the map and pad DevTools handed these tests
                // belong to a game an earlier test replaced.
                CellRect pad = new CellRect(origin.x, origin.z, PadSize, PadSize);

                ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
                if (standDef == null)
                {
                    story.Check(false, "the game has its outfit stand");
                    return;
                }

                // Reuse another mod's owner list when the mod list already supplies one. A second
                // would make two wherever a mod put one on this def, and the test would try theirs
                // against theirs: Outfit Stands Plus' base comp derives from CompAssignableToPawn
                // without overriding PostExposeData, so it saves under the game's keys exactly as
                // the plain one added here does, and the load logs a list read twice. The finders
                // exclude only Shift Change's subclass and cannot tell the added comp from a
                // supplied one, and the four-mod list has no other owner list, so only a larger
                // list shows it.
                CompProperties present = standDef.comps.FirstOrDefault(
                    cp => cp != null && cp.compClass != null
                          && typeof(CompAssignableToPawn).IsAssignableFrom(cp.compClass)
                          && !typeof(CompAssignableToPawn_ShiftStand).IsAssignableFrom(cp.compClass));
                CompProperties_AssignableToPawn foreignProps = null;
                if (present == null)
                {
                    foreignProps = new CompProperties_AssignableToPawn();
                    standDef.comps.Add(foreignProps);
                    story.Note("every outfit stand is given a second owner list, the game's own kind, "
                               + "as another outfit-stand mod gives one");
                }
                else
                {
                    story.Note("outfit stands already carry another mod's owner list");
                }
                try
                {
                    Scene scene = Stage(map, pad, StageKit.Displacing);
                    if (scene == null)
                    {
                        story.Check(false, "a stand with the second owner list, and a colonist, could be set up");
                        return;
                    }
                    string name = scene.Colonist.LabelShort;
                    story.Note("a stand holding a duster, and " + name
                               + " beside it in a shirt, trousers and a parka");

                    List<CompAssignableToPawn> foreigners = scene.OutfitStand.AllComps
                        .OfType<CompAssignableToPawn>()
                        .Where(c => !(c is CompAssignableToPawn_ShiftStand))
                        .ToList();
                    CompAssignableToPawn foreign = foreigners.FirstOrDefault();
                    CompAssignableToPawn_ShiftStand ours =
                        scene.OutfitStand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                    if (foreign == null || ours == null)
                    {
                        story.Check(false, "the stand has Shift Change's owner list and another mod's");
                        return;
                    }

                    // An owner set through the other list's own call, which none of Shift Change's
                    // overrides see, and the reconciled mark off: the shape a stand from before the
                    // copy loads with.
                    foreign.TryAssignPawn(scene.Colonist);
                    ours.ownersUnified = false;
                    string ownerLoadID = scene.Colonist.GetUniqueLoadID();
                    IntVec3 standCell = scene.OutfitStand.Position;
                    story.Note(name + " is made the stand's owner through the other list alone, and the "
                               + "stand is not marked reconciled, as a stand from before Shift Change "
                               + "copied its owners across");

                    // Checked, so a mod list that grows a second other list says so: the test is
                    // Shift Change's list against theirs, and a second turns it into theirs against
                    // theirs while it still goes through the motions.
                    story.Check(foreigners.Count == 1,
                                "the stand carries exactly one other mod's owner list, so what is tried "
                                + "is Shift Change's list against that one");
                    story.Check(ours.AssignedPawnsForReading.Count == 0,
                                "Shift Change's own list starts empty: " + name
                                + " is the other list's owner, not its own");

                    if (!TrySave(story, ForeignSaveName, out string savePath))
                    {
                        return;
                    }
                    string saved = File.ReadAllText(savePath);
                    story.Check(NodeContains(saved, LegacyKey, ownerLoadID),
                                "the save names " + name + " under the game's key, " + LegacyKey
                                + ", the other list's");
                    story.Check(!NodeContains(saved, OurKey, ownerLoadID),
                                "and never under Shift Change's own");

                    Load(ForeignSaveName);
                    story.Note("the game is loaded from " + ForeignSaveName);

                    story.Check(!Logged(DuplicateRegistration),
                                "no list read the shared key twice: the log has no warning of it");
                    story.Check(!Logged(FailedTake),
                                "none asked for references it never read");
                    story.Check(!Logged(UnconsumedWarning),
                                "and the load resolved every reference it read, with none left over");

                    Building_OutfitStand loaded = FindStand(standCell) ?? FindStandAnywhere();
                    if (loaded == null)
                    {
                        MissingStand(story, "the stand is on the loaded map");
                        return;
                    }
                    CompAssignableToPawn loadedForeign = loaded.AllComps
                        .OfType<CompAssignableToPawn>()
                        .FirstOrDefault(c => !(c is CompAssignableToPawn_ShiftStand));
                    CompAssignableToPawn_ShiftStand loadedOurs =
                        loaded.TryGetComp<CompAssignableToPawn_ShiftStand>();
                    if (loadedForeign == null || loadedOurs == null)
                    {
                        story.Check(false, "the loaded stand still has both owner lists");
                        return;
                    }

                    story.Check(loadedForeign.AssignedPawnsForReading.Any(p => p != null
                                    && p.GetUniqueLoadID() == ownerLoadID),
                                "the other list still has " + name + " as its owner");
                    story.Check(loadedOurs.AssignedPawnsForReading.Any(p => p != null
                                    && p.GetUniqueLoadID() == ownerLoadID),
                                "and Shift Change's adopted " + name + ", the only owner either list had");
                    story.Check(loadedOurs.ownersUnified,
                                "and the stand is marked reconciled, so it never adopts again");
                }
                finally
                {
                    // Undo only what this test added. A def edit outliving its test would change
                    // what later tests try, and a mod list that supplied its own other owner list
                    // added nothing here, so this must not remove somebody else's comp.
                    if (foreignProps != null)
                    {
                        standDef.comps.Remove(foreignProps);
                    }
                }
            }
        }

        /// <summary>
        /// Saves two stands serving allowed areas, one whose area was renamed and one whose area
        /// was deleted, loads them back, and checks the first came back bound to its area under the
        /// new name, and the second serving an area that is gone, under the deleted one's name. A
        /// third, uninstalled and carried by a pawn off every map, keeps its binding to the live
        /// area through the save, as a stand carried by a caravan must to serve that area again at
        /// home.
        ///
        /// <para>The file is checked first. A rename changes an allowed area's load ID, which
        /// embeds the name, so the reference has to carry the name the area had when saved. The
        /// deleted area's reference must be dropped rather than left for the loader to fail on,
        /// and a stand with no binding writes no reference at all.</para>
        ///
        /// <para>A test of its own on a fresh pad: the test of an older save loads the first
        /// test's file and takes the first stand it finds, so more stands in that file would make
        /// it test whichever came first.</para>
        /// </summary>
        [Test]
        [TestDescription("a stand's allowed area comes back from a save under its new name, a deleted "
                         + "area's stand comes back serving nothing, and a boxed stand keeps its area")]
        internal IEnumerator AStandsAreaComesBackFromASave()
        {
            return Unstaged(Body);

            void Body(Story story)
            {
                Map map = Find.CurrentMap;
                if (map == null || !TryFindPad(map, out IntVec3 origin))
                {
                    CannotStage(story, "the loaded map has a clear pad to set the stands up on");
                    return;
                }
                CellRect pad = new CellRect(origin.x, origin.z, PadSize, PadSize);
                WorkTypeDef doctor = WorkType("Doctor");
                Scene scene = doctor == null
                    ? null
                    : Stage(map, pad, StageKit.Displacing, enclose: true);
                if (scene == null)
                {
                    story.Check(false, "the game has its Doctor work, and a stand and a colonist "
                                       + "could be set up in a walled room");
                    return;
                }
                story.Note("a stand holding a duster in a walled room, and " + scene.Colonist.LabelShort
                           + " beside it in a shirt, trousers and a parka");

                IntVec3 boundCell = scene.OutfitStand.Position;
                IntVec3 paintedCell = boundCell + new IntVec3(2, 0, 2);
                IntVec3 lostCell = boundCell + new IntVec3(4, 0, 2);
                CompShiftStand lost = CatchmentKit.StageStand(scene, lostCell);
                Area_Allowed area = CatchmentKit.NewArea(scene, "Shift test area", boundCell, paintedCell);
                Area_Allowed doomed = CatchmentKit.NewArea(scene, "Shift test gone", lostCell);
                if (lost == null || area == null || doomed == null)
                {
                    story.Check(false, "a second stand and two allowed areas could be set up");
                    return;
                }
                // A third stand, uninstalled and carried by a pawn off every map, as a caravan
                // carries one. Its area lives on, so the save keeps the binding for when the stand
                // is set down at home again.
                CompShiftStand boxed = CatchmentKit.StageStand(scene, boundCell + new IntVec3(3, 0, 3));
                if (boxed == null)
                {
                    story.Check(false, "a third stand could be set up");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                scene.Comp.SetServeArea(area);
                area.SetLabel("Shift test renamed");
                story.Note("the stand serves " + doctor.labelShort + " in the allowed area \"Shift test area\", "
                           + "painted on it and a cell nearby, which is then renamed \"Shift test renamed\"");
                lost.ToggleWork(doctor);
                lost.SetServeArea(doomed);
                doomed.Delete();
                story.Note("a second stand serves " + doctor.labelShort + " in \"Shift test gone\", "
                           + "painted on it alone, which is then deleted");
                boxed.ToggleWork(doctor);
                boxed.SetServeArea(area);
                MinifiedThing box = boxed.parent.MakeMinified();
                Pawn carrier = DebugTools_Fixtures.AveragePawn(Gender.Male, "Carrier");
                bool carried = box != null && carrier.inventory.innerContainer.TryAdd(box);
                Find.WorldPawns.PassToWorld(carrier, RimWorld.Planet.PawnDiscardDecideMode.KeepForever);
                string carrierID = carrier.ThingID;
                string areaLoadID = area.GetUniqueLoadID();
                string doomedLoadID = doomed.GetUniqueLoadID();
                story.Note("a third serves " + doctor.labelShort + " in the renamed area, and is uninstalled "
                           + "and given to Carrier, a pawn off every map, to carry");

                story.Check(scene.Comp.serves == CatchmentKind.Area && lost.serves == CatchmentKind.Area,
                            "both stands on the map serve an allowed area before the save");
                story.Check(carried && boxed.parent.MapHeld == null && boxed.serveArea == area
                                && boxed.serves == CatchmentKind.Area,
                            "and the boxed one, serving the renamed area, is carried off every map");

                if (!TrySave(story, AreaSaveName, out string savePath))
                {
                    return;
                }
                string saved = File.ReadAllText(savePath);
                story.Check(NodeContains(saved, AreaKey, areaLoadID),
                            "the save refers to the stand's area by its new name");
                story.Check(NodeContains(saved, AreaLabelKey, "Shift test renamed"),
                            "and gives that name as its label");
                story.Check(!NodeContains(saved, AreaKey, doomedLoadID)
                                && NodeContains(saved, AreaLabelKey, "Shift test gone"),
                            "the deleted area's stand keeps the area's name in the save, but no reference to it");
                story.Check(NodeBodies(saved, AreaKey).Count() == 2
                                && NodeBodies(saved, AreaKey).All(body => body.Contains(areaLoadID)),
                            "and only the first stand and the boxed one refer to an area, both to the live one");

                Load(AreaSaveName);
                story.Note("the game is loaded from " + AreaSaveName);
                story.Check(!Logged(UnconsumedWarning),
                            "the load resolved every reference it read: the log has no warning of any left over");

                Building_OutfitStand loadedBound = FindStand(boundCell);
                Building_OutfitStand loadedLost = FindStand(lostCell);
                if (loadedBound == null || loadedLost == null)
                {
                    MissingStand(story, "the two stands are back at " + boundCell + " and " + lostCell
                                        + " after loading");
                    return;
                }
                CompShiftStand bound = loadedBound.TryGetComp<CompShiftStand>();
                CompShiftStand unbound = loadedLost.TryGetComp<CompShiftStand>();
                if (bound == null || unbound == null)
                {
                    story.Check(false, "the loaded stands still have Shift Change's settings");
                    return;
                }
                Area live = bound.LiveServeArea();
                Pawn loadedCarrier = Find.WorldPawns.AllPawnsAliveOrDead.FirstOrDefault(p => p.ThingID == carrierID);
                CompShiftStand loadedBoxed = (loadedCarrier?.inventory?.innerContainer
                        .OfType<MinifiedThing>().FirstOrDefault()?.InnerThing)?.TryGetComp<CompShiftStand>();
                story.Check(loadedBoxed != null && loadedBoxed.serves == CatchmentKind.Area && live != null
                                && loadedBoxed.serveArea == live,
                            "the boxed stand came back still bound to the area, off every map");
                story.Check(bound.serves == CatchmentKind.Area && live != null
                                && live.Label == "Shift test renamed",
                            "the first stand came back serving its area, under the new name");
                story.Check(bound.Covers(paintedCell, loadedBound.Map),
                            "which covers the cell painted in it");
                story.Check(CompShiftStand.CatchmentStands.Contains(bound),
                            "and the load listed it again among the stands that serve beyond their room");
                story.Check(unbound.serves == CatchmentKind.Area && unbound.serveArea == null
                                && unbound.serveAreaLabel == "Shift test gone",
                            "the second came back with its allowed area gone, still under the name \"Shift test gone\"");
                story.Check(!unbound.Covers(lostCell, loadedLost.Map),
                            "and serves nothing, not even its own cell");
            }
        }

        /// <summary>
        /// Saves in the middle of a weapon loan, loads it, and checks that the stand's two
        /// references came back to the same two weapons, its club in the colonist's hand and their
        /// own autopistol in their pack. Then the loaded colonist changes back through the real
        /// driver, since a reference that resolved to the wrong thing would pass a look at the
        /// stand's record and fail only on the way home.
        ///
        /// <para>The stand holds a weapon and no clothes, so the weapon references are all that
        /// keeps it on shift across the load. Its weapon rows go off before the save: a new stand
        /// starts with them on, so finding them off after the load can only come from the save,
        /// which is how a stand built before stands lent weapons keeps its settings.</para>
        /// </summary>
        [Test]
        [TestDescription("a save in the middle of a weapon loan keeps both weapons where they were, "
                         + "and the loan still ends")]
        internal IEnumerator ASaveMidLoanKeepsBothWeapons()
        {
            return Unstaged(Body);

            void Body(Story story)
            {
                Map map = Find.CurrentMap;
                if (map == null || !TryFindPad(map, out IntVec3 origin))
                {
                    CannotStage(story, "the loaded map has a clear pad to set the stand up on");
                    return;
                }
                Scene scene = WeaponKit.ArmedOnly(map, new CellRect(origin.x, origin.z, PadSize, PadSize));
                if (scene == null)
                {
                    story.Check(false, "a stand holding a club and a colonist beside it could be set up");
                    return;
                }
                story.Note(WeaponKit.ArmedOnlySays);
                string name = scene.Colonist.LabelShort;
                if (!WeaponKit.Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                story.Check(scene.Comp.issuedWeapon == issued && scene.Comp.ownWeapon == own,
                            "the stand records the club it lent and the autopistol " + name + " put away");
                scene.OutfitStand.GetStoreSettings().filter.SetAllow(ThingCategoryDefOf.Weapons, allow: false);
                story.Note("the stand's weapon rows are switched off, which a new stand starts with on");

                // Captured before the load: afterwards these objects belong to a disposed game.
                IntVec3 standCell = scene.OutfitStand.Position;
                int colonistID = scene.Colonist.thingIDNumber;
                string issuedLoadID = issued.GetUniqueLoadID();
                string ownLoadID = own.GetUniqueLoadID();

                if (!TrySave(story, LoanSaveName, out string savePath))
                {
                    return;
                }
                string saved = File.ReadAllText(savePath);
                story.Check(NodeContains(saved, IssuedWeaponKey, issuedLoadID),
                            "the save names the club under " + IssuedWeaponKey);
                story.Check(NodeContains(saved, OwnWeaponKey, ownLoadID),
                            "and the autopistol under " + OwnWeaponKey);

                Load(LoanSaveName);
                story.Note("the game is loaded from " + LoanSaveName);
                story.Check(!Logged(UnconsumedWarning),
                            "the load resolved every reference it read: the log has no warning of any left over");

                Building_OutfitStand loaded = FindStand(standCell);
                if (loaded == null)
                {
                    MissingStand(story, "the stand is back at " + standCell + " after loading");
                    return;
                }
                CompShiftStand ledger = loaded.TryGetComp<CompShiftStand>();
                Pawn pawn = ledger?.Borrower;
                if (!story.Check(pawn != null && pawn.thingIDNumber == colonistID,
                                 "the loaded stand still lends to " + name))
                {
                    return;
                }
                ThingWithComps loadedIssued = ledger.issuedWeapon;
                ThingWithComps loadedOwn = ledger.ownWeapon;
                ThingFilter filter = loaded.GetStoreSettings().filter;
                story.Check(ledger.OnShift, "the stand's shift is still on");
                story.Check(!WeaponKit.WeaponRows().Any(row => filter.Allows(row)),
                            "its weapon rows are still off, as it saved them");
                story.Check(loadedIssued != null && loadedIssued.GetUniqueLoadID() == issuedLoadID
                                && pawn.equipment.Primary == loadedIssued,
                            "the club the stand records is the one in " + name + "'s hand");
                story.Check(loadedOwn != null && loadedOwn.GetUniqueLoadID() == ownLoadID
                                && pawn.inventory.innerContainer.Contains(loadedOwn),
                            "and the autopistol it records is the one in their pack");
                story.Check(CompShiftStand.OnShiftStandFor(pawn) == ledger,
                            "the load put the loan back in " + name + "'s name");
                if (loadedIssued == null || loadedOwn == null)
                {
                    return;
                }

                Scene home = new Scene { Map = loaded.Map, OutfitStand = loaded, Colonist = pawn, Comp = ledger };
                story.Check(SwapNow(story, home), name + " changes back after the load");
                story.Check(loaded.HeldWeapon == loadedIssued, "the club went back to the stand");
                story.Check(pawn.equipment.Primary == loadedOwn, "the autopistol came out of their pack");
                story.Check(!ledger.OnShift, "and the stand's record is cleared");
            }
        }

        /// <summary>
        /// The test of an older save with no other mod's owner list beside Shift Change's: the
        /// key migration. The owner must arrive, and saving again must write it under Shift
        /// Change's own key.
        /// </summary>
        internal static void KeyMigration(Story story, CompAssignableToPawn_ShiftStand assign,
                                          string ownerLoadID)
        {
            story.Note("the stand has no other mod's owner list, so Shift Change reads the game's key itself");
            story.Check(assign.AssignedPawnsForReading.Any(p => p != null
                            && p.GetUniqueLoadID() == ownerLoadID),
                        "Shift Change's owner list has the owner the older save named");

            // Only a migration that collected the reference writes the owner back out under Shift
            // Change's key; an owner held in memory that is never saved passes the check above.
            if (!TrySave(story, ReSaveName, out string resavePath))
            {
                return;
            }
            string resaved = File.ReadAllText(resavePath);

            story.Check(NodeContains(resaved, OurKey, ownerLoadID),
                        "saving again names the owner under Shift Change's own key");
            story.Check(!NodeContains(resaved, LegacyKey, ownerLoadID),
                        "and no longer under the game's");
        }

        /// <summary>
        /// The test of an older save beside another mod's owner list: the route
        /// <see cref="CompAssignableToPawn_ShiftStand.PostExposeData"/> promises a legacy save
        /// that carries both mods.
        ///
        /// <para>The decline leaves no trace. The stand's entry carries the game's key twice, the
        /// other list's and Shift Change's rewritten one, and the loader answers every read of it
        /// with the first (<c>ScribeLoader.EnterNode</c> looks the child up by name). Had Shift
        /// Change's list read it too, the second registration would be refused and its take would
        /// find nothing: the two log lines checked absent here.</para>
        ///
        /// <para>The owner comes back by the other road. Shift Change's list loads empty, the other
        /// list holds the owner (the first test's copy put it there), and the reconcile adopts it.
        /// The copy writes it back, so saving again writes it under Shift Change's key and under
        /// the game's once per other list, the count the first test makes.</para>
        /// </summary>
        internal static void DeclinedAndReconciled(Story story, CompAssignableToPawn_ShiftStand assign,
                                                   List<CompAssignableToPawn> foreigners,
                                                   string ownerLoadID)
        {
            CompAssignableToPawn foreign = foreigners[0];
            story.Note("the stand also has another mod's owner list, and the game's key is that list's, "
                       + "so Shift Change leaves the key to it");
            story.Check(foreigners.Count == 1,
                        "the stand carries exactly one other mod's owner list, so what is tried "
                        + "is Shift Change's list against that one");
            story.Check(!Logged(DuplicateRegistration),
                        "Shift Change left the game's key to the other list: the log has no warning "
                        + "that it was read twice");
            story.Check(!Logged(FailedTake),
                        "nor that a list asked for references it never read");
            story.Check(foreign.AssignedPawnsForReading.Any(p => p != null
                            && p.GetUniqueLoadID() == ownerLoadID),
                        "the other list still has its owner");
            story.Check(assign.AssignedPawnsForReading.Any(p => p != null
                            && p.GetUniqueLoadID() == ownerLoadID),
                        "and Shift Change's adopted that owner through the reconcile, the only way "
                        + "open beside another mod's list");
            story.Check(assign.ownersUnified,
                        "and the stand is marked reconciled, so it never adopts again");

            if (!TrySave(story, ReSaveName, out string resavePath))
            {
                return;
            }
            string resaved = File.ReadAllText(resavePath);
            int genericHits = NodeBodies(resaved, LegacyKey).Count(body => body.Contains(ownerLoadID));

            story.Check(NodeContains(resaved, OurKey, ownerLoadID),
                        "saving again names the adopted owner under Shift Change's own key");
            story.Check(genericHits == foreigners.Count,
                        "and under the game's only as the other list's copy (" + genericHits + " found, "
                        + foreigners.Count + " expected)");
        }

        /// <summary>
        /// Saves the game under <paramref name="name"/>, with a note, or fails the test.
        /// <c>GameDataSaveLoader.SaveGame</c> returns nothing and catches and logs its own
        /// exceptions, so success is the file being there, which is why an earlier save under
        /// the same name must be gone first: left in place, it would pass for this game's.
        /// </summary>
        internal static bool TrySave(Story story, string name, out string path)
        {
            SavesInUse.Add(name);
            path = GenFilePaths.FilePathForSavedGame(name);
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // Whether the file is still there decides, below.
            }
            if (File.Exists(path))
            {
                story.Check(false, "the game saves as " + name + ": an earlier save at " + path
                                   + " could not be removed first");
                return false;
            }
            GameDataSaveLoader.SaveGame(name);
            if (File.Exists(path))
            {
                story.Note("the game is saved as " + name);
                return true;
            }
            story.Check(false, "the game saves as " + name + ": nothing was written to " + path);
            return false;
        }

        /// <summary>
        /// The stand is gone after a load: decides whether that is Shift Change's failure or
        /// the mod list's. A known gap when that load logged
        /// <see cref="SaveRoundTrip.FinalizeAbort"/>
        /// (<see cref="SaveRoundTrip.LoadAbortCulprit"/>), since it never reached the
        /// pass that spawns things and the test tried nothing; calling that a failure would blame
        /// Shift Change's saving for another mod's static cache.
        ///
        /// <para>It excuses nothing else. A check that failed earlier in the test still fails it,
        /// and with no abort in that load the stand is missing for a reason nobody understands,
        /// which stays a failure.</para>
        /// </summary>
        internal static void MissingStand(Story story, string where)
        {
            string culprit = LoadAbortCulprit();
            if (culprit == null)
            {
                story.Check(false, where + ", so the rest can be checked");
                return;
            }
            story.KnownGap(false, where,
                           "the game's map loading (Map.FinalizeLoading) aborted in " + culprit
                           + ", so the map spawned nothing. Not Shift Change's doing: another mod's "
                           + "static cache, not cleared between loads in one session");
        }

        /// <summary>
        /// There is nowhere to set up: the same fork as <see cref="MissingStand"/>, one step
        /// earlier. When a prefix on <c>Map.FinalizeLoading</c> threw in the load before, its map
        /// has no rooms and <c>TryFindPad</c> refuses it, so the tests after the first find no pad.
        /// An abort later in that method leaves rooms that can still be built, and the test sets
        /// up as usual.
        ///
        /// <para>That is a known gap only when the load that made the current game logged
        /// <see cref="SaveRoundTrip.FinalizeAbort"/>
        /// (<see cref="SaveRoundTrip.LoadAbortCulprit"/>). A map that was never usable
        /// is a broken run and stays a failure, as a test does when DevTools' own map has no pad
        /// (<see cref="StoryFixture.ReadyPad"/>). Nothing past setting up is excused: no stand
        /// def, no stand, an owner list missing off the stand all still fail.</para>
        /// </summary>
        internal static void CannotStage(Story story, string where)
        {
            string culprit = LoadAbortCulprit();
            if (culprit == null)
            {
                story.Check(false, where);
                return;
            }
            story.KnownGap(false, where,
                           "the game's map loading (Map.FinalizeLoading) aborted in " + culprit
                           + ", so the map an earlier test left behind has no regions to set "
                           + "anything up on. Not Shift Change's doing: another mod's static cache, "
                           + "not cleared between loads in one session");
        }
    }
}
