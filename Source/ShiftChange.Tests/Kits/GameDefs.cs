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
    }
}
