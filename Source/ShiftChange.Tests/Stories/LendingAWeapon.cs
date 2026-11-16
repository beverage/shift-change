using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DevTools.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.SceneKit;
using static ShiftChange.Tests.WeaponKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// A stand that lends the weapon it holds: what a stand takes, whom it arms, and the loan
    /// itself, the stand's weapon in the colonist's hand and their own in their pack until they
    /// change back. Every loan goes through the real driver, because the weapon step's whole claim
    /// is that it survives the clothes half's early returns. How a loan ends when the trip back
    /// is not the usual one is <see cref="EndingALoan"/>'s, a draft mid-loan
    /// <see cref="DraftingWithALoan"/>'s, and a downing <see cref="DownedWithALoan"/>'s.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.LendingAWeapon)]
    internal class LendingAWeapon : StoryFixture
    {
        /// <summary>
        /// A stand with no clothes in it dresses a colonist, lends the club and takes the same club
        /// back. Nothing else opens the stand's record here, so <c>OnShift</c>, the registry of
        /// loans and the driver's checks for an empty plan are each what this leans on.
        /// </summary>
        [Test]
        [TestDescription("a stand holding only a weapon lends it to a colonist and takes the same one back")]
        internal IEnumerator AStandHoldingOnlyAWeaponLendsIt()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps weapon = scene.OutfitStand.HeldWeapon;
                List<Apparel> wear = new List<Apparel>();
                List<Apparel> store = new List<Apparel>();
                if (!story.Check(weapon != null, "the stand holds the club"))
                {
                    return;
                }
                story.Check(pawn.equipment.Primary == null, name + "'s hand is empty");
                story.Check(!SwapPlan.BuildDress(pawn, scene.OutfitStand, wear, store) && wear.Count == 0 && store.Count == 0,
                            "there are no clothes to change");
                story.Check(SwapPlan.WouldDress(pawn, scene.OutfitStand), "and still the stand would send " + name + " to it");
                story.Check(scene.Comp.HasStock, "the stand does not read as empty");
                int clothes = pawn.apparel.WornApparel.Count;
                story.Check(PlannedWait(scene) > 0, "the change takes time, as changing clothes does");

                story.Check(SwapNow(story, scene), name + " changes at the stand");
                story.Check(pawn.equipment.Primary == weapon, "the same club is in " + name + "'s hand");
                story.Check(scene.OutfitStand.HeldWeapon == null, "and the stand let go of it");
                story.Check(scene.Comp.issuedWeapon == weapon, "the stand records the club it lent");
                story.Check(scene.Comp.OnShift && scene.Comp.Borrower == pawn, "and counts " + name + " on shift");
                story.Check(CompShiftStand.OnShiftStandFor(pawn) == scene.Comp,
                            "with a loan in " + name + "'s name, so a trip back exists");
                story.Check(pawn.apparel.WornApparel.Count == clothes, "no clothes moved");
                story.Check(PlannedWait(scene) > 0, "changing back takes time too");

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(scene.OutfitStand.HeldWeapon == weapon, "the same club is back on the stand");
                story.Check(pawn.equipment.Primary == null, "and " + name + "'s hand is empty again");
                story.Check(!scene.Comp.OnShift && scene.Comp.issuedWeapon == null, "the stand's record is cleared");
                story.Check(CompShiftStand.OnShiftStandFor(pawn) == null, "and the loan with it");
                story.Check(pawn.apparel.WornApparel.Count == clothes, "no clothes moved");
                story.Check(SwapPlan.WouldDress(pawn, scene.OutfitStand), "the stand can lend the club again");
            }
        }

        /// <summary>
        /// Every stand def starts with the weapon rows on, a stand inheriting vanilla's default
        /// from another mod included, and that is all that moved: the same apparel is allowed, its
        /// rows are all on, and no weapon is allowed until a player ticks one. No Default Shelf
        /// Storage empties this default on purpose, so with it on the list nothing is allowed.
        /// </summary>
        [Test]
        [TestDescription("every kind of stand starts with the weapon rows on, and nothing else in its default moved")]
        internal IEnumerator EveryStandStartsWithTheWeaponRowsOn()
        {
            return Unstaged(Body);

            void Body(Story story)
            {
                List<ThingDef> stands = DefDatabase<ThingDef>.AllDefs
                    .Where(def => def.thingClass != null && typeof(Building_OutfitStand).IsAssignableFrom(def.thingClass))
                    .ToList();
                HashSet<ThingDef> expected = new HashSet<ThingDef>();
                bool emptied = ModActive("FrozenSnowFox.NoDefaultShelfStorage");
                if (!emptied)
                {
                    expected.UnionWith(DefDatabase<ThingDef>.AllDefs.Where(
                        def => def.IsWithinCategory(ThingCategoryDefOf.Apparel)
                               && !def.IsWithinCategory(ThingCategoryDefOf.ApparelUtility)
                               && !def.IsWithinCategory(ThingCategoryDefOf.Weapons)));
                }
                else
                {
                    story.Note("No Default Shelf Storage is on the list, which empties every default, "
                               + "so no stand allows anything");
                }
                List<SpecialThingFilterDef> apparelRows = DefDatabase<SpecialThingFilterDef>.AllDefs
                    .Where(special => special.parentCategory == ThingCategoryDefOf.Apparel)
                    .ToList();
                List<SpecialThingFilterDef> weaponRows = WeaponRows();

                story.Check(stands.Count > 0, "the game has " + stands.Count + " kinds of outfit stand to read");
                story.Check(weaponRows.Count >= 6, "the Weapons list has at least vanilla's six rows (" + weaponRows.Count + ")");
                foreach (ThingDef stand in stands)
                {
                    ThingFilter filter = stand.building.defaultStorageSettings.filter;
                    story.Check(weaponRows.All(row => filter.Allows(row)), stand.label + " starts with the weapon rows on");
                    story.Check(apparelRows.All(row => filter.Allows(row)), "and the apparel rows on");
                    story.Check(expected.SetEquals(filter.AllowedThingDefs), "and allows what it always did, no weapon among it");
                }
            }
        }

        [Test]
        [TestDescription("a hauler stocks a new stand with a weapon once that weapon's own row is ticked")]
        internal IEnumerator AHaulerStocksANewStandFromTheWeaponsOwnRow()
        {
            return Staged(ArmedOnly, ArmedOnlySays, (scene, story) => HaulerStocks(story, scene, Club));
        }

        /// <summary>
        /// A stand built under vanilla's default, as the README's steps for one say. Disallowing
        /// the Weapons category is what that default does, and it turns the weapon rows off with
        /// it. Every weapon matches some row, so a weapon's own row alone is refused.
        /// </summary>
        [Test]
        [TestDescription("a stand built before Shift Change lent weapons wants its weapon rows on first")]
        internal IEnumerator AnOlderStandWantsItsWeaponRowsOnFirst()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                ThingWithComps club = WeaponBesideADefaultStand(scene, Club);
                if (!story.Check(club != null, "a club lies at the stand's foot"))
                {
                    return;
                }
                ThingFilter filter = scene.OutfitStand.GetStoreSettings().filter;
                filter.SetAllow(ThingCategoryDefOf.Weapons, false);
                filter.SetAllow(club.def, true);
                story.Note("the stand's Weapons category is turned off, as vanilla's default had it, and the club's row ticked");
                story.Check(filter.Allows(club.def), "the club's own row reads ticked");
                story.Check(HaulAIUtility.HaulToStorageJob(pawn, club, false) == null, "and still nobody would haul a club to it");

                foreach (SpecialThingFilterDef row in WeaponRows())
                {
                    filter.SetAllow(row, true);
                }
                Job haul = HaulAIUtility.HaulToStorageJob(pawn, club, false);
                story.Check(haul?.def == JobDefOf.HaulToContainer, "with the weapon rows on, " + pawn.LabelShort + " would");
                club.Destroy();
            }
        }

        /// <summary>
        /// A deposit-only stand keeps its weapon through both legs, while the parka it was set up
        /// for moves. The colonist could wield the club beforehand, so the setting is what stopped
        /// it.
        /// </summary>
        [Test]
        [TestDescription("a deposit-only stand keeps its weapon while it takes and gives back a parka")]
        internal IEnumerator ADepositOnlyStandIssuesNoWeapon()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingDef parkaDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Parka");
                ThingWithComps weapon = scene.OutfitStand.HeldWeapon;
                if (!story.Check(parkaDef != null && weapon != null && WearOne(pawn, "Apparel_Parka"),
                                 name + " puts on a parka to leave at the stand"))
                {
                    return;
                }
                Apparel parka = pawn.apparel.WornApparel.First(a => a.def == parkaDef);
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == weapon, "any other stand would lend " + name + " the club");

                scene.Comp.ToggleRest();
                scene.Comp.SetDepositOnly(true);
                StorageSettings settings = scene.OutfitStand.GetStoreSettings();
                settings.filter.SetDisallowAll();
                settings.filter.SetAllow(parkaDef, allow: true);
                story.Note("the stand serves sleep, deposit-only, and accepts parkas alone");
                story.Check(scene.Comp.DepositOnly, "the stand is deposit-only");
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == null, "and now would lend no weapon");

                story.Check(SwapNow(story, scene), name + " leaves the parka at the stand");
                story.Check(parka.ParentHolder == scene.OutfitStand, "the parka is in the stand");
                story.Check(scene.OutfitStand.HeldWeapon == weapon, "the club stays on it");
                story.Check(pawn.equipment.Primary == null && scene.Comp.issuedWeapon == null,
                            "and " + name + "'s hand stays empty, with no weapon recorded as lent");

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(pawn.apparel.WornApparel.Contains(parka), "the parka is back on");
                story.Check(scene.OutfitStand.HeldWeapon == weapon && pawn.equipment.Primary == null,
                            "the club is still on the stand and the hand still empty");
                story.Check(!scene.Comp.OnShift, "and the stand's record is cleared");
            }
        }

        /// <summary>
        /// A colonist the wield check refuses leaves the club on the stand and still takes the
        /// duster, and changing back returns the duster with the club untouched.
        /// </summary>
        [Test]
        [TestDescription("a colonist who cannot wield the stand's weapon still gets the uniform")]
        internal IEnumerator AColonistWhoCannotWieldItStillGetsTheUniform()
        {
            return Staged(Body, StageKit.WeaponAndUniform);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps weapon = scene.OutfitStand.HeldWeapon;
                BackstoryDef pacifist = DefDatabase<BackstoryDef>.AllDefs.FirstOrDefault(
                    b => b.slot == BackstorySlot.Adulthood && (b.workDisables & WorkTags.Violent) != 0);
                if (!story.Check(weapon != null && pacifist != null, "the game has an adulthood that rules out violence"))
                {
                    return;
                }
                pawn.story.Adulthood = pacifist;
                pawn.Notify_DisabledWorkTypesChanged();
                story.Note(name + " grew up " + pacifist.title + ", which rules out violence");
                story.Check(pawn.WorkTagIsDisabled(WorkTags.Violent), name + " cannot do violence");
                story.Check(!SwapPlan.CanWield(pawn, weapon) && SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == null,
                            "so the stand would not lend them the club");
                story.Check(SwapPlan.WouldDress(pawn, scene.OutfitStand), "and still sends them for the duster");

                story.Check(SwapNow(story, scene), name + " changes at the stand");
                Apparel uniform = scene.Comp.IssuedUniformForReading.FirstOrDefault();
                story.Check(uniform != null, name + " is wearing the duster");
                story.Check(scene.OutfitStand.HeldWeapon == weapon && pawn.equipment.Primary == null,
                            "the club stayed on the stand and the hand empty");
                story.Check(scene.Comp.issuedWeapon == null, "with no weapon recorded as lent");
                if (uniform == null)
                {
                    return;
                }

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(uniform.ParentHolder == scene.OutfitStand, "the duster is back on the stand");
                story.Check(scene.OutfitStand.HeldWeapon == weapon, "beside the club");
                story.Check(!scene.Comp.OnShift, "and the stand's record is cleared");
            }
        }

        /// <summary>
        /// A colonist who cannot shoot leaves a gun on the stand and still takes the duster, and
        /// changing back returns the duster with the gun untouched. The controls are the same
        /// colonist able to shoot, who may wield the gun, and the club, which they are still lent.
        /// No backstory in the base game disables shooting alone, so one is built here, and the
        /// colonist's own is put back at the end because no database holds the built one.
        /// </summary>
        [Test]
        [TestDescription("a colonist who cannot shoot leaves a gun on the stand and still gets the uniform")]
        internal IEnumerator AColonistWhoCannotShootLeavesTheGun()
        {
            return Staged(ArmedWithUniform, ArmedWithUniformSays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps club = scene.OutfitStand.HeldWeapon;
                ThingWithComps gun = MakeWeapon(Autopistol);
                if (!story.Check(club != null && gun != null, "the stand holds a club and a duster, and an autopistol is to hand"))
                {
                    gun?.Destroy();
                    return;
                }
                story.Check(SwapPlan.CanWield(pawn, gun), name + " may wield an autopistol while they can shoot");

                BackstoryDef adulthood = pawn.story.Adulthood;
                pawn.story.Adulthood = new BackstoryDef
                {
                    defName = "ShiftChange_TestCannotShoot",
                    title = "test non-shooter",
                    titleShort = "non-shooter",
                    baseDesc = CannotShootDesc,
                    description = CannotShootDesc,
                    slot = BackstorySlot.Adulthood,
                    workDisables = WorkTags.Shooting,
                };
                pawn.Notify_DisabledWorkTypesChanged();
                try
                {
                    story.Check(pawn.WorkTagIsDisabled(WorkTags.Shooting) && !pawn.WorkTagIsDisabled(WorkTags.Violent),
                                "given a backstory that rules out shooting, " + name + " cannot shoot and can still fight");
                    story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == club,
                                "so the stand would still lend them the club, which is melee");
                    scene.OutfitStand.RemoveHeldWeapon(club);
                    club.Destroy();
                    if (!story.Check(scene.OutfitStand.TryAddHeldWeapon(gun), "the stand's club is swapped for the autopistol"))
                    {
                        gun.Destroy();
                        return;
                    }
                    story.Check(!SwapPlan.CanWield(pawn, gun) && SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == null,
                                "but would not lend them the autopistol");
                    story.Check(SwapPlan.WouldDress(pawn, scene.OutfitStand), "and still sends them for the duster");

                    if (!story.Check(SwapNow(story, scene), name + " changes at the stand"))
                    {
                        return;
                    }
                    Apparel uniform = scene.Comp.IssuedUniformForReading.FirstOrDefault();
                    story.Check(scene.OutfitStand.HeldWeapon == gun && pawn.equipment.Primary == null,
                                "the autopistol stayed on the stand and the hand empty");
                    story.Check(scene.Comp.issuedWeapon == null, "with no weapon recorded as lent");
                    if (!story.Check(uniform != null, name + " is wearing the duster"))
                    {
                        return;
                    }

                    story.Check(SwapNow(story, scene), name + " changes back");
                    story.Check(uniform.ParentHolder == scene.OutfitStand, "the duster is back on the stand");
                    story.Check(scene.OutfitStand.HeldWeapon == gun, "beside the autopistol");
                    Closed(story, scene);
                }
                finally
                {
                    pawn.story.Adulthood = adulthood;
                    pawn.Notify_DisabledWorkTypesChanged();
                }
            }
        }

        /// <summary>The text of the backstory that stops a colonist shooting, for anything that
        /// shows the colonist while it is in place.</summary>
        internal const string CannotShootDesc = "Set by a Shift Change test: cannot shoot, can still fight.";

        /// <summary>
        /// A persona weapon stays on the stand unless it is bonded to this colonist, since equipping
        /// one bonds it for good and a stand cannot ask. The driver runs as well as the plan: a
        /// change started anyway must not put it in a hand. One that can never bond is an ordinary
        /// weapon, which is the half a plain "has the comp" reading would get wrong. Once bonded to
        /// the colonist, it goes out and comes back through the driver with the bond intact, a
        /// known gap where Royalty is not installed (<see cref="PersonaCanBeEquipped"/>).
        /// </summary>
        [Test]
        [TestDescription("a persona weapon goes only to the colonist it is bonded to")]
        internal IEnumerator APersonaWeaponGoesOnlyToItsBondedColonist()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps weapon = scene.OutfitStand.HeldWeapon;
                if (!story.Check(weapon != null, "the stand holds the club"))
                {
                    return;
                }
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == weapon, "as a plain club the stand would lend it");

                CompBladelinkWeapon persona = MakePersona(weapon);
                story.Note("the club becomes a persona weapon");
                story.Check(weapon.TryGetComp<CompBladelinkWeapon>() == persona, "the club is a persona weapon");
                story.Check(persona.Biocodable && persona.CodedPawn == null, "bonded to nobody");
                story.Check(!SwapPlan.CanWield(pawn, weapon), "so the stand would not lend it to " + name);
                story.Check(!SwapPlan.WouldDress(pawn, scene.OutfitStand), "and sends nobody to a stand holding only that");

                story.Check(SwapNow(story, scene), "a change started anyway runs to its end");
                story.Check(scene.OutfitStand.HeldWeapon == weapon, "the persona club stays on the stand");
                story.Check(pawn.equipment.Primary == null && persona.CodedPawn == null, "and " + name + "'s hand stays empty, nothing bonded");
                story.Check(!scene.Comp.OnShift, "with no loan opened");

                WeaponTraitDef neverBond = new WeaponTraitDef { neverBond = true };
                persona.TraitsListForReading.Add(neverBond);
                story.Check(SwapPlan.CanWield(pawn, weapon), "one with a trait that forbids bonding is lent like any other");
                persona.TraitsListForReading.Remove(neverBond);

                persona.CodeFor(pawn);
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == weapon,
                            "and one already bonded to " + name + " is theirs to take");
                if (!PersonaCanBeEquipped(story, "a persona club bonded to its borrower goes out and comes back"))
                {
                    persona.UnCode();
                    return;
                }
                story.Check(SwapNow(story, scene), name + " changes at the stand");
                story.Check(pawn.equipment.Primary == weapon && persona.CodedPawn == pawn,
                            "the persona club is in their hand, bonded to them");
                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(scene.OutfitStand.HeldWeapon == weapon && pawn.equipment.Primary == null,
                            "it is back on the stand, their hand empty again");
                story.Check(persona.CodedPawn == pawn && pawn.equipment.bondedWeapon == weapon, "and still bonded to " + name);
                Closed(story, scene);
                persona.UnCode();
            }
        }

        /// <summary>
        /// A colonist's own weapon rides in their pack while the stand's is in their hand, and both
        /// come back as the same instances, alongside a uniform. The stand's slot, the pack and the
        /// hand are each checked on both legs.
        /// </summary>
        [Test]
        [TestDescription("a colonist's own weapon rides in their pack while they hold the stand's, and both come back")]
        internal IEnumerator TheirOwnWeaponRidesInTheirPack()
        {
            return Staged(ArmedWithUniform, ArmedWithUniformSays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps issued = scene.OutfitStand.HeldWeapon;
                ThingWithComps own = Arm(pawn, Autopistol);
                if (!story.Check(issued != null && own != null, name + " holds an autopistol of their own"))
                {
                    return;
                }
                ThingOwner pack = pawn.inventory.innerContainer;
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == issued, "the stand would lend them its club");

                story.Check(SwapNow(story, scene), name + " changes at the stand");
                Apparel uniform = scene.Comp.IssuedUniformForReading.FirstOrDefault();
                story.Check(pawn.equipment.Primary == issued && scene.OutfitStand.HeldWeapon == null,
                            "the club is in " + name + "'s hand, off the stand");
                story.Check(pack.Contains(own) && !own.Spawned, "their autopistol is in their pack, not on the ground");
                story.Check(scene.Comp.issuedWeapon == issued && scene.Comp.ownWeapon == own,
                            "the stand records both weapons");
                if (!story.Check(uniform != null, "and " + name + " is wearing the duster"))
                {
                    return;
                }

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(pawn.equipment.Primary == own && !pack.Contains(own), "their autopistol is back in hand, out of the pack");
                story.Check(scene.OutfitStand.HeldWeapon == issued, "the same club is back on the stand");
                story.Check(uniform.ParentHolder == scene.OutfitStand, "and the duster with it");
                story.Check(!scene.Comp.OnShift && scene.Comp.issuedWeapon == null && scene.Comp.ownWeapon == null,
                            "the stand's record is cleared, both weapons included");
            }
        }

        /// <summary>
        /// Three hands the stand's club does not replace, each against an ordinary hand that gives
        /// way. The driver runs on the one a drop would destroy, since there the check is all that
        /// keeps the weapon in the world, and the duster still goes out and comes back.
        /// </summary>
        [Test]
        [TestDescription("a weapon a drop would destroy, one that stacks, or one like the stand's stays in the colonist's hand")]
        internal IEnumerator SomeWeaponsStayInTheHand()
        {
            return Staged(ArmedWithUniform, ArmedWithUniformSays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps stock = scene.OutfitStand.HeldWeapon;
                ThingWithComps plain = Arm(pawn, Autopistol);
                if (!story.Check(stock != null && plain != null, name + " holds an autopistol of their own"))
                {
                    return;
                }
                story.Check(SwapPlan.HandAllows(pawn, stock), "an ordinary autopistol gives way to the club");
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == stock, "and the stand would lend it");
                pawn.equipment.DestroyAllEquipment();

                ThingWithComps twin = Arm(pawn, Club);
                story.Check(twin != null && !SwapPlan.HandAllows(pawn, stock), "a club like the stand's stays in hand");
                pawn.equipment.DestroyAllEquipment();

                ThingWithComps stack = Arm(pawn, "WoodLog");
                story.Check(stack != null && !SwapPlan.HandAllows(pawn, stock), "so does a wood log, which stacks");
                story.Check(stack != null && !SwapPlan.CanWield(pawn, stack), "and the stand would not lend one either");
                pawn.equipment.DestroyAllEquipment();

                ThingWithComps fragile = Arm(pawn, "Gun_ChargeBlasterHeavy");
                if (!story.Check(fragile != null && fragile.def.destroyOnDrop,
                                 name + " holds a heavy charge blaster, which a drop destroys"))
                {
                    return;
                }
                story.Check(!SwapPlan.HandAllows(pawn, stock), "it stays in hand");

                story.Check(SwapNow(story, scene), name + " changes at the stand");
                Apparel uniform = scene.Comp.IssuedUniformForReading.FirstOrDefault();
                story.Check(uniform != null, name + " is wearing the duster");
                story.Check(pawn.equipment.Primary == fragile && !fragile.Destroyed, "the blaster is whole and in hand");
                story.Check(scene.OutfitStand.HeldWeapon == stock, "the club stayed on the stand");
                story.Check(scene.Comp.issuedWeapon == null && scene.Comp.ownWeapon == null, "with no weapon recorded");

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(pawn.equipment.Primary == fragile, "the blaster never left their hand");
                story.Check(!scene.Comp.OnShift, "and the stand's record is cleared");
            }
        }

        /// <summary>
        /// A shared stand arms no slave and gives a Brawler no gun, and one assigned to the colonist
        /// does both, the assignment being the player's answer. Each refusal sits beside a control
        /// that differs only in what the check keys on.
        /// </summary>
        [Test]
        [TestDescription("a shared stand arms no slave and no Brawler with a gun, and a stand assigned to them does")]
        internal IEnumerator ASharedStandHoldsBackForTheAssignment()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps club = scene.OutfitStand.HeldWeapon;
                ThingWithComps gun = MakeWeapon(Autopistol);
                CompAssignableToPawn_ShiftStand owners = scene.OutfitStand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                if (!story.Check(club != null && gun != null && owners != null, "the stand can be assigned, and an autopistol is to hand"))
                {
                    gun?.Destroy();
                    return;
                }
                story.Check(scene.Comp.IsPool, "the stand is shared");
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == club, "and would lend " + name + " its club");

                pawn.story.traits.GainTrait(new Trait(TraitDefOf.Brawler));
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == club,
                            "made a Brawler, " + name + " would still get the club, which is melee");
                scene.OutfitStand.RemoveHeldWeapon(club);
                club.Destroy();
                if (!story.Check(scene.OutfitStand.TryAddHeldWeapon(gun), "the stand's club is swapped for an autopistol"))
                {
                    gun.Destroy();
                    return;
                }
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == null, "a shared stand keeps its gun from a Brawler");
                story.Check(!SwapPlan.WouldDress(pawn, scene.OutfitStand), "so it sends nobody");
                owners.TryAssignPawn(pawn);
                story.Check(scene.Comp.IsAssignedTo(pawn), "assigned to " + name);
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == gun, "the stand lends them the gun");
                owners.TryUnassignPawn(pawn);
                pawn.story.traits.RemoveTrait(pawn.story.traits.GetTrait(TraitDefOf.Brawler));
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == gun,
                            "shared again, it lends the gun to " + name + " once they are no Brawler");

                pawn.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Slave);
                story.Check(pawn.IsSlave, name + " is made a slave");
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == null, "a shared stand arms no slave");
                owners.TryAssignPawn(pawn);
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == gun, "and one assigned to them does");
            }
        }

        /// <summary>
        /// A persona weapon in the colonist's hand gives way to the stand's like any other, and a
        /// jealous one waits for the stand to be assigned to them, since taking up the club would
        /// cost them mood. Jealousy follows the bonded weapon wherever it lies, so one in the pack
        /// holds the club back too, and the stand lends the persona weapon itself without asking
        /// only to an empty hand, since the change back would put the hand's weapon in it again.
        /// The game tells a persona weapon nothing while Royalty is not active, so there a staged
        /// jealous trait costs nothing and the rest is a known gap. With Royalty the test takes
        /// its own Jealous trait, runs the assigned loan through the driver and checks the thought
        /// the game gives for it. A colonist with no mood, a trait from another mod that answers
        /// the same hook, and one that only inherits it are checked as well, and each refusal sits
        /// beside the control without the trait.
        /// </summary>
        [Test]
        [TestDescription("a persona weapon gives way to the stand's, and a jealous one waits for the stand to be assigned")]
        internal IEnumerator AJealousPersonaWeaponWaitsForTheAssignment()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps club = scene.OutfitStand.HeldWeapon;
                ThingWithComps own = Arm(pawn, Autopistol);
                CompAssignableToPawn_ShiftStand owners = scene.OutfitStand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                if (!story.Check(club != null && own != null && owners != null,
                                 "the stand can be assigned, and " + name + " holds an autopistol of their own"))
                {
                    return;
                }
                ThingOwner pack = pawn.inventory.innerContainer;

                // Equipped again only past the Royalty gate below, as MakePersona says.
                CompBladelinkWeapon persona = MakePersona(own);
                persona.CodeFor(pawn);
                story.Check(pawn.equipment.bondedWeapon == own, "the autopistol becomes a persona weapon bonded to " + name);
                story.Check(SwapPlan.HandAllows(pawn, club), "it gives way to the club like any other");
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == club, "and the shared stand would lend the club");

                if (!ModsConfig.RoyaltyActive)
                {
                    WeaponTraitDef staged = new WeaponTraitDef { workerClass = typeof(WeaponTraitWorker_Jealous) };
                    persona.TraitsListForReading.Add(staged);
                    story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == club,
                                "with Royalty not active a jealous trait costs nothing, so the shared stand still lends the club");
                    persona.TraitsListForReading.Remove(staged);
                    story.KnownGap(false, "a jealous persona weapon waits for the stand to be assigned",
                                   "Royalty is not active on this mod list");
                    persona.UnCode();
                    return;
                }
                WeaponTraitDef jealous = DefDatabase<WeaponTraitDef>.GetNamedSilentFail("Jealous");
                if (!story.Check(jealous != null && SwapPlan.AnswersAnotherWeapon(jealous.workerClass),
                                 "Royalty's Jealous trait is loaded, and its worker answers another weapon in hand"))
                {
                    persona.UnCode();
                    return;
                }
                persona.TraitsListForReading.Add(jealous);
                story.Note("the autopistol turns jealous");
                story.Check(SwapPlan.AngersJealousPersona(pawn, club), "taking up the club would anger it");
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == null, "so the shared stand keeps the club");
                story.Check(!SwapPlan.WouldDress(pawn, scene.OutfitStand), "and sends nobody");
                Need_Mood mood = pawn.needs.mood;
                pawn.needs.mood = null;
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == club,
                            "a colonist with no mood would get it, since Royalty's trait gives them no thought");
                pawn.needs.mood = mood;
                owners.TryAssignPawn(pawn);
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == club,
                            "assigned to " + name + ", the stand lends the club");

                story.Check(SwapNow(story, scene), name + " changes at the stand");
                story.Check(pawn.equipment.Primary == club && pack.Contains(own),
                            "the club is in hand and the persona autopistol in the pack");
                story.Check(persona.CodedPawn == pawn && pawn.equipment.bondedWeapon == own, "still bonded to " + name);
                List<Thought_Memory> memories = pawn.needs.mood.thoughts.memories.Memories;
                story.Check(memories.Any(m => m.def == ThoughtDefOf.JealousRage && !m.ShouldDiscard),
                            "the game gives " + name + " the autopistol's jealousy for the club");
                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(pawn.equipment.Primary == own && scene.OutfitStand.HeldWeapon == club,
                            "the persona autopistol is back in hand, the club on the stand");
                story.Check(memories.All(m => m.def != ThoughtDefOf.JealousRage || m.ShouldDiscard),
                            "and the jealousy lifts with it back in hand");
                owners.TryUnassignPawn(pawn);

                story.Check(WeaponHands.ToPack(pawn, own), "the autopistol goes into " + name + "'s pack");
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == null,
                            "with the jealous one in the pack, the shared stand still keeps its club");
                persona.TraitsListForReading.Remove(jealous);
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == club,
                            "without the trait, the same pack and empty hand get the club");
                WeaponTraitDef answering = new WeaponTraitDef { workerClass = typeof(AnswersAnotherWeaponWorker) };
                persona.TraitsListForReading.Add(answering);
                pawn.needs.mood = null;
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == null,
                            "a trait from another mod whose worker answers another weapon in hand keeps it back, mood or none");
                pawn.needs.mood = mood;
                persona.TraitsListForReading.Remove(answering);
                WeaponTraitDef inheriting = new WeaponTraitDef { workerClass = typeof(WeaponTraitWorker_PsyfocusOnKill) };
                persona.TraitsListForReading.Add(inheriting);
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == club,
                            "one whose worker only inherits that hook, as Royalty's psyfocus-on-kill does, lends it");
                persona.TraitsListForReading.Remove(inheriting);
                persona.TraitsListForReading.Add(jealous);
                pack.Remove(own);
                scene.OutfitStand.RemoveHeldWeapon(club);
                club.Destroy();
                if (!story.Check(scene.OutfitStand.TryAddHeldWeapon(own), "the persona autopistol goes on the stand in the club's place"))
                {
                    persona.UnCode();
                    own.Destroy();
                    return;
                }
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == own,
                            "the shared stand would lend it to " + name + "'s empty hand, which angers nothing");
                ThingWithComps spare = Arm(pawn, Club);
                story.Check(spare != null && SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == null,
                            "holding a club, which the change back would hand them again, " + name + " waits for the assignment");
                persona.TraitsListForReading.Remove(jealous);
                story.Check(SwapPlan.WeaponToIssue(pawn, scene.OutfitStand) == own,
                            "without the trait, the shared stand lends it anyway");
                persona.UnCode();
            }
        }

        /// <summary>
        /// A loan that moved no clothes leaves the wardrobe running, the outfit check and a recolor
        /// nobody ordered both, since all the colonist wears is their own. The control differs only
        /// in the clothes half of the stand's record.
        /// </summary>
        [Test]
        [TestDescription("a loan that moved no clothes leaves the colonist free to change their own")]
        internal IEnumerator ALoanThatMovedNoClothesLeavesTheWardrobeRunning()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                Apparel shirt = Worn(pawn, "Apparel_BasicShirt");
                if (!story.Check(shirt != null, pawn.LabelShort + " wears a shirt"))
                {
                    return;
                }
                JobDriver_RecolorApparel recolor = new JobDriver_RecolorApparel
                {
                    pawn = pawn,
                    job = JobMaker.MakeJob(JobDefOf.Wait),
                };
                Job given = null;
                bool reserved = true;
                story.Check(CompShiftStand.OnShiftStandFor(pawn) == scene.Comp && !scene.Comp.ClothesMoved,
                            "the loan moved no clothes");
                story.Check(Patch_OptimizeApparelOnShift.Prefix(pawn, ref given), "the outfit check runs");
                story.Check(Patch_RecolorExecutionGuard.Prefix(recolor, ref reserved), "and a recolor is let through");

                scene.Comp.issuedUniform.Add(shirt);
                story.Check(!Patch_OptimizeApparelOnShift.Prefix(pawn, ref given),
                            "with the shirt counted as the stand's, the outfit check waits");
                story.Check(!Patch_RecolorExecutionGuard.Prefix(recolor, ref reserved), "and the recolor is refused");
                scene.Comp.issuedUniform.Remove(shirt);

                story.Check(SwapNow(story, scene), pawn.LabelShort + " changes back");
                Closed(story, scene);
            }
        }

        /// <summary>
        /// The slot is empty while the club is out, so something else can fill it. The returning
        /// club goes down beside the stand, unforbidden, and the newcomer keeps the slot.
        /// </summary>
        [Test]
        [TestDescription("a weapon coming back to a stand that has filled its slot goes down beside it")]
        internal IEnumerator AWeaponComingBackToARefilledSlotGoesDown()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps weapon = scene.OutfitStand.HeldWeapon;
                if (!story.Check(weapon != null, "the stand holds the club"))
                {
                    return;
                }
                story.Check(SwapNow(story, scene), name + " changes at the stand");
                story.Check(pawn.equipment.Primary == weapon, "the club is in " + name + "'s hand");

                ThingWithComps newcomer = MakeWeapon();
                story.Check(scene.OutfitStand.TryAddHeldWeapon(newcomer), "a second club fills the stand's slot");

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(scene.OutfitStand.HeldWeapon == newcomer, "the second club keeps the slot");
                story.Check(pawn.equipment.Primary == null, name + "'s hand is empty");
                if (!story.Check(weapon.Spawned, "the club " + name + " brought back is on the ground"))
                {
                    return;
                }
                story.Check(weapon.Position.InHorDistOf(scene.OutfitStand.Position, 3f), "beside the stand");
                story.Check(!weapon.IsForbidden(Faction.OfPlayer), "and not forbidden");
                story.Check(!scene.Comp.OnShift, "the stand's record is cleared");
            }
        }

        /// <summary>
        /// A loan with no uniform wears nothing, so the pane names the weapon alone, and that line
        /// goes once the colonist no longer has it, with the stand's record still open.
        /// </summary>
        [Test]
        [TestDescription("the stand's inspect pane names a weapon out on loan with no outfit")]
        internal IEnumerator TheInspectPaneNamesAWeaponOut()
        {
            return Staged(ArmedOnly, ArmedOnlySays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                string name = scene.Colonist.LabelShort;
                string wearing = "ShiftChange.InspectOnShift".Translate(name);
                string lent = "ShiftChange.InspectWeaponOut".Translate(name, issued.LabelNoParenthesis);
                scene.Comp.ToggleWork(WorkTypeDefOf.Hunting);
                story.Check(scene.Comp.ServesSomething, "the stand serves hunting");
                story.Check(Inspect(scene).Contains(lent), "its pane says: " + lent);
                story.Check(!Inspect(scene).Contains(wearing), "and does not say: " + wearing);

                issued.Destroy();
                story.Note("the club is destroyed in " + name + "'s hand");
                story.Check(scene.Comp.OnShift, "the stand's record stays open");
                story.Check(!Inspect(scene).Contains(lent), "and the club's line goes");
            }
        }

        [Test]
        [TestDescription("the stand's inspect pane gives an outfit and a weapon out on loan a line each")]
        internal IEnumerator TheInspectPaneGivesAnOutfitAndAWeaponALineEach()
        {
            return Staged(ArmedWithUniform, ArmedWithUniformSays, Body);

            void Body(Scene scene, Story story)
            {
                if (!Lend(story, scene, out ThingWithComps issued, out ThingWithComps own))
                {
                    return;
                }
                string name = scene.Colonist.LabelShort;
                string wearing = "ShiftChange.InspectOnShift".Translate(name);
                string lent = "ShiftChange.InspectWeaponOut".Translate(name, issued.LabelNoParenthesis);
                scene.Comp.ToggleWork(WorkTypeDefOf.Hunting);
                story.Check(scene.Comp.ServesSomething, "the stand serves hunting");
                story.Check(Inspect(scene).Contains(wearing), "its pane says: " + wearing);
                story.Check(Inspect(scene).Contains(lent), "and: " + lent);
            }
        }

        /// <summary>
        /// A trait worker that answers its colonist taking up another weapon, as one from another
        /// mod might. Only its class is read; nothing instantiates it.
        /// </summary>
        internal class AnswersAnotherWeaponWorker : WeaponTraitWorker
        {
            public override void Notify_OtherWeaponWielded(CompBladelinkWeapon weapon)
            {
            }
        }
    }
}
