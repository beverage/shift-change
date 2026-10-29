// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
#if HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Cases for what arms a stand (work, recreation, sleep) and for deposit-only, registered and
    /// ordered in <see cref="DebugTools_LifecycleHarness.Run"/>.
    /// </summary>
    internal static class HarnessTriggers
    {
        /// <summary>
        /// The joy-branch classifier and its room table, as a table with no map: a game update,
        /// not an edit, breaks it. <c>IsRecreationJob</c> keys on <c>joyKind</c> plus a
        /// driver-class exclusion. Reading carries a joyKind but picks its spot mid-job (at
        /// StartJob its target is the book), so it is excluded by driver. VisitSickPawn is Doctor
        /// work with joyKind Social, the one vanilla overlap; it stays in class so every
        /// room-resolver site reads it B-first and they cannot disagree.
        /// </summary>
        internal static bool RecreationClassifierHolds()
        {
            JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
            JobDef reading = DefDatabase<JobDef>.GetNamedSilentFail("Reading");
            JobDef visitSick = DefDatabase<JobDef>.GetNamedSilentFail("VisitSickPawn");
            if (chess == null || reading == null || visitSick == null)
            {
                return Expect(false, "the joy job defs resolve");
            }

            bool ok = Expect(chess.joyKind != null, "a plain joy job still carries a joyKind")
                    & Expect(Patch_JobInterception.IsRecreationJob(JobMaker.MakeJob(chess)),
                             "and classifies as recreation")
                    & Expect(!Patch_JobInterception.IsRecreationJob(JobMaker.MakeJob(JobDefOf.Wait)),
                             "a job with no joyKind does not (control)");

            ok &= Expect(reading.joyKind != null,
                         "Reading still carries a joyKind — the exclusion is by driver, not by kind")
                & Expect(!Patch_JobInterception.IsRecreationJob(JobMaker.MakeJob(reading)),
                         "and is excluded anyway, because it picks its spot mid-job");

            ok &= Expect(visitSick.joyKind != null,
                         "VisitSickPawn is still joy-class in vanilla")
                & Expect(Patch_JobInterception.IsRecreationJob(JobMaker.MakeJob(visitSick)),
                         "and stays in class, so every resolver reads it B-first");

            RoomRoleDef recRoom = DefDatabase<RoomRoleDef>.GetNamedSilentFail("RecRoom");
            RoomRoleDef hospital = DefDatabase<RoomRoleDef>.GetNamedSilentFail("Hospital");
            return ok
                & Expect(recRoom != null && RoomWorkTypes.RecreationForRole(recRoom),
                         "a rec room dresses for recreation by default")
                & Expect(hospital == null || !RoomWorkTypes.RecreationForRole(hospital),
                         "a work room does not (control)")
                & Expect(!RoomWorkTypes.RecreationForRole(null),
                         "and neither does an unroled room");
        }

        /// <summary>
        /// Work and recreation are mutually exclusive on one stand, which holds one outfit for one
        /// purpose (docs/DESIGN.md, "The recreation branch"). Asserted both ways because two
        /// separate methods each clear the other's half, and a half-applied rule leaves a stand
        /// claiming both, which the dialog cannot render. Each cleared half is asserted beside the
        /// half just turned on, its control; the case ends with both off, which must read as
        /// excluded.
        /// </summary>
        internal static bool WorkAndRecreationAreExclusive(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (doctor == null)
            {
                return Expect(false, "the Doctor work type resolves");
            }

            fix.Comp.ToggleWork(doctor);
            bool ok = Expect(fix.Comp.HandlesWork(doctor), "work on: the stand serves doctoring")
                    & Expect(!fix.Comp.HandlesRecreation(), "and not recreation");

            fix.Comp.ToggleRecreation();
            ok &= Expect(fix.Comp.HandlesRecreation(), "recreation on: the stand serves joy")
                & Expect(!fix.Comp.HandlesWork(doctor), "and the work half was CLEARED, not stacked")
                & Expect(fix.Comp.WorkTypes.Count == 0, "with no work types left behind");

            fix.Comp.ToggleWork(doctor);
            ok &= Expect(fix.Comp.HandlesWork(doctor), "work again: doctoring is back")
                & Expect(!fix.Comp.HandlesRecreation(), "and recreation was cleared in turn");

            fix.Comp.ToggleRecreation();
            fix.Comp.ToggleRecreation();
            return ok
                & Expect(!fix.Comp.HandlesRecreation(), "recreation off again")
                & Expect(fix.Comp.IsExcluded, "and the stand reads as excluded, not automatic");
        }

        /// <summary>
        /// The joy trigger through the real interception entry point: a recreation job in the
        /// stand's room dresses the pawn, and jobs that must not divert do not. The joy job has
        /// targetB on the pawn's cell because the recreation arm resolves B-first (for sit-and-play
        /// classes B is where the pawn sits, A the venue building). A work job aimed the same way
        /// is the control that the stand's exclusivity, not the geometry, does the work.
        /// </summary>
        internal static bool JoyJobInTheRoomDresses(Fixture fix)
        {
            JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
            JobDef reading = DefDatabase<JobDef>.GetNamedSilentFail("Reading");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            if (chess == null || reading == null)
            {
                return Expect(false, "the joy job defs resolve");
            }

            // The pad's room has no role, so declare the trigger explicitly.
            fix.Comp.ToggleRecreation();
            bool ok = Expect(fix.Comp.HandlesRecreation(), "the stand serves recreation");

            ok &= Expect(Diverts(fix, JoyJob(fix, chess)),
                         "a joy job in this room dresses (positive control)");

            Job forced = JoyJob(fix, chess);
            forced.playerForced = true;
            ok &= Expect(!Diverts(fix, forced), "a player-forced joy job is never diverted");

            ok &= Expect(!Diverts(fix, JoyJob(fix, reading)),
                         "reading is not served, even though it is joy-class");

            if (tend != null)
            {
                ok &= Expect(!Diverts(fix, WorkJob(fix, tend)),
                             "and work is not served by a recreation stand");
            }

            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (doctor != null)
            {
                fix.Comp.ToggleWork(doctor);
                ok &= Expect(!Diverts(fix, JoyJob(fix, chess)),
                             "a work stand ignores the joy job it just served");
            }
            return ok;
        }

        /// <summary>
        /// The sleep trigger through the real interception entry point: going to bed in the stand's
        /// room dresses the pawn, and the lay-downs that must not divert do not. The negatives
        /// carry the case: ordinary sleep and medical bed rest share one driver class and one
        /// JobDef, held apart by two tests nothing else in the mod would notice breaking, a
        /// <c>workGiverDef</c> and a <c>JobTag</c>. Break either and a pyjama stand dresses the
        /// wounded, and the hospital-gown checkbox goes dead without a word. It also covers the
        /// mid-sleep re-trigger (a lay-down re-issued on the bed does not dress), which jobs keep
        /// a pawn in bed, and the wake-up: on shift and on the bed, a job that leaves the room has
        /// the change-back inserted first, while a lay-down does not (control).
        /// </summary>
        internal static bool SleepJobInTheRoomDresses(Fixture fix)
        {
            ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
            JobDef laydown = DefDatabase<JobDef>.GetNamedSilentFail("LayDown");
            JobDef groundSleep = DefDatabase<JobDef>.GetNamedSilentFail("Wait_Asleep");
            WorkGiverDef recuperate =
                DefDatabase<WorkGiverDef>.GetNamedSilentFail("PatientGoToBedRecuperate");
            if (bedDef == null || laydown == null || groundSleep == null)
            {
                return Expect(false, "the bed and lay-down defs resolve");
            }

            Building_Bed bed = DebugTools_Fixtures.Spawn(
                fix.Map, bedDef, ThingDefOf.WoodLog,
                new IntVec3(fix.Stand.Position.x + 2, 0, fix.Stand.Position.z + 2),
                Rot4.North) as Building_Bed;
            if (bed == null)
            {
                return Expect(false, "a bed spawns in the stand's room");
            }

            // Unlike the other cases, do not declare the trigger: spawning a bed changes the room's
            // role. RoomRoleWorker_Bedroom scores a room holding one unowned humanlike bed at
            // 100000, beating every other worker, so the pad is a Bedroom as the bed lands.
            // HandlesRest() is then already true from RestRoles, and ToggleRest() would turn it
            // off. Asserting the automatic default is the stronger claim anyway.
            //
            // An untagged lay-down by an urgent patient is medical bed rest (IsMedicalLayDown),
            // which no sleep stand dresses, so the positive control needs a colonist who needs no
            // doctor; the fixture arranges that (DebugTools_Fixtures.RemoveTendableConditions).
            bool ok = Expect(!HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn),
                             "the colonist needs no doctor to begin with (precondition)")
                    & Expect(fix.Comp.HandlesRest(),
                             "the bed makes this a bedroom, so the stand serves sleep automatically")
                    & Expect(fix.Comp.IsAutomatic,
                             "from the room's role, not an override (that is what is being tested)")
                    & Expect(!fix.Comp.HandlesRecreation(),
                             "and not recreation — the role tables are disjoint");
            if (!fix.Comp.HandlesRest())
            {
                // If a modded room role outscores Bedroom here, declare the trigger instead.
                fix.Comp.ToggleRest();
                ok &= Expect(fix.Comp.HandlesRest(), "declared explicitly as a fallback");
            }

            ok &= Expect(Patch_JobInterception.IsRestJob(JobMaker.MakeJob(laydown, bed)),
                         "a lay-down job targeting a bed classifies as rest")
                & Expect(Diverts(fix, JobMaker.MakeJob(laydown, bed)),
                         "and going to bed in this room dresses (positive control)");

            Job onTheFloor = JobMaker.MakeJob(groundSleep, fix.Pawn.Position);
            ok &= Expect(!Patch_JobInterception.IsRestJob(onTheFloor),
                         "sleeping on the ground is not a rest job")
                & Expect(!Diverts(fix, onTheFloor), "and does not dress");

            ok &= Expect(!Diverts(fix, JobMaker.MakeJob(laydown, bed),
                                  JobTag.RestingForMedicalReasons),
                         "bed rest tagged for medical reasons does not dress");

            if (recuperate != null)
            {
                Job viaWorkGiver = JobMaker.MakeJob(laydown, bed);
                viaWorkGiver.workGiverDef = recuperate;
                ok &= Expect(!Diverts(fix, viaWorkGiver),
                             "and neither does the PatientBedRest work giver's own job");
            }

            Job forced = JobMaker.MakeJob(laydown, bed);
            forced.playerForced = true;
            ok &= Expect(!Diverts(fix, forced), "a player-forced lay-down is never diverted");

            // Mid-sleep re-trigger, the reason OnABed exists: JobInBedUtility.KeepLyingDown
            // re-queues a bare LayDown when an in-bed job ends, and it reaches StartJob after
            // Pawn_JobTracker has nulled curJob. pawn.InBed() needs a current job, so it is false
            // for a colonist lying in their own bed (true only at the wake-up, when a job starts
            // while the lay-down is current). OnABed checks the pawn's cell instead.
            ok &= Expect(!Patch_JobInterception.OnABed(fix.Pawn),
                         "a pawn at the stand does not read as on a bed (control)");
            IntVec3 wasAt = fix.Pawn.Position;
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = bed.Position;
            ok &= Expect(Patch_JobInterception.OnABed(fix.Pawn),
                         "a pawn standing on the bed does")
                & Expect(!Diverts(fix, JobMaker.MakeJob(laydown, bed)),
                         "and a re-issued lay-down does not walk them to the wardrobe");

            // The predicates disagree about this pawn, yet no assertion fails if OnABed is swapped
            // back for InBed(): the fixture reaches "on a bed" by assigning Position, never by
            // running a LayDown driver, so posture stays NotLaying and InBed() is false here for a
            // second reason, and the wake-up block below would still pass. Catching a revert needs
            // a ticked lay-down with a live driver, which these cases avoid because pathfinding is
            // asynchronous. The report prints both answers side by side, as a tripwire for a
            // reader.
            ok &= Expect(!fix.Pawn.InBed(),
                         "and vanilla's own InBed() says the OPPOSITE about the same pawn — "
                         + "that gap is why the arms use OnABed, and why this case cannot "
                         + "catch a revert to InBed() on its own");

            // StaysInBed's discrimination: delete its CanBeginNow half and every assertion above
            // still passes. Both directions from the same on-a-bed position.
            ok &= Expect(Patch_JobInterception.StaysInBed(fix.Pawn, JobMaker.MakeJob(laydown, bed)),
                         "a lay-down job keeps them in bed, so no change-back fires")
                & Expect(!Patch_JobInterception.StaysInBed(fix.Pawn,
                             JobMaker.MakeJob(JobDefOf.Wait, fix.Pawn.Position)),
                         "a job that cannot be done lying down does not — this is the wake-up, "
                         + "and it must change them back while they are still at the stand");

            // Bed-target fallback for in-bed drivers that do not override CanBeginNowWhileLyingDown
            // (Meditate, Ingest), built as JobGiver_MeditateInBed builds one: the pawn's cell in A,
            // the bed in B, so only the B half of TargetsBedUnder can catch it.
            Job inBedByTarget = JobMaker.MakeJob(JobDefOf.Wait, fix.Pawn.Position, bed);
            ok &= Expect(Patch_JobInterception.TargetsBedUnder(fix.Pawn, inBedByTarget),
                         "a job whose target IS this bed counts as staying")
                & Expect(Patch_JobInterception.StaysInBed(fix.Pawn, inBedByTarget),
                         "so StaysInBed catches it even though its driver does not say so");

            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            ok &= Expect(!Patch_JobInterception.OnABed(fix.Pawn),
                         "and the pawn is back off the bed");

            // The wake-up, end to end: the assertions above test predicates, not that the
            // change-back is inserted. This drives TryInsertSwap with the pawn on shift and on the
            // bed, since the failure it guards is a colonist doing the day's first job in
            // sleepwear, then walking back to change. pawn.InBed() reads true at exactly the
            // wake-up boundary, so a guard built on it would suppress the change-back there.
            //
            // Nothing may be queued from the probes above: a job at the head of the queue is one
            // vanilla put back behind a haul, and an errand ahead of the job they dressed for keeps
            // the outfit on (QueuedAheadOfServedWork), which would answer the wake-up below.
            Settle(fix.Pawn);
            if (!RunSwap(fix))
            {
                return ok & Expect(false, "the pawn could dress for the wake-up test");
            }
            ok &= Expect(fix.Comp.OnShift, "on shift, so a return trip exists (control)");

            IntVec3 outside = new IntVec3(fix.Stand.Position.x + 8, 0, fix.Stand.Position.z);
            if (!outside.InBounds(fix.Map) || outside.GetRoom(fix.Map) == fix.Stand.GetRoom())
            {
                return ok & Expect(false, "a cell outside the stand's room resolves for the test");
            }

            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = bed.Position;
            ok &= Expect(Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, outside)),
                         "WAKE-UP: a job that takes them out of the room changes them back FIRST, "
                         + "before they do it")
                & Expect(!Diverts(fix, JobMaker.MakeJob(laydown, bed)),
                         "while a job that keeps them in bed still does not (control)");

            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            return ok;
        }

        /// <summary>
        /// A stand ticked for vanilla's PatientBedRest work type dresses a colonist going to bed to
        /// recuperate, and leaves one who needs a doctor first where they are.
        ///
        /// <para>It guards a shipped bug: the tickbox did nothing while every medical assertion
        /// the harness then had, all negatives, stayed green. The patient givers are NonScanJob
        /// overrides and JobGiver_Work stamps workGiverDef on its scanner paths only, so the job
        /// reaches StartJob naming no work type and the work arm never sees it. Only a positive
        /// assertion tells a dead tickbox from a working one.</para>
        ///
        /// <para>The bed is Medical, as a hospital's is, which keeps the fixture honest:
        /// RoomRoleWorker_Bedroom returns 0 on seeing a medical bed, so the room scores as a
        /// Hospital and the stand does not pick up the sleep trigger. Work and sleep are exclusive
        /// on a stand, so a bedroom fixture would test the wrong arm.</para>
        /// </summary>
        internal static bool MedicalBedRestDressesAtAGownStand(Fixture fix)
        {
            ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
            JobDef laydown = DefDatabase<JobDef>.GetNamedSilentFail("LayDown");
            WorkTypeDef bedRest = DefDatabase<WorkTypeDef>.GetNamedSilentFail("PatientBedRest");
            if (bedDef == null || laydown == null || bedRest == null)
            {
                return Expect(false, "the bed, lay-down and bed-rest defs resolve");
            }

            Building_Bed bed = DebugTools_Fixtures.Spawn(
                fix.Map, bedDef, ThingDefOf.WoodLog,
                new IntVec3(fix.Stand.Position.x + 2, 0, fix.Stand.Position.z + 2),
                Rot4.North) as Building_Bed;
            if (bed == null)
            {
                return Expect(false, "a bed spawns in the stand's room");
            }
            bed.Medical = true;

            // Tick the row the README tells a player to tick, and nothing else. The colonist must
            // need no doctor to begin with: vanilla does not tend the conditions of a pawn
            // generated to join the player, so the fixture removes them.
            fix.Comp.ToggleWork(bedRest);
            bool ok = Expect(!HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn),
                             "the colonist needs no doctor to begin with (precondition)")
                    & Expect(bed.Medical,
                             "the bed takes the medical flag, so the room is a hospital")
                    & Expect(fix.Comp.HandlesWork(bedRest),
                             "and the stand is ticked for bed rest")
                    & Expect(!fix.Comp.HandlesRest(),
                             "without taking the sleep trigger — the gown and the pyjamas "
                             + "stay separate rows");

            ok &= Expect(Diverts(fix, JobMaker.MakeJob(laydown, bed),
                                 JobTag.RestingForMedicalReasons),
                         "so going to bed to recuperate dresses at the stand");

            // The urgent half: vanilla splits patients itself, WorkGiver_PatientGoToBedTreatment
            // gating on ShouldSeekMedicalRestUrgent and Recuperate taking the rest, so reading the
            // same predicate dresses the recovering and leaves the bleeding alone. One pawn either
            // side of one wound, so the wound is the only variable.
            BodyPartRecord part = fix.Pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault();
            Hediff wound = HediffMaker.MakeHediff(HediffDefOf.Cut, fix.Pawn, part);
            wound.Severity = 6f;
            fix.Pawn.health.AddHediff(wound, part);
            ok &= Expect(HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn),
                         "an untended wound makes this pawn an urgent case (precondition)")
                & Expect(!fix.Pawn.Downed,
                         "and does not down them, which would refuse for a different reason")
                & Expect(!Diverts(fix, JobMaker.MakeJob(laydown, bed),
                                  JobTag.RestingForMedicalReasons),
                         "so a patient who needs a doctor now is not sent to a wardrobe first");

            fix.Pawn.health.RemoveHediff(wound);
            ok &= Expect(!HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn),
                         "the wound is gone again")
                & Expect(Diverts(fix, JobMaker.MakeJob(laydown, bed),
                                 JobTag.RestingForMedicalReasons),
                         "and the same pawn dresses once tended — the control that says the "
                         + "refusal above was the wound and not the fixture");

            // Already in bed: vanilla reissues the patient job at a pawn lying in the bed, after a
            // tend most obviously; without the OnABed guard each reissue is a trip to the wardrobe.
            IntVec3 wasAt = fix.Pawn.Position;
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = bed.Position;
            ok &= Expect(Patch_JobInterception.OnABed(fix.Pawn),
                         "a pawn standing on the bed reads as on it (precondition)")
                & Expect(!Diverts(fix, JobMaker.MakeJob(laydown, bed),
                                  JobTag.RestingForMedicalReasons),
                         "and a reissued patient job leaves them where they are");

            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;

            Job forced = JobMaker.MakeJob(laydown, bed);
            forced.playerForced = true;
            return ok
                & Expect(!Diverts(fix, forced, JobTag.RestingForMedicalReasons),
                         "a player-forced trip to a sickbed is never diverted");
        }

        /// <summary>
        /// The medical emergency setting. Off, an emergency is never delayed for a change; on, the
        /// two medical emergencies (the doctor's tend and the critical patient's lay-down) stop
        /// being exempt, and fighting a fire still does not. The firefighting assertion earns the
        /// case: a setting keyed on the emergency flag alone would walk a colonist to a wardrobe
        /// while the base burns (docs/DESIGN.md, "The sleep branch"). The stand is ticked for
        /// Firefighter so that refusal cannot be read as "no stand served that work". The setting
        /// is restored before returning, or every later case would test something nobody chose.
        /// </summary>
        internal static bool MedicalEmergencySettingCoversTheRightWork(Fixture fix)
        {
            if (ShiftChangeMod.Settings == null)
            {
                return Expect(false, "mod settings are loaded");
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
                return Expect(false, "the emergency givers, work types and bed defs resolve");
            }

            bool was = ShiftChangeMod.Settings.medicalEmergenciesChangeFirst;
            bool ok = Expect(!was, "the setting ships off, which is the default under test")
                    & Expect(tendEmergency.emergency,
                             "an emergency tend is still flagged emergency")
                    & Expect(fightFires.emergency,
                             "and so is fighting a fire — which is the whole trap");

            fix.Comp.ToggleWork(doctor);
            fix.Comp.ToggleWork(firefighter);
            ok &= Expect(fix.Comp.HandlesWork(doctor), "the stand serves doctoring")
                & Expect(fix.Comp.HandlesWork(firefighter),
                         "and firefighting, so a refusal below cannot mean 'no stand for it'");

            ok &= Expect(!Diverts(fix, WorkJob(fix, tendEmergency)),
                         "with the setting off an emergency tend is not delayed")
                & Expect(!Diverts(fix, WorkJob(fix, fightFires)),
                         "and neither is fighting a fire");

            ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = true;
            ok &= Expect(Diverts(fix, WorkJob(fix, tendEmergency)),
                         "with it on the doctor changes into scrubs first")
                & Expect(!Diverts(fix, WorkJob(fix, fightFires)),
                         "and the fire is STILL not delayed — the switch is medical, "
                         + "not emergency");

            // The patient arm: the critical patient's job carries no giver to flag, so the setting
            // relaxes the urgency refusal in MedicalRestWorkType instead.
            Building_Bed bed = DebugTools_Fixtures.Spawn(
                fix.Map, bedDef, ThingDefOf.WoodLog,
                new IntVec3(fix.Stand.Position.x + 2, 0, fix.Stand.Position.z + 2),
                Rot4.North) as Building_Bed;
            if (bed == null)
            {
                ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = was;
                return ok & Expect(false, "a bed spawns in the stand's room");
            }
            bed.Medical = true;
            fix.Comp.ToggleWork(bedRest);

            BodyPartRecord part = fix.Pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault();
            Hediff wound = HediffMaker.MakeHediff(HediffDefOf.Cut, fix.Pawn, part);
            wound.Severity = 6f;
            fix.Pawn.health.AddHediff(wound, part);
            ok &= Expect(HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn),
                         "the pawn is an urgent case again (precondition)")
                & Expect(fix.Comp.HandlesWork(bedRest), "and the stand is ticked for bed rest")
                & Expect(Diverts(fix, JobMaker.MakeJob(laydown, bed),
                                 JobTag.RestingForMedicalReasons),
                         "so with the setting on a critical patient stops for the gown");

            ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = false;
            ok &= Expect(!Diverts(fix, JobMaker.MakeJob(laydown, bed),
                                  JobTag.RestingForMedicalReasons),
                         "and with it off the same patient does not — one switch, both arms");

            fix.Pawn.health.RemoveHediff(wound);
            ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = was;
            return ok & Expect(!ShiftChangeMod.Settings.medicalEmergenciesChangeFirst,
                               "the setting was restored for the cases that follow");
        }

        /// <summary>
        /// The new-stand setting decides what a stand made from then on starts as, through
        /// <c>ThingMaker.MakeThing</c>, the call a finished frame makes. A stand made with it off
        /// spawns as vanilla furniture; the fixture's stand, made before the flip, stays in
        /// service (control). That a load never applies it is asserted in
        /// <see cref="DebugTools_SaveRoundTrip.RoundTrip"/>.
        /// </summary>
        internal static bool NewStandSettingDecidesOnlyNewStands(Fixture fix)
        {
            bool was = ShiftChangeMod.Settings.newStandsInService;
            bool ok = Expect(was, "the setting ships on, which is the default under test");
            try
            {
                CompShiftStand on = (ThingMaker.MakeThing(fix.Stand.def, ThingDefOf.WoodLog)
                    as ThingWithComps)?.TryGetComp<CompShiftStand>();
                ok &= Expect(on != null && on.IsAutomatic, "with it on a new stand starts automatic");

                ShiftChangeMod.Settings.newStandsInService = false;
                Building_OutfitStand stand =
                    ThingMaker.MakeThing(fix.Stand.def, ThingDefOf.WoodLog) as Building_OutfitStand;
                CompShiftStand off = stand?.TryGetComp<CompShiftStand>();
                if (off == null || Patch_AllowRemovingToggle.AllowRemovingItemsRef == null)
                {
                    return ok & Expect(false, "a stand and its removal flag could be made with it off");
                }
                ok &= Expect(off.IsExcluded, "with it off a new stand starts as not used for shift changes");

                // Spawned with the removal flag on: a stand in service would force it off here.
                stand.SetFactionDirect(Faction.OfPlayer);
                Patch_AllowRemovingToggle.AllowRemovingItemsRef(stand) = true;
                GenSpawn.Spawn(stand, fix.Stand.Position + new IntVec3(2, 0, 0), fix.Map, Rot4.North);
                ok &= Expect(off.IsExcluded, "and spawning it keeps it so")
                    & Expect(Patch_AllowRemovingToggle.AllowRemovingItemsRef(stand),
                             "its Allow removing items stays as set, as on a vanilla stand")
                    & Expect(!off.BlocksTrade, "and its contents can be traded")
                    & Expect(!fix.Comp.IsExcluded, "while the stand made before the flip is still in service");
            }
            finally
            {
                ShiftChangeMod.Settings.newStandsInService = was;
            }
            return ok & Expect(ShiftChangeMod.Settings.newStandsInService,
                               "the setting was restored for the cases that follow");
        }

        /// <summary>
        /// Deposit only: a colonist parks what the stand's filter accepts, keeps the rest of their
        /// clothes on, and gets it all back on the return trip. The stand must be claimed at all:
        /// <c>OnShift</c> counts a stored garment as well as an issued one, or the parka sits in a
        /// rack with no borrower, registry entry or return trip, gone from the player's side. And a
        /// filter wide enough to take everything they wear must make the stand decline, not send a
        /// colonist to bed naked.
        /// </summary>
        internal static bool DepositOnlyParksAndReturns(Fixture fix)
        {
            ThingDef parkaDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Parka");
            ThingDef shirtDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_BasicShirt");
            ThingDef pantsDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Pants");
            if (parkaDef == null || shirtDef == null || pantsDef == null)
            {
                return Expect(false, "the fixture's apparel defs resolve");
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
                return Expect(false, "fixture is wearing a parka to park");
            }

            fix.Comp.ToggleRest();
            fix.Comp.SetDepositOnly(true);
            bool ok = Expect(fix.Comp.DepositOnly, "the stand is deposit-only");

            // The filter is the control surface and its default is permissive: OutfitStandBase
            // ships defaultStorageSettings (Apparel minus ApparelUtility and Weapons) and PostMake
            // copies it onto every stand. That default is what makes WouldBeNude load-bearing.
            StorageSettings settings = fix.Stand.GetStoreSettings();
            ok &= Expect(settings.filter.Allows(parkaDef),
                         "a freshly built stand's filter ACCEPTS ordinary apparel out of the box");

            settings.filter.SetDisallowAll();
            ok &= Expect(!SwapPlan.WouldDress(fix.Pawn, fix.Stand),
                         "and with the filter emptied by hand, the stand is not selected");

            settings.filter.SetAllow(parkaDef, allow: true);
            List<Apparel> wear = new List<Apparel>();
            List<Apparel> store = new List<Apparel>();
            ok &= Expect(SwapPlan.BuildDress(fix.Pawn, fix.Stand, wear, store),
                         "with the parka allowed, there is a plan")
                & Expect(wear.Count == 0, "and it issues nothing")
                & Expect(store.Count == 1 && store.Contains(parka),
                         "parking exactly what the filter accepts, and nothing else");

            // Never bare: widen the filter to everything they have on and the stand must decline.
            settings.filter.SetAllow(shirtDef, allow: true);
            settings.filter.SetAllow(pantsDef, allow: true);
            ok &= Expect(!SwapPlan.WouldDress(fix.Pawn, fix.Stand),
                         "a filter that would take everything makes the stand decline");
            settings.filter.SetAllow(shirtDef, allow: false);
            settings.filter.SetAllow(pantsDef, allow: false);

            ok &= Expect(RunSwap(fix), "the deposit leg ran to completion")
                & Expect(parka.ParentHolder == fix.Stand, "the parka is in the stand")
                & Expect(fix.Pawn.apparel.WornApparel.Contains(parka) == false,
                         "and off the pawn")
                & Expect(fix.Pawn.apparel.WornApparel.Count == 2,
                         "while their own clothes stayed on")
                & Expect(fix.Comp.OnShift, "the stand reads as in use")
                & Expect(fix.Comp.Borrower == fix.Pawn, "with the borrower recorded")
                & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                         "and the registry pointing at it, so a return trip exists")
                & Expect(fix.Comp.IssuedUniformForReading.Count == 0,
                         "nothing was issued, which is the point");

            ok &= Expect(RunSwap(fix), "the return leg ran to completion")
                & Expect(fix.Pawn.apparel.WornApparel.Contains(parka), "the parka is back on")
                & Expect(!fix.Comp.OnShift, "and the stand is free again");

            // The utility-layer trap, last because it changes what the pawn wears. A shield belt
            // covers neither Torso nor Legs, and ApparelUtility is what a stand's default filter
            // excludes, so the belt survives a deposit. A guard asking "is any garment left on?"
            // would be satisfied by it and license stripping everything that covers the colonist;
            // the test is coverage, as vanilla's Pawn_ApparelTracker.PsychologicallyNude is.
            if (!WearOne(fix.Pawn, "Apparel_ShieldBelt"))
            {
                return ok & Expect(false, "a shield belt can be worn for the utility-layer case");
            }
            settings.filter.SetAllow(shirtDef, allow: true);
            settings.filter.SetAllow(pantsDef, allow: true);
            List<Apparel> beltWear = new List<Apparel>();
            List<Apparel> beltStore = new List<Apparel>();
            return ok
                & Expect(!SwapPlan.BuildDress(fix.Pawn, fix.Stand, beltWear, beltStore),
                         "a deposit that would leave only a shield belt is refused")
                & Expect(beltStore.Count == 0, "and leaves no half-built plan behind")
                & Expect(SwapPlan.WouldBeNude(fix.Pawn, fix.Pawn.apparel.WornApparel),
                         "because stripping to nothing reads as nude (positive control)")
                & Expect(!SwapPlan.WouldBeNude(fix.Pawn, beltWear),
                         "while keeping everything on does not (negative control)");
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
        internal static bool DepositOnlyStandIsNoHaulTarget(Fixture fix)
        {
            Map map = fix.Map;
            ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
            ThingDef dusterDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Duster");
            if (standDef == null || dusterDef == null)
            {
                return Expect(false, "the fixture's stand and apparel defs resolve");
            }

            // Stage stocks a duster, and a stand holding one refuses the duster below by vanilla's
            // own conflict gate. Empty is also deposit-only's resting state.
            ClearStand(fix.Stand);
            IntVec3 origin = fix.Stand.Position;

            Building_OutfitStand other = DebugTools_Fixtures.Spawn(
                map, standDef, ThingDefOf.WoodLog,
                origin + new IntVec3(3, 0, 0), Rot4.North) as Building_OutfitStand;
            if (other == null)
            {
                return Expect(false, "a second, ordinary stand could be staged");
            }
            ClearStand(other);

            // Next to the fixture stand and two cells from the other, so the search's distance
            // tiebreak has one answer and the switch below shows.
            Apparel garment = DebugTools_Fixtures.MakeGarment(dusterDef, null);
            GenSpawn.Spawn(garment, origin + new IntVec3(1, 0, 0), map);

            IHaulDestination found;
            bool ok = Expect(
                StoreUtility.TryFindBestBetterNonSlotGroupStorageFor(
                    garment, null, map, StoragePriority.Normal, Faction.OfPlayer, out found)
                && found == fix.Stand,
                "an empty stand outbids a Normal stockpile, and the nearest one is chosen "
                + "(positive control: this is the vanilla behaviour being narrowed)");

            fix.Comp.ToggleRest();
            fix.Comp.SetDepositOnly(true);
            ok &= Expect(fix.Comp.DepositOnly, "the stand is deposit-only");

            ok &= Expect(
                StoreUtility.TryFindBestBetterNonSlotGroupStorageFor(
                    garment, null, map, StoragePriority.Normal, Faction.OfPlayer, out found)
                && found == other,
                "and the hauler now walks past it to the ordinary stand instead");

            // The narrowing is the destination flag alone: if this fails, a stand's own deposit and
            // a player's ordered delivery went with it.
            ok &= Expect(((IHaulDestination)fix.Stand).Accepts(garment),
                         "while the stand still ACCEPTS the garment, so a deposit and an "
                         + "ordered delivery are untouched");

            if (!StockOne(other, "Apparel_Duster"))
            {
                return ok & Expect(false, "the ordinary stand can be stocked for the immunity case");
            }
            ok &= Expect(!((IHaulDestination)other).Accepts(garment),
                         "a stand already holding a conflicting garment refuses it "
                         + "(HasRoomForApparelOfDef — vanilla's gate, not ours)");
            bool anywhere = StoreUtility.TryFindBestBetterNonSlotGroupStorageFor(
                garment, null, map, StoragePriority.Normal, Faction.OfPlayer, out found);
            ok &= Expect(!anywhere || (found != fix.Stand && found != other),
                         "so with one stand deposit-only and the other full, neither takes it");

            fix.Comp.SetDepositOnly(false);
            return ok
                & Expect(StoreUtility.TryFindBestBetterNonSlotGroupStorageFor(
                             garment, null, map, StoragePriority.Normal, Faction.OfPlayer, out found)
                         && found == fix.Stand,
                         "and dropping the mode hands the stand straight back, with nothing to invalidate");
        }

        /// <summary>
        /// The sleep-branch classifier and its room table, as a table with no map, like
        /// <see cref="RecreationClassifierHolds"/>: every assertion is a def-database fact, so a
        /// game update, not an edit, breaks it. The bed-shaped half is in
        /// <see cref="SleepJobInTheRoomDresses"/>.
        /// </summary>
        internal static bool RestClassifierHolds()
        {
            JobDef laydown = DefDatabase<JobDef>.GetNamedSilentFail("LayDown");
            JobDef groundSleep = DefDatabase<JobDef>.GetNamedSilentFail("Wait_Asleep");
            WorkTypeDef bedRest = DefDatabase<WorkTypeDef>.GetNamedSilentFail("PatientBedRest");
            WorkGiverDef recuperate =
                DefDatabase<WorkGiverDef>.GetNamedSilentFail("PatientGoToBedRecuperate");
            if (laydown == null || groundSleep == null)
            {
                return Expect(false, "the lay-down job defs resolve");
            }

            bool ok = Expect(laydown.driverClass != null
                             && typeof(JobDriver_LayDown).IsAssignableFrom(laydown.driverClass),
                             "LayDown still runs on JobDriver_LayDown")
                    & Expect(groundSleep.driverClass != null
                             && typeof(JobDriver_LayDown).IsAssignableFrom(groundSleep.driverClass),
                             "so does ground sleep, which is why the classifier demands a bed")
                    & Expect(!Patch_JobInterception.IsRestJob(JobMaker.MakeJob(laydown)),
                             "and a lay-down job with no bed does not classify as rest");

            // The medical half: def-database facts and nothing more, all true while the tickbox
            // does nothing. PatientBedRest is the work type MedicalRestWorkType charges a tagged
            // lay-down to; delivery is asserted in MedicalBedRestDressesAtAGownStand.
            ok &= Expect(bedRest != null, "vanilla still has a PatientBedRest work type")
                & Expect(bedRest == null || bedRest.visible,
                         "still visible, so it still appears in the stand's own grid")
                & Expect(recuperate == null || recuperate.workType == bedRest,
                         "and its work giver still names it, so charging medical rest to it "
                         + "still matches what the work tab shows a player");

            RoomRoleDef bedroom = DefDatabase<RoomRoleDef>.GetNamedSilentFail("Bedroom");
            RoomRoleDef recRoom = DefDatabase<RoomRoleDef>.GetNamedSilentFail("RecRoom");
            return ok
                & Expect(bedroom != null && RoomWorkTypes.RestForRole(bedroom),
                         "a bedroom dresses for sleep by default")
                & Expect(recRoom == null || !RoomWorkTypes.RestForRole(recRoom),
                         "a rec room does not — the three role tables stay disjoint")
                & Expect(!RoomWorkTypes.RestForRole(null),
                         "and neither does an unroled room");
        }

        /// <summary>
        /// Fishing dresses at a stand ticked for it.
        ///
        /// <para>Odyssey's Fish giver hands its jobs out from <c>WorkGiver_Fish.NonScanJob</c>
        /// only, and <c>JobGiver_Work</c> stamps <c>workGiverDef</c> on its scanner paths alone, so
        /// a fishing job arrives with no giver, tagged with the giver's own <c>Fieldwork</c>;
        /// <c>JobRoomTargets.NonScanGivers</c> hands it its giver back. Built as Odyssey builds it,
        /// it dresses in the stand's room, as the same job stamped with its giver does (control).
        /// With the tag a player's order carries it does not, since the row keys on the tag
        /// too.</para>
        ///
        /// <para>Fishing water is outdoors on nearly every map, so the setup that matters is a hut
        /// by the shore serving a radius: fishing out past the door does not dress in room mode
        /// (control), dresses once the ring reaches it, and not past the ring. Vanilla's
        /// opportunistic haul can put the fishing job back in the queue untagged and start a haul
        /// first; untagged it still reads as fishing, and on shift that haul keeps the uniform on,
        /// where the same haul with nothing queued changes them back.</para>
        /// </summary>
        internal static bool FishingDressesAtAStandTickedForIt(Fixture fix)
        {
            WorkTypeDef fishing = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Fishing");
            WorkGiverDef fishGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Fish");
            JobDef fish = JobDefOf.Fish;
            if (fishing == null || fishGiver == null || fish == null)
            {
                return Expect(false, "Odyssey's fishing work type, giver and job resolve");
            }
            if (fix.Outside.Area == 0)
            {
                return Expect(false, "the fixture has a door and open ground outside it");
            }
            fix.Comp.ToggleWork(fishing);
            MakeCalm(fix.Map);

            // The shape WorkGiver_Fish.NonScanJob builds: fishing cell in A, cell to stand on in B.
            // First both inside the room, so the room does not decide it.
            IntVec3 at = fix.Stand.Position;
            IntVec3 spot = at + new IntVec3(3, 0, 3);
            IntVec3 standOn = at + new IntVec3(3, 0, 2);
            Job stamped = JobMaker.MakeJob(fish, spot, standOn);
            stamped.workGiverDef = fishGiver;
            IntVec3 shore = new IntVec3(fix.Outside.minX, 0, at.z);
            IntVec3 farShore = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ);

            bool ok = Expect(fix.Comp.HandlesWork(fishing), "the stand is ticked for fishing")
                    & Expect(Probe(fix, stamped, JobTag.Fieldwork),
                             "a fishing job carrying its giver dresses (control)")
                    & Expect(Probe(fix, JobMaker.MakeJob(fish, spot, standOn), JobTag.Fieldwork),
                             "a fishing job as Odyssey hands it out, with no giver, dresses")
                    & Expect(!Probe(fix, JobMaker.MakeJob(fish, spot, standOn), JobTag.Misc),
                             "the same job with an order's tag does not: the row keys on the giver's own tag")
                    & Expect(!Probe(fix, JobMaker.MakeJob(fish, shore, shore), JobTag.Fieldwork),
                             "room mode: fishing out past the door does not dress (control)");

            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(5);
            Job forced = JobMaker.MakeJob(fish, shore, shore);
            forced.playerForced = true;
            ok &= Expect(Probe(fix, JobMaker.MakeJob(fish, shore, shore), JobTag.Fieldwork),
                         "radius 5: fishing out past the door, inside the ring, dresses: the hut by the shore")
                & Expect(!Probe(fix, JobMaker.MakeJob(fish, farShore, farShore), JobTag.Fieldwork),
                         "fishing past the ring does not")
                & Expect(Probe(fix, JobMaker.MakeJob(fish, shore, shore)),
                         "the job with no tag, as vanilla puts it back in the queue behind a haul, still dresses")
                & Expect(JobRoomTargets.NonScanGiver(forced, null) == null,
                         "while a player-forced one with no tag is not read as fishing");

            // On shift, the shape vanilla's opportunistic haul leaves: the fishing job back at the
            // head of the queue, untagged, and a haul started first
            // (Pawn_JobTracker.TryOpportunisticJob).
            ok &= Expect(RunSwap(fix), "dressed for fishing")
                & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= HarnessCatchments.NotSentStraightBack(fix);
            ok &= Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.HaulToCell, farShore, shore)),
                         "a haul from past the ring changes them back (control)");
            fix.Pawn.jobs.jobQueue.EnqueueFirst(JobMaker.MakeJob(fish, shore, shore));
            return ok & Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.HaulToCell, farShore, shore)),
                               "the same haul keeps the uniform on with fishing inside the ring queued behind it, untagged");
        }
    }
}
#endif
