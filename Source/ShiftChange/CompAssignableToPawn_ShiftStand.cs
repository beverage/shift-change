using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Ownership for an outfit stand. The base comp already supplies the "set
    /// owner" gizmo, the <c>Dialog_AssignBuildingOwner</c> window and reference
    /// scribing (<c>CompAssignableToPawn.cs:164-195</c>), so all this adds is a
    /// narrower candidate list: a stand that dresses for doctoring should not
    /// offer itself to a colonist who cannot doctor.
    ///
    /// <c>maxAssignedPawnsCount</c> stays at its default of 1, and that is not
    /// merely tidiness — <c>Building_OutfitStand.HasRoomForApparelOfDef</c> is a
    /// conflict check rather than a count (<c>:332-342</c>), so a stand holds
    /// exactly one outfit's worth. Two owners could not both park their clothes
    /// in it even if we let them.
    /// </summary>
    public class CompAssignableToPawn_ShiftStand : CompAssignableToPawn
    {
        public override IEnumerable<Pawn> AssigningCandidates
        {
            get
            {
                if (!parent.Spawned)
                {
                    return Enumerable.Empty<Pawn>();
                }

                IEnumerable<Pawn> colonists = parent.Map.mapPawns.FreeColonists;
                List<WorkTypeDef> works = parent.TryGetComp<CompShiftStand>()?.WorkTypes;
                if (works == null || works.Count == 0)
                {
                    // No work types resolved (roleless room, no override). Let
                    // the player assign anyway — they may be setting the owner
                    // before setting the room up.
                    return colonists;
                }
                // Capable of ANY of the set — a workshop stand covering
                // crafting and tailoring is assignable to a pure tailor.
                return colonists.Where(p => works.Any(w => !p.WorkTypeIsDisabled(w)));
            }
        }

        /// <summary>
        /// Vanilla's label is always "Set owner" — the base comp never reports
        /// who owns the thing, anywhere. Beds only appear to, because
        /// <c>Building_Bed.GetInspectString</c> writes the owner itself. On a
        /// stand that leaves ownership invisible outside the assign dialog, so
        /// name it on the button.
        /// </summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                // The base comp hardcodes Misc4 (N) on the assignment gizmo
                // (CompAssignableToPawn.cs:176) — harmless on beds and
                // thrones, but the outfit stand is a STORAGE building, and N
                // is copy-settings there (StorageSettingsClipboard.cs:40).
                // House rule (principal, 2026-08-08): on anything with
                // copyable settings, never bind over N, J, F or O.
                if (gizmo is Command command)
                {
                    command.hotKey = null;
                }
                yield return gizmo;
            }
        }

        /// <summary>
        /// Names the pawn floating over the stand at closest zoom, so a room of
        /// pool stands can be read at a glance instead of clicked through.
        ///
        /// The base draws the assigned owner (<c>CompAssignableToPawn.cs:62-81</c>),
        /// which says nothing about a pool stand — the interesting fact there is
        /// who currently has it out. A forbidden-style X was the other option and
        /// is rejected: `OverlayTypes.Forbidden` means *forbidden* in RimWorld's
        /// vocabulary, so it would report the wrong thing, and it cannot say
        /// whose stand it is.
        /// </summary>
        public override void DrawGUIOverlay()
        {
            CompShiftStand shift = parent.TryGetComp<CompShiftStand>();
            Pawn borrower = shift?.Borrower;
            if (borrower == null || !shift.OnShift)
            {
                base.DrawGUIOverlay();
                return;
            }

            if (Find.CameraDriver.CurrentZoom != CameraZoomRange.Closest || !PlayerCanSeeAssignments)
            {
                return;
            }
            GenMapUI.DrawThingLabel(parent, borrower.LabelShort, GenMapUI.DefaultThingLabelColor);
        }

        protected override string GetAssignmentGizmoLabel()
        {
            List<Pawn> assigned = AssignedPawnsForReading;
            if (assigned.Count > 0)
            {
                return "ShiftChange.OwnerGizmoLabel".Translate(assigned[0].LabelShort);
            }
            // With pooling off an unassigned stand is not "shared", it is
            // simply unowned — vanilla's own "Set owner" says that best.
            // Inlined rather than base.GetAssignmentGizmoLabel(): the base is
            // PROTECTED (CompAssignableToPawn.cs:154-156), and hot-swapped
            // bodies on the twin type cannot pass the protected-access check
            // — the same failure Window.Margin produced (2026-08-08).
            return ShiftChangeMod.PoolingEnabled
                ? "ShiftChange.PoolGizmoLabel".Translate()
                : "CommandThingSetOwnerLabel".Translate();
        }

        protected override string GetAssignmentGizmoDesc()
        {
            CompShiftStand comp = parent.TryGetComp<CompShiftStand>();
            if (comp == null || comp.WorkTypes.Count == 0)
            {
                return "ShiftChange.AssignDescNoWork".Translate();
            }
            string works = comp.WorkTypesLabel();
            return ShiftChangeMod.PoolingEnabled
                ? "ShiftChange.AssignDesc".Translate(works)
                : "ShiftChange.AssignDescNoPool".Translate(works);
        }

        // Note what is deliberately NOT here: unassigning does not abandon the
        // ledger. Since pooling landed, an unassigned stand is still perfectly
        // usable, so a pawn who is mid-shift keeps their claim and can still
        // change back — the stand simply returns to the pool afterwards.
        // Reaping a ledger whose borrower is *gone* is Patch_UnclaimStands'
        // job, and it has to be, because a pool borrower was never assigned
        // here at all.
    }
}
