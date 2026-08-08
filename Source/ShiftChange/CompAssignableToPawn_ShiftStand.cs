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
                WorkTypeDef work = parent.TryGetComp<CompShiftStand>()?.WorkType;
                if (work == null)
                {
                    // No work type resolved (roleless room, no override). Let
                    // the player assign anyway — they may be setting the owner
                    // before setting the room up.
                    return colonists;
                }
                return colonists.Where(p => !p.WorkTypeIsDisabled(work));
            }
        }

        /// <summary>
        /// Vanilla's label is always "Set owner" — the base comp never reports
        /// who owns the thing, anywhere. Beds only appear to, because
        /// <c>Building_Bed.GetInspectString</c> writes the owner itself. On a
        /// stand that leaves ownership invisible outside the assign dialog, so
        /// name it on the button.
        /// </summary>
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
            return assigned.Count > 0
                ? "ShiftChange.OwnerGizmoLabel".Translate(assigned[0].LabelShort)
                : "ShiftChange.PoolGizmoLabel".Translate();
        }

        protected override string GetAssignmentGizmoDesc()
        {
            WorkTypeDef work = parent.TryGetComp<CompShiftStand>()?.WorkType;
            return work == null
                ? "ShiftChange.AssignDescNoWork".Translate()
                : "ShiftChange.AssignDesc".Translate(work.gerundLabel ?? work.defName);
        }

        // Note what is deliberately NOT here: unassigning does not abandon the
        // ledger. Since pooling landed, an unassigned stand is still perfectly
        // usable, so a pawn who is mid-shift keeps their claim and can still
        // change back — the stand simply returns to the pool afterwards.
        // Reaping a ledger whose borrower is *gone* is Patch_Ownership's job,
        // and it has to be, because a pool borrower was never assigned here at
        // all.
    }
}
