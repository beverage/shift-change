using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Keeps haulers out of a deposit-only stand. Such a stand takes in what
    /// the sleep change parks on it, and nothing else.
    ///
    /// <para><b>Vanilla does not merely permit stocking an outfit stand, it
    /// goes looking.</b> <c>OutfitStandBase</c> ships
    /// <c>defaultStorageSettings</c> at priority <c>Important</c>, above an
    /// ordinary stockpile, so an EMPTY stand outbids the shelf a garment is
    /// already sitting on: haulers walk apparel out of storage and into the
    /// stand until <c>HasRoomForApparelOfDef</c> refuses, one garment per body
    /// part group. On a display stand that is the feature. On a deposit-only
    /// stand it is the exact inverse of the point — empty is the correct
    /// resting state, every slot a hauler fills is a slot the colonist's own
    /// armour cannot land in, and
    /// <see cref="Patch_AllowRemovingToggle"/> holds <c>allowRemovingItems</c>
    /// off while the stand is in service, so what a hauler puts in cannot be
    /// hauled back out.</para>
    ///
    /// <para><b>Which is why it presents as one cursed stand.</b> Found in play
    /// 2026-09-08 on exactly one of three identically configured stands, and
    /// the difference was their CONTENTS: <c>Accepts</c> ends at
    /// <c>HasRoomForApparelOfDef</c>, which refuses anything conflicting with
    /// what the stand already holds (<c>Building_OutfitStand.cs:332</c>), so a
    /// stand with a spare outfit parked on it is immune — every slot is taken.
    /// The other two held an alternate gear set. The third was deposit-only,
    /// whose resting state is empty, and it had a duplicate of its owner's
    /// armour sitting in a Normal-priority stockpile for the stand to outbid.
    /// Both conditions are needed, neither is visible in the stand's settings,
    /// and deposit-only is the one mode that guarantees the first one forever.
    /// A player diagnosing this by comparing configurations finds nothing.</para>
    ///
    /// <para><b>One question gates the automatic path and nothing else.</b>
    /// <c>StoreUtility.TryFindBestBetterNonSlotGroupStorageFor</c> skips any
    /// destination whose <c>HaulDestinationEnabled</c> is false
    /// (<c>StoreUtility.cs:252</c>), and that property is
    /// <c>Building_OutfitStand</c>'s one unconditional <c>true</c>
    /// (<c>Building_OutfitStand.cs:100</c>). Answering false removes the stand
    /// from the search; the two EXPLICIT player routes — the "put apparel on
    /// stand" targeter (<c>Building_OutfitStand.cs:675</c>) and
    /// <c>FloatMenuOptionProvider_DressOtherPawn</c> — build the
    /// <c>PutApparelOnOutfitStand</c> job directly and never consult it, so an
    /// ordered delivery still works. Refusing those too would have made the
    /// stand unstockable by any means, and the player asking for a specific
    /// garment on a specific stand is not the problem being solved.</para>
    ///
    /// <para><b>Nothing needs invalidating when the mode changes.</b> The
    /// engine reads this property inside the search loop rather than caching it
    /// — its only two readers are <c>StoreUtility.cs:193</c> (slot group
    /// parents, which a stand is not) and <c>:252</c> — so
    /// <c>haulDestinationManager</c> keeps listing the stand and simply passes
    /// over it. Ticking deposit-only on or off therefore lands on the next haul
    /// search with no lister to drive, which matters because the answer is
    /// keyed on <see cref="CompShiftStand.DepositOnly"/> and that gates itself
    /// on <c>HandlesRest()</c>: carry the stand out of the bedroom and it is
    /// ordinary storage again, immediately.</para>
    ///
    /// <para><b>Stands already stocked heal themselves.</b> This stops future
    /// deliveries and moves nothing, so a stand a hauler filled before the fix
    /// stays filled — until the next sleep change, where
    /// <c>TryDropThingsToMakeRoomForThingOfDef</c>
    /// (<c>JobDriver_SwapAtStand.cs:384</c>) evicts whatever is in the way of
    /// the deposit. The difference is where the evicted garment goes: back to a
    /// stockpile now, rather than straight back onto the stand it was just
    /// dropped from.</para>
    ///
    /// <para><b>The target is named as a string because it has no C# name.</b>
    /// An explicit interface implementation is emitted under its qualified
    /// name, verified against the shipped assembly's metadata rather than
    /// assumed: <c>RimWorld.IHaulDestination.get_HaulDestinationEnabled</c>.
    /// That is the same problem a private method poses and gets the same
    /// answer here as in <see cref="Patch_OptimizeApparelOnShift"/>, which
    /// names <c>TryGiveJob</c> the same way. An earlier version resolved it
    /// through <c>GetInterfaceMap</c> instead; that survives one failure this
    /// does not, Ludeon making the implementation implicit, and fails
    /// identically on every other, for forty lines and a patch class shaped
    /// like nothing else in the mod.</para>
    ///
    /// <para>A target that stops resolving throws out of <c>PatchAll</c> and
    /// takes the rest of the mod's patches with it. That is true of every
    /// patch in this mod, not just this one, and wants a single answer for all
    /// thirteen rather than a private guard on the newest.</para>
    ///
    /// <para><c>Building_KidOutfitStand</c> and Outfit Stands Plus' powered
    /// stands inherit the implementation, so one patch covers the family.</para>
    /// </summary>
    [HarmonyPatch(typeof(Building_OutfitStand), "RimWorld.IHaulDestination.get_HaulDestinationEnabled")]
    public static class Patch_DepositOnlyHauling
    {
        // ReSharper disable once InconsistentNaming — Harmony injection.
        public static void Postfix(Building_OutfitStand __instance, ref bool __result)
        {
            if (!__result)
            {
                return;
            }
            CompShiftStand comp = __instance?.TryGetComp<CompShiftStand>();
            if (comp != null && comp.DepositOnly)
            {
                __result = false;
            }
        }
    }
}
