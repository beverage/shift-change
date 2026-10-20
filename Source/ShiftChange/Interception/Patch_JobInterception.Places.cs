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
        /// Whether every place the job names, its three targets and both queues,
        /// is inside the stand's catchment. A place that cannot be read is
        /// skipped, and a job that names none is judged where the pawn stands.
        /// Judging every place, not just the target, sends a pawn back before a
        /// haul to a freezer outside, while the stand is still near.
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
        /// Whether one place a job names is inside the catchment; a place that
        /// cannot be read counts as covered. Sets <paramref name="named"/> when the
        /// place is readable, on this map or another.
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
                    // Outside the catchment whatever its coordinates read.
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

            /// <summary>A cell on this map, read into a JobPlace.</summary>
            Here,
        }

        /// <summary>
        /// Reads one place a job names: a thing at the cell where it is held, and
        /// a solid place at its own cell with the room cell beside it
        /// (<see cref="RoomBearing"/>). <see cref="PlaceCovered"/> and
        /// <see cref="AddAirTarget"/> both read through this, so the catchment
        /// checks and the vacuum walk cannot disagree about where a job goes.
        /// It reads a solid place as <see cref="TargetPlace"/> reads the job's
        /// own location, through <see cref="RoomBearing"/>, so a wall's repair is
        /// inside or outside by the same rule whichever check asks.
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
        /// Where the job happens: <c>targetA</c>, else the first readable entry
        /// of <c>targetQueueA</c>. A job built from a target queue, such as
        /// vanilla cleaning or a Pick Up And Haul haul, leaves targetA empty. The
        /// jobs in <see cref="JobRoomTargets.RoomIsTargetB"/> answer from targetB
        /// first.
        /// </summary>
        internal static JobPlace TargetPlace(Job job, Map map)
        {
            // A carried thing's PositionHeld is its carrier's cell, so once the pawn
            // has picked the thing up this answers where the pawn stands, not where
            // the thing lay. The arms ask at a job boundary, before the pickup; the
            // catch-up sweep can ask mid-carry, where the carrier's room is the one
            // a freed stand would have to be in to match anyway.
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
        /// A cell that has a room, for a work target that does not.
        ///
        /// <para>A non-walkable cell holding a full-fillage thing is in no region
        /// (<c>RegionTypeUtility.GetExpectedRegionType</c> returns
        /// <c>RegionType.None</c>), so <c>GetRoom</c> answers null on the cells of
        /// a building that is both impassable and full-fillage. The arms read a
        /// job's room from its target cell and give up on null, so a solid work
        /// target, such as Rimatomics' 4x4 plutonium processor, would be invisible
        /// to this mod: pawns work at it and never change.</para>
        ///
        /// <para>The engine has no helper for this. <c>Thing.GetRoom</c> and
        /// <c>GetRoomOrAdjacent</c> both work from the thing's Position, and on a
        /// 4x4 solid building that centre cell's eight neighbours are still
        /// inside it, so both answer null too. A building set into a wall has no
        /// interaction cell either. So the ring around the whole occupied rect
        /// is walked, and the room owning most of it wins.</para>
        ///
        /// <para>The answer must not depend on a stand or on iteration order: the
        /// dressing and change-back arms share it and must read the same room for
        /// the same job, or a pawn walks between the stand and the work
        /// forever.</para>
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

            // One room owns more of the ring than any other, so enclosure cannot
            // change the answer and is not tested. Mine jobs are the hot caller:
            // every mineable rock is impassable and full-fillage, and its ring is
            // rock plus one room, so testing would cost a PsychologicallyOutdoors
            // call on each.
            if (atTop == 1)
            {
                return firstCell[leader];
            }

            // An enclosed room beats an outdoor one: the outdoor room is a single
            // map-wide object, so letting it win a tie would hand the job to the
            // whole colony. PsychologicallyOutdoors is cheap after its first call,
            // since each of the room's districts caches its open-roof count, and
            // the count check short-circuits ahead of it, so only the tied rooms
            // are asked.
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

            // With two enclosed rooms, or none, nothing separates the tied rooms, so
            // return the roomless cell rather than guess. Both arms then read no room
            // and agree: no dressing, and a pawn already dressed returns their gear.
            if (enclosed != 1)
            {
                return cell;
            }
            return firstCell[pick];
        }
    }
}
