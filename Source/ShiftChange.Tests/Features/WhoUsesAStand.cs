using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DevTools.Testing;
using HarmonyLib;
using RimWorld;
using Verse;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Who may use a stand: its owners, the groups the player picks, and stands from other mods.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.WhoUsesAStand)]
    internal class WhoUsesAStand : FeatureFixture
    {
        /// <summary>What <see cref="SceneKit.Build"/> sets up.</summary>
        internal const string OnShift =
            "a colonist on shift at a stand: he wears what it held, and it keeps the clothes of his that made way";

        /// <summary>What <see cref="SceneKit.Stage"/> sets up, with the parka.</summary>
        internal const string BesideAStand =
            "a stand holding a duster, and a colonist beside it in a shirt, trousers and a parka";

        /// <summary>What <see cref="SceneKit.StageWithForeignAssignable"/> sets up.</summary>
        internal const string BesideAStandWithTwoLists =
            "a stand holding a duster, with a second owner list beside Shift Change's, as another "
            + "outfit-stand mod gives one, and a colonist beside it in a shirt, trousers and a parka";

        /// <summary>
        /// What protects a stand's contents: while it is in service, the game's Allow removing
        /// items setting is off.
        ///
        /// <para>Driven through every way into service, not only a load, since the way to reach
        /// the setting is to take the stand out of service, turn it on and put the stand back,
        /// and that last step must not leave it on. A stand not used for shift changes is the
        /// counterpoint: it is ordinary furniture, and the player owns its setting.</para>
        ///
        /// <para>The last two checks pin the engine fact behind
        /// <see cref="CompShiftStand.EnforceRemovalFlag"/>: <c>Building_OutfitStand</c> returns
        /// this one field for both <c>IApparelSource.ApparelSourceEnabled</c> and
        /// <c>IHaulSource.HaulSourceEnabled</c>, so the outfit optimizer's gate is the setting and
        /// no Harmony patch on it is needed. If a game update decouples them these fail, and the
        /// patch is back on the table.</para>
        /// </summary>
        [Test]
        [TestDescription("a stand in service keeps Allow removing items off, whichever way it went into service")]
        internal IEnumerator AllowRemovingStaysOffInService()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Scenario scenario)
            {
                AccessTools.FieldRef<Building_OutfitStand, bool> flag =
                    Patch_AllowRemovingToggle.AllowRemovingItemsRef;
                if (flag == null)
                {
                    // A failure here is not a stale test: the protection rides on this field, so a
                    // rename silently removes it.
                    scenario.Check(false, "the stand's Allow removing items setting can still be found");
                    return;
                }
                WorkTypeDef doctor = WorkType("Doctor");
                if (doctor == null)
                {
                    scenario.Check(false, "the Doctor work type exists");
                    return;
                }

                scene.Comp.SetExcluded();
                flag(scene.OutfitStand) = true;
                scenario.Note("the stand is set to Not used for shift changes, and Allow removing items turned on");
                scenario.Check(flag(scene.OutfitStand),
                            "a stand not used for shift changes keeps it as the player set it");

                scene.Comp.SetAutomatic();
                scenario.Check(!flag(scene.OutfitStand), "setting the stand to Automatic (from the room) turns it off");

                scene.Comp.SetExcluded();
                flag(scene.OutfitStand) = true;
                scene.Comp.ToggleWork(doctor);
                scenario.Note("the stand is taken out of service and the setting turned on again, then Doctor ticked");
                scenario.Check(!flag(scene.OutfitStand), "ticking Doctor turns it off");

                scene.Comp.SetExcluded();
                flag(scene.OutfitStand) = true;
                scene.Comp.ToggleRecreation();
                scenario.Note("and once more, with Recreation ticked this time");
                scenario.Check(!flag(scene.OutfitStand), "ticking Recreation turns it off");
                scenario.Check(!scene.Comp.IsExcluded, "and the stand really is back in service");

                scenario.Check(!((IApparelSource)scene.OutfitStand).ApparelSourceEnabled,
                            "so colonists choosing their outfits cannot take from the stand: "
                            + "the game's gate for that is the same setting");
                scenario.Check(!((IHaulSource)scene.OutfitStand).HaulSourceEnabled,
                            "and haulers cannot take from it while it is in service");
            }
        }

        // Both button patches, Change back's and Allow removing items', hand the game's own
        // sequence of buttons back untouched when they have nothing to add, instead of wrapping it
        // in an iterator (why: Patch_ChangeBackGizmo.Postfix). The button bar is rebuilt every
        // rendered frame for every selected object, so a wrapper would sit around every button
        // handed to it to answer a question that is usually no.
        //
        // The bar a player sees is the same either way, so a test that only checks which buttons
        // come out passes on a wrapper too. Only the identity of the returned sequence tells the
        // two apart: a patch with nothing to do returns the very object it was given. That is the
        // check doing the work below; the rest guard the behaviour around it.

        /// <summary>A colonist on no stand gets the game's own sequence of buttons back, itself.</summary>
        [Test]
        [TestDescription("a colonist on no stand gets their buttons through exactly as the game made them")]
        internal IEnumerator NoChangeBackOffShift()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Scenario scenario)
            {
                Pawn bystander = AnotherColonist(scenario, scene, Gender.Female, "Bystander");
                List<Gizmo> chain = Sentinels();
                IEnumerable<Gizmo> passed = Patch_ChangeBackGizmo.Postfix(chain, bystander);

                scenario.Check(CompShiftStand.OnShiftStandFor(bystander) == null,
                            bystander.LabelShort + " is on shift at no stand");
                scenario.Check(ReferenceEquals(passed, chain),
                            "so Shift Change hands back the very sequence of buttons it was given, "
                            + "not a wrapper around it");
                scenario.Check(SameSequence(passed, chain), "with every one of the game's buttons still in it, in order");

                // Harmony never hands the patch a null colonist, but it guards against one, and
                // dropping that guard should fail here rather than throw in somebody's colony.
                scenario.Check(ReferenceEquals(Patch_ChangeBackGizmo.Postfix(chain, null), chain),
                            "and with no colonist at all, the buttons are handed back untouched too");
            }
        }

        /// <summary>For a colonist in uniform, Shift Change adds the Change back button, last.</summary>
        [Test]
        [TestDescription("a colonist on shift gets a Change back button after their others, "
                         + "even with Shift Change's automatic changes off")]
        internal IEnumerator ChangeBackOfferedOnShift()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                List<Gizmo> chain = Sentinels();
                List<Gizmo> got = Patch_ChangeBackGizmo.Postfix(chain, scene.Colonist).ToList();

                scenario.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == scene.Comp,
                            name + " is on shift at the stand, in what it held");
                scenario.Check(got.Count == chain.Count + 1,
                            "so " + name + "'s buttons come back with exactly one added");
                scenario.Check(SameSequence(got.Take(chain.Count), chain),
                            "the game's own buttons are untouched, and still in order");
                scenario.Check(got.Count > chain.Count
                            && got[got.Count - 1] is Command_Action button
                            && button.groupKey == Patch_ChangeBackGizmo.GroupKey,
                            "and the one added is Change back, last");

                // Interception going off, by the standing switch or the fault latch, stops the mod
                // acting on its own and must not also remove the player's manual way out, or every
                // colonist already in uniform is stranded. Each way is checked alone, so a gate on
                // either one fails here.
                bool armedBefore = Patch_JobInterception.Enabled;
                bool faultedBefore = Patch_JobInterception.faulted;
                try
                {
                    Patch_JobInterception.Enabled = false;
                    scenario.Note("Shift Change's automatic changes are switched off, as its tweak value does");
                    scenario.Check(Patch_ChangeBackGizmo.Postfix(chain, scene.Colonist).Count() == chain.Count + 1,
                                "Change back is still offered");
                    Patch_JobInterception.Enabled = armedBefore;
                    Patch_JobInterception.faulted = true;
                    scenario.Note("switched back on, they are shut off instead, as repeated errors do");
                    scenario.Check(Patch_ChangeBackGizmo.Postfix(chain, scene.Colonist).Count() == chain.Count + 1,
                                "and Change back is still offered then too");
                }
                finally
                {
                    Patch_JobInterception.Enabled = armedBefore;
                    Patch_JobInterception.faulted = faultedBefore;
                }

                // A stand off the map with its ledger kept, after a gravship takeoff's WillReplace
                // despawn or a minify's Vanish, still answers the registry for this colonist; the
                // takeoff's mode stands in for both. The gate's Spawned test is all that stops the
                // button pointing at a stand that is not on the map.
                IntVec3 cell = scene.OutfitStand.Position;
                scene.OutfitStand.DeSpawn(DestroyMode.WillReplace);
                scenario.Note("the stand is lifted off the map, as a gravship takeoff does, still keeping "
                           + name + "'s clothes");
                scenario.Check(ReferenceEquals(Patch_ChangeBackGizmo.Postfix(chain, scene.Colonist), chain),
                            "while the stand is off the map, " + name + " gets no Change back at all");
                GenSpawn.Spawn(scene.OutfitStand, cell, scene.Map, Rot4.North);
                scene.OutfitStand.PostSwapMap();
            }
        }

        /// <summary>
        /// Shift Change rewrites only the game's own Allow removing items button, and only while
        /// the stand is in service.
        /// </summary>
        [Test]
        [TestDescription("Shift Change rewrites a stand's Allow removing items button only while it is in "
                         + "service, and leaves other buttons alone")]
        internal IEnumerator AllowRemovingButtonOnlyInService()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Scenario scenario)
            {
                string vanillaLabel = "CommandAllowRemovingApparel".Translate().ToString();
                string ours = "ShiftChange.AllowRemovingDesc".Translate().RawText;
                const string untouched = "vanilla's own description, verbatim";

                scene.Comp.SetExcluded();
                Command_Toggle foreign = RemovalToggle(vanillaLabel, active: false, desc: untouched);
                List<Gizmo> chain = new List<Gizmo> { Sentinels()[0], foreign };
                IEnumerable<Gizmo> passed = Patch_AllowRemovingToggle.Postfix(chain, scene.OutfitStand);

                scenario.Check(scene.Comp.IsExcluded, "the stand is set to Not used for shift changes");
                scenario.Check(ReferenceEquals(passed, chain),
                            "so Shift Change hands back the very sequence of buttons it was given, "
                            + "not a wrapper around it");
                scenario.Check(foreign.defaultDesc == untouched,
                            "and the Allow removing items tooltip is left exactly as the game wrote it");
                scenario.Check(!foreign.Disabled, "with the button still live");

                scene.Comp.SetAutomatic();
                Command_Toggle governed = RemovalToggle(vanillaLabel, active: false, desc: untouched);
                Command_Toggle alreadyOn = RemovalToggle(vanillaLabel, active: true, desc: untouched);
                // A toggle that is not the game's. The patch finds the game's by its label, the
                // command being an anonymous Command_Toggle with no other handle, so a renamed
                // label key makes it match nothing and the stand fall back to the game's own. This
                // button is that fallback.
                Command_Toggle stranger = RemovalToggle("Another mod's toggle", active: false,
                                                        desc: untouched);
                List<Gizmo> governedChain = new List<Gizmo> { governed, alreadyOn, stranger };
                List<Gizmo> got = Patch_AllowRemovingToggle.Postfix(governedChain, scene.OutfitStand).ToList();

                scenario.Check(!scene.Comp.IsExcluded, "set to Automatic (from the room), the stand is back in service");
                scenario.Check(SameSequence(got, governedChain), "every button still comes through, in order");
                scenario.Check(governed.defaultDesc != untouched && governed.defaultDesc.Contains(ours),
                            "the Allow removing items tooltip now carries Shift Change's paragraph "
                            + "as well as the game's");
                // TaggedString's implicit conversion to string strips tags, so the patch must use
                // .RawText on both halves of the tooltip: assigning a Translate() result straight
                // to the field silently deletes the colour and bold. This check guards that.
                scenario.Check(ours.Contains("<color") && governed.defaultDesc.Contains("<color"),
                            "with its colour and bold intact, not stripped on the way");
                scenario.Check(governed.Disabled,
                            "a button that is off is greyed out while the stand is in service");
                scenario.Check(!alreadyOn.Disabled, "while one already on keeps its way back out");
                scenario.Check(stranger.defaultDesc == untouched && !stranger.Disabled,
                            "and another mod's toggle passes through untouched");
            }
        }

        /// <summary>
        /// Ownership is a set. The danger in going from one owner to many is a reader that still
        /// asks whether the owner is this colonist and answers no for everyone but the first, so
        /// the checks about a second owner are the ones that matter.
        /// </summary>
        [Test]
        [TestDescription("a stand with owners is theirs alone, the second owner's as much as the first's, "
                         + "and shared again once the last is removed")]
        internal IEnumerator OnlyItsOwnersMayUseAStand()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing), BesideAStand, Body);

            void Body(Scene scene, Scenario scenario)
            {
                CompAssignableToPawn assignable =
                    scene.OutfitStand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                if (assignable == null)
                {
                    scenario.Check(false, "the stand has Shift Change's owner list");
                    return;
                }

                Pawn a = scene.Colonist;
                Pawn b = AnotherColonist(scenario, scene, Gender.Female, "OwnerB");
                Pawn c = AnotherColonist(scenario, scene, Gender.Male, "Outsider");
                string first = a.LabelShort, second = b.LabelShort, outsider = c.LabelShort;

                scenario.Check(scene.Comp.IsPool, "with no owners, the stand is shared");
                scenario.Check(scene.Comp.CanBeClaimedBy(a) && scene.Comp.CanBeClaimedBy(c),
                            "and anyone able may use it, " + first + " and " + outsider + " alike");

                assignable.TryAssignPawn(a);
                scenario.Note(first + " is made its owner");
                scenario.Check(!scene.Comp.IsPool, "with an owner, it is no longer shared");
                scenario.Check(scene.Comp.IsAssignedTo(a), first + " is listed as its owner");
                scenario.Check(scene.Comp.CanBeClaimedBy(a), first + " may use it");
                scenario.Check(!scene.Comp.CanBeClaimedBy(c), "and nobody else may: not " + outsider);

                assignable.TryAssignPawn(b);
                scenario.Note(second + " is made an owner too");
                scenario.Check(scene.Comp.AssignedOwners.Count == 2, "the stand lists both owners");
                scenario.Check(scene.Comp.CanBeClaimedBy(a), "the first owner, " + first + ", may still use it");
                // The one a single-owner reader gets wrong.
                scenario.Check(scene.Comp.CanBeClaimedBy(b), "and so may the second, " + second);
                scenario.Check(!scene.Comp.CanBeClaimedBy(c), "while " + outsider + " still may not");

                assignable.TryUnassignPawn(a);
                scenario.Check(!scene.Comp.CanBeClaimedBy(a),
                            "removing " + first + " as an owner means " + first + " may no longer use it");
                scenario.Check(scene.Comp.CanBeClaimedBy(b), "and leaves " + second + " its owner");

                assignable.TryUnassignPawn(b);
                scenario.Check(scene.Comp.IsPool, "removing the last owner makes the stand shared again");
                scenario.Check(scene.Comp.CanBeClaimedBy(c), "and " + outsider + " may use it again");
            }
        }

        /// <summary>
        /// The owner window's list of colonists to add: the Men and Women filters narrow it, and
        /// never hide a colonist who is already an owner, since a filter that hid the owner you
        /// wanted to remove would be a trap.
        /// </summary>
        [Test]
        [TestDescription("a stand's owner window offers the men under Men and the women under Women, "
                         + "and no filter hides an owner")]
        internal IEnumerator OwnerWindowFiltersByGender()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing), BesideAStand, Body);

            void Body(Scene scene, Scenario scenario)
            {
                CompAssignableToPawn assignable =
                    scene.OutfitStand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                if (assignable == null)
                {
                    scenario.Check(false, "the stand has Shift Change's owner list");
                    return;
                }

                Pawn man = AnotherColonist(scenario, scene, Gender.Male, "FilterM");
                Pawn woman = AnotherColonist(scenario, scene, Gender.Female, "FilterF");
                string he = man.LabelShort, she = woman.LabelShort;

                Dialog_AssignStandOwners dialog = new Dialog_AssignStandOwners(assignable);
                scenario.Note("the stand's owner window is opened");

                dialog.filter = Dialog_AssignStandOwners.Filter.All;
                List<Pawn> all = dialog.Candidates();
                scenario.Check(all.Contains(man) && all.Contains(woman),
                            "under All, both " + he + " and " + she + " are offered");

                dialog.filter = Dialog_AssignStandOwners.Filter.Male;
                List<Pawn> males = dialog.Candidates();
                scenario.Check(males.Contains(man), "under Men, " + he + " is offered");
                scenario.Check(!males.Contains(woman), "and " + she + " is not");
                scenario.Check(males.All(p => p.gender == Gender.Male),
                            "and no woman anywhere in the colony slipped through");

                dialog.filter = Dialog_AssignStandOwners.Filter.Female;
                List<Pawn> females = dialog.Candidates();
                scenario.Check(females.Contains(woman), "under Women, " + she + " is offered");
                scenario.Check(!females.Contains(man), "and " + he + " is not");

                // An owner leaves the list of colonists to add; the window draws them among the
                // owners above it, whatever the filter says.
                assignable.TryAssignPawn(woman);
                dialog.filter = Dialog_AssignStandOwners.Filter.All;
                scenario.Note(she + " is made an owner, and the filter set back to All");
                scenario.Check(!dialog.Candidates().Contains(woman), she + " is no longer offered to add");
                scenario.Check(assignable.AssignedPawnsForReading.Contains(woman),
                            "because she is among the stand's owners now");

                dialog.filter = Dialog_AssignStandOwners.Filter.Male;
                scenario.Check(assignable.AssignedPawnsForReading.Contains(woman),
                            "and under Men, which leaves her out, she is still an owner");

                assignable.TryUnassignPawn(woman);
            }
        }

        /// <summary>
        /// One owner list per stand, with another mod's owner list holding a copy of it. Outfit
        /// Stands Plus draws its "equip outfit" button for every stand whose list names the
        /// colonist, so a list nobody can see or maintain would send a colonist to somebody else's
        /// stand. Checked through every way Shift Change's list changes (assign, a second owner
        /// that no longer fits the copy's one place, unassign, the release when a colonist is lost)
        /// and in both modes, since a stand set to "Not used for shift changes" must also hide the
        /// other mod's Set owner and show ours.
        /// </summary>
        [Test]
        [TestDescription("another outfit-stand mod's owner list names a stand's one owner, stays empty "
                         + "for a group, and its Set owner button gives way to Shift Change's")]
        internal IEnumerator AnotherModsOwnerListFollowsOurs()
        {
            return Staged(StageWithForeignAssignable, BesideAStandWithTwoLists, Body);

            void Body(Scene scene, Scenario scenario)
            {
                CompAssignableToPawn_ShiftStand ours =
                    scene.OutfitStand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                CompAssignableToPawn foreign = ForeignAssignableOn(scene.OutfitStand);
                if (ours == null || foreign == null)
                {
                    scenario.Check(false, "the stand has Shift Change's owner list and another mod's");
                    return;
                }
                if (foreign.TotalSlots != 1)
                {
                    // The second-owner checks assume one place, as Outfit Stands Plus has; a bigger
                    // list is a different test.
                    scenario.Check(false, "the other mod's list has room for one owner, as Outfit Stands Plus's does");
                    return;
                }

                Pawn a = scene.Colonist;
                Pawn b = AnotherColonist(scenario, scene, Gender.Female, "CopyB");
                string first = a.LabelShort, second = b.LabelShort;

                scenario.Check(foreign.AssignedPawnsForReading.Count == 0,
                            "with no owners, the other mod's list is empty");

                ours.TryAssignPawn(a);
                scenario.Check(OwnersAre(foreign, a),
                            "with " + first + " as its one owner, the other mod's list names "
                            + first + " and nobody else");

                ours.TryAssignPawn(b);
                scenario.Check(ours.AssignedPawnsForReading.Count == 2,
                            "with " + second + " added, Shift Change lists both owners");
                scenario.Check(foreign.AssignedPawnsForReading.Count == 0,
                            "and two do not fit the other list's one place, so it is emptied, not cut short");

                ours.TryUnassignPawn(a);
                scenario.Check(OwnersAre(foreign, b), "back to one owner, the other list names " + second);

                // The release unassigns through Shift Change's list, so the other list follows and
                // a colonist who died or left loses their button on the stand.
                Patch_UnclaimStands.ReapStandsFor(b);
                scenario.Note("the game releases what " + second + " owned, as when a colonist dies");
                scenario.Check(ours.AssignedPawnsForReading.Count == 0, "Shift Change's list lets " + second + " go");
                scenario.Check(foreign.AssignedPawnsForReading.Count == 0, "and the other list with it");

                IEnumerable<Gizmo> hidden = null;
                scene.Comp.SetAutomatic();
                scenario.Check(!Patch_ForeignOwnerGizmos.Prefix(foreign, ref hidden),
                            "a stand in service hides the other mod's Set owner button");

                scene.Comp.SetExcluded();
                List<Command> ourGizmos = ours.CompGetGizmosExtra().OfType<Command>().ToList();
                scenario.Check(!Patch_ForeignOwnerGizmos.Prefix(foreign, ref hidden),
                            "and so does a stand set to Not used for shift changes");
                scenario.Check(ourGizmos.Count == 1, "which shows Shift Change's owner button in its place");
                scenario.Check(ourGizmos.Count == 1
                            && ourGizmos[0].defaultLabel == "CommandThingSetOwnerLabel".Translate(),
                            "labelled Set owner, since a stand not used for shift changes is shared by nobody");

                ours.TryAssignPawn(a);
                scenario.Check(OwnersAre(foreign, a), "and there too the other list names " + first + " once ours does");

                ours.TryUnassignPawn(a);
                scene.Comp.SetAutomatic();
            }
        }

        /// <summary>
        /// The one-time reconcile for a stand from an older save, where the two owner lists were
        /// independent. Each step sets up the disagreement as such a save carries it (Shift
        /// Change's list set, then the other mod's rewritten behind its back) and runs the
        /// <see cref="CompAssignableToPawn_ShiftStand.UnifyOwners"/> a spawn does.
        ///
        /// <para>The rule: if only one list has owners it becomes the list; if both do and they
        /// disagree, the one the player could see wins, Shift Change's on a stand in service and
        /// the other mod's on a stand set to "Not used for shift changes". The last step is what
        /// makes it one-time: once reconciled, another list that differs is overwritten, never
        /// adopted.</para>
        /// </summary>
        [Test]
        [TestDescription("a stand from an older save whose two owner lists disagree keeps the one "
                         + "the player could see")]
        internal IEnumerator OlderSavesKeepTheOwnersYouSaw()
        {
            return Staged(StageWithForeignAssignable, BesideAStandWithTwoLists, Body);

            void Body(Scene scene, Scenario scenario)
            {
                CompAssignableToPawn_ShiftStand ours =
                    scene.OutfitStand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                CompAssignableToPawn foreign = ForeignAssignableOn(scene.OutfitStand);
                if (ours == null || foreign == null)
                {
                    scenario.Check(false, "the stand has Shift Change's owner list and another mod's");
                    return;
                }
                if (foreign.TotalSlots != 1)
                {
                    scenario.Check(false, "the other mod's list has room for one owner, as Outfit Stands Plus's does");
                    return;
                }

                Pawn a = scene.Colonist;
                Pawn b = AnotherColonist(scenario, scene, Gender.Female, "ReconcileB");
                string first = a.LabelShort, second = b.LabelShort;

                // In service both lists name someone and disagree; Shift Change's is the one the
                // player could see.
                scene.Comp.SetAutomatic();
                ours.TryAssignPawn(a);
                Diverge(foreign, a, b);
                ours.ownersUnified = false;
                scenario.Note("as an older save left it: the stand in service and owned by " + first
                           + ", the other mod's list naming " + second + " instead; the stand is loaded");
                ours.UnifyOwners();
                scenario.Check(OwnersAre(ours, a) && OwnersAre(foreign, a),
                            "a stand in service keeps its own owner, " + first
                            + ", and the other list's stale one is replaced");
                scenario.Check(ours.ownersUnified, "and the stand is marked as reconciled");

                // Not used for shift changes: the other mod's list is the one the player could see.
                scene.Comp.SetExcluded();
                Diverge(foreign, a, b);
                ours.ownersUnified = false;
                ours.UnifyOwners();
                scenario.Check(OwnersAre(ours, b) && OwnersAre(foreign, b),
                            "a stand set to Not used for shift changes takes the other mod's owner, "
                            + second + ", instead");

                // Only the other list names anyone. Adopted, so an earlier assignment is not lost.
                scene.Comp.SetAutomatic();
                ours.TryUnassignPawn(b);
                foreign.ForceAddPawn(a);
                ours.ownersUnified = false;
                ours.UnifyOwners();
                scenario.Check(OwnersAre(ours, a),
                            "a stand with no owners of its own adopts the only owner there is, " + first);

                // Only Shift Change's list names anyone, and two of them: nothing to adopt, and
                // nothing that fits the copy.
                ours.TryAssignPawn(b);
                ours.ownersUnified = false;
                ours.UnifyOwners();
                scenario.Check(ours.AssignedPawnsForReading.Count == 2
                            && foreign.AssignedPawnsForReading.Count == 0,
                            "a stand with two owners keeps both, and the other list, with room for one, "
                            + "stays empty");

                // Once reconciled, the other list has no say.
                ours.TryUnassignPawn(b);
                Diverge(foreign, a, b);
                ours.UnifyOwners();
                scenario.Check(OwnersAre(ours, a) && OwnersAre(foreign, a),
                            "once reconciled, a stand whose other list drifted rewrites it from its own, "
                            + "never adopting it");

                ours.TryUnassignPawn(a);
            }
        }

        /// <summary>
        /// A stand with a group of owners, taken out of service, keeps them, and has them all when
        /// it goes back. A past build that cleared the list on the way out cost a configured group
        /// on an ordinary edit of the stand.
        ///
        /// <para>Driven through all four ways out of service, since three are not the "Not used
        /// for shift changes" row: unticking the last work type, Recreation or Sleeping lands in
        /// the same state (<see cref="CompShiftStand.ToggleWork"/> and its two siblings fall
        /// through to <see cref="CompShiftStand.SetExcluded"/>). Each way first puts the stand back
        /// in service through the matching tick, which shows it really left.</para>
        /// </summary>
        [Test]
        [TestDescription("a stand's group of owners stays whole when it is taken out of service, "
                         + "whichever box is unticked")]
        internal IEnumerator AGroupKeepsItsStandOutOfService()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing), BesideAStand, Body);

            void Body(Scene scene, Scenario scenario)
            {
                CompAssignableToPawn_ShiftStand ours =
                    scene.OutfitStand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                WorkTypeDef doctor = WorkType("Doctor");
                if (ours == null || doctor == null)
                {
                    scenario.Check(false, "the stand has Shift Change's owner list, and the Doctor work type exists");
                    return;
                }

                Pawn a = scene.Colonist;
                Pawn b = AnotherColonist(scenario, scene, Gender.Female, "GroupB");

                scene.Comp.SetAutomatic();
                ours.TryAssignPawn(a);
                ours.TryAssignPawn(b);
                scenario.Check(OwnersAre(ours, a, b),
                            "a stand in service, owned by the group of " + a.LabelShort + " and " + b.LabelShort);

                scene.Comp.SetExcluded();
                scenario.Check(scene.Comp.IsExcluded && OwnersAre(ours, a, b),
                            "set to Not used for shift changes, it keeps both owners");

                // A stand out of service has no work types to start from, so each tick below
                // starts a fresh set.
                scene.Comp.ToggleWork(doctor);
                scenario.Check(!scene.Comp.IsExcluded && scene.Comp.WorkTypes.Count == 1,
                            "ticking Doctor puts it back in service, for that one work type");
                scene.Comp.ToggleWork(doctor);
                scenario.Check(scene.Comp.IsExcluded && OwnersAre(ours, a, b),
                            "unticking that last work type takes it out again, and it keeps both owners");

                scene.Comp.ToggleRecreation();
                scenario.Check(!scene.Comp.IsExcluded && scene.Comp.HandlesRecreation(),
                            "ticking Recreation puts it back in service");
                scene.Comp.ToggleRecreation();
                scenario.Check(scene.Comp.IsExcluded && OwnersAre(ours, a, b),
                            "unticking Recreation keeps both owners");

                scene.Comp.ToggleRest();
                scenario.Check(!scene.Comp.IsExcluded && scene.Comp.HandlesRest(),
                            "ticking Sleeping puts it back in service");
                scene.Comp.ToggleRest();
                scenario.Check(scene.Comp.IsExcluded && OwnersAre(ours, a, b),
                            "unticking Sleeping keeps both owners");

                scene.Comp.SetAutomatic();
                scenario.Check(OwnersAre(ours, a, b), "and set to Automatic (from the room), the group is whole");
                scenario.Check(scene.Comp.CanBeClaimedBy(a) && scene.Comp.CanBeClaimedBy(b),
                            "and either owner may use the stand");

                ours.TryUnassignPawn(a);
                ours.TryUnassignPawn(b);
            }
        }

        /// <summary>
        /// On a stand out of service the owner window's Assign is a pick: one colonist replaces
        /// the whole list, a kept group included, and the other mod's list then names that
        /// colonist, so its button comes back.
        ///
        /// <para>Driven through <see cref="Dialog_AssignStandOwners.Assign"/>, the call a row's
        /// button makes, so the window's own choice between adding and replacing is under test.
        /// The stand in service is the counterpoint: the same call there must add, or a group
        /// could never be built.</para>
        /// </summary>
        [Test]
        [TestDescription("on a stand not used for shift changes, choosing one owner replaces a kept group, "
                         + "and the other mod's button comes back for them")]
        internal IEnumerator OnePickReplacesAKeptGroup()
        {
            return Staged(StageWithForeignAssignable, BesideAStandWithTwoLists, Body);

            void Body(Scene scene, Scenario scenario)
            {
                CompAssignableToPawn_ShiftStand ours =
                    scene.OutfitStand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                CompAssignableToPawn foreign = ForeignAssignableOn(scene.OutfitStand);
                if (ours == null || foreign == null)
                {
                    scenario.Check(false, "the stand has Shift Change's owner list and another mod's");
                    return;
                }
                if (foreign.TotalSlots != 1)
                {
                    scenario.Check(false, "the other mod's list has room for one owner, as Outfit Stands Plus's does");
                    return;
                }

                Pawn a = scene.Colonist;
                Pawn b = AnotherColonist(scenario, scene, Gender.Female, "PickB");
                Pawn c = AnotherColonist(scenario, scene, Gender.Male, "PickC");
                string first = a.LabelShort, second = b.LabelShort, third = c.LabelShort;
                Dialog_AssignStandOwners dialog = new Dialog_AssignStandOwners(ours);

                scene.Comp.SetAutomatic();
                dialog.Assign(a);
                dialog.Assign(b);
                scenario.Note("in the stand's owner window, " + first + " and then " + second + " are assigned");
                scenario.Check(!dialog.SingleOwner, "on a stand in service, the window adds owners rather than picks one");
                scenario.Check(OwnersAre(ours, a, b), "so the two make a group");
                scenario.Check(foreign.AssignedPawnsForReading.Count == 0,
                            "which does not fit the other mod's one place, so its list is empty");

                scene.Comp.SetExcluded();
                scenario.Check(dialog.SingleOwner, "set to Not used for shift changes, the window picks one colonist");
                scenario.Check(OwnersAre(ours, a, b), "and until a pick is made, the group is kept");

                dialog.Assign(c);
                scenario.Check(OwnersAre(ours, c), "picking " + third + " replaces the whole kept group");
                scenario.Check(OwnersAre(foreign, c),
                            "and the other list names " + third + ", so the other mod's button comes back");

                dialog.Assign(a);
                scenario.Check(OwnersAre(ours, a) && OwnersAre(foreign, a),
                            "picking " + first + " next replaces " + third);

                scene.Comp.SetAutomatic();
                scenario.Check(OwnersAre(ours, a),
                            "back in service, the pick stands, and the group it replaced stays gone");

                ours.TryUnassignPawn(a);
            }
        }

        /// <summary>
        /// A group kept on a stand out of service reaches nobody through the other mod's list, and
        /// comes back whole when the stand is moved.
        ///
        /// <para>The list half: a group fits Outfit Stands Plus's one place no better out of
        /// service than in it, so that list stays empty. The move half: the game parks a packed
        /// stand's owners and puts them back on landing through <c>TryAssignPawn</c>, one at a
        /// time, so an override that replaced the list would bring the group back as its last
        /// member. The landing also runs
        /// <see cref="CompAssignableToPawn_ShiftStand.UnifyOwners"/>, the spawn step a load
        /// shares, which must not clear a group either. Packed and set down as
        /// <see cref="LosingAStandOrAColonist.ReinstalledStandKeepsItsSettings"/> does it.</para>
        /// </summary>
        [Test]
        [TestDescription("a group kept on a stand not used for shift changes gets no other mod's button, "
                         + "and is all still there after the stand is moved")]
        internal IEnumerator AKeptGroupSurvivesAMove()
        {
            return Staged(StageWithForeignAssignable, BesideAStandWithTwoLists, Body);

            void Body(Scene scene, Scenario scenario)
            {
                CompAssignableToPawn_ShiftStand ours =
                    scene.OutfitStand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                CompAssignableToPawn foreign = ForeignAssignableOn(scene.OutfitStand);
                if (ours == null || foreign == null)
                {
                    scenario.Check(false, "the stand has Shift Change's owner list and another mod's");
                    return;
                }
                if (foreign.TotalSlots != 1)
                {
                    scenario.Check(false, "the other mod's list has room for one owner, as Outfit Stands Plus's does");
                    return;
                }

                Pawn a = scene.Colonist;
                Pawn b = AnotherColonist(scenario, scene, Gender.Female, "KeptB");

                scene.Comp.SetAutomatic();
                ours.TryAssignPawn(a);
                ours.TryAssignPawn(b);
                scene.Comp.SetExcluded();
                scenario.Check(OwnersAre(ours, a, b),
                            "a stand set to Not used for shift changes keeps its group of "
                            + a.LabelShort + " and " + b.LabelShort);
                scenario.Check(foreign.AssignedPawnsForReading.Count == 0,
                            "which fits no single place, so the other mod's list names nobody");

                Map map = scene.Map;
                IntVec3 from = scene.OutfitStand.Position;
                IntVec3 to = new IntVec3(from.x, 0, from.z + 2);
                Rot4 rot = scene.OutfitStand.Rotation;

                MinifiedThing box = scene.OutfitStand.MakeMinified();
                if (box == null)
                {
                    scenario.Check(false, "the stand can be packed up to move");
                    return;
                }
                scenario.Check(ours.AssignedPawnsForReading.Count == 0,
                            "packed up, the stand sets its owners aside, off its list");

                box.InnerThing = null;
                box.Destroy();
                GenSpawn.Spawn(scene.OutfitStand, to, map, rot);
                scenario.Note("the stand is set down two cells further north");

                scenario.Check(scene.OutfitStand.Spawned && scene.Comp.IsExcluded,
                            "it stands again, still not used for shift changes");
                scenario.Check(OwnersAre(ours, a, b), "with the whole group back, not just its last member");
                scenario.Check(foreign.AssignedPawnsForReading.Count == 0, "and the other list still names nobody");

                scene.Comp.SetAutomatic();
                scenario.Check(OwnersAre(ours, a, b), "set to Automatic (from the room), the group may use it again");

                ours.TryUnassignPawn(a);
                ours.TryUnassignPawn(b);
            }
        }

        /// <summary>
        /// Outfit Stands Plus' stand button, reading Shift Change's list of stands, offers the
        /// same stands in the same order as its own walk over every building. A known gap unless
        /// Outfit Stands Plus is loaded.
        ///
        /// <para>It first reads their method's IL: the original walks the colony's buildings once,
        /// and what runs calls the stand list in its place.</para>
        ///
        /// <para>The world is built to show either half going wrong. The scene's stand belongs to
        /// a second colonist. The owner gets a mechanized stand and then a second vanilla stand,
        /// put up in that order, and a third stand that names the owner but has no faction, which
        /// both lookups must leave out. Owners go into their lists through the base class, so the
        /// test checks the lookup and nothing about how Shift Change's copy fills those lists.
        /// Giving the mechanized stand no faction and back appends it to the end of the colony's
        /// building list, and the stand list has to follow.</para>
        ///
        /// <para>Order is checked because it is behaviour: the owner's two buttons merge into one
        /// and a click goes to the first, which the test checks. Their method runs with the switch
        /// off and on; that the off half is their walk rests on their method calling the list (the
        /// IL check) and the list handing back the walk when off (asked directly, at the
        /// end).</para>
        /// </summary>
        [Test]
        [TestDescription("Outfit Stands Plus' button, reading Shift Change's list of stands, sends a colonist "
                         + "to the same stands in the same order as its own search")]
        internal IEnumerator OutfitStandsPlusButtonsMatchTheirOwn()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing), BesideAStand, Body);

            void Body(Scene scene, Scenario scenario)
            {
                if (Patch_OutfitStandsPlusUseButton.useCompType == null)
                {
                    scenario.KnownGap(false, "Outfit Stands Plus' button gives what its own search gives",
                        "Outfit Stands Plus is not on this mod list; --with=khamenman.outfitstandsplus loads it");
                    return;
                }
                if (!scenario.Check(Patch_OutfitStandsPlusUseButton.applied,
                                 Patch_OutfitStandsPlusUseButton.applied
                                     ? "Shift Change's change to its button took hold at startup"
                                     : "Shift Change's change to its button took hold at startup; it stood aside because "
                                       + Patch_OutfitStandsPlusUseButton.standingAside))
                {
                    return;
                }

                MethodInfo standsCall = AccessTools.Method(typeof(Patch_OutfitStandsPlusUseButton),
                    nameof(Patch_OutfitStandsPlusUseButton.Stands));
                List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(
                    Patch_OutfitStandsPlusUseButton.walkSite);
                List<CodeInstruction> running = PatchProcessor.GetCurrentInstructions(
                    Patch_OutfitStandsPlusUseButton.walkSite);
                scenario.Check(original.Count(Patch_OutfitStandsPlusUseButton.IsTheWalk) == 1,
                            "Outfit Stands Plus' own code walks every building the colony owns, once");
                scenario.Check(!running.Any(Patch_OutfitStandsPlusUseButton.IsTheWalk)
                            && running.Count(i => i.Calls(standsCall)) == 1,
                            "the code that runs asks Shift Change's list of stands instead, once");
                scenario.Check(Patch_OutfitStandsPlusUseButton.callsReplaced == 1,
                            "as the line Shift Change logged at startup says: "
                            + Patch_OutfitStandsPlusUseButton.callsReplaced + " replaced");

                Type commandType = GenTypes.GetTypeInAnyAssembly("OutfitStandsPlus.Commands.OutfitStandsPlusUseCommand");
                Type ownerCompType = GenTypes.GetTypeInAnyAssembly(
                    "OutfitStandsPlus.ThingComps.CompAssignableToPawn_OutfitStandsPlusBase");
                FieldInfo standField = commandType == null ? null : AccessTools.Field(commandType, "OutfitStand");
                ThingDef powered = DefDatabase<ThingDef>.GetNamedSilentFail("OutfitStandsPlus_MechanizedOutfitStand");
                if (!scenario.Check(standField != null && ownerCompType != null && powered != null,
                                 "Outfit Stands Plus' button, owner list and mechanized outfit stand can all be found"))
                {
                    return;
                }

                Map map = scene.Map;
                Pawn owner = scene.Colonist;
                Pawn other = AnotherColonist(scenario, scene, Gender.Female, "Other");
                IntVec3 origin = scene.OutfitStand.Position;
                Building_OutfitStand poweredStand = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                    map, powered, ThingDefOf.Steel, origin + new IntVec3(4, 0, 0), Rot4.North);
                Building_OutfitStand secondVanilla = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                    map, scene.OutfitStand.def, ThingDefOf.WoodLog, origin + new IntVec3(4, 0, 2), Rot4.North);
                Building_OutfitStand unowned = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                    map, scene.OutfitStand.def, ThingDefOf.WoodLog, origin + new IntVec3(4, 0, 4), Rot4.North);
                unowned.SetFaction(null);

                CompAssignableToPawn[] lists =
                {
                    ForeignAssignableOn(scene.OutfitStand), ForeignAssignableOn(poweredStand),
                    ForeignAssignableOn(secondVanilla), ForeignAssignableOn(unowned),
                };
                if (!scenario.Check(lists.All(c => c != null && ownerCompType.IsInstanceOfType(c)),
                                 "every stand carries Outfit Stands Plus' owner list"))
                {
                    return;
                }
                lists[0].ForceAddPawn(other);
                lists[1].ForceAddPawn(owner);
                lists[2].ForceAddPawn(owner);
                lists[3].ForceAddPawn(owner);
                scenario.Note(other.LabelShort + " owns the first stand; " + owner.LabelShort
                           + " owns a mechanized stand, a second stand put up after it, and a third "
                           + "that belongs to no faction");

                ThingComp ownerUse = UseCompOn(owner);
                ThingComp otherUse = UseCompOn(other);
                if (!scenario.Check(ownerUse != null && otherUse != null,
                                 "both colonists carry Outfit Stands Plus' stand button"))
                {
                    return;
                }

                BothAgree(scenario, ownerUse, otherUse, standField,
                          new[] { poweredStand, secondVanilla }, scene.OutfitStand,
                          "as put up", out List<Gizmo> ownerButtons);
                scenario.Check(ownerButtons.Count == 2 && ownerButtons[0].GroupsWith(ownerButtons[1]),
                            "the owner's two buttons merge into one, so their order decides where a click goes");

                poweredStand.SetFaction(null);
                poweredStand.SetFaction(Faction.OfPlayer);
                // No faction change clears an owner list, in the game, Outfit Stands Plus or this
                // mod, so this adds nothing; it stays so the second gather tests the order alone.
                lists[1].ForceAddPawn(owner);
                scenario.Note("the mechanized stand is given to no faction and back, "
                           + "which moves it to the end of the colony's buildings");
                BothAgree(scenario, ownerUse, otherUse, standField,
                          new[] { secondVanilla, poweredStand }, scene.OutfitStand,
                          "after the mechanized stand changed hands and came back", out _);

                bool before = Patch_OutfitStandsPlusUseButton.Enabled;
                try
                {
                    Patch_OutfitStandsPlusUseButton.Enabled = true;
                    Type on = Patch_OutfitStandsPlusUseButton.Stands(map.listerBuildings).GetType();
                    Patch_OutfitStandsPlusUseButton.Enabled = false;
                    Type off = Patch_OutfitStandsPlusUseButton.Stands(map.listerBuildings).GetType();
                    scenario.Check(on.DeclaringType == typeof(Patch_OutfitStandsPlusUseButton)
                                && off.DeclaringType == typeof(ListerBuildings),
                                "Shift Change's tweak value for it hands back its list when on, "
                                + "and the game's walk when off");
                }
                finally
                {
                    Patch_OutfitStandsPlusUseButton.Enabled = before;
                }
            }
        }

        /// <summary>
        /// Gathers both colonists' Outfit Stands Plus buttons with Shift Change's list off, then
        /// on, and checks the walk gives what the world says and the list gives exactly the same.
        /// </summary>
        internal static void BothAgree(Scenario scenario, ThingComp ownerUse, ThingComp otherUse, FieldInfo standField,
                                       Building_OutfitStand[] ownerExpected, Building_OutfitStand otherExpected,
                                       string when, out List<Gizmo> ownerButtons)
        {
            List<Building_OutfitStand> walkOwner, walkOther, listOwner, listOther;
            bool before = Patch_OutfitStandsPlusUseButton.Enabled;
            try
            {
                Patch_OutfitStandsPlusUseButton.Enabled = false;
                walkOwner = Gather(ownerUse, standField, out _);
                walkOther = Gather(otherUse, standField, out _);
                Patch_OutfitStandsPlusUseButton.Enabled = true;
                listOwner = Gather(ownerUse, standField, out ownerButtons);
                listOther = Gather(otherUse, standField, out _);
            }
            finally
            {
                Patch_OutfitStandsPlusUseButton.Enabled = before;
            }

            scenario.Check(walkOwner.SequenceEqual(ownerExpected),
                        when + ": its own walk gives the owner " + Labels(ownerExpected)
                        + ", and not the stand with no faction (got " + Labels(walkOwner) + ")");
            scenario.Check(listOwner.SequenceEqual(walkOwner),
                        when + ": Shift Change's list gives the owner the same stands in the same order (got "
                        + Labels(listOwner) + ")");
            scenario.Check(walkOther.SequenceEqual(new[] { otherExpected }) && listOther.SequenceEqual(walkOther),
                        when + ": both give the other colonist their own stand and nothing else");
        }

        /// <summary>
        /// Rewrites the other mod's list from <paramref name="from"/> to <paramref name="to"/>
        /// behind Shift Change's back: these are that list's own calls, which none of Shift
        /// Change's overrides see, so no sync runs.
        /// </summary>
        internal static void Diverge(CompAssignableToPawn foreign, Pawn from, Pawn to)
        {
            foreign.ForceRemovePawn(from);
            foreign.ForceAddPawn(to);
        }

        /// <summary>Outfit Stands Plus' use comp on a colonist, found by type.</summary>
        internal static ThingComp UseCompOn(Pawn pawn)
        {
            return pawn.AllComps.FirstOrDefault(c => Patch_OutfitStandsPlusUseButton.useCompType.IsInstanceOfType(c));
        }

        /// <summary>Gathers the command bar's buttons, and the stand behind each.</summary>
        internal static List<Building_OutfitStand> Gather(ThingComp use, FieldInfo standField, out List<Gizmo> buttons)
        {
            buttons = use.CompGetGizmosExtra()?.ToList() ?? new List<Gizmo>();
            return buttons.Select(g => standField.GetValue(g) as Building_OutfitStand).ToList();
        }

        internal static string Labels(IList<Building_OutfitStand> stands)
        {
            return stands.Count == 0
                ? "nothing"
                : string.Join(", ", stands.Select(s => s == null ? "null" : s.def.defName + "#" + s.thingIDNumber));
        }
    }
}
