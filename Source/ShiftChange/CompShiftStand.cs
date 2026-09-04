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
        /// Player override for the RECREATION trigger. MUTUALLY
        /// EXCLUSIVE with <see cref="workTypeOverrides"/> (principal,
        /// 2026-08-16: one stand, one outfit, one purpose) — the toggles
        /// enforce it, so true implies the work set is empty. True is itself
        /// what marks that works-empty custom set as custom rather than
        /// automatic — "recreation only" needs no extra mode flag — and in
        /// automatic mode the room's role answers instead
        /// (<see cref="RoomWorkTypes.RecreationForRole"/>). Absent from old
        /// saves and defaults false, so every pre-branch state loads
        /// unchanged.
        /// </summary>
        internal bool recreationOverride;

        /// <summary>
        /// Player override for the SLEEP trigger, the third class beside work
        /// and recreation. Exactly the same shape as
        /// <see cref="recreationOverride"/> and mutually exclusive with both
        /// of the others — one stand, one outfit, one purpose — so true
        /// implies an empty work set and no recreation. Absent from old saves
        /// and defaults false, so every pre-sleep state loads unchanged.
        /// </summary>
        internal bool restOverride;

        /// <summary>
        /// This stand hands nothing OUT: the pawn parks the garments its
        /// storage filter accepts and keeps everything else on. The
        /// power-armour-before-bed case (principal, 2026-09-02).
        ///
        /// <para><b>Only meaningful on a sleep stand</b>, and
        /// <see cref="DepositOnly"/> enforces that rather than trusting the
        /// dialog to hide the row. Undressing into a rack is a coherent
        /// action but "not one any pawn should reach by deciding to go do some
        /// hauling" (SwapPlan.cs) — the sleep trigger is what makes it safe,
        /// so a stand switched back to work must not carry the flag with
        /// it.</para>
        ///
        /// <para>Stored raw and gated on read, not cleared on a mode switch,
        /// so a player who flips a stand to work and back finds their setting
        /// where they left it.</para>
        /// </summary>
        internal bool depositOnly;

        /// <summary>
        /// Opt-out. A decorative or storage stand standing in a roled room would
        /// otherwise join that room's pool, which is a surprise nobody asked
        /// for.
        /// </summary>
        internal bool excluded;

        /// <summary>
        /// Swap the pawn's whole outfit rather than only what the stand's kit
        /// conflicts with. Default off: a full change costs the equip time of
        /// every garment in both directions, which is the right price for a
        /// sauna robe or a set of scrubs and the wrong one for a lab coat worn
        /// over ordinary clothes.
        ///
        /// <para>Per stand rather than a mod setting, because it is a property
        /// of what the rack holds — a robe rack wants it, a lab-coat rack does
        /// not.</para>
        ///
        /// <para>Read only when BUILDING a dress plan. The return trip is
        /// driven entirely by the ledger (<see cref="JobDriver_SwapAtStand.PlanUndress"/>
        /// walks <see cref="IssuedUniformForReading"/> and
        /// <see cref="StoredOwnerApparelForReading"/>), so flipping this while a
        /// uniform is out cannot strand anything: whatever went into the stand
        /// comes back regardless of what the flag says by then.</para>
        /// </summary>
        internal bool fullChange;

        /// <summary>
        /// Keep this stand's contents out of trade windows.
        ///
        /// <para><b>Vanilla offers them, and the removal flag is no defence.</b>
        /// <c>TradeUtility.AllLaunchableThingsForTrade</c> carries an explicit
        /// <c>Building_OutfitStand</c> branch that yields <c>HeldItems</c>
        /// (<c>TradeUtility.cs:123</c>) for orbital ships, and
        /// <c>Pawn_TraderTracker.ColonyThingsWillingToBuy</c> walks
        /// <c>AllColonistBuildingsOfType&lt;IHaulSource&gt;()</c> and yields
        /// everything they directly hold (<c>Pawn_TraderTracker.cs:123-134</c>)
        /// for visiting caravans. That second one enumerates by TYPE, so
        /// <c>HaulSourceEnabled</c> — the one thing <c>allowRemovingItems</c>
        /// actually gates — is never consulted, and
        /// <see cref="Patch_AllowRemovingToggle"/>'s enforcement buys nothing
        /// here. <c>TradeDeal.InSellablePosition</c> then whitelists
        /// <c>ParentHolder is Building_OutfitStand</c> so the unspawned held
        /// items sail through the position check (<c>TradeDeal.cs:85</c>). The
        /// result is a uniform in active rotation — and the owner's own clothes
        /// parked beside it — listed for sale to the next trader who walks in.</para>
        ///
        /// <para><b>Defaults ON, and saves that predate it adopt it.</b> A stand
        /// in service holds kit the colony is using, so the safe state is the
        /// default state; that is the same call as
        /// <see cref="EnforceRemovalFlag"/>, and for the same reason — the
        /// exposure is invisible from the inspect pane, so a player cannot
        /// audit it by eye and will not go looking for a switch they do not
        /// know they need.</para>
        ///
        /// <para>Read through <see cref="BlocksTrade"/>, never directly: that
        /// property folds in <see cref="excluded"/>, because a stand declared
        /// not-ours is untouched vanilla no matter what this flag says.</para>
        /// </summary>
        internal bool withholdFromTrade = true;

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
        /// <para>EITHER half of the ledger counts, not just the issued one. A
        /// deposit-only stand issues nothing at all — the whole point — and
        /// keying this on <see cref="issuedUniform"/> alone would leave it
        /// unclaimed: no borrower, no entry in <see cref="OnShiftStands"/>, no
        /// return trip, and a colonist's power armour locked in a rack that
        /// still advertised itself as free to the next pawn.</para>
        ///
        /// <para>Widening it is safe for every pre-existing state because the
        /// combination it newly admits — nothing issued, something stored —
        /// could not be recorded before: <c>DoTransfer</c> re-dresses the pawn
        /// and returns without calling <see cref="NotifyDressed"/> whenever a
        /// dress trip issues nothing. Deposit-only is the first path that
        /// reaches it deliberately.</para>
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
        /// The pawn this stand is reserved for, or null when it is a pool
        /// stand. Distinct from <see cref="Borrower"/>: assignment is the
        /// player's standing intent, borrowing is who is wearing it now.
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

        /// <summary>
        /// Owner names for the inspect pane, capped. Bulk assignment is the
        /// point of the owner list — "Assign all" on a colony of forty is two
        /// clicks — so the unbounded version of this line was not a corner
        /// case, it was the feature working as intended printing forty short
        /// names into a pane sized for three.
        /// </summary>
        internal static string OwnerNames(List<Pawn> owners)
        {
            const int shown = 3;
            if (owners.Count <= shown)
            {
                return owners.Select(p => p.LabelShort).ToCommaList();
            }
            string head = owners.Take(shown).Select(p => p.LabelShort).ToCommaList();
            return "ShiftChange.InspectOwnersOverflow".Translate(head, owners.Count - shown);
        }

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
                // A recreation-only or sleep-only custom set: the override
                // bool alone marks custom mode (see the fields), and its work
                // half is genuinely empty — falling through to the room's
                // defaults here would resurrect work types the player just
                // narrowed away.
                if (recreationOverride || restOverride)
                {
                    return NoWork;
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

        /// <summary>
        /// Whether this stand dresses for recreation — the joy-branch
        /// parallel of <see cref="HandlesWork"/>. Excluded wins; a
        /// custom set (either half non-empty) answers from its own bool;
        /// automatic asks the room's role, so a stand in a rec room lights up
        /// exactly the way a kitchen stand does for cooking.
        /// </summary>
        public bool HandlesRecreation()
        {
            if (excluded)
            {
                return false;
            }
            if (HasCustomSet)
            {
                return recreationOverride;
            }
            if (!parent.Spawned)
            {
                return false;
            }
            return RoomWorkTypes.RecreationForRole(parent.GetRoom()?.Role);
        }

        /// <summary>
        /// Whether this stand dresses for SLEEP — the third trigger, resolved
        /// exactly like <see cref="HandlesRecreation"/>: excluded wins, a
        /// custom set answers from its own bool, and automatic asks the room's
        /// role, so a stand in a bedroom lights up the way a kitchen stand
        /// does for cooking.
        /// </summary>
        public bool HandlesRest()
        {
            if (excluded)
            {
                return false;
            }
            if (HasCustomSet)
            {
                return restOverride;
            }
            if (!parent.Spawned)
            {
                return false;
            }
            return RoomWorkTypes.RestForRole(parent.GetRoom()?.Role);
        }

        /// <summary>
        /// The stand is in CUSTOM mode: the player has picked its triggers
        /// explicitly, so no half of it falls back to the room's defaults. Any
        /// one of the three trigger stores being non-empty says so, and the
        /// exclusivity rule means at most one ever is.
        /// </summary>
        internal bool HasCustomSet =>
            (workTypeOverrides != null && workTypeOverrides.Count > 0)
            || recreationOverride || restOverride;

        /// <summary>
        /// The player has EXPLICITLY put this stand on the recreation or sleep
        /// trigger, as opposed to the room's role having done it for them.
        ///
        /// <para>The dialog needs the distinction: it hides the work grid while
        /// a trigger is on, and hiding it on an AUTOMATIC stand left a bedroom
        /// stand with no reachable path to a work type at all — the grid was
        /// hidden, unticking the Sleeping row fell through to excluded, and
        /// clicking Automatic put the room's default straight back (adversarial
        /// review, 2026-09-03). An automatic stand's trigger is a suggestion
        /// from the room and must stay overridable in one click.</para>
        /// </summary>
        public bool IsTriggerOverridden => !excluded && (recreationOverride || restOverride);

        /// <summary>
        /// Deposit-only is a property of a SLEEP stand and of nothing else —
        /// see <see cref="depositOnly"/> for why the gate lives on the read
        /// rather than on the write.
        /// </summary>
        public bool DepositOnly => depositOnly && HandlesRest();

        public void SetDepositOnly(bool on)
        {
            depositOnly = on;
        }

        public bool IsExcluded => excluded;

        public bool FullChange => fullChange;

        public void SetFullChange(bool on)
        {
            fullChange = on;
        }

        public bool WithholdFromTrade => withholdFromTrade;

        public void SetWithholdFromTrade(bool on)
        {
            withholdFromTrade = on;
        }

        /// <summary>
        /// The question <see cref="Patch_WithholdFromTrade"/> asks. Keyed on the
        /// DECLARATION rather than on resolved work types, exactly like the
        /// removal-flag disable: a declared stand sitting in a roleless room is
        /// "ours, currently idle" and its uniform deserves the same cover,
        /// while an excluded stand is somebody's display piece and stays
        /// vanilla — flag or no flag.
        /// </summary>
        public bool BlocksTrade => !excluded && withholdFromTrade;

        // Both threads narrowed "automatic": recreation and sleep are explicit
        // overrides like a custom work set, while fullChange is NOT — it is a
        // property of the rack's contents, orthogonal to which trigger the
        // stand answers to, so an automatic stand with a full-change robe on it
        // is still automatic. Deposit-only is the same kind of thing as
        // fullChange and stays out of this test for the same reason.
        public bool IsAutomatic => !excluded && !HasCustomSet;

        public void SetAutomatic()
        {
            excluded = false;
            recreationOverride = false;
            restOverride = false;
            workTypeOverrides?.Clear();
            EnforceRemovalFlag();
        }

        public void SetExcluded()
        {
            excluded = true;
            recreationOverride = false;
            restOverride = false;
            workTypeOverrides?.Clear();
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

        /// <summary>
        /// Flips one work type in the effective set, entering custom mode from
        /// wherever the stand currently is: toggling while automatic seeds the
        /// custom set from the room's defaults first, toggling while excluded
        /// starts a fresh set, and emptying the custom set collapses back to
        /// excluded so the states stay canonical. Work and recreation are
        /// mutually exclusive (principal, 2026-08-16), so touching any work
        /// type also drops the recreation trigger — the dialog hides this
        /// list while recreation is on, so reaching here from a rec stand is
        /// a deliberate switch, not a surprise.
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
            recreationOverride = false;
            restOverride = false;
            if (!workTypeOverrides.Remove(work))
            {
                workTypeOverrides.Add(work);
            }
            if (workTypeOverrides.Count == 0)
            {
                SetExcluded();
            }
            EnforceRemovalFlag();
        }

        /// <summary>
        /// Flips the recreation trigger. Work types, recreation and sleep are
        /// MUTUALLY EXCLUSIVE on a stand (principal, 2026-08-16, extended to
        /// the third trigger 2026-09-02): the stand holds one outfit, and one
        /// outfit serves one purpose — so turning recreation on clears the
        /// other two outright rather than riding beside them, and turning it
        /// off falls back to excluded (nothing selected IS the excluded state
        /// under another name). No seeding either way: there is nothing to
        /// preserve across an exclusive switch.
        /// </summary>
        public void ToggleRecreation()
        {
            bool current = HandlesRecreation();
            excluded = false;
            workTypeOverrides = workTypeOverrides ?? new List<WorkTypeDef>();
            workTypeOverrides.Clear();
            restOverride = false;
            recreationOverride = !current;
            if (!recreationOverride)
            {
                SetExcluded();
            }
            EnforceRemovalFlag();
        }

        /// <summary>
        /// Flips the sleep trigger, the exact mirror of
        /// <see cref="ToggleRecreation"/> and bound by the same exclusivity.
        ///
        /// <para><see cref="depositOnly"/> is deliberately NOT cleared here.
        /// It is a property of the rack rather than of the trigger — the same
        /// standing as <see cref="fullChange"/> — and clearing it would lose
        /// the player's setting every time they toggled the row off and on to
        /// see what it did.</para>
        /// </summary>
        public void ToggleRest()
        {
            bool current = HandlesRest();
            excluded = false;
            workTypeOverrides = workTypeOverrides ?? new List<WorkTypeDef>();
            workTypeOverrides.Clear();
            recreationOverride = false;
            restOverride = !current;
            if (!restOverride)
            {
                SetExcluded();
            }
            EnforceRemovalFlag();
        }

        /// <summary>
        /// Display helper: "doctoring, researching" etc. The recreation
        /// trigger joins the same list, so every surface that names the
        /// stand's purpose names all of it.
        /// </summary>
        public string WorkTypesLabel()
        {
            List<WorkTypeDef> works = WorkTypes;
            bool recreation = HandlesRecreation();
            bool rest = HandlesRest();
            if (works.Count == 0 && !recreation && !rest)
            {
                return "ShiftChange.None".Translate();
            }
            List<string> labels = new List<string>(works.Count + 1);
            for (int i = 0; i < works.Count; i++)
            {
                labels.Add(works[i].gerundLabel ?? works[i].labelShort ?? works[i].defName);
            }
            if (recreation)
            {
                labels.Add("ShiftChange.Recreation".Translate().RawText);
            }
            if (rest)
            {
                labels.Add("ShiftChange.Rest".Translate().RawText);
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
            // NOT Notify_StandFreed. The stand is free in the ledger, but the
            // pawn who just undressed still holds its maxPawns=1 RESERVATION —
            // this runs from a toil finish action, and the tracker does not
            // release reservations until later in the teardown
            // (Pawn_JobTracker.CleanupCurrentJob:492). Every candidate the
            // catch-up looked at was rejected by CanReserveAndReach, so it
            // could never fire on its own trigger.
            //
            // JobDriver_SwapAtStand raises it from a GLOBAL finish action
            // instead, which the tracker runs at :497 — after the release.
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
        /// borrower is gone and their claims went with them. The JOB DRIVER
        /// does not — it runs from a toil finish action, while the pawn still
        /// holds the stand's reservation, so an announcement there reaches a
        /// candidate list that CanReserveAndReach has already emptied. The
        /// driver defers it to a global finish action instead.
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

        public override string CompInspectStringExtra()
        {
            if (excluded)
            {
                return "ShiftChange.InspectExcluded".Translate();
            }
            if (WorkTypes.Count == 0 && !HandlesRecreation() && !HandlesRest())
            {
                return null;
            }

            string line = "ShiftChange.InspectWork".Translate(WorkTypesLabel());

            // Say who owns or holds it here as well as on the gizmo: the base
            // comp reports ownership nowhere, and a stand that looks unassigned
            // is indistinguishable from one that simply never fires.
            List<Pawn> owners = AssignedOwners;
            line += "\n" + (owners.Count == 1
                ? "ShiftChange.InspectOwner".Translate(owners[0].LabelShort)
                : owners.Count > 1
                ? "ShiftChange.InspectOwners".Translate(owners.Count, OwnerNames(owners))
                : (ShiftChangeMod.PoolingEnabled
                    ? "ShiftChange.InspectPool".Translate()
                    // Keep the UI honest: with pooling off, "shared" would be
                    // a lie the player debugs for an evening.
                    : "ShiftChange.InspectPoolDisabled".Translate()));

            // Worth a line of its own: a full change is the difference between
            // a pawn keeping their own clothes under the uniform and wearing
            // nothing but the stand's kit, and nothing else on this pane would
            // tell them which stand they are looking at. Suppressed while
            // excluded, matching the dialog — the flag does nothing there.
            if (fullChange && !excluded)
            {
                line += "\n" + "ShiftChange.InspectFullChange".Translate();
            }

            // Only the OFF state earns a line, and it earns one because it is
            // the state that loses clothes. Same idiom as the full-change line
            // above — report the setting that DIFFERS from the default, since
            // "protected" stamped on every stand in the colony is noise, and
            // noise is what the player learns to stop reading. Excluded stands
            // returned above, so this is already an in-service stand.
            if (!withholdFromTrade)
            {
                line += "\n" + "ShiftChange.InspectTradeable".Translate();
            }

            if (OnShift && borrower != null)
            {
                line += "\n" + "ShiftChange.InspectOnShift".Translate(borrower.LabelShort);
            }
            else if (DepositOnly)
            {
                // NOT the empty line. An empty deposit-only stand is the
                // normal, correct, fully-working state — it hands nothing out
                // — so "nothing to change into" would report the feature as a
                // fault. Say what it does instead, since the storage filter
                // is the setting that makes it work and the one a player who
                // sees nothing happen needs pointing at.
                line += "\n" + "ShiftChange.InspectDepositOnly".Translate();
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
            // The label reads the regime out loud: this one button decides
            // whether the stand is a shift stand or a regular one — and,
            // beside another outfit-stand mod, whose owner control it shows
            // — so its face carries the state instead of a static caption.
            //
            // ONE work type on the face, "(+N)" for the rest (principal,
            // 2026-08-18): a workshop's four gerunds overflow a gizmo label
            // into unreadability, and future set-bearing stands (recreation)
            // only grow the list. The full set stays on the hover desc and
            // in the inspect pane.
            string regime;
            if (excluded)
            {
                regime = "ShiftChange.RegimeOff".Translate().RawText;
            }
            else
            {
                List<WorkTypeDef> effective = WorkTypes;
                if (effective.Count == 0)
                {
                    regime = "ShiftChange.RegimeIdle".Translate().RawText;
                }
                else
                {
                    string first = effective[0].gerundLabel ?? effective[0].labelShort ?? effective[0].defName;
                    regime = effective.Count == 1
                        ? "ShiftChange.RegimeShift".Translate(first).RawText
                        : "ShiftChange.RegimeShiftMore".Translate(first, effective.Count - 1).RawText;
                }
            }
            yield return new Command_Action
            {
                defaultLabel = regime,
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
