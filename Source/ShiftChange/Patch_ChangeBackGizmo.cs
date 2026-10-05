using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShiftChange
{
    /// <summary>
    /// The abort button: a gizmo on any colonist currently in a stand's
    /// uniform that cancels their orders and sends them to change back NOW,
    /// on the player's timing rather than at their next job boundary. A press
    /// latches them out of dressing in that room until they leave it
    /// (<see cref="Patch_JobInterception.ChangedBackAt"/>), and drafting drops
    /// the latch. Deliberately not automatic: no auto-change on a raid, and a
    /// pawn drafted in uniform stays in it. Why each: docs/DESIGN.md, "Change
    /// back".
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

        /// <summary>
        /// The gate is deliberately NOT an iterator. `GizmoGridDrawer`
        /// rebuilds the bar once per rendered frame for every selected object
        /// (`GizmoGridDrawer.cs:54-100` caches on `Time.frameCount` plus the
        /// selection), and passthrough postfixes nest in run order — ours runs
        /// last on `Pawn.GetGizmos`, so an iterator here wraps every gizmo of
        /// every other mod in the chain, for every selected pawn, every frame,
        /// to decide something that is almost always "no". Returning `values`
        /// untouched keeps our frame off that path entirely; only a pawn who
        /// actually gets the button pays for the wrapper.
        /// </summary>
        // ReSharper disable once InconsistentNaming — Harmony injection.
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            // Deliberately NOT gated on Patch_JobInterception.Enabled. That
            // flag latching false is precisely the moment this button matters
            // most: automatic return trips have stopped, so without it every
            // pawn currently in uniform is stranded there with no route back
            // at all. The kill switch exists to stop the mod ACTING on its
            // own; it must not also remove the player's manual remedy.
            if (__instance == null || !__instance.IsColonistPlayerControlled)
            {
                return values;
            }
            // No SessionGuard.Ensure() call of our own: OnShiftStandFor opens
            // with one, so a second here only repeats a reference compare.
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
