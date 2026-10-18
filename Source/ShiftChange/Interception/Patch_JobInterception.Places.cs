using System;
using System.Collections.Generic;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ShiftChange
{
    public static partial class Patch_JobInterception
    {
        /// <summary>
        /// Every place this job names is inside the stand's catchment: its
        /// three targets and both queues. Pass-through work is judged at both
        /// ends this way, so a haul to a freezer outside changes the pawn back
        /// before it, while the stand is still near. A place that cannot be
        /// read is skipped, as <see cref="TargetCell"/> skips it, and a job
        /// that names none is judged where the pawn stands.
        /// </summary>
        internal static bool EveryPlaceCovered(Job job, Pawn pawn, CompShiftStand stand, Map map)
        {
            bool named = false;
            if (!PlaceCovered(job.targetA, stand, map, ref named)
                || !PlaceCovered(job.targetB, stand, map, ref named)
                || !PlaceCovered(job.targetC, stand, map, ref named)
                || !QueueCovered(job.targetQueueA, stand, map, ref named)
                || !QueueCovered(job.targetQueueB, stand, map, ref named))
            {
                return false;
            }
            return named || stand.Covers(pawn.Position, map);
        }

        /// <summary>
        /// One place a job names is inside the catchment, or cannot be read.
        /// Sets <paramref name="named"/> once any place could be. A solid place
        /// is judged as the job's own location is, at its own cell, then the
        /// cell beside it with a room, so a wall's repair is inside or outside
        /// by the same rule whichever check asks.
        /// </summary>
        internal static bool PlaceCovered(LocalTargetInfo place, CompShiftStand stand, Map map,
                                          ref bool named)
        {
            JobPlace read;
            switch (ReadPlace(place, map, out read))
            {
                case PlaceRead.Unreadable:
                    return true;
                case PlaceRead.OtherMap:
                    // Outside this map's catchment whatever its coordinates read.
                    named = true;
                    return false;
                default:
                    named = true;
                    return stand.Covers(read, map);
            }
        }

        /// <summary>What <see cref="ReadPlace"/> made of a place a job names.</summary>
        internal enum PlaceRead
        {
            /// <summary>No cell on this map: an empty target, or one out of bounds.</summary>
            Unreadable,

            /// <summary>A thing on another map, as a delivery across levels would name.</summary>
            OtherMap,

            /// <summary>A place on this map.</summary>
            Here,
        }

        /// <summary>
        /// One place a job names, read the one way every check that reads a
        /// job's places reads it: a thing where it is held, and a solid place
        /// at its own cell with the room cell beside it
        /// (<see cref="RoomBearing"/>). The catchment checks
        /// (<see cref="PlaceCovered"/>) and the vacuum walk
        /// (<see cref="AddAirTarget"/>) both read places through this, so the
        /// two cannot come to disagree about where a job goes.
        /// </summary>
        internal static PlaceRead ReadPlace(LocalTargetInfo place, Map map, out JobPlace read)
        {
            read = JobPlace.Invalid;
            if (place.HasThing && place.Thing.MapHeld != null && place.Thing.MapHeld != map)
            {
                return PlaceRead.OtherMap;
            }
            IntVec3 cell = place.HasThing ? place.Thing.PositionHeld : place.Cell;
            if (!cell.IsValid || !cell.InBounds(map))
            {
                return PlaceRead.Unreadable;
            }
            read = new JobPlace(cell, RoomBearing(cell, place, map));
            return PlaceRead.Here;
        }

        /// <summary><see cref="PlaceCovered"/> for every entry of a target queue.</summary>
        internal static bool QueueCovered(List<LocalTargetInfo> queue, CompShiftStand stand, Map map,
                                          ref bool named)
        {
            if (queue == null)
            {
                return true;
            }
            for (int i = 0; i < queue.Count; i++)
            {
                if (!PlaceCovered(queue[i], stand, map, ref named))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>A room for a verbose line: its role, or that there is none.</summary>
        internal static string RoomName(Room room)
        {
            return room == null ? "no room" : (room.Role?.defName ?? "unroled") + " room";
        }

        /// <summary>
        /// Where the job happens. <c>targetA</c> covers the work types room
        /// mode cares about, but queue-based jobs (hauling, harvesting) leave
        /// it empty — verified in the spike — so fall back to the queue rather
        /// than treating those as "no location".
        ///
        /// <para>A short list of jobs answers from <c>targetB</c> instead,
        /// because they name their destination in targetA and the thing being
        /// carried in targetB; see <see cref="JobRoomTargets"/>.</para>
        ///
        /// <para>Both cells are kept: the one the target names, and the room
        /// cell <see cref="RoomBearing"/> gives a solid target, since a radius
        /// or an area can serve the first where a room stand needs the second
        /// (<see cref="JobPlace"/>).</para>
        /// </summary>
        internal static JobPlace TargetPlace(Job job, Map map)
        {
            // A few jobs name their DESTINATION in targetA and the thing being
            // carried in targetB, which is the reverse of both vanilla shapes
            // — see JobRoomTargets for the list and why it is a list. Tried
            // first, and it falls through to targetA when targetB is unreadable
            // so a malformed job still resolves somewhere.
            //
            // targetB's thing is read exactly like targetA's, PositionHeld and
            // all. That means once the pawn has PICKED THE ROD UP this answers
            // "wherever the carrier is standing" rather than the pool. At a job
            // boundary, which is where both arms ask, the rod is still on the
            // ground and the answer is the pool. The catch-up sweep can ask
            // mid-carry, and there it degrades to the room the pawn is walking
            // through — which is the room a freed stand would have to be in to
            // match anyway, so it stays harmless.
            if (JobRoomTargets.UsesTargetB(job.def))
            {
                IntVec3 carried = job.targetB.HasThing
                    ? job.targetB.Thing.PositionHeld
                    : job.targetB.Cell;
                if (carried.IsValid && carried.InBounds(map))
                {
                    return new JobPlace(carried, RoomBearing(carried, job.targetB, map));
                }
            }

            IntVec3 cell = job.targetA.HasThing ? job.targetA.Thing.PositionHeld : job.targetA.Cell;
            if (cell.IsValid && cell.InBounds(map))
            {
                return new JobPlace(cell, RoomBearing(cell, job.targetA, map));
            }

            List<LocalTargetInfo> queue = job.targetQueueA;
            if (queue != null)
            {
                for (int i = 0; i < queue.Count; i++)
                {
                    IntVec3 queued = queue[i].HasThing ? queue[i].Thing.PositionHeld : queue[i].Cell;
                    if (queued.IsValid && queued.InBounds(map))
                    {
                        return JobPlace.At(queued);
                    }
                }
            }
            return JobPlace.Invalid;
        }

        /// <summary>The room cell of <see cref="TargetPlace"/>.</summary>
        internal static IntVec3 TargetCell(Job job, Map map)
        {
            return TargetPlace(job, map).RoomCell;
        }

        /// <summary>
        /// A cell that HAS a room, for a work target that does not.
        ///
        /// <para>A building's own cells stop belonging to any room once it is
        /// both impassable and full-fillage:
        /// <c>RegionTypeUtility.GetExpectedRegionType</c> returns
        /// <c>RegionType.None</c> for a non-walkable cell holding anything
        /// whose <c>Fillage</c> is <c>Full</c>, so no region covers it and
        /// <c>GetRoom</c> answers null. Every arm here reads the job's room
        /// from the target cell and gives up when it is null, so a work target
        /// built like that is invisible to this mod: pawns work at it and
        /// never change. Rimatomics' plutonium processor is one (impassable,
        /// fillPercent 1, 4x4), which is how two colonists supervised research
        /// at it in a room with two stands set to Research and nothing
        /// happened.</para>
        ///
        /// <para>The engine has no helper for this. <c>Thing.GetRoom</c> and
        /// <c>GetRoomOrAdjacent</c> both work from the thing's Position, and on
        /// a 4x4 solid building that centre cell's eight neighbours are still
        /// inside the building, so both answer null too. The ring around the
        /// whole occupied rect is what has to be walked.</para>
        ///
        /// <para>Resolved by COUNT first: the room owning most of the ring
        /// wins. At an equal count an enclosed room beats an outdoor one,
        /// because the outdoor room is one map-wide object and every stand
        /// this mod looks for lives in a room. Two enclosed rooms that still
        /// tie get no answer at all: this returns the roomless cell rather
        /// than guess. The engine has no opinion on such a building either,
        /// since one wedged into a wall carries no interaction cell.</para>
        ///
        /// <para>Nothing here reads a stand, and iteration order changes
        /// nothing, so both arms read the same room for the same job. That is
        /// required rather than tidy: make the answer depend on where a free
        /// stand happens to be and the two arms disagree, which is a pawn
        /// walking between the stand and the work forever.</para>
        ///
        /// <para>Only runs when the cheap answer is null, so the ordinary case
        /// costs one region lookup.</para>
        /// </summary>
        internal static IntVec3 RoomBearing(IntVec3 cell, LocalTargetInfo target, Map map)
        {
            if (cell.GetRoom(map) != null)
            {
                return cell;
            }
            Thing thing = target.HasThing ? target.Thing : null;
            if (thing == null || !thing.Spawned || thing.Map != map)
            {
                return cell;
            }

            // Count how much of the ring each room owns, keeping the first
            // cell seen for each. Iteration order decides nothing below: the
            // winner is settled by count and by indoor-ness, and a tie that
            // survives both refuses rather than picks.
            Dictionary<Room, int> counts = new Dictionary<Room, int>();
            Dictionary<Room, IntVec3> firstCell = new Dictionary<Room, IntVec3>();
            foreach (IntVec3 ring in GenAdj.CellsAdjacent8Way(thing))
            {
                if (!ring.InBounds(map))
                {
                    continue;
                }
                Room room = ring.GetRoom(map);
                if (room == null)
                {
                    continue;
                }
                int seen;
                if (!counts.TryGetValue(room, out seen))
                {
                    firstCell[room] = ring;
                }
                counts[room] = seen + 1;
            }

            int topCount = 0;
            int atTop = 0;
            Room leader = null;
            foreach (KeyValuePair<Room, int> entry in counts)
            {
                if (entry.Value > topCount)
                {
                    topCount = entry.Value;
                    atTop = 1;
                    leader = entry.Key;
                }
                else if (entry.Value == topCount)
                {
                    atTop++;
                }
            }

            if (atTop == 0)
            {
                return cell;
            }

            // The ordinary case, and the reason enclosure is not consulted
            // here: one room owns more of the ring than any other, so whether
            // it is enclosed cannot change the answer. Mining is the hot
            // caller — every mineable rock is impassable and full-fillage, so
            // every Mine job reaches this — and its ring is rock plus one
            // room. Testing enclosure anyway would spend a
            // PsychologicallyOutdoors on each of those for nothing.
            if (atTop == 1)
            {
                return firstCell[leader];
            }

            // A genuine tie, so now it is worth asking. An enclosed room beats
            // an outdoor one: the outdoor room is a single map-wide object,
            // and letting it take a tie hands the job to a "room" spanning the
            // whole colony. PsychologicallyOutdoors reads a per-room cached
            // count after its first call (Room.cs:557), and the count check
            // short-circuits ahead of it, so only the tied rooms are asked.
            Room pick = null;
            int enclosed = 0;
            foreach (KeyValuePair<Room, int> entry in counts)
            {
                if (entry.Value != topCount || entry.Key.PsychologicallyOutdoors)
                {
                    continue;
                }
                pick = entry.Key;
                enclosed++;
            }

            // Exactly one enclosed room among the tied ones wins. Otherwise
            // there is nothing left to separate them — two enclosed rooms, or
            // none at all — so refuse rather than guess. Both arms then read
            // null and agree, which is the conservative pair: no dressing, and
            // a pawn already dressed returns their gear.
            if (enclosed != 1)
            {
                return cell;
            }
            return firstCell[pick];
        }
    }
}
