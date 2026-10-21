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
    /// Cases for the swap itself: the driver's round trip, full change, and the four decency cases
    /// that decide what a stand may take.
    ///
    /// <para>Case order is decided in one place,
    /// <see cref="DebugTools_LifecycleHarness.Run"/>.</para>
    /// </summary>
    internal static class HarnessSwap
    {
        /// <summary>
        /// A stand whose stock displaces nothing still takes its uniform back on the return trip.
        ///
        /// <para>Guards a past bug: a Shell-layer duster over shirt and trousers, which share no
        /// layer with it, was donated to the pawn for good and the stand emptied itself,
        /// silently.</para>
        ///
        /// <para>Both legs go through the real driver on the real tracker, because the failure sits
        /// past a job boundary that the hand-assembled <see cref="Build"/> fixture never
        /// crosses.</para>
        ///
        /// <para>The ledger-cleared assertion alone proves nothing here: the driver's fallback for
        /// an empty incoming set, <c>NothingToWear</c>, calls <c>AbandonLedger</c> and clears it
        /// too. The uniform-off, back-in-the-stand, own-clothes and can-dress-again assertions are
        /// the ones that fail without the fix.</para>
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
        /// The driver builds a correct ledger and gives everything back, force-worn flags included.
        ///
        /// <para>No other case covers the forced-flag half. The engine's
        /// <c>Pawn_ApparelTracker.Notify_ApparelRemoved</c> clears the forced flag on every
        /// removal, so the driver captures it before removing and restores it on the way back, and
        /// <see cref="Build"/> hands <c>NotifyDressed</c> an empty forced list, so this is the one
        /// case that reaches that path.</para>
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
            // The player's explicit choice, which the swap must not downgrade to policy-managed.
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
        /// Full change: the pawn ends up wearing the stand's kit and nothing else, and gets every
        /// garment back on the return trip.
        ///
        /// <para>The <c>Displacing</c> fixture is what makes this assert anything. Its pawn wears a
        /// shirt and trousers (OnSkin) under a parka (Shell) and the stand holds a duster (Shell),
        /// so the ordinary swap displaces the parka alone, and whatever happens to the shirt and
        /// trousers is attributable to the flag.</para>
        ///
        /// <para>An empty stand must still refuse. That assertion is a control as much as a rule:
        /// it fails if the full-change pass is moved above the <c>toWear.Count > 0</c> test in
        /// <see cref="SwapPlan.BuildDress"/>.</para>
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

            // The return trip is driven by the ledger, not the flag, so it must hand everything
            // back with the flag off.
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

            // Empty the stand: full change alone must not make it dress anyone.
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
        /// Full change must not strip a colonist bare: the dress path asks the same decency
        /// question the deposit path does (docs/DESIGN.md, "The dress path asks the same question
        /// and answers it differently").
        ///
        /// <para>The garment is the one <see cref="HarnessTriggers.DepositOnlyParksAndReturns"/>
        /// uses for its utility-layer trap. A shield belt covers <c>Waist</c> and neither
        /// <c>Torso</c> nor <c>Legs</c>, so it cannot dress anybody. That case asserts the deposit
        /// path refuses a plan that leaves a colonist in just the belt; this one asserts the dress
        /// path does not leave them in it either.</para>
        ///
        /// <para>The verdict comes from vanilla's own
        /// <c>Pawn_ApparelTracker.PsychologicallyNude</c>, read off a real spawned pawn, not from
        /// our transcription of it (<c>SwapPlan.WouldBeNude</c>). The transcription is asserted
        /// separately as a control, so if the two disagree it is ours that is wrong.</para>
        /// </summary>
        internal static bool FullChangeRefusesToStripThemBare(Fixture fix)
        {
            // The guard ships off, so this case turns it on; Case() restores the setting.
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

            // Replace the fixture's duster with exactly one garment that covers no group the
            // decency test counts: the belt covers Waist only.
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

            // Which garments are held back is the design: KeepThemDecent ranks candidates by
            // innermost layer, so the two OnSkin garments stay on and the Shell parka is the one
            // that goes to the stand. Asserting the parka by name stops a "keep the first one that
            // works" from passing.
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
        /// A stand holding trousers and nothing else, worn by a man. He ends up in trousers, which
        /// vanilla is happy with, so the swap is legitimate and this case passes.
        ///
        /// <para>Its job is to be the control for <see cref="LegsOnlyStandStripsAWoman"/>: the same
        /// stand, flag and plan, with only the colonist differing. Without it the pair proves
        /// nothing, since a case that fails for everybody says the feature is broken, not that the
        /// rule is asymmetric.</para>
        ///
        /// <para>It also catches the gender parameter on <see cref="Stage"/> ceasing to take
        /// effect, which would make both halves the same test while the pair kept passing; hence
        /// the explicit gender assertion.</para>
        /// </summary>
        internal static bool LegsOnlyStandLeavesAManDecent(Fixture fix)
        {
            // The guard ships off, so this case turns it on; Case() restores the setting.
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
        /// The same stand, trousers and flag, with a woman: the plan holds her own shirt back, so
        /// she ends in the stand's trousers and her shirt, decent, and stays decent through the
        /// return trip. Vanilla would call her nude in the trousers alone, because its rule for
        /// women wants a covered torso as well (<c>Pawn_ApparelTracker.PsychologicallyNude</c>: a
        /// man is nude on missing trousers alone, a woman on missing trousers or a missing shirt).
        ///
        /// <para>A fixture of men alone would pass a plan that strips every woman at this
        /// stand.</para>
        /// </summary>
        internal static bool LegsOnlyStandStripsAWoman(Fixture fix)
        {
            // The guard ships off, so this case turns it on; Case() restores the setting.
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

            // The pair: the man wore the stand's trousers and nothing else. She keeps her shirt as
            // well, because the plan's decency question depends on who is wearing the clothes.
            ok &= Expect(fix.Pawn.apparel.WornApparel.Count == 2,
                         "she is wearing the stand's trousers AND her own shirt, where the "
                         + "man needed only the trousers")
                & Expect(!fix.Pawn.apparel.PsychologicallyNude,
                         "and is left decent, as he was");

            // Same tail as the shield-belt case: the swap must be recoverable.
            fix.Comp.SetFullChange(false);
            ok &= Expect(RunSwap(fix), "return leg ran to completion");
            return ok
                & Expect(!fix.Pawn.apparel.PsychologicallyNude,
                         "and the return trip puts her back in her own clothes");
        }

        /// <summary>
        /// A nudist at the same stand that the decency guard rescues everyone else from is left in
        /// the shield belt, and that is correct.
        ///
        /// <para>The guard exists to stop a swap costing a colonist their clothes against the
        /// player's intent. For a nudist the intent is the opposite: <c>ClothedNudist</c> is a mood
        /// penalty, so holding garments back would fight them every shift with no way out short of
        /// unbuilding the stand.</para>
        ///
        /// <para>The predicate is asserted unchanged: this case is about the response to nudity,
        /// not a redefinition of it. If <c>WouldBeNude</c> stops reporting this pawn as nude, the
        /// exemption is being reached for the wrong reason.</para>
        /// </summary>
        internal static bool NudistIsExemptFromDecency(Fixture fix)
        {
            // The guard ships off, so this case turns it on; Case() restores the setting.
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
        /// The global override, off: an ordinary colonist at the same stand is stripped as if the
        /// guard did not exist.
        ///
        /// <para>This asserts the setting is wired, which nothing else does. A toggle that reads a
        /// field nobody consults looks like a working one, and its purpose is to be reachable by a
        /// player whose colony the guard is breaking.</para>
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

            // Pins the shipped default, which nothing else does. Off is permanent
            // (ShiftChangeSettings.keepColonistsDecent), and a default that flips by accident
            // changes colonies after an update with no sign. On a run with fresh settings, as
            // run-harness.sh's own save-data folder gives, a failure means the default moved.
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

            // Turn it on; Case() restores the setting, since mod settings are global and cases run
            // in sequence.
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
#endif
