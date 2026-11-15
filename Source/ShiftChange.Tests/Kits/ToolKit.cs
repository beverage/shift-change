using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.SceneKit;
using static ShiftChange.Tests.WeaponKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// What the tool groups share (<see cref="VanillaWeaponsExpandedTools"/>,
    /// <see cref="MedievalOverhaulTools"/>): a stand that lends another mod's real tool, a
    /// colonist sure to take it, the work that tool is for, and Simple Sidearms read by reflection.
    /// The tools are found by defName and Simple Sidearms by type name, so neither mod reaches the
    /// shipping dll, nor this one's references.
    /// </summary>
    internal static class ToolKit
    {
        internal const string Hammer = "VWE_MeleeWeapon_Hammer";
        internal const string Bow = "VWE_Bow_Compound";
        internal const string Rifle = "Gun_BoltActionRifle";

        internal const string MemoryClass = "SimpleSidearms.rimworld.CompSidearmMemory";
        internal const string QuickSwitchClass = "SimpleSidearms.rimworld.JobGiver_QuicklySwitchWeapons";
        internal const string RetrieveClass = "SimpleSidearms.rimworld.JobGiver_RetrieveWeapon";
        internal const string AssignmentClass = "PeteTimesSix.SimpleSidearms.Utilities.WeaponAssingment";
        internal const string WeaponHediffsClass = "VEF.Weapons.CompProperties_WeaponHediffs";

        internal const string WeaponsPackageId = "VanillaExpanded.VWE";
        internal const string SidearmsPackageId = "PeteTimesSix.SimpleSidearms";
        internal const string MedievalOverhaulPackageId = "DankPyon.Medieval.Overhaul";

        /// <summary>What <see cref="ToolStand"/> sets up.</summary>
        internal const string ToolStandSays =
            "a stand in the corner of a walled room, holding a club that each test swaps for its tool, and "
            + "a colonist beside it in a shirt and trousers, empty-handed, with no work they refuse and every "
            + "skill at 6";

        /// <summary>
        /// A stand in a walled room holding a club and no clothes, and a colonist with no disabled
        /// work and every skill at 6 with no passion, so a rolled backstory cannot refuse a tool or
        /// zero a stat. The club is the test's to replace with its tool (<see cref="Hold"/>).
        /// </summary>
        internal static Scene ToolStand(Map map, CellRect pad)
        {
            Scene scene = Stage(map, pad, StageKit.WeaponOnly, enclose: true, capableOf: WorkTypeDefOf.Hunting);
            if (scene == null)
            {
                return null;
            }
            Pawn pawn = scene.Colonist;
            pawn.story.Adulthood = DefDatabase<BackstoryDef>.AllDefs.First(
                b => b.slot == BackstorySlot.Adulthood && b.workDisables == WorkTags.None);
            pawn.story.Childhood = DefDatabase<BackstoryDef>.AllDefs.First(
                b => b.slot == BackstorySlot.Childhood && b.workDisables == WorkTags.None);
            pawn.Notify_DisabledWorkTypesChanged();
            pawn.workSettings.EnableAndInitialize();
            foreach (SkillRecord skill in pawn.skills.skills)
            {
                skill.Level = 6;
                skill.passion = Passion.None;
            }
            MakeCalm(map);
            return scene;
        }

        /// <summary>
        /// Replaces the stand's weapon with a new one of this def. Null when the def is missing,
        /// with the stand's weapon left where it was, or when the stand refuses the new one, by
        /// which time the old one is destroyed and the stand holds no weapon.
        /// </summary>
        internal static ThingWithComps Hold(Scene scene, string defName)
        {
            ThingWithComps tool = MakeWeapon(defName);
            if (tool == null)
            {
                return null;
            }
            ThingWithComps held = scene.OutfitStand.HeldWeapon;
            if (held != null)
            {
                scene.OutfitStand.RemoveHeldWeapon(held);
                held.Destroy();
            }
            if (!scene.OutfitStand.TryAddHeldWeapon(tool))
            {
                tool.Destroy();
                return null;
            }
            return tool;
        }

        /// <summary>
        /// The colonist's own rifle, remembered as an equip order leaves it when Simple Sidearms
        /// is on the list (<see cref="ArmRemembered"/>), in hand without it.
        /// </summary>
        internal static ThingWithComps OwnRifle(Scene scene)
        {
            return ModActive(SidearmsPackageId) ? ArmRemembered(scene) : Arm(scene.Colonist, Rifle);
        }

        /// <summary>
        /// A rifle in hand, remembered by Simple Sidearms as an equip order would leave it: the
        /// one weapon in its memory, its primary, with ranged as the preferred mode. A Simple
        /// Sidearms type that no longer resolves throws, which fails the test.
        /// </summary>
        internal static ThingWithComps ArmRemembered(Scene scene)
        {
            ThingWithComps rifle = Arm(scene.Colonist, Rifle);
            object memory = Memory(scene.Colonist);
            ((IList)Traverse.Create(memory).Field("rememberedWeapons").GetValue()).Clear();
            AccessTools.Method(memory.GetType(), "InformOfAddedPrimary").Invoke(memory, new object[] { rifle });
            Traverse mode = Traverse.Create(memory).Field("primaryWeaponMode");
            mode.SetValue(Enum.Parse(mode.GetValueType(), "Ranged"));
            return rifle;
        }

        /// <summary>Simple Sidearms' memory comp for the colonist, made if they have none.</summary>
        internal static object Memory(Pawn pawn)
        {
            Type type = GenTypes.GetTypeInAnyAssembly(MemoryClass);
            return AccessTools.Method(type, "GetMemoryCompForPawn").Invoke(null, new object[] { pawn, true });
        }

        /// <summary>The colonist's remembered weapons, in order, as def and stuff names.</summary>
        internal static List<string> Remembered(Pawn pawn)
        {
            IEnumerable list = (IEnumerable)Traverse.Create(Memory(pawn)).Field("rememberedWeapons").GetValue();
            return list.Cast<object>().Select(pair =>
            {
                Traverse read = Traverse.Create(pair);
                return read.Field("thing").GetValue<ThingDef>().defName + "/"
                       + read.Field("stuff").GetValue<ThingDef>()?.defName;
            }).ToList();
        }

        /// <summary>
        /// Simple Sidearms' quick switch, as its think node calls it, with its idle delay and any
        /// tool it drew for a job cleared first, so it switches now rather than after its delay.
        /// </summary>
        internal static void QuickSwitch(Pawn pawn)
        {
            Traverse memory = Traverse.Create(Memory(pawn));
            memory.Field("autotoolToil").SetValue(null);
            memory.Field("autotoolJob").SetValue(null);
            memory.Field("delayIdleSwitchTimestamp").SetValue(-60);
            Type type = GenTypes.GetTypeInAnyAssembly(QuickSwitchClass);
            AccessTools.Method(type, "TryGiveJobStatic").Invoke(null, new object[] { pawn, false });
        }

        /// <summary>The fetch job Simple Sidearms would give now, or null.</summary>
        internal static Job Retrieve(Pawn pawn)
        {
            Type type = GenTypes.GetTypeInAnyAssembly(RetrieveClass);
            return (Job)AccessTools.Method(type, "TryGiveJobStatic").Invoke(null, new object[] { pawn, false });
        }

        /// <summary>Simple Sidearms' choice of the best weapon the colonist carries for this stat.</summary>
        internal static void SidearmsPick(Pawn pawn, StatDef stat)
        {
            Type assignment = GenTypes.GetTypeInAnyAssembly(AssignmentClass);
            AccessTools.Method(assignment, "equipBestWeaponFromInventoryByStatModifiers")
                .Invoke(null, new object[] { pawn, new List<StatDef> { stat } });
        }

        /// <summary>A wooden wall frame east of the colonist and the job that finishes it.</summary>
        internal static Job FrameJob(Scene scene)
        {
            Thing frame = ThingMaker.MakeThing(ThingDefOf.Wall.frameDef, ThingDefOf.WoodLog);
            frame.SetFactionDirect(Faction.OfPlayer);
            GenSpawn.Spawn(frame, scene.Colonist.Position + IntVec3.East, scene.Map);
            return JobMaker.MakeJob(JobDefOf.FinishFrame, frame);
        }

        /// <summary>A granite rock east of the colonist and the job that mines it. The job ignores
        /// designations, so the rock needs none.</summary>
        internal static Job MineJob(Scene scene)
        {
            Thing rock = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Granite"));
            GenSpawn.Spawn(rock, scene.Colonist.Position + IntVec3.East, scene.Map);
            Job job = JobMaker.MakeJob(JobDefOf.Mine, rock);
            job.ignoreDesignations = true;
            return job;
        }

        /// <summary>
        /// Starts a real work job from <paramref name="giver"/> and steps the clock to its working
        /// step, where Simple Sidearms draws a tool. False, with the job the colonist is on
        /// instead, when the job never got there.
        /// </summary>
        internal static bool StartWork(Story story, Scene scene, Job job, WorkGiverDef giver)
        {
            Pawn pawn = scene.Colonist;
            job.workGiverDef = giver;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null, JobTag.MiscWork);
            JobDriver driver = pawn.jobs.curDriver;
            if (driver != null && driver.ActiveSkill == null)
            {
                GameClock.Step(() => pawn.jobs.curDriver != driver || driver.ActiveSkill != null, 30, scene.Hold);
            }
            bool working = driver != null && pawn.jobs.curDriver == driver && driver.ActiveSkill != null;
            // Some jobs have no label (Mine is one), so the defName stands in.
            string started = pawn.LabelShort + " starts " + (job.def.label.NullOrEmpty() ? job.def.defName : job.def.label)
                             + " and reaches the working step";
            return story.Check(working, working
                ? started
                : started + "; instead their job is " + (pawn.CurJobDef == null ? "none" : pawn.CurJobDef.defName));
        }

        internal static void EndWork(Pawn pawn)
        {
            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
        }

        /// <summary>
        /// The stat before its post-process curve, which is what an equipped offset moves in step
        /// with. The request form is the one that honours the flag: the overload taking a thing
        /// applies the curve whatever it is told, and hunting stealth has one.
        /// </summary>
        internal static float Stat(Pawn pawn, StatDef stat)
        {
            return stat.Worker.GetValue(StatRequest.For(pawn), applyPostProcess: false);
        }

        /// <summary>
        /// An offset adds to the colonist's base for the stat, and skill and capacity factors
        /// multiply the sum, so with nothing else offsetting the stat it rises by offset over base
        /// in proportion, whatever their skills are. The base is the race's own where its statBases
        /// list the stat, as <c>StatWorker.GetBaseValueFor</c> reads it, and the stat's default
        /// otherwise. False for a base of 0, which has no proportion to take.
        /// </summary>
        internal static bool RaisedBy(Pawn pawn, StatDef stat, float before, float during, float offset)
        {
            float baseValue = stat.Worker.GetBaseValueFor(StatRequest.For(pawn));
            return baseValue > 0f && Mathf.Abs(during - before * (1f + offset / baseValue)) < 0.001f;
        }

        /// <summary>
        /// The stat moved the way the offset points, and stayed put for an offset of 0. Another
        /// offset on the same stat, from gear, a trait or a hediff, changes how far it moves but
        /// not which way.
        /// </summary>
        internal static bool Moved(float before, float during, float offset)
        {
            float moved = during - before;
            return offset == 0f
                ? Mathf.Abs(moved) < 0.001f
                : moved * offset > 0f && Mathf.Abs(moved) >= 0.0001f;
        }

        /// <summary>
        /// A def's equipped offsets, one per stat, at the value the engine applies: where a patch
        /// lists a stat twice, the first entry is the one <c>GetStatOffsetFromList</c> reads.
        /// </summary>
        internal static List<StatModifier> ToolOffsets(ThingDef def)
        {
            List<StatModifier> listed = def.equippedStatOffsets ?? new List<StatModifier>();
            return listed.Select(mod => mod.stat).Distinct()
                .Select(stat => new StatModifier { stat = stat, value = listed.GetStatOffsetFromList(stat) })
                .ToList();
        }

        /// <summary>
        /// One tool, from the work that sends the colonist to the stand to the working step and
        /// back: the tool in hand and the rifle packed, and the same two where they started. The
        /// stand serves the giver's own work type. Against the empty hand, the work stat rises by
        /// the tool's offset and every other stat it offsets moves its way; the rifle's own offsets
        /// go to the pack with it, so the rifle in hand is no baseline. All of them are back once
        /// the rifle is. Without <paramref name="makeJob"/> no work starts and the stats are read
        /// in hand, after <paramref name="inHand"/> where a test has a step to take there.
        /// </summary>
        internal static void AcceptTool(Story story, Scene scene, string toolName, string giverName,
                                        StatDef stat, Func<Scene, Job> makeJob, Action<Pawn> inHand = null)
        {
            Pawn pawn = scene.Colonist;
            string name = pawn.LabelShort;
            ThingWithComps tool = Hold(scene, toolName);
            WorkGiverDef giver = Giver(giverName);
            if (!story.Check(tool != null && giver != null && stat != null,
                             "the game has " + toolName + ", the " + giverName + " work and its stat, and the stand "
                             + "takes the tool"))
            {
                return;
            }
            List<StatModifier> offsets = ToolOffsets(tool.def);
            List<float> bare = offsets.Select(mod => Stat(pawn, mod.stat)).ToList();
            ThingWithComps rifle = OwnRifle(scene);
            scene.Comp.ToggleWork(giver.workType);
            List<float> before = offsets.Select(mod => Stat(pawn, mod.stat)).ToList();
            story.Note("the stand holds a " + tool.LabelNoCount + " and serves " + giver.workType.labelShort + "; "
                       + name + " holds a rifle of their own");

            story.Check(offsets.GetStatOffsetFromList(stat) > 0f, "the " + tool.def.label + " raises " + stat.label);
            story.Check(SendsToChange(scene, WorkJob(scene, giver)), giver.label + " sends " + name + " to the stand");
            if (!story.Check(StoryFixture.SwapNow(story, scene), name + " changes at the stand"))
            {
                return;
            }
            story.Check(scene.Comp.issuedWeapon == tool && scene.Comp.ownWeapon == rifle,
                        "the stand records the " + tool.def.label + " it lent and the rifle " + name + " put away");

            if (makeJob != null)
            {
                StartWork(story, scene, makeJob(scene), giver);
            }
            inHand?.Invoke(pawn);
            story.Check(pawn.equipment.Primary == tool, "the " + tool.def.label + " is in " + name + "'s hand");
            story.Check(pawn.inventory.innerContainer.Contains(rifle), "and the rifle in their pack");
            for (int i = 0; i < offsets.Count; i++)
            {
                StatModifier mod = offsets[i];
                float during = Stat(pawn, mod.stat);
                string change = ": " + bare[i] + " to " + during;
                if (mod.stat == stat)
                {
                    story.Check(RaisedBy(pawn, stat, bare[i], during, mod.value),
                                stat.label + " is up by the tool's offset" + change);
                }
                else
                {
                    story.Check(Moved(bare[i], during, mod.value),
                                mod.stat.label + " moves the way the tool's offset points" + change);
                }
            }
            if (makeJob != null)
            {
                EndWork(pawn);
            }

            story.Check(StoryFixture.SwapNow(story, scene), name + " changes back");
            story.Check(pawn.equipment.Primary == rifle, "the same rifle is back in their hand");
            story.Check(scene.OutfitStand.HeldWeapon == tool, "the same " + tool.def.label + " is back on the stand");
            for (int i = 0; i < offsets.Count; i++)
            {
                story.Check(Mathf.Abs(Stat(pawn, offsets[i].stat) - before[i]) < 0.001f,
                            "and " + offsets[i].stat.label + " is back to what it was");
            }
            Closed(story, scene);
        }
    }
}
