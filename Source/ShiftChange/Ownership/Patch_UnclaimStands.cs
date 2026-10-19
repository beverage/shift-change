using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Releases a lost pawn's stands, assigned and borrowed, when vanilla unclaims what they
    /// owned (death, trade, kidnap, and a colony guest or prisoner leaving the map; a colonist
    /// leaving keeps everything). <c>Pawn_Ownership.UnclaimAll</c> clears a fixed
    /// list that holds no stand, so this postfix extends it. See docs/DESIGN.md, "Ownership
    /// and the ledger".
    /// </summary>
    [HarmonyPatch(typeof(Pawn_Ownership), nameof(Pawn_Ownership.UnclaimAll))]
    public static class Patch_UnclaimStands
    {
        // Harmony fills ___pawn from the patched class's private field of that name.
        // ReSharper disable once InconsistentNaming
        public static void Postfix(Pawn ___pawn)
        {
            ReapStandsFor(___pawn);
        }

        /// <summary>Frees every spawned stand the pawn is assigned to or has on loan.</summary>
        internal static void ReapStandsFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            try
            {
                // A pool borrower was never assigned to anything, so no assignment names
                // the stand they have on loan; find those by the ledger. Loans first: the
                // catch-up AbandonLedger raises still sees this pawn as owner, so a stand they
                // owned is not offered to someone mid-job.
                foreach (CompShiftStand borrowed in CompShiftStand.StandsBorrowedBy(pawn).ToList())
                {
                    borrowed.AbandonLedger(pawn);
                }

                // Nothing indexes stands by owner, so scan every stand on every map. The
                // exits that reach here are rare.
                List<ThingDef> standDefs = Patch_JobInterception.StandDefs;
                List<Map> maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++)
                {
                    for (int d = 0; d < standDefs.Count; d++)
                    {
                        List<Thing> stands = maps[i].listerThings.ThingsOfDef(standDefs[d]);
                        for (int j = stands.Count - 1; j >= 0; j--)
                        {
                            // Our comp first, any other assignable as the fallback: the order
                            // AssignedOwners reads in. A stand that carries only another mod's
                            // comp is reserved from that list, so a pawn left in it would hold
                            // the stand for good unless this clears it too.
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
