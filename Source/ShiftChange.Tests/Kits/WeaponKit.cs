using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// What the tests of a stand that lends a weapon share (<see cref="LendingAWeapon"/>,
    /// <see cref="EndingALoan"/>, <see cref="DraftingWithALoan"/>,
    /// <see cref="DownedWithALoan"/> and the tool groups): the
    /// weapons, the scenes, the loan every exit starts from, and the checks of where both weapons
    /// end up. Each of those checks names the hand, the pack and the ground, because a move that
    /// loses a weapon loses it silently: nothing in the game notices a thing held by nothing.
    /// </summary>
    internal static class WeaponKit
    {
        internal const string Club = "MeleeWeapon_Club";
        internal const string Autopistol = "Gun_Autopistol";
        internal const string Revolver = "Gun_Revolver";

        /// <summary>The stand <see cref="StageKit.WeaponOnly"/> stocks.</summary>
        internal const string ClubOnlyStand = "a stand holding a club and nothing else";

        /// <summary>The stand <see cref="StageKit.WeaponAndUniform"/> stocks.</summary>
        internal const string ClubAndDusterStand = "a stand holding a duster and a club";

        /// <summary>How both weapon kits leave the colonist, after the colonist's name.</summary>
        internal const string BesideItEmptyHanded = " beside it in a shirt and trousers, empty-handed";

        /// <summary>What <see cref="ArmedOnly"/> sets up.</summary>
        internal const string ArmedOnlySays = ClubOnlyStand + ", and a colonist who can hunt" + BesideItEmptyHanded;

        /// <summary>What <see cref="ArmedWithUniform"/> sets up.</summary>
        internal const string ArmedWithUniformSays =
            ClubAndDusterStand + ", and a colonist who can hunt" + BesideItEmptyHanded;

        /// <summary>What <see cref="ArmedOnSecondMap"/> sets up.</summary>
        internal const string ArmedOnSecondMapSays = "on a second map, " + ArmedOnlySays;

        /// <summary>What <see cref="ArmedInARoom"/> sets up.</summary>
        internal const string ArmedInARoomSays = "in a walled and roofed room, " + ArmedOnlySays;

        /// <summary>
        /// A weapon held by nothing, a club by default. Melee because a backstory can disable
        /// shooting without disabling violence, so a colonist staged able to hunt may still refuse
        /// a gun from a stand. <see cref="Arm"/> asks no such question. Null when the def is
        /// missing.
        /// </summary>
        internal static ThingWithComps MakeWeapon(string defName = Club)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            return def == null
                ? null
                : (ThingWithComps)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
        }

        /// <summary>
        /// A new weapon straight into the colonist's empty hand, as a colonist already armed holds
        /// one. Null when the def is missing.
        /// </summary>
        internal static ThingWithComps Arm(Pawn pawn, string defName)
        {
            ThingWithComps weapon = MakeWeapon(defName);
            if (weapon != null)
            {
                pawn.equipment.AddEquipment(weapon);
            }
            return weapon;
        }

        /// <summary>The special rows at the top of a stand's Weapons list.</summary>
        internal static List<SpecialThingFilterDef> WeaponRows()
        {
            return DefDatabase<SpecialThingFilterDef>.AllDefs
                .Where(special => special.parentCategory == ThingCategoryDefOf.Weapons)
                .ToList();
        }

        /// <summary>The stand's inspect text, which is null for a stand that serves nothing, as a
        /// staged stand does until a work type is ticked.</summary>
        internal static string Inspect(Scene scene)
        {
            return scene.Comp.CompInspectStringExtra() ?? "";
        }

        /// <summary>
        /// Empties the scene's stand, puts it on a new stand's settings and lays a weapon of this
        /// def on the stand's interaction cell, under the colonist, so that neither leg of a haul
        /// waits on a path. Null when the def is missing.
        /// </summary>
        internal static ThingWithComps WeaponBesideADefaultStand(Scene scene, string defName)
        {
            Building_OutfitStand stand = scene.OutfitStand;
            ThingWithComps onStand = stand.HeldWeapon;
            if (onStand != null)
            {
                stand.RemoveHeldWeapon(onStand);
                onStand.Destroy();
            }
            stand.GetStoreSettings().CopyFrom(stand.def.building.defaultStorageSettings);

            ThingWithComps weapon = MakeWeapon(defName);
            if (weapon != null)
            {
                GenSpawn.Spawn(weapon, stand.InteractionCell, scene.Map);
                weapon.SetForbidden(false, warnOnFail: false);
            }
            return weapon;
        }

        /// <summary>
        /// Stocking a new stand as the README tells a player to: it wants no weapon until one is
        /// ticked, and then the weapon's own row is enough for a hauler to bring one.
        /// </summary>
        internal static void HaulerStocks(Story story, Scene scene, string defName)
        {
            Pawn pawn = scene.Colonist;
            string name = pawn.LabelShort;
            ThingWithComps weapon = WeaponBesideADefaultStand(scene, defName);
            if (!story.Check(weapon != null, "the game has " + defName))
            {
                return;
            }
            string label = weapon.def.label;
            story.Note("the stand is emptied and put on a new stand's settings, and a " + label + " lies at its foot");
            story.Check(HaulAIUtility.HaulToStorageJob(pawn, weapon, false) == null,
                        "a new stand wants no weapon, so " + name + " would not haul the " + label + " to it");

            scene.OutfitStand.GetStoreSettings().filter.SetAllow(weapon.def, true);
            Job haul = HaulAIUtility.HaulToStorageJob(pawn, weapon, false);
            if (!story.Check(haul?.def == JobDefOf.HaulToContainer,
                             "with the " + label + "'s own row ticked, " + name + " would haul it there"))
            {
                return;
            }
            pawn.jobs.TryTakeOrderedJob(haul, JobTag.Misc);
            JobDriver driver = pawn.jobs.curDriver;
            ClockRun run = GameClock.Step(() => pawn.jobs.curDriver != driver, 2000, scene.Hold);
            story.Check(scene.OutfitStand.HeldWeapon == weapon,
                        "ordered to, " + name + " hauls it, and " + run.Ticks + " ticks later the " + label
                        + " is on the stand");
        }

        /// <summary>
        /// True where Royalty is installed. Elsewhere it notes <paramref name="what"/> as a known
        /// gap and returns false, since the comp <see cref="MakePersona"/> adds logs an error
        /// whenever it is equipped there. A test asks before it puts a persona weapon in a hand.
        /// </summary>
        internal static bool PersonaCanBeEquipped(Story story, string what)
        {
            if (ModLister.RoyaltyInstalled)
            {
                return true;
            }
            story.KnownGap(false, what,
                           "Royalty is not installed, and the game logs an error whenever a persona weapon is equipped without it");
            return false;
        }

        /// <summary>
        /// Makes this weapon a persona weapon bonded to nobody, on the instance, since persona
        /// weapons are Royalty's and a scene stays on Core defs. The comp's equip hook logs an
        /// error where Royalty is not installed, so a test that puts the weapon in a hand after
        /// this asks <see cref="PersonaCanBeEquipped"/> first.
        /// </summary>
        internal static CompBladelinkWeapon MakePersona(ThingWithComps weapon)
        {
            CompBladelinkWeapon persona = new CompBladelinkWeapon { parent = weapon };
            persona.Initialize(new CompProperties_Biocodable
            {
                compClass = typeof(CompBladelinkWeapon),
                biocodeOnEquip = true,
            });
            weapon.AllComps.Add(persona);
            return persona;
        }

        /// <summary>
        /// The wait the driver plans for the next change: starts it, reads the driver, ends it.
        /// An interrupted change moves nothing, since the transfer is its last step.
        /// </summary>
        internal static int PlannedWait(Scene scene)
        {
            Pawn pawn = scene.Colonist;
            Job swap = JobMaker.MakeJob(ShiftChangeDefOf.ShiftChange_SwapAtStand, scene.OutfitStand);
            pawn.jobs.StartJob(swap, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null, JobTag.ChangingApparel);
            int wait = (pawn.jobs.curDriver as JobDriver_SwapAtStand)?.duration ?? -1;
            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            return wait;
        }

        /// <summary>
        /// A stand holding only a club, and a colonist able to wield it: one staged able to hunt,
        /// which is tagged Violent, so no rolled backstory refuses every weapon.
        /// </summary>
        internal static Scene ArmedOnly(Map map, CellRect pad)
        {
            return Stage(map, pad, StageKit.WeaponOnly, capableOf: WorkTypeDefOf.Hunting);
        }

        /// <summary>
        /// <see cref="ArmedOnly"/> in a walled and roofed room, which a room stand covers: in the
        /// open the room touches the map edge, and a room stand covers nothing there.
        /// </summary>
        internal static Scene ArmedInARoom(Map map, CellRect pad)
        {
            return Stage(map, pad, StageKit.WeaponOnly, enclose: true, capableOf: WorkTypeDefOf.Hunting);
        }

        /// <summary><see cref="ArmedOnly"/> with a duster on the stand as well.</summary>
        internal static Scene ArmedWithUniform(Map map, CellRect pad)
        {
            return Stage(map, pad, StageKit.WeaponAndUniform, capableOf: WorkTypeDefOf.Hunting);
        }

        /// <summary>
        /// <see cref="ArmedOnly"/> on a <see cref="SceneKit.SecondMap"/>, floored first: that
        /// map's generator lays no ground a colonist can stand on, and the change steps the clock.
        /// </summary>
        internal static Scene ArmedOnSecondMap(Map map, CellRect pad)
        {
            return OnSecondMap(map, (away, awayPad) =>
            {
                foreach (IntVec3 cell in awayPad)
                {
                    away.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                }
                return ArmedOnly(away, awayPad);
            });
        }

        /// <summary>
        /// The stand's club out on loan, with the colonist's autopistol in their pack: where every
        /// test of how a loan ends starts. False, the test failed with why, when it did not get
        /// there.
        /// </summary>
        internal static bool Lend(Story story, Scene scene, out ThingWithComps issued, out ThingWithComps own)
        {
            Pawn pawn = scene.Colonist;
            string name = pawn.LabelShort;
            issued = scene.OutfitStand.HeldWeapon;
            own = Arm(pawn, Autopistol);
            if (!story.Check(issued != null && own != null,
                             "the stand holds a club, and " + name + " an autopistol of their own"))
            {
                return false;
            }
            return story.Check(StoryFixture.SwapNow(story, scene), name + " changes at the stand")
                   & story.Check(pawn.equipment.Primary == issued, "the stand's club is in " + name + "'s hand")
                   & story.Check(pawn.inventory.innerContainer.Contains(own), "and their autopistol in their pack");
        }

        /// <summary>
        /// A loan closed with no trip back to the stand: the colonist's autopistol in hand, the
        /// stand's club in their pack, neither on the ground.
        /// </summary>
        internal static bool Settled(Story story, Scene scene, ThingWithComps issued, ThingWithComps own)
        {
            Pawn pawn = scene.Colonist;
            ThingOwner pack = pawn.inventory.innerContainer;
            return story.Check(pawn.equipment.Primary == own, "their own autopistol is back in hand")
                   & story.Check(!pack.Contains(own), "and out of their pack")
                   & story.Check(pack.Contains(issued), "the stand's club is in their pack")
                   & story.Check(!issued.Spawned && !own.Spawned, "with neither left on the ground")
                   & Closed(story, scene);
        }

        /// <summary>Nothing moved: the stand's club in hand, the colonist's autopistol in the pack.</summary>
        internal static bool Untouched(Story story, Scene scene, ThingWithComps issued, ThingWithComps own)
        {
            Pawn pawn = scene.Colonist;
            return story.Check(pawn.equipment.Primary == issued, "the club stays in hand")
                   & story.Check(pawn.inventory.innerContainer.Contains(own), "their autopistol stays in their pack")
                   & story.Check(!issued.Spawned && !own.Spawned, "with neither on the ground");
        }

        /// <summary>The ledger is closed, both weapons and the registry of loans included.</summary>
        internal static bool Closed(Story story, Scene scene)
        {
            return story.Check(!scene.Comp.OnShift, "the stand no longer counts anyone on shift")
                   & story.Check(scene.Comp.issuedWeapon == null && scene.Comp.ownWeapon == null,
                                 "its record of both weapons is cleared")
                   & story.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == null,
                                 "and no stand has " + scene.Colonist.LabelShort + " on shift");
        }

        /// <summary>
        /// The engine remembers the weapon that fell from a downed colonist's hand, for them to
        /// fetch on getting up. Hospitality's downed patch makes that drop without the remember
        /// flag, for every colonist, so on a list with that mod nothing is remembered. True when
        /// the club is remembered.
        /// </summary>
        internal static bool HandIsRemembered(Story story, Pawn pawn, ThingWithComps issued)
        {
            return HandIsRemembered(story, pawn, issued, "the club");
        }

        /// <summary>
        /// <see cref="HandIsRemembered(Story, Pawn, ThingWithComps)"/> for any weapon that was in
        /// hand, which <paramref name="what"/> names.
        /// </summary>
        internal static bool HandIsRemembered(Story story, Pawn pawn, ThingWithComps weapon, string what)
        {
            bool remembered = pawn.mindState.droppedWeapon == weapon;
            string sentence = "and " + what + " from their hand is remembered for them to fetch";
            if (ModActive("Orion.Hospitality"))
            {
                story.KnownGap(remembered, sentence,
                    "Hospitality's downed patch drops the engine's remember flag, for any colonist");
                return remembered;
            }
            return story.Check(remembered, sentence);
        }

        /// <summary>
        /// Orders a job at the stand as a player's button does, and steps the clock to its end. It
        /// has to be an order: started any other way the job waits behind Shift Change's trip
        /// back, and never meets the loan.
        /// </summary>
        internal static bool OrderJob(Story story, Scene scene, JobDef def)
        {
            Pawn pawn = scene.Colonist;
            pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(def, scene.OutfitStand), JobTag.Misc);
            if (!story.Check(pawn.CurJobDef == def, "the order starts " + def.defName + ", not a change"))
            {
                return false;
            }
            JobDriver driver = pawn.jobs.curDriver;
            ClockRun run = GameClock.Step(() => pawn.jobs.curDriver != driver, 5000, scene.Hold);
            return story.Check(run.Done, def.defName + " runs to its end (" + run.Ticks + " ticks)");
        }
    }
}
