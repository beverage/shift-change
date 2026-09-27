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
    /// <para><b>One owner list per stand, and it is this one.</b> Another mod's
    /// assignable comp on the same building (Outfit Stands Plus adds one) used
    /// to keep a list of its own here. It was hidden behind our Set owner while
    /// the stand did shift work, but it stayed live everywhere that mod reads
    /// it: its per-pawn "equip outfit" / "return to stand" button and its
    /// inspect line. A stale owner there sent a colonist to swap another
    /// colonist's parked kit, and nothing on the stand could show or clear it.
    /// So that comp now holds a copy of this list
    /// (<see cref="SyncForeignOwners"/>), in both modes, and its own Set owner
    /// is hidden everywhere this comp is present.</para>
    ///
    /// <para>The owner list is a SET (the XML raises
    /// <c>maxAssignedPawnsCount</c> to 1000), but the stand still holds one
    /// outfit: <c>Building_OutfitStand.HasRoomForApparelOfDef</c> is a conflict
    /// check rather than a count (<c>:332-342</c>), so owners take turns and the
    /// ledger has one borrower at a time.</para>
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

        /// <summary>
        /// Whether another mod's assignable comp shares this stand. When one
        /// does, the generic <c>assignedPawns</c> nodes in a save belong to
        /// IT — reading them as our legacy data would fight that comp over
        /// the same load-id bank key (a duplicate-registration error on the
        /// loading pass, a failed take on the resolving one, in red, on
        /// every stand that predates us).
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
        /// Whether this stand's owner list has been reconciled with a foreign
        /// assignable's since the single-list model arrived. The reconcile runs
        /// once per stand and never again: after it, this list is the only
        /// authority, and a foreign list that differs is drift to overwrite
        /// rather than a player's choice to adopt.
        ///
        /// <para>True from construction, so a stand built under this version
        /// never reconciles; the scribe default is false, so every stand in an
        /// older save loads needing it. That pairing is the whole migration
        /// switch. <c>Scribe_Values</c> hands the default back when the node is
        /// absent and omits the node whenever the value matches it
        /// (<c>Scribe_Values.cs:70-78,88</c>), so the only value ever written
        /// is true.</para>
        /// </summary>
        internal bool ownersUnified = true;

        /// <summary>
        /// Keys <c>Log.ErrorOnce</c> per foreign comp type, so a mod whose
        /// override throws is reported once rather than on every assignment.
        /// </summary>
        internal const int ForeignOwnerErrorKey = 0x53434F50;

        public override void TryAssignPawn(Pawn pawn)
        {
            base.TryAssignPawn(pawn);
            SyncForeignOwners();
        }

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
            // The base restores a reinstalled stand's owners first, through
            // TryAssignPawn, and SyncForeignOwners stays idle until the
            // reconcile below has run, so the foreign list is still exactly as
            // the save left it when the reconcile reads it.
            base.PostSpawnSetup(respawningAfterLoad);
            UnifyOwners();
        }

        /// <summary>
        /// The reconcile if this stand has never had one, then the copy. Every
        /// spawn comes through here (a load, a reinstall, a gravship landing),
        /// so a copy that drifted while nothing was watching, whether through
        /// another mod's own sweep or a save edited by hand, is simply
        /// rewritten.
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
        /// The one-time reconcile for a stand from an older save, where the two
        /// lists were independent. If only one of them has owners, it becomes
        /// THE list. If both do and they disagree, the one the player could see
        /// wins: this one on a stand in shift use, where the foreign Set owner
        /// was hidden, and the foreign one on a stand set to "Not used for
        /// shift changes", where ours was.
        ///
        /// <para>Adopting a foreign owner onto a shift stand whose own list is
        /// empty turns a pooled stand into an owned one. That owner was already
        /// live on the other mod's button, so adoption makes it visible and
        /// editable rather than inventing it. The same rule keeps the
        /// assignments of anyone who used Outfit Stands Plus before this mod:
        /// their stands would otherwise load pooled, open to any capable
        /// colonist while somebody's own clothes sit inside. It also recovers
        /// the one case the key migration in <see cref="PostExposeData"/> gave
        /// up on.</para>
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
            // Through the overrides rather than the list: they sort as the
            // base does, and their sync is a no-op until ownersUnified is set.
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
        /// Who the reconcile may adopt. Nothing ever reaped a foreign list
        /// before the copy existed (vanilla's unclaim does not reach stands,
        /// and our reaper cleared only this comp), so it can still name a
        /// colonist who has since died or left. Those stay behind.
        /// </summary>
        internal static bool Adoptable(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Destroyed && pawn.IsColonist;
        }

        /// <summary>
        /// Writes this list into every foreign assignable on the stand when it
        /// fits that comp's capacity, and empties it otherwise.
        ///
        /// <para>Through the base class's <c>ForceAddPawn</c> and
        /// <c>ForceRemovePawn</c>, never the foreign <c>TryAssignPawn</c>:
        /// Outfit Stands Plus overrides that one with a sweep that unassigns
        /// the pawn from every other stand on the map, and a colonist here
        /// routinely owns several. No foreign type is named, so any mod's
        /// assignable gets the same treatment.</para>
        ///
        /// <para>"Fits" is <c>TotalSlots</c>, which Outfit Stands Plus leaves at
        /// vanilla's default of one. One owner makes a personal stand in that
        /// mod's model, and its button and inspect line should name that
        /// owner. A shared or unowned stand has no single owner to name, and an
        /// empty copy keeps its button off the stand altogether.</para>
        ///
        /// <para>Idle until the first-load reconcile has run
        /// (<see cref="ownersUnified"/>). The base comp restores a reinstalled
        /// stand's owners through <see cref="TryAssignPawn"/> before
        /// <see cref="PostSpawnSetup"/> reaches the reconcile, and a copy written
        /// then would erase the foreign list the reconcile has to read.</para>
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
                    // Their override threw. Our own assignment has already
                    // happened and stands; a copy that failed only leaves their
                    // button where it was.
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
        /// Narrows <paramref name="pawns"/> to the ones every foreign assignable
        /// on this stand would offer itself. With our Set owner the only one on
        /// the stand, the other mod's eligibility rules (Outfit Stands Plus keeps
        /// adult and child stands apart) would otherwise be lost, and the copy
        /// would hand it an owner it never accepts.
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

        public override void PostExposeData()
        {
            Scribe_Collections.Look(ref assignedPawns, "shiftChangeAssignedPawns", LookMode.Reference);
            Scribe_Collections.Look(ref uninstalledAssignedPawns, "shiftChangeUninstalledAssignedPawns", LookMode.Reference);
            Scribe_Values.Look(ref ownersUnified, "shiftChangeOwnersUnified", defaultValue: false);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Decide the migration HERE, and only here. LoadingVars is
                // the one pass where a Look's outcome reveals whether the
                // node exists: the loader's XML cursor is only alive during
                // it (ScribeLoader.EnterNode consults the document iff
                // curXmlParent != null; in the later passes it is pure path
                // bookkeeping and always "succeeds"), and a missing node
                // nulls the list while a present one leaves it untouched.
                //
                // And only when the generic keys are UNCONTESTED. On a stand
                // that also carries another mod's assignable comp — Outfit
                // Stands Plus' own stands always, and any stand in a save
                // where that mod arrived first — the generic nodes are that
                // comp's live data, not our legacy format. Missing prefixed
                // keys there mean this comp is simply NEW on this stand:
                // start empty, read nothing, and their comp loads its owner
                // in peace. (The one edge this gives up, a save from the four
                // pre-v1.0.2 days with BOTH mods and an owner set through us,
                // is recovered on first spawn anyway: ReconcileForeignOwners
                // adopts the foreign list when ours is empty, and that list
                // holds exactly that owner.)
                bool contested = ForeignAssignableBesideUs();
                migrateAssigned = assignedPawns == null && !contested;
                migrateUninstalled = uninstalledAssignedPawns == null && !contested;
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
        /// Give the owners back when the stand is set down again.
        ///
        /// <para>The base comp already does the hard half: any despawn that is
        /// not <c>WillReplace</c> parks <c>assignedPawns</c> in
        /// <c>uninstalledAssignedPawns</c> (<c>CompAssignableToPawn.cs:197-206</c>),
        /// and the next spawn offers each of them back — but only to comps
        /// that say yes HERE, and the base's answer is a flat <c>false</c>
        /// (<c>:233-236</c>). It then CLEARS the parked list either way
        /// (<c>:219</c>). Beds and thrones are the only two types in the
        /// engine that override this, so every other assignable building
        /// silently loses its owners to a reinstall, and so did we: minifying
        /// is an ordinary despawn (<c>MinifyUtility.MakeMinified:15</c>), so
        /// moving a stand three tiles left unowned it.</para>
        ///
        /// <para>Deliberately NOT gated on <see cref="AssigningCandidates"/>.
        /// That list narrows to pawns capable of the stand's work, and the
        /// work is read from the ROOM the stand is standing in — which
        /// mid-relocation is wherever the player just put it. Re-validating
        /// against it would quietly drop the owner of a doctoring stand the
        /// moment it was set down in a kitchen. Assignment is the player's
        /// standing intent; moving the furniture is not a change of intent.
        /// Shape otherwise follows <c>CompAssignableToPawn_Throne:30-38</c>.</para>
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
        /// Vanilla's label is always "Set owner" — the base comp never reports
        /// who owns the thing, anywhere. Beds only appear to, because
        /// <c>Building_Bed.GetInspectString</c> writes the owner itself. On a
        /// stand that leaves ownership invisible outside the assign dialog, so
        /// name it on the button.
        /// </summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // One Set owner per stand, and it is this one in both modes. The
            // list it edits is the stand's only owner list; another mod's
            // assignable holds a copy of it (SyncForeignOwners), and
            // Patch_ForeignOwnerGizmos hides that mod's own Set owner wherever
            // this comp is present, so the two cannot drift apart again.
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                // The base comp hardcodes Misc4 (N) on the assignment gizmo
                // (CompAssignableToPawn.cs:176) — harmless on beds and
                // thrones, but the outfit stand is a STORAGE building, and N
                // is copy-settings there (StorageSettingsClipboard.cs:40).
                // House rule (decided 2026-08-08): on anything with
                // copyable settings, never bind over N, J, F or O.
                if (gizmo is Command command)
                {
                    command.hotKey = null;
                }
                // Swap the WINDOW, keep the base's label, icon and
                // description. Vanilla's dialog closes itself after one
                // assignment (gated on MaxAssignedPawnsCount == 1) and its row
                // drawers are private, so neither multi-select nor a gender
                // column is reachable from outside it. Replacing the action is
                // the whole intervention — no patch on a vanilla window, so no
                // other assignable building in the load order changes.
                if (gizmo is Command_Action assign)
                {
                    assign.action = () => Find.WindowStack.Add(new Dialog_AssignStandOwners(this));
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
            if (shift != null && shift.IsExcluded)
            {
                // Not used for shift changes, so there is no borrower to name.
                // Where another mod's assignable shares the stand, it draws the
                // owner from its copy of this list and ours would print the
                // same name twice; a stand with no such comp has never had a
                // label from us in this mode.
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
            // With pooling off an unassigned stand is not "shared", it is
            // simply unowned — vanilla's own "Set owner" says that best. So is
            // a stand not used for shift changes, which pools nothing whatever
            // the setting says; now that the owner control no longer yields to
            // another mod's there, this is the label a wardrobe stand shows.
            // Inlined rather than base.GetAssignmentGizmoLabel(): the base is
            // PROTECTED (CompAssignableToPawn.cs:154-156), and hot-swapped
            // bodies on the twin type cannot pass the protected-access check
            // — the same failure Window.Margin produced (2026-08-08).
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
                // Its owners drive nothing of ours in this mode. What they
                // still reach is another mod's per-pawn buttons, through the
                // copy, and nothing else on the stand says so.
                return "ShiftChange.AssignDescExcluded".Translate();
            }
            // The inert test must match CompInspectStringExtra's: a
            // recreation-only or sleep-only stand has ZERO work types by
            // design (the trigger-only guard in CompShiftStand.WorkTypes), and
            // calling the feature's flagship state "no work type yet"
            // contradicted the inspect pane on the same stand (review,
            // 2026-08-15). Every trigger added here has to be added there too.
            if (comp == null || (comp.WorkTypes.Count == 0
                                 && !comp.HandlesRecreation() && !comp.HandlesRest()))
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
