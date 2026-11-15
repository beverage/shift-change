using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.GameDefs;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Jobs for the tests of what the uniform rides along on (<see cref="RidingAlong"/>): errands
    /// that are part of work already under way, where changing out and back in again costs two
    /// wardrobe walks and buys nothing.
    ///
    /// <para>The return trip judges a job by where its first target stands, which for a meal or an
    /// ingredient is the storeroom, so each of these would otherwise send a colonist who is about
    /// to come straight back to the stand to change first.</para>
    /// </summary>
    internal static class RideAlongKit
    {
        /// <summary>
        /// Five of vanilla's seven feeding givers; <see cref="DeliverFoodGivers"/> has the other
        /// two. Each builds its job with the food in targetA and the patient or prisoner in
        /// targetB. The hemogen givers are Biotech's and resolve only with it loaded.
        /// </summary>
        internal static readonly string[] FeedPatientGivers =
        {
            "DoctorFeedHumanlikes", "DoctorFeedAnimals", "HandlingFeedPatientAnimals", "FeedPrisoner", "FeedHemogen",
        };

        internal static readonly string[] DeliverFoodGivers =
        {
            "DeliverFoodToPrisoner", "DeliverHemogenToPrisoner",
        };

        /// <summary>A feeding job, built the way its giver builds one.</summary>
        internal static Job FeedingJob(JobDef def, Thing food, Pawn patient, WorkGiverDef giver)
        {
            Job job = JobMaker.MakeJob(def, food, patient);
            job.count = 1;
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>
        /// <paramref name="also"/> and the work type of each feeding giver that resolves, once
        /// each: Doctor, Handling and Warden in vanilla. Complex Jobs moves several givers to work
        /// of its own, so a stand ticked for the vanilla names would not serve them.
        /// </summary>
        internal static List<WorkTypeDef> FeedingWork(WorkTypeDef also)
        {
            List<WorkTypeDef> works = new List<WorkTypeDef> { also };
            foreach (string name in FeedPatientGivers.Concat(DeliverFoodGivers))
            {
                WorkTypeDef work = Giver(name)?.workType;
                if (work != null && !works.Contains(work))
                {
                    works.Add(work);
                }
            }
            return works;
        }

        /// <summary>
        /// A job of the given giver at a cell, for the queue. Never started: it only has to be what
        /// sits at the head of the queue when the errand in front of it arrives.
        /// </summary>
        internal static Job QueuedWork(WorkGiverDef giver, IntVec3 at)
        {
            Job job = JobMaker.MakeJob(JobDefOf.DoBill, at);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>A vanilla haul with no giver, as an opportunistic hauler builds one.</summary>
        internal static Job GiverlessHaul(Thing thing, IntVec3 to)
        {
            Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, thing, to);
            job.count = thing.stackCount;
            job.haulMode = HaulMode.ToCellStorage;
            return job;
        }

        /// <summary>
        /// Start the queue's head the way the engine does: end whatever the pawn is doing and let
        /// the think tree's queue node hand it out, which runs every <c>StartJob</c> prefix, ours
        /// and any other mod's, in Harmony's order, which need not be the load order. Returns the
        /// def the pawn ended up on, then leaves the pawn idle and the queue empty.
        /// </summary>
        internal static JobDef StartFromQueue(Scene scene, Job job, JobTag? tag)
        {
            Pawn pawn = scene.Colonist;
            pawn.jobs.ClearQueuedJobs();
            pawn.jobs.jobQueue.EnqueueFirst(job, tag);
            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: true);
            JobDef started = pawn.CurJobDef;
            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            pawn.jobs.ClearQueuedJobs();
            Patch_JobInterception.LastBlockedTick.Remove(pawn.thingIDNumber);
            return started;
        }

        /// <summary>The job's first target or its first queued one is this thing.</summary>
        internal static bool Hauls(Job job, Thing thing)
        {
            if (job == null || thing == null)
            {
                return false;
            }
            if (job.targetA.Thing == thing)
            {
                return true;
            }
            return job.targetQueueA != null && job.targetQueueA.Any(t => t.Thing == thing);
        }

        /// <summary>A Pick Up And Haul haul, shaped as their giver and driver build one.</summary>
        internal static Job HaulToInventoryJob(JobDef def, Thing thing, IntVec3 storeCell)
        {
            Job job = JobMaker.MakeJob(def, LocalTargetInfo.Invalid, storeCell);
            job.targetQueueA = new List<LocalTargetInfo> { thing };
            job.targetQueueB = new List<LocalTargetInfo> { storeCell };
            job.countQueue = new List<int> { thing.stackCount };
            return job;
        }
    }
}
