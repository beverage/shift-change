using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Ownership for an outfit stand: narrows the base comp's candidates to pawns who can do
    /// the stand's work, and owns its scribing (see <see cref="PostExposeData"/>).
    ///
    /// <para>Its list is the stand's only owner list. Another mod's assignable comp on the
    /// building (Outfit Stands Plus adds one) holds a copy (<see cref="SyncForeignOwners"/>),
    /// and its own Set owner is hidden (<see cref="Patch_ForeignOwnerGizmos"/>). Why:
    /// docs/DESIGN.md, "Ownership and the ledger".</para>
    ///
    /// <para>The list is a set (the XML raises <c>maxAssignedPawnsCount</c> to 1000), but
    /// <c>Building_OutfitStand.HasRoomForApparelOfDef</c> is a conflict check, not a count: a
    /// stand holds one outfit, so owners take turns and the ledger has one borrower.</para>
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

                IEnumerable<Pawn> colonists = WithinForeignCandidates(parent.Map.mapPawns.FreeColonists);
                List<WorkTypeDef> works = parent.TryGetComp<CompShiftStand>()?.WorkTypes;
                if (works == null || works.Count == 0)
                {
                    // No work types (a roleless room, a recreation- or sleep-only stand, or one
                    // not used for shift changes): every colonist may own it.
                    return colonists;
                }
                return colonists.Where(p => works.Any(w => !p.WorkTypeIsDisabled(w)));
            }
        }

        // Migration decisions: made in LoadingVars, replayed in ResolvingCrossRefs, never scribed.
        internal bool migrateAssigned;
        internal bool migrateUninstalled;

        /// <summary>
        /// Whether another mod's assignable comp shares this stand, which makes the generic
        /// <c>assignedPawns</c> nodes in a save its data, not our legacy format: reading them
        /// too would register the same load ids twice.
        /// </summary>
        internal bool ForeignAssignableBesideUs()
        {
            List<ThingComp> comps = parent.AllComps;
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] is CompAssignableToPawn && !(comps[i] is CompAssignableToPawn_ShiftStand))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether this list has been reconciled with another mod's assignable comp, once per
        /// stand (<see cref="ReconcileForeignOwners"/>); after that this list is the authority.
        /// True on a new stand and scribed with a default of false, so a stand from an older
        /// save loads needing it: <c>Scribe_Values</c> returns the default for an absent node
        /// and omits the node when the value equals the default, so only true is ever written.
        /// </summary>
        internal bool ownersUnified = true;

        /// <summary>Key for <c>Log.ErrorOnce</c>, mixed with a foreign comp's type.</summary>
        internal const int ForeignOwnerErrorKey = 0x53434F50;

        /// <summary>
        /// Adds an owner, in both modes. It must not replace the list: the base's
        /// <c>PostSpawnSetup</c> re-adds a reinstalled stand's parked owners through this call,
        /// one at a time. The one-owner pick is <see cref="AssignSoleOwner"/>.
        /// </summary>
        public override void TryAssignPawn(Pawn pawn)
        {
            base.TryAssignPawn(pawn);
            SyncForeignOwners();
        }

        /// <summary>Makes <paramref name="pawn"/> the only owner, replacing the list.</summary>
        internal void AssignSoleOwner(Pawn pawn)
        {
            foreach (Pawn owner in AssignedPawnsForReading.ToList())
            {
                if (owner != pawn)
                {
                    base.TryUnassignPawn(owner);
                }
            }
            TryAssignPawn(pawn);
        }

        /// <summary>
        /// Whether the owner dialog picks one colonist (<see cref="AssignSoleOwner"/>) instead
        /// of editing a group: true on a stand not used for shift changes. A group the stand
        /// held in shift use is kept, not cleared on the way in
        /// (<see cref="CompShiftStand.SetExcluded"/>) or on load, until a pick replaces it.
        /// Why: docs/DESIGN.md, "Ownership and the ledger".
        /// </summary>
        internal bool SingleOwnerOnly => parent.TryGetComp<CompShiftStand>()?.IsExcluded ?? false;

        public override void TryUnassignPawn(Pawn pawn, bool sort = true, bool uninstall = false)
        {
            base.TryUnassignPawn(pawn, sort, uninstall);
            SyncForeignOwners();
        }

        public override void ForceAddPawn(Pawn pawn)
        {
            base.ForceAddPawn(pawn);
            SyncForeignOwners();
        }

        public override void ForceRemovePawn(Pawn pawn)
        {
            base.ForceRemovePawn(pawn);
            SyncForeignOwners();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            // The base re-adds a reinstalled stand's owners first, through TryAssignPawn. The
            // sync stays idle until the reconcile has run, so the reconcile reads the foreign
            // list as the save left it.
            base.PostSpawnSetup(respawningAfterLoad);
            UnifyOwners();
        }

        /// <summary>
        /// The reconcile if due, then the copy, on every spawn (load, reinstall, gravship landing).
        /// The copy runs every time because the foreign list drifts unwatched: Outfit Stands
        /// Plus' assign sweep unassigns its comp on every stand on the map, ours included, and
        /// saves are edited by hand.
        /// </summary>
        internal void UnifyOwners()
        {
            if (!ownersUnified)
            {
                ReconcileForeignOwners();
                ownersUnified = true;
            }
            SyncForeignOwners();
        }

        /// <summary>
        /// The one-time reconcile for a stand from an older save, where the two lists were
        /// independent. If only one names anyone it becomes the list; if both do and they
        /// disagree, the one the player could see wins: ours in shift use, the foreign one on a
        /// stand not used for shift changes. Why adopting is right even onto a pooled stand:
        /// docs/DESIGN.md, "Ownership and the ledger".
        /// </summary>
        internal void ReconcileForeignOwners()
        {
            List<Pawn> theirs = null;
            List<ThingComp> comps = parent.AllComps;
            for (int i = 0; i < comps.Count && theirs == null; i++)
            {
                if (comps[i] is CompAssignableToPawn foreign && !(foreign is CompAssignableToPawn_ShiftStand))
                {
                    List<Pawn> adoptable = foreign.AssignedPawnsForReading.Where(Adoptable).ToList();
                    if (adoptable.Count > 0)
                    {
                        theirs = adoptable;
                    }
                }
            }
            if (theirs == null)
            {
                return;
            }
            List<Pawn> ours = AssignedPawnsForReading;
            bool agree = ours.Count == theirs.Count && theirs.All(ours.Contains);
            bool excluded = parent.TryGetComp<CompShiftStand>()?.IsExcluded ?? false;
            if (agree || (ours.Count > 0 && !excluded))
            {
                return;
            }
            // Through the overrides, not the list: they sort as the base does, and their sync
            // is idle until ownersUnified is set.
            foreach (Pawn pawn in ours.ToList())
            {
                ForceRemovePawn(pawn);
            }
            foreach (Pawn pawn in theirs)
            {
                ForceAddPawn(pawn);
            }
        }

        /// <summary>
        /// Who the reconcile may adopt. Vanilla's unclaim never reaches stands, and before the
        /// copy our reaper cleared only this comp, so a foreign list from an older save can
        /// still name a pawn who has since died or left.
        /// </summary>
        internal static bool Adoptable(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Destroyed && pawn.IsColonist;
        }

        /// <summary>
        /// Copies this list into every other assignable comp on the stand: the owners when they
        /// fit its <c>TotalSlots</c>, nothing otherwise. It writes through <c>ForceAddPawn</c>
        /// and <c>ForceRemovePawn</c>, never the foreign <c>TryAssignPawn</c>, which Outfit
        /// Stands Plus overrides with a sweep that unassigns the pawn from every other stand.
        /// Idle until the reconcile has run (<see cref="ownersUnified"/>, see
        /// <see cref="PostSpawnSetup"/>): a copy written first would erase the foreign list the
        /// reconcile has to read.
        /// </summary>
        internal void SyncForeignOwners()
        {
            if (!ownersUnified || parent == null)
            {
                return;
            }
            List<ThingComp> comps = parent.AllComps;
            for (int i = 0; i < comps.Count; i++)
            {
                if (!(comps[i] is CompAssignableToPawn foreign) || foreign is CompAssignableToPawn_ShiftStand)
                {
                    continue;
                }
                try
                {
                    CopyOwnersInto(foreign);
                }
                catch (System.Exception e)
                {
                    // Their override threw. Our assignment stands; only their list is left stale.
                    Log.ErrorOnce("[ShiftChange] could not copy a stand's owners into "
                                  + foreign.GetType().FullName + ": " + e,
                                  ForeignOwnerErrorKey ^ foreign.GetType().GetHashCode());
                }
            }
        }

        internal void CopyOwnersInto(CompAssignableToPawn foreign)
        {
            List<Pawn> ours = AssignedPawnsForReading;
            bool fits = ours.Count > 0 && ours.Count <= foreign.TotalSlots;
            List<Pawn> theirs = foreign.AssignedPawnsForReading;
            List<Pawn> stale = null;
            for (int i = 0; i < theirs.Count; i++)
            {
                if (!fits || !ours.Contains(theirs[i]))
                {
                    (stale ?? (stale = new List<Pawn>())).Add(theirs[i]);
                }
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    foreign.ForceRemovePawn(stale[i]);
                }
            }
            if (!fits)
            {
                return;
            }
            for (int i = 0; i < ours.Count; i++)
            {
                if (!theirs.Contains(ours[i]))
                {
                    foreign.ForceAddPawn(ours[i]);
                }
            }
        }

        /// <summary>
        /// Narrows <paramref name="pawns"/> to those every other assignable comp on the stand
        /// would offer, which keeps the other mod's eligibility rules (Outfit Stands Plus keeps
        /// adult and child stands apart) now that ours is the only Set owner shown.
        /// </summary>
        internal IEnumerable<Pawn> WithinForeignCandidates(IEnumerable<Pawn> pawns)
        {
            List<ThingComp> comps = parent.AllComps;
            for (int i = 0; i < comps.Count; i++)
            {
                if (!(comps[i] is CompAssignableToPawn foreign) || foreign is CompAssignableToPawn_ShiftStand)
                {
                    continue;
                }
                HashSet<Pawn> allowed;
                try
                {
                    allowed = new HashSet<Pawn>(foreign.AssigningCandidates);
                }
                catch (System.Exception e)
                {
                    Log.ErrorOnce("[ShiftChange] could not read a stand's candidates from "
                                  + foreign.GetType().FullName + ": " + e,
                                  ForeignOwnerErrorKey ^ foreign.GetType().GetHashCode() ^ 1);
                    continue;
                }
                pawns = pawns.Where(allowed.Contains);
            }
            return pawns;
        }

        /// <summary>
        /// Scribes the owner lists under mod-prefixed keys, with no base call. Comps scribe flat
        /// into the thing's save node, and the base's generic keys would collide with the
        /// second <c>CompAssignableToPawn</c> that Outfit Stands Plus puts on the building: both
        /// would look up the same node, the second's registration of it is refused with an
        /// error, and that comp loads empty.
        /// </summary>
        public override void PostExposeData()
        {
            Scribe_Collections.Look(ref assignedPawns, "shiftChangeAssignedPawns", LookMode.Reference);
            Scribe_Collections.Look(ref uninstalledAssignedPawns, "shiftChangeUninstalledAssignedPawns", LookMode.Reference);
            Scribe_Values.Look(ref ownersUnified, "shiftChangeOwnersUnified", defaultValue: false);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Decide the migration here only: LoadingVars is the one pass where a Look reveals
                // whether the node exists (a missing node nulls the list, a present one leaves
                // it untouched); later passes only follow the path and always succeed.
                //
                // Migrate only when the generic keys are uncontested. Beside another mod's
                // assignable comp (with Outfit Stands Plus active, every stand has one) they are
                // its live data, not our legacy format, so start empty and read nothing;
                // ReconcileForeignOwners recovers an owner this comp saved there.
                bool contested = ForeignAssignableBesideUs();
                migrateAssigned = assignedPawns == null && !contested;
                migrateUninstalled = uninstalledAssignedPawns == null && !contested;
            }
            if (Scribe.mode == LoadSaveMode.LoadingVars
                || Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
            {
                // Read the base's generic keys under the same decision in both load passes: a
                // reference list registers its load ids in LoadingVars, keyed on parent and node
                // path, and collects them in ResolvingCrossRefs, so a one-sided read registers an
                // owner it never collects. Carry the decision in the flags, not a null re-test:
                // by the second pass the Looks above have returned empty lists, never null.
                if (migrateAssigned)
                {
                    Scribe_Collections.Look(ref assignedPawns, "assignedPawns", LookMode.Reference);
                }
                if (migrateUninstalled)
                {
                    Scribe_Collections.Look(ref uninstalledAssignedPawns, "uninstalledAssignedPawns", LookMode.Reference);
                }
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                migrateAssigned = false;
                migrateUninstalled = false;
                // The base's PostLoadInit scrub, for lists the base no longer loads, plus null
                // safety for saves that predate this comp.
                assignedPawns = assignedPawns ?? new List<Pawn>();
                uninstalledAssignedPawns = uninstalledAssignedPawns ?? new List<Pawn>();
                assignedPawns.RemoveAll(p => p == null);
                uninstalledAssignedPawns.RemoveAll(p => p == null);
            }
        }

        /// <summary>
        /// Keeps a stand's owners across a reinstall. Any despawn but <c>WillReplace</c>, a
        /// minify included, parks <c>assignedPawns</c> in <c>uninstalledAssignedPawns</c>, and
        /// the next spawn offers each back here. The base answers false and clears the parked
        /// list either way, and only beds and thrones override it. Deliberately not gated on
        /// <see cref="AssigningCandidates"/>: it reads work from the room, which mid-move is
        /// wherever the player set the stand down, and re-checking would drop the owner.
        /// </summary>
        protected override bool CanSetUninstallAssignedPawn(Pawn pawn)
        {
            if (pawn == null || AssignedAnything(pawn) || !(bool)CanAssignTo(pawn))
            {
                return false;
            }
            return pawn.IsColonist;
        }

        /// <summary>
        /// The base's Set owner gizmo with its hotkey stripped and its window swapped for ours.
        /// <see cref="Patch_ForeignOwnerGizmos"/> hides other mods' own, so this is the only one.
        /// </summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                // The base binds this gizmo to Misc4 (N), which is copy settings on a storage
                // building. Never bind N, J, F or O on anything with copyable settings.
                if (gizmo is Command command)
                {
                    command.hotKey = null;
                }
                // Swap the window, keep the base's label, icon and description. Vanilla's
                // Dialog_AssignBuildingOwner keeps its row drawers private, has no header seam,
                // and its Assign always adds here, so the gender column, filter, bulk buttons and
                // one-owner pick need our own window. Replacing the action is the whole change:
                // no patch on a vanilla window, so no other assignable building is touched.
                if (gizmo is Command_Action assign)
                {
                    assign.action = () => Find.WindowStack.Add(new Dialog_AssignStandOwners(this));
                    // Not groupable: stands with the same label merge when several are selected,
                    // and a merged button opens every stand's dialog, each closing the one
                    // before, so it edits one list while looking as if it edits all.
                    assign.groupable = false;
                }
                yield return gizmo;
            }
        }

        /// <summary>
        /// Names the borrower over the stand at closest zoom, so a room of pool stands can be
        /// read at a glance. The base draws the assigned owner, which says nothing about a pool
        /// stand, and <c>OverlayTypes.Forbidden</c> means forbidden and cannot say whose stand
        /// it is.
        /// </summary>
        public override void DrawGUIOverlay()
        {
            CompShiftStand shift = parent.TryGetComp<CompShiftStand>();
            if (shift != null && shift.IsExcluded)
            {
                // Not used for shift changes, so no borrower to name. Where another mod's
                // assignable shares the stand it draws from its copy, and drawing here too would
                // print the owner twice.
                return;
            }
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

        /// <summary>
        /// Names the owner on the button: the base label is always "Set owner", and the base
        /// comp never shows who owns the thing (beds only seem to, as
        /// <c>Building_Bed.GetInspectString</c> writes the owner itself).
        /// </summary>
        protected override string GetAssignmentGizmoLabel()
        {
            List<Pawn> assigned = AssignedPawnsForReading;
            if (assigned.Count == 1)
            {
                return "ShiftChange.OwnerGizmoLabel".Translate(assigned[0].LabelShort);
            }
            if (assigned.Count > 1)
            {
                return "ShiftChange.OwnerGizmoLabelMany".Translate(assigned.Count);
            }
            // Unowned rather than shared when pooling is off, or on a stand not used for shift
            // changes, which pools nothing: vanilla's "Set owner" says that best. Inlined, not
            // base.GetAssignmentGizmoLabel(): it is protected, and a hot-swapped method body
            // cannot pass the protected-access check.
            CompShiftStand shift = parent.TryGetComp<CompShiftStand>();
            return ShiftChangeMod.PoolingEnabled && (shift == null || !shift.IsExcluded)
                ? "ShiftChange.PoolGizmoLabel".Translate()
                : "CommandThingSetOwnerLabel".Translate();
        }

        protected override string GetAssignmentGizmoDesc()
        {
            CompShiftStand comp = parent.TryGetComp<CompShiftStand>();
            if (comp != null && comp.IsExcluded)
            {
                // Owners drive nothing of ours in this mode; they only reach another mod's
                // per-pawn buttons, through the copy, and nothing else says so.
                return "ShiftChange.AssignDescExcluded".Translate();
            }
            // Must match CompShiftStand.CompInspectStringExtra, which also asks ServesSomething:
            // a recreation-only or sleep-only stand has no work types by design.
            if (comp == null || !comp.ServesSomething)
            {
                return "ShiftChange.AssignDescNoWork".Translate();
            }
            string works = comp.WorkTypesLabel();
            return ShiftChangeMod.PoolingEnabled
                ? comp.ByCatchment("ShiftChange.AssignDesc").Translate(works)
                : "ShiftChange.AssignDescNoPool".Translate(works);
        }

        // Unassigning deliberately does not abandon the ledger: an unassigned stand is still
        // usable from the pool, so a mid-shift pawn keeps their claim. Patch_UnclaimStands
        // reaps a gone borrower's ledger, and has to, since a pool borrower was never assigned.
    }
}
