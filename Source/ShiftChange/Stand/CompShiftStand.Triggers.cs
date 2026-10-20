using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    public partial class CompShiftStand
    {
        /// <summary>Player override: a set, because a room hosts several kinds of work.</summary>
        internal List<WorkTypeDef> workTypeOverrides = new List<WorkTypeDef>();

        /// <summary>The single work type older saves wrote, folded into the set on load.</summary>
        internal WorkTypeDef workTypeOverrideLegacy;

        /// <summary>
        /// The recreation trigger. Mutually exclusive with the work set and the sleep trigger,
        /// because a stand holds one outfit for one purpose; the toggles enforce it. True also
        /// marks an empty work set as custom rather than automatic.
        /// </summary>
        internal bool recreationOverride;

        /// <summary>The sleep trigger, exclusive like <see cref="recreationOverride"/>.</summary>
        internal bool restOverride;

        /// <summary>
        /// Hands nothing out: the pawn parks the garments the storage filter accepts and keeps
        /// everything else on. Meaningful only on a sleep stand, since the sleep trigger is what
        /// makes the mode safe. Read through <see cref="DepositOnly"/>, never directly: it gates
        /// the flag on the sleep trigger. Stored raw, so a flip to work and back keeps it.
        /// </summary>
        internal bool depositOnly;

        /// <summary>Opt-out, so a decorative stand does not join its room's pool.</summary>
        internal bool excluded;

        /// <summary>
        /// Swap the whole outfit, not only what the stand's kit conflicts with. Off by default:
        /// a full change costs the equip time of every garment in both directions. Read only
        /// when building a dress plan; the return trip is driven by the ledger, so flipping it
        /// while a uniform is out strands nothing.
        /// </summary>
        internal bool fullChange;

        /// <summary>
        /// Keeps the stand's contents out of trade windows. Vanilla lists them to orbital ships
        /// and caravans whatever the stand's "Allow removing items" flag says, so this defaults
        /// on, and saves without it load protected (docs/DESIGN.md, "Withholding from trade").
        /// Read through <see cref="BlocksTrade"/>, never directly: it folds in
        /// <see cref="excluded"/>.
        /// </summary>
        internal bool withholdFromTrade = true;

        internal static readonly List<WorkTypeDef> NoWork = new List<WorkTypeDef>();

        /// <summary>
        /// The work types this stand dresses for: none when excluded, else the override set,
        /// else the room's role defaults. Empty means inert. Never null; treat as read-only.
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
                // A recreation-only or sleep-only custom set has no work types on purpose; the
                // room's defaults would bring back ones the player removed.
                if (recreationOverride || restOverride)
                {
                    return NoWork;
                }
                // Automatic is room mode only: a radius or an area has no role to read.
                if (!parent.Spawned || serves != CatchmentKind.Room)
                {
                    return NoWork;
                }
                return AutomaticWorkTypes;
            }
        }

        /// <summary>How long an automatic answer is reused: about 4 s at normal speed.</summary>
        internal const int AutomaticCacheTicks = 250;

        internal List<WorkTypeDef> automaticCache;
        internal int automaticCacheTick = -1;
        internal int automaticCacheRoomId = -1;

        /// <summary>
        /// The room's role defaults plus the work its contents earn
        /// (<see cref="RoomContentsWork"/>). May be the shared role list, so treat as read-only.
        ///
        /// <para>Cached: the contents half calls <see cref="Room.ContainedAndAdjacentThings"/>,
        /// which clears and rebuilds a set and a list on every call, and the gizmo label reads
        /// this every frame. An entry expires when the stand's room ID changes (a rebuilt room
        /// usually gets a new one, but one split by a fence can keep it when only part is
        /// rebuilt) and after <see cref="AutomaticCacheTicks"/> of <c>TicksGame</c>, which
        /// notices a building added to an existing room. <c>TicksGame</c> stops while the game
        /// is paused, so such a building shows on unpause.</para>
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
        /// Whether this stand dresses for recreation, the joy parallel of
        /// <see cref="HandlesWork"/>: excluded wins, a custom set answers from its own bool,
        /// automatic asks the room's role.
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
            // Work wins. The role tables are disjoint, but work earned from a room's contents
            // does not pass through a role, so without this check a room with a recreation role
            // and such a building would answer yes to both, the dual-purpose stand that the
            // exclusivity rule forbids.
            return RoomWorkTypes.RecreationForRole(parent.GetRoom()?.Role)
                && AutomaticWorkTypes.Count == 0;
        }

        /// <summary>
        /// Whether this stand dresses for sleep, resolved like <see cref="HandlesRecreation"/>.
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
            // Work wins here too. RoomWorkTypes keeps the recreation and sleep roles disjoint.
            return RoomWorkTypes.RestForRole(parent.GetRoom()?.Role)
                && AutomaticWorkTypes.Count == 0;
        }

        /// <summary>
        /// This stand's triggers are set explicitly, by the player or by
        /// <see cref="TickAutomaticSet"/>, so nothing falls back to the room. Any of the three
        /// stores being non-empty says so; exclusivity means at most one is.
        /// </summary>
        internal bool HasCustomSet =>
            (workTypeOverrides != null && workTypeOverrides.Count > 0)
            || recreationOverride || restOverride;

        /// <summary>
        /// Recreation or sleep is set explicitly: ticked by the player, or written by
        /// <see cref="TickAutomaticSet"/> when an automatic stand left room mode. An automatic
        /// stand's trigger is only the room's suggestion, so it does not count. Not the dialog's
        /// test for hiding the work list: that is <see cref="HidesWorkList"/>.
        /// </summary>
        public bool IsTriggerOverridden => !excluded && (recreationOverride || restOverride);

        /// <summary>
        /// The explicit trigger is the one this stand's room would suggest: Sleeping in a
        /// bedroom, Recreation in a rec room. An automatic stand's suggestion becomes this when
        /// it moves to a radius or an area (<see cref="TickAutomaticSet"/>). Read from the
        /// room's role each time, so nothing is saved for it.
        /// </summary>
        internal bool TriggerIsRoomsOwn
        {
            get
            {
                if (!IsTriggerOverridden || !parent.Spawned)
                {
                    return false;
                }
                // Work wins in the room's reading: a room giving work types suggests no trigger.
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
        /// The settings window hides the work list when the stand has an explicit trigger its
        /// room would not have suggested, since ticking a work type would silently drop it.
        /// Every other stand keeps the list, so an automatic stand or one holding the room's own
        /// trigger stays one click from a work type (docs/DESIGN.md, "The sleep branch").
        /// </summary>
        internal bool HidesWorkList => IsTriggerOverridden && !TriggerIsRoomsOwn;

        /// <summary>The deposit-only flag, gated on the sleep trigger.</summary>
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
        /// The question <see cref="Patch_WithholdFromTrade"/> asks. Keyed on the declaration,
        /// not the resolved work types, so an excluded stand stays vanilla whatever the flag says.
        /// </summary>
        public bool BlocksTrade => !excluded && withholdFromTrade;

        /// <summary>
        /// Not excluded, with no explicit work set or trigger. <see cref="fullChange"/> and
        /// <see cref="depositOnly"/> describe the rack's contents, not the trigger, so they do
        /// not count against it.
        /// </summary>
        public bool IsAutomatic => !excluded && !HasCustomSet;

        public void SetAutomatic()
        {
            excluded = false;
            recreationOverride = false;
            restOverride = false;
            workTypeOverrides?.Clear();
            ApplyServes(CatchmentKind.Room);
            EnforceRemovalFlag();
        }

        public void SetExcluded()
        {
            excluded = true;
            recreationOverride = false;
            restOverride = false;
            workTypeOverrides?.Clear();
            // A stand outside room mode must have something ticked, so go back to room mode.
            // The radius is kept for when it comes back.
            ApplyServes(CatchmentKind.Room);
            // Owners are kept, not cleared: unticking the last trigger lands here too, and
            // clearing would cost a configured group on an ordinary edit. While excluded, picking
            // one colonist replaces the group (CompAssignableToPawn_ShiftStand.SingleOwnerOnly).
        }

        /// <summary>
        /// Flips one work type, entering custom mode from wherever the stand is: automatic seeds
        /// the set from the room's defaults, excluded starts a fresh one, and emptying it falls
        /// back to excluded. Also drops the recreation and sleep triggers, which are exclusive
        /// with work types.
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
        /// Flips the recreation trigger. On clears the work set and the sleep trigger; off falls
        /// back to excluded, which is what nothing selected means. Nothing is seeded, since
        /// there is nothing to preserve across an exclusive switch.
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
        /// Flips the sleep trigger, the mirror of <see cref="ToggleRecreation"/>.
        /// <see cref="depositOnly"/> is deliberately not cleared: it belongs to the rack, like
        /// <see cref="fullChange"/>, and clearing it would lose the player's setting on every
        /// toggle.
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
