using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    public class CompProperties_ShiftStand : CompProperties
    {
        public CompProperties_ShiftStand()
        {
            compClass = typeof(CompShiftStand);
        }
    }

    /// <summary>
    /// The Shift Change half of a vanilla outfit stand: which work type it
    /// dresses for, and the checkout ledger for whoever currently has its
    /// uniform out.
    ///
    /// The ledger exists because the stand itself has no idea whose clothes it
    /// holds — <see cref="Building_OutfitStand"/> scribes only its container,
    /// its storage settings and <c>allowRemovingItems</c>, and vanilla's own
    /// <c>JobDriver_UseOutfitStand</c> hands any wearable item to whoever turns
    /// up. Ownership comes from the sibling
    /// <see cref="CompAssignableToPawn_ShiftStand"/>; this comp records which
    /// specific items moved which way, so the return trip puts the right
    /// clothes back on the right pawn.
    /// </summary>
    public class CompShiftStand : ThingComp
    {
        /// <summary>The owner's own clothes, currently parked in the stand.</summary>
        internal List<Apparel> storedOwnerApparel = new List<Apparel>();

        /// <summary>The stand's clothes, currently worn by the owner.</summary>
        internal List<Apparel> issuedUniform = new List<Apparel>();

        /// <summary>
        /// The subset of <see cref="storedOwnerApparel"/> that was FORCE-WORN
        /// when it was checked in. Vanilla clears the forced flag on every
        /// removal (<c>Pawn_ApparelTracker.Notify_ApparelRemoved:784-790</c>),
        /// so without this record a force-worn duster comes back from a shift
        /// as ordinary policy-managed clothing and the optimizer swaps it away.
        /// </summary>
        internal List<Apparel> storedForcedApparel = new List<Apparel>();

        /// <summary>
        /// Who currently has this stand's uniform on. This — not the assigned
        /// owner — is the truth for the return trip, because an unassigned
        /// stand is a POOL stand that anyone capable may borrow.
        /// </summary>
        internal Pawn borrower;

        /// <summary>
        /// Player override — a SET, because rooms host families of work
        /// (principal, 2026-08-08). Empty means "infer from the room's role".
        /// </summary>
        internal List<WorkTypeDef> workTypeOverrides = new List<WorkTypeDef>();

        /// <summary>
        /// Pre-set-redesign saves scribed a single "workTypeOverride" def.
        /// Loaded once and folded into <see cref="workTypeOverrides"/>.
        /// </summary>
        internal WorkTypeDef workTypeOverrideLegacy;

        /// <summary>
        /// Opt-out. A decorative or storage stand standing in a roled room would
        /// otherwise join that room's pool, which is a surprise nobody asked
        /// for.
        /// </summary>
        internal bool excluded;

        /// <summary>
        /// Stands that currently have a uniform out on a pawn, so the return
        /// trip can be found without sweeping the map. Keyed by BORROWER and
        /// rebuilt on load, since <see cref="PostSpawnSetup"/> runs for every
        /// comp on both paths.
        /// </summary>
        internal static readonly Dictionary<Pawn, CompShiftStand> OnShiftStands =
            new Dictionary<Pawn, CompShiftStand>();

        public Building_OutfitStand Stand => parent as Building_OutfitStand;

        public bool OnShift => issuedUniform.Count > 0;

        public Pawn Borrower => borrower;

        /// <summary>
        /// Is this pawn still the colony's, for ledger purposes?
        ///
        /// <b>Faction, never spawnedness.</b> Banishment on a spawned colonist
        /// runs <c>pawn.SetFaction(null)</c> and stops
        /// (<c>PawnBanishUtility.cs:66-69</c>) — faction is the only field it
        /// touches, so faction is what we test. The mirror image is why
        /// spawnedness is the wrong axis: a gravship flight despawns the
        /// borrower with <c>WillReplace</c> and never touches their faction,
        /// and the stand is set back down BEFORE the pawns are
        /// (<c>GravshipPlacementUtility.cs:35-36</c>) — so at the moment
        /// <see cref="PostSpawnSetup"/> asks this on landing, a perfectly good
        /// borrower is despawned. Testing spawnedness here would destroy a
        /// live ledger on every landing.
        ///
        /// The <c>HostFaction</c> clause is vanilla's phrasing, from
        /// <c>CompAssignableToPawn.PlayerCanSeeAssignments</c>
        /// (<c>:28</c>) and <c>Banish</c>'s own entry guard (<c>:22</c>).
        /// </summary>
        internal static bool StillOurs(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }
            // Not Faction.OfPlayer — it logs an error when it cannot resolve
            // (Faction.cs:214-224). Not Faction.OfPlayerSilentFail either: it
            // ends in Find.FactionManager.OfPlayer (Faction.cs:259) and
            // Find.FactionManager dereferences World.factionManager unguarded
            // (Find.cs:188), so it throws when Find.World is null. This chain
            // is null-safe (Find.cs:100-109) and silent.
            Faction player = Find.World?.factionManager?.OfPlayer;
            if (player == null)
            {
                // Cannot tell, so keep the ledger. A predicate that reaps when
                // it does not know is one that eats live ledgers.
                return true;
            }
            return pawn.Faction == player || pawn.HostFaction == player;
        }

        /// <summary>
        /// The pawn this stand is reserved for, or null when it is a pool
        /// stand. Distinct from <see cref="Borrower"/>: assignment is the
        /// player's standing intent, borrowing is who is wearing it now.
        /// </summary>
        public Pawn AssignedOwner
        {
            get
            {
                CompAssignableToPawn comp = parent.TryGetComp<CompAssignableToPawn>();
                List<Pawn> assigned = comp?.AssignedPawnsForReading;
                return assigned != null && assigned.Count > 0 ? assigned[0] : null;
            }
        }

        public bool IsPool => AssignedOwner == null;

        /// <summary>
        /// Unassigned means pool: any pawn may take it — unless the player
        /// turned pooling off in mod settings, in which case an unassigned
        /// stand is inert and only explicit assignment participates. Assigned
        /// means reserved, and nobody else gets a look in — that is what
        /// keeps a surgeon's tailored kit off a passing hauler.
        /// </summary>
        public bool CanBeClaimedBy(Pawn pawn)
        {
            Pawn owner = AssignedOwner;
            if (owner != null)
            {
                return owner == pawn;
            }
            return ShiftChangeMod.PoolingEnabled;
        }

        /// <summary>
        /// Whether the stand holds anything to change into. An empty pool stand
        /// would otherwise be claimed, walked to, and swapped with — a wasted
        /// trip that looks like a bug.
        /// </summary>
        public bool HasWearable
        {
            get
            {
                Building_OutfitStand stand = Stand;
                if (stand == null)
                {
                    return false;
                }
                IReadOnlyList<Thing> held = stand.HeldItems;
                for (int i = 0; i < held.Count; i++)
                {
                    if (held[i] is Apparel)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        internal static readonly List<WorkTypeDef> NoWork = new List<WorkTypeDef>();

        /// <summary>
        /// The work types this stand dresses for: empty when the player
        /// excluded it, otherwise the explicit override set, otherwise the
        /// enclosing room's role defaults. Empty means inert — an ordinary
        /// vanilla outfit stand. Never null; treat as read-only.
        /// </summary>
        public List<WorkTypeDef> WorkTypes
        {
            get
            {
                if (excluded)
                {
                    return NoWork;
                }
                if (workTypeOverrides != null && workTypeOverrides.Count > 0)
                {
                    return workTypeOverrides;
                }
                if (!parent.Spawned)
                {
                    return NoWork;
                }
                return RoomWorkTypes.ForRole(parent.GetRoom()?.Role);
            }
        }

        public bool HandlesWork(WorkTypeDef work)
        {
            return work != null && WorkTypes.Contains(work);
        }

        public bool IsExcluded => excluded;

        public bool IsAutomatic => !excluded && (workTypeOverrides == null || workTypeOverrides.Count == 0);

        public void SetAutomatic()
        {
            excluded = false;
            workTypeOverrides?.Clear();
        }

        public void SetExcluded()
        {
            excluded = true;
            workTypeOverrides?.Clear();
        }

        /// <summary>
        /// Flips one work type in the effective set, entering custom mode from
        /// wherever the stand currently is: toggling while automatic seeds the
        /// custom set from the room's defaults first, toggling while excluded
        /// starts a fresh set, and emptying the custom set collapses back to
        /// excluded so the three states stay canonical.
        /// </summary>
        public void ToggleWork(WorkTypeDef work)
        {
            if (work == null)
            {
                return;
            }
            List<WorkTypeDef> effective = new List<WorkTypeDef>(WorkTypes);
            excluded = false;
            workTypeOverrides = workTypeOverrides ?? new List<WorkTypeDef>();
            workTypeOverrides.Clear();
            workTypeOverrides.AddRange(effective);
            if (!workTypeOverrides.Remove(work))
            {
                workTypeOverrides.Add(work);
            }
            if (workTypeOverrides.Count == 0)
            {
                SetExcluded();
            }
        }

        /// <summary>Display helper: "doctoring, researching" etc.</summary>
        public string WorkTypesLabel()
        {
            List<WorkTypeDef> works = WorkTypes;
            if (works.Count == 0)
            {
                return "ShiftChange.None".Translate();
            }
            List<string> labels = new List<string>(works.Count);
            for (int i = 0; i < works.Count; i++)
            {
                labels.Add(works[i].gerundLabel ?? works[i].labelShort ?? works[i].defName);
            }
            return labels.ToCommaList();
        }

        /// <summary>Called by SessionGuard when the loaded game changes.</summary>
        internal static void ResetSessionState()
        {
            OnShiftStands.Clear();
        }

        public static CompShiftStand OnShiftStandFor(Pawn pawn)
        {
            SessionGuard.Ensure();
            CompShiftStand comp;
            if (!OnShiftStands.TryGetValue(pawn, out comp))
            {
                return null;
            }
            // A stand that was destroyed while its uniform was out leaves a
            // stale entry; drop it rather than hand back a comp whose parent
            // is gone.
            if (comp == null || comp.parent == null || comp.parent.Destroyed
                || !comp.OnShift || comp.borrower != pawn)
            {
                OnShiftStands.Remove(pawn);
                return null;
            }
            return comp;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            SessionGuard.Ensure();

            // Rebuild from the BORROWER, not the assigned owner. Inferring it
            // from ownership was only ever right by coincidence — reassign a
            // stand while its uniform is out and the return trip would have
            // pointed at the wrong pawn.
            if (OnShift && !StillOurs(borrower))
            {
                // Nobody left to honour the ledger. Two ways in: a save from
                // before pooling (borrower null), or a borrower who left the
                // colony without reaching Pawn_Ownership.UnclaimAll —
                // banishment, which Patch_BanishStands now catches eagerly.
                // This branch is the repair path for saves that predate that
                // patch, and the backstop for any other faction-loss route we
                // do not hook.
                //
                // ReleaseBorrower rather than clearing the lists by hand: it
                // takes the FORCED flags off too, and a banished pawn is alive
                // and standing on the map in our force-worn uniform. It
                // null-guards the holder, so the pre-pooling case still works.
                // Not AbandonLedger — that fires Notify_StandFreed, which
                // sweeps the map for a catch-up dress, and running that from
                // inside Map.FinalizeLoading is a fine way to break a load.
                ReleaseBorrower();
            }
            else if (borrower != null && OnShift)
            {
                CompShiftStand incumbent;
                if (OnShiftStands.TryGetValue(borrower, out incumbent)
                    && incumbent != null && incumbent != this && incumbent.OnShift
                    && incumbent.borrower == borrower)
                {
                    // Two stands both claiming the same borrower, and only one
                    // can be their return trip. The incumbent keeps them; this
                    // ledger is the stale copy (a stand reinstalled from a box
                    // it was minified into mid-shift, carrying a checkout that
                    // has since moved on), so drop it rather than silently
                    // stealing the registry entry and stranding the uniform
                    // the other stand is still holding clothes for.
                    storedOwnerApparel.Clear();
                    issuedUniform.Clear();
                    storedForcedApparel.Clear();
                    borrower = null;
                }
                else
                {
                    OnShiftStands[borrower] = this;
                }
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            if (mode == DestroyMode.WillReplace)
            {
                // NOT a teardown — the stand is being lifted to another map and
                // will be put back down intact. Odyssey's gravship despawns
                // everything aboard this way (GravshipUtility.cs:389,397), and
                // Building_OutfitStand.DeSpawn deliberately KEEPS its contents
                // in this mode (:392), so the stand, the parked civvies and the
                // borrower all survive the flight. Releasing here destroyed
                // only the record of who owned what: the cook landed
                // permanently in whites with no return trip, and her next shift
                // read her own clothes as the room's uniform and force-wore
                // them — uniform and civvies swapped for good.
                //
                // The ledger is re-registered on landing by PostSpawnSetup, and
                // PostSwapMap below handles a borrower who did not come along.
                // Vanilla's own comp on this same building guards the identical
                // case (CompAssignableToPawn.cs:199).
                return;
            }
            // Deconstructed, burnt down, or minified into a box — either way
            // the stand is out of the borrower's reach and the ledger cannot
            // be honoured. Dropping the registry entry alone was not enough:
            // it left the uniform FORCE-WORN, and forced is what makes the
            // uniform stick. See ReleaseBorrower for why that is a trap with
            // no way out.
            ReleaseBorrower();
        }

        /// <summary>
        /// The stand has been set back down on a new map after a gravship
        /// flight. The ledger was kept across the trip on purpose (see
        /// <see cref="PostDeSpawn"/>), so the only open question is whether the
        /// borrower came too — a colonist left behind cannot walk back for
        /// their clothes, and their uniform would stay force-worn forever.
        ///
        /// Mirrors vanilla's <c>CompAssignableToPawn.PostSwapMap</c>
        /// (<c>CompAssignableToPawn.cs:222-230</c>) on the same building.
        /// </summary>
        public override void PostSwapMap()
        {
            base.PostSwapMap();
            if (borrower == null || !OnShift)
            {
                return;
            }
            if (borrower.DestroyedOrNull() || !borrower.SpawnedOrAnyParentSpawned)
            {
                ReleaseBorrower();
                return;
            }
            // Idempotent with PostSpawnSetup's own rebuild; ordering between
            // the two is not guaranteed and both must leave the registry right.
            OnShiftStands[borrower] = this;
        }

        /// <summary>
        /// Take the forced flag off every issued garment the holder still has
        /// on. Split out so <see cref="AbandonLedger"/> shares it with
        /// <see cref="ReleaseBorrower"/>: "ledger cleared ⇒ forced flags
        /// cleared" is one invariant, not a property that happened to hold on
        /// the routes we had thought about.
        ///
        /// It did not matter while every abandonment followed a DEATH, where
        /// the flags go with the corpse. Banishment leaves the pawn alive,
        /// spawned and wearing the uniform, and forced is what makes a uniform
        /// stick — so an abandonment that skipped this pinned a living
        /// colonist into it with every route back out already closed.
        /// </summary>
        internal void UnforceIssued(Pawn holder)
        {
            if (holder == null)
            {
                return;
            }
            for (int i = 0; i < issuedUniform.Count; i++)
            {
                Apparel apparel = issuedUniform[i];
                if (apparel != null && holder.apparel != null
                    && holder.apparel.WornApparel.Contains(apparel))
                {
                    holder.outfits?.forcedHandler?.SetForced(apparel, forced: false);
                }
            }
        }

        /// <summary>
        /// Hand the uniform to whoever is wearing it and free the stand.
        ///
        /// Clearing the ledger without clearing the forced flags pins the
        /// borrower into the uniform permanently, because every route back out
        /// keys off the ledger — the automatic return trip, the Change back
        /// button, and the optimizer pause all look the pawn up in
        /// <see cref="OnShiftStands"/> — while vanilla's own
        /// <c>JobGiver_OptimizeApparel</c> skips force-worn apparel by design.
        /// Remove the ledger and all four close at once, leaving the Assign
        /// tab's "Clear forced apparel" as the only remedy: lossy, and nobody
        /// finds it. So the flags come off first, and the garments stay on the
        /// pawn as ordinary policy-managed clothing that vanilla will replace
        /// in its own time.
        /// </summary>
        internal void ReleaseBorrower()
        {
            Pawn holder = borrower;
            if (holder != null)
            {
                UnforceIssued(holder);
                OnShiftStands.Remove(holder);
            }
            storedOwnerApparel.Clear();
            issuedUniform.Clear();
            storedForcedApparel.Clear();
            borrower = null;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref storedOwnerApparel, "storedOwnerApparel", LookMode.Reference);
            Scribe_Collections.Look(ref issuedUniform, "issuedUniform", LookMode.Reference);
            Scribe_Collections.Look(ref storedForcedApparel, "storedForcedApparel", LookMode.Reference);
            Scribe_References.Look(ref borrower, "borrower");
            Scribe_Collections.Look(ref workTypeOverrides, "workTypeOverrides", LookMode.Def);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Read-only: saves from before the set redesign carried a
                // single def under this name. Folded into the list below.
                Scribe_Defs.Look(ref workTypeOverrideLegacy, "workTypeOverride");
            }
            Scribe_Values.Look(ref excluded, "excluded", defaultValue: false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Anything burnt, stolen or removed by hand while we were not
                // looking comes back null. Dropping it here is the whole of
                // the orphan story on this side: the ledger simply forgets,
                // and vanilla's own eviction handles the leftovers in the
                // container.
                storedOwnerApparel = storedOwnerApparel ?? new List<Apparel>();
                issuedUniform = issuedUniform ?? new List<Apparel>();
                storedForcedApparel = storedForcedApparel ?? new List<Apparel>();
                storedOwnerApparel.RemoveAll(a => a == null);
                issuedUniform.RemoveAll(a => a == null);
                storedForcedApparel.RemoveAll(a => a == null);
                workTypeOverrides = workTypeOverrides ?? new List<WorkTypeDef>();
                workTypeOverrides.RemoveAll(w => w == null);
                if (workTypeOverrides.Count == 0 && workTypeOverrideLegacy != null)
                {
                    workTypeOverrides.Add(workTypeOverrideLegacy);
                }
                workTypeOverrideLegacy = null;
            }
        }

        /// <summary>
        /// Records a completed dressing: <paramref name="stored"/> went into
        /// the stand, <paramref name="issued"/> came out onto the pawn.
        /// </summary>
        public void NotifyDressed(Pawn pawn, List<Apparel> stored, List<Apparel> issued, List<Apparel> storedForced)
        {
            storedOwnerApparel.Clear();
            storedOwnerApparel.AddRange(stored);
            issuedUniform.Clear();
            issuedUniform.AddRange(issued);
            storedForcedApparel.Clear();
            if (storedForced != null)
            {
                storedForcedApparel.AddRange(storedForced);
            }
            if (issuedUniform.Count > 0)
            {
                borrower = pawn;
                OnShiftStands[pawn] = this;
            }
            else
            {
                // Nothing was actually issued (empty stand, nothing wearable).
                // Do not claim the stand for a swap that did not happen.
                borrower = null;
            }
        }

        public void NotifyUndressed(Pawn pawn)
        {
            storedOwnerApparel.Clear();
            issuedUniform.Clear();
            storedForcedApparel.Clear();
            borrower = null;
            OnShiftStands.Remove(pawn);
            // The stand just returned to the pool. Someone may have started
            // working bare in this room while every stand was out — catch
            // them up now rather than at their next job.
            Patch_JobInterception.Notify_StandFreed(this, pawn);
        }

        /// <summary>
        /// Whether this garment was force-worn at check-in, and should have the
        /// flag restored when it comes back on. Consult BEFORE
        /// <see cref="NotifyUndressed"/> clears the ledger.
        /// </summary>
        public bool WasForcedWhenStored(Apparel apparel)
        {
            return storedForcedApparel.Contains(apparel);
        }

        public List<Apparel> StoredOwnerApparelForReading => storedOwnerApparel;

        public List<Apparel> IssuedUniformForReading => issuedUniform;

        /// <summary>
        /// Clears the ledger without a return trip — the borrower is gone
        /// (dead, traded, kidnapped, off the map). Their clothes stay in the
        /// stand as ordinary contents, and the next claimant's first swap lets
        /// vanilla's <c>TryDropThingsToMakeRoomForThingOfDef</c> evict whatever
        /// conflicts. Frees the stand back into the pool.
        /// </summary>
        public void AbandonLedger(Pawn formerBorrower)
        {
            // On death and trade this is a no-op that costs nothing. On
            // banishment it is the whole point: that pawn is alive, spawned,
            // and would otherwise keep the force-worn uniform forever with the
            // ledger — and so every route out of it — already gone.
            UnforceIssued(borrower ?? formerBorrower);
            if (formerBorrower != null)
            {
                OnShiftStands.Remove(formerBorrower);
            }
            storedOwnerApparel.Clear();
            issuedUniform.Clear();
            storedForcedApparel.Clear();
            borrower = null;
            // Freed by abandonment rather than a return trip, but freed all
            // the same. Excluding the former borrower matters here: an owner
            // unassigned mid-shift is still WEARING the untracked uniform and
            // must not be the one interrupted to claim it again.
            Patch_JobInterception.Notify_StandFreed(this, formerBorrower);
        }

        /// <summary>Every stand on every map that this pawn has out on loan.</summary>
        public static IEnumerable<CompShiftStand> StandsBorrowedBy(Pawn pawn)
        {
            ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
            if (standDef == null || pawn == null)
            {
                yield break;
            }
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                List<Thing> stands = maps[i].listerThings.ThingsOfDef(standDef);
                for (int j = 0; j < stands.Count; j++)
                {
                    CompShiftStand comp = stands[j].TryGetComp<CompShiftStand>();
                    if (comp != null && comp.borrower == pawn)
                    {
                        yield return comp;
                    }
                }
            }
        }

        public override string CompInspectStringExtra()
        {
            if (excluded)
            {
                return "ShiftChange.InspectExcluded".Translate();
            }
            if (WorkTypes.Count == 0)
            {
                return null;
            }

            string line = "ShiftChange.InspectWork".Translate(WorkTypesLabel());

            // Say who owns or holds it here as well as on the gizmo: the base
            // comp reports ownership nowhere, and a stand that looks unassigned
            // is indistinguishable from one that simply never fires.
            Pawn owner = AssignedOwner;
            line += "\n" + (owner != null
                ? "ShiftChange.InspectOwner".Translate(owner.LabelShort)
                : (ShiftChangeMod.PoolingEnabled
                    ? "ShiftChange.InspectPool".Translate()
                    // Keep the UI honest: with pooling off, "shared" would be
                    // a lie the player debugs for an evening.
                    : "ShiftChange.InspectPoolDisabled".Translate()));

            if (OnShift && borrower != null)
            {
                line += "\n" + "ShiftChange.InspectOnShift".Translate(borrower.LabelShort);
            }
            else if (!HasWearable)
            {
                line += "\n" + "ShiftChange.InspectEmpty".Translate();
            }
            return line;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            string source = excluded
                ? "ShiftChange.Excluded".Translate().RawText
                : IsAutomatic
                    ? "ShiftChange.FromRoom".Translate().RawText
                    : "ShiftChange.Manual".Translate().RawText;
            yield return new Command_Action
            {
                defaultLabel = "ShiftChange.SetWorkTypeLabel".Translate(),
                defaultDesc = "ShiftChange.SetWorkTypeDesc".Translate(WorkTypesLabel(), source),
                // The pencil, not ForbidOff — a forbid glyph on a
                // configuration gizmo reads as "this stand is disabled".
                // Zero-art rule: icons come from vanilla's atlas only.
                icon = TexButton.Rename,
                action = () => Find.WindowStack.Add(new Dialog_SetStandWorkTypes(this)),
            };
        }
    }
}
