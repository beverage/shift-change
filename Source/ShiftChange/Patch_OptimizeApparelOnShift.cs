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
        // ReSharper disable once InconsistentNaming — Harmony result injection.
        public static bool Prefix(Pawn pawn, ref Job __result)
        {
            if (!Patch_JobInterception.Enabled || pawn == null)
            {
                return true;
            }
            SessionGuard.Ensure();
            if (CompShiftStand.OnShiftStandFor(pawn) == null)
            {
                return true;
            }
            __result = null;
            return false;
        }
    }
}
