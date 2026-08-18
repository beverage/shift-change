using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// One Set owner per stand. On a stand DECLARED for shift changes (its
    /// "Not used for shift changes" switch off), a foreign assignable comp's
    /// gizmo is hidden so ours is the only owner control; flip the switch
    /// and ours yields instead (see
    /// <see cref="CompAssignableToPawn_ShiftStand.CompGetGizmosExtra"/>).
    /// The declaration — never a room inference — picks the surface, so two
    /// near-identical Set owner buttons never share a stand.
    ///
    /// Patching the BASE method body is what scopes this: our own comp
    /// OVERRIDES <c>CompGetGizmosExtra</c>, so virtual dispatch never brings
    /// it here, and any comp running the base implementation on one of our
    /// stands is by definition a foreign owner control. Mod-agnostic on
    /// purpose — no other mod is named, and a building without our comp
    /// (beds, thrones, racks, other mods' stands we do not govern) passes
    /// through untouched. The foreign comp itself is never written: its
    /// ledger, and its gizmo on stands declared not-ours, stay entirely its
    /// own.
    /// </summary>
    [HarmonyPatch(typeof(CompAssignableToPawn), nameof(CompAssignableToPawn.CompGetGizmosExtra))]
    public static class Patch_ForeignOwnerGizmos
    {
        // ReSharper disable once InconsistentNaming — Harmony injection.
        public static bool Prefix(CompAssignableToPawn __instance, ref IEnumerable<Gizmo> __result)
        {
            // Our own comp reaches this method body too — not by virtual
            // dispatch, but through the explicit base.CompGetGizmosExtra()
            // call inside its override. Without this check the prefix
            // suppressed our own Set owner on every declared stand (found
            // in play, 2026-08-18): "foreign" must mean the INSTANCE type,
            // never the method being executed.
            if (__instance is CompAssignableToPawn_ShiftStand)
            {
                return true;
            }
            CompShiftStand shift = __instance?.parent?.TryGetComp<CompShiftStand>();
            if (shift == null || shift.IsExcluded)
            {
                return true;
            }
            __result = Enumerable.Empty<Gizmo>();
            return false;
        }
    }
}
