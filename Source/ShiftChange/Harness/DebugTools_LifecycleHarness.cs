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
// Case bodies live in sibling types under Harness/, imported statically so the case list in Cases
// reads as plain method names. That list is the order authority.
using static ShiftChange.HarnessCatchments;
using static ShiftChange.HarnessFixtures;
using static ShiftChange.HarnessGates;
using static ShiftChange.HarnessInterop;
using static ShiftChange.HarnessLifecycle;
using static ShiftChange.HarnessGizmos;
using static ShiftChange.HarnessOwnership;
using static ShiftChange.HarnessRecolor;
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
    /// tally. <see cref="Cases"/> lists the cases, in the order <see cref="Run"/> runs them.
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
        /// <param name="map">The map every case stages on.</param>
        /// <param name="origin">The pad's south-west corner: the clicked cell, or the cell the
        /// auto-run's finder chose.</param>
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
            // The pad goes in the header, so a failure that depends on the map can be traced to it.
            IntVec3 centre = map.Center - new IntVec3(PadSize / 2, 0, PadSize / 2);
            Report.Append("[ShiftChange] lifecycle harness, pad at (").Append(origin.x).Append(", ")
                  .Append(origin.z).AppendLine(origin == centre ? "), the map centre" : "), not the map centre");

            foreach (HarnessCase harnessCase in Cases())
            {
                harnessCase.Run(map, pad);
            }

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

        /// <summary>
        /// Every case, in the order <see cref="Run"/> runs them. Each names the group that is its
        /// DevTools fixture (HarnessDevTools.cs); the auto-run ignores the groups.
        /// </summary>
        internal static CaseList Cases()
        {
            CaseList cases = new CaseList();
            cases.Group = CaseGroup.Flights;
            // First: every case below takes for granted what this checks.
            cases.On("the fixture colonist is healthy and holds still under the stopped clock, and ground past the pad is ground open to the map edge",
                     (map, pad, name) => Case(map, pad, name, fix => HarnessFixtureChecks.FixtureColonistHoldsStill(fix, pad)));
            cases.On("gravship flight keeps the ledger, and the shift ends normally after landing",
                     GravshipFlight);
            cases.On("gravship flight without the borrower releases it", GravshipFlightLeftBehind);
            // Flights that leave the borrower and the stand on two maps, the second a pocket map
            // (HarnessFixtures.SecondMap).
            cases.On("a stand that flies without its borrower keeps the shift, and ends it normally when they meet again",
                     StandFliesWithoutItsBorrower);
            cases.On("a borrower who flies without their stand keeps the shift, and ends it normally on coming back",
                     BorrowerFliesWithoutTheirStand);
            // The next two build on the second map, so the fixture's Map is the pocket one and the
            // body is handed the harness's map and pad.
            cases.On("a stand lost with the map it was left on stops pinning its borrower's uniform",
                     (map, pad, name) => Case(map, pad, name, BuildOnSecondMap, fix => StandLostWithItsMap(fix, map, pad)));
            cases.On("a boxed stand lost with its map releases its ledger too",
                     (map, pad, name) => Case(map, pad, name, BuildOnSecondMap, fix => BoxedStandLostWithItsMap(fix, map, pad)));
            cases.Group = CaseGroup.VacuumGate;
            // The vacuum gate's ground: the second map is a vacuum map; the first case checks it.
            cases.On("a room on the second map, a vacuum map, holds air while the open floor past its door does not",
                     (map, pad, name) => Case(name, () => HarnessVacuum.VacuumMapStages(map)));
            cases.On("a bill fetching through vacuum keeps a crafter in their vacsuit, where one inside dresses them",
                     HarnessVacuum.StageShip, HarnessVacuum.IngredientInVacuumKeepsTheirGear);
            cases.On("a bill fetching from a safe room only vacuum leads to keeps them in their vacsuit too",
                     HarnessVacuum.StageShip, HarnessVacuum.SafeRoomAcrossVacuumKeepsTheirGear);
            cases.On("on a ship whose rooms all connect through air, the same bill dresses them",
                     HarnessVacuum.StageConnectedShip, HarnessVacuum.ConnectedShipChangesNothing);
            cases.On("a crafter in uniform changes back before a bill through vacuum, and not before one inside",
                     HarnessVacuum.StageShip, HarnessVacuum.OnShiftChangesBackBeforeVacuum);
            cases.On("a crafter with no vacuum protection to lose is dressed as before",
                     HarnessVacuum.StageShip, HarnessVacuum.UnprotectedCrafterIsUnaffected);
            cases.On("an order through vacuum runs as given, and only a warning says so",
                     HarnessVacuum.StageShip, HarnessVacuum.OrderIntoVacuumRunsAsGiven);
            cases.On("an emergency through vacuum sends a colonist in uniform to change first",
                     HarnessVacuum.StageShip, HarnessVacuum.EmergencyIntoVacuumChangesBackFirst);
            cases.On("off a vacuum map, a crafter in a vacsuit is dressed for a bill fetching from outside, as before",
                     HarnessVacuum.StageEnclosedCrafter, HarnessVacuum.PlanetMapChangesNothing);
            cases.On("a stand holding a vacsuit dresses an unsuited crafter for a bill through vacuum",
                     HarnessVacuum.StageShip, HarnessVacuum.VacsuitStandStillDresses);
            cases.Group = CaseGroup.VacuumRoutes;
            cases.On("a vacsuit stand across the room wins over a nearer one that would take the suit off",
                     HarnessVacuum.StageShip, HarnessVacuum.ProtectiveStandWinsOverANearerOne);
            cases.On("an area stand painted over open floor does not dress for a delivery from out there",
                     HarnessVacuum.StageShip, HarnessVacuum.AreaDeliveryAcrossVacuumKeepsTheirGear);
            cases.On("a freed stand does not catch up a crafter on a job fetching through vacuum",
                     HarnessVacuum.StageShip, HarnessVacuum.FreedStandSkipsAJobThroughVacuum);
            cases.On("an errand ahead of the stand's work rides along in uniform, but not into vacuum",
                     HarnessVacuum.StageConnectedShip, HarnessVacuum.ErrandAheadOfWorkChangesBackBeforeVacuum);
            cases.On("the air walk does not pass a door forbidden to the colonist",
                     HarnessVacuum.StageConnectedShip, HarnessVacuum.ForbiddenDoorIsNoWayThrough);
            cases.On("a colonist cut off from their stand by vacuum is not sent across it to change",
                     HarnessVacuum.StageShip, HarnessVacuum.StrandedColonistIsNotSentAcrossToChange);
            cases.On("a cooldown does not wave a colonist in uniform into vacuum",
                     HarnessVacuum.StageShip, HarnessVacuum.CooldownDoesNotWaveThemIntoVacuum);
            cases.On("an urgent patient goes straight to a bed across vacuum",
                     HarnessVacuum.StageShip, HarnessVacuum.UrgentPatientGoesStraightToBedAcrossVacuum);
            cases.On("a colonist whose stand is on another map is warned before vacuum",
                     HarnessVacuum.StageCrafterAtHome, HarnessVacuum.StandOnAnotherMapStillWarns);
            cases.Group = CaseGroup.Lifecycle;
            cases.On("every stand def asks to hear when its map is removed", StandDefsHearMapRemoval);
            cases.On("teardown (deconstruct, burn) releases the ledger", TeardownReleases);
            cases.On("reinstalling mid-shift keeps the ledger and the return trip",
                     ReinstallKeepsTheLedger);
            cases.On("reinstalling a stand keeps its owner, mode and flag",
                     ReinstallKeepsConfiguration);
            cases.On("an owner who dies while their stand is boxed does not own it again",
                     DeadOwnerNotRestoredOnReinstall);
            cases.On("an owner kidnapped while their stand is boxed neither owns it nor "
                     + "keeps their shift there",
                     KidnappedOwnerNotRestoredOnReinstall);
            cases.On("borrower death reaps the ledger", DeathReaps);
            cases.On("borrower banishment reaps the ledger", BanishmentReaps);
            cases.Group = CaseGroup.FaultLatch;
            cases.On("repeated faults disable interception, a load re-arms it", FaultLatchRecovers);
            cases.Group = CaseGroup.DriverAndOwners;
            cases.On("a stand that displaces nothing still hands the uniform back",
                     (m, p) => Stage(m, p, StageKit.NonDisplacing), NonDisplacingReturns);
            cases.On("the driver returns own clothes and their forced flags",
                     (m, p) => Stage(m, p, StageKit.Displacing), DriverRoundTrip);
            cases.On("work and recreation are mutually exclusive on a stand",
                     (m, p) => Stage(m, p, StageKit.Displacing), WorkAndRecreationAreExclusive);
            cases.On("the removal flag is held off while a stand is in service",
                     RemovalFlagHeldOffInService);
            cases.On("the change-back gate hands the chain back untouched",
                     ChangeBackGateHandsTheChainBack);
            cases.On("and still appends the button for a pawn in uniform",
                     ChangeBackGateStillAppends);
            cases.On("the removal-toggle gate leaves a stand it does not govern alone",
                     RemovalToggleGateLeavesForeignStandsAlone);
            cases.On("an owner list restricts the stand to its owners",
                     (m, p) => Stage(m, p, StageKit.Displacing), OwnerListRestricts);
            cases.On("the owner dialog's gender filter offers the right candidates",
                     (m, p) => Stage(m, p, StageKit.Displacing), OwnerFilterOffersTheRightPawns);
            cases.On("one owner is copied into another mod's owner list, a shared stand is not",
                     StageWithForeignAssignable, ForeignOwnersCopyOurs);
            cases.On("an older save's two owner lists reconcile to the one the player could see",
                     StageWithForeignAssignable, ReconcileKeepsTheVisibleList);
            cases.On("a group stand keeps its owners out of shift use, however it gets there",
                     (m, p) => Stage(m, p, StageKit.Displacing), GroupSurvivesLeavingShiftUse);
            cases.On("out of shift use one pick replaces a kept group and reaches the other mod's list",
                     StageWithForeignAssignable, PickReplacesAKeptGroup);
            cases.On("a kept group reaches nobody through the copy and survives a move",
                     StageWithForeignAssignable, KeptGroupSurvivesAMove);
            // A known gap unless Outfit Stands Plus is loaded (--with=).
            cases.On("Outfit Stands Plus' stand button offers what its own walk would, in order",
                     (m, p) => Stage(m, p, StageKit.Displacing), OutfitStandsPlusButtonMatchesTheirs);
            cases.Group = CaseGroup.Arms;
            cases.On("a joy job in the stand's room dresses",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     JoyJobInTheRoomDresses);
            cases.On("going to bed in the stand's room dresses, medical bed rest does not",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     SleepJobInTheRoomDresses);
            cases.On("a stand ticked for bed rest dresses a patient, but never an urgent one",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     MedicalBedRestDressesAtAGownStand);
            cases.On("the medical emergency setting covers doctors and patients, never fires",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     MedicalEmergencySettingCoversTheRightWork);
            cases.On("the new-stand setting decides what a stand made from then on starts as",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     NewStandSettingDecidesOnlyNewStands);
            cases.On("the job classifier sorts every job shape in the arms' order",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     HarnessJobKinds.ClassifierSortsEveryJobShape);
            cases.On("a deposit-only stand parks what its filter accepts and hands it back",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     DepositOnlyParksAndReturns);
            cases.On("and haulers leave it alone, while an ordinary stand still takes deliveries",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     DepositOnlyStandIsNoHaulTarget);
            cases.On("full change swaps the whole outfit and gives it all back",
                     (m, p) => Stage(m, p, StageKit.Displacing), FullChangeSwapsEverything);
            cases.On("full change refuses to strip a colonist bare",
                     (m, p) => Stage(m, p, StageKit.Displacing),
                     FullChangeRefusesToStripThemBare);
            cases.On("a legs-only stand leaves a man decent (control for the pair)",
                     (m, p) => Stage(m, p, StageKit.Displacing, gender: Gender.Male),
                     LegsOnlyStandLeavesAManDecent);
            cases.On("and the same stand keeps a woman's shirt on, because her rule wants it",
                     (m, p) => Stage(m, p, StageKit.Displacing, gender: Gender.Female),
                     LegsOnlyStandStripsAWoman);
            cases.On("a nudist is left undressed, because that is the point",
                     (m, p) => Stage(m, p, StageKit.Displacing), NudistIsExemptFromDecency);
            cases.On("the decency guard ships off, and turning it on takes effect",
                     (m, p) => Stage(m, p, StageKit.Displacing), DecencyGuardIsOptIn);
            cases.Group = CaseGroup.Gates;
            cases.On("the rules the description promises hold",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                     capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     PromisesHold);
            cases.On("a meal break gets them out of uniform first",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), MealBreakChangesOut);
            cases.On("and a sleepwear meal break does too, even with the meal in inventory",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     SleepwearMealBreakChangesOut);
            cases.On("under threat they may not change in, but may change back",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                     capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     DangerGateIsOneDirectional);
            cases.On("a pawn under a lord duty is left alone, and released when it ends",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                     capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     DutyGateShutsBothArms);
            cases.On("a job handed to a pawn mid-swap is not deferred on top of it",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                     capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     MidSwapJobIsNotDeferredAgain);
            cases.On("a freed stand catches up a colonist working bare",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                     capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     FreedStandCatchesUp);
            cases.On("Change back holds the room until they leave it, and drafting drops it",
                     (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     ChangeBackLatchHoldsUntilTheyLeave);
            cases.Group = CaseGroup.Radius;
            cases.On("a radius covers its ring, inclusive, and room mode its room",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), RadiusCoversItsRing);
            cases.On("a stand leaves room mode only with something ticked, and comes back with nothing",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), CatchmentNeedsSomethingTicked);
            cases.On("a freed radius stand interrupts nobody, a room stand still does",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                     capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     FreedRadiusStandCatchesNobody);
            cases.On("a radius stand dresses within its ring, past its room, and not beyond the ring",
                     (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     RadiusStandDressesWithinItsRing);
            cases.On("a room stand in the open serves no outdoor recreation, a radius stand does",
                     StageWithDoorway, RadiusServesOutdoorRecreation);
            cases.On("a room stand in the open serves no work either, and the alert lists it",
                     (m, p) => Stage(m, p, StageKit.Displacing,
                                     capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     RoomStandInTheOpenServesNothing);
            cases.On("an owned radius stand beats a nearer shared room stand",
                     (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     OwnedRadiusStandBeatsANearerSharedOne);
            cases.On("a radius work stand keeps the uniform on for its work, pass-through work and idling, and nothing else",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     RadiusWorkStandKeepsTheUniformInItsRing);
            cases.On("pass-through work is judged at both ends, and an errand ahead of the stand's work rides along",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     PassThroughWorkIsJudgedAtBothEnds);
            cases.Group = CaseGroup.BillProducts;
            // The stonecutting bill's own work: Crafting, or Stone Cut with Complex Jobs.
            WorkTypeDef billWork =
                DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsStonecut")?.workType;
            cases.On("a bill's product hauled to a stockpile in the area keeps the uniform on",
                     (m, p) => StageWithDoorway(m, p, billWork),
                     f => BillProductHaul(f, CatchmentKind.Area, true, true, true));
            cases.On("a bill's product hauled to a stockpile in the ring keeps the uniform on",
                     (m, p) => StageWithDoorway(m, p, billWork),
                     f => BillProductHaul(f, CatchmentKind.Radius, true, true, true));
            cases.On("the same with Hauling not ticked, because the bill's work is what the stand serves",
                     (m, p) => StageWithDoorway(m, p, billWork),
                     f => BillProductHaul(f, CatchmentKind.Area, false, true, true));
            cases.On("a bill's product bound for a stockpile past the area changes them back first",
                     (m, p) => StageWithDoorway(m, p, billWork),
                     f => BillProductHaul(f, CatchmentKind.Area, true, false, false));
            cases.On("a bill's product bound for a stockpile past the ring changes them back first",
                     (m, p) => StageWithDoorway(m, p, billWork),
                     f => BillProductHaul(f, CatchmentKind.Radius, true, false, false));
            cases.On("the same with Hauling not ticked",
                     (m, p) => StageWithDoorway(m, p, billWork),
                     f => BillProductHaul(f, CatchmentKind.Area, false, false, false));
            cases.On("a room stand keeps the uniform on for a bill's product bound for a stockpile in the room",
                     (m, p) => StageWithDoorway(m, p, billWork),
                     f => BillProductHaul(f, CatchmentKind.Room, true, true, true));
            cases.On("and for one bound past its door, as it always has",
                     (m, p) => StageWithDoorway(m, p, billWork),
                     f => BillProductHaul(f, CatchmentKind.Room, true, false, true));
            cases.On("a bill's product is judged at the stockpile, not the crafter's unpainted cell",
                     (m, p) => StageWithDoorway(m, p, billWork),
                     BillProductIsJudgedAtTheStockpile);
            cases.On("a bill's product goes by the ordinary rule once the stand stops serving the bill",
                     (m, p) => StageWithDoorway(m, p, billWork),
                     BillProductOfUnservedWorkChangesThemBack);
            cases.On("a haul that starts with the stack on the ground is no bill's product",
                     (m, p) => StageWithDoorway(m, p, billWork),
                     BillProductMustBeInHand);
            cases.On("the same stack in hand after other served work is no bill's product",
                     (m, p) => StageWithDoorway(m, p, billWork),
                     BillProductMustComeFromABill);
            cases.On("a crafter off shift is not dressed to store what they made",
                     (m, p) => StageWithDoorway(m, p, billWork),
                     BillProductDressesNobody);
            cases.Group = CaseGroup.Latches;
            cases.On("a radius sleep stand changes them back on the first job out of bed",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     RadiusSleepStandKeepsItOnlyForBed);
            cases.On("a radius recreation stand keeps the outfit on for play and idling, not for hauling",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     RadiusRecreationStandKeepsItForPlay);
            cases.On("the job a colonist was just dressed for never changes them back, though its target walks away",
                     (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     JustDressedForNeverSendsThemBack);
            cases.On("the change-back latch holds its stand and trigger only, until they leave the ring",
                     (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     LatchHoldsItsStandAndTriggerOnly);
            cases.On("at a sleep rack the latch holds through the night and lifts on waking",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     SleepRackLatchLiftsOnWaking);
            cases.On("an area stand's latch holds beside the stand and for work in its area",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                     capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     AreaLatchHoldsBesideItsStand);
            // A known gap unless Complex Jobs is loaded (--with=).
            cases.On("a delivery dresses nobody at a room or radius stand, keeps its own work's uniform on, and the site does",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     DeliveriesKeepTheirOwnWorkOn);
            cases.On("a delivery outside the stand's work changes pyjamas and whites back first",
                     (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Construction")),
                     DeliveryOutsideTheStandsWorkChangesThemBack);
            cases.On("a room stand beside a door serves its own room, and work in the doorway dresses nobody",
                     (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     RoomStandBesideADoorServesItsOwnRoom);
            cases.On("an urgent patient on shift goes straight to bed, untagged and behind a haul too",
                     (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     UrgentPatientGoesStraightToBed);
            cases.On("the catchment registry forgets a stand that is no longer spawned",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     RegistryForgetsAStandNoLongerSpawned);
            cases.On("the Serves row, the inspect pane and the switch's tooltip follow the catchment",
                     (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     CatchmentWordsFollowTheMode);
            cases.Group = CaseGroup.Areas;
            cases.On("an area covers its painted cells while the stand stands in one, and nothing else",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), AreaCoversItsCells);
            cases.On("an area stand dresses for work painted in, past its room, and not elsewhere",
                     (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     AreaStandDressesWithinItsArea);
            cases.On("an area stand dresses for a delivery whose steel and every site are painted in, and for no other",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                     capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Construction")),
                     AreaStandDressesForADeliveryItHolds);
            cases.On("a bedroom stand moved to an area keeps its work list, and a work type replaces sleeping",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), BedroomStandMovedToAnAreaKeepsItsWorkList);
            cases.On("a deleted area leaves its stand serving nothing, and a namesake is not adopted",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                     capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     DeletedAreaServesNobody);
            cases.On("a gravship landing rebinds the area it carried, with nobody on shift, and no unrelated namesake",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                     capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     GravshipLandingRebindsTheArea);
            cases.On("the alert lists a stand whose area is gone or that stands outside it",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                     capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     ServesNothingAlertListsTheStand);
            cases.On("the Serves menu offers the allowed areas but Home, marks one leaving the stand out, and binds the one picked",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), ServesMenuOffersTheAllowedAreas);
            cases.On("New allowed area copies the stand's room, is named for its work, and greys at vanilla's cap",
                     (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     NewAllowedAreaCopiesTheRoom);
            cases.On("Paint and Manage areas close the settings window before vanilla's tools open",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), PaintAndManageAreasCloseTheWindowFirst);
            cases.On("an area stand's row, inspect pane and switch name the area, and say when it serves nothing",
                     (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     AreaWordsNameTheArea);
            cases.On("a solid target is judged at its own cell, then the floor beside it",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                     capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Mining")),
                     SolidTargetIsJudgedAtItsOwnCellFirst);
            cases.On("a stand outside the colonist's allowed area dresses nobody",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                     capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     StandOutsideTheAreaDressesNobody);
            cases.On("nor do the recreation and sleep arms send anyone to it",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     StandOutsideTheAreaServesNoRecreationOrSleep);
            cases.On("a colonist whose stand is outside their area keeps the outfit on, and the stand says why",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                     capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     StandOutsideTheAreaKeepsTheOutfitOn);
            cases.Group = CaseGroup.RideAlong;
            cases.On("feeding a patient or a prisoner dresses nobody, and keeps a doctor's or a warden's uniform on",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), FeedingRidesAlong);
            cases.On("a meal run keeps the uniform on only for a patient inside, and so does a haul slipped in ahead of it",
                     (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                     MealRunKeepsTheUniformForAPatientInside);
            cases.On("an errand queued ahead of work the stand serves keeps the uniform on",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                     DetourAheadOfServedWorkKeepsTheUniform);
            cases.On("and so does vanilla's own opportunistic haul on the way to a bill",
                     (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Hauling")),
                     VanillaOpportunisticHaulKeepsTheUniform);
            // Known gaps unless the mod is loaded (--with=).
            cases.On("Common Sense's bill haul keeps a cook in uniform",
                     StageWithDoorway, CommonSenseBillHaulKeepsTheUniform);
            cases.On("Pick Up And Haul's follow-up jobs keep the uniform on",
                     StageWithDoorway, PickUpAndHaulFollowUpsKeepTheUniform);
            cases.Group = CaseGroup.Assorted;
            cases.On("the stand's switch names what it serves, recreation and sleep included",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), SwitchNamesWhatTheStandServes);
            cases.On("selecting several stands shows each stand's own buttons",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), StandButtonsStaySeparate);
            cases.On("hovering a stand's button marks that stand and no other",
                     (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), HoveredButtonsMarkTheirOwnStand);
            cases.On("fishing dresses at a stand ticked for it, and at a radius stand by the shore",
                     (m, p) => StageWithDoorway(m, p, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Fishing")),
                     FishingDressesAtAStandTickedForIt);
            // A pair that only means anything together: the refusal that stops a checked-out pawn
            // re-arming from the spare pile, and the release that lets them shop once their parked
            // kit is worn out.
            cases.On("a checked-out pawn refuses a spare that duplicates their parked kit",
                     ParkedKitRefusesSpares);
            cases.On("but takes one once the parked kit is worn through",
                     WornOutParkedKitStillGoesShopping);
            // A known gap unless Ideology is active.
            cases.On("a recolor order goes through on shift, the optimizer's recolor does not, and none reaches a parked garment",
                     RecolorOrderGoesThroughOnShift);
            // These two need a live map and no fixture, so they sit after the fixture cases and
            // before the game-replacing ones below.
            cases.On("a work target with no room of its own still resolves one",
                     (map, pad, name) => Case(name, () => SolidTargetResolvesARoom(map, pad)));
            cases.On("a work target in an exterior wall resolves the room from either side",
                     (map, pad, name) => Case(name, () => ExteriorWallTargetResolvesTheRoom(map, pad)));

            cases.Group = CaseGroup.SaveLoad;
            // Last among the map cases: these replace Current.Game, so `map`, `pad` and every
            // fixture above belong to a disposed game once they have run. They register through the
            // fixture-less overload for the same reason, since Teardown would clear a pad on the
            // disposed map. See DebugTools_SaveRoundTrip.
            cases.On("a save/load round trip keeps the owner and the ledger, and sweeps the removal flag",
                     (map, pad, name) => Case(name, () => DebugTools_SaveRoundTrip.RoundTrip(map, pad)));
            cases.On("a legacy-key save gets its owner back and re-saves it prefixed",
                     DebugTools_SaveRoundTrip.LegacyMigration);
            cases.On("an older save's foreign owner round-trips without contest and is adopted",
                     DebugTools_SaveRoundTrip.ForeignAssignable);
            cases.On("an area binding survives a save under a new name, and a deleted area's is dropped",
                     DebugTools_SaveRoundTrip.AreaRoundTrip);

            cases.Group = CaseGroup.Tables;
            // These need no map, so the game replacement above does not touch them.
            cases.On("the recreation job classifier holds", RecreationClassifierHolds);
            cases.On("the sleep job classifier holds", RestClassifierHolds);
            cases.On("the room-role table resolves", RoomRoleTableResolves);
            cases.On("each feeding giver's work is served where its patients lie",
                     FeedingWorkIsServedWherePatientsLie);
            cases.On("the job-target and ignored-giver tables hold", JobTablesHold);
            cases.On("the pass-through tags and the incidental job list hold", PassThroughAndIncidentalTablesHold);
            cases.On("the room-contents markers hold", ContentsMarkersHold);
            cases.On("the work-type dialog never hands its listing a short rect",
                     WorkTypeDialogBodyRectFitsTheBody);
            // Last by convention: it restores the tallies, the gap flag and the transcript around
            // its deliberate failures, so its position does not change the result.
            cases.On("the harness counts its own results correctly", HarnessAccounting);
            return cases;
        }

        /// <summary>The cases of one group, in run order.</summary>
        internal static IEnumerable<HarnessCase> CasesIn(CaseGroup group)
        {
            return Cases().Where(harnessCase => harnessCase.Group == group);
        }

        /// <summary>
        /// The groups <see cref="Cases"/> falls into, in run order; each is one DevTools fixture.
        /// DevTools keeps a fixture's tests in the order given only up to 16 of them, so no group
        /// holds more. A DevTools test, <c>HarnessCaseList</c>, fails when one does, or when
        /// DevTools would not run a group's cases once each, in this order.
        /// </summary>
        internal enum CaseGroup
        {
            Flights,
            VacuumGate,
            VacuumRoutes,
            Lifecycle,
            FaultLatch,
            DriverAndOwners,
            Arms,
            Gates,
            Radius,
            BillProducts,
            Latches,
            Areas,
            RideAlong,
            Assorted,
            SaveLoad,
            Tables,
        }

        /// <summary>
        /// One case of <see cref="Cases"/>: its name, its group, and its <c>Case</c> call.
        /// </summary>
        internal sealed class HarnessCase
        {
            internal readonly CaseGroup Group;
            internal readonly string Name;
            internal readonly Action<Map, CellRect, string> Call;

            /// <summary>
            /// False for a case added by <c>On(name, Func&lt;bool&gt;)</c>, which is handed no map
            /// or pad; two save/load legs find a pad of their own.
            /// </summary>
            internal readonly bool NeedsPad;

            internal HarnessCase(CaseGroup group, string name, Action<Map, CellRect, string> run,
                                 bool needsPad)
            {
                Group = group;
                Name = name;
                Call = run;
                NeedsPad = needsPad;
            }

            internal void Run(Map map, CellRect pad)
            {
                Call(map, pad, Name);
            }

            /// <summary>The name, which DevTools puts in the test's own.</summary>
            public override string ToString()
            {
                return Name;
            }
        }

        /// <summary>
        /// Builds <see cref="Cases"/>. Each <c>On</c> overload makes the <c>Case</c> call of the
        /// same shape, and a case whose body needs the harness's map or pad is handed them with
        /// its name.
        /// </summary>
        internal sealed class CaseList : List<HarnessCase>
        {
            /// <summary>The group of the cases added from here on.</summary>
            internal CaseGroup Group;

            internal void On(string name, Action<Map, CellRect, string> run)
            {
                Add(new HarnessCase(Group, name, run, true));
            }

            internal void On(string name, Func<Fixture, bool> body)
            {
                On(name, (map, pad, caseName) => Case(map, pad, caseName, body));
            }

            internal void On(string name, Func<Map, CellRect, Fixture> stage, Func<Fixture, bool> body)
            {
                On(name, (map, pad, caseName) => Case(map, pad, caseName, stage, body));
            }

            internal void On(string name, Func<bool> body)
            {
                Add(new HarnessCase(Group, name, (map, pad, caseName) => Case(caseName, body), false));
            }
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
