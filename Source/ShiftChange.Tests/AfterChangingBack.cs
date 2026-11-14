using System.Collections;
using DevTools.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// What happens once a colonist changes back: a freed stand calls the next colonist over, and
    /// after Change back the stand they left does not dress them again until they move on, wake or
    /// are drafted.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.AfterChangingBack)]
    internal class AfterChangingBack : StoryFixture
    {
        /// <summary>What <see cref="HarnessFixtures.Stage"/> sets up with the pad enclosed.</summary>
        internal const string InAWalledRoom =
            "a stand holding a duster in a walled, roofed room, and a colonist beside it "
            + "in a shirt, trousers and a parka";

        /// <summary>The same, with a colonist who can do Doctor work.</summary>
        internal const string DoctorInAWalledRoom =
            "a stand holding a duster in a walled, roofed room, and a colonist able to doctor beside it "
            + "in a shirt, trousers and a parka";

        /// <summary>
        /// What <see cref="HarnessFixtures.StageWithDoorway(Map, CellRect, WorkTypeDef)"/> sets up
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
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                          capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                          DoctorInAWalledRoom, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    story.Check(false, "the game has Doctor work and its tending of colonists");
                    return;
                }
                fix.Comp.ToggleWork(doctor);
                story.Note("Doctor is ticked on the stand");

                MakeCalm(fix.Map);
                story.Check(fix.Map.dangerWatcher.DangerRating == StoryDanger.None,
                            "the map is calm, as in danger a freed stand calls nobody over");
                story.Check(SwapNow(story, fix), name + " changes into the duster for the shift");

                Job working;
                Pawn bare = DoctoringInHerOwnClothes(story, fix, doctor, tend, out working);
                string other = bare.LabelShort;
                story.Check(SwapPlan.WouldDress(bare, fix.Stand), other + " could wear the stand's outfit");
                story.Check(StillWorking(bare, tend), "and is still doctoring in her own clothes");

                story.Check(SwapNow(story, fix), name + " changes back, which frees the stand");
                story.Check(bare.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
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
            return Staged((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                          DoctorBesideADoor, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    story.Check(false, "the game has Doctor work and its tending of colonists");
                    return;
                }
                if (fix.Outside.Area == 0)
                {
                    story.Check(false, "the stand's room has a door with open ground outside it");
                    return;
                }
                fix.Comp.ToggleWork(doctor);
                MakeCalm(fix.Map);
                Patch_JobInterception.ChangedBackAt.Remove(fix.Pawn);
                story.Note("Doctor is ticked on the stand, the map holds no enemies, "
                           + "and no Change back holds the stand off " + name);

                story.Check(Diverts(fix, WorkJob(fix, tend)), "doctoring in the stand's room dresses " + name);
                story.Check(SwapNow(story, fix), name + " changes into the duster for the shift");
                if (!story.Check(fix.Comp.OnShift, "and is on shift at the stand"))
                {
                    return;
                }

                // The button's own path: it orders a change and sets the hold. The ordered change
                // is cut short and run through SwapNow, which steps the clock until it ends; a
                // change interrupted before anything moves announces nothing, so the cut leaves no
                // trace.
                Patch_ChangeBackGizmo.ChangeBack(fix.Pawn, fix.Comp);
                story.Note(name + "'s Change back button is pressed");
                story.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "the stand now holds off dressing " + name + " again");
                fix.Pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                story.Check(SwapNow(story, fix), name + " changes back into their own clothes");
                story.Check(!fix.Comp.OnShift, "so nothing of the stand's is out");

                story.Check(!Diverts(fix, WorkJob(fix, tend)),
                            "held off, doctoring in the same room does not dress " + name);
                story.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "and starting a job inside the room keeps the hold");

                // A stand in another room still dresses them meanwhile. It is outside the door and
                // the colonist stays inside, so the hold is still on while the work arm looks out
                // there. A room stand in the open serves nothing, so it serves the smallest ring
                // instead.
                IntVec3 outsideCell = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 1);
                Building_OutfitStand other = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                    fix.Map, fix.Stand.def, ThingDefOf.WoodLog, outsideCell, Rot4.North);
                CompShiftStand otherComp = other?.TryGetComp<CompShiftStand>();
                if (otherComp == null || !StockOne(other, "Apparel_Duster"))
                {
                    story.Check(false, "a second stand holding a duster can be put outside the door");
                    return;
                }
                otherComp.ToggleWork(doctor);
                otherComp.SetServes(CatchmentKind.Radius);
                otherComp.SetServeRadius(CompShiftStand.MinServeRadius);
                story.Note("a second stand holding a duster stands outside the door, Doctor ticked, "
                           + "serving within " + CompShiftStand.MinServeRadius + " cells");
                story.Check(other.GetRoom() != fix.Stand.GetRoom(), "the second stand is in another room");
                story.Check(otherComp.serves == CatchmentKind.Radius, "serving a radius around it");
                story.Check(Diverts(fix, HarnessGates.DoctoringAt(tend, outsideCell)),
                            "doctoring out there still dresses " + name + " at it");
                story.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "while the hold on their own room stays");
                other.Destroy();
                story.Note("the second stand is taken away");

                // Leaving ends the hold at the next job, whatever the job: here a Wait no work
                // gave, which nothing dresses for.
                IntVec3 inside = fix.Pawn.Position;
                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = outsideCell;
                story.Note(name + " steps outside the door and waits there");
                Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, outsideCell));
                story.Check(!Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "a job outside the room ends the hold");
                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = inside;
                story.Note(name + " is back inside");
                story.Check(Diverts(fix, WorkJob(fix, tend)),
                            "so back inside, doctoring in the room dresses " + name + " again");

                Patch_JobInterception.ChangedBackAt[fix.Pawn] = new Patch_JobInterception.ChangeBackLatch(fix.Stand);
                story.Note("the stand holds off dressing " + name + " again, as after Change back");
                story.Check(!Diverts(fix, WorkJob(fix, tend)),
                            "held off again, doctoring in the room does not dress " + name);
                if (fix.Pawn.drafter != null)
                {
                    fix.Pawn.drafter.Drafted = true;
                    fix.Pawn.drafter.Drafted = false;
                    story.Note(name + " is drafted and released");
                    story.Check(!Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                                "drafting ends the hold");
                    story.Check(Diverts(fix, WorkJob(fix, tend)),
                                "so doctoring in the room dresses " + name + " again");
                }

                // A freed stand calling colonists over honours the hold too, or a stand coming free
                // would dress again the colonist the player just took out of uniform.
                Patch_JobInterception.ChangedBackAt[fix.Pawn] = new Patch_JobInterception.ChangeBackLatch(fix.Stand);
                Job working = WorkingAt(tend, fix.Stand.Position + new IntVec3(3, 0, 2));
                fix.Pawn.jobs.StartJob(working, JobCondition.InterruptForced, null,
                    resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);
                story.Note("held off once more, " + name + " is set doctoring in the room");
                story.Check(StillWorking(fix.Pawn, tend),
                            "held off, " + name + " starts doctoring in their own clothes");
                Patch_JobInterception.Notify_StandFreed(fix.Comp, null);
                story.Note("the stand says it is free");
                story.Check(StillWorking(fix.Pawn, tend),
                            "and the stand coming free does not interrupt them to dress");
                Patch_JobInterception.ChangedBackAt.Remove(fix.Pawn);
                Patch_JobInterception.Notify_StandFreed(fix.Comp, null);
                story.Note("the hold is lifted by hand, and the stand says it is free again");
                story.Check(fix.Pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                            "without the hold, the same stand coming free interrupts " + name + " to dress");
                Settle(fix.Pawn);
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
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                          capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                          DoctorInAWalledRoom, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    story.Check(false, "the game has Doctor work and its tending of colonists");
                    return;
                }
                fix.Comp.ToggleWork(doctor);
                MakeCalm(fix.Map);
                story.Note("Doctor is ticked on the stand, and the map holds no enemies");

                story.Check(SwapNow(story, fix), name + " changes into the duster for the shift");
                NotSentStraightBack(story, fix);

                Job working;
                Pawn bare = DoctoringInHerOwnClothes(story, fix, doctor, tend, out working);
                string other = bare.LabelShort;
                story.Check(StillWorking(bare, tend), other + " is doctoring in the room in her own clothes");

                fix.Comp.SetServes(CatchmentKind.Radius);
                fix.Comp.SetServeRadius(10);
                story.Note("Within a radius is chosen under Serves, and the radius set to 10 cells");
                story.Check(fix.Comp.Covers(fix.Stand.Position + new IntVec3(3, 0, 2), fix.Map),
                            "the ring takes in " + other + "'s doctoring");
                story.Check(SwapNow(story, fix), name + " changes back, which frees the stand");
                story.Check(StillWorking(bare, tend),
                            "the freed stand, serving a radius, does not interrupt " + other);

                fix.Comp.SetServes(CatchmentKind.Room);
                story.Note("the stand is set back to serve its room");
                story.Check(SwapNow(story, fix), "serving its room again, " + name + " changes into the duster");
                story.Check(SwapNow(story, fix), "and changes back, which frees the stand");
                story.Check(bare.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                            "so the freed stand, serving its room, interrupts " + other + " to dress her");
                Patch_JobInterception.DeferredJob resumed;
                story.Check(Patch_JobInterception.DressedFor.TryGetValue(bare, out resumed) && resumed.Is(working),
                            "and her doctoring is remembered as the job she dressed for, "
                            + "so going back to it keeps the uniform on");
                Patch_JobInterception.DressedFor.Remove(bare);
                Settle(bare);
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
            return Staged((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                          DoctorBesideADoor, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
                JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
                if (doctor == null || tend == null || chess == null)
                {
                    story.Check(false, "the game has Doctor work, its tending of colonists, and chess");
                    return;
                }
                if (fix.Outside.Area == 0)
                {
                    story.Check(false, "the stand's room has a door with open ground outside it");
                    return;
                }
                fix.Comp.ToggleWork(doctor);
                fix.Comp.SetServes(CatchmentKind.Radius);
                fix.Comp.SetServeRadius(4);
                MakeCalm(fix.Map);
                story.Note("Doctor is ticked on the stand, which serves within 4 cells, "
                           + "and the map holds no enemies");

                IntVec3 at = fix.Stand.Position;
                CompShiftStand pooled = HarnessCatchments.StageStand(fix, at + new IntVec3(0, 0, 2));
                CompShiftStand play = HarnessCatchments.StageStand(fix, at + new IntVec3(0, 0, 4));
                // Three cells along the open strip from the door: a ring of the smallest radius
                // there reaches the work beside the door (3 cells) and neither the held stand
                // (about 3.6) nor the room's work.
                CompShiftStand open = HarnessCatchments.StageStand(fix, new IntVec3(fix.Outside.minX, 0, at.z + 3));
                if (pooled == null || play == null || open == null)
                {
                    story.Check(false, "three more stands holding a duster can be put up");
                    return;
                }
                pooled.ToggleWork(doctor);
                play.ToggleRecreation();
                open.ToggleWork(doctor);
                // A room stand in the open serves nothing, so this one serves a ring.
                open.SetServes(CatchmentKind.Radius);
                open.SetServeRadius(CompShiftStand.MinServeRadius);
                story.Note("three more stands holding a duster: a shared one two cells north of the first, "
                           + "Doctor ticked, serving its room; one four cells north, Recreation ticked; "
                           + "and one outside the door, Doctor ticked, serving within "
                           + CompShiftStand.MinServeRadius + " cells");

                IntVec3 inRoom = at + new IntVec3(2, 0, 2);
                IntVec3 joyAt = at + new IntVec3(3, 0, 3);
                IntVec3 doorside = new IntVec3(fix.Outside.minX, 0, at.z);
                IntVec3 pastRing = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 1);
                story.Check(fix.Comp.Covers(inRoom, fix.Map) && fix.Comp.Covers(doorside, fix.Map)
                            && !fix.Comp.Covers(pastRing, fix.Map),
                            "the first stand's ring takes in work in the room and beside the door, "
                            + "and not the far end outside");
                story.Check(pooled.serves == CatchmentKind.Room && pooled.Covers(at, fix.Map)
                            && !open.Covers(at, fix.Map),
                            "the shared stand's room holds the first stand, and the ring of the stand outside "
                            + "does not reach it");
                story.Check(open.Covers(doorside, fix.Map) && !open.Covers(inRoom, fix.Map),
                            "and that ring takes in work beside the door, not the room's");
                story.Check(SwapNow(story, fix), name + " changes into the first stand's duster for the shift");
                if (!story.Check(fix.Comp.OnShift, "and is on shift at it"))
                {
                    return;
                }
                NotSentStraightBack(story, fix);

                // The button orders a change; it is cut short and run through SwapNow, which steps
                // the clock until it ends.
                Patch_ChangeBackGizmo.ChangeBack(fix.Pawn, fix.Comp);
                fix.Pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                story.Note(name + "'s Change back button is pressed");
                story.Check(SwapNow(story, fix), name + " changes back into their own clothes");
                story.Check(!fix.Comp.OnShift && Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "nothing of the stand's is out, and it now holds off dressing them again");
                Settle(fix.Pawn);

                story.Check(!Diverts(fix, HarnessGates.DoctoringAt(tend, inRoom)),
                            "held off, doctoring in the ring dresses " + name
                            + " neither at the first stand nor at the shared stand beside it");
                story.Check(Diverts(fix, JobMaker.MakeJob(chess, joyAt, joyAt)),
                            "the recreation stand in the same room still dresses them for chess: "
                            + "the hold is on work only");
                story.Check(Diverts(fix, HarnessGates.DoctoringAt(tend, doorside)),
                            "and the stand outside still dresses them for doctoring beside the door, "
                            + "its ring not reaching the first stand");
                story.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "the hold stays all the while, since " + name + " never left the ring");

                Patch_JobInterception.ChangedBackAt.Remove(fix.Pawn);
                story.Note("the hold is lifted by hand");
                story.Check(Diverts(fix, HarnessGates.DoctoringAt(tend, inRoom)),
                            "with the hold lifted, the same doctoring in the ring dresses " + name);

                Patch_JobInterception.ChangedBackAt[fix.Pawn] = new Patch_JobInterception.ChangeBackLatch(fix.Stand);
                IntVec3 wasAt = fix.Pawn.Position;
                // One cell past the ring is beside a covered cell, where work at the ring's edge is
                // done from, so the pause there is not leaving.
                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = pastRing;
                story.Note("held off again, " + name + " stands outside one cell past the ring, and pauses");
                Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1));
                story.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "a pause one cell past the ring, beside a cell it takes in, keeps the hold");
                IntVec3 farEnd = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ);
                bool farClear = System.Array.TrueForAll(GenAdj.AdjacentCellsAndInside,
                                                        o => !fix.Comp.Covers(farEnd + o, fix.Map));
                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = farEnd;
                story.Note(name + " moves to the far end outside and waits there");
                Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, farEnd));
                story.Check(farClear && !Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "a job at the far end, with no cell of the ring beside it, ends the hold");
                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = wasAt;
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
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), InAWalledRoom, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                if (bedDef == null)
                {
                    story.Check(false, "the game has the bed");
                    return;
                }
                // Ticked before the bed lands: a bed makes the pad a bedroom, and on an automatic
                // stand ToggleRest would then turn sleep off.
                fix.Comp.ToggleRest();
                fix.Comp.SetServes(CatchmentKind.Radius);
                fix.Comp.SetServeRadius(3);
                MakeCalm(fix.Map);
                story.Note("Sleeping is ticked on the stand, which serves within 3 cells, "
                           + "and the map holds no enemies");

                IntVec3 at = fix.Stand.Position;
                IntVec3 inside = at + new IntVec3(1, 0, 2);
                Building_Bed bed = DebugTools_Fixtures.Spawn(
                    fix.Map, bedDef, ThingDefOf.WoodLog, at + new IntVec3(2, 0, 0), Rot4.North) as Building_Bed;
                if (bed == null)
                {
                    story.Check(false, "a bed can be put up inside the ring");
                    return;
                }
                story.Note("a bed stands two cells east of the stand, inside its ring");

                story.Check(SwapNow(story, fix), name + " changes into the duster for bed");
                if (!story.Check(fix.Comp.OnShift, "and is on shift at the stand"))
                {
                    return;
                }
                NotSentStraightBack(story, fix);

                // The button orders a change; it is cut short and run through SwapNow, which steps
                // the clock until it ends.
                Patch_ChangeBackGizmo.ChangeBack(fix.Pawn, fix.Comp);
                fix.Pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                story.Note(name + "'s Change back button is pressed");
                story.Check(SwapNow(story, fix), name + " changes back out of their sleepwear");
                story.Check(!fix.Comp.OnShift && Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "nothing of the stand's is out, and it now holds off dressing them again");
                Settle(fix.Pawn);

                story.Check(!Diverts(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed)),
                            "held off, going to bed does not dress " + name + " again");
                story.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "and lying down does not end the hold");

                // Vanilla's StartJob can put the lay-down back in the queue and start a haul first
                // (Pawn_JobTracker.TryOpportunisticJob).
                fix.Pawn.jobs.jobQueue.EnqueueFirst(JobMaker.MakeJob(JobDefOf.LayDown, bed));
                Diverts(fix, JobMaker.MakeJob(JobDefOf.HaulToCell, inside, inside));
                fix.Pawn.jobs.ClearQueuedJobs();
                story.Note("the game starts a haul ahead of lying down, which waits in the queue");
                story.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "a haul started ahead of going to bed is not waking up: the hold stays");

                IntVec3 wasAt = fix.Pawn.Position;
                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = bed.Position;
                story.Note(name + " is in the bed, and pauses between jobs");
                Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1));
                story.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "the pause between jobs is not waking up");
                Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, inside));
                story.Check(!Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "the first job after lying down is waking up, and ends the hold");

                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = wasAt;
                story.Note(name + " is back beside the stand");
                story.Check(Diverts(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed)),
                            "so going to bed again dresses " + name);

                // Pressed while they lie in bed: the press is what woke them.
                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = bed.Position;
                Patch_JobInterception.ChangedBackAt[fix.Pawn] = new Patch_JobInterception.ChangeBackLatch(fix.Stand);
                story.Note(name + " is in the bed again, and the stand holds off dressing them, "
                           + "as a Change back pressed there leaves it");
                Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, inside));
                story.Check(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                            "a Change back that woke them is not waking up: the hold lasts through the job after it");
                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = wasAt;
            }
        }

        /// <summary>
        /// A second colonist, a woman able to doctor, in a shirt and trousers of her own, two cells
        /// east and two north of the stand, set doctoring in the room on a job a freed stand may
        /// interrupt and that stays open while the clock runs (<see cref="HarnessFixtures.WorkingAt"/>).
        /// Cleared with the scene.
        /// </summary>
        internal static Pawn DoctoringInHerOwnClothes(Story story, Fixture fix, WorkTypeDef doctor,
                                                      WorkGiverDef tend, out Job working)
        {
            Pawn bare = DebugTools_Fixtures.AveragePawn(Gender.Female, "Bare", doctor);
            bare.apparel?.DestroyAll();
            bare.workSettings?.EnableAndInitialize();
            GenSpawn.Spawn(bare, fix.Stand.Position + new IntVec3(2, 0, 2), fix.Map, Rot4.North);
            fix.Extras.Add(bare);
            WearOne(bare, "Apparel_BasicShirt");
            WearOne(bare, "Apparel_Pants");
            working = WorkingAt(tend, fix.Stand.Position + new IntVec3(3, 0, 2));
            bare.jobs.StartJob(working, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);
            story.Note(bare.LabelShort + ", a woman able to doctor, joins the colony in the room "
                       + "in a shirt and trousers of her own, and starts doctoring there");
            return bare;
        }
    }
}
