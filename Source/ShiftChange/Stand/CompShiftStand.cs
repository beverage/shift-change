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

        /// <summary>Sets <c>notifyMapRemoved</c> on every def that carries this comp, for
        /// <see cref="CompShiftStand.Notify_MapRemoved"/>: map removal never despawns a
        /// building, and <c>ThingWithComps.Notify_MyMapRemoved</c> tells comps only when the
        /// def asks. Here rather than in the patches, so it reaches whichever mod added the
        /// comp.</summary>
        public override void ResolveReferences(ThingDef parentDef)
        {
            base.ResolveReferences(parentDef);
            parentDef.notifyMapRemoved = true;
        }
    }

    /// <summary>
    /// Shift Change's half of a vanilla outfit stand: what it dresses for, and a ledger
    /// of the garments that went between it and its borrower, because
    /// <see cref="Building_OutfitStand"/> records nothing about whose clothes it holds.
    /// Partial: settings in .Triggers, what it serves in .Catchment, display in .Ui.
    /// </summary>
    public partial class CompShiftStand : ThingComp
    {
        /// <summary>The borrower's own clothes, currently parked in the stand.</summary>
        internal List<Apparel> storedOwnerApparel = new List<Apparel>();

        /// <summary>The stand's clothes, currently worn by the borrower.</summary>
        internal List<Apparel> issuedUniform = new List<Apparel>();

        /// <summary>The parked garments that were force-worn, since vanilla's
        /// <c>Pawn_ApparelTracker.Notify_ApparelRemoved</c> clears the flag.</summary>
        internal List<Apparel> storedForcedApparel = new List<Apparel>();

        /// <summary>Who wears this stand's uniform now. The return trip follows this,
        /// not the assigned owner, because a pool stand lends to anyone.</summary>
        internal Pawn borrower;

        /// <summary>Stands with a uniform out, keyed by borrower, so the return trip needs
        /// no map sweep. Not saved; <see cref="PostSpawnSetup"/> refills it.</summary>
        internal static readonly Dictionary<Pawn, CompShiftStand> OnShiftStands =
            new Dictionary<Pawn, CompShiftStand>();

        public Building_OutfitStand Stand => parent as Building_OutfitStand;

        /// <summary>The ledger is open: a uniform is out, or a pawn's own clothes are parked.
        /// Either half counts: a deposit-only stand issues nothing and still owes the parked
        /// garments back.</summary>
        public bool OnShift => issuedUniform.Count > 0 || storedOwnerApparel.Count > 0;

        public Pawn Borrower => borrower;

        /// <summary>Whether the pawn still belongs to the colony, as a member, guest or
        /// prisoner, by vanilla's own test (<c>CompAssignableToPawn.PlayerCanSeeAssignments</c>,
        /// <c>PawnBanishUtility.Banish</c>). Faction, never spawnedness: a banished colonist
        /// can stay spawned and leaves the faction, while a gravship flight despawns the
        /// borrower without touching it, and a landing sets the stand down before its pawns,
        /// so at <see cref="PostSpawnSetup"/> a live borrower may still be despawned.</summary>
        internal static bool StillOurs(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }
            // Not Faction.OfPlayer, which logs an error when it cannot resolve, nor
            // OfPlayerSilentFail, which throws when Find.World is null; this does neither.
            Faction player = Find.World?.factionManager?.OfPlayer;
            if (player == null)
            {
                // Cannot tell, so keep the ledger.
                return true;
            }
            return pawn.Faction == player || pawn.HostFaction == player;
        }

        /// <summary>Whether the reaper would have released this pawn, had the stand been on a
        /// map to find when they left: dead, gone from the colony, or kidnapped. Kidnapping
        /// runs <c>UnclaimAll</c> but keeps the player faction, so <see cref="StillOurs"/>
        /// alone passes a kidnapped colonist.</summary>
        internal static bool Departed(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !StillOurs(pawn))
            {
                return true;
            }
            // KidnapUtility.IsKidnapped reads Find.FactionManager, which throws without a world.
            return Find.World?.factionManager != null && pawn.IsKidnapped();
        }

        /// <summary>The pawns this stand is reserved for, empty for a pool stand. Never null:
        /// the assignable comp's own list, or the shared <see cref="NoOwners"/> on a stand
        /// without one, so callers only read it.</summary>
        public List<Pawn> AssignedOwners
        {
            get
            {
                // Ours by exact type first: Outfit Stands Plus adds a second
                // CompAssignableToPawn to this def, and comp order follows patch
                // order. The fallback serves a stand with only a foreign one.
                CompAssignableToPawn comp = parent.TryGetComp<CompAssignableToPawn_ShiftStand>()
                    ?? parent.TryGetComp<CompAssignableToPawn>();
                return comp?.AssignedPawnsForReading ?? NoOwners;
            }
        }

        internal static readonly List<Pawn> NoOwners = new List<Pawn>();

        public bool IsAssignedTo(Pawn pawn) => AssignedOwners.Contains(pawn);

        public bool IsPool => AssignedOwners.Count == 0;

        public bool CanBeClaimedBy(Pawn pawn)
        {
            List<Pawn> owners = AssignedOwners;
            if (owners.Count > 0)
            {
                return owners.Contains(pawn);
            }
            return ShiftChangeMod.PoolingEnabled;
        }

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

        /// <summary>Keeps vanilla's "Allow removing items" flag off while the stand is in
        /// service; called on spawn and on every entry into service. The flag is what
        /// <c>IApparelSource.ApparelSourceEnabled</c> returns, and
        /// <c>JobGiver_OptimizeApparel</c> is the only reader of that property, so this
        /// alone keeps the optimizer out of the stand. Out of service, the flag is the
        /// player's.</summary>
        internal void EnforceRemovalFlag()
        {
            if (!excluded)
            {
                Patch_AllowRemovingToggle.EnforceOffInService(Stand);
            }
        }

        /// <summary>Called by SessionGuard when the loaded game changes.</summary>
        internal static void ResetSessionState()
        {
            OnShiftStands.Clear();
            CatchmentStands.Clear();
        }

        public static CompShiftStand OnShiftStandFor(Pawn pawn)
        {
            SessionGuard.Ensure();
            CompShiftStand comp;
            if (!OnShiftStands.TryGetValue(pawn, out comp))
            {
                return null;
            }
            // A stand destroyed while its uniform was out leaves a stale entry.
            if (comp == null || comp.parent == null || comp.parent.Destroyed
                || !comp.OnShift || comp.borrower != pawn)
            {
                OnShiftStands.Remove(pawn);
                return null;
            }
            return comp;
        }

        /// <summary>Applies the new-stand setting. <c>ThingMaker.MakeThing</c> is the only caller,
        /// a finished frame included; a load rebuilds the comp without it and reads
        /// <see cref="excluded"/> back from the save, so a stand that already exists never
        /// changes when the setting does.</summary>
        public override void PostPostMake()
        {
            base.PostPostMake();
            if (!ShiftChangeMod.NewStandsInService)
            {
                // The field alone: every other setting is still at its default here, and the
                // stand is not spawned, so SetExcluded's catchment reset has nothing to do.
                excluded = true;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            SessionGuard.Ensure();
            if (serves != CatchmentKind.Room)
            {
                CatchmentStands.Add(this);
            }

            // Unconditional, not load-only: a stand set down from a box needs it too.
            EnforceRemovalFlag();

            if (OnShift && Departed(borrower))
            {
                // Nobody left to honour the ledger: no borrower (older saves), one who
                // died or was kidnapped while the stand was boxed, when no map's lister holds
                // it for StandsBorrowedBy to find, or one who left the colony by a route no
                // patch catches. Not AbandonLedger: its map sweep is unsafe inside
                // Map.FinalizeLoading.
                ReleaseBorrower();
            }
            else if (borrower != null && OnShift)
            {
                CompShiftStand incumbent;
                if (OnShiftStands.TryGetValue(borrower, out incumbent)
                    && incumbent != null && incumbent != this && incumbent.OnShift
                    && incumbent.borrower == borrower)
                {
                    // Another stand holds this borrower's checkout, so this one is
                    // a stale copy, from a box set down after the shift moved on. Taking
                    // the entry would strand the checkout the other stand still holds.
                    storedOwnerApparel.Clear();
                    issuedUniform.Clear();
                    storedForcedApparel.Clear();
                    borrower = null;
                }
                else
                {
                    OnShiftStands[borrower] = this;
                    if (!respawningAfterLoad)
                    {
                        // Set back down, not loaded. From a box, PostDeSpawn took the
                        // flags off; after a gravship landing nothing did, so this only
                        // re-sets one cleared by hand, which changes nothing while the
                        // shift lasts: the optimizer, the one reader that acts on the
                        // flag, is paused for anyone on shift, and every way out of the
                        // shift clears it. Not on a load, where the flags are the pawn's
                        // own saved state and the player may have cleared one.
                        ForceIssued(borrower);
                    }
                }
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            // Before the early returns, whatever the mode; PostSpawnSetup puts it back.
            CatchmentStands.Remove(this);
            if (mode == DestroyMode.WillReplace)
            {
                // A gravship flight: Odyssey's gravship despawns everything aboard in this
                // mode, and Building_OutfitStand.DeSpawn keeps its contents, so the ledger
                // stays, as vanilla's CompAssignableToPawn keeps its owners. Released here,
                // the borrower's parked clothes would stay in the stand unrecorded and be
                // issued as its kit, and the uniform could never go back. PostSpawnSetup
                // re-registers it on landing, and PostSwapMap decides about a borrower who
                // did not come along.
                return;
            }
            if (mode == DestroyMode.Vanish)
            {
                // Minified: MinifyUtility.MakeMinified despawns with Vanish, which
                // keeps the contents too, so the ledger rides along in the box, for the
                // same reason. Only
                // the forced flags come off, since the box may never be set down. If the
                // optimizer then takes the uniform off in transit, PlanUndress returns
                // only what the pawn still wears.
                UnforceIssued(borrower);
                return;
            }
            // Deconstructed or destroyed: the contents are dropped, so release.
            ReleaseBorrower();
        }

        /// <summary>
        /// After a gravship landing: rebinds the served area, and settles a borrower who
        /// did not come along. One on no map, left on a map the takeoff abandoned, can never
        /// come back for their clothes, so the ledger is released. One on a map a grav
        /// anchor kept stays on shift, uniform forced and wardrobe paused, until they share a
        /// map again (docs/DESIGN.md, "Ownership and the ledger"). The test is vanilla's
        /// own, from <c>CompAssignableToPawn.PostSwapMap</c> on the same building.
        /// </summary>
        public override void PostSwapMap()
        {
            base.PostSwapMap();
            // Before the early return: an idle stand rebinds too.
            RebindAfterLanding();
            if (borrower == null || !OnShift)
            {
                return;
            }
            if (borrower.DestroyedOrNull() || !borrower.SpawnedOrAnyParentSpawned)
            {
                ReleaseBorrower();
                return;
            }
            // PostSpawnSetup registered it already, since a landing spawns everything
            // before it calls this; repeated so this method leaves the registry correct by
            // itself.
            OnShiftStands[borrower] = this;
        }

        /// <summary>
        /// The stand's map is being removed with the stand on it: left behind by a takeoff
        /// from a map with no grav anchor, or any map abandoned or closed. Removal never
        /// despawns a building, so <see cref="PostDeSpawn"/> never runs and this is the
        /// stand's only notice, asked for by
        /// <see cref="CompProperties_ShiftStand.ResolveReferences"/>. The stand and the
        /// parked clothes go with the map, so the ledger is released and the borrower keeps
        /// the uniform as ordinary clothing. Not <see cref="AbandonLedger"/>: its catch-up
        /// sweeps the map being removed. A stand boxed on the map hears this too.
        /// </summary>
        public override void Notify_MapRemoved()
        {
            base.Notify_MapRemoved();
            ReleaseBorrower();
        }

        /// <summary>Takes the forced flag off the issued garments the holder still wears.
        /// Every path that drops the ledger while the uniform may still be worn calls it,
        /// since a forced uniform with no ledger is stuck
        /// (<see cref="ReleaseBorrower"/>).</summary>
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

        /// <summary>The reverse of <see cref="UnforceIssued"/>, again only for garments the
        /// holder still wears: the optimizer may have taken one off while the stand was
        /// boxed.</summary>
        internal void ForceIssued(Pawn holder)
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
                    holder.outfits?.forcedHandler?.SetForced(apparel, forced: true);
                }
            }
        }

        /// <summary>Frees the stand, leaving the uniform on its wearer as ordinary clothing.
        /// The forced flags come off first: every way out of a uniform finds the pawn
        /// through <see cref="OnShiftStands"/>, and <c>JobGiver_OptimizeApparel</c> never
        /// removes forced apparel, so a forced uniform with no ledger is stuck.</summary>
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
            Scribe_Values.Look(ref recreationOverride, "recreation", defaultValue: false);
            Scribe_Values.Look(ref restOverride, "rest", defaultValue: false);
            Scribe_Values.Look(ref depositOnly, "depositOnly", defaultValue: false);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Load only: older saves hold a single work type under this key.
                Scribe_Defs.Look(ref workTypeOverrideLegacy, "workTypeOverride");
            }
            Scribe_Values.Look(ref excluded, "excluded", defaultValue: false);
            Scribe_Values.Look(ref fullChange, "fullChange", defaultValue: false);
            // Defaults true, so a save without the node loads protected. Scribe_Values
            // reads a missing node as the default and writes none for a default value.
            Scribe_Values.Look(ref withholdFromTrade, "withholdFromTrade", defaultValue: true);
            // Prefixed: comps scribe flat into the stand's node, beside other mods' comps.
            Scribe_Values.Look(ref serves, "shiftChangeServes", CatchmentKind.Room);
            Scribe_Values.Look(ref serveRadius, "shiftChangeServeRadius", DefaultServeRadius);
            // By reference. An area the save will not write in full (deleted, or its
            // map gone) is dropped first, or the reference fails on load; the label
            // stays. Not tested against the stand's map: a boxed stand is on none.
            if (Scribe.mode == LoadSaveMode.Saving && !AreaSavedInFull(serveArea))
            {
                serveArea = null;
            }
            // No node without a binding, so a stand that serves no area writes nothing here.
            if (Scribe.mode != LoadSaveMode.Saving || serveArea != null)
            {
                Scribe_References.Look(ref serveArea, "shiftChangeServeArea");
            }
            Scribe_Values.Look(ref serveAreaLabel, "shiftChangeServeAreaLabel");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // A garment destroyed while the ledger still named it, or a work type from a
                // removed mod, loads back as null; the ledger and the settings forget it.
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

                serveRadius = Mathf.Clamp(serveRadius, MinServeRadius, MaxServeRadius);
                // Only a stand with something ticked leaves room mode, as in the setters.
                // After the null-strip above: ticks from a removed mod no longer count.
                if (serves != CatchmentKind.Room && (excluded || !HasCustomSet))
                {
                    serves = CatchmentKind.Room;
                }
            }
        }

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
            // OnShift, not issuedUniform: a deposit-only trip issues nothing and must still
            // claim the stand, or nothing hands the parked clothes back.
            if (OnShift)
            {
                borrower = pawn;
                OnShiftStands[pawn] = this;
            }
            else
            {
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
            // Not Notify_StandFreed: in a toil finish action the pawn still holds the
            // stand's reservation. JobDriver_SwapAtStand announces after its release.
        }

        /// <summary>Whether this garment was force-worn when parked. Read it before
        /// <see cref="NotifyUndressed"/> clears the ledger.</summary>
        public bool WasForcedWhenStored(Apparel apparel)
        {
            return storedForcedApparel.Contains(apparel);
        }

        public List<Apparel> StoredOwnerApparelForReading => storedOwnerApparel;

        public List<Apparel> IssuedUniformForReading => issuedUniform;

        /// <summary>Clears the ledger without a return trip and frees the stand: for a
        /// borrower who is gone (dead, traded, kidnapped, banished, off the map), and
        /// for one the swap driver could not change back, who keeps the uniform. Their
        /// clothes stay in the stand, and the next claimant's swap evicts whatever conflicts,
        /// through <c>Building_OutfitStand.TryDropThingsToMakeRoomForThingOfDef</c>.</summary>
        /// <param name="announce">Raise the catch-up now; the swap driver defers it until
        /// the stand's reservation is released.</param>
        public void AbandonLedger(Pawn formerBorrower, bool announce = true)
        {
            // Nothing to do for a dead or traded borrower, whose flags left with them; a
            // banished one is alive and still in the uniform.
            UnforceIssued(borrower ?? formerBorrower);
            if (formerBorrower != null)
            {
                OnShiftStands.Remove(formerBorrower);
            }
            storedOwnerApparel.Clear();
            issuedUniform.Clear();
            storedForcedApparel.Clear();
            borrower = null;
            // Excludes the former borrower, who still wears the uniform.
            if (announce)
            {
                Patch_JobInterception.Notify_StandFreed(this, formerBorrower);
            }
        }

        /// <summary>Every stand on every map that this pawn has out on loan.</summary>
        public static IEnumerable<CompShiftStand> StandsBorrowedBy(Pawn pawn)
        {
            if (pawn == null)
            {
                yield break;
            }
            List<ThingDef> standDefs = Patch_JobInterception.StandDefs;
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                for (int d = 0; d < standDefs.Count; d++)
                {
                    List<Thing> stands = maps[i].listerThings.ThingsOfDef(standDefs[d]);
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
        }
    }
}
