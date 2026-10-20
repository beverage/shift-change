using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShiftChange
{
    /// <summary>
    /// The Change back button: a gizmo on any colonist in a stand's uniform that cancels their
    /// orders and sends them to change back now, on the player's timing rather than at their
    /// next job boundary. A press latches them out of dressing at that stand, and at stands with
    /// its trigger that cover it, until they leave its catchment or wake
    /// (<see cref="Patch_JobInterception.ChangedBackAt"/>), and drafting drops the latch. It is
    /// deliberately not automatic: no auto-change on a raid, and a pawn drafted in uniform stays
    /// in it. Why each: docs/DESIGN.md, "Change back".
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_ChangeBackGizmo
    {
        /// <summary>
        /// Shared by every instance of this gizmo so multi-select merges them into one button
        /// (<c>Command.GroupsWith</c> compares label, icon and groupKey). Each pawn's own command
        /// still fires, since grouped gizmos inherit the click by default
        /// (<c>Gizmo.alsoClickIfOtherInGroupClicked</c>), so the action below only ever handles
        /// its own pawn.
        /// </summary>
        internal const int GroupKey = 83619427;

        /// <summary>
        /// The gate is deliberately not an iterator: <c>GizmoGridDrawer</c> rebuilds the bar once
        /// per rendered frame for every selected object, and passthrough postfixes nest in run
        /// order, so an iterator here wraps every gizmo the postfixes before it pass through, for
        /// every selected pawn, every frame, to decide something that is almost always no.
        /// Returning <c>values</c> untouched keeps this off that path; only a pawn who gets the
        /// button pays for the wrapper.
        /// </summary>
        // ReSharper disable once InconsistentNaming — Harmony injection.
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            // Reads neither Patch_JobInterception.Enabled nor faulted. With interception switched
            // off or latched off by its faults, automatic return trips have stopped and a pawn in
            // uniform has no other way back; both switches stop the mod acting on its own, and
            // neither may also remove the player's manual remedy.
            if (__instance == null || !__instance.IsColonistPlayerControlled)
            {
                return values;
            }
            // No SessionGuard.Ensure() here: OnShiftStandFor opens with one.
            CompShiftStand stand = CompShiftStand.OnShiftStandFor(__instance);
            if (stand?.parent == null || !stand.parent.Spawned)
            {
                return values;
            }
            return WithChangeBack(values, __instance, stand);
        }

        internal static IEnumerable<Gizmo> WithChangeBack(IEnumerable<Gizmo> values,
                                                         Pawn pawn, CompShiftStand stand)
        {
            foreach (Gizmo gizmo in values)
            {
                yield return gizmo;
            }
            yield return BuildCommand(pawn, stand);
        }

        internal static Command_Action BuildCommand(Pawn pawn, CompShiftStand stand)
        {
            Command_Action command = new Command_Action
            {
                defaultLabel = "ShiftChange.ChangeBackLabel".Translate(),
                defaultDesc = stand.ByCatchment("ShiftChange.ChangeBackDesc").Translate(
                    stand.parent.Label.Named("STAND")),
                // The stand's own build icon: the mod ships no art, and it shows where the pawn
                // is about to walk.
                icon = stand.parent.def.uiIcon,
                groupKey = GroupKey,
                // No hotkey, as on the stand's gizmos.
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
            // Drop anything queued behind the current job, so the pawn goes to the stand and stops.
            pawn.jobs?.ClearQueuedJobs();

            Job swap = JobMaker.MakeJob(ShiftChangeDefOf.ShiftChange_SwapAtStand, stand.parent);
            // Vanilla's ordered-job path: it sets playerForced, lets an uninterruptible current
            // job finish (a tend), and honours the queue-order key, so the button behaves like
            // any other right-click order.
            if (pawn.jobs != null && pawn.jobs.TryTakeOrderedJob(swap, JobTag.ChangingApparel))
            {
                Patch_JobInterception.ChangedBackAt[pawn] =
                    new Patch_JobInterception.ChangeBackLatch(stand.parent);
            }
        }
    }

    /// <summary>
    /// Drafting drops the change-back latch: after a fight, whether the pawn has left the
    /// catchment is no longer the useful question, so an undrafted pawn resumes ordinary shift
    /// changes. The setter also fires on undrafting, which is harmless: the latch is only ever
    /// set by the button, and a second clear is a no-op. It does not change what drafting does
    /// to a uniform; a pawn drafted in whites stays in whites.
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
