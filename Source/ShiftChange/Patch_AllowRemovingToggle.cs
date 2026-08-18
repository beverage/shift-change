using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Governs the stand's "Allow removing items" toggle while the stand is
    /// in service: the tooltip tells the truth, and the harmful transition
    /// is refused.
    ///
    /// <para><b>The toggle does not do what a player reasonably assumes.</b>
    /// <c>allowRemovingItems</c> gates only <c>IHaulSource.HaulSourceEnabled</c>
    /// and <c>IApparelSource.ApparelSourceEnabled</c>
    /// (<c>Building_OutfitStand.cs:102,104</c>) — hauling out, and the apparel
    /// optimizer's view of the contents. <c>IHaulDestination</c> is
    /// unconditionally true (<c>:100</c>), so stocking always works, and our
    /// swap takes kit through <c>RemoveApparel</c>, a bare
    /// <c>innerContainer.Remove</c> with no toggle check, so changing ignores
    /// it entirely. OFF — the default — is the correct configuration for a
    /// stand in service.</para>
    ///
    /// <para><b>And ownership is provably no protection.</b>
    /// <c>JobGiver_OptimizeApparel:147-154</c> is the whole gate on the
    /// raid path: it checks <c>ApparelSourceEnabled</c> and nothing else —
    /// no assignable comp, no owner, nothing of ours. With the toggle on,
    /// ANY colonist's optimizer may take clothes off a stand explicitly
    /// owned by someone else, and setting an owner changes nothing. A
    /// control that looks like protection and is not produces reports that
    /// look like mod bugs; the tooltip alone did not stop the first one.</para>
    ///
    /// <para><b>So the disable is asymmetric, and that is the design.</b>
    /// While the stand is DECLARED in service (its "Not used for shift
    /// changes" switch off), the toggle is disabled with a reason only when
    /// it is OFF — the harmful transition becomes unreachable. A toggle
    /// already ON stays fully live, so the way out of the harmful state
    /// never closes. A stand declared not-ours is untouched vanilla. Keyed
    /// on the DECLARATION, not on resolved work types: a declared stand in
    /// a roleless room is "ours, currently idle", and its uniform deserves
    /// the same protection.</para>
    ///
    /// <para><b>Text is APPENDED to vanilla's, not substituted for it.</b>
    /// Vanilla's half stays correct and stays translated in every language
    /// the game ships; only the added paragraph is ours to localise. The
    /// toggle is matched by its vanilla LABEL because the command is an
    /// anonymous <c>Command_Toggle</c> with no other handle; if Ludeon ever
    /// renames that key the match stops finding it and the whole patch
    /// reverts to vanilla behaviour — a graceful failure.</para>
    /// </summary>
    [HarmonyPatch(typeof(Building_OutfitStand), nameof(Building_OutfitStand.GetGizmos))]
    public static class Patch_AllowRemovingToggle
    {
        // ReSharper disable once InconsistentNaming — Harmony injection.
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values,
                                                 Building_OutfitStand __instance)
        {
            // NOT merely "does this stand carry our comp" — the XML patch
            // adds it to every def in the family, so every stand does. The
            // question is whether this stand is DECLARED in service; the
            // excluded switch is the player's explicit answer.
            CompShiftStand comp = __instance?.TryGetComp<CompShiftStand>();
            bool ours = comp != null && !comp.IsExcluded;
            string vanillaLabel = ours ? "CommandAllowRemovingApparel".Translate().ToString() : null;

            foreach (Gizmo gizmo in values)
            {
                if (ours && gizmo is Command_Toggle toggle && toggle.defaultLabel == vanillaLabel)
                {
                    // .RawText on BOTH halves, and it is load-bearing.
                    // TaggedString's implicit conversion to string calls
                    // StripTags() (TaggedString.cs:120-123), so assigning a
                    // Translate() result straight into this string field
                    // silently deletes the rich-text markup — no error, no
                    // literal tags on screen, just plain text. That ate the
                    // colour and bold on this very tooltip once (2026-08-17).
                    toggle.defaultDesc = "CommandAllowRemovingApparelDesc".Translate().RawText
                                         + "\n\n" + "ShiftChange.AllowRemovingDesc".Translate().RawText;
                    if (toggle.isActive != null && !toggle.isActive())
                    {
                        // Off stays off while in service; on keeps its way
                        // back. Disable(), not hiding: a hidden toggle
                        // strands the ON state with no exit, and a greyed
                        // button with a reason is vanilla's own idiom.
                        toggle.Disable("ShiftChange.AllowRemovingDisabled".Translate());
                    }
                }
                yield return gizmo;
            }
        }
    }
}
