// HARNESS only: dev tooling, compiled out of a plain Release build (the
// configuration table is in ShiftChange.csproj). The guard is whole-file,
// always, and check-invariants.py enforces it; why: docs/DESIGN.md,
// "Development tooling".
#if HARNESS
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Harness cases for catchments: a stand serving a radius around itself,
    /// or an allowed area, instead of its room. docs/DESIGN.md, "Catchments".
    ///
    /// <para>Case ORDER is decided in one place,
    /// <see cref="DebugTools_LifecycleHarness.Run"/>.</para>
    /// </summary>
    internal static partial class HarnessCatchments
    {

        /// <summary>
        /// LEAVING ROOM MODE NEEDS SOMETHING TICKED, and leaving shift use
        /// brings the stand back to its room with its radius kept. Automatic is
        /// a room's reading, so an automatic stand's set becomes ticks on the
        /// way out, and one with nothing to tick stays put. The window greys
        /// its Serves row on <see cref="CompShiftStand.ServesSomething"/>, a
        /// test apart from the setter's, so the two are checked against each
        /// other in every state that draws the row: greyed where a radius is
        /// refused, live where one is taken, and live in radius mode, where
        /// going back to the room always is. A stand out of shift use draws no
        /// row at all.
        /// </summary>
        internal static bool CatchmentNeedsSomethingTicked(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
            if (doctor == null || bedDef == null)
            {
                return Expect(false, "the Doctor and bed defs resolve");
            }

            bool ok = Expect(fix.Comp.IsAutomatic && fix.Comp.WorkTypes.Count == 0
                             && !fix.Comp.HandlesRecreation() && !fix.Comp.HandlesRest(),
                             "an automatic stand in an unroled room serves nothing (control)");
            fix.Comp.SetServes(CatchmentKind.Radius);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Room,
                         "so it cannot serve a radius: there is nothing to tick")
                & Expect(!CompShiftStand.CatchmentStands.Contains(fix.Comp),
                         "and it is not in the registry")
                & Expect(!fix.Comp.ServesSomething,
                         "and the window greys its Serves row to match");

            // A medical bed makes the pad a hospital, so automatic reads Doctor.
            Building_Bed bed = DebugTools_Fixtures.Spawn(
                fix.Map, bedDef, ThingDefOf.WoodLog,
                new IntVec3(fix.Stand.Position.x + 2, 0, fix.Stand.Position.z + 2),
                Rot4.North) as Building_Bed;
            if (bed == null)
            {
                return Expect(false, "a bed spawns in the stand's room");
            }
            bed.Medical = true;
            ok &= Expect(fix.Comp.IsAutomatic && fix.Comp.HandlesWork(doctor),
                         "a medical bed makes it an automatic Doctor stand (control)")
                & Expect(fix.Comp.ServesSomething,
                         "so the window leaves its Serves row live");

            fix.Comp.SetServes(CatchmentKind.Radius);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Radius,
                         "with something to tick, it serves a radius")
                & Expect(!fix.Comp.IsAutomatic && fix.Comp.HasCustomSet
                         && fix.Comp.HandlesWork(doctor) && fix.Comp.WorkTypes.Count == 1,
                         "its automatic Doctor became a tick, so nothing visibly changed")
                & Expect(CompShiftStand.CatchmentStands.Contains(fix.Comp),
                         "and it is in the registry")
                & Expect(fix.Comp.ServesSomething,
                         "and its Serves row stays live, so it can switch back");

            // Back on This room with the tick untouched, it is the room's own
            // reading again, so Automatic, and follows the room from here on.
            fix.Comp.SetServes(CatchmentKind.Room);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Room && fix.Comp.IsAutomatic
                         && fix.Comp.HandlesWork(doctor),
                         "back on This room with its tick untouched, it is automatic again, reading the hospital");
            WorkTypeDef research = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Research");
            if (research != null)
            {
                fix.Comp.SetServes(CatchmentKind.Radius);
                fix.Comp.ToggleWork(research);
                fix.Comp.SetServes(CatchmentKind.Room);
                ok &= Expect(fix.Comp.serves == CatchmentKind.Room && !fix.Comp.IsAutomatic
                             && fix.Comp.HandlesWork(doctor) && fix.Comp.HandlesWork(research),
                             "but ticks changed while it served a radius stay ticks when it comes back");
                fix.Comp.ToggleWork(research);
            }
            fix.Comp.SetServes(CatchmentKind.Radius);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Radius && !fix.Comp.IsAutomatic
                         && fix.Comp.WorkTypes.Count == 1,
                         "and it serves a radius with Doctor ticked again, for the steps below (control)");

            fix.Comp.SetServeRadius(7);
            fix.Comp.ToggleWork(doctor);
            ok &= Expect(fix.Comp.IsExcluded,
                         "unticking the last work type takes it out of shift use (control)")
                & Expect(fix.Comp.serves == CatchmentKind.Room,
                         "and back to its room")
                & Expect(!CompShiftStand.CatchmentStands.Contains(fix.Comp),
                         "out of the registry")
                & Expect(fix.Comp.serveRadius == 7,
                         "with its radius kept for when it comes back");

            fix.Comp.SetServes(CatchmentKind.Radius);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Room,
                         "a stand not used for shift changes cannot serve a radius");

            fix.Comp.SetAutomatic();
            ok &= Expect(fix.Comp.IsAutomatic && fix.Comp.serves == CatchmentKind.Room,
                         "Automatic puts it back in its room, reading the hospital again")
                & Expect(fix.Comp.ServesSomething,
                         "with its Serves row live again");
            fix.Comp.SetServes(CatchmentKind.Radius);
            return ok & Expect(fix.Comp.serves == CatchmentKind.Radius,
                               "and the setter agrees: it serves a radius again");
        }

        /// <summary>
        /// A ROOM STAND IN THE OPEN SERVES NOTHING. On open ground the stand's
        /// room is all the open ground on the map, so serving it covers no cell
        /// and dresses nobody, for work as for recreation and sleep, and the
        /// alert lists the stand. Given a radius, the same work dresses and the
        /// alert drops it (control). A colonist already in its uniform, as an
        /// older save can hold, changes back at their next job once the stand
        /// serves its room again.
        /// </summary>
        internal static bool RoomStandInTheOpenServesNothing(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            if (doctor == null || tend == null)
            {
                return Expect(false, "the doctor work defs resolve");
            }
            Alert alert = VanillaAlert(typeof(Alert_ShiftStandServesNothing));
            if (alert == null)
            {
                return Expect(false, "vanilla's alert readout made the alert");
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            IntVec3 near = fix.Stand.Position + new IntVec3(2, 0, 2);
            bool ok = Expect(fix.Comp.StandsInTheOpen && fix.Comp.serves == CatchmentKind.Room,
                             "the stand stands in the open, serving its room (control)")
                    & Expect(!fix.Comp.Covers(near, fix.Map) && !fix.Comp.Covers(fix.Stand.Position, fix.Map),
                             "which covers nothing, not even its own cell")
                    & Expect(!Probe(fix, WorkAt(tend, near)), "so Doctor work right beside it dresses nobody")
                    & Expect(Lists(alert, fix.Stand), "and the alert lists it");

            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(5);
            ok &= Expect(Probe(fix, WorkAt(tend, near)), "given a radius, the same work dresses (control)")
                & Expect(!Lists(alert, fix.Stand), "and the alert drops it (control)");

            // As an older save can hold: a colonist in the uniform of a room
            // stand in the open. The dressing probe recorded its job as the one
            // just dressed for, which on shift would keep the uniform on
            // whatever the stand covers.
            Patch_JobInterception.DressedFor.Remove(fix.Pawn);
            ok &= Expect(RunSwap(fix), "dressed at the stand while it serves a radius")
                & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(!Probe(fix, WorkAt(tend, near)), "in uniform, work inside the ring keeps it on (control)");
            fix.Comp.SetServes(CatchmentKind.Room);
            return ok & Expect(Probe(fix, WorkAt(tend, near)),
                               "back on its room, the same work changes them back: the stand covers nothing");
        }

        /// <summary>
        /// As a swap succeeds, the job tracker starts a one-tick pause
        /// (<c>Wait_MaintainPosture</c>) before the queue gets a turn, so it
        /// falls between every swap and the job deferred behind it. Judged as
        /// a job, it sent a radius sleep stand's colonist straight back to
        /// change. It rides along now, and this asserts so after every
        /// dressing here, then settles the colonist, since a swap left running
        /// would answer every probe below with <c>MidSwap</c>.
        /// </summary>
        internal static bool NotSentStraightBack(Fixture fix)
        {
            JobDef next = fix.Pawn.CurJobDef;
            bool ok = Expect(next != ShiftChangeDefOf.ShiftChange_SwapAtStand,
                             "the job after the swap (" + (next?.defName ?? "none")
                             + ") does not send them straight back");
            Settle(fix.Pawn);
            return ok;
        }

        /// <summary>
        /// A job of this giver's work type at a cell, reserving nothing:
        /// <see cref="HarnessGates.DoctoringAt"/> for any giver.
        /// </summary>
        internal static Job WorkAt(WorkGiverDef giver, IntVec3 at)
        {
            return HarnessGates.DoctoringAt(giver, at);
        }

        /// <summary>A haul to a cell, from a hauling giver.</summary>
        internal static Job Haul(WorkGiverDef giver, Thing thing, IntVec3 to)
        {
            Job job = HarnessRideAlong.GiverlessHaul(thing, to);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>Twenty steel on a cell, for a delivery.</summary>
        internal static Thing Steel(Map map, IntVec3 at)
        {
            Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
            steel.stackCount = 20;
            return GenSpawn.Spawn(steel, at, map);
        }

        /// <summary>
        /// THE WORDS FOLLOW THE CATCHMENT. The Serves row, the inspect pane and
        /// the switch's tooltip all read the stand's mode: its room, the open
        /// ground (nothing, whatever the stand is for), or its radius.
        /// Asserted on the strings the window draws, since no case draws one;
        /// the room's wording is the control for each radius one.
        /// </summary>
        internal static bool CatchmentWordsFollowTheMode(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (doctor == null)
            {
                return Expect(false, "the Doctor work type resolves");
            }
            if (fix.Outside.Area == 0)
            {
                return Expect(false, "the fixture has a door and open ground outside it");
            }
            fix.Comp.ToggleWork(doctor);
            string manual = "ShiftChange.Manual".Translate();
            string radiusLine = "ShiftChange.InspectServesRadius".Translate(7);

            bool ok = Expect(fix.Comp.ServesLabel() == (string)"ShiftChange.ServesRoom".Translate(),
                             "a room stand's Serves row reads this room")
                    & Expect(fix.Comp.ByCatchment("ShiftChange.RestRow") == "ShiftChange.RestRow",
                             "its wording is the room's")
                    & Expect(HarnessGizmos.Switch(fix.Comp)?.defaultDesc
                             == (string)"ShiftChange.SetWorkTypeDesc".Translate(fix.Comp.WorkTypesLabel(), manual),
                             "so is its switch's tooltip")
                    & Expect(!fix.Comp.CompInspectStringExtra().Contains(radiusLine),
                             "and the inspect pane has no radius line");

            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(7);
            ok &= Expect(fix.Comp.ServesLabel() == (string)"ShiftChange.ServesRadius".Translate(7),
                         "a radius stand's row reads within 7 cells")
                & Expect(fix.Comp.ByCatchment("ShiftChange.RestRow") == "ShiftChange.RestRowRadius",
                         "its wording is the radius variant")
                & Expect(HarnessGizmos.Switch(fix.Comp)?.defaultDesc
                         == (string)"ShiftChange.SetWorkTypeDescRadius".Translate(fix.Comp.WorkTypesLabel(), manual, 7),
                         "so is its switch's tooltip, naming 7 cells")
                & Expect(fix.Comp.CompInspectStringExtra().Contains(radiusLine),
                         "and the inspect pane says it serves a radius of 7 cells");

            CompShiftStand open = StageStand(fix, new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ));
            if (open == null)
            {
                return ok & Expect(false, "a stand could be staged in the open");
            }
            string nothingLine = "ShiftChange.InspectServesNothingOutdoors".Translate();
            open.ToggleWork(doctor);
            ok &= Expect(open.StandsInTheOpen
                         && open.ServesLabel() == (string)"ShiftChange.ServesNothingOutdoors".Translate(),
                         "a work stand in the open serves nothing there")
                & Expect(open.CompInspectStringExtra().Contains(nothingLine),
                         "and its inspect pane says so");
            open.ToggleRecreation();
            ok &= Expect(open.ServesLabel() == (string)"ShiftChange.ServesNothingOutdoors".Translate(),
                         "nor does a recreation stand there");
            open.SetServes(CatchmentKind.Radius);
            return ok & Expect(open.ServesLabel() == (string)"ShiftChange.ServesRadius".Translate(open.serveRadius)
                               && !open.CompInspectStringExtra().Contains(nothingLine),
                               "until it serves a radius (control)");
        }

        /// <summary>Another outfit stand on the fixture's map, stocked with a duster, or null.</summary>
        internal static CompShiftStand StageStand(Fixture fix, IntVec3 at)
        {
            Building_OutfitStand stand = DebugTools_Fixtures.Spawn(
                fix.Map, fix.Stand.def, ThingDefOf.WoodLog, at, Rot4.North) as Building_OutfitStand;
            return stand != null && StockOne(stand, "Apparel_Duster")
                ? stand.TryGetComp<CompShiftStand>()
                : null;
        }

        /// <summary>
        /// A new allowed area on the fixture's map, named and painted on
        /// <paramref name="cells"/>, or null when the map already holds ten.
        /// Recorded on the fixture, whose teardown deletes it, since an area
        /// outlives the pad.
        /// </summary>
        internal static Area_Allowed NewArea(Fixture fix, string label, params IntVec3[] cells)
        {
            Area_Allowed area;
            if (!fix.Map.areaManager.TryMakeNewAllowed(out area))
            {
                return null;
            }
            fix.Areas.Add(area);
            area.SetLabel(label);
            for (int i = 0; i < cells.Length; i++)
            {
                area[cells[i]] = true;
            }
            return area;
        }

        /// <summary>
        /// A SOLID TARGET IS JUDGED AT ITS OWN CELL, THEN THE FLOOR BESIDE IT.
        /// A rock has no room on its own cell, so a mining job's room cell is
        /// the floor beside it, and room mode dresses by that (control). An area
        /// dresses a miner with the rock painted, or only the floor around it,
        /// and not with neither; a miner in uniform keeps it for the rock
        /// painted alone. A wall's repair, judged as pass-through work at the
        /// places it names, keeps the uniform on with only the floor around the
        /// wall painted, and not with nothing painted.
        /// </summary>
        internal static bool SolidTargetIsJudgedAtItsOwnCellFirst(Fixture fix)
        {
            WorkTypeDef mining = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Mining");
            WorkGiverDef mine = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Mine");
            WorkGiverDef repair = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Repair");
            ThingDef granite = DefDatabase<ThingDef>.GetNamedSilentFail("Granite");
            if (mining == null || mine == null || repair == null || granite == null)
            {
                return Expect(false, "the mining, repair and granite defs resolve");
            }
            fix.Comp.ToggleWork(mining);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            Thing rock = GenSpawn.Spawn(ThingMaker.MakeThing(granite), at + new IntVec3(3, 0, 3), fix.Map);
            if (rock == null || !rock.Spawned)
            {
                return Expect(false, "a granite rock spawns in the stand's room");
            }
            Job mineRock = JobOn(JobDefOf.Mine, mine, rock);
            JobPlace place = Patch_JobInterception.SharedTargetPlace(
                mineRock, Patch_JobInterception.Classify(fix.Pawn, mineRock, null), fix.Map);
            bool ok = Expect(rock.Position.GetRoom(fix.Map) == null && place.Cell == rock.Position
                             && place.RoomCell != rock.Position && place.RoomCell.GetRoom(fix.Map) != null,
                             "the rock's own cell has no room, so the job's room cell is a floor cell beside it (control)")
                    & Expect(Probe(fix, JobOn(JobDefOf.Mine, mine, rock)),
                             "room mode dresses for mining it, by that floor cell (control)");

            Area_Allowed area = NewArea(fix, "Shift harness quarry", at, rock.Position);
            if (area == null)
            {
                return ok & Expect(false, "the map has room for another allowed area");
            }
            fix.Comp.SetServeArea(area);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Area && !area[place.RoomCell],
                         "the stand serves an area with the rock painted and its floor cell not (control)")
                & Expect(Probe(fix, JobOn(JobDefOf.Mine, mine, rock)), "which dresses for mining the rock face");

            area[rock.Position] = false;
            ok &= Expect(!Probe(fix, JobOn(JobDefOf.Mine, mine, rock)), "with the rock unpainted too, it does not");

            List<IntVec3> aroundRock = new List<IntVec3>(GenAdj.CellsAdjacent8Way(rock));
            SetCells(area, aroundRock, true);
            ok &= Expect(Probe(fix, JobOn(JobDefOf.Mine, mine, rock)),
                         "with only the floor around the rock painted, it does");

            SetCells(area, aroundRock, false);
            area[rock.Position] = true;
            // Each dressing probe recorded its job as the one just dressed for,
            // and on shift that grace would keep the uniform on for the same
            // job whatever the catchment said: the probe below would pass for
            // the wrong reason.
            Patch_JobInterception.DressedFor.Remove(fix.Pawn);
            ok &= Expect(RunSwap(fix), "dressed for mining") & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(!Probe(fix, JobOn(JobDefOf.Mine, mine, rock)),
                         "a miner in uniform keeps it on for the rock painted alone");

            // Clear of the rock's ring, so the two sets of painted cells never
            // meet. One wall inside a room does not split it.
            Thing wall = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel),
                                        at + new IntVec3(0, 0, 3), fix.Map);
            if (wall == null || !wall.Spawned)
            {
                return ok & Expect(false, "a wall spawns in the stand's room");
            }
            List<IntVec3> aroundWall = new List<IntVec3>(GenAdj.CellsAdjacent8Way(wall));
            SetCells(area, aroundWall, true);
            ok &= Expect(!area[wall.Position] && !Probe(fix, JobOn(JobDefOf.Repair, repair, wall)),
                         "repairing a wall with only the floor around it painted keeps the uniform on: pass-through work, judged by the same rule");
            SetCells(area, aroundWall, false);
            return ok & Expect(Probe(fix, JobOn(JobDefOf.Repair, repair, wall)),
                               "with nothing around it painted, it changes them back (control)");
        }

        /// <summary>
        /// A fresh job of <paramref name="def"/> on <paramref name="target"/>,
        /// as <paramref name="giver"/> hands it out. One per probe: jobs are
        /// pooled, and one a probe queued can be handed out again once the
        /// queue is cleared.
        /// </summary>
        internal static Job JobOn(JobDef def, WorkGiverDef giver, Thing target)
        {
            Job job = JobMaker.MakeJob(def, target);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>Paints or clears every cell of <paramref name="cells"/> that is on the map.</summary>
        internal static void SetCells(Area area, List<IntVec3> cells, bool painted)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].InBounds(area.Map))
                {
                    area[cells[i]] = painted;
                }
            }
        }

        /// <summary>A fresh report from <paramref name="alert"/> lists <paramref name="stand"/>.</summary>
        internal static bool Lists(Alert alert, Thing stand)
        {
            AlertReport report = alert.GetReport();
            return report.active && report.culpritsThings != null && report.culpritsThings.Contains(stand);
        }

        /// <summary>
        /// The instance of <paramref name="type"/> that vanilla's alert readout
        /// made for this game, or null. Read off the readout's private list,
        /// since what is under test is that vanilla made it from the subclass.
        /// </summary>
        internal static Alert VanillaAlert(System.Type type)
        {
            AlertsReadout readout = (Find.UIRoot as UIRoot_Play)?.alerts;
            List<Alert> all = readout == null
                ? null
                : AccessTools.Field(typeof(AlertsReadout), "AllAlerts").GetValue(readout) as List<Alert>;
            return all?.Find(a => a != null && a.GetType() == type);
        }

        /// <summary>
        /// A ROOM STAND BESIDE A DOOR SERVES ITS OWN ROOM ONLY. The region
        /// lister files a thing under every region it can be touched from, so
        /// the doorway's room lists the stand just inside the door and a room
        /// stand just outside it (control). Neither covers work in the
        /// doorway, and the dress search agrees: that work dresses nobody,
        /// where the same work inside the room does (control). The stand
        /// outside stands in the open, where the alert says it serves nothing.
        /// </summary>
        internal static bool RoomStandBesideADoorServesItsOwnRoom(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            if (doctor == null || tend == null)
            {
                return Expect(false, "the doctor work defs resolve");
            }
            if (fix.Outside.Area == 0)
            {
                return Expect(false, "the fixture has a door and open ground outside it");
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            Map map = fix.Map;
            IntVec3 at = fix.Stand.Position;
            IntVec3 door = at + new IntVec3(-1, 0, 0);
            CompShiftStand outdoor = StageStand(fix, at + new IntVec3(-2, 0, 0));
            if (outdoor == null)
            {
                return Expect(false, "a stand could be staged outside the door");
            }
            outdoor.ToggleWork(doctor);

            Room doorway = door.GetRoom(map);
            List<Thing> listed = doorway != null
                ? doorway.ContainedThings(fix.Stand.def).ToList()
                : new List<Thing>();
            return Expect(door.GetDoor(map) != null && doorway != null && doorway.IsDoorway,
                          "the cell beside the stand is a door, a room of its own (control)")
                 & Expect(listed.Contains(fix.Stand) && listed.Contains(outdoor.parent),
                          "whose room lists the stand inside and the stand outside (control)")
                 & Expect(outdoor.ServeFault == CatchmentFault.InTheOpen,
                          "the stand outside stands in the open and serves nothing (control)")
                 & Expect(!fix.Comp.Covers(door, map) && !outdoor.Covers(door, map),
                          "neither stand covers the doorway")
                 & Expect(!Probe(fix, HarnessGates.DoctoringAt(tend, door)),
                          "so work in the doorway dresses nobody")
                 & Expect(Probe(fix, HarnessGates.DoctoringAt(tend, at + new IntVec3(2, 0, 2))),
                          "where the same work inside the room does (control)");
        }

        /// <summary>
        /// AN URGENT PATIENT ON SHIFT GOES STRAIGHT TO BED, with the medical
        /// emergency setting off. In room mode, a colonist in uniform who takes
        /// a wound needing a doctor now lies down in a bed out past the door
        /// without changing back first; so they do when vanilla puts the
        /// lay-down back in the queue untagged, and a haul it starts ahead of
        /// the lay-down keeps the uniform on too. That untagged lay-down never
        /// reads as ordinary sleep. At a radius stand they go straight to the
        /// bed inside the ring. Recuperating with no such wound changes them
        /// back first, the urgent case does with the setting on, and the haul
        /// does with nothing queued: all controls.
        /// </summary>
        internal static bool UrgentPatientGoesStraightToBed(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
            if (doctor == null || bedDef == null)
            {
                return Expect(false, "the doctor and bed defs resolve");
            }
            if (fix.Outside.Area == 0)
            {
                return Expect(false, "the fixture has a door and open ground outside it");
            }
            // Anything tendable from generation would make the colonist an
            // urgent case before the wound below (MedicalBedRestDressesAtAGownStand).
            List<string> stripped = new List<string>();
            foreach (Hediff existing in fix.Pawn.health.hediffSet.hediffs.ToList())
            {
                if (existing.TendableNow())
                {
                    stripped.Add(existing.LabelCap);
                    fix.Pawn.health.RemoveHediff(existing);
                }
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);
            Map map = fix.Map;
            Building_Bed bedOut = DebugTools_Fixtures.Spawn(
                map, bedDef, ThingDefOf.WoodLog, new IntVec3(fix.Outside.minX, 0, fix.Outside.minZ + 2),
                Rot4.North) as Building_Bed;
            Building_Bed bedIn = DebugTools_Fixtures.Spawn(
                map, bedDef, ThingDefOf.WoodLog, fix.Stand.Position + new IntVec3(2, 0, 0), Rot4.North) as Building_Bed;
            if (bedOut == null || bedIn == null)
            {
                return Expect(false, "a bed spawns out past the door and one in the room");
            }
            bedOut.Medical = true;
            bedIn.Medical = true;
            IntVec3 haulFrom = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ);
            IntVec3 haulTo = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 1);

            bool ok = Expect(!HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn),
                             "the colonist needs no doctor to begin with (precondition"
                             + (stripped.Count > 0 ? "; removed " + string.Join(", ", stripped) : "") + ")")
                    & Expect(!fix.Comp.Covers(bedOut.Position, map) && fix.Comp.Covers(bedIn.Position, map),
                             "the stand's room holds one bed and not the one past the door (control)")
                    & Expect(RunSwap(fix), "dressed for doctoring")
                    & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);

            bool was = ShiftChangeMod.Settings.medicalEmergenciesChangeFirst;
            Hediff wound = null;
            try
            {
                ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = false;
                ok &= Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, bedOut), JobTag.RestingForMedicalReasons),
                             "room mode: recuperating in the bed past the door changes them back first (control)");

                BodyPartRecord part = fix.Pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault();
                wound = HediffMaker.MakeHediff(HediffDefOf.Cut, fix.Pawn, part);
                wound.Severity = 6f;
                fix.Pawn.health.AddHediff(wound, part);
                ok &= Expect(HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn) && !fix.Pawn.Downed,
                             "an untended wound makes them an urgent case, still on their feet (precondition)")
                    & Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, bedOut), JobTag.RestingForMedicalReasons),
                             "ROOM MODE: so they go straight to the bed past the door in uniform")
                    & Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, bedOut)),
                             "and so they do when vanilla puts the lay-down back in the queue untagged")
                    & Expect(!Patch_JobInterception.IsOrdinarySleep(fix.Pawn, JobMaker.MakeJob(JobDefOf.LayDown, bedIn), null),
                             "which never reads as ordinary sleep, that a sleep stand would dress them for");

                // The shape vanilla's opportunistic haul leaves: the lay-down
                // back at the head of the queue, untagged, and a haul first.
                ok &= Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.HaulToCell, haulFrom, haulTo)),
                             "a haul past the door changes them back (control)");
                fix.Pawn.jobs.jobQueue.EnqueueFirst(JobMaker.MakeJob(JobDefOf.LayDown, bedOut));
                ok &= Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.HaulToCell, haulFrom, haulTo)),
                             "the same haul keeps the uniform on with the urgent lay-down queued behind it");

                ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = true;
                ok &= Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, bedOut), JobTag.RestingForMedicalReasons),
                             "with the medical emergency setting on, they change back first (control)");
                ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = false;

                fix.Comp.SetServes(CatchmentKind.Radius);
                fix.Comp.SetServeRadius(3);
                ok &= Expect(fix.Comp.serves == CatchmentKind.Radius && fix.Comp.Covers(bedIn.Position, map)
                             && fix.Comp.OnShift,
                             "the stand now serves a radius holding the bed in the room, the colonist still on shift (control)")
                    & Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, bedIn), JobTag.RestingForMedicalReasons),
                             "RADIUS: they go straight to the bed inside the ring");
                fix.Pawn.health.RemoveHediff(wound);
                wound = null;
                ok &= Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, bedIn), JobTag.RestingForMedicalReasons),
                             "recuperating in that bed changes them back first (control)");
            }
            finally
            {
                ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = was;
                if (wound != null)
                {
                    fix.Pawn.health.RemoveHediff(wound);
                }
            }
            return ok;
        }

        /// <summary>
        /// THE REGISTRY FORGETS A STAND THAT IS NO LONGER SPAWNED. Removing a
        /// map despawns none of its buildings, so a catchment stand left on an
        /// abandoned map would stay listed, holding the map through its area.
        /// An unspawned stand stands in for one here: a stand search drops it,
        /// and keeps the fixture's spawned stand (control).
        /// </summary>
        internal static bool RegistryForgetsAStandNoLongerSpawned(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (doctor == null)
            {
                return Expect(false, "the Doctor work type resolves");
            }
            fix.Comp.ToggleWork(doctor);
            fix.Comp.SetServes(CatchmentKind.Radius);
            ThingWithComps loose = ThingMaker.MakeThing(fix.Stand.def, ThingDefOf.WoodLog) as ThingWithComps;
            CompShiftStand stale = loose?.TryGetComp<CompShiftStand>();
            if (stale == null)
            {
                return Expect(false, "an unspawned stand could be made");
            }
            CompShiftStand.CatchmentStands.Add(stale);
            bool ok = Expect(CompShiftStand.CatchmentStands.Contains(stale)
                             && CompShiftStand.CatchmentStands.Contains(fix.Comp),
                             "the registry holds the spawned stand and the unspawned one (control)");
            Patch_JobInterception.FindAvailableStand(fix.Stand.Position, fix.Map, fix.Pawn, doctor);
            ok &= Expect(!CompShiftStand.CatchmentStands.Contains(stale), "a stand search drops the unspawned one")
                & Expect(CompShiftStand.CatchmentStands.Contains(fix.Comp), "and keeps the spawned stand");
            CompShiftStand.CatchmentStands.Remove(stale);
            return ok;
        }
    }
}
#endif
