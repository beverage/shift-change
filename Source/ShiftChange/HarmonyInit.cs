#if DEBUG
using EditCompileReload;
#endif
using HarmonyLib;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Applies Shift Change's patches once at game start.
    ///
    /// Two residents: <see cref="Patch_JobInterception"/>, which inserts the
    /// swap ahead of automatic work (and the return trip on the way out), and
    /// <see cref="Patch_UnclaimStands"/>, which releases a pawn's assigned and
    /// borrowed stands when vanilla unclaims their beds and thrones — death,
    /// trade, kidnap, map exit.
    ///
    /// Both are wrapped so an exception degrades to vanilla behaviour rather
    /// than breaking job assignment, which would brick the colony — the
    /// interception patch disables itself outright on a throw.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        static HarmonyInit()
        {
            new Harmony("MrBeverage.ShiftChange").PatchAll();
#if DEBUG
            // ECR hot reload is compiled into Debug builds only — see the
            // dev-loop section of CLAUDE.md. Route its logging into the dev
            // log so a swap landing (or failing) is visible, not an act of
            // faith.
            EcrLog.messageCallback = s => Log.Message("[ShiftChange/ECR] " + s);
            EcrLog.errorCallback = s => Log.Error("[ShiftChange/ECR] " + s);
#endif
        }

#if DEBUG
        // ECR invokes any static parameterless method with this name after a
        // reload — the heartbeat that a swap actually landed.
        private static void OnEditCompileReload()
        {
            Log.Message("[ShiftChange] hot reload applied");
        }
#endif
    }
}
