using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShiftChange
{
    /// <summary>
    /// Pauses vanilla's wardrobe optimizer for pawns in uniform. On shift the worn apparel is the
    /// stand's kit, and <c>JobGiver_OptimizeApparel.TryGiveJob</c> issues a <c>RecolorApparel</c>
    /// job whenever any worn apparel has a <c>DesiredColor</c> and a styling station and dye are
    /// reachable, which would dye the staged uniform for good. That recolor is vanilla
    /// Ideology's: Dubs Paint Shop recolors through its own comp and never sets
    /// <c>DesiredColor</c>.
    ///
    /// <para>The rest of the giver has nothing to do either: the uniform is force-worn, so the
    /// giver leaves it alone, and the pawn's own clothes are parked in the stand, out of a
    /// worn-apparel scan's sight. So the whole giver is skipped while a pawn is checked out.
    /// <c>nextApparelOptimizeTick</c> is left alone, so the first think cycle after changing back
    /// optimizes the pawn's own clothes as normal.</para>
    ///
    /// <para>The styling station's float-menu order calls <c>TryCreateRecolorJob</c> directly
    /// and never passes through this giver, but <see cref="Patch_RecolorExecutionGuard"/> still
    /// refuses that order while the pawn is on shift, at its reservations.</para>
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_OptimizeApparel), "TryGiveJob")]
    public static class Patch_OptimizeApparelOnShift
    {
        // Not gated on Patch_JobInterception.Enabled or faulted. The fault latch also counts
        // throws from a neighbouring mod's patch inside the nested StartJob, so gating on either
        // would let an unrelated mod's exceptions re-open the recolor path for pawns still in
        // uniform. The ledger, asked below, is the condition: a pawn on shift needs protecting
        // whether or not interception is running.
        // ReSharper disable once InconsistentNaming — Harmony result injection.
        public static bool Prefix(Pawn pawn, ref Job __result)
        {
            if (pawn == null)
            {
                return true;
            }
            try
            {
                SessionGuard.Ensure();
                if (CompShiftStand.OnShiftStandFor(pawn) == null)
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                // Fail open: this giver is consulted on every think cycle, so a throw recurs.
                Log.Error("[ShiftChange] optimizer pause threw for "
                          + pawn.LabelShort + ", letting vanilla run: " + e);
                return true;
            }
            __result = null;
            return false;
        }
    }

    /// <summary>
    /// Cancels a <c>RecolorApparel</c> job when it starts. The giver pause stops new ones, but
    /// the job queue is saved with the pawn, so a queued job survives a load, and the driver
    /// never re-checks its apparel queue: it dyes whatever the job names, wherever that now
    /// sits, garments a swap has since parked in a stand included. A job already running when
    /// the game saved resumes without asking for reservations, so this does not reach it.
    ///
    /// <para>Failing <c>TryMakePreToilReservations</c> is the clean kill: a queued job that fails
    /// it is ended quietly as no longer valid, and the pawn picks another job. A player's order
    /// fails it too, with a log warning. The job is blocked while the pawn is on shift (the worn
    /// apparel is the uniform) and when any queued garment is parked in an outfit stand; off
    /// shift, in their own clothes, recoloring runs as normal.</para>
    /// </summary>
    [HarmonyPatch(typeof(JobDriver_RecolorApparel), nameof(JobDriver_RecolorApparel.TryMakePreToilReservations))]
    public static class Patch_RecolorExecutionGuard
    {
        // Not gated on Patch_JobInterception.Enabled or faulted, for the same reason as the
        // giver pause above: it has to keep working while interception is off.
        // ReSharper disable once InconsistentNaming — Harmony injection.
        public static bool Prefix(JobDriver_RecolorApparel __instance, ref bool __result)
        {
            try
            {
                SessionGuard.Ensure();
                Pawn pawn = __instance.pawn;
                if (pawn != null && CompShiftStand.OnShiftStandFor(pawn) != null)
                {
                    __result = false;
                    return false;
                }
                List<LocalTargetInfo> queue = __instance.job?.GetTargetQueue(TargetIndex.B);
                if (queue != null)
                {
                    for (int i = 0; i < queue.Count; i++)
                    {
                        if (queue[i].Thing?.ParentHolder is Building_OutfitStand)
                        {
                            __result = false;
                            return false;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("[ShiftChange] recolor guard threw for "
                          + (__instance?.pawn?.LabelShort ?? "an unknown pawn")
                          + ", letting vanilla run: " + e);
            }
            return true;
        }
    }
}
