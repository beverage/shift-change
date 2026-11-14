using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DevTools.Testing;
using HarmonyLib;
using RimWorld;
using Verse;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Who may use a stand: its owners, the groups the player picks, and stands from other mods.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.WhoUsesAStand)]
    internal class WhoUsesAStand : StoryFixture
    {
        /// <summary>What <see cref="HarnessFixtures.Build"/> sets up.</summary>
        internal const string OnShift =
            "a colonist on shift at a stand: he wears what it held, and it keeps the clothes of his that made way";

        /// <summary>What <see cref="HarnessFixtures.Stage"/> sets up, with the parka.</summary>
        internal const string BesideAStand =
            "a stand holding a duster, and a colonist beside it in a shirt, trousers and a parka";

        /// <summary>What <see cref="HarnessFixtures.StageWithForeignAssignable"/> sets up.</summary>
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

            void Body(Fixture fix, Story story)
            {
                AccessTools.FieldRef<Building_OutfitStand, bool> flag =
                    Patch_AllowRemovingToggle.AllowRemovingItemsRef;
                if (flag == null)
                {
                    // A failure here is not a stale test: the protection rides on this field, so a
                    // rename silently removes it.
                    story.Check(false, "the stand's Allow removing items setting can still be found");
                    return;
                }
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                if (doctor == null)
                {
                    story.Check(false, "the Doctor work type exists");
                    return;
                }

                fix.Comp.SetExcluded();
                flag(fix.Stand) = true;
                story.Note("the stand is set to Not used for shift changes, and Allow removing items turned on");
                story.Check(flag(fix.Stand), "a stand not used for shift changes keeps it as the player set it");

                fix.Comp.SetAutomatic();
                story.Check(!flag(fix.Stand), "setting the stand to Automatic (from the room) turns it off");

                fix.Comp.SetExcluded();
                flag(fix.Stand) = true;
                fix.Comp.ToggleWork(doctor);
                story.Note("the stand is taken out of service and the setting turned on again, then Doctor ticked");
                story.Check(!flag(fix.Stand), "ticking Doctor turns it off");

                fix.Comp.SetExcluded();
                flag(fix.Stand) = true;
                fix.Comp.ToggleRecreation();
                story.Note("and once more, with Recreation ticked this time");
                story.Check(!flag(fix.Stand), "ticking Recreation turns it off");
                story.Check(!fix.Comp.IsExcluded, "and the stand really is back in service");

                story.Check(!((IApparelSource)fix.Stand).ApparelSourceEnabled,
                            "so colonists choosing their outfits cannot take from the stand: "
                            + "the game's gate for that is the same setting");
                story.Check(!((IHaulSource)fix.Stand).HaulSourceEnabled,
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

            void Body(Fixture fix, Story story)
            {
                Pawn bystander = AnotherColonist(story, fix, Gender.Female, "Bystander");
                List<Gizmo> chain = Sentinels();
                IEnumerable<Gizmo> passed = Patch_ChangeBackGizmo.Postfix(chain, bystander);

                story.Check(CompShiftStand.OnShiftStandFor(bystander) == null,
                            bystander.LabelShort + " is on shift at no stand");
                story.Check(ReferenceEquals(passed, chain),
                            "so Shift Change hands back the very sequence of buttons it was given, "
                            + "not a wrapper around it");
                story.Check(SameSequence(passed, chain), "with every one of the game's buttons still in it, in order");

                // Harmony never hands the patch a null colonist, but it guards against one, and
                // dropping that guard should fail here rather than throw in somebody's colony.
                story.Check(ReferenceEquals(Patch_ChangeBackGizmo.Postfix(chain, null), chain),
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

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                List<Gizmo> chain = Sentinels();
                List<Gizmo> got = Patch_ChangeBackGizmo.Postfix(chain, fix.Pawn).ToList();

                story.Check(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                            name + " is on shift at the stand, in what it held");
                story.Check(got.Count == chain.Count + 1,
                            "so " + name + "'s buttons come back with exactly one added");
                story.Check(SameSequence(got.Take(chain.Count), chain),
                            "the game's own buttons are untouched, and still in order");
                story.Check(got.Count > chain.Count
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
                    story.Note("Shift Change's automatic changes are switched off, as its tweak value does");
                    story.Check(Patch_ChangeBackGizmo.Postfix(chain, fix.Pawn).Count() == chain.Count + 1,
                                "Change back is still offered");
                    Patch_JobInterception.Enabled = armedBefore;
                    Patch_JobInterception.faulted = true;
                    story.Note("switched back on, they are shut off instead, as repeated errors do");
                    story.Check(Patch_ChangeBackGizmo.Postfix(chain, fix.Pawn).Count() == chain.Count + 1,
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
                IntVec3 cell = fix.Stand.Position;
                fix.Stand.DeSpawn(DestroyMode.WillReplace);
                story.Note("the stand is lifted off the map, as a gravship takeoff does, still keeping "
                           + name + "'s clothes");
                story.Check(ReferenceEquals(Patch_ChangeBackGizmo.Postfix(chain, fix.Pawn), chain),
                            "while the stand is off the map, " + name + " gets no Change back at all");
                GenSpawn.Spawn(fix.Stand, cell, fix.Map, Rot4.North);
                fix.Stand.PostSwapMap();
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

            void Body(Fixture fix, Story story)
            {
                string vanillaLabel = "CommandAllowRemovingApparel".Translate().ToString();
                string ours = "ShiftChange.AllowRemovingDesc".Translate().RawText;
                const string untouched = "vanilla's own description, verbatim";

                fix.Comp.SetExcluded();
                Command_Toggle foreign = RemovalToggle(vanillaLabel, active: false, desc: untouched);
                List<Gizmo> chain = new List<Gizmo> { Sentinels()[0], foreign };
                IEnumerable<Gizmo> passed = Patch_AllowRemovingToggle.Postfix(chain, fix.Stand);

                story.Check(fix.Comp.IsExcluded, "the stand is set to Not used for shift changes");
                story.Check(ReferenceEquals(passed, chain),
                            "so Shift Change hands back the very sequence of buttons it was given, "
                            + "not a wrapper around it");
                story.Check(foreign.defaultDesc == untouched,
                            "and the Allow removing items tooltip is left exactly as the game wrote it");
                story.Check(!foreign.Disabled, "with the button still live");

                fix.Comp.SetAutomatic();
                Command_Toggle governed = RemovalToggle(vanillaLabel, active: false, desc: untouched);
                Command_Toggle alreadyOn = RemovalToggle(vanillaLabel, active: true, desc: untouched);
                // A toggle that is not the game's. The patch finds the game's by its label, the
                // command being an anonymous Command_Toggle with no other handle, so a renamed
                // label key makes it match nothing and the stand fall back to the game's own. This
                // button is that fallback.
                Command_Toggle stranger = RemovalToggle("Another mod's toggle", active: false,
                                                        desc: untouched);
                List<Gizmo> governedChain = new List<Gizmo> { governed, alreadyOn, stranger };
                List<Gizmo> got = Patch_AllowRemovingToggle.Postfix(governedChain, fix.Stand).ToList();

                story.Check(!fix.Comp.IsExcluded, "set to Automatic (from the room), the stand is back in service");
                story.Check(SameSequence(got, governedChain), "every button still comes through, in order");
                story.Check(governed.defaultDesc != untouched && governed.defaultDesc.Contains(ours),
                            "the Allow removing items tooltip now carries Shift Change's paragraph "
                            + "as well as the game's");
                // TaggedString's implicit conversion to string strips tags, so the patch must use
                // .RawText on both halves of the tooltip: assigning a Translate() result straight
                // to the field silently deletes the colour and bold. This check guards that.
                story.Check(ours.Contains("<color") && governed.defaultDesc.Contains("<color"),
                            "with its colour and bold intact, not stripped on the way");
                story.Check(governed.Disabled,
                            "a button that is off is greyed out while the stand is in service");
                story.Check(!alreadyOn.Disabled, "while one already on keeps its way back out");
                story.Check(stranger.defaultDesc == untouched && !stranger.Disabled,
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
            return Staged((m, p) => Stage(m, p, StageKit.Displacing), BesideAStand, Body);

            void Body(Fixture fix, Story story)
            {
                CompAssignableToPawn assignable =
                    fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                if (assignable == null)
                {
                    story.Check(false, "the stand has Shift Change's owner list");
                    return;
                }

                Pawn a = fix.Pawn;
                Pawn b = AnotherColonist(story, fix, Gender.Female, "OwnerB");
                Pawn c = AnotherColonist(story, fix, Gender.Male, "Outsider");
                string first = a.LabelShort, second = b.LabelShort, outsider = c.LabelShort;

                story.Check(fix.Comp.IsPool, "with no owners, the stand is shared");
                story.Check(fix.Comp.CanBeClaimedBy(a) && fix.Comp.CanBeClaimedBy(c),
                            "and anyone able may use it, " + first + " and " + outsider + " alike");

                assignable.TryAssignPawn(a);
                story.Note(first + " is made its owner");
                story.Check(!fix.Comp.IsPool, "with an owner, it is no longer shared");
                story.Check(fix.Comp.IsAssignedTo(a), first + " is listed as its owner");
                story.Check(fix.Comp.CanBeClaimedBy(a), first + " may use it");
                story.Check(!fix.Comp.CanBeClaimedBy(c), "and nobody else may: not " + outsider);

                assignable.TryAssignPawn(b);
                story.Note(second + " is made an owner too");
                story.Check(fix.Comp.AssignedOwners.Count == 2, "the stand lists both owners");
                story.Check(fix.Comp.CanBeClaimedBy(a), "the first owner, " + first + ", may still use it");
                // The one a single-owner reader gets wrong.
                story.Check(fix.Comp.CanBeClaimedBy(b), "and so may the second, " + second);
                story.Check(!fix.Comp.CanBeClaimedBy(c), "while " + outsider + " still may not");

                assignable.TryUnassignPawn(a);
                story.Check(!fix.Comp.CanBeClaimedBy(a),
                            "removing " + first + " as an owner means " + first + " may no longer use it");
                story.Check(fix.Comp.CanBeClaimedBy(b), "and leaves " + second + " its owner");

                assignable.TryUnassignPawn(b);
                story.Check(fix.Comp.IsPool, "removing the last owner makes the stand shared again");
                story.Check(fix.Comp.CanBeClaimedBy(c), "and " + outsider + " may use it again");
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
            return Staged((m, p) => Stage(m, p, StageKit.Displacing), BesideAStand, Body);

            void Body(Fixture fix, Story story)
            {
                CompAssignableToPawn assignable =
                    fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                if (assignable == null)
                {
                    story.Check(false, "the stand has Shift Change's owner list");
                    return;
                }

                Pawn man = AnotherColonist(story, fix, Gender.Male, "FilterM");
                Pawn woman = AnotherColonist(story, fix, Gender.Female, "FilterF");
                string he = man.LabelShort, she = woman.LabelShort;

                Dialog_AssignStandOwners dialog = new Dialog_AssignStandOwners(assignable);
                story.Note("the stand's owner window is opened");

                dialog.filter = Dialog_AssignStandOwners.Filter.All;
                List<Pawn> all = dialog.Candidates();
                story.Check(all.Contains(man) && all.Contains(woman),
                            "under All, both " + he + " and " + she + " are offered");

                dialog.filter = Dialog_AssignStandOwners.Filter.Male;
                List<Pawn> males = dialog.Candidates();
                story.Check(males.Contains(man), "under Men, " + he + " is offered");
                story.Check(!males.Contains(woman), "and " + she + " is not");
                story.Check(males.All(p => p.gender == Gender.Male),
                            "and no woman anywhere in the colony slipped through");

                dialog.filter = Dialog_AssignStandOwners.Filter.Female;
                List<Pawn> females = dialog.Candidates();
                story.Check(females.Contains(woman), "under Women, " + she + " is offered");
                story.Check(!females.Contains(man), "and " + he + " is not");

                // An owner leaves the list of colonists to add; the window draws them among the
                // owners above it, whatever the filter says.
                assignable.TryAssignPawn(woman);
                dialog.filter = Dialog_AssignStandOwners.Filter.All;
                story.Note(she + " is made an owner, and the filter set back to All");
                story.Check(!dialog.Candidates().Contains(woman), she + " is no longer offered to add");
                story.Check(assignable.AssignedPawnsForReading.Contains(woman),
                            "because she is among the stand's owners now");

                dialog.filter = Dialog_AssignStandOwners.Filter.Male;
                story.Check(assignable.AssignedPawnsForReading.Contains(woman),
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

            void Body(Fixture fix, Story story)
            {
                CompAssignableToPawn_ShiftStand ours =
                    fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                CompAssignableToPawn foreign = ForeignAssignableOn(fix.Stand);
                if (ours == null || foreign == null)
                {
                    story.Check(false, "the stand has Shift Change's owner list and another mod's");
                    return;
                }
                if (foreign.TotalSlots != 1)
                {
                    // The second-owner checks assume one place, as Outfit Stands Plus has; a bigger
                    // list is a different test.
                    story.Check(false, "the other mod's list has room for one owner, as Outfit Stands Plus's does");
                    return;
                }

                Pawn a = fix.Pawn;
                Pawn b = AnotherColonist(story, fix, Gender.Female, "CopyB");
                string first = a.LabelShort, second = b.LabelShort;

                story.Check(foreign.AssignedPawnsForReading.Count == 0,
                            "with no owners, the other mod's list is empty");

                ours.TryAssignPawn(a);
                story.Check(Names(foreign, a),
                            "with " + first + " as its one owner, the other mod's list names "
                            + first + " and nobody else");

                ours.TryAssignPawn(b);
                story.Check(ours.AssignedPawnsForReading.Count == 2,
                            "with " + second + " added, Shift Change lists both owners");
                story.Check(foreign.AssignedPawnsForReading.Count == 0,
                            "and two do not fit the other list's one place, so it is emptied, not cut short");

                ours.TryUnassignPawn(a);
                story.Check(Names(foreign, b), "back to one owner, the other list names " + second);

                // The release unassigns through Shift Change's list, so the other list follows and
                // a colonist who died or left loses their button on the stand.
                Patch_UnclaimStands.ReapStandsFor(b);
                story.Note("the game releases what " + second + " owned, as when a colonist dies");
                story.Check(ours.AssignedPawnsForReading.Count == 0, "Shift Change's list lets " + second + " go");
                story.Check(foreign.AssignedPawnsForReading.Count == 0, "and the other list with it");

                IEnumerable<Gizmo> hidden = null;
                fix.Comp.SetAutomatic();
                story.Check(!Patch_ForeignOwnerGizmos.Prefix(foreign, ref hidden),
                            "a stand in service hides the other mod's Set owner button");

                fix.Comp.SetExcluded();
                List<Command> ourGizmos = ours.CompGetGizmosExtra().OfType<Command>().ToList();
                story.Check(!Patch_ForeignOwnerGizmos.Prefix(foreign, ref hidden),
                            "and so does a stand set to Not used for shift changes");
                story.Check(ourGizmos.Count == 1, "which shows Shift Change's owner button in its place");
                story.Check(ourGizmos.Count == 1
                            && ourGizmos[0].defaultLabel == "CommandThingSetOwnerLabel".Translate(),
                            "labelled Set owner, since a stand not used for shift changes is shared by nobody");

                ours.TryAssignPawn(a);
                story.Check(Names(foreign, a), "and there too the other list names " + first + " once ours does");

                ours.TryUnassignPawn(a);
                fix.Comp.SetAutomatic();
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

            void Body(Fixture fix, Story story)
            {
                CompAssignableToPawn_ShiftStand ours =
                    fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                CompAssignableToPawn foreign = ForeignAssignableOn(fix.Stand);
                if (ours == null || foreign == null)
                {
                    story.Check(false, "the stand has Shift Change's owner list and another mod's");
                    return;
                }
                if (foreign.TotalSlots != 1)
                {
                    story.Check(false, "the other mod's list has room for one owner, as Outfit Stands Plus's does");
                    return;
                }

                Pawn a = fix.Pawn;
                Pawn b = AnotherColonist(story, fix, Gender.Female, "ReconcileB");
                string first = a.LabelShort, second = b.LabelShort;

                // In service both lists name someone and disagree; Shift Change's is the one the
                // player could see.
                fix.Comp.SetAutomatic();
                ours.TryAssignPawn(a);
                Diverge(foreign, a, b);
                ours.ownersUnified = false;
                story.Note("as an older save left it: the stand in service and owned by " + first
                           + ", the other mod's list naming " + second + " instead; the stand is loaded");
                ours.UnifyOwners();
                story.Check(Names(ours, a) && Names(foreign, a),
                            "a stand in service keeps its own owner, " + first
                            + ", and the other list's stale one is replaced");
                story.Check(ours.ownersUnified, "and the stand is marked as reconciled");

                // Not used for shift changes: the other mod's list is the one the player could see.
                fix.Comp.SetExcluded();
                Diverge(foreign, a, b);
                ours.ownersUnified = false;
                ours.UnifyOwners();
                story.Check(Names(ours, b) && Names(foreign, b),
                            "a stand set to Not used for shift changes takes the other mod's owner, "
                            + second + ", instead");

                // Only the other list names anyone. Adopted, so an earlier assignment is not lost.
                fix.Comp.SetAutomatic();
                ours.TryUnassignPawn(b);
                foreign.ForceAddPawn(a);
                ours.ownersUnified = false;
                ours.UnifyOwners();
                story.Check(Names(ours, a),
                            "a stand with no owners of its own adopts the only owner there is, " + first);

                // Only Shift Change's list names anyone, and two of them: nothing to adopt, and
                // nothing that fits the copy.
                ours.TryAssignPawn(b);
                ours.ownersUnified = false;
                ours.UnifyOwners();
                story.Check(ours.AssignedPawnsForReading.Count == 2
                            && foreign.AssignedPawnsForReading.Count == 0,
                            "a stand with two owners keeps both, and the other list, with room for one, "
                            + "stays empty");

                // Once reconciled, the other list has no say.
                ours.TryUnassignPawn(b);
                Diverge(foreign, a, b);
                ours.UnifyOwners();
                story.Check(Names(ours, a) && Names(foreign, a),
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
            return Staged((m, p) => Stage(m, p, StageKit.Displacing), BesideAStand, Body);

            void Body(Fixture fix, Story story)
            {
                CompAssignableToPawn_ShiftStand ours =
                    fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                if (ours == null || doctor == null)
                {
                    story.Check(false, "the stand has Shift Change's owner list, and the Doctor work type exists");
                    return;
                }

                Pawn a = fix.Pawn;
                Pawn b = AnotherColonist(story, fix, Gender.Female, "GroupB");

                fix.Comp.SetAutomatic();
                ours.TryAssignPawn(a);
                ours.TryAssignPawn(b);
                story.Check(Names(ours, a, b),
                            "a stand in service, owned by the group of " + a.LabelShort + " and " + b.LabelShort);

                fix.Comp.SetExcluded();
                story.Check(fix.Comp.IsExcluded && Names(ours, a, b),
                            "set to Not used for shift changes, it keeps both owners");

                // A stand out of service has no work types to start from, so each tick below
                // starts a fresh set.
                fix.Comp.ToggleWork(doctor);
                story.Check(!fix.Comp.IsExcluded && fix.Comp.WorkTypes.Count == 1,
                            "ticking Doctor puts it back in service, for that one work type");
                fix.Comp.ToggleWork(doctor);
                story.Check(fix.Comp.IsExcluded && Names(ours, a, b),
                            "unticking that last work type takes it out again, and it keeps both owners");

                fix.Comp.ToggleRecreation();
                story.Check(!fix.Comp.IsExcluded && fix.Comp.HandlesRecreation(),
                            "ticking Recreation puts it back in service");
                fix.Comp.ToggleRecreation();
                story.Check(fix.Comp.IsExcluded && Names(ours, a, b),
                            "unticking Recreation keeps both owners");

                fix.Comp.ToggleRest();
                story.Check(!fix.Comp.IsExcluded && fix.Comp.HandlesRest(),
                            "ticking Sleeping puts it back in service");
                fix.Comp.ToggleRest();
                story.Check(fix.Comp.IsExcluded && Names(ours, a, b),
                            "unticking Sleeping keeps both owners");

                fix.Comp.SetAutomatic();
                story.Check(Names(ours, a, b), "and set to Automatic (from the room), the group is whole");
                story.Check(fix.Comp.CanBeClaimedBy(a) && fix.Comp.CanBeClaimedBy(b),
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

            void Body(Fixture fix, Story story)
            {
                CompAssignableToPawn_ShiftStand ours =
                    fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                CompAssignableToPawn foreign = ForeignAssignableOn(fix.Stand);
                if (ours == null || foreign == null)
                {
                    story.Check(false, "the stand has Shift Change's owner list and another mod's");
                    return;
                }
                if (foreign.TotalSlots != 1)
                {
                    story.Check(false, "the other mod's list has room for one owner, as Outfit Stands Plus's does");
                    return;
                }

                Pawn a = fix.Pawn;
                Pawn b = AnotherColonist(story, fix, Gender.Female, "PickB");
                Pawn c = AnotherColonist(story, fix, Gender.Male, "PickC");
                string first = a.LabelShort, second = b.LabelShort, third = c.LabelShort;
                Dialog_AssignStandOwners dialog = new Dialog_AssignStandOwners(ours);

                fix.Comp.SetAutomatic();
                dialog.Assign(a);
                dialog.Assign(b);
                story.Note("in the stand's owner window, " + first + " and then " + second + " are assigned");
                story.Check(!dialog.SingleOwner, "on a stand in service, the window adds owners rather than picks one");
                story.Check(Names(ours, a, b), "so the two make a group");
                story.Check(foreign.AssignedPawnsForReading.Count == 0,
                            "which does not fit the other mod's one place, so its list is empty");

                fix.Comp.SetExcluded();
                story.Check(dialog.SingleOwner, "set to Not used for shift changes, the window picks one colonist");
                story.Check(Names(ours, a, b), "and until a pick is made, the group is kept");

                dialog.Assign(c);
                story.Check(Names(ours, c), "picking " + third + " replaces the whole kept group");
                story.Check(Names(foreign, c),
                            "and the other list names " + third + ", so the other mod's button comes back");

                dialog.Assign(a);
                story.Check(Names(ours, a) && Names(foreign, a), "picking " + first + " next replaces " + third);

                fix.Comp.SetAutomatic();
                story.Check(Names(ours, a),
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
        /// <see cref="HarnessLifecycle.ReinstallKeepsConfiguration"/> does it.</para>
        /// </summary>
        [Test]
        [TestDescription("a group kept on a stand not used for shift changes gets no other mod's button, "
                         + "and is all still there after the stand is moved")]
        internal IEnumerator AKeptGroupSurvivesAMove()
        {
            return Staged(StageWithForeignAssignable, BesideAStandWithTwoLists, Body);

            void Body(Fixture fix, Story story)
            {
                CompAssignableToPawn_ShiftStand ours =
                    fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                CompAssignableToPawn foreign = ForeignAssignableOn(fix.Stand);
                if (ours == null || foreign == null)
                {
                    story.Check(false, "the stand has Shift Change's owner list and another mod's");
                    return;
                }
                if (foreign.TotalSlots != 1)
                {
                    story.Check(false, "the other mod's list has room for one owner, as Outfit Stands Plus's does");
                    return;
                }

                Pawn a = fix.Pawn;
                Pawn b = AnotherColonist(story, fix, Gender.Female, "KeptB");

                fix.Comp.SetAutomatic();
                ours.TryAssignPawn(a);
                ours.TryAssignPawn(b);
                fix.Comp.SetExcluded();
                story.Check(Names(ours, a, b),
                            "a stand set to Not used for shift changes keeps its group of "
                            + a.LabelShort + " and " + b.LabelShort);
                story.Check(foreign.AssignedPawnsForReading.Count == 0,
                            "which fits no single place, so the other mod's list names nobody");

                Map map = fix.Map;
                IntVec3 from = fix.Stand.Position;
                IntVec3 to = new IntVec3(from.x, 0, from.z + 2);
                Rot4 rot = fix.Stand.Rotation;

                MinifiedThing box = fix.Stand.MakeMinified();
                if (box == null)
                {
                    story.Check(false, "the stand can be packed up to move");
                    return;
                }
                story.Check(ours.AssignedPawnsForReading.Count == 0,
                            "packed up, the stand sets its owners aside, off its list");

                box.InnerThing = null;
                box.Destroy();
                GenSpawn.Spawn(fix.Stand, to, map, rot);
                story.Note("the stand is set down two cells further north");

                story.Check(fix.Stand.Spawned && fix.Comp.IsExcluded,
                            "it stands again, still not used for shift changes");
                story.Check(Names(ours, a, b), "with the whole group back, not just its last member");
                story.Check(foreign.AssignedPawnsForReading.Count == 0, "and the other list still names nobody");

                fix.Comp.SetAutomatic();
                story.Check(Names(ours, a, b), "set to Automatic (from the room), the group may use it again");

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
            return Staged((m, p) => Stage(m, p, StageKit.Displacing), BesideAStand, Body);

            void Body(Fixture fix, Story story)
            {
                if (Patch_OutfitStandsPlusUseButton.useCompType == null)
                {
                    story.KnownGap(false, "Outfit Stands Plus' button gives what its own search gives",
                        "Outfit Stands Plus is not on this mod list; --with=khamenman.outfitstandsplus loads it");
                    return;
                }
                if (!story.Check(Patch_OutfitStandsPlusUseButton.applied,
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
                story.Check(original.Count(Patch_OutfitStandsPlusUseButton.IsTheWalk) == 1,
                            "Outfit Stands Plus' own code walks every building the colony owns, once");
                story.Check(!running.Any(Patch_OutfitStandsPlusUseButton.IsTheWalk)
                            && running.Count(i => i.Calls(standsCall)) == 1,
                            "the code that runs asks Shift Change's list of stands instead, once");
                story.Check(Patch_OutfitStandsPlusUseButton.callsReplaced == 1,
                            "as the line Shift Change logged at startup says: "
                            + Patch_OutfitStandsPlusUseButton.callsReplaced + " replaced");

                Type commandType = GenTypes.GetTypeInAnyAssembly("OutfitStandsPlus.Commands.OutfitStandsPlusUseCommand");
                Type ownerCompType = GenTypes.GetTypeInAnyAssembly(
                    "OutfitStandsPlus.ThingComps.CompAssignableToPawn_OutfitStandsPlusBase");
                FieldInfo standField = commandType == null ? null : AccessTools.Field(commandType, "OutfitStand");
                ThingDef powered = DefDatabase<ThingDef>.GetNamedSilentFail("OutfitStandsPlus_MechanizedOutfitStand");
                if (!story.Check(standField != null && ownerCompType != null && powered != null,
                                 "Outfit Stands Plus' button, owner list and mechanized outfit stand can all be found"))
                {
                    return;
                }

                Map map = fix.Map;
                Pawn owner = fix.Pawn;
                Pawn other = AnotherColonist(story, fix, Gender.Female, "Other");
                IntVec3 origin = fix.Stand.Position;
                Building_OutfitStand poweredStand = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                    map, powered, ThingDefOf.Steel, origin + new IntVec3(4, 0, 0), Rot4.North);
                Building_OutfitStand secondVanilla = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                    map, fix.Stand.def, ThingDefOf.WoodLog, origin + new IntVec3(4, 0, 2), Rot4.North);
                Building_OutfitStand unowned = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                    map, fix.Stand.def, ThingDefOf.WoodLog, origin + new IntVec3(4, 0, 4), Rot4.North);
                unowned.SetFaction(null);

                CompAssignableToPawn[] lists =
                {
                    ForeignAssignableOn(fix.Stand), ForeignAssignableOn(poweredStand),
                    ForeignAssignableOn(secondVanilla), ForeignAssignableOn(unowned),
                };
                if (!story.Check(lists.All(c => c != null && ownerCompType.IsInstanceOfType(c)),
                                 "every stand carries Outfit Stands Plus' owner list"))
                {
                    return;
                }
                lists[0].ForceAddPawn(other);
                lists[1].ForceAddPawn(owner);
                lists[2].ForceAddPawn(owner);
                lists[3].ForceAddPawn(owner);
                story.Note(other.LabelShort + " owns the first stand; " + owner.LabelShort
                           + " owns a mechanized stand, a second stand put up after it, and a third "
                           + "that belongs to no faction");

                ThingComp ownerUse = UseCompOn(owner);
                ThingComp otherUse = UseCompOn(other);
                if (!story.Check(ownerUse != null && otherUse != null,
                                 "both colonists carry Outfit Stands Plus' stand button"))
                {
                    return;
                }

                BothAgree(story, ownerUse, otherUse, standField, new[] { poweredStand, secondVanilla }, fix.Stand,
                          "as put up", out List<Gizmo> ownerButtons);
                story.Check(ownerButtons.Count == 2 && ownerButtons[0].GroupsWith(ownerButtons[1]),
                            "the owner's two buttons merge into one, so their order decides where a click goes");

                poweredStand.SetFaction(null);
                poweredStand.SetFaction(Faction.OfPlayer);
                // No faction change clears an owner list, in the game, Outfit Stands Plus or this
                // mod, so this adds nothing; it stays so the second gather tests the order alone.
                lists[1].ForceAddPawn(owner);
                story.Note("the mechanized stand is given to no faction and back, "
                           + "which moves it to the end of the colony's buildings");
                BothAgree(story, ownerUse, otherUse, standField, new[] { secondVanilla, poweredStand }, fix.Stand,
                          "after the mechanized stand changed hands and came back", out _);

                bool before = Patch_OutfitStandsPlusUseButton.Enabled;
                try
                {
                    Patch_OutfitStandsPlusUseButton.Enabled = true;
                    Type on = Patch_OutfitStandsPlusUseButton.Stands(map.listerBuildings).GetType();
                    Patch_OutfitStandsPlusUseButton.Enabled = false;
                    Type off = Patch_OutfitStandsPlusUseButton.Stands(map.listerBuildings).GetType();
                    story.Check(on.DeclaringType == typeof(Patch_OutfitStandsPlusUseButton)
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
        /// Another colonist beside the stand, where
        /// <see cref="HarnessFixtures.SpawnExtra(Fixture, Gender, string)"/> puts one, and cleared
        /// with the scene. A spawn that throws is retried as there, each retry noted in the story,
        /// and the last try's exception fails the test.
        /// </summary>
        internal static Pawn AnotherColonist(Story story, Fixture fix, Gender gender, string nick)
        {
            IntVec3 cell = fix.Stand.Position + new IntVec3(2, 0, fix.Extras.Count + 1);
            for (int attempt = 1; ; attempt++)
            {
                Pawn pawn = DebugTools_Fixtures.AveragePawn(gender, nick);
                pawn.workSettings?.EnableAndInitialize();
                try
                {
                    GenSpawn.Spawn(pawn, cell, fix.Map, Rot4.North);
                    fix.Extras.Add(pawn);
                    story.Note(pawn.LabelShort + (gender == Gender.Female ? ", a woman," : ", a man,")
                               + " joins the colony beside the stand");
                    return pawn;
                }
                catch (Exception e) when (attempt < BuildAttempts)
                {
                    story.Note("spawning " + nick + " threw on try " + attempt + ": "
                               + e.GetType().Name + ": " + e.Message);
                    try
                    {
                        // Likely half spawned: clear it before the retry lands on the same cell.
                        if (!pawn.Destroyed)
                        {
                            pawn.Destroy();
                        }
                    }
                    catch (Exception)
                    {
                        // The scene's teardown clears the pad whatever is left on it.
                    }
                }
            }
        }

        /// <summary>
        /// Gathers both colonists' Outfit Stands Plus buttons with Shift Change's list off, then
        /// on, and checks the walk gives what the world says and the list gives exactly the same.
        /// </summary>
        internal static void BothAgree(Story story, ThingComp ownerUse, ThingComp otherUse, FieldInfo standField,
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

            story.Check(walkOwner.SequenceEqual(ownerExpected),
                        when + ": its own walk gives the owner " + Labels(ownerExpected)
                        + ", and not the stand with no faction (got " + Labels(walkOwner) + ")");
            story.Check(listOwner.SequenceEqual(walkOwner),
                        when + ": Shift Change's list gives the owner the same stands in the same order (got "
                        + Labels(listOwner) + ")");
            story.Check(walkOther.SequenceEqual(new[] { otherExpected }) && listOther.SequenceEqual(walkOther),
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
