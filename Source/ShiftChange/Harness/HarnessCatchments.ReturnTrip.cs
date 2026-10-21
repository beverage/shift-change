// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
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
        /// Staying dressed at a radius work stand: inside its ring, the uniform stays on for the
        /// stand's own work, for pass-through work, and for idling and wandering, and for nothing
        /// else. Past the ring nothing keeps it, in the stand's own room included. Room mode
        /// keeping it for anything in the room is the control, and each job kept is paired with
        /// one of the same shape that changes them back, so a probe failing for its own reasons
        /// cannot pass as staying dressed.
        /// </summary>
        internal static bool RadiusWorkStandKeepsTheUniformInItsRing(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            WorkGiverDef cook = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsCook");
            WorkGiverDef haul = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulGeneral");
            WorkGiverDef clean = DefDatabase<WorkGiverDef>.GetNamedSilentFail("CleanFilth");
            WorkGiverDef construct = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructFinishFrames");
            JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
            ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
            if (doctor == null || tend == null || cook == null || haul == null || clean == null
                || construct == null || chess == null || bedDef == null)
            {
                return Expect(false, "the work, joy and bed defs resolve");
            }
            // Ticked before the bed lands: a bed makes the pad a bedroom, which an automatic stand
            // would read as sleep.
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 inside = at + new IntVec3(1, 0, 2);
            IntVec3 past = at + new IntVec3(4, 0, 4);
            Building_Bed bed = DebugTools_Fixtures.Spawn(
                fix.Map, bedDef, ThingDefOf.WoodLog, at + new IntVec3(2, 0, 0), Rot4.North) as Building_Bed;
            if (bed == null)
            {
                return Expect(false, "a bed spawns in the stand's room");
            }

            bool ok = Expect(RunSwap(fix), "dressed for the shift")
                    & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(!Probe(fix, WorkAt(cook, past)),
                         "room mode: work the stand does not serve, anywhere in its room, keeps the uniform on (control)")
                & Expect(!Probe(fix, JobMaker.MakeJob(chess, inside, inside)),
                         "and so does recreation in the room, as it always has (control)");

            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(3);
            ok &= Expect(fix.Comp.Covers(inside, fix.Map) && fix.Comp.Covers(bed.Position, fix.Map)
                         && !fix.Comp.Covers(past, fix.Map),
                         "radius 3 covers the work cell and the bed, not the room's far corner (control)")
                & Expect(!Probe(fix, WorkAt(tend, inside)),
                         "Doctor work inside the ring keeps the uniform on")
                & Expect(Probe(fix, WorkAt(tend, past)),
                         "Doctor work past the ring, in the stand's own room, changes them back")
                & Expect(Probe(fix, WorkAt(cook, inside)),
                         "cooking inside the ring changes them back: the stand does not serve it")
                & Expect(!Probe(fix, WorkAt(haul, inside)),
                         "hauling inside the ring keeps the uniform on: pass-through work")
                & Expect(!Probe(fix, WorkAt(clean, inside)), "and so does cleaning")
                & Expect(!Probe(fix, WorkAt(construct, inside)), "and constructing")
                & Expect(Probe(fix, WorkAt(haul, past)), "hauling past the ring changes them back")
                & Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.GotoWander, inside)),
                         "wandering inside the ring keeps the uniform on")
                & Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.GotoWander, past)),
                         "wandering past it changes them back")
                & Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.Wait)),
                         "idling where they stand, inside the ring, keeps the uniform on")
                & Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, inside)),
                         "sleeping on the ground inside the ring changes them back: it is not on the list")
                & Expect(Probe(fix, JobMaker.MakeJob(chess, inside, inside)),
                         "and so does recreation inside it")
                & Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed)),
                         "and going to bed inside it");

            // A wait goes nowhere, so it is judged where the colonist stands.
            IntVec3 wasAt = fix.Pawn.Position;
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = past;
            ok &= Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.Wait)),
                         "idling past the ring changes them back");
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            return ok & Expect(fix.Comp.OnShift, "and they are still on shift at the end");
        }

        /// <summary>
        /// Pass-through work is judged at both ends. A haul with its item and its destination
        /// inside the ring keeps the uniform on; with either end past it, the colonist changes
        /// back before the haul, while the stand is near. A cleaning run's queue is judged the
        /// same way. An errand with no giver, queued ahead of the stand's own work inside the
        /// ring, rides along whatever its ends (docs/DESIGN.md, "Errands queued ahead of work").
        /// Room mode keeping the outbound haul, which never leaves the room, is the control.
        /// </summary>
        internal static bool PassThroughWorkIsJudgedAtBothEnds(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            WorkGiverDef haul = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulGeneral");
            WorkGiverDef clean = DefDatabase<WorkGiverDef>.GetNamedSilentFail("CleanFilth");
            if (doctor == null || tend == null || haul == null || clean == null
                || ThingDefOf.RawPotatoes == null || ThingDefOf.Filth_Dirt == null)
            {
                return Expect(false, "the work, potato and dirt defs resolve");
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            Map map = fix.Map;
            IntVec3 at = fix.Stand.Position;
            IntVec3 from = at + new IntVec3(1, 0, 2);
            IntVec3 to = at + new IntVec3(0, 0, 3);
            IntVec3 past = at + new IntVec3(4, 0, 4);
            Thing near = Potatoes(map, from);
            Thing far = Potatoes(map, at + new IntVec3(4, 0, 2));
            Thing dirtHere = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Filth_Dirt),
                                            at + new IntVec3(2, 0, 1), map);
            Thing dirtThere = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Filth_Dirt),
                                             at + new IntVec3(0, 0, 2), map);
            Thing dirtPast = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Filth_Dirt), past, map);

            bool ok = Expect(RunSwap(fix), "dressed for the shift")
                    & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(!Probe(fix, Haul(haul, near, past)),
                         "room mode: a haul to the room's far corner keeps the uniform on (control)");

            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(3);
            ok &= Expect(fix.Comp.Covers(from, map) && fix.Comp.Covers(to, map)
                         && fix.Comp.Covers(dirtHere.Position, map) && fix.Comp.Covers(dirtThere.Position, map)
                         && !fix.Comp.Covers(far.Position, map) && !fix.Comp.Covers(past, map),
                         "radius 3 covers the near item, the dirt and the cell on the ring, not the far corner (control)")
                & Expect(!Probe(fix, Haul(haul, near, to)),
                         "a haul from inside the ring to a cell on it keeps the uniform on")
                & Expect(Probe(fix, Haul(haul, near, past)),
                         "the same haul to a cell past the ring changes them back first")
                & Expect(Probe(fix, Haul(haul, far, to)),
                         "and so does one bringing an item in from past the ring")
                & Expect(!Probe(fix, Cleaning(clean, dirtHere, dirtThere)),
                         "cleaning with every queued filth inside the ring keeps the uniform on")
                & Expect(Probe(fix, Cleaning(clean, dirtHere, dirtPast)),
                         "one queued filth past the ring changes them back");

            JobQueue queue = fix.Pawn.jobs.jobQueue;
            queue.EnqueueFirst(HarnessRideAlong.QueuedWork(tend, from));
            ok &= Expect(!Probe(fix, HarnessRideAlong.GiverlessHaul(far, to)),
                         "an errand with no giver, ahead of the stand's own work inside the ring, keeps it on");
            queue.EnqueueFirst(HarnessRideAlong.QueuedWork(tend, past));
            ok &= Expect(Probe(fix, HarnessRideAlong.GiverlessHaul(far, to)),
                         "ahead of its work past the ring, it changes them back (control)");
            return ok & Expect(fix.Comp.OnShift, "and they are still on shift at the end");
        }

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

        /// <summary>
        /// Staying dressed at a radius sleep stand: the first job out of bed changes the colonist
        /// back, wherever it is, a haul inside the ring included. Going to a bed inside the ring
        /// keeps the pyjamas on, since the stand would dress them for it; a bed past the ring
        /// does not, and nor does bed rest for medical reasons, which is the work arm's. The same
        /// haul at a work stand keeping the uniform on is the control.
        /// </summary>
        internal static bool RadiusSleepStandKeepsItOnlyForBed(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef haul = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulGeneral");
            ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
            if (doctor == null || haul == null || bedDef == null)
            {
                return Expect(false, "the work and bed defs resolve");
            }
            // Ticked before the beds land: a bed makes the pad a bedroom, and on an automatic
            // stand ToggleRest would then turn sleep off.
            fix.Comp.ToggleRest();
            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(3);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 inside = at + new IntVec3(1, 0, 2);
            Building_Bed near = DebugTools_Fixtures.Spawn(
                fix.Map, bedDef, ThingDefOf.WoodLog, at + new IntVec3(2, 0, 0), Rot4.North) as Building_Bed;
            Building_Bed far = DebugTools_Fixtures.Spawn(
                fix.Map, bedDef, ThingDefOf.WoodLog, at + new IntVec3(4, 0, 3), Rot4.North) as Building_Bed;
            if (near == null || far == null)
            {
                return Expect(false, "two beds spawn in the stand's room");
            }

            bool ok = Expect(fix.Comp.HandlesRest() && fix.Comp.serves == CatchmentKind.Radius,
                             "the stand serves sleep within a radius (control)")
                    & Expect(fix.Comp.Covers(near.Position, fix.Map) && !fix.Comp.Covers(far.Position, fix.Map),
                             "its ring covers one bed and not the other (control)")
                    & Expect(RunSwap(fix), "dressed for bed")
                    & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, near)),
                         "going to a bed inside the ring keeps the pyjamas on")
                & Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, far)),
                         "a bed past the ring changes them back")
                & Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, near), JobTag.RestingForMedicalReasons),
                         "and so does bed rest for medical reasons, inside the ring or not");

            IntVec3 wasAt = fix.Pawn.Position;
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = near.Position;
            ok &= Expect(Probe(fix, WorkAt(haul, inside)),
                         "WAKE-UP: the first job out of bed changes them back, a haul inside the ring included")
                & Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.GotoWander, inside)),
                         "and so does wandering inside it")
                & Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, near)),
                         "while a job that keeps them in bed does not (control)");

            fix.Comp.ToggleWork(doctor);
            ok &= Expect(fix.Comp.HandlesWork(doctor) && fix.Comp.serves == CatchmentKind.Radius
                         && fix.Comp.OnShift,
                         "the same stand now serves Doctor work in its ring, the colonist still on shift (control)")
                & Expect(!Probe(fix, WorkAt(haul, inside)),
                         "and the same haul keeps the uniform on at a work stand (control)");
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            return ok;
        }

        /// <summary>
        /// Staying dressed at a radius recreation stand: inside its ring, recreation, idling and
        /// wandering keep the outfit on, and pass-through work does not, since that is a work
        /// stand's. A swimmer wanders between pools in swimwear, but changes before hauling. The
        /// same haul at a work stand keeping the uniform on is the control.
        /// </summary>
        internal static bool RadiusRecreationStandKeepsItForPlay(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef haul = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulGeneral");
            JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
            if (doctor == null || haul == null || chess == null)
            {
                return Expect(false, "the work and joy defs resolve");
            }
            fix.Comp.ToggleRecreation();
            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(3);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 inside = at + new IntVec3(1, 0, 2);
            IntVec3 past = at + new IntVec3(4, 0, 4);
            bool ok = Expect(fix.Comp.HandlesRecreation() && fix.Comp.serves == CatchmentKind.Radius,
                             "the stand serves recreation within a radius (control)")
                    & Expect(RunSwap(fix), "dressed for play")
                    & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(!Probe(fix, JobMaker.MakeJob(chess, inside, inside)),
                         "recreation inside the ring keeps the outfit on")
                & Expect(Probe(fix, JobMaker.MakeJob(chess, past, past)),
                         "recreation past the ring changes them back")
                & Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.GotoWander, inside)),
                         "wandering inside the ring keeps it on")
                & Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.Wait)),
                         "and so does idling where they stand")
                & Expect(Probe(fix, WorkAt(haul, inside)),
                         "hauling inside the ring changes them back: pass-through work is a work stand's");

            fix.Comp.ToggleWork(doctor);
            ok &= Expect(fix.Comp.HandlesWork(doctor) && fix.Comp.serves == CatchmentKind.Radius
                         && fix.Comp.OnShift,
                         "the same stand now serves Doctor work in its ring, the colonist still on shift (control)")
                & Expect(!Probe(fix, WorkAt(haul, inside)),
                         "and the same haul keeps the uniform on at a work stand (control)");
            return ok;
        }

        /// <summary>
        /// The tables a radius stand stays dressed by. Pass-through work is read from work tags,
        /// so a modded work type carrying the Hauling tag under a name of its own passes through,
        /// and one named Hauling without the tag does not. The incidental list's six rows all
        /// resolve, so none is a typo that can never match, and each counts only without a giver.
        /// </summary>
        internal static bool PassThroughAndIncidentalTablesHold()
        {
            bool ok = true;
            foreach (string name in new[] { "Hauling", "Cleaning", "Construction", "Firefighter" })
            {
                WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail(name);
                ok &= Expect(work != null && JobRoomTargets.PassesThrough(work),
                             name + " is pass-through work");
            }
            foreach (string name in new[] { "Doctor", "Cooking", "BasicWorker", "PlantCutting", "Growing", "Mining" })
            {
                WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail(name);
                ok &= Expect(work != null && !JobRoomTargets.PassesThrough(work),
                             name + " is not");
            }
            WorkTypeDef tagged = new WorkTypeDef
            {
                defName = "Harness_FSFHauling",
                workTags = WorkTags.ManualDumb | WorkTags.Hauling,
            };
            WorkTypeDef untagged = new WorkTypeDef { defName = "Hauling", workTags = WorkTags.ManualDumb };
            ok &= Expect(JobRoomTargets.PassesThrough(tagged),
                         "a modded work type carrying the Hauling tag passes through, whatever its name")
                & Expect(!JobRoomTargets.PassesThrough(untagged),
                         "and one named Hauling without the tag does not: the tag is the test")
                & Expect(!JobRoomTargets.PassesThrough(null), "nor does no work type");

            WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulGeneral");
            ok &= Expect(JobRoomTargets.IncidentalJobs.Count == 6, "the incidental list has its six rows")
                & Expect(giver != null, "the hauling giver resolves");
            foreach (string name in JobRoomTargets.IncidentalJobs)
            {
                JobDef def = DefDatabase<JobDef>.GetNamedSilentFail(name);
                if (!Expect(def != null, name + " resolves, so the row can match"))
                {
                    ok = false;
                    continue;
                }
                Job job = JobMaker.MakeJob(def);
                ok &= Expect(JobRoomTargets.IsIncidental(job), name + " with no giver is incidental");
                job.workGiverDef = giver;
                ok &= Expect(!JobRoomTargets.IsIncidental(job), "and with a giver is not");
            }
            foreach (JobDef def in new[] { JobDefOf.LayDown, JobDefOf.Ingest, JobDefOf.HaulToCell,
                                           JobDefOf.Wait_Combat })
            {
                ok &= Expect(!JobRoomTargets.IsIncidental(JobMaker.MakeJob(def)),
                             def.defName + " is not incidental");
            }
            return ok;
        }

        /// <summary>
        /// The job a colonist was just dressed for never changes them back, though its target
        /// leaves the catchment while they change: a patient here, standing in for an animal
        /// walking out of an unwalled ring or a cow out of the barn. Dressed through the work
        /// arm's own <c>Insert</c>, and the deferred job started by the engine's own queue, so
        /// the job tracker's pause between the two is on the path and must not spend the grace.
        /// Vanilla can start a haul first and put the job back in the queue: that haul keeps the
        /// uniform on, and so does the job starting again. The next job out there changes them
        /// back, which is the control: the grace lasts one job. Once for a radius stand, then for
        /// a room stand whose patient leaves the room.
        /// </summary>
        internal static bool JustDressedForNeverSendsThemBack(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            if (doctor == null || tend == null)
            {
                return Expect(false, "the doctor work defs resolve");
            }
            if (fix.Outside.Area == 0)
            {
                return Expect(false, "the fixture has a door and open ground outside it");
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);
            Pawn patient = SpawnExtra(fix, Gender.Female, "Patient");
            IntVec3 patientAt = patient.Position;

            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(3);
            bool ok = Expect(fix.Comp.Covers(patientAt, fix.Map), "the patient is inside the ring (control)")
                    & DressedForThenMoved(fix, tend, patient, fix.Stand.Position + new IntVec3(4, 0, 4),
                                          "radius: the patient walks past the ring");

            // Changed back while the stand still serves its radius, so the freed stand catches
            // nobody up on the way to room mode.
            ok &= Expect(RunSwap(fix), "changed back between the passes")
                & Expect(!fix.Comp.OnShift, "and off shift (control)");
            Settle(fix.Pawn);
            patient.pather?.StopDead();
            patient.Position = patientAt;

            fix.Comp.SetServes(CatchmentKind.Room);
            IntVec3 outside = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ);
            ok &= Expect(fix.Comp.Covers(patientAt, fix.Map) && !fix.Comp.Covers(outside, fix.Map),
                         "room mode covers the patient and not the ground outside (control)")
                & DressedForThenMoved(fix, tend, patient, outside, "room: the patient walks out of the room");
            return ok;
        }

        /// <summary>
        /// One pass of <see cref="JustDressedForNeverSendsThemBack"/>: dress for tending the
        /// patient, move the patient to <paramref name="away"/> while the colonist changes, then
        /// let the engine start the tending.
        /// </summary>
        internal static bool DressedForThenMoved(Fixture fix, WorkGiverDef tend, Pawn patient,
                                                 IntVec3 away, string pass)
        {
            Pawn pawn = fix.Pawn;
            Job tending = JobMaker.MakeJob(JobDefOf.Wait, patient);
            tending.workGiverDef = tend;
            JobQueue queue = pawn.jobs.jobQueue;
            bool inserted = Patch_JobInterception.TryInsertSwap(tending, null, pawn, pawn.jobs);
            bool ok = Expect(inserted && pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand
                             && queue.Count > 0 && queue[0].job == tending,
                             pass + ": the work arm sends them to dress, the tending deferred behind the swap");
            // RunSwap's own swap takes over from the inserted one, and the queue keeps the
            // tending: the swap driver never touches it.
            ok &= Expect(RunSwap(fix), "they dressed")
                & Expect(fix.Comp.OnShift && queue.Count > 0 && queue[0].job == tending,
                         "on shift, with the tending still queued (control)");

            patient.pather?.StopDead();
            patient.Position = away;
            ok &= Expect(!fix.Comp.Covers(away, fix.Map), "the patient walked away while they changed (control)")
                & Expect(pawn.CurJobDef == JobDefOf.Wait_MaintainPosture,
                         "the job tracker paused between the swap and the queue (control: the pause is on the path)");

            // The pause ends as the tracker ends it at expiry, and the queue starts the tending
            // through StartJob, this mod's prefix included.
            pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
            ok &= Expect(pawn.CurJob == tending,
                         "the tending they dressed for starts (" + (pawn.CurJobDef?.defName ?? "no job") + ")")
                & Expect(fix.Comp.OnShift, "in uniform: the job they were dressed for does not send them back");

            // Vanilla's StartJob can then put the tending back at the head of the queue,
            // untagged, and start a haul first (Pawn_JobTracker.TryOpportunisticJob).
            queue.EnqueueFirst(tending);
            ok &= Expect(!Diverts(fix, JobMaker.MakeJob(JobDefOf.HaulToCell, away, away)),
                         "a haul vanilla starts first, the tending queued behind it, keeps the uniform on")
                & Expect(!Diverts(fix, tending),
                         "and the tending, starting again after the haul, still does");
            queue.Extract(tending);

            Job again = JobMaker.MakeJob(JobDefOf.Wait, patient);
            again.workGiverDef = tend;
            ok &= Expect(Probe(fix, again), "but the next job out there does (control: the grace lasts one job)");
            Settle(pawn);
            return ok & Expect(fix.Comp.OnShift, "and they are still on shift at the end of the pass");
        }
    }
}
#endif
