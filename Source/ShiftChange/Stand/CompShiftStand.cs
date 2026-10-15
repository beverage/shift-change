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
    ///
    /// The class is <c>partial</c>, across four files in Stand/. This one
    /// holds the ledger, the stand's lifecycle, its ownership reads and save
    /// and load. The other three hold what it dresses for and its settings
    /// (.Triggers), where it serves (.Catchment) and what the player sees
    /// (.Ui); docs/DEVELOPMENT.md maps them.
    /// </summary>
    public partial class CompShiftStand : ThingComp
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
        /// Stands that currently have a uniform out on a pawn, so the return
        /// trip can be found without sweeping the map. Keyed by BORROWER and
        /// rebuilt on load, since <see cref="PostSpawnSetup"/> runs for every
        /// comp on both paths.
        /// </summary>
        internal static readonly Dictionary<Pawn, CompShiftStand> OnShiftStands =
            new Dictionary<Pawn, CompShiftStand>();

        public Building_OutfitStand Stand => parent as Building_OutfitStand;

        /// <summary>
        /// This stand has something out on a pawn, so the return trip has
        /// work to do.
        ///
        /// <para>EITHER half of the ledger counts, not just the issued one: a
        /// deposit-only stand issues nothing, and keyed on
        /// <see cref="issuedUniform"/> alone it would never be claimed, so
        /// nothing would hand its contents back. Why the widening is safe for
        /// every older save: docs/DESIGN.md, "The sleep branch".</para>
        /// </summary>
        public bool OnShift => issuedUniform.Count > 0 || storedOwnerApparel.Count > 0;

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
        /// The pawns this stand is reserved for: never null, and empty when it
        /// is a pool stand, in which case it is the shared
        /// <see cref="NoOwners"/>, so callers only read it. Distinct from
        /// <see cref="Borrower"/>: assignment is the player's standing intent,
        /// borrowing is who is wearing it now.
        /// </summary>
        public List<Pawn> AssignedOwners
        {
            get
            {
                // OUR comp by exact type first, never "the first assignable".
                // Outfit Stands Plus adds a second CompAssignableToPawn
                // subclass to this same def, and comp order follows patch
                // order — reading whichever came first meant our Set owner
                // dialog could write a list this property never read. The
                // base-typed fallback is deliberate: a stand carrying only a
                // foreign assignable (an Outfit Stands Plus stand, say)
                // still gets owner semantics from it.
                CompAssignableToPawn comp = parent.TryGetComp<CompAssignableToPawn_ShiftStand>()
                    ?? parent.TryGetComp<CompAssignableToPawn>();
                return comp?.AssignedPawnsForReading ?? NoOwners;
            }
        }

        internal static readonly List<Pawn> NoOwners = new List<Pawn>();

        public bool IsAssignedTo(Pawn pawn) => AssignedOwners.Contains(pawn);

        public bool IsPool => AssignedOwners.Count == 0;

        /// <summary>
        /// Unassigned means pool: any pawn may take it — unless the player
        /// turned pooling off in mod settings, in which case an unassigned
        /// stand is inert and only explicit assignment participates. One or
        /// more owners means the stand serves that set and nobody outside it,
        /// which is what keeps a surgeon's tailored kit off a passing hauler
        /// and a gown off the wrong colonist.
        ///
        /// Note the consequence at the edges: lose every owner — death,
        /// banishment, the reaper — and the list empties, so the stand falls
        /// back to being a pool stand rather than staying inert.
        /// </summary>
        public bool CanBeClaimedBy(Pawn pawn)
        {
            List<Pawn> owners = AssignedOwners;
            if (owners.Count > 0)
            {
                return owners.Contains(pawn);
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

        /// <summary>
        /// Holds the one invariant the mod's contents protection rests on:
        /// while a stand is in service, vanilla's "Allow removing items" flag
        /// is off.
        ///
        /// <para>Called on spawn and from EVERY entry into service, which is
        /// what makes this an invariant rather than a load-time migration. The
        /// documented way to reach the flag is to set a stand to "Not used for
        /// shift changes", flip it, and put the stand back — and that last step
        /// always lands in one of the three callers, so the window that round
        /// trip used to leave open is closed.</para>
        ///
        /// <para>Hold this and <c>IApparelSource.ApparelSourceEnabled</c> is
        /// false by construction, because it IS this flag
        /// (<c>Building_OutfitStand.cs:104</c>) and
        /// <c>JobGiver_OptimizeApparel:149</c> is its only reader in the whole
        /// engine. That is why no Harmony patch on that property is needed:
        /// the protection falls out of the invariant. Do not add one without
        /// first breaking this.</para>
        ///
        /// <para>Leaving service is deliberately untouched. An excluded stand
        /// is ordinary vanilla furniture and the player owns its flag.</para>
        /// </summary>
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
            if (serves != CatchmentKind.Room)
            {
                CatchmentStands.Add(this);
            }

            // Unconditional, not load-only: this also covers a stand
            // reinstalled from a minified box. A fresh build defaults to the
            // flag off, so the ordinary case is a no-op.
            EnforceRemovalFlag();

            // Rebuild from the BORROWER, not the assigned owner. Inferring it
            // from ownership was only ever right by coincidence — reassign a
            // stand while its uniform is out and the return trip would have
            // pointed at the wrong pawn.
            if (OnShift && (borrower == null || borrower.Dead || !StillOurs(borrower)))
            {
                // Nobody left to honour the ledger. Three ways in: a save from
                // before pooling (borrower null), a borrower who DIED while
                // this stand sat in a box, or a borrower who left the
                // colony without reaching Pawn_Ownership.UnclaimAll —
                // banishment, which Patch_BanishStands now catches eagerly.
                // This branch is the repair path for saves that predate that
                // patch, and the backstop for any other faction-loss route we
                // do not hook.
                //
                // The death clause is load-bearing since the ledger started
                // riding along inside a minified box: both reapers find their
                // stands by sweeping listerThings
                // (Patch_UnclaimStands.ReapStandsFor, StandsBorrowedBy), and a
                // boxed stand is on no map's lister, so a borrower who dies
                // mid-haul reaches us only here. Dead, not spawnedness — see
                // StillOurs for why that axis is wrong on this method.
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
                    if (!respawningAfterLoad)
                    {
                        // Set back down, not loaded. PostDeSpawn took the
                        // forced flags off so a box that never came back could
                        // not pin anyone; the box DID come back, so put them
                        // on again and the shift resumes exactly as it stood.
                        //
                        // Load-gated deliberately: on a load the flags are the
                        // pawn's own scribed state, and re-forcing there would
                        // overrule a player who cleared one by hand mid-shift.
                        ForceIssued(borrower);
                    }
                }
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            // Off the map whatever the mode; PostSpawnSetup puts it back.
            CatchmentStands.Remove(this);
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
            if (mode == DestroyMode.Vanish)
            {
                // Minified into a box — uninstalled, or the middle of a
                // Reinstall. The stand is coming back, and critically its
                // CONTENTS come with it: Vanish keeps them exactly as
                // WillReplace does (Building_OutfitStand.cs:390-397), and
                // minifying is a Vanish (MinifyUtility.MakeMinified:15).
                //
                // So the ledger rides along too. The alternative shipped
                // once and was wrong in play: releasing here left the
                // borrower's civvies inside the box with nothing saying whose
                // they were, so the stand landed reading them as its own kit
                // and the pawn could never hand the uniform back. Ejecting
                // them to the floor first was no better — same permanent
                // swap, clothes underfoot instead of in a crate. Contents
                // kept means ledger kept; that is the same rule the
                // WillReplace branch above already follows, and this is
                // simply the other mode that obeys it.
                //
                // The one thing that must NOT ride along is the FORCED flag.
                // A box can be sold, burnt or left in a stockpile forever,
                // and a pawn pinned into a force-worn uniform has no route
                // out (see ReleaseBorrower). Unforced, the worst case is that
                // the optimizer swaps the uniform off while the stand is in
                // transit, which PlanUndress already tolerates — it returns
                // only what the pawn still wears. PostSpawnSetup puts the
                // flags back on landing.
                UnforceIssued(borrower);
                return;
            }
            // Deconstructed or burnt down. These DROP the container
            // (Building_OutfitStand.cs:392), so the civvies are already on the
            // floor and the stand is genuinely gone — the ledger cannot be
            // honoured. Dropping the registry entry alone was not enough: it
            // left the uniform FORCE-WORN, and forced is what makes the
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
            // FIRST, ahead of the early return below: an idle stand rebinds
            // too, and an idle stand is the common case.
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
        /// The mirror of <see cref="UnforceIssued"/>, for the one case that
        /// takes the flags off without ending the shift: a stand minified into
        /// a box and set back down again. Only garments the holder is STILL
        /// wearing are re-forced — the optimizer is free to have swapped one
        /// away while the stand was in transit, and that is precisely the
        /// escape hatch the unforcing exists to leave open.
        /// </summary>
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
            Scribe_Values.Look(ref recreationOverride, "recreation", defaultValue: false);
            Scribe_Values.Look(ref restOverride, "rest", defaultValue: false);
            Scribe_Values.Look(ref depositOnly, "depositOnly", defaultValue: false);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Read-only: saves from before the set redesign carried a
                // single def under this name. Folded into the list below.
                Scribe_Defs.Look(ref workTypeOverrideLegacy, "workTypeOverride");
            }
            Scribe_Values.Look(ref excluded, "excluded", defaultValue: false);
            Scribe_Values.Look(ref fullChange, "fullChange", defaultValue: false);
            // defaultValue: true is what carries the protection into saves
            // that predate it. ScribeExtractor.ValueFromNode hands back the
            // default when the node is absent (Scribe_Values.cs:88), and the
            // saver omits the node whenever the value MATCHES the default
            // (:70-78) — so the only thing ever written here is a deliberate
            // opt-out, and a save round trip never manufactures one.
            Scribe_Values.Look(ref withholdFromTrade, "withholdFromTrade", defaultValue: true);
            // Prefixed, unlike the older keys above: comps scribe flat into the
            // stand's node, beside any other mod's comp on the same building.
            Scribe_Values.Look(ref serves, "shiftChangeServes", CatchmentKind.Room);
            Scribe_Values.Look(ref serveRadius, "shiftChangeServeRadius", DefaultServeRadius);
            // The area by reference, as a pawn's allowed area is saved. One that
            // is gone, deleted or with its map gone from the game, is dropped
            // first: the saver warns of a reference to anything not saved in
            // full, and the loader of one it cannot resolve. The label stays and
            // keeps naming it. Not dropped for being on another map than the
            // stand: a stand boxed in a caravan is on none, and dropped there it
            // came home serving nothing, with nothing to bind it again but a
            // gravship landing. Nothing is written without a binding, so a
            // stand that never served an area saves exactly as before.
            if (Scribe.mode == LoadSaveMode.Saving && !AreaSavedInFull(serveArea))
            {
                serveArea = null;
            }
            if (Scribe.mode != LoadSaveMode.Saving || serveArea != null)
            {
                Scribe_References.Look(ref serveArea, "shiftChangeServeArea");
            }
            Scribe_Values.Look(ref serveAreaLabel, "shiftChangeServeAreaLabel");

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

                serveRadius = Mathf.Clamp(serveRadius, MinServeRadius, MaxServeRadius);
                // The same canonical state the setters keep: another catchment
                // only with something ticked. Reached by a hand-edited save,
                // or by one whose ticked work types all came from a mod since
                // removed: their nulls were stripped just above.
                if (serves != CatchmentKind.Room && (excluded || !HasCustomSet))
                {
                    serves = CatchmentKind.Room;
                }
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
            // OnShift, not issuedUniform: a deposit-only trip issues nothing
            // and still has to claim the stand, or nothing ever hands the
            // stored garments back. Everything else is unchanged — a trip that
            // moved nothing in EITHER direction leaves both lists empty and
            // still does not claim.
            if (OnShift)
            {
                borrower = pawn;
                OnShiftStands[pawn] = this;
            }
            else
            {
                // Nothing moved at all (empty stand, nothing wearable, nothing
                // the filter would take). Do not claim the stand for a swap
                // that did not happen.
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
            // NOT Notify_StandFreed: this runs from a toil finish action,
            // while the pawn who just undressed still holds the stand's
            // reservation. JobDriver_SwapAtStand announces from a global finish
            // action instead, after the release (docs/DESIGN.md, "The mid-job
            // catch-up").
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
        /// <param name="announce">
        /// Raise the catch-up for whoever is working bare in this room. The
        /// reaper paths (death, trade, kidnap, banishment) want this: the
        /// borrower is gone and their claims went with them. The job driver
        /// passes false and announces later itself, once the stand's
        /// reservation is released (docs/DESIGN.md, "The mid-job catch-up").
        /// </param>
        public void AbandonLedger(Pawn formerBorrower, bool announce = true)
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
            if (announce)
            {
                Patch_JobInterception.Notify_StandFreed(this, formerBorrower);
            }
        }

        /// <summary>Every stand on every map that this pawn has out on loan.
        /// Sweeps the whole patched family, not just the vanilla def.</summary>
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
