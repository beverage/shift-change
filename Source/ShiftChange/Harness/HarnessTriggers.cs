// HARNESS only — see the configuration table in ShiftChange.csproj. The
// harness is dev tooling and does not ship: a Release build compiles this
// file out entirely, and devtools/run-harness.sh asks for it back with
// -p:Harness=true on top of Release codegen.
//
// The guard is whole-file, always. Never put an #if HARNESS inside a file
// that ships — a shipping build and a harness build must differ by the
// presence of these types and by nothing else, or a harness run stops saying
// anything about the assembly that goes out. check-invariants.py enforces it.
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
    /// Harness cases for what arms a stand — work, recreation and sleep —
    /// including the two job classifiers and the deposit-only behaviour.
    ///
    /// <para>Split out of <see cref="DebugTools_LifecycleHarness"/> on
    /// 2026-09-15. These are separate TYPES rather than partials on purpose: a
    /// decompiler merges partials back into one class, so partials would have
    /// left the shipped dll reading exactly as it did before. The registration
    /// list that decides case ORDER stays in
    /// <see cref="DebugTools_LifecycleHarness.Run"/> and must not be
    /// scattered.</para>
    /// </summary>
    internal static class HarnessTriggers
    {
        /// <summary>
        /// The joy-branch classifier, as a table.
        ///
        /// <para><see cref="Patch_JobInterception.IsRecreationJob"/> decides
        /// which arm a job reaches, and it keys on <c>joyKind</c> plus one
        /// driver-class exclusion. Both halves have a case here because both
        /// were found by review rather than by design: Reading carries a
        /// joyKind but picks its spot MID-JOB, so at StartJob its target is
        /// whatever shelf the book sits on; and VisitSickPawn is Doctor work
        /// that carries joyKind Social, the one vanilla overlap, deliberately
        /// left IN class so every room-resolver site reads it B-first and
        /// they cannot disagree.</para>
        ///
        /// <para>No map: this is a static fact about the def database, and it
        /// is most likely to break on a game update rather than on an edit.</para>
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

            // The room half of the same table.
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

            // Turning recreation off leaves nothing selected, which IS the
            // excluded state — a stand that serves neither must say so rather
            // than silently falling back to the room.
            fix.Comp.ToggleRecreation();
            fix.Comp.ToggleRecreation();
            return ok
                & Expect(!fix.Comp.HandlesRecreation(), "recreation off again")
                & Expect(fix.Comp.IsExcluded, "and the stand reads as excluded, not automatic");
        }

        /// <summary>
        /// The joy trigger, driven through the real interception entry point:
        /// a recreation job done in the stand's room dresses the pawn, and the
        /// jobs that must not divert do not.
        ///
        /// <para>The joy job is built with targetB on the pawn's own cell,
        /// because the recreation arm resolves B-FIRST — for the sit-and-play
        /// classes B is where the pawn sits while joy ticks, and A is the
        /// venue building. A work job targeted the same way is the control
        /// that shows the stand's exclusivity is doing the work, not the
        /// geometry.</para>
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

            // The pad's room has no role, so declare the trigger explicitly —
            // the same thing PromisesHold does for doctoring.
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

            // Flip the stand back to work and the same joy job must stop
            // diverting — the trigger, not the room, is what decides.
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
        /// The SLEEP trigger, driven through the real interception entry
        /// point: going to bed in the stand's room dresses the pawn, and the
        /// lay-down jobs that must NOT divert do not.
        ///
        /// <para>The negatives carry this case. Ordinary sleep and MEDICAL bed
        /// rest share one driver class and one JobDef, and they are held apart
        /// by two tests nothing else in the mod would notice breaking — a
        /// <c>workGiverDef</c> and a <c>JobTag</c>. Break either and vanilla's
        /// PatientBedRest work type quietly stops owning its own jobs: a
        /// pyjama stand starts dressing the wounded, and the checkbox a player
        /// ticked for hospital gowns goes dead with nothing to say so.</para>
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

            // DO NOT DECLARE THE TRIGGER HERE. Every other case in this file
            // calls a Toggle to opt the pad's roleless room in, and copying
            // that idiom would have inverted this case: a bed is not a chess
            // table, and spawning one CHANGES THE ROOM'S ROLE.
            // RoomRoleWorker_Bedroom scores a room holding a single unowned
            // humanlike bed at 100000, which beats every other worker, so the
            // pad is a Bedroom the moment the bed lands — HandlesRest() is
            // already true from RestRoles, and ToggleRest() would turn it OFF
            // and fall through to SetExcluded(). Assert the automatic default
            // instead; it is the stronger claim anyway, since it exercises the
            // RestRoles table rather than bypassing it.
            bool ok = Expect(fix.Comp.HandlesRest(),
                             "the bed makes this a bedroom, so the stand serves sleep automatically")
                    & Expect(fix.Comp.IsAutomatic,
                             "from the room's role, not an override (that is what is being tested)")
                    & Expect(!fix.Comp.HandlesRecreation(),
                             "and not recreation — the role tables are disjoint");
            if (!fix.Comp.HandlesRest())
            {
                // Belt and braces: if a modded room-role worker outscores
                // Bedroom on this pad, declare the trigger so the rest of the
                // case still tests what it is here to test.
                fix.Comp.ToggleRest();
                ok &= Expect(fix.Comp.HandlesRest(), "declared explicitly as a fallback");
            }

            ok &= Expect(Patch_JobInterception.IsRestJob(JobMaker.MakeJob(laydown, bed)),
                         "a lay-down job targeting a bed classifies as rest")
                & Expect(Diverts(fix, JobMaker.MakeJob(laydown, bed)),
                         "and going to bed in this room dresses (positive control)");

            // GROUND SLEEP: the same driver class, no bed. This is the whole
            // reason the classifier tests the TARGET and not the driver alone.
            Job onTheFloor = JobMaker.MakeJob(groundSleep, fix.Pawn.Position);
            ok &= Expect(!Patch_JobInterception.IsRestJob(onTheFloor),
                         "sleeping on the ground is not a rest job")
                & Expect(!Diverts(fix, onTheFloor), "and does not dress");

            // MEDICAL BED REST, both routes into it. Neither may reach the
            // sleep arm: the PatientBedRest work type owns them.
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

            // A player-forced trip to bed is an order, in this arm as in every
            // other one.
            Job forced = JobMaker.MakeJob(laydown, bed);
            forced.playerForced = true;
            ok &= Expect(!Diverts(fix, forced), "a player-forced lay-down is never diverted");

            // THE MID-SLEEP RE-TRIGGER, and the reason OnABed exists.
            // JobInBedUtility.KeepLyingDown re-queues a bare LayDown whenever
            // an in-bed job ends, and at that boundary Pawn_JobTracker has
            // already nulled curJob — so pawn.InBed(), which requires CurJob,
            // answers FALSE for a colonist lying in their own bed and the guard
            // written to protect sleepers protected nobody. Position is what
            // answers correctly.
            ok &= Expect(!Patch_JobInterception.OnABed(fix.Pawn),
                         "a pawn at the stand does not read as on a bed (control)");
            IntVec3 wasAt = fix.Pawn.Position;
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = bed.Position;
            ok &= Expect(Patch_JobInterception.OnABed(fix.Pawn),
                         "a pawn standing on the bed does")
                & Expect(!Diverts(fix, JobMaker.MakeJob(laydown, bed)),
                         "and a re-issued lay-down does not walk them to the wardrobe");

            // THE DIVERGENCE, PINNED — and read the caveat before trusting it.
            //
            // OnABed and RestUtility.InBed() disagree about this pawn, and that
            // disagreement is the entire reason OnABed exists: InBed() is
            // CurrentBed() != null, CurrentBed() bails on CurJob == null, and
            // Pawn_JobTracker nulls curJob before TryFindAndStartJob — so at a
            // job boundary InBed() answers false for a colonist lying in their
            // own bed, and the guards written to protect sleepers protected
            // nobody.
            //
            // WHAT THIS ASSERTION CANNOT DO: fail when someone swaps OnABed
            // back for InBed(). The fixture reaches "on a bed" by assigning
            // Position, never by running a LayDown driver, so posture stays
            // NotLaying and InBed() is false here for a second reason as well.
            // A reverted return trip would therefore not fire its guard in the
            // harness either, and the WAKE-UP case below would still pass.
            // Catching that needs a ticked lay-down job with a live driver,
            // which is the machinery these cases avoid on purpose (1.6
            // pathfinding is async — see the fixture notes).
            //
            // So this is a tripwire for a READER, not for CI. It prints the two
            // answers side by side in the report so the next person to touch
            // this arm sees which predicate is load-bearing and why, instead of
            // finding it only in a comment they had no reason to open.
            ok &= Expect(!fix.Pawn.InBed(),
                         "and vanilla's own InBed() says the OPPOSITE about the same pawn — "
                         + "that gap is why the arms use OnABed, and why this case cannot "
                         + "catch a revert to InBed() on its own");

            // STAYSINBED'S DISCRIMINATION, which the return trip depends on and
            // which nothing else here covers — delete the CanBeginNow half and
            // every assertion above still passes. Both directions, from the same
            // on-a-bed position, so the only variable is the incoming job.
            ok &= Expect(Patch_JobInterception.StaysInBed(fix.Pawn, JobMaker.MakeJob(laydown, bed)),
                         "a lay-down job keeps them in bed, so no change-back fires")
                & Expect(!Patch_JobInterception.StaysInBed(fix.Pawn,
                             JobMaker.MakeJob(JobDefOf.Wait, fix.Pawn.Position)),
                         "a job that cannot be done lying down does not — this is the wake-up, "
                         + "and it must change them back while they are still at the stand");

            // The bed-target fallback, for the drivers that run in bed but do
            // not override CanBeginNowWhileLyingDown (Meditate, Ingest). Built
            // the way JobGiver_MeditateInBed builds one: pawn cell in A, bed in B.
            Job inBedByTarget = JobMaker.MakeJob(JobDefOf.Wait, fix.Pawn.Position, bed);
            ok &= Expect(Patch_JobInterception.TargetsBedUnder(fix.Pawn, inBedByTarget),
                         "a job whose target IS this bed counts as staying")
                & Expect(Patch_JobInterception.StaysInBed(fix.Pawn, inBedByTarget),
                         "so StaysInBed catches it even though its driver does not say so");

            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            ok &= Expect(!Patch_JobInterception.OnABed(fix.Pawn),
                         "and the pawn is back off the bed");

            // THE WAKE-UP, END TO END. Everything above tests PREDICATES, and a
            // predicate returning false is not the same claim as "the
            // change-back was actually inserted". This drives TryInsertSwap
            // itself with the pawn genuinely on shift and genuinely on the bed,
            // because the failure it guards is the one a player sees and
            // reports: a colonist doing the first job of the day in sleepwear,
            // then walking back across the base to change. That was a real
            // defect in the first version of this arm — pawn.InBed() reads TRUE
            // at exactly the wake-up boundary, so the guard meant to protect
            // sleepers suppressed the change-back at the one moment the pawn
            // was standing beside their own stand.
            if (!RunSwap(fix))
            {
                return ok & Expect(false, "the pawn could dress for the wake-up test");
            }
            ok &= Expect(fix.Comp.OnShift, "on shift, so a return trip exists (control)");

            // Outside the enclosed pad, so the job resolves to a DIFFERENT room
            // — which is what "this job takes them out of the bedroom" means to
            // the return trip.
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
        /// THE HOSPITAL GOWN: a stand ticked for vanilla's PatientBedRest work
        /// type dresses a colonist going to bed to recuperate, and leaves one
        /// who needs a doctor first exactly where they are.
        ///
        /// <para><b>This case exists because its absence shipped a dead
        /// feature.</b> Every medical assertion the harness had was a negative
        /// — medical rest must not reach the sleep arm — and
        /// <see cref="RestClassifierHolds"/> added that PatientBedRest is still
        /// a work type whose giver still names it. All of that was true the
        /// whole time the tickbox did nothing: the patient givers are
        /// NonScanJob overrides, JobGiver_Work stamps workGiverDef on its
        /// scanner paths only, so the job reached StartJob naming no work type
        /// and the work arm never saw it. A forwarding address is not a
        /// delivery, and only a positive can tell the two apart. Reported by a
        /// player on the Workshop, 2026-09-23.</para>
        ///
        /// <para>The bed is set MEDICAL, which is what a hospital is made of
        /// and also what keeps the fixture honest:
        /// <c>RoomRoleWorker_Bedroom</c> returns 0 the moment it sees a medical
        /// bed, so the room scores as a Hospital and the stand does not quietly
        /// pick up the sleep trigger that
        /// <see cref="SleepJobInTheRoomDresses"/> relies on. Work and sleep are
        /// mutually exclusive on a stand, so a bedroom fixture would have
        /// tested the wrong arm.</para>
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

            // Tick the row the README tells a player to tick, and nothing else.
            fix.Comp.ToggleWork(bedRest);
            bool ok = Expect(bed.Medical,
                             "the bed takes the medical flag, so the room is a hospital")
                    & Expect(fix.Comp.HandlesWork(bedRest),
                             "and the stand is ticked for bed rest")
                    & Expect(!fix.Comp.HandlesRest(),
                             "without taking the sleep trigger — the gown and the pyjamas "
                             + "stay separate rows");

            ok &= Expect(Diverts(fix, JobMaker.MakeJob(laydown, bed),
                                 JobTag.RestingForMedicalReasons),
                         "so going to bed to recuperate dresses at the stand");

            // THE URGENT HALF, which is the emergency rule reaching this arm.
            // Vanilla splits the two itself: WorkGiver_PatientGoToBedTreatment
            // gates on ShouldSeekMedicalRestUrgent and Recuperate takes the
            // rest, so reading the same predicate puts the gown on the
            // recovering and leaves the bleeding alone. Asserted on ONE pawn
            // either side of one wound, so the only variable is the wound.
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

            // ALREADY IN BED. Vanilla reissues the patient job at a pawn
            // lying in the bed, after a tend most obviously, and without the
            // OnABed guard every reissue is a fresh trip to the wardrobe. The
            // sleep arm paid for this one in play when the trigger shipped.
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

            // A PLAYER-FORCED trip to bed is an order here as in every arm.
            Job forced = JobMaker.MakeJob(laydown, bed);
            forced.playerForced = true;
            return ok
                & Expect(!Diverts(fix, forced, JobTag.RestingForMedicalReasons),
                         "a player-forced trip to a sickbed is never diverted");
        }

        /// <summary>
        /// DEPOSIT ONLY: the stand that hands nothing out. A colonist parks
        /// what its storage filter accepts, keeps the rest of their clothes
        /// on, and gets it all back on the return trip.
        ///
        /// <para>Two things here are load-bearing far outside this feature.
        /// The first is that the stand is CLAIMED at all: <c>OnShift</c> was
        /// "something was issued" until deposit-only arrived, and had it
        /// stayed that way the parka would be parked in a rack with no
        /// borrower, no registry entry and no return trip — gone, from the
        /// player's side, with no error. The second is the refusal: a filter
        /// wide enough to take everything they have on must make the stand
        /// decline rather than send a colonist to bed naked.</para>
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

            // THE FILTER IS THE CONTROL SURFACE, AND ITS DEFAULT IS PERMISSIVE.
            // This asserted the opposite until 2026-09-03 — the comment claimed
            // "a fresh stand accepts nothing", which is the false premise the
            // whole deposit-only safety story was built on. OutfitStandBase
            // ships defaultStorageSettings (category Apparel minus
            // ApparelUtility and Weapons) and PostMake copies it, so pin the
            // real default down here: it is the fact that makes WouldBeNude
            // load-bearing rather than decorative.
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

            // NEVER BARE. Widen the filter to everything they have on and the
            // stand must decline outright rather than strip them.
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

            // The morning. Nothing new drives this: PlanUndress walks the
            // stored half of the ledger exactly as it does after any swap.
            ok &= Expect(RunSwap(fix), "the return leg ran to completion")
                & Expect(fix.Pawn.apparel.WornApparel.Contains(parka), "the parka is back on")
                & Expect(!fix.Comp.OnShift, "and the stand is free again");

            // THE UTILITY-LAYER TRAP, last because it changes what the pawn
            // wears. A shield belt covers no body part, and ApparelUtility is
            // precisely what the outfit stand's DEFAULT filter excludes — so a
            // guard that asked "is ANY garment left on?" was satisfied by the
            // belt and licensed stripping everything that actually covered the
            // colonist. Vanilla's nudity test is coverage-based
            // (Pawn_ApparelTracker.PsychologicallyNude), and so is ours now.
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
        /// HAULING: an empty deposit-only stand is not somewhere to put
        /// apparel, and an ordinary one still is.
        ///
        /// <para>Driven through <c>StoreUtility</c>'s own search rather than by
        /// reading our flag back, because the flag is not the claim — "a hauler
        /// does not come" is. The one approximation is the argument
        /// <c>currentPriority</c>, passed as <c>Normal</c>: that is the value
        /// <c>CurrentStoragePriorityOf</c> returns for a garment sitting in an
        /// ordinary stockpile, which is where the garment in the report was.
        /// Everything downstream of it is the engine's.</para>
        ///
        /// <para><b>Two stands, because "not stand A" is a claim a broken
        /// search satisfies too.</b> B is an ordinary stand, further away, and
        /// the assertion is that the hauler switches TO it — so a result of
        /// "nowhere to put it" fails rather than passes.</para>
        ///
        /// <para><b>And B is filled at the end, because that is the state that
        /// made this so hard to see in play (2026-09-08).</b> Three stands
        /// configured identically, one afflicted: <c>Accepts</c> ends at
        /// <c>HasRoomForApparelOfDef</c>, so a stand already holding a
        /// conflicting garment is immune by vanilla's own gate. The other two
        /// held an alternate gear set. Deposit-only is the one mode whose
        /// resting state is empty, so it is the one that can never become
        /// immune on its own.</para>
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

            // Empty is deposit-only's resting state, and an empty stand is the
            // only kind vanilla will haul into. Stage stocks a duster.
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

            // Adjacent to the fixture stand and three cells from the other, so
            // the search's distance tiebreak has an unambiguous answer and the
            // switch below is visible.
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

            // The narrowing is the DESTINATION flag and nothing else. If this
            // ever fails, the sleep change's own deposit and the player's
            // right-click delivery have gone with it.
            ok &= Expect(((IHaulDestination)fix.Stand).Accepts(garment),
                         "while the stand still ACCEPTS the garment, so a deposit and an "
                         + "ordered delivery are untouched");

            // Vanilla's own immunity, which is why two identical stands in play
            // never showed this: fill the fallback and it stops accepting too.
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
        /// The sleep-branch classifier and its room table, as a table.
        ///
        /// <para>Every assertion here is a fact about the DEF DATABASE rather
        /// than about our code, so this is the case that fires on a game
        /// update instead of on an edit — the same job
        /// <see cref="RecreationClassifierHolds"/> does for the joy branch.</para>
        ///
        /// <para>No map, so the bed-shaped half lives in
        /// <see cref="SleepJobInTheRoomDresses"/> and only the negatives are
        /// here.</para>
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

            // The medical half. PatientBedRest is the work type
            // MedicalRestWorkType charges a tagged lay-down to, so if it
            // stopped existing, stopped being visible, or stopped being what
            // its own giver names, the gown row would go quiet again.
            //
            // THESE ARE DEF-DATABASE FACTS AND NOTHING MORE. Read on their own
            // they once looked like proof the feature worked, and they were all
            // true for the whole period it did not: the job never carried the
            // giver, so nothing here was ever consulted at runtime. The
            // delivery is asserted in MedicalBedRestDressesAtAGownStand, which
            // is the case to look at if this one is green and players say the
            // tickbox is dead.
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
    }
}
#endif
