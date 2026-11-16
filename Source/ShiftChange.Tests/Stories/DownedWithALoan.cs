using System.Collections;
using DevTools.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.WeaponKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// A borrower downed with a weapon on loan. Downing drops the hand and the pack, as for any
    /// downed colonist, and the engine remembers the weapon that was in hand for the colonist to
    /// fetch on getting up (<c>JobGiver_PickupDroppedWeapon</c>, high in the colonist think tree).
    /// The first two change the colonist back as soon as they are up; each test after them lets
    /// the game's own think tree choose what the colonist does once up, stepping the whole
    /// clock, and names where both weapons end up.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.DownedWithALoan)]
    internal class DownedWithALoan : StoryFixture
    {
        /// <summary>The most ticks a colonist who has just got up is given to take a weapon up.</summary>
        internal const int TakeUpTicks = 2500;

        /// <summary>
        /// Downing drops the hand and the pack: the hand's weapon forbidden, the pack's only
        /// outside the home area. The colonist changes back straight after getting up, before any
        /// fetch, so the trip back finds neither weapon. It closes the record without touching
        /// what lies on the ground, and points the engine's note at their own weapon, so vanilla's
        /// fetch goes for that.
        /// </summary>
        [Test]
        [TestDescription("a downed borrower drops both weapons, and the loan still closes when they change back")]
        internal IEnumerator ADownedBorrowerDropsBothWeapons()
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
                Hediff anesthetic = GoDown(story, scene);
                story.Check(issued.Spawned && own.Spawned, "both weapons fall to the ground");
                story.Check(issued.IsForbidden(Faction.OfPlayer), "the club from their hand forbidden");
                story.Check(own.IsForbidden(Faction.OfPlayer) != scene.Map.areaManager.Home[own.Position],
                            "the autopistol from their pack forbidden only outside the home area");
                HandIsRemembered(story, pawn, issued);
                story.Check(scene.Comp.OnShift, "the stand counts them on shift until they change back");
                GetUp(story, scene, anesthetic);

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(issued.Spawned && own.Spawned, "both weapons stay where they fell");
                story.Check(scene.OutfitStand.HeldWeapon == null && pawn.equipment.Primary == null,
                            "the stand got nothing back, and " + name + "'s hand stays empty");
                Closed(story, scene);
                story.Check(pawn.mindState.droppedWeapon == own, "their own autopistol is remembered for them to fetch");
                Job fetch = Fetch(pawn);
                story.Check(fetch != null && fetch.def == JobDefOf.Equip && fetch.targetA.Thing == own,
                            "and the game's own job for a dropped weapon goes for it");
            }
        }

        /// <summary>
        /// A colonist who came to the stand with nothing in hand changes back before taking the
        /// stand's club up, as a job the stand does not dress for has them do. The loan closes
        /// with the club on the ground, and the engine's note of it goes with the loan: taken up
        /// after the change, the club would become their own and leave the stand empty.
        /// </summary>
        [Test]
        [TestDescription("a borrower who came empty-handed and changes back before taking the stand's weapon up leaves it where it fell")]
        internal IEnumerator AnEmptyHandedBorrowerWhoChangesBackFirstLeavesTheStandsWeapon()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps issued = scene.OutfitStand.HeldWeapon;
                if (!story.Check(issued != null && pawn.equipment.Primary == null,
                                 "the stand holds the club, and " + name + "'s hand is empty")
                    || !story.Check(SwapNow(story, scene), name + " changes at the stand")
                    || !story.Check(pawn.equipment.Primary == issued && scene.Comp.ownWeapon == null,
                                    "the club is in hand, with no weapon of their own recorded"))
                {
                    return;
                }
                Hediff anesthetic = GoDown(story, scene);
                if (!HandIsRemembered(story, pawn, issued))
                {
                    return;
                }
                GetUp(story, scene, anesthetic);
                if (!story.Check(pawn.mindState.droppedWeapon == issued,
                                 "the club is still remembered as they get up"))
                {
                    return;
                }

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(issued.Spawned && scene.OutfitStand.HeldWeapon == null,
                            "the club stays where it fell, and the stand got nothing back");
                Closed(story, scene);
                story.Check(pawn.mindState.droppedWeapon == null && Fetch(pawn) == null,
                            "and the club is no longer remembered for " + name + " to fetch");
            }
        }

        /// <summary>
        /// A room stand keeps the uniform on for anything inside its room, so the fetch of the
        /// stand's club, a step from where the colonist fell, puts it back in hand on loan still.
        /// The trip back returns it to the stand, and their own autopistol, which is not in the
        /// pack to draw, is fetched from where it fell.
        /// </summary>
        [Test]
        [TestDescription("a borrower who gets up where they fell takes the stand's weapon back up, and their own after changing back")]
        internal IEnumerator ABorrowerWhoGetsUpTakesBothBackUp()
        {
            return Staged(ArmedInARoom, ArmedInARoomSays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                Hediff anesthetic = GoDown(story, scene);
                if (!HandIsRemembered(story, pawn, issued))
                {
                    return;
                }
                GetUp(story, scene, anesthetic);
                if (!TakesUp(story, scene, issued, "the club"))
                {
                    return;
                }
                story.Check(scene.Comp.OnShift, "still on loan, with the stand counting them on shift");

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(scene.OutfitStand.HeldWeapon == issued, "the club went back to the stand");
                story.Check(own.Spawned && pawn.equipment.Primary == null,
                            "their autopistol is still on the ground, and their hand empty");
                Closed(story, scene);
                TakesUp(story, scene, own, "their own autopistol");
            }
        }

        /// <summary>
        /// A radius stand changes its borrower back before any job it does not dress for, as a
        /// room stand does for a job outside its room, so without a rule for it the fetch of the
        /// stand's club would close the loan first and then make the club the colonist's own. A
        /// fetch of the weapon on loan keeps them in uniform instead, and the rest goes as in a
        /// room.
        /// </summary>
        [Test]
        [TestDescription("a radius stand's borrower who gets up where they fell takes its weapon back up before changing back")]
        internal IEnumerator ARadiusStandsBorrowerTakesItsWeaponBackUpFirst()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                scene.Comp.SetServes(CatchmentKind.Radius);
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                Hediff anesthetic = GoDown(story, scene);
                if (!HandIsRemembered(story, pawn, issued))
                {
                    return;
                }
                GetUp(story, scene, anesthetic);
                if (!TakesUp(story, scene, issued, "the club")
                    | !story.Check(scene.Comp.OnShift, "still on loan, with the stand counting them on shift"))
                {
                    return;
                }

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(scene.OutfitStand.HeldWeapon == issued, "the club went back to the stand");
                Closed(story, scene);
                TakesUp(story, scene, own, "their own autopistol");
            }
        }

        /// <summary>
        /// A colonist who came to the stand with nothing in hand has no weapon of their own on the
        /// ground. The club goes back into hand and home to the stand, and nothing is left for
        /// them to fetch. At a radius stand, where the fetch would otherwise close the loan first.
        /// </summary>
        [Test]
        [TestDescription("a borrower who came empty-handed and went down takes the stand's weapon back up, and it goes home")]
        internal IEnumerator AnEmptyHandedBorrowerTakesTheStandsWeaponBackUp()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                scene.Comp.SetServes(CatchmentKind.Radius);
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps issued = scene.OutfitStand.HeldWeapon;
                if (!story.Check(issued != null, "the stand holds the club, and " + name + "'s hand is empty")
                    || !story.Check(SwapNow(story, scene), name + " changes at the stand")
                    || !story.Check(pawn.equipment.Primary == issued && scene.Comp.ownWeapon == null,
                                    "the club is in hand, with no weapon of their own recorded"))
                {
                    return;
                }
                Hediff anesthetic = GoDown(story, scene);
                if (!HandIsRemembered(story, pawn, issued))
                {
                    return;
                }
                GetUp(story, scene, anesthetic);
                if (!TakesUp(story, scene, issued, "the club")
                    | !story.Check(scene.Comp.OnShift, "still on loan, with the stand counting them on shift"))
                {
                    return;
                }

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(scene.OutfitStand.HeldWeapon == issued, "the club went back to the stand");
                story.Check(pawn.equipment.Primary == null, "their hand is empty");
                Closed(story, scene);
                story.Check(pawn.mindState.droppedWeapon == null && Fetch(pawn) == null,
                            "and nothing is left for them to fetch");
            }
        }

        /// <summary>
        /// Drafted, the colonist held their own autopistol, so that is the weapon the engine
        /// remembers and fetches. In a room the stand covers, the fetch keeps the uniform on, and
        /// the trip back leaves the autopistol in hand. The stand's club stays where it fell,
        /// forbidden, for the player to deal with as for any downed colonist, and the trip back
        /// closes the loan without it.
        /// </summary>
        [Test]
        [TestDescription("a borrower downed while drafted gets up to fetch their own weapon, and the stand's stays on the ground")]
        internal IEnumerator ABorrowerDownedWhileDraftedFetchesTheirOwn()
        {
            return Staged(ArmedInARoom, ArmedInARoomSays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                pawn.drafter.Drafted = true;
                story.Check(pawn.equipment.Primary == own, "drafted, " + name + "'s autopistol is in hand");
                Hediff anesthetic = GoDown(story, scene);
                if (!story.Check(issued.Spawned && own.Spawned, "both weapons fall to the ground")
                    || !HandIsRemembered(story, pawn, own, "the autopistol"))
                {
                    return;
                }
                GetUp(story, scene, anesthetic);
                if (!TakesUp(story, scene, own, "their own autopistol")
                    | !story.Check(scene.Comp.OnShift, "still on loan, with the stand counting them on shift"))
                {
                    return;
                }

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(pawn.equipment.Primary == own, "their own autopistol stays in hand");
                story.Check(issued.Spawned && scene.OutfitStand.HeldWeapon == null,
                            "the club stays on the ground, and the stand got nothing back");
                Closed(story, scene);
            }
        }

        /// <summary>
        /// The stand burns while its borrower lies downed, so the loan closes with no trip back
        /// and both weapons on the ground. The club is an ordinary item from then on, and the
        /// colonist, once up, fetches their own autopistol rather than the club that was in hand.
        /// </summary>
        [Test]
        [TestDescription("a downed borrower whose stand burns gets up to fetch their own weapon, not the stand's")]
        internal IEnumerator ADownedBorrowerWhoseStandBurnsFetchesTheirOwn()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                Hediff anesthetic = GoDown(story, scene);
                if (!story.Check(issued.Spawned && own.Spawned, "both weapons fall to the ground"))
                {
                    return;
                }
                scene.OutfitStand.Destroy(DestroyMode.KillFinalize);
                story.Check(scene.OutfitStand.Destroyed, "the stand burns down");
                Closed(story, scene);
                story.Check(issued.Spawned && own.Spawned, "both weapons stay where they fell");

                GetUp(story, scene, anesthetic);
                TakesUp(story, scene, own, "their own autopistol");
                story.Check(issued.Spawned, "and the club stays on the ground");
            }
        }

        /// <summary>Downs the colonist under anesthetic, which wears off when it is removed.</summary>
        internal static Hediff GoDown(Story story, Scene scene)
        {
            Pawn pawn = scene.Colonist;
            Hediff anesthetic = pawn.health.AddHediff(HediffDefOf.Anesthetic);
            story.Check(pawn.Downed, pawn.LabelShort + " goes down under anesthetic");
            return anesthetic;
        }

        /// <summary>Removes the anesthetic, so the colonist gets up where they fell, uncarried.</summary>
        internal static void GetUp(Story story, Scene scene, Hediff anesthetic)
        {
            Pawn pawn = scene.Colonist;
            pawn.health.RemoveHediff(anesthetic);
            story.Check(!pawn.Downed, pawn.LabelShort + " gets back up where they fell");
        }

        /// <summary>
        /// Steps the clock, the think tree choosing the colonist's jobs, until a weapon is in
        /// their hand, and checks it is <paramref name="weapon"/>.
        /// </summary>
        internal static bool TakesUp(Story story, Scene scene, ThingWithComps weapon, string what)
        {
            Pawn pawn = scene.Colonist;
            ClockRun run = GameClock.Step(() => pawn.equipment.Primary != null, TakeUpTicks, scene.Hold);
            ThingWithComps held = pawn.equipment.Primary;
            return story.Check(held == weapon, held == weapon
                ? pawn.LabelShort + " takes " + what + " up (" + run.Ticks + " ticks)"
                : pawn.LabelShort + " takes " + what + " up; instead their hand holds "
                  + (held == null ? "nothing after " + run.Ticks + " ticks" : held.LabelShort)
                  + ", and their job is " + (pawn.CurJobDef == null ? "none" : pawn.CurJobDef.defName));
        }

        /// <summary>
        /// The job the colonist think tree's fetch of a dropped weapon gives now, as the tree
        /// configures it, forbidden or not; null when it gives none.
        /// </summary>
        internal static Job Fetch(Pawn pawn)
        {
            return new JobGiver_PickupDroppedWeapon { ignoreForbidden = true }
                .TryIssueJobPackage(pawn, default(JobIssueParams)).Job;
        }
    }
}
