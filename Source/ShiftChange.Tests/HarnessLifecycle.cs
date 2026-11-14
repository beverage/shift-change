using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Harness cases for the engine lifecycle events a ledger has to survive mid-shift: gravship
    /// flight, with the stand and its borrower together or apart, and a reinstall. Also holds the
    /// flight and meal-break
    /// helpers <see cref="HarnessVacuum"/> reuses. Cases are listed, and ordered, in
    /// HarnessCases.cs.
    /// </summary>
    internal static class HarnessLifecycle
    {
        /// <summary>
        /// A flight carrying the stand and its borrower together. The stand, the parked civvies
        /// and the borrower all survive a takeoff's <c>WillReplace</c> despawn, so the ledger must
        /// survive too, or the pawn lands in a uniform with no way out (<c>Building_OutfitStand</c>
        /// keeps its contents in that mode, see <see cref="CompShiftStand.PostDeSpawn"/>). A
        /// gravship in flight is a world object with no gear tab, so nothing happens to the pawn's
        /// clothes meanwhile: after landing the next meal break sends them to the stand, and the
        /// return leg ends the shift normally.
        /// </summary>
        internal static bool GravshipFlight(Fixture fix)
        {
            IntVec3 standCell = fix.Stand.Position;
            IntVec3 pawnCell = fix.Pawn.Position;
            Apparel uniform = FirstIssued(fix);
            List<Apparel> parked = new List<Apparel>(fix.Comp.StoredOwnerApparelForReading);
            if (uniform == null || parked.Count == 0)
            {
                return Expect(false, "the fixture is mid-shift with clothes parked");
            }

            fix.Stand.DeSpawn(DestroyMode.WillReplace);
            fix.Pawn.DeSpawn(DestroyMode.WillReplace);
            GenSpawn.Spawn(fix.Stand, standCell, fix.Map, Rot4.North);
            GenSpawn.Spawn(fix.Pawn, pawnCell, fix.Map, Rot4.North);
            fix.Stand.PostSwapMap();
            fix.Pawn.PostSwapMap();

            bool ok = Expect(fix.Comp.OnShift, "stand still reads on-shift")
                    & Expect(fix.Comp.Borrower == fix.Pawn, "borrower survived the flight")
                    & Expect(fix.Comp.StoredOwnerApparelForReading.Count == fix.StoredCount,
                             "parked civvies still in the ledger")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                             "registry points back at the stand (Change back works)")
                    & Expect(MealBreakSendsThemBack(fix),
                             "and the next meal break sends them to the stand on its own");
            return ok & ShiftEndsNormally(fix, uniform, parked);
        }

        /// <summary>
        /// The stand flies and the borrower stays, spawned, on a map the takeoff keeps because it
        /// has a grav anchor (<c>WorldComponent_GravshipController.TakeoffEnded</c>). The shift
        /// stays suspended while they are apart, uniform forced and wardrobe optimizer paused,
        /// and resumes once they share a map (docs/DESIGN.md, "Ownership and the ledger"). The
        /// pause keys on the registry entry, which is what is asserted; the optimizer is not run.
        /// </summary>
        internal static bool StandFliesWithoutItsBorrower(Fixture fix)
        {
            Apparel uniform = FirstIssued(fix);
            List<Apparel> parked = new List<Apparel>(fix.Comp.StoredOwnerApparelForReading);
            if (uniform == null || parked.Count == 0)
            {
                return Expect(false, "the fixture is mid-shift with clothes parked");
            }
            Map home = fix.Map;
            IntVec3 standCell = fix.Stand.Position;
            Map away = SecondMap(home);
            if (away == null)
            {
                return Expect(false, "a second map was made");
            }
            fix.Pocket = away;

            Fly(fix.Stand, away, away.Center);
            bool ok = Expect(fix.Stand.Map == away, "the stand landed on the second map")
                    & Expect(fix.Pawn.Spawned && fix.Pawn.Map == home,
                             "and the borrower is still spawned on the first, which the takeoff kept")
                    & Expect(fix.Comp.OnShift && fix.Comp.Borrower == fix.Pawn,
                             "the shift is kept while they are apart")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                             "and the registry still sends the borrower to the stand")
                    & Expect(StillForced(fix, uniform), "the uniform stays forced while they are apart")
                    & Expect(!MealBreakSendsThemBack(fix),
                             "and a meal break does not send them after a stand on another map");
            for (int i = 0; i < parked.Count; i++)
            {
                ok &= Expect(fix.Stand.HeldItems.Contains(parked[i]),
                             parked[i].def.defName + " flew with the stand");
            }

            Fly(fix.Stand, home, standCell);
            ok &= Expect(fix.Stand.Map == home, "the stand flew back")
                & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                         "and the registry still sends the borrower to it")
                & Expect(MealBreakSendsThemBack(fix),
                         "and the next meal break sends them to the stand on its own");
            return ok & ShiftEndsNormally(fix, uniform, parked);
        }

        /// <summary>
        /// The mirror of <see cref="StandFliesWithoutItsBorrower"/>: the borrower flies and the
        /// stand stays on the anchored map. The shift is kept while they are apart, the uniform
        /// still forced and no meal break sending them after a stand on another map, and it
        /// resumes once the borrower flies back. The stand is never told, since it does not move
        /// and nothing of ours hears a pawn's <c>PostSwapMap</c>.
        /// </summary>
        internal static bool BorrowerFliesWithoutTheirStand(Fixture fix)
        {
            Apparel uniform = FirstIssued(fix);
            List<Apparel> parked = new List<Apparel>(fix.Comp.StoredOwnerApparelForReading);
            if (uniform == null || parked.Count == 0)
            {
                return Expect(false, "the fixture is mid-shift with clothes parked");
            }
            Map home = fix.Map;
            Map away = SecondMap(home);
            if (away == null)
            {
                return Expect(false, "a second map was made");
            }
            fix.Pocket = away;

            Fly(fix.Pawn, away, away.Center);
            bool ok = Expect(fix.Pawn.Map == away, "the borrower landed on the second map")
                    & Expect(fix.Stand.Spawned && fix.Stand.Map == home,
                             "and the stand is still on the first, which the takeoff kept")
                    & Expect(fix.Comp.OnShift && fix.Comp.Borrower == fix.Pawn,
                             "the shift is kept while they are apart")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                             "and the registry still sends the borrower to the stand")
                    & Expect(fix.Pawn.apparel.WornApparel.Contains(uniform),
                             "and the borrower still wears the uniform")
                    & Expect(StillForced(fix, uniform), "forced, while they are apart")
                    & Expect(!MealBreakSendsThemBack(fix),
                             "and a meal break does not send them after a stand on another map");

            Fly(fix.Pawn, home, fix.Stand.InteractionCell);
            ok &= Expect(fix.Pawn.Map == home, "the borrower flew back")
                & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                         "and the registry still sends them to the stand")
                & Expect(MealBreakSendsThemBack(fix),
                         "and the next meal break sends them to the stand on its own");
            return ok & ShiftEndsNormally(fix, uniform, parked);
        }

        /// <summary>
        /// One thing's gravship flight, in the engine's order: <c>PreSwapMap</c> and a
        /// <c>WillReplace</c> despawn at takeoff (<c>GravshipUtility.GenerateGravship</c>), a
        /// spawn on the landing map, then <c>PostSwapMap</c>
        /// (<c>GravshipPlacementUtility.PostSwapMap</c>). For a flight carrying one of the pair;
        /// <see cref="GravshipFlight"/> interleaves the two as the engine does with both aboard,
        /// without the <c>PreSwapMap</c> step, which nothing of ours overrides.
        /// </summary>
        internal static void Fly(Thing thing, Map to, IntVec3 cell)
        {
            Rot4 rot = thing.Rotation;
            thing.PreSwapMap();
            thing.DeSpawn(DestroyMode.WillReplace);
            GenSpawn.Spawn(thing, cell, to, rot);
            thing.PostSwapMap();
        }

        /// <summary>
        /// The return leg, run for real at the stand: the uniform must end up back on the stand
        /// and every parked garment back on the borrower.
        /// </summary>
        internal static bool ShiftEndsNormally(Fixture fix, Apparel uniform, List<Apparel> parked)
        {
            // On the interaction cell, so the return leg starts without a walk, as every staged
            // swap does (HarnessFixtures.Stage).
            fix.Pawn.Position = fix.Stand.InteractionCell;
            if (!RunSwap(fix))
            {
                return Expect(false, "the return leg ran to completion");
            }
            bool ok = Expect(!fix.Comp.OnShift, "the return leg ended the shift");
            for (int i = 0; i < parked.Count; i++)
            {
                ok &= Expect(fix.Pawn.apparel.WornApparel.Contains(parked[i]),
                             parked[i].def.defName + " is back on the pawn");
            }
            return ok & Expect(fix.Stand.HeldItems.Contains(uniform),
                               "and the uniform is back on the stand");
        }

        /// <summary>
        /// Whether a meal break would send the borrower to the stand right now: the automatic
        /// return trip, asked of the real interception and left with no trace
        /// (<see cref="Probe"/>). A meal on the ground is a sit-down break, which changes the pawn
        /// out first wherever they eat (<c>MealBreakChangesOut</c>), so this asks of a flight only
        /// whether the stand is within reach. The probe clears the retry cooldown a refusal
        /// stamps, which in play can delay the first change back after a reunion by up to
        /// <see cref="Patch_JobInterception.RetryCooldownTicks"/>.
        /// </summary>
        internal static bool MealBreakSendsThemBack(Fixture fix)
        {
            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
            if (mealDef == null || !fix.Pawn.Spawned)
            {
                return false;
            }
            Thing meal = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), fix.Pawn.Position, fix.Pawn.Map);
            bool sent = Probe(fix, IngestJob(meal));
            if (!meal.Destroyed)
            {
                meal.Destroy();
            }
            return sent;
        }

        /// <summary>Whether the borrower's uniform still carries the forced flag.</summary>
        internal static bool StillForced(Fixture fix, Apparel uniform)
        {
            return fix.Pawn.outfits != null && fix.Pawn.outfits.forcedHandler.IsForced(uniform);
        }

        /// <summary>The first garment the fixture issued, or null.</summary>
        internal static Apparel FirstIssued(Fixture fix)
        {
            return fix.Comp.IssuedUniformForReading.Count > 0
                ? fix.Comp.IssuedUniformForReading[0]
                : null;
        }

        /// <summary>
        /// Moving a stand while somebody has its uniform out: they must still be able to hand it
        /// back afterwards. Guards a past bug where a move released the ledger and left the pawn
        /// stuck in the uniform.
        ///
        /// <para>A minify is a <c>Vanish</c> despawn and <c>Building_OutfitStand</c> keeps its
        /// container through one, so the borrower's civvies travel inside the box. The ledger has
        /// to travel with them, or the stand lands reading those civvies as its own kit; dropped
        /// on the floor instead, they leave the pawn just as stuck in the uniform. The forced
        /// flag is the exception: it must not survive the boxed window, so the case asserts it
        /// off while boxed and on again on landing.</para>
        /// </summary>
        internal static bool ReinstallKeepsTheLedger(Fixture fix)
        {
            Apparel uniform = fix.Comp.IssuedUniformForReading.Count > 0
                ? fix.Comp.IssuedUniformForReading[0]
                : null;
            List<Apparel> parked = new List<Apparel>(fix.Comp.StoredOwnerApparelForReading);
            if (uniform == null || parked.Count == 0)
            {
                return Expect(false, "the fixture is mid-shift with clothes parked");
            }

            Map map = fix.Map;
            IntVec3 to = new IntVec3(fix.Stand.Position.x, 0, fix.Stand.Position.z + 2);
            Rot4 rot = fix.Stand.Rotation;

            MinifiedThing box = fix.Stand.MakeMinified();
            if (box == null)
            {
                return Expect(false, "the stand minified");
            }
            bool ok = Expect(fix.Comp.OnShift, "the ledger rode into the box")
                    & Expect(fix.Comp.Borrower == fix.Pawn, "and still names the borrower");
            for (int i = 0; i < parked.Count; i++)
            {
                ok &= Expect(fix.Stand.HeldItems.Contains(parked[i]),
                             parked[i].def.defName + " travelled with the stand");
            }
            if (fix.Pawn.outfits != null)
            {
                ok &= Expect(!fix.Pawn.outfits.forcedHandler.IsForced(uniform),
                             "the uniform is NOT pinned while the stand is boxed");
            }

            box.InnerThing = null;
            box.Destroy();
            GenSpawn.Spawn(fix.Stand, to, map, rot);

            ok &= Expect(fix.Stand.Spawned, "the stand is back on the map")
                & Expect(fix.Comp.OnShift, "the ledger survived the move")
                & Expect(fix.Comp.Borrower == fix.Pawn, "the borrower survived it")
                & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                         "the registry points back at the stand — Change back works")
                & Expect(fix.Comp.StoredOwnerApparelForReading.Count == parked.Count,
                         "every parked garment is still in the ledger");
            if (fix.Pawn.outfits != null)
            {
                ok &= Expect(fix.Pawn.outfits.forcedHandler.IsForced(uniform),
                             "and the uniform is force-worn again on landing");
            }

            // The return trip actually runs and the pawn gets their own clothes back: the
            // behaviour, where everything above is state. The pawn stands on the interaction cell,
            // so the leg starts without a walk, as every staged swap does (HarnessFixtures.Stage).
            fix.Pawn.Position = fix.Stand.InteractionCell;
            if (!RunSwap(fix))
            {
                return ok & Expect(false, "the return leg ran to completion");
            }
            ok &= Expect(!fix.Comp.OnShift, "the uniform went back into the stand");
            for (int i = 0; i < parked.Count; i++)
            {
                ok &= Expect(fix.Pawn.apparel.WornApparel.Contains(parked[i]),
                             parked[i].def.defName + " is back on the pawn");
            }
            return ok & Expect(fix.Stand.HeldItems.Contains(uniform),
                               "and the uniform is back on the stand");
        }
    }
}
