using System.Collections;
using DevTools.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// What happens once a colonist changes back: a freed stand calls the next colonist over, and
    /// after Change back the stand they left does not dress them again until they move on, wake or
    /// are drafted.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.AfterChangingBack)]
    internal class AfterChangingBack : FeatureFixture
    {
        /// <summary>What <see cref="SceneKit.Stage"/> sets up with the pad enclosed.</summary>
        internal const string InAWalledRoom =
            "a stand holding a duster in a walled, roofed room, and a colonist beside it "
            + "in a shirt, trousers and a parka";

        /// <summary>The same, with a colonist who can do Doctor work.</summary>
        internal const string DoctorInAWalledRoom =
            "a stand holding a duster in a walled, roofed room, and a colonist able to doctor beside it "
            + "in a shirt, trousers and a parka";

        /// <summary>
        /// What <see cref="SceneKit.StageWithDoorway(Map, CellRect, WorkTypeDef)"/> sets up
        /// with a colonist who can do Doctor work.
        /// </summary>
        internal const string DoctorBesideADoor =
            "a stand holding a duster in a walled, roofed room, a door beside it to open ground, "
            + "and a colonist able to doctor beside the stand in a shirt, trousers and a parka";

        /// <summary>
        /// A stand freed by a colonist changing back interrupts a colonist already working in
        /// their own clothes in its room, to dress them. Guards the announcement's timing: the
        /// swap driver announces the freed stand from a global finish action, which the job
        /// tracker runs after it releases the departing colonist's reservation (a toil's finish
        /// action runs earlier). Announced from a toil, every candidate fails
        /// <c>CanReserveAndReach</c> against the stand's single reservation and the colonist is
        /// never interrupted at all, which makes the test pass or fail outright.
        /// </summary>
        [Test]
        [TestDescription("a colonist changing back frees the stand, which calls over a colonist "
                         + "doctoring in her own clothes in its room")]
        internal IEnumerator ChangingBackFreesTheStandForTheNext()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true,
                                          capableOf: WorkType("Doctor")),
                          DoctorInAWalledRoom, Body);

            void Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    scenario.Check(false, "the game has Doctor work and its tending of colonists");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                scenario.Note("Doctor is ticked on the stand");

                MakeCalm(scene.Map);
                scenario.Check(scene.Map.dangerWatcher.DangerRating == StoryDanger.None,
                            "the map is calm, as in danger a freed stand calls nobody over");
                scenario.Check(SwapNow(scenario, scene), name + " changes into the duster for the shift");

                Job working;
                Pawn bare = DoctoringInHerOwnClothes(scenario, scene, doctor, tend, out working);
                string other = bare.LabelShort;
                scenario.Check(SwapPlan.WouldDress(bare, scene.OutfitStand), other + " could wear the stand's outfit");
                scenario.Check(StillWorking(bare, tend), "and is still doctoring in her own clothes");

                scenario.Check(SwapNow(scenario, scene), name + " changes back, which frees the stand");
                scenario.Check(bare.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                            "the freed stand interrupts " + other + "'s doctoring to dress her");
            }
        }

        /// <summary>
        /// Change back's hold: after Change back, neither the stand they changed at nor another
        /// stand serving the same thing in that room dresses them until they leave the room; a
        /// stand in another room still dresses them meanwhile, drafting ends the hold, and a freed
        /// stand calling colonists over honours it. Every refusal sits beside the same job without
        /// the hold, which dresses them (docs/DESIGN.md, "Change back").
        /// </summary>
        [Test]
        [TestDescription("after Change back, doctoring in the same room does not dress them again "
                         + "until they leave it or are drafted, while a stand in another room still does")]
        internal IEnumerator ChangeBackHoldsTheRoomUntilTheyLeave()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, WorkType("Doctor")),
                          DoctorBesideADoor, Body);

            void Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    scenario.Check(false, "the game has Doctor work and its tending of colonists");
                    return;
                }
                if (scene.Outside.Area == 0)
                {
                    scenario.Check(false, "the stand's room has a door with open ground outside it");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                Patch_JobInterception.ChangedBackAt.Remove(scene.Colonist);
                scenario.Note("Doctor is ticked on the stand, the map holds no enemies, "
                           + "and no Change back holds the stand off " + name);

                scenario.Check(SendsToChangeKeepingQueue(scene, WorkJob(scene, tend)),
                            "doctoring in the stand's room dresses " + name);
                scenario.Check(SwapNow(scenario, scene), name + " changes into the duster for the shift");
                if (!scenario.Check(scene.Comp.OnShift, "and is on shift at the stand"))
                {
                    return;
                }

                // The button's own path: it orders a change and sets the hold. The ordered change
                // is cut short and run through SwapNow, which steps the clock until it ends; a
                // change interrupted before anything moves announces nothing, so the cut leaves no
                // trace.
                Patch_ChangeBackGizmo.ChangeBack(scene.Colonist, scene.Comp);
                scenario.Note(name + "'s Change back button is pressed");
                scenario.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "the stand now holds off dressing " + name + " again");
                scene.Colonist.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                scenario.Check(SwapNow(scenario, scene), name + " changes back into their own clothes");
                scenario.Check(!scene.Comp.OnShift, "so nothing of the stand's is out");

                scenario.Check(!SendsToChangeKeepingQueue(scene, WorkJob(scene, tend)),
                            "held off, doctoring in the same room does not dress " + name);
                scenario.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "and starting a job inside the room keeps the hold");

                // A stand in another room still dresses them meanwhile. It is outside the door and
                // the colonist stays inside, so the hold is still on while the work arm looks out
                // there. A room stand in the open serves nothing, so it serves the smallest ring
                // instead.
                IntVec3 outsideCell = new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ - 1);
                Building_OutfitStand other = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                    scene.Map, scene.OutfitStand.def, ThingDefOf.WoodLog, outsideCell, Rot4.North);
                CompShiftStand otherComp = other?.TryGetComp<CompShiftStand>();
                if (otherComp == null || !StockOne(other, "Apparel_Duster"))
                {
                    scenario.Check(false, "a second stand holding a duster can be put outside the door");
                    return;
                }
                otherComp.ToggleWork(doctor);
                otherComp.SetServes(CatchmentKind.Radius);
                otherComp.SetServeRadius(CompShiftStand.MinServeRadius);
                scenario.Note("a second stand holding a duster stands outside the door, Doctor ticked, "
                           + "serving within " + CompShiftStand.MinServeRadius + " cells");
                scenario.Check(other.GetRoom() != scene.OutfitStand.GetRoom(), "the second stand is in another room");
                scenario.Check(otherComp.serves == CatchmentKind.Radius, "serving a radius around it");
                scenario.Check(SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, outsideCell)),
                            "doctoring out there still dresses " + name + " at it");
                scenario.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "while the hold on their own room stays");
                other.Destroy();
                scenario.Note("the second stand is taken away");

                // Leaving ends the hold at the next job, whatever the job: here a Wait no work
                // gave, which nothing dresses for.
                IntVec3 inside = scene.Colonist.Position;
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = outsideCell;
                scenario.Note(name + " steps outside the door and waits there");
                SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(JobDefOf.Wait, outsideCell));
                scenario.Check(!Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "a job outside the room ends the hold");
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = inside;
                scenario.Note(name + " is back inside");
                scenario.Check(SendsToChangeKeepingQueue(scene, WorkJob(scene, tend)),
                            "so back inside, doctoring in the room dresses " + name + " again");

                Patch_JobInterception.ChangedBackAt[scene.Colonist] =
                    new Patch_JobInterception.ChangeBackLatch(scene.OutfitStand);
                scenario.Note("the stand holds off dressing " + name + " again, as after Change back");
                scenario.Check(!SendsToChangeKeepingQueue(scene, WorkJob(scene, tend)),
                            "held off again, doctoring in the room does not dress " + name);
                if (scene.Colonist.drafter != null)
                {
                    scene.Colonist.drafter.Drafted = true;
                    scene.Colonist.drafter.Drafted = false;
                    scenario.Note(name + " is drafted and released");
                    scenario.Check(!Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                                "drafting ends the hold");
                    scenario.Check(SendsToChangeKeepingQueue(scene, WorkJob(scene, tend)),
                                "so doctoring in the room dresses " + name + " again");
                }

                // A freed stand calling colonists over honours the hold too, or a stand coming free
                // would dress again the colonist the player just took out of uniform.
                Patch_JobInterception.ChangedBackAt[scene.Colonist] =
                    new Patch_JobInterception.ChangeBackLatch(scene.OutfitStand);
                Job working = WorkingAt(tend, scene.OutfitStand.Position + new IntVec3(3, 0, 2));
                scene.Colonist.jobs.StartJob(working, JobCondition.InterruptForced, null,
                    resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);
                scenario.Note("held off once more, " + name + " is set doctoring in the room");
                scenario.Check(StillWorking(scene.Colonist, tend),
                            "held off, " + name + " starts doctoring in their own clothes");
                Patch_JobInterception.Notify_StandFreed(scene.Comp, null);
                scenario.Note("the stand says it is free");
                scenario.Check(StillWorking(scene.Colonist, tend),
                            "and the stand coming free does not interrupt them to dress");
                Patch_JobInterception.ChangedBackAt.Remove(scene.Colonist);
                Patch_JobInterception.Notify_StandFreed(scene.Comp, null);
                scenario.Note("the hold is lifted by hand, and the stand says it is free again");
                scenario.Check(scene.Colonist.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                            "without the hold, the same stand coming free interrupts " + name + " to dress");
                ClearSwapDecision(scene.Colonist);
            }
        }

        /// <summary>
        /// A freed stand serving a radius interrupts nobody, where a stand serving its room calls
        /// over a colonist already working in their own clothes inside it. Both halves on one
        /// scene, the room half showing the colonist is there to be called.
        /// </summary>
        [Test]
        [TestDescription("a stand serving a radius, freed by a colonist changing back, does not interrupt "
                         + "a colonist doctoring in her own clothes, where serving its room it does")]
        internal IEnumerator AFreedRadiusStandInterruptsNobody()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true,
                                          capableOf: WorkType("Doctor")),
                          DoctorInAWalledRoom, Body);

            void Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    scenario.Check(false, "the game has Doctor work and its tending of colonists");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                MakeCalm(scene.Map);
                scenario.Note("Doctor is ticked on the stand, and the map holds no enemies");

                scenario.Check(SwapNow(scenario, scene), name + " changes into the duster for the shift");
                NotSentStraightBack(scenario, scene);

                Job working;
                Pawn bare = DoctoringInHerOwnClothes(scenario, scene, doctor, tend, out working);
                string other = bare.LabelShort;
                scenario.Check(StillWorking(bare, tend), other + " is doctoring in the room in her own clothes");

                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(10);
                scenario.Note("Within a radius is chosen under Serves, and the radius set to 10 cells");
                scenario.Check(scene.Comp.Covers(scene.OutfitStand.Position + new IntVec3(3, 0, 2), scene.Map),
                            "the ring takes in " + other + "'s doctoring");
                scenario.Check(SwapNow(scenario, scene), name + " changes back, which frees the stand");
                scenario.Check(StillWorking(bare, tend),
                            "the freed stand, serving a radius, does not interrupt " + other);

                scene.Comp.SetServes(CatchmentKind.Room);
                scenario.Note("the stand is set back to serve its room");
                scenario.Check(SwapNow(scenario, scene), "serving its room again, " + name + " changes into the duster");
                scenario.Check(SwapNow(scenario, scene), "and changes back, which frees the stand");
                scenario.Check(bare.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                            "so the freed stand, serving its room, interrupts " + other + " to dress her");
                Patch_JobInterception.DeferredJob resumed;
                scenario.Check(Patch_JobInterception.DressedFor.TryGetValue(bare, out resumed) && resumed.Is(working),
                            "and her doctoring is remembered as the job she dressed for, "
                            + "so going back to it keeps the uniform on");
                Patch_JobInterception.DressedFor.Remove(bare);
                ClearSwapDecision(bare);
            }
        }

        /// <summary>
        /// Change back's hold is per stand and per thing served. After Change back at a stand
        /// serving a radius, neither it nor a shared room stand beside it, serving the same work,
        /// dresses the colonist. A recreation stand in the same room does, and so does a radius
        /// stand in the open whose ring does not reach the held one. Lifting the hold by hand
        /// shows the refusal is the hold's. One cell past the ring, beside a covered cell, still
        /// keeps it, and a job with no covered cell beside it ends it.
        /// </summary>
        [Test]
        [TestDescription("after Change back at a stand serving a radius, its work dresses them at neither "
                         + "it nor a stand beside it, while recreation and a ring that misses it still do, "
                         + "until they leave the ring")]
        internal IEnumerator ChangeBackHoldsOnlyItsStandAndWork()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, WorkType("Doctor")),
                          DoctorBesideADoor, Body);

            void Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
                if (doctor == null || tend == null || chess == null)
                {
                    scenario.Check(false, "the game has Doctor work, its tending of colonists, and chess");
                    return;
                }
                if (scene.Outside.Area == 0)
                {
                    scenario.Check(false, "the stand's room has a door with open ground outside it");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(4);
                MakeCalm(scene.Map);
                scenario.Note("Doctor is ticked on the stand, which serves within 4 cells, "
                           + "and the map holds no enemies");

                IntVec3 at = scene.OutfitStand.Position;
                CompShiftStand pooled = CatchmentKit.StageStand(scene, at + new IntVec3(0, 0, 2));
                CompShiftStand play = CatchmentKit.StageStand(scene, at + new IntVec3(0, 0, 4));
                // Three cells along the open strip from the door: a ring of the smallest radius
                // there reaches the work beside the door (3 cells) and neither the held stand
                // (about 3.6) nor the room's work.
                CompShiftStand open = CatchmentKit.StageStand(scene, new IntVec3(scene.Outside.minX, 0, at.z + 3));
                if (pooled == null || play == null || open == null)
                {
                    scenario.Check(false, "three more stands holding a duster can be put up");
                    return;
                }
                pooled.ToggleWork(doctor);
                play.ToggleRecreation();
                open.ToggleWork(doctor);
                // A room stand in the open serves nothing, so this one serves a ring.
                open.SetServes(CatchmentKind.Radius);
                open.SetServeRadius(CompShiftStand.MinServeRadius);
                scenario.Note("three more stands holding a duster: a shared one two cells north of the first, "
                           + "Doctor ticked, serving its room; one four cells north, Recreation ticked; "
                           + "and one outside the door, Doctor ticked, serving within "
                           + CompShiftStand.MinServeRadius + " cells");

                IntVec3 inRoom = at + new IntVec3(2, 0, 2);
                IntVec3 joyAt = at + new IntVec3(3, 0, 3);
                IntVec3 doorside = new IntVec3(scene.Outside.minX, 0, at.z);
                IntVec3 pastRing = new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ - 1);
                scenario.Check(scene.Comp.Covers(inRoom, scene.Map) && scene.Comp.Covers(doorside, scene.Map)
                            && !scene.Comp.Covers(pastRing, scene.Map),
                            "the first stand's ring takes in work in the room and beside the door, "
                            + "and not the far end outside");
                scenario.Check(pooled.serves == CatchmentKind.Room && pooled.Covers(at, scene.Map)
                            && !open.Covers(at, scene.Map),
                            "the shared stand's room holds the first stand, and the ring of the stand outside "
                            + "does not reach it");
                scenario.Check(open.Covers(doorside, scene.Map) && !open.Covers(inRoom, scene.Map),
                            "and that ring takes in work beside the door, not the room's");
                scenario.Check(SwapNow(scenario, scene), name + " changes into the first stand's duster for the shift");
                if (!scenario.Check(scene.Comp.OnShift, "and is on shift at it"))
                {
                    return;
                }
                NotSentStraightBack(scenario, scene);

                // The button orders a change; it is cut short and run through SwapNow, which steps
                // the clock until it ends.
                Patch_ChangeBackGizmo.ChangeBack(scene.Colonist, scene.Comp);
                scene.Colonist.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                scenario.Note(name + "'s Change back button is pressed");
                scenario.Check(SwapNow(scenario, scene), name + " changes back into their own clothes");
                scenario.Check(!scene.Comp.OnShift && Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "nothing of the stand's is out, and it now holds off dressing them again");
                ClearSwapDecision(scene.Colonist);

                scenario.Check(!SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, inRoom)),
                            "held off, doctoring in the ring dresses " + name
                            + " neither at the first stand nor at the shared stand beside it");
                scenario.Check(SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(chess, joyAt, joyAt)),
                            "the recreation stand in the same room still dresses them for chess: "
                            + "the hold is on work only");
                scenario.Check(SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, doorside)),
                            "and the stand outside still dresses them for doctoring beside the door, "
                            + "its ring not reaching the first stand");
                scenario.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "the hold stays all the while, since " + name + " never left the ring");

                Patch_JobInterception.ChangedBackAt.Remove(scene.Colonist);
                scenario.Note("the hold is lifted by hand");
                scenario.Check(SendsToChangeKeepingQueue(scene, GateKit.DoctoringAt(tend, inRoom)),
                            "with the hold lifted, the same doctoring in the ring dresses " + name);

                Patch_JobInterception.ChangedBackAt[scene.Colonist] =
                    new Patch_JobInterception.ChangeBackLatch(scene.OutfitStand);
                IntVec3 wasAt = scene.Colonist.Position;
                // One cell past the ring is beside a covered cell, where work at the ring's edge is
                // done from, so the pause there is not leaving.
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = pastRing;
                scenario.Note("held off again, " + name + " stands outside one cell past the ring, and pauses");
                SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1));
                scenario.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "a pause one cell past the ring, beside a cell it takes in, keeps the hold");
                IntVec3 farEnd = new IntVec3(scene.Outside.minX, 0, scene.Outside.maxZ);
                bool farClear = System.Array.TrueForAll(GenAdj.AdjacentCellsAndInside,
                                                        o => !scene.Comp.Covers(farEnd + o, scene.Map));
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = farEnd;
                scenario.Note(name + " moves to the far end outside and waits there");
                SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(JobDefOf.Wait, farEnd));
                scenario.Check(farClear && !Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "a job at the far end, with no cell of the ring beside it, ends the hold");
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = wasAt;
            }
        }

        /// <summary>
        /// At a stand for sleeping, Change back's hold lasts through the night and ends on waking.
        /// After Change back, going to bed does not dress the colonist again, and the job
        /// tracker's pause does not count as waking; the first job after lying down ends the hold,
        /// and bed dresses them again, which shows the refusal was the hold's. A haul the game
        /// starts ahead of lying down, putting that back in the queue, is not the waking either,
        /// and neither is a Change back that wakes a sleeper: the hold lasts through the job after
        /// it.
        /// </summary>
        [Test]
        [TestDescription("after Change back at a stand for sleeping, going to bed does not dress them again "
                         + "until they have slept and woken")]
        internal IEnumerator ChangeBackAtASleepStandLastsTheNight()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InAWalledRoom, Body);

            void Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                if (bedDef == null)
                {
                    scenario.Check(false, "the game has the bed");
                    return;
                }
                // Ticked before the bed lands: a bed makes the pad a bedroom, and on an automatic
                // stand ToggleRest would then turn sleep off.
                scene.Comp.ToggleRest();
                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(3);
                MakeCalm(scene.Map);
                scenario.Note("Sleeping is ticked on the stand, which serves within 3 cells, "
                           + "and the map holds no enemies");

                IntVec3 at = scene.OutfitStand.Position;
                IntVec3 inside = at + new IntVec3(1, 0, 2);
                Building_Bed bed = DebugTools_Fixtures.Spawn(
                    scene.Map, bedDef, ThingDefOf.WoodLog, at + new IntVec3(2, 0, 0), Rot4.North) as Building_Bed;
                if (bed == null)
                {
                    scenario.Check(false, "a bed can be put up inside the ring");
                    return;
                }
                scenario.Note("a bed stands two cells east of the stand, inside its ring");

                scenario.Check(SwapNow(scenario, scene), name + " changes into the duster for bed");
                if (!scenario.Check(scene.Comp.OnShift, "and is on shift at the stand"))
                {
                    return;
                }
                NotSentStraightBack(scenario, scene);

                // The button orders a change; it is cut short and run through SwapNow, which steps
                // the clock until it ends.
                Patch_ChangeBackGizmo.ChangeBack(scene.Colonist, scene.Comp);
                scene.Colonist.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                scenario.Note(name + "'s Change back button is pressed");
                scenario.Check(SwapNow(scenario, scene), name + " changes back out of their sleepwear");
                scenario.Check(!scene.Comp.OnShift && Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "nothing of the stand's is out, and it now holds off dressing them again");
                ClearSwapDecision(scene.Colonist);

                scenario.Check(!SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(JobDefOf.LayDown, bed)),
                            "held off, going to bed does not dress " + name + " again");
                scenario.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "and lying down does not end the hold");

                // Vanilla's StartJob can put the lay-down back in the queue and start a haul first
                // (Pawn_JobTracker.TryOpportunisticJob).
                scene.Colonist.jobs.jobQueue.EnqueueFirst(JobMaker.MakeJob(JobDefOf.LayDown, bed));
                SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(JobDefOf.HaulToCell, inside, inside));
                scene.Colonist.jobs.ClearQueuedJobs();
                scenario.Note("the game starts a haul ahead of lying down, which waits in the queue");
                scenario.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "a haul started ahead of going to bed is not waking up: the hold stays");

                IntVec3 wasAt = scene.Colonist.Position;
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = bed.Position;
                scenario.Note(name + " is in the bed, and pauses between jobs");
                SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1));
                scenario.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "the pause between jobs is not waking up");
                SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(JobDefOf.Wait, inside));
                scenario.Check(!Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "the first job after lying down is waking up, and ends the hold");

                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = wasAt;
                scenario.Note(name + " is back beside the stand");
                scenario.Check(SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(JobDefOf.LayDown, bed)),
                            "so going to bed again dresses " + name);

                // Pressed while they lie in bed: the press is what woke them.
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = bed.Position;
                Patch_JobInterception.ChangedBackAt[scene.Colonist] =
                    new Patch_JobInterception.ChangeBackLatch(scene.OutfitStand);
                scenario.Note(name + " is in the bed again, and the stand holds off dressing them, "
                           + "as a Change back pressed there leaves it");
                SendsToChangeKeepingQueue(scene, JobMaker.MakeJob(JobDefOf.Wait, inside));
                scenario.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(scene.Colonist),
                            "a Change back that woke them is not waking up: the hold lasts through the job after it");
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = wasAt;
            }
        }

        /// <summary>
        /// A second colonist, a woman able to doctor, in a shirt and trousers of her own, two cells
        /// east and two north of the stand, set doctoring in the room on a job a freed stand may
        /// interrupt and that stays open while the clock runs (<see cref="SceneKit.WorkingAt"/>).
        /// Cleared with the scene.
        /// </summary>
        internal static Pawn DoctoringInHerOwnClothes(Scenario scenario, Scene scene, WorkTypeDef doctor,
                                                      WorkGiverDef tend, out Job working)
        {
            Pawn bare = DebugTools_Fixtures.AveragePawn(Gender.Female, "Bare", doctor);
            bare.apparel?.DestroyAll();
            bare.workSettings?.EnableAndInitialize();
            GenSpawn.Spawn(bare, scene.OutfitStand.Position + new IntVec3(2, 0, 2), scene.Map, Rot4.North);
            scene.Extras.Add(bare);
            WearOne(bare, "Apparel_BasicShirt");
            WearOne(bare, "Apparel_Pants");
            working = WorkingAt(tend, scene.OutfitStand.Position + new IntVec3(3, 0, 2));
            bare.jobs.StartJob(working, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);
            scenario.Note(bare.LabelShort + ", a woman able to doctor, joins the colony in the room "
                       + "in a shirt and trousers of her own, and starts doctoring there");
            return bare;
        }
    }
}
