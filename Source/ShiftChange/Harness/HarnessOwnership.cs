// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
#if HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Harness cases for who a stand serves: owner lists and the owner dialog's filter, the
    /// copy of the owner list in another mod's assignable and the reconcile behind it, a
    /// stand not used for shift changes, the removal flag held off in service, and the two
    /// gizmo-chain gates. Cases are registered, and ordered, in
    /// <see cref="DebugTools_LifecycleHarness.Run"/>.
    /// </summary>
    internal static class HarnessOwnership
    {
        /// <summary>
        /// Ownership is a set. The danger in going from one owner to many is a reader that still
        /// asks whether the owner is this pawn and answers no for everyone but the first, so the
        /// assertions about a second owner are the ones that matter.
        /// </summary>
        internal static bool OwnerListRestricts(Fixture fix)
        {
            CompAssignableToPawn assignable =
                fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
            if (assignable == null)
            {
                return Expect(false, "the stand carries our assignable comp");
            }

            Pawn a = fix.Pawn;
            Pawn b = SpawnExtra(fix, Gender.Female, "OwnerB");
            Pawn c = SpawnExtra(fix, Gender.Male, "Outsider");

            bool ok = Expect(fix.Comp.IsPool, "no owners: the stand is a pool stand")
                    & Expect(fix.Comp.CanBeClaimedBy(a) && fix.Comp.CanBeClaimedBy(c),
                             "and anyone capable may claim it");

            assignable.TryAssignPawn(a);
            ok &= Expect(!fix.Comp.IsPool, "one owner: no longer a pool stand")
                & Expect(fix.Comp.IsAssignedTo(a), "the owner is assigned")
                & Expect(fix.Comp.CanBeClaimedBy(a), "the owner may claim it")
                & Expect(!fix.Comp.CanBeClaimedBy(c), "and nobody else may");

            assignable.TryAssignPawn(b);
            ok &= Expect(fix.Comp.AssignedOwners.Count == 2, "two owners are both held")
                & Expect(fix.Comp.CanBeClaimedBy(a), "the FIRST owner may still claim it")
                // The one a single-owner reader gets wrong.
                & Expect(fix.Comp.CanBeClaimedBy(b), "the SECOND owner may claim it too")
                & Expect(!fix.Comp.CanBeClaimedBy(c), "and an outsider still may not");

            assignable.TryUnassignPawn(a);
            ok &= Expect(!fix.Comp.CanBeClaimedBy(a), "removing an owner revokes that one")
                & Expect(fix.Comp.CanBeClaimedBy(b), "and leaves the other intact");

            assignable.TryUnassignPawn(b);
            return ok
                & Expect(fix.Comp.IsPool, "removing the last owner returns it to the pool")
                & Expect(fix.Comp.CanBeClaimedBy(c), "and an outsider may claim it again");
        }

        /// <summary>
        /// The owner dialog's candidate list: the filter narrows by gender and never hides a pawn
        /// who is already an owner, since a filter that hid the owner you wanted to remove would
        /// be a trap.
        /// </summary>
        internal static bool OwnerFilterOffersTheRightPawns(Fixture fix)
        {
            CompAssignableToPawn assignable =
                fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
            if (assignable == null)
            {
                return Expect(false, "the stand carries our assignable comp");
            }

            Pawn man = SpawnExtra(fix, Gender.Male, "FilterM");
            Pawn woman = SpawnExtra(fix, Gender.Female, "FilterF");

            Dialog_AssignStandOwners dialog = new Dialog_AssignStandOwners(assignable);

            dialog.filter = Dialog_AssignStandOwners.Filter.All;
            List<Pawn> all = dialog.Candidates();
            bool ok = Expect(all.Contains(man) && all.Contains(woman),
                             "All: both colonists are offered");

            dialog.filter = Dialog_AssignStandOwners.Filter.Male;
            List<Pawn> males = dialog.Candidates();
            ok &= Expect(males.Contains(man), "Men: the man is offered")
                & Expect(!males.Contains(woman), "and the woman is not")
                & Expect(males.All(p => p.gender == Gender.Male),
                         "and nothing else in the colony slipped through");

            dialog.filter = Dialog_AssignStandOwners.Filter.Female;
            List<Pawn> females = dialog.Candidates();
            ok &= Expect(females.Contains(woman), "Women: the woman is offered")
                & Expect(!females.Contains(man), "and the man is not");

            // An assigned pawn leaves the candidate list; the dialog draws them in the assigned
            // section above it, whatever the filter says.
            assignable.TryAssignPawn(woman);
            dialog.filter = Dialog_AssignStandOwners.Filter.All;
            ok &= Expect(!dialog.Candidates().Contains(woman),
                         "an owner is no longer offered as a candidate")
                & Expect(assignable.AssignedPawnsForReading.Contains(woman),
                         "because they are an owner now");

            dialog.filter = Dialog_AssignStandOwners.Filter.Male;
            ok &= Expect(assignable.AssignedPawnsForReading.Contains(woman),
                         "and a filter that excludes her does not hide her ownership");

            assignable.TryUnassignPawn(woman);
            return ok;
        }

        /// <summary>
        /// One owner list per stand, with another mod's assignable holding a copy of it. Outfit
        /// Stands Plus draws its "equip outfit" button for every stand whose list names the pawn,
        /// so a list nobody can see or maintain would send a colonist to somebody else's stand.
        /// Asserted through every way our list changes (assign, a second owner that no longer
        /// fits its one slot, unassign, the reaper) and in both modes, since a stand set to "Not
        /// used for shift changes" must also hide the foreign Set owner and show ours.
        /// </summary>
        internal static bool ForeignOwnersCopyOurs(Fixture fix)
        {
            CompAssignableToPawn_ShiftStand ours =
                fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
            CompAssignableToPawn foreign = ForeignAssignableOn(fix.Stand);
            if (ours == null || foreign == null)
            {
                return Expect(false, "the stand carries our assignable and a foreign one");
            }
            if (foreign.TotalSlots != 1)
            {
                // The second-owner assertions assume one slot, as Outfit Stands Plus has; a bigger
                // foreign comp is a different test.
                return Expect(false, "the foreign comp holds one owner, as Outfit Stands Plus's does (control)");
            }

            Pawn a = fix.Pawn;
            Pawn b = SpawnExtra(fix, Gender.Female, "CopyB");

            bool ok = Expect(foreign.AssignedPawnsForReading.Count == 0,
                             "no owners: the copy is empty (control)");

            ours.TryAssignPawn(a);
            ok &= Expect(Names(foreign, a), "one owner: the foreign list names exactly that owner");

            ours.TryAssignPawn(b);
            ok &= Expect(ours.AssignedPawnsForReading.Count == 2, "two owners are both ours (control)")
                & Expect(foreign.AssignedPawnsForReading.Count == 0,
                         "and they do not fit its one slot, so the copy is emptied, not truncated");

            ours.TryUnassignPawn(a);
            ok &= Expect(Names(foreign, b), "back to one owner: the copy names the one left");

            // The reaper unassigns through our comp, so the foreign list follows and a colonist
            // who died or left loses their button on the stand.
            Patch_UnclaimStands.ReapStandsFor(b);
            ok &= Expect(ours.AssignedPawnsForReading.Count == 0, "the reaper clears our list (control)")
                & Expect(foreign.AssignedPawnsForReading.Count == 0, "and the copy with it");

            IEnumerable<Gizmo> hidden = null;
            fix.Comp.SetAutomatic();
            ok &= Expect(!Patch_ForeignOwnerGizmos.Prefix(foreign, ref hidden),
                         "a stand in shift use hides the foreign Set owner");

            fix.Comp.SetExcluded();
            List<Command> ourGizmos = ours.CompGetGizmosExtra().OfType<Command>().ToList();
            ok &= Expect(!Patch_ForeignOwnerGizmos.Prefix(foreign, ref hidden),
                         "and so does a stand set to Not used for shift changes")
                & Expect(ourGizmos.Count == 1, "which shows ours in its place")
                & Expect(ourGizmos.Count == 1
                         && ourGizmos[0].defaultLabel == "CommandThingSetOwnerLabel".Translate(),
                         "labelled Set owner, since a stand in that mode pools nobody");

            ours.TryAssignPawn(a);
            ok &= Expect(Names(foreign, a), "and the copy works in that mode too");

            ours.TryUnassignPawn(a);
            fix.Comp.SetAutomatic();
            return ok;
        }

        /// <summary>
        /// The one-time reconcile for a stand from an older save, where the two owner lists were
        /// independent. Each arm stages the disagreement as such a save carries it (our list set,
        /// then the foreign one rewritten behind its back) and runs the
        /// <see cref="CompAssignableToPawn_ShiftStand.UnifyOwners"/> a spawn does.
        ///
        /// <para>The rule: if only one list has owners it becomes the list; if both do and they
        /// disagree, the one the player could see wins, ours on a stand in shift use and theirs
        /// on a stand set to "Not used for shift changes". The last arm is what makes it
        /// one-time: once reconciled, a foreign list that differs is overwritten, never
        /// adopted.</para>
        /// </summary>
        internal static bool ReconcileKeepsTheVisibleList(Fixture fix)
        {
            CompAssignableToPawn_ShiftStand ours =
                fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
            CompAssignableToPawn foreign = ForeignAssignableOn(fix.Stand);
            if (ours == null || foreign == null)
            {
                return Expect(false, "the stand carries our assignable and a foreign one");
            }
            if (foreign.TotalSlots != 1)
            {
                return Expect(false, "the foreign comp holds one owner, as Outfit Stands Plus's does (control)");
            }

            Pawn a = fix.Pawn;
            Pawn b = SpawnExtra(fix, Gender.Female, "ReconcileB");

            // In shift use both lists name someone and disagree; ours is the one the player could
            // see.
            fix.Comp.SetAutomatic();
            ours.TryAssignPawn(a);
            Diverge(foreign, a, b);
            ours.ownersUnified = false;
            ours.UnifyOwners();
            bool ok = Expect(Names(ours, a) && Names(foreign, a),
                             "a stand in shift use keeps its own owner, and the stale foreign one is replaced")
                    & Expect(ours.ownersUnified, "and is marked reconciled");

            // Not used for shift changes: theirs is the one the player could see.
            fix.Comp.SetExcluded();
            Diverge(foreign, a, b);
            ours.ownersUnified = false;
            ours.UnifyOwners();
            ok &= Expect(Names(ours, b) && Names(foreign, b),
                         "a stand set to Not used for shift changes takes the foreign owner instead");

            // Only the foreign list names anyone. Adopted, so an earlier assignment is not lost.
            fix.Comp.SetAutomatic();
            ours.TryUnassignPawn(b);
            foreign.ForceAddPawn(a);
            ours.ownersUnified = false;
            ours.UnifyOwners();
            ok &= Expect(Names(ours, a), "an empty list adopts the only owner there is");

            // Only ours names anyone, and two of them: nothing to adopt, and nothing that fits the
            // copy.
            ours.TryAssignPawn(b);
            ours.ownersUnified = false;
            ours.UnifyOwners();
            ok &= Expect(ours.AssignedPawnsForReading.Count == 2
                         && foreign.AssignedPawnsForReading.Count == 0,
                         "two owners of ours stay, and the copy stays empty");

            // Once reconciled, the foreign list has no say.
            ours.TryUnassignPawn(b);
            Diverge(foreign, a, b);
            ours.UnifyOwners();
            ok &= Expect(Names(ours, a) && Names(foreign, a),
                         "after the reconcile, a copy that drifted is rewritten from ours, never adopted");

            ours.TryUnassignPawn(a);
            return ok;
        }

        /// <summary>
        /// A group stand taken out of shift use keeps its owners, and has them all when it goes
        /// back. A past build that cleared the list on the way out cost a configured group on an
        /// ordinary edit of the stand.
        ///
        /// <para>Driven through all four ways into that mode, since three are not the "Not used
        /// for shift changes" row: unticking the last work type, Recreation or Sleeping lands in
        /// the same state (<see cref="CompShiftStand.ToggleWork"/> and its two siblings fall
        /// through to <see cref="CompShiftStand.SetExcluded"/>). Each arm re-enters shift use
        /// through the matching tick, the control that it really left.</para>
        /// </summary>
        internal static bool GroupSurvivesLeavingShiftUse(Fixture fix)
        {
            CompAssignableToPawn_ShiftStand ours =
                fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (ours == null || doctor == null)
            {
                return Expect(false, "the stand carries our assignable comp and the Doctor work type resolves");
            }

            Pawn a = fix.Pawn;
            Pawn b = SpawnExtra(fix, Gender.Female, "GroupB");

            fix.Comp.SetAutomatic();
            ours.TryAssignPawn(a);
            ours.TryAssignPawn(b);
            bool ok = Expect(Names(ours, a, b), "a stand in shift use holds a group of two (control)");

            fix.Comp.SetExcluded();
            ok &= Expect(fix.Comp.IsExcluded && Names(ours, a, b),
                         "the Not used for shift changes row keeps both owners");

            // A stand out of shift use has no work types to seed from, so each trigger below
            // starts a fresh set.
            fix.Comp.ToggleWork(doctor);
            ok &= Expect(!fix.Comp.IsExcluded && fix.Comp.WorkTypes.Count == 1,
                         "ticking Doctor puts it back in shift use with one work type (control)");
            fix.Comp.ToggleWork(doctor);
            ok &= Expect(fix.Comp.IsExcluded && Names(ours, a, b),
                         "unticking that last work type keeps both owners");

            fix.Comp.ToggleRecreation();
            ok &= Expect(!fix.Comp.IsExcluded && fix.Comp.HandlesRecreation(),
                         "ticking Recreation puts it back in shift use (control)");
            fix.Comp.ToggleRecreation();
            ok &= Expect(fix.Comp.IsExcluded && Names(ours, a, b),
                         "unticking Recreation keeps both owners");

            fix.Comp.ToggleRest();
            ok &= Expect(!fix.Comp.IsExcluded && fix.Comp.HandlesRest(),
                         "ticking Sleeping puts it back in shift use (control)");
            fix.Comp.ToggleRest();
            ok &= Expect(fix.Comp.IsExcluded && Names(ours, a, b),
                         "unticking Sleeping keeps both owners");

            fix.Comp.SetAutomatic();
            ok &= Expect(Names(ours, a, b), "and back in shift use the group is whole")
                & Expect(fix.Comp.CanBeClaimedBy(a) && fix.Comp.CanBeClaimedBy(b),
                         "and the claim check lets either owner take it");

            ours.TryUnassignPawn(a);
            ours.TryUnassignPawn(b);
            return ok;
        }

        /// <summary>
        /// On a stand out of shift use the owner dialog's Assign is a pick: one colonist replaces
        /// the whole list, a kept group included, and the copy then names that colonist, so the
        /// other mod's button comes back.
        ///
        /// <para>Driven through <see cref="Dialog_AssignStandOwners.Assign"/>, the call a row's
        /// button makes, so the dialog's own choice between adding and replacing is under test.
        /// The shift-use arm is the control: the same call there must add, or a group stand could
        /// never be built.</para>
        /// </summary>
        internal static bool PickReplacesAKeptGroup(Fixture fix)
        {
            CompAssignableToPawn_ShiftStand ours =
                fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
            CompAssignableToPawn foreign = ForeignAssignableOn(fix.Stand);
            if (ours == null || foreign == null)
            {
                return Expect(false, "the stand carries our assignable and a foreign one");
            }
            if (foreign.TotalSlots != 1)
            {
                return Expect(false, "the foreign comp holds one owner, as Outfit Stands Plus's does (control)");
            }

            Pawn a = fix.Pawn;
            Pawn b = SpawnExtra(fix, Gender.Female, "PickB");
            Pawn c = SpawnExtra(fix, Gender.Male, "PickC");
            Dialog_AssignStandOwners dialog = new Dialog_AssignStandOwners(ours);

            fix.Comp.SetAutomatic();
            dialog.Assign(a);
            dialog.Assign(b);
            bool ok = Expect(!dialog.SingleOwner, "in shift use the dialog does not pick (control)")
                    & Expect(Names(ours, a, b), "so its Assign adds, and builds a group (control)")
                    & Expect(foreign.AssignedPawnsForReading.Count == 0,
                             "which fits no single slot, so the copy is empty (control)");

            fix.Comp.SetExcluded();
            ok &= Expect(dialog.SingleOwner, "out of shift use the dialog picks one colonist")
                & Expect(Names(ours, a, b), "and until it does, the group is kept (control)");

            dialog.Assign(c);
            ok &= Expect(Names(ours, c), "one pick replaces the whole kept group")
                & Expect(Names(foreign, c),
                         "and the copy names that colonist, so the other mod's button comes back");

            dialog.Assign(a);
            ok &= Expect(Names(ours, a) && Names(foreign, a), "a second pick replaces the first");

            fix.Comp.SetAutomatic();
            ok &= Expect(Names(ours, a),
                         "back in shift use the pick stands, and the group it replaced stays gone");

            ours.TryUnassignPawn(a);
            return ok;
        }

        /// <summary>
        /// A group kept on a stand out of shift use reaches nobody through the copy, and comes
        /// back whole when the stand is moved.
        ///
        /// <para>The copy half: a group fits Outfit Stands Plus's one slot no better out of shift
        /// use than in it, so that list stays empty. The move half: the base parks a minified
        /// stand's owners and puts them back on landing through <c>TryAssignPawn</c>, one at a
        /// time, so an override that replaced the list would bring the group back as its last
        /// member. The landing also runs
        /// <see cref="CompAssignableToPawn_ShiftStand.UnifyOwners"/>, the spawn step a load
        /// shares, which must not clear a group either. Minified and set down as
        /// <see cref="HarnessLifecycle.ReinstallKeepsConfiguration"/> does it.</para>
        /// </summary>
        internal static bool KeptGroupSurvivesAMove(Fixture fix)
        {
            CompAssignableToPawn_ShiftStand ours =
                fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
            CompAssignableToPawn foreign = ForeignAssignableOn(fix.Stand);
            if (ours == null || foreign == null)
            {
                return Expect(false, "the stand carries our assignable and a foreign one");
            }
            if (foreign.TotalSlots != 1)
            {
                return Expect(false, "the foreign comp holds one owner, as Outfit Stands Plus's does (control)");
            }

            Pawn a = fix.Pawn;
            Pawn b = SpawnExtra(fix, Gender.Female, "KeptB");

            fix.Comp.SetAutomatic();
            ours.TryAssignPawn(a);
            ours.TryAssignPawn(b);
            fix.Comp.SetExcluded();
            bool ok = Expect(Names(ours, a, b), "a group is kept on a stand out of shift use (control)")
                    & Expect(foreign.AssignedPawnsForReading.Count == 0,
                             "and fits no single slot, so the copy names nobody");

            Map map = fix.Map;
            IntVec3 from = fix.Stand.Position;
            IntVec3 to = new IntVec3(from.x, 0, from.z + 2);
            Rot4 rot = fix.Stand.Rotation;

            MinifiedThing box = fix.Stand.MakeMinified();
            if (box == null)
            {
                return ok & Expect(false, "the stand minified");
            }
            ok &= Expect(ours.AssignedPawnsForReading.Count == 0,
                         "boxed, the owners are parked off the list (control)");

            box.InnerThing = null;
            box.Destroy();
            GenSpawn.Spawn(fix.Stand, to, map, rot);

            ok &= Expect(fix.Stand.Spawned && fix.Comp.IsExcluded,
                         "the stand lands, still out of shift use (control)")
                & Expect(Names(ours, a, b), "with the whole group back, not its last member")
                & Expect(foreign.AssignedPawnsForReading.Count == 0, "and the copy still names nobody");

            fix.Comp.SetAutomatic();
            ok &= Expect(Names(ours, a, b), "back in shift use, the group serves again");

            ours.TryUnassignPawn(a);
            ours.TryUnassignPawn(b);
            return ok;
        }

        /// <summary>
        /// Rewrites the foreign list from <paramref name="from"/> to <paramref name="to"/> behind
        /// our comp's back: these are the foreign comp's own calls, which none of our overrides
        /// see, so no sync runs.
        /// </summary>
        internal static void Diverge(CompAssignableToPawn foreign, Pawn from, Pawn to)
        {
            foreign.ForceRemovePawn(from);
            foreign.ForceAddPawn(to);
        }

        /// <summary>
        /// The invariant the contents protection rests on: while a stand is in service, vanilla's
        /// <c>allowRemovingItems</c> is off.
        ///
        /// <para>Driven through every entry into service, not only a load, since the way to reach
        /// the flag is to exclude the stand, flip it and put the stand back, and that last step
        /// must not leave it on. An excluded stand is the control: it is ordinary vanilla
        /// furniture and the player owns its flag.</para>
        ///
        /// <para>The last two assertions pin the engine fact behind
        /// <see cref="CompShiftStand.EnforceRemovalFlag"/>: <c>Building_OutfitStand</c> returns
        /// this one field for both <c>IApparelSource.ApparelSourceEnabled</c> and
        /// <c>IHaulSource.HaulSourceEnabled</c>, so the optimizer's gate is the flag and no
        /// Harmony patch on it is needed. If a game update decouples them these fail, and the
        /// patch is back on the table.</para>
        /// </summary>
        internal static bool RemovalFlagHeldOffInService(Fixture fix)
        {
            AccessTools.FieldRef<Building_OutfitStand, bool> flag =
                Patch_AllowRemovingToggle.AllowRemovingItemsRef;
            if (flag == null)
            {
                // A failure here is not a stale test: the protection rides on this field, so a
                // rename silently removes it.
                return Expect(false, "allowRemovingItems still resolves");
            }
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (doctor == null)
            {
                return Expect(false, "the Doctor work type resolves");
            }

            fix.Comp.SetExcluded();
            flag(fix.Stand) = true;
            bool ok = Expect(flag(fix.Stand),
                             "an excluded stand keeps the flag the player set (control)");

            fix.Comp.SetAutomatic();
            ok &= Expect(!flag(fix.Stand), "going automatic clears it");

            fix.Comp.SetExcluded();
            flag(fix.Stand) = true;
            fix.Comp.ToggleWork(doctor);
            ok &= Expect(!flag(fix.Stand), "declaring a work type clears it");

            fix.Comp.SetExcluded();
            flag(fix.Stand) = true;
            fix.Comp.ToggleRecreation();
            ok &= Expect(!flag(fix.Stand), "declaring recreation clears it")
                & Expect(!fix.Comp.IsExcluded, "and the stand really is in service (control)");

            return ok
                & Expect(!((IApparelSource)fix.Stand).ApparelSourceEnabled,
                         "so the optimizer's gate is shut by construction")
                & Expect(!((IHaulSource)fix.Stand).HaulSourceEnabled,
                         "and the stand is not a haul source while in service");
        }

        // --------------------------------------------------- the gizmo gates
        //
        // Both gizmo postfixes hand the upstream sequence back untouched when they have nothing
        // to add, instead of wrapping it in an iterator (why: Patch_ChangeBackGizmo.Postfix). The
        // gizmo bar is rebuilt every rendered frame for every selected object, so a wrapper would
        // sit around every gizmo handed to it to answer a question that is usually no.
        //
        // The bar a player sees is the same either way, so a case that only checks which gizmos
        // come out passes on a wrapper too. Only the reference identity of the returned sequence
        // tells the two apart: a gate returns the very object it was given. That is the assertion
        // doing the work below; the rest guard the behaviour around it.

        /// <summary>A pawn on no stand gets the upstream gizmo sequence back itself.</summary>
        internal static bool ChangeBackGateHandsTheChainBack(Fixture fix)
        {
            Pawn bystander = SpawnExtra(fix, Gender.Female, "Bystander");
            List<Gizmo> chain = Sentinels();
            IEnumerable<Gizmo> passed = Patch_ChangeBackGizmo.Postfix(chain, bystander);

            bool ok = Expect(CompShiftStand.OnShiftStandFor(bystander) == null,
                             "the bystander is on no stand (control)")
                    & Expect(ReferenceEquals(passed, chain),
                             "so the gate hands back the SAME sequence, not a wrapper around it")
                    & Expect(SameSequence(passed, chain),
                             "with every upstream gizmo still in it, in order");

            // Harmony never hands us a null pawn, but the gate guards against one, and dropping
            // that guard should fail here rather than throw in somebody's colony.
            ok &= Expect(ReferenceEquals(Patch_ChangeBackGizmo.Postfix(chain, null), chain),
                         "and a null pawn is handed back untouched too");
            return ok;
        }

        /// <summary>For a pawn in uniform the gate appends the Change back button, last.</summary>
        internal static bool ChangeBackGateStillAppends(Fixture fix)
        {
            List<Gizmo> chain = Sentinels();
            List<Gizmo> got = Patch_ChangeBackGizmo.Postfix(chain, fix.Pawn).ToList();

            bool ok = Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                             "the fixture pawn is in uniform (control)")
                    & Expect(got.Count == chain.Count + 1,
                             "so the chain comes back with exactly one gizmo added")
                    & Expect(SameSequence(got.Take(chain.Count), chain),
                             "the upstream gizmos are untouched, and still in order")
                    & Expect(got.Count > chain.Count
                             && got[got.Count - 1] is Command_Action button
                             && button.groupKey == Patch_ChangeBackGizmo.GroupKey,
                             "and the added one is ours, appended LAST");

            // Interception going off, by the standing switch or the fault latch, stops the mod
            // acting on its own and must not also remove the player's manual way out, or every pawn
            // already in uniform is stranded. Each way is asserted alone, so a gate on either one
            // fails here.
            bool armedBefore = Patch_JobInterception.Enabled;
            bool faultedBefore = Patch_JobInterception.faulted;
            try
            {
                Patch_JobInterception.Enabled = false;
                ok &= Expect(
                    Patch_ChangeBackGizmo.Postfix(chain, fix.Pawn).Count() == chain.Count + 1,
                    "and it is STILL offered with interception switched off");
                Patch_JobInterception.Enabled = armedBefore;
                Patch_JobInterception.faulted = true;
                ok &= Expect(
                    Patch_ChangeBackGizmo.Postfix(chain, fix.Pawn).Count() == chain.Count + 1,
                    "and with interception latched off by its faults");
            }
            finally
            {
                Patch_JobInterception.Enabled = armedBefore;
                Patch_JobInterception.faulted = faultedBefore;
            }

            // A stand off the map with its ledger kept, after a takeoff's WillReplace despawn or a
            // minify's Vanish, still answers the registry for this pawn; the takeoff's mode
            // stands in for both. The gate's Spawned test is all that stops the button pointing at
            // a stand that is not on the map.
            IntVec3 cell = fix.Stand.Position;
            fix.Stand.DeSpawn(DestroyMode.WillReplace);
            ok &= Expect(ReferenceEquals(Patch_ChangeBackGizmo.Postfix(chain, fix.Pawn), chain),
                         "while a stand that is boxed rather than spawned gets no button at all");
            GenSpawn.Spawn(fix.Stand, cell, fix.Map, Rot4.North);
            fix.Stand.PostSwapMap();
            return ok;
        }

        /// <summary>The gate rewrites only vanilla's removal toggle, and only in service.</summary>
        internal static bool RemovalToggleGateLeavesForeignStandsAlone(Fixture fix)
        {
            string vanillaLabel = "CommandAllowRemovingApparel".Translate().ToString();
            string ours = "ShiftChange.AllowRemovingDesc".Translate().RawText;
            const string untouched = "vanilla's own description, verbatim";

            fix.Comp.SetExcluded();
            Command_Toggle foreign = RemovalToggle(vanillaLabel, active: false, desc: untouched);
            List<Gizmo> chain = new List<Gizmo> { Sentinels()[0], foreign };
            IEnumerable<Gizmo> passed = Patch_AllowRemovingToggle.Postfix(chain, fix.Stand);

            bool ok = Expect(fix.Comp.IsExcluded, "the stand is declared not-ours (control)")
                    & Expect(ReferenceEquals(passed, chain),
                             "so the gate hands back the SAME sequence, not a wrapper around it")
                    & Expect(foreign.defaultDesc == untouched,
                             "and vanilla's tooltip is left exactly as vanilla wrote it")
                    & Expect(!foreign.Disabled, "with the toggle still live");

            fix.Comp.SetAutomatic();
            Command_Toggle governed = RemovalToggle(vanillaLabel, active: false, desc: untouched);
            Command_Toggle alreadyOn = RemovalToggle(vanillaLabel, active: true, desc: untouched);
            // A toggle that is not vanilla's. The patch finds vanilla's by its label, the command
            // being an anonymous Command_Toggle with no other handle, so a renamed label key makes
            // it match nothing and the stand fall back to vanilla. This arm is that fallback.
            Command_Toggle stranger = RemovalToggle("Another mod's toggle", active: false,
                                                    desc: untouched);
            List<Gizmo> governedChain = new List<Gizmo> { governed, alreadyOn, stranger };
            List<Gizmo> got = Patch_AllowRemovingToggle.Postfix(governedChain, fix.Stand).ToList();

            return ok
                & Expect(!fix.Comp.IsExcluded, "the stand is back in service (control)")
                & Expect(SameSequence(got, governedChain),
                         "every gizmo still comes through, in order")
                & Expect(governed.defaultDesc != untouched && governed.defaultDesc.Contains(ours),
                         "the tooltip now carries our paragraph as well as vanilla's")
                // TaggedString's implicit conversion to string strips tags, so the patch must use
                // .RawText on both halves of the tooltip: assigning a Translate() result straight
                // to the field silently deletes the colour and bold. This assertion guards that.
                & Expect(ours.Contains("<color") && governed.defaultDesc.Contains("<color"),
                         "with its rich-text markup intact, not stripped by TaggedString")
                & Expect(governed.Disabled,
                         "an OFF toggle is disabled while the stand is in service")
                & Expect(!alreadyOn.Disabled,
                         "while one already ON keeps its way back out")
                & Expect(stranger.defaultDesc == untouched && !stranger.Disabled,
                         "and a toggle that is not vanilla's passes through untouched");
        }
    }
}
#endif
