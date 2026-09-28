using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// One Set owner per stand, and it is ours. On any stand carrying our comp,
    /// whether or not it is used for shift changes, a foreign assignable
    /// comp's gizmo is hidden. The list it would edit is no longer an
    /// independent one: it holds a copy of ours
    /// (<see cref="CompAssignableToPawn_ShiftStand.SyncForeignOwners"/>), and
    /// an edit made through it would be overwritten at the next sync.
    ///
    /// <para>This once hid the foreign control only on stands in shift use and
    /// left the foreign LIST live behind it. That list kept driving the other
    /// mod's per-pawn button, so an owner nobody could see or clear sent a
    /// colonist to swap another colonist's parked kit. Hiding a control is only
    /// safe once nothing reads what it edits independently.</para>
    ///
    /// Patching the BASE method body is what scopes this: our own comp
    /// OVERRIDES <c>CompGetGizmosExtra</c>, so virtual dispatch never brings
    /// it here, and any comp running the base implementation on one of our
    /// stands is by definition a foreign owner control. Mod-agnostic on
    /// purpose — no other mod is named, and a building without our comp
    /// (beds, thrones, racks, other mods' stands we do not govern) passes
    /// through untouched, its own list and gizmo entirely its own.
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
            // Not gated on the stand's mode any more. A stand set to "Not
            // used for shift changes" still has one owner list, ours, and the
            // foreign comp there holds its copy like everywhere else.
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
