using HarmonyLib;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Applies Shift Change's patches once at game start.
    ///
    /// Today that is only <see cref="Patch_InterceptionProbe"/>, which observes
    /// and logs — it changes no game state at all. Everything it touches is
    /// wrapped so an exception degrades to vanilla behaviour rather than
    /// breaking job assignment, which would brick the colony.
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
