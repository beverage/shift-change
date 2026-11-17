using System.Collections;
using System.Collections.Generic;
using DevTools.Testing;
using RimWorld;
using Verse;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Keep colonists decent, on the game's own clock: with Change the whole outfit ticked, a
    /// stand that would leave a colonist without basic clothing holds their innermost garments
    /// back instead (docs/DESIGN.md, "The dress path asks the same question and answers it
    /// differently"). The verdict comes from the game's own rule,
    /// <c>Pawn_ApparelTracker.PsychologicallyNude</c>, read off a real colonist. Shift Change's
    /// copy of the rule (<see cref="SwapPlan.WouldBeNude"/>) is checked beside it, so if the two
    /// disagree, ours is wrong.
    ///
    /// <para><see cref="GuardIsOffUntilTurnedOn"/> checks the shipped default, not the running
    /// game's setting, which a player may have turned on.</para>
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.KeepingThemDecent)]
    internal class KeepingThemDecent : FeatureFixture
    {
        /// <summary>
        /// A stand holding only a shield belt, which covers the waist and neither torso nor legs,
        /// with the whole outfit ticked. The parka, the outermost garment, is the one asked for by
        /// name, so a plan that keeps the first garments that happen to work fails.
        /// </summary>
        [Test]
        [TestDescription("with Keep colonists decent on, a stand holding only a belt keeps a "
                         + "colonist's shirt and trousers on and takes only the parka")]
        internal IEnumerator WholeOutfitKeepsThemDressed()
        {
            return Staged(Body);

            IEnumerator Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                List<Apparel> before = new List<Apparel>(scene.Colonist.apparel.WornApparel);
                Apparel parka = Worn(scene.Colonist, "Apparel_Parka");
                if (!SetDecent(scenario, true)
                    || !scenario.Check(before.Count >= 3 && parka != null,
                                    name + " wears a shirt, trousers and a parka")
                    || !StockOnly(scenario, scene, "Apparel_ShieldBelt", "a shield belt"))
                {
                    yield break;
                }
                scenario.Check(scene.OutfitStand.HeldItems.Count == 1,
                            "one garment, which covers neither torso nor legs");
                scenario.Check(!scene.Colonist.apparel.PsychologicallyNude,
                            name + " starts out dressed, as the game judges it");
                scenario.Check(SwapPlan.WouldBeNude(scene.Colonist, before),
                            "and taking off all three would leave " + name
                            + " naked by Shift Change's reckoning too");

                scene.Comp.SetFullChange(true);
                scenario.Note("Change the whole outfit is ticked on the stand");
                List<Apparel> wear = new List<Apparel>();
                List<Apparel> store = new List<Apparel>();
                bool planned = SwapPlan.BuildDress(scene.Colonist, scene.OutfitStand, wear, store);
                scenario.Check(planned && wear.Count == 1, "the stand plans to hand over the belt");
                scenario.Check(!SwapPlan.WouldBeNude(scene.Colonist, store, wear),
                            "and its plan leaves " + name + " dressed");
                scenario.Check(before.FindAll(a => a != parka && !store.Contains(a)).Count == 2,
                            "by keeping the shirt and trousers on");
                scenario.Check(store.Count == 1 && store[0] == parka,
                            "and taking only the parka, the outermost");

                yield return Swap(scene, scenario, name + " changes into the belt");
                scenario.Check(!scene.Colonist.apparel.PsychologicallyNude,
                            name + " is still dressed, as the game judges it");
                scenario.Check(scene.Comp.StoredOwnerApparelForReading.Count == 1,
                            "the stand keeps only the parka for " + name);
                scenario.Check(scene.Colonist.apparel.WornApparel.Count == 3,
                            name + " wears the belt over their own shirt and trousers");

                scene.Comp.SetFullChange(false);
                scenario.Note("Change the whole outfit is unticked again");
                yield return Swap(scene, scenario, name + " changes back");
                scenario.Check(before.TrueForAll(scene.Colonist.apparel.WornApparel.Contains),
                            "every garment came back on");
                scenario.Check(!scene.Colonist.apparel.PsychologicallyNude, "and " + name + " is dressed as before");
            }
        }

        /// <summary>
        /// The control for <see cref="LegsOnlyStandKeepsAWomansShirtOn"/>: the same stand,
        /// setting and plan, with only the colonist differing. A pair that fails for everyone says
        /// the feature is broken, not that the game's rule differs for men and women. The gender
        /// check catches <see cref="SceneKit.Stage"/> ceasing to apply it, which would make
        /// the pair one test twice.
        /// </summary>
        [Test]
        [TestDescription("with Keep colonists decent on, a stand holding only trousers "
                         + "dresses a man in the trousers alone")]
        internal IEnumerator LegsOnlyStandLeavesAManDressed()
        {
            return Staged(Body, gender: Gender.Male);

            IEnumerator Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                if (!SetDecent(scenario, true) || !StockOnly(scenario, scene, "Apparel_Pants", "trousers"))
                {
                    yield break;
                }
                scenario.Check(scene.Colonist.gender == Gender.Male, name + " is a man; the pair of tests turns on this");
                scenario.Check(!scene.Colonist.apparel.PsychologicallyNude, "who starts out dressed");

                scene.Comp.SetFullChange(true);
                scenario.Note("Change the whole outfit is ticked on the stand");
                yield return Swap(scene, scenario, name + " changes into the trousers");
                scenario.Check(scene.Colonist.apparel.WornApparel.Count == 1,
                            "he wears the stand's trousers and nothing else");
                scenario.Check(!scene.Colonist.apparel.PsychologicallyNude,
                            "and the game calls that dressed, so the change was fair");
            }
        }

        /// <summary>
        /// The same stand and setting with a woman: the game's rule calls her nude in trousers
        /// alone, since it wants her torso covered as well, so the plan keeps her own shirt on,
        /// and she stays dressed through the way back. A test of men alone would pass a plan that
        /// strips every woman at this stand.
        /// </summary>
        [Test]
        [TestDescription("and the same stand keeps a woman's own shirt on, "
                         + "since the game's rule wants her torso covered")]
        internal IEnumerator LegsOnlyStandKeepsAWomansShirtOn()
        {
            return Staged(Body, gender: Gender.Female);

            IEnumerator Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                if (!SetDecent(scenario, true) || !StockOnly(scenario, scene, "Apparel_Pants", "trousers"))
                {
                    yield break;
                }
                scenario.Check(scene.Colonist.gender == Gender.Female,
                            name + " is a woman; the pair of tests turns on this");
                scenario.Check(!scene.Colonist.apparel.PsychologicallyNude, "who starts out dressed");

                scene.Comp.SetFullChange(true);
                scenario.Note("Change the whole outfit is ticked on the stand");
                yield return Swap(scene, scenario, name + " changes into the trousers");
                scenario.Check(scene.Colonist.apparel.WornApparel.Count == 2,
                            "she wears the stand's trousers and her own shirt, "
                            + "where the man needed only the trousers");
                scenario.Check(!scene.Colonist.apparel.PsychologicallyNude, "and is left dressed, as he was");

                scene.Comp.SetFullChange(false);
                scenario.Note("Change the whole outfit is unticked again");
                yield return Swap(scene, scenario, name + " changes back");
                scenario.Check(!scene.Colonist.apparel.PsychologicallyNude, "in her own clothes again");
            }
        }

        /// <summary>
        /// A nudist at the belt stand is left in the belt, and that is right: for a nudist,
        /// clothes are a mood penalty, so holding garments back would fight them every shift. Shift
        /// Change's rule must still call the result nude; what differs is the answer to it, so an
        /// exemption reached because the rule stopped seeing nudity fails here.
        /// </summary>
        [Test]
        [TestDescription("a nudist is left in just the belt, because that is what they want")]
        internal IEnumerator NudistIsLeftUndressed()
        {
            return Staged(Body);

            IEnumerator Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                if (!SetDecent(scenario, true)
                    || !scenario.Check(scene.Colonist.story?.traits != null, name + " can have traits"))
                {
                    yield break;
                }
                if (!scene.Colonist.story.traits.HasTrait(TraitDefOf.Nudist))
                {
                    scene.Colonist.story.traits.GainTrait(new Trait(TraitDefOf.Nudist));
                }
                if (!StockOnly(scenario, scene, "Apparel_ShieldBelt", "a shield belt"))
                {
                    yield break;
                }
                List<Apparel> before = new List<Apparel>(scene.Colonist.apparel.WornApparel);
                scenario.Check(SwapPlan.PrefersNudity(scene.Colonist), name + " is a nudist; the exemption turns on this");

                scene.Comp.SetFullChange(true);
                scenario.Note("Change the whole outfit is ticked on the stand");
                List<Apparel> wear = new List<Apparel>();
                List<Apparel> store = new List<Apparel>();
                scenario.Check(SwapPlan.BuildDress(scene.Colonist, scene.OutfitStand, wear, store),
                            "the stand still plans a change");
                scenario.Check(store.Count == before.Count, "taking everything, holding nothing back");
                scenario.Check(SwapPlan.WouldBeNude(scene.Colonist, store, wear),
                            "though that leaves " + name + " naked by Shift Change's reckoning: "
                            + "what changed is the answer, not the question");

                yield return Swap(scene, scenario, name + " changes into the belt");
                scenario.Check(scene.Colonist.apparel.WornApparel.Count == 1,
                            name + " wears the belt and nothing else, as the stand is set up");
                yield return Swap(scene, scenario, name + " changes back");
                scenario.Check(scene.Colonist.apparel.WornApparel.Count == before.Count,
                            "and " + name + "'s own clothes came back");
            }
        }

        /// <summary>
        /// The setting ships off and stays off (<see cref="ShiftChangeSettings.keepColonistsDecent"/>):
        /// a default that flipped by accident would change colonies after an update with no sign.
        /// It reads the default from fresh settings, not the running game's, which a player may
        /// have changed, and then checks the setting is wired: a toggle that reads a field nobody
        /// consults looks like a working one.
        /// </summary>
        [Test]
        [TestDescription("Keep colonists decent ships off, and turning it on takes effect")]
        internal IEnumerator GuardIsOffUntilTurnedOn()
        {
            return Staged(Body);

            IEnumerator Body(Scene scene, Scenario scenario)
            {
                string name = scene.Colonist.LabelShort;
                scenario.Check(!new ShiftChangeSettings().keepColonistsDecent,
                            "Keep colonists decent is off in a new game's settings");
                if (!StockOnly(scenario, scene, "Apparel_ShieldBelt", "a shield belt")
                    || !SetDecent(scenario, false))
                {
                    yield break;
                }
                List<Apparel> before = new List<Apparel>(scene.Colonist.apparel.WornApparel);
                scenario.Check(!SwapPlan.PrefersNudity(scene.Colonist),
                            name + " is no nudist, so only the setting decides");

                scene.Comp.SetFullChange(true);
                scenario.Note("Change the whole outfit is ticked on the stand");
                List<Apparel> wear = new List<Apparel>();
                List<Apparel> store = new List<Apparel>();
                scenario.Check(SwapPlan.BuildDress(scene.Colonist, scene.OutfitStand, wear, store),
                            "the stand plans a change");
                scenario.Check(store.Count == before.Count,
                            "which takes everything, as every version up to 1.3.0 did");

                if (!SetDecent(scenario, true))
                {
                    yield break;
                }
                List<Apparel> onWear = new List<Apparel>();
                List<Apparel> onStore = new List<Apparel>();
                scenario.Check(ShiftChangeMod.DecencyEnabled, "turning it on takes effect");
                scenario.Check(SwapPlan.BuildDress(scene.Colonist, scene.OutfitStand, onWear, onStore),
                            "the stand still plans a change");
                scenario.Check(onStore.Count < before.Count, "and now holds something back");
                yield break;
            }
        }
    }
}
