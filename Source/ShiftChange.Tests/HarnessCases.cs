using System;
using System.Collections;
using DevTools;
using DevTools.Testing;
using RimWorld;
using Verse;
// Case bodies live in sibling types, imported statically so the stages below read as plain
// method names.
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessCatchments;
using static ShiftChange.Tests.HarnessFixtures;
using static ShiftChange.Tests.HarnessGates;
using static ShiftChange.Tests.HarnessLifecycle;
using static ShiftChange.Tests.HarnessRideAlong;
using static ShiftChange.Tests.HarnessTriggers;

// The cases not yet written as stories, one DevTools test each, in run order. DevTools runs the
// fixtures by their ExecutionPriority, which is each one's place in CaseGroup, the save/load legs
// last of all, and a fixture's tests in the order they are declared here. HarnessTestOrder checks both. A test is
// named after its case's body; its sentence, the TestDescription, opens its transcript in
// Test.log and is its tooltip in DevTools' test explorer. How the Run calls stage and judge a
// case: HarnessFixture, in HarnessDevTools.cs.
namespace ShiftChange.Tests
{
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Flights)]
    internal class HarnessFlights : HarnessFixture
    {
        [Test]
        [TestDescription("gravship flight keeps the ledger, and the shift ends normally after landing")]
        internal IEnumerator GravshipFlight() => Run(HarnessLifecycle.GravshipFlight);

        // Flights that leave the borrower and the stand on two maps, the second a pocket map
        // (HarnessFixtures.SecondMap).
        [Test]
        [TestDescription("a stand that flies without its borrower keeps the shift, and ends it normally when they meet again")]
        internal IEnumerator StandFliesWithoutItsBorrower()
            => Run(HarnessLifecycle.StandFliesWithoutItsBorrower);

        [Test]
        [TestDescription("a borrower who flies without their stand keeps the shift, and ends it normally on coming back")]
        internal IEnumerator BorrowerFliesWithoutTheirStand()
            => Run(HarnessLifecycle.BorrowerFliesWithoutTheirStand);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.VacuumGate)]
    internal class HarnessVacuumGate : HarnessFixture
    {
        [Test]
        [TestDescription("a crafter in uniform changes back before a bill through vacuum, and not before one inside")]
        internal IEnumerator OnShiftChangesBackBeforeVacuum()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.OnShiftChangesBackBeforeVacuum);

        [Test]
        [TestDescription("an order through vacuum runs as given, and only a warning says so")]
        internal IEnumerator OrderIntoVacuumRunsAsGiven()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.OrderIntoVacuumRunsAsGiven);

        [Test]
        [TestDescription("an emergency through vacuum sends a colonist in uniform to change first")]
        internal IEnumerator EmergencyIntoVacuumChangesBackFirst()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.EmergencyIntoVacuumChangesBackFirst);

        [Test]
        [TestDescription("a stand holding a vacsuit dresses an unsuited crafter for a bill through vacuum")]
        internal IEnumerator VacsuitStandStillDresses()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.VacsuitStandStillDresses);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.VacuumRoutes)]
    internal class HarnessVacuumRoutes : HarnessFixture
    {
        [Test]
        [TestDescription("an errand ahead of the stand's work rides along in uniform, but not into vacuum")]
        internal IEnumerator ErrandAheadOfWorkChangesBackBeforeVacuum()
            => Run(HarnessVacuum.StageConnectedShip, HarnessVacuum.ErrandAheadOfWorkChangesBackBeforeVacuum);

        [Test]
        [TestDescription("a colonist cut off from their stand by vacuum is not sent across it to change")]
        internal IEnumerator StrandedColonistIsNotSentAcrossToChange()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.StrandedColonistIsNotSentAcrossToChange);

        [Test]
        [TestDescription("a cooldown does not wave a colonist in uniform into vacuum")]
        internal IEnumerator CooldownDoesNotWaveThemIntoVacuum()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.CooldownDoesNotWaveThemIntoVacuum);

        [Test]
        [TestDescription("an urgent patient goes straight to a bed across vacuum")]
        internal IEnumerator UrgentPatientGoesStraightToBedAcrossVacuum()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.UrgentPatientGoesStraightToBedAcrossVacuum);

        [Test]
        [TestDescription("a colonist whose stand is on another map is warned before vacuum")]
        internal IEnumerator StandOnAnotherMapStillWarns()
            => Run(HarnessVacuum.StageCrafterAtHome, HarnessVacuum.StandOnAnotherMapStillWarns);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Lifecycle)]
    internal class HarnessLifecycleEvents : HarnessFixture
    {
        [Test]
        [TestDescription("reinstalling mid-shift keeps the ledger and the return trip")]
        internal IEnumerator ReinstallKeepsTheLedger()
            => Run(HarnessLifecycle.ReinstallKeepsTheLedger);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Arms)]
    internal class HarnessArms : HarnessFixture
    {
        [Test]
        [TestDescription("going to bed in the stand's room dresses, medical bed rest does not")]
        internal IEnumerator SleepJobInTheRoomDresses()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessTriggers.SleepJobInTheRoomDresses);

        [Test]
        [TestDescription("a deposit-only stand parks what its filter accepts and hands it back")]
        internal IEnumerator DepositOnlyParksAndReturns()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessTriggers.DepositOnlyParksAndReturns);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Gates)]
    internal class HarnessGateCases : HarnessFixture
    {
        [Test]
        [TestDescription("a meal break gets them out of uniform first")]
        internal IEnumerator MealBreakChangesOut()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessGates.MealBreakChangesOut);

        [Test]
        [TestDescription("and a sleepwear meal break does too, even with the meal in inventory")]
        internal IEnumerator SleepwearMealBreakChangesOut()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessGates.SleepwearMealBreakChangesOut);

        [Test]
        [TestDescription("under threat they may not change in, but may change back")]
        internal IEnumerator DangerGateIsOneDirectional()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessGates.DangerGateIsOneDirectional);

        [Test]
        [TestDescription("a pawn under a lord duty is left alone, and released when it ends")]
        internal IEnumerator DutyGateShutsBothArms()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessGates.DutyGateShutsBothArms);

        [Test]
        [TestDescription("a freed stand catches up a colonist working bare")]
        internal IEnumerator FreedStandCatchesUp()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessGates.FreedStandCatchesUp);

        [Test]
        [TestDescription("Change back holds the room until they leave it, and drafting drops it")]
        internal IEnumerator ChangeBackLatchHoldsUntilTheyLeave()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessGates.ChangeBackLatchHoldsUntilTheyLeave);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Radius)]
    internal class HarnessRadius : HarnessFixture
    {
        [Test]
        [TestDescription("a freed radius stand interrupts nobody, a room stand still does")]
        internal IEnumerator FreedRadiusStandCatchesNobody()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.FreedRadiusStandCatchesNobody);

        [Test]
        [TestDescription("a room stand in the open serves no work either, and the alert lists it")]
        internal IEnumerator RoomStandInTheOpenServesNothing()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.RoomStandInTheOpenServesNothing);

        [Test]
        [TestDescription("a radius work stand keeps the uniform on for its work, pass-through work and idling, and nothing else")]
        internal IEnumerator RadiusWorkStandKeepsTheUniformInItsRing()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessCatchments.RadiusWorkStandKeepsTheUniformInItsRing);

        [Test]
        [TestDescription("pass-through work is judged at both ends, and an errand ahead of the stand's work rides along")]
        internal IEnumerator PassThroughWorkIsJudgedAtBothEnds()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessCatchments.PassThroughWorkIsJudgedAtBothEnds);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.BillProducts)]
    internal class HarnessBillProducts : HarnessFixture
    {
        /// <summary>
        /// The stonecutting bill's own work: Crafting, or Stone Cut with Complex Jobs.
        /// </summary>
        internal static WorkTypeDef BillWork
            => DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsStonecut")?.workType;

        [Test]
        [TestDescription("a bill's product hauled to a stockpile in the area keeps the uniform on")]
        internal IEnumerator BillProductHauledInTheAreaKeepsTheUniform()
            => Run((m, p) => StageWithDoorway(m, p, BillWork), f => BillProductHaul(f, CatchmentKind.Area, true, true, true));

        [Test]
        [TestDescription("a bill's product hauled to a stockpile in the ring keeps the uniform on")]
        internal IEnumerator BillProductHauledInTheRingKeepsTheUniform()
            => Run((m, p) => StageWithDoorway(m, p, BillWork), f => BillProductHaul(f, CatchmentKind.Radius, true, true, true));

        [Test]
        [TestDescription("the same with Hauling not ticked, because the bill's work is what the stand serves")]
        internal IEnumerator BillProductHauledInTheAreaKeepsItWithoutHauling()
            => Run((m, p) => StageWithDoorway(m, p, BillWork), f => BillProductHaul(f, CatchmentKind.Area, false, true, true));

        [Test]
        [TestDescription("a bill's product bound for a stockpile past the area changes them back first")]
        internal IEnumerator BillProductBoundPastTheAreaChangesThemBack()
            => Run((m, p) => StageWithDoorway(m, p, BillWork), f => BillProductHaul(f, CatchmentKind.Area, true, false, false));

        [Test]
        [TestDescription("a bill's product bound for a stockpile past the ring changes them back first")]
        internal IEnumerator BillProductBoundPastTheRingChangesThemBack()
            => Run((m, p) => StageWithDoorway(m, p, BillWork), f => BillProductHaul(f, CatchmentKind.Radius, true, false, false));

        [Test]
        [TestDescription("the same with Hauling not ticked")]
        internal IEnumerator BillProductBoundPastTheAreaChangesThemBackWithoutHauling()
            => Run((m, p) => StageWithDoorway(m, p, BillWork), f => BillProductHaul(f, CatchmentKind.Area, false, false, false));

        [Test]
        [TestDescription("a room stand keeps the uniform on for a bill's product bound for a stockpile in the room")]
        internal IEnumerator RoomStandKeepsItForABillProductInTheRoom()
            => Run((m, p) => StageWithDoorway(m, p, BillWork), f => BillProductHaul(f, CatchmentKind.Room, true, true, true));

        [Test]
        [TestDescription("and for one bound past its door, as it always has")]
        internal IEnumerator RoomStandKeepsItForABillProductPastItsDoor()
            => Run((m, p) => StageWithDoorway(m, p, BillWork), f => BillProductHaul(f, CatchmentKind.Room, true, false, true));

        [Test]
        [TestDescription("a bill's product is judged at the stockpile, not the crafter's unpainted cell")]
        internal IEnumerator BillProductIsJudgedAtTheStockpile()
            => Run((m, p) => StageWithDoorway(m, p, BillWork), HarnessCatchments.BillProductIsJudgedAtTheStockpile);

        [Test]
        [TestDescription("a bill's product goes by the ordinary rule once the stand stops serving the bill")]
        internal IEnumerator BillProductOfUnservedWorkChangesThemBack()
            => Run((m, p) => StageWithDoorway(m, p, BillWork), HarnessCatchments.BillProductOfUnservedWorkChangesThemBack);

        [Test]
        [TestDescription("a haul that starts with the stack on the ground is no bill's product")]
        internal IEnumerator BillProductMustBeInHand()
            => Run((m, p) => StageWithDoorway(m, p, BillWork), HarnessCatchments.BillProductMustBeInHand);

        [Test]
        [TestDescription("the same stack in hand after other served work is no bill's product")]
        internal IEnumerator BillProductMustComeFromABill()
            => Run((m, p) => StageWithDoorway(m, p, BillWork), HarnessCatchments.BillProductMustComeFromABill);

        [Test]
        [TestDescription("a crafter off shift is not dressed to store what they made")]
        internal IEnumerator BillProductDressesNobody()
            => Run((m, p) => StageWithDoorway(m, p, BillWork), HarnessCatchments.BillProductDressesNobody);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Latches)]
    internal class HarnessLatches : HarnessFixture
    {
        [Test]
        [TestDescription("a radius sleep stand changes them back on the first job out of bed")]
        internal IEnumerator RadiusSleepStandKeepsItOnlyForBed()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessCatchments.RadiusSleepStandKeepsItOnlyForBed);

        [Test]
        [TestDescription("a radius recreation stand keeps the outfit on for play and idling, not for hauling")]
        internal IEnumerator RadiusRecreationStandKeepsItForPlay()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessCatchments.RadiusRecreationStandKeepsItForPlay);

        [Test]
        [TestDescription("the job a colonist was just dressed for never changes them back, though its target walks away")]
        internal IEnumerator JustDressedForNeverSendsThemBack()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.JustDressedForNeverSendsThemBack);

        [Test]
        [TestDescription("the change-back latch holds its stand and trigger only, until they leave the ring")]
        internal IEnumerator LatchHoldsItsStandAndTriggerOnly()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.LatchHoldsItsStandAndTriggerOnly);

        [Test]
        [TestDescription("at a sleep rack the latch holds through the night and lifts on waking")]
        internal IEnumerator SleepRackLatchLiftsOnWaking()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessCatchments.SleepRackLatchLiftsOnWaking);

        // A known gap unless Complex Jobs is loaded (--with=).
        [Test]
        [TestDescription("a delivery dresses nobody at a room or radius stand, keeps its own work's uniform on, and the site does")]
        internal IEnumerator DeliveriesKeepTheirOwnWorkOn()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessCatchments.DeliveriesKeepTheirOwnWorkOn);

        [Test]
        [TestDescription("a delivery outside the stand's work changes pyjamas and whites back first")]
        internal IEnumerator DeliveryOutsideTheStandsWorkChangesThemBack()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Construction")), HarnessCatchments.DeliveryOutsideTheStandsWorkChangesThemBack);

        [Test]
        [TestDescription("an urgent patient on shift goes straight to bed, untagged and behind a haul too")]
        internal IEnumerator UrgentPatientGoesStraightToBed()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.UrgentPatientGoesStraightToBed);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Areas)]
    internal class HarnessAreas : HarnessFixture
    {
        [Test]
        [TestDescription("an area stand dresses for a delivery whose steel and every site are painted in, and for no other")]
        internal IEnumerator AreaStandDressesForADeliveryItHolds()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Construction")), HarnessCatchments.AreaStandDressesForADeliveryItHolds);

        [Test]
        [TestDescription("the alert lists a stand whose area is gone or that stands outside it")]
        internal IEnumerator ServesNothingAlertListsTheStand()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.ServesNothingAlertListsTheStand);

        [Test]
        [TestDescription("a solid target is judged at its own cell, then the floor beside it")]
        internal IEnumerator SolidTargetIsJudgedAtItsOwnCellFirst()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Mining")), HarnessCatchments.SolidTargetIsJudgedAtItsOwnCellFirst);

        [Test]
        [TestDescription("a colonist whose stand is outside their area keeps the outfit on, and the stand says why")]
        internal IEnumerator StandOutsideTheAreaKeepsTheOutfitOn()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessGates.StandOutsideTheAreaKeepsTheOutfitOn);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.RideAlong)]
    internal class HarnessRideAlongCases : HarnessFixture
    {
        [Test]
        [TestDescription("feeding a patient or a prisoner dresses nobody, and keeps a doctor's or a warden's uniform on")]
        internal IEnumerator FeedingRidesAlong()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessRideAlong.FeedingRidesAlong);

        [Test]
        [TestDescription("a meal run keeps the uniform on only for a patient inside, and so does a haul slipped in ahead of it")]
        internal IEnumerator MealRunKeepsTheUniformForAPatientInside()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessRideAlong.MealRunKeepsTheUniformForAPatientInside);

        [Test]
        [TestDescription("an errand queued ahead of work the stand serves keeps the uniform on")]
        internal IEnumerator DetourAheadOfServedWorkKeepsTheUniform()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessRideAlong.DetourAheadOfServedWorkKeepsTheUniform);

        [Test]
        [TestDescription("and so does vanilla's own opportunistic haul on the way to a bill")]
        internal IEnumerator VanillaOpportunisticHaulKeepsTheUniform()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Hauling")), HarnessRideAlong.VanillaOpportunisticHaulKeepsTheUniform);

        // Known gaps unless the mod is loaded (--with=).
        [Test]
        [TestDescription("Common Sense's bill haul keeps a cook in uniform")]
        internal IEnumerator CommonSenseBillHaulKeepsTheUniform()
            => Run(StageWithDoorway, HarnessRideAlong.CommonSenseBillHaulKeepsTheUniform);

        [Test]
        [TestDescription("Pick Up And Haul's follow-up jobs keep the uniform on")]
        internal IEnumerator PickUpAndHaulFollowUpsKeepTheUniform()
            => Run(StageWithDoorway, HarnessRideAlong.PickUpAndHaulFollowUpsKeepTheUniform);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Assorted)]
    internal class HarnessAssorted : HarnessFixture
    {
        [Test]
        [TestDescription("fishing dresses at a stand ticked for it, and at a radius stand by the shore")]
        internal IEnumerator FishingDressesAtAStandTickedForIt()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Fishing")), HarnessTriggers.FishingDressesAtAStandTickedForIt);
    }

    /// <summary>
    /// The four save and load legs of <see cref="DebugTools_SaveRoundTrip"/>, in run order, each
    /// on the game the one before left. Only the first leg reads the map and pad. The others run on
    /// the game the leg before loaded, which a mod that breaks a load can leave with no clear pad,
    /// and they reach their own known gap for that.
    ///
    /// <para>A load replaces the game DevTools generated, and DevTools goes on tracking that
    /// game's map. Asking for a world of its own makes DevTools generate a fresh one for this
    /// fixture and again for whichever fixture follows. It runs last, out of its group's place,
    /// so none follows.</para>
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.SaveLoadPriority)]
    [WarningsAllowedFrom(HarnessDevToolsSaveLoad.VarietyFrame, HarnessDevToolsSaveLoad.StaleReference)]
    [ErrorsAllowedFrom(HarnessDevToolsSaveLoad.VarietyFrame, HarnessDevToolsSaveLoad.WrongReference)]
    [ErrorsAllowedFrom(HarnessDevToolsSaveLoad.OwnLoadFrame,
                       HarnessDevToolsSaveLoad.VehicleCacheAbort)]
    internal class HarnessDevToolsSaveLoad : HarnessFixture, IWorldGeneration
    {
        WorldGenerationSettings IWorldGeneration.WorldGenerationSettings
        {
            get { return new WorldGenerationSettings(); }
        }

        /// <summary>
        /// The save code of Vanilla Food Variety Expanded's per-colonist diet record. The mod keeps
        /// one for every colonist it has seen in a static table that no game end clears, and
        /// DevTools plays several games in one run, so an earlier game's colonists reach this
        /// fixture's saves. Their load logs a reference that resolves to nothing or to some other
        /// thing. A stand owner saved wrongly logs the same lines from this mod's code, so they
        /// pass only with this frame on their stack.
        /// </summary>
        internal const string VarietyFrame =
            @"VanillaCookingExpandedVariety\.PawnDiet\.ExposeData";

        /// <summary>
        /// The warnings for a stale pawn or corpse id: it matches nothing, or falls back to
        /// whatever thing now has its number.
        /// </summary>
        internal const string StaleReference =
            @"^Could not resolve reference to (Thing|object) with loadID Thing_";

        /// <summary>The error for a stale id whose number now belongs to a thing of another type.</summary>
        internal const string WrongReference =
            @"^Exception getting object with thing id \d+ of type Verse\.(Pawn|Corpse)\.";

        /// <summary>
        /// This mod's load call. Vehicle Framework keeps a static map-component cache keyed by map
        /// index that no in-process load clears, so a leg's load can collide with an earlier
        /// game's entry and abort FinalizeLoading; the leg already records that as a known gap.
        /// The abort's stack holds only engine frames and this call, and its message names the
        /// cache, which no code of this mod can.
        /// </summary>
        internal const string OwnLoadFrame = @"ShiftChange\.Tests\.DebugTools_SaveRoundTrip\.NoteLoad\(";

        /// <summary>The FinalizeLoading abort that names Vehicle Framework's cache.</summary>
        internal const string VehicleCacheAbort =
            @"^Error in Map\.FinalizeLoading\(\): [\s\S]*SmashTools\.DetachedMapComponentCache";

        /// <summary>
        /// Runs a case as the game runs a load: inside LongEventHandler's finishing pass, so what
        /// the load defers to that pass runs after the load. With no long event running, a
        /// deferred action runs on the spot, halfway through the load, before the world it reads
        /// has been loaded.
        /// </summary>
        protected override void Wrap(Action runCase)
        {
            LongEventHandler.ExecuteWhenFinished(runCase);
        }

        // These replace Current.Game, so DevTools' map, the pad and every fixture before them
        // belong to a disposed game once they have run. They stage no fixture for the same
        // reason, since Teardown would clear a pad on the disposed map. See
        // DebugTools_SaveRoundTrip.
        [Test]
        [TestDescription("a save/load round trip keeps the owner and the ledger, and sweeps the removal flag")]
        internal IEnumerator RoundTrip()
            => RunOnPad(() => DebugTools_SaveRoundTrip.RoundTrip(Map, Pad));

        [Test]
        [TestDescription("a legacy-key save gets its owner back and re-saves it prefixed")]
        internal IEnumerator LegacyMigration() => Run(DebugTools_SaveRoundTrip.LegacyMigration);

        [Test]
        [TestDescription("an older save's foreign owner round-trips without contest and is adopted")]
        internal IEnumerator ForeignAssignable() => Run(DebugTools_SaveRoundTrip.ForeignAssignable);

        [Test]
        [TestDescription("an area binding survives a save under a new name, and a deleted area's is dropped")]
        internal IEnumerator AreaRoundTrip() => Run(DebugTools_SaveRoundTrip.AreaRoundTrip);
    }
}
