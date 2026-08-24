using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Keeps a shift stand's contents out of trade windows.
    ///
    /// <para><b>Vanilla puts them there, by two separate routes.</b> An orbital
    /// ship's goods come from <c>TradeUtility.AllLaunchableThingsForTrade</c>,
    /// which special-cases <c>Building_OutfitStand</c> and yields its
    /// <c>HeldItems</c> (<c>TradeUtility.cs:123</c>). A visiting caravan's come
    /// from <c>Pawn_TraderTracker.ColonyThingsWillingToBuy</c>, which walks
    /// <c>AllColonistBuildingsOfType&lt;IHaulSource&gt;()</c> and yields
    /// everything each one directly holds
    /// (<c>Pawn_TraderTracker.cs:123-134</c>). Neither consults
    /// <c>allowRemovingItems</c>: the lister keys on TYPE, so a stand whose
    /// <c>HaulSourceEnabled</c> is false is enumerated anyway, and
    /// <see cref="Patch_AllowRemovingToggle"/>'s enforcement — which does hold
    /// the optimizer off — is simply not in this story.</para>
    ///
    /// <para><b>One choke point serves both.</b> Every route funnels into
    /// <c>TradeDeal.AddAllTradeables</c>, which re-tests each candidate with
    /// <c>PlayerSellableNow</c> and drops it on false (<c>TradeDeal.cs:46-50</c>)
    /// — before the item ever becomes a <c>Tradeable</c>, so it does not appear
    /// greyed, it does not appear at all. Patching there rather than at the two
    /// collectors also covers gift mode, which shares the same deal.</para>
    ///
    /// <para><b>Nothing outside trade sees this.</b> Every caller of
    /// <c>PlayerSellableNow</c> in the engine is trade-side (the two collectors
    /// above and the deal itself); the def-level <c>EverPlayerSellable</c>, which
    /// <c>StatWorker</c> and <c>Dialog_SellableItems</c> use, is a different
    /// method and is untouched. Caravan packing, hauling, raider theft and the
    /// outfit optimizer all run through other code entirely — this withholds the
    /// stand's kit from traders and does nothing else.</para>
    ///
    /// <para><b>The failure mode is vanilla.</b> A postfix on a public static
    /// that has kept this signature across versions, doing nothing unless it
    /// finds our comp: if the method moves, the patch fails to apply, is logged
    /// by Harmony, and stands go back to being tradeable — which is exactly
    /// where they started.</para>
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
