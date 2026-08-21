// SCENES only — see the config table in ShiftChange.csproj. Pure diagnostics:
// this file reads state and prints it, and changes nothing. It is dev-only
// because it reports engine internals a player has no use for, not because it
// is dangerous.
#if SCENES
using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShiftChange
{
    /// <summary>
    /// Why is this pawn not taking recreation?
    ///
    /// <para><b>Why this exists.</b> Every gate below returns null silently.
    /// <c>JobGiver_GetJoy</c> skips a giver whose kind is bored without a word,
    /// and <c>JoyGiver_GoSwimming.TryGiveJob</c> has five separate early
    /// returns and logs none of them. A stand that never fires therefore looks
    /// identical to a stand that is misconfigured, and the mod's own Verbose
    /// flag cannot tell them apart: Verbose only speaks once interception is
    /// handling a job, so SILENCE means no recreation job was ever started and
    /// no stand setting could have changed it.</para>
    ///
    /// <para>This walks both halves — vanilla's selection gates first, then
    /// ours — and names whichever one is shut. It reports and changes nothing,
    /// so it composes with <see cref="DebugTools_PoolStage.DrainRecreation"/>
    /// rather than replacing it: explain, then drain, then explain again.</para>
    /// </summary>
    internal static class DebugTools_Explain
    {
        internal static void ExplainRecreation(Pawn pawn)
        {
            if (pawn?.Map == null || !pawn.RaceProps.Humanlike)
            {
                Messages.Message("Pick a humanlike pawn on the map.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Map map = pawn.Map;
            StringBuilder report = new StringBuilder();
            report.Append("[ShiftChange] recreation report for ").AppendLine(pawn.LabelShort);

            // ---------------------------------------------------- the pawn
            Need_Joy joy = pawn.needs?.joy;
            if (joy == null)
            {
                report.AppendLine("  joy need: ABSENT — this pawn never takes recreation at all.");
                Log.Message(report.ToString());
                return;
            }
            report.Append("  joy need: ").AppendLine(joy.CurLevel.ToStringPercent());

            // The most decisive gate of the lot, and it was printed as a bare
            // value once — which read as neutral beside every other line. A
            // drafted pawn never reaches the joy think tree at all: no giver
            // runs, no job is made, and interception has nothing to intercept,
            // so Verbose stays silent and looks like OUR fault.
            report.Append("  drafted: ").Append(pawn.Drafted)
                  .AppendLine(pawn.Drafted
                      ? "  <-- BLOCKS everything; a drafted pawn takes no joy at all"
                      : "");

            // A hard early return in the swimming giver, and easy to miss: a
            // pawn about to get hungry or sleepy never even looks for water.
            bool basicNeed = PawnUtility.WillSoonHaveBasicNeed(pawn);
            report.Append("  will soon have a basic need: ").Append(basicNeed)
                  .AppendLine(basicNeed ? "  <-- BLOCKS swimming outright" : "");

            // ------------------------------------------- IS JOY RUNNING AT ALL
            // The gate above every giver, and the one that makes all of the
            // detail below moot when it is shut. ThinkNode_Priority_GetJoy
            // returns a bare 0 for a handful of unrelated reasons and the pawn
            // then idle-wanders, which looks exactly like "recreation is
            // broken". Ask it for the number rather than reasoning about it.
            report.Append("  current job: ")
                  .AppendLine(pawn.CurJobDef?.defName ?? "none (idle)");

            TimeAssignmentDef assignment = pawn.timetable == null
                ? TimeAssignmentDefOf.Anything
                : pawn.timetable.CurrentAssignment;
            report.Append("  time assignment: ").Append(assignment.defName)
                  .Append(", allowJoy=").Append(assignment.allowJoy)
                  .AppendLine(assignment.allowJoy ? "" : "  <-- BLOCKS all joy");

            bool tooEarly = Find.TickManager.TicksGame < 5000;
            report.Append("  game ticks: ").Append(Find.TickManager.TicksGame)
                  .AppendLine(tooEarly ? "  <-- BLOCKS all joy (none before tick 5000)" : "");

            bool lord = JoyUtility.LordPreventsGettingJoy(pawn);
            report.Append("  lord prevents joy: ").Append(lord)
                  .AppendLine(lord ? "  <-- BLOCKS all joy (ritual, caravan or gathering)" : "");

            float priority = new ThinkNode_Priority_GetJoy().GetPriority(pawn);
            report.Append("  JOY PRIORITY: ").Append(priority.ToString("0.##"))
                  .AppendLine(priority <= 0f
                      ? "  <-- ZERO: the joy think node never runs, so nothing below matters"
                      : "  (non-zero: joy is being sought)");

            // ---------------------------------------------- joy tolerances
            // The usual culprit. Past 0.5 a kind is BORED and every giver of
            // that kind is skipped, not merely down-weighted; boredom only
            // lifts once tolerance decays back under 0.3.
            report.AppendLine("  joy tolerances:");
            string tolerances = joy.tolerances.TolerancesString();
            report.AppendLine(string.IsNullOrEmpty(tolerances)
                ? "    (all zero — nothing is bored)"
                : tolerances);

            int bored = 0;
            List<JoyKindDef> kinds = DefDatabase<JoyKindDef>.AllDefsListForReading;
            for (int i = 0; i < kinds.Count; i++)
            {
                if (joy.tolerances.BoredOf(kinds[i]))
                {
                    bored++;
                }
            }
            report.Append("    bored kinds: ").Append(bored).Append(" of ")
                  .AppendLine(kinds.Count.ToString());

            // ---------------------------------------------------- the room
            Room room = pawn.Position.GetRoom(map);

            // A DOORWAY is its own one-cell room (Room.IsDoorway, :457). Stand
            // a pawn on a threshold and every room-scoped line below describes
            // that single tile — no water, no stands — which reads exactly like
            // a broken fixture. Say so instead.
            if (room != null && room.IsDoorway)
            {
                report.AppendLine("  room: THIS PAWN IS STANDING IN A DOORWAY, which is its own "
                    + "one-cell room. Everything below describes that tile alone — move them "
                    + "inside and run this again.");
            }
            report.Append("  room: ")
                  .Append(room == null ? "none" : room.Role?.defName ?? "unroled")
                  .Append(room == null ? "" : ", " + room.Temperature.ToString("F0") + " °C")
                  .Append(room == null ? "" : room.PsychologicallyOutdoors ? ", outdoors" : ", indoors")
                  .AppendLine(room != null && room.TouchesMapEdge ? ", TOUCHES MAP EDGE" : "");

            // ------------------------------------------- the swimming gates
            // Named specifically because the pool fixture is the case this was
            // written for; every one of these is a silent null return upstream.
            if (room != null)
            {
                bool warmEnough = room.PsychologicallyOutdoors
                    ? JoyGiver_GoSwimming.HappyToSwimOutsideOnMap(map)
                    : room.Temperature > 10f;
                report.Append("  swim: warm enough: ").Append(warmEnough)
                      .AppendLine(warmEnough ? "" : "  <-- BLOCKS swimming (needs > 10 °C)");

                IntVec3 water = IntVec3.Invalid;
                foreach (IntVec3 cell in room.Cells)
                {
                    TerrainDef terrain = cell.GetTerrain(map);
                    if (terrain.IsWater && terrain.toxicBuildupFactor == 0f
                        && cell.Standable(map) && !cell.Fogged(map)
                        && !cell.IsForbidden(pawn))
                    {
                        water = cell;
                        break;
                    }
                }
                report.Append("  swim: usable water cell: ")
                      .AppendLine(water.IsValid ? water.ToString() : "NONE  <-- BLOCKS swimming");

                if (water.IsValid)
                {
                    bool path = SwimPathFinder.TryFindSwimPath(pawn, water, out List<IntVec3> found);
                    report.Append("  swim: path from that cell: ")
                          .AppendLine(path
                              ? found.Count + " hops"
                              : "NONE  <-- BLOCKS swimming (pool too small or obstructed)");
                }
            }

            // ------------------------------------------- what she can DO now
            // GROUND TRUTH, not a replica. Everything above re-implements the
            // giver's gates and can therefore be wrong in the giver's favour —
            // the swim block once reported a usable cell and a 14-hop path for
            // a pawn whose GoSwimming still returned null. So ASK each giver.
            //
            // TryGiveJob only builds a Job; it reserves nothing and starts
            // nothing, so calling every giver is a read.
            report.AppendLine("  joy givers (live TryGiveJob):");
            List<JoyGiverDef> givers = DefDatabase<JoyGiverDef>.AllDefsListForReading;
            int yielding = 0;
            for (int i = 0; i < givers.Count; i++)
            {
                JoyGiverDef giver = givers[i];
                string verdict;
                if (joy.tolerances.BoredOf(giver.joyKind))
                {
                    verdict = "bored of " + giver.joyKind.defName;
                }
                else if (!giver.Worker.CanBeGivenTo(pawn))
                {
                    verdict = "CanBeGivenTo false";
                }
                else
                {
                    Job offered = null;
                    try
                    {
                        offered = giver.Worker.TryGiveJob(pawn);
                    }
                    catch (Exception e)
                    {
                        verdict = "THREW: " + e.GetType().Name;
                    }
                    if (offered != null)
                    {
                        yielding++;
                        IntVec3 where = offered.targetB.IsValid
                            ? Patch_JobInterception.JoyTargetCell(offered, map)
                            : offered.targetA.Cell;
                        Room there = where.IsValid && where.InBounds(map) ? where.GetRoom(map) : null;
                        verdict = "JOB " + offered.def.defName + " at " + where
                                + (there == room ? "  <-- IN THIS ROOM" : " (another room)");
                    }
                    else
                    {
                        verdict = "no job";
                    }
                }
                report.Append("    ").Append(giver.defName)
                      .Append(" (chance ").Append(giver.baseChance.ToString("0.##")).Append("): ")
                      .AppendLine(verdict);
            }
            report.Append("    ").Append(yielding)
                  .AppendLine(" giver(s) can produce a job right now — the roll picks among these by weight.");

            // ----------------------------------------------------- our half
            // Only reachable if something above let a joy job start; reported
            // anyway, because "our stand is fine" is the answer that sends the
            // next hour upstream instead of into this mod.
            report.Append("  interception enabled: ").AppendLine(Patch_JobInterception.Enabled.ToString());
            if (room == null)
            {
                report.AppendLine("  stand: no room to search.");
            }
            else
            {
                CompShiftStand stand = Patch_JobInterception.FindAvailableStand(
                    room, pawn, null, recreation: true);
                if (stand == null)
                {
                    report.AppendLine("  stand: none in this room is free AND set to recreation.");
                    foreach (Thing thing in room.ContainedAndAdjacentThings)
                    {
                        CompShiftStand any = (thing as ThingWithComps)?.TryGetComp<CompShiftStand>();
                        if (any != null)
                        {
                            report.Append("    ").Append(thing.LabelShort)
                                  .Append(": recreation=").Append(any.HandlesRecreation())
                                  .Append(", excluded=").Append(any.IsExcluded)
                                  .Append(", onShift=").Append(any.OnShift)
                                  .Append(", hasWearable=").AppendLine(any.HasWearable.ToString());
                        }
                    }
                }
                else
                {
                    report.AppendLine("  stand: FOUND and available — our half is ready.");
                }
            }

            Log.Message(report.ToString());
            Messages.Message("Recreation report for " + pawn.LabelShort
                + " written to the dev log.", MessageTypeDefOf.TaskCompletion, historical: false);
        }
    }
}
#endif
