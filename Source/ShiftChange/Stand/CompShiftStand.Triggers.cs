using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    public partial class CompShiftStand
    {
        /// <summary>
        /// Player override — a SET, because rooms host families of work
        /// (decided 2026-08-08). Empty means "infer from the room's role".
        /// </summary>
        internal List<WorkTypeDef> workTypeOverrides = new List<WorkTypeDef>();

        /// <summary>
        /// Pre-set-redesign saves scribed a single "workTypeOverride" def.
        /// Loaded once and folded into <see cref="workTypeOverrides"/>.
        /// </summary>
        internal WorkTypeDef workTypeOverrideLegacy;

        /// <summary>
        /// Player override for the RECREATION trigger. MUTUALLY
        /// EXCLUSIVE with <see cref="workTypeOverrides"/> (decided
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
        /// power-armour-before-bed case (decided 2026-09-02).
        ///
        /// <para><b>Only meaningful on a sleep stand</b>, and
        /// <see cref="DepositOnly"/> enforces that rather than trusting the
        /// dialog to hide the row: the sleep trigger is what makes the mode
        /// safe, so a stand switched back to work must not carry the flag with
        /// it (docs/DESIGN.md, "The sleep branch").</para>
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
        /// Keep this stand's contents out of trade windows. Vanilla lists them
        /// to orbital ships and caravans whatever the removal flag says, so
        /// this defaults ON, and saves that predate it adopt it: why on both
        /// counts, docs/DESIGN.md, "Withholding from trade".
        ///
        /// <para>Read through <see cref="BlocksTrade"/>, never directly: that
        /// property folds in <see cref="excluded"/>, because a stand declared
        /// not-ours is untouched vanilla no matter what this flag says.</para>
        /// </summary>
        internal bool withholdFromTrade = true;

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
                // Automatic is the room's reading, and only a stand serving its
                // room has one (docs/DESIGN.md, "Catchments").
                if (!parent.Spawned || serves != CatchmentKind.Room)
                {
                    return NoWork;
                }
                return AutomaticWorkTypes;
            }
        }

        /// <summary>
        /// How long an automatic answer is reused before the room is looked at
        /// again. ~4 seconds at normal speed.
        /// </summary>
        internal const int AutomaticCacheTicks = 250;

        internal List<WorkTypeDef> automaticCache;
        internal int automaticCacheTick = -1;
        internal int automaticCacheRoomId = -1;

        /// <summary>
        /// The room's own answer: its role's defaults, plus whatever its
        /// CONTENTS earn (see <see cref="RoomContentsWork"/>, which covers the
        /// rooms the engine gives no useful role to).
        ///
        /// <para>CACHED, and it has to be. The contents half calls
        /// <see cref="Room.ContainedAndAdjacentThings"/>, which clears and
        /// rebuilds a set and a list every call, and this property is read
        /// every frame to draw the gizmo label. Two invalidations: the room
        /// identity changing — walls moved, stand reinstalled — and a short
        /// interval, which is what notices a reactor built into a room that
        /// already existed. A building placed while the game is PAUSED
        /// therefore lands on unpause, since TicksGame is what expires the
        /// entry; that is the only visible lag and it is not worth a listener.</para>
        ///
        /// <para>Returns the shared role list unchanged when contents add
        /// nothing, which is every room in a game without a contents mod
        /// installed, so the common path allocates nothing.</para>
        /// </summary>
        internal List<WorkTypeDef> AutomaticWorkTypes
        {
            get
            {
                Room room = parent.GetRoom();
                if (room == null)
                {
                    return NoWork;
                }
                if (!RoomContentsWork.Any)
                {
                    return RoomWorkTypes.ForRole(room.Role);
                }

                TickManager ticks = Find.TickManager;
                int now = ticks != null ? ticks.TicksGame : -1;
                if (automaticCache != null
                    && automaticCacheRoomId == room.ID
                    && now >= 0 && automaticCacheTick >= 0
                    && now - automaticCacheTick < AutomaticCacheTicks)
                {
                    return automaticCache;
                }

                List<WorkTypeDef> byRole = RoomWorkTypes.ForRole(room.Role);
                List<WorkTypeDef> combined = new List<WorkTypeDef>(byRole);
                RoomContentsWork.Collect(room, combined);
                // Nothing added: hand back the SHARED list rather than a copy,
                // so identity comparisons and allocation both behave as they
                // did before contents existed.
                if (combined.Count == byRole.Count)
                {
                    combined = byRole;
                }

                automaticCache = combined;
                automaticCacheRoomId = room.ID;
                automaticCacheTick = now;
                return combined;
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
            if (!parent.Spawned || serves != CatchmentKind.Room)
            {
                return false;
            }
            // WORK WINS, and this limb is what still enforces that. The tables
            // used to guarantee it by construction — ResolvedRecreation drops
            // any role that ResolvedRole already claims — but contents-supplied
            // work does not go through a role at all, so a recreation-roled
            // room holding a marker building could now answer yes to both and
            // resurrect the dual-purpose stand the exclusivity rule exists to
            // prevent. A rec room with a reactor core in it is absurd; so was
            // the collision the table guards, and it is guarded anyway.
            return RoomWorkTypes.RecreationForRole(parent.GetRoom()?.Role)
                && AutomaticWorkTypes.Count == 0;
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
            if (!parent.Spawned || serves != CatchmentKind.Room)
            {
                return false;
            }
            // Same ordering guard as HandlesRecreation, for the same reason:
            // work, then recreation, then rest, decided identically every time.
            return RoomWorkTypes.RestForRole(parent.GetRoom()?.Role)
                && AutomaticWorkTypes.Count == 0;
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
        /// <para>The dialog hides the work grid only for an explicit trigger,
        /// because an automatic stand's trigger is a suggestion from the room
        /// and must stay overridable in one click; hiding it on any active
        /// trigger once left a bedroom stand no path to a work type
        /// (docs/DESIGN.md, "The sleep branch"). Not for every explicit one
        /// either: <see cref="HidesWorkList"/> is the dialog's test.</para>
        /// </summary>
        public bool IsTriggerOverridden => !excluded && (recreationOverride || restOverride);

        /// <summary>
        /// The explicit trigger is the one this stand's room would suggest
        /// anyway: Sleeping ticked in a bedroom, Recreation in a rec room. That
        /// is what an automatic stand's suggestion becomes when it moves to a
        /// radius or an area, which has to tick it for real
        /// (<see cref="TickAutomaticSet"/>), so it keeps the automatic stand's
        /// one-click path to a work type. Read from the room live, through the
        /// same role tables <see cref="HandlesRest"/> and
        /// <see cref="HandlesRecreation"/> consult, so nothing is saved for it
        /// and a room that changes role changes the answer.
        /// </summary>
        internal bool TriggerIsRoomsOwn
        {
            get
            {
                if (!IsTriggerOverridden || !parent.Spawned)
                {
                    return false;
                }
                // Work wins in the room's own reading, so a room that would
                // give work types suggests no trigger.
                if (AutomaticWorkTypes.Count > 0)
                {
                    return false;
                }
                RoomRoleDef role = parent.GetRoom()?.Role;
                return restOverride
                    ? RoomWorkTypes.RestForRole(role)
                    : RoomWorkTypes.RecreationForRole(role);
            }
        }

        /// <summary>
        /// The settings window hides the work list: an explicit trigger the
        /// room would not have suggested. Ticking a work type would silently
        /// drop that trigger, so the list stays out of reach until it is
        /// unticked. Every other stand keeps the list, and ticking a work type
        /// replaces the room's suggestion (docs/DESIGN.md, "The sleep branch").
        /// </summary>
        internal bool HidesWorkList => IsTriggerOverridden && !TriggerIsRoomsOwn;

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
        /// DECLARATION rather than on resolved work types, like the
        /// removal-flag disable, so an excluded stand stays vanilla whatever
        /// the flag says (docs/DESIGN.md, "Withholding from trade").
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
            // Automatic is a room's reading, so it is room mode.
            ApplyServes(CatchmentKind.Room);
            EnforceRemovalFlag();
        }

        public void SetExcluded()
        {
            excluded = true;
            recreationOverride = false;
            restOverride = false;
            workTypeOverrides?.Clear();
            // Back to room mode, so no stand is ever in another catchment with
            // nothing ticked. The radius is kept for when it comes back
            // (docs/DESIGN.md, "Catchments").
            ApplyServes(CatchmentKind.Room);
            // Owners are left alone. A group list waits here for the stand to
            // go back into shift use, and picking one colonist while it is out
            // of shift use is what replaces it
            // (CompAssignableToPawn_ShiftStand.SingleOwnerOnly). Unticking the
            // last work type, Recreation or Sleeping lands here too, so
            // clearing owners on this path would cost a configured group on an
            // ordinary edit.
        }

        /// <summary>
        /// Flips one work type in the effective set, entering custom mode from
        /// wherever the stand currently is: toggling while automatic seeds the
        /// custom set from the room's defaults first, toggling while excluded
        /// starts a fresh set, and emptying the custom set collapses back to
        /// excluded so the states stay canonical. Work types, recreation and
        /// sleep are mutually exclusive (decided 2026-08-16), so touching any
        /// work type also drops the recreation and sleep triggers. The dialog
        /// hides this list while a trigger the room did not suggest is ticked,
        /// so reaching here from such a stand is a deliberate switch; on a
        /// stand whose trigger is the room's suggestion, automatic or ticked,
        /// the list stays reachable on purpose (see
        /// <see cref="HidesWorkList"/>).
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
        /// MUTUALLY EXCLUSIVE on a stand (decided 2026-08-16, extended to
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
    }
}
