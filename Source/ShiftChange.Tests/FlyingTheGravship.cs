using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DevTools.Testing;
using RimWorld;
using Verse;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;
using static ShiftChange.Tests.HarnessLifecycle;

namespace ShiftChange.Tests
{
    /// <summary>
    /// A gravship flight with a stand aboard, and flights that part a colonist from the stand whose
    /// outfit they wear: the shift is kept through each, and ends at the stand as usual.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.FlyingTheGravship)]
    internal class FlyingTheGravship : StoryFixture
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

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                IntVec3 standCell = fix.Stand.Position;
                IntVec3 pawnCell = fix.Pawn.Position;
                Apparel uniform = FirstIssued(fix);
                List<Apparel> parked = new List<Apparel>(fix.Comp.StoredOwnerApparelForReading);
                if (uniform == null || parked.Count == 0)
                {
                    story.Check(false, name + " is on shift in the stand's outfit, "
                                       + "their own clothes kept on the stand");
                    return;
                }

                // The engine's order with both aboard: both despawned at takeoff, both spawned on
                // landing, then each told. PreSwapMap is left out: nothing of Shift Change's
                // overrides it.
                fix.Stand.DeSpawn(DestroyMode.WillReplace);
                fix.Pawn.DeSpawn(DestroyMode.WillReplace);
                GenSpawn.Spawn(fix.Stand, standCell, fix.Map, Rot4.North);
                GenSpawn.Spawn(fix.Pawn, pawnCell, fix.Map, Rot4.North);
                fix.Stand.PostSwapMap();
                fix.Pawn.PostSwapMap();
                story.Note("a gravship takes off with the stand and " + name
                           + " aboard, and lands them where they stood");

                story.Check(fix.Comp.OnShift, "the stand's shift goes on");
                story.Check(fix.Comp.Borrower == fix.Pawn, "it still lends its outfit to " + name);
                story.Check(fix.Comp.StoredOwnerApparelForReading.Count == fix.StoredCount,
                            "and still keeps " + name + "'s own clothes for them");
                story.Check(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                            name + " is still on shift at it, so their Change back button "
                            + "leads there");
                story.Check(MealBreakSendsThemBack(fix),
                            "and a meal break would send " + name
                            + " to the stand to change back first");
                ShiftEndsNormally(story, fix, uniform, parked);
            }
        }

        // Flights that leave the colonist and the stand on two maps, the second a pocket map
        // (HarnessFixtures.SecondMap).

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

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                Apparel uniform = FirstIssued(fix);
                List<Apparel> parked = new List<Apparel>(fix.Comp.StoredOwnerApparelForReading);
                if (uniform == null || parked.Count == 0)
                {
                    story.Check(false, name + " is on shift in the stand's outfit, "
                                       + "their own clothes kept on the stand");
                    return;
                }
                Map home = fix.Map;
                IntVec3 standCell = fix.Stand.Position;
                Map away = SecondMap(home);
                if (away == null)
                {
                    story.Check(false, "a second map can be made for the gravship to land on, "
                                       + "a pocket of space from Odyssey");
                    return;
                }
                fix.Pocket = away;
                story.Note("a second map is made, a small pocket of space");

                Fly(fix.Stand, away, away.Center);
                story.Note("the stand flies off to it in a gravship, " + name + " staying behind");
                story.Check(fix.Stand.Map == away, "the stand lands on the second map");
                story.Check(fix.Pawn.Spawned && fix.Pawn.Map == home,
                            "and " + name + " is still on the first, which the takeoff kept");
                story.Check(fix.Comp.OnShift && fix.Comp.Borrower == fix.Pawn,
                            "the shift is kept while they are apart");
                story.Check(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                            "and " + name + " is still on shift at the stand, on another map");
                story.Check(StillForced(fix, uniform),
                            "the " + uniform.def.label + " stays forced on " + name
                            + " while they are apart");
                story.Check(!MealBreakSendsThemBack(fix),
                            "and a meal break would not send " + name
                            + " after a stand on another map");
                for (int i = 0; i < parked.Count; i++)
                {
                    story.Check(fix.Stand.HeldItems.Contains(parked[i]),
                                name + "'s " + parked[i].def.label + " flew with the stand");
                }

                Fly(fix.Stand, home, standCell);
                story.Note("the stand flies back to where it stood");
                story.Check(fix.Stand.Map == home, "the stand is back on " + name + "'s map");
                story.Check(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                            "and " + name + " is still on shift at it");
                story.Check(MealBreakSendsThemBack(fix),
                            "and a meal break would send " + name
                            + " to the stand to change back first");
                ShiftEndsNormally(story, fix, uniform, parked);
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

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                Apparel uniform = FirstIssued(fix);
                List<Apparel> parked = new List<Apparel>(fix.Comp.StoredOwnerApparelForReading);
                if (uniform == null || parked.Count == 0)
                {
                    story.Check(false, name + " is on shift in the stand's outfit, "
                                       + "their own clothes kept on the stand");
                    return;
                }
                Map home = fix.Map;
                Map away = SecondMap(home);
                if (away == null)
                {
                    story.Check(false, "a second map can be made for the gravship to land on, "
                                       + "a pocket of space from Odyssey");
                    return;
                }
                fix.Pocket = away;
                story.Note("a second map is made, a small pocket of space");

                Fly(fix.Pawn, away, away.Center);
                story.Note(name + " flies off to it in a gravship, leaving the stand behind");
                story.Check(fix.Pawn.Map == away, name + " lands on the second map");
                story.Check(fix.Stand.Spawned && fix.Stand.Map == home,
                            "and the stand is still on the first, which the takeoff kept");
                story.Check(fix.Comp.OnShift && fix.Comp.Borrower == fix.Pawn,
                            "the shift is kept while they are apart");
                story.Check(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                            "and " + name + " is still on shift at the stand, on another map");
                story.Check(fix.Pawn.apparel.WornApparel.Contains(uniform),
                            name + " still wears the " + uniform.def.label);
                story.Check(StillForced(fix, uniform), "forced on them while they are apart");
                story.Check(!MealBreakSendsThemBack(fix),
                            "and a meal break would not send " + name
                            + " after a stand on another map");

                Fly(fix.Pawn, home, fix.Stand.InteractionCell);
                story.Note(name + " flies back, landing beside the stand");
                story.Check(fix.Pawn.Map == home, name + " is back on the stand's map");
                story.Check(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                            "and still on shift at it");
                story.Check(MealBreakSendsThemBack(fix),
                            "and a meal break would send " + name
                            + " to the stand to change back first");
                ShiftEndsNormally(story, fix, uniform, parked);
            }
        }

        /// <summary>
        /// Changing back at the stand, run for real on the game's clock: the outfit must end up
        /// back on the stand and every one of the colonist's own clothes back on them.
        /// </summary>
        internal static void ShiftEndsNormally(Story story, Fixture fix, Apparel uniform,
                                               List<Apparel> parked)
        {
            string name = fix.Pawn.LabelShort;
            // On the interaction cell, so the change starts without a walk, as every staged
            // change does (HarnessFixtures.Stage).
            fix.Pawn.Position = fix.Stand.InteractionCell;
            if (!SwapNow(story, fix))
            {
                story.Check(false, name + " changes back at the stand");
                return;
            }
            story.Check(!fix.Comp.OnShift,
                        name + " changes back at the stand, which ends the shift");
            for (int i = 0; i < parked.Count; i++)
            {
                story.Check(fix.Pawn.apparel.WornApparel.Contains(parked[i]),
                            name + " wears their own " + parked[i].def.label + " again");
            }
            story.Check(fix.Stand.HeldItems.Contains(uniform),
                        "and the " + uniform.def.label + " is back on the stand");
        }
    }
}
