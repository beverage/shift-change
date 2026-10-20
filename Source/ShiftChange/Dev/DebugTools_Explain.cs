// SCENES only (configuration table: ShiftChange.csproj). Read-only diagnostics: this file prints
// state and changes none. It is dev-only because it reports engine internals, not because it is
// dangerous.
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
    /// Why is this pawn not taking recreation? Walks the game's joy gates and then this mod's stand
    /// search, and names whichever is shut.
    ///
    /// The game's gates return null without a log line: <c>JobGiver_GetJoy</c> skips a giver whose
    /// kind is bored, and <c>JoyGiver_GoSwimming.TryGiveJob</c> has several early returns. The
    /// mod's Verbose flag only speaks once interception is handling a job, so a pawn that never
    /// starts a recreation job leaves no log, whatever the stand settings are. This reports and
    /// changes nothing, so it composes with <see cref="DebugTools_PoolStage.DrainRecreation"/>:
    /// explain, drain, explain again.
    /// </summary>
    internal static class DebugTools_Explain
    {
        /// <summary>Writes a recreation report for the pawn to the dev log.</summary>
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

            Need_Joy joy = pawn.needs?.joy;
            if (joy == null)
            {
                report.AppendLine("  joy need: ABSENT — this pawn never takes recreation at all.");
                Log.Message(report.ToString());
                return;
            }
            report.Append("  joy need: ").AppendLine(joy.CurLevel.ToStringPercent());

            // A drafted pawn never reaches the joy think tree: no giver runs and no job is made, so
            // interception has nothing to intercept and Verbose stays silent.
            report.Append("  drafted: ").Append(pawn.Drafted)
                  .AppendLine(pawn.Drafted
                      ? "  <-- BLOCKS everything; a drafted pawn takes no joy at all"
                      : "");

            // The swimming giver returns early for a pawn about to get hungry or sleepy.
            bool basicNeed = PawnUtility.WillSoonHaveBasicNeed(pawn);
            report.Append("  will soon have a basic need: ").Append(basicNeed)
                  .AppendLine(basicNeed ? "  <-- BLOCKS swimming outright" : "");

            // The gate above every giver. ThinkNode_Priority_GetJoy returns 0 for several unrelated
            // reasons (the lines down to the priority), after which the pawn wanders as if
            // recreation were broken; the priority line asks the node itself.
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

            // The usual culprit: a joy kind whose tolerance passes 0.5 is bored and all its givers
            // are skipped, not down-weighted. The boredom lifts only below 0.3.
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

            Room room = pawn.Position.GetRoom(map);

            // A doorway is a one-cell room of its own, so every room-scoped line below would
            // describe that single tile and read like a broken fixture. Say so instead.
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

            // Approximations of GoSwimming's silent early returns, checked in this room only:
            // warm enough, a usable water cell, and a swim path from it. The giver searches the
            // nearest regions, not only this room, so the live check below is authoritative.
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

            // Ground truth, not a replica: the checks above re-implement the givers' gates and can
            // pass for a pawn whose giver still returns null, so ask each giver. A vanilla
            // giver's TryGiveJob only builds a Job and reserves and starts nothing, so this is a
            // read; a modded giver is assumed to behave the same.
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

            // Interception only matters once a joy job starts, but report it regardless: a healthy
            // stand is then a stated result and the search moves upstream.
            report.Append("  interception enabled: ").AppendLine(Patch_JobInterception.Enabled.ToString());
            if (room == null)
            {
                report.AppendLine("  stand: no room to search.");
            }
            else
            {
                CompShiftStand stand = Patch_JobInterception.FindAvailableStand(
                    pawn.Position, map, pawn, null, Patch_JobInterception.StandTrigger.Recreation);
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
                                  .Append(", rest=").Append(any.HandlesRest())
                                  .Append(", depositOnly=").Append(any.DepositOnly)
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
