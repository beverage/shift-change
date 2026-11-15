using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DevTools.Testing;
using RimWorld;
using Verse;
using static ShiftChange.Tests.LifecycleKit;
using static ShiftChange.Tests.SceneKit;
using static ShiftChange.Tests.WeaponKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// How a weapon loan ends: a trip back that finds the weapons somewhere the trip out did not
    /// leave them, and the ends with no trip back at all, where the stand burns, the colonist is
    /// banished, or a map goes. Each starts from the same loan (<see cref="Lend"/>) and names
    /// where both weapons end up. A borrower downed mid-loan is <see cref="DownedWithALoan"/>'s.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.EndingALoan)]
    internal class EndingALoan : StoryFixture
    {
        /// <summary>
        /// A sidearm mod's switch moves the stand's club to the pack and draws the colonist's own
        /// gun. The trip back reads where things are now: the club goes back from the pack, and the
        /// gun already in hand stays there.
        /// </summary>
        [Test]
        [TestDescription("a stand's weapon moved to the pack goes back from there, and a gun drawn in its place stays in hand")]
        internal IEnumerator AStandsWeaponInThePackGoesBackFromThere()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                story.Check(WeaponHands.ToPack(pawn, issued), "the club goes into " + pawn.LabelShort + "'s pack");
                story.Check(WeaponHands.FromPack(pawn, own), "and their autopistol comes out of it");
                story.Check(PlannedWait(scene) > 0, "changing back still takes time, for the club");

                story.Check(SwapNow(story, scene), pawn.LabelShort + " changes back");
                story.Check(scene.OutfitStand.HeldWeapon == issued, "the club went back to the stand from the pack");
                story.Check(!pawn.inventory.innerContainer.Contains(issued), "and left the pack");
                story.Check(pawn.equipment.Primary == own, "the autopistol stayed in hand");
                Closed(story, scene);
            }
        }

        /// <summary>
        /// The stand's club is destroyed in the colonist's hand. Nothing goes back, and the
        /// record's other half still brings their own gun out, which is why it counts toward
        /// <see cref="CompShiftStand.OnShift"/>.
        /// </summary>
        [Test]
        [TestDescription("a lent weapon destroyed in the hand still brings the colonist's own out of their pack")]
        internal IEnumerator ALentWeaponDestroyedStillBringsTheirOwnOut()
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
                story.Note("the club is destroyed in " + pawn.LabelShort + "'s hand");
                story.Check(pawn.equipment.Primary == null, "their hand is empty");
                story.Check(scene.Comp.OnShift, "and the stand still counts them on shift");

                story.Check(SwapNow(story, scene), pawn.LabelShort + " changes back");
                story.Check(pawn.equipment.Primary == own, "their own autopistol is back in hand");
                story.Check(scene.OutfitStand.HeldWeapon == null, "and the stand got nothing back");
                Closed(story, scene);
            }
        }

        /// <summary>
        /// The colonist picked up something else on shift. Their choice stays in the hand, their
        /// own gun stays in the pack, and the stand's club still goes home.
        /// </summary>
        [Test]
        [TestDescription("a spare weapon a colonist took up on shift stays in hand, with their own left in the pack")]
        internal IEnumerator ASpareTakenUpOnShiftStaysInHand()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                story.Check(WeaponHands.ToPack(pawn, issued), "the club goes into " + pawn.LabelShort + "'s pack");
                ThingWithComps spare = Arm(pawn, Revolver);
                story.Check(spare != null && pawn.equipment.Primary == spare, "and they take up a revolver");

                story.Check(SwapNow(story, scene), pawn.LabelShort + " changes back");
                story.Check(scene.OutfitStand.HeldWeapon == issued, "the club went back to the stand");
                story.Check(pawn.equipment.Primary == spare, "the revolver stayed in hand");
                story.Check(pawn.inventory.innerContainer.Contains(own), "and their autopistol in their pack");
                Closed(story, scene);
            }
        }

        /// <summary>
        /// The colonist's own gun is destroyed in their pack. The stand's club and the duster go
        /// back as usual and the hand ends empty.
        /// </summary>
        [Test]
        [TestDescription("a colonist whose own weapon was destroyed in their pack changes back empty-handed")]
        internal IEnumerator TheirOwnWeaponGoneTheyChangeBackEmptyHanded()
        {
            return Staged(ArmedWithUniform, ArmedWithUniformSays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                Apparel uniform = scene.Comp.IssuedUniformForReading.FirstOrDefault();
                story.Check(uniform != null, pawn.LabelShort + " is wearing the duster");
                own.Destroy();
                story.Note("their autopistol is destroyed in their pack");

                story.Check(SwapNow(story, scene), pawn.LabelShort + " changes back");
                story.Check(scene.OutfitStand.HeldWeapon == issued, "the club went back to the stand");
                story.Check(uniform != null && uniform.ParentHolder == scene.OutfitStand, "and the duster");
                story.Check(pawn.equipment.Primary == null, "their hand is empty");
                Closed(story, scene);
            }
        }

        /// <summary>
        /// The stand burns with its club out. There is nothing to go back to, so the colonist
        /// keeps the club in their pack and fights with their own gun again, and the duster is
        /// ordinary clothing.
        /// </summary>
        [Test]
        [TestDescription("a burnt stand leaves its weapon in the colonist's pack and their own in hand")]
        internal IEnumerator ABurntStandLeavesItsWeaponInThePack()
        {
            return Staged(ArmedWithUniform, ArmedWithUniformSays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                Apparel uniform = scene.Comp.IssuedUniformForReading.FirstOrDefault();
                scene.OutfitStand.Destroy(DestroyMode.KillFinalize);
                story.Check(scene.OutfitStand.Destroyed, "the stand burns down");
                Settled(story, scene, issued, own);
                story.Check(uniform != null && pawn.apparel.WornApparel.Contains(uniform), "the duster stays on");
                if (uniform != null && pawn.outfits != null)
                {
                    story.Check(!pawn.outfits.forcedHandler.IsForced(uniform), "and is no longer forced");
                }
            }
        }

        /// <summary>
        /// A boxed stand burns with its box. It was never spawned, so no despawn tells it, and
        /// <see cref="CompShiftStand.PostDestroy"/> is its only notice.
        /// </summary>
        [Test]
        [TestDescription("a stand burnt while uninstalled in its box settles the loan as a burnt stand does")]
        internal IEnumerator AStandBurntInItsBoxSettlesTheSame()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                IntVec3 cell = scene.OutfitStand.Position;
                MinifiedThing box = scene.OutfitStand.MakeMinified();
                if (!story.Check(box != null, "the stand is uninstalled into a box"))
                {
                    return;
                }
                GenSpawn.Spawn(box, cell, scene.Map);
                story.Check(scene.Comp.OnShift, "the loan rode into the box");
                story.Check(scene.Colonist.equipment.Primary == issued, "with the club still in " + scene.Colonist.LabelShort + "'s hand");

                box.Destroy(DestroyMode.KillFinalize);
                story.Check(scene.OutfitStand.Destroyed, "the box burns, the stand with it");
                Settled(story, scene, issued, own);
            }
        }

        /// <summary>
        /// A banished borrower reaches the stand's check for departed borrowers, so both weapons
        /// stay where they are: the stand's in hand, their own in the pack.
        /// </summary>
        [Test]
        [TestDescription("a banished borrower keeps the weapons they hold")]
        internal IEnumerator ABanishedBorrowerKeepsWhatTheyHold()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                PawnBanishUtility.Banish(scene.Colonist, giveThoughts: false);
                story.Check(CompShiftStand.Departed(scene.Colonist), scene.Colonist.LabelShort + " is banished");
                Untouched(story, scene, issued, own);
                Closed(story, scene);
            }
        }

        /// <summary>
        /// A borrower banished while the stand is boxed, which the check for departed borrowers
        /// cannot find. The release comes from <see cref="CompShiftStand.PostSpawnSetup"/>, the one
        /// that also runs inside <c>Map.FinalizeLoading</c>, where nothing may move through the
        /// ground.
        /// </summary>
        [Test]
        [TestDescription("a borrower banished while their stand was in its box keeps the weapons they hold")]
        internal IEnumerator ABorrowerBanishedWhileTheStandWasBoxedKeepsWhatTheyHold()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Map map = scene.Map;
                IntVec3 to = new IntVec3(scene.OutfitStand.Position.x, 0, scene.OutfitStand.Position.z + 2);
                Rot4 rot = scene.OutfitStand.Rotation;
                MinifiedThing box = scene.OutfitStand.MakeMinified();
                if (!story.Check(box != null, "the stand is uninstalled into a box"))
                {
                    return;
                }

                PawnBanishUtility.Banish(scene.Colonist, giveThoughts: false);
                story.Check(scene.Comp.OnShift, scene.Colonist.LabelShort + " is banished, and the boxed stand still counts them on shift");

                box.InnerThing = null;
                box.Destroy();
                GenSpawn.Spawn(scene.OutfitStand, to, map, rot);
                story.Check(scene.OutfitStand.Spawned, "the stand is set down again two cells north");
                Untouched(story, scene, issued, own);
                Closed(story, scene);
            }
        }

        /// <summary>
        /// A box set down after the shift moved on to another stand is a stale copy. Its record
        /// goes and its weapons settle, while the stand now holding the shift keeps it.
        /// </summary>
        [Test]
        [TestDescription("a boxed stand set down after its colonist's shift moved to another stand settles its weapons")]
        internal IEnumerator AStaleBoxSettlesItsWeapons()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                Map map = scene.Map;
                IntVec3 at = scene.OutfitStand.Position;
                Rot4 rot = scene.OutfitStand.Rotation;
                CompShiftStand next = DebugTools_Fixtures.Spawn(map, scene.OutfitStand.def, scene.OutfitStand.Stuff,
                    new IntVec3(at.x + 2, 0, at.z), rot).TryGetComp<CompShiftStand>();
                MinifiedThing box = scene.OutfitStand.MakeMinified();
                Apparel shirt = Worn(pawn, "Apparel_BasicShirt");
                if (!story.Check(next != null && box != null && shirt != null,
                                 "a second stand stands beside the first, which is uninstalled into a box"))
                {
                    return;
                }
                next.NotifyDressed(pawn, new List<Apparel>(), new List<Apparel> { shirt }, null);
                story.Note("the second stand records " + pawn.LabelShort + "'s shirt as its own, so the shift is now there");

                box.InnerThing = null;
                box.Destroy();
                GenSpawn.Spawn(scene.OutfitStand, at, map, rot);
                story.Check(scene.OutfitStand.Spawned, "the first stand is set down again");
                story.Check(pawn.equipment.Primary == own, "their own autopistol is back in hand");
                story.Check(pawn.inventory.innerContainer.Contains(issued), "the first stand's club is in their pack");
                story.Check(!scene.Comp.OnShift, "the first stand's stale record is gone");
                story.Check(CompShiftStand.OnShiftStandFor(pawn) == next, "and the second stand keeps the shift");
                next.AbandonLedger(pawn, announce: false);
            }
        }

        /// <summary>
        /// The stand burns while its borrower is on no map, as a colonist in a caravan is. There
        /// is no ground for a drop to use, so the weapons cross between containers.
        /// </summary>
        [Test]
        [TestDescription("a borrower on no map, as in a caravan, settles the loan without the ground")]
        internal IEnumerator ABorrowerOnNoMapSettlesWithoutTheGround()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                scene.Colonist.DeSpawn(DestroyMode.Vanish);
                story.Check(scene.Colonist.MapHeld == null, scene.Colonist.LabelShort + " leaves the map");

                scene.OutfitStand.Destroy(DestroyMode.KillFinalize);
                story.Note("the stand burns down");
                Settled(story, scene, issued, own);
            }
        }

        /// <summary>
        /// The map goes with the borrower and the stand both on it. Vanilla despawns the colonist
        /// and unclaims for them before it passes them to the world, so the stand's check for
        /// departed borrowers reaches a colonist on no map with the map still standing.
        /// </summary>
        [Test]
        [TestDescription("a map removed with a borrower on it settles the loan between containers")]
        internal IEnumerator AMapRemovedUnderItsBorrowerSettlesBetweenContainers()
        {
            return Staged(ArmedOnSecondMap, ArmedOnSecondMapSays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Map origin = scene.SecondMap;
                RemoveSecondMap(origin, keepPawns: true);
                scene.SecondMap = null;
                story.Check(!Find.Maps.Contains(origin), "the map is removed");
                story.Check(!scene.Colonist.Destroyed && scene.Colonist.MapHeld == null,
                            "and " + scene.Colonist.LabelShort + " passed to the world");
                Settled(story, scene, issued, own);
            }
        }

        /// <summary>
        /// The map goes with the stand on it and the borrower on another. The stand's only notice
        /// comes in the middle of the teardown, and the moves it makes happen on the colonist's
        /// map.
        /// </summary>
        [Test]
        [TestDescription("a map removed from under a stand settles the loan of a borrower on another map")]
        internal IEnumerator AMapRemovedFromUnderAStandSettlesItsBorrower()
        {
            return Staged(ArmedOnSecondMap, ArmedOnSecondMapSays, Body);

            // The scene is on a second map, so the colonist flies to the test's own map, onto its pad.
            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Map origin = scene.SecondMap;
                Fly(scene.Colonist, Map, new IntVec3(Pad.minX + 3, 0, Pad.minZ + 1));
                story.Check(scene.Colonist.Map == Map, scene.Colonist.LabelShort + " flies home, leaving the stand behind");

                RemoveSecondMap(origin);
                scene.SecondMap = null;
                story.Check(scene.OutfitStand.Discarded, "the map the stand was left on is removed, the stand with it");
                Settled(story, scene, issued, own);
            }
        }

        /// <summary>
        /// Outfit Stands Plus' own job to put things back on a stand, ordered mid-loan. Their stand
        /// filter refuses the club at first, so it stays in hand; with weapons allowed it goes back
        /// on the stand. Either way Shift Change's trip back still closes the loan.
        /// </summary>
        [Test]
        [TestDescription("Outfit Stands Plus' job to put things back on a stand leaves a loan that still closes")]
        internal IEnumerator OutfitStandsPlusReturnJobLeavesALoanThatCloses()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                JobDef returnJob = DefDatabase<JobDef>.GetNamedSilentFail("OutfitStandsPlus_JobReturnToStand");
                if (returnJob == null)
                {
                    story.KnownGap(false, "Outfit Stands Plus' job to put things back leaves a loan that still closes",
                        "Outfit Stands Plus is not on this mod list; --with=khamenman.outfitstandsplus loads it");
                    return;
                }
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                OrderJob(story, scene, returnJob);
                story.Check(pawn.equipment.Primary == issued, "the stand's filter refuses the club, so it stays in hand");
                story.Check(scene.Comp.OnShift, "with the loan still open");

                scene.OutfitStand.GetStoreSettings().filter.SetAllow(ThingCategoryDefOf.Weapons, allow: true);
                OrderJob(story, scene, returnJob);
                story.Check(scene.OutfitStand.HeldWeapon == issued, "with weapons allowed, the club goes back on the stand");
                story.Check(pawn.equipment.Primary == null, "which leaves " + pawn.LabelShort + "'s hand empty");
                story.Check(pawn.inventory.innerContainer.Contains(own), "and their autopistol in their pack");

                story.Check(SwapNow(story, scene), pawn.LabelShort + " changes back");
                story.Check(pawn.equipment.Primary == own, "their own autopistol is drawn");
                story.Check(scene.OutfitStand.HeldWeapon == issued, "the club stays on the stand");
                Closed(story, scene);
            }
        }
    }
}
