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
using static ShiftChange.Tests.HarnessInterop;
using static ShiftChange.Tests.HarnessLifecycle;
using static ShiftChange.Tests.HarnessGizmos;
using static ShiftChange.Tests.HarnessOwnership;
using static ShiftChange.Tests.HarnessRecolor;
using static ShiftChange.Tests.HarnessRideAlong;
using static ShiftChange.Tests.HarnessScoring;
using static ShiftChange.Tests.HarnessSwap;
using static ShiftChange.Tests.HarnessTables;
using static ShiftChange.Tests.HarnessTriggers;

// Every case, one DevTools test each, in run order. DevTools runs the fixtures by their
// ExecutionPriority, which is each one's place in CaseGroup, the save/load legs last of all, and
// a fixture's tests in the order they are declared here. HarnessTestOrder checks both. A test is
// named after its case's body; its sentence, the TestDescription, opens its transcript in
// Test.log and is its tooltip in DevTools' test explorer. How the Run calls stage and judge a
// case: HarnessFixture, in HarnessDevTools.cs.
namespace ShiftChange.Tests
{
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Flights)]
    internal class HarnessFlights : HarnessFixture
    {
        // First: every case below takes for granted what this checks.
        [Test]
        [TestDescription("the fixture colonist is healthy and holds still under the stopped clock, and ground past the pad is ground open to the map edge")]
        internal IEnumerator FixtureColonistHoldsStill()
            => Run(fix => HarnessFixtureChecks.FixtureColonistHoldsStill(fix, Pad));

        [Test]
        [TestDescription("gravship flight keeps the ledger, and the shift ends normally after landing")]
        internal IEnumerator GravshipFlight() => Run(HarnessLifecycle.GravshipFlight);

        [Test]
        [TestDescription("gravship flight without the borrower releases it")]
        internal IEnumerator GravshipFlightLeftBehind()
            => Run(HarnessLifecycle.GravshipFlightLeftBehind);

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

        // The next two build on the second map, so the fixture's Map is the pocket one and the
        // body is handed DevTools' map and the pad.
        [Test]
        [TestDescription("a stand lost with the map it was left on stops pinning its borrower's uniform")]
        internal IEnumerator StandLostWithItsMap()
            => Run(BuildOnSecondMap, fix => HarnessLifecycle.StandLostWithItsMap(fix, Map, Pad));

        [Test]
        [TestDescription("a boxed stand lost with its map releases its ledger too")]
        internal IEnumerator BoxedStandLostWithItsMap()
            => Run(BuildOnSecondMap, fix => HarnessLifecycle.BoxedStandLostWithItsMap(fix, Map, Pad));
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.VacuumGate)]
    internal class HarnessVacuumGate : HarnessFixture
    {
        // The vacuum gate's ground: the second map is a vacuum map; the first case checks it.
        [Test]
        [TestDescription("a room on the second map, a vacuum map, holds air while the open floor past its door does not")]
        internal IEnumerator VacuumMapStages()
            => RunOnPad(() => HarnessVacuum.VacuumMapStages(Map));

        [Test]
        [TestDescription("a bill fetching through vacuum keeps a crafter in their vacsuit, where one inside dresses them")]
        internal IEnumerator IngredientInVacuumKeepsTheirGear()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.IngredientInVacuumKeepsTheirGear);

        [Test]
        [TestDescription("a bill fetching from a safe room only vacuum leads to keeps them in their vacsuit too")]
        internal IEnumerator SafeRoomAcrossVacuumKeepsTheirGear()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.SafeRoomAcrossVacuumKeepsTheirGear);

        [Test]
        [TestDescription("on a ship whose rooms all connect through air, the same bill dresses them")]
        internal IEnumerator ConnectedShipChangesNothing()
            => Run(HarnessVacuum.StageConnectedShip, HarnessVacuum.ConnectedShipChangesNothing);

        [Test]
        [TestDescription("a crafter in uniform changes back before a bill through vacuum, and not before one inside")]
        internal IEnumerator OnShiftChangesBackBeforeVacuum()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.OnShiftChangesBackBeforeVacuum);

        [Test]
        [TestDescription("a crafter with no vacuum protection to lose is dressed as before")]
        internal IEnumerator UnprotectedCrafterIsUnaffected()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.UnprotectedCrafterIsUnaffected);

        [Test]
        [TestDescription("an order through vacuum runs as given, and only a warning says so")]
        internal IEnumerator OrderIntoVacuumRunsAsGiven()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.OrderIntoVacuumRunsAsGiven);

        [Test]
        [TestDescription("an emergency through vacuum sends a colonist in uniform to change first")]
        internal IEnumerator EmergencyIntoVacuumChangesBackFirst()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.EmergencyIntoVacuumChangesBackFirst);

        [Test]
        [TestDescription("off a vacuum map, a crafter in a vacsuit is dressed for a bill fetching from outside, as before")]
        internal IEnumerator PlanetMapChangesNothing()
            => Run(HarnessVacuum.StageEnclosedCrafter, HarnessVacuum.PlanetMapChangesNothing);

        [Test]
        [TestDescription("a stand holding a vacsuit dresses an unsuited crafter for a bill through vacuum")]
        internal IEnumerator VacsuitStandStillDresses()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.VacsuitStandStillDresses);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.VacuumRoutes)]
    internal class HarnessVacuumRoutes : HarnessFixture
    {
        [Test]
        [TestDescription("a vacsuit stand across the room wins over a nearer one that would take the suit off")]
        internal IEnumerator ProtectiveStandWinsOverANearerOne()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.ProtectiveStandWinsOverANearerOne);

        [Test]
        [TestDescription("an area stand painted over open floor does not dress for a delivery from out there")]
        internal IEnumerator AreaDeliveryAcrossVacuumKeepsTheirGear()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.AreaDeliveryAcrossVacuumKeepsTheirGear);

        [Test]
        [TestDescription("a freed stand does not catch up a crafter on a job fetching through vacuum")]
        internal IEnumerator FreedStandSkipsAJobThroughVacuum()
            => Run(HarnessVacuum.StageShip, HarnessVacuum.FreedStandSkipsAJobThroughVacuum);

        [Test]
        [TestDescription("an errand ahead of the stand's work rides along in uniform, but not into vacuum")]
        internal IEnumerator ErrandAheadOfWorkChangesBackBeforeVacuum()
            => Run(HarnessVacuum.StageConnectedShip, HarnessVacuum.ErrandAheadOfWorkChangesBackBeforeVacuum);

        [Test]
        [TestDescription("the air walk does not pass a door forbidden to the colonist")]
        internal IEnumerator ForbiddenDoorIsNoWayThrough()
            => Run(HarnessVacuum.StageConnectedShip, HarnessVacuum.ForbiddenDoorIsNoWayThrough);

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
        [TestDescription("every stand def asks to hear when its map is removed")]
        internal IEnumerator StandDefsHearMapRemoval()
            => Run(HarnessLifecycle.StandDefsHearMapRemoval);

        [Test]
        [TestDescription("teardown (deconstruct, burn) releases the ledger")]
        internal IEnumerator TeardownReleases() => Run(HarnessLifecycle.TeardownReleases);

        [Test]
        [TestDescription("reinstalling mid-shift keeps the ledger and the return trip")]
        internal IEnumerator ReinstallKeepsTheLedger()
            => Run(HarnessLifecycle.ReinstallKeepsTheLedger);

        [Test]
        [TestDescription("reinstalling a stand keeps its owner, mode and flag")]
        internal IEnumerator ReinstallKeepsConfiguration()
            => Run(HarnessLifecycle.ReinstallKeepsConfiguration);

        [Test]
        [TestDescription("an owner who dies while their stand is boxed does not own it again")]
        internal IEnumerator DeadOwnerNotRestoredOnReinstall()
            => Run(HarnessLifecycle.DeadOwnerNotRestoredOnReinstall);

        [Test]
        [TestDescription("an owner kidnapped while their stand is boxed neither owns it nor keeps their shift there")]
        internal IEnumerator KidnappedOwnerNotRestoredOnReinstall()
            => Run(HarnessLifecycle.KidnappedOwnerNotRestoredOnReinstall);

        [Test]
        [TestDescription("borrower death reaps the ledger")]
        internal IEnumerator DeathReaps() => Run(HarnessLifecycle.DeathReaps);

        [Test]
        [TestDescription("borrower banishment reaps the ledger")]
        internal IEnumerator BanishmentReaps() => Run(HarnessLifecycle.BanishmentReaps);
    }

    /// <summary>
    /// The case throws three faults through interception on purpose. Each logs an error, and the
    /// third also logs the latch closing. Only those are allowed: a throw of any other kind logs
    /// its own error, which fails the test.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.FaultLatch)]
    [ErrorsAllowed(@"\[ShiftChange\] injected test fault",
                   @"\[ShiftChange\] interception disabled for this game after \d+ faults")]
    internal class HarnessFaultLatch : HarnessFixture
    {
        [Test]
        [TestDescription("repeated faults disable interception, a load re-arms it")]
        internal IEnumerator FaultLatchRecovers() => Run(HarnessLifecycle.FaultLatchRecovers);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.DriverAndOwners)]
    internal class HarnessDriverAndOwners : HarnessFixture
    {
        [Test]
        [TestDescription("a stand that displaces nothing still hands the uniform back")]
        internal IEnumerator NonDisplacingReturns()
            => Run((m, p) => Stage(m, p, StageKit.NonDisplacing), HarnessSwap.NonDisplacingReturns);

        [Test]
        [TestDescription("the driver returns own clothes and their forced flags")]
        internal IEnumerator DriverRoundTrip()
            => Run((m, p) => Stage(m, p, StageKit.Displacing), HarnessSwap.DriverRoundTrip);

        [Test]
        [TestDescription("work and recreation are mutually exclusive on a stand")]
        internal IEnumerator WorkAndRecreationAreExclusive()
            => Run((m, p) => Stage(m, p, StageKit.Displacing), HarnessTriggers.WorkAndRecreationAreExclusive);

        [Test]
        [TestDescription("the removal flag is held off while a stand is in service")]
        internal IEnumerator RemovalFlagHeldOffInService()
            => Run(HarnessOwnership.RemovalFlagHeldOffInService);

        [Test]
        [TestDescription("the change-back gate hands the chain back untouched")]
        internal IEnumerator ChangeBackGateHandsTheChainBack()
            => Run(HarnessOwnership.ChangeBackGateHandsTheChainBack);

        [Test]
        [TestDescription("and still appends the button for a pawn in uniform")]
        internal IEnumerator ChangeBackGateStillAppends()
            => Run(HarnessOwnership.ChangeBackGateStillAppends);

        [Test]
        [TestDescription("the removal-toggle gate leaves a stand it does not govern alone")]
        internal IEnumerator RemovalToggleGateLeavesForeignStandsAlone()
            => Run(HarnessOwnership.RemovalToggleGateLeavesForeignStandsAlone);

        [Test]
        [TestDescription("an owner list restricts the stand to its owners")]
        internal IEnumerator OwnerListRestricts()
            => Run((m, p) => Stage(m, p, StageKit.Displacing), HarnessOwnership.OwnerListRestricts);

        [Test]
        [TestDescription("the owner dialog's gender filter offers the right candidates")]
        internal IEnumerator OwnerFilterOffersTheRightPawns()
            => Run((m, p) => Stage(m, p, StageKit.Displacing), HarnessOwnership.OwnerFilterOffersTheRightPawns);

        [Test]
        [TestDescription("one owner is copied into another mod's owner list, a shared stand is not")]
        internal IEnumerator ForeignOwnersCopyOurs()
            => Run(StageWithForeignAssignable, HarnessOwnership.ForeignOwnersCopyOurs);

        [Test]
        [TestDescription("an older save's two owner lists reconcile to the one the player could see")]
        internal IEnumerator ReconcileKeepsTheVisibleList()
            => Run(StageWithForeignAssignable, HarnessOwnership.ReconcileKeepsTheVisibleList);

        [Test]
        [TestDescription("a group stand keeps its owners out of shift use, however it gets there")]
        internal IEnumerator GroupSurvivesLeavingShiftUse()
            => Run((m, p) => Stage(m, p, StageKit.Displacing), HarnessOwnership.GroupSurvivesLeavingShiftUse);

        [Test]
        [TestDescription("out of shift use one pick replaces a kept group and reaches the other mod's list")]
        internal IEnumerator PickReplacesAKeptGroup()
            => Run(StageWithForeignAssignable, HarnessOwnership.PickReplacesAKeptGroup);

        [Test]
        [TestDescription("a kept group reaches nobody through the copy and survives a move")]
        internal IEnumerator KeptGroupSurvivesAMove()
            => Run(StageWithForeignAssignable, HarnessOwnership.KeptGroupSurvivesAMove);

        // A known gap unless Outfit Stands Plus is loaded (--with=).
        [Test]
        [TestDescription("Outfit Stands Plus' stand button offers what its own walk would, in order")]
        internal IEnumerator OutfitStandsPlusButtonMatchesTheirs()
            => Run((m, p) => Stage(m, p, StageKit.Displacing), HarnessInterop.OutfitStandsPlusButtonMatchesTheirs);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Arms)]
    internal class HarnessArms : HarnessFixture
    {
        [Test]
        [TestDescription("a joy job in the stand's room dresses")]
        internal IEnumerator JoyJobInTheRoomDresses()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessTriggers.JoyJobInTheRoomDresses);

        [Test]
        [TestDescription("going to bed in the stand's room dresses, medical bed rest does not")]
        internal IEnumerator SleepJobInTheRoomDresses()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessTriggers.SleepJobInTheRoomDresses);

        [Test]
        [TestDescription("a stand ticked for bed rest dresses a patient, but never an urgent one")]
        internal IEnumerator MedicalBedRestDressesAtAGownStand()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessTriggers.MedicalBedRestDressesAtAGownStand);

        [Test]
        [TestDescription("the medical emergency setting covers doctors and patients, never fires")]
        internal IEnumerator MedicalEmergencySettingCoversTheRightWork()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessTriggers.MedicalEmergencySettingCoversTheRightWork);

        [Test]
        [TestDescription("the new-stand setting decides what a stand made from then on starts as")]
        internal IEnumerator NewStandSettingDecidesOnlyNewStands()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessTriggers.NewStandSettingDecidesOnlyNewStands);

        [Test]
        [TestDescription("the job classifier sorts every job shape in the arms' order")]
        internal IEnumerator ClassifierSortsEveryJobShape()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessJobKinds.ClassifierSortsEveryJobShape);

        [Test]
        [TestDescription("a deposit-only stand parks what its filter accepts and hands it back")]
        internal IEnumerator DepositOnlyParksAndReturns()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessTriggers.DepositOnlyParksAndReturns);

        [Test]
        [TestDescription("and haulers leave it alone, while an ordinary stand still takes deliveries")]
        internal IEnumerator DepositOnlyStandIsNoHaulTarget()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessTriggers.DepositOnlyStandIsNoHaulTarget);

        [Test]
        [TestDescription("full change swaps the whole outfit and gives it all back")]
        internal IEnumerator FullChangeSwapsEverything()
            => Run((m, p) => Stage(m, p, StageKit.Displacing), HarnessSwap.FullChangeSwapsEverything);

        [Test]
        [TestDescription("full change refuses to strip a colonist bare")]
        internal IEnumerator FullChangeRefusesToStripThemBare()
            => Run((m, p) => Stage(m, p, StageKit.Displacing), HarnessSwap.FullChangeRefusesToStripThemBare);

        [Test]
        [TestDescription("a legs-only stand leaves a man decent (control for the pair)")]
        internal IEnumerator LegsOnlyStandLeavesAManDecent()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, gender: Gender.Male), HarnessSwap.LegsOnlyStandLeavesAManDecent);

        [Test]
        [TestDescription("and the same stand keeps a woman's shirt on, because her rule wants it")]
        internal IEnumerator LegsOnlyStandStripsAWoman()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, gender: Gender.Female), HarnessSwap.LegsOnlyStandStripsAWoman);

        [Test]
        [TestDescription("a nudist is left undressed, because that is the point")]
        internal IEnumerator NudistIsExemptFromDecency()
            => Run((m, p) => Stage(m, p, StageKit.Displacing), HarnessSwap.NudistIsExemptFromDecency);

        [Test]
        [TestDescription("the decency guard ships off, and turning it on takes effect")]
        internal IEnumerator DecencyGuardIsOptIn()
            => Run((m, p) => Stage(m, p, StageKit.Displacing), HarnessSwap.DecencyGuardIsOptIn);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Gates)]
    internal class HarnessGateCases : HarnessFixture
    {
        [Test]
        [TestDescription("the rules the description promises hold")]
        internal IEnumerator PromisesHold()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessGates.PromisesHold);

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
        [TestDescription("a job handed to a pawn mid-swap is not deferred on top of it")]
        internal IEnumerator MidSwapJobIsNotDeferredAgain()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessGates.MidSwapJobIsNotDeferredAgain);

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
        [TestDescription("a radius covers its ring, inclusive, and room mode its room")]
        internal IEnumerator RadiusCoversItsRing()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessCatchments.RadiusCoversItsRing);

        [Test]
        [TestDescription("a stand leaves room mode only with something ticked, and comes back with nothing")]
        internal IEnumerator CatchmentNeedsSomethingTicked()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessCatchments.CatchmentNeedsSomethingTicked);

        [Test]
        [TestDescription("an automatic stand reads its room's new role on the same tick, with the contents scan on")]
        internal IEnumerator AutomaticReadingFollowsTheRoomsRole()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessCatchments.AutomaticReadingFollowsTheRoomsRole);

        [Test]
        [TestDescription("a freed radius stand interrupts nobody, a room stand still does")]
        internal IEnumerator FreedRadiusStandCatchesNobody()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.FreedRadiusStandCatchesNobody);

        [Test]
        [TestDescription("a radius stand dresses within its ring, past its room, and not beyond the ring")]
        internal IEnumerator RadiusStandDressesWithinItsRing()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.RadiusStandDressesWithinItsRing);

        [Test]
        [TestDescription("a room stand in the open serves no outdoor recreation, a radius stand does")]
        internal IEnumerator RadiusServesOutdoorRecreation()
            => Run(StageWithDoorway, HarnessCatchments.RadiusServesOutdoorRecreation);

        [Test]
        [TestDescription("a room stand in the open serves no work either, and the alert lists it")]
        internal IEnumerator RoomStandInTheOpenServesNothing()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.RoomStandInTheOpenServesNothing);

        [Test]
        [TestDescription("an owned radius stand beats a nearer shared room stand")]
        internal IEnumerator OwnedRadiusStandBeatsANearerSharedOne()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.OwnedRadiusStandBeatsANearerSharedOne);

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

        [Test]
        [TestDescription("an area stand's latch holds beside the stand and for work in its area")]
        internal IEnumerator AreaLatchHoldsBesideItsStand()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.AreaLatchHoldsBesideItsStand);

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
        [TestDescription("a room stand beside a door serves its own room, and work in the doorway dresses nobody")]
        internal IEnumerator RoomStandBesideADoorServesItsOwnRoom()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.RoomStandBesideADoorServesItsOwnRoom);

        [Test]
        [TestDescription("an urgent patient on shift goes straight to bed, untagged and behind a haul too")]
        internal IEnumerator UrgentPatientGoesStraightToBed()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.UrgentPatientGoesStraightToBed);

        [Test]
        [TestDescription("the catchment registry forgets a stand that is no longer spawned")]
        internal IEnumerator RegistryForgetsAStandNoLongerSpawned()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessCatchments.RegistryForgetsAStandNoLongerSpawned);

        [Test]
        [TestDescription("the Serves row, the inspect pane and the switch's tooltip follow the catchment")]
        internal IEnumerator CatchmentWordsFollowTheMode()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.CatchmentWordsFollowTheMode);
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Areas)]
    internal class HarnessAreas : HarnessFixture
    {
        [Test]
        [TestDescription("an area covers its painted cells while the stand stands in one, and nothing else")]
        internal IEnumerator AreaCoversItsCells()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessCatchments.AreaCoversItsCells);

        [Test]
        [TestDescription("an area stand dresses for work painted in, past its room, and not elsewhere")]
        internal IEnumerator AreaStandDressesWithinItsArea()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.AreaStandDressesWithinItsArea);

        [Test]
        [TestDescription("an area stand dresses for a delivery whose steel and every site are painted in, and for no other")]
        internal IEnumerator AreaStandDressesForADeliveryItHolds()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Construction")), HarnessCatchments.AreaStandDressesForADeliveryItHolds);

        [Test]
        [TestDescription("a bedroom stand moved to an area keeps its work list, and a work type replaces sleeping")]
        internal IEnumerator BedroomStandMovedToAnAreaKeepsItsWorkList()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessCatchments.BedroomStandMovedToAnAreaKeepsItsWorkList);

        [Test]
        [TestDescription("a deleted area leaves its stand serving nothing, and a namesake is not adopted")]
        internal IEnumerator DeletedAreaServesNobody()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.DeletedAreaServesNobody);

        [Test]
        [TestDescription("a gravship landing rebinds the area it carried, with nobody on shift, and no unrelated namesake")]
        internal IEnumerator GravshipLandingRebindsTheArea()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.GravshipLandingRebindsTheArea);

        [Test]
        [TestDescription("the alert lists a stand whose area is gone or that stands outside it")]
        internal IEnumerator ServesNothingAlertListsTheStand()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.ServesNothingAlertListsTheStand);

        [Test]
        [TestDescription("the Serves menu offers the allowed areas but Home, marks one leaving the stand out, and binds the one picked")]
        internal IEnumerator ServesMenuOffersTheAllowedAreas()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessCatchments.ServesMenuOffersTheAllowedAreas);

        [Test]
        [TestDescription("New allowed area copies the stand's room, is named for its work, and greys at vanilla's cap")]
        internal IEnumerator NewAllowedAreaCopiesTheRoom()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.NewAllowedAreaCopiesTheRoom);

        [Test]
        [TestDescription("Paint and Manage areas close the settings window before vanilla's tools open")]
        internal IEnumerator PaintAndManageAreasCloseTheWindowFirst()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessCatchments.PaintAndManageAreasCloseTheWindowFirst);

        [Test]
        [TestDescription("an area stand's row, inspect pane and switch name the area, and say when it serves nothing")]
        internal IEnumerator AreaWordsNameTheArea()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessCatchments.AreaWordsNameTheArea);

        [Test]
        [TestDescription("a solid target is judged at its own cell, then the floor beside it")]
        internal IEnumerator SolidTargetIsJudgedAtItsOwnCellFirst()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Mining")), HarnessCatchments.SolidTargetIsJudgedAtItsOwnCellFirst);

        [Test]
        [TestDescription("a stand outside the colonist's allowed area dresses nobody")]
        internal IEnumerator StandOutsideTheAreaDressesNobody()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true, capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")), HarnessGates.StandOutsideTheAreaDressesNobody);

        [Test]
        [TestDescription("nor do the recreation and sleep arms send anyone to it")]
        internal IEnumerator StandOutsideTheAreaServesNoRecreationOrSleep()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessGates.StandOutsideTheAreaServesNoRecreationOrSleep);

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
        [TestDescription("the stand's switch names what it serves, recreation and sleep included")]
        internal IEnumerator SwitchNamesWhatTheStandServes()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessGizmos.SwitchNamesWhatTheStandServes);

        [Test]
        [TestDescription("selecting several stands shows each stand's own buttons")]
        internal IEnumerator StandButtonsStaySeparate()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessGizmos.StandButtonsStaySeparate);

        [Test]
        [TestDescription("hovering a stand's button marks that stand and no other")]
        internal IEnumerator HoveredButtonsMarkTheirOwnStand()
            => Run((m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HarnessGizmos.HoveredButtonsMarkTheirOwnStand);

        [Test]
        [TestDescription("fishing dresses at a stand ticked for it, and at a radius stand by the shore")]
        internal IEnumerator FishingDressesAtAStandTickedForIt()
            => Run((m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Fishing")), HarnessTriggers.FishingDressesAtAStandTickedForIt);

        // A pair that only means anything together: the refusal that stops a checked-out pawn
        // re-arming from the spare pile, and the release that lets them shop once their parked
        // kit is worn out.
        [Test]
        [TestDescription("a checked-out pawn refuses a spare that duplicates their parked kit")]
        internal IEnumerator ParkedKitRefusesSpares() => Run(HarnessScoring.ParkedKitRefusesSpares);

        [Test]
        [TestDescription("but takes one once the parked kit is worn through")]
        internal IEnumerator WornOutParkedKitStillGoesShopping()
            => Run(HarnessScoring.WornOutParkedKitStillGoesShopping);

        // A known gap unless Ideology is active.
        [Test]
        [TestDescription("a recolor order goes through on shift, the optimizer's recolor does not, and none reaches a parked garment")]
        internal IEnumerator RecolorOrderGoesThroughOnShift()
            => Run(HarnessRecolor.RecolorOrderGoesThroughOnShift);

        // These two read DevTools' map and the pad, and stage no fixture.
        [Test]
        [TestDescription("a work target with no room of its own still resolves one")]
        internal IEnumerator SolidTargetResolvesARoom()
            => RunOnPad(() => HarnessTables.SolidTargetResolvesARoom(Map, Pad));

        [Test]
        [TestDescription("a work target in an exterior wall resolves the room from either side")]
        internal IEnumerator ExteriorWallTargetResolvesTheRoom()
            => RunOnPad(() => HarnessTables.ExteriorWallTargetResolvesTheRoom(Map, Pad));
    }

    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.Tables)]
    internal class HarnessTableCases : HarnessFixture
    {
        [Test]
        [TestDescription("the recreation job classifier holds")]
        internal IEnumerator RecreationClassifierHolds()
            => Run(HarnessTriggers.RecreationClassifierHolds);

        [Test]
        [TestDescription("the sleep job classifier holds")]
        internal IEnumerator RestClassifierHolds() => Run(HarnessTriggers.RestClassifierHolds);

        [Test]
        [TestDescription("the room-role table resolves")]
        internal IEnumerator RoomRoleTableResolves() => Run(HarnessTables.RoomRoleTableResolves);

        [Test]
        [TestDescription("each hospital and barn patient giver's work is served there")]
        internal IEnumerator PatientWorkIsServedWherePatientsLie()
            => Run(HarnessTables.PatientWorkIsServedWherePatientsLie);

        [Test]
        [TestDescription("the job-target and ignored-giver tables hold")]
        internal IEnumerator JobTablesHold() => Run(HarnessTables.JobTablesHold);

        [Test]
        [TestDescription("the pass-through tags and the incidental job list hold")]
        internal IEnumerator PassThroughAndIncidentalTablesHold()
            => Run(HarnessCatchments.PassThroughAndIncidentalTablesHold);

        [Test]
        [TestDescription("the room-contents markers hold")]
        internal IEnumerator ContentsMarkersHold() => Run(HarnessTables.ContentsMarkersHold);

        [Test]
        [TestDescription("the work-type dialog never hands its listing a short rect")]
        internal IEnumerator WorkTypeDialogBodyRectFitsTheBody()
            => Run(HarnessTables.WorkTypeDialogBodyRectFitsTheBody);
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
