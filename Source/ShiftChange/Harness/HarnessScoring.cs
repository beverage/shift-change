// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
#if HARNESS
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using static ShiftChange.DebugTools_LifecycleHarness;

namespace ShiftChange
{
    /// <summary>
    /// Harness cases for <see cref="Patch_ParkedKitOutscoresSpares"/>, which stops a checked-out
    /// pawn re-arming from the colony's spare pile while their own kit is parked in the stand. The
    /// failure is invisible in play: a pawn in an identical garment looks like one who never moved.
    ///
    /// <para>The two cases are one assertion. The rule is a comparison, never an equivalence test
    /// (the patch says why), and the first case alone cannot tell the two apart. Refuse while the
    /// parked kit is sound, allow once it is not; delete either half and the other stops meaning
    /// anything.</para>
    ///
    /// <para>Nothing is spawned: <c>ApparelScoreRaw</c> reads a garment's def stats, hit points,
    /// quality and stuff and never asks where it is, so a spare from <c>ThingMaker</c> scores as
    /// one in a stockpile does. The ledger entry the patch keys on is built for real, through
    /// <c>SwapPlan.BuildDress</c> and <c>NotifyDressed</c>.</para>
    /// </summary>
    internal static class HarnessScoring
    {
        /// <summary>
        /// A checked-out pawn refuses a spare duplicating what is parked. Three controls keep the
        /// refusal honest: a spare that displaces nothing parked, the worn garment itself, and the
        /// same spare once the pawn is off shift.
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

            // A garment that displaces nothing parked must come through untouched. Chosen against
            // what is actually parked, never hard-coded: with Vanilla Apparel Expanded loaded the
            // fixture parks a duster, without it the pawn's tribalwear, whose legs conflict with
            // pants, so a fixed pants control would assert the opposite of the truth and read as a
            // broken patch.
            Apparel unrelated = NonConflictingSpare(fix);
            ok &= Expect(unrelated != null,
                         "a control garment that displaces nothing parked could be made");
            if (unrelated != null)
            {
                ok &= Expect(!Refused(fix.Pawn, unrelated),
                             "a spare that displaces nothing parked is not refused (control)");
            }

            // The same conflict, but worn. ApparelScoreGain subtracts the worn scores, so refusing
            // a worn garment would inflate every candidate instead of suppressing it.
            Apparel worn = fix.Pawn.apparel != null && fix.Pawn.apparel.WornApparel.Count > 0
                ? fix.Pawn.apparel.WornApparel[0]
                : null;
            if (worn != null)
            {
                ok &= Expect(!Refused(fix.Pawn, worn),
                             "a WORN garment is never refused (control)");
            }

            // Last: it drops the ledger entry the rest of the case needs. Teardown removes it
            // anyway.
            CompShiftStand.OnShiftStands.Remove(fix.Pawn);
            ok &= Expect(!Refused(fix.Pawn, spare),
                         "and the same spare scores normally once off shift (control)");
            return ok;
        }

        /// <summary>
        /// A parked garment worn through no longer answers for the pawn, so the same spare is
        /// allowed again. Only the parked garment's condition differs between the two arms.
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

            // A fifth of full hit points is well under the knee of the game's
            // HitPointsPercentScoreFactorCurve, which is 1.0 from 52% up, 0.3 from 22% to 50% and
            // at most 0.2 from 20% down, so the parked garment is the worse one by the game's own
            // reckoning, not by a threshold of ours.
            parked.HitPoints = Mathf.Max(1, parked.MaxHitPoints / 5);
            ok &= Expect(!Refused(fix.Pawn, spare),
                         "and is allowed once the parked garment is worn through");
            return ok;
        }

        /// <summary>The first garment the fixture's swap parked.</summary>
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
        /// A sound copy of a garment: same def, stuff and quality, full hit points. Quality
        /// especially, or the copy scores differently from its original and decides both cases for
        /// the wrong reason.
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
        /// A spare that provably displaces nothing the fixture parked, picked by asking
        /// <c>CanWearTogether</c> rather than assuming a layer. Null when none qualifies; the
        /// caller asserts on that, since a control that cannot be built must fail the case.
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
        /// Whether our postfix refused this garment, read by magnitude: vanilla's own refusals
        /// inside <c>ApparelScoreRaw</c> are -10, far shallower than <c>RefusedScore</c>. A mod
        /// that multiplies scores could push one past it, so if this fails on a full modlist, look
        /// for other postfixes on the scorer before blaming the patch.
        /// </summary>
        internal static bool Refused(Pawn pawn, Apparel apparel)
        {
            return JobGiver_OptimizeApparel.ApparelScoreRaw(pawn, apparel)
                   <= Patch_ParkedKitOutscoresSpares.RefusedScore;
        }
    }
}
#endif
