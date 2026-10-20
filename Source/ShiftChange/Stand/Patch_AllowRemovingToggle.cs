using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Governs the stand's "Allow removing items" toggle while the stand is in service: the
    /// tooltip says what the toggle exposes, and turning it on is refused.
    ///
    /// <para>The toggle gates only <c>IHaulSource.HaulSourceEnabled</c> and
    /// <c>IApparelSource.ApparelSourceEnabled</c>: hauling out, and the apparel optimizer's view
    /// of the contents. <c>IHaulDestination</c> is unconditionally enabled, so stocking always
    /// works, and our swap takes kit through <c>RemoveApparel</c>, a bare
    /// <c>innerContainer.Remove</c> with no toggle check, so changing ignores the toggle. Off,
    /// the default, is the right setting for a stand in service.</para>
    ///
    /// <para>Ownership is no protection. <c>JobGiver_OptimizeApparel</c> checks
    /// <c>ApparelSourceEnabled</c> and nothing else (no assignable comp, no owner), so with the
    /// toggle on, any colonist's optimizer may take clothes off a stand someone else owns.</para>
    ///
    /// <para>The disable is asymmetric. While the stand is declared in service (its "Not used for
    /// shift changes" switch is off), the toggle is disabled with a reason only when it is off,
    /// so the harmful transition is unreachable, while a toggle already on stays live and the
    /// way out of that state never closes. The test is the declaration, not the resolved work
    /// types: a declared stand in a roleless room is ours and currently idle, and its uniform
    /// needs the same protection. Since the disable never empties the on state,
    /// <see cref="CompShiftStand.EnforceRemovalFlag"/> forces the flag off on spawn and at every
    /// entry into service, through <see cref="EnforceOffInService"/> below.</para>
    ///
    /// <para>The tooltip appends to vanilla's text, so vanilla's half stays translated in every
    /// language the game ships. The toggle is found by its vanilla label, since the command is an
    /// anonymous <c>Command_Toggle</c> with no other handle; if that label changes, the match
    /// finds nothing and the patch falls back to vanilla.</para>
    /// </summary>
    [HarmonyPatch(typeof(Building_OutfitStand), nameof(Building_OutfitStand.GetGizmos))]
    public static class Patch_AllowRemovingToggle
    {
        // ReSharper disable once InconsistentNaming — Harmony injection.
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values,
                                                 Building_OutfitStand __instance)
        {
            // Every stand in the family carries our comp (the XML patch adds it to each def), so
            // the test is whether the stand is declared in service; the excluded switch is the
            // player's answer.
            CompShiftStand comp = __instance?.TryGetComp<CompShiftStand>();
            if (comp == null || comp.IsExcluded)
            {
                // Nothing to rewrite, so skip wrapping every gizmo in an enumerator for as long
                // as the stand stays selected.
                return values;
            }
            return WithGovernedToggle(values);
        }

        internal static IEnumerable<Gizmo> WithGovernedToggle(IEnumerable<Gizmo> values)
        {
            string vanillaLabel = "CommandAllowRemovingApparel".Translate().ToString();

            foreach (Gizmo gizmo in values)
            {
                if (gizmo is Command_Toggle toggle && toggle.defaultLabel == vanillaLabel)
                {
                    // RawText on both halves: TaggedString's implicit conversion to string strips
                    // the rich-text tags, so assigning a Translate() result straight to this
                    // string field would silently delete the tooltip's colour and bold.
                    toggle.defaultDesc = "CommandAllowRemovingApparelDesc".Translate().RawText
                                         + "\n\n" + "ShiftChange.AllowRemovingDesc".Translate().RawText;
                    if (toggle.isActive != null && !toggle.isActive())
                    {
                        // Only an off toggle is disabled; an on one stays live so it can be turned
                        // off. Disabled rather than hidden, which would strand the on state.
                        toggle.Disable("ShiftChange.AllowRemovingDisabled".Translate());
                    }
                }
                yield return gizmo;
            }
        }

        /// <summary>
        /// Write access to vanilla's private toggle field, looked up by name. Null when the
        /// field is not found, in which case <see cref="EnforceOffInService"/> does nothing.
        /// </summary>
        internal static readonly AccessTools.FieldRef<Building_OutfitStand, bool> AllowRemovingItemsRef
            = ResolveAllowRemovingItems();

        internal static AccessTools.FieldRef<Building_OutfitStand, bool> ResolveAllowRemovingItems()
        {
            FieldInfo field = AccessTools.DeclaredField(typeof(Building_OutfitStand), "allowRemovingItems");
            return field == null
                ? null
                : AccessTools.FieldRefAccess<Building_OutfitStand, bool>(field);
        }

        /// <summary>
        /// Turns the toggle off on a stand in service. The caller owns the "is it in service"
        /// test, so <see cref="CompShiftStand.EnforceRemovalFlag"/> is the only thing that should
        /// call this.
        ///
        /// <para>Nothing else in this mod clears the flag: our swaps never call vanilla's
        /// <c>SetAllowHauling</c>, and vanilla clears the flag only on a player-ordered swap, so
        /// a stand driven purely automatically would stay exposed. The flag can already be on in
        /// a save from before the disable, or after the player set the stand to "Not used for
        /// shift changes" to unlock it and put it back, and nothing on the inspect pane shows
        /// it.</para>
        ///
        /// <para>Overriding an explicit click is the deliberate cost: off is the documented
        /// setting for a stand in service, and the published release notes promise that the
        /// toggle holds itself off while the stand is in service.</para>
        ///
        /// <para>A bare field write, not vanilla's <c>SetAllowHauling</c>: that setter also
        /// recalculates <c>listerHaulables</c>, and a storage recalculation from inside
        /// <c>Map.FinalizeLoading</c> can break a load. <c>ListerHaulables.HaulSourcesCheckTick</c>
        /// re-derives every haul source on a rolling basis, so the cached list converges on its
        /// own within a tick or two.</para>
        /// </summary>
        internal static void EnforceOffInService(Building_OutfitStand stand)
        {
            if (stand == null || AllowRemovingItemsRef == null)
            {
                return;
            }
            if (AllowRemovingItemsRef(stand))
            {
                AllowRemovingItemsRef(stand) = false;
            }
        }
    }
}
