using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// When vanilla unclaims everything a lost pawn owned, unclaim their
    /// stands too — assigned AND borrowed.
    ///
    /// This has to exist because <c>Pawn_Ownership.UnclaimAll()</c> clears a
    /// HARDCODED list — bed, grave, throne, deathrest casket
    /// (<c>Pawn_Ownership.cs:286-292</c>). It does not walk
    /// <c>CompAssignableToPawn</c> buildings, so our stand would otherwise stay
    /// assigned to a corpse. Hooking the same method means we inherit vanilla's
    /// notion of when ownership ends rather than inventing one: it is called
    /// from <c>Pawn.Destroy</c> (<c>Pawn.cs:2341</c>), the left-map path
    /// (<c>:2565</c>), <c>PreTraded</c> (<c>:2599</c>) and <c>PreKidnapped</c>
    /// (<c>:2645</c>) — exactly the set players already expect from beds.
    ///
    /// The borrowed half is a separate job since pooling landed: a pawn who
    /// borrowed an unassigned stand was never assigned to anything, so
    /// unassignment alone would miss them entirely and their clothes would
    /// hold a pool stand hostage forever. Their garments stay in the stand as
    /// ordinary contents, and vanilla's
    /// <c>TryDropThingsToMakeRoomForThingOfDef</c> evicts whatever conflicts the
    /// first time someone else claims it. No reclaim logic of our own.
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

                ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
                if (standDef == null)
                {
                    return;
                }

                // A sweep, but this fires on death, trade, kidnap and map exit
                // only — rare events, and the alternative is a registry that
                // has to stay correct across save/load for no benefit.
                List<Map> maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++)
                {
                    List<Thing> stands = maps[i].listerThings.ThingsOfDef(standDef);
                    for (int j = stands.Count - 1; j >= 0; j--)
                    {
                        CompAssignableToPawn comp = stands[j].TryGetComp<CompAssignableToPawn>();
                        if (comp != null && comp.AssignedPawnsForReading.Contains(pawn))
                        {
                            comp.TryUnassignPawn(pawn);
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
