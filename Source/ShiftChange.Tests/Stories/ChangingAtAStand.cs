using System.Collections;
using System.Collections.Generic;
using DevTools.Testing;
using RimWorld;
using Verse;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// A colonist changing at a stand and back, on the game's own clock (<see cref="GameClock"/>),
    /// through the real driver on the colonist's own job tracker.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.ChangingAtAStand)]
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

            IEnumerator Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                Apparel parka = Worn(scene.Colonist, "Apparel_Parka");
                if (!story.Check(parka != null, name + " wears a parka the duster displaces"))
                {
                    yield break;
                }
                // The player's choice, which a change must not downgrade to policy-managed.
                scene.Colonist.outfits.forcedHandler.SetForced(parka, forced: true);
                story.Note(name + "'s parka is forced, as a player sets it in the gear tab");

                yield return Swap(scene, story, name + " changes into the duster");
                story.Check(scene.Comp.Borrower == scene.Colonist, "the stand lends its duster to " + name);
                story.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == scene.Comp,
                            name + " is on shift at this stand");
                story.Check(parka.ParentHolder == scene.OutfitStand, "the parka hangs on the stand");
                story.Check(scene.Comp.StoredOwnerApparelForReading.Contains(parka),
                            "which keeps it for " + name);
                story.Check(scene.Comp.WasForcedWhenStored(parka), "and remembers it was forced");

                yield return Swap(scene, story, name + " changes back");
                story.Check(scene.Colonist.apparel.WornApparel.Contains(parka), "the parka is back on");
                story.Check(scene.Colonist.outfits.forcedHandler.IsForced(parka),
                            "still forced, as the player left it");
                story.Check(!scene.Comp.OnShift, "the stand is free again");
                story.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == null, name + " is off shift");
            }
        }

        [Test]
        [TestDescription("a colonist walks over to a stand to change, and walks back to it to change back")]
        internal IEnumerator WalksToTheStand()
        {
            return Staged(Body);

            IEnumerator Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                Apparel parka = Worn(scene.Colonist, "Apparel_Parka");
                IntVec3 across = new IntVec3(Pad.maxX - 1, 0, Pad.maxZ - 1);
                if (!story.Check(parka != null && across.Standable(Map),
                                 name + " wears a parka, and there is room to stand across the pad"))
                {
                    yield break;
                }

                MoveTo(scene.Colonist, across);
                story.Note(name + " stands " + Distance(scene, across) + " cells from the stand");
                yield return Swap(scene, story, name + " walks over and changes into the duster");
                story.Check(scene.Colonist.Position == scene.OutfitStand.InteractionCell, name + " is at the stand");
                story.Check(parka.ParentHolder == scene.OutfitStand, "the parka hangs on the stand");

                MoveTo(scene.Colonist, across);
                yield return Swap(scene, story, name + " walks back and changes out of it");
                story.Check(scene.Colonist.apparel.WornApparel.Contains(parka), "the parka is back on");
                story.Check(scene.OutfitStand.HeldItems.Count == 1
                            && scene.OutfitStand.HeldItems[0].def.defName == "Apparel_Duster",
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

            IEnumerator Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                yield return Swap(scene, story, name + " changes into the duster");
                Apparel duster = scene.Comp.IssuedUniformForReading.Count > 0
                    ? scene.Comp.IssuedUniformForReading[0]
                    : null;
                story.Check(duster != null, "the stand lends the duster to " + name);
                story.Check(scene.Comp.StoredOwnerApparelForReading.Count == 0,
                            "and keeps none of " + name + "'s clothes: the duster covers neither "
                            + "shirt nor trousers");
                if (duster == null)
                {
                    yield break;
                }

                yield return Swap(scene, story, name + " changes back");
                story.Check(!scene.Colonist.apparel.WornApparel.Contains(duster), "the duster came off");
                story.Check(duster.ParentHolder == scene.OutfitStand, "and is back on the stand");
                story.Check(scene.Colonist.apparel.WornApparel.Count == 2,
                            name + " still has their own shirt and trousers on");
                story.Check(!scene.Comp.OnShift, "the stand is free again");
                story.Check(SwapPlan.WouldDress(scene.Colonist, scene.OutfitStand), "and would dress someone again");
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

            IEnumerator Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                List<Apparel> before = new List<Apparel>(scene.Colonist.apparel.WornApparel);
                if (!story.Check(before.Count >= 3, name + " wears a shirt, trousers and a parka"))
                {
                    yield break;
                }
                scene.Comp.SetFullChange(true);
                story.Note("Change the whole outfit is ticked on the stand");

                yield return Swap(scene, story, name + " changes into the duster");
                story.Check(scene.Comp.OnShift, name + " is on shift at this stand");
                List<Apparel> worn = scene.Colonist.apparel.WornApparel;
                story.Check(before.TrueForAll(a => !worn.Contains(a)),
                            "every garment " + name + " arrived in came off");
                story.Check(scene.Comp.StoredOwnerApparelForReading.Count == before.Count,
                            "and the stand keeps all " + before.Count + " for " + name);
                story.Check(worn.Count > 0, name + " is wearing the duster, not nothing");

                // The way back follows what the stand keeps, not the setting.
                scene.Comp.SetFullChange(false);
                story.Note("Change the whole outfit is unticked again");
                yield return Swap(scene, story, name + " changes back");
                story.Check(before.TrueForAll(scene.Colonist.apparel.WornApparel.Contains),
                            "all " + before.Count + " came back on");
                story.Check(!scene.Comp.OnShift, "the stand is free again");

                scene.Comp.SetFullChange(true);
                ClearStand(scene.OutfitStand);
                story.Check(!SwapPlan.WouldDress(scene.Colonist, scene.OutfitStand),
                            "emptied, with the setting ticked, the stand dresses nobody");
            }
        }
    }
}
