using HarmonyLib;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Applies Shift Change's patches once at game start.
    ///
    /// Two residents: <see cref="Patch_JobInterception"/>, which inserts the
    /// swap ahead of automatic work (and the return trip on the way out), and
    /// <see cref="Patch_Ownership"/>, which unassigns a stand when its owner
    /// dies, is traded, is kidnapped or leaves the map.
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
        }
    }
}
