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

            // The deposit-only stand issues nothing, so it cannot go through
            // the pass below — which is built around "what could this pawn put
            // on" and returns false the moment the answer is nothing. See
            // BuildDeposit for why that is a whole separate plan rather than a
            // flag threaded through this one.
            if (IsDepositOnly(stand))
            {
                return BuildDeposit(pawn, stand, toStore);
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
        /// Read from the stand for the same reason as <see cref="IsFullChange"/>
        /// — one source, so the selector and the driver cannot disagree.
        /// <see cref="CompShiftStand.DepositOnly"/> is itself gated on the
        /// stand being a SLEEP stand, which is what keeps the undress-only
        /// plan out of the work and recreation arms.
        /// </summary>
        internal static bool IsDepositOnly(Building_OutfitStand stand)
        {
            return stand?.TryGetComp<CompShiftStand>()?.DepositOnly == true;
        }

        /// <summary>
        /// The plan for a stand that hands nothing out: the pawn parks the
        /// garments this stand's STORAGE FILTER accepts and keeps the rest on.
        ///
        /// <para><b>The filter is the whole control surface</b>, deliberately.
        /// The case this exists for is "park the power armour before bed"
        /// (decided 2026-09-02), and "which garments" is a question vanilla
        /// already asks on every outfit stand, with a UI the player knows. A
        /// second mod-side list would be the same question asked twice, and
        /// the two would drift.</para>
        ///
        /// <para><b>A fresh stand accepts almost everything, so the filter is
        /// something the player must NARROW, not something they must fill in.</b>
        /// This comment claimed the opposite until 2026-09-03, and the claim
        /// was checked against <c>Building_OutfitStand</c>'s own def rather
        /// than its parent: <c>OutfitStandBase</c> carries a
        /// <c>defaultStorageSettings</c> allowing the whole Apparel category
        /// minus ApparelUtility and Weapons, and <c>PostMake</c> copies it.
        /// The safety of this path therefore rests ENTIRELY on
        /// <see cref="WouldBeNude"/> below, not on an empty filter — an
        /// out-of-the-box deposit-only stand would otherwise take a
        /// colonist's shirt, trousers and armour and leave them in the shield
        /// belt its default filter happens to exclude.</para>
        ///
        /// <para>Conflicts with whatever the stand already holds are NOT
        /// resolved here, matching the dress path: the driver calls
        /// <c>TryDropThingsToMakeRoomForThingOfDef</c> before each PutBack, so
        /// eviction is vanilla's and happens once, at transfer time.</para>
        /// </summary>
        /// <returns>true if anything would be deposited.</returns>
        internal static bool BuildDeposit(Pawn pawn, Building_OutfitStand stand, List<Apparel> toStore)
        {
            StorageSettings settings = stand.GetStoreSettings();
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                Apparel item = worn[i];
                if (item == null)
                {
                    continue;
                }
                // Vanilla's own gate, the same one the conflict pass and
                // vanilla's stand driver apply (JobDriver_UseOutfitStand.cs:49-53).
                if (pawn.apparel.IsLocked(item))
                {
                    continue;
                }
                // Both halves of "would this stand take it": the player's
                // filter, and the def's fixed settings underneath it — a
                // modded stand may refuse a category outright, and depositing
                // into one would only bounce the garment back onto the floor.
                if (settings != null && !settings.AllowedToAccept(item))
                {
                    continue;
                }
                if (!stand.CanEverStoreThing(item))
                {
                    continue;
                }
                toStore.Add(item);
            }

            if (toStore.Count == 0)
            {
                return false;
            }

            // NEVER STRIP THEM BARE. The standing rule on every path through
            // this file is that the worst case is a pawn in the WRONG clothes,
            // never a pawn without them.
            //
            // This asked "is ANY garment left on?" until 2026-09-03, and a
            // garment count is the wrong question: utility apparel — a shield
            // belt, a smokepop belt, a jump pack — covers no body part at all,
            // and is exactly what the stand's DEFAULT filter excludes. So a
            // marine in armour, helmet and shield belt satisfied the old test
            // with the belt alone and went to bed in nothing else.
            //
            // Declining outright rather than holding a garment back is still
            // deliberate: which garment to keep is a judgment this code has no
            // basis to make, and a stand that quietly deposits all-but-one is
            // harder to diagnose than one that plainly does nothing.
            if (WouldBeNude(pawn, toStore))
            {
                toStore.Clear();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Would depositing <paramref name="leaving"/> make this pawn
        /// psychologically nude?
        ///
        /// <para>A transcription of <c>Pawn_ApparelTracker.PsychologicallyNude</c>
        /// (<c>:186-228</c>) and the <c>HasBasicApparel</c> it calls
        /// (<c>:667-688</c>), evaluated against the apparel that would REMAIN
        /// rather than against what is worn now. Vanilla's own standard is
        /// used rather than a stricter one on purpose — requiring both a
        /// covered torso and covered legs would refuse the ordinary case of a
        /// man in trousers and armour, which vanilla is perfectly happy with,
        /// and a rule that blocks the feature's main use case is not a safety
        /// rule.</para>
        ///
        /// <para>Transcribed rather than called because the engine's version
        /// asks about <c>wornApparel</c> and the question here is
        /// hypothetical. Keep the two in step: this is what decides whether a
        /// colonist takes the Naked thought every night for the rest of the
        /// game.</para>
        /// </summary>
        internal static bool WouldBeNude(Pawn pawn, List<Apparel> leaving)
        {
            // Vanilla's two exemptions, in its order.
            if (pawn.gender == Gender.None || pawn.IsWildMan())
            {
                return false;
            }

            bool hasShirt = false;
            bool hasPants = false;
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count && !(hasShirt && hasPants); i++)
            {
                Apparel item = worn[i];
                if (item == null || leaving.Contains(item))
                {
                    continue;
                }
                List<BodyPartGroupDef> groups = item.def?.apparel?.bodyPartGroups;
                if (groups == null)
                {
                    continue;
                }
                for (int j = 0; j < groups.Count; j++)
                {
                    if (groups[j] == BodyPartGroupDefOf.Torso)
                    {
                        hasShirt = true;
                    }
                    else if (groups[j] == BodyPartGroupDefOf.Legs)
                    {
                        hasPants = true;
                    }
                }
            }

            // A pawn with no legs left cannot be trouserless (vanilla, :198-213).
            if (!hasPants)
            {
                bool anyLegs = false;
                foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
                {
                    if (part.IsInGroup(BodyPartGroupDefOf.Legs))
                    {
                        anyLegs = true;
                        break;
                    }
                }
                if (!anyLegs)
                {
                    hasPants = true;
                }
            }

            return pawn.gender == Gender.Male ? !hasPants : !hasPants || !hasShirt;
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
