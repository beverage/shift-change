#if DEBUG
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// The on-screen answer to "which testing mode am I in?" (decided
    /// 2026-08-08). Debug builds draw a small badge top-right, under
    /// vanilla's dev-mode indicator:
    ///
    ///   cyan:   "lab build — reload armed" — gameplay interception is LIVE
    ///           (identical logic to Release), but the first reload will
    ///           fork the session;
    ///   orange: "LAB MODE — gameplay off · don't save" — the quarantine has
    ///           fired and the session is disposable.
    ///
    /// Release builds compile none of this. No badge IS the live-mode
    /// signal: if you cannot see a badge, you are on the shipping build and
    /// your colony is safe to play and save.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot), nameof(UIRoot.UIRootOnGUI))]
    internal static class Patch_ModeBadge
    {
        /// <summary>Set by OnEditCompileReload when the quarantine fires.</summary>
        internal static bool GameplayDark;

        internal static void Postfix()
        {
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = GameplayDark
                ? new Color(1f, 0.55f, 0.2f)
                : new Color(0.45f, 0.9f, 1f, 0.8f);
            Rect rect = new Rect(UI.screenWidth - 330f, 24f, 320f, 20f);
            Widgets.Label(rect, GameplayDark
                ? "Shift Change LAB MODE — gameplay off · don't save"
                : "Shift Change lab build — reload armed");
            if (Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, GameplayDark
                    ? "A hot reload has fired: all Shift Change gameplay interception is disabled and this session is disposable — do not save. Restart on a Release build for live testing."
                    : "Debug build with hot reload armed. Gameplay runs exactly as Release until the first reload; from then on the session becomes a UI lab. Open a disposable save before iterating.");
            }
            GUI.color = Color.white;
            Text.Anchor = anchor;
            Text.Font = font;
        }
    }
}
#endif
