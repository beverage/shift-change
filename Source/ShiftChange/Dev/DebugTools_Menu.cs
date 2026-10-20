// Compiled only with SCENES (see the configuration table in ShiftChange.csproj). This file holds
// the mod's entire debug-menu surface; a Release build has none of it, so the game draws no
// "Shift Change" category (it does not draw an empty one).
#if SCENES
using System.Collections.Generic;
using LudeonTK;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// The mod's debug actions as one collapsing menu entry, not a category of loose ones.
    ///
    /// <para>A <c>[DebugAction]</c> method returning <c>List&lt;DebugActionNode&gt;</c> is drawn
    /// as a submenu, with "..." appended to its label. The children carry no
    /// <c>sourceAttribute</c> on purpose: <c>DebugActionNode.VisibleNow</c> tolerates a null one,
    /// so the game-state and Odyssey gating on the parent governs the whole submenu.</para>
    ///
    /// <para>The menu's search box (<c>DebugTabMenu</c>) filters only the current node's
    /// children, so a nested action is not found by typing its name at the top level; filter to
    /// "dev tools" first, then open the entry.</para>
    ///
    /// <para>devtools/bridge/shoot-controls-card.py and shoot-sleep-card.py select their stage
    /// by menu label, so renaming a label or the parent entry breaks them.</para>
    /// </summary>
    internal static class DebugTools_Menu
    {
        [DebugAction("Shift Change", "Dev tools",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap,
            requiresOdyssey = true)]
        internal static List<DebugActionNode> DevTools()
        {
            // A ToolMap child needs only an action: the node hands it to a DebugTool, and each
            // builder reads UI.MouseCell() itself.
            return new List<DebugActionNode>
            {
                new DebugActionNode("Build demo stage", DebugActionType.ToolMap,
                                    DebugTools_DemoStage.BuildDemoStage),
                new DebugActionNode("Build preview stage", DebugActionType.ToolMap,
                                    DebugTools_PreviewStage.BuildPreviewStage),
                new DebugActionNode("Run lifecycle harness", DebugActionType.ToolMap,
                                    DebugTools_LifecycleHarness.RunHarness),
                new DebugActionNode("Build pool room stage", DebugActionType.ToolMap,
                                    DebugTools_PoolStage.BuildPoolStage),
                // A ToolMapForPawns child passes its delegate as the constructor's fourth
                // argument, pawnAction, and leaves action null: DebugActionNode keeps the two
                // separate.
                new DebugActionNode("Drain recreation", DebugActionType.ToolMapForPawns,
                                    null, DebugTools_PoolStage.DrainRecreation),
                new DebugActionNode("Build rec room stage", DebugActionType.ToolMap,
                                    DebugTools_RecRoomStage.BuildRecRoomStage),
                new DebugActionNode("Build sleep stage", DebugActionType.ToolMap,
                                    DebugTools_SleepStage.BuildSleepStage),
                new DebugActionNode("Build sleep card stage", DebugActionType.ToolMap,
                                    DebugTools_SleepStage.BuildSleepCardStage),
                new DebugActionNode("Explain recreation", DebugActionType.ToolMapForPawns,
                                    null, DebugTools_Explain.ExplainRecreation),
                new DebugActionNode("Export scene", DebugActionType.ToolMap,
                                    DebugTools_ExportScene.ExportScene),
            };
        }
    }
}
#endif
