using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DevTools.Testing;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Errands that ride along in uniform with the work a stand serves: feeding a patient, a meal
    /// run, a haul on the way to a bill. Each is part of work already under way, where changing
    /// out and back in again costs two walks to the stand and buys nothing.
    ///
    /// <para>Changing back judges a job by where its first target is, which for a meal or an
    /// ingredient is the storeroom, so each of these would otherwise send a colonist who is about
    /// to come straight back to the stand to change first.</para>
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.RidingAlong)]
    internal class RidingAlong : StoryFixture
    {
        /// <summary>What <see cref="HarnessFixtures.Stage"/> sets up with the pad enclosed.</summary>
        internal const string InARoom =
            "a stand holding a duster in the corner of a walled room, "
            + "and a colonist beside it in a shirt, trousers and a parka";

        /// <summary>What <see cref="HarnessFixtures.StageWithDoorway(Map, CellRect)"/> sets up.</summary>
        internal const string InARoomWithADoor =
            "a stand holding a duster in the corner of a walled room, a door beside it onto open ground, "
            + "and a colonist beside it in a shirt, trousers and a parka";

        /// <summary>The same, with a colonist able to doctor.</summary>
        internal const string InARoomWithADoorAndADoctor =
            "a stand holding a duster in the corner of a walled room, a door beside it onto open ground, "
            + "and a colonist who can doctor beside it in a shirt, trousers and a parka";

        /// <summary>The same, with a colonist able to haul.</summary>
        internal const string InARoomWithADoorAndAHauler =
            "a stand holding a duster in the corner of a walled room, a door beside it onto open ground, "
            + "and a colonist who can haul beside it in a shirt, trousers and a parka";

        /// <summary>
        /// Feeding dresses nobody, and a colonist whose stand serves its work is not sent back to
        /// change for it, for a patient in the stand's room. In another stand's outfit, pyjamas
        /// here, they change back first. A patient elsewhere is
        /// <see cref="MealForAPatientInTheRoomKeepsTheUniformOn"/>'s. Shift Change passes feeding
        /// over because a meal run needs no uniform and neither target places it: the meal, the
        /// first target, puts the job wherever the meal is stored, and the patient, where it ends
        /// up, would dress a bare doctor for a trip that starts at the storeroom.
        ///
        /// <para>Every refusal is paired with the same job handed out as tending to patients,
        /// which Shift Change does not pass over, and which still dresses and still changes them
        /// back. Without that pair, a feeding job failing its reservation dry run would satisfy
        /// every negative here.</para>
        /// </summary>
        [Test]
        [TestDescription("a doctor or warden in uniform fetches a meal from outside the room for a patient "
                         + "or a prisoner without changing back, and nobody changes to feed one")]
        internal IEnumerator FeedingAPatientKeepsTheUniformOn()
        {
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
                ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
                if (doctor == null || tend == null || mealDef == null)
                {
                    story.Check(false, "the game has the Doctor work type, its tend to patients work "
                                       + "and the simple meal");
                    return;
                }
                // Doctoring for the tending each refusal is paired with, and every feeding giver's
                // own work.
                List<WorkTypeDef> served = HarnessRideAlong.FeedingWork(doctor);
                ToggleEach(fix, served);
                MakeCalm(fix.Map);
                string works = string.Join(", ", served.Select(w => WorkTypeLabels.Of(w)));
                story.Note("ticked on the stand: " + works + "; the map is calm");

                Map map = fix.Map;
                Room room = fix.Stand.GetRoom();
                Pawn patient = AnotherColonist(story, fix, Gender.Female, "Patient");
                StayPut(patient);
                Thing mealHere = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef),
                                                fix.Stand.Position + new IntVec3(3, 0, 3), map);
                IntVec3 outside = OpenGround(fix, fix.Stand.Position + new IntVec3(8, 0, 0));
                if (!outside.InBounds(map) || outside.GetRoom(map) == room)
                {
                    story.Check(false, "there is open ground outside the stand's room for a meal");
                    return;
                }
                Thing mealAway = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), outside, map);
                string patientName = patient.LabelShort;
                story.Note(patientName + " stays where she is, as a patient in bed would; a simple meal "
                           + "lies in the stand's room, and another outside it");

                List<KeyValuePair<WorkGiverDef, JobDef>> rows = new List<KeyValuePair<WorkGiverDef, JobDef>>();
                foreach (string giverName in HarnessRideAlong.FeedPatientGivers)
                {
                    WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(giverName);
                    if (giver != null)
                    {
                        rows.Add(new KeyValuePair<WorkGiverDef, JobDef>(giver, JobDefOf.FeedPatient));
                    }
                }
                foreach (string giverName in HarnessRideAlong.DeliverFoodGivers)
                {
                    WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(giverName);
                    if (giver != null)
                    {
                        rows.Add(new KeyValuePair<WorkGiverDef, JobDef>(giver, JobDefOf.DeliverFood));
                    }
                }

                IntVec3 read = Patch_JobInterception.TargetCell(
                    HarnessRideAlong.FeedingJob(JobDefOf.FeedPatient, mealAway, patient, tend), map);
                story.Check(served.All(fix.Comp.HandlesWork),
                            "the stand serves doctoring and the work of every way to feed someone (" + works + ")");
                story.Check(rows.Count >= 5,
                            "the game's five Core ways to feed a patient or a prisoner are there ("
                            + rows.Count + " of seven; Biotech adds the two with hemogen)");
                story.Check(read == mealAway.Position,
                            "feeding someone counts as happening where the meal is, not where the patient is"
                            + (read == mealAway.Position
                                ? ""
                                : ": counted at " + read + ", the meal at " + mealAway.Position
                                  + " (put down at " + outside + ", "
                                  + (mealAway.Spawned ? "on the map" : "not on the map") + "), and on that cell: "
                                  + string.Join(", ", mealAway.Position.GetThingList(map).Select(t => t.LabelShort))));

                // Not yet dressed, with the meal stored in the stand's room: the job is this room's.
                story.Check(Probe(fix, HarnessRideAlong.FeedingJob(JobDefOf.FeedPatient, mealHere, patient, tend)),
                            "a meal from the room for " + patientName + ", handed out as tending to patients, "
                            + "sends " + name + " to change: such a job can dress");
                foreach (KeyValuePair<WorkGiverDef, JobDef> row in rows)
                {
                    story.Check(!Probe(fix, HarnessRideAlong.FeedingJob(row.Value, mealHere, patient, row.Key)),
                                FeedingGiver(row.Key) + " sends nobody to change, even with the meal in the stand's room");
                }

                story.Check(SwapNow(story, fix), name + " changes into the stand's outfit");
                if (!story.Check(fix.Comp.OnShift, "and is on shift"))
                {
                    return;
                }

                story.Check(Probe(fix, HarnessRideAlong.FeedingJob(JobDefOf.FeedPatient, mealAway, patient, tend)),
                            "handed out as tending to patients, the meal from outside the room still changes "
                            + name + " back first");
                foreach (KeyValuePair<WorkGiverDef, JobDef> row in rows)
                {
                    story.Check(!Probe(fix, HarnessRideAlong.FeedingJob(row.Value, mealAway, patient, row.Key)),
                                FeedingGiver(row.Key) + " keeps the uniform on while " + name
                                + " fetches the meal from outside the room");
                }

                // The meal run keeps the uniform on only for a stand serving its work; in another
                // stand's outfit, pyjamas here, it changes them back first.
                ToggleEach(fix, served);
                fix.Comp.ToggleRest();
                story.Note("the stand's work is unticked and Sleeping ticked instead, "
                           + "so its outfit is now " + name + "'s pyjamas");
                story.Check(fix.Comp.HandlesRest() && !served.Any(fix.Comp.HandlesWork) && fix.Comp.OnShift,
                            "the stand now serves sleep and none of that work, and " + name + " still wears its outfit");
                foreach (KeyValuePair<WorkGiverDef, JobDef> row in rows)
                {
                    story.Check(Probe(fix, HarnessRideAlong.FeedingJob(row.Value, mealAway, patient, row.Key)),
                                FeedingGiver(row.Key) + " changes " + name + " out of pyjamas before the meal run");
                }
                story.Check(fix.Comp.OnShift, "and " + name + " is still on shift at the end");
            }
        }

        /// <summary>
        /// A meal run keeps the uniform on only for a patient inside. On shift at a stand serving
        /// the work of both feeding patients and delivering food to prisoners, with the meal stored
        /// out past the door: a patient in the room keeps the uniform on, and one out past the door
        /// changes them back now, while the stand is near. A prisoner's meal left at a spot out
        /// there does the same, and at a spot in the room keeps it on. A haul the game slips in
        /// ahead of the meal run keeps the uniform on when the meal run would, and only then. In
        /// pyjamas, a meal from the room for a patient in the room is the room's, and one carried
        /// to a patient out past the door changes them back first.
        /// </summary>
        [Test]
        [TestDescription("a colonist in uniform carrying a meal to a patient in the stand's room keeps it on, "
                         + "and changes back first for one out past the door, a haul slipped in ahead too")]
        internal IEnumerator MealForAPatientInTheRoomKeepsTheUniformOn()
        {
            return Staged((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                          InARoomWithADoorAndADoctor, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkGiverDef feed = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorFeedHumanlikes");
                WorkGiverDef deliver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DeliverFoodToPrisoner");
                ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
                if (feed?.workType == null || deliver?.workType == null || mealDef == null)
                {
                    story.Check(false, "the game has feeding patients and delivering food to prisoners, "
                                       + "their work types, and the simple meal");
                    return;
                }
                // Doctoring and warden work in vanilla; Complex Jobs moves both givers to work of
                // its own.
                List<WorkTypeDef> served =
                    new[] { feed.workType, deliver.workType }.Distinct().ToList();
                if (fix.Outside.Height < 4)
                {
                    story.Check(false, "the room has a door and four cells of open ground outside it");
                    return;
                }
                ToggleEach(fix, served);
                MakeCalm(fix.Map);
                story.Note("ticked on the stand: " + string.Join(", ", served.Select(w => WorkTypeLabels.Of(w)))
                           + "; the map is calm");

                Map map = fix.Map;
                Room room = fix.Stand.GetRoom();
                IntVec3 at = fix.Stand.Position;
                int x = fix.Outside.minX;
                int top = fix.Outside.maxZ;
                Pawn patientIn = AnotherColonist(story, fix, Gender.Female, "Patient");
                Pawn patientOut = AnotherColonist(story, fix, Gender.Male, "Outpatient",
                                                  new IntVec3(x, 0, top), "out past the door");
                StayPut(patientIn);
                StayPut(patientOut);
                Thing mealOut = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), new IntVec3(x, 0, top - 1), map);
                Thing mealIn = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), at + new IntVec3(3, 0, 3), map);
                Thing steel = HarnessCatchments.Steel(map, new IntVec3(x, 0, top - 2));
                IntVec3 store = new IntVec3(x, 0, top - 3);
                string inside = patientIn.LabelShort;
                string outside = patientOut.LabelShort;
                story.Note("both stay where they are, as patients in bed would; a simple meal lies in the room, "
                           + "and another out past the door, beside twenty steel");

                story.Check(patientIn.GetRoom() == room && patientOut.GetRoom() != room
                            && mealOut.GetRoom() != room && mealIn.GetRoom() == room,
                            inside + " and a meal are in the stand's room, " + outside
                            + " and another meal out past the door");
                story.Check(served.All(fix.Comp.HandlesWork),
                            "the stand serves the work of feeding patients and of delivering food to prisoners");
                story.Check(SwapNow(story, fix), name + " changes into the stand's outfit");
                if (!story.Check(fix.Comp.OnShift, "and is on shift"))
                {
                    return;
                }

                Job spotIn = HarnessRideAlong.FeedingJob(JobDefOf.DeliverFood, mealOut, patientIn, deliver);
                spotIn.targetC = at + new IntVec3(3, 0, 2);
                Job spotOut = HarnessRideAlong.FeedingJob(JobDefOf.DeliverFood, mealOut, patientIn, deliver);
                spotOut.targetC = store;
                story.Check(!Probe(fix, HarnessRideAlong.FeedingJob(JobDefOf.FeedPatient, mealOut, patientIn, feed)),
                            "fetching a meal from out past the door for " + inside + ", in the room, keeps the uniform on");
                story.Check(Probe(fix, HarnessRideAlong.FeedingJob(JobDefOf.FeedPatient, mealOut, patientOut, feed)),
                            "fetching one for " + outside + ", out past the door, changes " + name
                            + " back now, while the stand is near");
                story.Check(!Probe(fix, spotIn), "a prisoner's meal left at a spot in the room keeps it on");
                story.Check(Probe(fix, spotOut), "and one left at a spot out past the door changes them back");

                // The shape the game's opportunistic haul leaves: the meal run put back at the head
                // of the queue, handed out by no work, and a haul started first.
                JobQueue queue = fix.Pawn.jobs.jobQueue;
                story.Check(Probe(fix, HarnessRideAlong.GiverlessHaul(steel, store)),
                            "hauling the steel out past the door, with nothing to do after it, changes "
                            + name + " back");
                queue.EnqueueFirst(HarnessRideAlong.FeedingJob(JobDefOf.FeedPatient, mealOut, patientIn, feed));
                story.Note("a meal run for " + inside + " is queued next");
                story.Check(!Probe(fix, HarnessRideAlong.GiverlessHaul(steel, store)),
                            "the same haul slipped in ahead of a meal run for " + inside
                            + ", in the room, keeps the uniform on");
                queue.EnqueueFirst(HarnessRideAlong.FeedingJob(JobDefOf.FeedPatient, mealOut, patientOut, feed));
                story.Note("a meal run for " + outside + " is queued next instead");
                story.Check(Probe(fix, HarnessRideAlong.GiverlessHaul(steel, store)),
                            "but not ahead of one for " + outside + ", out past the door");

                ToggleEach(fix, served);
                fix.Comp.ToggleRest();
                story.Note("the stand's work is unticked and Sleeping ticked instead, "
                           + "so its outfit is now " + name + "'s pyjamas");
                story.Check(fix.Comp.HandlesRest() && !served.Any(fix.Comp.HandlesWork) && fix.Comp.OnShift,
                            "the stand now serves sleep and not that work, and " + name + " still wears its outfit");
                story.Check(!Probe(fix, HarnessRideAlong.FeedingJob(JobDefOf.FeedPatient, mealIn, patientIn, feed)),
                            "in pyjamas, carrying a meal from the room to " + inside + ", in the room, keeps them on");
                story.Check(Probe(fix, HarnessRideAlong.FeedingJob(JobDefOf.FeedPatient, mealIn, patientOut, feed)),
                            "and carrying one to " + outside + ", out past the door, changes " + name + " back first");
                story.Check(fix.Comp.OnShift, "and " + name + " is still on shift at the end");
            }
        }

        /// <summary>
        /// An errand slotted in ahead of work the stand serves keeps the uniform on. The shape is
        /// built from the game's own jobs only, so it runs on every mod list; the two that the
        /// game and Common Sense build have tests of their own below. Why it rides along:
        /// docs/DESIGN.md, "Errands queued ahead of work".
        ///
        /// <para>The comparisons are the rule's narrowness: nothing queued, work this stand does
        /// not serve, served work in another room, and another errand at the head all still change
        /// them back, and so does a meal break, which has its own policy. It never dresses anyone:
        /// a colonist not on shift is left alone by every job no work handed out.</para>
        /// </summary>
        [Test]
        [TestDescription("a colonist in uniform hauls something slipped in ahead of a cooking bill in the "
                         + "stand's room without changing back, but not ahead of other work or another errand")]
        internal IEnumerator HaulAheadOfACookingBillKeepsTheUniformOn()
        {
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef cooking = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cooking");
                WorkGiverDef cookBills = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsCook");
                WorkGiverDef tailorBills = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsMakeApparel");
                ThingDef potatoDef = DefDatabase<ThingDef>.GetNamedSilentFail("RawPotatoes");
                ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
                if (cooking == null || cookBills == null || tailorBills == null
                    || potatoDef == null || mealDef == null)
                {
                    story.Check(false, "the game has cooking and tailoring bills, potatoes and the simple meal");
                    return;
                }
                fix.Comp.ToggleWork(cooking);
                MakeCalm(fix.Map);
                story.Note("Cooking is ticked on the stand, and the map is calm");

                Map map = fix.Map;
                Room room = fix.Stand.GetRoom();
                IntVec3 bench = fix.Stand.Position + new IntVec3(3, 0, 3);
                IntVec3 outside = OpenGround(fix, fix.Stand.Position + new IntVec3(8, 0, 0));
                IntVec3 store = OpenGround(fix, fix.Stand.Position + new IntVec3(8, 0, 2));
                IntVec3 mealCell = OpenGround(fix, outside + new IntVec3(0, 0, 1));
                if (!outside.InBounds(map) || !store.InBounds(map) || outside.GetRoom(map) == room)
                {
                    story.Check(false, "there is open ground outside the stand's room for potatoes and a meal");
                    return;
                }
                Thing potatoes = ThingMaker.MakeThing(potatoDef);
                potatoes.stackCount = 10;
                GenSpawn.Spawn(potatoes, outside, map);
                Thing meal = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), mealCell, map);
                story.Note("ten potatoes and a simple meal lie outside the room");

                story.Check(SwapNow(story, fix), name + " changes into the stand's outfit");
                if (!story.Check(fix.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                JobQueue queue = fix.Pawn.jobs.jobQueue;

                fix.Pawn.jobs.ClearQueuedJobs();
                story.Check(Probe(fix, HarnessRideAlong.GiverlessHaul(potatoes, store)),
                            "hauling the potatoes from outside the room, with nothing to do after it, changes "
                            + name + " back");

                queue.EnqueueFirst(HarnessRideAlong.QueuedWork(cookBills, bench));
                story.Note("a cooking bill in the room is queued next");
                story.Check(!Probe(fix, HarnessRideAlong.GiverlessHaul(potatoes, store)),
                            "the same haul slipped in ahead of a cooking bill in this room keeps the uniform on");

                queue.EnqueueFirst(HarnessRideAlong.QueuedWork(tailorBills, bench));
                story.Note("a tailoring bill in the room is queued next instead");
                story.Check(Probe(fix, HarnessRideAlong.GiverlessHaul(potatoes, store)),
                            "but not ahead of tailoring, which the stand does not serve");

                queue.EnqueueFirst(HarnessRideAlong.QueuedWork(cookBills, outside));
                story.Note("a cooking bill outside the room is queued next instead");
                story.Check(Probe(fix, HarnessRideAlong.GiverlessHaul(potatoes, store)),
                            "nor ahead of the stand's own cooking in another room");

                queue.EnqueueFirst(HarnessRideAlong.GiverlessHaul(potatoes, store));
                story.Note("another haul is queued next instead");
                story.Check(Probe(fix, HarnessRideAlong.GiverlessHaul(potatoes, store)),
                            "nor ahead of another errand");

                queue.EnqueueFirst(HarnessRideAlong.QueuedWork(cookBills, bench));
                story.Note("a cooking bill in the room is queued next again");
                story.Check(Probe(fix, IngestJob(meal)),
                            "and a meal break still changes " + name + " out, a cooking bill queued or not");

                story.Check(!Probe(fix, JobMaker.MakeJob(JobDefOf.UnloadYourInventory)),
                            "unloading their inventory, a job with no target at all, keeps the uniform on");

                // Off shift: the rule is about staying dressed and nothing else.
                story.Check(SwapNow(story, fix), name + " changes back into their own clothes");
                story.Check(!fix.Comp.OnShift, "and is off shift");
                queue.EnqueueFirst(HarnessRideAlong.QueuedWork(cookBills, bench));
                story.Note("a cooking bill in the room is queued next");
                story.Check(!Probe(fix, HarnessRideAlong.GiverlessHaul(potatoes, store)),
                            "off shift, an errand no work handed out sends nobody to change, "
                            + "whatever is queued behind it");
                story.Check(Probe(fix, HarnessRideAlong.QueuedWork(cookBills, bench)),
                            "while the cooking bill itself sends " + name + " to change");
            }
        }

        /// <summary>
        /// The game's own opportunistic haul, driven for real: a crafter in uniform who picks
        /// something up on the way to a bill does it in uniform.
        ///
        /// <para><see cref="HaulAheadOfACookingBillKeepsTheUniformOn"/> builds the shape by hand;
        /// here the engine builds it. The bill is started through the colonist's own job tracker,
        /// so <c>Pawn_JobTracker.StartJob</c> runs <c>TryOpportunisticJob</c> itself, puts the bill
        /// at the front of the queue and starts the haul in its place, from inside its own
        /// <c>StartJob</c> and so through Shift Change's prefix a second time.</para>
        ///
        /// <para>The geometry is the game's and it is tight. The search takes an item only when it
        /// is within half the distance to the bench, its storage is within 0.6 of that distance of
        /// the bench, and the whole detour stays within 1.7 times the straight walk. In a five-cell
        /// room with the item outside the wall, that needs the walk to the bench to be the room's
        /// full diagonal: the colonist in the north-west corner beside the west wall, the bench in
        /// the south-east corner, the item straight outside the wall from the colonist, the
        /// stockpile beside the bench. The stand's cell is nearer the door but pass-through, not
        /// standable. The two detour limits have about 0.4 of a cell to spare. The test asks the
        /// game's search directly first, so a geometry that stops qualifying fails there with a
        /// reason, not later as Shift Change's bug. A crafting spot is the bench because it is one
        /// cell: a stove's position is the middle of three, which cannot reach the corner.</para>
        /// </summary>
        [Test]
        [TestDescription("a crafter in uniform whom the game sends to pick up potatoes on the way to a bill "
                         + "fetches them in uniform")]
        internal IEnumerator HaulOnTheWayToABillKeepsTheUniformOn()
        {
            return Staged((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Hauling")),
                          InARoomWithADoorAndAHauler, Body);

            void Body(Fixture fix, Story story)
            {
                WorkTypeDef crafting = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting");
                WorkGiverDef spotBills = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsUseCraftingSpot");
                ThingDef spotDef = DefDatabase<ThingDef>.GetNamedSilentFail("CraftingSpot");
                ThingDef potatoDef = DefDatabase<ThingDef>.GetNamedSilentFail("RawPotatoes");
                RecipeDef recipe = spotDef?.AllRecipes?.FirstOrDefault();
                if (crafting == null || spotBills == null || spotDef == null || potatoDef == null
                    || recipe == null || fix.Outside.Area == 0)
                {
                    story.Check(false, "the game has crafting, the crafting spot and its bills, and potatoes, "
                                       + "and the room has a door");
                    return;
                }
                Map map = fix.Map;
                Pawn pawn = fix.Pawn;
                string name = pawn.LabelShort;
                fix.Comp.ToggleWork(crafting);
                MakeCalm(map);
                story.Note("Crafting is ticked on the stand, and the map is calm");

                // Relative to the pad's corner, which the doorway strip pins: it runs up the pad's
                // west side, one column out. The stand is at (1, 1) and the door at (0, 1).
                IntVec3 corner = new IntVec3(fix.Outside.minX + 1, 0, fix.Outside.minZ);
                IntVec3 waitAt = corner + new IntVec3(1, 0, 5);
                IntVec3 itemAt = corner + new IntVec3(-1, 0, 5);
                IntVec3 benchAt = corner + new IntVec3(5, 0, 1);
                IntVec3 storeAt = corner + new IntVec3(4, 0, 2);

                // Facing south, so its interaction cell is inside the room rather than in the south
                // wall.
                Building_WorkTable spot = DebugTools_Fixtures.Spawn(map, spotDef, null, benchAt, Rot4.South)
                    as Building_WorkTable;
                if (spot == null)
                {
                    story.Check(false, "a crafting spot could be put down in the stand's room");
                    return;
                }
                Bill_Production bill = new Bill_Production(recipe);
                spot.BillStack.AddBill(bill);

                Zone_Stockpile stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,
                                                              map.zoneManager);
                map.zoneManager.RegisterZone(stockpile);
                try
                {
                    stockpile.AddCell(storeAt);
                    stockpile.settings.Priority = StoragePriority.Critical;
                    Thing potatoes = ThingMaker.MakeThing(potatoDef);
                    potatoes.stackCount = 10;
                    GenSpawn.Spawn(potatoes, itemAt, map);
                    story.Note("a crafting spot with a bill in the room's south-east corner, a critical "
                               + "stockpile cell beside it, and ten potatoes outside the west wall");

                    story.Check(SwapNow(story, fix), name + ", a crafter, changes into the stand's outfit");
                    if (!story.Check(fix.Comp.OnShift, "and is on shift"))
                    {
                        return;
                    }
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    pawn.jobs.ClearQueuedJobs();
                    pawn.pather?.StopDead();
                    pawn.Position = waitAt;
                    story.Note(name + " stops, and is put in the room's north-west corner, "
                               + "the potatoes just outside the wall");

                    Job billJob = JobMaker.MakeJob(JobDefOf.DoBill, spot);
                    billJob.bill = bill;
                    billJob.workGiverDef = spotBills;

                    // The engine half, asked directly: the game's own search offers a haul of the
                    // potatoes for this bill from here, handed out by no work.
                    Job offered = pawn.jobs.TryOpportunisticJob(null, billJob);
                    bool shaped = offered != null && offered.def == JobDefOf.HaulToCell
                                  && offered.targetA.Thing == potatoes && offered.workGiverDef == null;
                    if (!story.Check(shaped, "on the way to the crafting spot's bill, the game itself offers "
                                             + name + " a haul of the potatoes first"))
                    {
                        story.Note("it offered "
                                   + (offered == null ? "nothing" : JobCalled(offered.def) + " of " + offered.targetA)
                                   + "; " + name + " at " + pawn.Position + ", the crafting spot at " + benchAt
                                   + ", the potatoes at " + itemAt + ", the stockpile at " + storeAt);
                        return;
                    }

                    // Shift Change's half: start the bill as the game would, and let the game's
                    // StartJob queue the bill and start the haul.
                    pawn.jobs.StartJob(billJob, JobCondition.None, null,
                        resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);
                    story.Note(name + " is given the bill");
                    JobQueue queue = pawn.jobs.jobQueue;
                    story.Check(queue.Count > 0 && queue[0].job == billJob,
                                "the game puts the bill next in line and starts the haul in its place");
                    story.Check(pawn.CurJobDef != ShiftChangeDefOf.ShiftChange_SwapAtStand,
                                name + " is not sent to change out for it");
                    story.Check(pawn.CurJobDef == JobDefOf.HaulToCell && HarnessRideAlong.Hauls(pawn.CurJob, potatoes),
                                name + " fetches the potatoes in uniform");
                    story.Check(fix.Comp.OnShift, "and is still on shift for the bill behind it");

                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    pawn.jobs.ClearQueuedJobs();
                    Job lone = HaulAIUtility.HaulToStorageJob(pawn, potatoes, false);
                    story.Check(lone != null && Probe(fix, lone),
                                "the same haul with nothing to do after it still changes " + name + " back");
                }
                finally
                {
                    stockpile.Delete();
                }
            }
        }

        // Known gaps unless the mod is loaded (--with=).

        /// <summary>
        /// Common Sense's bill haul keeps a cook in uniform. As it ships, Common Sense queues a
        /// haul of an ingredient from outside the room, handed out by no work, ahead of the bill,
        /// and its <c>StartJob</c> prefix always runs before Shift Change's. Why that would change
        /// a cook out first, and why the prefix order is fixed: docs/DESIGN.md, "Errands queued
        /// ahead of work".
        ///
        /// <para>Driven end to end through the colonist's own job tracker, so Common Sense's real
        /// prefix builds the queue and Shift Change's then judges the haul it hands out. The same
        /// haul with no bill behind it still changes them back. A known gap without the
        /// mod.</para>
        /// </summary>
        [Test]
        [TestDescription("a cook in uniform whom Common Sense sends for an ingredient ahead of a bill "
                         + "fetches it without changing back")]
        internal IEnumerator CommonSenseIngredientHaulKeepsTheUniformOn()
        {
            return Staged(StageWithDoorway, InARoomWithADoor, Body);

            void Body(Fixture fix, Story story)
            {
                if (!ModsConfig.IsActive("avilmask.CommonSense"))
                {
                    story.KnownGap(false, "Common Sense's haul of an ingredient ahead of a bill keeps a cook in uniform",
                        "Common Sense is not on this mod list; --with=avilmask.commonsense loads it");
                    return;
                }
                Type settings = AccessTools.TypeByName("CommonSense.Settings");
                FieldInfo setting = settings == null ? null : AccessTools.Field(settings, "hauling_over_bills");
                if (!story.Check(setting != null && (bool)setting.GetValue(null),
                                 "Common Sense's \"haul ingredients over doing bills\" is on, as it ships"))
                {
                    return;
                }

                WorkTypeDef cooking = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cooking");
                WorkGiverDef cookBills = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsCook");
                ThingDef stoveDef = DefDatabase<ThingDef>.GetNamedSilentFail("FueledStove");
                RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("CookMealSimple");
                ThingDef potatoDef = DefDatabase<ThingDef>.GetNamedSilentFail("RawPotatoes");
                if (cooking == null || cookBills == null || stoveDef == null || recipe == null
                    || potatoDef == null || fix.Outside.Area == 0)
                {
                    story.Check(false, "the game has cooking, the fueled stove, simple meals and potatoes, "
                                       + "and the room has a door");
                    return;
                }
                Map map = fix.Map;
                string name = fix.Pawn.LabelShort;
                fix.Comp.ToggleWork(cooking);
                MakeCalm(map);
                story.Note("Cooking is ticked on the stand, and the map is calm");

                // A kitchen: the stove across the room from the stand, the potatoes on the ground
                // outside, and a stockpile just outside the door, nearer the cook than the stove
                // is, which is Common Sense's own condition for queueing the haul at all.
                IntVec3 stoveAt = fix.Stand.Position + new IntVec3(2, 0, 3);
                Building_WorkTable stove = DebugTools_Fixtures.Spawn(map, stoveDef, null, stoveAt, Rot4.North)
                    as Building_WorkTable;
                if (stove == null)
                {
                    story.Check(false, "a fueled stove could be built in the stand's room");
                    return;
                }
                Bill_Production bill = new Bill_Production(recipe);
                stove.BillStack.AddBill(bill);

                IntVec3 storeCell = new IntVec3(fix.Outside.minX, 0, fix.Stand.Position.z);
                IntVec3 potatoCell = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 1);
                Zone_Stockpile stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,
                                                              map.zoneManager);
                map.zoneManager.RegisterZone(stockpile);
                stockpile.AddCell(storeCell);
                stockpile.settings.Priority = StoragePriority.Critical;
                Thing potatoes = ThingMaker.MakeThing(potatoDef);
                potatoes.stackCount = 10;
                GenSpawn.Spawn(potatoes, potatoCell, map);
                story.Note("a fueled stove with a bill for simple meals across the room, a critical stockpile "
                           + "cell just outside the door, and ten potatoes further out");

                story.Check(SwapNow(story, fix), name + ", a cook, changes into the stand's outfit");
                if (!story.Check(fix.Comp.OnShift, "and is on shift"))
                {
                    return;
                }

                Job billJob = JobMaker.MakeJob(JobDefOf.DoBill, stove);
                billJob.bill = bill;
                billJob.workGiverDef = cookBills;
                billJob.targetQueueB = new List<LocalTargetInfo> { potatoes };
                billJob.countQueue = new List<int> { potatoes.stackCount };

                Pawn pawn = fix.Pawn;
                pawn.jobs.ClearQueuedJobs();
                pawn.jobs.StartJob(billJob, JobCondition.InterruptForced, null,
                    resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);
                story.Note(name + " is given the stove's bill, to cook with the potatoes");

                // The engine half: what Common Sense really does with that bill.
                JobQueue queue = pawn.jobs.jobQueue;
                Job haul = queue.Count > 0 ? queue[0].job : null;
                bool shaped = queue.Count == 2 && haul != null && haul.workGiverDef == null
                              && HarnessRideAlong.Hauls(haul, potatoes) && queue[1].job == billJob;
                if (!story.Check(shaped, "Common Sense queues a haul of the potatoes from outside, handed out "
                                         + "by no work, ahead of the bill"))
                {
                    story.Note("the queue: " + string.Join(", ", queue.Select(q => JobCalled(q.job.def)
                        + (q.job.workGiverDef == null ? "" : " for " + q.job.workGiverDef.label)).ToArray()));
                    return;
                }
                JobDef haulDef = haul.def;

                // Shift Change's half: the haul starts at the next job boundary, from the queue,
                // through every prefix in their real order.
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: true);
                story.Check(pawn.CurJobDef != ShiftChangeDefOf.ShiftChange_SwapAtStand,
                            name + " is not sent to change out for Common Sense's haul");
                story.Check(pawn.CurJobDef == haulDef && HarnessRideAlong.Hauls(pawn.CurJob, potatoes),
                            name + " fetches the potatoes in uniform");
                story.Check(fix.Comp.OnShift, "and is still on shift for the bill behind it");

                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                pawn.jobs.ClearQueuedJobs();
                Job lone = HaulAIUtility.HaulToStorageJob(pawn, potatoes, false);
                story.Check(lone != null && Probe(fix, lone),
                            "the same haul with no bill behind it still changes " + name + " back");
            }
        }

        /// <summary>
        /// Pick Up And Haul's follow-up jobs keep the uniform on: the unload at the end of the haul
        /// (<c>JobDriver_HaulToInventory</c>, the last toil), the unload its checker queues
        /// (<c>PawnUnloadChecker</c>), and the next haul its driver queues. Why they read as
        /// leaving the room: docs/DESIGN.md, "Mod compatibility".
        ///
        /// <para>Each is started from the queue through the colonist's own job tracker, the way
        /// the mod's driver hands it over. A haul from the mod's own work, starting outside, still
        /// changes them back first. A known gap without the mod.</para>
        /// </summary>
        [Test]
        [TestDescription("a colonist in uniform unloads and hauls on with Pick Up And Haul without "
                         + "changing back between the steps")]
        internal IEnumerator PickUpAndHaulNextStepsKeepTheUniformOn()
        {
            return Staged(StageWithDoorway, InARoomWithADoor, Body);

            void Body(Fixture fix, Story story)
            {
                JobDef unload = DefDatabase<JobDef>.GetNamedSilentFail("UnloadYourHauledInventory");
                JobDef toInventory = DefDatabase<JobDef>.GetNamedSilentFail("HaulToInventory");
                WorkGiverDef theirGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulToInventory");
                if (unload == null || toInventory == null || theirGiver == null)
                {
                    story.KnownGap(false, "Pick Up And Haul's next steps keep a colonist in uniform",
                        "Pick Up And Haul is not on this mod list; --with=mehni.pickupandhaul loads it");
                    return;
                }
                WorkTypeDef cooking = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cooking");
                ThingDef potatoDef = DefDatabase<ThingDef>.GetNamedSilentFail("RawPotatoes");
                if (cooking == null || potatoDef == null || fix.Outside.Area == 0)
                {
                    story.Check(false, "the game has cooking and potatoes, and the room has a door");
                    return;
                }
                Map map = fix.Map;
                Pawn pawn = fix.Pawn;
                string name = pawn.LabelShort;
                fix.Comp.ToggleWork(cooking);
                MakeCalm(map);
                story.Note("Cooking is ticked on the stand, and the map is calm");

                // Their driver reads its comp off the colonist the moment it starts, so a colonist
                // without it would throw for a reason that is not Shift Change's.
                if (!story.Check(pawn.AllComps.Any(c => c.GetType().Name == "CompHauledToInventory"),
                                 name + " carries Pick Up And Haul's record of what they hauled"))
                {
                    return;
                }

                IntVec3 storeCell = new IntVec3(fix.Outside.minX, 0, fix.Stand.Position.z);
                IntVec3 itemCell = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 1);
                IntVec3 standingOut = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 3);
                Thing potatoes = ThingMaker.MakeThing(potatoDef);
                potatoes.stackCount = 10;
                GenSpawn.Spawn(potatoes, itemCell, map);
                story.Note("ten potatoes lie out past the door");

                story.Check(SwapNow(story, fix), name + " changes into the stand's outfit");
                if (!story.Check(fix.Comp.OnShift, "and is on shift"))
                {
                    return;
                }

                // The haul as their work giver hands it out: stamped, and judged at its first
                // item, outside the kitchen, so it changes them back first.
                Job handedOut = HarnessRideAlong.HaulToInventoryJob(toInventory, potatoes, storeCell);
                handedOut.workGiverDef = theirGiver;
                story.Check(Probe(fix, handedOut),
                            "a haul Pick Up And Haul's own work hands out, starting outside, changes "
                            + name + " back first");

                story.Note("an unload at the cell just outside the door is queued, as their haul ends with one");
                JobDef started = HarnessRideAlong.StartFromQueue(fix, JobMaker.MakeJob(unload, storeCell), JobTag.Misc);
                story.Check(started == unload,
                            "unloading there keeps the uniform on (it started: " + JobCalled(started) + ")");

                story.Note("the next haul of the potatoes is queued, as their driver queues one");
                started = HarnessRideAlong.StartFromQueue(
                    fix, HarnessRideAlong.HaulToInventoryJob(toInventory, potatoes, storeCell), JobTag.Misc);
                story.Check(started == toInventory,
                            "the next haul keeps the uniform on too (it started: " + JobCalled(started) + ")");

                IntVec3 home = pawn.Position;
                pawn.pather?.StopDead();
                pawn.Position = standingOut;
                story.Note(name + " is put out past the door, and an unload is queued on them there, "
                           + "as their checker does");
                started = HarnessRideAlong.StartFromQueue(fix, JobMaker.MakeJob(unload, pawn), JobTag.Misc);
                pawn.pather?.StopDead();
                pawn.Position = home;
                story.Note(name + " is put back beside the stand");
                story.Check(started == unload,
                            "unloading out of the room, queued by their checker, keeps it on as well (it started: "
                            + JobCalled(started) + ")");
                story.Check(fix.Comp.OnShift, "and " + name + " is still on shift at the end");
            }
        }

        /// <summary>
        /// A way to feed someone by the name the game gives it, with its kind of work, which tells
        /// apart two that share a name: "feed animals" (doctoring).
        /// </summary>
        internal static string FeedingGiver(WorkGiverDef giver)
        {
            string label = giver.label.NullOrEmpty() ? giver.defName : giver.label;
            return "\"" + label + "\" ("
                   + (giver.workType == null ? "no work type" : WorkTypeLabels.Of(giver.workType)) + ")";
        }

        /// <summary>A job by the name the game gives it, or nothing.</summary>
        internal static string JobCalled(JobDef def)
        {
            if (def == null)
            {
                return "nothing";
            }
            return def.label.NullOrEmpty() ? def.defName : def.label;
        }
    }
}
