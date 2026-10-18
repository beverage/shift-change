using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShiftChange
{
    public static partial class Patch_JobInterception
    {
        /// <summary>
        /// The most regions an air walk visits before it stops and reports
        /// <see cref="AirRoute.Unread"/>: past it the walk decides nothing,
        /// which dresses nobody and sends nobody to change.
        /// </summary>
        internal const int AirWalkRegionCap = 4000;

        /// <summary>
        /// The fewest ticks between two vacuum messages about one colonist, an
        /// in-game hour. A drafted colonist starts a forced job on every click
        /// and every attack, and vanilla answers a repeated message by flashing
        /// it and playing its sound again.
        /// </summary>
        internal const int VacuumWarningTicks = 2500;

        /// <summary>
        /// The regions an air walk still has to reach. Cleared after every walk,
        /// so it never holds a region past the call that filled it.
        /// </summary>
        internal static readonly HashSet<Region> AirWalkTargets = new HashSet<Region>();

        /// <summary>
        /// When each colonist was last told about vacuum, by thingIDNumber.
        /// Cleared with the session (<see cref="ResetSessionState"/>).
        /// </summary>
        internal static readonly Dictionary<int, int> LastVacuumWarningTick = new Dictionary<int, int>();

        /// <summary>What an air walk found.</summary>
        internal enum AirRoute
        {
            /// <summary>Every place was reached through air.</summary>
            Air,

            /// <summary>
            /// Some place was not: it is in vacuum, only vacuum leads to it, or
            /// it is on another map. Also the answer when the walk starts in
            /// vacuum.
            /// </summary>
            Vacuum,

            /// <summary>
            /// The walk could not be made: no region to start from, the map's
            /// region updater switched off, or the region cap reached.
            /// </summary>
            Unread,
        }

        /// <summary>What the gate makes of a colonist in uniform about to take a job.</summary>
        internal enum UniformInVacuum
        {
            /// <summary>
            /// Nothing to do: not a vacuum map, the uniform protects as well as
            /// their own clothes, or the trip stays in air or could not be read.
            /// </summary>
            Fine,

            /// <summary>The trip goes through vacuum and the stand can be reached through air: change back first.</summary>
            ChangeBack,

            /// <summary>The trip goes through vacuum, and so would the walk to the stand.</summary>
            StandAcrossVacuum,

            /// <summary>The trip goes through vacuum, and the stand is not on this map.</summary>
            StandElsewhere,
        }

        internal static bool VacuumMap(Map map)
        {
            return map != null && map.Biome.inVacuum;
        }

        /// <summary>
        /// The dress plan <see cref="SwapPlan.WouldDress"/> has just built, in
        /// its scratch lists, would leave the pawn less protected against
        /// vacuum (<see cref="SwapPlan.LeavesThemLessProtected"/>). Read straight
        /// after <c>WouldDress</c>, before anything builds another plan. Two
        /// apparel sums and a stat read; the region walk is
        /// <see cref="DressingTripGoesThroughVacuum"/>.
        /// </summary>
        internal static bool PlanLowersVacuumProtection(Pawn pawn, Map map)
        {
            return VacuumMap(map)
                   && SwapPlan.LeavesThemLessProtected(pawn, SwapPlan.ScratchStore, SwapPlan.ScratchWear);
        }

        /// <summary>
        /// The trip from <paramref name="stand"/> to the places
        /// <paramref name="job"/> names cannot be shown to stay in air
        /// (<see cref="AirRouteFor"/>), so a stand whose plan lowers vacuum
        /// protection must not dress them for it. A walk that could not be made
        /// counts: it dresses nobody. Asked by the stand search of a stand that
        /// would win, and by the mid-job catch-up. docs/DESIGN.md, "Vacuum".
        /// </summary>
        internal static bool DressingTripGoesThroughVacuum(Job job, Pawn pawn, CompShiftStand stand, Map map)
        {
            if (AirRouteFor(job, pawn, map, StandCell(stand)) == AirRoute.Air)
            {
                return false;
            }
            if (Verbose)
            {
                Log.Message($"[ShiftChange] {pawn.LabelShort} passes over {stand.parent.LabelShort} for " +
                            $"{job.def.defName}: it goes through vacuum, and the stand's outfit protects less");
            }
            return true;
        }

        /// <summary>
        /// The pawn on shift at <paramref name="onShift"/> is about to take
        /// <paramref name="job"/> through vacuum, from where they stand, in a
        /// uniform that protects them less than the clothes they parked
        /// (<see cref="SwapPlan.UniformLeavesThemLessProtected"/>). The same
        /// trip question the dressing asks, so the two directions agree. Then
        /// whether changing back first is worth anything: only when the walk
        /// to the stand stays in air, since otherwise the walk to change is
        /// itself the crossing the gate exists to prevent. docs/DESIGN.md,
        /// "Vacuum".
        /// </summary>
        internal static UniformInVacuum UniformIntoVacuum(Job job, Pawn pawn, CompShiftStand onShift, Map map)
        {
            if (!VacuumMap(map)
                || !SwapPlan.UniformLeavesThemLessProtected(pawn, onShift.IssuedUniformForReading,
                                                            onShift.StoredOwnerApparelForReading))
            {
                return UniformInVacuum.Fine;
            }
            if (AirRouteFor(job, pawn, map, pawn.Position) != AirRoute.Vacuum)
            {
                return UniformInVacuum.Fine;
            }
            Thing stand = onShift.parent;
            if (stand == null || !stand.Spawned || stand.Map != map)
            {
                return UniformInVacuum.StandElsewhere;
            }
            switch (AirRouteTo(pawn, map, pawn.Position, StandCell(onShift)))
            {
                case AirRoute.Air:
                    return UniformInVacuum.ChangeBack;
                case AirRoute.Vacuum:
                    return UniformInVacuum.StandAcrossVacuum;
                default:
                    return UniformInVacuum.Fine;
            }
        }

        /// <summary>
        /// The return trip would walk a colonist in uniform through vacuum to
        /// change: a vacuum map, a uniform that protects less than their own
        /// clothes, and no way to the stand through air. Asked before every
        /// return the return trip starts, whatever it starts it for, so a
        /// colonist cut off from their stand by vacuum is not sent across it
        /// in the clothes that make the crossing dangerous. They change back
        /// at the next job boundary they reach from the stand's side.
        /// </summary>
        internal static bool ReturnCrossesVacuum(Pawn pawn, CompShiftStand onShift, Map map)
        {
            Thing stand = onShift.parent;
            if (!VacuumMap(map) || stand == null || stand.MapHeld != map
                || !SwapPlan.UniformLeavesThemLessProtected(pawn, onShift.IssuedUniformForReading,
                                                            onShift.StoredOwnerApparelForReading))
            {
                return false;
            }
            if (AirRouteTo(pawn, map, pawn.Position, StandCell(onShift)) != AirRoute.Vacuum)
            {
                return false;
            }
            if (Verbose)
            {
                Log.Message($"[ShiftChange] {pawn.LabelShort} keeps the outfit on: the way back to " +
                            $"{stand.LabelShort} goes through vacuum, and the outfit protects less");
            }
            return true;
        }

        /// <summary>
        /// The order a colonist was just given takes them through vacuum in a
        /// uniform that protects less than their own clothes. Orders run as
        /// given in both directions (docs/DESIGN.md, "The gates"), so this only
        /// says so. Drafted moves are orders too, which is why it is also asked
        /// for a drafted colonist the interception otherwise leaves alone, and
        /// why it carries the guards <see cref="MapToDecideOn"/> would have
        /// applied. True if a message was posted.
        /// </summary>
        internal static bool WarnIfOrderedIntoVacuum(Job job, Pawn pawn)
        {
            if (job == null || !job.playerForced || pawn == null || !pawn.Spawned
                || Current.ProgramState != ProgramState.Playing
                || pawn.Faction != Faction.OfPlayer || !pawn.RaceProps.Humanlike || pawn.Downed
                || job.def == ShiftChangeDefOf.ShiftChange_SwapAtStand
                || !VacuumMap(pawn.Map) || WarnedLately(pawn))
            {
                return false;
            }
            CompShiftStand worn = CompShiftStand.OnShiftStandFor(pawn);
            return worn != null && WarnOfVacuum(pawn, UniformIntoVacuum(job, pawn, worn, pawn.Map), ordered: true);
        }

        /// <summary>
        /// Tell the player a colonist is heading through vacuum in a uniform
        /// that protects less, when nothing the mod does will change it: an
        /// order, which runs as given, or a stand the colonist cannot reach
        /// through air or at all. A change back the mod is about to start says
        /// nothing. At most once a <see cref="VacuumWarningTicks"/> per
        /// colonist; the caller checks <see cref="WarnedLately"/> first, before
        /// the walk. True if a message was posted.
        /// </summary>
        internal static bool WarnOfVacuum(Pawn pawn, UniformInVacuum outcome, bool ordered)
        {
            string key;
            switch (outcome)
            {
                case UniformInVacuum.ChangeBack:
                    if (!ordered)
                    {
                        return false;
                    }
                    key = "ShiftChange.OrderIntoVacuum";
                    break;
                case UniformInVacuum.StandAcrossVacuum:
                    key = "ShiftChange.VacuumStandAcrossVacuum";
                    break;
                case UniformInVacuum.StandElsewhere:
                    key = "ShiftChange.VacuumStandElsewhere";
                    break;
                default:
                    return false;
            }
            Messages.Message(key.Translate(pawn.LabelShort), pawn, MessageTypeDefOf.CautionInput, historical: false);
            LastVacuumWarningTick[pawn.thingIDNumber] = Find.TickManager.TicksGame;
            return true;
        }

        /// <summary>The colonist was told about vacuum less than <see cref="VacuumWarningTicks"/> ago.</summary>
        internal static bool WarnedLately(Pawn pawn)
        {
            int last;
            return LastVacuumWarningTick.TryGetValue(pawn.thingIDNumber, out last)
                   && Find.TickManager.TicksGame - last < VacuumWarningTicks;
        }

        /// <summary>
        /// Whether every place <paramref name="job"/> names can be reached from
        /// <paramref name="from"/> through air (<see cref="AirWalk"/>). Those
        /// are the places <see cref="EveryPlaceCovered"/> reads, read the same
        /// way (<see cref="ReadPlace"/>), so a bill's ingredients count. A safe
        /// place cut off by vacuum fails as surely as a place in vacuum.
        ///
        /// <para>It reads the map, not the path. The pathfinder ignores vacuum,
        /// so where air and vacuum both connect two places and crossing the
        /// vacuum is shorter, the pawn may cross it anyway: the known gap,
        /// left out of v1.5.2 on purpose. Not part of the shared room resolver,
        /// which must not read the map; both directions ask this same
        /// question. docs/DESIGN.md, "Vacuum".</para>
        /// </summary>
        internal static AirRoute AirRouteFor(Job job, Pawn pawn, Map map, IntVec3 from)
        {
            HashSet<Region> targets = AirWalkTargets;
            targets.Clear();
            try
            {
                if (!AddAirTarget(job.targetA, map, targets)
                    || !AddAirTarget(job.targetB, map, targets)
                    || !AddAirTarget(job.targetC, map, targets)
                    || !AddAirTargets(job.targetQueueA, map, targets)
                    || !AddAirTargets(job.targetQueueB, map, targets))
                {
                    return AirRoute.Vacuum;
                }
                return AirWalk(pawn, map, from, targets);
            }
            finally
            {
                targets.Clear();
            }
        }

        /// <summary>Whether one cell can be reached from <paramref name="from"/> through air.</summary>
        internal static AirRoute AirRouteTo(Pawn pawn, Map map, IntVec3 from, IntVec3 to)
        {
            HashSet<Region> targets = AirWalkTargets;
            targets.Clear();
            try
            {
                AddAirTarget(to, map, targets);
                return AirWalk(pawn, map, from, targets);
            }
            finally
            {
                targets.Clear();
            }
        }

        /// <summary>
        /// The air walk: region by region from <paramref name="from"/>,
        /// stepping only into regions whose room holds less vacuum than harms a
        /// pawn (<see cref="HoldsAir"/>) and that the pawn may pass
        /// (<c>Region.Allows</c>, as reachability asks it: a door they cannot
        /// open, or one forbidden to them, is not a way through, however much
        /// air is behind it). Empties <paramref name="targets"/> as it reaches
        /// them, and stops when they are all reached.
        /// </summary>
        internal static AirRoute AirWalk(Pawn pawn, Map map, IntVec3 from, HashSet<Region> targets)
        {
            if (!map.regionAndRoomUpdater.Enabled || !from.IsValid || !from.InBounds(map))
            {
                return AirRoute.Unread;
            }
            Region start = from.GetRegion(map);
            if (start == null)
            {
                return AirRoute.Unread;
            }
            if (!HoldsAir(start))
            {
                return AirRoute.Vacuum;
            }
            targets.Remove(start);
            if (targets.Count == 0)
            {
                return AirRoute.Air;
            }
            TraverseParms parms = TraverseParms.For(pawn);
            int visited = 0;
            bool capped = false;
            RegionTraverser.BreadthFirstTraverse(start,
                (fromRegion, to) => HoldsAir(to) && to.Allows(parms, false),
                region =>
                {
                    targets.Remove(region);
                    if (targets.Count == 0)
                    {
                        return true;
                    }
                    if (++visited >= AirWalkRegionCap)
                    {
                        capped = true;
                        return true;
                    }
                    return false;
                }, AirWalkRegionCap + 1);
            if (targets.Count == 0)
            {
                return AirRoute.Air;
            }
            return capped ? AirRoute.Unread : AirRoute.Vacuum;
        }

        /// <summary>
        /// A region holding air: its room reads below the vacuum that harms a
        /// pawn. A doorway reads the mean of the rooms on either side, so the
        /// door between air and open space stops the walk too.
        /// </summary>
        internal static bool HoldsAir(Region region)
        {
            Room room = region?.Room;
            return room != null && room.Vacuum < VacuumUtility.MinVacuumForDamage;
        }

        /// <summary>
        /// Adds the region a place stands in to <paramref name="targets"/>, the
        /// place read by <see cref="ReadPlace"/>: its own cell, or for a solid
        /// place the cell beside it with a room. A place that cannot be read is
        /// skipped. False for a thing on another map, which no walk on this
        /// one reaches.
        /// </summary>
        internal static bool AddAirTarget(LocalTargetInfo place, Map map, HashSet<Region> targets)
        {
            JobPlace read;
            PlaceRead found = ReadPlace(place, map, out read);
            if (found == PlaceRead.OtherMap)
            {
                return false;
            }
            if (found == PlaceRead.Here)
            {
                Region region = read.Cell.GetRegion(map) ?? read.RoomCell.GetRegion(map);
                if (region != null)
                {
                    targets.Add(region);
                }
            }
            return true;
        }

        /// <summary><see cref="AddAirTarget"/> for every entry of a target queue.</summary>
        internal static bool AddAirTargets(List<LocalTargetInfo> queue, Map map, HashSet<Region> targets)
        {
            if (queue == null)
            {
                return true;
            }
            for (int i = 0; i < queue.Count; i++)
            {
                if (!AddAirTarget(queue[i], map, targets))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Where a colonist stands to use <paramref name="stand"/>.</summary>
        internal static IntVec3 StandCell(CompShiftStand stand)
        {
            Thing thing = stand.parent;
            return thing.def.hasInteractionCell ? thing.InteractionCell : thing.Position;
        }
    }
}
