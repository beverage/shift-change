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
        internal static readonly List<Apparel> ScratchWear = new List<Apparel>();
        internal static readonly List<Apparel> ScratchStore = new List<Apparel>();

        /// <summary>
        /// Every gate vanilla's own <c>Wear</c> applies, asked BEFORE anything
        /// is committed.
        ///
        /// <c>Pawn_ApparelTracker.Wear</c> is void and bails on each of these —
        /// no body parts, biocoded to someone else, <c>PawnCanWear</c> false —
        /// but it calls <c>newApparel.DeSpawnOrDeselect()</c> first
        /// (<c>Pawn_ApparelTracker.cs:434-450</c>). So a garment that fails one
        /// of them is ALREADY detached when the check fires: taken out of the
        /// stand, never put on the pawn, held by nothing, gone from the world
        /// with a single dev-log warning. Asking in advance is what stops that;
        /// the driver verifies the outcome afterwards regardless, because the
        /// pawn can lose a body part during the walk.
        /// </summary>
        public static bool CanWear(Pawn pawn, Apparel apparel)
        {
            return apparel != null
                   && pawn?.apparel != null
                   && apparel.PawnCanWear(pawn)
                   && ApparelUtility.HasPartsToWear(pawn, apparel.def)
                   && !(CompBiocodable.IsBiocoded(apparel) && !CompBiocodable.IsBiocodedFor(apparel, pawn));
        }

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
                if (!CanWear(pawn, candidate))
                {
                    continue;
                }

                bool canTake = true;

                // Against candidates already accepted this pass, not only the
                // worn list. The stand's one-outfit invariant normally makes
                // two conflicting garments impossible to stock — but that
                // invariant lives in Building_OutfitStand's own code
                // (HasRoomForApparelOfDef), not here, and if a storage mod
                // relaxes it, Wear() would silently drop the earlier garment
                // on the floor. Checked first because it needs no
                // displacement bookkeeping to roll back.
                for (int k = 0; k < toWear.Count; k++)
                {
                    if (!ApparelUtility.CanWearTogether(candidate.def, toWear[k].def, pawn.RaceProps.body))
                    {
                        canTake = false;
                        break;
                    }
                }
                if (!canTake)
                {
                    continue;
                }

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

            // Full change: everything else comes off too, so the pawn's
            // insulation becomes entirely the stand's kit rather than the kit
            // layered over their own clothes.
            //
            // Gated on toWear being non-empty: a stand that would issue NOTHING
            // must still decline, so the selector never sends a pawn on a trip
            // that cannot dress them.
            //
            // This is not the guard that keeps anyone clothed — DoTransfer
            // refuses independently, returning before a garment comes off when
            // the incoming set is empty in the dress direction. Undressing into
            // a rack is a coherent action, but not one any pawn should reach by
            // deciding to go do some hauling; it would need its own trigger.
            //
            // IsLocked is the one garment that stays, matching the conflict
            // pass above and vanilla's own stand driver
            // (JobDriver_UseOutfitStand.cs:49-53).
            if (toWear.Count > 0 && IsFullChange(stand))
            {
                for (int j = 0; j < worn.Count; j++)
                {
                    Apparel wornItem = worn[j];
                    if (!pawn.apparel.IsLocked(wornItem) && !toStore.Contains(wornItem))
                    {
                        toStore.Add(wornItem);
                    }
                }
            }

            return toWear.Count > 0;
        }

        /// <summary>
        /// Read from the stand rather than passed in by the caller, so the
        /// selector and the driver cannot form different plans for the same
        /// stand — the disagreement this whole file exists to prevent.
        /// </summary>
        internal static bool IsFullChange(Building_OutfitStand stand)
        {
            return stand?.TryGetComp<CompShiftStand>()?.FullChange == true;
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
