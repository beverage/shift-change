using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Keeps a shift stand's contents out of trade windows, by refusing them at
    /// <c>PlayerSellableNow</c>: <c>TradeDeal.AddAllTradeables</c> re-tests
    /// every candidate from both of vanilla's trade routes there, so they are
    /// absent rather than greyed, and nothing outside trade calls it. The two
    /// routes, why the removal flag is no defence, and why the failure mode is
    /// vanilla: docs/DESIGN.md, "Withholding from trade".
    /// </summary>
    [HarmonyPatch(typeof(TradeUtility), nameof(TradeUtility.PlayerSellableNow))]
    public static class Patch_WithholdFromTrade
    {
        /// <param name="t">
        /// The argument slot as it stands at method exit, which vanilla
        /// reassigns on its first line (<c>t = t.GetInnerIfMinified()</c>). It
        /// makes no difference here — apparel is not minifiable, and a
        /// minified thing's inner item is held by the <c>MinifiedThing</c>, not
        /// by a stand — so the unwrapped value answers the same question.
        /// </param>
        // ReSharper disable once InconsistentNaming — Harmony injection.
        public static void Postfix(Thing t, ref bool __result)
        {
            if (!__result)
            {
                return;
            }
            // ParentHolder, not PositionHeld: the items in question are
            // unspawned inside the stand's innerContainer, and their holder IS
            // the building (which is how TradeDeal.InSellablePosition identifies
            // them too). A loose item on the floor answers Map here and fails
            // the type test immediately, so the common case costs one branch.
            if (!(t?.ParentHolder is Building_OutfitStand stand))
            {
                return;
            }
            CompShiftStand comp = stand.TryGetComp<CompShiftStand>();
            if (comp != null && comp.BlocksTrade)
            {
                __result = false;
            }
        }
    }
}
