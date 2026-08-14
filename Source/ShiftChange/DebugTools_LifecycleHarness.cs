using System;
using System.Collections.Generic;
using System.Text;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

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
    /// <para><b>Fixture setup is not tested by this.</b> Getting a pawn on
    /// shift here moves apparel and calls <c>NotifyDressed</c> directly rather
    /// than running <see cref="JobDriver_SwapAtStand"/>, because the driver
    /// needs a walk and a delay. The plan still comes from
    /// <see cref="SwapPlan"/>, so the wearability answer is the shared one —
    /// but a harness pass says nothing about whether the DRIVER builds a
    /// correct ledger. That remains a play observation.</para>
    ///
    /// <para>Ships in Release, dev-mode gated, like the other two debug tools.
    /// A test you have to switch build configurations to run is a test that
    /// stops being run.</para>
    ///
    /// <para><b>First run, 2026-08-14</b>, clean Release restart on the ~100-mod
    /// profile, quicktest map: 4 passed, 0 failed, 1 known gap. The gravship
    /// case passed on all four assertions, which is what the
    /// <c>WillReplace</c> guard in <see cref="CompShiftStand.PostDeSpawn"/> was
    /// written for and had until then only been reasoned about.</para>
    /// </summary>
    internal static class DebugTools_LifecycleHarness
    {
        /// <summary>Cleared and rebuilt per case, so no case inherits another's mess.</summary>
        internal const int PadSize = 7;

        internal static readonly StringBuilder Report = new StringBuilder();
        internal static int Passed;
        internal static int Failed;
        internal static int KnownGaps;

        [DebugAction("Shift Change", "Run lifecycle harness",
            actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap,
            requiresOdyssey = true)]
        internal static void RunHarness()
        {
            Run(Find.CurrentMap, UI.MouseCell(), toast: true);
        }

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

        // -------------------------------------------------------- fixturing

        /// <summary>One case's throwaway world: a stocked stand and a borrower wearing its uniform.</summary>
        internal class Fixture
        {
            internal Map Map;
            internal Building_OutfitStand Stand;
            internal Pawn Pawn;
            internal CompShiftStand Comp;
            internal int StoredCount;
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
                        fix = Build(map, pad);
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
                        : "fixture could not be built (nothing displaced — check the stocked apparel)");
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

            Building_OutfitStand stand = (Building_OutfitStand)DebugTools_DemoStage.Spawn(
                map, standDef, ThingDefOf.WoodLog, standCell, Rot4.North);
            DebugTools_DemoStage.Stock(stand, new[] { "VAE_Apparel_LabCoat" },
                DebugTools_DemoStage.DusterGreen);

            Pawn pawn = DebugTools_DemoStage.AveragePawn(Gender.Male, "Test");
            DebugTools_DemoStage.DressInStartingKit(pawn, researcher: true);
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
