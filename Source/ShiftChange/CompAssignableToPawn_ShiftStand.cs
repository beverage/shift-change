using System.Collections.Generic;
using System.Linq;
using RimWorld;
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

        protected override string GetAssignmentGizmoDesc()
        {
            WorkTypeDef work = parent.TryGetComp<CompShiftStand>()?.WorkType;
            return work == null
                ? "ShiftChange.AssignDescNoWork".Translate()
                : "ShiftChange.AssignDesc".Translate(work.gerundLabel ?? work.defName);
        }

        public override void TryUnassignPawn(Pawn pawn, bool sort = true, bool uninstall = false)
        {
            // The owner is being removed while their clothes may still be
            // checked out. Forget the ledger rather than strand it: the
            // garments stay in the stand as ordinary contents and vanilla's
            // TryDropThingsToMakeRoomForThingOfDef evicts whatever conflicts
            // when the next owner first swaps.
            parent.TryGetComp<CompShiftStand>()?.AbandonLedger(pawn);
            base.TryUnassignPawn(pawn, sort, uninstall);
        }

        public override void ForceRemovePawn(Pawn pawn)
        {
            parent.TryGetComp<CompShiftStand>()?.AbandonLedger(pawn);
            base.ForceRemovePawn(pawn);
        }
    }
}
