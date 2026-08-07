using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Unassigns a pawn's stand when they stop being the colony's problem.
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
    /// The unassign drops the checkout ledger too (see
    /// <c>CompAssignableToPawn_ShiftStand.TryUnassignPawn</c>): a dead owner's
    /// clothes stay in the stand as ordinary contents, and vanilla's
    /// <c>TryDropThingsToMakeRoomForThingOfDef</c> evicts whatever conflicts
    /// the first time a new owner swaps. No reclaim logic of our own.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_Ownership), nameof(Pawn_Ownership.UnclaimAll))]
    public static class Patch_Ownership
    {
        // ReSharper disable once InconsistentNaming — Harmony field injection.
        public static void Postfix(Pawn ___pawn)
        {
            if (___pawn == null)
            {
                return;
            }

            try
            {
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
                        if (comp != null && comp.AssignedPawnsForReading.Contains(___pawn))
                        {
                            comp.TryUnassignPawn(___pawn);
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Error("[ShiftChange] failed to unassign stands for " + ___pawn.LabelShort + ": " + e);
            }
        }
    }
}
