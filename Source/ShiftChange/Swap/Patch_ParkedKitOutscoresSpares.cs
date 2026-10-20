using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Stops a checked-out pawn re-arming from the colony's spare pile while their own kit sits
    /// parked in the stand.
    ///
    /// <para>Parked apparel is deliberately invisible to apparel scans:
    /// <see cref="Patch_AllowRemovingToggle"/> holds <c>allowRemovingItems</c> off, the one flag
    /// gating <c>IApparelSource.ApparelSourceEnabled</c>. To a scorer that is a pawn who owns no
    /// armour, and <c>ApparelScoreGain</c> multiplies a candidate's score by ten when it replaces
    /// nothing worn. Loadout mods stack their own weight on top (Compositable Loadouts weights a
    /// garment by <c>5^(elements.Count - index)</c>, 125 for the first of three tags), so the
    /// pawn walks to the nearest match.</para>
    ///
    /// <para>The spare does the harm at change-back: the pawn's own kit goes back on, Wear drops
    /// the spare it conflicts with, a hauler returns that to the pile, and the next shift takes
    /// one again. Kit, painted kit included, drifts between owners.</para>
    ///
    /// <para><see cref="Patch_OptimizeApparelOnShift"/> does not cover this: it stops only
    /// vanilla's <c>JobGiver_OptimizeApparel.TryGiveJob</c>, and Compositable Loadouts ships its
    /// own think node (<c>Humanlike_PostDuty</c>, priority 100) that reaches the same scorer
    /// through the static <c>ApparelScoreGain</c>. So the refusal lives at the scorer both paths
    /// share and needs no dependency on either.</para>
    ///
    /// <para>The rule is a comparison, never an equivalence test. A pawn whose parked suit is
    /// worn through should go and find another, and loadout mods already arrange that:
    /// Compositable Loadouts' own item filter carries a hit-point range, so a garment below it
    /// stops matching, loses its weight and scores far lower. So the question is whether
    /// something parked still scores at least as well as the candidate, asked through the same
    /// scorer that produced the candidate's number, so every weighting applies.</para>
    ///
    /// <para>Both sides are re-scored rather than one compared against <c>__result</c>: two mods'
    /// postfixes on one method run in an order neither mod controls, so a neighbour whose
    /// postfix has not run yet would hand us an unweighted candidate to compare against a
    /// weighted parked garment, which suppresses nearly everything, the worn-through case
    /// included. Scoring both through a nested call puts them on the same footing whatever the
    /// order.</para>
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_OptimizeApparel), nameof(JobGiver_OptimizeApparel.ApparelScoreRaw))]
    public static class Patch_ParkedKitOutscoresSpares
    {
        /// <summary>
        /// The magnitude vanilla's <c>ApparelScoreGain</c> uses to refuse a garment outright. Deep
        /// enough to survive a neighbour's postfix multiplying it, and then the factor of ten
        /// <c>ApparelScoreGain</c> applies when a candidate replaces nothing worn.
        /// </summary>
        internal const float RefusedScore = -1000f;

        /// <summary>
        /// Set while the postfix calls the scorer itself, so the nested call does not recurse into
        /// this postfix. A neighbour's postfix still runs on it, which is what makes the two sides
        /// comparable. Thread-static because nothing promises the scorer is only reached from the
        /// main thread, and a miss would recurse without bound inside a job giver.
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

            // The same method builds the worn-score cache, which ApparelScoreGain subtracts.
            // Refusing a worn garment here would inflate every candidate instead of
            // suppressing one.
            if (ap.Wearer != null)
            {
                return;
            }

            // Already refused, by vanilla or a neighbour.
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
                        // Only a parked garment that would displace the candidate answers it: a
                        // helmet says nothing about whether the pawn needs a chest piece.
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
                // Fail open. This scorer runs for every candidate on every optimize pass, and
                // a throw would stop the pawn dressing at all, where the wrong suit is only a
                // nuisance.
                reentered = false;
                Log.ErrorOnce("[ShiftChange] parked-kit scoring threw for "
                              + pawn.LabelShort + ", letting the score stand: " + e,
                              "ShiftChange.ParkedKit".GetHashCode());
            }
        }
    }
}
