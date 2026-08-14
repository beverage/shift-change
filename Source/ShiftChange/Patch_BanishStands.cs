using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Banishment is the one colony exit vanilla never routes through
    /// <c>Pawn_Ownership.UnclaimAll</c>, so it is the one
    /// <see cref="Patch_UnclaimStands"/> cannot catch.
    ///
    /// <para><c>PawnBanishUtility.Banish</c> on a SPAWNED colonist falls past
    /// the caravan branch, clears guest status (<c>:53-56</c>), runs
    /// <c>pawn.SetFaction(null)</c> (<c>:66-69</c>) and returns. No unclaim.
    /// It does not self-heal when the pawn later wanders off the map either:
    /// <c>Pawn.ExitMap</c> gates its <c>UnclaimAll</c> on a flag
    /// (<c>Pawn.cs:2552,2565</c>) that the guest-status clear above has already
    /// made false. Left alone, the stand keeps a ledger for a colonist who is
    /// gone — never offered to anyone else, inspect pane naming an absent
    /// pawn, forever.</para>
    ///
    /// <para><b>Why this is eager rather than a liveness check at the read
    /// points.</b> A lazy predicate answers "is the borrower still ours",
    /// which is exactly the question that un-answers itself: recruit that
    /// wanderer back and the ledger — still sitting there, still populated —
    /// becomes live again, and the stand goes dead a second time. Reaping at
    /// the moment of banishment leaves nothing to revive.
    /// <c>CompShiftStand.StillOurs</c> still exists as the load-time repair
    /// for saves that predate this patch.</para>
    ///
    /// <para>Patches the three-argument overload; the two-argument one
    /// (<c>PawnBanishUtility.cs:15-18</c>) forwards to it, so both are
    /// covered by the one patch.</para>
    /// </summary>
    [HarmonyPatch(typeof(PawnBanishUtility), nameof(PawnBanishUtility.Banish),
        new[] { typeof(Pawn), typeof(PlanetTile), typeof(bool) })]
    public static class Patch_BanishStands
    {
        public static void Postfix(Pawn pawn)
        {
            // Same reaper as death, trade and kidnap, so banishment produces
            // the behaviour players have already seen from the other exits.
            Patch_UnclaimStands.ReapStandsFor(pawn);
        }
    }
}
