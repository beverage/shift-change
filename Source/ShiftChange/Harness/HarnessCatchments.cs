// HARNESS only: dev tooling, compiled out of a plain Release build (the
// configuration table is in ShiftChange.csproj). The guard is whole-file,
// always, and check-invariants.py enforces it; why: docs/DESIGN.md,
// "Development tooling".
#if HARNESS
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Harness cases for catchments: a stand serving a radius around itself
    /// instead of its room. docs/DESIGN.md, "Catchments".
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
        /// ground (everywhere for work, nothing for recreation), or its radius.
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
            open.ToggleWork(doctor);
            ok &= Expect(open.StandsInTheOpen
                         && open.ServesLabel() == (string)"ShiftChange.ServesOutdoors".Translate(),
                         "a work stand in the open serves everywhere outdoors");
            open.ToggleRecreation();
            ok &= Expect(open.ServesLabel() == (string)"ShiftChange.ServesNothingOutdoors".Translate(),
                         "a recreation stand there serves nothing");
            open.SetServes(CatchmentKind.Radius);
            return ok & Expect(open.ServesLabel() == (string)"ShiftChange.ServesRadius".Translate(open.serveRadius),
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
        /// THE CHANGE-BACK LATCH IS PER STAND AND TRIGGER. After Change back at
        /// a radius stand, neither it nor a pooled room stand beside it, with
        /// the same trigger, dresses the colonist. A recreation stand in the
        /// same room does, and so does a work stand in the open whose
        /// catchment does not reach the latched one. Lifting the latch by hand
        /// is the control for the refusal, and a job boundary past the ring
        /// releases it.
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
            CompShiftStand open = StageStand(fix, new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ));
            if (pooled == null || play == null || open == null)
            {
                return Expect(false, "three more stands could be staged");
            }
            pooled.ToggleWork(doctor);
            play.ToggleRecreation();
            open.ToggleWork(doctor);

            IntVec3 inRoom = at + new IntVec3(2, 0, 2);
            IntVec3 joyAt = at + new IntVec3(3, 0, 3);
            IntVec3 doorside = new IntVec3(fix.Outside.minX, 0, at.z);
            IntVec3 pastRing = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 1);
            bool ok = Expect(fix.Comp.Covers(inRoom, fix.Map) && fix.Comp.Covers(doorside, fix.Map)
                             && !fix.Comp.Covers(pastRing, fix.Map),
                             "the ring covers the work in the room and beside the door, and not the far end outside (control)")
                    & Expect(pooled.serves == CatchmentKind.Room && pooled.Covers(at, fix.Map)
                             && !open.Covers(at, fix.Map),
                             "the pooled stand's room holds the latched stand, the open stand's does not (control)")
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
                         "and so does a work stand in the open, which does not reach the latched stand")
                & Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "the latch holding all the while, since they never left the ring");

            Patch_JobInterception.ChangedBackAt.Remove(fix.Pawn);
            ok &= Expect(Diverts(fix, HarnessGates.DoctoringAt(tend, inRoom)),
                         "with the latch lifted, the same work in the ring dresses them (control)");

            Patch_JobInterception.ChangedBackAt[fix.Pawn] = new Patch_JobInterception.ChangeBackLatch(fix.Stand);
            IntVec3 wasAt = fix.Pawn.Position;
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = pastRing;
            Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, pastRing));
            ok &= Expect(!Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "a job boundary past the ring releases the latch");
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            return ok;
        }

        /// <summary>
        /// AT A SLEEP RACK THE LATCH HOLDS THROUGH THE NIGHT AND LIFTS ON
        /// WAKING. After Change back, going to bed does not dress the colonist
        /// again, and the job tracker's pause does not count as waking; the
        /// first job after the lay-down releases the latch, and bed dresses
        /// them again, which is the control. Released on lying down instead,
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
