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
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
// The case bodies live in sibling TYPES under Harness/ (split 2026-09-15).
// Imported statically so the registration list in Run() below reads as plain
// method names — that list is the ORDER authority and has to stay legible.
using static ShiftChange.HarnessFixtures;
using static ShiftChange.HarnessGates;
using static ShiftChange.HarnessLifecycle;
using static ShiftChange.HarnessOwnership;
using static ShiftChange.HarnessSwap;
using static ShiftChange.HarnessTables;
using static ShiftChange.HarnessTriggers;

namespace ShiftChange
{
    /// <summary>
    /// Dev mode → Shift Change → Run lifecycle harness, then click a clear map
    /// cell. Builds a throwaway stand-and-borrower fixture, fires one real
    /// engine lifecycle event at it, checks the ledger landed where it should,
    /// tears the fixture down, and repeats for every event we make a claim
    /// about. Results go to the dev log; a toast gives the tally.
    ///
    /// <para><b>Why this exists.</b> Every lifecycle claim in this mod was
    /// previously verified by arranging the situation in a live colony. Some of
    /// those situations cannot reasonably be arranged: a gravship launch needs
    /// substructure, fuel, thrusters and a pilot console before the game will
    /// let one leave the ground, and the thing we need to observe takes one
    /// tick in the middle of it. That cost meant the gravship path shipped
    /// reasoned-about rather than tested, and it was wrong (the ledger was
    /// destroyed on a flight the stand was designed to survive).</para>
    ///
    /// <para><b>What it does and does not prove.</b> Each case splits in two.
    /// The ENGINE half — "a gravship despawns aboard-things with
    /// <c>WillReplace</c>" — is a static fact about the engine, settled by
    /// reading <c>GravshipUtility.cs:389</c>, and no amount of play makes it
    /// truer. The OUR half — "given that call, our ledger survives" — is what
    /// this harness drives. It is the half that has actually been wrong.</para>
    ///
    /// <para><b>The one rule that keeps it honest: call the engine's own entry
    /// points.</b> Every case below goes through <c>Thing.DeSpawn</c>,
    /// <c>GenSpawn.Spawn</c>, <c>Pawn.Kill</c>, <c>PawnBanishUtility.Banish</c>
    /// — never our <c>PostDeSpawn</c> directly. The engine's comp dispatch,
    /// its ordering and its own side effects then run for real, and only the
    /// ORCHESTRATION around them is simulated. A harness that hand-rolls the
    /// call sequence tests the author's model of the engine and certifies
    /// whatever that model got wrong.</para>
    ///
    /// <para><b>Two kinds of fixture.</b> The lifecycle cases hand-assemble a
    /// checked-out state (<see cref="Build"/> moves apparel and calls
    /// <c>NotifyDressed</c>) because what they test is what happens to a
    /// ledger AFTERWARDS. The driver cases stage an undressed pawn
    /// (<see cref="Stage"/>) and run <see cref="JobDriver_SwapAtStand"/> for
    /// real through the pawn's own tracker (<see cref="RunSwap"/>), because
    /// what they test is whether the driver builds that ledger correctly in
    /// the first place — which nothing covered until 2026-08-14, and which is
    /// where B1 lived.</para>
    ///
    /// <para><b>Two engine traps the driver cases had to pay for</b>, both
    /// invisible from the API surface and both recorded at their call sites:
    /// pathfinding in 1.6 is ASYNCHRONOUS, so ticking one pawn never completes
    /// a walk (hence staging on the interaction cell); and Jobs are POOLED, so
    /// a Job reference held across its own completion silently becomes the
    /// pawn's next job (hence watching the driver, not the job).</para>
    ///
    /// <para>Save/load round trips run in-process through the engine's
    /// synchronous loader, as the last map cases, since they replace the whole
    /// game — see <see cref="DebugTools_SaveRoundTrip"/>. Still out of reach: a
    /// real gravship launch.</para>
    ///
    /// <para><b>What ships: none of this (2026-09-17).</b> The whole harness
    /// is behind <c>#if HARNESS</c>, which a plain Release build does not
    /// define, so neither this body nor <c>-shiftchange-harness</c> exists in
    /// a player's install. <c>run-harness.sh</c> asks for them with
    /// <c>-p:Harness=true</c> on top of Release codegen, and sweeps
    /// <c>Assemblies/</c> back to the shipping dll when it is done. The
    /// <c>[DebugAction]</c> wrapper remains SCENES only on top of that.
    ///
    /// Two earlier rationales sat here and are both superseded. The first —
    /// "ships in Release, dev-mode gated, like the other two debug tools" —
    /// was wrong because the debug actions menu is a surface players genuinely
    /// use. The second — "the BODY ships, because a gate is only worth running
    /// if it asserts against the literal dll players install" — was right
    /// about what it wanted and paid for it in the wrong currency. The thing
    /// it was protecting is that no shipping code path differs between the
    /// build under test and the build that goes out, and that is now a rule
    /// with a check behind it: HARNESS is only ever a whole-file guard
    /// (<c>check-invariants.py</c>), so the two builds differ by the presence
    /// of these types and by nothing else.
    ///
    /// What survives from both: a test you have to switch build
    /// configurations to run is a test that stops being run. Nobody switches
    /// configurations here either — <c>run-harness.sh</c> passes the property
    /// itself, and the command is the same one it always was.</para>
    ///
    /// <para><b>First run, 2026-08-14</b>, clean Release restart on the ~100-mod
    /// profile, quicktest map: 4 passed, 0 failed, 1 known gap. The gravship
    /// case passed on all four assertions, which is what the
    /// <c>WillReplace</c> guard in <see cref="CompShiftStand.PostDeSpawn"/> was
    /// written for and had until then only been reasoned about. Later the same
    /// day, with the banishment gap closed and the driver, functional and
    /// meta cases added: <b>13 passed, 0 failed, 0 known gaps</b>. Every bug
    /// fixed that day now has a guard, and six of the rules the store
    /// description promises players are asserted against the code.</para>
    /// </summary>
    internal static class DebugTools_LifecycleHarness
    {
        /// <summary>Cleared and rebuilt per case, so no case inherits another's mess.</summary>
        internal const int PadSize = 7;

        internal static readonly StringBuilder Report = new StringBuilder();
        internal static int Passed;
        internal static int Failed;
        internal static int KnownGaps;

#if SCENES
        /// <summary>
        /// The hand-driven entry point, SCENES only — so it is absent from a
        /// shipped build's debug menu. Reached from the "Dev tools..." submenu
        /// (<see cref="DebugTools_Menu"/>), which carries the game-state and
        /// Odyssey gating.
        ///
        /// <see cref="Run"/> below is the shared body, reached from here on a
        /// dev build and from <c>-shiftchange-harness</c> under
        /// <c>run-harness.sh</c>. Neither door exists in a shipping build.
        ///
        /// The menu entry is what cannot ship: this is a <c>ToolMap</c> action
        /// with no confirmation, and on a live colony it clears its 7×7 pad 22
        /// times over — destroying buildings and stock outright and vanishing
        /// any pawn standing there, gear and all, with no corpse and no letter.
        /// </summary>
        internal static void RunHarness()
        {
            Run(Find.CurrentMap, UI.MouseCell(), toast: true);
        }
#endif

        /// <summary>
        /// The whole harness, for both entry points — the debug action above
        /// and the <c>-shiftchange-harness</c> auto-run in
        /// <see cref="Patch_HarnessAutoRun"/>. One body, because a headless run
        /// that exercised a different path from the one a human clicks would be
        /// worth very little.
        /// </summary>
        /// <param name="toast">
        /// Player-facing messages for the interactive run; log lines for the
        /// headless one, where nobody is watching the screen and a toast on a
        /// map about to be torn down goes nowhere.
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

            Case(map, pad, "gravship flight keeps the ledger", GravshipFlight);
            Case(map, pad, "gravship flight without the borrower releases it", GravshipFlightLeftBehind);
            Case(map, pad, "teardown (deconstruct, burn) releases the ledger", TeardownReleases);
            Case(map, pad, "reinstalling mid-shift keeps the ledger and the return trip",
                 ReinstallKeepsTheLedger);
            Case(map, pad, "reinstalling a stand keeps its owner, mode and flag",
                 ReinstallKeepsConfiguration);
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
            Case(map, pad, "a joy job in the stand's room dresses",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 JoyJobInTheRoomDresses);
            Case(map, pad, "going to bed in the stand's room dresses, medical bed rest does not",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true),
                 SleepJobInTheRoomDresses);
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
            Case(map, pad, "a freed stand catches up a colonist working bare",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 FreedStandCatchesUp);
            // Needs a live map and touches no fixture, so it sits at the end of
            // the map cases and before the game-replacing ones below.
            Case("a work target with no room of its own still resolves one",
                 () => SolidTargetResolvesARoom(map, pad));
            Case("a work target in an exterior wall resolves the room from either side",
                 () => ExteriorWallTargetResolvesTheRoom(map, pad));

            // Last among the map cases: these replace Current.Game, so `map`,
            // `pad` and every fixture above them belong to a disposed game once
            // they have run. They register through the fixture-less overload
            // for the same reason — Teardown would clear a pad on the disposed
            // map. See DebugTools_SaveRoundTrip.
            Case("a save/load round trip keeps the owner and the ledger, and sweeps the removal flag",
                 () => DebugTools_SaveRoundTrip.RoundTrip(map, pad));
            Case("a legacy-key save migrates its owner and re-saves prefixed",
                 DebugTools_SaveRoundTrip.LegacyMigration);
            Case("a foreign assignable's owner round-trips without contest",
                 DebugTools_SaveRoundTrip.ForeignAssignable);

            // The two below need no map, so they are unaffected by the game
            // replacement above.
            Case("the recreation job classifier holds", RecreationClassifierHolds);
            Case("the sleep job classifier holds", RestClassifierHolds);
            Case("the room-role table resolves", RoomRoleTableResolves);
            Case("the job-target and ignored-giver tables hold", JobTablesHold);
            Case("the room-contents markers hold", ContentsMarkersHold);
            Case("the work-type dialog never hands its listing a short rect",
                 WorkTypeDialogBodyRectFitsTheBody);
            // Last: it drives a deliberately failing assertion, and the tallies
            // are global.
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

        /// <summary>
        /// A refusal, addressed to whoever is actually there to read it.
        /// </summary>
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

        // ------------------------------------------------------------ cases

        /// <summary>
        /// The harness checks itself. Register LAST — it deliberately drives a
        /// failing assertion, and the tallies are global.
        ///
        /// Without this, <c>run-harness.sh</c> greps the log for PASSED and
        /// exits zero on a harness whose <see cref="Expect"/> has been inverted
        /// or whose counters have stopped moving — and the cases above are
        /// trusted on nothing.
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

            // Put the counters and the transcript back before reporting, so the
            // three deliberate assertions above do not reach the tally or the
            // log the author reads.
            Passed = passedBefore;
            Failed = failedBefore;
            KnownGaps = gapsBefore;
            GapThisCase = gapFlagBefore;
            Report.Length = 0;
            Report.Append(saved);

            // Expect REPORTS; Case COUNTS. A case with ten failing assertions
            // is one failure, not ten — deliberate, so the tally counts
            // behaviours rather than sentences. Asserted explicitly because
            // the first version of this self-check assumed the opposite and
            // was wrong about the harness it was checking.
            return Expect(truePassed, "Expect(true) returns true")
                 & Expect(!falsePassed, "Expect(false) returns false")
                 & Expect(failedDelta == 0 && passedDelta == 0,
                          "assertions do not touch the tally — Case does")
                 & Expect(gapReturned, "a known gap returns true, so it is not a failure")
                 & Expect(gapsDelta == 1, "and increments KnownGaps exactly once")
                 & Expect(GapThisCase == gapFlagBefore, "the gap flag was restored");
        }

        /// <summary>One case's throwaway world: a stocked stand and a borrower wearing its uniform.</summary>
        internal class Fixture
        {
            internal Map Map;
            internal Building_OutfitStand Stand;
            internal Pawn Pawn;
            internal CompShiftStand Comp;
            internal int StoredCount;

            /// <summary>Extra pawns a case spawned; swept by <see cref="Teardown"/>.</summary>
            internal List<Pawn> Extras = new List<Pawn>();
        }

        /// <summary>
        /// What a staged fixture wears and what its stand holds. Named CORE
        /// defs throughout, never the demo stage's defaults: the harness runs
        /// on a four-mod profile by default and on the development list under
        /// <c>--full</c>, and a fixture built from Vanilla Apparel Expanded
        /// defs would silently become a different test between the two.
        /// </summary>
        internal enum StageKit
        {
            /// <summary>
            /// Stand: a duster (Shell). Pawn: shirt and trousers (OnSkin) plus
            /// a parka (Shell) — so the duster displaces the parka and the
            /// ledger has something in it.
            /// </summary>
            Displacing,

            /// <summary>
            /// The same without the parka, so the duster (Shell) shares no
            /// layer with shirt and trousers (OnSkin) and displaces NOTHING.
            /// This is B1's shape, and <see cref="Build"/> structurally cannot
            /// produce it — it returns null when nothing is displaced.
            /// </summary>
            NonDisplacing,
        }

        /// <summary>
        /// Fixture builds are retried, because spawning a freshly generated
        /// pawn is not reliable on a large mod list and the failure is not
        /// ours. Observed 2026-08-14: `Pawn_HealthTracker.Notify_Spawned`
        /// threw `Collection was modified` under seven third-party postfixes
        /// on `Pawn.SpawnSetup` (VEF shields, Athena, LightsOut and others),
        /// for one pawn roll out of two runs. A gate that fails at random gets
        /// ignored, so retry — but PRINT every retry, because silently
        /// swallowing them would hide a genuine intermittent bug of our own.
        /// </summary>
        internal const int BuildAttempts = 3;

        /// <summary>Set by <see cref="ExpectKnownGap"/>, read by <see cref="Case"/>.</summary>
        internal static bool GapThisCase;

        internal static void Case(Map map, CellRect pad, string name, Func<Fixture, bool> body)
        {
            Case(map, pad, name, Build, body);
        }

        /// <summary>
        /// A case with no fixture at all — for anything that asserts about
        /// static tables or the harness's own machinery and needs no map.
        /// </summary>
        internal static void Case(string name, Func<bool> body)
        {
            Report.Append("  ").AppendLine(name);
            GapThisCase = false;
            // MOD SETTINGS ARE GLOBAL AND CASES RUN IN SEQUENCE. A case that
            // needs the decency guard in a particular state sets it and does
            // not clean up; this is the cleanup, so a leak cannot silently
            // decide the outcome of every case after it. Snapshot-and-restore
            // rather than force-to-default: the harness must not fight a
            // player's own configuration on a manual run either.
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
        /// A case that stages its own fixture. <paramref name="stage"/> returns
        /// null to mean "this fixture cannot be built", which is a failure —
        /// the retry loop is for THROWS, which are a modlist hazard rather
        /// than ours (see <see cref="BuildAttempts"/>).
        /// </summary>
        internal static void Case(Map map, CellRect pad, string name,
                                  Func<Map, CellRect, Fixture> stage, Func<Fixture, bool> body)
        {
            Report.Append("  ").AppendLine(name);
            GapThisCase = false;
            Fixture fix = null;
            // MOD SETTINGS ARE GLOBAL AND CASES RUN IN SEQUENCE. A case that
            // needs the decency guard in a particular state sets it and does
            // not clean up; this is the cleanup, so a leak cannot silently
            // decide the outcome of every case after it. Snapshot-and-restore
            // rather than force-to-default: the harness must not fight a
            // player's own configuration on a manual run either.
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
                    // A case carrying a known gap is counted once, as a gap.
                    // Counting it as a pass as well overstated the tally.
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
        /// An assertion we expect to fail, with the reason recorded. Counted
        /// separately so a green run stays meaningful — and so that the day it
        /// starts passing, the harness says so instead of staying quiet.
        ///
        /// Nothing calls this today: banishment was the last gap and it is
        /// closed. Kept because the counting semantics are fiddly enough to get
        /// wrong twice, and the next gap will want them.
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
