using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Hides a foreign assignable comp's Set owner gizmo on every stand carrying our comp, so the
    /// stand has one Set owner and it is ours. The foreign list holds a copy of ours
    /// (<see cref="CompAssignableToPawn_ShiftStand.SyncForeignOwners"/>), so an edit made through
    /// its gizmo would be overwritten at the next sync, and Outfit Stands Plus drives its
    /// per-colonist stand button and inspect line from that list. The prefix sits on the base
    /// <c>CompAssignableToPawn.CompGetGizmosExtra</c>, which Outfit Stands Plus' owner comp
    /// inherits without overriding; a foreign comp that overrides it without calling base keeps
    /// its gizmo. The patch names no mod, and a building without our comp (a bed, a throne, a
    /// stand our patches do not reach) is untouched.
    /// </summary>
    [HarmonyPatch(typeof(CompAssignableToPawn), nameof(CompAssignableToPawn.CompGetGizmosExtra))]
    public static class Patch_ForeignOwnerGizmos
    {
        // ReSharper disable once InconsistentNaming — Harmony injection.
        public static bool Prefix(CompAssignableToPawn __instance, ref IEnumerable<Gizmo> __result)
        {
            // Our own comp reaches this body through the base call in its override. "Foreign"
            // therefore means the instance's type, not the method running: without this test our
            // own Set owner would be hidden on every stand.
            if (__instance is CompAssignableToPawn_ShiftStand)
            {
                return true;
            }
            // No mode test: a stand not used for shift changes still keeps its owners in our
            // list, with a copy in the foreign comp.
            CompShiftStand shift = __instance?.parent?.TryGetComp<CompShiftStand>();
            if (shift == null)
            {
                return true;
            }
            __result = Enumerable.Empty<Gizmo>();
            return false;
        }
    }
}
