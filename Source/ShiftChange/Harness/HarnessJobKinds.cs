// HARNESS only: dev tooling, compiled out of a plain Release build (the
// configuration table is in ShiftChange.csproj). The guard is whole-file,
// always, and check-invariants.py enforces it; why: docs/DESIGN.md,
// "Development tooling".
#if HARNESS
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;
using JobClass = ShiftChange.Patch_JobInterception.JobClass;
using JobKind = ShiftChange.Patch_JobInterception.JobKind;

namespace ShiftChange
{
    /// <summary>
    /// The job classifier (<see cref="Patch_JobInterception.Classify"/>) as a
    /// table: every job shape the other cases build, and the kind and work
    /// type each one gets. The cases that drive a job through the prefix
    /// test what each reader does with the answer; this one tests the answer,
    /// so a change to the order of the classifier's tests fails here by name.
    /// </summary>
    internal static class HarnessJobKinds
    {
        /// <summary>
        /// One row: <paramref name="job"/>, started with <paramref name="tag"/>,
        /// is <paramref name="kind"/> charged to <paramref name="work"/>. A
        /// failing line says what came back.
        /// </summary>
        internal static bool Sorts(Fixture fix, string what, Job job, JobTag? tag, JobKind kind, WorkTypeDef work)
        {
            JobClass c = Patch_JobInterception.Classify(fix.Pawn, job, tag);
            bool right = c.Kind == kind && c.Work == work;
            return Expect(right, what + ": " + Describe(kind, work)
                                 + (right ? "" : ", got " + Describe(c.Kind, c.Work)));
        }

        internal static string Describe(JobKind kind, WorkTypeDef work)
        {
            return work == null ? kind.ToString() : kind + " (" + work.defName + ")";
        }

        internal static JobClass Of(Fixture fix, Job job, JobTag? tag = null)
        {
            return Patch_JobInterception.Classify(fix.Pawn, job, tag);
        }

        internal static Job Stamped(JobDef def, WorkGiverDef giver)
        {
            Job job = JobMaker.MakeJob(def);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>
        /// THE CLASSIFIER SORTS EVERY JOB SHAPE IN THE ARMS' ORDER. Work from
        /// a giver, recreation, reading and eating; ordinary sleep, sleeping on
        /// the ground, and medical lay-downs tagged and untagged for a patient
        /// who is and is not urgent, with the medical-emergency setting off and
        /// on; a delivery and a site job from every delivery giver on the list;
        /// a meal run from every feeding giver; the jobs that ride along, a
        /// direct order and the emergencies; and Odyssey's fishing, stamped
        /// and not. VisitSickPawn is the row the order exists for: Doctor work
        /// that carries a joyKind is Work, and the same job from the joy giver
        /// is Recreation.
        /// </summary>
        internal static bool ClassifierSortsEveryJobShape(Fixture fix)
        {
            if (ShiftChangeMod.Settings == null)
            {
                return Expect(false, "mod settings are loaded");
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
                return Expect(false, "the work types, givers, jobs, bed and meal the table uses resolve");
            }
            // Anything tendable from generation would make the colonist an
            // urgent case before the wound below (UrgentPatientGoesStraightToBed).
            foreach (Hediff existing in fix.Pawn.health.hediffSet.hediffs.ToList())
            {
                if (existing.TendableNow())
                {
                    fix.Pawn.health.RemoveHediff(existing);
                }
            }
            Map map = fix.Map;
            IntVec3 at = fix.Stand.Position;
            Building_Bed bed = DebugTools_Fixtures.Spawn(
                map, bedDef, ThingDefOf.WoodLog, at + new IntVec3(2, 0, 0), Rot4.North) as Building_Bed;
            Thing meal = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), at + new IntVec3(3, 0, 3), map);
            Thing steel = HarnessCatchments.Steel(map, at + new IntVec3(1, 0, 3));
            if (bed == null || meal == null || steel == null)
            {
                return Expect(false, "a bed, a meal and a stack of steel spawn in the stand's room");
            }
            IntVec3 cell = at + new IntVec3(3, 0, 2);

            bool ok = Expect(!HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn)
                             && !Patch_JobInterception.OnABed(fix.Pawn),
                             "the colonist needs no doctor and is not on a bed (precondition)");

            // Work, recreation, and the row the order exists for.
            JobClass visitAsWork = Of(fix, Stamped(visitSick, visitGiver));
            ok &= Sorts(fix, "a tend from its giver", HarnessCatchments.WorkAt(tend, cell), null,
                        JobKind.Work, doctor)
                & Sorts(fix, "VisitSickPawn from the doctor's giver", Stamped(visitSick, visitGiver), null,
                        JobKind.Work, doctor)
                & Expect(visitAsWork.RecreationJob, "and it is still recreation class, so it is placed B-first")
                & Sorts(fix, "VisitSickPawn from the joy giver, with no giver stamped", JobMaker.MakeJob(visitSick),
                        null, JobKind.Recreation, null)
                & Sorts(fix, "a game of chess", JobMaker.MakeJob(chess), null, JobKind.Recreation, null)
                & Sorts(fix, "reading, which carries a joyKind", JobMaker.MakeJob(reading), null, JobKind.Other, null)
                & Expect(!Of(fix, JobMaker.MakeJob(reading)).RecreationJob, "and is not recreation class")
                & Sorts(fix, "eating", IngestJob(meal), null, JobKind.Other, null)
                & Expect(Of(fix, IngestJob(meal)).Ingest, "and it is an ingest job");

            // Sleep and medical rest, the setting off and on.
            bool was = ShiftChangeMod.Settings.medicalEmergenciesChangeFirst;
            Hediff wound = null;
            try
            {
                ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = false;
                JobClass sleep = Of(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed));
                JobClass ground = Of(fix, JobMaker.MakeJob(JobDefOf.LayDown, fix.Pawn.Position));
                ok &= Sorts(fix, "going to bed", JobMaker.MakeJob(JobDefOf.LayDown, bed), null, JobKind.Rest, null)
                    & Expect(sleep.OrdinarySleep && sleep.LyingDown && !sleep.UrgentPatient,
                             "and it is ordinary sleep, lying down, and no urgent patient")
                    & Sorts(fix, "medical bed rest, tagged as the patient givers tag it",
                            JobMaker.MakeJob(JobDefOf.LayDown, bed), JobTag.RestingForMedicalReasons,
                            JobKind.Work, bedRest)
                    & Expect(!Of(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed), JobTag.RestingForMedicalReasons)
                                 .OrdinarySleep,
                             "and it is not ordinary sleep")
                    & Sorts(fix, "sleeping on the ground", JobMaker.MakeJob(JobDefOf.LayDown, fix.Pawn.Position),
                            null, JobKind.Other, null)
                    & Expect(ground.LyingDown && !ground.RestJob, "and it is lying down, but no rest job");

                BodyPartRecord part = fix.Pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault();
                wound = HediffMaker.MakeHediff(HediffDefOf.Cut, fix.Pawn, part);
                wound.Severity = 6f;
                fix.Pawn.health.AddHediff(wound, part);
                JobClass urgent = Of(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed));
                ok &= Expect(HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn) && !fix.Pawn.Downed,
                             "an untended wound makes them an urgent case, still on their feet (precondition)")
                    & Sorts(fix, "an urgent patient's bed rest with the setting off",
                            JobMaker.MakeJob(JobDefOf.LayDown, bed), JobTag.RestingForMedicalReasons,
                            JobKind.Rest, null)
                    & Sorts(fix, "and the same lay-down put back in the queue untagged",
                            JobMaker.MakeJob(JobDefOf.LayDown, bed), null, JobKind.Rest, null)
                    & Expect(urgent.UrgentPatient && !urgent.OrdinarySleep,
                             "which is an urgent patient, and never ordinary sleep");

                ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = true;
                ok &= Sorts(fix, "an urgent patient's bed rest with the setting on",
                            JobMaker.MakeJob(JobDefOf.LayDown, bed), JobTag.RestingForMedicalReasons,
                            JobKind.Work, bedRest)
                    & Sorts(fix, "and untagged", JobMaker.MakeJob(JobDefOf.LayDown, bed), null,
                            JobKind.Work, bedRest)
                    & Expect(!Of(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed)).UrgentPatient,
                             "and no urgent patient, once the setting lets them be dressed");
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
                ok &= Sorts(fix, giver.defName + "'s delivery", HarnessCatchments.DeliveryAt(giver, steel.Position, cell),
                            null, JobKind.Delivery, giver.workType)
                    & Sorts(fix, "and a RemoveFloor it hands out", HarnessCatchments.FloorRemovalAt(giver, cell), null,
                            JobKind.Work, giver.workType);
            }
            WorkGiverDef seeds = DefDatabase<WorkGiverDef>.GetNamedSilentFail(JobRoomTargets.SeedPlanter);
            if (seeds != null)
            {
                ok &= Sorts(fix, "the seed planter's PlantSeed", Stamped(JobDefOf.PlantSeed, seeds), null,
                            JobKind.Delivery, seeds.workType)
                    & Sorts(fix, "and a CutPlant it hands out", Stamped(JobDefOf.CutPlant, seeds), null,
                            JobKind.Work, seeds.workType);
            }

            // Meal runs from every feeding giver, and an ignored giver's job
            // that is not one: the shape of Rimatomics' chemfuel run.
            foreach (string name in HarnessRideAlong.FeedPatientGivers.Concat(HarnessRideAlong.DeliverFoodGivers))
            {
                WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(name);
                if (giver == null)
                {
                    continue;
                }
                JobDef def = HarnessRideAlong.DeliverFoodGivers.Contains(name)
                    ? JobDefOf.DeliverFood
                    : JobDefOf.FeedPatient;
                ok &= Sorts(fix, name + "'s " + def.defName,
                            HarnessRideAlong.FeedingJob(def, meal, fix.Pawn, giver), null,
                            JobKind.MealRun, giver.workType);
            }
            ok &= Sorts(fix, "a feeding giver's haul", HarnessCatchments.Haul(doctorFeed, steel, cell), null,
                        JobKind.IgnoredGiver, doctor);

            // The gates: the jobs that ride along, a direct order, the
            // emergencies, and the giver-less walks.
            Job forced = HarnessCatchments.WorkAt(tend, cell);
            forced.playerForced = true;
            JobClass order = Of(fix, forced);
            ok &= Sorts(fix, "the job tracker's pause", JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1), null,
                        JobKind.RidesAlong, null)
                & Expect(Patch_JobInterception.ExemptInEitherDirection(
                             Of(fix, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1))),
                         "and it is exempt in both directions")
                & Sorts(fix, "an unload with no giver", JobMaker.MakeJob(unload), null, JobKind.RidesAlong, null)
                & Sorts(fix, "a haul from its giver", HarnessCatchments.Haul(hauling, steel, cell), null,
                        JobKind.Work, hauling.workType)
                & Sorts(fix, "a haul with no giver, as vanilla's opportunistic haul starts",
                        HarnessRideAlong.GiverlessHaul(steel, cell), null, JobKind.Other, null)
                & Expect(order.Kind == JobKind.Work && order.PlayerForced
                         && Patch_JobInterception.ExemptInEitherDirection(order),
                         "a direct order is still work, and exempt");

            try
            {
                ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = false;
                ok &= Expect(Of(fix, HarnessCatchments.WorkAt(tendEmergency, cell)).ExemptEmergency
                             && Of(fix, HarnessCatchments.WorkAt(fightFires, cell)).ExemptEmergency,
                             "with the setting off an emergency tend and a fire are exempt")
                    & Expect(!Of(fix, HarnessCatchments.WorkAt(tend, cell)).ExemptEmergency,
                             "and an ordinary tend is not (control)");
                ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = true;
                ok &= Expect(!Of(fix, HarnessCatchments.WorkAt(tendEmergency, cell)).ExemptEmergency,
                             "with it on the emergency tend is not")
                    & Expect(Of(fix, HarnessCatchments.WorkAt(fightFires, cell)).ExemptEmergency,
                             "and the fire still is");
            }
            finally
            {
                ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = was;
            }

            Job walk = JobMaker.MakeJob(JobDefOf.Goto, cell);
            Job walkForWork = JobMaker.MakeJob(JobDefOf.Goto, cell);
            walkForWork.workGiverDef = tend;
            ok &= Sorts(fix, "a walk", walk, null, JobKind.Other, null)
                & Expect(Of(fix, walk).Incidental, "and it is incidental")
                & Expect(!Of(fix, walkForWork).Incidental, "and the same walk from a giver is not");

            // Odyssey's fishing, stamped and not, by tag.
            WorkGiverDef fishGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Fish");
            JobDef fish = JobDefOf.Fish;
            if (fishGiver != null && fish != null)
            {
                Job forcedFish = JobMaker.MakeJob(fish, cell, cell);
                forcedFish.playerForced = true;
                ok &= Sorts(fix, "fishing stamped with its giver", Stamped(fish, fishGiver), JobTag.Fieldwork,
                            JobKind.Work, fishGiver.workType)
                    & Sorts(fix, "fishing as Odyssey hands it out, with no giver", JobMaker.MakeJob(fish, cell, cell),
                            JobTag.Fieldwork, JobKind.Work, fishGiver.workType)
                    & Sorts(fix, "and put back in the queue untagged", JobMaker.MakeJob(fish, cell, cell), null,
                            JobKind.Work, fishGiver.workType)
                    & Sorts(fix, "and with an order's tag", JobMaker.MakeJob(fish, cell, cell), JobTag.Misc,
                            JobKind.Other, null)
                    & Sorts(fix, "and ordered by the player", forcedFish, null, JobKind.Other, null);
            }
            return ok;
        }
    }
}
#endif
