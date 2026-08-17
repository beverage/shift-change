using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Makes the stand's "Allow removing items" toggle tell the truth once
    /// Shift Change is installed.
    ///
    /// <para><b>The toggle does not do what a player reasonably assumes it
    /// does here.</b> Vanilla's own description implies it governs whether
    /// pawns may take things off the stand, and under this mod that misleads
    /// in both directions: players turn it ON believing shift changes need it,
    /// and fear turning it OFF in case that breaks them. Neither is true.
    /// <c>allowRemovingItems</c> gates only <c>IHaulSource.HaulSourceEnabled</c>
    /// and <c>IApparelSource.ApparelSourceEnabled</c>
    /// (<c>Building_OutfitStand.cs:102,104</c>) — hauling out, and the apparel
    /// optimizer's view of the contents. <c>IHaulDestination</c> is
    /// unconditionally true (<c>:100</c>), so stocking always works, and our
    /// swap takes kit through <c>RemoveApparel</c>, a bare
    /// <c>innerContainer.Remove</c> with no toggle check, so changing ignores
    /// it entirely.</para>
    ///
    /// <para>OFF — the default — is therefore already the correct shift
    /// configuration, and ON is the misconfiguration: it exposes an idle
    /// stand's contents to every colonist's apparel optimizer, which may
    /// simply adopt the uniform as daywear. The optimizer pause protects only
    /// the pawn currently on shift and the reservation only an active
    /// borrower, so neither covers an idle stand.</para>
    ///
    /// <para><b>Text is APPENDED to vanilla's, not substituted for it.</b>
    /// Vanilla's half then stays correct and stays translated in every
    /// language the game ships; only the added paragraph is ours to
    /// localise.</para>
    ///
    /// <para>UI only — no behaviour changes here, deliberately. Vanilla's
    /// toggle keeps doing exactly what it did, which is also why this can be
    /// a postfix on the building rather than anything structural. It matches
    /// the toggle by its vanilla LABEL because the command is an anonymous
    /// <c>Command_Toggle</c> with no other handle; if Ludeon ever renames that
    /// key the match simply stops finding it and the tooltip reverts to
    /// vanilla's, which is a graceful failure rather than a broken one.</para>
    /// </summary>
    [HarmonyPatch(typeof(Building_OutfitStand), nameof(Building_OutfitStand.GetGizmos))]
    public static class Patch_AllowRemovingTooltip
    {
        // ReSharper disable once InconsistentNaming — Harmony injection.
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values,
                                                 Building_OutfitStand __instance)
        {
            // NOT merely "does this stand carry our comp" — the XML patch adds
            // it to the vanilla ThingDef, so every outfit stand in the game
            // does. The question is whether this stand PARTICIPATES, and the
            // comp already answers it: WorkTypes is empty when the stand is
            // excluded or its room has no role, and its own contract calls
            // that "inert — an ordinary vanilla outfit stand".
            //
            // Staying quiet there matches what the mod already does elsewhere
            // (CompInspectStringExtra returns null for the same case) and
            // honours the narrower promise: a stand the player has not put to
            // work behaves exactly as it does in vanilla, tooltip included.
            CompShiftStand comp = __instance?.TryGetComp<CompShiftStand>();
            bool ours = comp != null && comp.WorkTypes.Count > 0;
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
                }
                yield return gizmo;
            }
        }
    }
}
