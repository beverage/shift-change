using System.Collections;
using DevTools.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// What a meal break, danger and a duty do to a colonist in uniform, or on their way to put one
    /// on: a meal sends them to change back first, danger holds back changing in but never
    /// changing back, and a duty holds back both until it ends.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.MealsDangerAndDuties)]
    internal class MealsDangerAndDuties : StoryFixture
    {
        /// <summary>What <see cref="Stage"/> sets up with the pad walled, roofed and floored.</summary>
        internal const string InARoom =
            "a stand holding a duster in a walled room, and a colonist beside it in a shirt, trousers and a parka";

        /// <summary>The same, with a colonist whose backstory leaves them able to doctor.</summary>
        internal const string InARoomADoctor =
            "a stand holding a duster in a walled room, and a colonist able to doctor beside it "
            + "in a shirt, trousers and a parka";

        /// <summary>
        /// At a work stand a meal is a sit-down break: the uniform comes off first wherever the food
        /// is stored, or a cook would carry a meal across the base in whites to reach a chair. The
        /// one exception is food already in hand or pack, which is just eaten.
        /// </summary>
        [Test]
        [TestDescription("a colonist in uniform changes back before eating a meal from storage, "
                         + "but eats one they carry as they are")]
        internal IEnumerator AMealBreakChangesThemOutOfUniform()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                story.Check(SwapNow(story, scene), name + " changes into the stand's duster");
                story.Check(scene.Comp.OnShift, "and is on shift");
                if (!scene.Comp.OnShift)
                {
                    return;
                }

                ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
                if (mealDef == null)
                {
                    story.Check(false, "the game has its simple meal");
                    return;
                }

                Thing stored = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), scene.Colonist.Position, scene.Map);
                story.Note("a simple meal is set down at " + name + "'s feet");
                story.Check(SendsToChangeKeepingQueue(scene, IngestJob(stored)),
                            "going to eat it, " + name + " is sent to change out of the duster first");

                Thing carried = ThingMaker.MakeThing(mealDef);
                scene.Colonist.inventory.innerContainer.TryAdd(carried);
                story.Note(name + " is given a simple meal to carry");
                story.Check(!SendsToChangeKeepingQueue(scene, IngestJob(carried)),
                            "but a meal they carry is simply eaten, duster and all");
            }
        }

        /// <summary>
        /// The meal-break rule at a sleep stand, where food already in the colonist's pack still
        /// changes them out of sleepwear first. At the wake-up the colonist is still in the stand's
        /// room, near it, so eating elsewhere in sleepwear and walking back to change is the trip
        /// the sleep trigger exists to prevent. At a work or recreation stand carried food is just
        /// eaten (<see cref="AMealBreakChangesThemOutOfUniform"/>: the same colonist and meal, the
        /// opposite answer).
        ///
        /// <para>Both halves are checked. The breakfast from storage changes them out of sleepwear,
        /// and the carried one must answer the same, so the two differ only in where the food
        /// is.</para>
        /// </summary>
        [Test]
        [TestDescription("a colonist in a bedroom stand's sleepwear changes back before breakfast, even one "
                         + "they carry, and even getting straight out of bed")]
        internal IEnumerator BreakfastChangesThemOutOfSleepwear()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
                if (bedDef == null || mealDef == null)
                {
                    story.Check(false, "the game has its bed and its simple meal");
                    return;
                }

                // Sleeping is not ticked by hand. A bed makes the pad a bedroom
                // (RoomRoleWorker_Bedroom scores a single unowned humanlike bed at 100000), so the
                // stand serves sleep from its role already, and a tick would turn it off: the trap
                // WhatSendsThemToChange.GoingToBedInTheRoomSendsThemToChange describes.
                Building_Bed bed = DebugTools_Fixtures.Spawn(
                    scene.Map, bedDef, ThingDefOf.WoodLog,
                    new IntVec3(scene.OutfitStand.Position.x + 2, 0, scene.OutfitStand.Position.z + 2),
                    Rot4.North) as Building_Bed;
                if (bed == null)
                {
                    story.Check(false, "a bed is built in the stand's room");
                    return;
                }
                story.Note("a bed is built in the stand's room");

                story.Check(scene.Comp.HandlesRest(), "the bed makes the room a bedroom, so the stand serves sleep");
                story.Check(!scene.Comp.HandlesRecreation(),
                            "and not recreation, whose meals go by a rule of their own");
                if (!scene.Comp.HandlesRest())
                {
                    return;
                }

                story.Check(SwapNow(story, scene), name + " changes into the stand's duster for bed");
                story.Check(scene.Comp.OnShift, "and is on shift");
                if (!scene.Comp.OnShift)
                {
                    return;
                }

                Thing stored = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), scene.Colonist.Position, scene.Map);
                story.Note("a simple meal is set down at " + name + "'s feet");
                story.Check(SendsToChangeKeepingQueue(scene, IngestJob(stored)),
                            "going to eat it for breakfast, " + name + " changes out of the sleepwear first, "
                            + "as always");

                Thing carried = ThingMaker.MakeThing(mealDef);
                scene.Colonist.inventory.innerContainer.TryAdd(carried);
                story.Note(name + " is given a simple meal to carry");
                story.Check(SendsToChangeKeepingQueue(scene, IngestJob(carried)),
                            "and so for a breakfast they carry, which is what this test guards");

                // The wake-up: the real breakfast is decided with the colonist still on the bed, and
                // StaysInBed must not swallow it there. JobDriver_Ingest does not override
                // CanBeginNowWhileLyingDown (the base returns false) and an ingest job's second target
                // is not a bed, so eating is not something done lying down and the change back is
                // reached. Should that change, the bug returns behind a different guard; this pins it.
                IntVec3 wasAt = scene.Colonist.Position;
                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = bed.Position;
                story.Note(name + " is moved onto the bed, as on waking");
                story.Check(Patch_JobInterception.OnABed(scene.Colonist), name + " counts as on the bed");
                story.Check(!Patch_JobInterception.StaysInBed(scene.Colonist, IngestJob(carried)),
                            "and eating is not something done lying in it");
                story.Check(SendsToChangeKeepingQueue(scene, IngestJob(carried)),
                            "so on waking " + name + " changes back before breakfast, by the stand");

                scene.Colonist.pather?.StopDead();
                scene.Colonist.Position = wasAt;
            }
        }

        /// <summary>
        /// Danger holds a change back one way only: under threat a colonist may not change into a
        /// uniform, but one already wearing it still changes back. The same work job sending them to
        /// change on a calm map shows the stand would otherwise. Guards a shipped regression where
        /// one gate answered for both ways (docs/DESIGN.md, "The gates"), so both halves run against
        /// the same threat: a test that only showed dressing held back would pass on the broken
        /// build.
        ///
        /// <para>The way back is set off by a meal break rather than by leaving the room: at a work
        /// stand a meal whose food is not on the colonist sends them to change wherever the food
        /// sits, so no second room is needed.</para>
        ///
        /// <para>No hostile is on the map while the clock runs. The change ticks the whole map, and
        /// a ticked colonist reacts to a hostile three cells away: the think tree overrides the
        /// change partway (<c>InterruptForced</c> at its first step), now and then. So the threat
        /// is staged twice, around the change, and cleared before anything ticks.</para>
        /// </summary>
        [Test]
        [TestDescription("with a hostile on the map a doctor is not sent to change before tending, "
                         + "but one already in uniform still changes back for a meal")]
        internal IEnumerator DangerKeepsThemFromChangingInButNotBack()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true,
                                          capableOf: WorkType("Doctor")),
                          InARoomADoctor, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
                if (doctor == null || tend == null || mealDef == null)
                {
                    story.Check(false, "the game has its doctoring, its tending and its simple meal");
                    return;
                }
                scene.Comp.ToggleWork(doctor);

                // Arrange the calm rather than assume it: a generated map can start with a hostile on
                // it (see MakeCalm).
                int swept = MakeCalm(scene.Map);
                story.Note("the stand is ticked for doctoring, and the map is calm ("
                           + swept + " hostile things cleared)");
                story.Check(scene.Map.dangerWatcher.DangerRating == StoryDanger.None,
                            "the colony is in no danger");
                story.Check(SendsToChangeKeepingQueue(scene, WorkJob(scene, tend)),
                            "and " + name + " changes before tending a patient on their own");

                // The half that stays shut. Deciding a job never ticks the colonist, so the hostile
                // cannot disturb it.
                Pawn threat = Threat(scene);
                if (threat == null)
                {
                    story.Check(false, "a hostile can be set down near the stand");
                    return;
                }
                story.Note("a hostile " + threat.kindDef.label + " is set down near the stand");
                story.Check(scene.Map.dangerWatcher.DangerRating != StoryDanger.None,
                            "and the colony is in danger");
                story.Check(!SendsToChangeKeepingQueue(scene, WorkJob(scene, tend)),
                            "so " + name + " goes to tend the patient without changing");
                ClearThreat(scene, threat);
                story.Note("the hostile is removed");

                // Dressed by the change itself, not by the trigger just shown shut, and with no
                // hostile on the map: this is the only step that runs the clock.
                story.Check(SwapNow(story, scene), name + " changes into the stand's duster on the calm map");
                story.Check(scene.Comp.OnShift, "and is on shift");
                if (!scene.Comp.OnShift)
                {
                    return;
                }

                threat = Threat(scene);
                if (threat == null)
                {
                    story.Check(false, "a hostile can be set down again for the way back");
                    return;
                }
                Thing meal = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), scene.Colonist.Position, scene.Map);
                story.Note("a hostile " + threat.kindDef.label + " is set down again, and a simple meal at "
                           + name + "'s feet");
                story.Check(scene.Map.dangerWatcher.DangerRating != StoryDanger.None,
                            "the colony is in danger again");
                story.Check(SendsToChangeKeepingQueue(scene, IngestJob(meal)),
                            "but going to eat, " + name + " still changes out of the duster first");
                ClearThreat(scene, threat);
                story.Note("the hostile is removed");

                story.Check(scene.Map.dangerWatcher.DangerRating == StoryDanger.None,
                            "and with it gone, the colony is in no danger again");
            }
        }

        /// <summary>
        /// A duty holds both ways shut: a colonist given a duty by a lord (a ritual, a party, a
        /// caravan forming, defending a spot) is neither dressed nor sent back to change until the
        /// duty ends, and a bare duty with no lord behind it holds them too. Each refusal sits
        /// beside the same job for the colonist free. Guards a shipped bug that left colonists
        /// standing at a stand for a whole ritual, changing (docs/DESIGN.md, "The gates").
        ///
        /// <para>Staged through <c>LordMaker</c> rather than by setting fields, because the gate
        /// reads what the engine writes, and a test that wrote them itself would certify its own
        /// guess. <c>LordJob_DefendPoint</c> needs no ritual, no DLC and no second colonist; any
        /// lord duty would do. The test also checks the gate opens again the moment the duty ends,
        /// so it cannot latch a colonist out of the mod.</para>
        /// </summary>
        [Test]
        [TestDescription("a colonist told to defend a spot is neither sent to change nor changed back, "
                         + "and is free to be again the moment the duty ends")]
        internal IEnumerator ADutyLeavesThemAloneUntilItEnds()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true,
                                          capableOf: WorkType("Doctor")),
                          InARoomADoctor, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef doctor = WorkType("Doctor");
                WorkGiverDef tend = Giver("DoctorTendToHumanlikes");
                ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
                if (doctor == null || tend == null || mealDef == null)
                {
                    story.Check(false, "the game has its doctoring, its tending and its simple meal");
                    return;
                }
                scene.Comp.ToggleWork(doctor);
                int swept = MakeCalm(scene.Map);
                story.Note("the stand is ticked for doctoring, and the map is calm ("
                           + swept + " hostile things cleared)");

                story.Check(SendsToChangeKeepingQueue(scene, WorkJob(scene, tend)),
                            name + ", free, changes before tending a patient on their own");

                Lord lord = LordMaker.MakeNewLord(Faction.OfPlayer,
                    new LordJob_DefendPoint(scene.Colonist.Position), scene.Map, new[] { scene.Colonist });
                if (lord == null)
                {
                    story.Check(false, name + " can be told to defend a spot");
                    return;
                }
                story.Note(name + " is told to defend the spot where they stand");
                story.Check(scene.Colonist.GetLord() == lord, name + " is under those orders");
                story.Check(scene.Colonist.mindState?.duty != null, "and has a duty from them");
                story.Check(!SendsToChangeKeepingQueue(scene, WorkJob(scene, tend)),
                            "so " + name + " goes to tend the patient without changing");

                // Dressed while free, since dressing is shut under a duty. Changing back is the way
                // that strands a colonist.
                lord.RemovePawn(scene.Colonist);
                story.Note(name + " is released from the duty");
                story.Check(SwapNow(story, scene), name + " changes into the stand's duster, once released");
                story.Check(scene.Comp.OnShift, "and is on shift");
                if (!scene.Comp.OnShift)
                {
                    scene.Map.lordManager.RemoveLord(lord);
                    return;
                }

                // A meal break sets off the way back without a second room: at a work stand a meal
                // sends them to change wherever the food is.
                Thing meal = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), scene.Colonist.Position, scene.Map);
                story.Note("a simple meal is set down at " + name + "'s feet");
                story.Check(SendsToChangeKeepingQueue(scene, IngestJob(meal)),
                            "going to eat it, " + name + ", free, changes out of the duster first");

                lord.AddPawn(scene.Colonist);
                story.Note(name + " is told to defend the spot again");
                story.Check(scene.Colonist.mindState?.duty != null, name + " has a duty again");
                story.Check(!SendsToChangeKeepingQueue(scene, IngestJob(meal)),
                            "and going to eat, is not sent to change back: the duty would take them from the "
                            + "stand mid-change, again and again, and leave them stranded there");

                lord.RemovePawn(scene.Colonist);
                story.Note(name + " is released from the duty again");
                story.Check(scene.Colonist.GetLord() == null && scene.Colonist.mindState?.duty == null,
                            "which ends both the orders and the duty");
                story.Check(SendsToChangeKeepingQueue(scene, IngestJob(meal)),
                            "so the meal sends " + name + " to change back the moment the duty ends");

                // A duty with no lord behind it, which a mod's own think node can leave on a
                // colonist. The gate reads the duty as well as the lord for this.
                scene.Colonist.mindState.duty = new PawnDuty(DutyDefOf.Idle);
                story.Note(name + " is given a duty with no lord behind it, as a mod can leave one");
                story.Check(!SendsToChangeKeepingQueue(scene, IngestJob(meal)),
                            "which holds them back from changing too");
                scene.Colonist.mindState.duty = null;
                story.Note("the duty is cleared");
                story.Check(SendsToChangeKeepingQueue(scene, IngestJob(meal)),
                            "and with it cleared, the meal sends them to change back again");

                scene.Map.lordManager.RemoveLord(lord);
            }
        }
    }
}
