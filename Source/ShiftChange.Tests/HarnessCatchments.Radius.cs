using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    internal static partial class HarnessCatchments
    {
        /// <summary>The cells in the game's own radial pattern, before a mod enlarges it.</summary>
        internal const int GameRadialCells = 20000;

        /// <summary>
        /// A radius replaces the room. Work outside the stand's room but inside its ring dresses;
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
            // A job the catch-up may interrupt, open across every swap below (WorkingAt).
            Job working = WorkingAt(tend, fix.Stand.Position + new IntVec3(3, 0, 2));
            bare.jobs.StartJob(working, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);
            ok &= Expect(StillWorking(bare, tend),
                         "the second colonist is working bare in the room (control)");

            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(10);
            ok &= Expect(fix.Comp.Covers(fix.Stand.Position + new IntVec3(3, 0, 2), fix.Map),
                         "the radius covers their work")
                & Expect(RunSwap(fix), "the first colonist changed back, freeing the stand")
                & Expect(StillWorking(bare, tend),
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
