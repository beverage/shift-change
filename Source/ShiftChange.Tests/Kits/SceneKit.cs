using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Setting up what the tests play on, the pad and the <see cref="Scene"/> a test stages on it,
    /// the swap, and the teardown, shared by every test. Most
    /// tests build a throwaway scene, drive it, check the outcome and tear it down
    /// (<see cref="StoryFixture"/>); a lifecycle test fires one real engine event at a checked-out
    /// stand and checks where the ledger lands.
    ///
    /// <para>Each lifecycle test splits in two. The engine's half is read from the game's code and
    /// taken as given: a gravship flight, say, despawns what is aboard with <c>WillReplace</c>. The
    /// test drives our half: given that call, where the ledger lands.</para>
    ///
    /// <para>Fire lifecycle events through the engine's own entry points, never our comp
    /// handlers directly, so the engine's comp dispatch, its ordering and its side effects run
    /// for real and only the orchestration around them is simulated. docs/TESTING.md, "Rules the
    /// suite follows".</para>
    ///
    /// <para>Two kinds of scene. Lifecycle tests hand-assemble a checked-out state
    /// (<see cref="Build"/>) to test what happens to a ledger afterwards. Driver tests stage an
    /// undressed pawn (<see cref="Stage"/>) and run <see cref="JobDriver_SwapAtStand"/> for real
    /// through the pawn's own tracker (<see cref="RunSwap"/>), to test whether the driver builds
    /// that ledger.</para>
    ///
    /// <para>Engine traps shape the scenes (docs/TESTING.md, "Five engine traps"). Jobs are pooled,
    /// so <see cref="RunSwap"/> watches the driver, not the job. A generated colonist is not
    /// tended, so staging removes what it arrives needing tended. And the swap steps the
    /// game's own clock, so everything on the map moves while it runs: a test holds still what it
    /// arranged (<see cref="Scene.Hold"/>, <see cref="SceneKit.StayPut"/>).</para>
    /// </summary>
    internal static class SceneKit
    {
        /// <summary>Side of the pad every test is handed, cleared and rebuilt per test.</summary>
        internal const int PadSize = 7;

        /// <summary>
        /// Somewhere the tests can stand: the map centre if usable, so runs are comparable, and a
        /// random cell otherwise. The pad is cleared before
        /// use, so the bar is a pad in bounds whose origin is standable and not water, not an
        /// empty one.
        ///
        /// <para>Every cell of the pad and its one-cell margin must be in a room that touches the
        /// map edge. Tests reach past their pad: the exterior-wall test counts the ring around a
        /// wall on the pad's edge, and some tests need a stand in the open, which the mod tests
        /// with <c>Room.TouchesMapEdge</c>, on the margin outside a door or on the pad left
        /// unwalled.
        /// Natural rock has no room, and a valley ringed by mountains, an island in deep water or a
        /// ruin's walls give the margin a room that does not reach the edge. The margin is a closed
        /// ring around the pad, so it still reaches the edge once the tests build walls on the pad.
        /// A map whose region updater a failed load left off is refused without a search, which
        /// <see cref="SavingAndLoading.CannotStage"/> relies on.</para>
        /// </summary>
        internal static bool TryFindPad(Map map, out IntVec3 origin)
        {
            // When a prefix on Map.FinalizeLoading throws, the body that turns the map's
            // RegionAndRoomUpdater back on never runs, so its rooms are never built and every
            // room lookup on it logs a warning.
            if (!map.regionAndRoomUpdater.Enabled)
            {
                origin = IntVec3.Invalid;
                return false;
            }
            Predicate<IntVec3> usable = c =>
            {
                CellRect pad = new CellRect(c.x, c.z, PadSize, PadSize);
                if (!pad.ExpandedBy(1).InBounds(map)
                    || !c.Standable(map)
                    || c.GetTerrain(map).IsWater)
                {
                    return false;
                }
                foreach (IntVec3 cell in pad.ExpandedBy(1))
                {
                    Room room = cell.GetRoom(map);
                    if (room == null || !room.TouchesMapEdge)
                    {
                        return false;
                    }
                }
                return true;
            };

            IntVec3 centre = map.Center - new IntVec3(PadSize / 2, 0, PadSize / 2);
            if (usable(centre))
            {
                origin = centre;
                return true;
            }
            return CellFinderLoose.TryGetRandomCellWith(usable, map, 500, out origin);
        }

        /// <summary>
        /// Scene builds are retried because spawning a freshly generated pawn is unreliable on a
        /// large mod list and the failure is not ours: <c>Pawn.SpawnSetup</c> calls
        /// <c>Pawn_HealthTracker.Notify_Spawned</c>, which walks the pawn's hediffs, and a hediff
        /// that adds or removes another there throws <c>Collection was modified</c>. Every retry
        /// is printed, so an intermittent bug of ours still shows.
        /// </summary>
        internal const int BuildAttempts = 3;

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
        internal static Job JoyJob(Scene scene, JobDef def)
        {
            return JobMaker.MakeJob(def, scene.Colonist.Position, scene.Colonist.Position);
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
        internal static void ClearThreat(Scene scene, Pawn threat)
        {
            if (threat != null && !threat.Destroyed)
            {
                threat.Destroy();
            }
            ForceDangerRecheck(scene.Map);
        }

        /// <summary>
        /// A live hostile on the map, which is all <c>StoryDanger</c> is: <c>DangerWatcher</c> sums
        /// <c>kindDef.combatPower</c> over the attack-target cache. It stands inert only while
        /// nothing ticks: clear it (<see cref="ClearThreat"/>) before the test steps the clock,
        /// or it moves and attacks, and the colonist reacts to it.
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
        internal static Pawn Threat(Scene scene)
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
            IntVec3 cell = scene.OutfitStand.Position + new IntVec3(3, 0, 3);
            if (!cell.InBounds(scene.Map))
            {
                cell = scene.Colonist.Position;
            }
            Pawn threat = PawnGenerator.GeneratePawn(kind, hostile);
            GenSpawn.Spawn(threat, cell, scene.Map, Rot4.North);
            scene.Map.fogGrid.Unfog(threat.Position);
            // Registered so Teardown removes it if the test throws before ClearThreat.
            scene.Extras.Add(threat);
            ForceDangerRecheck(scene.Map);
            return threat;
        }

        /// <summary>
        /// Drops <c>DangerWatcher</c>'s cache stamp so the next read recomputes. The rating is
        /// cached for 101 ticks, and a test arranges and reads the map between ticks, usually
        /// with none in between, so a spawned hostile would sit behind a stale <c>None</c> and the
        /// test would assert nothing.
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
        /// Whether <paramref name="job"/> sends the colonist to the stand to change, asked of the
        /// real decision function (<c>TryInsertSwap</c>). Leaves no trace but the job it put off,
        /// still queued: cancels a change it started and clears the retry cooldown a refusal
        /// stamps, which would otherwise make later asks in the test return false for the wrong
        /// reason.
        /// </summary>
        internal static bool SendsToChangeKeepingQueue(Scene scene, Job job, JobTag? tag = null)
        {
            bool inserted = Patch_JobInterception.TryInsertSwap(job, tag, scene.Colonist, scene.Colonist.jobs);
            if (inserted && scene.Colonist.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand)
            {
                scene.Colonist.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }
            Patch_JobInterception.LastBlockedTick.Remove(scene.Colonist.thingIDNumber);
            return inserted;
        }

        /// <summary>
        /// <see cref="SendsToChangeKeepingQueue"/>, then drops the job it put off. That job waits
        /// in the queue with its reservations held, so a meal or patient reserved by one ask would
        /// fail the next one's dry run, and the test would read a reservation clash as a refusal.
        /// </summary>
        internal static bool SendsToChange(Scene scene, Job job, JobTag? tag = null)
        {
            bool sends = SendsToChangeKeepingQueue(scene, job, tag);
            scene.Colonist.jobs.ClearQueuedJobs();
            return sends;
        }

        /// <summary>
        /// Puts a pawn back as they were before <c>TryInsertSwap</c> was asked (no change, nothing
        /// queued, no retry cooldown), for tests that call it directly to inspect what it left
        /// behind first.
        /// </summary>
        internal static void ClearSwapDecision(Pawn pawn)
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
        internal static Job WorkJob(Scene scene, WorkGiverDef giver)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Wait, scene.Colonist.Position);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>Toggles each of <paramref name="works"/> on the scene's stand.</summary>
        internal static void ToggleEach(Scene scene, IEnumerable<WorkTypeDef> works)
        {
            foreach (WorkTypeDef work in works)
            {
                scene.Comp.ToggleWork(work);
            }
        }

        /// <summary>
        /// Makes interception throw <paramref name="times"/> times, via the real patch entry. A
        /// Harmony prefix on <c>TryInsertSwap</c> throws in its place, so each throw reaches the
        /// catch in <see cref="Patch_JobInterception.Prefix"/> as a real one would, and comes off
        /// again before this returns, so the shipped code carries nothing for the test.
        /// </summary>
        internal static void Fault(Pawn pawn, int times)
        {
            Harmony harmony = new Harmony(FaultHarmonyId);
            MethodInfo decide = AccessTools.Method(typeof(Patch_JobInterception),
                                                   nameof(Patch_JobInterception.TryInsertSwap));
            harmony.Patch(decide, prefix: new HarmonyMethod(typeof(SceneKit), nameof(ThrowInstead)));
            try
            {
                for (int i = 0; i < times; i++)
                {
                    Patch_JobInterception.Prefix(JobMaker.MakeJob(JobDefOf.Wait), null, pawn, pawn.jobs);
                }
            }
            finally
            {
                harmony.Unpatch(decide, HarmonyPatchType.Prefix, FaultHarmonyId);
            }
        }

        /// <summary>The Harmony id <see cref="Fault"/> patches under.</summary>
        internal const string FaultHarmonyId = "MrBeverage.ShiftChange.Tests.Fault";

        /// <summary>
        /// <see cref="Fault"/>'s prefix: throws before <c>TryInsertSwap</c> runs.
        /// </summary>
        internal static bool ThrowInstead()
        {
            throw new Exception("[ShiftChange] injected test fault");
        }

        /// <summary>
        /// Walls the pad's edge and roofs and floors all of it, so the interior is a proper room.
        /// The functional tests need one: <c>FindAvailableStand</c> walks
        /// <c>room.ContainedThings</c>, and on the open pad that room is the whole outdoors. The
        /// lifecycle tests act on a ledger directly and do not.
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
        /// interaction cell, with no ledger yet: tests that watch the driver build one start here
        /// and call <see cref="RunSwap"/>. The stand sits one cell in from the pad's south-west
        /// corner. Null when the stand def, the duster or the comp cannot be had.
        /// </summary>
        internal static Scene Stage(Map map, CellRect pad, StageKit kit,
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
            // woman on missing trousers or a bare torso. A male-only scene would certify as safe
            // a whole class of stand configurations that strip a woman.
            Pawn pawn = DebugTools_Fixtures.AveragePawn(gender, "Test", capableOf);
            pawn.apparel?.DestroyAll();
            if (capableOf != null)
            {
                pawn.workSettings?.EnableAndInitialize();
            }
            // On the interaction cell, so the swap starts without a walk and a test's checks are
            // about the change alone. ChangingAtAStand.WalksToTheStand covers the walk, which is
            // vanilla's Toils_Goto.
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
            return new Scene { Map = map, OutfitStand = stand, Colonist = pawn, Comp = comp, StoredCount = 0 };
        }

        /// <summary>
        /// <see cref="Stage"/> in an enclosed room with a door in its west wall beside the stand,
        /// and the strip of ground outside that wall cleared, floored with soil and unfogged. For
        /// tests that need a colonist inside to reach something outside: hauling asks
        /// <c>CanReach</c> before it hands out a job
        /// (<c>HaulAIUtility.PawnCanAutomaticallyHaulFast</c>), and the return trip asks it of the
        /// stand before it starts a swap, so in a sealed room neither can happen and a test would
        /// pass for that reason.
        ///
        /// <para>The strip is the pad's one-cell margin, which
        /// <see cref="TryFindPad"/> keeps in bounds and in a room that
        /// touches the map edge, so a stand put there is in the open; soil because a generated map
        /// can put anything there, unfogged because a fogged item is never hauled.</para>
        /// </summary>
        internal static Scene StageWithDoorway(Map map, CellRect pad)
        {
            return StageWithDoorway(map, pad, null);
        }

        /// <summary>
        /// <see cref="StageWithDoorway(Map, CellRect)"/> with a colonist who can do
        /// <paramref name="capableOf"/>: vanilla's opportunistic haul refuses anyone whose hauling
        /// is disabled, and a generated colonist's backstory can disable it.
        /// </summary>
        internal static Scene StageWithDoorway(Map map, CellRect pad, WorkTypeDef capableOf)
        {
            Scene scene = Stage(map, pad, StageKit.Displacing, enclose: true, capableOf: capableOf);
            if (scene == null)
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
            scene.Outside = outside;
            return scene;
        }

        /// <summary>
        /// <see cref="Stage"/> with a second, foreign assignable comp on the stand, the shape
        /// Outfit Stands Plus gives the stands it touches. It reuses one the mod list already
        /// supplies, as <see cref="SavingAndLoading.AnotherModsOwnerComesThroughALoad"/> explains;
        /// otherwise a plain <c>CompAssignableToPawn</c> serves, since the code under test names
        /// no foreign type.
        ///
        /// <para>The comp goes on the def, because comps are built from <c>def.comps</c> when the
        /// thing is made, and the edit is reverted as soon as the stand exists, so nothing made
        /// afterwards sees it. The stand keeps its comp while spawned, but a load rebuilds comps
        /// from the def and drops it.</para>
        /// </summary>
        internal static Scene StageWithForeignAssignable(Map map, CellRect pad)
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
        internal static bool OwnersAre(CompAssignableToPawn comp, params Pawn[] pawns)
        {
            List<Pawn> owners = comp.AssignedPawnsForReading;
            return owners.Count == pawns.Length && pawns.All(owners.Contains);
        }

        /// <summary>
        /// Strips a scene's stand to empty, so a test can state what it holds instead of working
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
        /// A stand-in for <paramref name="giver"/>'s work at <paramref name="at"/> that a freed
        /// stand's catch-up may interrupt: a posture wait stamped with the giver and aimed at the
        /// cell, which is where the catch-up reads the work as happening. It is suspendable and
        /// casually interruptible, as the catch-up asks, and stays open however long the clock
        /// runs, as real work would. A Goto ends once the pawn gets there, and Core's Wait is not
        /// suspendable.
        /// </summary>
        internal static Job WorkingAt(WorkGiverDef giver, IntVec3 at)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, at);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>
        /// Keeps a patient where the test put them while the clock steps, as one in bed would
        /// stay: a posture wait with no end, which only the test or an emergency replaces. A pawn
        /// spawned for a test otherwise picks jobs of its own once the clock runs, and wanders off.
        /// </summary>
        internal static void StayPut(Pawn pawn)
        {
            pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture), JobCondition.InterruptForced,
                null, resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);
        }

        /// <summary>Whether <paramref name="pawn"/> is still on a <see cref="WorkingAt"/> job from <paramref name="giver"/>.</summary>
        internal static bool StillWorking(Pawn pawn, WorkGiverDef giver)
        {
            return pawn.CurJobDef == JobDefOf.Wait_MaintainPosture && pawn.CurJob.workGiverDef == giver;
        }

        /// <summary>
        /// Starts a change at the scene's stand on the colonist's own job tracker and returns its
        /// driver, with <paramref name="ended"/> told how it ends. Null, with the reason in
        /// <paramref name="notStarted"/>, when the change is not what runs once
        /// <c>StartJob</c> returns: a job can end as it starts (a failed reservation, the game's
        /// guard against ten jobs in one tick) and leave another job's driver current, and
        /// watching that driver would take its end for the change's.
        ///
        /// <para>Watch the driver, never the Job. Jobs are pooled: when ours ends it goes back to
        /// JobMaker's pool and is handed out again as the pawn's next job, so <c>CurJob != swap</c>
        /// compares a reference vanilla has already recycled and stays equal. Drivers are built
        /// per job and never pooled.</para>
        /// </summary>
        internal static JobDriver StartSwap(Scene scene, Action<JobCondition> ended, out string notStarted)
        {
            Job swap = JobMaker.MakeJob(ShiftChangeDefOf.ShiftChange_SwapAtStand, scene.OutfitStand);
            scene.Colonist.jobs.StartJob(swap, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null,
                JobTag.ChangingApparel);
            JobDriver started = scene.Colonist.jobs.curDriver;
            if (!(started is JobDriver_SwapAtStand))
            {
                notStarted = "the change ended as it started, and "
                             + (scene.Colonist.CurJobDef == null ? "no job" : scene.Colonist.CurJobDef.defName)
                             + " took its place";
                return null;
            }
            started.AddFinishAction(ended);
            notStarted = null;
            return started;
        }

        /// <summary>
        /// Runs one swap through the real driver: starts it on the pawn's own tracker
        /// (<see cref="StartSwap"/>), then steps the game's clock (<see cref="GameClock.Step"/>),
        /// within this frame, until it ends. Returns false, after saying why to
        /// <paramref name="why"/> a line at a time, when the swap does not start, ends with
        /// anything but <c>Succeeded</c>, or outlasts
        /// <paramref name="maxTicks"/>. Every caller checks it, so a swap that gave up fails
        /// loudly instead of falling through to checks that pass for the wrong reason.
        ///
        /// <para>The whole game ticks, as in play: the map, every pawn on it, the test's other
        /// colonists included, and the clock that needs and cooldowns read, none of which ticking
        /// the one pawn alone would do (docs/TESTING.md, "Five engine traps").</para>
        /// </summary>
        internal static bool RunSwap(Scene scene, Action<string> why, int maxTicks)
        {
            // Capture how it ended, not only that it did: "the driver changed" is also true of a
            // job that failed its reservations and died before its transfer toil.
            JobCondition ended = JobCondition.None;
            JobDriver started = StartSwap(scene, c => ended = c, out string notStarted);
            if (started == null)
            {
                why(notStarted);
                return false;
            }

            GameClock.Step(() => scene.Colonist.jobs?.curDriver != started, maxTicks, scene.Hold);
            if (scene.Colonist.jobs?.curDriver != started)
            {
                if (ended == JobCondition.Succeeded)
                {
                    return true;
                }
                why("the swap ended with " + ended + ", not Succeeded — toil " + started.CurToilIndex
                    + ", pawn at " + scene.Colonist.Position + " vs cell " + scene.OutfitStand.InteractionCell);
                // Say what took the pawn: an end condition names the verb and leaves the
                // actor unnamed.
                Job replacement = scene.Colonist.CurJob;
                why(new StringBuilder("  taken by ")
                      .Append(replacement == null ? "nothing" : replacement.def.defName)
                      .Append(", driver ")
                      .Append(scene.Colonist.jobs?.curDriver == null
                              ? "null" : scene.Colonist.jobs.curDriver.GetType().Name)
                      .Append(", giver ")
                      .Append(replacement == null || replacement.jobGiver == null
                              ? "none" : replacement.jobGiver.GetType().Name)
                      .Append(", lastTag ")
                      .Append(scene.Colonist.mindState == null
                              ? "none" : scene.Colonist.mindState.lastJobTag.ToString())
                      .Append(", playerForced ")
                      .Append(replacement != null && replacement.playerForced)
                      .Append(", queued ")
                      .Append(scene.Colonist.jobs?.jobQueue == null ? -1 : scene.Colonist.jobs.jobQueue.Count)
                      .Append(", duty ")
                      .Append(scene.Colonist.mindState == null || scene.Colonist.mindState.duty == null
                              ? "none" : scene.Colonist.mindState.duty.def.defName)
                      .Append(", lord ")
                      .Append(scene.Colonist.GetLord() == null
                              ? "none" : scene.Colonist.GetLord().LordJob.GetType().Name)
                      .Append(", onShift ").Append(scene.Comp.OnShift)
                      .Append(", rest ")
                      .Append(scene.Colonist.needs?.rest == null
                              ? "none" : scene.Colonist.needs.rest.CurLevelPercentage.ToStringPercent())
                      .Append(", ticks at zero rest ")
                      .Append(scene.Colonist.needs?.rest == null ? -1 : scene.Colonist.needs.rest.TicksAtZero)
                      .ToString());
                return false;
            }

            // A bare timeout costs a debugging cycle: say where it got stuck.
            JobDriver driver = scene.Colonist.jobs?.curDriver;
            JobDriver_SwapAtStand ours = driver as JobDriver_SwapAtStand;
            why(new StringBuilder("stuck after ").Append(maxTicks).Append(" ticks — driver ")
                  .Append(driver == null ? "null" : driver.GetType().Name)
                  .Append(", toil ").Append(driver == null ? -1 : driver.CurToilIndex)
                  .Append(", ticksLeft ").Append(driver == null ? -1 : driver.ticksLeftThisToil)
                  .Append(", sameDriver ").Append(scene.Colonist.jobs?.curDriver == started)
                  .Append(", undressing ").Append(ours != null && ours.undressing)
                  .Append(", toWear ").Append(ours == null ? -1 : ours.toWear.Count)
                  .Append(", toStore ").Append(ours == null ? -1 : ours.toStore.Count)
                  .Append(", onShift ").Append(scene.Comp.OnShift)
                  .Append(", moving ").Append(scene.Colonist.pather != null && scene.Colonist.pather.Moving)
                  .Append(", at ").Append(scene.Colonist.Position)
                  .Append(" vs cell ").Append(scene.OutfitStand.InteractionCell)
                  .Append(", stanceBusy ")
                  .Append(scene.Colonist.stances != null && scene.Colonist.stances.FullBodyBusy)
                  .ToString());
            return false;
        }

        /// <summary>
        /// A checked-out scene, put on shift the way
        /// <see cref="JobDriver_SwapAtStand.DoTransfer"/> does it: plan from
        /// <see cref="SwapPlan"/>, move the apparel, then <c>NotifyDressed</c> with an empty
        /// stored-forced list. The pawn wears the researcher's starting kit (tunic, duster,
        /// helmet). The stand holds Vanilla Apparel Expanded's lab coat, which displaces the
        /// duster, or without that mod (the four-mod list) a vanilla shirt and trousers, which
        /// displace the tunic. A ledger with nothing stored is no use to the assertions that
        /// follow, so Build returns null without one.
        /// </summary>
        internal static Scene Build(Map map, CellRect pad)
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

            return new Scene
            {
                Map = map,
                OutfitStand = stand,
                Colonist = pawn,
                Comp = comp,
                StoredCount = stored.Count,
            };
        }

        /// <summary>
        /// Removes what a test left: pawns and their registry entries, the stand, areas, zones,
        /// claimed cells and the second map, then clears the pad's things and roofs. Terrain stays
        /// as the test left it. Destroying the stand fires our own release path, which is fine
        /// since the assertions have run.
        /// </summary>
        internal static void Teardown(Scene scene, Map map, CellRect pad)
        {
            if (scene != null)
            {
                for (int i = 0; i < scene.Extras.Count; i++)
                {
                    Pawn extra = scene.Extras[i];
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
                if (scene.Colonist != null)
                {
                    CompShiftStand.OnShiftStands.Remove(scene.Colonist);
                    if (!scene.Colonist.Destroyed)
                    {
                        scene.Colonist.Destroy();
                    }
                }
                if (scene.OutfitStand != null && !scene.OutfitStand.Destroyed)
                {
                    scene.OutfitStand.Destroy();
                }
                // What the test made lies on the scene's map, which for a ship or a second-map
                // scene is not the pad's.
                Map own = scene.Map ?? map;
                for (int i = 0; i < scene.Areas.Count; i++)
                {
                    Area area = scene.Areas[i];
                    if (area != null && own.areaManager.AllAreas.Contains(area))
                    {
                        area.Delete();
                    }
                }
                // The doorway scene's strip lies outside the pad, so the pad clear below never
                // reaches it. A stockpile a test left there would be a haul destination for every
                // later test.
                if (scene.Outside.Area > 0)
                {
                    foreach (IntVec3 cell in scene.Outside)
                    {
                        own.zoneManager.ZoneAt(cell)?.Delete();
                    }
                    GenDebug.ClearArea(scene.Outside, own);
                }
                for (int i = 0; i < scene.Claimed.Count; i++)
                {
                    IntVec3 cell = scene.Claimed[i];
                    own.zoneManager.ZoneAt(cell)?.Delete();
                    GenDebug.ClearArea(CellRect.SingleCell(cell), own);
                }
                // After the pawn and the stand, which may be standing on it. Caught: this runs in
                // the test's finally, and a throw would end the run for every test after this one.
                if (scene.SecondMap != null && Find.Maps.Contains(scene.SecondMap))
                {
                    try
                    {
                        RemoveSecondMap(scene.SecondMap);
                    }
                    catch (Exception e)
                    {
                        // The whole exception in Test.log, its first line in the result.
                        DevTools.DevLog.Write("teardown could not remove the second map: " + e);
                        DevTools.Testing.Test.Fail("teardown could not remove the second map: "
                                                   + e.GetType().Name + ": " + e.Message);
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
        /// <see cref="Scene.SecondMap"/> so <see cref="Teardown"/> removes it.
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
        /// would keep a scene's colonist in the game after the test.
        ///
        /// <para>It also drops the redraw the map queued for itself. A new map asks for its first
        /// full redraw once the long event it was made in is over (<c>Map.FinalizeInit</c>, through
        /// <c>LongEventHandler.ExecuteWhenFinished</c>). A test running inside a long event would
        /// see the redraw run after it, on a map already gone, logging a failure for every layer
        /// of every section; <see cref="SecondMap"/> drew the map already. The queue is walked by
        /// index and this map's entries sit after the running test's, so removing them is safe.
        /// With no long event running nothing is queued, since the redraw then runs at once.</para>
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
        /// both on the second map, for a test that flies the borrower to DevTools' map and
        /// leaves the stand behind. The scene's <c>Map</c> is the second map, so the test is
        /// handed DevTools' map and pad itself.
        /// </summary>
        internal static Scene BuildOnSecondMap(Map map, CellRect pad)
        {
            Map away = SecondMap(map);
            if (away == null)
            {
                return null;
            }
            Scene scene = null;
            try
            {
                scene = Build(away, CellRect.CenteredOn(away.Center, PadSize / 2));
            }
            finally
            {
                // A build that threw or came back empty leaves no scene to
                // carry the map to Teardown, so it goes here.
                if (scene == null)
                {
                    RemoveSecondMap(away);
                }
            }
            if (scene != null)
            {
                scene.SecondMap = away;
            }
            return scene;
        }

        /// <summary>
        /// Makes <paramref name="cell"/>, past the pad, bare soil with nothing on it and no roof,
        /// for a test to put things on, and has <see cref="Teardown"/> clear it again. A cell
        /// outside the map is returned untouched, for the caller's bounds check.
        ///
        /// <para>The pad finder checks one cell of margin around the pad
        /// (<see cref="TryFindPad"/>) and these cells lie further out, so on
        /// some maps they hold natural rock. A meal spawned onto rock reads as being in the cell
        /// beside it. And whatever a test left there would stay on the map for every later test,
        /// where vanilla's opportunistic haul can pick it up.</para>
        /// </summary>
        internal static IntVec3 OpenGround(Scene scene, IntVec3 cell)
        {
            Map map = scene.Map;
            if (!cell.InBounds(map))
            {
                return cell;
            }
            GenDebug.ClearArea(CellRect.SingleCell(cell), map);
            map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
            scene.Claimed.Add(cell);
            return cell;
        }

    }
}
