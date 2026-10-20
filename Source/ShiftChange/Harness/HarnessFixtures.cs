// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
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
using Verse.AI.Group;
using static ShiftChange.DebugTools_LifecycleHarness;

namespace ShiftChange
{
    /// <summary>Staging, pumping and teardown shared by every harness case.</summary>
    internal static class HarnessFixtures
    {
        /// <summary>
        /// A spare colonist on the pad, registered for teardown. Spawning is retried like a fixture
        /// build (<see cref="BuildAttempts"/>): on a large mod list it can throw
        /// <c>Collection was modified</c> in <c>Pawn_HealthTracker.Notify_Spawned</c>, under
        /// third-party postfixes on <c>Pawn.SpawnSetup</c>. Every retry is printed, and the last
        /// attempt's exception fails the case.
        /// </summary>
        internal static Pawn SpawnExtra(Fixture fix, Gender gender, string nick)
        {
            return SpawnExtra(fix, gender, nick, fix.Stand.Position + new IntVec3(2, 0, fix.Extras.Count + 1));
        }

        /// <summary><see cref="SpawnExtra(Fixture, Gender, string)"/> on a given cell.</summary>
        internal static Pawn SpawnExtra(Fixture fix, Gender gender, string nick, IntVec3 cell)
        {
            for (int attempt = 1; ; attempt++)
            {
                Pawn pawn = DebugTools_Fixtures.AveragePawn(gender, nick);
                pawn.workSettings?.EnableAndInitialize();
                try
                {
                    GenSpawn.Spawn(pawn, cell, fix.Map, Rot4.North);
                    fix.Extras.Add(pawn);
                    return pawn;
                }
                catch (Exception e) when (attempt < BuildAttempts)
                {
                    Report.Append("    RETRY extra pawn spawn threw on attempt ").Append(attempt)
                          .Append(" — ").Append(e.GetType().Name).Append(": ")
                          .AppendLine(e.Message);
                    try
                    {
                        // Likely half spawned: clear it before the retry lands on the same cell.
                        if (!pawn.Destroyed)
                        {
                            pawn.Destroy();
                        }
                    }
                    catch (Exception)
                    {
                        // Teardown clears the pad whatever is left on it.
                    }
                }
            }
        }

        /// <summary>Stand-in gizmos: never drawn, only counted and compared by reference.</summary>
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
        /// A recreation job whose B target is the pawn's own cell, the shape the joy arm resolves.
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
        /// Removes a staged threat and drops the danger cache's stamp, so the map reads calm at
        /// once; a plain <c>Destroy</c> leaves the rating stale until something drops it.
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
        /// A live hostile on the map, which is all <c>StoryDanger</c> is: <c>DangerWatcher</c> sums
        /// <c>kindDef.combatPower</c> over the attack-target cache. It is never ticked (the harness
        /// ticks fixture pawns one at a time, never the map), so it stands inert, but a colonist
        /// ticked while it is there reacts to it: clear it (<see cref="ClearThreat"/>) before
        /// anything pumps the colonist.
        ///
        /// <para>Unfogged, because <c>IsActiveThreatToPlayer</c> refuses a fogged target
        /// (<c>canBeFogged</c> defaults false), and map generation fogs every cell not connected
        /// to the start spot, which clearing rock does not lift. An insect, not a pirate: the
        /// quicktest world has no pirate faction, so <c>Faction.OfPirates</c> can be null, while
        /// <c>OfInsects</c> is permanent and always hostile. A Megascarab's
        /// <c>CompCanBeDormant</c> does not start dormant, so it reads awake and counts, and
        /// <c>ignoreHives</c> drops only insects whose lord defends a hive; this one has no lord.
        /// The fallbacks cover a world missing a faction; null means none could be staged.</para>
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
            // Registered so Teardown removes it if the case throws before ClearThreat.
            fix.Extras.Add(threat);
            ForceDangerRecheck(fix.Map);
            return threat;
        }

        /// <summary>
        /// Drops <c>DangerWatcher</c>'s cache stamp so the next read recomputes. The rating is
        /// cached for 101 ticks and <c>TicksGame</c> never advances during a case, which runs
        /// inside one frame, so a spawned hostile would sit behind a stale <c>None</c> and the
        /// case would assert nothing.
        /// </summary>
        internal static void ForceDangerRecheck(Map map)
        {
            AccessTools.Field(typeof(DangerWatcher), "lastUpdateTick")
                       ?.SetValue(map.dangerWatcher, -10000);
        }

        /// <summary>
        /// Makes the map calm rather than asserting that it is, and returns how many hostile
        /// targets it swept. A quicktest map is generated, not arranged, and can come up with
        /// something already hostile on it, which fails every control that opens with
        /// <c>DangerRating == None</c> and each assertion behind it, since the danger gate is
        /// one-directional and nothing dresses on a map under threat.
        ///
        /// <para><c>DangerWatcher.CalculateDangerRating</c> sums, over
        /// <c>attackTargetsCache.TargetsHostileToColony</c> through <c>AffectsStoryDanger</c>, only
        /// a Pawn (its <c>kindDef.combatPower</c>) or an unmanned mortar, so sweeping that whole
        /// set clears the rating with no arithmetic. The set is snapshotted first, because
        /// destroying a target mutates the cache being enumerated. It sweeps a threat staged by
        /// <see cref="Threat"/> too, so call it before staging one.</para>
        /// </summary>
        internal static int MakeCalm(Map map)
        {
            int swept = 0;
            foreach (IAttackTarget target in map.attackTargetsCache.TargetsHostileToColony.ToList())
            {
                Thing thing = target?.Thing;
                if (thing != null && !thing.Destroyed)
                {
                    thing.Destroy();
                    swept++;
                }
            }
            ForceDangerRecheck(map);
            return swept;
        }

        /// <summary>
        /// Asks the real decision function (<c>TryInsertSwap</c>) what it would do and leaves no
        /// trace: cancels a swap it started and clears the retry cooldown a refusal stamps, which
        /// would otherwise make later probes in the case return false for the wrong reason.
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
        /// <see cref="Diverts"/>, then drops whatever it queued. A divert enqueues the deferred job
        /// with its reservations held, so a meal or patient reserved by one probe would fail the
        /// next probe's dry run, and the case would read a reservation clash as a refusal.
        /// </summary>
        internal static bool Probe(Fixture fix, Job job, JobTag? tag = null)
        {
            bool diverted = Diverts(fix, job, tag);
            fix.Pawn.jobs.ClearQueuedJobs();
            return diverted;
        }

        /// <summary>
        /// Puts a pawn back where a probe found them (no swap, nothing queued, no retry cooldown),
        /// for cases that call <c>TryInsertSwap</c> directly to inspect what it left behind first.
        /// </summary>
        internal static void Settle(Pawn pawn)
        {
            if (pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }
            pawn.jobs.ClearQueuedJobs();
            Patch_JobInterception.LastBlockedTick.Remove(pawn.thingIDNumber);
        }

        /// <summary>
        /// A <c>Wait</c> job carrying <paramref name="giver"/>, aimed at the pawn's own cell so it
        /// reads as work done in the stand's room.
        /// </summary>
        internal static Job WorkJob(Fixture fix, WorkGiverDef giver)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Wait, fix.Pawn.Position);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>
        /// Makes interception throw <paramref name="times"/> times, via the real patch entry.
        /// </summary>
        internal static void Fault(Pawn pawn, int times)
        {
            Patch_JobInterception.injectFaults = times;
            for (int i = 0; i < times; i++)
            {
                Patch_JobInterception.Prefix(
                    JobMaker.MakeJob(JobDefOf.Wait), null, pawn, pawn.jobs);
            }
            // Disarm in case a call never reached the injection point.
            Patch_JobInterception.injectFaults = 0;
        }

        /// <summary>
        /// Walls the pad's edge and roofs and floors all of it, so the interior is a proper room.
        /// The functional cases need one: <c>FindAvailableStand</c> walks
        /// <c>room.ContainedThings</c>, and on the open pad that room is the whole outdoors. The
        /// lifecycle cases act on a ledger directly and do not.
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

        /// <summary>
        /// A stand stocked with a duster and a pawn dressed per <paramref name="kit"/> on its
        /// interaction cell, with no ledger yet: cases that watch the driver build one start here
        /// and call <see cref="RunSwap"/>. The stand sits one cell in from the pad's south-west
        /// corner. Null when the stand def, the duster or the comp cannot be had.
        /// </summary>
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

            // Gender is a parameter because vanilla's decency rule is asymmetric
            // (Pawn_ApparelTracker.PsychologicallyNude): a man is nude on missing trousers alone, a
            // woman on missing trousers or a bare torso. A male-only fixture would certify as safe
            // a whole class of stand configurations that strip a woman.
            Pawn pawn = DebugTools_Fixtures.AveragePawn(gender, "Test", capableOf);
            pawn.apparel?.DestroyAll();
            if (capableOf != null)
            {
                pawn.workSettings?.EnableAndInitialize();
            }
            // On the interaction cell, so the driver's Goto toil arrives at once and no path is
            // ever requested. Pathfinding is asynchronous: Pawn_PathFollower.PatherTick waits on a
            // path request served by a job system that the game's own update loop drives, not
            // Pawn.DoTick, so ticking one pawn leaves it moving forever, a cell short of the stand.
            // The cost is that these cases do not exercise the walk, which is vanilla's Toils_Goto;
            // they exist to prove the transfer.
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
        /// <see cref="Stage"/> in an enclosed room with a door in its west wall beside the stand,
        /// and the strip of ground outside that wall cleared, floored with soil and unfogged. For
        /// cases that need a colonist inside to reach something outside: hauling asks
        /// <c>CanReach</c> before it hands out a job
        /// (<c>HaulAIUtility.PawnCanAutomaticallyHaulFast</c>), and the return trip asks it of the
        /// stand before it starts a swap, so in a sealed room neither can happen and a case would
        /// pass for that reason.
        ///
        /// <para>The strip is the pad's one-cell margin, which
        /// <see cref="Patch_HarnessAutoRun.TryFindPad"/> keeps in bounds and in a room that touches
        /// the map edge, so a stand put there is in the open; soil because a quicktest map can put
        /// anything there, unfogged because a fogged item is never hauled.</para>
        /// </summary>
        internal static Fixture StageWithDoorway(Map map, CellRect pad)
        {
            return StageWithDoorway(map, pad, null);
        }

        /// <summary>
        /// <see cref="StageWithDoorway(Map, CellRect)"/> with a colonist who can do
        /// <paramref name="capableOf"/>: vanilla's opportunistic haul refuses anyone whose hauling
        /// is disabled, and a generated colonist's backstory can disable it.
        /// </summary>
        internal static Fixture StageWithDoorway(Map map, CellRect pad, WorkTypeDef capableOf)
        {
            Fixture fix = Stage(map, pad, StageKit.Displacing, enclose: true, capableOf: capableOf);
            if (fix == null)
            {
                return null;
            }
            CellRect outside = new CellRect(pad.minX - 1, pad.minZ, 1, pad.Height);
            if (!outside.InBounds(map))
            {
                return null;
            }
            IntVec3 doorCell = new IntVec3(pad.minX, 0, pad.minZ + 1);
            Building wall = doorCell.GetEdifice(map);
            if (wall != null)
            {
                wall.Destroy();
            }
            DebugTools_Fixtures.Spawn(map, ThingDefOf.Door, ThingDefOf.WoodLog, doorCell, Rot4.North);

            GenDebug.ClearArea(outside, map);
            foreach (IntVec3 cell in outside)
            {
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                map.fogGrid.Unfog(cell);
            }
            fix.Outside = outside;
            return fix;
        }

        /// <summary>
        /// <see cref="Stage"/> with a second, foreign assignable comp on the stand, the shape
        /// Outfit Stands Plus gives the stands it touches. It reuses one the mod list already
        /// supplies, for the reason <see cref="DebugTools_SaveRoundTrip.ForeignAssignable"/> gives;
        /// otherwise a plain <c>CompAssignableToPawn</c> serves, since the code under test names no
        /// foreign type.
        ///
        /// <para>The comp goes on the def, because comps are built from <c>def.comps</c> when the
        /// thing is made, and the edit is reverted as soon as the stand exists, so nothing made
        /// afterwards sees it. The stand keeps its comp while spawned, but a load rebuilds comps
        /// from the def and drops it.</para>
        /// </summary>
        internal static Fixture StageWithForeignAssignable(Map map, CellRect pad)
        {
            ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
            if (standDef == null)
            {
                return null;
            }
            CompProperties_AssignableToPawn added = null;
            if (!standDef.comps.Any(IsForeignAssignableProps))
            {
                added = new CompProperties_AssignableToPawn();
                standDef.comps.Add(added);
            }
            try
            {
                return Stage(map, pad, StageKit.Displacing);
            }
            finally
            {
                if (added != null)
                {
                    standDef.comps.Remove(added);
                }
            }
        }

        internal static bool IsForeignAssignableProps(CompProperties props)
        {
            return props != null && props.compClass != null
                   && typeof(CompAssignableToPawn).IsAssignableFrom(props.compClass)
                   && !typeof(CompAssignableToPawn_ShiftStand).IsAssignableFrom(props.compClass);
        }

        /// <summary>The first assignable comp on the stand that is not ours.</summary>
        internal static CompAssignableToPawn ForeignAssignableOn(ThingWithComps stand)
        {
            return stand?.AllComps.OfType<CompAssignableToPawn>()
                .FirstOrDefault(c => !(c is CompAssignableToPawn_ShiftStand));
        }

        /// <summary>The list names exactly these pawns, in any order, and nobody else.</summary>
        internal static bool Names(CompAssignableToPawn comp, params Pawn[] pawns)
        {
            List<Pawn> owners = comp.AssignedPawnsForReading;
            return owners.Count == pawns.Length && pawns.All(owners.Contains);
        }

        /// <summary>
        /// Strips a fixture stand to empty, so a case can state what it holds instead of working
        /// around the duster <see cref="Stage"/> stocks.
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
        /// Runs one swap through the real driver: starts the job on the pawn's own tracker, then
        /// ticks that pawn until the job ends. Returns false, after printing why, when the swap
        /// gets no driver, ends with anything but <c>Succeeded</c>, or outlasts
        /// <paramref name="maxTicks"/>. Every caller asserts on it, so a pump that gave up fails
        /// loudly instead of falling through to assertions that pass for the wrong reason.
        ///
        /// <para>One pawn is a complete pump: ticking it drives its pather and job tracker, the
        /// toil delay counts down in <c>JobDriver.DriverTick</c>, and nothing in this driver reads
        /// <c>TicksGame</c>, so no <c>TickManager</c>, world tick or other pawn's AI is
        /// involved.</para>
        /// </summary>
        internal static bool RunSwap(Fixture fix, int maxTicks = 5000)
        {
            Job swap = JobMaker.MakeJob(ShiftChangeDefOf.ShiftChange_SwapAtStand, fix.Stand);
            fix.Pawn.jobs.StartJob(swap, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null,
                JobTag.ChangingApparel);

            // Watch the driver, never the Job. Jobs are pooled: when ours ends it goes back to
            // JobMaker's pool and is handed out again as the pawn's next job, so `CurJob != swap`
            // compares a reference vanilla has already recycled and stays equal. Drivers are built
            // per job and never pooled.
            JobDriver started = fix.Pawn.jobs.curDriver;
            if (started == null)
            {
                Report.AppendLine("      the swap job never got a driver");
                return false;
            }

            // Capture how it ended, not only that it did: "the driver changed" is also true of a
            // job that failed its reservations and died before its transfer toil.
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
                    // Report what took the pawn: an end condition names the verb and leaves the
                    // actor unnamed.
                    Job replacement = fix.Pawn.CurJob;
                    Report.Append("        taken by ")
                          .Append(replacement == null ? "nothing" : replacement.def.defName)
                          .Append(", driver ")
                          .Append(fix.Pawn.jobs.curDriver == null
                                  ? "null" : fix.Pawn.jobs.curDriver.GetType().Name)
                          .Append(", giver ")
                          .Append(replacement == null || replacement.jobGiver == null
                                  ? "none" : replacement.jobGiver.GetType().Name)
                          .Append(", lastTag ")
                          .Append(fix.Pawn.mindState == null
                                  ? "none" : fix.Pawn.mindState.lastJobTag.ToString())
                          .Append(", playerForced ")
                          .Append(replacement != null && replacement.playerForced)
                          .Append(", queued ")
                          .Append(fix.Pawn.jobs.jobQueue == null ? -1 : fix.Pawn.jobs.jobQueue.Count)
                          .Append(", duty ")
                          .Append(fix.Pawn.mindState == null || fix.Pawn.mindState.duty == null
                                  ? "none" : fix.Pawn.mindState.duty.def.defName)
                          .Append(", lord ")
                          .Append(fix.Pawn.GetLord() == null
                                  ? "none" : fix.Pawn.GetLord().LordJob.GetType().Name)
                          .Append(", onShift ").Append(fix.Comp.OnShift)
                          .Append(", rest ")
                          .Append(fix.Pawn.needs?.rest == null
                                  ? "none" : fix.Pawn.needs.rest.CurLevelPercentage.ToStringPercent())
                          .Append(", ticks at zero rest ")
                          .Append(fix.Pawn.needs?.rest == null ? -1 : fix.Pawn.needs.rest.TicksAtZero)
                          .AppendLine();
                    return false;
                }
                // The game clock stands still while the harness runs, inside one frame, and the
                // pawn's needs are gated on it. The fixture only hands out colonists whose needs do
                // not tick under a stopped clock (DebugTools_Fixtures.NeedsTickWhilePumped), so
                // rest, food and mood hold still here without being topped up.
                //
                // This ticks the whole pawn, so a third-party Pawn.Tick postfix can throw here; the
                // case-level catch reports that as `FAIL threw:` rather than swallowing it.
                fix.Pawn.DoTick();
            }

            // A bare timeout costs a debugging cycle: say where it got stuck.
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
        /// A checked-out fixture, put on shift the way
        /// <see cref="JobDriver_SwapAtStand.DoTransfer"/> does it: plan from
        /// <see cref="SwapPlan"/>, move the apparel, then <c>NotifyDressed</c> with an empty
        /// stored-forced list. The pawn wears the researcher's starting kit (tunic, duster,
        /// helmet). The stand holds Vanilla Apparel Expanded's lab coat, which displaces the
        /// duster, or without that mod (the four-mod list) a vanilla shirt and trousers, which
        /// displace the tunic. A ledger with nothing stored is no use to the assertions that
        /// follow, so Build returns null without one.
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

            // The driver's own sequence: plan from SwapPlan, own clothes into the stand, uniform
            // out and on.
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
        /// Removes what a case left: pawns and their registry entries, the stand, areas, zones,
        /// claimed cells and the second map, then clears the pad's things and roofs. Terrain stays
        /// as the case left it. Destroying the stand fires our own release path, which is fine
        /// since the assertions have run.
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
                for (int i = 0; i < fix.Areas.Count; i++)
                {
                    Area area = fix.Areas[i];
                    if (area != null && map.areaManager.AllAreas.Contains(area))
                    {
                        area.Delete();
                    }
                }
                // The doorway fixture's strip lies outside the pad, so the pad clear below never
                // reaches it. A stockpile a case left there would be a haul destination for every
                // later case.
                if (fix.Outside.Area > 0)
                {
                    foreach (IntVec3 cell in fix.Outside)
                    {
                        map.zoneManager.ZoneAt(cell)?.Delete();
                    }
                    GenDebug.ClearArea(fix.Outside, map);
                }
                for (int i = 0; i < fix.Claimed.Count; i++)
                {
                    IntVec3 cell = fix.Claimed[i];
                    map.zoneManager.ZoneAt(cell)?.Delete();
                    GenDebug.ClearArea(CellRect.SingleCell(cell), map);
                }
                // After the pawn and the stand, which may be standing on it. Caught: this runs in
                // the case's finally, and a throw would end the run for every case after this one.
                if (fix.Pocket != null && Find.Maps.Contains(fix.Pocket))
                {
                    try
                    {
                        RemoveSecondMap(fix.Pocket);
                    }
                    catch (Exception e)
                    {
                        Fail("teardown could not remove the second map: " + e);
                    }
                }
            }
            GenDebug.ClearArea(pad, map);
        }

        /// <summary>Side of the square map <see cref="SecondMap"/> makes.</summary>
        internal const int SecondMapSize = 50;

        /// <summary>
        /// A second map, for a gravship flight that leaves the borrower and their stand on
        /// different maps; null when Odyssey's generator is missing. The caller puts it on
        /// <see cref="Fixture.Pocket"/> so <see cref="Teardown"/> removes it.
        ///
        /// <para>A pocket map, the one kind the engine makes without a world tile
        /// (<c>PocketMapUtility.GeneratePocketMap</c>), from Odyssey's <c>SpacePocket</c>
        /// generator, which only lays terrain. Removing it (<see cref="RemoveSecondMap"/>) reaches
        /// <c>Game.DeinitAndRemoveMap</c>, as an origin map abandoned after a takeoff does.</para>
        ///
        /// <para>The generator turns the region and room updater off, and a gravship landing turns
        /// it back on before it spawns anything
        /// (<c>GravshipPlacementUtility.PlaceGravshipInMap</c>). So does this, so a stand set down
        /// here gets a room as a landed one would.</para>
        ///
        /// <para>It also builds the map's drawing sections. A map is drawn into sections only the
        /// first time it is drawn (<c>MapDrawer.RegenerateEverythingNow</c>), and
        /// <c>MapDrawer.Dispose</c> walks them with no null check, so removing a map nobody ever
        /// looked at throws. Vanilla's debug action makes its new pocket map the current map, and
        /// an origin map in play has always been drawn.</para>
        /// </summary>
        internal static Map SecondMap(Map source)
        {
            MapGeneratorDef generator = DefDatabase<MapGeneratorDef>.GetNamedSilentFail("SpacePocket");
            if (generator == null)
            {
                return null;
            }
            Map map = PocketMapUtility.GeneratePocketMap(
                new IntVec3(SecondMapSize, 1, SecondMapSize), generator, null, source);
            map.regionAndRoomUpdater.Enabled = true;
            map.mapDrawer.RegenerateEverythingNow();
            return map;
        }

        /// <summary>
        /// Removes a <see cref="SecondMap"/>, destroying any pawn still on it first: map removal
        /// passes the pawns it finds to the world (<c>MapDeiniter.PassPawnsToWorld</c>), which
        /// would keep a fixture colonist in the game after the case.
        ///
        /// <para>It also drops the redraw the map queued for itself. A new map asks for its first
        /// full redraw once the long event it was made in is over (<c>Map.FinalizeInit</c>, through
        /// <c>LongEventHandler.ExecuteWhenFinished</c>). The headless run is itself one of those
        /// queued actions, so the redraw would run after the harness, on a map already gone, and
        /// log a failure for every layer of every section; <see cref="SecondMap"/> drew the map
        /// already. The queue is walked by index and this map's entries sit after the running
        /// harness's, so removing them is safe. A run from the debug menu queues nothing, since
        /// there the redraw runs at once.</para>
        /// </summary>
        internal static void RemoveSecondMap(Map away)
        {
            List<Action> queued = AccessTools.Field(typeof(LongEventHandler), "toExecuteWhenFinished")
                ?.GetValue(null) as List<Action>;
            queued?.RemoveAll(action => action != null && action.Target == away);

            List<Pawn> left = away.mapPawns.AllPawns.ToList();
            for (int i = 0; i < left.Count; i++)
            {
                CompShiftStand.OnShiftStands.Remove(left[i]);
                if (!left[i].Destroyed)
                {
                    left[i].Destroy();
                }
            }
            PocketMapUtility.DestroyPocketMap(away);
        }

        /// <summary>
        /// <see cref="Build"/> on a <see cref="SecondMap"/>: the stand and its borrower, on shift,
        /// both on the second map, for a case that flies the borrower to the harness's own map and
        /// leaves the stand behind. The fixture's <c>Map</c> is the second map, so the case is
        /// handed the harness's map and pad itself.
        /// </summary>
        internal static Fixture BuildOnSecondMap(Map map, CellRect pad)
        {
            Map away = SecondMap(map);
            if (away == null)
            {
                return null;
            }
            Fixture fix = null;
            try
            {
                fix = Build(away, CellRect.CenteredOn(away.Center, PadSize / 2));
            }
            finally
            {
                // A build that threw or came back empty leaves no fixture to
                // carry the map to Teardown, so it goes here.
                if (fix == null)
                {
                    RemoveSecondMap(away);
                }
            }
            if (fix != null)
            {
                fix.Pocket = away;
            }
            return fix;
        }

        /// <summary>
        /// Makes <paramref name="cell"/>, past the pad, bare soil with nothing on it and no roof,
        /// for a case to put things on, and has <see cref="Teardown"/> clear it again. A cell
        /// outside the map is returned untouched, for the caller's bounds check.
        ///
        /// <para>The pad finder checks one cell of margin around the pad
        /// (<c>Patch_HarnessAutoRun.TryFindPad</c>) and these cells lie further out, so on some
        /// maps they hold natural rock. A meal spawned onto rock reads as being in the cell beside
        /// it. And whatever a case left there would stay on the map for every later case, where
        /// vanilla's opportunistic haul can pick it up.</para>
        /// </summary>
        internal static IntVec3 OpenGround(Fixture fix, IntVec3 cell)
        {
            Map map = fix.Map;
            if (!cell.InBounds(map))
            {
                return cell;
            }
            GenDebug.ClearArea(CellRect.SingleCell(cell), map);
            map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
            fix.Claimed.Add(cell);
            return cell;
        }

    }
}
#endif
