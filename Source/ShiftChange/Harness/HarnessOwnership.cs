// HARNESS only: dev tooling, compiled out of a plain Release build (the
// configuration table is in ShiftChange.csproj). The guard is whole-file,
// always, and check-invariants.py enforces it; why: docs/DESIGN.md,
// "Development tooling".
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
    /// Harness cases for who a stand serves: owner lists, the owner dialog's
    /// filter, the copy of the owner list in another mod's assignable and the
    /// one-time reconcile that set it up, a stand not used for shift changes
    /// (one colonist picked, a group from shift use kept), the removal flag
    /// held off in service, and the two gizmo-chain gates that must hand an
    /// untouched sequence back.
    ///
    /// <para>Case ORDER is decided in one place,
    /// <see cref="DebugTools_LifecycleHarness.Run"/>.</para>
    /// </summary>
    internal static class HarnessOwnership
    {
        /// <summary>
        /// Ownership is a SET. The single-owner model this replaced would pass
        /// the first two assertions and fail every one after them, which is the
        /// point of the case: the danger in going from one owner to many is
        /// that a reader somewhere still asks "is THE owner this pawn" and
        /// silently answers no for everyone but the first.
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
                // The one that a single-owner reader gets wrong.
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
        /// The dialog's candidate list. Two claims worth pinning: the filter
        /// narrows by gender, and it never hides a pawn who is already an
        /// owner — a filter that hid the owner you wanted to remove would be a
        /// trap rather than a shortcut.
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

            // An assigned pawn leaves the CANDIDATE list — they are drawn in
            // the assigned section above it instead, whatever the filter says.
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
        /// One owner list per stand, and another mod's assignable holds a copy.
        ///
        /// <para>The copy is what keeps that mod's per-pawn button honest.
        /// Outfit Stands Plus draws its "equip outfit" button for every stand
        /// whose list names the pawn, so a list nobody can see or maintain is a
        /// button that walks a colonist to somebody else's stand. Asserted
        /// through every way our list changes (assign, a second owner that no
        /// longer fits its one slot, unassign, the reaper) and in both modes,
        /// because the foreign Set owner used to come back on a stand set to
        /// "Not used for shift changes".</para>
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
                // Every assertion about a second owner assumes one slot, which
                // is what Outfit Stands Plus has. A mod list that supplies a
                // bigger comp is a different test and should say so.
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

            // The reaper goes through our unassign. Nothing ever reaped the
            // foreign list before the copy, so a colonist who died or left
            // kept their button on the stand.
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
        /// The one-time reconcile for a stand from a save that predates the
        /// copy, when the two lists were independent. Each arm stages the
        /// disagreement the way such a save carries it (our list set, then the
        /// foreign one rewritten behind its back) and runs the same
        /// <see cref="CompAssignableToPawn_ShiftStand.UnifyOwners"/> a spawn
        /// does.
        ///
        /// <para>The rule: if only one list has owners it becomes THE list; if
        /// both do and they disagree, the one the player could see wins, ours
        /// on a stand in shift use and theirs on a stand set to "Not used for
        /// shift changes". The last arm is what makes it one-time: once
        /// reconciled, a foreign list that differs is overwritten, never
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

            // In shift use, both lists name someone and they disagree. Ours was
            // the control the player could see.
            fix.Comp.SetAutomatic();
            ours.TryAssignPawn(a);
            Diverge(foreign, a, b);
            ours.ownersUnified = false;
            ours.UnifyOwners();
            bool ok = Expect(Names(ours, a) && Names(foreign, a),
                             "a stand in shift use keeps its own owner, and the stale foreign one is replaced")
                    & Expect(ours.ownersUnified, "and is marked reconciled");

            // Not used for shift changes: theirs was the visible control.
            fix.Comp.SetExcluded();
            Diverge(foreign, a, b);
            ours.ownersUnified = false;
            ours.UnifyOwners();
            ok &= Expect(Names(ours, b) && Names(foreign, b),
                         "a stand set to Not used for shift changes takes the foreign owner instead");

            // Only the foreign list names anyone. Adopted, so an assignment made
            // before this mod arrived is not lost.
            fix.Comp.SetAutomatic();
            ours.TryUnassignPawn(b);
            foreign.ForceAddPawn(a);
            ours.ownersUnified = false;
            ours.UnifyOwners();
            ok &= Expect(Names(ours, a), "an empty list adopts the only owner there is");

            // Only ours names anyone, and two of them: nothing to adopt, and
            // nothing that fits the copy.
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
        /// A group stand taken out of shift use keeps its owners, and has them
        /// all when it goes back.
        ///
        /// <para>Driven through all four ways into that mode, because three of
        /// them are not the "Not used for shift changes" row: unticking the
        /// last work type, Recreation or Sleeping lands in the same state
        /// (<see cref="CompShiftStand.ToggleWork"/> and its two siblings fall
        /// through to <see cref="CompShiftStand.SetExcluded"/>). A build that
        /// cleared the list on the way in cost a configured group on an
        /// ordinary edit of the stand, so the list is kept (decided
        /// 2026-09-27). Each arm re-enters shift use through the matching
        /// tick, which is the control that the arm really left it.</para>
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

            // From here each trigger starts a fresh set, since a stand out of
            // shift use has no work types to seed one from.
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
        /// On a stand out of shift use the owner dialog's Assign is a pick: one
        /// colonist replaces the whole list, a kept group included, and the
        /// copy then names that colonist, so the other mod's button comes back.
        ///
        /// <para>Driven through <see cref="Dialog_AssignStandOwners.Assign"/>,
        /// the call a row's button makes, so the dialog's own choice between
        /// adding and replacing is under test and not only the comp method
        /// behind it. The shift-use arm is the control: the same call there has
        /// to ADD, or a group stand could never be built.</para>
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
        /// A group kept on a stand out of shift use reaches nobody through the
        /// copy, and comes back whole when the stand is moved.
        ///
        /// <para>The copy half: the group fits Outfit Stands Plus's one slot no
        /// better out of shift use than in it, so that list stays empty and none
        /// of the group gets that mod's button for this stand.</para>
        ///
        /// <para>The move half is the trap this case exists for. The base parks
        /// a minified stand's owners and puts them back on landing through
        /// <c>TryAssignPawn</c>, one at a time
        /// (<c>CompAssignableToPawn.cs:197-220</c>), so an override that
        /// replaced there returned the group as its last member. The landing
        /// also runs <see cref="CompAssignableToPawn_ShiftStand.UnifyOwners"/>,
        /// the spawn step a load shares, which is where an earlier build of this
        /// rule cleared a group. Minified and relanded the way
        /// <see cref="HarnessLifecycle.ReinstallKeepsConfiguration"/> does
        /// it.</para>
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
        /// Rewrites the foreign list from <paramref name="from"/> to
        /// <paramref name="to"/> behind our comp's back. These are the base
        /// calls on the foreign comp, which no override of ours sees, so no
        /// sync runs.
        /// </summary>
        internal static void Diverge(CompAssignableToPawn foreign, Pawn from, Pawn to)
        {
            foreign.ForceRemovePawn(from);
            foreign.ForceAddPawn(to);
        }

        /// <summary>
        /// The invariant the contents protection rests on: while a stand is in
        /// service, vanilla's <c>allowRemovingItems</c> is off.
        ///
        /// <para>Driven through EVERY entry into service, not just a load,
        /// because the documented way to reach the flag is to exclude the
        /// stand, flip it, and put the stand back — and that last step is what
        /// used to leave a live window. An excluded stand is the control: it
        /// is ordinary vanilla furniture and the player owns its flag.</para>
        ///
        /// <para>The last two assertions are the tripwire for
        /// <see cref="CompShiftStand.EnforceRemovalFlag"/>'s claim that no
        /// Harmony patch on <c>ApparelSourceEnabled</c> is needed. They pin the
        /// engine fact the claim rests on — that the optimizer's gate IS this
        /// field (<c>Building_OutfitStand.cs:104</c>). If Ludeon ever decouples
        /// them these fail, and the patch goes back on the table.</para>
        /// </summary>
        internal static bool RemovalFlagHeldOffInService(Fixture fix)
        {
            AccessTools.FieldRef<Building_OutfitStand, bool> flag =
                Patch_AllowRemovingToggle.AllowRemovingItemsRef;
            if (flag == null)
            {
                // Not a stale test — the whole guard rides on this field, so a
                // rename means the protection is silently gone.
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
        // Both gizmo postfixes hand the upstream sequence back UNTOUCHED when
        // they have nothing to add, rather than wrapping it in an iterator
        // that re-yields every gizmo. `GizmoGridDrawer` rebuilds the
        // bar once per rendered frame for every selected object, and ours is
        // the LAST postfix on `Pawn.GetGizmos`, so a wrapper there sits around
        // every other mod's gizmos for every selected pawn to answer a
        // question that is usually "no".
        //
        // THE CHANGE IS INVISIBLE FROM THE OUTSIDE. The bar a player sees is
        // identical either way, so a case that only checks which gizmos come
        // out passes just as happily on a wrapper. What tells the two apart is
        // REFERENCE IDENTITY of the returned sequence: a gate returns the very
        // object it was given, and nothing else can. That is the assertion
        // doing the work below; the rest guard the behaviour around it.

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

            // The defensive arm. Harmony will not hand us a null pawn, but the
            // guard is written and a refactor that drops it should fail here
            // rather than throw in somebody's colony.
            ok &= Expect(ReferenceEquals(Patch_ChangeBackGizmo.Postfix(chain, null), chain),
                         "and a null pawn is handed back untouched too");
            return ok;
        }

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

            // THE INVARIANT THAT HAD A COMMENT AND NO TEST. The fault latch
            // stops the mod ACTING on its own; it must not also take away the
            // player's manual way out, because a latched switch is exactly the
            // moment every pawn already in uniform is stranded there.
            bool armedBefore = Patch_JobInterception.Enabled;
            try
            {
                Patch_JobInterception.Enabled = false;
                ok &= Expect(
                    Patch_ChangeBackGizmo.Postfix(chain, fix.Pawn).Count() == chain.Count + 1,
                    "and it is STILL offered once interception has latched off");
            }
            finally
            {
                Patch_JobInterception.Enabled = armedBefore;
            }

            // A boxed stand keeps its ledger — that is the reinstall case's
            // whole point — so the registry still answers for this pawn. The
            // Spawned half of the guard is the only thing between that and a
            // button pointing at a stand inside a crate.
            IntVec3 cell = fix.Stand.Position;
            fix.Stand.DeSpawn(DestroyMode.WillReplace);
            ok &= Expect(ReferenceEquals(Patch_ChangeBackGizmo.Postfix(chain, fix.Pawn), chain),
                         "while a stand that is boxed rather than spawned gets no button at all");
            GenSpawn.Spawn(fix.Stand, cell, fix.Map, Rot4.North);
            fix.Stand.PostSwapMap();
            return ok;
        }

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
            // A toggle that is not vanilla's. The patch matches by LABEL,
            // because the command is anonymous and there is no other handle —
            // so if Ludeon renames the key the match stops finding it and the
            // whole patch reverts to vanilla behaviour. This arm is that
            // graceful failure, written down.
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
                // .RawText on both halves is load-bearing: TaggedString's
                // implicit conversion to string calls StripTags, so assigning a
                // Translate() result straight into the field silently deletes
                // the markup. It ate the colour and bold on this very tooltip
                // once, on 2026-08-17, with no error and nothing on screen.
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
