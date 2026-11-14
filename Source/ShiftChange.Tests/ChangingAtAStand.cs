using System.Collections;
using System.Collections.Generic;
using DevTools.Testing;
using RimWorld;
using Verse;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// A colonist changing at a stand and back, on the game's own clock (<see cref="GameClock"/>),
    /// through the real driver on the colonist's own job tracker.
    ///
    /// <para>Each test replaced a harness case that ticked its colonist alone, after both ran side by side
    /// and agreed check for check, except <see cref="WalksToTheStand"/>, which had no such twin: a
    /// pawn ticked alone never gets a path.</para>
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.ChangingAtAStand)]
    internal class ChangingAtAStand : StoryFixture
    {
        /// <summary>
        /// The driver builds a correct ledger and gives everything back, forced flags included:
        /// <c>Pawn_ApparelTracker.Notify_ApparelRemoved</c> clears the forced flag on every
        /// removal, so the driver captures it before and restores it on the way back.
        /// </summary>
        [Test]
        [TestDescription("a colonist changes into a stand's duster and back, "
                         + "and their forced parka comes back still forced")]
        internal IEnumerator IntoUniformAndBack()
        {
            return Staged(Body);

            IEnumerator Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                Apparel parka = Worn(fix.Pawn, "Apparel_Parka");
                if (!story.Check(parka != null, name + " wears a parka the duster displaces"))
                {
                    yield break;
                }
                // The player's choice, which a change must not downgrade to policy-managed.
                fix.Pawn.outfits.forcedHandler.SetForced(parka, forced: true);
                story.Note(name + "'s parka is forced, as a player sets it in the gear tab");

                yield return Swap(fix, story, name + " changes into the duster");
                story.Check(fix.Comp.Borrower == fix.Pawn, "the stand lends its duster to " + name);
                story.Check(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                            name + " is on shift at this stand");
                story.Check(parka.ParentHolder == fix.Stand, "the parka hangs on the stand");
                story.Check(fix.Comp.StoredOwnerApparelForReading.Contains(parka),
                            "which keeps it for " + name);
                story.Check(fix.Comp.WasForcedWhenStored(parka), "and remembers it was forced");

                yield return Swap(fix, story, name + " changes back");
                story.Check(fix.Pawn.apparel.WornApparel.Contains(parka), "the parka is back on");
                story.Check(fix.Pawn.outfits.forcedHandler.IsForced(parka),
                            "still forced, as the player left it");
                story.Check(!fix.Comp.OnShift, "the stand is free again");
                story.Check(CompShiftStand.OnShiftStandFor(fix.Pawn) == null, name + " is off shift");
            }
        }

        [Test]
        [TestDescription("a colonist walks over to a stand to change, and walks back to it to change back")]
        internal IEnumerator WalksToTheStand()
        {
            return Staged(Body);

            IEnumerator Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                Apparel parka = Worn(fix.Pawn, "Apparel_Parka");
                IntVec3 across = new IntVec3(Pad.maxX - 1, 0, Pad.maxZ - 1);
                if (!story.Check(parka != null && across.Standable(Map),
                                 name + " wears a parka, and there is room to stand across the pad"))
                {
                    yield break;
                }

                MoveTo(fix.Pawn, across);
                story.Note(name + " stands " + Distance(fix, across) + " cells from the stand");
                yield return Swap(fix, story, name + " walks over and changes into the duster");
                story.Check(fix.Pawn.Position == fix.Stand.InteractionCell, name + " is at the stand");
                story.Check(parka.ParentHolder == fix.Stand, "the parka hangs on the stand");

                MoveTo(fix.Pawn, across);
                yield return Swap(fix, story, name + " walks back and changes out of it");
                story.Check(fix.Pawn.apparel.WornApparel.Contains(parka), "the parka is back on");
                story.Check(fix.Stand.HeldItems.Count == 1 && fix.Stand.HeldItems[0].def.defName == "Apparel_Duster",
                            "and the duster is back on the stand");
            }
        }

        /// <summary>
        /// A stand whose duster shares no layer with the colonist's shirt and trousers still takes
        /// it back. Guards a past bug: such a duster was given to the colonist for good, and the
        /// stand emptied itself, silently.
        ///
        /// <para>That the stand is free proves nothing alone: the driver's fallback for a change
        /// with nothing to wear frees it too. The duster coming off and back onto the stand, the
        /// colonist's own clothes and the stand dressing again are the checks that fail without
        /// the fix.</para>
        /// </summary>
        [Test]
        [TestDescription("a stand whose duster goes on over a colonist's own clothes, "
                         + "displacing nothing, still takes it back")]
        internal IEnumerator UniformOverOwnClothesGoesBack()
        {
            return Staged(Body, StageKit.NonDisplacing);

            IEnumerator Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                yield return Swap(fix, story, name + " changes into the duster");
                Apparel duster = fix.Comp.IssuedUniformForReading.Count > 0
                    ? fix.Comp.IssuedUniformForReading[0]
                    : null;
                story.Check(duster != null, "the stand lends the duster to " + name);
                story.Check(fix.Comp.StoredOwnerApparelForReading.Count == 0,
                            "and keeps none of " + name + "'s clothes: the duster covers neither "
                            + "shirt nor trousers");
                if (duster == null)
                {
                    yield break;
                }

                yield return Swap(fix, story, name + " changes back");
                story.Check(!fix.Pawn.apparel.WornApparel.Contains(duster), "the duster came off");
                story.Check(duster.ParentHolder == fix.Stand, "and is back on the stand");
                story.Check(fix.Pawn.apparel.WornApparel.Count == 2,
                            name + " still has their own shirt and trousers on");
                story.Check(!fix.Comp.OnShift, "the stand is free again");
                story.Check(SwapPlan.WouldDress(fix.Pawn, fix.Stand), "and would dress someone again");
            }
        }

        /// <summary>
        /// Change the whole outfit: the colonist ends up in the stand's duster alone, and gets
        /// every garment back. The colonist wears a shirt and trousers under a parka, and only the
        /// parka shares the duster's layer, so whatever happens to the shirt and trousers is the
        /// setting's doing. An empty stand must still dress nobody, which fails if the whole-outfit
        /// pass runs before the stand's check that it has something to hand out
        /// (<see cref="SwapPlan.BuildDress"/>).
        /// </summary>
        [Test]
        [TestDescription("with Change the whole outfit ticked, a colonist changes every garment "
                         + "at the stand and gets them all back")]
        internal IEnumerator WholeOutfitChangesAndComesBack()
        {
            return Staged(Body);

            IEnumerator Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                List<Apparel> before = new List<Apparel>(fix.Pawn.apparel.WornApparel);
                if (!story.Check(before.Count >= 3, name + " wears a shirt, trousers and a parka"))
                {
                    yield break;
                }
                fix.Comp.SetFullChange(true);
                story.Note("Change the whole outfit is ticked on the stand");

                yield return Swap(fix, story, name + " changes into the duster");
                story.Check(fix.Comp.OnShift, name + " is on shift at this stand");
                List<Apparel> worn = fix.Pawn.apparel.WornApparel;
                story.Check(before.TrueForAll(a => !worn.Contains(a)),
                            "every garment " + name + " arrived in came off");
                story.Check(fix.Comp.StoredOwnerApparelForReading.Count == before.Count,
                            "and the stand keeps all " + before.Count + " for " + name);
                story.Check(worn.Count > 0, name + " is wearing the duster, not nothing");

                // The way back follows what the stand keeps, not the setting.
                fix.Comp.SetFullChange(false);
                story.Note("Change the whole outfit is unticked again");
                yield return Swap(fix, story, name + " changes back");
                story.Check(before.TrueForAll(fix.Pawn.apparel.WornApparel.Contains),
                            "all " + before.Count + " came back on");
                story.Check(!fix.Comp.OnShift, "the stand is free again");

                fix.Comp.SetFullChange(true);
                ClearStand(fix.Stand);
                story.Check(!SwapPlan.WouldDress(fix.Pawn, fix.Stand),
                            "emptied, with the setting ticked, the stand dresses nobody");
            }
        }
    }
}
