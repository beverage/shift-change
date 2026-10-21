// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
#if HARNESS
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;

namespace ShiftChange
{
    /// <summary>
    /// Harness case for <see cref="Patch_OptimizeApparelOnShift"/> and
    /// <see cref="Patch_RecolorExecutionGuard"/>: on shift the optimizer's recolor never runs,
    /// the player's order from a styling station does, and no recolor reaches a parked garment.
    /// </summary>
    internal static class HarnessRecolor
    {
        /// <summary>
        /// On shift the styling station's order is taken while the optimizer's recolor is not,
        /// and a recolor naming a parked garment is refused, ordered or not. Driven through the
        /// giver, a job's reservations and <c>TryTakeOrderedJob</c> as the station calls it, each
        /// refusal beside a control that differs only in what the guard keys on. Guards the
        /// order being refused on shift, with a log warning. A known gap without Ideology, whose
        /// defs the job and the station are.
        /// </summary>
        internal static bool RecolorOrderGoesThroughOnShift(Fixture fix)
        {
            if (!ModsConfig.IdeologyActive || JobDefOf.RecolorApparel == null
                || ThingDefOf.StylingStation == null || ThingDefOf.Dye == null)
            {
                return ExpectKnownGap(false, "a recolor order goes through on shift",
                    "Ideology is not active on this mod list; add ludeon.rimworld.ideology to it");
            }
            Pawn pawn = fix.Pawn;
            Apparel uniform = fix.Comp.issuedUniform.Count > 0 ? fix.Comp.issuedUniform[0] : null;
            Apparel parked = HarnessScoring.ParkedGarment(fix);
            bool ok = Expect(CompShiftStand.OnShiftStandFor(pawn) == fix.Comp,
                             "the pawn is on shift (precondition)")
                    & Expect(uniform != null && pawn.apparel.WornApparel.Contains(uniform),
                             "and wears the stand's uniform (precondition)")
                    & Expect(parked != null && parked.ParentHolder is Building_OutfitStand,
                             "and their own garment is parked in the stand (precondition)");
            if (!ok)
            {
                return false;
            }

            // The station across the pad, its interaction cell free, and dye beside it.
            DebugTools_Fixtures.Spawn(fix.Map, ThingDefOf.StylingStation, ThingDefOf.WoodLog,
                fix.Stand.Position + new IntVec3(2, 0, 4), Rot4.North);
            Thing dye = ThingMaker.MakeThing(ThingDefOf.Dye);
            dye.stackCount = 10;
            GenSpawn.Spawn(dye, fix.Stand.Position + new IntVec3(4, 0, 2), fix.Map);
            uniform.DesiredColor = new Color(0.8f, 0.2f, 0.2f);
            ok &= Expect(JobGiver_OptimizeApparel.TryCreateRecolorJob(pawn, out _, dryRun: true),
                         "the station would offer Recolor apparel (precondition)");

            ok &= Expect(!OptimizerJob(pawn).IsValid,
                         "on shift the optimizer gives no job, though the uniform wants dyeing");
            ok &= Expect(RefusedAtReservations(pawn, RecolorJob(pawn, ordered: false, names: null)),
                         "a recolor job the player did not order is refused on shift");

            Job order = RecolorJob(pawn, ordered: false, names: null);
            ok &= Expect(order != null && order.GetTargetQueue(TargetIndex.B).Contains(uniform),
                         "the station's order names the uniform");
            if (order != null)
            {
                ok &= Expect(pawn.jobs.TryTakeOrderedJob(order, JobTag.Misc),
                             "the station's order is taken on shift")
                    & Expect(pawn.CurJobDef == JobDefOf.RecolorApparel,
                             "and the pawn starts it")
                    & Expect(pawn.apparel.WornApparel.Contains(uniform),
                             "still in uniform, since an order is never intercepted");
                pawn.jobs.StopAll();
            }
            ok &= Expect(RefusedAtReservations(pawn, RecolorJob(pawn, ordered: true, names: parked)),
                         "an order naming the parked garment is refused");

            CompShiftStand.OnShiftStands.Remove(pawn);
            ThinkResult offShift = OptimizerJob(pawn);
            ok &= Expect(offShift.IsValid && offShift.Job.def == JobDefOf.RecolorApparel
                         && Reserves(pawn, offShift.Job),
                         "off shift the optimizer's recolor is given and reserves (control)")
                & Expect(RefusedAtReservations(pawn, RecolorJob(pawn, ordered: false, names: parked)),
                         "and one naming the parked garment is still refused");
            return ok;
        }

        /// <summary>The optimizer's answer for this pawn now, its retry delay cleared.</summary>
        internal static ThinkResult OptimizerJob(Pawn pawn)
        {
            pawn.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame;
            return new JobGiver_OptimizeApparel().TryIssueJobPackage(pawn, default(JobIssueParams));
        }

        /// <summary>
        /// The recolor job the station builds, optionally marked as an order and pointed at one
        /// garment in place of the worn ones.
        /// </summary>
        internal static Job RecolorJob(Pawn pawn, bool ordered, Apparel names)
        {
            if (!JobGiver_OptimizeApparel.TryCreateRecolorJob(pawn, out Job job) || job == null)
            {
                return null;
            }
            job.playerForced = ordered;
            if (names != null)
            {
                List<LocalTargetInfo> queue = job.GetTargetQueue(TargetIndex.B);
                queue.Clear();
                queue.Add(names);
                job.count = 1;
            }
            return job;
        }

        /// <summary>Whether the job's reservations succeed, releasing whatever they took.</summary>
        internal static bool Reserves(Pawn pawn, Job job)
        {
            if (job == null)
            {
                return false;
            }
            bool reserved = job.TryMakePreToilReservations(pawn, errorOnFailed: false);
            pawn.ClearReservationsForJob(job);
            return reserved;
        }

        /// <summary>
        /// Whether a job was built and then failed its reservations. A job that could not be
        /// built is no refusal, so it does not count as one.
        /// </summary>
        internal static bool RefusedAtReservations(Pawn pawn, Job job)
        {
            return job != null && !Reserves(pawn, job);
        }
    }
}
#endif
