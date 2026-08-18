using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Ownership for an outfit stand. The base comp already supplies the "set
    /// owner" gizmo and the <c>Dialog_AssignBuildingOwner</c> window; this
    /// subclass narrows the candidate list — a stand that dresses for
    /// doctoring should not offer itself to a colonist who cannot doctor —
    /// and owns its own scribing (see <see cref="PostExposeData"/>: the
    /// base's generic keys collide with Outfit Stands Plus' sibling comp).
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
        /// Scribes the assignment lists under mod-prefixed keys, replacing the
        /// base comp's scribing entirely — deliberately no base call.
        ///
        /// Comps scribe FLAT into their parent thing's save node
        /// (<c>ThingWithComps.ExposeData</c> just runs each comp in order,
        /// <c>:237-251</c>), and the base writes the generic
        /// <c>assignedPawns</c>/<c>uninstalledAssignedPawns</c> keys
        /// (<c>CompAssignableToPawn.cs:185-195</c>). Outfit Stands Plus puts a
        /// second <c>CompAssignableToPawn</c> subclass on this same building,
        /// and duplicate keys do not error on load — BOTH comps read the
        /// FIRST node under the name, so ownership smears between the two
        /// mods on every save/load. Unique keys end our half of that; theirs
        /// then round-trips correctly too, because ours no longer shadows
        /// its key.
        /// </summary>
        /// <summary>
        /// Per-load migration decisions, made in LoadingVars and REPLAYED in
        /// ResolvingCrossRefs. Working state, never scribed. Fields, not a
        /// property, and internal — the hot-swap rules.
        /// </summary>
        internal bool migrateAssigned;
        internal bool migrateUninstalled;

        public override void PostExposeData()
        {
            Scribe_Collections.Look(ref assignedPawns, "shiftChangeAssignedPawns", LookMode.Reference);
            Scribe_Collections.Look(ref uninstalledAssignedPawns, "shiftChangeUninstalledAssignedPawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Decide the migration HERE, and only here. LoadingVars is
                // the one pass where a Look's outcome reveals whether the
                // node exists: the loader's XML cursor is only alive during
                // it (ScribeLoader.EnterNode consults the document iff
                // curXmlParent != null; in the later passes it is pure path
                // bookkeeping and always "succeeds"), and a missing node
                // nulls the list while a present one leaves it untouched.
                migrateAssigned = assignedPawns == null;
                migrateUninstalled = uninstalledAssignedPawns == null;
            }
            if (Scribe.mode == LoadSaveMode.LoadingVars
                || Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
            {
                // v1.0.0/v1.0.1 saves scribed through the base under its
                // generic keys; read those under the SAME decision in BOTH
                // load passes. Reference lists load in two phases —
                // LoadingVars registers the wanted load-ids in a bank keyed
                // on parent + node path, ResolvingCrossRefs collects them —
                // so an asymmetric fallback registers an owner it never
                // collects.
                //
                // The flags, not a null re-test, carry the decision into
                // the second pass. By then the primary Looks above have
                // already consumed their own missing-node placeholders and
                // handed back EMPTY lists (TakeResolvedRefList never
                // returns null), so "is the list null" stops meaning
                // anything. That exact re-test shipped once and lost the
                // owner: registered in pass one, skipped in pass two,
                // reaped as "List with 1 elements" in the loader's
                // unconsumed-loadIDs warning.
                //
                // Beside Outfit Stands Plus, the one-time migration load
                // also has their comp reading the same legacy node: the
                // bank rejects their duplicate registration and their comp
                // comes up unowned, at the cost of two log errors — one
                // load, on already-degraded saves, and strictly better than
                // the silent cross-adoption it replaces.
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
                // The base's PostLoadInit scrub, applied to lists the base no
                // longer loads for us — plus null-safety for saves that
                // predate the comp entirely.
                assignedPawns = assignedPawns ?? new List<Pawn>();
                uninstalledAssignedPawns = uninstalledAssignedPawns ?? new List<Pawn>();
                assignedPawns.RemoveAll(p => p == null);
                uninstalledAssignedPawns.RemoveAll(p => p == null);
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
