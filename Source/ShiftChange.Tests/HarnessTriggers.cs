using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Cases for what arms a stand (work, recreation, sleep) and for deposit-only, listed and
    /// ordered in HarnessCases.cs.
    /// </summary>
    internal static class HarnessTriggers
    {
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
