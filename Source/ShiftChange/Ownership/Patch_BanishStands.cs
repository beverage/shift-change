using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Frees a banished colonist's stands, which <see cref="Patch_UnclaimStands"/> cannot catch:
    /// on a spawned colonist <c>PawnBanishUtility.Banish</c> never reaches
    /// <c>Pawn_Ownership.UnclaimAll</c>. It falls past the caravan branch, clears guest
    /// status, runs <c>pawn.SetFaction(null)</c> and returns. A later map exit does not catch
    /// it either: <c>Pawn.ExitMap</c> unclaims only for the colony's guests, prisoners and
    /// slaves and those it released, which a banished pawn is not. The reaper is safe to run
    /// twice, as it does when the caravan branch kills the pawn.
    ///
    /// <para>Reap now rather than test at the read points whether a borrower is still ours: a
    /// pawn recruited back would bring a ledger that was only disbelieved back to life.</para>
    ///
    /// <para>Patches the three-argument overload, which the two-argument one forwards to.</para>
    /// </summary>
    [HarmonyPatch(typeof(PawnBanishUtility), nameof(PawnBanishUtility.Banish),
        new[] { typeof(Pawn), typeof(PlanetTile), typeof(bool) })]
    public static class Patch_BanishStands
    {
        public static void Postfix(Pawn pawn)
        {
            Patch_UnclaimStands.ReapStandsFor(pawn);
        }
    }
}
