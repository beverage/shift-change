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
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Harness cases for the gates on interception: meal breaks, the danger and duty gates, the
    /// change-back latch, a stand outside the colonist's allowed area, and the catch-up that
    /// dresses a colonist already working bare. Case order is set where the cases are listed, in
    /// HarnessCases.cs.
    /// </summary>
    internal static class HarnessGates
    {
        /// <summary>
        /// On a work stand a meal is a sit-down break: the uniform comes off first wherever the
        /// food is stored, or a cook would carry a meal across the base in whites to reach a chair.
        /// The one exception is food already in hand or pack, which is just eaten.
        /// </summary>
        internal static bool MealBreakChangesOut(Fixture fix)
        {
            bool ok = Expect(RunSwap(fix), "dressed for the shift")
                    & Expect(fix.Comp.OnShift, "and is on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }

            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
            if (mealDef == null)
            {
                return Expect(false, "MealSimple resolves");
            }

            Thing stored = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef),
                                          fix.Pawn.Position, fix.Map);
            ok &= Expect(Diverts(fix, IngestJob(stored)),
                         "a meal break gets them out of uniform first");

            Thing carried = ThingMaker.MakeThing(mealDef);
            fix.Pawn.inventory.innerContainer.TryAdd(carried);
            ok &= Expect(!Diverts(fix, IngestJob(carried)),
                         "but food already carried is simply eaten");
            return ok;
        }

        /// <summary>
        /// The meal-break rule on a sleep stand, where food already in the inventory still changes
        /// the pawn out of sleepwear first. At the wake-up the pawn is still in the stand's room,
        /// near it, so eating elsewhere in sleepwear and walking back to change is the trip the
        /// rest arm exists to prevent. On a work or recreation stand carried food is just eaten
        /// (<see cref="MealBreakChangesOut"/>: same colonist, same meal, the opposite answer).
        ///
        /// <para>Both halves are asserted. The stored half is the control: a stockpile breakfast
        /// changes the pawn out of sleepwear, and the carried half must answer the same, so the two
        /// differ only in where the food is.</para>
        /// </summary>
        internal static bool SleepwearMealBreakChangesOut(Fixture fix)
        {
            ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
            if (bedDef == null || mealDef == null)
            {
                return Expect(false, "the bed and meal defs resolve");
            }

            // Do not toggle rest on by hand. A bed makes the pad a bedroom (RoomRoleWorker_Bedroom
            // scores a single unowned humanlike bed at 100000), so HandlesRest() is already true
            // from the RestRoles table and a toggle would turn it off. Same trap as
            // SleepJobInTheRoomDresses.
            Building_Bed bed = DebugTools_Fixtures.Spawn(
                fix.Map, bedDef, ThingDefOf.WoodLog,
                new IntVec3(fix.Stand.Position.x + 2, 0, fix.Stand.Position.z + 2),
                Rot4.North) as Building_Bed;
            if (bed == null)
            {
                return Expect(false, "a bed spawns in the stand's room");
            }

            bool ok = Expect(fix.Comp.HandlesRest(),
                             "the bed makes this a bedroom, so the stand serves sleep")
                    & Expect(!fix.Comp.HandlesRecreation(),
                             "and not recreation, which has its own carve-out in this branch");
            if (!fix.Comp.HandlesRest())
            {
                return false;
            }

            ok &= Expect(RunSwap(fix), "dressed for bed")
                & Expect(fix.Comp.OnShift, "and is on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }

            Thing stored = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef),
                                          fix.Pawn.Position, fix.Map);
            ok &= Expect(Diverts(fix, IngestJob(stored)),
                         "a stored breakfast gets them out of sleepwear first, as it always did");

            Thing carried = ThingMaker.MakeThing(mealDef);
            fix.Pawn.inventory.innerContainer.TryAdd(carried);
            ok &= Expect(Diverts(fix, IngestJob(carried)),
                         "and so does one already in their inventory, which is the regression this guards");

            // The wake-up: the real breakfast is decided with the pawn still standing on the bed,
            // and StaysInBed must not swallow it there. JobDriver_Ingest does not override
            // CanBeginNowWhileLyingDown (the base returns false) and an ingest job's targetB is not
            // a bed, so eating is not something done lying down and the return trip is reached. If
            // that changes, the bug returns behind a different guard; this pins it.
            IntVec3 wasAt = fix.Pawn.Position;
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = bed.Position;
            ok &= Expect(Patch_JobInterception.OnABed(fix.Pawn),
                         "the pawn is on the bed (control for the wake-up)")
                & Expect(!Patch_JobInterception.StaysInBed(fix.Pawn, IngestJob(carried)),
                         "and eating is not something they can do lying in it")
                & Expect(Diverts(fix, IngestJob(carried)),
                         "so the wake-up changes them back BEFORE breakfast, at the stand");

            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = wasAt;
            return ok;
        }

        /// <summary>
        /// The danger gate is one-directional: under threat a pawn may not change into a uniform,
        /// but one already wearing it still changes back. The control is the same work job dressing
        /// on a calm map. Guards a shipped regression where one gate answered for both arms
        /// (docs/DESIGN.md, "The gates"), so both halves run against the same threat: a case that
        /// only proved dressing blocked would pass on the broken build.
        ///
        /// <para>The return leg is triggered by a meal break rather than by geometry: on a work
        /// stand an ingest job whose food is not on the pawn diverts wherever the food sits, so no
        /// second room is needed.</para>
        ///
        /// <para>No hostile is on the map while the colonist is ticked. The harness ticks fixture
        /// pawns one at a time and never the map, so a threat never acts on its own, but
        /// <see cref="RunSwap"/> ticks the colonist, and a ticked colonist reacts to a hostile
        /// three tiles away: the think tree overrides the swap mid-run (<c>InterruptForced</c> at
        /// toil 1), intermittently. So the threat is staged twice, around the driver run, and
        /// cleared before anything ticks.</para>
        /// </summary>
        internal static bool DangerGateIsOneDirectional(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
            if (doctor == null || tend == null || mealDef == null)
            {
                return Expect(false, "the doctor and meal defs resolve");
            }
            fix.Comp.ToggleWork(doctor);

            // Arrange the calm rather than assume it: a generated map can start with a hostile on
            // it (see MakeCalm).
            MakeCalm(fix.Map);
            bool ok = Expect(fix.Map.dangerWatcher.DangerRating == StoryDanger.None,
                             "the map starts calm (control)")
                    & Expect(Diverts(fix, WorkJob(fix, tend)),
                             "and an automatic work job dresses while calm (control)");

            // The half that stays gated. Diverts() never ticks the pawn, so the hostile cannot
            // disturb it.
            Pawn threat = Threat(fix);
            if (threat == null)
            {
                return Expect(false, "a hostile could be staged");
            }
            ok &= Expect(fix.Map.dangerWatcher.DangerRating != StoryDanger.None,
                         "a live hostile raises the danger rating")
                & Expect(!Diverts(fix, WorkJob(fix, tend)),
                         "under threat, an automatic work job does NOT dress");
            ClearThreat(fix, threat);

            // Dressed through the driver, not the trigger the assertion above just showed is shut,
            // and with no hostile on the map: this is the only step that ticks the colonist.
            ok &= Expect(RunSwap(fix), "dressed for the shift on a calm map")
                & Expect(fix.Comp.OnShift, "and is on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }

            threat = Threat(fix);
            if (threat == null)
            {
                return Expect(false, "a hostile could be staged for the return leg");
            }
            Thing meal = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef),
                                        fix.Pawn.Position, fix.Map);
            ok &= Expect(fix.Map.dangerWatcher.DangerRating != StoryDanger.None,
                         "the threat is back for the return leg")
                & Expect(Diverts(fix, IngestJob(meal)),
                         "but a meal break STILL gets them out of uniform under threat");
            ClearThreat(fix, threat);

            ok &= Expect(fix.Map.dangerWatcher.DangerRating == StoryDanger.None,
                         "and the map reads calm again once the threat is gone");
            return ok;
        }

        /// <summary>
        /// The duty gate shuts both arms: a pawn under a lord duty is neither dressed nor sent back
        /// to change until the duty ends, and a bare duty with no lord holds it shut too. Each
        /// refusal sits beside the same job on a free colonist. Guards a shipped stranding bug
        /// (docs/DESIGN.md, "The gates").
        ///
        /// <para>Staged through <c>LordMaker</c> rather than by setting fields, because the gate
        /// reads what the engine writes and a fixture that wrote them itself would certify its own
        /// guess. <c>LordJob_DefendPoint</c> needs no ritual, no DLC and no second pawn; any lord
        /// duty would do. The case also asserts the gate opens again the moment the duty ends, so
        /// it cannot latch a colonist out of the mod.</para>
        /// </summary>
        internal static bool DutyGateShutsBothArms(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
            if (doctor == null || tend == null || mealDef == null)
            {
                return Expect(false, "the doctor and meal defs resolve");
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            bool ok = Expect(Diverts(fix, WorkJob(fix, tend)),
                             "an automatic work job dresses a free colonist (control)");

            Lord lord = LordMaker.MakeNewLord(Faction.OfPlayer,
                new LordJob_DefendPoint(fix.Pawn.Position), fix.Map, new[] { fix.Pawn });
            if (lord == null)
            {
                return Expect(false, "a lord could be staged");
            }
            ok &= Expect(fix.Pawn.GetLord() == lord, "the colonist is under that lord")
                & Expect(fix.Pawn.mindState?.duty != null, "and the lord issued them a duty")
                & Expect(!Diverts(fix, WorkJob(fix, tend)),
                         "so an automatic work job does NOT dress them");

            // Dressed while free, since the dress arm is shut under a duty. The return arm is the
            // one that strands a pawn.
            lord.RemovePawn(fix.Pawn);
            ok &= Expect(RunSwap(fix), "dressed for the shift once released")
                & Expect(fix.Comp.OnShift, "and is on shift (control)");
            if (!fix.Comp.OnShift)
            {
                fix.Map.lordManager.RemoveLord(lord);
                return false;
            }

            // A meal break triggers the return trip without a second room: the ingest branch
            // changes them out wherever the food is.
            Thing meal = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef),
                                        fix.Pawn.Position, fix.Map);
            ok &= Expect(Diverts(fix, IngestJob(meal)),
                         "a meal break changes a free colonist back (control)");

            lord.AddPawn(fix.Pawn);
            ok &= Expect(fix.Pawn.mindState?.duty != null, "back under a duty")
                & Expect(!Diverts(fix, IngestJob(meal)),
                         "and the return trip does NOT fire — this is the stranding");

            lord.RemovePawn(fix.Pawn);
            ok &= Expect(fix.Pawn.GetLord() == null && fix.Pawn.mindState?.duty == null,
                         "releasing the pawn clears the lord and the duty together")
                & Expect(Diverts(fix, IngestJob(meal)),
                         "so the change-back fires the moment the duty ends");

            // A duty with no lord owning it, which a mod's own think node can leave on a pawn. The
            // gate tests the duty as well as the lord for this.
            fix.Pawn.mindState.duty = new PawnDuty(DutyDefOf.Idle);
            ok &= Expect(!Diverts(fix, IngestJob(meal)),
                         "a bare duty with no lord holds the gate shut too");
            fix.Pawn.mindState.duty = null;
            ok &= Expect(Diverts(fix, IngestJob(meal)),
                         "and clearing it opens the gate again (control)");

            fix.Map.lordManager.RemoveLord(lord);
            return ok;
        }

        /// <summary>
        /// A freed stand catches up a colonist already working bare in its room. Guards the
        /// announcement's timing: the swap driver announces the freed stand from a global finish
        /// action, which the job tracker runs after it releases the departing pawn's reservation (a
        /// toil's finish action runs earlier). Announced from a toil, every candidate fails
        /// <c>CanReserveAndReach</c> against the stand's single reservation and the colonist is
        /// never interrupted at all, which makes the case binary.
        /// </summary>
        internal static bool FreedStandCatchesUp(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            if (doctor == null || tend == null)
            {
                return Expect(false, "the doctor work defs resolve");
            }
            fix.Comp.ToggleWork(doctor);

            MakeCalm(fix.Map);
            bool ok = Expect(fix.Map.dangerWatcher.DangerRating == StoryDanger.None,
                             "the map is calm — the catch-up is danger-gated")
                    & Expect(RunSwap(fix), "the first colonist dressed");

            Pawn bare = DebugTools_Fixtures.AveragePawn(Gender.Female, "Bare", doctor);
            bare.apparel?.DestroyAll();
            bare.workSettings?.EnableAndInitialize();
            GenSpawn.Spawn(bare, fix.Stand.Position + new IntVec3(2, 0, 2), fix.Map, Rot4.North);
            fix.Extras.Add(bare);
            WearOne(bare, "Apparel_BasicShirt");
            WearOne(bare, "Apparel_Pants");

            // Working in the room, in their own clothes, on a job the catch-up may interrupt and
            // that stays open while the first colonist changes back (WorkingAt).
            Job working = WorkingAt(tend, fix.Stand.Position + new IntVec3(3, 0, 2));
            bare.jobs.StartJob(working, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);

            ok &= Expect(SwapPlan.WouldDress(bare, fix.Stand),
                         "the second colonist could wear what the stand holds")
                & Expect(StillWorking(bare, tend),
                         "and is still on the interruptible job we gave them (control)");

            ok &= Expect(RunSwap(fix), "the first colonist changed back, freeing the stand");
            return ok & Expect(bare.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                               "the freed stand interrupted them to dress");
        }

        /// <summary>
        /// The change-back latch: after Change back, neither the stand they changed at nor another
        /// stand serving the same trigger in that room dresses them until they leave the room; a
        /// stand in another room still dresses them meanwhile, drafting drops the latch, and the
        /// mid-job catch-up honours it. Every refusal sits beside the same job without the latch as
        /// its control (docs/DESIGN.md, "Change back").
        /// </summary>
        internal static bool ChangeBackLatchHoldsUntilTheyLeave(Fixture fix)
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
            Patch_JobInterception.ChangedBackAt.Remove(fix.Pawn);

            bool ok = Expect(Diverts(fix, WorkJob(fix, tend)),
                             "a work job in the stand's room dresses (control)")
                    & Expect(RunSwap(fix), "dressed for the shift")
                    & Expect(fix.Comp.OnShift, "and is on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }

            // The button's own path: it orders a swap and sets the latch. The ordered swap is cut
            // short and run through RunSwap, which steps the clock until it ends; a swap
            // interrupted before its transfer announces nothing, so the cut leaves no trace.
            Patch_ChangeBackGizmo.ChangeBack(fix.Pawn, fix.Comp);
            ok &= Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "Change back set the latch");
            fix.Pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            ok &= Expect(RunSwap(fix), "and they changed back")
                & Expect(!fix.Comp.OnShift, "so nothing of the stand's is out (control)");

            ok &= Expect(!Diverts(fix, WorkJob(fix, tend)),
                         "latched: a work job in the same room does NOT dress them")
                & Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "and a job boundary inside the room keeps the latch");

            // A stand in another room still dresses them meanwhile. It is outside the door and the
            // colonist stays inside, so the latch is still held while the work arm looks out there.
            // A room stand in the open serves nothing, so it serves the smallest ring instead.
            IntVec3 outsideCell = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 1);
            Building_OutfitStand other = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                fix.Map, fix.Stand.def, ThingDefOf.WoodLog, outsideCell, Rot4.North);
            CompShiftStand otherComp = other?.TryGetComp<CompShiftStand>();
            if (otherComp == null || !StockOne(other, "Apparel_Duster"))
            {
                return Expect(false, "a second stand could be staged outside the door");
            }
            otherComp.ToggleWork(doctor);
            otherComp.SetServes(CatchmentKind.Radius);
            otherComp.SetServeRadius(CompShiftStand.MinServeRadius);
            ok &= Expect(other.GetRoom() != fix.Stand.GetRoom(),
                         "the second stand is in another room")
                & Expect(otherComp.serves == CatchmentKind.Radius,
                         "serving a ring around it (control)")
                & Expect(Diverts(fix, DoctoringAt(tend, outsideCell)),
                         "and work out there still dresses them at it")
                & Expect(Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "while the latch on their own room holds");
            other.Destroy();

            // Leaving releases it at the next job boundary, whatever the job: here a giver-less
            // Wait, which no arm dresses for.
            IntVec3 inside = fix.Pawn.Position;
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = outsideCell;
            Diverts(fix, JobMaker.MakeJob(JobDefOf.Wait, outsideCell));
            ok &= Expect(!Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                         "a job boundary outside the room releases the latch");
            fix.Pawn.pather?.StopDead();
            fix.Pawn.Position = inside;
            ok &= Expect(Diverts(fix, WorkJob(fix, tend)),
                         "so back inside, the room's work dresses them again (control)");

            Patch_JobInterception.ChangedBackAt[fix.Pawn] = new Patch_JobInterception.ChangeBackLatch(fix.Stand);
            ok &= Expect(!Diverts(fix, WorkJob(fix, tend)),
                         "latched again, the room's work does not dress them");
            if (fix.Pawn.drafter != null)
            {
                fix.Pawn.drafter.Drafted = true;
                fix.Pawn.drafter.Drafted = false;
                ok &= Expect(!Patch_JobInterception.ChangedBackAt.ContainsKey(fix.Pawn),
                             "drafting dropped the latch")
                    & Expect(Diverts(fix, WorkJob(fix, tend)),
                             "so the room's work dresses them again (control)");
            }

            // The catch-up honours it too, or a stand returning to the pool would re-dress the
            // colonist the player just pulled out.
            Patch_JobInterception.ChangedBackAt[fix.Pawn] = new Patch_JobInterception.ChangeBackLatch(fix.Stand);
            Job working = WorkingAt(tend, fix.Stand.Position + new IntVec3(3, 0, 2));
            fix.Pawn.jobs.StartJob(working, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);
            ok &= Expect(StillWorking(fix.Pawn, tend),
                         "latched, the colonist starts the work in their own clothes");
            Patch_JobInterception.Notify_StandFreed(fix.Comp, null);
            ok &= Expect(StillWorking(fix.Pawn, tend),
                         "and a freed stand does not interrupt them to dress");
            Patch_JobInterception.ChangedBackAt.Remove(fix.Pawn);
            Patch_JobInterception.Notify_StandFreed(fix.Comp, null);
            ok &= Expect(fix.Pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                         "but without the latch the same freed stand does (control)");
            Settle(fix.Pawn);
            return ok;
        }

        /// <summary>An automatic doctoring job at a cell, as the work arm sees one.</summary>
        internal static Job DoctoringAt(WorkGiverDef giver, IntVec3 at)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Wait, at);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>
        /// An allowed area holding every cell of the stand's room except the stand's own: the work
        /// is inside it and the stand is not. Better Pawn Control's alert areas, or any
        /// hand-painted area, can leave a stand out the same way.
        /// </summary>
        internal static Area_Allowed AreaWithoutTheStand(Fixture fix)
        {
            Area_Allowed area;
            if (!fix.Map.areaManager.TryMakeNewAllowed(out area))
            {
                return null;
            }
            foreach (IntVec3 cell in fix.Stand.GetRoom().Cells)
            {
                if (cell != fix.Stand.Position)
                {
                    area[cell] = true;
                }
            }
            return area;
        }

        /// <summary>
        /// A colonist whose stand is outside their allowed area keeps the outfit on, and the stand
        /// says why. This is the return trip: an area painted after dressing, or switched by Better
        /// Pawn Control's alerts, leaves a borrower whose stand they may no longer walk to. The
        /// uniform rides along, the retry cooldown is set, and the inspect pane names the cause.
        /// The Change back button is unaffected: it is an order, and orders ignore allowed areas in
        /// vanilla too.
        ///
        /// <para>The control is the same out-of-room job with the stand inside their area, asked
        /// before the area is painted and again once the stand is back in it: it changes them back
        /// as usual and the inspect line is absent.</para>
        /// </summary>
        internal static bool StandOutsideTheAreaKeepsTheOutfitOn(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
            if (doctor == null || tend == null || mealDef == null)
            {
                return Expect(false, "the doctor and meal defs resolve");
            }
            fix.Comp.ToggleWork(doctor);
            MakeCalm(fix.Map);

            Pawn pawn = fix.Pawn;
            Map map = fix.Map;
            IntVec3 outside = OpenGround(fix, fix.Stand.Position + new IntVec3(8, 0, 0));
            IntVec3 mealCell = OpenGround(fix, outside + new IntVec3(0, 0, 1));
            if (!outside.InBounds(map) || outside.GetRoom(map) == fix.Stand.GetRoom())
            {
                return Expect(false, "a cell outside the stand's room resolves for the test");
            }
            Thing meal = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), mealCell, map);
            string line = "ShiftChange.InspectOutsideArea".Translate(pawn.LabelShort);

            bool ok = Expect(RunSwap(fix), "dressed for the shift")
                    & Expect(fix.Comp.OnShift, "and is on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= Expect(Probe(fix, DoctoringAt(tend, outside)),
                         "an out-of-room job changes them back while the stand is in their area (control)")
                & Expect(!fix.Comp.CompInspectStringExtra().Contains(line),
                         "and the stand says nothing about areas (control)");

            Area_Allowed area = AreaWithoutTheStand(fix);
            if (area == null)
            {
                return Expect(false, "an allowed area could be made");
            }
            try
            {
                area[outside] = true;
                area[meal.Position] = true;
                pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;
                ok &= Expect(fix.Stand.IsForbidden(pawn),
                             "the area leaves the stand out, so it is forbidden to them (control)");

                int now = Find.TickManager.TicksGame;
                bool inserted = Patch_JobInterception.TryInsertSwap(DoctoringAt(tend, outside), null,
                                                                    pawn, pawn.jobs);
                int blocked;
                bool cooled = Patch_JobInterception.LastBlockedTick.TryGetValue(pawn.thingIDNumber, out blocked)
                              && blocked == now;
                ok &= Expect(!inserted, "a colonist whose stand is outside their allowed area keeps the outfit on")
                    & Expect(cooled, "and the retry cooldown is set, so the next job boundary does not try again")
                    & Expect(CompShiftStand.OnShiftStandFor(pawn) == fix.Comp, "they are still on shift");
                if (inserted)
                {
                    Report.Append("      a change-back was started anyway: still running ")
                          .Append(pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand)
                          .AppendLine();
                }
                string inspect = fix.Comp.CompInspectStringExtra() ?? "";
                ok &= Expect(inspect.Contains(line), "the stand's inspect pane says why: \"" + line + "\"");
                Settle(pawn);

                ok &= Expect(!Probe(fix, IngestJob(meal)),
                             "a meal break does not walk them out of their area to change either");

                area[fix.Stand.Position] = true;
                ok &= Expect(!fix.Stand.IsForbidden(pawn), "the stand is back inside their area (control)")
                    & Expect(Probe(fix, DoctoringAt(tend, outside)),
                             "so the next out-of-room job changes them back as usual")
                    & Expect(!fix.Comp.CompInspectStringExtra().Contains(line), "and the inspect line is gone");
            }
            finally
            {
                pawn.playerSettings.AreaRestrictionInPawnCurrentMap = null;
                area.Delete();
            }
            return ok;
        }
    }
}
