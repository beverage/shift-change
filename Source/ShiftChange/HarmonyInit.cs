#if DEBUG
using EditCompileReload;
#endif
using HarmonyLib;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Applies the mod's Harmony patches once at game start: every attribute patch through
    /// PatchAll, then the Outfit Stands Plus patch by hand.
    ///
    /// A patch target that does not resolve makes PatchAll throw. The game logs one
    /// static-constructor error, and the attribute patches after the failed one are lost along
    /// with the Outfit Stands Plus patch.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        static HarmonyInit()
        {
            Harmony harmony = new Harmony("MrBeverage.ShiftChange");
            harmony.PatchAll();
            // By hand, not by attribute: its target is in Outfit Stands Plus, which may not be
            // installed, and TryApply decides at startup whether to apply at all.
            Patch_OutfitStandsPlusUseButton.TryApply(harmony);
#if DEBUG
            // Hot reload through ECR is compiled into Debug builds only (docs/DEVELOPMENT.md).
            // Its log goes to the dev log, so a swap that landed or failed shows. The watchdog
            // stands in for ECR's file watcher, which never fires under Unity's Mono on macOS.
            EcrLog.messageCallback = s => Log.Message("[ShiftChange/ECR] " + s);
            EcrLog.errorCallback = s => Log.Error("[ShiftChange/ECR] " + s);
            EcrWatchdog.Start();
#endif
        }

#if DEBUG
        // ECR calls any static parameterless method of this name after a reload, so the warning
        // below shows that a swap landed. The method is also the quarantine: ECR loads each
        // reload as a new assembly and redirects every method into its twin there, not only the
        // changed ones, and a twin cannot call an abstract base class's implicit protected
        // constructor. A JobDriver of ours created after a reload therefore throws inside
        // vanilla's StartJob and wedges the pawn's job tracker.
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
