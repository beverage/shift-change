using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DevTools.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;
using JobClass = ShiftChange.Patch_JobInterception.JobClass;
using JobKind = ShiftChange.Patch_JobInterception.JobKind;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Which jobs send a colonist to a stand to change, going to bed and getting up again
    /// included, what a deposit-only stand takes from them, and the settings that decide it.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.WhatSendsThemToChange)]
    internal class WhatSendsThemToChange : StoryFixture
    {
        /// <summary>What <see cref="Stage"/> sets up with the pad walled, roofed and floored.</summary>
        internal const string InARoom =
            "a stand holding a duster in a walled room, and a colonist beside it in a shirt, trousers and a parka";

        /// <summary>The same, with a colonist whose backstory leaves them able to doctor.</summary>
        internal const string InARoomADoctor =
            "a stand holding a duster in a walled room, and a colonist able to doctor beside it "
            + "in a shirt, trousers and a parka";

        /// <summary>What <see cref="Stage"/> sets up on the open pad.</summary>
        internal const string InTheOpen =
            "a stand holding a duster in the open, and a colonist beside it in a shirt, trousers and a parka";

        /// <summary>
        /// What <see cref="HarnessFixtures.StageWithDoorway(Map, CellRect, WorkTypeDef)"/> sets up
        /// for fishing: the walled room with a door in its west wall, beside the stand, onto a strip
        /// of open ground.
        /// </summary>
        internal const string InARoomWithADoorAFisher =
            "a stand holding a duster in a walled room, a door beside it onto open ground, "
            + "and a colonist able to fish beside it in a shirt, trousers and a parka";

        /// <summary>
        /// The recreation trigger through the mod's real decision as a job starts: a game of
        /// chess in the stand's room sends the colonist to change, and the jobs that must not do
        /// not. The game of chess has its second target on the colonist's cell, because the
        /// recreation trigger reads that target first (for games played sitting down it is where
        /// the colonist sits, the first target the table). A work job aimed the same way shows
        /// that what the stand serves, not where the job is, decides.
        /// </summary>
        [Test]
        [TestDescription("a stand ticked for recreation sends a colonist to change before a game of chess "
                         + "in its room, but not before reading, an order or work")]
        internal IEnumerator ChessInTheRoomSendsThemToChange()
        {
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
                JobDef reading = DefDatabase<JobDef>.GetNamedSilentFail("Reading");
                WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
                if (chess == null || reading == null)
                {
                    story.Check(false, "the game has its chess and reading jobs");
                    return;
                }

                // The pad's room has no role, so tick the trigger by hand.
                fix.Comp.ToggleRecreation();
                story.Note("Recreation is ticked on the stand");
                story.Check(fix.Comp.HandlesRecreation(), "the stand serves recreation");
                story.Check(Diverts(fix, JoyJob(fix, chess)),
                            name + " sits down to chess in its room and is sent to change first");

                Job forced = JoyJob(fix, chess);
                forced.playerForced = true;
                story.Check(!Diverts(fix, forced), "but not when the player orders the game");
                story.Check(!Diverts(fix, JoyJob(fix, reading)),
                            "nor before reading, which is recreation too but picks its spot only once begun");
                if (tend != null)
                {
                    story.Check(!Diverts(fix, WorkJob(fix, tend)),
                                "nor before tending a patient, which is work, not recreation");
                }

                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                if (doctor != null)
                {
                    fix.Comp.ToggleWork(doctor);
                    story.Note("the stand is switched to doctoring");
                    story.Check(!Diverts(fix, JoyJob(fix, chess)),
                                "and a stand serving doctoring lets the same game of chess go by");
                }
            }
        }

        /// <summary>
        /// A stand ticked for the game's bed rest work type (resting in bed) dresses a colonist
        /// going to bed to recuperate, and leaves one who needs a doctor first where they are.
        ///
        /// <para>It guards a shipped bug: the tickbox did nothing while every medical check then
        /// written, all negatives, passed. The patient givers are NonScanJob overrides and
        /// JobGiver_Work stamps workGiverDef on its scanner paths only, so the job reaches
        /// StartJob naming no work type and the work trigger never sees it. Only a positive check
        /// tells a dead tickbox from a working one.</para>
        ///
        /// <para>The bed is medical, as a hospital's is, which keeps the scene honest:
        /// RoomRoleWorker_Bedroom returns 0 on seeing a medical bed, so the room scores as a
        /// hospital and the stand does not pick up the sleep trigger. Work and sleep are exclusive
        /// on a stand, so a bedroom would test the wrong trigger.</para>
        /// </summary>
        [Test]
        [TestDescription("a colonist going to bed to recuperate changes first at a stand ticked for "
                         + "resting in bed, unless they need a doctor now")]
        internal IEnumerator BedRestSendsAPatientToChange()
        {
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                JobDef laydown = DefDatabase<JobDef>.GetNamedSilentFail("LayDown");
                WorkTypeDef bedRest = DefDatabase<WorkTypeDef>.GetNamedSilentFail("PatientBedRest");
                if (bedDef == null || laydown == null || bedRest == null)
                {
                    story.Check(false, "the game has its bed, its lay-down job and its bed rest work");
                    return;
                }

                Building_Bed bed = DebugTools_Fixtures.Spawn(
                    fix.Map, bedDef, ThingDefOf.WoodLog,
                    new IntVec3(fix.Stand.Position.x + 2, 0, fix.Stand.Position.z + 2),
                    Rot4.North) as Building_Bed;
                if (bed == null)
                {
                    story.Check(false, "a bed is built in the stand's room");
                    return;
                }
                bed.Medical = true;

                // Tick the row the README tells a player to tick, and nothing else. The colonist must
                // need no doctor to begin with: vanilla does not tend the conditions of a pawn
                // generated to join the player, so the scene's colonist arrives without them.
                fix.Comp.ToggleWork(bedRest);
                story.Note("a bed for medical use is built in the room, and the stand is ticked for resting in bed");
                story.Check(!HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn),
                            name + " needs no doctor to begin with");
                story.Check(bed.Medical, "the bed is for medical use, so the room is a hospital");
                story.Check(fix.Comp.HandlesWork(bedRest), "and the stand is ticked for resting in bed");
                story.Check(!fix.Comp.HandlesRest(),
                            "but not for sleeping: the gown and the pyjamas are separate rows");
                story.Check(Diverts(fix, JobMaker.MakeJob(laydown, bed), JobTag.RestingForMedicalReasons),
                            "so " + name + ", going to bed to recuperate, changes at the stand first");

                // The urgent half: vanilla splits patients itself, WorkGiver_PatientGoToBedTreatment
                // gating on ShouldSeekMedicalRestUrgent and Recuperate taking the rest, so reading the
                // same predicate dresses the recovering and leaves the bleeding alone. One colonist
                // either side of one wound, so the wound is the only difference.
                BodyPartRecord part = fix.Pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault();
                Hediff wound = HediffMaker.MakeHediff(HediffDefOf.Cut, fix.Pawn, part);
                wound.Severity = 6f;
                fix.Pawn.health.AddHediff(wound, part);
                story.Note(name + " is given a cut, left untended");
                story.Check(HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn),
                            "which makes " + name + " need a doctor now");
                story.Check(!fix.Pawn.Downed,
                            "without downing them, which would keep them from the stand for another reason");
                story.Check(!Diverts(fix, JobMaker.MakeJob(laydown, bed), JobTag.RestingForMedicalReasons),
                            "so going to bed now, " + name + " is not sent to the stand first");

                fix.Pawn.health.RemoveHediff(wound);
                story.Check(!HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn), "the cut is gone again");
                story.Check(Diverts(fix, JobMaker.MakeJob(laydown, bed), JobTag.RestingForMedicalReasons),
                            "and " + name + " changes first again, so it was the wound that kept them from it");

                // Already in bed: vanilla reissues the patient job to a pawn lying in the bed, after a
                // tend most obviously; without the OnABed guard each reissue is a trip to the wardrobe.
                IntVec3 wasAt = fix.Pawn.Position;
                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = bed.Position;
                story.Check(Patch_JobInterception.OnABed(fix.Pawn), name + ", standing on the bed, counts as in it");
                story.Check(!Diverts(fix, JobMaker.MakeJob(laydown, bed), JobTag.RestingForMedicalReasons),
                            "and told again to rest there, stays in bed");

                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = wasAt;

                Job forced = JobMaker.MakeJob(laydown, bed);
                forced.playerForced = true;
                story.Check(!Diverts(fix, forced, JobTag.RestingForMedicalReasons),
                            "a trip to the sickbed the player orders never stops at the stand");
            }
        }

        /// <summary>
        /// Medical emergencies change first. Off, an emergency is never delayed for a change; on,
        /// the two medical emergencies (the doctor's tend and the critical patient's trip to bed)
        /// stop being exempt, and fighting a fire still does not. The firefighting check earns the
        /// test: a setting keyed on the emergency flag alone would walk a colonist to a wardrobe
        /// while the base burns (docs/DESIGN.md, "The sleep branch"). The stand is ticked for
        /// firefighting so that refusal cannot be read as "no stand served that work". The setting
        /// is put back before the end, or every later test would test something nobody chose.
        /// </summary>
        [Test]
        [TestDescription("with Medical emergencies change first on, a doctor's emergency tend and a critical "
                         + "patient's trip to bed wait for a change, and a fire still never does")]
        internal IEnumerator MedicalEmergenciesChangeFirstButFiresDoNot()
        {
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                if (ShiftChangeMod.Settings == null)
                {
                    story.Check(false, "Shift Change's settings can be read");
                    return;
                }
                WorkGiverDef tendEmergency =
                    DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendEmergency");
                WorkGiverDef fightFires = DefDatabase<WorkGiverDef>.GetNamedSilentFail("FightFires");
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                WorkTypeDef firefighter = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Firefighter");
                WorkTypeDef bedRest = DefDatabase<WorkTypeDef>.GetNamedSilentFail("PatientBedRest");
                ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                JobDef laydown = DefDatabase<JobDef>.GetNamedSilentFail("LayDown");
                if (tendEmergency == null || fightFires == null || doctor == null
                    || firefighter == null || bedRest == null || bedDef == null || laydown == null)
                {
                    story.Check(false, "the game has its emergency tending and firefighting, its doctoring, "
                                       + "firefighting and bed rest work, a bed and its lay-down job");
                    return;
                }

                bool was = ShiftChangeMod.Settings.medicalEmergenciesChangeFirst;
                story.Check(!was, "Medical emergencies change first is off, as it ships");
                story.Check(tendEmergency.emergency,
                            "the game still counts tending patients with urgent needs as an emergency");
                story.Check(fightFires.emergency, "and fighting fires too, which is the trap");

                fix.Comp.ToggleWork(doctor);
                fix.Comp.ToggleWork(firefighter);
                story.Note("the stand is ticked for doctoring and firefighting");
                story.Check(fix.Comp.HandlesWork(doctor), "the stand serves doctoring");
                story.Check(fix.Comp.HandlesWork(firefighter),
                            "and firefighting, so a refusal below cannot mean no stand serves it");
                story.Check(!Diverts(fix, WorkJob(fix, tendEmergency)),
                            "with the setting off, " + name + " answers an emergency tend without changing");
                story.Check(!Diverts(fix, WorkJob(fix, fightFires)), "and goes to fight a fire without changing");

                ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = true;
                story.Note("Medical emergencies change first is turned on");
                story.Check(Diverts(fix, WorkJob(fix, tendEmergency)),
                            "with it on, " + name + " changes first for the emergency tend");
                story.Check(!Diverts(fix, WorkJob(fix, fightFires)),
                            "but still goes straight to the fire: the setting is for medical emergencies only");

                // The patient side: the critical patient's job carries no giver to flag, so the
                // setting relaxes the urgency refusal in MedicalRestWorkType instead.
                Building_Bed bed = DebugTools_Fixtures.Spawn(
                    fix.Map, bedDef, ThingDefOf.WoodLog,
                    new IntVec3(fix.Stand.Position.x + 2, 0, fix.Stand.Position.z + 2),
                    Rot4.North) as Building_Bed;
                if (bed == null)
                {
                    ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = was;
                    story.Check(false, "a bed is built in the stand's room");
                    return;
                }
                bed.Medical = true;
                fix.Comp.ToggleWork(bedRest);

                BodyPartRecord part = fix.Pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault();
                Hediff wound = HediffMaker.MakeHediff(HediffDefOf.Cut, fix.Pawn, part);
                wound.Severity = 6f;
                fix.Pawn.health.AddHediff(wound, part);
                story.Note("a bed for medical use is built in the room, the stand is ticked for resting in bed "
                           + "too, and " + name + " is given a cut, left untended");
                story.Check(HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn),
                            "which makes " + name + " an urgent patient");
                story.Check(fix.Comp.HandlesWork(bedRest), "and the stand is ticked for resting in bed");
                story.Check(Diverts(fix, JobMaker.MakeJob(laydown, bed), JobTag.RestingForMedicalReasons),
                            "so with the setting on, " + name + " stops at the stand on the way to the sickbed");

                ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = false;
                story.Note("Medical emergencies change first is turned off");
                story.Check(!Diverts(fix, JobMaker.MakeJob(laydown, bed), JobTag.RestingForMedicalReasons),
                            "and with it off goes straight to bed: one setting covers doctor and patient");

                fix.Pawn.health.RemoveHediff(wound);
                ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = was;
                story.Check(!ShiftChangeMod.Settings.medicalEmergenciesChangeFirst,
                            "Medical emergencies change first is off again for the tests that follow");
            }
        }

        /// <summary>
        /// New stands are used for shift changes decides what a stand made from then on starts as,
        /// through <c>ThingMaker.MakeThing</c>, the call a finished frame makes. A stand made with
        /// it off is built as vanilla furniture; the scene's stand, made before the change, stays
        /// in service. That a load never applies it is checked in
        /// <see cref="DebugTools_SaveRoundTrip.RoundTrip"/>.
        /// </summary>
        [Test]
        [TestDescription("with New stands are used for shift changes off, a stand built from then on is "
                         + "ordinary furniture, and the stands already built stay in service")]
        internal IEnumerator NewStandsFollowTheSetting()
        {
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Fixture fix, Story story)
            {
                bool was = ShiftChangeMod.Settings.newStandsInService;
                story.Check(was, "New stands are used for shift changes is on, as it ships");
                try
                {
                    CompShiftStand on = (ThingMaker.MakeThing(fix.Stand.def, ThingDefOf.WoodLog)
                        as ThingWithComps)?.TryGetComp<CompShiftStand>();
                    story.Check(on != null && on.IsAutomatic,
                                "with it on, a new stand starts on Automatic, from its room");

                    ShiftChangeMod.Settings.newStandsInService = false;
                    story.Note("New stands are used for shift changes is turned off");
                    Building_OutfitStand stand =
                        ThingMaker.MakeThing(fix.Stand.def, ThingDefOf.WoodLog) as Building_OutfitStand;
                    CompShiftStand off = stand?.TryGetComp<CompShiftStand>();
                    if (off == null || Patch_AllowRemovingToggle.AllowRemovingItemsRef == null)
                    {
                        story.Check(false, "with it off, a stand can still be made, and its Allow removing items read");
                        return;
                    }
                    story.Check(off.IsExcluded, "with it off, a new stand starts as Not used for shift changes");

                    // Built with Allow removing items ticked: a stand in service would untick it here.
                    stand.SetFactionDirect(Faction.OfPlayer);
                    Patch_AllowRemovingToggle.AllowRemovingItemsRef(stand) = true;
                    GenSpawn.Spawn(stand, fix.Stand.Position + new IntVec3(2, 0, 0), fix.Map, Rot4.North);
                    story.Note("it is built two cells along, with Allow removing items ticked");
                    story.Check(off.IsExcluded, "and stays so once it is on the map");
                    story.Check(Patch_AllowRemovingToggle.AllowRemovingItemsRef(stand),
                                "its Allow removing items stays ticked, as on a vanilla stand");
                    story.Check(!off.BlocksTrade, "and what it holds can be traded");
                    story.Check(!fix.Comp.IsExcluded, "while the stand built before the change is still in service");
                }
                finally
                {
                    ShiftChangeMod.Settings.newStandsInService = was;
                }
                story.Check(ShiftChangeMod.Settings.newStandsInService,
                            "New stands are used for shift changes is on again for the tests that follow");
            }
        }

        /// <summary>
        /// Sorts every shape of job the other tests build, in the order Shift Change asks its
        /// questions of a job (<see cref="Patch_JobInterception.Classify"/>): a follow-up, a
        /// delivery, a meal run, work that dresses nobody, work, sleep, recreation, or something
        /// else. The tests that send a job through the decision test what each trigger does with
        /// the answer; this one tests the answer, so a change to the order fails here by name.
        /// Visiting the sick is the row the order exists for: from the doctor's work giver it is
        /// work, charged to that giver's work (doctoring unless a mod moved it), though it carries
        /// a joyKind; from the joy giver it is recreation. It runs in a scene because medical rest
        /// depends on the colonist's health and on a bed.
        /// </summary>
        [Test]
        [TestDescription("each job a colonist starts counts as work, recreation, sleep, a delivery or "
                         + "something else, as Shift Change sorts it before deciding who changes")]
        internal IEnumerator WhatEachJobCountsAs()
        {
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                if (ShiftChangeMod.Settings == null)
                {
                    story.Check(false, "Shift Change's settings can be read");
                    return;
                }
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                WorkTypeDef bedRest = ShiftChangeDefOf.PatientBedRest;
                WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
                WorkGiverDef visitGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("VisitSickPawn");
                WorkGiverDef tendEmergency = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendEmergency");
                WorkGiverDef fightFires = DefDatabase<WorkGiverDef>.GetNamedSilentFail("FightFires");
                WorkGiverDef hauling = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulGeneral");
                WorkGiverDef doctorFeed = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorFeedHumanlikes");
                JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
                JobDef reading = DefDatabase<JobDef>.GetNamedSilentFail("Reading");
                JobDef visitSick = DefDatabase<JobDef>.GetNamedSilentFail("VisitSickPawn");
                JobDef unload = DefDatabase<JobDef>.GetNamedSilentFail("UnloadYourInventory");
                ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
                if (doctor == null || bedRest == null || tend == null || visitGiver == null || tendEmergency == null
                    || fightFires == null || hauling == null || doctorFeed == null || chess == null || reading == null
                    || visitSick == null || unload == null || bedDef == null || mealDef == null)
                {
                    story.Check(false, "the game has the work, work givers, jobs, bed and meal the table uses");
                    return;
                }
                Map map = fix.Map;
                IntVec3 at = fix.Stand.Position;
                Building_Bed bed = DebugTools_Fixtures.Spawn(
                    map, bedDef, ThingDefOf.WoodLog, at + new IntVec3(2, 0, 0), Rot4.North) as Building_Bed;
                Thing meal = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), at + new IntVec3(3, 0, 3), map);
                Thing steel = HarnessCatchments.Steel(map, at + new IntVec3(1, 0, 3));
                if (bed == null || meal == null || steel == null)
                {
                    story.Check(false, "a bed, a meal and a stack of steel are put in the stand's room");
                    return;
                }
                story.Note("a bed, a simple meal and twenty steel are put in the stand's room");
                IntVec3 cell = at + new IntVec3(3, 0, 2);

                story.Check(!HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn)
                            && !Patch_JobInterception.OnABed(fix.Pawn),
                            name + " needs no doctor and is not in a bed");

                // Work, recreation, and the row the order exists for.
                JobClass visitAsWork = KindOf(fix, Stamped(visitSick, visitGiver));
                SortsAs(story, fix, "tending a patient for " + Giver(tend), HarnessCatchments.WorkAt(tend, cell), null,
                        JobKind.Work, doctor);
                story.Check(visitGiver.workType == doctor || HarnessTables.IsModWork(visitGiver.workType),
                            Giver(visitGiver) + " belongs to the doctor's work, or to a mod's own");
                SortsAs(story, fix, "visiting the sick for " + Giver(visitGiver),
                        Stamped(visitSick, visitGiver), null, JobKind.Work, visitGiver.workType);
                story.Check(visitAsWork.RecreationJob,
                            "and still recreation too, so its room is read from where the visitor sits");
                SortsAs(story, fix, "visiting the sick for fun, from no work", JobMaker.MakeJob(visitSick), null,
                        JobKind.Recreation, null);
                SortsAs(story, fix, "a game of chess", JobMaker.MakeJob(chess), null, JobKind.Recreation, null);
                SortsAs(story, fix, "reading, which the game counts as recreation", JobMaker.MakeJob(reading), null,
                        JobKind.Other, null);
                story.Check(!KindOf(fix, JobMaker.MakeJob(reading)).RecreationJob,
                            "and not recreation here, since a reader picks their spot only once begun");
                SortsAs(story, fix, "eating", IngestJob(meal), null, JobKind.Other, null);
                story.Check(KindOf(fix, IngestJob(meal)).Ingest, "which counts as eating");

                // Sleep and medical rest, the setting off and on. The patient givers tag a medical
                // lay-down RestingForMedicalReasons, and vanilla requeues it untagged when it slips a
                // haul in ahead.
                bool was = ShiftChangeMod.Settings.medicalEmergenciesChangeFirst;
                Hediff wound = null;
                try
                {
                    ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = false;
                    story.Note("Medical emergencies change first is off");
                    JobClass sleep = KindOf(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed));
                    JobClass ground = KindOf(fix, JobMaker.MakeJob(JobDefOf.LayDown, fix.Pawn.Position));
                    SortsAs(story, fix, "going to bed", JobMaker.MakeJob(JobDefOf.LayDown, bed), null,
                            JobKind.Rest, null);
                    story.Check(sleep.OrdinarySleep && sleep.LyingDown && !sleep.UrgentPatient,
                                "ordinary sleep, lying down, and no urgent patient's");
                    SortsAs(story, fix, "going to bed to recuperate, as bed rest sends a patient",
                            JobMaker.MakeJob(JobDefOf.LayDown, bed), JobTag.RestingForMedicalReasons,
                            JobKind.Work, bedRest);
                    story.Check(!KindOf(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed),
                                                    JobTag.RestingForMedicalReasons).OrdinarySleep,
                                "which is not ordinary sleep");
                    SortsAs(story, fix, "sleeping on the ground", JobMaker.MakeJob(JobDefOf.LayDown, fix.Pawn.Position),
                            null, JobKind.Other, null);
                    story.Check(ground.LyingDown && !ground.RestJob, "lying down, but not going to bed");

                    BodyPartRecord part = fix.Pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault();
                    wound = HediffMaker.MakeHediff(HediffDefOf.Cut, fix.Pawn, part);
                    // An untended wound makes the colonist an urgent patient; the check below
                    // shows they stay on their feet.
                    wound.Severity = 6f;
                    fix.Pawn.health.AddHediff(wound, part);
                    story.Note(name + " is given a cut, left untended");
                    JobClass urgent = KindOf(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed));
                    story.Check(HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn) && !fix.Pawn.Downed,
                                "which makes " + name + " an urgent patient, still on their feet");
                    SortsAs(story, fix, "the urgent patient going to bed to recuperate, with the setting off",
                            JobMaker.MakeJob(JobDefOf.LayDown, bed), JobTag.RestingForMedicalReasons,
                            JobKind.Rest, null);
                    SortsAs(story, fix, "and the same trip to bed put back in the queue",
                            JobMaker.MakeJob(JobDefOf.LayDown, bed), null, JobKind.Rest, null);
                    story.Check(urgent.UrgentPatient && !urgent.OrdinarySleep,
                                "an urgent patient's, never ordinary sleep");

                    ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = true;
                    story.Note("Medical emergencies change first is turned on");
                    SortsAs(story, fix, "the urgent patient going to bed, with the setting on",
                            JobMaker.MakeJob(JobDefOf.LayDown, bed), JobTag.RestingForMedicalReasons,
                            JobKind.Work, bedRest);
                    SortsAs(story, fix, "and put back in the queue", JobMaker.MakeJob(JobDefOf.LayDown, bed), null,
                            JobKind.Work, bedRest);
                    story.Check(!KindOf(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed)).UrgentPatient,
                                "and no longer an urgent patient's, now the setting lets them change");
                }
                finally
                {
                    ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = was;
                    if (wound != null)
                    {
                        fix.Pawn.health.RemoveHediff(wound);
                    }
                }

                // Deliveries and the site jobs the same givers hand out.
                foreach (WorkGiverDef giver in JobRoomTargets.DeliveryGivers.OrderBy(g => g.defName))
                {
                    SortsAs(story, fix, "steel carried for " + Giver(giver),
                            HarnessCatchments.DeliveryAt(giver, steel.Position, cell), null,
                            JobKind.Delivery, giver.workType);
                    SortsAs(story, fix, "and a floor removal it hands out", HarnessCatchments.FloorRemovalAt(giver, cell),
                            null, JobKind.Work, giver.workType);
                }
                WorkGiverDef seeds = DefDatabase<WorkGiverDef>.GetNamedSilentFail(JobRoomTargets.SeedPlanter);
                if (seeds != null)
                {
                    SortsAs(story, fix, "sowing for " + Giver(seeds), Stamped(JobDefOf.PlantSeed, seeds),
                            null, JobKind.Delivery, seeds.workType);
                    SortsAs(story, fix, "and cutting a plant, which it hands out too",
                            Stamped(JobDefOf.CutPlant, seeds), null, JobKind.Work, seeds.workType);
                }

                // Meal runs from every feeding giver, and an ignored giver's job that is not one, the
                // shape of Rimatomics' chemfuel run.
                foreach (string giverName in HarnessRideAlong.FeedPatientGivers.Concat(HarnessRideAlong.DeliverFoodGivers))
                {
                    WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(giverName);
                    if (giver == null)
                    {
                        continue;
                    }
                    JobDef def = HarnessRideAlong.DeliverFoodGivers.Contains(giverName)
                        ? JobDefOf.DeliverFood
                        : JobDefOf.FeedPatient;
                    SortsAs(story, fix, "a meal carried for " + Giver(giver),
                            HarnessRideAlong.FeedingJob(def, meal, fix.Pawn, giver), null,
                            JobKind.MealRun, giver.workType);
                }
                SortsAs(story, fix, "a haul handed out by " + Giver(doctorFeed),
                        HarnessCatchments.Haul(doctorFeed, steel, cell), null, JobKind.IgnoredGiver, doctorFeed.workType);

                // The gates: the jobs that ride along (the job tracker's pause, an unload), a direct
                // order, the emergencies, and the walks no work hands out.
                Job forced = HarnessCatchments.WorkAt(tend, cell);
                forced.playerForced = true;
                JobClass order = KindOf(fix, forced);
                SortsAs(story, fix, "a moment's pause between jobs", JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1),
                        null, JobKind.RidesAlong, null);
                story.Check(Patch_JobInterception.ExemptInEitherDirection(
                                KindOf(fix, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1))),
                            "which never sends anyone to change, either way");
                SortsAs(story, fix, "unloading their inventory, from no work", JobMaker.MakeJob(unload), null,
                        JobKind.RidesAlong, null);
                SortsAs(story, fix, "a haul for " + Giver(hauling), HarnessCatchments.Haul(hauling, steel, cell), null,
                        JobKind.Work, hauling.workType);
                SortsAs(story, fix, "a haul from no work, as the game's haul on the way starts",
                        HarnessRideAlong.GiverlessHaul(steel, cell), null, JobKind.Other, null);
                story.Check(order.Kind == JobKind.Work && order.PlayerForced
                            && Patch_JobInterception.ExemptInEitherDirection(order),
                            "a tend the player orders is still work, and never sends anyone to change");

                try
                {
                    ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = false;
                    story.Note("Medical emergencies change first is off");
                    story.Check(KindOf(fix, HarnessCatchments.WorkAt(tendEmergency, cell)).ExemptEmergency
                                && KindOf(fix, HarnessCatchments.WorkAt(fightFires, cell)).ExemptEmergency,
                                "an emergency tend and a fire never wait for a change");
                    story.Check(!KindOf(fix, HarnessCatchments.WorkAt(tend, cell)).ExemptEmergency,
                                "while an ordinary tend may");
                    ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = true;
                    story.Note("Medical emergencies change first is turned on");
                    story.Check(!KindOf(fix, HarnessCatchments.WorkAt(tendEmergency, cell)).ExemptEmergency,
                                "the emergency tend may wait for one now");
                    story.Check(KindOf(fix, HarnessCatchments.WorkAt(fightFires, cell)).ExemptEmergency,
                                "and the fire still never does");
                }
                finally
                {
                    ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = was;
                }

                Job walk = JobMaker.MakeJob(JobDefOf.Goto, cell);
                Job walkForWork = JobMaker.MakeJob(JobDefOf.Goto, cell);
                walkForWork.workGiverDef = tend;
                SortsAs(story, fix, "a walk", walk, null, JobKind.Other, null);
                story.Check(KindOf(fix, walk).Incidental,
                            "and one the outfit can stay on for, inside a stand's radius or area");
                story.Check(!KindOf(fix, walkForWork).Incidental, "but not the same walk for work");

                // Odyssey hands fishing out with no giver, so it is sorted by tag. Vanilla requeues
                // a job without its tag when it slips a haul in ahead, hence the row put back in the
                // queue.
                WorkGiverDef fishGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Fish");
                JobDef fish = JobDefOf.Fish;
                if (fishGiver != null && fish != null)
                {
                    Job forcedFish = JobMaker.MakeJob(fish, cell, cell);
                    forcedFish.playerForced = true;
                    SortsAs(story, fix, "fishing for " + Giver(fishGiver), Stamped(fish, fishGiver),
                            JobTag.Fieldwork, JobKind.Work, fishGiver.workType);
                    SortsAs(story, fix, "fishing as Odyssey hands it out, from no work",
                            JobMaker.MakeJob(fish, cell, cell), JobTag.Fieldwork, JobKind.Work, fishGiver.workType);
                    SortsAs(story, fix, "and put back in the queue", JobMaker.MakeJob(fish, cell, cell), null,
                            JobKind.Work, fishGiver.workType);
                    SortsAs(story, fix, "and tagged as an order", JobMaker.MakeJob(fish, cell, cell), JobTag.Misc,
                            JobKind.Other, null);
                    SortsAs(story, fix, "and ordered by the player", forcedFish, null, JobKind.Other, null);
                }
            }
        }

        /// <summary>
        /// An empty deposit-only stand is not somewhere to put apparel, and an ordinary one still
        /// is.
        ///
        /// <para>Driven through <c>StoreUtility</c>'s own search, since the claim is "a hauler does
        /// not come", not our flag. The one approximation is <c>currentPriority</c>, passed as
        /// <c>Normal</c>, what <c>CurrentStoragePriorityOf</c> returns for a garment in an ordinary
        /// stockpile; everything downstream is the engine's.</para>
        ///
        /// <para>Two stands, because "not stand A" is a claim a broken search satisfies too: B is
        /// an ordinary stand, further away, and the hauler must switch to it. B is filled at the
        /// end, because <c>Accepts</c> ends at <c>HasRoomForApparelOfDef</c>, so a stand already
        /// holding a conflicting garment is immune by vanilla's own gate, and deposit-only, whose
        /// resting state is empty, never gets that immunity on its own.</para>
        /// </summary>
        [Test]
        [TestDescription("haulers walk past a deposit-only stand to an ordinary one, "
                         + "and come back to it once Deposit only is off")]
        internal IEnumerator HaulersLeaveADepositOnlyStandAlone()
        {
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Fixture fix, Story story)
            {
                Map map = fix.Map;
                ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
                ThingDef dusterDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Duster");
                if (standDef == null || dusterDef == null)
                {
                    story.Check(false, "the game has its outfit stand and duster");
                    return;
                }

                // The scene's stand holds a duster, and a stand holding one refuses the duster below
                // by vanilla's own conflict gate. Empty is also deposit-only's resting state.
                ClearStand(fix.Stand);
                IntVec3 origin = fix.Stand.Position;

                Building_OutfitStand other = DebugTools_Fixtures.Spawn(
                    map, standDef, ThingDefOf.WoodLog,
                    origin + new IntVec3(3, 0, 0), Rot4.North) as Building_OutfitStand;
                if (other == null)
                {
                    story.Check(false, "a second, ordinary stand is built three cells along");
                    return;
                }
                ClearStand(other);

                // Next to the scene's stand and two cells from the other, so the search's distance
                // tiebreak has one answer and the switch below shows.
                Apparel garment = DebugTools_Fixtures.MakeGarment(dusterDef, null);
                GenSpawn.Spawn(garment, origin + new IntVec3(1, 0, 0), map);
                story.Note("the stand is emptied, a second, empty stand is built three cells along, "
                           + "and a duster is dropped beside the first");

                IHaulDestination found;
                story.Check(
                    StoreUtility.TryFindBestBetterNonSlotGroupStorageFor(
                        garment, null, map, StoragePriority.Normal, Faction.OfPlayer, out found)
                    && found == fix.Stand,
                    "a hauler would carry the duster to the nearer empty stand, over a stockpile of normal "
                    + "priority, as in the base game");

                fix.Comp.ToggleRest();
                fix.Comp.SetDepositOnly(true);
                story.Note("the first stand is ticked for sleeping, with Deposit only, issue no outfit");
                story.Check(fix.Comp.DepositOnly, "the stand is deposit only");
                story.Check(
                    StoreUtility.TryFindBestBetterNonSlotGroupStorageFor(
                        garment, null, map, StoragePriority.Normal, Faction.OfPlayer, out found)
                    && found == other,
                    "and the hauler now walks past it to the ordinary stand instead");

                // Only the haul search is narrowed: if this fails, a stand's own deposit and a
                // player's ordered delivery went with it.
                story.Check(((IHaulDestination)fix.Stand).Accepts(garment),
                            "while the stand still accepts the duster, so its owner's deposit and a "
                            + "delivery the player orders still go there");

                if (!StockOne(other, "Apparel_Duster"))
                {
                    story.Check(false, "the ordinary stand takes a duster of its own");
                    return;
                }
                story.Check(!((IHaulDestination)other).Accepts(garment),
                            "the ordinary stand, holding a duster now, has no room for another, "
                            + "by the game's own rule");
                bool anywhere = StoreUtility.TryFindBestBetterNonSlotGroupStorageFor(
                    garment, null, map, StoragePriority.Normal, Faction.OfPlayer, out found);
                story.Check(!anywhere || (found != fix.Stand && found != other),
                            "so with one stand deposit only and the other full, neither takes it");

                fix.Comp.SetDepositOnly(false);
                story.Note("Deposit only is turned off");
                story.Check(StoreUtility.TryFindBestBetterNonSlotGroupStorageFor(
                                garment, null, map, StoragePriority.Normal, Faction.OfPlayer, out found)
                            && found == fix.Stand,
                            "and the hauler comes back to the first stand at once");
            }
        }

        /// <summary>
        /// What the mod's description promises, as a table: automatic work only, emergencies never
        /// delayed, drafted colonists left alone, work the stand does not serve ignored. They are
        /// advertised behaviour, not a past bug. Driven through <c>TryInsertSwap</c>, the decision
        /// the job interception calls. Each refusal sits beside the same set-up without the reason
        /// for it, since a stand that was never going to dress anyone satisfies "did not send them"
        /// too.
        /// </summary>
        [Test]
        [TestDescription("a doctor changes before tending a patient on their own, but never for an order, "
                         + "an emergency, while drafted, or for work the stand does not serve")]
        internal IEnumerator OrdersEmergenciesAndDraftsAreNeverHeldUp()
        {
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                          capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                          InARoomADoctor, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
                WorkGiverDef urgent = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendEmergency");
                if (doctor == null || tend == null)
                {
                    story.Check(false, "the game has its doctoring and tending");
                    return;
                }
                // The pad's room has no role to infer work from, so name the work.
                fix.Comp.ToggleWork(doctor);
                story.Note("the stand is ticked for doctoring");

                story.Check(fix.Comp.HandlesWork(doctor), "the stand serves doctoring");
                story.Check(Diverts(fix, WorkJob(fix, tend)),
                            name + " changes before tending a patient on their own");

                Job forced = WorkJob(fix, tend);
                forced.playerForced = true;
                story.Check(!Diverts(fix, forced), "but never when the player orders it with a right-click");

                if (urgent != null)
                {
                    story.Check(urgent.emergency,
                                "the game still counts tending patients with urgent needs as an emergency");
                    story.Check(!Diverts(fix, WorkJob(fix, urgent)),
                                "and " + name + " answers one without stopping at the stand");
                }

                if (fix.Pawn.drafter != null)
                {
                    fix.Pawn.drafter.Drafted = true;
                    story.Check(!Diverts(fix, WorkJob(fix, tend)), "drafted, " + name + " is never sent to change");
                    fix.Pawn.drafter.Drafted = false;
                    story.Check(Diverts(fix, WorkJob(fix, tend)),
                                "and undrafted, is sent to change for the same tending again");
                }

                WorkGiverDef cook = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsCook");
                if (cook != null)
                {
                    story.Check(!Diverts(fix, WorkJob(fix, cook)),
                                name + " cooks without changing: the stand does not serve cooking");
                }
            }
        }

        /// <summary>
        /// A job handed to a colonist in the middle of a change is not put off for another change
        /// on top of it, whatever interrupted them (docs/DESIGN.md, "The gates"); a lord's duty is
        /// one cause. The same work job before the change starts and after it ends shows the stand
        /// would dress them otherwise. The colonist is put on a real change through their own job
        /// tracker, because the decision reads their current job from exactly there.
        /// </summary>
        [Test]
        [TestDescription("a colonist already changing at a stand is not sent to change again "
                         + "by the next job they are handed")]
        internal IEnumerator AJobMidChangeIsNotPutOffAgain()
        {
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                          capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                          InARoomADoctor, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
                if (doctor == null || tend == null)
                {
                    story.Check(false, "the game has its doctoring and tending");
                    return;
                }
                fix.Comp.ToggleWork(doctor);
                int swept = MakeCalm(fix.Map);
                story.Note("the stand is ticked for doctoring, and the map is calm ("
                           + swept + " hostile things cleared)");

                story.Check(Diverts(fix, WorkJob(fix, tend)), "idle, " + name + " changes before tending a patient");

                Job swap = JobMaker.MakeJob(ShiftChangeDefOf.ShiftChange_SwapAtStand, fix.Stand);
                fix.Pawn.jobs.StartJob(swap, JobCondition.InterruptForced, null,
                    resumeCurJobAfterwards: false, cancelBusyStances: true, null,
                    JobTag.ChangingApparel);
                story.Check(fix.Pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                            name + " starts changing at the stand");
                story.Check(!Diverts(fix, WorkJob(fix, tend)),
                            "and a tending job handed to them now is not put off for another change");

                fix.Pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                story.Check(fix.Pawn.CurJobDef != ShiftChangeDefOf.ShiftChange_SwapAtStand, "the change is stopped");
                story.Check(Diverts(fix, WorkJob(fix, tend)),
                            "and the next tending job sends " + name + " to change as usual");
            }
        }

        /// <summary>
        /// Work and recreation are exclusive on one stand, which holds one outfit for one purpose
        /// (docs/DESIGN.md, "The recreation branch"). Checked both ways because two separate
        /// methods each clear the other's half, and a half-applied rule leaves a stand claiming
        /// both, which the dialog cannot show. Each cleared half is checked beside the half just
        /// turned on; the test ends with both off, which must read as not used.
        /// </summary>
        [Test]
        [TestDescription("ticking recreation on a stand clears its work, and ticking work clears recreation")]
        internal IEnumerator AStandServesWorkOrRecreationNotBoth()
        {
            return Staged((m, p) => Stage(m, p, StageKit.Displacing), InTheOpen, Body);

            void Body(Fixture fix, Story story)
            {
                WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                if (doctor == null)
                {
                    story.Check(false, "the game has its doctoring");
                    return;
                }

                fix.Comp.ToggleWork(doctor);
                story.Note("doctoring is ticked on the stand");
                story.Check(fix.Comp.HandlesWork(doctor), "the stand serves doctoring");
                story.Check(!fix.Comp.HandlesRecreation(), "and not recreation");

                fix.Comp.ToggleRecreation();
                story.Note("Recreation is ticked");
                story.Check(fix.Comp.HandlesRecreation(), "the stand serves recreation");
                story.Check(!fix.Comp.HandlesWork(doctor), "and doctoring was cleared, not kept beside it");
                story.Check(fix.Comp.WorkTypes.Count == 0, "with no work left ticked");

                fix.Comp.ToggleWork(doctor);
                story.Note("doctoring is ticked again");
                story.Check(fix.Comp.HandlesWork(doctor), "the stand serves doctoring again");
                story.Check(!fix.Comp.HandlesRecreation(), "and recreation was cleared in turn");

                fix.Comp.ToggleRecreation();
                fix.Comp.ToggleRecreation();
                story.Note("Recreation is ticked and unticked");
                story.Check(!fix.Comp.HandlesRecreation(), "recreation is off again");
                story.Check(fix.Comp.IsExcluded,
                            "and the stand reads as Not used for shift changes, not Automatic");
            }
        }

        /// <summary>
        /// The sleep trigger through the mod's real decision as a job starts: going to bed in the
        /// stand's room sends the colonist to change, and the lay-downs that must not do not. The
        /// refusals carry the test: going to sleep and medical bed rest share one job and one
        /// driver, held apart by two things nothing else in the mod would notice breaking, the
        /// job's work giver and its tag. Break either and a pyjama stand dresses the wounded, and
        /// the hospital gown's row goes dead without a word. It also covers the lay-down the game
        /// hands out again in the night (it sends nobody to change), which jobs keep a colonist in
        /// bed, and the wake-up: on shift and on the bed, a job that leaves the room has the change
        /// back put first, while lying down again does not.
        /// </summary>
        [Test]
        [TestDescription("a colonist turning in for the night changes at the bedroom's stand first, never for "
                         + "bed rest or an order, and on waking changes back before leaving the room")]
        internal IEnumerator GoingToBedInTheRoomSendsThemToChange()
        {
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                JobDef laydown = DefDatabase<JobDef>.GetNamedSilentFail("LayDown");
                JobDef groundSleep = DefDatabase<JobDef>.GetNamedSilentFail("Wait_Asleep");
                WorkGiverDef recuperate =
                    DefDatabase<WorkGiverDef>.GetNamedSilentFail("PatientGoToBedRecuperate");
                if (bedDef == null || laydown == null || groundSleep == null)
                {
                    story.Check(false, "the game has its bed, its lay-down job and sleeping on the ground");
                    return;
                }

                Building_Bed bed = DebugTools_Fixtures.Spawn(
                    fix.Map, bedDef, ThingDefOf.WoodLog,
                    new IntVec3(fix.Stand.Position.x + 2, 0, fix.Stand.Position.z + 2),
                    Rot4.North) as Building_Bed;
                if (bed == null)
                {
                    story.Check(false, "a bed is built in the stand's room");
                    return;
                }
                story.Note("a bed is built in the stand's room");

                // Sleeping is not ticked by hand, since a bed changes the room's role:
                // RoomRoleWorker_Bedroom scores a room holding one unowned humanlike bed at 100000,
                // beating every other worker, so the pad is a bedroom as the bed lands, the stand
                // serves sleep from its role already, and a tick would turn it off. The automatic
                // answer is the stronger claim anyway.
                //
                // An untagged lay-down by an urgent patient is medical bed rest (IsMedicalLayDown),
                // which no sleep stand dresses, so going to bed needs a colonist who needs no doctor;
                // the scene's colonist is generated without one
                // (DebugTools_Fixtures.RemoveTendableConditions).
                story.Check(!HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn),
                            name + " needs no doctor to begin with");
                story.Check(fix.Comp.HandlesRest(),
                            "the bed makes the room a bedroom, so the stand serves sleep on its own");
                story.Check(fix.Comp.IsAutomatic,
                            "the stand is on Automatic: the room decides, not a tick in its settings");
                story.Check(!fix.Comp.HandlesRecreation(),
                            "and it does not serve recreation, which no room serves beside sleep");
                if (!fix.Comp.HandlesRest())
                {
                    // A modded room role may outscore the bedroom here: then tick Sleeping instead.
                    fix.Comp.ToggleRest();
                    story.Check(fix.Comp.HandlesRest(), "Sleeping, ticked by hand instead, makes it serve sleep");
                }

                story.Check(Patch_JobInterception.IsRestJob(JobMaker.MakeJob(laydown, bed)),
                            "lying down in the bed counts as going to sleep");
                story.Check(Diverts(fix, JobMaker.MakeJob(laydown, bed)),
                            "and going to bed in this room sends " + name + " to change at the stand first");

                Job onTheFloor = JobMaker.MakeJob(groundSleep, fix.Pawn.Position);
                story.Check(!Patch_JobInterception.IsRestJob(onTheFloor),
                            "sleeping on the ground does not count as going to bed");
                story.Check(!Diverts(fix, onTheFloor), "and sends nobody to change");

                story.Check(!Diverts(fix, JobMaker.MakeJob(laydown, bed), JobTag.RestingForMedicalReasons),
                            "going to bed to rest for medical reasons does not send " + name + " to change");

                if (recuperate != null)
                {
                    Job viaWorkGiver = JobMaker.MakeJob(laydown, bed);
                    viaWorkGiver.workGiverDef = recuperate;
                    story.Check(!Diverts(fix, viaWorkGiver),
                                "nor does going to bed to recuperate, the bed rest work's own job");
                }

                Job forced = JobMaker.MakeJob(laydown, bed);
                forced.playerForced = true;
                story.Check(!Diverts(fix, forced), "and going to bed on the player's order never stops at the stand");

                // The lay-down handed out again in the night, the reason OnABed exists:
                // JobInBedUtility.KeepLyingDown queues a bare LayDown when a job in bed ends, and it
                // reaches StartJob after Pawn_JobTracker has cleared the current job. pawn.InBed()
                // needs a current job, so it is false for a colonist lying in their own bed (true only
                // at the wake-up, when a job starts while the lay-down is current). OnABed reads the
                // colonist's cell instead.
                story.Check(!Patch_JobInterception.OnABed(fix.Pawn), name + ", by the stand, is not on a bed");
                IntVec3 wasAt = fix.Pawn.Position;
                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = bed.Position;
                story.Note(name + " is moved onto the bed");
                story.Check(Patch_JobInterception.OnABed(fix.Pawn), name + " now counts as on the bed");
                story.Check(!Diverts(fix, JobMaker.MakeJob(laydown, bed)),
                            "and told again to lie down, is not walked to the stand");

                // The two readings disagree about this colonist, yet no check fails if OnABed is
                // swapped back for InBed(): the test puts the colonist on the bed by setting their
                // cell, never by running a lay-down, so their posture stays standing and InBed() is
                // false here for a second reason, and the wake-up below would still pass. Catching
                // that needs a lay-down ticked with a live driver. The check sets the two answers
                // side by side, as a tripwire for a reader.
                story.Check(!fix.Pawn.InBed(),
                            "though the game itself says " + name + " is not in bed: that is why Shift Change "
                            + "reads the cell, and why this test alone cannot catch a return to the game's reading");

                // Delete StaysInBed's CanBeginNowWhileLyingDown half and every check above still
                // passes: so both directions, from the same place on the bed.
                story.Check(Patch_JobInterception.StaysInBed(fix.Pawn, JobMaker.MakeJob(laydown, bed)),
                            "lying down keeps " + name + " in bed, so nothing changes them back");
                story.Check(!Patch_JobInterception.StaysInBed(fix.Pawn,
                                JobMaker.MakeJob(JobDefOf.Wait, fix.Pawn.Position)),
                            "a job that cannot be done lying down does not: this is the wake-up, "
                            + "and it must change them back while they are still by the stand");

                // The bed as a target, for jobs done in bed whose driver does not say so (meditating,
                // eating), built as JobGiver_MeditateInBed builds one: the colonist's cell first, the
                // bed second, so only the second half of TargetsBedUnder can catch it.
                Job inBedByTarget = JobMaker.MakeJob(JobDefOf.Wait, fix.Pawn.Position, bed);
                story.Check(Patch_JobInterception.TargetsBedUnder(fix.Pawn, inBedByTarget),
                            "a job aimed at this very bed counts as staying in it");
                story.Check(Patch_JobInterception.StaysInBed(fix.Pawn, inBedByTarget),
                            "so it keeps " + name + " in bed, though the job does not say it is done lying down");

                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = wasAt;
                story.Check(!Patch_JobInterception.OnABed(fix.Pawn), name + " is back by the stand, off the bed");

                // The wake-up, end to end: the checks above test the rules, not that the change back
                // is put first. This runs the decision with the colonist on shift and on the bed,
                // since the failure it guards is a colonist doing the day's first job in sleepwear,
                // then walking back to change. pawn.InBed() reads true at exactly the wake-up, so a
                // guard built on it would hold the change back there.
                //
                // Nothing may be left queued by the jobs above: a job at the head of the queue is
                // one vanilla put back behind a haul, and an errand ahead of the job they dressed for
                // keeps the outfit on (QueuedAheadOfServedWork), which would answer the wake-up.
                Settle(fix.Pawn);
                if (!SwapNow(story, fix))
                {
                    story.Check(false, name + " changes into the stand's duster for the wake-up");
                    return;
                }
                story.Note(name + " changes into the stand's duster");
                story.Check(fix.Comp.OnShift, name + " is on shift, so there is a way back to change");

                IntVec3 outside = new IntVec3(fix.Stand.Position.x + 8, 0, fix.Stand.Position.z);
                if (!outside.InBounds(fix.Map) || outside.GetRoom(fix.Map) == fix.Stand.GetRoom())
                {
                    story.Check(false, "there is ground outside the stand's room, eight cells along");
                    return;
                }

                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = bed.Position;
                story.Note(name + " is moved onto the bed, in the duster");
                story.Check(Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, outside)),
                            "waking, a job that takes " + name + " out of the room changes them back first, "
                            + "before they do it");
                story.Check(!Diverts(fix, JobMaker.MakeJob(laydown, bed)),
                            "while lying down again still does not");

                fix.Pawn.pather?.StopDead();
                fix.Pawn.Position = wasAt;
            }
        }

        /// <summary>
        /// Deposit only: a colonist leaves what the stand's storage filter accepts, keeps the rest
        /// of their clothes on, and gets it all back on the way out. The stand must count as in use
        /// all the same: being on shift counts a garment taken in as well as one handed out, or the
        /// parka sits in a rack with no borrower and no way back, gone from the player's side. And a
        /// filter wide enough to take everything they wear must make the stand decline, not send a
        /// colonist to bed naked.
        /// </summary>
        [Test]
        [TestDescription("at a stand set to Deposit only, a colonist leaves just the parka its storage filter "
                         + "takes, keeps the rest on and gets it back, and is never left with nothing on")]
        internal IEnumerator DepositOnlyTakesTheParkaAndGivesItBack()
        {
            return Staged((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                ThingDef parkaDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Parka");
                ThingDef shirtDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_BasicShirt");
                ThingDef pantsDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Pants");
                if (parkaDef == null || shirtDef == null || pantsDef == null)
                {
                    story.Check(false, "the game has its parka, shirt and trousers");
                    return;
                }
                Apparel parka = null;
                List<Apparel> worn = fix.Pawn.apparel.WornApparel;
                for (int i = 0; i < worn.Count; i++)
                {
                    if (worn[i].def == parkaDef)
                    {
                        parka = worn[i];
                    }
                }
                if (parka == null)
                {
                    story.Check(false, name + " wears a parka to leave at the stand");
                    return;
                }

                fix.Comp.ToggleRest();
                fix.Comp.SetDepositOnly(true);
                story.Note("the stand is ticked for sleeping, with Deposit only, issue no outfit");
                story.Check(fix.Comp.DepositOnly, "the stand is set to Deposit only");

                // The storage filter decides, and its default is generous: OutfitStandBase ships
                // defaultStorageSettings (apparel, less utility items and weapons) and PostMake
                // copies it onto every stand. That default is what gives the never-naked rule work.
                StorageSettings settings = fix.Stand.GetStoreSettings();
                story.Check(settings.filter.Allows(parkaDef),
                            "a newly built stand's storage filter takes ordinary clothes such as the parka");

                settings.filter.SetDisallowAll();
                story.Note("everything is unticked in the stand's storage filter");
                story.Check(!SwapPlan.WouldDress(fix.Pawn, fix.Stand),
                            "and with nothing allowed, the stand is not one to change at");

                settings.filter.SetAllow(parkaDef, allow: true);
                story.Note("the parka is allowed in the filter");
                List<Apparel> wear = new List<Apparel>();
                List<Apparel> store = new List<Apparel>();
                story.Check(SwapPlan.BuildDress(fix.Pawn, fix.Stand, wear, store),
                            "with the parka allowed, the stand plans a change for " + name);
                story.Check(wear.Count == 0, "which hands out nothing");
                story.Check(store.Count == 1 && store.Contains(parka),
                            "and takes in exactly what the filter allows, the parka, and nothing else");

                // Never bare: widen the filter to everything they have on and the stand must decline.
                settings.filter.SetAllow(shirtDef, allow: true);
                settings.filter.SetAllow(pantsDef, allow: true);
                story.Check(!SwapPlan.WouldDress(fix.Pawn, fix.Stand),
                            "with the shirt and trousers allowed too, the stand would take everything, so it declines");
                settings.filter.SetAllow(shirtDef, allow: false);
                settings.filter.SetAllow(pantsDef, allow: false);
                story.Note("the shirt and trousers are disallowed again");

                story.Check(SwapNow(story, fix), name + " leaves their things at the stand");
                story.Check(parka.ParentHolder == fix.Stand, "the parka is on the stand");
                story.Check(!fix.Pawn.apparel.WornApparel.Contains(parka), "and off " + name);
                story.Check(fix.Pawn.apparel.WornApparel.Count == 2, "who keeps their own shirt and trousers on");
                story.Check(fix.Comp.OnShift, "the stand counts as in use");
                story.Check(fix.Comp.Borrower == fix.Pawn, "by " + name);
                story.Check(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                            "and " + name + " is on shift at it, so there is a way back");
                story.Check(fix.Comp.IssuedUniformForReading.Count == 0,
                            "though it handed out nothing, which is the point");

                story.Check(SwapNow(story, fix), name + " collects their things");
                story.Check(fix.Pawn.apparel.WornApparel.Contains(parka), "the parka is back on");
                story.Check(!fix.Comp.OnShift, "and the stand is free again");

                // The utility layer, last because it changes what the colonist wears. A shield belt
                // covers neither torso nor legs, and utility items are what a stand's default filter
                // leaves out, so the belt stays on through a deposit. A rule asking "is any garment
                // left on?" would be satisfied by it and strip everything that covers the colonist;
                // the question is coverage, as the game's own Pawn_ApparelTracker.PsychologicallyNude
                // asks it.
                if (!WearOne(fix.Pawn, "Apparel_ShieldBelt"))
                {
                    story.Check(false, name + " can put on a shield belt");
                    return;
                }
                settings.filter.SetAllow(shirtDef, allow: true);
                settings.filter.SetAllow(pantsDef, allow: true);
                story.Note(name + " puts on a shield belt, and the shirt and trousers are allowed again");
                List<Apparel> beltWear = new List<Apparel>();
                List<Apparel> beltStore = new List<Apparel>();
                story.Check(!SwapPlan.BuildDress(fix.Pawn, fix.Stand, beltWear, beltStore),
                            "the stand plans no change that would leave " + name + " in just the shield belt");
                story.Check(beltStore.Count == 0, "and leaves no half-made plan behind");
                story.Check(SwapPlan.WouldBeNude(fix.Pawn, fix.Pawn.apparel.WornApparel),
                            "since taking off everything " + name + " wears would leave them naked");
                story.Check(!SwapPlan.WouldBeNude(fix.Pawn, beltWear), "while taking off nothing would not");
            }
        }

        /// <summary>
        /// Fishing sends a colonist to change at a stand ticked for it.
        ///
        /// <para>Odyssey's fishing work giver hands its jobs out from <c>WorkGiver_Fish.NonScanJob</c>
        /// only, and <c>JobGiver_Work</c> stamps <c>workGiverDef</c> on its scanner paths alone, so
        /// a fishing job arrives with no giver, tagged with the giver's own <c>Fieldwork</c>;
        /// <c>JobRoomTargets.NonScanGivers</c> hands it its giver back. Built as Odyssey builds it,
        /// it sends the colonist to change in the stand's room, as the same job stamped with its
        /// giver does. With the tag a player's order carries it does not, since the row keys on the
        /// tag too.</para>
        ///
        /// <para>Fishing water is outdoors on nearly every map, so the set-up that matters is a hut
        /// by the shore whose stand serves a radius: fishing out past the door sends nobody to
        /// change while the stand serves its room, does once the ring reaches it, and not past the
        /// ring. Vanilla's opportunistic haul can put the fishing job back in the queue untagged and
        /// start a haul first; untagged it still reads as fishing, and on shift that haul keeps the
        /// uniform on, where the same haul with nothing queued changes them back.</para>
        /// </summary>
        [Test]
        [TestDescription("a colonist going fishing changes first at a stand ticked for fishing, and at a hut "
                         + "by the shore whose stand's radius takes in the water")]
        internal IEnumerator FishingSendsThemToChange()
        {
            return Staged((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Fishing")),
                          InARoomWithADoorAFisher, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkTypeDef fishing = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Fishing");
                WorkGiverDef fishGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Fish");
                JobDef fish = JobDefOf.Fish;
                if (fishing == null || fishGiver == null || fish == null)
                {
                    story.Check(false, "the game has Odyssey's fishing work, its work giver and its fishing job");
                    return;
                }
                if (fix.Outside.Area == 0)
                {
                    story.Check(false, "the room has a door and open ground outside it");
                    return;
                }
                fix.Comp.ToggleWork(fishing);
                int swept = MakeCalm(fix.Map);
                story.Note("the stand is ticked for fishing, and the map is calm ("
                           + swept + " hostile things cleared)");

                // The shape WorkGiver_Fish.NonScanJob builds: the fishing cell first, the cell to
                // stand on second. Both inside the room to begin with, so the room does not decide it.
                IntVec3 at = fix.Stand.Position;
                IntVec3 spot = at + new IntVec3(3, 0, 3);
                IntVec3 standOn = at + new IntVec3(3, 0, 2);
                Job stamped = JobMaker.MakeJob(fish, spot, standOn);
                stamped.workGiverDef = fishGiver;
                IntVec3 shore = new IntVec3(fix.Outside.minX, 0, at.z);
                IntVec3 farShore = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ);

                story.Check(fix.Comp.HandlesWork(fishing), "the stand serves fishing");
                story.Check(Probe(fix, stamped, JobTag.Fieldwork),
                            "fishing in the room, with the job naming its work giver, sends " + name + " to change");
                story.Check(Probe(fix, JobMaker.MakeJob(fish, spot, standOn), JobTag.Fieldwork),
                            "and so does the same job as Odyssey hands it out, naming none");
                story.Check(!Probe(fix, JobMaker.MakeJob(fish, spot, standOn), JobTag.Misc),
                            "but not with the tag a player's order carries: fishing is known by its work giver's tag");
                story.Check(!Probe(fix, JobMaker.MakeJob(fish, shore, shore), JobTag.Fieldwork),
                            "while the stand serves its room, fishing out past the door sends nobody to change");

                fix.Comp.SetServes(CatchmentKind.Radius);
                fix.Comp.SetServeRadius(5);
                story.Note("the stand is set to serve Within a radius, of 5 cells: a hut by the shore");
                Job forced = JobMaker.MakeJob(fish, shore, shore);
                forced.playerForced = true;
                story.Check(Probe(fix, JobMaker.MakeJob(fish, shore, shore), JobTag.Fieldwork),
                            "now fishing out past the door, inside the ring, sends " + name + " to change");
                story.Check(!Probe(fix, JobMaker.MakeJob(fish, farShore, farShore), JobTag.Fieldwork),
                            "fishing past the ring does not");
                story.Check(Probe(fix, JobMaker.MakeJob(fish, shore, shore)),
                            "the job with no tag, as the game puts it back in the queue behind a haul, still does");
                story.Check(JobRoomTargets.NonScanGiver(forced, null) == null,
                            "while one the player ordered, with no tag, is not read as fishing");

                // On shift, the shape vanilla's opportunistic haul leaves
                // (Pawn_JobTracker.TryOpportunisticJob): the fishing job back at the head of the
                // queue, untagged, and a haul started first.
                story.Check(SwapNow(story, fix), name + " changes into the duster to go fishing");
                story.Check(fix.Comp.OnShift, "and is on shift");
                if (!fix.Comp.OnShift)
                {
                    return;
                }
                NotSentStraightBack(story, fix);
                story.Check(Probe(fix, JobMaker.MakeJob(JobDefOf.HaulToCell, farShore, shore)),
                            "hauling something in from past the ring changes them back first");
                fix.Pawn.jobs.jobQueue.EnqueueFirst(JobMaker.MakeJob(fish, shore, shore));
                story.Note("fishing inside the ring is put back at the head of " + name + "'s queue, untagged, "
                           + "as the game leaves it behind a haul");
                story.Check(!Probe(fix, JobMaker.MakeJob(JobDefOf.HaulToCell, farShore, shore)),
                            "and the same haul keeps the duster on, with that fishing queued behind it");
            }
        }

        /// <summary>
        /// One row of the job table: <paramref name="job"/>, started with <paramref name="tag"/>,
        /// counts as <paramref name="kind"/>, charged to <paramref name="work"/>. A failing line
        /// says what it counted as instead.
        /// </summary>
        internal static bool SortsAs(Story story, Fixture fix, string what, Job job, JobTag? tag, JobKind kind,
                                     WorkTypeDef work)
        {
            JobClass c = Patch_JobInterception.Classify(fix.Pawn, job, tag);
            bool right = c.Kind == kind && c.Work == work;
            return story.Check(right, what + ": " + Counted(kind, work)
                                      + (right ? "" : ", but it counts as " + Counted(c.Kind, c.Work)));
        }

        /// <summary>
        /// What a job counts as, in words, with the work it is charged to as the stand's dialog
        /// names it.
        /// </summary>
        internal static string Counted(JobKind kind, WorkTypeDef work)
        {
            string what;
            switch (kind)
            {
                case JobKind.RidesAlong:
                    what = "a follow-up the outfit stays on for";
                    break;
                case JobKind.Delivery:
                    what = "a delivery";
                    break;
                case JobKind.MealRun:
                    what = "a meal run";
                    break;
                case JobKind.IgnoredGiver:
                    what = "work that dresses nobody";
                    break;
                case JobKind.Work:
                    what = "work";
                    break;
                case JobKind.Rest:
                    what = "sleep";
                    break;
                case JobKind.Recreation:
                    what = "recreation";
                    break;
                default:
                    what = "something else";
                    break;
            }
            return work == null ? what : what + " (" + WorkTypeLabels.Of(work) + ")";
        }

        /// <summary>A work giver by its label, as the work tab lists it, or its defName when it has none.</summary>
        internal static string Giver(WorkGiverDef giver)
        {
            return giver.label.NullOrEmpty() ? giver.defName : "\"" + giver.label + "\"";
        }

        /// <summary>What Shift Change counts <paramref name="job"/> as, for this colonist.</summary>
        internal static JobClass KindOf(Fixture fix, Job job, JobTag? tag = null)
        {
            return Patch_JobInterception.Classify(fix.Pawn, job, tag);
        }

        /// <summary>A job of <paramref name="def"/> stamped with its giver.</summary>
        internal static Job Stamped(JobDef def, WorkGiverDef giver)
        {
            Job job = JobMaker.MakeJob(def);
            job.workGiverDef = giver;
            return job;
        }
    }
}
