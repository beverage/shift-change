using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Keeps haulers from filling a deposit-only stand. Such a stand takes in only what a sleep
    /// change parks on it, and vanilla's haulers would otherwise fill an empty stand from the
    /// colony's shelves. Answering false here removes the stand from their automatic search
    /// only: an ordered delivery builds its job without asking, so it still lands. Why, and why
    /// nothing needs invalidating when the mode changes: docs/DESIGN.md, "The sleep branch".
    ///
    /// <para>The target is a string because an explicit interface implementation has no C#
    /// name; it is emitted under its qualified name,
    /// <c>RimWorld.IHaulDestination.get_HaulDestinationEnabled</c>. A target that stops
    /// resolving throws out of <c>PatchAll</c> and takes the rest of the mod's patches with it,
    /// as it would for any attribute patch here, so a guard belongs on all of them at once, not
    /// on this one.</para>
    ///
    /// <para><c>Building_KidOutfitStand</c> and Outfit Stands Plus' powered stands inherit the
    /// implementation, so one patch covers the family.</para>
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
