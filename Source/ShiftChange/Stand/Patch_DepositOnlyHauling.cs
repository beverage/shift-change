using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Keeps haulers out of a deposit-only stand. Such a stand takes in what
    /// the sleep change parks on it, and nothing else: vanilla's haulers would
    /// otherwise fill an empty stand from the colony's shelves, and answering
    /// false here takes it out of their automatic search only, so an ordered
    /// delivery still lands. Why, how it was found, and why nothing needs
    /// invalidating when the mode changes: docs/DESIGN.md, "The sleep branch".
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
