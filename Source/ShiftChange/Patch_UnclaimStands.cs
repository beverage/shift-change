using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// When vanilla unclaims everything a lost pawn owned, unclaim their
    /// stands too — assigned AND borrowed. <c>Pawn_Ownership.UnclaimAll()</c>
    /// clears a hardcoded list no stand is on, and hooking it inherits
    /// vanilla's notion of when ownership ends (death, trade, kidnap, map exit)
    /// rather than inventing one. A pool borrower was never assigned to
    /// anything, so the borrowed half is its own sweep. The engine's call
    /// sites, and why no reclaim logic is needed: docs/DESIGN.md, "Ownership
    /// and the ledger".
    /// </summary>
    [HarmonyPatch(typeof(Pawn_Ownership), nameof(Pawn_Ownership.UnclaimAll))]
    public static class Patch_UnclaimStands
    {
        // ReSharper disable once InconsistentNaming — Harmony field injection.
        public static void Postfix(Pawn ___pawn)
        {
            ReapStandsFor(___pawn);
        }

        /// <summary>
        /// The reaper itself, callable without going through
        /// <c>UnclaimAll</c>. <see cref="Patch_BanishStands"/> needs it because
        /// banishment is the one colony exit vanilla never routes through that
        /// method, so the postfix above never fires for it.
        /// </summary>
        internal static void ReapStandsFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            try
            {
                // Free anything they had on loan first — this covers pool
                // stands, which no assignment would ever have named.
                foreach (CompShiftStand borrowed in CompShiftStand.StandsBorrowedBy(pawn).ToList())
                {
                    borrowed.AbandonLedger(pawn);
                }

                // A sweep, but this fires on death, trade, kidnap and map exit
                // only — rare events, and the alternative is a registry that
                // has to stay correct across save/load for no benefit. The
                // def list is the whole patched family, not just the vanilla
                // stand.
                List<ThingDef> standDefs = Patch_JobInterception.StandDefs;
                List<Map> maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++)
                {
                    for (int d = 0; d < standDefs.Count; d++)
                    {
                        List<Thing> stands = maps[i].listerThings.ThingsOfDef(standDefs[d]);
                        for (int j = stands.Count - 1; j >= 0; j--)
                        {
                            // Exact type first, any assignable as fallback —
                            // the same order AssignedOwners reads with, and
                            // for the same reason (see its comment). The
                            // fallback WRITE is deliberate: if a foreign comp
                            // is what our reservation logic reads, a corpse
                            // left in that comp's list would reserve the
                            // stand forever unless this reaper can clear it
                            // too.
                            CompAssignableToPawn comp = stands[j].TryGetComp<CompAssignableToPawn_ShiftStand>()
                                ?? stands[j].TryGetComp<CompAssignableToPawn>();
                            if (comp != null && comp.AssignedPawnsForReading.Contains(pawn))
                            {
                                comp.TryUnassignPawn(pawn);
                            }
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Error("[ShiftChange] failed to unassign stands for " + pawn.LabelShort + ": " + e);
            }
        }
    }
}
