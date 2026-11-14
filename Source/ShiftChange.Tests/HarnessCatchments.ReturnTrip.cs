using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    internal static partial class HarnessCatchments
    {
        /// <summary>Ten raw potatoes on a cell, for a haul.</summary>
        internal static Thing Potatoes(Map map, IntVec3 at)
        {
            Thing potatoes = ThingMaker.MakeThing(ThingDefOf.RawPotatoes);
            potatoes.stackCount = 10;
            return GenSpawn.Spawn(potatoes, at, map);
        }

        /// <summary>A cleaning run over the given filth, queued as the giver queues it.</summary>
        internal static Job Cleaning(WorkGiverDef giver, params Thing[] filth)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Clean);
            for (int i = 0; i < filth.Length; i++)
            {
                job.AddQueuedTarget(TargetIndex.A, filth[i]);
            }
            job.workGiverDef = giver;
            return job;
        }

    }
}
