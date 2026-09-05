using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShiftChange
{
    /// <summary>
    /// The abort button: a gizmo on any colonist currently in a stand's
    /// uniform that cancels their orders and sends them to change back.
    ///
    /// It exists because the automatic return trip is a PULL, not a push: it
    /// fires at a job boundary, when the pawn's own next job takes them out
    /// of the room. A pawn caught in whites the moment a raid lands is not at
    /// a job boundary and may not reach one for hours — sleep is the longest
    /// job in the game — so without this button their way out is the player
    /// finding the right stand and issuing a manual order. The stand is
    /// exactly what the player should not have to hunt for, since the ledger
    /// already knows it.
    ///
    /// <b>The danger gate no longer suppresses the return</b> (decided
    /// 2026-08-31). It once gated both arms, which is why this button was
    /// originally described as existing *because of* it; now it gates
    /// dressing only (<see cref="Patch_JobInterception"/>), so a pawn left to
    /// themselves does change back during a raid — at their own next job
    /// boundary, on their own errand. That narrows this button's job to what
    /// it was always best at: changing back NOW, on the player's timing
    /// rather than the think tree's.
    ///
    /// <b>Deliberately not automatic.</b> No auto-change on raid, and
    /// nothing here reacts to drafting: a player may well want a pawn
    /// drafted in uniform. Direct orders are never second-guessed.
    /// The button is the whole of the feature.
    ///
    /// <b>The room-exit latch.</b> Changing back is pointless if the think
    /// tree hands the pawn the same room job a moment later and the dress
    /// trigger fires again — the pawn would cycle at the stand while the
    /// player is still reaching for the draft key. So a press latches them
    /// out of THAT ROOM until they have actually left it
    /// (<see cref="Patch_JobInterception.ChangedBackAt"/>), which is the
    /// question the player is really asking and needs no counting. They keep
    /// working in the room meanwhile — it is the changing that must not
    /// cycle, not the work — and a job in a DIFFERENT room dresses them
    /// normally, because that is a different uniform and they are leaving
    /// anyway. Drafting drops the latch outright, so
    /// raid → change back → draft → fight → undraft returns to ordinary
    /// behaviour with no residue.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_ChangeBackGizmo
    {
        /// <summary>
        /// Shared by every instance of this gizmo so multi-select merges
        /// them into one button (<c>Command.GroupsWith</c> compares label,
        /// icon and groupKey). Each pawn's own command still fires — grouped
        /// gizmos inherit the click by default
        /// (<c>Gizmo.alsoClickIfOtherInGroupClicked</c>) — so the action
        /// below only ever handles its own pawn.
        /// </summary>
        internal const int GroupKey = 83619427;

        // ReSharper disable once InconsistentNaming — Harmony injection.
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            foreach (Gizmo gizmo in values)
            {
                yield return gizmo;
            }

            // Deliberately NOT gated on Patch_JobInterception.Enabled. That
            // flag latching false is precisely the moment this button matters
            // most: automatic return trips have stopped, so without it every
            // pawn currently in uniform is stranded there with no route back
            // at all. The kill switch exists to stop the mod ACTING on its
            // own; it must not also remove the player's manual remedy.
            if (__instance == null || !__instance.IsColonistPlayerControlled)
            {
                yield break;
            }
            SessionGuard.Ensure();
            CompShiftStand stand = CompShiftStand.OnShiftStandFor(__instance);
            if (stand?.parent == null || !stand.parent.Spawned)
            {
                yield break;
            }

            yield return BuildCommand(__instance, stand);
        }

        internal static Command_Action BuildCommand(Pawn pawn, CompShiftStand stand)
        {
            Command_Action command = new Command_Action
            {
                defaultLabel = "ShiftChange.ChangeBackLabel".Translate(),
                defaultDesc = "ShiftChange.ChangeBackDesc".Translate(
                    stand.parent.Label.Named("STAND")),
                // The stand's own build icon — this mod ships no art, and
                // the picture of the stand is also the clearest statement of
                // where the pawn is about to walk.
                icon = stand.parent.def.uiIcon,
                groupKey = GroupKey,
                // No hotkey, by the same rule the stand's gizmos follow.
                action = () => ChangeBack(pawn, stand)
            };

            if (pawn.Downed || pawn.InMentalState)
            {
                command.Disable("ShiftChange.ChangeBackUnavailable".Translate());
            }
            else if (!pawn.CanReach(stand.parent, PathEndMode.InteractionCell, Danger.Deadly))
            {
                command.Disable("ShiftChange.ChangeBackUnreachable".Translate(
                    stand.parent.Label.Named("STAND")));
            }
            return command;
        }

        internal static void ChangeBack(Pawn pawn, CompShiftStand stand)
        {
            // "Cancel orders" is the literal half of the button: anything
            // the player or the think tree had lined up behind the current
            // job is dropped, so the pawn goes to the stand and stops.
            pawn.jobs?.ClearQueuedJobs();

            Job swap = JobMaker.MakeJob(ShiftChangeDefOf.ShiftChange_SwapAtStand, stand.parent);
            // Vanilla's own ordered-job path: it sets playerForced, respects
            // an uninterruptible current job (a tend finishes first) and
            // honours the queue-order key, so the button behaves like every
            // other right-click order in the game.
            if (pawn.jobs != null && pawn.jobs.TryTakeOrderedJob(swap, JobTag.ChangingApparel))
            {
                Patch_JobInterception.ChangedBackAt[pawn] = stand.parent;
            }
        }
    }

    /// <summary>
    /// Drafting drops the room-exit latch, so an undrafted pawn resumes
    /// ordinary shift changes immediately — a fight is a decisive enough
    /// break that "have they left the room" stops being the useful question.
    /// Patching the setter (rather than polling) means the clear happens on
    /// the same frame as the draft, and it fires on UNdrafting too —
    /// harmless, since the latch is only ever set by the button and a second
    /// clear is a no-op.
    ///
    /// Note this patch does NOT change what drafting does to a uniform: a
    /// pawn drafted in whites stays in whites, deliberately.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted), MethodType.Setter)]
    public static class Patch_DraftClearsDressBlock
    {
        // ReSharper disable once InconsistentNaming — Harmony field injection.
        public static void Postfix(Pawn ___pawn)
        {
            if (___pawn != null)
            {
                Patch_JobInterception.ChangedBackAt.Remove(___pawn);
            }
        }
    }
}
