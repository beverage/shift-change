// SCENES only — see the config table in ShiftChange.csproj. This file is the
// mod's ENTIRE debug-menu surface, and in a shipped build it does not exist,
// so the "Shift Change" category never renders (an empty category is not drawn).
#if SCENES
using System.Collections.Generic;
using LudeonTK;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// One collapsing entry instead of a category of loose ones.
    ///
    /// <para>A <c>[DebugAction]</c> method returning
    /// <c>List&lt;DebugActionNode&gt;</c> is wired up as a <c>childGetter</c>
    /// and rendered as a submenu, with "..." appended to the label
    /// automatically (<c>DebugTabMenu_Actions.GenerateCacheForMethod:60-67</c>).
    /// No custom window is involved — this is the native shape, and it is what
    /// Rimefeller achieves with a hand-built <c>Dialog</c>: one vanilla-menu
    /// entry, everything else nested under it. A custom window would only earn
    /// its keep if an inspector pane were ever wanted beside the buttons.</para>
    ///
    /// <para>The children carry no <c>sourceAttribute</c>, which is fine and
    /// deliberate: <c>DebugActionNode.VisibleNow</c> null-guards it, so the
    /// game-state and Odyssey gating on the parent below is what governs the
    /// whole submenu. <c>ActiveNow</c> still greys a <c>ToolMap</c> child out
    /// when no map is being drawn.</para>
    ///
    /// <para><b>The cost, stated so nobody rediscovers it:</b> the menu's
    /// search box filters ONE LEVEL. <c>DebugTabMenu.VisibleActions</c> is
    /// <c>CurrentNode.children</c> and the filter tests each node's own label,
    /// so nesting these makes them unreachable by typing <c>lifecycle</c> at
    /// the top level — you filter to <c>dev tools</c> first, then open. Three
    /// items behind one entry is the trade made deliberately, and it is paid on
    /// dev builds only.</para>
    ///
    /// <para>Children also lose the automatic <c>"T: "</c> tool prefix, which
    /// <c>GenerateCacheForMethod</c> adds only to attribute-driven nodes. Left
    /// off deliberately: every child here IS a map tool, so the prefix would
    /// mark all three and distinguish none.</para>
    /// </summary>
    internal static class DebugTools_Menu
    {
        [DebugAction("Shift Change", "Dev tools",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap,
            requiresOdyssey = true)]
        internal static List<DebugActionNode> DevTools()
        {
            // ToolMap children need only actionType + action: the node hands
            // the action to a DebugTool and each of these reads UI.MouseCell()
            // itself, exactly as they did when they were top-level actions.
            return new List<DebugActionNode>
            {
                new DebugActionNode("Build demo stage", DebugActionType.ToolMap,
                                    DebugTools_DemoStage.BuildDemoStage),
                new DebugActionNode("Build preview stage", DebugActionType.ToolMap,
                                    DebugTools_PreviewStage.BuildPreviewStage),
                new DebugActionNode("Run lifecycle harness", DebugActionType.ToolMap,
                                    DebugTools_LifecycleHarness.RunHarness),
            };
        }
    }
}
#endif
