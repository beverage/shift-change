using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShiftChange
{
    /// <summary>
    /// Pauses vanilla's wardrobe optimizer for pawns in uniform.
    ///
    /// The trigger case, found in play 2026-08-09: an on-shift pawn walked
    /// off to a styling station and dyed their WORK OUTFIT to their
    /// favourite colour. The job is vanilla Ideology, not the paint mod it
    /// resembles: <c>JobGiver_OptimizeApparel.TryGiveJob</c> issues a
    /// <c>RecolorApparel</c> job (<c>JobGiver_OptimizeApparel.cs:84</c>)
    /// whenever any WORN apparel has a <c>DesiredColor</c> and a styling
    /// station plus dye are reachable — on the same 6,000–9,000-tick
    /// <c>nextApparelOptimizeTick</c> cadence as wardrobe optimization,
    /// which is why it reads as random. (Dubs Paint Shop is innocent: it
    /// paints via its own comp instantly or through its Art work givers, and
    /// never touches <c>DesiredColor</c> — in vanilla the only setter is the
    /// styling station dialog, <c>Dialog_StylingStation.cs:765</c>.)
    ///
    /// While on shift the worn apparel IS the stand's kit, so that recolor
    /// permanently redyes a staged uniform — and the optimizer has no other
    /// legitimate business either: the uniform is force-worn precisely so
    /// this giver leaves it alone, and the pawn's own wardrobe is parked in
    /// the stand where a worn-apparel scan cannot see it. So the whole giver
    /// is skipped while a pawn is checked out; <c>nextApparelOptimizeTick</c>
    /// is left untouched, so the first think cycle after changing back out
    /// optimizes (and dyes) the civvies as normal.
    ///
    /// Deliberately NOT blocked: the styling station's float-menu order,
    /// which calls <c>TryCreateRecolorJob</c> directly
    /// (<c>Building_StylingStation.cs:30-34</c>) and never passes through
    /// this giver — a player's explicit order proceeds, the same
    /// direct-order rule the job interception follows.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_OptimizeApparel), "TryGiveJob")]
    public static class Patch_OptimizeApparelOnShift
    {
        // Deliberately NOT gated on Patch_JobInterception.Enabled. That flag is
        // the interception's kill switch and latches false on any throw inside
        // the nested StartJob — including a throw from a NEIGHBOUR mod's patch.
        // Tying this pause to it meant an unrelated mod's exception re-opened
        // the recolor path on pawns still standing in their uniforms, which is
        // verbatim the 2026-08-09 bug this patch exists to prevent, and the
        // only unrecoverable consequence of self-disabling. A pawn who is on
        // shift needs protecting whether or not interception is still running;
        // the ledger is the correct condition, and it is the one asked below.
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
                // This giver is consulted on every think cycle, so a throw
                // here recurs forever rather than once. Fail open.
                Log.Error("[ShiftChange] optimizer pause threw for "
                          + pawn.LabelShort + ", letting vanilla run: " + e);
                return true;
            }
            __result = null;
            return false;
        }
    }

    /// <summary>
    /// Second layer, at EXECUTION: the giver pause above stops new recolor
    /// jobs being issued, but a RecolorApparel job that already exists still
    /// runs — curJob and the job queue are scribed into the save, so a job
    /// issued in a pre-pause session resumes after load and dyes the uniform
    /// anyway (a stand's toque came back favourite-colour green, 2026-08-09).
    /// The driver never re-checks its apparel queue: it dyes whatever the
    /// job names, wherever it now sits (JobDriver_RecolorApparel.cs:74) —
    /// including garments a swap has since parked in a stand.
    ///
    /// Failing TryMakePreToilReservations is the clean kill: vanilla treats
    /// a failed reservation at job start as routine (races do it constantly)
    /// and the think tree simply picks another job. Blocked when the pawn is
    /// on shift (worn apparel is the uniform) or when any queued garment is
    /// parked in an outfit stand (the post-undress legacy case). Off shift,
    /// on their own clothes, recoloring runs untouched.
    /// </summary>
    [HarmonyPatch(typeof(JobDriver_RecolorApparel), nameof(JobDriver_RecolorApparel.TryMakePreToilReservations))]
    public static class Patch_RecolorExecutionGuard
    {
        // Not gated on Patch_JobInterception.Enabled either, and for the same
        // reason as the giver pause above — this is the layer that catches
        // recolor jobs scribed into the save before the pause existed, so it
        // has to survive the interception switching itself off.
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
