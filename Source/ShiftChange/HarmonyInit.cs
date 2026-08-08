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
            // faith. The watchdog replaces ECR's FileSystemWatcher trigger,
            // which never fires under Unity's Mono on macOS.
            EcrLog.messageCallback = s => Log.Message("[ShiftChange/ECR] " + s);
            EcrLog.errorCallback = s => Log.Error("[ShiftChange/ECR] " + s);
            EcrWatchdog.Start();
#endif
        }

#if DEBUG
        // ECR invokes any static parameterless method with this name after a
        // reload — the heartbeat that a swap actually landed. It is also the
        // QUARANTINE: ECR redirects EVERY method, not just changed ones, and
        // a twin body cannot call an abstract base's implicit (protected)
        // constructor — so any post-reload instantiation of our JobDriver
        // detonates inside vanilla's StartJob and wedges the pawn's tracker
        // (2026-08-08, in play). From the first reload onward this session is
        // a UI lab, not a colony: all gameplay interception goes dark, and
        // behaviour testing happens on a clean Release restart, as the
        // discipline always said — now enforced by code instead of memory.
        internal static void OnEditCompileReload()
        {
            Patch_JobInterception.Enabled = false;
            Patch_JobInterception.DressMidJob = false;
            Patch_ModeBadge.GameplayDark = true;
            Log.Warning("[ShiftChange] hot reload applied — gameplay interception disabled for this session (UI-lab mode); restart on a Release build for behaviour testing");
        }
#endif
    }
}
