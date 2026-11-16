using RimWorld;
using Verse;

namespace ShiftChange.Tests
{
    /// <summary>
    /// The game's defs the tests name most, looked up by defName. Each returns null for a def the
    /// loaded game lacks, as a DLC or another mod can leave it out, so a test checks what it got
    /// and fails with a sentence rather than an error.
    /// </summary>
    internal static class GameDefs
    {
        /// <summary>A work type, such as <c>Doctor</c> or <c>Hauling</c>, or null.</summary>
        internal static WorkTypeDef WorkType(string defName)
        {
            return DefDatabase<WorkTypeDef>.GetNamedSilentFail(defName);
        }

        /// <summary>A work giver, such as <c>DoctorTendToHumanlikes</c>, or null.</summary>
        internal static WorkGiverDef Giver(string defName)
        {
            return DefDatabase<WorkGiverDef>.GetNamedSilentFail(defName);
        }

        /// <summary>A stat, such as <c>MiningSpeed</c>, or null when no loaded mod defines it.</summary>
        internal static StatDef StatNamed(string defName)
        {
            return DefDatabase<StatDef>.GetNamedSilentFail(defName);
        }

        /// <summary>
        /// Whether a mod is on this list, by packageId in any case. A Workshop copy installed
        /// beside a local one of the same packageId is active under a <c>_steam</c> suffix, which
        /// <c>ModsConfig.IsActive</c> does not match.
        /// </summary>
        internal static bool ModActive(string packageId)
        {
            return ModLister.GetActiveModWithIdentifier(packageId, ignorePostfix: true) != null;
        }
    }
}
