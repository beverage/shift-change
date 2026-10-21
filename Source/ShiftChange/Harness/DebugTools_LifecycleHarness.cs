// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
#if HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
// Case bodies live in sibling types under Harness/, imported statically so the registration list in
// Run reads as plain method names. That list is the order authority.
using static ShiftChange.HarnessCatchments;
using static ShiftChange.HarnessFixtures;
using static ShiftChange.HarnessGates;
using static ShiftChange.HarnessInterop;
using static ShiftChange.HarnessLifecycle;
using static ShiftChange.HarnessGizmos;
using static ShiftChange.HarnessOwnership;
using static ShiftChange.HarnessRideAlong;
using static ShiftChange.HarnessScoring;
using static ShiftChange.HarnessSwap;
using static ShiftChange.HarnessTables;
using static ShiftChange.HarnessTriggers;

namespace ShiftChange
{
    /// <summary>
    /// The regression suite's runner. Most cases build a throwaway fixture, drive it, check the
    /// outcome and tear it down; a lifecycle case fires one real engine event at a checked-out
    /// stand and checks where the ledger lands. Results go to the dev log, and a toast gives the
    /// tally. <see cref="Run"/> registers the cases, and its list is the order they run in.
    ///
    /// <para>Each lifecycle case splits in two. The engine's half is read from the game's code and
    /// taken as given: a gravship flight, say, despawns what is aboard with <c>WillReplace</c>. The
    /// harness drives our half: given that call, where the ledger lands.</para>
    ///
    /// <para>Fire lifecycle events through the engine's own entry points, never our comp
    /// handlers directly, so the engine's comp dispatch, its ordering and its side effects run
    /// for real and only the orchestration around them is simulated. docs/TESTING.md, "Rules the
    /// suite follows".</para>
    ///
    /// <para>Two kinds of fixture. Lifecycle cases hand-assemble a checked-out state
    /// (<see cref="Build"/>) to test what happens to a ledger afterwards. Driver cases stage an
    /// undressed pawn (<see cref="Stage"/>) and run <see cref="JobDriver_SwapAtStand"/> for real
    /// through the pawn's own tracker (<see cref="RunSwap"/>), to test whether the driver builds
    /// that ledger.</para>
    ///
    /// <para>Four engine traps shape the fixtures (docs/TESTING.md, "Four engine traps").
    /// Pathfinding is asynchronous, so a pawn is staged on the stand's interaction cell. Jobs are
    /// pooled, so the pump watches the driver, not the job. The clock stands still, so the fixture
    /// hands out only colonists whose needs do not tick while it is stopped. A generated colonist
    /// is not tended, so the fixture removes what it arrives needing tended.</para>
    /// </summary>
    internal static class DebugTools_LifecycleHarness
    {
        /// <summary>Side of the pad every case is handed, cleared and rebuilt per case.</summary>
        internal const int PadSize = 7;

        internal static readonly StringBuilder Report = new StringBuilder();
        internal static int Passed;
        internal static int Failed;
        internal static int KnownGaps;

#if SCENES
        /// <summary>
        /// The hand-driven entry point, SCENES only: a <c>ToolMap</c> action with no confirmation.
        /// Cases clear their pad with <c>GenDebug.ClearArea</c>, and some clear past it: a one-cell
        /// margin, the strip west of the pad and single cells further out, the last two reset to
        /// soil. ClearArea destroys buildings and items and vanishes any pawn there, gear and all,
        /// with no corpse and no letter. Reached from the Dev tools submenu
        /// (<see cref="DebugTools_Menu"/>); <see cref="Run"/> is the shared body.
        /// </summary>
        internal static void RunHarness()
        {
            Run(Find.CurrentMap, UI.MouseCell(), toast: true);
        }
#endif

        /// <summary>
        /// Runs every case and reports. The one body behind the debug action and the
        /// <c>-shiftchange-harness</c> auto-run (<see cref="Patch_HarnessAutoRun"/>), so a headless
        /// run exercises the path a human click does.
        /// </summary>
        /// <param name="toast">
        /// True for the interactive run: a refusal goes on screen rather than to the log, and the
        /// tally to both. False for the headless one, where nobody watches the screen: the log
        /// alone.
        /// </param>
        /// <returns>true when nothing failed. Known gaps do not count as failures.</returns>
        internal static bool Run(Map map, IntVec3 origin, bool toast)
        {
            if (map == null)
            {
                Complain("no current map — cannot run the harness.", toast);
                return false;
            }
            CellRect pad = new CellRect(origin.x, origin.z, PadSize, PadSize);
            if (!pad.InBounds(map))
            {
                Complain("Lifecycle harness needs a clear " + PadSize + "×" + PadSize
                    + " area — click further from the map edge.", toast);
                return false;
            }
            if (DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand") == null)
            {
                Complain("Odyssey outfit stand def not found — cannot run the harness.", toast);
                return false;
            }

            Report.Length = 0;
            Passed = 0;
            Failed = 0;
            KnownGaps = 0;
            Report.AppendLine("[ShiftChange] lifecycle harness");

            // First: every case below takes for granted what this checks.
            Case(map, pad, "the fixture colonist is healthy and holds still under the stopped clock, and ground past the pad is ground",
                 HarnessFixtureChecks.FixtureColonistHoldsStill);
            Case(map, pad, "gravship flight keeps the ledger, and the shift ends normally after landing",
                 GravshipFlight);
            Case(map, pad, "gravship flight without the borrower releases it", GravshipFlightLeftBehind);
            // Flights that leave the borrower and the stand on two maps, the second a pocket map
            // (HarnessFixtures.SecondMap).
            Case(map, pad, "a stand that flies without its borrower keeps the shift, and ends it normally when they meet again",
                 StandFliesWithoutItsBorrower);
            Case(map, pad, "a borrower who flies without their stand keeps the shift, and ends it normally on coming back",
                 BorrowerFliesWithoutTheirStand);
            // The next two build on the second map, so the fixture's Map is the pocket one and the
            // body is handed the harness's map and pad.
            Case(map, pad, "a stand lost with the map it was left on stops pinning its borrower's uniform",
                 BuildOnSecondMap, fix => StandLostWithItsMap(fix, map, pad));
            Case(map, pad, "a boxed stand lost with its map releases its ledger too",
                 BuildOnSecondMap, fix => BoxedStandLostWithItsMap(fix, map, pad));
            // The vacuum gate's ground: the second map is a vacuum map; the first case checks it.
            Case("a room on the second map, a vacuum map, holds air while the open floor past its door does not",
                 () => HarnessVacuum.VacuumMapStages(map));
            Case(map, pad, "a bill fetching through vacuum keeps a crafter in their vacsuit, where one inside dresses them",
                 HarnessVacuum.StageShip, HarnessVacuum.IngredientInVacuumKeepsTheirGear);
            Case(map, pad, "a bill fetching from a safe room only vacuum leads to keeps them in their vacsuit too",
                 HarnessVacuum.StageShip, HarnessVacuum.SafeRoomAcrossVacuumKeepsTheirGear);
            Case(map, pad, "on a ship whose rooms all connect through air, the same bill dresses them",
                 HarnessVacuum.StageConnectedShip, HarnessVacuum.ConnectedShipChangesNothing);
            Case(map, pad, "a crafter in uniform changes back before a bill through vacuum, and not before one inside",
                 HarnessVacuum.StageShip, HarnessVacuum.OnShiftChangesBackBeforeVacuum);
            Case(map, pad, "a crafter with no vacuum protection to lose is dressed as before",
                 HarnessVacuum.StageShip, HarnessVacuum.UnprotectedCrafterIsUnaffected);
            Case(map, pad, "an order through vacuum runs as given, and only a warning says so",
                 HarnessVacuum.StageShip, HarnessVacuum.OrderIntoVacuumRunsAsGiven);
            Case(map, pad, "an emergency through vacuum sends a colonist in uniform to change first",
                 HarnessVacuum.StageShip, HarnessVacuum.EmergencyIntoVacuumChangesBackFirst);
            Case(map, pad, "off a vacuum map, a crafter in a vacsuit is dressed for a bill fetching from outside, as before",
                 HarnessVacuum.StageEnclosedCrafter, HarnessVacuum.PlanetMapChangesNothing);
            Case(map, pad, "a stand holding a vacsuit dresses an unsuited crafter for a bill through vacuum",
                 HarnessVacuum.StageShip, HarnessVacuum.VacsuitStandStillDresses);
            Case(map, pad, "a vacsuit stand across the room wins over a nearer one that would take the suit off",
                 HarnessVacuum.StageShip, HarnessVacuum.ProtectiveStandWinsOverANearerOne);
            Case(map, pad, "an area stand painted over open floor does not dress for a delivery from out there",
                 HarnessVacuum.StageShip, HarnessVacuum.AreaDeliveryAcrossVacuumKeepsTheirGear);
            Case(map, pad, "a freed stand does not catch up a crafter on a job fetching through vacuum",
                 HarnessVacuum.StageShip, HarnessVacuum.FreedStandSkipsAJobThroughVacuum);
            Case(map, pad, "an errand ahead of the stand's work rides along in uniform, but not into vacuum",
                 HarnessVacuum.StageConnectedShip, HarnessVacuum.ErrandAheadOfWorkChangesBackBeforeVacuum);
            Case(map, pad, "the air walk does not pass a door forbidden to the colonist",
                 HarnessVacuum.StageConnectedShip, HarnessVacuum.ForbiddenDoorIsNoWayThrough);
            Case(map, pad, "a colonist cut off from their stand by vacuum is not sent across it to change",
                 HarnessVacuum.StageShip, HarnessVacuum.StrandedColonistIsNotSentAcrossToChange);
            Case(map, pad, "a cooldown does not wave a colonist in uniform into vacuum",
                 HarnessVacuum.StageShip, HarnessVacuum.CooldownDoesNotWaveThemIntoVacuum);
            Case(map, pad, "an urgent patient goes straight to a bed across vacuum",
                 HarnessVacuum.StageShip, HarnessVacuum.UrgentPatientGoesStraightToBedAcrossVacuum);
            Case(map, pad, "a colonist whose stand is on another map is warned before vacuum",
                 HarnessVacuum.StageCrafterAtHome, HarnessVacuum.StandOnAnotherMapStillWarns);
            Case("every stand def asks to hear when its map is removed", StandDefsHearMapRemoval);
            Case(map, pad, "teardown (deconstruct, burn) releases the ledger", TeardownReleases);
            Case(map, pad, "reinstalling mid-shift keeps the ledger and the return trip",
                 ReinstallKeepsTheLedger);
            Case(map, pad, "reinstalling a stand keeps its owner, mode and flag",
                 ReinstallKeepsConfiguration);
            Case(map, pad, "an owner who dies while their stand is boxed does not own it again",
                 DeadOwnerNotRestoredOnReinstall);
            Case(map, pad, "borrower death reaps the ledger", DeathReaps);
            Case(map, pad, "borrower banishment reaps the ledger", BanishmentReaps);
            Case(map, pad, "repeated faults disable interception, a load re-arms it", FaultLatchRecovers);
            Case(map, pad, "a stand that displaces nothing still hands the uniform back",
                 (m, p) => Stage(m, p, StageKit.NonDisplacing), NonDisplacingReturns);
            Case(map, pad, "the driver returns own clothes and their forced flags",
                 (m, p) => Stage(m, p, StageKit.Displacing), DriverRoundTrip);
            Case(map, pad, "work and recreation are mutually exclusive on a stand",
                 (m, p) => Stage(m, p, StageKit.Displacing), WorkAndRecreationAreExclusive);
            Case(map, pad, "the removal flag is held off while a stand is in service",
                 RemovalFlagHeldOffInService);
            Case(map, pad, "the change-back gate hands the chain back untouched",
                 ChangeBackGateHandsTheChainBack);
            Case(map, pad, "and still appends the button for a pawn in uniform",
                 ChangeBackGateStillAppends);
            Case(map, pad, "the removal-toggle gate leaves a stand it does not govern alone",
                 RemovalToggleGateLeavesForeignStandsAlone);
            Case(map, pad, "an owner list restricts the stand to its owners",
                 (m, p) => Stage(m, p, StageKit.Displacing), OwnerListRestricts);
            Case(map, pad, "the owner dialog's gender filter offers the right candidates",
                 (m, p) => Stage(m, p, StageKit.Displacing), OwnerFilterOffersTheRightPawns);
            Case(map, pad, "one owner is copied into another mod's owner list, a shared stand is not",
                 StageWithForeignAssignable, ForeignOwnersCopyOurs);
            Case(map, pad, "an older save's two owner lists reconcile to the one the player could see",
                 StageWithForeignAssignable, ReconcileKeepsTheVisibleList);
            Case(map, pad, "a group stand keeps its owners out of shift use, however it gets there",
                 (m, p) => Stage(m, p, StageKit.Displacing), GroupSurvivesLeavingShiftUse);
            Case(map, pad, "out of shift use one pick replaces a kept group and reaches the other mod's list",
                 StageWithForeignAssignable, PickReplacesAKeptGroup);
            Case(map, pad, "a kept group reaches nobody through the copy and survives a move",
                 StageWithForeignAssignable, KeptGroupSurvivesAMove);
            // A known gap unless Outfit Stands Plus is loaded (--with=).
            Case(map, pad, "Outfit Stands Plus' stand button offers what its own walk would, in order",
                 (m, p) => Stage(m, p, StageKit.Displacing), OutfitStandsPlusButtonMatchesTheirs);
            Case(map, pad, "a joy job in the stand's room dresses",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 JoyJobInTheRoomDresses);
            Case(map, pad, "going to bed in the stand's room dresses, medical bed rest does not",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 SleepJobInTheRoomDresses);
            Case(map, pad, "a stand ticked for bed rest dresses a patient, but never an urgent one",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 MedicalBedRestDressesAtAGownStand);
            Case(map, pad, "the medical emergency setting covers doctors and patients, never fires",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 MedicalEmergencySettingCoversTheRightWork);
            Case(map, pad, "the job classifier sorts every job shape in the arms' order",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 HarnessJobKinds.ClassifierSortsEveryJobShape);
            Case(map, pad, "a deposit-only stand parks what its filter accepts and hands it back",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 DepositOnlyParksAndReturns);
            Case(map, pad, "and haulers leave it alone, while an ordinary stand still takes deliveries",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 DepositOnlyStandIsNoHaulTarget);
            Case(map, pad, "full change swaps the whole outfit and gives it all back",
                 (m, p) => Stage(m, p, StageKit.Displacing), FullChangeSwapsEverything);
            Case(map, pad, "full change refuses to strip a colonist bare",
                 (m, p) => Stage(m, p, StageKit.Displacing),
                 FullChangeRefusesToStripThemBare);
            Case(map, pad, "a legs-only stand leaves a man decent (control for the pair)",
                 (m, p) => Stage(m, p, StageKit.Displacing, gender: Gender.Male),
                 LegsOnlyStandLeavesAManDecent);
            Case(map, pad, "and the same stand keeps a woman's shirt on, because her rule wants it",
                 (m, p) => Stage(m, p, StageKit.Displacing, gender: Gender.Female),
                 LegsOnlyStandStripsAWoman);
            Case(map, pad, "a nudist is left undressed, because that is the point",
                 (m, p) => Stage(m, p, StageKit.Displacing), NudistIsExemptFromDecency);
            Case(map, pad, "the decency guard ships off, and turning it on takes effect",
                 (m, p) => Stage(m, p, StageKit.Displacing), DecencyGuardIsOptIn);
            Case(map, pad, "the rules the description promises hold",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 PromisesHold);
            Case(map, pad, "a meal break gets them out of uniform first",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), MealBreakChangesOut);
            Case(map, pad, "and a sleepwear meal break does too, even with the meal in inventory",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 SleepwearMealBreakChangesOut);
            Case(map, pad, "under threat they may not change in, but may change back",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 DangerGateIsOneDirectional);
            Case(map, pad, "a pawn under a lord duty is left alone, and released when it ends",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 DutyGateShutsBothArms);
            Case(map, pad, "a job handed to a pawn mid-swap is not deferred on top of it",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 MidSwapJobIsNotDeferredAgain);
            Case(map, pad, "a freed stand catches up a colonist working bare",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 FreedStandCatchesUp);
            Case(map, pad, "Change back holds the room until they leave it, and drafting drops it",
                 (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 ChangeBackLatchHoldsUntilTheyLeave);
            Case(map, pad, "a radius covers its ring, inclusive, and room mode its room",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), RadiusCoversItsRing);
            Case(map, pad, "a stand leaves room mode only with something ticked, and comes back with nothing",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), CatchmentNeedsSomethingTicked);
            Case(map, pad, "a freed radius stand interrupts nobody, a room stand still does",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 FreedRadiusStandCatchesNobody);
            Case(map, pad, "a radius stand dresses within its ring, past its room, and not beyond the ring",
                 (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 RadiusStandDressesWithinItsRing);
            Case(map, pad, "a room stand in the open serves no outdoor recreation, a radius stand does",
                 StageWithDoorway, RadiusServesOutdoorRecreation);
            Case(map, pad, "a room stand in the open serves no work either, and the alert lists it",
                 (m, p) => Stage(m, p, StageKit.Displacing,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 RoomStandInTheOpenServesNothing);
            Case(map, pad, "an owned radius stand beats a nearer shared room stand",
                 (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 OwnedRadiusStandBeatsANearerSharedOne);
            Case(map, pad, "a radius work stand keeps the uniform on for its work, pass-through work and idling, and nothing else",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 RadiusWorkStandKeepsTheUniformInItsRing);
            Case(map, pad, "pass-through work is judged at both ends, and an errand ahead of the stand's work rides along",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 PassThroughWorkIsJudgedAtBothEnds);
            Case(map, pad, "a radius sleep stand changes them back on the first job out of bed",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 RadiusSleepStandKeepsItOnlyForBed);
            Case(map, pad, "a radius recreation stand keeps the outfit on for play and idling, not for hauling",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 RadiusRecreationStandKeepsItForPlay);
            Case(map, pad, "the job a colonist was just dressed for never changes them back, though its target walks away",
                 (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 JustDressedForNeverSendsThemBack);
            Case(map, pad, "the change-back latch holds its stand and trigger only, until they leave the ring",
                 (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 LatchHoldsItsStandAndTriggerOnly);
            Case(map, pad, "at a sleep rack the latch holds through the night and lifts on waking",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 SleepRackLatchLiftsOnWaking);
            Case(map, pad, "an area stand's latch holds beside the stand and for work in its area",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 AreaLatchHoldsBesideItsStand);
            // A known gap unless Complex Jobs is loaded (--with=).
            Case(map, pad, "a delivery dresses nobody at a room or radius stand, keeps its own work's uniform on, and the site does",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 DeliveriesKeepTheirOwnWorkOn);
            Case(map, pad, "a delivery outside the stand's work changes pyjamas and whites back first",
                 (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Construction")),
                 DeliveryOutsideTheStandsWorkChangesThemBack);
            Case(map, pad, "a room stand beside a door serves its own room, and work in the doorway dresses nobody",
                 (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 RoomStandBesideADoorServesItsOwnRoom);
            Case(map, pad, "an urgent patient on shift goes straight to bed, untagged and behind a haul too",
                 (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 UrgentPatientGoesStraightToBed);
            Case(map, pad, "the catchment registry forgets a stand that is no longer spawned",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 RegistryForgetsAStandNoLongerSpawned);
            Case(map, pad, "the Serves row, the inspect pane and the switch's tooltip follow the catchment",
                 (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 CatchmentWordsFollowTheMode);
            Case(map, pad, "an area covers its painted cells while the stand stands in one, and nothing else",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), AreaCoversItsCells);
            Case(map, pad, "an area stand dresses for work painted in, past its room, and not elsewhere",
                 (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 AreaStandDressesWithinItsArea);
            Case(map, pad, "an area stand dresses for a delivery whose steel and every site are painted in, and for no other",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Construction")),
                 AreaStandDressesForADeliveryItHolds);
            Case(map, pad, "a bedroom stand moved to an area keeps its work list, and a work type replaces sleeping",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), BedroomStandMovedToAnAreaKeepsItsWorkList);
            Case(map, pad, "a deleted area leaves its stand serving nothing, and a namesake is not adopted",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 DeletedAreaServesNobody);
            Case(map, pad, "a gravship landing rebinds the area it carried, with nobody on shift, and no unrelated namesake",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 GravshipLandingRebindsTheArea);
            Case(map, pad, "the alert lists a stand whose area is gone or that stands outside it",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 ServesNothingAlertListsTheStand);
            Case(map, pad, "the Serves menu offers the allowed areas but Home, marks one leaving the stand out, and binds the one picked",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), ServesMenuOffersTheAllowedAreas);
            Case(map, pad, "New allowed area copies the stand's room, is named for its work, and greys at vanilla's cap",
                 (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 NewAllowedAreaCopiesTheRoom);
            Case(map, pad, "Paint and Manage areas close the settings window before vanilla's tools open",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), PaintAndManageAreasCloseTheWindowFirst);
            Case(map, pad, "an area stand's row, inspect pane and switch name the area, and say when it serves nothing",
                 (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 AreaWordsNameTheArea);
            Case(map, pad, "a solid target is judged at its own cell, then the floor beside it",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Mining")),
                 SolidTargetIsJudgedAtItsOwnCellFirst);
            Case(map, pad, "a stand outside the colonist's allowed area dresses nobody",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 StandOutsideTheAreaDressesNobody);
            Case(map, pad, "nor do the recreation and sleep arms send anyone to it",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 StandOutsideTheAreaServesNoRecreationOrSleep);
            Case(map, pad, "a colonist whose stand is outside their area keeps the outfit on, and the stand says why",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 StandOutsideTheAreaKeepsTheOutfitOn);
            Case(map, pad, "feeding a patient or a prisoner dresses nobody, and keeps a doctor's or a warden's uniform on",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), FeedingRidesAlong);
            Case(map, pad, "a meal run keeps the uniform on only for a patient inside, and so does a haul slipped in ahead of it",
                 (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 MealRunKeepsTheUniformForAPatientInside);
            Case(map, pad, "an errand queued ahead of work the stand serves keeps the uniform on",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 DetourAheadOfServedWorkKeepsTheUniform);
            Case(map, pad, "and so does vanilla's own opportunistic haul on the way to a bill",
                 (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Hauling")),
                 VanillaOpportunisticHaulKeepsTheUniform);
            // Known gaps unless the mod is loaded (--with=).
            Case(map, pad, "Common Sense's bill haul keeps a cook in uniform",
                 StageWithDoorway, CommonSenseBillHaulKeepsTheUniform);
            Case(map, pad, "Pick Up And Haul's follow-up jobs keep the uniform on",
                 StageWithDoorway, PickUpAndHaulFollowUpsKeepTheUniform);
            Case(map, pad, "the stand's switch names what it serves, recreation and sleep included",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), SwitchNamesWhatTheStandServes);
            Case(map, pad, "selecting several stands shows each stand's own buttons",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), StandButtonsStaySeparate);
            Case(map, pad, "fishing dresses at a stand ticked for it, and at a radius stand by the shore",
                 (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Fishing")),
                 FishingDressesAtAStandTickedForIt);
            // A pair that only means anything together: the refusal that stops a checked-out pawn
            // re-arming from the spare pile, and the release that lets them shop once their parked
            // kit is worn out.
            Case(map, pad, "a checked-out pawn refuses a spare that duplicates their parked kit",
                 ParkedKitRefusesSpares);
            Case(map, pad, "but takes one once the parked kit is worn through",
                 WornOutParkedKitStillGoesShopping);
            // These two need a live map and no fixture, so they sit after the fixture cases and
            // before the game-replacing ones below.
            Case("a work target with no room of its own still resolves one",
                 () => SolidTargetResolvesARoom(map, pad));
            Case("a work target in an exterior wall resolves the room from either side",
                 () => ExteriorWallTargetResolvesTheRoom(map, pad));

            // Last among the map cases: these replace Current.Game, so `map`, `pad` and every
            // fixture above belong to a disposed game once they have run. They register through the
            // fixture-less overload for the same reason, since Teardown would clear a pad on the
            // disposed map. See DebugTools_SaveRoundTrip.
            Case("a save/load round trip keeps the owner and the ledger, and sweeps the removal flag",
                 () => DebugTools_SaveRoundTrip.RoundTrip(map, pad));
            Case("a legacy-key save gets its owner back and re-saves it prefixed",
                 DebugTools_SaveRoundTrip.LegacyMigration);
            Case("an older save's foreign owner round-trips without contest and is adopted",
                 DebugTools_SaveRoundTrip.ForeignAssignable);
            Case("an area binding survives a save under a new name, and a deleted area's is dropped",
                 DebugTools_SaveRoundTrip.AreaRoundTrip);

            // These need no map, so the game replacement above does not touch them.
            Case("the recreation job classifier holds", RecreationClassifierHolds);
            Case("the sleep job classifier holds", RestClassifierHolds);
            Case("the room-role table resolves", RoomRoleTableResolves);
            Case("the job-target and ignored-giver tables hold", JobTablesHold);
            Case("the pass-through tags and the incidental job list hold", PassThroughAndIncidentalTablesHold);
            Case("the room-contents markers hold", ContentsMarkersHold);
            Case("the work-type dialog never hands its listing a short rect",
                 WorkTypeDialogBodyRectFitsTheBody);
            // Last by convention: it restores the tallies, the gap flag and the transcript around
            // its deliberate failures, so its position does not change the result.
            Case("the harness counts its own results correctly", HarnessAccounting);

            Report.Append("result: ").Append(Passed).Append(" passed, ")
                  .Append(Failed).Append(" failed, ")
                  .Append(KnownGaps).Append(" known gaps");
            Log.Message(Report.ToString());
            if (toast)
            {
                Messages.Message("Lifecycle harness: " + Passed + " passed, " + Failed + " failed, "
                    + KnownGaps + " known gaps — see the dev log.",
                    Failed > 0 ? MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.TaskCompletion,
                    historical: false);
            }
            return Failed == 0;
        }

        internal static void Complain(string what, bool toast)
        {
            if (toast)
            {
                Messages.Message(what, MessageTypeDefOf.RejectInput, historical: false);
            }
            else
            {
                Log.Error("[ShiftChange] " + what);
            }
        }

        /// <summary>
        /// The harness checks itself. Without it <c>run-harness.sh</c> would pass a harness whose
        /// <see cref="Expect"/> or counters had stopped working.
        /// </summary>
        internal static bool HarnessAccounting()
        {
            int passedBefore = Passed;
            int failedBefore = Failed;
            int gapsBefore = KnownGaps;
            bool gapFlagBefore = GapThisCase;
            StringBuilder saved = new StringBuilder(Report.ToString());

            bool truePassed = Expect(true, "(self-check) a true assertion returns true");
            bool falsePassed = Expect(false, "(self-check) a false assertion returns false");
            bool gapReturned = ExpectKnownGap(false, "(self-check) a gap", "expected");

            int passedDelta = Passed - passedBefore;
            int failedDelta = Failed - failedBefore;
            int gapsDelta = KnownGaps - gapsBefore;

            // Put the counters and the transcript back first, so the three deliberate assertions
            // above reach neither the tally nor the log.
            Passed = passedBefore;
            Failed = failedBefore;
            KnownGaps = gapsBefore;
            GapThisCase = gapFlagBefore;
            Report.Length = 0;
            Report.Append(saved);

            // Expect reports; Case counts. A case with ten failing assertions is one failure, not
            // ten, so the tally counts behaviours rather than sentences.
            return Expect(truePassed, "Expect(true) returns true")
                 & Expect(!falsePassed, "Expect(false) returns false")
                 & Expect(failedDelta == 0 && passedDelta == 0,
                          "assertions do not touch the tally — Case does")
                 & Expect(gapReturned, "a known gap returns true, so it is not a failure")
                 & Expect(gapsDelta == 1, "and increments KnownGaps exactly once")
                 & Expect(GapThisCase == gapFlagBefore, "the gap flag was restored");
        }

        /// <summary>
        /// One case's throwaway world: a stand, the colonist it is staged for, and what a case adds
        /// beyond them. <see cref="Teardown"/> sweeps all of it.
        /// </summary>
        internal class Fixture
        {
            internal Map Map;
            internal Building_OutfitStand Stand;
            internal Pawn Pawn;
            internal CompShiftStand Comp;
            internal int StoredCount;

            internal List<Pawn> Extras = new List<Pawn>();

            /// <summary>Allowed areas a case made; a map holds ten at most.</summary>
            internal List<Area> Areas = new List<Area>();

            /// <summary>
            /// The strip outside the west door that
            /// <see cref="HarnessFixtures.StageWithDoorway(Map, CellRect)"/> cuts, past the pad;
            /// empty for every other fixture.
            /// </summary>
            internal CellRect Outside = CellRect.Empty;

            /// <summary>Cells past the pad that cases put things on (<c>OpenGround</c>).</summary>
            internal List<IntVec3> Claimed = new List<IntVec3>();

            /// <summary>
            /// A second map (<see cref="HarnessFixtures.SecondMap"/>), so a flight can leave the
            /// borrower and their stand apart.
            /// </summary>
            internal Map Pocket;
        }

        /// <summary>
        /// What a staged fixture wears and what its stand holds. Core defs only, never the demo
        /// stage's: the default list is four mods and <c>--full</c> copies the runner's own, and a
        /// fixture built from Vanilla Apparel Expanded defs would be a different test on each.
        /// </summary>
        internal enum StageKit
        {
            /// <summary>
            /// Stand: a duster (Shell). Pawn: shirt and trousers (OnSkin) plus a parka (Shell), so
            /// the duster displaces the parka and the ledger has something in it.
            /// </summary>
            Displacing,

            /// <summary>
            /// The same without the parka, so the duster (Shell) shares no layer with shirt and
            /// trousers (OnSkin) and displaces nothing. <see cref="Build"/> cannot produce this
            /// shape: it returns null when nothing is displaced.
            /// </summary>
            NonDisplacing,
        }

        /// <summary>
        /// Fixture builds are retried because spawning a freshly generated pawn is unreliable on a
        /// large mod list and the failure is not ours: <c>Pawn.SpawnSetup</c> calls
        /// <c>Pawn_HealthTracker.Notify_Spawned</c>, which walks the pawn's hediffs, and a hediff
        /// that adds or removes another there throws <c>Collection was modified</c>. Every retry
        /// is printed, so an intermittent bug of ours still shows.
        /// </summary>
        internal const int BuildAttempts = 3;

        /// <summary>Set by <see cref="ExpectKnownGap"/>, read by <c>Case</c>.</summary>
        internal static bool GapThisCase;

        /// <summary>A case on a checked-out fixture (<see cref="Build"/>).</summary>
        internal static void Case(Map map, CellRect pad, string name, Func<Fixture, bool> body)
        {
            Case(map, pad, name, Build, body);
        }

        /// <summary>
        /// A case with no fixture, for anything that asserts about static tables or the harness's
        /// own machinery and needs no map.
        /// </summary>
        internal static void Case(string name, Func<bool> body)
        {
            Report.Append("  ").AppendLine(name);
            GapThisCase = false;
            // Mod settings are global and cases run in sequence. A case that needs the decency
            // guard in a given state sets it and does not clean up; this restores it, so a leak
            // cannot decide the cases after it. Snapshot and restore rather than reset to the
            // default, so a manual run does not fight the player's own configuration.
            bool decencyBefore = ShiftChangeMod.Settings != null
                                 && ShiftChangeMod.Settings.keepColonistsDecent;
            try
            {
                if (!body())
                {
                    Failed++;
                }
                else if (!GapThisCase)
                {
                    Passed++;
                }
            }
            catch (Exception e)
            {
                Fail("threw: " + e);
            }
            finally
            {
                if (ShiftChangeMod.Settings != null)
                {
                    ShiftChangeMod.Settings.keepColonistsDecent = decencyBefore;
                }
            }
        }

        /// <summary>
        /// A case that stages its own fixture. <paramref name="stage"/> returns null for "this
        /// fixture cannot be built", which fails the case. The retry loop is for throws (see
        /// <see cref="BuildAttempts"/>).
        /// </summary>
        internal static void Case(Map map, CellRect pad, string name,
                                  Func<Map, CellRect, Fixture> stage, Func<Fixture, bool> body)
        {
            Report.Append("  ").AppendLine(name);
            GapThisCase = false;
            Fixture fix = null;
            // Restores the decency setting, as the fixture-less overload does.
            bool decencyBefore = ShiftChangeMod.Settings != null
                                 && ShiftChangeMod.Settings.keepColonistsDecent;
            try
            {
                Exception lastBuildError = null;
                for (int attempt = 1; attempt <= BuildAttempts && fix == null; attempt++)
                {
                    try
                    {
                        fix = stage(map, pad);
                    }
                    catch (Exception e)
                    {
                        lastBuildError = e;
                        Report.Append("    RETRY fixture build threw on attempt ").Append(attempt)
                              .Append(" — ").Append(e.GetType().Name).Append(": ")
                              .AppendLine(e.Message);
                        Teardown(null, map, pad);
                    }
                }
                if (fix == null)
                {
                    Fail(lastBuildError != null
                        ? "fixture build failed " + BuildAttempts + " times, last: " + lastBuildError
                        : "fixture could not be staged");
                    return;
                }

                if (!body(fix))
                {
                    Failed++;
                }
                else if (!GapThisCase)
                {
                    // A case carrying a known gap counts once, as a gap, and not also as a pass.
                    Passed++;
                }
            }
            catch (Exception e)
            {
                Fail("threw: " + e);
            }
            finally
            {
                if (ShiftChangeMod.Settings != null)
                {
                    ShiftChangeMod.Settings.keepColonistsDecent = decencyBefore;
                }
                Teardown(fix, map, pad);
            }
        }

        internal static bool Expect(bool condition, string what)
        {
            Report.Append(condition ? "    PASS  " : "    FAIL  ").AppendLine(what);
            return condition;
        }

        /// <summary>
        /// An assertion expected to fail, with the reason recorded. It prints GAP and counts a
        /// known gap, not a failure, so a green run stays meaningful. When the condition starts to
        /// hold it prints FIXED instead, so the case gets updated.
        /// </summary>
        internal static bool ExpectKnownGap(bool condition, string what, string why)
        {
            if (condition)
            {
                Report.Append("    FIXED ").Append(what)
                      .AppendLine(" — known gap now passes, update the harness");
                return true;
            }
            Report.Append("    GAP   ").Append(what).Append(" — ").AppendLine(why);
            KnownGaps++;
            GapThisCase = true;
            return true;
        }

        internal static void Fail(string why)
        {
            Report.Append("    FAIL  ").AppendLine(why);
            Failed++;
        }
    }
}
#endif
