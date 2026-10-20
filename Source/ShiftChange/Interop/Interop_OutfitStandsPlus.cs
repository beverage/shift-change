using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Outfit Stands Plus scales swap time by a factor from its settings, one for powered and
    /// one for unpowered stands, through <c>OutfitStandsPlusMainComp.ApplyEquipSpeedFactor</c>.
    /// Its comp sits on vanilla's stands as well as its own. This applies the same factor to a
    /// shift change, so it is never slower than a manual swap at the same stand.
    /// No assembly reference: the type is found by name, and the bridge does nothing when the
    /// mod is absent or has renamed or reshaped the comp. An exception thrown inside their code
    /// turns the bridge off for the session instead of failing the swap.
    /// </summary>
    internal static class Interop_OutfitStandsPlus
    {
        internal static bool resolved;
        internal static Type compType;
        internal static MethodInfo applyMethod;

        internal static int ApplySwapSpeedFactor(Building_OutfitStand stand, int duration)
        {
            if (stand == null || duration <= 0)
            {
                return duration;
            }
            if (!resolved)
            {
                // Once per session: a mod that is absent stays absent.
                resolved = true;
                compType = GenTypes.GetTypeInAnyAssembly("OutfitStandsPlus.ThingComps.OutfitStandsPlusMainComp");
                if (compType != null)
                {
                    applyMethod = compType.GetMethod("ApplyEquipSpeedFactor",
                        BindingFlags.Instance | BindingFlags.Public,
                        null, new[] { typeof(int) }, null);
                }
            }
            if (applyMethod == null)
            {
                return duration;
            }
            List<ThingComp> comps = stand.AllComps;
            for (int i = 0; i < comps.Count; i++)
            {
                if (compType.IsInstanceOfType(comps[i]))
                {
                    try
                    {
                        return (int)applyMethod.Invoke(comps[i], new object[] { duration });
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[ShiftChange] Outfit Stands Plus swap-speed bridge disabled after an error in their comp: " + e);
                        applyMethod = null;
                        return duration;
                    }
                }
            }
            return duration;
        }
    }
}
