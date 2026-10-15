using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Stops a checked-out pawn re-arming from the colony's spare pile while
    /// their own kit sits parked in the stand.
    ///
    /// <para><b>The hole, found in play 2026-09-18.</b> A pawn's parked
    /// apparel is deliberately invisible to apparel scans:
    /// <see cref="Patch_AllowRemovingToggle"/> holds
    /// <c>allowRemovingItems</c> off, which is the one flag gating
    /// <c>IApparelSource.ApparelSourceEnabled</c>. From a scorer's side that
    /// reads as a pawn who owns no armour at all, and a pawn who owns no
    /// armour is the most attractive customer in the game:
    /// <c>ApparelScoreGain</c> multiplies by TEN when a candidate replaces
    /// nothing worn. Loadout mods stack their own multiplier on top —
    /// Compositable Loadouts weights a garment by
    /// <c>5^(elements.Count - index)</c>, so 125x for a pawn's first tag —
    /// and the pawn walks to the nearest match.</para>
    ///
    /// <para>Then the change-back hands their own suit back, the two
    /// conflict, one is dropped, a hauler returns it to the pile, and the
    /// cycle has somewhere to start again. On the colony this was diagnosed
    /// from, five pawns had swapped power armour within 1.7 in-game days
    /// against a control who had worn the same suit for 30, the pile had
    /// grown to fifteen identical suits, and painted kit had crossed
    /// owners.</para>
    ///
    /// <para><b>Why the pause we already have does not cover it.</b>
    /// <see cref="Patch_OptimizeApparelOnShift"/> stops vanilla's
    /// <c>JobGiver_OptimizeApparel.TryGiveJob</c>, and that is all it stops.
    /// Compositable Loadouts ships its own think node at
    /// <c>Humanlike_PostDuty</c> priority 100 — above rest, work and joy — and
    /// reaches the same scorer through the static
    /// <c>ApparelScoreGain</c>. Pausing one giver per neighbouring mod does
    /// not scale and leaves the next one to find the hole again, so the
    /// refusal lives at the scorer both paths share and needs no dependency
    /// on any of them.</para>
    ///
    /// <para><b>The rule is a comparison, never an equivalence test.</b>
    /// "Refuse a candidate that duplicates something parked" would be wrong:
    /// a pawn whose parked suit is worn through SHOULD go and find another,
    /// and loadout mods already implement exactly that. Compositable
    /// Loadouts does not lean on vanilla's hit-point curve — its own item
    /// filter carries a hit-point range, and a garment below it stops
    /// matching, loses the 125x weight and scores three orders of magnitude
    /// lower, which is what sends the pawn shopping. So the question asked
    /// here is the only one that keeps that intact:
    /// <b>does something parked still score at least as well as the
    /// candidate?</b> Quality, damage, tainting, insulation, royal-title
    /// minimums and every mod's own weighting come along for free, because
    /// the comparison runs through the same scorer that produced the number
    /// we are being asked to approve.</para>
    ///
    /// <para><b>Both sides are re-scored rather than one side compared to
    /// <c>__result</c>.</b> Harmony gives no ordering guarantee between two
    /// mods' postfixes on one method. Read <c>__result</c> and a neighbour
    /// whose postfix has not run yet hands us an unweighted candidate to
    /// compare against a weighted parked garment, which suppresses nearly
    /// everything — including the worn-through case above. Scoring both
    /// sides through a nested call puts them on the same footing whatever
    /// the order, at the cost of one extra evaluation per conflicting
    /// candidate, for pawns who are checked out and nobody else.</para>
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_OptimizeApparel), nameof(JobGiver_OptimizeApparel.ApparelScoreRaw))]
    public static class Patch_ParkedKitOutscoresSpares
    {
        /// <summary>
        /// The magnitude vanilla itself uses to refuse a garment outright in
        /// <c>ApparelScoreGain</c>. Deep enough to survive a neighbour's
        /// postfix multiplying it afterwards, and the no-conflict path
        /// multiplying it by ten after that.
        /// </summary>
        internal const float RefusedScore = -1000f;

        /// <summary>
        /// Re-entrancy guard for the nested scoring below. Thread-static
        /// rather than plain static because nothing promises the scorer is
        /// only ever reached from the main thread, and the failure mode if
        /// that assumption breaks is unbounded recursion inside a job giver.
        ///
        /// <para>It suppresses only OUR postfix. A neighbour's still runs on
        /// the nested call, which is precisely what makes the two sides
        /// comparable.</para>
        /// </summary>
        [ThreadStatic]
        internal static bool reentered;

        // ReSharper disable once InconsistentNaming — Harmony result injection.
        public static void Postfix(Pawn pawn, Apparel ap, ref float __result)
        {
            if (reentered || pawn == null || ap == null)
            {
                return;
            }

            // The same method builds the WORN score cache, and
            // ApparelScoreGain SUBTRACTS those. Refusing a worn garment here
            // would inflate every candidate instead of suppressing one.
            if (ap.Wearer != null)
            {
                return;
            }

            // Already refused by vanilla or by a neighbour; leave it refused.
            if (__result <= RefusedScore)
            {
                return;
            }

            try
            {
                SessionGuard.Ensure();
                CompShiftStand stand = CompShiftStand.OnShiftStandFor(pawn);
                if (stand == null)
                {
                    return;
                }
                List<Apparel> parked = stand.StoredOwnerApparelForReading;
                if (parked.Count == 0 || pawn.RaceProps == null)
                {
                    return;
                }

                reentered = true;
                try
                {
                    float candidateScore = JobGiver_OptimizeApparel.ApparelScoreRaw(pawn, ap);
                    for (int i = 0; i < parked.Count; i++)
                    {
                        Apparel garment = parked[i];
                        if (garment == null || garment == ap)
                        {
                            continue;
                        }
                        // Only a garment that would DISPLACE the candidate is
                        // an answer to it. A parked helmet says nothing about
                        // whether the pawn needs a chest piece.
                        if (ApparelUtility.CanWearTogether(garment.def, ap.def, pawn.RaceProps.body))
                        {
                            continue;
                        }
                        if (JobGiver_OptimizeApparel.ApparelScoreRaw(pawn, garment) >= candidateScore)
                        {
                            __result = RefusedScore;
                            return;
                        }
                    }
                }
                finally
                {
                    reentered = false;
                }
            }
            catch (Exception e)
            {
                // This scorer runs for every candidate on every optimize pass,
                // so a throw here recurs forever rather than once. Fail open:
                // a pawn wearing the wrong suit is a nuisance, a scorer that
                // throws is a pawn who stops dressing at all.
                reentered = false;
                Log.ErrorOnce("[ShiftChange] parked-kit scoring threw for "
                              + pawn.LabelShort + ", letting the score stand: " + e,
                              "ShiftChange.ParkedKit".GetHashCode());
            }
        }
    }
}
