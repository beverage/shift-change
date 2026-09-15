using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Harness cases for the gates on interception: the promises the store
    /// description makes, meal breaks, the danger gate, and the catch-up pass
    /// that dresses a colonist already working bare.
    ///
    /// <para>Split out of <see cref="DebugTools_LifecycleHarness"/> on
    /// 2026-09-15. These are separate TYPES rather than partials on purpose: a
    /// decompiler merges partials back into one class, so partials would have
    /// left the shipped dll reading exactly as it did before. The registration
    /// list that decides case ORDER stays in
    /// <see cref="DebugTools_LifecycleHarness.Run"/> and must not be
    /// scattered.</para>
    /// </summary>
    internal static class HarnessGates
    {
        /// <summary>
        /// THE PROMISES, as a decision table.
        ///
        /// Everything above this is bug-shaped — a guard against something
        /// that once went wrong. This is the other kind: the mod's advertised
        /// behaviour, asserted against the rules the README and the store
        /// description actually print. Those are promises to players, and M4
        /// showed a promise can be wrong in all three descriptions at once
        /// with nothing to notice.
        ///
        /// <para>Driven through <c>TryInsertSwap</c>, the real decision
        /// function the interception prefix calls. Every negative is paired
        /// with a POSITIVE CONTROL — the same setup without the gate — because
        /// "did not divert" is worthless on its own: a stand that was never
        /// eligible would satisfy it just as well.</para>
        /// </summary>
        internal static bool PromisesHold(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            WorkGiverDef urgent = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendEmergency");
            if (doctor == null || tend == null)
            {
                return Expect(false, "the doctor work defs resolve");
            }
            // The pad's room has no role, so name the work explicitly rather
            // than relying on an inference this fixture cannot make.
            fix.Comp.ToggleWork(doctor);

            bool ok = Expect(fix.Comp.HandlesWork(doctor), "the stand serves doctoring")
                    & Expect(Diverts(fix, WorkJob(fix, tend)),
                             "an automatic doctoring job dresses (positive control)");

            Job forced = WorkJob(fix, tend);
            forced.playerForced = true;
            ok &= Expect(!Diverts(fix, forced), "a right-click order is never diverted");

            if (urgent != null)
            {
                ok &= Expect(urgent.emergency, "DoctorTendEmergency is still flagged emergency")
                    & Expect(!Diverts(fix, WorkJob(fix, urgent)),
                             "an emergency is never delayed by a wardrobe trip");
            }

            if (fix.Pawn.drafter != null)
            {
                fix.Pawn.drafter.Drafted = true;
                ok &= Expect(!Diverts(fix, WorkJob(fix, tend)), "a drafted pawn is never diverted");
                fix.Pawn.drafter.Drafted = false;
                ok &= Expect(Diverts(fix, WorkJob(fix, tend)),
                             "and is diverted again once undrafted (control)");
            }

            // "Only doing the room's work does." A job of a work type this
            // stand does not serve must not dress anyone.
            WorkGiverDef cook = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsCook");
            if (cook != null)
            {
                ok &= Expect(!Diverts(fix, WorkJob(fix, cook)),
                             "work the stand does not serve is not diverted");
            }
            return ok;
        }

        /// <summary>
        /// The meal-break promise — and the one the copy got WRONG.
        ///
        /// All three descriptions used to say "eating in a room changes
        /// nothing". The code has always said the opposite, deliberately
        /// (decided 2026-08-08): a meal is a sit-down break, so the uniform
        /// comes off first wherever the food is stored, because otherwise a
        /// cook carries a meal across the base in whites to reach a chair —
        /// the exact walk this mod exists to prevent. The single exception is
        /// food already in hand or pack, which is just eaten.
        ///
        /// Nothing caught that for months. This is what would have.
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
        /// The danger gate is ONE-DIRECTIONAL. Under threat a pawn may not
        /// change INTO a uniform, but a pawn already wearing one still changes
        /// back.
        ///
        /// The regression this guards is the shipped one (2026-08-31, found in
        /// play): the gate sat above BOTH arms, so a raid froze every borrower
        /// in costume instead of pausing them, and since nothing fires on the
        /// way back to <c>None</c>, anyone whose next job was a long one wore
        /// it well past the all-clear. Four colonists spent a raid in evening
        /// dress with their flak vests parked in a full-change stand.
        ///
        /// Both halves are asserted against the SAME threat, because the bug
        /// was not either arm in isolation — it was that one gate answered for
        /// both. A test that only proved dressing is blocked would have passed
        /// on the broken build.
        ///
        /// The return leg is triggered by a meal break rather than by geometry:
        /// for a work stand an ingest job whose food is not already on the pawn
        /// diverts wherever the food sits (the sit-down-break rule), so the
        /// case needs no second room and cannot drift when the pad moves.
        ///
        /// <para><b>No hostile is on the map while the colonist is ticked.</b>
        /// The threat never acts on its own — the harness ticks fixture pawns
        /// one at a time and never the map — but <see cref="RunSwap"/> ticks
        /// the COLONIST, and a colonist reacts to a hostile three tiles away.
        /// The first version staged one threat for the whole case and the swap
        /// died with <c>InterruptForced</c> at toil 1 while the pawn stood on
        /// the interaction cell: the think tree had overridden the job
        /// mid-run. Intermittently, which is worse than never — it passed
        /// once and failed the release preflight. So the threat is staged
        /// twice, around the driver run, and cleared before anything ticks.
        /// That also models the report more honestly: the colonists were
        /// dressed BEFORE the raid landed.</para>
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

            ForceDangerRecheck(fix.Map);
            bool ok = Expect(fix.Map.dangerWatcher.DangerRating == StoryDanger.None,
                             "the map starts calm (control)")
                    & Expect(Diverts(fix, WorkJob(fix, tend)),
                             "and an automatic work job dresses while calm (control)");

            // The half that stays gated. Diverts() never ticks the pawn, so a
            // hostile standing in the room cannot disturb it.
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

            // Dressed through the DRIVER rather than the trigger, since the
            // trigger is what the assertion above just proved is shut — and on
            // a map with no hostile on it, because this is the only part of
            // the case that ticks the colonist.
            ok &= Expect(RunSwap(fix), "dressed for the shift on a calm map")
                & Expect(fix.Comp.OnShift, "and is on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }

            // The half the one-directional gate opened.
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
        /// M3'S REGRESSION GUARD: a freed stand catches up a colonist already
        /// working bare in its room.
        ///
        /// The announcement used to fire from a TOIL finish action, while the
        /// departing pawn still held the stand's <c>maxPawns = 1</c>
        /// reservation — so every candidate died on
        /// <c>CanReserveAndReach</c> and the interrupt simply never happened.
        /// It moved to a global finish action, which the tracker runs after
        /// <c>CleanupCurrentJob</c> releases reservations
        /// (<c>:492</c> then <c>:497</c>).
        ///
        /// Binary, with no timing subtlety: under the regression the second
        /// colonist is never interrupted at all.
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

            // Working in the room, in their own clothes, on a job the catch-up
            // is allowed to interrupt.
            //
            // Goto rather than Wait, because Core's Wait is suspendable=false
            // and the filter rejects it outright. Targeted at ANOTHER cell,
            // not this pawn's own: StartJob runs ReadyForNextToil
            // synchronously, so a Goto to where the pawn already stands
            // arrives and completes inside StartJob, and the think tree hands
            // them a Wait_Wander — suspendable=false, filtered out, and the
            // case then fails for a reason that has nothing to do with M3.
            // Aimed elsewhere the job stays open: the path request is never
            // served, because nothing ticks this pawn.
            Job working = JobMaker.MakeJob(JobDefOf.Goto,
                                           fix.Stand.Position + new IntVec3(3, 0, 2));
            working.workGiverDef = tend;
            bare.jobs.StartJob(working, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);

            ok &= Expect(SwapPlan.WouldDress(bare, fix.Stand),
                         "the second colonist could wear what the stand holds")
                & Expect(bare.CurJobDef == JobDefOf.Goto,
                         "and is still on the interruptible job we gave them (control)");

            ok &= Expect(RunSwap(fix), "the first colonist changed back, freeing the stand");
            return ok & Expect(bare.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                               "the freed stand interrupted them to dress");
        }
    }
}
