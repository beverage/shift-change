using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DevTools.Testing;
using RimWorld;
using Verse;
using static ShiftChange.Tests.SceneKit;
using static ShiftChange.Tests.LifecycleKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// A gravship flight with a stand aboard, and flights that part a colonist from the stand whose
    /// outfit they wear: the shift is kept through each, and ends at the stand as usual.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.FlyingTheGravship)]
    internal class FlyingTheGravship : FeatureFixture
    {
        /// <summary>
        /// A flight carrying the stand and the colonist on shift at it together. The stand, the
        /// clothes kept on it and the colonist all survive a takeoff's <c>WillReplace</c> despawn,
        /// so the shift must survive too, or the colonist lands in an outfit with no way out
        /// (<c>Building_OutfitStand</c> keeps its contents in that mode, see
        /// <see cref="CompShiftStand.PostDeSpawn"/>). A gravship in flight is a world object with
        /// no gear tab, so nothing happens to the colonist's clothes meanwhile: after landing the
        /// next meal break sends them to the stand, and changing back there ends the shift as
        /// usual.
        /// </summary>
        [Test]
        [TestDescription("a colonist on shift flies in the same gravship as their stand, and after "
                         + "landing the shift goes on and ends at the stand as usual")]
        internal IEnumerator StandAndColonistFlyTogether()
        {
            return Staged(Build, LosingAStandOrAColonist.OnShift, Body);

            void Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                IntVec3 standCell = scene.OutfitStand.Position;
                IntVec3 pawnCell = scene.Colonist.Position;
                Apparel uniform = FirstIssued(scene);
                List<Apparel> parked = new List<Apparel>(scene.Comp.StoredOwnerApparelForReading);
                if (uniform == null || parked.Count == 0)
                {
                    scenario.Check(false, name + " is on shift in the stand's outfit, "
                                       + "their own clothes kept on the stand");
                    return;
                }

                // The engine's order with both aboard: both despawned at takeoff, both spawned on
                // landing, then each told. PreSwapMap is left out: nothing of Shift Change's
                // overrides it.
                scene.OutfitStand.DeSpawn(DestroyMode.WillReplace);
                scene.Colonist.DeSpawn(DestroyMode.WillReplace);
                GenSpawn.Spawn(scene.OutfitStand, standCell, scene.Map, Rot4.North);
                GenSpawn.Spawn(scene.Colonist, pawnCell, scene.Map, Rot4.North);
                scene.OutfitStand.PostSwapMap();
                scene.Colonist.PostSwapMap();
                scenario.Note("a gravship takes off with the stand and " + name
                           + " aboard, and lands them where they stood");

                scenario.Check(scene.Comp.OnShift, "the stand's shift goes on");
                scenario.Check(scene.Comp.Borrower == scene.Colonist, "it still lends its outfit to " + name);
                scenario.Check(scene.Comp.StoredOwnerApparelForReading.Count == scene.StoredCount,
                            "and still keeps " + name + "'s own clothes for them");
                scenario.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == scene.Comp,
                            name + " is still on shift at it, so their Change back button "
                            + "leads there");
                scenario.Check(MealBreakSendsThemBack(scene),
                            "and a meal break would send " + name
                            + " to the stand to change back first");
                ShiftEndsNormally(scenario, scene, uniform, parked);
            }
        }

        // Flights that leave the colonist and the stand on two maps, the second a pocket map
        // (SceneKit.SecondMap).

        /// <summary>
        /// The stand flies and the colonist stays, spawned, on a map the takeoff keeps because it
        /// has a grav anchor (<c>WorldComponent_GravshipController.TakeoffEnded</c>). The shift
        /// stays suspended while they are apart, the outfit forced and the wardrobe optimizer
        /// paused, and resumes once they share a map (docs/DESIGN.md, "Ownership and the
        /// ledger"). The pause keys on the colonist being on shift at the stand
        /// (<see cref="CompShiftStand.OnShiftStandFor"/>), which is what is checked; the optimizer
        /// is not run.
        /// </summary>
        [Test]
        [TestDescription("a stand that flies off without the colonist on shift at it keeps their "
                         + "shift, and ends it as usual once it flies back")]
        internal IEnumerator StandFliesWithoutItsColonist()
        {
            return Staged(Build, LosingAStandOrAColonist.OnShift, Body);

            void Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                Apparel uniform = FirstIssued(scene);
                List<Apparel> parked = new List<Apparel>(scene.Comp.StoredOwnerApparelForReading);
                if (uniform == null || parked.Count == 0)
                {
                    scenario.Check(false, name + " is on shift in the stand's outfit, "
                                       + "their own clothes kept on the stand");
                    return;
                }
                Map home = scene.Map;
                IntVec3 standCell = scene.OutfitStand.Position;
                Map away = SecondMap(home);
                if (away == null)
                {
                    scenario.Check(false, "a second map can be made for the gravship to land on, "
                                       + "a pocket of space from Odyssey");
                    return;
                }
                scene.SecondMap = away;
                scenario.Note("a second map is made, a small pocket of space");

                Fly(scene.OutfitStand, away, away.Center);
                scenario.Note("the stand flies off to it in a gravship, " + name + " staying behind");
                scenario.Check(scene.OutfitStand.Map == away, "the stand lands on the second map");
                scenario.Check(scene.Colonist.Spawned && scene.Colonist.Map == home,
                            "and " + name + " is still on the first, which the takeoff kept");
                scenario.Check(scene.Comp.OnShift && scene.Comp.Borrower == scene.Colonist,
                            "the shift is kept while they are apart");
                scenario.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == scene.Comp,
                            "and " + name + " is still on shift at the stand, on another map");
                scenario.Check(StillForced(scene, uniform),
                            "the " + uniform.def.label + " stays forced on " + name
                            + " while they are apart");
                scenario.Check(!MealBreakSendsThemBack(scene),
                            "and a meal break would not send " + name
                            + " after a stand on another map");
                for (int i = 0; i < parked.Count; i++)
                {
                    scenario.Check(scene.OutfitStand.HeldItems.Contains(parked[i]),
                                name + "'s " + parked[i].def.label + " flew with the stand");
                }

                Fly(scene.OutfitStand, home, standCell);
                scenario.Note("the stand flies back to where it stood");
                scenario.Check(scene.OutfitStand.Map == home, "the stand is back on " + name + "'s map");
                scenario.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == scene.Comp,
                            "and " + name + " is still on shift at it");
                scenario.Check(MealBreakSendsThemBack(scene),
                            "and a meal break would send " + name
                            + " to the stand to change back first");
                ShiftEndsNormally(scenario, scene, uniform, parked);
            }
        }

        /// <summary>
        /// The mirror of <see cref="StandFliesWithoutItsColonist"/>: the colonist flies and the
        /// stand stays on the anchored map. The shift is kept while they are apart, the outfit
        /// still forced and no meal break sending them after a stand on another map, and it
        /// resumes once the colonist flies back. The stand is never told, since it does not move
        /// and nothing of Shift Change's hears a pawn's <c>PostSwapMap</c>.
        /// </summary>
        [Test]
        [TestDescription("a colonist on shift who flies off without their stand keeps the shift "
                         + "and the outfit, and ends it as usual once back")]
        internal IEnumerator ColonistFliesWithoutTheirStand()
        {
            return Staged(Build, LosingAStandOrAColonist.OnShift, Body);

            void Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                Apparel uniform = FirstIssued(scene);
                List<Apparel> parked = new List<Apparel>(scene.Comp.StoredOwnerApparelForReading);
                if (uniform == null || parked.Count == 0)
                {
                    scenario.Check(false, name + " is on shift in the stand's outfit, "
                                       + "their own clothes kept on the stand");
                    return;
                }
                Map home = scene.Map;
                Map away = SecondMap(home);
                if (away == null)
                {
                    scenario.Check(false, "a second map can be made for the gravship to land on, "
                                       + "a pocket of space from Odyssey");
                    return;
                }
                scene.SecondMap = away;
                scenario.Note("a second map is made, a small pocket of space");

                Fly(scene.Colonist, away, away.Center);
                scenario.Note(name + " flies off to it in a gravship, leaving the stand behind");
                scenario.Check(scene.Colonist.Map == away, name + " lands on the second map");
                scenario.Check(scene.OutfitStand.Spawned && scene.OutfitStand.Map == home,
                            "and the stand is still on the first, which the takeoff kept");
                scenario.Check(scene.Comp.OnShift && scene.Comp.Borrower == scene.Colonist,
                            "the shift is kept while they are apart");
                scenario.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == scene.Comp,
                            "and " + name + " is still on shift at the stand, on another map");
                scenario.Check(scene.Colonist.apparel.WornApparel.Contains(uniform),
                            name + " still wears the " + uniform.def.label);
                scenario.Check(StillForced(scene, uniform), "forced on them while they are apart");
                scenario.Check(!MealBreakSendsThemBack(scene),
                            "and a meal break would not send " + name
                            + " after a stand on another map");

                Fly(scene.Colonist, home, scene.OutfitStand.InteractionCell);
                scenario.Note(name + " flies back, landing beside the stand");
                scenario.Check(scene.Colonist.Map == home, name + " is back on the stand's map");
                scenario.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == scene.Comp,
                            "and still on shift at it");
                scenario.Check(MealBreakSendsThemBack(scene),
                            "and a meal break would send " + name
                            + " to the stand to change back first");
                ShiftEndsNormally(scenario, scene, uniform, parked);
            }
        }

        /// <summary>
        /// Changing back at the stand, run for real on the game's clock: the outfit must end up
        /// back on the stand and every one of the colonist's own clothes back on them.
        /// </summary>
        internal static void ShiftEndsNormally(Scenario scenario, Scene scene, Apparel uniform,
                                               List<Apparel> parked)
        {
            string name = scene.Colonist.LabelShort;
            // On the interaction cell, so the change starts without a walk, as every staged
            // change does (SceneKit.Stage).
            scene.Colonist.Position = scene.OutfitStand.InteractionCell;
            if (!SwapNow(scenario, scene))
            {
                scenario.Check(false, name + " changes back at the stand");
                return;
            }
            scenario.Check(!scene.Comp.OnShift,
                        name + " changes back at the stand, which ends the shift");
            for (int i = 0; i < parked.Count; i++)
            {
                scenario.Check(scene.Colonist.apparel.WornApparel.Contains(parked[i]),
                            name + " wears their own " + parked[i].def.label + " again");
            }
            scenario.Check(scene.OutfitStand.HeldItems.Contains(uniform),
                        "and the " + uniform.def.label + " is back on the stand");
        }
    }
}
