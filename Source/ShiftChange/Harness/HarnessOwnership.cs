// HARNESS only — see the configuration table in ShiftChange.csproj. The
// harness is dev tooling and does not ship: a Release build compiles this
// file out entirely, and devtools/run-harness.sh asks for it back with
// -p:Harness=true on top of Release codegen.
//
// The guard is whole-file, always. Never put an #if HARNESS inside a file
// that ships — a shipping build and a harness build must differ by the
// presence of these types and by nothing else, or a harness run stops saying
// anything about the assembly that goes out. check-invariants.py enforces it.
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
    /// filter, the removal flag held off in service, and the two gizmo-chain
    /// gates that must hand an untouched sequence back.
    ///
    /// <para>Split out of <see cref="DebugTools_LifecycleHarness"/> on
    /// 2026-09-15. These are separate TYPES rather than partials on purpose: a
    /// decompiler merges partials back into one class, so partials would have
    /// left the shipped dll reading exactly as it did before. The registration
    /// list that decides case ORDER stays in
    /// <see cref="DebugTools_LifecycleHarness.Run"/> and must not be
    /// scattered.</para>
    /// </summary>
    internal static class HarnessOwnership
    {
        /// <summary>
        /// Work and recreation are MUTUALLY EXCLUSIVE on one stand (decided
        /// 2026-08-16): it holds one outfit, and one outfit serves one purpose.
        ///
        /// <para>Asserted in both directions, because the rule is enforced by
        /// two separate methods that each clear the other's half — and a
        /// half-applied version of it leaves a stand claiming both, which the
        /// dialog then cannot render honestly (it hides the work grid while
        /// recreation is on).</para>
        /// </summary>
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
