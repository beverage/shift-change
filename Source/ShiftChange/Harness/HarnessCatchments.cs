// HARNESS only: dev tooling, compiled out of a plain Release build (the
// configuration table is in ShiftChange.csproj). The guard is whole-file,
// always, and check-invariants.py enforces it; why: docs/DESIGN.md,
// "Development tooling".
#if HARNESS
using System.Collections.Generic;
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
    internal static class HarnessCatchments
    {
        /// <summary>
        /// A RADIUS COVERS ITS RING, INCLUSIVE: every cell
        /// <c>GenRadial.NumCellsInRadius</c> counts, which is what
        /// <c>DrawRadiusRing</c> outlines, and not the next cell of the
        /// pattern. Asserted against the ring's own cell list rather than a
        /// distance formula, since the ring is what the player reads. Room
        /// mode's answer, the room, is the control.
        /// </summary>
        internal static bool RadiusCoversItsRing(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (doctor == null)
            {
                return Expect(false, "the Doctor work type resolves");
            }
            fix.Comp.ToggleWork(doctor);
            IntVec3 at = fix.Stand.Position;
            // The stand stands one cell in from the pad's south-west corner, so
            // two cells west of it is the pad's margin, outside the walls.
            IntVec3 inRoom = at + new IntVec3(2, 0, 2);
            IntVec3 pastWall = at + new IntVec3(-2, 0, 2);
            bool ok = Expect(fix.Comp.serves == CatchmentKind.Room, "a new stand serves its room")
                    & Expect(fix.Comp.Covers(inRoom, fix.Map), "which covers a cell inside it (control)")
                    & Expect(!fix.Comp.Covers(pastWall, fix.Map), "and not one past the wall");

            const int radius = 5;
            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(radius);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Radius && fix.Comp.serveRadius == radius,
                         "the stand now serves a radius of " + radius)
                & Expect(fix.Comp.Covers(pastWall, fix.Map),
                         "which crosses the wall, as a radius does");

            int ringCells = GenRadial.NumCellsInRadius(radius);
            int checkedCells = 0;
            bool allIn = true;
            for (int i = 0; i < ringCells; i++)
            {
                IntVec3 cell = at + GenRadial.RadialPattern[i];
                if (!cell.InBounds(fix.Map))
                {
                    continue;
                }
                checkedCells++;
                if (!fix.Comp.Covers(cell, fix.Map))
                {
                    allIn = false;
                    Report.Append("      not covered: ").Append(cell).AppendLine();
                }
            }
            IntVec3 next = at + GenRadial.RadialPattern[ringCells];
            ok &= Expect(checkedCells > 0 && allIn,
                         "every cell the ring outlines is covered (" + checkedCells + " checked)")
                & Expect(next.InBounds(fix.Map) && !fix.Comp.Covers(next, fix.Map),
                         "and the first cell past the ring is not")
                & Expect(fix.Comp.Covers(at + new IntVec3(radius, 0, 0), fix.Map),
                         "a cell exactly " + radius + " away on an axis is in")
                & Expect(!fix.Comp.Covers(at + new IntVec3(4, 0, 4), fix.Map),
                         "one 4 along and 4 up, past the ring, is out")
                & Expect(!fix.Comp.Covers(at, null), "and nothing is covered on no map")
                & Expect(CompShiftStand.MaxServeRadius == 79,
                         "the slider stops at 79 cells, the number the README and the changelog give");
            return ok;
        }

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
        /// A RADIUS REPLACES THE ROOM. Work outside the stand's room but inside
        /// its ring dresses; work past the ring does not, even in the stand's
        /// own room. Room mode's answers on the same cells are the control.
        /// </summary>
        internal static bool RadiusStandDressesWithinItsRing(Fixture fix)
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

            IntVec3 at = fix.Stand.Position;
            IntVec3 nearOutside = new IntVec3(fix.Outside.minX, 0, at.z);
            IntVec3 farOutside = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ);
            IntVec3 farCorner = at + new IntVec3(4, 0, 4);
            bool ok = Expect(nearOutside.GetRoom(fix.Map) != fix.Stand.GetRoom(),
                             "the ground outside the door is another room (control)")
                    & Expect(!Diverts(fix, HarnessGates.DoctoringAt(tend, nearOutside)),
                             "room mode: work outside the stand's room does not dress (control)")
                    & Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, farCorner)),
                             "room mode: work in the room's far corner does (control)");

            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(5);
            ok &= Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, nearOutside)),
                         "radius 5: work outside the room but inside the ring dresses")
                & Expect(!Diverts(fix, HarnessGates.DoctoringAt(tend, farOutside)),
                         "work past the ring does not")
                & Expect(!Diverts(fix, HarnessGates.DoctoringAt(tend, farCorner)),
                         "and nor does the room's far corner, past the ring: the radius replaces the room");

            fix.Comp.SetServeRadius(6);
            ok &= Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, farCorner)),
                         "with the ring widened to reach it, the far corner dresses again (control)");
            return ok;
        }

        /// <summary>
        /// THE OUTDOOR GUARD IS FOR ROOM STANDS. A room stand standing in the
        /// open serves no outdoor recreation, because the outdoor room spans
        /// the whole map; the same stand given a radius does, because a radius
        /// is bounded. The radius half is the control.
        /// </summary>
        internal static bool RadiusServesOutdoorRecreation(Fixture fix)
        {
            JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
            if (chess == null)
            {
                return Expect(false, "the chess joy job resolves");
            }
            if (fix.Outside.Area == 0)
            {
                return Expect(false, "the fixture has a door and open ground outside it");
            }
            // The pad's own stand stays out of it: not used for shift changes.
            fix.Comp.SetExcluded();

            IntVec3 standCell = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 1);
            IntVec3 joyCell = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 2);
            Building_OutfitStand outdoor = DebugTools_Fixtures.Spawn(
                fix.Map, fix.Stand.def, ThingDefOf.WoodLog, standCell, Rot4.North) as Building_OutfitStand;
            CompShiftStand comp = outdoor?.TryGetComp<CompShiftStand>();
            if (comp == null || !StockOne(outdoor, "Apparel_Duster"))
            {
                return Expect(false, "a stand could be staged outside the door");
            }
            comp.ToggleRecreation();

            Room open = outdoor.GetRoom();
            bool ok = Expect(open != null && open.TouchesMapEdge,
                             "the stand stands in the open, in the room that touches the map edge (control)")
                    & Expect(comp.HandlesRecreation() && comp.serves == CatchmentKind.Room,
                             "and serves recreation from its room (control)")
                    & Expect(!Diverts(fix, JobMaker.MakeJob(chess, joyCell, joyCell)),
                             "so a joy job beside it does NOT dress: the outdoor guard");

            comp.SetServes(CatchmentKind.Radius);
            comp.SetServeRadius(3);
            ok &= Expect(Diverts(fix, JobMaker.MakeJob(chess, joyCell, joyCell)),
                         "the same stand given a radius dresses for it (control)");
            return ok;
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
        /// PRECEDENCE IS UNCHANGED: the colonist's own stand first, then the
        /// nearest. An owned radius stand outside the room beats a nearer
        /// shared room stand inside it, and without the owner the nearer one
        /// wins, which is the control.
        /// </summary>
        internal static bool OwnedRadiusStandBeatsANearerSharedOne(Fixture fix)
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

            IntVec3 standCell = new IntVec3(fix.Outside.minX, 0, fix.Stand.Position.z + 2);
            Building_OutfitStand mine = DebugTools_Fixtures.Spawn(
                fix.Map, fix.Stand.def, ThingDefOf.WoodLog, standCell, Rot4.North) as Building_OutfitStand;
            CompShiftStand mineComp = mine?.TryGetComp<CompShiftStand>();
            CompAssignableToPawn_ShiftStand owner = mine?.TryGetComp<CompAssignableToPawn_ShiftStand>();
            if (mineComp == null || owner == null || !StockOne(mine, "Apparel_Duster"))
            {
                return Expect(false, "a second stand could be staged outside the door");
            }
            mineComp.ToggleWork(doctor);
            mineComp.SetServes(CatchmentKind.Radius);
            mineComp.SetServeRadius(6);

            IntVec3 workAt = fix.Stand.Position + new IntVec3(1, 0, 0);
            bool okay = Expect(mineComp.Covers(workAt, fix.Map),
                               "the outside stand's ring covers work in the room (control)")
                      & Expect(Patch_JobInterception.FindAvailableStand(workAt, fix.Map, fix.Pawn, doctor)
                               == fix.Comp,
                               "with no owners, the nearer shared room stand is chosen (control)");

            owner.TryAssignPawn(fix.Pawn);
            okay &= Expect(Patch_JobInterception.FindAvailableStand(workAt, fix.Map, fix.Pawn, doctor)
                           == mineComp,
                           "with the radius stand theirs, it is chosen over the nearer shared one");
            return okay;
        }

        /// <summary>
        /// A FREED RADIUS STAND INTERRUPTS NOBODY, where a room stand catches up
        /// a colonist already working bare inside it. Both halves on one
        /// fixture, the room half as the control.
        /// </summary>
        internal static bool FreedRadiusStandCatchesNobody(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            if (doctor == null || tend == null)
            {
                return Expect(false, "the doctor work defs resolve");
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            bool ok = Expect(RunSwap(fix), "the first colonist dressed");
            ok &= NotSentStraightBack(fix);

            Pawn bare = DebugTools_Fixtures.AveragePawn(Gender.Female, "Bare", doctor);
            bare.apparel?.DestroyAll();
            bare.workSettings?.EnableAndInitialize();
            GenSpawn.Spawn(bare, fix.Stand.Position + new IntVec3(2, 0, 2), fix.Map, Rot4.North);
            fix.Extras.Add(bare);
            WearOne(bare, "Apparel_BasicShirt");
            WearOne(bare, "Apparel_Pants");
            // Goto aimed at another cell, for the reason FreedStandCatchesUp
            // gives: it stays open, and it is suspendable.
            Job working = JobMaker.MakeJob(JobDefOf.Goto, fix.Stand.Position + new IntVec3(3, 0, 2));
            working.workGiverDef = tend;
            bare.jobs.StartJob(working, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);
            ok &= Expect(bare.CurJobDef == JobDefOf.Goto,
                         "the second colonist is working bare in the room (control)");

            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(10);
            ok &= Expect(fix.Comp.Covers(fix.Stand.Position + new IntVec3(3, 0, 2), fix.Map),
                         "the radius covers their work")
                & Expect(RunSwap(fix), "the first colonist changed back, freeing the stand")
                & Expect(bare.CurJobDef == JobDefOf.Goto,
                         "and the freed radius stand did NOT interrupt them");

            fix.Comp.SetServes(CatchmentKind.Room);
            ok &= Expect(RunSwap(fix), "back in room mode, the first colonist dressed again")
                & Expect(RunSwap(fix), "and changed back, freeing the stand")
                & Expect(bare.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                         "so the freed room stand interrupted them to dress (control)");
            Patch_JobInterception.DeferredJob resumed;
            ok &= Expect(Patch_JobInterception.DressedFor.TryGetValue(bare, out resumed) && resumed.Is(working),
                         "and the job it interrupted is recorded as the one they dressed for, so its return keeps the uniform on");
            Patch_JobInterception.DressedFor.Remove(bare);
            Settle(bare);
            return ok;
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

        /// <summary>
        /// STAYING DRESSED AT A RADIUS WORK STAND: inside its ring, the
        /// uniform stays on for the stand's own work, for pass-through work,
        /// and for idling and wandering, and for nothing else. Past the ring
        /// nothing keeps it, in the stand's own room included. Room mode
        /// keeping it for anything in the room is the control, and each job
        /// kept is paired with one of the same shape that changes them back,
        /// so a probe failing for its own reasons cannot pass as staying
        /// dressed.
        /// </summary>
        internal static bool RadiusWorkStandKeepsTheUniformInItsRing(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            WorkGiverDef cook = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsCook");
            WorkGiverDef haul = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulGeneral");
            WorkGiverDef clean = DefDatabase<WorkGiverDef>.GetNamedSilentFail("CleanFilth");
            WorkGiverDef construct = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructFinishFrames");
            JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
            ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
            if (doctor == null || tend == null || cook == null || haul == null || clean == null
                || construct == null || chess == null || bedDef == null)
            {
                return Expect(false, "the work, joy and bed defs resolve");
            }
            // Ticked before the bed lands: a bed makes the pad a bedroom.
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 inside = at + new IntVec3(1, 0, 2);
            IntVec3 past = at + new IntVec3(4, 0, 4);
            Building_Bed bed = DebugTools_Fixtures.Spawn(
                fix.Map, bedDef, ThingDefOf.WoodLog, at + new IntVec3(2, 0, 0), Rot4.North) as Building_Bed;
            if (bed == null)
            {
                return Expect(false, "a bed spawns in the stand's room");
            }

            bool ok = Expect(RunSwap(fix), "dressed for the shift")
                    & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(!Probe(fix, WorkAt(cook, past)),
                         "room mode: work the stand does not serve, anywhere in its room, keeps the uniform on (control)")
                & Expect(!Probe(fix, JobMaker.MakeJob(chess, inside, inside)),
                         "and so does recreation in the room, as it always has (control)");

            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(3);
            ok &= Expect(fix.Comp.Covers(inside, fix.Map) && fix.Comp.Covers(bed.Position, fix.Map)
                         && !fix.Comp.Covers(past, fix.Map),
                         "radius 3 covers the work cell and the bed, not the room's far corner (control)")
                & Expect(!Probe(fix, WorkAt(tend, inside)),
                         "Doctor work inside the ring keeps the uniform on")
                & Expect(Probe(fix, WorkAt(tend, past)),
                         "Doctor work past the ring, in the stand's own room, changes them back")
                & Expect(Probe(fix, WorkAt(cook, inside)),
                         "cooking inside the ring changes them back: the stand does not serve it")
                & Expect(!Probe(fix, WorkAt(haul, inside)),
                         "hauling inside the ring keeps the uniform on: pass-through work")
                & Expect(!Probe(fix, WorkAt(clean, inside)), "and so does cleaning")
                & Expect(!Probe(fix, WorkAt(construct, inside)), "and constructing")
                & Expect(Probe(fix, WorkAt(haul, past)), "hauling past the ring changes them back")
                & Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.GotoWander, inside)),
                         "wandering inside the ring keeps the uniform on")
                & Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.GotoWander, past)),
                         "wandering past it changes them back")
                & Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.Wait)),
                         "idling where they stand, inside the ring, keeps the uniform on")
                & Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, inside)),
                         "sleeping on the ground inside the ring changes them back: it is not on the list")
                & Expect(Probe(fix, JobMaker.MakeJob(chess, inside, inside)),
                         "and so does recreation inside it")
                & Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed)),
                         "and going to bed inside it");

            // A wait goes nowhere, so it is judged where the colonist stands.
            IntVec3 wasAt = fix.Pawn.Position;
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = past;
            ok &= Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.Wait)),
                         "idling past the ring changes them back");
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            return ok & Expect(fix.Comp.OnShift, "and they are still on shift at the end");
        }

        /// <summary>
        /// PASS-THROUGH WORK IS JUDGED AT BOTH ENDS. A haul with its item and its
        /// destination inside the ring keeps the uniform on; with either end
        /// past it, the colonist changes back before the haul, while the stand
        /// is near. A cleaning run's queue is judged the same way. An errand
        /// with no giver, queued ahead of the stand's own work inside the ring,
        /// rides along whatever its ends (docs/DESIGN.md, "Errands queued ahead
        /// of work"). Room mode keeping the
        /// outbound haul, which never leaves the room, is the control.
        /// </summary>
        internal static bool PassThroughWorkIsJudgedAtBothEnds(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            WorkGiverDef haul = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulGeneral");
            WorkGiverDef clean = DefDatabase<WorkGiverDef>.GetNamedSilentFail("CleanFilth");
            if (doctor == null || tend == null || haul == null || clean == null
                || ThingDefOf.RawPotatoes == null || ThingDefOf.Filth_Dirt == null)
            {
                return Expect(false, "the work, potato and dirt defs resolve");
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            Map map = fix.Map;
            IntVec3 at = fix.Stand.Position;
            IntVec3 from = at + new IntVec3(1, 0, 2);
            IntVec3 to = at + new IntVec3(0, 0, 3);
            IntVec3 past = at + new IntVec3(4, 0, 4);
            Thing near = Potatoes(map, from);
            Thing far = Potatoes(map, at + new IntVec3(4, 0, 2));
            Thing dirtHere = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Filth_Dirt),
                                            at + new IntVec3(2, 0, 1), map);
            Thing dirtThere = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Filth_Dirt),
                                             at + new IntVec3(0, 0, 2), map);
            Thing dirtPast = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Filth_Dirt), past, map);

            bool ok = Expect(RunSwap(fix), "dressed for the shift")
                    & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(!Probe(fix, Haul(haul, near, past)),
                         "room mode: a haul to the room's far corner keeps the uniform on (control)");

            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(3);
            ok &= Expect(fix.Comp.Covers(from, map) && fix.Comp.Covers(to, map)
                         && fix.Comp.Covers(dirtHere.Position, map) && fix.Comp.Covers(dirtThere.Position, map)
                         && !fix.Comp.Covers(far.Position, map) && !fix.Comp.Covers(past, map),
                         "radius 3 covers the near item, the dirt and the cell on the ring, not the far corner (control)")
                & Expect(!Probe(fix, Haul(haul, near, to)),
                         "a haul from inside the ring to a cell on it keeps the uniform on")
                & Expect(Probe(fix, Haul(haul, near, past)),
                         "the same haul to a cell past the ring changes them back first")
                & Expect(Probe(fix, Haul(haul, far, to)),
                         "and so does one bringing an item in from past the ring")
                & Expect(!Probe(fix, Cleaning(clean, dirtHere, dirtThere)),
                         "cleaning with every queued filth inside the ring keeps the uniform on")
                & Expect(Probe(fix, Cleaning(clean, dirtHere, dirtPast)),
                         "one queued filth past the ring changes them back");

            JobQueue queue = fix.Pawn.jobs.jobQueue;
            queue.EnqueueFirst(HarnessRideAlong.QueuedWork(tend, from));
            ok &= Expect(!Probe(fix, HarnessRideAlong.GiverlessHaul(far, to)),
                         "an errand with no giver, ahead of the stand's own work inside the ring, keeps it on");
            queue.EnqueueFirst(HarnessRideAlong.QueuedWork(tend, past));
            ok &= Expect(Probe(fix, HarnessRideAlong.GiverlessHaul(far, to)),
                         "ahead of its work past the ring, it changes them back (control)");
            return ok & Expect(fix.Comp.OnShift, "and they are still on shift at the end");
        }

        /// <summary>Ten raw potatoes on a cell, for a haul.</summary>
        internal static Thing Potatoes(Map map, IntVec3 at)
        {
            Thing potatoes = ThingMaker.MakeThing(ThingDefOf.RawPotatoes);
            potatoes.stackCount = 10;
            return GenSpawn.Spawn(potatoes, at, map);
        }

        /// <summary>A haul to a cell, from a hauling giver.</summary>
        internal static Job Haul(WorkGiverDef giver, Thing thing, IntVec3 to)
        {
            Job job = HarnessRideAlong.GiverlessHaul(thing, to);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>A cleaning run over the given filth, queued as the giver queues it.</summary>
        internal static Job Cleaning(WorkGiverDef giver, params Thing[] filth)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Clean);
            for (int i = 0; i < filth.Length; i++)
            {
                job.AddQueuedTarget(TargetIndex.A, filth[i]);
            }
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>
        /// STAYING DRESSED AT A RADIUS SLEEP STAND: the first job out of bed
        /// changes the colonist back, wherever it is, a haul inside the ring
        /// included. Going to a bed inside the ring keeps the pyjamas on, since
        /// the stand would dress them for it; a bed past the ring does not, and
        /// nor does bed rest for medical reasons, which is the work arm's. The
        /// same haul at a work stand keeping the uniform on is the control.
        /// </summary>
        internal static bool RadiusSleepStandKeepsItOnlyForBed(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef haul = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulGeneral");
            ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
            if (doctor == null || haul == null || bedDef == null)
            {
                return Expect(false, "the work and bed defs resolve");
            }
            // Ticked before the beds land. A bed makes the pad a bedroom, and on
            // an automatic stand ToggleRest would then turn sleep OFF.
            fix.Comp.ToggleRest();
            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(3);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 inside = at + new IntVec3(1, 0, 2);
            Building_Bed near = DebugTools_Fixtures.Spawn(
                fix.Map, bedDef, ThingDefOf.WoodLog, at + new IntVec3(2, 0, 0), Rot4.North) as Building_Bed;
            Building_Bed far = DebugTools_Fixtures.Spawn(
                fix.Map, bedDef, ThingDefOf.WoodLog, at + new IntVec3(4, 0, 3), Rot4.North) as Building_Bed;
            if (near == null || far == null)
            {
                return Expect(false, "two beds spawn in the stand's room");
            }

            bool ok = Expect(fix.Comp.HandlesRest() && fix.Comp.serves == CatchmentKind.Radius,
                             "the stand serves sleep within a radius (control)")
                    & Expect(fix.Comp.Covers(near.Position, fix.Map) && !fix.Comp.Covers(far.Position, fix.Map),
                             "its ring covers one bed and not the other (control)")
                    & Expect(RunSwap(fix), "dressed for bed")
                    & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, near)),
                         "going to a bed inside the ring keeps the pyjamas on")
                & Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, far)),
                         "a bed past the ring changes them back")
                & Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, near), JobTag.RestingForMedicalReasons),
                         "and so does bed rest for medical reasons, inside the ring or not");

            IntVec3 wasAt = fix.Pawn.Position;
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = near.Position;
            ok &= Expect(Probe(fix, WorkAt(haul, inside)),
                         "WAKE-UP: the first job out of bed changes them back, a haul inside the ring included")
                & Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.GotoWander, inside)),
                         "and so does wandering inside it")
                & Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, near)),
                         "while a job that keeps them in bed does not (control)");

            fix.Comp.ToggleWork(doctor);
            ok &= Expect(fix.Comp.HandlesWork(doctor) && fix.Comp.serves == CatchmentKind.Radius
                         && fix.Comp.OnShift,
                         "the same stand now serves Doctor work in its ring, the colonist still on shift (control)")
                & Expect(!Probe(fix, WorkAt(haul, inside)),
                         "and the same haul keeps the uniform on at a work stand (control)");
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            return ok;
        }

        /// <summary>
        /// STAYING DRESSED AT A RADIUS RECREATION STAND (decided 2026-10-09):
        /// inside its ring, recreation, idling and wandering keep the outfit
        /// on, and pass-through work does not, since that is a work stand's. A
        /// swimmer wanders between pools in swimwear, but changes before
        /// hauling. The same haul at a work stand keeping the uniform on is
        /// the control.
        /// </summary>
        internal static bool RadiusRecreationStandKeepsItForPlay(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef haul = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulGeneral");
            JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
            if (doctor == null || haul == null || chess == null)
            {
                return Expect(false, "the work and joy defs resolve");
            }
            fix.Comp.ToggleRecreation();
            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(3);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 inside = at + new IntVec3(1, 0, 2);
            IntVec3 past = at + new IntVec3(4, 0, 4);
            bool ok = Expect(fix.Comp.HandlesRecreation() && fix.Comp.serves == CatchmentKind.Radius,
                             "the stand serves recreation within a radius (control)")
                    & Expect(RunSwap(fix), "dressed for play")
                    & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(!Probe(fix, JobMaker.MakeJob(chess, inside, inside)),
                         "recreation inside the ring keeps the outfit on")
                & Expect(Probe(fix, JobMaker.MakeJob(chess, past, past)),
                         "recreation past the ring changes them back")
                & Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.GotoWander, inside)),
                         "wandering inside the ring keeps it on")
                & Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.Wait)),
                         "and so does idling where they stand")
                & Expect(Probe(fix, WorkAt(haul, inside)),
                         "hauling inside the ring changes them back: pass-through work is a work stand's");

            fix.Comp.ToggleWork(doctor);
            ok &= Expect(fix.Comp.HandlesWork(doctor) && fix.Comp.serves == CatchmentKind.Radius
                         && fix.Comp.OnShift,
                         "the same stand now serves Doctor work in its ring, the colonist still on shift (control)")
                & Expect(!Probe(fix, WorkAt(haul, inside)),
                         "and the same haul keeps the uniform on at a work stand (control)");
            return ok;
        }

        /// <summary>
        /// THE TABLES A RADIUS STAND STAYS DRESSED BY. Pass-through work is
        /// read from work tags, so a modded work type carrying the Hauling tag
        /// under a name of its own passes through, and one named Hauling
        /// without the tag does not. The incidental list's six rows all
        /// resolve, so none is a typo that can never match, and each counts
        /// only without a giver.
        /// </summary>
        internal static bool PassThroughAndIncidentalTablesHold()
        {
            bool ok = true;
            foreach (string name in new[] { "Hauling", "Cleaning", "Construction", "Firefighter" })
            {
                WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail(name);
                ok &= Expect(work != null && JobRoomTargets.PassesThrough(work),
                             name + " is pass-through work");
            }
            foreach (string name in new[] { "Doctor", "Cooking", "BasicWorker", "PlantCutting", "Growing", "Mining" })
            {
                WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail(name);
                ok &= Expect(work != null && !JobRoomTargets.PassesThrough(work),
                             name + " is not");
            }
            WorkTypeDef tagged = new WorkTypeDef
            {
                defName = "Harness_FSFHauling",
                workTags = WorkTags.ManualDumb | WorkTags.Hauling,
            };
            WorkTypeDef untagged = new WorkTypeDef { defName = "Hauling", workTags = WorkTags.ManualDumb };
            ok &= Expect(JobRoomTargets.PassesThrough(tagged),
                         "a modded work type carrying the Hauling tag passes through, whatever its name")
                & Expect(!JobRoomTargets.PassesThrough(untagged),
                         "and one named Hauling without the tag does not: the tag is the test")
                & Expect(!JobRoomTargets.PassesThrough(null), "nor does no work type");

            WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulGeneral");
            ok &= Expect(JobRoomTargets.IncidentalJobs.Count == 6, "the incidental list has its six rows")
                & Expect(giver != null, "the hauling giver resolves");
            foreach (string name in JobRoomTargets.IncidentalJobs)
            {
                JobDef def = DefDatabase<JobDef>.GetNamedSilentFail(name);
                if (!Expect(def != null, name + " resolves, so the row can match"))
                {
                    ok = false;
                    continue;
                }
                Job job = JobMaker.MakeJob(def);
                ok &= Expect(JobRoomTargets.IsIncidental(job), name + " with no giver is incidental");
                job.workGiverDef = giver;
                ok &= Expect(!JobRoomTargets.IsIncidental(job), "and with a giver is not");
            }
            foreach (JobDef def in new[] { JobDefOf.LayDown, JobDefOf.Ingest, JobDefOf.HaulToCell,
                                           JobDefOf.Wait_Combat })
            {
                ok &= Expect(!JobRoomTargets.IsIncidental(JobMaker.MakeJob(def)),
                             def.defName + " is not incidental");
            }
            return ok;
        }

        /// <summary>
        /// THE JOB A COLONIST WAS JUST DRESSED FOR NEVER CHANGES THEM BACK,
        /// though its target leaves the catchment while they change: a
        /// patient here, standing in for an animal walking out of an unwalled
        /// ring or a cow out of the barn. Dressed through the work arm's own
        /// <c>Insert</c>, and the deferred job started by the engine's own
        /// queue, so the job tracker's pause between the two is on the path
        /// and must not spend the grace. The next job out there changes them
        /// back, which is the control: the grace lasts one job. Once for a
        /// radius stand, then for a room stand whose patient leaves the room.
        /// </summary>
        internal static bool JustDressedForNeverSendsThemBack(Fixture fix)
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
            Pawn patient = SpawnExtra(fix, Gender.Female, "Patient");
            IntVec3 patientAt = patient.Position;

            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(3);
            bool ok = Expect(fix.Comp.Covers(patientAt, fix.Map), "the patient is inside the ring (control)")
                    & DressedForThenMoved(fix, tend, patient, fix.Stand.Position + new IntVec3(4, 0, 4),
                                          "radius: the patient walks past the ring");

            // Changed back while the stand still serves its radius, so the
            // freed stand catches nobody up on the way to room mode.
            ok &= Expect(RunSwap(fix), "changed back between the passes")
                & Expect(!fix.Comp.OnShift, "and off shift (control)");
            Settle(fix.Pawn);
            patient.pather?.StopDead();
            patient.Position = patientAt;

            fix.Comp.SetServes(CatchmentKind.Room);
            IntVec3 outside = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ);
            ok &= Expect(fix.Comp.Covers(patientAt, fix.Map) && !fix.Comp.Covers(outside, fix.Map),
                         "room mode covers the patient and not the ground outside (control)")
                & DressedForThenMoved(fix, tend, patient, outside, "room: the patient walks out of the room");
            return ok;
        }

        /// <summary>
        /// One pass of <see cref="JustDressedForNeverSendsThemBack"/>: dress for
        /// tending the patient, move the patient to <paramref name="away"/>
        /// while the colonist changes, then let the engine start the tending.
        /// </summary>
        internal static bool DressedForThenMoved(Fixture fix, WorkGiverDef tend, Pawn patient,
                                                 IntVec3 away, string pass)
        {
            Pawn pawn = fix.Pawn;
            Job tending = JobMaker.MakeJob(JobDefOf.Wait, patient);
            tending.workGiverDef = tend;
            JobQueue queue = pawn.jobs.jobQueue;
            bool inserted = Patch_JobInterception.TryInsertSwap(tending, null, pawn, pawn.jobs);
            bool ok = Expect(inserted && pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand
                             && queue.Count > 0 && queue[0].job == tending,
                             pass + ": the work arm sends them to dress, the tending deferred behind the swap");
            // RunSwap's own swap takes over from the inserted one, and the
            // queue keeps the tending: the swap driver never touches it.
            ok &= Expect(RunSwap(fix), "they dressed")
                & Expect(fix.Comp.OnShift && queue.Count > 0 && queue[0].job == tending,
                         "on shift, with the tending still queued (control)");

            patient.pather?.StopDead();
            patient.Position = away;
            ok &= Expect(!fix.Comp.Covers(away, fix.Map), "the patient walked away while they changed (control)")
                & Expect(pawn.CurJobDef == JobDefOf.Wait_MaintainPosture,
                         "the job tracker paused between the swap and the queue (control: the pause is on the path)");

            // The pause ends as the tracker ends it at expiry, and the queue
            // starts the tending through StartJob, this mod's prefix included.
            pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
            ok &= Expect(pawn.CurJob == tending,
                         "the tending they dressed for starts (" + (pawn.CurJobDef?.defName ?? "no job") + ")")
                & Expect(fix.Comp.OnShift, "in uniform: the job they were dressed for does not send them back");

            Job again = JobMaker.MakeJob(JobDefOf.Wait, patient);
            again.workGiverDef = tend;
            ok &= Expect(Probe(fix, again), "but the next job out there does (control: the grace lasts one job)");
            Settle(pawn);
            return ok & Expect(fix.Comp.OnShift, "and they are still on shift at the end of the pass");
        }

        /// <summary>
        /// A CONSTRUCTION DELIVERY CHANGES NOBODY, in either direction: a bare
        /// builder fetching steel stays bare, and a dressed one keeps the
        /// hardhat on, wherever the steel lies. The site jobs settle the
        /// uniform, and their dressing a bare builder, or changing a dressed
        /// one back past the ring, is the control. The stand ticks every work
        /// type the givers use, so each refusal is the ignore and not an
        /// unserved job. Core's five delivery-class givers and the seed
        /// planter are probed; Complex Jobs' two delivery givers are a known
        /// gap without it.
        /// </summary>
        internal static bool DeliveriesChangeNobody(Fixture fix)
        {
            WorkTypeDef construction = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Construction");
            WorkTypeDef hauling = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Hauling");
            WorkTypeDef growing = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Growing");
            WorkGiverDef finish = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructFinishFrames");
            WorkGiverDef sow = DefDatabase<WorkGiverDef>.GetNamedSilentFail("GrowerSow");
            if (construction == null || hauling == null || growing == null || finish == null || sow == null)
            {
                return Expect(false, "the construction, hauling and growing defs resolve");
            }
            fix.Comp.ToggleWork(construction);
            fix.Comp.ToggleWork(hauling);
            fix.Comp.ToggleWork(growing);

            List<WorkGiverDef> ignored = new List<WorkGiverDef>();
            List<string> names = new List<string>(HarnessTables.OfficialDeliveryGivers) { "PlantSeed" };
            foreach (string name in names)
            {
                WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(name);
                if (giver != null)
                {
                    ignored.Add(giver);
                }
            }
            bool complexJobs = ModsConfig.IsActive("FrozenSnowFox.ComplexJobs");
            int complexFound = 0;
            if (complexJobs)
            {
                WorkTypeDef fsfHauling = DefDatabase<WorkTypeDef>.GetNamedSilentFail("FSFHauling");
                if (fsfHauling != null)
                {
                    fix.Comp.ToggleWork(fsfHauling);
                }
                foreach (string name in new[] { "FSFDeliverResourcesToFrames", "FSFDeliverResourcesToBlueprints" })
                {
                    WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(name);
                    if (giver != null)
                    {
                        ignored.Add(giver);
                        complexFound++;
                    }
                }
            }
            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(3);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 inside = at + new IntVec3(1, 0, 2);
            IntVec3 past = at + new IntVec3(4, 0, 4);
            bool ok = Expect(ignored.Count >= 6, "Core's five delivery-class givers and the seed planter resolve")
                    & Expect(Probe(fix, WorkAt(finish, inside)),
                             "finishing a frame inside the ring dresses a bare builder (control)");
            foreach (WorkGiverDef giver in ignored)
            {
                ok &= Expect(fix.Comp.HandlesWork(giver.workType) && !Probe(fix, WorkAt(giver, inside)),
                             giver.defName + " dresses nobody, though the stand serves its work and its stack is inside the ring");
            }

            ok &= Expect(RunSwap(fix), "dressed for the shift")
                & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(Probe(fix, WorkAt(finish, past)), "a frame past the ring changes them back (control)")
                & Expect(Probe(fix, WorkAt(sow, past)), "and so does sowing past it (control)");
            foreach (WorkGiverDef giver in ignored)
            {
                ok &= Expect(!Probe(fix, WorkAt(giver, past)),
                             giver.defName + " keeps the uniform on, its stack past the ring");
            }

            if (!complexJobs)
            {
                return ok & ExpectKnownGap(false, "Complex Jobs' two delivery givers change nobody either",
                                           "Complex Jobs is not on this mod list; --with=FrozenSnowFox.ComplexJobs loads it");
            }
            return ok & Expect(complexFound == 2, "Complex Jobs' two delivery givers resolve, and were probed above");
        }

        /// <summary>
        /// THE WORDS FOLLOW THE CATCHMENT. The Serves row, the inspect pane and
        /// the switch's tooltip all read the stand's mode: its room, the open
        /// ground (nothing, whatever the stand is for), or its radius.
        /// Asserted on the strings the window draws, since no case opens one;
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
        /// AN AREA COVERS ITS PAINTED CELLS while the stand stands in one. It
        /// reaches a painted cell past the walls and leaves out an unpainted
        /// cell of the stand's own room, since the area replaces the room, and
        /// it covers nothing once the stand's own cell is cleared. Room mode's
        /// answers on the same cells are the control. Binding needs something
        /// ticked and an area a colonist could be restricted to, and the radius
        /// served before comes back on switching to a radius.
        /// </summary>
        internal static bool AreaCoversItsCells(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (doctor == null)
            {
                return Expect(false, "the Doctor work type resolves");
            }
            IntVec3 at = fix.Stand.Position;
            IntVec3 inRoom = at + new IntVec3(2, 0, 2);
            IntVec3 farCorner = at + new IntVec3(4, 0, 4);
            IntVec3 pastWall = at + new IntVec3(-2, 0, 2);
            Area_Allowed area = NewArea(fix, "Shift harness area", at, inRoom, pastWall);
            if (area == null)
            {
                return Expect(false, "the map has room for another allowed area");
            }

            bool ok = Expect(fix.Comp.Covers(farCorner, fix.Map) && !fix.Comp.Covers(pastWall, fix.Map),
                             "room mode covers the room's far corner and nothing past the wall (control)");
            fix.Comp.SetServeArea(area);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Room && fix.Comp.serveArea == null,
                         "with nothing ticked, the stand cannot serve an area, and the refusal binds nothing");

            fix.Comp.ToggleWork(doctor);
            fix.Comp.SetServeArea(fix.Map.areaManager.BuildRoof);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Room && fix.Comp.serveArea == null,
                         "nor the build-roof area, which no colonist can be restricted to");

            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(9);
            fix.Comp.SetServeArea(area);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Area && fix.Comp.serveArea == area,
                         "with Doctoring ticked, it serves the area")
                & Expect(CompShiftStand.CatchmentStands.Contains(fix.Comp),
                         "and is in the registry")
                & Expect(fix.Comp.Covers(inRoom, fix.Map),
                         "which covers a painted cell of its room")
                & Expect(fix.Comp.Covers(pastWall, fix.Map),
                         "and a painted cell past the wall")
                & Expect(!fix.Comp.Covers(farCorner, fix.Map),
                         "but not the room's unpainted far corner: the area replaces the room")
                & Expect(!fix.Comp.Covers(inRoom, null),
                         "and nothing on no map");

            area[at] = false;
            ok &= Expect(!fix.Comp.Covers(inRoom, fix.Map) && !fix.Comp.Covers(pastWall, fix.Map),
                         "with its own cell cleared, the stand stands outside its area and covers nothing");
            area[at] = true;
            ok &= Expect(fix.Comp.Covers(inRoom, fix.Map),
                         "painted back in, it covers again (control)");

            fix.Comp.SetServes(CatchmentKind.Radius);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Radius && fix.Comp.serveRadius == 9,
                         "switched to a radius, it serves the radius it had before");
            fix.Comp.SetServes(CatchmentKind.Area);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Area && fix.Comp.serveArea == area,
                         "and switched back, the area it was bound to");

            fix.Comp.ToggleWork(doctor);
            return ok & Expect(fix.Comp.IsExcluded && fix.Comp.serves == CatchmentKind.Room,
                               "unticking the last work type takes it back to its room (control)")
                      & Expect(fix.Comp.serveArea == area,
                               "with the area binding kept while the area lives");
        }

        /// <summary>
        /// AN AREA REPLACES THE ROOM, through the real dressing decision. Work
        /// on a painted cell outside the stand's room dresses; work on an
        /// unpainted cell does not, the stand's own room included. Room mode's
        /// answers on the same cells are the control, and painting the far
        /// corner in brings it back.
        /// </summary>
        internal static bool AreaStandDressesWithinItsArea(Fixture fix)
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

            IntVec3 at = fix.Stand.Position;
            IntVec3 nearOutside = new IntVec3(fix.Outside.minX, 0, at.z);
            IntVec3 farOutside = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ);
            IntVec3 farCorner = at + new IntVec3(4, 0, 4);
            Area_Allowed area = NewArea(fix, "Shift harness area", at, nearOutside);
            if (area == null)
            {
                return Expect(false, "the map has room for another allowed area");
            }

            bool ok = Expect(!Diverts(fix, HarnessGates.DoctoringAt(tend, nearOutside)),
                             "room mode: work outside the stand's room does not dress (control)")
                    & Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, farCorner)),
                             "room mode: work in the room's far corner does (control)");

            fix.Comp.SetServeArea(area);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Area,
                         "the stand now serves the area (control)")
                & Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, nearOutside)),
                         "work on a painted cell outside the room dresses")
                & Expect(!Diverts(fix, HarnessGates.DoctoringAt(tend, farOutside)),
                         "work on an unpainted cell outside does not")
                & Expect(!Diverts(fix, HarnessGates.DoctoringAt(tend, farCorner)),
                         "and nor does the room's unpainted far corner: the area replaces the room");

            area[farCorner] = true;
            return ok & Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, farCorner)),
                               "painted in, the far corner dresses again (control)");
        }

        /// <summary>
        /// A DELETED AREA LEAVES ITS STAND SERVING NOTHING. The stand stays in
        /// area mode under the area's last name, a rename included, and covers
        /// no cell and dresses nobody, rather than falling back to its room. A
        /// new area under the same name is not adopted, since default names
        /// repeat; picking it binds it, which is the paired positive.
        /// </summary>
        internal static bool DeletedAreaServesNobody(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            if (doctor == null || tend == null)
            {
                return Expect(false, "the doctor work defs resolve");
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 inRoom = at + new IntVec3(2, 0, 2);
            Area_Allowed area = NewArea(fix, "Shift harness area", at, inRoom);
            if (area == null)
            {
                return Expect(false, "the map has room for another allowed area");
            }
            fix.Comp.SetServeArea(area);
            area.SetLabel("Shift harness renamed");
            bool ok = Expect(fix.Comp.serves == CatchmentKind.Area && fix.Comp.Covers(inRoom, fix.Map),
                             "the stand serves its area (control)")
                    & Expect(fix.Comp.LiveServeArea() == area
                             && fix.Comp.serveAreaLabel == "Shift harness renamed",
                             "and, read again after a rename, goes by the area's new name")
                    & Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, inRoom)),
                             "work in the area dresses (control)");

            area.Delete();
            ok &= Expect(fix.Comp.LiveServeArea() == null && !fix.Comp.Covers(inRoom, fix.Map),
                         "with the area deleted, the stand covers nothing")
                & Expect(!Diverts(fix, HarnessGates.DoctoringAt(tend, inRoom)),
                         "and dresses nobody for the same work")
                & Expect(fix.Comp.serves == CatchmentKind.Area
                         && fix.Comp.serveAreaLabel == "Shift harness renamed",
                         "it stays in area mode under the area's last name, rather than falling back to its room");

            Area_Allowed namesake = NewArea(fix, "Shift harness renamed", at, inRoom);
            if (namesake == null)
            {
                return ok & Expect(false, "the map has room for another allowed area");
            }
            ok &= Expect(!fix.Comp.Covers(inRoom, fix.Map) && fix.Comp.serveArea != namesake,
                         "a new area under the same name is not adopted");
            fix.Comp.SetServeArea(namesake);
            return ok & Expect(fix.Comp.serveArea == namesake && fix.Comp.Covers(inRoom, fix.Map),
                               "picking it binds it, and it covers again (control)");
        }

        /// <summary>
        /// THE ALERT LISTS A STAND THAT SERVES NOTHING. Vanilla made the alert
        /// from the subclass alone. It leaves out an area stand that serves
        /// (control), lists the stand while it stands outside its area and once
        /// the area is deleted, naming the area each time, and drops it when
        /// the stand serves its room again. A colonist in the stand's uniform,
        /// kept in it by work inside the area, changes back for the same work
        /// once the area is deleted.
        /// </summary>
        internal static bool ServesNothingAlertListsTheStand(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            if (doctor == null || tend == null)
            {
                return Expect(false, "the doctor work defs resolve");
            }
            Alert alert = VanillaAlert(typeof(Alert_ShiftStandServesNothing));
            bool ok = Expect(alert != null, "vanilla's alert readout made the alert from the subclass alone");
            if (alert == null)
            {
                return false;
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 inRoom = at + new IntVec3(2, 0, 2);
            Area_Allowed area = NewArea(fix, "Shift harness area", at, inRoom);
            if (area == null)
            {
                return ok & Expect(false, "the map has room for another allowed area");
            }
            fix.Comp.SetServeArea(area);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Area && !Lists(alert, fix.Stand),
                         "a stand serving its area is not listed (control)");

            area[at] = false;
            ok &= Expect(Lists(alert, fix.Stand) && Explains(alert, "Shift harness area"),
                         "standing outside its area, it is listed, under the area's name");
            area[at] = true;
            ok &= Expect(!Lists(alert, fix.Stand), "painted back in, it is not (control)");

            ok &= Expect(RunSwap(fix), "dressed at the area stand")
                & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(!Probe(fix, WorkAt(tend, inRoom)),
                         "Doctor work in the area keeps the uniform on (control)");

            area.Delete();
            ok &= Expect(Lists(alert, fix.Stand) && Explains(alert, "Shift harness area"),
                         "with the area deleted, the stand is listed, under the area's name")
                & Expect(Probe(fix, WorkAt(tend, inRoom)),
                         "and the colonist in its uniform changes back for the same work");

            fix.Comp.SetServes(CatchmentKind.Room);
            return ok & Expect(!Lists(alert, fix.Stand),
                               "serving its room again, it is not listed (control)");
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
            JobPlace place = Patch_JobInterception.SharedTargetPlace(JobOn(JobDefOf.Mine, mine, rock), fix.Map);
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
        /// The explanation <paramref name="alert"/> gives with a fresh report
        /// contains <paramref name="text"/>. Vanilla asks for the report first
        /// too: the explanation is built from what the report found.
        /// </summary>
        internal static bool Explains(Alert alert, string text)
        {
            alert.GetReport();
            return alert.GetExplanation().Resolve().Contains(text);
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
        /// THE CHANGE-BACK LATCH IS PER STAND AND TRIGGER. After Change back at
        /// a radius stand, neither it nor a pooled room stand beside it, with
        /// the same trigger, dresses the colonist. A recreation stand in the
        /// same room does, and so does a radius stand in the open whose ring
        /// does not reach the latched one. Lifting the latch by hand
        /// is the control for the refusal. One cell past the ring, beside a
        /// covered cell, still holds it, and a job boundary with no covered
        /// cell beside it releases it.
        /// </summary>
        internal static bool LatchHoldsItsStandAndTriggerOnly(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
            if (doctor == null || tend == null || chess == null)
            {
                return Expect(false, "the doctor and joy defs resolve");
            }
            if (fix.Outside.Area == 0)
            {
                return Expect(false, "the fixture has a door and open ground outside it");
            }
            fix.Comp.ToggleWork(doctor);
            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(4);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            CompShiftStand pooled = StageStand(fix, at + new IntVec3(0, 0, 2));
            CompShiftStand play = StageStand(fix, at + new IntVec3(0, 0, 4));
            // Three cells along the open strip from the door: a ring of the
            // smallest radius there reaches the work beside the door (3 cells)
            // and neither the latched stand (about 3.6) nor the room's work.
            CompShiftStand open = StageStand(fix, new IntVec3(fix.Outside.minX, 0, at.z + 3));
            if (pooled == null || play == null || open == null)
            {
                return Expect(false, "three more stands could be staged");
            }
            pooled.ToggleWork(doctor);
            play.ToggleRecreation();
            open.ToggleWork(doctor);
            // A room stand in the open serves nothing, so this one serves a ring.
            open.SetServes(CatchmentKind.Radius);
            open.SetServeRadius(CompShiftStand.MinServeRadius);

            IntVec3 inRoom = at + new IntVec3(2, 0, 2);
            IntVec3 joyAt = at + new IntVec3(3, 0, 3);
            IntVec3 doorside = new IntVec3(fix.Outside.minX, 0, at.z);
            IntVec3 pastRing = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 1);
            bool ok = Expect(fix.Comp.Covers(inRoom, fix.Map) && fix.Comp.Covers(doorside, fix.Map)
                             && !fix.Comp.Covers(pastRing, fix.Map),
                             "the ring covers the work in the room and beside the door, and not the far end outside (control)")
                    & Expect(pooled.serves == CatchmentKind.Room && pooled.Covers(at, fix.Map)
                             && !open.Covers(at, fix.Map),
                             "the pooled stand's room holds the latched stand, the open stand's ring does not (control)")
                    & Expect(open.Covers(doorside, fix.Map) && !open.Covers(inRoom, fix.Map),
                             "and that ring takes the work beside the door, not the room's (control)")
                    & Expect(RunSwap(fix), "dressed at the radius stand")
                    & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);

            Patch_ChangeBackGizmo.ChangeBack(fix.Pawn, fix.Comp);
            fix.Pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            ok &= Expect(RunSwap(fix), "Change back: they changed out")
                & Expect(!fix.Comp.OnShift && Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "and the latch is set (control)");
            Settle(fix.Pawn);

            ok &= Expect(!Diverts(fix, HarnessGates.DoctoringAt(tend, inRoom)),
                         "latched: work in the ring dresses them neither at the stand nor at the pooled stand beside it")
                & Expect(Diverts(fix, JobMaker.MakeJob(chess, joyAt, joyAt)),
                         "a recreation stand in the same room still does: the latch is the work trigger's")
                & Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, doorside)),
                         "and so does a radius stand in the open whose ring does not reach the latched stand")
                & Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "the latch holding all the while, since they never left the ring");

            Patch_JobInterception.ChangedBackAt.Remove(fix.Pawn);
            ok &= Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, inRoom)),
                         "with the latch lifted, the same work in the ring dresses them (control)");

            Patch_JobInterception.ChangedBackAt[fix.Pawn] = new Patch_JobInterception.ChangeBackLatch(fix.Stand);
            IntVec3 wasAt = fix.Pawn.Position;
            // One cell past the ring is beside a covered cell, where work at
            // the ring's edge is done from, so the pause there is not leaving.
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = pastRing;
            Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1));
            ok &= Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "the pause one cell past the ring, beside a covered cell, keeps the latch");
            IntVec3 farEnd = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ);
            bool farClear = System.Array.TrueForAll(GenAdj.AdjacentCellsAndInside,
                                                    o => !fix.Comp.Covers(farEnd + o, fix.Map));
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = farEnd;
            Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, farEnd));
            ok &= Expect(farClear && !Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "a job boundary at the far end, no covered cell beside it, releases the latch");
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            return ok;
        }

        /// <summary>
        /// AT A SLEEP RACK THE LATCH HOLDS THROUGH THE NIGHT AND LIFTS ON
        /// WAKING. After Change back, going to bed does not dress the colonist
        /// again, and the job tracker's pause does not count as waking; the
        /// first job after the lay-down releases the latch, and bed dresses
        /// them again, which is the control. A haul vanilla starts ahead of the
        /// lay-down, putting it back in the queue, is not the waking either.
        /// Released on lying down instead,
        /// the latch would undo the order at the very next boundary. A Change
        /// back that wakes a sleeper is not itself the waking: the latch holds
        /// through the job after it.
        /// </summary>
        internal static bool SleepRackLatchLiftsOnWaking(Fixture fix)
        {
            ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
            if (bedDef == null)
            {
                return Expect(false, "the bed def resolves");
            }
            // Ticked before the bed lands, as in RadiusSleepStandKeepsItOnlyForBed.
            fix.Comp.ToggleRest();
            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(3);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 inside = at + new IntVec3(1, 0, 2);
            Building_Bed bed = DebugTools_Fixtures.Spawn(
                fix.Map, bedDef, ThingDefOf.WoodLog, at + new IntVec3(2, 0, 0), Rot4.North) as Building_Bed;
            if (bed == null)
            {
                return Expect(false, "a bed spawns inside the ring");
            }

            bool ok = Expect(RunSwap(fix), "dressed for bed")
                    & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);

            Patch_ChangeBackGizmo.ChangeBack(fix.Pawn, fix.Comp);
            fix.Pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            ok &= Expect(RunSwap(fix), "Change back: they changed out of their sleepwear")
                & Expect(!fix.Comp.OnShift && Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "and the latch is set (control)");
            Settle(fix.Pawn);

            ok &= Expect(!Diverts(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed)),
                         "latched: going to bed does not dress them again")
                & Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "and lying down does not release the latch");

            // Vanilla's StartJob can put the lay-down back in the queue and
            // start a haul first (Pawn_JobTracker.TryOpportunisticJob).
            fix.Pawn.jobs.jobQueue.EnqueueFirst(JobMaker.MakeJob(JobDefOf.LayDown, bed));
            Diverts(fix, JobMaker.MakeJob(JobDefOf.HaulToCell, inside, inside));
            fix.Pawn.jobs.ClearQueuedJobs();
            ok &= Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "a haul vanilla starts ahead of the lay-down, which waits in the queue, is not the waking");

            IntVec3 wasAt = fix.Pawn.Position;
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = bed.Position;
            Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1));
            ok &= Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "the job tracker's pause between jobs is not the waking");
            Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, inside));
            ok &= Expect(!Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "WAKE-UP: the first job after the lay-down releases the latch");

            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            ok &= Expect(Diverts(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed)),
                         "so going to bed again dresses them (control)");

            // Pressed while they lie in bed: the press is what woke them.
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = bed.Position;
            Patch_JobInterception.ChangedBackAt[fix.Pawn] = new Patch_JobInterception.ChangeBackLatch(fix.Stand);
            Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, inside));
            ok &= Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "a Change back that woke them is not the waking: the latch holds through the job after it");
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            return ok;
        }
    }
}
#endif
