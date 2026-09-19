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
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using static ShiftChange.DebugTools_LifecycleHarness;

namespace ShiftChange
{
    /// <summary>
    /// Harness cases for <see cref="Patch_ParkedKitOutscoresSpares"/>: the
    /// refusal that stops a checked-out pawn re-arming from the colony's
    /// spare pile while their own kit is parked in the stand.
    ///
    /// <para><b>These cases are why this exists rather than a play
    /// observation.</b> The failure is invisible from the outside — a pawn
    /// wearing an identical garment looks exactly like a pawn who never
    /// moved, and the only tell on the colony it was found on was that gear
    /// had quietly crossed owners over many days. Nothing about that is
    /// catchable by looking.</para>
    ///
    /// <para><b>The second case is the load-bearing one.</b> A refusal keyed
    /// on "you already own one of these" would be wrong, and would look
    /// perfectly healthy in the first case. A pawn whose parked garment is
    /// worn through SHOULD go and find another, and loadout mods already
    /// implement exactly that through their own item filters. So the pair is
    /// the assertion: refuse while the parked kit is sound, allow once it is
    /// not. Delete either half and the other stops meaning anything.</para>
    ///
    /// <para><b>Nothing is spawned.</b> <c>ApparelScoreRaw</c> reads def
    /// stats, hit points, quality and stuff, and never asks where a garment
    /// is standing — so a spare made with <c>ThingMaker</c> exercises the
    /// scorer exactly as a garment in a stockpile does, without a cell to
    /// find or clean up. What the patch keys on is the pawn's ledger entry,
    /// and the fixture builds that for real through
    /// <c>SwapPlan.BuildDress</c> and <c>NotifyDressed</c>.</para>
    /// </summary>
    internal static class HarnessScoring
    {
        /// <summary>
        /// A checked-out pawn refuses a spare that duplicates what is parked,
        /// with three controls: a spare that displaces nothing parked, the
        /// worn garment itself, and the same spare once the pawn is off
        /// shift. Without those, "the score was low" could just as easily be
        /// a garment nobody would ever have taken.
        /// </summary>
        internal static bool ParkedKitRefusesSpares(Fixture fix)
        {
            Apparel parked = ParkedGarment(fix);
            if (parked == null)
            {
                return Expect(false, "the fixture parked a garment to compare against");
            }

            Apparel spare = Duplicate(parked);
            if (spare == null)
            {
                return Expect(false, "a spare of the parked garment could be made");
            }

            bool ok = Expect(fix.Comp.Borrower == fix.Pawn, "the pawn is checked out (precondition)")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                             "and the ledger names this stand (precondition)")
                    & Expect(Refused(fix.Pawn, spare),
                             "a spare duplicating the parked garment is refused");

            // A garment that displaces nothing parked answers no question the
            // parked one has already answered, so it must come through
            // untouched. CHOSEN AGAINST WHAT IS ACTUALLY PARKED, never
            // hard-coded: this control named Apparel_Pants on the assumption
            // that the fixture parks a duster, which is what it does with
            // Vanilla Apparel Expanded loaded. Without it, Stock falls back to
            // a shirt and pants and the garment displaced is the pawn's
            // tribalwear — Legs on OnSkin, which conflicts with pants. The
            // control then asserted the exact opposite of the truth and read
            // as the patch being broken.
            Apparel unrelated = NonConflictingSpare(fix);
            ok &= Expect(unrelated != null,
                         "a control garment that displaces nothing parked could be made");
            if (unrelated != null)
            {
                ok &= Expect(!Refused(fix.Pawn, unrelated),
                             "a spare that displaces nothing parked is not refused (control)");
            }

            // The same conflict, but WORN. ApparelScoreGain subtracts worn
            // scores, so refusing one would inflate every candidate instead
            // of suppressing it — the inverse of the bug.
            Apparel worn = fix.Pawn.apparel != null && fix.Pawn.apparel.WornApparel.Count > 0
                ? fix.Pawn.apparel.WornApparel[0]
                : null;
            if (worn != null)
            {
                ok &= Expect(!Refused(fix.Pawn, worn),
                             "a WORN garment is never refused (control)");
            }

            // Last, because it drops the ledger entry the case depends on.
            // Teardown removes it anyway, so nothing is put back.
            CompShiftStand.OnShiftStands.Remove(fix.Pawn);
            ok &= Expect(!Refused(fix.Pawn, spare),
                         "and the same spare scores normally once off shift (control)");
            return ok;
        }

        /// <summary>
        /// The half that keeps the refusal honest: a parked garment that is
        /// worn through no longer answers for the pawn, and the spare is
        /// allowed again. Both arms run against the same pawn and the same
        /// spare, so the only thing that differs is the parked garment's
        /// condition.
        /// </summary>
        internal static bool WornOutParkedKitStillGoesShopping(Fixture fix)
        {
            Apparel parked = ParkedGarment(fix);
            if (parked == null)
            {
                return Expect(false, "the fixture parked a garment to wear out");
            }
            if (!parked.def.useHitPoints || parked.MaxHitPoints < 5)
            {
                return Expect(false, "the parked garment tracks hit points");
            }

            Apparel spare = Duplicate(parked);
            if (spare == null)
            {
                return Expect(false, "a spare of the parked garment could be made");
            }

            bool ok = Expect(Refused(fix.Pawn, spare),
                             "the spare is refused while the parked garment is sound (control)");

            // Under the knee of HitPointsPercentScoreFactorCurve, which is
            // flat at 1.0 above 52% and drops to 0.3 below it — so a fifth of
            // full is unambiguously the worse garment by the game's own
            // reckoning, not by a threshold of ours.
            parked.HitPoints = Mathf.Max(1, parked.MaxHitPoints / 5);
            ok &= Expect(!Refused(fix.Pawn, spare),
                         "and is allowed once the parked garment is worn through");
            return ok;
        }

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// The first garment the fixture's swap parked. The default fixture
        /// stores a duster to make room for a lab coat, and both sit on the
        /// Shell layer, so a duplicate duster is a genuine conflict.
        /// </summary>
        internal static Apparel ParkedGarment(Fixture fix)
        {
            if (fix == null || fix.Comp == null)
            {
                return null;
            }
            List<Apparel> parked = fix.Comp.StoredOwnerApparelForReading;
            for (int i = 0; i < parked.Count; i++)
            {
                if (parked[i] != null)
                {
                    return parked[i];
                }
            }
            return null;
        }

        /// <summary>
        /// A sound copy of a garment: same def, same stuff, same quality,
        /// full hit points. Quality especially — leave it at whatever
        /// <c>ThingMaker</c> hands back and the copy scores differently from
        /// its original, which would decide both cases for the wrong reason.
        /// </summary>
        internal static Apparel Duplicate(Apparel source)
        {
            if (source == null)
            {
                return null;
            }
            Apparel copy = ThingMaker.MakeThing(source.def, source.Stuff) as Apparel;
            if (copy == null)
            {
                return null;
            }
            CompQuality sourceQuality = source.TryGetComp<CompQuality>();
            CompQuality copyQuality = copy.TryGetComp<CompQuality>();
            if (sourceQuality != null && copyQuality != null)
            {
                copyQuality.SetQuality(sourceQuality.Quality, ArtGenerationContext.Colony);
            }
            if (copy.def.useHitPoints)
            {
                copy.HitPoints = copy.MaxHitPoints;
            }
            return copy;
        }

        /// <summary>
        /// A spare that provably displaces nothing the fixture parked, picked
        /// by asking <c>CanWearTogether</c> rather than by assuming a layer.
        /// Returns null when no candidate qualifies, which the caller asserts
        /// on — a control that cannot be built must fail the case, not vanish
        /// from it.
        /// </summary>
        internal static Apparel NonConflictingSpare(Fixture fix)
        {
            List<Apparel> parked = fix.Comp.StoredOwnerApparelForReading;
            string[] candidates =
            {
                "Apparel_ShieldBelt", "Apparel_SimpleHelmet",
                "Apparel_Pants", "Apparel_BasicShirt",
            };
            for (int c = 0; c < candidates.Length; c++)
            {
                Apparel spare = MakeOne(candidates[c]);
                if (spare == null)
                {
                    continue;
                }
                bool clashes = false;
                for (int i = 0; i < parked.Count && !clashes; i++)
                {
                    if (parked[i] == null)
                    {
                        continue;
                    }
                    clashes = !ApparelUtility.CanWearTogether(
                        parked[i].def, spare.def, fix.Pawn.RaceProps.body);
                }
                if (!clashes)
                {
                    return spare;
                }
            }
            return null;
        }

        internal static Apparel MakeOne(string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null || def.apparel == null)
            {
                return null;
            }
            ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            return ThingMaker.MakeThing(def, stuff) as Apparel;
        }

        /// <summary>
        /// Did our postfix refuse this garment for this pawn?
        ///
        /// <para>Reads the refusal by its magnitude, which is safe because
        /// vanilla's own refusals inside <c>ApparelScoreRaw</c> are -10, an
        /// order of magnitude shallower. A neighbouring mod that multiplies
        /// scores could in principle drive a vanilla -10 past -1000, so if
        /// this ever reads as a failure on a full modlist, check what else is
        /// postfixing the scorer before believing the patch broke.</para>
        /// </summary>
        internal static bool Refused(Pawn pawn, Apparel apparel)
        {
            return JobGiver_OptimizeApparel.ApparelScoreRaw(pawn, apparel)
                   <= Patch_ParkedKitOutscoresSpares.RefusedScore;
        }
    }
}
#endif
