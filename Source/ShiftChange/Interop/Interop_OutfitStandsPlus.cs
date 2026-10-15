using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Soft bridge to Outfit Stands Plus' swap-speed comp. Their powered
    /// stands advertise faster changes, and their own use-driver honors that
    /// through <c>OutfitStandsPlusMainComp.ApplyEquipSpeedFactor</c> — a
    /// shift change at the same stand should not be mysteriously slower than
    /// a manual swap. No assembly reference: the type is resolved by name
    /// once, and the whole bridge collapses to a no-op when the mod is
    /// absent, or has renamed or reshaped the comp. Any surprise thrown
    /// inside their code disables the bridge for the session instead of
    /// faulting the swap.
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
                // Once per session. A missing type stays missing; no point
                // re-asking the assembly list on every swap.
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
