using System;
using System.Collections.Generic;
using System.Text;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

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
    /// <para><b>What ships and what does not (2026-08-17).</b> This
    /// BODY ships in every configuration, and that is load-bearing: the
    /// release gate is <c>-shiftchange-harness</c> via
    /// <see cref="Patch_HarnessAutoRun"/>, and <c>run-harness.sh</c> builds
    /// plain Release itself, so the gate keeps asserting against the literal
    /// dll players install. The <c>[DebugAction]</c> wrapper is SCENES only.
    ///
    /// This corrects the rationale that used to sit here — "ships in Release,
    /// dev-mode gated, like the other two debug tools; a test you have to
    /// switch build configurations to run is a test that stops being run."
    /// The second half is true and is why the body still ships. The first half
    /// was wrong twice over: the debug actions menu is a surface players
    /// genuinely use, and menu presence was never what kept this test alive —
    /// the launch flag is. Both halves are satisfied at once by compiling out
    /// the entry and keeping the code.</para>
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
        /// <see cref="Run"/> below is the shared body and ships in EVERY
        /// configuration, because <c>-shiftchange-harness</c> is the release
        /// gate and has to assert against the assembly players install.
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
            Case(map, pad, "plain despawn (deconstruct, minify) releases the ledger", MinifyReleases);
            Case(map, pad, "borrower death reaps the ledger", DeathReaps);
            Case(map, pad, "borrower banishment reaps the ledger", BanishmentReaps);
            Case(map, pad, "repeated faults disable interception, a load re-arms it", FaultLatchRecovers);
            Case(map, pad, "a stand that displaces nothing still hands the uniform back",
                 (m, p) => Stage(m, p, StageKit.NonDisplacing), NonDisplacingReturns);
            Case(map, pad, "the driver returns own clothes and their forced flags",
                 (m, p) => Stage(m, p, StageKit.Displacing), DriverRoundTrip);
            Case(map, pad, "the rules the description promises hold",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 PromisesHold);
            Case(map, pad, "a meal break gets them out of uniform first",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true), MealBreakChangesOut);
            Case(map, pad, "a freed stand catches up a colonist working bare",
                 (m, p) => Stage(m, p, StageKit.Displacing, enclose: true,
                                 capableOf: DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor")),
                 FreedStandCatchesUp);
            // Last among the map cases: these replace Current.Game, so `map`,
            // `pad` and every fixture above them belong to a disposed game once
            // they have run. They register through the fixture-less overload
            // for the same reason — Teardown would clear a pad on the disposed
            // map. See DebugTools_SaveRoundTrip.
            Case("a save/load round trip keeps the owner, the ledger and the forced flags",
                 () => DebugTools_SaveRoundTrip.RoundTrip(map, pad));
            Case("a legacy-key save migrates its owner and re-saves prefixed",
                 DebugTools_SaveRoundTrip.LegacyMigration);
            Case("a foreign assignable's owner round-trips without contest",
                 DebugTools_SaveRoundTrip.ForeignAssignable);

            // The two below need no map, so they are unaffected by the game
            // replacement above.
            Case("the room-role table resolves", RoomRoleTableResolves);
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
        /// The gravship case, and the reason the harness exists.
        ///
        /// A launch despawns everything aboard with
        /// <c>DestroyMode.WillReplace</c> (<c>GravshipUtility.cs:389,397</c>)
        /// and <c>Building_OutfitStand.DeSpawn</c> deliberately KEEPS its
        /// contents in that mode (<c>:392</c>), so the stand, the parked
        /// civvies and the borrower all survive the flight. The ledger must
        /// survive with them or the pawn lands in a uniform with no way out.
        /// </summary>
        internal static bool GravshipFlight(Fixture fix)
        {
            IntVec3 standCell = fix.Stand.Position;
            IntVec3 pawnCell = fix.Pawn.Position;

            fix.Stand.DeSpawn(DestroyMode.WillReplace);
            fix.Pawn.DeSpawn(DestroyMode.WillReplace);
            GenSpawn.Spawn(fix.Stand, standCell, fix.Map, Rot4.North);
            GenSpawn.Spawn(fix.Pawn, pawnCell, fix.Map, Rot4.North);
            fix.Stand.PostSwapMap();
            fix.Pawn.PostSwapMap();

            return Expect(fix.Comp.OnShift, "stand still reads on-shift")
                 & Expect(fix.Comp.Borrower == fix.Pawn, "borrower survived the flight")
                 & Expect(fix.Comp.StoredOwnerApparelForReading.Count == fix.StoredCount,
                          "parked civvies still in the ledger")
                 & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                          "registry points back at the stand (Change back works)");
        }

        /// <summary>
        /// The other half of the flight: the stand came, the borrower did not.
        /// Nobody can walk back for their clothes, so the ledger has to go —
        /// and critically the FORCED flags have to come off with it, or the
        /// pawn is pinned into the uniform with every route out closed.
        /// </summary>
        internal static bool GravshipFlightLeftBehind(Fixture fix)
        {
            IntVec3 standCell = fix.Stand.Position;
            Apparel uniform = fix.Comp.IssuedUniformForReading.Count > 0
                ? fix.Comp.IssuedUniformForReading[0]
                : null;

            // The stand flies; the borrower stays on the old map. Despawning
            // the pawn without respawning them is what "left behind" looks
            // like from the stand's side.
            fix.Stand.DeSpawn(DestroyMode.WillReplace);
            fix.Pawn.DeSpawn(DestroyMode.Vanish);
            GenSpawn.Spawn(fix.Stand, standCell, fix.Map, Rot4.North);
            fix.Stand.PostSwapMap();

            bool ok = Expect(!fix.Comp.OnShift, "ledger dropped for an absent borrower")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == null,
                             "registry entry cleared");
            if (uniform != null && fix.Pawn.outfits != null)
            {
                ok &= Expect(!fix.Pawn.outfits.forcedHandler.IsForced(uniform),
                             "uniform no longer force-worn");
            }
            return ok;
        }

        /// <summary>
        /// Deconstruct, burn down or minify into a box. Unlike the flight the
        /// stand really is gone, so the ledger cannot be honoured and the
        /// forced flags must come off.
        /// </summary>
        internal static bool MinifyReleases(Fixture fix)
        {
            Apparel uniform = fix.Comp.IssuedUniformForReading.Count > 0
                ? fix.Comp.IssuedUniformForReading[0]
                : null;

            fix.Stand.DeSpawn(DestroyMode.Vanish);

            bool ok = Expect(!fix.Comp.OnShift, "ledger released")
                    & Expect(fix.Comp.Borrower == null, "borrower cleared")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == null,
                             "registry entry cleared");
            if (uniform != null && fix.Pawn.outfits != null)
            {
                ok &= Expect(!fix.Pawn.outfits.forcedHandler.IsForced(uniform),
                             "uniform no longer force-worn");
            }
            return ok;
        }

        /// <summary>
        /// Death runs <c>Pawn.Kill</c> → <c>Pawn.Destroy(KillFinalize)</c> →
        /// <c>Pawn_Ownership.UnclaimAll</c> (<c>Pawn.cs:2350-2352</c>), which
        /// <see cref="Patch_UnclaimStands"/> hooks. Cheap to arrange in play,
        /// but it is the case the whole reaper story rests on, so it is worth
        /// having a regression marker for it.
        /// </summary>
        internal static bool DeathReaps(Fixture fix)
        {
            fix.Pawn.Kill(null);
            return Expect(!fix.Comp.OnShift, "ledger reaped on death")
                 & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == null,
                          "registry entry cleared");
        }

        /// <summary>
        /// The exit vanilla does not route through <c>UnclaimAll</c>, caught
        /// by <see cref="Patch_BanishStands"/> instead.
        ///
        /// Asserted to the same depth as <see cref="DeathReaps"/>, plus the
        /// forced flag — which matters here and not there, because banishment
        /// leaves the pawn ALIVE and standing on the map. A ledger cleared
        /// without clearing forced pins them into the uniform with every route
        /// out already closed.
        /// </summary>
        internal static bool BanishmentReaps(Fixture fix)
        {
            Apparel uniform = fix.Comp.IssuedUniformForReading.Count > 0
                ? fix.Comp.IssuedUniformForReading[0]
                : null;

            PawnBanishUtility.Banish(fix.Pawn, giveThoughts: false);

            bool ok = Expect(!fix.Comp.OnShift, "ledger reaped on banishment")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == null,
                             "registry entry cleared");
            if (uniform != null && fix.Pawn.outfits != null)
            {
                ok &= Expect(!fix.Pawn.outfits.forcedHandler.IsForced(uniform),
                             "uniform no longer force-worn");
            }
            // The reap must survive the pawn coming back. A liveness-only fix
            // passes everything above and then fails this, because the ledger
            // was never emptied — it was only being disbelieved.
            fix.Pawn.SetFaction(Faction.OfPlayer);
            ok &= Expect(!fix.Comp.OnShift, "still reaped after re-recruitment");
            return ok;
        }

        /// <summary>
        /// B1'S REGRESSION GUARD, and the only one.
        ///
        /// A stand whose stock displaces nothing — a Shell-layer duster over
        /// shirt and trousers, which share no layer with it — used to donate
        /// its uniform permanently: <c>DoTransfer</c> gated the whole return
        /// trip on <c>toWear.Count == 0</c>, so the pawn walked away still
        /// wearing it and the stand emptied itself forever. Silent, and
        /// cumulative.
        ///
        /// Both legs go through the real driver on the real tracker, because
        /// the bug lives past a job boundary that the hand-assembled
        /// <see cref="Build"/> fixture never crosses.
        ///
        /// With the fix reverted, the ledger and forced-flag assertions still
        /// pass — <c>NothingToWear</c> runs <c>AbandonLedger</c>, which tidies
        /// both. The three that fail are the ones that matter: the uniform is
        /// off the pawn, it is back in the stand, and the stand can dress
        /// somebody again.
        /// </summary>
        internal static bool NonDisplacingReturns(Fixture fix)
        {
            bool ok = Expect(RunSwap(fix), "dress leg ran to completion");
            Apparel uniform = fix.Comp.IssuedUniformForReading.Count > 0
                ? fix.Comp.IssuedUniformForReading[0]
                : null;
            ok &= Expect(uniform != null, "the duster was issued")
                & Expect(fix.Comp.StoredOwnerApparelForReading.Count == 0,
                         "and displaced nothing, which is the whole point");
            if (uniform == null)
            {
                return false;
            }

            ok &= Expect(RunSwap(fix), "return leg ran to completion");
            return ok
                & Expect(!fix.Pawn.apparel.WornApparel.Contains(uniform),
                         "the uniform came off")
                & Expect(uniform.ParentHolder == fix.Stand,
                         "and went back into the stand")
                & Expect(fix.Pawn.apparel.WornApparel.Count == 2,
                         "the pawn kept their own clothes")
                & Expect(!fix.Comp.OnShift, "ledger cleared")
                & Expect(SwapPlan.WouldDress(fix.Pawn, fix.Stand),
                         "the stand can dress somebody again");
        }

        /// <summary>
        /// The driver builds a correct ledger, and gives everything back.
        ///
        /// The forced-flag half is what nothing else covers:
        /// <c>Pawn_ApparelTracker.Notify_ApparelRemoved</c> clears the forced
        /// flag on every removal, so the driver captures it BEFORE removing and
        /// restores it on the way back. <see cref="Build"/> hands
        /// <c>NotifyDressed</c> an empty forced list, so that path has been at
        /// zero coverage.
        /// </summary>
        internal static bool DriverRoundTrip(Fixture fix)
        {
            Apparel parka = null;
            List<Apparel> worn = fix.Pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i].def.defName == "Apparel_Parka")
                {
                    parka = worn[i];
                }
            }
            if (parka == null)
            {
                return Expect(false, "fixture is wearing a parka to displace");
            }
            // The player's explicit choice, which the swap must not quietly
            // downgrade to policy-managed.
            fix.Pawn.outfits.forcedHandler.SetForced(parka, forced: true);

            bool ok = Expect(RunSwap(fix), "dress leg ran to completion")
                    & Expect(fix.Comp.Borrower == fix.Pawn, "borrower recorded")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                             "registry points at the stand")
                    & Expect(parka.ParentHolder == fix.Stand, "the parka was parked")
                    & Expect(fix.Comp.StoredOwnerApparelForReading.Contains(parka),
                             "and recorded in the ledger")
                    & Expect(fix.Comp.WasForcedWhenStored(parka),
                             "its force-worn flag was captured before removal");

            ok &= Expect(RunSwap(fix), "return leg ran to completion");
            return ok
                & Expect(fix.Pawn.apparel.WornApparel.Contains(parka),
                         "the parka came back on")
                & Expect(fix.Pawn.outfits.forcedHandler.IsForced(parka),
                         "still force-worn — the player's choice survived the shift")
                & Expect(!fix.Comp.OnShift, "ledger cleared")
                & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == null,
                         "registry entry cleared");
        }

        /// <summary>
        /// THE PROMISES, as a decision table.
        ///
        /// Everything above this is bug-shaped — a guard against something
        /// that once went wrong. This is the other kind: the mod's advertised
        /// behaviour, asserted against the rules the README and the store
        /// description actually print. Those are promises to players, and M4
        /// showed a promise can be wrong in all three descriptions at once
        /// with nothing to notice.
        ///
        /// <para>Driven through <c>TryInsertSwap</c>, the real decision
        /// function the interception prefix calls. Every negative is paired
        /// with a POSITIVE CONTROL — the same setup without the gate — because
        /// "did not divert" is worthless on its own: a stand that was never
        /// eligible would satisfy it just as well.</para>
        /// </summary>
        internal static bool PromisesHold(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            WorkGiverDef urgent = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendEmergency");
            if (doctor == null || tend == null)
            {
                return Expect(false, "the doctor work defs resolve");
            }
            // The pad's room has no role, so name the work explicitly rather
            // than relying on an inference this fixture cannot make.
            fix.Comp.ToggleWork(doctor);

            bool ok = Expect(fix.Comp.HandlesWork(doctor), "the stand serves doctoring")
                    & Expect(Diverts(fix, WorkJob(fix, tend)),
                             "an automatic doctoring job dresses (positive control)");

            Job forced = WorkJob(fix, tend);
            forced.playerForced = true;
            ok &= Expect(!Diverts(fix, forced), "a right-click order is never diverted");

            if (urgent != null)
            {
                ok &= Expect(urgent.emergency, "DoctorTendEmergency is still flagged emergency")
                    & Expect(!Diverts(fix, WorkJob(fix, urgent)),
                             "an emergency is never delayed by a wardrobe trip");
            }

            if (fix.Pawn.drafter != null)
            {
                fix.Pawn.drafter.Drafted = true;
                ok &= Expect(!Diverts(fix, WorkJob(fix, tend)), "a drafted pawn is never diverted");
                fix.Pawn.drafter.Drafted = false;
                ok &= Expect(Diverts(fix, WorkJob(fix, tend)),
                             "and is diverted again once undrafted (control)");
            }

            // "Only doing the room's work does." A job of a work type this
            // stand does not serve must not dress anyone.
            WorkGiverDef cook = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsCook");
            if (cook != null)
            {
                ok &= Expect(!Diverts(fix, WorkJob(fix, cook)),
                             "work the stand does not serve is not diverted");
            }
            return ok;
        }

        /// <summary>
        /// The meal-break promise — and the one the copy got WRONG.
        ///
        /// All three descriptions used to say "eating in a room changes
        /// nothing". The code has always said the opposite, deliberately
        /// (principal, 2026-08-08): a meal is a sit-down break, so the uniform
        /// comes off first wherever the food is stored, because otherwise a
        /// cook carries a meal across the base in whites to reach a chair —
        /// the exact walk this mod exists to prevent. The single exception is
        /// food already in hand or pack, which is just eaten.
        ///
        /// Nothing caught that for months. This is what would have.
        /// </summary>
        internal static bool MealBreakChangesOut(Fixture fix)
        {
            bool ok = Expect(RunSwap(fix), "dressed for the shift")
                    & Expect(fix.Comp.OnShift, "and is on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }

            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
            if (mealDef == null)
            {
                return Expect(false, "MealSimple resolves");
            }

            Thing stored = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef),
                                          fix.Pawn.Position, fix.Map);
            ok &= Expect(Diverts(fix, IngestJob(stored)),
                         "a meal break gets them out of uniform first");

            Thing carried = ThingMaker.MakeThing(mealDef);
            fix.Pawn.inventory.innerContainer.TryAdd(carried);
            ok &= Expect(!Diverts(fix, IngestJob(carried)),
                         "but food already carried is simply eaten");
            return ok;
        }

        internal static Job IngestJob(Thing food)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Ingest, food);
            job.count = 1;
            return job;
        }

        /// <summary>
        /// M3'S REGRESSION GUARD: a freed stand catches up a colonist already
        /// working bare in its room.
        ///
        /// The announcement used to fire from a TOIL finish action, while the
        /// departing pawn still held the stand's <c>maxPawns = 1</c>
        /// reservation — so every candidate died on
        /// <c>CanReserveAndReach</c> and the interrupt simply never happened.
        /// It moved to a global finish action, which the tracker runs after
        /// <c>CleanupCurrentJob</c> releases reservations
        /// (<c>:492</c> then <c>:497</c>).
        ///
        /// Binary, with no timing subtlety: under the regression the second
        /// colonist is never interrupted at all.
        /// </summary>
        internal static bool FreedStandCatchesUp(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            if (doctor == null || tend == null)
            {
                return Expect(false, "the doctor work defs resolve");
            }
            fix.Comp.ToggleWork(doctor);

            bool ok = Expect(fix.Map.dangerWatcher.DangerRating == StoryDanger.None,
                             "the map is calm — the catch-up is danger-gated")
                    & Expect(RunSwap(fix), "the first colonist dressed");

            Pawn bare = DebugTools_Fixtures.AveragePawn(Gender.Female, "Bare", doctor);
            bare.apparel?.DestroyAll();
            bare.workSettings?.EnableAndInitialize();
            GenSpawn.Spawn(bare, fix.Stand.Position + new IntVec3(2, 0, 2), fix.Map, Rot4.North);
            fix.Extras.Add(bare);
            WearOne(bare, "Apparel_BasicShirt");
            WearOne(bare, "Apparel_Pants");

            // Working in the room, in their own clothes, on a job the catch-up
            // is allowed to interrupt.
            //
            // Goto rather than Wait, because Core's Wait is suspendable=false
            // and the filter rejects it outright. Targeted at ANOTHER cell,
            // not this pawn's own: StartJob runs ReadyForNextToil
            // synchronously, so a Goto to where the pawn already stands
            // arrives and completes inside StartJob, and the think tree hands
            // them a Wait_Wander — suspendable=false, filtered out, and the
            // case then fails for a reason that has nothing to do with M3.
            // Aimed elsewhere the job stays open: the path request is never
            // served, because nothing ticks this pawn.
            Job working = JobMaker.MakeJob(JobDefOf.Goto,
                                           fix.Stand.Position + new IntVec3(3, 0, 2));
            working.workGiverDef = tend;
            bare.jobs.StartJob(working, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);

            ok &= Expect(SwapPlan.WouldDress(bare, fix.Stand),
                         "the second colonist could wear what the stand holds")
                & Expect(bare.CurJobDef == JobDefOf.Goto,
                         "and is still on the interruptible job we gave them (control)");

            ok &= Expect(RunSwap(fix), "the first colonist changed back, freeing the stand");
            return ok & Expect(bare.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                               "the freed stand interrupted them to dress");
        }

        /// <summary>
        /// Ask the real decision function what it would do, and leave no trace:
        /// cancel any swap it started, and clear the retry cooldown it stamps
        /// on a refusal — that cooldown would otherwise silently make every
        /// later probe in this case return false for the wrong reason.
        /// </summary>
        internal static bool Diverts(Fixture fix, Job job)
        {
            bool inserted = Patch_JobInterception.TryInsertSwap(job, null, fix.Pawn, fix.Pawn.jobs);
            if (inserted && fix.Pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand)
            {
                fix.Pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }
            Patch_JobInterception.LastBlockedTick.Remove(fix.Pawn.thingIDNumber);
            return inserted;
        }

        /// <summary>
        /// A job of the given work type, targeted where the pawn stands — so
        /// it reads as work done in the stand's own room.
        /// </summary>
        internal static Job WorkJob(Fixture fix, WorkGiverDef giver)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Wait, fix.Pawn.Position);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>
        /// Every def the room-role table names still exists.
        ///
        /// <c>RoomWorkTypes</c> resolves through <c>GetNamedSilentFail</c> and
        /// drops whatever is missing, so a renamed def empties a role's work
        /// list, <c>HandlesWork</c> returns false everywhere, and the mod does
        /// nothing at all — with a green harness and no log line. Most likely
        /// to fire on a game update rather than on an edit.
        /// </summary>
        internal static bool RoomRoleTableResolves()
        {
            bool ok = Expect(RoomWorkTypes.Defaults.Count > 0, "the table is not empty");
            foreach (KeyValuePair<string, string[]> entry in RoomWorkTypes.Defaults)
            {
                RoomRoleDef role = DefDatabase<RoomRoleDef>.GetNamedSilentFail(entry.Key);
                ok &= Expect(role != null, "room role " + entry.Key + " resolves");
                int resolved = 0;
                for (int i = 0; i < entry.Value.Length; i++)
                {
                    if (DefDatabase<WorkTypeDef>.GetNamedSilentFail(entry.Value[i]) != null)
                    {
                        resolved++;
                    }
                    else
                    {
                        Expect(false, "work type " + entry.Value[i] + " resolves");
                        ok = false;
                    }
                }
                if (role != null)
                {
                    ok &= Expect(RoomWorkTypes.ForRole(role).Count == resolved,
                                 entry.Key + " maps to all " + resolved + " of its work types");
                }
            }
            return ok;
        }

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

        /// <summary>
        /// The fault latch, driven by real throws through the real
        /// <see cref="Patch_JobInterception.Prefix"/> catch block rather than
        /// by calling the counter directly — the same rule as every other case
        /// here. A test that pokes <c>NoteFault</c> would prove the arithmetic
        /// and nothing about whether an exception in interception reaches it.
        ///
        /// Three claims, and the third is the bug this was written for. Until
        /// 2026-08-14 a single throw disabled the mod for the whole PROCESS —
        /// through a save load, a new colony, everything, until RimWorld was
        /// restarted — and said nothing to the player, because
        /// <c>Log.Error</c> does not open the log window outside dev mode.
        ///
        /// The one thing this cannot do is load an actual save; it changes the
        /// game reference under <see cref="SessionGuard"/> instead, which is
        /// the same trigger a load pulls.
        /// </summary>
        internal static bool FaultLatchRecovers(Fixture fix)
        {
            int limitBefore = Patch_JobInterception.FaultLimit;
            bool enabledBefore = Patch_JobInterception.Enabled;
            Patch_JobInterception.ResetSessionState();
            Patch_JobInterception.FaultLimit = 3;

            bool ok = Expect(!Patch_JobInterception.faulted, "starts armed");

            // Below the limit: counted, still serving. These catch blocks also
            // fire for a NEIGHBOUR's exception thrown through our frame, so
            // one throw must not take the mod out for the rest of the colony.
            Fault(fix.Pawn, 2);
            ok &= Expect(Patch_JobInterception.faultCount == 2, "throws are counted")
                & Expect(!Patch_JobInterception.faulted, "still armed below the limit");

            Fault(fix.Pawn, 1);
            ok &= Expect(Patch_JobInterception.faulted, "latched at the limit");

            // What a save load does. This is the whole fix: before it, the
            // only way back was quitting the game.
            SessionGuard.current = null;
            SessionGuard.Ensure();
            ok &= Expect(!Patch_JobInterception.faulted, "a game change re-arms it")
                & Expect(Patch_JobInterception.faultCount == 0, "the count resets with it");

            // Enabled is a STANDING decision — the player's, or the hot-reload
            // quarantine's — and the quarantine has to survive a load because
            // the wedging twin JobDriver is still loaded. Re-arming it here
            // would resurrect the 2026-08-08 tracker wedge.
            ok &= Expect(Patch_JobInterception.Enabled == enabledBefore,
                         "Enabled is left alone");

            Patch_JobInterception.FaultLimit = limitBefore;
            return ok;
        }

        /// <summary>
        /// Make interception throw <paramref name="times"/> times, through the
        /// real patch entry point.
        /// </summary>
        internal static void Fault(Pawn pawn, int times)
        {
            Patch_JobInterception.injectFaults = times;
            for (int i = 0; i < times; i++)
            {
                Patch_JobInterception.Prefix(
                    JobMaker.MakeJob(JobDefOf.Wait), null, pawn, pawn.jobs);
            }
            // Belt and braces: if a call did not reach the injection point the
            // counter would otherwise stay armed into the next case.
            Patch_JobInterception.injectFaults = 0;
        }

        // -------------------------------------------------------- fixturing

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
                Teardown(fix, map, pad);
            }
        }

        /// <summary>
        /// A stand and a pawn, dressed per <paramref name="kit"/>, with NO
        /// ledger — nothing has swapped yet. Cases that want to watch the
        /// driver build the ledger start here and call <see cref="RunSwap"/>;
        /// <see cref="Build"/> starts here too and then hand-assembles a
        /// checked-out state for the lifecycle cases.
        /// </summary>
        /// <summary>
        /// Wall, roof and floor the pad's perimeter, so its interior is a
        /// PROPER ROOM.
        ///
        /// The lifecycle cases do not need this — they act on a ledger
        /// directly. The functional cases do: <c>FindAvailableStand</c> walks
        /// <c>room.ContainedThings</c>, and on the open pad that room is the
        /// whole outdoors. Testing the decision table out there would assert
        /// against a huge-room path that the review has already flagged as
        /// questionable, and would then break the day it is tightened.
        /// </summary>
        internal static void EnclosePad(Map map, CellRect pad)
        {
            TerrainDef floor = DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor");
            foreach (IntVec3 cell in pad)
            {
                if (floor != null)
                {
                    map.terrainGrid.SetTerrain(cell, floor);
                }
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                bool edge = cell.x == pad.minX || cell.x == pad.maxX
                            || cell.z == pad.minZ || cell.z == pad.maxZ;
                if (edge)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog),
                                   cell, map);
                }
            }
        }

        internal static Fixture Stage(Map map, CellRect pad, StageKit kit,
                                      bool enclose = false, WorkTypeDef capableOf = null)
        {
            GenDebug.ClearArea(pad, map);
            ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
            if (standDef == null)
            {
                return null;
            }
            if (enclose)
            {
                EnclosePad(map, pad);
            }
            IntVec3 standCell = new IntVec3(pad.minX + 1, 0, pad.minZ + 1);

            Building_OutfitStand stand = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                map, standDef, ThingDefOf.WoodLog, standCell, Rot4.North);
            if (!StockOne(stand, "Apparel_Duster"))
            {
                return null;
            }

            Pawn pawn = DebugTools_Fixtures.AveragePawn(Gender.Male, "Test", capableOf);
            pawn.apparel?.DestroyAll();
            if (capableOf != null)
            {
                pawn.workSettings?.EnableAndInitialize();
            }
            // ON the interaction cell, so the driver's Goto toil arrives
            // immediately and no path is ever requested.
            //
            // This is not tidiness, it is the only way the pump works.
            // Pathfinding in 1.6 is ASYNCHRONOUS — Pawn_PathFollower.PatherTick
            // waits on `curPathRequest.TryGetPath(...)` (:261), and that
            // request is served by a job system the game's own update loop
            // drives, not by Pawn.DoTick(). Ticking one pawn therefore leaves
            // it "moving" forever, one cell from the stand, which is exactly
            // what the first version of these cases did for 5000 ticks.
            //
            // What it costs: these cases do not exercise the walk. The walk is
            // vanilla's Toils_Goto, not ours, and what they are here to prove
            // is the transfer.
            GenSpawn.Spawn(pawn, stand.InteractionCell, map, Rot4.North);
            WearOne(pawn, "Apparel_BasicShirt");
            WearOne(pawn, "Apparel_Pants");
            if (kit == StageKit.Displacing)
            {
                WearOne(pawn, "Apparel_Parka");
            }

            CompShiftStand comp = stand.TryGetComp<CompShiftStand>();
            if (comp == null)
            {
                return null;
            }
            return new Fixture { Map = map, Stand = stand, Pawn = pawn, Comp = comp, StoredCount = 0 };
        }

        internal static bool StockOne(Building_OutfitStand stand, string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            return def != null && stand.AddApparel(DebugTools_Fixtures.MakeGarment(def, null));
        }

        internal static bool WearOne(Pawn pawn, string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null || pawn.apparel == null)
            {
                return false;
            }
            Apparel garment = DebugTools_Fixtures.MakeGarment(def, null);
            pawn.apparel.Wear(garment);
            return pawn.apparel.WornApparel.Contains(garment);
        }

        /// <summary>
        /// Run one swap to completion through the REAL driver: start the job on
        /// the pawn's own tracker, then tick that pawn until the job ends.
        ///
        /// <para>One pawn is a complete pump. <c>Pawn.Tick</c> drives
        /// <c>pather.PatherTick()</c> and <c>jobs.JobTrackerTick()</c>
        /// (<c>Verse/Pawn.cs:1555,:1573</c>), the toil delay counts down in
        /// <c>JobDriver.DriverTick</c>, and nothing in this driver reads
        /// <c>TicksGame</c> — so no <c>TickManager</c>, no world tick, and no
        /// other pawn's AI is involved. That is what makes this runnable from
        /// a debug action at all.</para>
        ///
        /// <para>Returns false on timeout rather than throwing, and every
        /// caller asserts on it: a pump that gave up must FAIL loudly, not
        /// fall through into assertions that then pass for the wrong
        /// reason.</para>
        /// </summary>
        internal static bool RunSwap(Fixture fix, int maxTicks = 5000)
        {
            Job swap = JobMaker.MakeJob(ShiftChangeDefOf.ShiftChange_SwapAtStand, fix.Stand);
            fix.Pawn.jobs.StartJob(swap, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null,
                JobTag.ChangingApparel);

            // Watch the DRIVER, never the Job. Jobs are POOLED: when ours ends
            // it goes back to JobMaker's pool and is handed straight out again
            // for the pawn's next job — so `CurJob != swap` compares a
            // reference that vanilla has already recycled under us and stays
            // equal forever. The first version of this waited 5000 ticks on a
            // JobDriver_WaitMaintainPosture that was wearing our job object.
            // Drivers are built per job and never pooled.
            JobDriver started = fix.Pawn.jobs.curDriver;
            if (started == null)
            {
                Report.AppendLine("      the swap job never got a driver");
                return false;
            }

            // Capture how it ENDED, not merely that it ended. "The driver
            // changed" is satisfied by a job that failed its reservations and
            // died before its transfer toil — which is exactly what a flaky
            // run looked like, reported as a pass, while every assertion after
            // it failed with no explanation.
            JobCondition ended = JobCondition.None;
            started.AddFinishAction(c => ended = c);

            for (int i = 0; i < maxTicks; i++)
            {
                if (fix.Pawn.jobs.curDriver != started)
                {
                    if (ended == JobCondition.Succeeded)
                    {
                        return true;
                    }
                    Report.Append("      the swap ended with ").Append(ended)
                          .Append(", not Succeeded — toil ").Append(started.CurToilIndex)
                          .Append(", pawn at ").Append(fix.Pawn.Position)
                          .Append(" vs cell ").Append(fix.Stand.InteractionCell)
                          .AppendLine();
                    return false;
                }
                // The whole pawn, so a third-party Pawn.Tick postfix can throw
                // in here. The case-level catch reports that as FAIL threw:
                // rather than swallowing it.
                fix.Pawn.DoTick();
            }

            // A timeout that says only "timed out" costs an entire debugging
            // cycle. Say where it got stuck.
            JobDriver driver = fix.Pawn.jobs?.curDriver;
            JobDriver_SwapAtStand ours = driver as JobDriver_SwapAtStand;
            Report.Append("      stuck after ").Append(maxTicks).Append(" ticks — driver ")
                  .Append(driver == null ? "null" : driver.GetType().Name)
                  .Append(", toil ").Append(driver == null ? -1 : driver.CurToilIndex)
                  .Append(", ticksLeft ").Append(driver == null ? -1 : driver.ticksLeftThisToil)
                  .Append(", sameDriver ").Append(fix.Pawn.jobs.curDriver == started)
                  .Append(", undressing ").Append(ours != null && ours.undressing)
                  .Append(", toWear ").Append(ours == null ? -1 : ours.toWear.Count)
                  .Append(", toStore ").Append(ours == null ? -1 : ours.toStore.Count)
                  .Append(", onShift ").Append(fix.Comp.OnShift)
                  .Append(", moving ").Append(fix.Pawn.pather != null && fix.Pawn.pather.Moving)
                  .Append(", at ").Append(fix.Pawn.Position)
                  .Append(" vs cell ").Append(fix.Stand.InteractionCell)
                  .Append(", stanceBusy ")
                  .Append(fix.Pawn.stances != null && fix.Pawn.stances.FullBodyBusy)
                  .AppendLine();
            return false;
        }

        /// <summary>
        /// A stand stocked with a lab coat and a pawn in a tunic and duster,
        /// put on shift the way <see cref="JobDriver_SwapAtStand.DoTransfer"/>
        /// does it — plan from <see cref="SwapPlan"/>, move the apparel, then
        /// <c>NotifyDressed</c>.
        ///
        /// The duster matters: without it the lab coat (Shell) displaces
        /// nothing on a tunic (OnSkin) and the ledger stores nothing, which is
        /// a real case but a useless FIXTURE — every assertion below is about
        /// a ledger with contents in it.
        /// </summary>
        internal static Fixture Build(Map map, CellRect pad)
        {
            GenDebug.ClearArea(pad, map);
            ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
            IntVec3 standCell = new IntVec3(pad.minX + 1, 0, pad.minZ + 1);
            IntVec3 pawnCell = new IntVec3(pad.minX + 3, 0, pad.minZ + 1);

            Building_OutfitStand stand = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                map, standDef, ThingDefOf.WoodLog, standCell, Rot4.North);
            DebugTools_Fixtures.Stock(stand, new[] { "VAE_Apparel_LabCoat" },
                DebugTools_Fixtures.DusterGreen);

            Pawn pawn = DebugTools_Fixtures.AveragePawn(Gender.Male, "Test");
            DebugTools_Fixtures.DressInStartingKit(pawn, researcher: true);
            GenSpawn.Spawn(pawn, pawnCell, map, Rot4.North);

            CompShiftStand comp = stand.TryGetComp<CompShiftStand>();
            if (comp == null)
            {
                return null;
            }

            // The transfer, exactly as the driver sequences it: plan from the
            // one shared predicate, own clothes off and into the stand,
            // uniform out and on, forced set, ledger recorded.
            List<Apparel> toWear = new List<Apparel>();
            List<Apparel> toStore = new List<Apparel>();
            if (!SwapPlan.BuildDress(pawn, stand, toWear, toStore) || toStore.Count == 0)
            {
                return null;
            }

            List<Apparel> stored = new List<Apparel>();
            foreach (Apparel apparel in toStore)
            {
                pawn.apparel.Remove(apparel);
                stand.AddApparel(apparel);
                stored.Add(apparel);
            }
            List<Apparel> issued = new List<Apparel>();
            foreach (Apparel apparel in toWear)
            {
                if (!stand.RemoveApparel(apparel))
                {
                    continue;
                }
                pawn.apparel.Wear(apparel);
                if (!pawn.apparel.WornApparel.Contains(apparel))
                {
                    continue;
                }
                pawn.outfits?.forcedHandler?.SetForced(apparel, forced: true);
                issued.Add(apparel);
            }
            if (issued.Count == 0)
            {
                return null;
            }
            comp.NotifyDressed(pawn, stored, issued, new List<Apparel>());

            return new Fixture
            {
                Map = map,
                Stand = stand,
                Pawn = pawn,
                Comp = comp,
                StoredCount = stored.Count,
            };
        }

        /// <summary>
        /// Leave nothing behind. Destroying the stand fires our own release
        /// path, which is fine — the assertions have already run, and a case
        /// that leaked a registry entry would otherwise poison the next one.
        /// </summary>
        internal static void Teardown(Fixture fix, Map map, CellRect pad)
        {
            if (fix != null)
            {
                for (int i = 0; i < fix.Extras.Count; i++)
                {
                    Pawn extra = fix.Extras[i];
                    if (extra == null)
                    {
                        continue;
                    }
                    CompShiftStand.OnShiftStands.Remove(extra);
                    if (!extra.Destroyed)
                    {
                        extra.Destroy();
                    }
                }
                if (fix.Pawn != null)
                {
                    CompShiftStand.OnShiftStands.Remove(fix.Pawn);
                    if (!fix.Pawn.Destroyed)
                    {
                        fix.Pawn.Destroy();
                    }
                }
                if (fix.Stand != null && !fix.Stand.Destroyed)
                {
                    fix.Stand.Destroy();
                }
            }
            GenDebug.ClearArea(pad, map);
        }

        // ------------------------------------------------------- assertions

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
