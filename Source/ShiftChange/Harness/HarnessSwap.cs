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
    /// Harness cases for the swap itself — the driver's round trip, full
    /// change, and the four decency cases that decide what a stand may take.
    ///
    /// <para>Split out of <see cref="DebugTools_LifecycleHarness"/> on
    /// 2026-09-15. These are separate TYPES rather than partials on purpose: a
    /// decompiler merges partials back into one class, so partials would have
    /// left the shipped dll reading exactly as it did before. The registration
    /// list that decides case ORDER stays in
    /// <see cref="DebugTools_LifecycleHarness.Run"/> and must not be
    /// scattered.</para>
    /// </summary>
    internal static class HarnessSwap
    {
        /// <summary>
        /// B1'S REGRESSION GUARD, and the only one.
        ///
        /// A stand whose stock displaces nothing — a Shell-layer duster over
        /// shirt and trousers, which share no layer with it — used to donate
        /// its uniform permanently: <c>DoTransfer</c> gated the whole return
        /// trip on <c>toWear.Count == 0</c>, so the pawn walked away still
        /// wearing it and the stand emptied itself forever. Silent, and
        /// cumulative.
        ///
        /// Both legs go through the real driver on the real tracker, because
        /// the bug lives past a job boundary that the hand-assembled
        /// <see cref="Build"/> fixture never crosses.
        ///
        /// With the fix reverted, the ledger and forced-flag assertions still
        /// pass — <c>NothingToWear</c> runs <c>AbandonLedger</c>, which tidies
        /// both. The three that fail are the ones that matter: the uniform is
        /// off the pawn, it is back in the stand, and the stand can dress
        /// somebody again.
        /// </summary>
        internal static bool NonDisplacingReturns(Fixture fix)
        {
            bool ok = Expect(RunSwap(fix), "dress leg ran to completion");
            Apparel uniform = fix.Comp.IssuedUniformForReading.Count > 0
                ? fix.Comp.IssuedUniformForReading[0]
                : null;
            ok &= Expect(uniform != null, "the duster was issued")
                & Expect(fix.Comp.StoredOwnerApparelForReading.Count == 0,
                         "and displaced nothing, which is the whole point");
            if (uniform == null)
            {
                return false;
            }

            ok &= Expect(RunSwap(fix), "return leg ran to completion");
            return ok
                & Expect(!fix.Pawn.apparel.WornApparel.Contains(uniform),
                         "the uniform came off")
                & Expect(uniform.ParentHolder == fix.Stand,
                         "and went back into the stand")
                & Expect(fix.Pawn.apparel.WornApparel.Count == 2,
                         "the pawn kept their own clothes")
                & Expect(!fix.Comp.OnShift, "ledger cleared")
                & Expect(SwapPlan.WouldDress(fix.Pawn, fix.Stand),
                         "the stand can dress somebody again");
        }

        /// <summary>
        /// The driver builds a correct ledger, and gives everything back.
        ///
        /// The forced-flag half is what nothing else covers:
        /// <c>Pawn_ApparelTracker.Notify_ApparelRemoved</c> clears the forced
        /// flag on every removal, so the driver captures it BEFORE removing and
        /// restores it on the way back. <see cref="Build"/> hands
        /// <c>NotifyDressed</c> an empty forced list, so that path has been at
        /// zero coverage.
        /// </summary>
        internal static bool DriverRoundTrip(Fixture fix)
        {
            Apparel parka = null;
            List<Apparel> worn = fix.Pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i].def.defName == "Apparel_Parka")
                {
                    parka = worn[i];
                }
            }
            if (parka == null)
            {
                return Expect(false, "fixture is wearing a parka to displace");
            }
            // The player's explicit choice, which the swap must not quietly
            // downgrade to policy-managed.
            fix.Pawn.outfits.forcedHandler.SetForced(parka, forced: true);

            bool ok = Expect(RunSwap(fix), "dress leg ran to completion")
                    & Expect(fix.Comp.Borrower == fix.Pawn, "borrower recorded")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                             "registry points at the stand")
                    & Expect(parka.ParentHolder == fix.Stand, "the parka was parked")
                    & Expect(fix.Comp.StoredOwnerApparelForReading.Contains(parka),
                             "and recorded in the ledger")
                    & Expect(fix.Comp.WasForcedWhenStored(parka),
                             "its force-worn flag was captured before removal");

            ok &= Expect(RunSwap(fix), "return leg ran to completion");
            return ok
                & Expect(fix.Pawn.apparel.WornApparel.Contains(parka),
                         "the parka came back on")
                & Expect(fix.Pawn.outfits.forcedHandler.IsForced(parka),
                         "still force-worn — the player's choice survived the shift")
                & Expect(!fix.Comp.OnShift, "ledger cleared")
                & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == null,
                         "registry entry cleared");
        }

        /// <summary>
        /// Full change: the pawn ends up wearing the stand's kit and nothing
        /// else, and gets every garment back on the return trip.
        ///
        /// <para>The `Displacing` fixture is what makes this assert anything.
        /// Its pawn wears a shirt and trousers (OnSkin) under a parka (Shell)
        /// and the stand holds a duster (Shell), so the ordinary swap displaces
        /// the parka ALONE. Anything the shirt and trousers do here is
        /// therefore attributable to the flag and to nothing else.</para>
        ///
        /// <para>Also pins the gate the flag rides on: an empty stand must
        /// still refuse. That assertion is a control as much as a rule — it
        /// fails loudly if the full-change pass is ever moved above the
        /// <c>toWear.Count > 0</c> test in <see cref="SwapPlan.BuildDress"/>.</para>
        /// </summary>
        internal static bool FullChangeSwapsEverything(Fixture fix)
        {
            List<Apparel> before = new List<Apparel>(fix.Pawn.apparel.WornApparel);
            if (before.Count < 3)
            {
                return Expect(false, "fixture is wearing shirt, trousers and a parka");
            }
            fix.Comp.SetFullChange(true);

            bool ok = Expect(RunSwap(fix), "dress leg ran to completion")
                    & Expect(fix.Comp.OnShift, "the stand went on shift (control)");

            // The point of the whole feature: nothing of their own is left on.
            List<Apparel> nowWorn = fix.Pawn.apparel.WornApparel;
            int ownStillWorn = 0;
            for (int i = 0; i < before.Count; i++)
            {
                if (nowWorn.Contains(before[i]))
                {
                    ownStillWorn++;
                }
            }
            ok &= Expect(ownStillWorn == 0, "every garment they arrived in came off")
                & Expect(fix.Comp.StoredOwnerApparelForReading.Count == before.Count,
                         "and all " + before.Count + " are in the ledger")
                & Expect(nowWorn.Count > 0, "they are not standing there naked");

            // The return trip is ledger-driven, so it must restore all of it
            // without consulting the flag — turn the flag off first and it
            // still has to hand everything back.
            fix.Comp.SetFullChange(false);
            ok &= Expect(RunSwap(fix), "return leg ran to completion");

            int returned = 0;
            for (int i = 0; i < before.Count; i++)
            {
                if (fix.Pawn.apparel.WornApparel.Contains(before[i]))
                {
                    returned++;
                }
            }
            ok &= Expect(returned == before.Count,
                         "all " + before.Count + " came back on, flag off or not")
                & Expect(!fix.Comp.OnShift, "ledger cleared");

            // The gate: full change on an empty stand still plans nothing.
            fix.Comp.SetFullChange(true);
            while (fix.Stand.HeldItems.Count > 0)
            {
                Thing held = fix.Stand.HeldItems[0];
                if (!fix.Stand.RemoveApparel(held as Apparel))
                {
                    break;
                }
                held.Destroy();
            }
            return ok
                & Expect(!SwapPlan.WouldDress(fix.Pawn, fix.Stand),
                         "an empty stand still refuses, full change or not");
        }

        /// <summary>
        /// Full change must not strip a colonist bare. Today it does.
        ///
        /// <para>The deposit path refuses a plan that would leave the pawn
        /// psychologically nude (<see cref="SwapPlan.WouldBeNude"/>,
        /// <c>SwapPlan.cs:278</c>). The DRESS path never asks: the driver
        /// consults a decency predicate only inside
        /// <c>if (toWear.Count == 0 ...)</c>
        /// (<c>JobDriver_SwapAtStand.cs:233</c>), so the moment a stand issues
        /// anything at all the question stops being asked — and full change is
        /// precisely the flag that then takes everything else off.</para>
        ///
        /// <para><b>The garment is deliberately the one the deposit-only case
        /// already uses for its utility-layer trap.</b> A shield belt covers
        /// <c>Waist</c>, and neither <c>Torso</c> nor <c>Legs</c>, so it cannot
        /// dress anybody. <see cref="DepositOnlyParksAndReturns"/> asserts that
        /// a stand must REFUSE to leave a colonist in it. This case asserts
        /// what happens when the same colonist is walked across the room and
        /// put into it on purpose. Same mod, same garment, opposite answers —
        /// which is what makes this a bug rather than a preference.</para>
        ///
        /// <para>The verdict comes from vanilla's own
        /// <c>Pawn_ApparelTracker.PsychologicallyNude</c>, read off a real
        /// spawned pawn, rather than from our transcription of it. The
        /// transcription is asserted separately as a control; if the two ever
        /// disagree it is ours that is wrong, and this case says so first.</para>
        ///
        /// <para><b>These assertions were known gaps until the retention pass
        /// landed</b> — the case was written first, confirmed the defect, and
        /// then became its regression test unchanged in shape. What it asserts
        /// now is the fix's actual contract: hold back the innermost garments
        /// decency needs, deposit the rest, and never decline the swap when
        /// holding back would do.</para>
        /// </summary>
        internal static bool FullChangeRefusesToStripThemBare(Fixture fix)
        {
            // The guard ships OFF (opt-in, decided 2026-09-08), so a case
            // about what it does has to turn it on. Case() restores it.
            if (ShiftChangeMod.Settings == null)
            {
                return Expect(false, "mod settings resolve");
            }
            ShiftChangeMod.Settings.keepColonistsDecent = true;

            List<Apparel> before = new List<Apparel>(fix.Pawn.apparel.WornApparel);
            if (before.Count < 3)
            {
                return Expect(false, "fixture is wearing shirt, trousers and a parka");
            }
            ThingDef parkaDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Parka");
            Apparel parka = before.Find(a => a.def == parkaDef);
            if (parka == null)
            {
                return Expect(false, "the fixture's parka resolves — it is the garment the "
                                     + "retention pass must choose to give up");
            }

            // Clear the duster the fixture stocks and leave exactly one garment
            // on the stand — one covering no group the decency test counts. It
            // covers Waist; "covers nothing" is the loose phrasing that put the
            // wrong premise into SwapPlan.cs:266-270 in the first place.
            ClearStand(fix.Stand);
            if (!StockOne(fix.Stand, "Apparel_ShieldBelt"))
            {
                return Expect(false, "a shield belt can be stocked for this case");
            }

            bool ok = Expect(fix.Stand.HeldItems.Count == 1,
                             "the stand holds one garment, covering neither torso nor legs (control)")
                & Expect(!fix.Pawn.apparel.PsychologicallyNude,
                         "the colonist starts out dressed, by vanilla's reckoning (control)")
                & Expect(SwapPlan.WouldBeNude(fix.Pawn, before),
                         "and losing all three would read as nude to us too (control)");

            fix.Comp.SetFullChange(true);

            List<Apparel> wear = new List<Apparel>();
            List<Apparel> store = new List<Apparel>();
            bool planned = SwapPlan.BuildDress(fix.Pawn, fix.Stand, wear, store);
            ok &= Expect(planned && wear.Count == 1,
                         "the stand issues the belt, so toWear is non-empty — which is the "
                         + "condition that switches the decency question off");

            ok &= Expect(!SwapPlan.WouldBeNude(fix.Pawn, store, wear),
                         "and the plan it builds still leaves them dressed");

            // WHICH garments were held back is the design, not an accident.
            // The retention pass ranks candidates by innermost layer, so the
            // two OnSkin garments stay on and the Shell parka is what goes to
            // the stand. Asserting the parka by name is what stops a future
            // "keep the first one that works" from passing this case.
            int heldBack = 0;
            for (int i = 0; i < before.Count; i++)
            {
                if (before[i] != parka && !store.Contains(before[i]))
                {
                    heldBack++;
                }
            }
            ok &= Expect(heldBack == 2,
                         "both OnSkin garments — the shirt and the trousers — were held back")
                & Expect(store.Count == 1 && store[0].def == parkaDef,
                         "and the outermost garment, the parka, is the one that goes");

            ok &= Expect(RunSwap(fix), "dress leg ran to completion");
            ok &= Expect(!fix.Pawn.apparel.PsychologicallyNude,
                         "and the colonist is still dressed afterwards, by vanilla's reckoning");

            ok &= Expect(fix.Comp.StoredOwnerApparelForReading.Count == 1,
                         "the ledger holds only what actually came off")
                & Expect(fix.Pawn.apparel.WornApparel.Count == 3,
                         "and they are wearing the belt over their own shirt and trousers");

            fix.Comp.SetFullChange(false);
            ok &= Expect(RunSwap(fix), "return leg ran to completion");

            int returned = 0;
            for (int i = 0; i < before.Count; i++)
            {
                if (fix.Pawn.apparel.WornApparel.Contains(before[i]))
                {
                    returned++;
                }
            }
            return ok
                & Expect(returned == before.Count, "and every garment came back on")
                & Expect(!fix.Pawn.apparel.PsychologicallyNude,
                         "leaving them dressed again");
        }

        /// <summary>
        /// A stand holding trousers and nothing else, worn by a man. He ends up
        /// in trousers, which vanilla is perfectly happy with — so the swap is
        /// legitimate and this case PASSES.
        ///
        /// <para><b>Its whole job is to be the control for
        /// <see cref="LegsOnlyStandStripsAWoman"/>.</b> Identical stand,
        /// identical flag, identical plan; only the colonist differs. Without
        /// this half the pair proves nothing — a case that goes red for
        /// everybody says the feature is broken, not that the rule is
        /// asymmetric.</para>
        ///
        /// <para>It also guards the thing most likely to rot: if the gender
        /// parameter on <see cref="Stage"/> ever stops taking effect, both
        /// halves quietly become the same test and the pair keeps passing while
        /// asserting half of what it claims. Hence the explicit gender
        /// assertion.</para>
        /// </summary>
        internal static bool LegsOnlyStandLeavesAManDecent(Fixture fix)
        {
            // The guard ships OFF (opt-in, decided 2026-09-08), so a case
            // about what it does has to turn it on. Case() restores it.
            if (ShiftChangeMod.Settings == null)
            {
                return Expect(false, "mod settings resolve");
            }
            ShiftChangeMod.Settings.keepColonistsDecent = true;

            ClearStand(fix.Stand);
            if (!StockOne(fix.Stand, "Apparel_Pants"))
            {
                return Expect(false, "trousers can be stocked");
            }

            bool ok = Expect(fix.Pawn.gender == Gender.Male,
                             "the fixture colonist is a man (control — the pair turns on this)")
                & Expect(!fix.Pawn.apparel.PsychologicallyNude,
                         "who starts out dressed (control)");

            fix.Comp.SetFullChange(true);
            ok &= Expect(RunSwap(fix), "dress leg ran to completion");

            return ok
                & Expect(fix.Pawn.apparel.WornApparel.Count == 1,
                         "he is wearing the stand's trousers and nothing else")
                & Expect(!fix.Pawn.apparel.PsychologicallyNude,
                         "and vanilla calls that dressed, so the swap was legitimate");
        }

        /// <summary>
        /// The same stand, the same trousers, the same flag — a woman. Vanilla
        /// calls her nude, because her rule wants a covered torso as well
        /// (<c>Pawn_ApparelTracker.PsychologicallyNude:218-222</c>: a man
        /// returns <c>!hasPants</c>, a woman with trousers on returns
        /// <c>!hasShirt</c>).
        ///
        /// <para><b>This is the configuration a male-only fixture certifies as
        /// safe.</b> The shield-belt case in
        /// <see cref="FullChangeRefusesToStripThemBare"/> strips everybody, so
        /// it would have been found eventually. This one strips half the
        /// colony and passes every test written against the other half, which
        /// is exactly the shape that reaches players — and it is a plain
        /// stand configuration, not a contrived one: trousers on a work stand,
        /// full change on, no top stocked.</para>
        ///
        /// <para>Was a known gap alongside the shield-belt case until the
        /// retention pass landed. It is now the regression test for the half of
        /// the rule a male-only fixture cannot see.</para>
        /// </summary>
        internal static bool LegsOnlyStandStripsAWoman(Fixture fix)
        {
            // The guard ships OFF (opt-in, decided 2026-09-08), so a case
            // about what it does has to turn it on. Case() restores it.
            if (ShiftChangeMod.Settings == null)
            {
                return Expect(false, "mod settings resolve");
            }
            ShiftChangeMod.Settings.keepColonistsDecent = true;

            ClearStand(fix.Stand);
            if (!StockOne(fix.Stand, "Apparel_Pants"))
            {
                return Expect(false, "trousers can be stocked");
            }

            bool ok = Expect(fix.Pawn.gender == Gender.Female,
                             "the fixture colonist is a woman (control — the pair turns on this)")
                & Expect(!fix.Pawn.apparel.PsychologicallyNude,
                         "who starts out dressed (control)");

            fix.Comp.SetFullChange(true);
            ok &= Expect(RunSwap(fix), "dress leg ran to completion");

            // THE PAIR, POST-FIX. The man walked away in the stand's trousers
            // and nothing else, and that was correct for him. She keeps her
            // shirt as well — the SAME stand, the same flag, a different plan,
            // because the plan now asks a question whose answer depends on who
            // is standing there.
            ok &= Expect(fix.Pawn.apparel.WornApparel.Count == 2,
                         "she is wearing the stand's trousers AND her own shirt, where the "
                         + "man needed only the trousers")
                & Expect(!fix.Pawn.apparel.PsychologicallyNude,
                         "and is left decent, as he was");

            // Same tail as the shield-belt case: recoverable, not destructive.
            fix.Comp.SetFullChange(false);
            ok &= Expect(RunSwap(fix), "return leg ran to completion");
            return ok
                & Expect(!fix.Pawn.apparel.PsychologicallyNude,
                         "and the return trip puts her back in her own clothes");
        }

        /// <summary>
        /// A nudist at the same stand that the decency guard rescues everyone
        /// else from. They are left in the shield belt, and that is correct.
        ///
        /// <para>The guard exists to stop a swap costing a colonist their
        /// clothes against the player's intent. For a nudist the intent is the
        /// opposite: <c>ClothedNudist</c> is a mood PENALTY, so holding
        /// garments back would fight them every shift with no way out short of
        /// unbuilding the stand.</para>
        ///
        /// <para>The predicate itself is asserted unchanged — this case is
        /// about the RESPONSE to nudity, not about redefining it. If
        /// <c>WouldBeNude</c> ever stops reporting this pawn as nude, the
        /// exemption is being reached for the wrong reason and the assertion
        /// below says so.</para>
        /// </summary>
        internal static bool NudistIsExemptFromDecency(Fixture fix)
        {
            // The guard ships OFF (opt-in, decided 2026-09-08), so a case
            // about what it does has to turn it on. Case() restores it.
            if (ShiftChangeMod.Settings == null)
            {
                return Expect(false, "mod settings resolve");
            }
            ShiftChangeMod.Settings.keepColonistsDecent = true;

            TraitDef nudist = TraitDefOf.Nudist;
            if (nudist == null || fix.Pawn.story?.traits == null)
            {
                return Expect(false, "the Nudist trait resolves");
            }
            if (!fix.Pawn.story.traits.HasTrait(nudist))
            {
                fix.Pawn.story.traits.GainTrait(new Trait(nudist));
            }

            ClearStand(fix.Stand);
            if (!StockOne(fix.Stand, "Apparel_ShieldBelt"))
            {
                return Expect(false, "a shield belt can be stocked");
            }
            List<Apparel> before = new List<Apparel>(fix.Pawn.apparel.WornApparel);

            bool ok = Expect(SwapPlan.PrefersNudity(fix.Pawn),
                             "the colonist is a nudist (control — the exemption turns on this)");

            fix.Comp.SetFullChange(true);
            List<Apparel> wear = new List<Apparel>();
            List<Apparel> store = new List<Apparel>();
            ok &= Expect(SwapPlan.BuildDress(fix.Pawn, fix.Stand, wear, store),
                         "there is still a plan")
                & Expect(store.Count == before.Count,
                         "and it takes everything, holding nothing back")
                & Expect(SwapPlan.WouldBeNude(fix.Pawn, store, wear),
                         "while the predicate still calls that nude — what changed is the "
                         + "response to it, not the definition");

            ok &= Expect(RunSwap(fix), "dress leg ran to completion");
            return ok
                & Expect(fix.Pawn.apparel.WornApparel.Count == 1,
                         "they are wearing the belt and nothing else, as configured")
                & Expect(RunSwap(fix), "return leg ran to completion")
                & Expect(fix.Pawn.apparel.WornApparel.Count == before.Count + 0,
                         "and their own clothes came back");
        }

        /// <summary>
        /// The global override, off. An ordinary colonist at the same stand is
        /// then stripped exactly as before the guard existed.
        ///
        /// <para>This asserts the setting is WIRED, which nothing else does. A
        /// toggle that reads a field nobody consults is indistinguishable from
        /// a working one at a glance, and its whole purpose is to be reachable
        /// by a player whose colony the guard is breaking.</para>
        ///
        /// <para>The setting is restored before returning. The harness runs
        /// against its own save-data folder so a leak cannot reach the player's
        /// config, but cases run in sequence and every later one would inherit
        /// it.</para>
        /// </summary>
        internal static bool DecencyGuardIsOptIn(Fixture fix)
        {
            if (ShiftChangeMod.Settings == null)
            {
                return Expect(false, "mod settings resolve");
            }
            ClearStand(fix.Stand);
            if (!StockOne(fix.Stand, "Apparel_ShieldBelt"))
            {
                return Expect(false, "a shield belt can be stocked");
            }
            List<Apparel> before = new List<Apparel>(fix.Pawn.apparel.WornApparel);

            // THE SHIPPED DEFAULT IS THE ASSERTION. Nothing else in the suite
            // pins it, and a default that flips by accident is exactly the kind
            // of change nobody notices until a colony behaves differently after
            // an update — which is the whole reason it ships off.
            //
            // This pins a SETTLED property, not a temporary state (2026-09-12).
            // Off is the permanent default because most players meet this mod
            // inside a mod pack; see ShiftChangeSettings.keepColonistsDecent.
            // If this case ever fails, the default moved — fix the default.
            bool ok = Expect(!ShiftChangeMod.DecencyEnabled,
                             "the guard is OFF out of the box: this ships opt-in")
                & Expect(!SwapPlan.PrefersNudity(fix.Pawn),
                         "and an ordinary colonist, so only the setting decides (control)");

            fix.Comp.SetFullChange(true);

            List<Apparel> wear = new List<Apparel>();
            List<Apparel> store = new List<Apparel>();
            ok &= Expect(SwapPlan.BuildDress(fix.Pawn, fix.Stand, wear, store),
                         "there is a plan")
                & Expect(store.Count == before.Count,
                         "which takes everything, exactly as every version up to v1.3.0 did");

            // Case() restores this.
            ShiftChangeMod.Settings.keepColonistsDecent = true;

            List<Apparel> onWear = new List<Apparel>();
            List<Apparel> onStore = new List<Apparel>();
            return ok
                & Expect(ShiftChangeMod.DecencyEnabled, "turning it on takes effect")
                & Expect(SwapPlan.BuildDress(fix.Pawn, fix.Stand, onWear, onStore),
                         "there is still a plan")
                & Expect(onStore.Count < before.Count,
                         "and it now holds something back");
        }
    }
}
