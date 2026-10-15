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
        /// Pawn → the latch the change-back button set
        /// (<see cref="Patch_ChangeBackGizmo"/>). While it holds, the stand
        /// they changed back at does not dress them again, and nor does any
        /// stand with the same trigger whose catchment covers it
        /// (<see cref="LatchBlocks"/>). It drops when they are seen starting
        /// a job outside that stand's catchment, or waking, and drafting drops
        /// it outright.
        ///
        /// The STAND is stored, not its room, so the catchment is re-derived
        /// live on both sides of every comparison and a rebuilt wall cannot
        /// leave a stale Room object behind. Keyed by Pawn reference rather
        /// than thingIDNumber on purpose: an object reference cannot collide
        /// across a save load the way a recycled ID can, so a stale entry is
        /// inert rather than wrong. SessionGuard still clears it so old-game
        /// pawns don't leak. Why positional rather than a countdown, and why
        /// waking: docs/DESIGN.md, "Change back".
        /// </summary>
        internal static readonly Dictionary<Pawn, ChangeBackLatch> ChangedBackAt =
            new Dictionary<Pawn, ChangeBackLatch>();

        /// <summary>One press of Change back: the stand, and whether the pawn has lain down since.</summary>
        internal sealed class ChangeBackLatch
        {
            internal readonly Thing Stand;

            /// <summary>
            /// Set at the first lay-down after the press, so the first job
            /// after that is the waking. Counted from the press: a Change back
            /// that wakes a sleeper is not itself the waking.
            /// </summary>
            internal bool LaidDown;

            internal ChangeBackLatch(Thing stand)
            {
                Stand = stand;
            }
        }

        /// <summary>
        /// Drops the latch when the pawn starts a job outside the latched
        /// stand's catchment (<see cref="StillInside"/>), or wakes: starts the
        /// first job after a lay-down. Called from the interception for every
        /// job it decides on, so it samples at those job boundaries with no
        /// tick hook; drafting drops the latch outright
        /// (<see cref="Patch_DraftClearsDressBlock"/>). (A doorway is its own Room,
        /// <c>Room.IsDoorway</c>, so in room mode a pawn caught mid-door reads
        /// as "left": a hair early, and they are in fact leaving.)
        /// </summary>
        internal static void UpdateChangeBackLatch(Pawn pawn, Job job, JobClass c)
        {
            ChangeBackLatch latch;
            if (!ChangedBackAt.TryGetValue(pawn, out latch))
            {
                return;
            }
            CompShiftStand stand = latch.Stand != null && latch.Stand.Spawned
                ? latch.Stand.TryGetComp<CompShiftStand>()
                : null;
            if (stand == null || !StillInside(stand, pawn, job, c))
            {
                ChangedBackAt.Remove(pawn);
                return;
            }
            // The job tracker's pause between jobs finishes the last one, so
            // it neither lies down nor wakes anyone.
            if (c.Kind == JobKind.RidesAlong)
            {
                return;
            }
            if (c.LyingDown)
            {
                latch.LaidDown = true;
            }
            else if (latch.LaidDown && !LyingDownNext(pawn))
            {
                ChangedBackAt.Remove(pawn);
            }
        }

        /// <summary>
        /// The pawn has not left the latched stand's catchment. Room mode asks
        /// the room the pawn stands in. A radius or an area also counts a pawn
        /// standing next to a covered cell, and the place the new job names.
        /// Changing back leaves the pawn beside the stand, on a cell an area
        /// need not hold, and work at a painted rock face or field edge is
        /// done from the unpainted cell beside it, before the job and in the
        /// job tracker's pause after it, which names no place. None of that is
        /// leaving.
        /// </summary>
        internal static bool StillInside(CompShiftStand stand, Pawn pawn, Job job, JobClass c)
        {
            Map map = pawn.MapHeld;
            if (stand.Covers(pawn.Position, map))
            {
                return true;
            }
            if (stand.serves == CatchmentKind.Room || map == null || stand.parent.Map != map)
            {
                return false;
            }
            IntVec3[] around = GenAdj.AdjacentCellsAndInside;
            for (int i = 0; i < around.Length; i++)
            {
                if (stand.Covers(pawn.Position + around[i], map))
                {
                    return true;
                }
            }
            JobPlace place = SharedTargetPlace(job, c, map);
            return place.IsValid && stand.Covers(place, map);
        }

        /// <summary>
        /// The pawn's next queued job is a lay-down: vanilla slipped a haul in
        /// ahead of it (<c>Pawn_JobTracker.TryOpportunisticJob</c>), so the
        /// job starting now is not the waking.
        /// </summary>
        internal static bool LyingDownNext(Pawn pawn)
        {
            JobQueue queue = pawn.jobs?.jobQueue;
            if (queue == null || queue.Count == 0)
            {
                return false;
            }
            QueuedJob head = queue.Peek();
            return head?.job != null && Classify(pawn, head.job, head.tag).LyingDown;
        }

        /// <summary>
        /// The latch keeps <paramref name="candidate"/> from dressing this pawn
        /// for <paramref name="trigger"/>: it is the stand they changed back
        /// at, or one with the same trigger whose catchment covers that stand.
        /// Asked per candidate, by the stand search and the catch-up, so every
        /// other stand dresses them normally: a stand elsewhere holds a
        /// different uniform, and so does a recreation stand beside a work
        /// stand.
        /// </summary>
        internal static bool LatchBlocks(Pawn pawn, CompShiftStand candidate, StandTrigger trigger)
        {
            ChangeBackLatch latch;
            if (!ChangedBackAt.TryGetValue(pawn, out latch))
            {
                return false;
            }
            Thing latched = latch.Stand;
            if (latched == null || !latched.Spawned)
            {
                return false;
            }
            if (candidate.parent == latched)
            {
                return true;
            }
            CompShiftStand latchedStand = latched.TryGetComp<CompShiftStand>();
            return latchedStand != null && ServesTrigger(latchedStand, trigger)
                   && candidate.Covers(latched.Position, latched.Map);
        }

        /// <summary>
        /// The job each pawn was last dressed for, deferred behind the swap.
        /// The return trip lets that one job through, though its target may
        /// have left the catchment while they changed, as an animal can
        /// (docs/DESIGN.md, "Catchments"), and keeps the uniform on for an
        /// errand vanilla puts in ahead of it (<see cref="QueuedAheadOfServedWork"/>).
        /// Dropped at the first decided job boundary that is neither that job
        /// nor one ahead of it in the queue (<see cref="TakeDressedFor"/>), and
        /// cleared with the session. Keyed by Pawn for the reason
        /// <see cref="ChangedBackAt"/> gives.
        /// </summary>
        internal static readonly Dictionary<Pawn, DeferredJob> DressedFor = new Dictionary<Pawn, DeferredJob>();

        /// <summary>
        /// A deferred job, with its load id kept beside it: jobs are pooled, so
        /// a recycled Job object must not pass for the one deferred, and
        /// <c>JobMaker.MakeJob</c> gives every job it hands out a new id. The
        /// def and first target, kept here before, let through a recycled
        /// object handed the same def and stack, which is the next delivery
        /// from one pile of steel.
        /// </summary>
        internal struct DeferredJob
        {
            internal readonly Job Job;
            internal readonly int LoadID;

            internal DeferredJob(Job job)
            {
                Job = job;
                LoadID = job.loadID;
            }

            internal bool Is(Job job)
            {
                return job != null && job == Job && job.loadID == LoadID;
            }
        }

        /// <summary>
        /// Whether <paramref name="job"/> is the job the pawn's
        /// <see cref="DressedFor"/> record names. The record stays while that
        /// job is starting or still waits at the head of the queue: vanilla's
        /// <c>StartJob</c> can put it back there and start a haul first
        /// (<c>Pawn_JobTracker.TryOpportunisticJob</c>), and the job then
        /// starts again. Any other job drops it.
        /// </summary>
        internal static bool TakeDressedFor(Pawn pawn, Job job)
        {
            DeferredJob deferred;
            if (!DressedFor.TryGetValue(pawn, out deferred))
            {
                return false;
            }
            if (deferred.Is(job))
            {
                return true;
            }
            JobQueue queue = pawn.jobs?.jobQueue;
            if (queue == null || queue.Count == 0 || !deferred.Is(queue.Peek()?.job))
            {
                DressedFor.Remove(pawn);
            }
            return false;
        }
    }
}
