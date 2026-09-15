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

namespace ShiftChange
{
    /// <summary>
    /// Staging and teardown for every harness case: builds a throwaway stand
    /// and borrower on a cleared pad, runs the real swap driver against it,
    /// and sweeps the pad afterwards so no case inherits another's mess.
    ///
    /// <para>Split out of <see cref="DebugTools_LifecycleHarness"/> on
    /// 2026-09-15. These are separate TYPES rather than partials on purpose: a
    /// decompiler merges partials back into one class, so partials would have
    /// left the shipped dll reading exactly as it did before. The registration
    /// list that decides case ORDER stays in
    /// <see cref="DebugTools_LifecycleHarness.Run"/> and must not be
    /// scattered.</para>
    /// </summary>
    internal static class HarnessFixtures
    {
        /// <summary>
        /// A spare colonist on the pad, registered for teardown. Bare of work
        /// types on purpose: the assignable comp offers every colonist when the
        /// stand has resolved none, which is what these cases want to test.
        /// </summary>
        internal static Pawn SpawnExtra(Fixture fix, Gender gender, string nick)
        {
            Pawn pawn = DebugTools_Fixtures.AveragePawn(gender, nick);
            pawn.workSettings?.EnableAndInitialize();
            GenSpawn.Spawn(pawn, fix.Stand.Position + new IntVec3(2, 0, fix.Extras.Count + 1),
                           fix.Map, Rot4.North);
            fix.Extras.Add(pawn);
            return pawn;
        }

        /// <summary>
        /// A stand-in gizmo chain. Identity is all that matters — these are
        /// never drawn, only counted and compared by reference.
        /// </summary>
        internal static List<Gizmo> Sentinels()
        {
            return new List<Gizmo>
            {
                new Command_Action { defaultLabel = "sentinel one" },
                new Command_Action { defaultLabel = "sentinel two" },
                new Command_Action { defaultLabel = "sentinel three" },
            };
        }

        /// <summary>Same gizmos, same order, by reference.</summary>
        internal static bool SameSequence(IEnumerable<Gizmo> got, IList<Gizmo> want)
        {
            List<Gizmo> actual = got.ToList();
            if (actual.Count != want.Count)
            {
                return false;
            }
            for (int i = 0; i < want.Count; i++)
            {
                if (!ReferenceEquals(actual[i], want[i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>A stand-in for vanilla's own "Allow removing items" toggle.</summary>
        internal static Command_Toggle RemovalToggle(string label, bool active, string desc)
        {
            return new Command_Toggle
            {
                defaultLabel = label,
                defaultDesc = desc,
                isActive = () => active,
                toggleAction = () => { },
            };
        }

        /// <summary>
        /// A recreation job whose B target is the pawn's own cell — the shape
        /// the joy arm resolves. <see cref="WorkJob"/> is its work-arm twin.
        /// </summary>
        internal static Job JoyJob(Fixture fix, JobDef def)
        {
            return JobMaker.MakeJob(def, fix.Pawn.Position, fix.Pawn.Position);
        }

        internal static Job IngestJob(Thing food)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Ingest, food);
            job.count = 1;
            return job;
        }

        /// <summary>
        /// Remove a staged threat and drop the cache stamp with it, so the map
        /// reads calm again NOW rather than 101 ticks from now that never
        /// arrive. Hands the next case a calm map — the freed-stand case
        /// asserts on that by name, so a stale non-None left behind here fails
        /// a case that has nothing wrong with it.
        /// </summary>
        internal static void ClearThreat(Fixture fix, Pawn threat)
        {
            if (threat != null && !threat.Destroyed)
            {
                threat.Destroy();
            }
            ForceDangerRecheck(fix.Map);
        }

        /// <summary>
        /// A live hostile on the map, which is all <c>StoryDanger</c> is:
        /// <c>DangerWatcher</c> sums <c>kindDef.combatPower</c> over the
        /// attack-target cache.
        ///
        /// It is never ticked. The harness ticks fixture pawns one at a time
        /// and never the map, so this one stands inert beside the pad and
        /// cannot fight, flee or path — which is what makes a raider safe to
        /// park next to the colonist whose job we are about to drive.
        ///
        /// Unfogged deliberately: <c>IsActiveThreatToPlayer</c> refuses a
        /// fogged target (<c>canBeFogged</c> defaults false), and a quicktest
        /// map is fogged everywhere the starting pawns have not walked.
        ///
        /// <para><b>Insects, not pirates.</b> The first version of this asked
        /// for <c>Faction.OfPirates</c> and failed on staging rather than on
        /// behaviour (harness run, 2026-08-31): pirates are generated into a
        /// world by worldgen and the quicktest world has none, so the faction
        /// came back null. <c>OfInsects</c> is a permanent faction present in
        /// every world, permanently hostile, and a Megascarab carries no
        /// <c>CompCanBeDormant</c> — so it reads awake and counts toward the
        /// rating. (<c>IsActiveThreatToPlayer</c>'s <c>ignoreHives</c> flag
        /// excludes hives, not the insects themselves.) The fallbacks exist so
        /// a world missing one permanent faction still stages a threat rather
        /// than reporting a behavioural failure that never ran.</para>
        /// </summary>
        internal static Pawn Threat(Fixture fix)
        {
            Faction hostile = Faction.OfInsects;
            PawnKindDef kind = PawnKindDefOf.Megascarab;
            if (hostile == null || kind == null)
            {
                hostile = Faction.OfMechanoids;
                kind = PawnKindDefOf.Mech_Scyther;
            }
            if (hostile == null || kind == null)
            {
                hostile = Faction.OfPirates;
                kind = PawnKindDefOf.Pirate;
            }
            if (hostile == null || kind == null)
            {
                return null;
            }
            IntVec3 cell = fix.Stand.Position + new IntVec3(3, 0, 3);
            if (!cell.InBounds(fix.Map))
            {
                cell = fix.Pawn.Position;
            }
            Pawn threat = PawnGenerator.GeneratePawn(kind, hostile);
            GenSpawn.Spawn(threat, cell, fix.Map, Rot4.North);
            fix.Map.fogGrid.Unfog(threat.Position);
            // Swept by Teardown as a backstop if an assertion below throws;
            // the case destroys it itself on the ordinary path, and Teardown
            // skips anything already destroyed.
            fix.Extras.Add(threat);
            ForceDangerRecheck(fix.Map);
            return threat;
        }

        /// <summary>
        /// Drop <c>DangerWatcher</c>'s cache stamp so the next read recomputes.
        ///
        /// The rating is cached for 101 ticks, and the harness runs from a
        /// <c>Game.FinalizeInit</c> postfix — before the game loop — so
        /// <c>TicksGame</c> never advances while a case runs. Without this a
        /// spawned hostile sits behind a stale <c>None</c> forever, and the
        /// case would pass by asserting nothing.
        /// </summary>
        internal static void ForceDangerRecheck(Map map)
        {
            AccessTools.Field(typeof(DangerWatcher), "lastUpdateTick")
                       ?.SetValue(map.dangerWatcher, -10000);
        }

        /// <summary>
        /// Ask the real decision function what it would do, and leave no trace:
        /// cancel any swap it started, and clear the retry cooldown it stamps
        /// on a refusal — that cooldown would otherwise silently make every
        /// later probe in this case return false for the wrong reason.
        /// </summary>
        internal static bool Diverts(Fixture fix, Job job, JobTag? tag = null)
        {
            bool inserted = Patch_JobInterception.TryInsertSwap(job, tag, fix.Pawn, fix.Pawn.jobs);
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
                                      bool enclose = false, WorkTypeDef capableOf = null,
                                      Gender gender = Gender.Male)
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

            // Gender is a parameter because vanilla's decency rule is ASYMMETRIC
            // (Pawn_ApparelTracker.PsychologicallyNude:214-222): a man is nude on
            // missing trousers alone, a woman on missing trousers OR a bare torso.
            // A male-only fixture therefore certifies as safe a whole class of
            // stand configurations that strip a woman. Default stays Male so every
            // case written before 2026-09-06 is untouched.
            Pawn pawn = DebugTools_Fixtures.AveragePawn(gender, "Test", capableOf);
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

        /// <summary>
        /// Strip a fixture stand back to empty, so a case can state exactly what
        /// it holds instead of working around the duster <see cref="Stage"/>
        /// stocks.
        /// </summary>
        internal static void ClearStand(Building_OutfitStand stand)
        {
            while (stand.HeldItems.Count > 0)
            {
                Thing held = stand.HeldItems[0];
                if (!stand.RemoveApparel(held as Apparel))
                {
                    break;
                }
                held.Destroy();
            }
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
    }
}
