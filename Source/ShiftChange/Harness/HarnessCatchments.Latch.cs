// HARNESS only: dev tooling, compiled out of a plain Release build (the
// configuration table is in ShiftChange.csproj). The guard is whole-file,
// always, and check-invariants.py enforces it; why: docs/DESIGN.md,
// "Development tooling".
#if HARNESS
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    internal static partial class HarnessCatchments
    {
        /// <summary>
        /// THE CHANGE-BACK LATCH IS PER STAND AND TRIGGER. After Change back at
        /// a radius stand, neither it nor a pooled room stand beside it, with
        /// the same trigger, dresses the colonist. A recreation stand in the
        /// same room does, and so does a radius stand in the open whose ring
        /// does not reach the latched one. Lifting the latch by hand
        /// is the control for the refusal. One cell past the ring, beside a
        /// covered cell, still holds it, and a job boundary with no covered
        /// cell beside it releases it.
        /// </summary>
        internal static bool LatchHoldsItsStandAndTriggerOnly(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
            if (doctor == null || tend == null || chess == null)
            {
                return Expect(false, "the doctor and joy defs resolve");
            }
            if (fix.Outside.Area == 0)
            {
                return Expect(false, "the fixture has a door and open ground outside it");
            }
            fix.Comp.ToggleWork(doctor);
            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(4);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            CompShiftStand pooled = StageStand(fix, at + new IntVec3(0, 0, 2));
            CompShiftStand play = StageStand(fix, at + new IntVec3(0, 0, 4));
            // Three cells along the open strip from the door: a ring of the
            // smallest radius there reaches the work beside the door (3 cells)
            // and neither the latched stand (about 3.6) nor the room's work.
            CompShiftStand open = StageStand(fix, new IntVec3(fix.Outside.minX, 0, at.z + 3));
            if (pooled == null || play == null || open == null)
            {
                return Expect(false, "three more stands could be staged");
            }
            pooled.ToggleWork(doctor);
            play.ToggleRecreation();
            open.ToggleWork(doctor);
            // A room stand in the open serves nothing, so this one serves a ring.
            open.SetServes(CatchmentKind.Radius);
            open.SetServeRadius(CompShiftStand.MinServeRadius);

            IntVec3 inRoom = at + new IntVec3(2, 0, 2);
            IntVec3 joyAt = at + new IntVec3(3, 0, 3);
            IntVec3 doorside = new IntVec3(fix.Outside.minX, 0, at.z);
            IntVec3 pastRing = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 1);
            bool ok = Expect(fix.Comp.Covers(inRoom, fix.Map) && fix.Comp.Covers(doorside, fix.Map)
                             && !fix.Comp.Covers(pastRing, fix.Map),
                             "the ring covers the work in the room and beside the door, and not the far end outside (control)")
                    & Expect(pooled.serves == CatchmentKind.Room && pooled.Covers(at, fix.Map)
                             && !open.Covers(at, fix.Map),
                             "the pooled stand's room holds the latched stand, the open stand's ring does not (control)")
                    & Expect(open.Covers(doorside, fix.Map) && !open.Covers(inRoom, fix.Map),
                             "and that ring takes the work beside the door, not the room's (control)")
                    & Expect(RunSwap(fix), "dressed at the radius stand")
                    & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);

            Patch_ChangeBackGizmo.ChangeBack(fix.Pawn, fix.Comp);
            fix.Pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            ok &= Expect(RunSwap(fix), "Change back: they changed out")
                & Expect(!fix.Comp.OnShift && Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "and the latch is set (control)");
            Settle(fix.Pawn);

            ok &= Expect(!Diverts(fix, HarnessGates.DoctoringAt(tend, inRoom)),
                         "latched: work in the ring dresses them neither at the stand nor at the pooled stand beside it")
                & Expect(Diverts(fix, JobMaker.MakeJob(chess, joyAt, joyAt)),
                         "a recreation stand in the same room still does: the latch is the work trigger's")
                & Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, doorside)),
                         "and so does a radius stand in the open whose ring does not reach the latched stand")
                & Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "the latch holding all the while, since they never left the ring");

            Patch_JobInterception.ChangedBackAt.Remove(fix.Pawn);
            ok &= Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, inRoom)),
                         "with the latch lifted, the same work in the ring dresses them (control)");

            Patch_JobInterception.ChangedBackAt[fix.Pawn] = new Patch_JobInterception.ChangeBackLatch(fix.Stand);
            IntVec3 wasAt = fix.Pawn.Position;
            // One cell past the ring is beside a covered cell, where work at
            // the ring's edge is done from, so the pause there is not leaving.
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = pastRing;
            Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1));
            ok &= Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "the pause one cell past the ring, beside a covered cell, keeps the latch");
            IntVec3 farEnd = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ);
            bool farClear = System.Array.TrueForAll(GenAdj.AdjacentCellsAndInside,
                                                    o => !fix.Comp.Covers(farEnd + o, fix.Map));
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = farEnd;
            Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, farEnd));
            ok &= Expect(farClear && !Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "a job boundary at the far end, no covered cell beside it, releases the latch");
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            return ok;
        }

        /// <summary>
        /// AT A SLEEP RACK THE LATCH HOLDS THROUGH THE NIGHT AND LIFTS ON
        /// WAKING. After Change back, going to bed does not dress the colonist
        /// again, and the job tracker's pause does not count as waking; the
        /// first job after the lay-down releases the latch, and bed dresses
        /// them again, which is the control. A haul vanilla starts ahead of the
        /// lay-down, putting it back in the queue, is not the waking either.
        /// Released on lying down instead,
        /// the latch would undo the order at the very next boundary. A Change
        /// back that wakes a sleeper is not itself the waking: the latch holds
        /// through the job after it.
        /// </summary>
        internal static bool SleepRackLatchLiftsOnWaking(Fixture fix)
        {
            ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
            if (bedDef == null)
            {
                return Expect(false, "the bed def resolves");
            }
            // Ticked before the bed lands, as in RadiusSleepStandKeepsItOnlyForBed.
            fix.Comp.ToggleRest();
            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(3);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 inside = at + new IntVec3(1, 0, 2);
            Building_Bed bed = DebugTools_Fixtures.Spawn(
                fix.Map, bedDef, ThingDefOf.WoodLog, at + new IntVec3(2, 0, 0), Rot4.North) as Building_Bed;
            if (bed == null)
            {
                return Expect(false, "a bed spawns inside the ring");
            }

            bool ok = Expect(RunSwap(fix), "dressed for bed")
                    & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);

            Patch_ChangeBackGizmo.ChangeBack(fix.Pawn, fix.Comp);
            fix.Pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            ok &= Expect(RunSwap(fix), "Change back: they changed out of their sleepwear")
                & Expect(!fix.Comp.OnShift && Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "and the latch is set (control)");
            Settle(fix.Pawn);

            ok &= Expect(!Diverts(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed)),
                         "latched: going to bed does not dress them again")
                & Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "and lying down does not release the latch");

            // Vanilla's StartJob can put the lay-down back in the queue and
            // start a haul first (Pawn_JobTracker.TryOpportunisticJob).
            fix.Pawn.jobs.jobQueue.EnqueueFirst(JobMaker.MakeJob(JobDefOf.LayDown, bed));
            Diverts(fix, JobMaker.MakeJob(JobDefOf.HaulToCell, inside, inside));
            fix.Pawn.jobs.ClearQueuedJobs();
            ok &= Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "a haul vanilla starts ahead of the lay-down, which waits in the queue, is not the waking");

            IntVec3 wasAt = fix.Pawn.Position;
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = bed.Position;
            Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1));
            ok &= Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "the job tracker's pause between jobs is not the waking");
            Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, inside));
            ok &= Expect(!Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "WAKE-UP: the first job after the lay-down releases the latch");

            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            ok &= Expect(Diverts(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed)),
                         "so going to bed again dresses them (control)");

            // Pressed while they lie in bed: the press is what woke them.
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = bed.Position;
            Patch_JobInterception.ChangedBackAt[fix.Pawn] = new Patch_JobInterception.ChangeBackLatch(fix.Stand);
            Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, inside));
            ok &= Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "a Change back that woke them is not the waking: the latch holds through the job after it");
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            return ok;
        }

        /// <summary>
        /// AN AREA STAND'S LATCH HOLDS BESIDE THE STAND. Changing back leaves
        /// the colonist on a cell next to the stand, which the area need not
        /// hold, and a painted field or rock face is worked from the unpainted
        /// cell beside it. Neither is leaving the catchment: the pause after
        /// the swap, work in the field and the pause after that work all keep
        /// the latch, and the latched stand still refuses the work. Lifting
        /// the latch by hand is the
        /// control for the refusal, and a job on unpainted ground away from
        /// the stand releases it.
        /// </summary>
        internal static bool AreaLatchHoldsBesideItsStand(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            if (doctor == null || tend == null)
            {
                return Expect(false, "the doctor work defs resolve");
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            Map map = fix.Map;
            IntVec3 at = fix.Stand.Position;
            IntVec3 beside = at + new IntVec3(0, 0, 1);
            IntVec3 field = at + new IntVec3(2, 0, 3);
            IntVec3 edge = at + new IntVec3(2, 0, 2);
            IntVec3 away = at + new IntVec3(4, 0, 1);
            Area_Allowed area = NewArea(fix, "Shift harness field", at, field, at + new IntVec3(3, 0, 3));
            if (area == null)
            {
                return Expect(false, "the map has room for another allowed area");
            }
            fix.Comp.SetServeArea(area);
            bool ok = Expect(fix.Comp.serves == CatchmentKind.Area && fix.Comp.Covers(field, map),
                             "the stand serves an area holding it and a field (control)")
                    & Expect(!fix.Comp.Covers(beside, map) && !fix.Comp.Covers(edge, map)
                             && !fix.Comp.Covers(away, map),
                             "and not the cell beside the stand, the field's edge or the ground away from both (control)");

            IntVec3 wasAt = fix.Pawn.Position;
            Patch_JobInterception.ChangedBackAt[fix.Pawn] = new Patch_JobInterception.ChangeBackLatch(fix.Stand);
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = beside;
            Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1));
            ok &= Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "the pause after changing back, on the unpainted cell beside the stand, keeps the latch");

            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = edge;
            ok &= Expect(!Diverts(fix, HarnessGates.DoctoringAt(tend, field)),
                         "latched: work in the field, from the unpainted edge beside it, does not dress them")
                & Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "and keeps the latch");
            Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1));
            ok &= Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "and so does the job tracker's pause after that work, which names no place, on the same edge");
            Patch_JobInterception.ChangedBackAt.Remove(fix.Pawn);
            ok &= Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, field)),
                         "with the latch lifted, the same work dresses them (control)");
            Settle(fix.Pawn);

            Patch_JobInterception.ChangedBackAt[fix.Pawn] = new Patch_JobInterception.ChangeBackLatch(fix.Stand);
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = away;
            Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, away));
            ok &= Expect(!Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "a job on unpainted ground away from the stand releases the latch");
            Patch_JobInterception.ChangedBackAt.Remove(fix.Pawn);
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            return ok;
        }
    }
}
#endif
