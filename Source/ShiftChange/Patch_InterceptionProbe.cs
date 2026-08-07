using System.Collections.Generic;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShiftChange
{
    /// <summary>
    /// THE SPIKE (BL-049). Observes only — it moves no apparel, starts no job
    /// and changes no state. Its whole purpose is to answer the one question
    /// the backlog item flagged as high risk before any of the rest is built:
    ///
    ///   <b>Is there a point in automatic job assignment where we know enough
    ///   to decide a swap, and can act on it?</b>
    ///
    /// Automatic Swap Outfit answered it by riding
    /// <c>Pawn_JobTracker.TryOpportunisticJob</c>, and that answer is wrong:
    /// vanilla bails out of that method when the pawn is drafted
    /// (<c>Pawn_JobTracker.cs:628</c>) and again unless the incoming job's def
    /// sets <c>allowOpportunisticPrefix</c> (<c>:657</c>). Only 164 of 321
    /// shipped JobDefs do, and <c>TendPatient</c> is not one of them — so the
    /// headline doctoring case can never fire through that hook.
    ///
    /// So we probe <see cref="Pawn_JobTracker.StartJob"/> instead, which every
    /// job passes through, and log what a real implementation would have
    /// decided there.
    ///
    /// What a play session should tell us:
    ///   1. Is <c>workGiverDef</c> populated at this point for think-tree work
    ///      jobs? (It should be — <c>JobGiver_Work</c> sets it — but "should"
    ///      has been wrong here before.)
    ///   2. Does <c>targetA</c> resolve to the room we expect for the work,
    ///      or does it point somewhere else (an ingredient, a haul source)?
    ///   3. How often does a match land on a job that is player-forced, i.e.
    ///      how much work does the <c>!playerForced</c> gate actually shed?
    ///   4. Does the log stay quiet in ordinary play, or is StartJob far too
    ///      chatty a place to hang this?
    ///
    /// Insertion, once the above holds, is NOT invented: vanilla does exactly
    /// it at <c>Pawn_JobTracker.cs:338-347</c> — enqueue the incoming job first
    /// (<c>jobQueue.EnqueueFirst</c>) and start a different one instead. We
    /// would do the same from here without touching the gated method. Note the
    /// obvious hazard for that step, which this probe deliberately does not
    /// reach: starting a job from inside a StartJob prefix re-enters StartJob.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_InterceptionProbe
    {
        /// <summary>Master switch. Dev-mode → Tweak values → ShiftChange.</summary>
        [TweakValue("ShiftChange")]
        public static bool ProbeEnabled = true;

        /// <summary>
        /// Log every automatic work job seen, not only the ones that match a
        /// stand's room. Answers question 1 and 2 above; far noisier.
        /// </summary>
        [TweakValue("ShiftChange")]
        public static bool ProbeVerbose = false;

        /// <summary>
        /// Minimum ticks between two log lines for the same (pawn, work type,
        /// room). StartJob fires constantly; without this the log is unusable
        /// and the probe itself becomes the performance problem.
        /// </summary>
        [TweakValue("ShiftChange", 0f, 20000f)]
        public static int ProbeThrottleTicks = 2500;

        private static readonly Dictionary<int, int> LastLoggedTick = new Dictionary<int, int>();

        private static List<ThingDef> standDefs;

        private static List<ThingDef> StandDefs
        {
            get
            {
                if (standDefs == null)
                {
                    standDefs = new List<ThingDef>();
                    // Named rather than typed because the enumeration source
                    // (Room.ContainedThings) is per-ThingDef. Silent-fail so a
                    // game without Odyssey, or without Biotech's kid stand,
                    // simply finds nothing instead of throwing at startup.
                    foreach (string name in new[] { "Building_OutfitStand", "Building_KidOutfitStand" })
                    {
                        ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                        if (def != null)
                        {
                            standDefs.Add(def);
                        }
                    }
                }
                return standDefs;
            }
        }

        // ReSharper disable once InconsistentNaming — Harmony field injection.
        public static void Prefix(Job newJob, Pawn ___pawn)
        {
            if (!ProbeEnabled)
            {
                return;
            }

            // An exception thrown here would break job assignment for every
            // pawn on the map, which is a bricked colony rather than a bad
            // log line. A probe is never worth that risk.
            try
            {
                Observe(newJob, ___pawn);
            }
            catch (System.Exception e)
            {
                ProbeEnabled = false;
                Log.Error("[ShiftChange] probe threw, disabling itself: " + e);
            }
        }

        private static void Observe(Job job, Pawn pawn)
        {
            if (job == null || pawn == null || Current.ProgramState != ProgramState.Playing)
            {
                return;
            }
            if (!pawn.Spawned || pawn.Faction != Faction.OfPlayer || !pawn.RaceProps.Humanlike)
            {
                return;
            }

            // No work giver means this is not work — sleeping, joy, hauling a
            // dropped thing on the way past, a mental state. Not our business.
            WorkTypeDef jobWork = job.workGiverDef?.workType;
            if (jobWork == null)
            {
                return;
            }

            Map map = pawn.MapHeld;
            if (map == null)
            {
                return;
            }

            // Question 2: is targetA even the place the work happens? Logged
            // rather than assumed.
            LocalTargetInfo target = job.targetA;
            IntVec3 cell = target.HasThing ? target.Thing.PositionHeld : target.Cell;
            if (!cell.IsValid || !cell.InBounds(map))
            {
                if (ProbeVerbose)
                {
                    Log.Message($"[ShiftChange] {pawn.LabelShort}: {job.def.defName} ({jobWork.defName}) — targetA has no usable cell");
                }
                return;
            }

            Room room = cell.GetRoom(map);
            RoomRoleDef role = room?.Role;
            WorkTypeDef roomWork = RoomWorkTypes.ForRole(role);

            if (ProbeVerbose)
            {
                LogThrottled(pawn, jobWork, room,
                    $"[ShiftChange] seen: {pawn.LabelShort} {job.def.defName} work={jobWork.defName} " +
                    $"forced={job.playerForced} room={role?.defName ?? "none"} roomWork={roomWork?.defName ?? "none"}");
            }

            // TODO(v1): a stand whose room has no role, or a role we have no
            // default for, should fall back to an explicit per-stand work-type
            // override. The comp that would hold it does not exist yet.
            if (roomWork == null || roomWork != jobWork)
            {
                return;
            }

            Building stand = FindStand(room);
            if (stand == null)
            {
                return;
            }

            // TODO(v1): the real gate is "this pawn owns THIS stand", via a
            // CompAssignableToPawn patched onto the vanilla def
            // (maxAssignedPawnsCount 1 — the stand holds one outfit, so
            // sharing is not merely untidy but impossible). The comp does not
            // exist yet, so the probe reports any stand in the room and the
            // ownership column reads "n/a".
            string verdict;
            if (job.playerForced)
            {
                verdict = "SKIP (player-forced)";
            }
            else if (map.dangerWatcher.DangerRating != StoryDanger.None)
            {
                verdict = $"SKIP (danger={map.dangerWatcher.DangerRating})";
            }
            else
            {
                verdict = "WOULD SWAP";
            }

            LogThrottled(pawn, jobWork, room,
                $"[ShiftChange] {verdict}: {pawn.LabelShort} → {job.def.defName} " +
                $"work={jobWork.defName} room={role.defName} stand={stand.LabelShort} " +
                $"standCell={stand.Position} pawnCell={pawn.Position} targetCell={cell}");
        }

        private static Building FindStand(Room room)
        {
            List<ThingDef> defs = StandDefs;
            for (int i = 0; i < defs.Count; i++)
            {
                foreach (Thing thing in room.ContainedThings(defs[i]))
                {
                    if (thing is Building building)
                    {
                        return building;
                    }
                }
            }
            return null;
        }

        private static void LogThrottled(Pawn pawn, WorkTypeDef work, Room room, string message)
        {
            int key = Gen.HashCombineInt(
                Gen.HashCombineInt(pawn.thingIDNumber, work.shortHash),
                room?.ID ?? 0);

            int now = Find.TickManager.TicksGame;
            int last;
            if (LastLoggedTick.TryGetValue(key, out last) && now - last < ProbeThrottleTicks)
            {
                return;
            }
            LastLoggedTick[key] = now;
            Log.Message(message);
        }
    }
}
