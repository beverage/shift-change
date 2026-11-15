using System.Collections;
using System.Linq;
using DevTools.Testing;
using RimWorld;
using Verse;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.SceneKit;
using static ShiftChange.Tests.WeaponKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// A colonist drafted with a weapon on loan: drafting draws their own weapon from the pack
    /// for the fight, and undrafting puts the stand's back in hand. Each test sets the game's own
    /// <c>Pawn_DraftController.Drafted</c>, since that setter's callers are the risk: it runs inside
    /// downing and a mental break as well as under the player's button.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.DraftingWithALoan)]
    internal class DraftingWithALoan : StoryFixture
    {
        /// <summary>
        /// The whole trade: their own gun out for the fight, the stand's club back afterwards, the
        /// stand's record and the duster untouched throughout, and a trip back that still ends the
        /// loan.
        /// </summary>
        [Test]
        [TestDescription("drafting a borrower draws their own weapon, and undrafting puts the stand's back in hand")]
        internal IEnumerator DraftingDrawsTheirOwnAndUndraftingPutsTheStandsBack()
        {
            return Staged(ArmedWithUniform, ArmedWithUniformSays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingOwner pack = pawn.inventory.innerContainer;
                Apparel uniform = scene.Comp.IssuedUniformForReading.FirstOrDefault();

                pawn.drafter.Drafted = true;
                story.Note(name + " is drafted");
                story.Check(pawn.equipment.Primary == own, "their own autopistol is in hand");
                story.Check(pack.Contains(issued) && !pack.Contains(own), "and the club in their pack in its place");
                story.Check(!issued.Spawned && !own.Spawned, "neither left on the ground");
                story.Check(scene.Comp.issuedWeapon == issued && scene.Comp.ownWeapon == own, "the stand still records both");
                story.Check(uniform != null && pawn.apparel.WornApparel.Contains(uniform), "and the duster stays on");

                pawn.drafter.Drafted = false;
                story.Note(name + " is undrafted");
                story.Check(pawn.equipment.Primary == issued, "the club is back in hand");
                story.Check(pack.Contains(own) && !pack.Contains(issued), "and the autopistol back in their pack");
                story.Check(!issued.Spawned && !own.Spawned, "neither left on the ground");

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(pawn.equipment.Primary == own, "their own autopistol is back in hand");
                story.Check(scene.OutfitStand.HeldWeapon == issued, "and the club on the stand");
                Closed(story, scene);
            }
        }

        /// <summary>
        /// Setting the draft to the value it already holds runs Shift Change's patch and must move
        /// nothing. Each half puts in hand the one weapon the matching real change would trade
        /// away, so a missing check shows.
        /// </summary>
        [Test]
        [TestDescription("drafting a colonist already drafted, or undrafting one who is not, moves no weapon")]
        internal IEnumerator ADraftSetThatChangesNothingMovesNothing()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                story.Check(WeaponHands.Exchange(pawn, own), "undrafted, " + name + " takes their autopistol from the pack");
                story.Check(SwapPlan.WeaponForDraft(pawn, scene.Comp, drafted: false) == issued,
                            "a real undraft would trade it for the club");
                pawn.drafter.Drafted = false;
                story.Check(pawn.equipment.Primary == own, "undrafting them again leaves the autopistol in hand");

                pawn.drafter.Drafted = true;
                story.Check(WeaponHands.Exchange(pawn, issued), "drafted, they take the club from the pack");
                story.Check(SwapPlan.WeaponForDraft(pawn, scene.Comp, drafted: true) == own,
                            "a real draft would trade it for the autopistol");
                pawn.drafter.Drafted = true;
                story.Check(pawn.equipment.Primary == issued, "drafting them again leaves the club in hand");
            }
        }

        /// <summary>
        /// A persona weapon of the colonist's own gives way to the stand's club, comes out for a
        /// draft and goes back in the pack after it, so the club is in hand for the rest of the
        /// shift. A known gap where Royalty is not installed, as <see cref="MakePersona"/> says.
        /// </summary>
        [Test]
        [TestDescription("a persona weapon of the colonist's own comes out for a draft and goes back in the pack after it")]
        internal IEnumerator TheirOwnPersonaWeaponTradesThroughADraft()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!PersonaCanBeEquipped(story, "a persona weapon of the colonist's own trades through a draft"))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps issued = scene.OutfitStand.HeldWeapon;
                ThingWithComps own = Arm(pawn, Autopistol);
                if (!story.Check(issued != null && own != null, "the stand holds a club, and " + name + " an autopistol of their own"))
                {
                    return;
                }
                ThingOwner pack = pawn.inventory.innerContainer;
                CompBladelinkWeapon persona = MakePersona(own);
                persona.CodeFor(pawn);
                story.Note("the autopistol becomes a persona weapon bonded to " + name);

                story.Check(SwapNow(story, scene), name + " changes at the stand");
                story.Check(pawn.equipment.Primary == issued && pack.Contains(own),
                            "the club is in hand and the persona autopistol in the pack");
                pawn.drafter.Drafted = true;
                story.Check(pawn.equipment.Primary == own && pack.Contains(issued),
                            "drafted, the persona autopistol is in hand and the club in the pack");
                pawn.drafter.Drafted = false;
                story.Check(pawn.equipment.Primary == issued && pack.Contains(own),
                            "undrafted, the club is back in hand and the autopistol in the pack");

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(pawn.equipment.Primary == own && scene.OutfitStand.HeldWeapon == issued,
                            "the persona autopistol is back in hand, the club on the stand");
                Closed(story, scene);
                persona.UnCode();
            }
        }

        /// <summary>
        /// The stand's club, bonded to its borrower mid-loan, trades through a draft like any lent
        /// weapon: it goes in the pack while their own autopistol is drawn, and comes back after.
        /// The change back returns it to the stand still bonded. A known gap where Royalty is not
        /// installed, as above.
        /// </summary>
        [Test]
        [TestDescription("a lent weapon bonded to its borrower goes in the pack for a draft and comes back after it")]
        internal IEnumerator ALentPersonaWeaponTradesThroughADraft()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!PersonaCanBeEquipped(story, "a lent persona weapon trades through a draft"))
                {
                    return;
                }
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingOwner pack = pawn.inventory.innerContainer;
                CompBladelinkWeapon persona = MakePersona(issued);
                persona.CodeFor(pawn);
                story.Check(pawn.equipment.bondedWeapon == issued, "the club in hand becomes a persona weapon bonded to " + name);

                pawn.drafter.Drafted = true;
                story.Check(pawn.equipment.Primary == own && pack.Contains(issued),
                            "drafted, their own autopistol is in hand and the persona club in the pack");
                pawn.drafter.Drafted = false;
                story.Check(pawn.equipment.Primary == issued && pack.Contains(own),
                            "undrafted, the persona club is back in hand and the autopistol in the pack");

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(pawn.equipment.Primary == own && scene.OutfitStand.HeldWeapon == issued,
                            "their own autopistol is back in hand, the persona club on the stand");
                story.Check(persona.CodedPawn == pawn && pawn.equipment.bondedWeapon == issued,
                            "and the club still bonded to " + name);
                Closed(story, scene);
                persona.UnCode();
            }
        }

        [Test]
        [TestDescription("a borrower with no weapon of their own fights with the stand's")]
        internal IEnumerator ABorrowerWithNoWeaponOfTheirOwnFightsWithTheStands()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps issued = scene.OutfitStand.HeldWeapon;
                if (!story.Check(issued != null, "the stand holds the club, and " + name + "'s hand is empty"))
                {
                    return;
                }
                story.Check(SwapNow(story, scene), name + " changes at the stand");
                story.Check(pawn.equipment.Primary == issued, "the club is in hand");
                story.Check(scene.Comp.ownWeapon == null, "with no weapon of their own recorded");

                pawn.drafter.Drafted = true;
                story.Check(pawn.equipment.Primary == issued, "drafted, they keep the club");
                pawn.drafter.Drafted = false;
                story.Check(pawn.equipment.Primary == issued, "and undrafted too");
            }
        }

        /// <summary>
        /// Something else in the hand was the colonist's choice, in either direction. Both of the
        /// stand's recorded weapons sit in the pack, so each set has one it could bring out. Simple
        /// Sidearms' own idle switch runs as the undraft clears the job and may draw from the pack,
        /// so under it the hand is left to that mod, and what is checked is that all three weapons
        /// are still carried and the stand's record of the loan has not moved.
        /// </summary>
        [Test]
        [TestDescription("a spare weapon in a borrower's hand stays there through a draft")]
        internal IEnumerator ASpareStaysInHandThroughADraft()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                ThingOwner pack = pawn.inventory.innerContainer;
                story.Check(WeaponHands.ToPack(pawn, issued), "the club goes into " + pawn.LabelShort + "'s pack");
                ThingWithComps spare = Arm(pawn, Revolver);
                story.Note("and they take up a revolver");

                pawn.drafter.Drafted = true;
                story.Check(spare != null && pawn.equipment.Primary == spare, "drafted, the revolver stays in hand");
                story.Check(pack.Contains(issued) && pack.Contains(own), "with the club and the autopistol in the pack");
                story.Check(SwapPlan.WeaponForDraft(pawn, scene.Comp, drafted: false) == null,
                            "and undrafting has nothing to bring out");
                pawn.drafter.Drafted = false;
                if (ModActive(ToolKit.SidearmsPackageId))
                {
                    story.Note("Simple Sidearms is on the list; its own switch decides which weapon is in hand after the undraft");
                    story.Check(new[] { spare, issued, own }.All(w => pawn.equipment.Primary == w || pack.Contains(w)),
                                "undrafted, " + pawn.LabelShort + " still carries all three");
                    story.Check(scene.Comp.issuedWeapon == issued && scene.Comp.ownWeapon == own,
                                "and the stand's record of the loan has not moved");
                    return;
                }
                story.Check(pawn.equipment.Primary == spare, "undrafted, the revolver is still in hand");
                story.Check(pack.Contains(issued) && pack.Contains(own), "and the other two still packed");
            }
        }

        /// <summary>
        /// The stand's club is destroyed in the hand. Drafting fills the empty hand with their own
        /// gun, and undrafting has nothing to bring out, so their own stays.
        /// </summary>
        [Test]
        [TestDescription("with the lent weapon destroyed, drafting draws the colonist's own and undrafting leaves it")]
        internal IEnumerator WithTheLentWeaponGoneDraftingDrawsTheirOwn()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                issued.Destroy();
                story.Check(pawn.equipment.Primary == null, "the club is destroyed in " + pawn.LabelShort + "'s hand");

                pawn.drafter.Drafted = true;
                story.Check(pawn.equipment.Primary == own, "drafted, their own autopistol is in hand");
                pawn.drafter.Drafted = false;
                story.Check(pawn.equipment.Primary == own, "and undrafted it stays there (hand: "
                                                           + (pawn.equipment.Primary?.def.defName ?? "empty") + ")");
            }
        }

        /// <summary>
        /// The colonist's own gun is destroyed in the fight. Undrafting fills the empty hand with
        /// the stand's club, and a later draft has nothing to bring out, so they fight with that.
        /// </summary>
        [Test]
        [TestDescription("with the colonist's own weapon lost in a fight, undrafting draws the stand's")]
        internal IEnumerator WithTheirOwnLostUndraftingDrawsTheStands()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                pawn.drafter.Drafted = true;
                own.Destroy();
                story.Check(pawn.equipment.Primary == null, "drafted, " + pawn.LabelShort + " loses their autopistol");

                pawn.drafter.Drafted = false;
                story.Check(pawn.equipment.Primary == issued, "undrafted, the club is in hand");
                pawn.drafter.Drafted = true;
                story.Check(pawn.equipment.Primary == issued, "and drafted again, they keep it");
            }
        }

        /// <summary>
        /// Banishing takes a drafted colonist's draft controller away inside
        /// <c>Pawn.SetFaction</c>, so the setter never runs and nothing trades back. The stand's
        /// check for departed borrowers closes the loan, and the fight's gun stays in hand.
        /// </summary>
        [Test]
        [TestDescription("a borrower banished while drafted keeps the weapon they fight with")]
        internal IEnumerator ABorrowerBanishedWhileDraftedKeepsWhatTheyHold()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                pawn.drafter.Drafted = true;
                story.Check(SwapPlan.WeaponForDraft(pawn, scene.Comp, drafted: false) == issued,
                            "drafted, an ordinary undraft would trade back to the club");

                PawnBanishUtility.Banish(pawn, giveThoughts: false);
                story.Check(pawn.drafter == null, pawn.LabelShort + " is banished, which takes their draft controller");
                story.Check(pawn.equipment.Primary == own, "their own autopistol stays in hand");
                story.Check(pawn.inventory.innerContainer.Contains(issued), "and the club in their pack");
                Closed(story, scene);
            }
        }

        /// <summary>
        /// Downing undrafts from inside <c>MakeDowned</c>, and a colonist downed in bed keeps both
        /// weapons. Off a bed both drop first, so the test puts them back where a bed would have
        /// left them and sets the draft again, which is the same setter on the same downed
        /// colonist.
        /// </summary>
        [Test]
        [TestDescription("an undraft on a downed borrower moves no weapon")]
        internal IEnumerator AnUndraftOnADownedBorrowerMovesNothing()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingOwner pack = pawn.inventory.innerContainer;
                pawn.drafter.Drafted = true;
                story.Check(SwapPlan.WeaponForDraft(pawn, scene.Comp, drafted: false) == issued,
                            "drafted and on their feet, an undraft would trade back to the club");

                pawn.health.AddHediff(HediffDefOf.Anesthetic);
                story.Check(pawn.Downed && !pawn.Drafted, name + " goes down under anesthetic, which undrafts them");
                story.Check(issued.Spawned && own.Spawned, "both weapons fall to the ground");
                issued.DeSpawn();
                pack.TryAdd(issued, canMergeWithExistingStacks: false);
                own.DeSpawn();
                pawn.equipment.AddEquipment(own);
                story.Note("the autopistol is put back in hand and the club in the pack, as a bed would have left them");

                pawn.drafter.Drafted = true;
                pawn.drafter.Drafted = false;
                story.Note(name + " is drafted and undrafted where they lie");
                story.Check(pawn.equipment.Primary == own, "their own autopistol stays in hand");
                story.Check(pack.Contains(issued), "and the club in their pack");
            }
        }

        /// <summary>
        /// A break undrafts before it sets the mental state, so that undraft reads as ordinary and
        /// trades back, with both weapons still on the colonist. A draft set during the break is a
        /// real change and moves nothing.
        /// </summary>
        [Test]
        [TestDescription("a mental break trades a drafted borrower's weapons back once, then holds still")]
        internal IEnumerator AMentalBreakTradesBackOnceThenHoldsStill()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingOwner pack = pawn.inventory.innerContainer;
                pawn.drafter.Drafted = true;
                story.Check(pawn.equipment.Primary == own, "drafted, " + name + "'s own autopistol is in hand");
                story.Check(pawn.mindState.mentalStateHandler.TryStartMentalState(
                                MentalStateDefOf.Wander_Sad, forced: true, transitionSilently: true),
                            name + " breaks and wanders sadly");
                story.Check(!pawn.Drafted, "the break undrafted them");
                story.Check(pawn.equipment.Primary == issued && pack.Contains(own),
                            "and that undraft traded back to the club, the autopistol in their pack");

                pawn.drafter.Drafted = true;
                story.Check(pawn.Drafted, "drafted during the break");
                story.Check(pawn.equipment.Primary == issued && pack.Contains(own), "they keep the club in hand");
                pawn.drafter.Drafted = false;
                pawn.MentalState.RecoverFromState();
            }
        }

        /// <summary>
        /// A drafted colonist sent to change back holds their own gun. The club goes home from the
        /// pack and the gun stays in hand, and undrafting after finds no loan to trade.
        /// </summary>
        [Test]
        [TestDescription("changing back while drafted returns the stand's weapon from the pack")]
        internal IEnumerator ChangingBackWhileDraftedReturnsTheStandsWeapon()
        {
            return Staged(ArmedWithUniform, ArmedWithUniformSays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                pawn.drafter.Drafted = true;
                story.Check(pawn.equipment.Primary == own, "drafted, " + pawn.LabelShort + "'s own autopistol is in hand");
                story.Check(SwapNow(story, scene), "they change back drafted");
                story.Check(scene.OutfitStand.HeldWeapon == issued, "the club went back to the stand from their pack");
                story.Check(pawn.equipment.Primary == own, "the autopistol stayed in hand");
                Closed(story, scene);

                pawn.drafter.Drafted = false;
                story.Check(pawn.equipment.Primary == own, "and undrafting leaves it there");
            }
        }
    }
}
