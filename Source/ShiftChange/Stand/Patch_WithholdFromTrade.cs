using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Keeps a shift stand's contents out of trade windows by refusing them at
    /// <c>PlayerSellableNow</c>. <c>TradeDeal.AddAllTradeables</c> re-tests every candidate from
    /// both of vanilla's trade routes there, so refused items are absent rather than greyed, and
    /// nothing outside trade calls it. Like any attribute patch here, a target that stops resolving
    /// throws out of <c>PatchAll</c> (<see cref="Patch_DepositOnlyHauling"/>). The two routes, and
    /// why the removal toggle is no defence: docs/DESIGN.md, "Withholding from trade".
    /// </summary>
    [HarmonyPatch(typeof(TradeUtility), nameof(TradeUtility.PlayerSellableNow))]
    public static class Patch_WithholdFromTrade
    {
        /// <param name="t">
        /// The argument as it stands at method exit: vanilla's first line reassigns it to the
        /// unwrapped inner item (<c>t = t.GetInnerIfMinified()</c>). That answers the same
        /// question here, since apparel is not minifiable and a minified thing's inner item is
        /// held by the <c>MinifiedThing</c>, not by a stand.
        /// </param>
        // ReSharper disable once InconsistentNaming — Harmony injection.
        public static void Postfix(Thing t, ref bool __result)
        {
            if (!__result)
            {
                return;
            }
            // ParentHolder, not PositionHeld: held items are unspawned inside the stand's
            // innerContainer, so their holder is the building (TradeDeal.InSellablePosition
            // identifies them the same way). A loose item's holder is a Map, which fails the
            // type test at once.
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
