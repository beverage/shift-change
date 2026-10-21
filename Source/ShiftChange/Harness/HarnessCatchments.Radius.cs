// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
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
    internal static partial class HarnessCatchments
    {
        /// <summary>
        /// A radius covers its ring, inclusive: every cell <c>GenRadial.NumCellsInRadius</c>
        /// counts, which is what <c>DrawRadiusRing</c> outlines, and not the next cell of the
        /// pattern. Asserted against the ring's own cell list rather than a distance formula,
        /// since the ring is what the player reads. Room mode's answer, the room, is the control.
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
            // The stand stands one cell in from the pad's south-west corner, so two cells west of
            // it is the pad's margin, outside the walls.
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
        /// A radius replaces the room. Work outside the stand's room but inside its ring dresses;
        /// work past the ring does not, even in the stand's own room. Room mode's answers on the
        /// same cells are the control.
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
        /// The outdoor guard is for room stands. A room stand standing in the open serves no
        /// outdoor recreation, because the outdoor room spans the whole map; the same stand given
        /// a radius does, because a radius is bounded. The radius half is the control.
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
        /// Precedence is unchanged: the colonist's own stand first, then the nearest. An owned
        /// radius stand outside the room beats a nearer shared room stand inside it, and without
        /// the owner the nearer one wins, which is the control.
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
        /// A freed radius stand interrupts nobody, where a room stand catches up a colonist
        /// already working bare inside it. Both halves on one fixture, the room half as the
        /// control.
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
            // Goto aimed at another cell: one to the pawn's own cell completes inside StartJob and
            // the think tree then hands them a Wait_Wander, which is not suspendable, so the
            // catch-up would pass it over. Aimed elsewhere it stays open, since nothing ticks
            // this pawn to serve the path request.
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
    }
}
#endif
