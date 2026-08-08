using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// What a swap at a given stand would actually move, for a given pawn.
    ///
    /// This exists as one shared function because it was briefly two, and they
    /// disagreed: the stand selector asked "does this hold any apparel?" while
    /// the job driver asked the real questions, so a rack holding a garment
    /// this particular pawn could not wear was selected, walked to, and swapped
    /// with — moving nothing. From the player's side that is indistinguishable
    /// from a pawn swapping at an empty rack (found in play, 2026-08-07).
    ///
    /// Any predicate about wearability belongs here and nowhere else.
    /// </summary>
    internal static class SwapPlan
    {
        private static readonly List<Apparel> ScratchWear = new List<Apparel>();
        private static readonly List<Apparel> ScratchStore = new List<Apparel>();

        /// <summary>
        /// Fills <paramref name="toWear"/> with what the pawn would take off the
        /// stand and <paramref name="toStore"/> with what they would take off
        /// themselves to make room.
        /// </summary>
        /// <returns>true if the swap would move anything at all.</returns>
        public static bool BuildDress(Pawn pawn, Building_OutfitStand stand,
                                      List<Apparel> toWear, List<Apparel> toStore)
        {
            toWear.Clear();
            toStore.Clear();

            if (pawn?.apparel == null || stand == null)
            {
                return false;
            }

            List<Apparel> worn = pawn.apparel.WornApparel;
            IReadOnlyList<Thing> held = stand.HeldItems;

            for (int i = 0; i < held.Count; i++)
            {
                Apparel candidate = held[i] as Apparel;
                if (candidate == null
                    || !candidate.PawnCanWear(pawn)
                    || !ApparelUtility.HasPartsToWear(pawn, candidate.def)
                    || (CompBiocodable.IsBiocoded(candidate) && !CompBiocodable.IsBiocodedFor(candidate, pawn)))
                {
                    continue;
                }

                bool canTake = true;
                int storeCountBefore = toStore.Count;
                for (int j = 0; j < worn.Count; j++)
                {
                    Apparel wornItem = worn[j];
                    if (ApparelUtility.CanWearTogether(candidate.def, wornItem.def, pawn.RaceProps.body))
                    {
                        continue;
                    }
                    // Vanilla's own gate, and the only one it applies
                    // (JobDriver_UseOutfitStand.cs:49-53).
                    if (pawn.apparel.IsLocked(wornItem))
                    {
                        canTake = false;
                        break;
                    }
                    if (!toStore.Contains(wornItem))
                    {
                        toStore.Add(wornItem);
                    }
                }

                if (!canTake)
                {
                    // Undo anything this candidate provisionally displaced —
                    // it is not coming off after all.
                    toStore.RemoveRange(storeCountBefore, toStore.Count - storeCountBefore);
                    continue;
                }
                if (!toWear.Contains(candidate))
                {
                    toWear.Add(candidate);
                }
            }

            return toWear.Count > 0;
        }

        /// <summary>
        /// Cheap yes/no for the stand selector: would sending this pawn here
        /// accomplish anything? Uses shared scratch lists — safe because the
        /// selector runs to completion before any job is started.
        /// </summary>
        public static bool WouldDress(Pawn pawn, Building_OutfitStand stand)
        {
            return BuildDress(pawn, stand, ScratchWear, ScratchStore);
        }
    }
}
