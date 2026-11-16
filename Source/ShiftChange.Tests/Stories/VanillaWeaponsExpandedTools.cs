using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DevTools.Testing;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.SceneKit;
using static ShiftChange.Tests.ToolKit;
using static ShiftChange.Tests.WeaponKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Vanilla Weapons Expanded's work tools lent by a stand under Simple Sidearms, which draws a
    /// tool for the work it suits and keeps a memory of the colonist's own weapons. The fixture
    /// registers only with both mods on the list, so a list without them runs none of it and
    /// reports no gap. With both on, a tool or a Simple Sidearms type that no longer resolves
    /// fails its test.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.VanillaWeaponsExpandedTools)]
    [LoadIfModsActive(WeaponsPackageId, SidearmsPackageId)]
    internal class VanillaWeaponsExpandedTools : StoryFixture
    {
        [Test]
        [TestDescription("a hammer from a stand speeds construction and goes back")]
        internal IEnumerator AHammerSpeedsConstructionAndGoesBack()
        {
            return Staged(ToolStand, ToolStandSays, Body);

            void Body(Scene scene, Story story)
            {
                AcceptTool(story, scene, Hammer, "ConstructFinishFrames", StatDefOf.ConstructionSpeed, FrameJob);
            }
        }

        /// <summary>
        /// The same round trip for a ranged weapon, with no work step to stage. Simple Sidearms
        /// picks by stat for a hunt, so its pick is asked for in hand: with the rifle packed, it
        /// keeps the bow there.
        /// </summary>
        [Test]
        [TestDescription("a compound bow from a stand raises hunting stealth and goes back")]
        internal IEnumerator ACompoundBowRaisesHuntingStealthAndGoesBack()
        {
            return Staged(ToolStand, ToolStandSays, Body);

            void Body(Scene scene, Story story)
            {
                StatDef stealth = StatNamed("HuntingStealth");
                AcceptTool(story, scene, Bow, "HunterHunt", stealth, null, pawn => SidearmsPick(pawn, stealth));
            }
        }

        /// <summary>
        /// Gives one thing a comp its def does not list, the steps <c>InitializeComps</c> and
        /// <c>PostMake</c> take for each listed one. False when the thing has no comp list of its
        /// own, since <c>AllComps</c> then returns a list every such thing shares.
        /// </summary>
        internal static bool GiveComp(ThingWithComps thing, CompProperties props)
        {
            if (thing.AllComps.Count == 0)
            {
                return false;
            }
            ThingComp comp = (ThingComp)Activator.CreateInstance(props.compClass);
            comp.parent = thing;
            thing.AllComps.Add(comp);
            comp.Initialize(props);
            comp.PostPostMake();
            return true;
        }

        /// <summary>
        /// A borrowed hammer never reaches Simple Sidearms' memory, in the hand or in the pack, so
        /// with a spare on the map nothing goes to fetch one once it is back on the stand. The
        /// last step remembers the spare by hand, which proves the fetch would come.
        /// </summary>
        [Test]
        [TestDescription("Simple Sidearms' memory of a colonist's weapons does not change across a loan")]
        internal IEnumerator SimpleSidearmsMemoryDoesNotChangeAcrossALoan()
        {
            return Staged(ToolStand, ToolStandSays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps hammer = Hold(scene, Hammer);
                if (!story.Check(hammer != null, "the stand holds a hammer"))
                {
                    return;
                }
                ThingWithComps rifle = ArmRemembered(scene);
                Thing spare = MakeWeapon(Hammer);
                GenSpawn.Spawn(spare, scene.OutfitStand.Position + new IntVec3(3, 0, 3), scene.Map);
                story.Note("a spare hammer lies on the floor of the room");
                List<string> memory = Remembered(pawn);
                story.Check(memory.Count == 1, "Simple Sidearms remembers only " + name + "'s rifle");
                story.Check(Retrieve(pawn) == null, "and would fetch nothing yet");

                if (!story.Check(SwapNow(story, scene), name + " changes at the stand"))
                {
                    return;
                }
                story.Check(pawn.equipment.Primary == hammer, "the hammer is in hand");
                story.Check(Remembered(pawn).SequenceEqual(memory), "the memory is the same");
                story.Check(Retrieve(pawn) == null, "and nothing would be fetched");

                QuickSwitch(pawn);
                story.Check(pawn.inventory.innerContainer.Contains(hammer), "Simple Sidearms' quick switch packs the hammer");
                story.Check(pawn.equipment.Primary == rifle, "and draws the rifle");
                story.Check(Remembered(pawn).SequenceEqual(memory), "the memory is the same");
                story.Check(Retrieve(pawn) == null, "and nothing would be fetched");

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(scene.OutfitStand.HeldWeapon == hammer, "the hammer is back on the stand");
                story.Check(Remembered(pawn).SequenceEqual(memory), "the memory is the same");
                story.Check(Retrieve(pawn) == null, "and nothing would be fetched");

                object comp = Memory(pawn);
                AccessTools.Method(comp.GetType(), "InformOfAddedSidearm").Invoke(comp, new object[] { spare });
                Job fetch = Retrieve(pawn);
                story.Check(fetch != null && fetch.targetA.Thing == spare,
                            "a hammer Simple Sidearms did remember would be fetched");
            }
        }

        /// <summary>
        /// Simple Sidearms packs the hammer when the colonist is idle and draws it again at the
        /// work step, and the stand still gets it back from wherever it ended up.
        /// </summary>
        [Test]
        [TestDescription("Simple Sidearms moving a lent tool mid-shift does not stop it going back")]
        internal IEnumerator SimpleSidearmsMovingTheToolDoesNotStopItGoingBack()
        {
            return Staged(ToolStand, ToolStandSays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps hammer = Hold(scene, Hammer);
                WorkGiverDef giver = Giver("ConstructFinishFrames");
                if (!story.Check(hammer != null && giver != null, "the stand holds a hammer and serves construction"))
                {
                    return;
                }
                ThingWithComps rifle = ArmRemembered(scene);
                scene.Comp.ToggleWork(WorkTypeDefOf.Construction);

                if (!story.Check(SwapNow(story, scene), name + " changes at the stand"))
                {
                    return;
                }
                QuickSwitch(pawn);
                story.Check(pawn.equipment.Primary == rifle, "idle, Simple Sidearms draws " + name + "'s rifle");
                story.Check(pawn.inventory.innerContainer.Contains(hammer), "and packs the hammer");
                if (StartWork(story, scene, FrameJob(scene), giver))
                {
                    story.Check(pawn.equipment.Primary == hammer, "the hammer is drawn for the work");
                }
                EndWork(pawn);

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(pawn.equipment.Primary == rifle, "the rifle is back in hand");
                story.Check(scene.OutfitStand.HeldWeapon == hammer, "and the hammer on the stand");
                Closed(story, scene);
            }
        }

        /// <summary>
        /// A draft mid-shift trades the two weapons each way, and Simple Sidearms' memory does not
        /// move. Its next idle switch draws the remembered rifle, which is its own call, and the
        /// trip back settles both from there.
        /// </summary>
        [Test]
        [TestDescription("under Simple Sidearms, a draft trades a lent tool for the colonist's rifle and back")]
        internal IEnumerator ADraftTradesTheToolUnderSimpleSidearms()
        {
            return Staged(ToolStand, ToolStandSays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                ThingWithComps hammer = Hold(scene, Hammer);
                WorkGiverDef giver = Giver("ConstructFinishFrames");
                if (!story.Check(hammer != null && giver != null, "the stand holds a hammer and serves construction"))
                {
                    return;
                }
                ThingWithComps rifle = ArmRemembered(scene);
                scene.Comp.ToggleWork(WorkTypeDefOf.Construction);
                List<string> memory = Remembered(pawn);

                if (!story.Check(SwapNow(story, scene), name + " changes at the stand"))
                {
                    return;
                }
                StartWork(story, scene, FrameJob(scene), giver);
                EndWork(pawn);
                pawn.drafter.Drafted = true;
                story.Check(pawn.equipment.Primary == rifle, "drafted, " + name + "'s rifle is in hand");
                story.Check(Remembered(pawn).SequenceEqual(memory), "and Simple Sidearms' memory is the same");
                pawn.drafter.Drafted = false;
                story.Check(pawn.equipment.Primary == hammer, "undrafted, the hammer is back");
                story.Check(Remembered(pawn).SequenceEqual(memory), "and the memory is the same");
                QuickSwitch(pawn);
                story.Check(pawn.equipment.Primary == rifle, "an idle switch draws the rifle");

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(pawn.equipment.Primary == rifle, "the rifle is in hand");
                story.Check(scene.OutfitStand.HeldWeapon == hammer, "and the hammer on the stand");
                Closed(story, scene);
            }
        }

        /// <summary>
        /// Vanilla Expanded Framework gives a colonist a weapon's hediffs while it is in the hand,
        /// and clears them when the weapon is dropped. A weapon going to the pack has to go by way
        /// of that drop, or the hediff stays with nothing left to clear it. No weapon on the list
        /// carries the comp, so this one hammer is given it (<see cref="GiveComp"/>) and its def,
        /// which every other hammer shares, is left alone.
        /// </summary>
        [Test]
        [TestDescription("a weapon's hediff stays with the hand, not the pack")]
        internal IEnumerator AWeaponsHediffStaysWithTheHand()
        {
            return Staged(ToolStand, ToolStandSays, Body);

            void Body(Scene scene, Story story)
            {
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                Type propsType = GenTypes.GetTypeInAnyAssembly(WeaponHediffsClass);
                if (!story.Check(propsType != null, "Vanilla Expanded Framework's weapon hediff comp resolves"))
                {
                    return;
                }
                HediffDef hediff = DefDatabase<HediffDef>.GetNamed("Hangover");
                CompProperties props = (CompProperties)Activator.CreateInstance(propsType);
                Traverse.Create(props).Field("hediffs").SetValue(new List<HediffDef> { hediff });
                ThingWithComps hammer = Hold(scene, Hammer);
                if (!story.Check(hammer != null && GiveComp(hammer, props),
                                 "the stand holds a hammer that gives a hangover in hand"))
                {
                    return;
                }
                ThingWithComps rifle = ArmRemembered(scene);
                scene.Comp.ToggleWork(WorkTypeDefOf.Construction);
                HediffSet hediffs = pawn.health.hediffSet;

                story.Check(!hediffs.HasHediff(hediff), name + " has no hangover before the loan");
                if (!story.Check(SwapNow(story, scene), name + " changes at the stand"))
                {
                    return;
                }
                story.Check(pawn.equipment.Primary == hammer, "the hammer is in hand");
                story.Check(hediffs.HasHediff(hediff), "and gives its hangover");
                pawn.drafter.Drafted = true;
                story.Check(pawn.inventory.innerContainer.Contains(hammer), "drafted, the hammer is packed");
                story.Check(!hediffs.HasHediff(hediff), "and the hangover is gone");
                pawn.drafter.Drafted = false;
                story.Check(pawn.equipment.Primary == hammer, "undrafted, the hammer is back in hand");
                story.Check(hediffs.HasHediff(hediff), "and so is the hangover");

                story.Check(SwapNow(story, scene), name + " changes back");
                story.Check(scene.OutfitStand.HeldWeapon == hammer, "the hammer is back on the stand");
                story.Check(!hediffs.HasHediff(hediff), "and the hangover is gone");
                story.Check(pawn.equipment.Primary == rifle, "the rifle is in hand");
                Closed(story, scene);
            }
        }
    }
}
