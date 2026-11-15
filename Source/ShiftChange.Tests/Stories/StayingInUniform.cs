using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DevTools.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.CatchmentKit;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Work that keeps a colonist in uniform away from the stand, at the edge of a radius, in bed,
    /// at play or on a delivery, and work that sends them back to change. Each test dresses a
    /// colonist at the stand on the game's own clock, then asks the mod's real decision about the
    /// jobs that could come next, each paired with one of the same shape that goes the other way.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.StayingInUniform)]
    internal class StayingInUniform : StoryFixture
    {
        /// <summary>What <see cref="SceneKit.Stage"/> sets up with the pad enclosed.</summary>
        internal const string InARoom =
            "a stand holding a duster in the corner of a walled, roofed room, "
            + "and a colonist beside it in a shirt, trousers and a parka";

        /// <summary>The same, with a colonist able to build.</summary>
        internal const string InARoomABuilder =
            "a stand holding a duster in the corner of a walled, roofed room, "
            + "and a colonist able to build beside it in a shirt, trousers and a parka";

        /// <summary>
        /// What <see cref="SceneKit.StageWithDoorway(Map, CellRect, WorkTypeDef)"/> sets up
        /// with a colonist able to doctor.
        /// </summary>
        internal const string BesideADoorADoctor =
            "a stand holding a duster in the corner of a walled, roofed room, a door beside it onto open "
            + "ground, and a colonist able to doctor beside the stand in a shirt, trousers and a parka";

        /// <summary>The same, with a colonist able to build.</summary>
        internal const string BesideADoorABuilder =
            "a stand holding a duster in the corner of a walled, roofed room, a door beside it onto open "
            + "ground, and a colonist able to build beside the stand in a shirt, trousers and a parka";

        /// <summary>
        /// A stand serving Doctor work within a radius: inside its ring, the uniform stays on for
        /// the stand's own work, for work passing through (hauling, cleaning, building), and for
        /// idling and wandering, and for nothing else. Past the ring nothing keeps it, in the
        /// stand's own room included. Serving its room, the stand keeps it on for anything in the
        /// room, for comparison, and each job kept is paired with one of the same shape that
        /// changes them back, so a job turned down for its own reasons cannot pass as staying
        /// dressed.
        /// </summary>
        [Test]
        [TestDescription("inside the ring of a stand serving Doctor work within a radius, the uniform stays on "
                         + "for doctoring, hauling, cleaning, building and idling, and for nothing else")]
        internal IEnumerator AWorkStandsRingKeepsTheUniformOn()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                WorkGiverDef cook = Giver("DoBillsCook");
                WorkGiverDef haul = Giver("HaulGeneral");
                WorkGiverDef clean = Giver("CleanFilth");
                WorkGiverDef construct = Giver("ConstructFinishFrames");
                JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
                ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                if (doctor == null || tend == null || cook == null || haul == null || clean == null
                    || construct == null || chess == null || bedDef == null)
                {
                    story.Check(false, "the game has Doctor work, cooking, hauling, cleaning, building, "
                                       + "chess and the bed");
                    return;
                }
                // Ticked before the bed lands: a bed makes the pad a bedroom, which an automatic stand
                // would read as sleep.
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                story.Note("Doctor is ticked on the stand, and the map is calm");

                IntVec3 at = scene.OutfitStand.Position;
                IntVec3 inside = at + new IntVec3(1, 0, 2);
                IntVec3 past = at + new IntVec3(4, 0, 4);
                Building_Bed bed = DebugTools_Fixtures.Spawn(
                    scene.Map, bedDef, ThingDefOf.WoodLog, at + new IntVec3(2, 0, 0), Rot4.North) as Building_Bed;
                if (bed == null)
                {
                    story.Check(false, "a bed can be built in the stand's room");
                    return;
                }
                story.Note("a bed is built in the room, two cells east of the stand");

                story.Check(SwapNow(story, scene), name + " changes into the stand's duster");
                if (!story.Check(scene.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                NotSentStraightBack(story, scene);
                story.Check(!SendsToChange(scene, WorkAt(cook, past)),
                            "serving its room, the stand keeps the uniform on for cooking anywhere in the room, "
                            + "though it does not serve cooking");
                story.Check(!SendsToChange(scene, JobMaker.MakeJob(chess, inside, inside)),
                            "and for a game of chess in the room, as it always has");

                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(3);
                story.Note("Within a radius is chosen under Serves, and the radius set to 3 cells");
                story.Check(scene.Comp.Covers(inside, scene.Map) && scene.Comp.Covers(bed.Position, scene.Map)
                            && !scene.Comp.Covers(past, scene.Map),
                            "the ring takes in a spot near the stand and the bed, not the room's far corner");
                story.Check(!SendsToChange(scene, WorkAt(tend, inside)),
                            "Doctor work inside the ring keeps the uniform on");
                story.Check(SendsToChange(scene, WorkAt(tend, past)),
                            "Doctor work past the ring, in the stand's own room, changes them back");
                story.Check(SendsToChange(scene, WorkAt(cook, inside)),
                            "cooking inside the ring changes them back: the stand does not serve it");
                story.Check(!SendsToChange(scene, WorkAt(haul, inside)),
                            "hauling inside the ring keeps the uniform on, as work passing through");
                story.Check(!SendsToChange(scene, WorkAt(clean, inside)), "and so does cleaning");
                story.Check(!SendsToChange(scene, WorkAt(construct, inside)), "and building");
                story.Check(SendsToChange(scene, WorkAt(haul, past)), "hauling past the ring changes them back");
                story.Check(!SendsToChange(scene, JobMaker.MakeJob(JobDefOf.GotoWander, inside)),
                            "wandering inside the ring keeps the uniform on");
                story.Check(SendsToChange(scene, JobMaker.MakeJob(JobDefOf.GotoWander, past)),
                            "wandering past it changes them back");
                story.Check(!SendsToChange(scene, JobMaker.MakeJob(JobDefOf.Wait)),
                            "idling where they stand, inside the ring, keeps the uniform on");
                story.Check(SendsToChange(scene, JobMaker.MakeJob(JobDefOf.LayDown, inside)),
                            "sleeping on the ground inside the ring changes them back: a work stand does not "
                            + "keep it on for that");
                story.Check(SendsToChange(scene, JobMaker.MakeJob(chess, inside, inside)),
                            "and so does a game of chess inside it");
                story.Check(SendsToChange(scene, JobMaker.MakeJob(JobDefOf.LayDown, bed)),
                            "and going to bed inside it");

                // A wait goes nowhere, so it is judged where the colonist stands.
                IntVec3 wasAt = scene.Colonist.Position;
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = past;
                story.Note(name + " is moved to the room's far corner, past the ring");
                story.Check(SendsToChange(scene, JobMaker.MakeJob(JobDefOf.Wait)),
                            "idling past the ring changes them back");
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = wasAt;
                story.Note(name + " is moved back beside the stand");
                story.Check(scene.Comp.OnShift, "and " + name + " is still on shift at the end");
            }
        }

        /// <summary>
        /// Work passing through is judged at both ends. A haul with its item and its destination
        /// inside the ring keeps the uniform on; with either end past it, the colonist changes
        /// back before the haul, while the stand is near. A cleaning run's queue is judged the
        /// same way. An errand no work handed out, queued ahead of the stand's own work inside the
        /// ring, rides along whatever its ends (docs/DESIGN.md, "Errands queued ahead of work").
        /// Serving its room, the stand keeps the uniform on for the outbound haul, which never
        /// leaves the room, for comparison.
        /// </summary>
        [Test]
        [TestDescription("a haul or a cleaning run keeps the uniform on only with both ends inside the ring, "
                         + "and an errand queued ahead of the stand's own work goes along with it")]
        internal IEnumerator AHaulIsJudgedAtBothEnds()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                WorkGiverDef haul = Giver("HaulGeneral");
                WorkGiverDef clean = Giver("CleanFilth");
                if (doctor == null || tend == null || haul == null || clean == null
                    || ThingDefOf.RawPotatoes == null || ThingDefOf.Filth_Dirt == null)
                {
                    story.Check(false, "the game has Doctor work, hauling, cleaning, potatoes and dirt");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                story.Note("Doctor is ticked on the stand, and the map is calm");

                Map map = scene.Map;
                IntVec3 at = scene.OutfitStand.Position;
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
                story.Note("potatoes lie near the stand and across the room, and dirt near the stand "
                           + "and in the room's far corner");

                story.Check(SwapNow(story, scene), name + " changes into the stand's duster");
                if (!story.Check(scene.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                NotSentStraightBack(story, scene);
                story.Check(!SendsToChange(scene, Haul(haul, near, past)),
                            "serving its room, the stand keeps the uniform on for a haul to the room's far corner");

                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(3);
                story.Note("Within a radius is chosen under Serves, and the radius set to 3 cells");
                story.Check(scene.Comp.Covers(from, map) && scene.Comp.Covers(to, map)
                            && scene.Comp.Covers(dirtHere.Position, map) && scene.Comp.Covers(dirtThere.Position, map)
                            && !scene.Comp.Covers(far.Position, map) && !scene.Comp.Covers(past, map),
                            "the ring takes in the near potatoes, the dirt near the stand and a cell on its edge, "
                            + "and not the far potatoes or the room's far corner");
                story.Check(!SendsToChange(scene, Haul(haul, near, to)),
                            "hauling potatoes from inside the ring to a cell on its edge keeps the uniform on");
                story.Check(SendsToChange(scene, Haul(haul, near, past)),
                            "the same haul to a cell past the ring changes them back first");
                story.Check(SendsToChange(scene, Haul(haul, far, to)),
                            "and so does one bringing potatoes in from past the ring");
                story.Check(!SendsToChange(scene, Cleaning(clean, dirtHere, dirtThere)),
                            "cleaning with all its dirt inside the ring keeps the uniform on");
                story.Check(SendsToChange(scene, Cleaning(clean, dirtHere, dirtPast)),
                            "with one patch of it past the ring, it changes them back");

                JobQueue queue = scene.Colonist.jobs.jobQueue;
                queue.EnqueueFirst(RideAlongKit.QueuedWork(tend, from));
                story.Note("Doctor work inside the ring is queued for " + name);
                story.Check(!SendsToChange(scene, RideAlongKit.GiverlessHaul(far, to)),
                            "a haul no work handed out, ahead of the stand's own work inside the ring, keeps it on");
                queue.EnqueueFirst(RideAlongKit.QueuedWork(tend, past));
                story.Note("Doctor work past the ring is queued instead");
                story.Check(SendsToChange(scene, RideAlongKit.GiverlessHaul(far, to)),
                            "ahead of that work past the ring, the same haul changes them back");
                story.Check(scene.Comp.OnShift, "and " + name + " is still on shift at the end");
            }
        }

        /// <summary>
        /// A stand serving Sleeping within a radius: the first job out of bed changes the colonist
        /// back, wherever it is, a haul inside the ring included. Going to a bed inside the ring
        /// keeps the pyjamas on, since the stand would dress them for it; a bed past the ring
        /// does not, and nor does bed rest for medical reasons, which is the work stand's. The
        /// same haul keeping the uniform on once the stand serves Doctor work is the comparison.
        /// </summary>
        [Test]
        [TestDescription("at a stand serving Sleeping within a radius, the pyjamas stay on for a bed inside "
                         + "the ring, and the first job out of bed changes them back")]
        internal IEnumerator ASleepStandKeepsThePyjamasOnOnlyForBed()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef haul = Giver("HaulGeneral");
                ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                if (doctor == null || haul == null || bedDef == null)
                {
                    story.Check(false, "the game has Doctor work, hauling and the bed");
                    return;
                }
                // Ticked before the beds land: a bed makes the pad a bedroom, and on an automatic
                // stand ticking Sleeping would then turn sleep off.
                scene.Comp.ToggleRest();
                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(3);
                MakeCalm(scene.Map);
                story.Note("Sleeping is ticked on the stand, Within a radius chosen under Serves, the radius "
                           + "set to 3 cells, and the map is calm");

                IntVec3 at = scene.OutfitStand.Position;
                IntVec3 inside = at + new IntVec3(1, 0, 2);
                Building_Bed near = DebugTools_Fixtures.Spawn(
                    scene.Map, bedDef, ThingDefOf.WoodLog, at + new IntVec3(2, 0, 0), Rot4.North) as Building_Bed;
                Building_Bed far = DebugTools_Fixtures.Spawn(
                    scene.Map, bedDef, ThingDefOf.WoodLog, at + new IntVec3(4, 0, 3), Rot4.North) as Building_Bed;
                if (near == null || far == null)
                {
                    story.Check(false, "two beds can be built in the stand's room");
                    return;
                }
                story.Note("a bed is built near the stand and another across the room");

                story.Check(scene.Comp.HandlesRest() && scene.Comp.serves == CatchmentKind.Radius,
                            "the stand serves sleeping within a radius");
                story.Check(scene.Comp.Covers(near.Position, scene.Map) && !scene.Comp.Covers(far.Position, scene.Map),
                            "its ring takes in the near bed and not the far one");
                story.Check(SwapNow(story, scene), name + " changes into the stand's duster for bed");
                if (!story.Check(scene.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                NotSentStraightBack(story, scene);
                story.Check(!SendsToChange(scene, JobMaker.MakeJob(JobDefOf.LayDown, near)),
                            "going to the bed inside the ring keeps the pyjamas on");
                story.Check(SendsToChange(scene, JobMaker.MakeJob(JobDefOf.LayDown, far)),
                            "the bed past the ring changes them back");
                story.Check(SendsToChange(scene, JobMaker.MakeJob(JobDefOf.LayDown, near),
                                          JobTag.RestingForMedicalReasons),
                            "and so does bed rest for medical reasons, inside the ring or not");

                IntVec3 wasAt = scene.Colonist.Position;
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = near.Position;
                story.Note(name + " is moved onto the near bed, as if just waking");
                story.Check(SendsToChange(scene, WorkAt(haul, inside)),
                            "on waking, the first job out of bed changes them back, a haul inside the ring included");
                story.Check(SendsToChange(scene, JobMaker.MakeJob(JobDefOf.GotoWander, inside)),
                            "and so does wandering inside it");
                story.Check(!SendsToChange(scene, JobMaker.MakeJob(JobDefOf.LayDown, near)),
                            "while staying in bed does not");

                scene.Comp.ToggleWork(doctor);
                story.Note("Doctor is ticked on the stand as well");
                story.Check(scene.Comp.HandlesWork(doctor) && scene.Comp.serves == CatchmentKind.Radius
                            && scene.Comp.OnShift,
                            "the same stand now serves Doctor work in its ring, " + name + " still on shift");
                story.Check(!SendsToChange(scene, WorkAt(haul, inside)),
                            "and the same haul keeps the uniform on, as at any work stand");
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = wasAt;
                story.Note(name + " is moved back beside the stand");
            }
        }

        /// <summary>
        /// A stand serving Recreation within a radius: inside its ring, recreation, idling and
        /// wandering keep the outfit on, and work passing through does not, since that is a work
        /// stand's. A swimmer wanders between pools in swimwear, but changes before hauling. The
        /// same haul keeping the uniform on once the stand serves Doctor work is the comparison.
        /// </summary>
        [Test]
        [TestDescription("at a stand serving Recreation within a radius, the outfit stays on for play, "
                         + "wandering and idling inside the ring, and comes off for hauling")]
        internal IEnumerator ARecreationStandKeepsTheOutfitOnForPlay()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef haul = Giver("HaulGeneral");
                JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
                if (doctor == null || haul == null || chess == null)
                {
                    story.Check(false, "the game has Doctor work, hauling and chess");
                    return;
                }
                scene.Comp.ToggleRecreation();
                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(3);
                MakeCalm(scene.Map);
                story.Note("Recreation is ticked on the stand, Within a radius chosen under Serves, the radius "
                           + "set to 3 cells, and the map is calm");

                IntVec3 at = scene.OutfitStand.Position;
                IntVec3 inside = at + new IntVec3(1, 0, 2);
                IntVec3 past = at + new IntVec3(4, 0, 4);
                story.Check(scene.Comp.HandlesRecreation() && scene.Comp.serves == CatchmentKind.Radius,
                            "the stand serves recreation within a radius");
                story.Check(SwapNow(story, scene), name + " changes into the stand's duster for play");
                if (!story.Check(scene.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                NotSentStraightBack(story, scene);
                story.Check(!SendsToChange(scene, JobMaker.MakeJob(chess, inside, inside)),
                            "a game of chess inside the ring keeps the outfit on");
                story.Check(SendsToChange(scene, JobMaker.MakeJob(chess, past, past)),
                            "one past the ring changes them back");
                story.Check(!SendsToChange(scene, JobMaker.MakeJob(JobDefOf.GotoWander, inside)),
                            "wandering inside the ring keeps it on");
                story.Check(!SendsToChange(scene, JobMaker.MakeJob(JobDefOf.Wait)),
                            "and so does idling where they stand");
                story.Check(SendsToChange(scene, WorkAt(haul, inside)),
                            "hauling inside the ring changes them back: work passing through is a work stand's");

                scene.Comp.ToggleWork(doctor);
                story.Note("Doctor is ticked on the stand as well");
                story.Check(scene.Comp.HandlesWork(doctor) && scene.Comp.serves == CatchmentKind.Radius
                            && scene.Comp.OnShift,
                            "the same stand now serves Doctor work in its ring, " + name + " still on shift");
                story.Check(!SendsToChange(scene, WorkAt(haul, inside)),
                            "and the same haul keeps the uniform on, as at any work stand");
            }
        }

        /// <summary>
        /// The job a colonist was just dressed for never changes them back, though its target
        /// leaves what the stand serves while they change: a patient here, standing in for an
        /// animal walking out of an unwalled ring or a cow out of the barn. The mod's own decision
        /// sends them to change, and the game's own queue starts the deferred job, so the job
        /// tracker's pause between the two is on the way and must not use up the grace. Vanilla
        /// can start a haul first and put the job back in the queue: that haul keeps the uniform
        /// on, and so does the job starting again. The next job out there changes them back: the
        /// grace lasts one job. Once for a stand serving a radius, then for one serving its room
        /// whose patient leaves the room.
        /// </summary>
        [Test]
        [TestDescription("a doctor keeps the uniform on for the tending they were dressed for, though the "
                         + "patient walks out of reach, and for that one job only")]
        internal IEnumerator TheJobTheyDressedForKeepsItOn()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, WorkType("Doctor")),
                          BesideADoorADoctor, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    story.Check(false, "the game has Doctor work and tending patients");
                    return;
                }
                if (scene.Outside.Area == 0)
                {
                    story.Check(false, "the room has a door, with open ground outside it");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                story.Note("Doctor is ticked on the stand, and the map is calm");
                Pawn patient = AnotherColonist(story, scene, Gender.Female, "Patient");
                StayPut(patient);
                IntVec3 patientAt = patient.Position;
                story.Note(patient.LabelShort + " holds still there, as a patient in bed would");

                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(3);
                story.Note("Within a radius is chosen under Serves, and the radius set to 3 cells");
                story.Check(scene.Comp.Covers(patientAt, scene.Map), patient.LabelShort + " is inside the ring");
                DressedForThenMoved(story, scene, tend, patient, scene.OutfitStand.Position + new IntVec3(4, 0, 4),
                                    "serving a radius", "past the ring");

                // Changed back while the stand still serves its radius, so the freed stand catches
                // nobody up on the way to its room.
                story.Check(SwapNow(story, scene), name + " changes back between the two passes");
                story.Check(!scene.Comp.OnShift, "and is off shift");
                ClearSwapDecision(scene.Colonist);
                patient.pather?.StopDead();
                patient.Position = patientAt;
                story.Note(patient.LabelShort + " is put back where she lay");

                scene.Comp.SetServes(CatchmentKind.Room);
                story.Note("This room is chosen under Serves");
                IntVec3 outside = new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ);
                story.Check(scene.Comp.Covers(patientAt, scene.Map) && !scene.Comp.Covers(outside, scene.Map),
                            "serving its room, the stand takes in " + patient.LabelShort
                            + " and not the ground outside the door");
                DressedForThenMoved(story, scene, tend, patient, outside, "serving its room", "out of the room");
            }
        }

        /// <summary>
        /// One pass of <see cref="TheJobTheyDressedForKeepsItOn"/>: the colonist is sent to dress
        /// for tending the patient, the patient moves to <paramref name="away"/> while they
        /// change, and then the game's queue starts the tending. <paramref name="pass"/> says what
        /// the stand serves, and <paramref name="gone"/> where the patient goes, in the game's
        /// words.
        /// </summary>
        internal static void DressedForThenMoved(Story story, Scene scene, WorkGiverDef tend, Pawn patient,
                                                 IntVec3 away, string pass, string gone)
        {
            Pawn pawn = scene.Colonist;
            string name = pawn.LabelShort;
            Job tending = JobMaker.MakeJob(JobDefOf.Wait, patient);
            tending.workGiverDef = tend;
            JobQueue queue = pawn.jobs.jobQueue;
            bool inserted = Patch_JobInterception.TryInsertSwap(tending, null, pawn, pawn.jobs);
            story.Check(inserted && pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand
                        && queue.Count > 0 && queue[0].job == tending,
                        pass + ": tending " + patient.LabelShort + " sends " + name
                        + " to change first, the tending queued behind the change");
            // The change started here gives way to the one SwapNow starts, and the queue keeps the
            // tending: the change never touches it.
            story.Check(SwapNow(story, scene), name + " changes into the stand's duster");
            story.Check(scene.Comp.OnShift && queue.Count > 0 && queue[0].job == tending,
                        "and is on shift, the tending still queued");

            patient.pather?.StopDead();
            patient.Position = away;
            story.Check(!scene.Comp.Covers(away, scene.Map),
                        patient.LabelShort + " has walked " + gone + " while " + name + " changed");
            story.Check(pawn.CurJobDef == JobDefOf.Wait_MaintainPosture,
                        "the game pauses " + name + " for a moment between the change and the queue, "
                        + "so the pause is on the way to the tending");

            // The pause ends as the job tracker ends it when it runs out, and the queue starts the
            // tending through the game's own StartJob, this mod's prefix included.
            pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
            story.Check(pawn.CurJob == tending,
                        "the tending " + name + " dressed for starts (" + (pawn.CurJobDef?.defName ?? "no job") + ")");
            story.Check(scene.Comp.OnShift, "in uniform: the job they were dressed for does not send them back");

            // Vanilla's StartJob can then put the tending back at the head of the queue,
            // untagged, and start a haul first (Pawn_JobTracker.TryOpportunisticJob).
            queue.EnqueueFirst(tending);
            story.Check(!SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(JobDefOf.HaulToCell, away, away)),
                        "a haul the game starts first, the tending queued behind it, keeps the uniform on");
            story.Check(!SendsToChangeKeepingQueue(scene, tending),
                        "and the tending, starting again after the haul, still does");
            queue.Extract(tending);

            Job again = JobMaker.MakeJob(JobDefOf.Wait, patient);
            again.workGiverDef = tend;
            story.Check(SendsToChange(scene, again),
                        "but the next tending out there changes them back: the grace lasts one job");
            ClearSwapDecision(pawn);
            story.Check(scene.Comp.OnShift, "and " + name + " is still on shift at the end of the pass");
        }

        // A known gap unless Complex Jobs is loaded (--with=).
        /// <summary>
        /// A delivery sends nobody to change at a stand serving a radius, and a colonist in
        /// uniform keeps the uniform of the delivery's own work on: with the stack past the ring
        /// and the site inside, since the stand serves that work, and changing back now for a
        /// site past it. The site jobs the same work hands out (removing an old floor) are
        /// ordinary work, and a haul the game puts ahead of a queued delivery keeps the uniform on,
        /// where the same haul alone changes them back. Each delivery is built as its work hands
        /// it out (<see cref="GiverTables.DeliveryDefFor"/>), for the five Core delivery works
        /// and seed planting; Complex Jobs' two are a known gap without that mod. The stand ticks
        /// each one's work type, so a refusal is the delivery rule and not unserved work.
        /// </summary>
        [Test]
        [TestDescription("a delivery sends nobody to change, and one in uniform keeps it on to supply a site "
                         + "inside the ring from anywhere, but changes back for a site past it")]
        internal IEnumerator ADeliveryKeepsItsOwnWorksUniformOn()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef construction = WorkType("Construction");
                WorkTypeDef growing = WorkType("Growing");
                WorkGiverDef finish = Giver("ConstructFinishFrames");
                WorkGiverDef sow = Giver("GrowerSow");
                WorkGiverDef blueprints = Giver("ConstructDeliverResourcesToBlueprints");
                if (construction == null || growing == null || finish == null || sow == null || blueprints == null)
                {
                    story.Check(false, "the game has Construction and Growing work, building frames, sowing "
                                       + "and delivering to blueprints");
                    return;
                }
                scene.Comp.ToggleWork(construction);
                scene.Comp.ToggleWork(growing);

                List<WorkGiverDef> givers = new List<WorkGiverDef>();
                List<string> names =
                    new List<string>(GiverTables.OfficialDeliveryGivers) { JobRoomTargets.SeedPlanter };
                foreach (string giverName in names)
                {
                    WorkGiverDef giver = Giver(giverName);
                    if (giver != null)
                    {
                        givers.Add(giver);
                    }
                }
                bool complexJobs = ModsConfig.IsActive("FrozenSnowFox.ComplexJobs");
                int complexFound = 0;
                if (complexJobs)
                {
                    foreach (string giverName in new[] { "FSFDeliverResourcesToFrames", "FSFDeliverResourcesToBlueprints" })
                    {
                        WorkGiverDef giver = Giver(giverName);
                        if (giver != null)
                        {
                            givers.Add(giver);
                            complexFound++;
                        }
                    }
                }
                // Each delivery's own work type, whatever a mod has repointed it to: Complex Jobs
                // moves Core's two hauling deliveries to its own delivering work.
                foreach (WorkGiverDef giver in givers)
                {
                    if (giver.workType != null && !scene.Comp.HandlesWork(giver.workType))
                    {
                        scene.Comp.ToggleWork(giver.workType);
                    }
                }
                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(3);
                MakeCalm(scene.Map);
                story.Note("Construction, Growing and every delivery's own work are ticked on the stand, "
                           + "Within a radius chosen under Serves, the radius set to 3 cells, and the map is calm");

                IntVec3 at = scene.OutfitStand.Position;
                IntVec3 inside = at + new IntVec3(1, 0, 2);
                IntVec3 insideSite = at + new IntVec3(2, 0, 1);
                IntVec3 past = at + new IntVec3(4, 0, 4);
                IntVec3 pastSite = at + new IntVec3(4, 0, 3);
                story.Check(givers.Count >= 6, "the game's five Core delivery works and seed planting are all found");
                story.Check(SendsToChange(scene, WorkAt(finish, inside)),
                            "building a frame inside the ring sends " + name + ", in their own clothes, to change");
                story.Check(SendsToChange(scene, FloorRemovalAt(blueprints, inside)),
                            "and so does removing an old floor inside it, though the same work hands out deliveries");
                foreach (WorkGiverDef giver in givers)
                {
                    story.Check(scene.Comp.HandlesWork(giver.workType)
                                && !SendsToChange(scene, DeliveryAt(giver, inside, insideSite)),
                                Called(giver) + ": its delivery sends nobody to change, though the stand serves "
                                + "its work and the whole trip is inside the ring");
                }

                story.Check(SwapNow(story, scene), name + " changes into the stand's duster");
                if (!story.Check(scene.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                NotSentStraightBack(story, scene);
                story.Check(SendsToChange(scene, WorkAt(finish, past)), "a frame past the ring changes them back");
                story.Check(SendsToChange(scene, WorkAt(sow, past)), "and so does sowing past it");
                story.Check(SendsToChange(scene, FloorRemovalAt(blueprints, past)),
                            "and so does removing an old floor past it: that is work at the site, not a delivery");
                foreach (WorkGiverDef giver in givers)
                {
                    story.Check(!SendsToChange(scene, DeliveryAt(giver, past, insideSite)),
                                Called(giver) + ": its delivery keeps the uniform on, the stack past the ring and "
                                + "the site inside, since the stand serves its work");
                }
                // JobDriver_PlantSeed writes the seed stack it splits into targetC, so a planting
                // resumed after an interruption has the stack's end of the trip there, not a site.
                WorkGiverDef seedPlanter = Giver(JobRoomTargets.SeedPlanter);
                if (seedPlanter != null)
                {
                    Job resumed = DeliveryAt(seedPlanter, past, insideSite);
                    resumed.targetC = past;
                    story.Check(!SendsToChange(scene, resumed),
                                "seed planting picked up again, with its seed stack past the ring where a site "
                                + "would be, keeps it on: that is no site");
                }
                // Real things, since this delivery reaches the reservation dry run of the mod's
                // decision.
                Thing steelInside = Steel(scene.Map, inside);
                Thing siteOutside = WallBlueprint(scene.Map, pastSite);
                story.Note("twenty steel are put inside the ring, and a wall blueprint placed past it");
                story.Check(siteOutside != null && SendsToChange(scene, Delivery(blueprints, steelInside, siteOutside)),
                            "a delivery to a site past the ring changes them back now, while the stand is near");

                // Jobs are pooled, so the next delivery from the same steel can be the same Job object
                // handed out again with a new id; the record of the job they were dressed for must
                // not let it through.
                Job dressedFor = Delivery(blueprints, steelInside, siteOutside);
                Patch_JobInterception.DressedFor[scene.Colonist] = new Patch_JobInterception.DeferredJob(dressedFor);
                story.Note(name + " is marked as just dressed for that delivery");
                story.Check(!SendsToChange(scene, dressedFor),
                            "the delivery they were dressed for keeps the uniform on, its site past the ring or not");
                Patch_JobInterception.DressedFor[scene.Colonist] = new Patch_JobInterception.DeferredJob(dressedFor);
                dressedFor.loadID = Find.UniqueIDsManager.GetNextJobID();
                story.Note("the same job is handed out again under a new number");
                story.Check(SendsToChange(scene, dressedFor),
                            "and handed out again, the same steel to the same site, it changes them back");
                Patch_JobInterception.DressedFor.Remove(scene.Colonist);

                // The shape vanilla's opportunistic haul leaves: the delivery put back at the head of
                // the queue, untagged, and a haul started first.
                story.Check(SendsToChange(scene, JobMaker.MakeJob(JobDefOf.HaulToCell, past, pastSite)),
                            "a haul past the ring changes them back");
                scene.Colonist.jobs.jobQueue.EnqueueFirst(DeliveryAt(blueprints, past, insideSite));
                story.Note("a delivery to a site inside the ring is queued for " + name);
                story.Check(!SendsToChange(scene, JobMaker.MakeJob(JobDefOf.HaulToCell, past, pastSite)),
                            "the same haul keeps it on with that delivery queued behind it");
                scene.Colonist.jobs.jobQueue.EnqueueFirst(DeliveryAt(blueprints, past, pastSite));
                story.Note("a delivery to a site past the ring is queued instead");
                story.Check(SendsToChange(scene, JobMaker.MakeJob(JobDefOf.HaulToCell, past, pastSite)),
                            "but not with that one queued behind it");

                if (!complexJobs)
                {
                    story.KnownGap(false, "Complex Jobs' two delivery works are checked too",
                                   "Complex Jobs is not on this mod list; --with=FrozenSnowFox.ComplexJobs loads it");
                    return;
                }
                story.Check(complexFound == 2, "Complex Jobs' two delivery works are found, and were checked above");
            }
        }

        /// <summary>
        /// A delivery outside the stand's work takes the return trip, judged by the whole trip.
        /// Pyjamas from a sleep stand and a cook's whites change back first for steel out past
        /// the door, to a site there or in the room; steel and site both in the room are the
        /// room's, for comparison. Once the stand serves Construction, a site in the room keeps the
        /// uniform on from steel past the door, the same fetch filed under Hauling included but
        /// not one filed under another work type; a first drop, a site or a further site past
        /// the door each change them back now. A stand serving Sleeping within a radius changes
        /// them back for a trip wholly inside its ring as well: the first job out of bed always
        /// does.
        /// </summary>
        [Test]
        [TestDescription("in pyjamas or a cook's whites, a colonist changes back before fetching steel from "
                         + "past the door, and in a builder's uniform only for a site out there")]
        internal IEnumerator ADeliveryForOtherWorkChangesThemBackFirst()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, WorkType("Construction")),
                          BesideADoorABuilder, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef construction = WorkType("Construction");
                WorkTypeDef cooking = WorkType("Cooking");
                WorkGiverDef deliver = Giver("ConstructDeliverResourcesToBlueprints");
                // Vanilla files the same fetch under Hauling when hauling outranks construction, or
                // the blueprint needs more skill.
                WorkGiverDef haulDeliver = Giver("DeliverResourcesToBlueprints");
                if (construction == null || cooking == null || deliver == null)
                {
                    story.Check(false, "the game has Construction and Cooking work and delivering to blueprints");
                    return;
                }
                if (scene.Outside.Area == 0)
                {
                    story.Check(false, "the room has a door, with open ground outside it");
                    return;
                }
                scene.Comp.ToggleRest();
                MakeCalm(scene.Map);
                story.Note("Sleeping is ticked on the stand, and the map is calm");

                Map map = scene.Map;
                IntVec3 at = scene.OutfitStand.Position;
                Thing steelOut = Steel(map, new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ));
                Thing siteOut = WallBlueprint(map, new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ - 1));
                Thing steelIn = Steel(map, at + new IntVec3(1, 0, 2));
                Thing siteIn = WallBlueprint(map, at + new IntVec3(2, 0, 1));
                Thing siteInToo = WallBlueprint(map, at + new IntVec3(3, 0, 2));
                Thing siteOutToo = WallBlueprint(map, new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ - 2));
                if (siteOut == null || siteIn == null || siteInToo == null || siteOutToo == null)
                {
                    story.Check(false, "the wall blueprints can be placed");
                    return;
                }
                story.Note("steel lies out past the door and in the room, with two wall blueprints placed "
                           + "in each");

                story.Check(scene.Comp.HandlesRest() && !scene.Comp.HandlesWork(construction),
                            "the stand serves sleeping and not Construction");
                story.Check(SwapNow(story, scene), name + " changes into the stand's duster for bed");
                if (!story.Check(scene.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                NotSentStraightBack(story, scene);
                story.Check(SendsToChange(scene, Delivery(deliver, steelOut, siteOut)),
                            "in pyjamas, a delivery from steel out past the door changes them back first");
                story.Check(SendsToChange(scene, Delivery(deliver, steelOut, siteIn)),
                            "and so does one to a site in the room: the trip starts at the steel");
                story.Check(!SendsToChange(scene, Delivery(deliver, steelIn, siteIn)),
                            "while one with steel and site both in the room is the room's, and keeps the pyjamas on");

                scene.Comp.ToggleWork(cooking);
                story.Note("Cooking is ticked on the stand");
                story.Check(scene.Comp.HandlesWork(cooking) && !scene.Comp.HandlesWork(construction)
                            && scene.Comp.OnShift,
                            "the stand now serves Cooking, " + name + " still on shift");
                story.Check(SendsToChange(scene, Delivery(deliver, steelOut, siteOut)),
                            "and in a cook's whites, the delivery from past the door changes them back first too");

                scene.Comp.ToggleWork(construction);
                story.Note("Construction is ticked on the stand as well");
                story.Check(scene.Comp.HandlesWork(construction) && scene.Comp.OnShift,
                            "the stand now serves Construction too");
                story.Check(!SendsToChange(scene, Delivery(deliver, steelOut, siteIn)),
                            "so a delivery from steel out past the door to a site in the room keeps the uniform on");
                story.Check(SendsToChange(scene, Delivery(deliver, steelIn, siteOut)),
                            "and one to a site out past the door changes them back now, while the stand is near");
                // First drop and site set apart, as the giver sets them (CatchmentKit.DeliveryVia).
                story.Check(!SendsToChange(scene, DeliveryVia(deliver, steelOut, siteInToo, siteIn)),
                            "a first drop and a site that differ, both in the room, keep it on");
                story.Check(SendsToChange(scene, DeliveryVia(deliver, steelOut, siteOut, siteIn)),
                            "a first drop out past the door changes them back, the site in the room");
                story.Check(SendsToChange(scene, DeliveryVia(deliver, steelOut, siteIn, siteOut)),
                            "and so does a site out there with the first drop in the room");
                story.Check(SendsToChange(scene, DeliveryVia(deliver, steelOut, siteIn, siteInToo, siteOutToo)),
                            "and a further site out there, queued behind the first drop");
                if (haulDeliver != null)
                {
                    story.Check(!scene.Comp.HandlesWork(haulDeliver.workType)
                                && !SendsToChange(scene, Delivery(haulDeliver, steelOut, siteIn)),
                                "the same fetch filed under " + haulDeliver.workType.defName
                                + ", which the stand does not tick, keeps it on too: it supplies construction");
                }
                // A fetch filed under a work type that is not hauling is that work's: VEF's art-skill
                // deliveries go under Art. Replanting a tree (Growing, and a delivery) stands in for
                // them here.
                WorkGiverDef replant = Giver("Replant");
                if (replant != null && replant.workType != null)
                {
                    story.Check((replant.workType.workTags & WorkTags.Hauling) == 0
                                && !scene.Comp.HandlesWork(replant.workType)
                                && SendsToChange(scene, Delivery(replant, steelOut, siteIn)),
                                "a fetch filed under " + replant.workType.defName
                                + ", which is not hauling, is not kept on for Construction");
                }

                scene.Comp.ToggleWork(construction);
                scene.Comp.ToggleWork(cooking);
                scene.Comp.ToggleRest();
                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(3);
                story.Note("Construction and Cooking are unticked, Sleeping ticked again, and Within a radius "
                           + "chosen under Serves, the radius set to 3 cells");
                story.Check(scene.Comp.HandlesRest() && scene.Comp.serves == CatchmentKind.Radius && scene.Comp.OnShift,
                            "the stand serves sleeping again, now within a radius, " + name + " still on shift");
                story.Check(scene.Comp.Covers(steelIn.Position, map) && scene.Comp.Covers(siteIn.Position, map),
                            "the ring takes in the steel and the site in the room");
                story.Check(SendsToChange(scene, Delivery(deliver, steelIn, siteIn)),
                            "and a delivery with the whole trip inside it changes them back: the first job out "
                            + "of bed always does");
            }
        }

        /// <summary>
        /// An urgent patient on shift goes straight to bed, with Medical emergencies change first
        /// off: serving its room, to a bed out past the door, whether the lay-down is marked as
        /// medical rest or the game puts it back in the queue unmarked, and with a haul the game
        /// starts ahead of the lay-down; serving a radius, to a bed inside the ring. Resting to
        /// recover with no such wound, or with the setting on, changes them back first, for
        /// comparison.
        /// </summary>
        [Test]
        [TestDescription("a doctor in uniform with an untended wound goes straight to a medical bed, even when "
                         + "the game queues the lay-down behind a haul")]
        internal IEnumerator AnUrgentPatientGoesStraightToBed()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, WorkType("Doctor")),
                          BesideADoorADoctor, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                if (doctor == null || bedDef == null)
                {
                    story.Check(false, "the game has Doctor work and the bed");
                    return;
                }
                if (scene.Outside.Area == 0)
                {
                    story.Check(false, "the room has a door, with open ground outside it");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                story.Note("Doctor is ticked on the stand, and the map is calm");
                Map map = scene.Map;
                Building_Bed bedOut = DebugTools_Fixtures.Spawn(
                    map, bedDef, ThingDefOf.WoodLog, new IntVec3(scene.Outside.minX, 0, scene.Outside.minZ + 2),
                    Rot4.North) as Building_Bed;
                Building_Bed bedIn = DebugTools_Fixtures.Spawn(
                    map, bedDef, ThingDefOf.WoodLog, scene.OutfitStand.Position + new IntVec3(2, 0, 0),
                    Rot4.North) as Building_Bed;
                if (bedOut == null || bedIn == null)
                {
                    story.Check(false, "a bed can be built out past the door and one in the room");
                    return;
                }
                bedOut.Medical = true;
                bedIn.Medical = true;
                story.Note("a medical bed is built out past the door, and another in the room beside the stand");
                IntVec3 haulFrom = new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ);
                IntVec3 haulTo = new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ - 1);

                // The wound below must be the only thing that makes them urgent. Vanilla leaves a
                // generated colonist's conditions untended, so the scene removes them
                // (DebugTools_Fixtures.RemoveTendableConditions).
                story.Check(!HealthAIUtility.ShouldSeekMedicalRestUrgent(scene.Colonist),
                            name + " needs no doctor to begin with");
                story.Check(!scene.Comp.Covers(bedOut.Position, map) && scene.Comp.Covers(bedIn.Position, map),
                            "the stand's room holds the bed beside the stand and not the one past the door");
                story.Check(SwapNow(story, scene), name + " changes into the stand's duster for doctoring");
                if (!story.Check(scene.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                NotSentStraightBack(story, scene);

                bool was = ShiftChangeMod.Settings.medicalEmergenciesChangeFirst;
                Hediff wound = null;
                try
                {
                    ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = false;
                    story.Note("Medical emergencies change first is off");
                    story.Check(SendsToChange(scene, JobMaker.MakeJob(JobDefOf.LayDown, bedOut),
                                              JobTag.RestingForMedicalReasons),
                                "serving its room, resting to recover in the bed past the door changes them back first");

                    BodyPartRecord part = scene.Colonist.health.hediffSet.GetNotMissingParts().FirstOrDefault();
                    wound = HediffMaker.MakeHediff(HediffDefOf.Cut, scene.Colonist, part);
                    wound.Severity = 6f;
                    scene.Colonist.health.AddHediff(wound, part);
                    story.Note(name + " gets a bad cut, untended");
                    story.Check(HealthAIUtility.ShouldSeekMedicalRestUrgent(scene.Colonist) && !scene.Colonist.Downed,
                                "the untended wound makes " + name + " an urgent case, still on their feet");
                    story.Check(!SendsToChange(scene, JobMaker.MakeJob(JobDefOf.LayDown, bedOut),
                                               JobTag.RestingForMedicalReasons),
                                "so they go straight to the bed past the door in uniform");
                    story.Check(!SendsToChange(scene, JobMaker.MakeJob(JobDefOf.LayDown, bedOut)),
                                "and so they do when the game puts the lay-down back in the queue unmarked");
                    story.Check(!Patch_JobInterception.IsOrdinarySleep(
                                    scene.Colonist, JobMaker.MakeJob(JobDefOf.LayDown, bedIn), null),
                                "which never reads as ordinary sleep, that a sleep stand would dress them for");

                    // The shape vanilla's opportunistic haul leaves: the lay-down
                    // back at the head of the queue, untagged, and a haul first.
                    story.Check(SendsToChange(scene, JobMaker.MakeJob(JobDefOf.HaulToCell, haulFrom, haulTo)),
                                "a haul past the door changes them back");
                    scene.Colonist.jobs.jobQueue.EnqueueFirst(JobMaker.MakeJob(JobDefOf.LayDown, bedOut));
                    story.Note("the lay-down in the bed past the door is queued for " + name);
                    story.Check(!SendsToChange(scene, JobMaker.MakeJob(JobDefOf.HaulToCell, haulFrom, haulTo)),
                                "the same haul keeps the uniform on with the urgent lay-down queued behind it");

                    ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = true;
                    story.Note("Medical emergencies change first is turned on");
                    story.Check(SendsToChange(scene, JobMaker.MakeJob(JobDefOf.LayDown, bedOut),
                                              JobTag.RestingForMedicalReasons),
                                "and now they change back first");
                    ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = false;
                    story.Note("and off again");

                    scene.Comp.SetServes(CatchmentKind.Radius);
                    scene.Comp.SetServeRadius(3);
                    story.Note("Within a radius is chosen under Serves, and the radius set to 3 cells");
                    story.Check(scene.Comp.serves == CatchmentKind.Radius && scene.Comp.Covers(bedIn.Position, map)
                                && scene.Comp.OnShift,
                                "the stand now serves a radius that takes in the bed in the room, "
                                + name + " still on shift");
                    story.Check(!SendsToChange(scene, JobMaker.MakeJob(JobDefOf.LayDown, bedIn),
                                               JobTag.RestingForMedicalReasons),
                                "serving a radius, they go straight to the bed inside the ring");
                    scene.Colonist.health.RemoveHediff(wound);
                    wound = null;
                    story.Note("the cut is taken away");
                    story.Check(SendsToChange(scene, JobMaker.MakeJob(JobDefOf.LayDown, bedIn),
                                              JobTag.RestingForMedicalReasons),
                                "resting to recover in that bed with no such wound changes them back first");
                }
                finally
                {
                    ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = was;
                    if (wound != null)
                    {
                        scene.Colonist.health.RemoveHediff(wound);
                    }
                }
            }
        }

        /// <summary>
        /// A stand serving an allowed area dresses for a delivery its area holds whole: with the
        /// steel, the site and a second site painted in, a builder in their own clothes is sent to
        /// change first, through the mod's real decision and its reservation dry run. Steel, a
        /// site or a second site outside the area each send nobody, and serving its room or a
        /// radius over the same cells sends nobody either, for comparison. The same fetch filed
        /// under Hauling sends nobody, with the stand ticking Construction alone, though the
        /// colonist does construction: a builder it is filed for seldom has the frame as their
        /// next job. On shift, a site in the area keeps the uniform on wherever the steel lies,
        /// filed under Hauling or not, and a site outside it changes them back now, as a frame
        /// outside it does.
        /// </summary>
        [Test]
        [TestDescription("a stand serving an allowed area sends a builder to change for a delivery whose steel "
                         + "and sites are all painted in, and for no other")]
        internal IEnumerator AnAreaStandDressesForADeliveryItHoldsWhole()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true,
                                          capableOf: WorkType("Construction")),
                          InARoomABuilder, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef construction = WorkType("Construction");
                WorkGiverDef deliver = Giver("ConstructDeliverResourcesToBlueprints");
                WorkGiverDef finish = Giver("ConstructFinishFrames");
                // Vanilla files the same fetch under Hauling when hauling outranks construction, or
                // the blueprint needs more skill.
                WorkGiverDef haulDeliver = Giver("DeliverResourcesToBlueprints");
                if (construction == null || deliver == null || finish == null)
                {
                    story.Check(false, "the game has Construction work, delivering to blueprints and building frames");
                    return;
                }
                scene.Comp.ToggleWork(construction);
                MakeCalm(scene.Map);
                story.Note("Construction is ticked on the stand, and the map is calm");

                Map map = scene.Map;
                IntVec3 at = scene.OutfitStand.Position;
                Thing steel = Steel(map, at + new IntVec3(2, 0, 1));
                Thing steelOut = Steel(map, at + new IntVec3(4, 0, 1));
                Thing site = WallBlueprint(map, at + new IntVec3(2, 0, 3));
                Thing nextSite = WallBlueprint(map, at + new IntVec3(3, 0, 3));
                Thing siteOut = WallBlueprint(map, at + new IntVec3(4, 0, 4));
                if (site == null || nextSite == null || siteOut == null)
                {
                    story.Check(false, "the wall blueprints can be placed");
                    return;
                }
                Area_Allowed area =
                    NewArea(scene, "Shift test yard", at, steel.Position, site.Position, nextSite.Position);
                if (area == null)
                {
                    story.Check(false, "the map has room for another allowed area");
                    return;
                }
                story.Note("steel lies in two places and three wall blueprints are placed in the room; an allowed "
                           + "area, Shift test yard, is painted over the stand, the near steel and two of the "
                           + "blueprints");

                story.Check(!SendsToChange(scene, Delivery(deliver, steel, site)),
                            "serving its room, the stand sends nobody to change for a delivery inside the room");
                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(6);
                story.Note("Within a radius is chosen under Serves, and the radius set to 6 cells");
                story.Check(scene.Comp.Covers(steel.Position, map) && scene.Comp.Covers(site.Position, map),
                            "the ring takes in the steel and the site");
                story.Check(!SendsToChange(scene, Delivery(deliver, steel, site)),
                            "and serving it, the stand sends nobody to change for a delivery inside the ring");

                scene.Comp.SetServeArea(area);
                story.Note("Shift test yard is chosen under Serves");
                story.Check(scene.Comp.serves == CatchmentKind.Area, "the stand now serves the area");
                story.Check(SendsToChange(scene, Delivery(deliver, steel, site)),
                            "a delivery with its steel and its site painted in sends " + name
                            + ", in their own clothes, to change");
                story.Check(SendsToChange(scene, Delivery(deliver, steel, site, nextSite)),
                            "and so does one supplying a second site in the area");
                story.Check(!SendsToChange(scene, Delivery(deliver, steelOut, site)),
                            "steel outside the area sends nobody");
                story.Check(!SendsToChange(scene, Delivery(deliver, steel, siteOut)),
                            "nor does a site outside it");
                story.Check(!SendsToChange(scene, Delivery(deliver, steel, site, siteOut)),
                            "nor a trip that also supplies a site outside it");

                Pawn_WorkSettings work = scene.Colonist.workSettings;
                bool hauled = haulDeliver != null && haulDeliver.workType != construction && work != null;
                if (hauled)
                {
                    int priority = work.GetPriority(construction);
                    work.SetPriority(construction, 3);
                    story.Note(name + "'s Construction priority is set to 3");
                    story.Check(work.WorkIsActive(construction) && !scene.Comp.HandlesWork(haulDeliver.workType),
                                name + " does construction, and the stand does not tick "
                                + haulDeliver.workType.defName);
                    story.Check(!SendsToChange(scene, Delivery(haulDeliver, steel, site)),
                                "the same fetch filed under " + haulDeliver.workType.defName
                                + " sends nobody, though the stand serves Construction");
                    work.SetPriority(construction, priority);
                    story.Note("and set back as it was");
                }

                story.Check(SwapNow(story, scene), name + " changes into the stand's duster");
                if (!story.Check(scene.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                NotSentStraightBack(story, scene);
                story.Check(!SendsToChange(scene, Delivery(deliver, steelOut, site)),
                            "in uniform, a delivery from steel outside the area to a site inside keeps it on");
                story.Check(!hauled || !SendsToChange(scene, Delivery(haulDeliver, steelOut, site)),
                            "and so does the same fetch filed under Hauling, the stand ticking Construction alone");
                story.Check(SendsToChange(scene, Delivery(deliver, steel, siteOut)),
                            "and one to a site outside the area changes them back now");
                story.Check(SendsToChange(scene, WorkAt(finish, siteOut.Position)),
                            "as a frame outside it does");
            }
        }

        /// <summary>
        /// A work giver as the game names it, its defName after the name, since two can share a
        /// name (Construction and Hauling both deliver resources to frames).
        /// </summary>
        internal static string Called(WorkGiverDef giver)
        {
            return "\"" + (giver.label ?? giver.defName) + "\" (" + giver.defName + ")";
        }
    }
}
