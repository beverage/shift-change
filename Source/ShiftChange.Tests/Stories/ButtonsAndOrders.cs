using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DevTools.Testing;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// The stand's buttons, the player's orders on shift, and the clothes a stand parks.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.ButtonsAndOrders)]
    internal class ButtonsAndOrders : StoryFixture
    {
        /// <summary>What <see cref="Stage"/> sets up with the pad walled, roofed and floored.</summary>
        internal const string InARoom =
            "a stand holding a duster in a walled room, and a colonist beside it in a shirt, trousers and a parka";

        /// <summary>
        /// What <see cref="Build"/> sets up: a colonist in a tunic (tribalwear without Vanilla
        /// Apparel Expanded), a green duster and a simple helmet, put on shift the way a change
        /// does it.
        /// </summary>
        internal const string OnShift =
            "a colonist on shift at a stand in the open, changed into its lab coat (its shirt and trousers "
            + "without Vanilla Apparel Expanded), with the clothes it displaced parked on the stand";

        /// <summary>
        /// The switch names what the stand serves, recreation and sleep included, as it names a
        /// work type. Guards a past bug where the switch listed work types alone, so a working
        /// recreation or sleep stand read as having no work yet (docs/DESIGN.md, "Rooms to work
        /// types"). Read from the stand's buttons, never from the screen.
        /// </summary>
        [Test]
        [TestDescription("a stand's switch says what it serves: a work type, recreation or sleep, "
                         + "or that it is not used")]
        internal IEnumerator TheSwitchSaysWhatTheStandServes()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Scene scene, Story story)
            {
                WorkTypeDef doctor = WorkType("Doctor");
                if (doctor == null)
                {
                    story.Check(false, "the game has its doctoring");
                    return;
                }
                string recreation = "ShiftChange.Recreation".Translate().RawText;
                string sleep = "ShiftChange.Rest".Translate().RawText;
                string idle = "ShiftChange.RegimeIdle".Translate().RawText;
                string off = "ShiftChange.RegimeOff".Translate().RawText;

                story.Check(GizmoKit.Switch(scene.Comp)?.defaultLabel == idle,
                            "on Automatic, in a room with no role, the switch reads \"" + idle + "\"");

                scene.Comp.ToggleWork(doctor);
                story.Check(GizmoKit.Switch(scene.Comp)?.defaultLabel
                            == GizmoKit.Shift(WorkTypeLabels.Of(doctor)),
                            "ticked for doctoring, it reads \"" + GizmoKit.Shift(WorkTypeLabels.Of(doctor)) + "\"");

                scene.Comp.ToggleRecreation();
                story.Check(scene.Comp.HandlesRecreation(), "ticked for Recreation, the stand serves recreation");
                story.Check(GizmoKit.Switch(scene.Comp)?.defaultLabel == GizmoKit.Shift(recreation),
                            "and its switch names recreation (it reads \""
                            + GizmoKit.Switch(scene.Comp)?.defaultLabel + "\")");

                scene.Comp.ToggleRest();
                story.Check(scene.Comp.HandlesRest(), "ticked for Sleeping, the stand serves sleep");
                story.Check(GizmoKit.Switch(scene.Comp)?.defaultLabel == GizmoKit.Shift(sleep),
                            "and its switch names sleep (it reads \""
                            + GizmoKit.Switch(scene.Comp)?.defaultLabel + "\")");

                scene.Comp.SetExcluded();
                story.Check(GizmoKit.Switch(scene.Comp)?.defaultLabel == off,
                            "and set to not used, it reads \"" + off + "\"");
            }
        }

        /// <summary>
        /// Selecting several stands shows each stand's own buttons, the switch and Set owner alike.
        /// Why a merged button misled: docs/DESIGN.md, "Rooms to work types". Two plain buttons
        /// with the same face do merge (<c>Command.GroupsWith</c> compares label, icon, hotkey and
        /// group key), so it is the <c>groupable</c> flag on our buttons that keeps the stands'
        /// apart.
        /// </summary>
        [Test]
        [TestDescription("with two stands selected, each keeps its own switch and Set owner button, "
                         + "never one merged for both")]
        internal IEnumerator EachSelectedStandKeepsItsOwnButtons()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Scene scene, Story story)
            {
                ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
                if (standDef == null)
                {
                    story.Check(false, "the game has its outfit stand");
                    return;
                }
                Building_OutfitStand other = DebugTools_Fixtures.Spawn(scene.Map, standDef, ThingDefOf.WoodLog,
                    scene.OutfitStand.Position + new IntVec3(2, 0, 0), Rot4.North) as Building_OutfitStand;
                CompShiftStand otherComp = other?.TryGetComp<CompShiftStand>();
                if (otherComp == null)
                {
                    story.Check(false, "a second stand is built two cells along");
                    return;
                }
                story.Note("a second stand is built two cells along");

                Command_Action mine = GizmoKit.Switch(scene.Comp);
                Command_Action theirs = GizmoKit.Switch(otherComp);
                if (!story.Check(mine != null && theirs != null, "both stands have a switch"))
                {
                    return;
                }
                Command_Action twinA = new Command_Action { defaultLabel = mine.defaultLabel, icon = mine.icon };
                Command_Action twinB = new Command_Action { defaultLabel = mine.defaultLabel, icon = mine.icon };
                story.Check(mine.defaultLabel == theirs.defaultLabel && mine.icon == theirs.icon,
                            "set up alike, the two stands' switches look the same");
                story.Check(twinA.GroupsWith(twinB), "and two plain buttons that look like that would merge into one");
                story.Check(!mine.GroupsWith(theirs) && !theirs.GroupsWith(mine),
                            "but the two stands' switches stay separate buttons");

                Command_Action myOwner = GizmoKit.OwnerButton(scene.OutfitStand);
                Command_Action theirOwner = GizmoKit.OwnerButton(other);
                story.Check(myOwner != null && theirOwner != null && myOwner.Label == theirOwner.Label,
                            "both stands show the same Set owner button");
                story.Check(myOwner != null && theirOwner != null
                            && !myOwner.GroupsWith(theirOwner) && !theirOwner.GroupsWith(myOwner),
                            "and those stay separate too");
            }
        }

        /// <summary>
        /// Hovering a stand's switch or Set owner button marks that stand and no other, so with
        /// several stands selected a player can find whose button is whose. The arrow is read
        /// from vanilla's list, which the next frame draws and empties. The square is read from
        /// the targeting material, whose colour vanilla rewrites each time it hands it out to
        /// draw, so a colour set beforehand changes only if the square was drawn. A direct call
        /// for the other stand shows both are read right.
        /// </summary>
        [Test]
        [TestDescription("hovering over a stand's switch or Set owner button points at that stand, "
                         + "and at no other")]
        internal IEnumerator HoveringAButtonPointsAtItsStand()
        {
            return Staged((map, pad) => Stage(map, pad, StageKit.Displacing, enclose: true), InARoom, Body);

            void Body(Scene scene, Story story)
            {
                ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
                List<Vector3> arrows = AccessTools.Field(typeof(TargetHighlighter), "arrowPositions")
                    ?.GetValue(null) as List<Vector3>;
                Material square = GenDraw.CurTargetingMat;
                if (!story.Check(standDef != null, "the game has its outfit stand")
                    || !story.Check(arrows != null, "the game's list of arrows to draw can be read")
                    || !story.Check(scene.Map == Find.CurrentMap, "the stand is on the map in view"))
                {
                    return;
                }
                Building_OutfitStand other = DebugTools_Fixtures.Spawn(scene.Map, standDef, ThingDefOf.WoodLog,
                    scene.OutfitStand.Position + new IntVec3(2, 0, 0), Rot4.North) as Building_OutfitStand;
                CompShiftStand otherComp = other?.TryGetComp<CompShiftStand>();
                if (otherComp == null)
                {
                    story.Check(false, "a second stand is built two cells along");
                    return;
                }
                story.Note("a second stand is built two cells along");

                void Reset()
                {
                    arrows.Clear();
                    // Vanilla hands the square out opaque, never clear.
                    square.color = Color.clear;
                }
                bool Squared()
                {
                    return square.color != Color.clear;
                }
                bool Marks(Thing stand)
                {
                    return arrows.Any(a => (a - stand.DrawPos).Yto0().magnitude < 0.6f);
                }
                string Seen()
                {
                    return " (" + arrows.Count + " arrows, the square " + (Squared() ? "drawn" : "not drawn") + ")";
                }
                void OnlyMarks(Command_Action button, Thing mine, Thing theirs, string what)
                {
                    Reset();
                    if (!story.Check(button?.onHover != null, what + " does something when hovered"))
                    {
                        return;
                    }
                    button.onHover();
                    story.Check(Marks(mine) && !Marks(theirs) && Squared(),
                                what + " marks its own stand and not the other" + Seen());
                }

                Reset();
                story.Check(!Squared(), "the targeting square keeps the colour set on it, so a drawn one shows");
                CompShiftStand.PointAt(other);
                story.Check(Marks(other) && !Marks(scene.OutfitStand) && Squared(),
                            "pointing at the second stand directly marks that stand alone" + Seen());
                OnlyMarks(GizmoKit.Switch(scene.Comp), scene.OutfitStand, other, "the first stand's switch");
                OnlyMarks(GizmoKit.Switch(otherComp), other, scene.OutfitStand, "the second stand's switch");
                OnlyMarks(GizmoKit.OwnerButton(scene.OutfitStand), scene.OutfitStand, other,
                          "the first stand's Set owner button");
                OnlyMarks(GizmoKit.OwnerButton(other), other, scene.OutfitStand, "the second stand's Set owner button");

                // Vanilla keeps calling the last hovered button while another main tab is open, so a
                // button can outlive its stand.
                Command_Action stale = GizmoKit.Switch(otherComp);
                other.Destroy();
                Reset();
                stale?.onHover?.Invoke();
                story.Check(stale != null && arrows.Count == 0 && !Squared(),
                            "a button kept after its stand is gone marks nothing" + Seen());
                arrows.Clear();
            }
        }

        // A pair that only means anything together: the refusal that stops a colonist on shift
        // re-arming from the spare pile, and the release that lets them shop once their parked
        // clothes are worn out. The rule is a comparison, never an equivalence test
        // (Patch_ParkedKitOutscoresSpares says why), and the first test alone cannot tell the two
        // apart. Nothing is spawned: ApparelScoreRaw reads a garment's def stats, hit points,
        // quality and stuff and never asks where it is, so a spare from ThingMaker scores as one
        // in a stockpile does. What the patch keys on is built for real, through
        // SwapPlan.BuildDress and NotifyDressed.

        /// <summary>
        /// A colonist on shift refuses a spare duplicating what is parked: in play the failure is
        /// invisible, since a colonist in an identical garment looks like one who never moved.
        /// Three more checks keep the refusal honest: a spare that displaces nothing parked, the
        /// worn garment itself, and the same spare once the colonist is off shift.
        /// </summary>
        [Test]
        [TestDescription("a colonist on shift will not put on a spare like the clothes they left on the stand")]
        internal IEnumerator OnShiftTheyLeaveSparesOfTheirParkedClothes()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                Apparel parked = ParkedGarment(scene);
                if (parked == null)
                {
                    story.Check(false, "the stand holds something of " + name + "'s to compare against");
                    return;
                }

                Apparel spare = Duplicate(parked);
                if (spare == null)
                {
                    story.Check(false, "a spare " + parked.def.label + " like the parked one can be made");
                    return;
                }
                string label = parked.def.label;

                story.Check(scene.Comp.Borrower == scene.Colonist, name + " has borrowed the stand's outfit");
                story.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == scene.Comp,
                            "and is on shift at this stand");
                story.Check(Refused(scene.Colonist, spare),
                            name + " refuses a spare " + label + " like the one parked on the stand");

                // A garment that displaces nothing parked must come through untouched. Chosen against
                // what is actually parked, never hard-coded: with Vanilla Apparel Expanded loaded the
                // scene parks a duster, without it the colonist's tribalwear, whose legs conflict with
                // trousers, so fixed trousers would check the opposite of the truth and read as a
                // broken patch.
                Apparel unrelated = NonConflictingSpare(scene);
                story.Check(unrelated != null, "a spare that would displace nothing on the stand can be made");
                if (unrelated != null)
                {
                    story.Check(!Refused(scene.Colonist, unrelated),
                                "and " + name + " would still take a " + unrelated.def.label
                                + ", which displaces nothing on the stand");
                }

                // The same conflict, but worn. ApparelScoreGain subtracts the worn scores, so refusing
                // a worn garment would inflate every candidate instead of suppressing it.
                Apparel worn = scene.Colonist.apparel != null && scene.Colonist.apparel.WornApparel.Count > 0
                    ? scene.Colonist.apparel.WornApparel[0]
                    : null;
                if (worn != null)
                {
                    story.Check(!Refused(scene.Colonist, worn),
                                "what " + name + " has on, such as the " + worn.def.label + ", is never refused");
                }

                // Last: it takes the colonist off shift, which the checks above need.
                CompShiftStand.OnShiftStands.Remove(scene.Colonist);
                story.Note(name + " is taken off shift");
                story.Check(!Refused(scene.Colonist, spare),
                            "and off shift, the same spare " + label + " is theirs to take");
            }
        }

        /// <summary>
        /// A parked garment worn through no longer answers for the colonist, so the same spare is
        /// allowed again. Only the parked garment's condition differs between the two halves.
        /// </summary>
        [Test]
        [TestDescription("but once the clothes on the stand are worn through, the spare is theirs to take")]
        internal IEnumerator WornOutParkedClothesCanBeReplaced()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                Apparel parked = ParkedGarment(scene);
                if (parked == null)
                {
                    story.Check(false, "the stand holds something of " + name + "'s to wear out");
                    return;
                }
                if (!parked.def.useHitPoints || parked.MaxHitPoints < 5)
                {
                    story.Check(false, "the " + parked.def.label + " on the stand can wear out");
                    return;
                }

                Apparel spare = Duplicate(parked);
                if (spare == null)
                {
                    story.Check(false, "a spare " + parked.def.label + " like the parked one can be made");
                    return;
                }
                string label = parked.def.label;

                story.Check(Refused(scene.Colonist, spare),
                            name + " refuses a spare " + label + " while the one on the stand is sound");

                // A fifth of full hit points is well under the knee of the game's
                // HitPointsPercentScoreFactorCurve, which is 1.0 from 52% up, 0.3 from 22% to 50% and
                // at most 0.2 from 20% down, so the parked garment is the worse one by the game's own
                // reckoning, not by a threshold of ours.
                parked.HitPoints = Mathf.Max(1, parked.MaxHitPoints / 5);
                story.Note("the " + label + " on the stand is worn down to " + parked.HitPoints + " of "
                           + parked.MaxHitPoints + " hit points");
                story.Check(!Refused(scene.Colonist, spare),
                            "and " + name + " takes the spare once the one on the stand is worn through");
            }
        }

        /// <summary>
        /// On shift the styling station's order is taken while the colonist's own urge to recolor
        /// is not, and a recolor naming a parked garment is refused, ordered or not
        /// (<see cref="Patch_OptimizeApparelOnShift"/>, <see cref="Patch_RecolorExecutionGuard"/>).
        /// Driven through the apparel optimizer, a job's reservations and <c>TryTakeOrderedJob</c>
        /// as the station calls it, each refusal beside a check that differs only in what the
        /// guard keys on. Guards the order being refused on shift, with a log warning. A known gap
        /// without Ideology, whose defs the job and the station are.
        /// </summary>
        [Test]
        [TestDescription("on shift a colonist carries out a recolor order from a styling station, but never "
                         + "dyes their outfit unasked, nor the clothes they left on the stand")]
        internal IEnumerator ARecolorOrderIsCarriedOutInUniform()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Story story)
            {
                if (!ModsConfig.IdeologyActive || JobDefOf.RecolorApparel == null
                    || ThingDefOf.StylingStation == null || ThingDefOf.Dye == null)
                {
                    story.KnownGap(false, "a colonist on shift carries out a recolor order",
                        "Ideology is not active on this mod list; add ludeon.rimworld.ideology to it");
                    return;
                }
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                Apparel uniform = scene.Comp.issuedUniform.Count > 0 ? scene.Comp.issuedUniform[0] : null;
                Apparel parked = ParkedGarment(scene);
                bool ready = story.Check(CompShiftStand.OnShiftStandFor(pawn) == scene.Comp, name + " is on shift")
                             & story.Check(uniform != null && pawn.apparel.WornApparel.Contains(uniform),
                                           "and wears the stand's outfit")
                             & story.Check(parked != null && parked.ParentHolder is Building_OutfitStand,
                                           "with their own clothes parked on the stand");
                if (!ready)
                {
                    return;
                }

                // The station across the pad, its interaction cell free, and dye beside it.
                DebugTools_Fixtures.Spawn(scene.Map, ThingDefOf.StylingStation, ThingDefOf.WoodLog,
                    scene.OutfitStand.Position + new IntVec3(2, 0, 4), Rot4.North);
                Thing dye = ThingMaker.MakeThing(ThingDefOf.Dye);
                dye.stackCount = 10;
                GenSpawn.Spawn(dye, scene.OutfitStand.Position + new IntVec3(4, 0, 2), scene.Map);
                uniform.DesiredColor = new Color(0.8f, 0.2f, 0.2f);
                story.Note("a styling station is built across the pad with ten dye beside it, and the "
                           + uniform.def.label + " is marked to be dyed red");
                story.Check(JobGiver_OptimizeApparel.TryCreateRecolorJob(pawn, out _, dryRun: true),
                            "the styling station would recolor it");

                story.Check(!OptimizerJob(pawn).IsValid,
                            "on shift " + name + " does not go to dye the outfit unasked, though it is marked");
                story.Check(RefusedAtReservations(pawn, RecolorJob(pawn, ordered: false, names: null)),
                            "and a recolor they were not ordered to do is refused");

                Job order = RecolorJob(pawn, ordered: false, names: null);
                story.Check(order != null && order.GetTargetQueue(TargetIndex.B).Contains(uniform),
                            "the station's order covers the " + uniform.def.label);
                if (order != null)
                {
                    story.Check(pawn.jobs.TryTakeOrderedJob(order, JobTag.Misc), name + " takes the order on shift");
                    story.Check(pawn.CurJobDef == JobDefOf.RecolorApparel, "and starts recoloring");
                    story.Check(pawn.apparel.WornApparel.Contains(uniform),
                                "still in the stand's outfit, since an order never sends anyone to change");
                    pawn.jobs.StopAll();
                }
                story.Check(RefusedAtReservations(pawn, RecolorJob(pawn, ordered: true, names: parked)),
                            "an order to recolor the clothes on the stand is refused");

                CompShiftStand.OnShiftStands.Remove(pawn);
                story.Note(name + " is taken off shift");
                ThinkResult offShift = OptimizerJob(pawn);
                story.Check(offShift.IsValid && offShift.Job.def == JobDefOf.RecolorApparel
                            && Reserves(pawn, offShift.Job),
                            "off shift, " + name + " goes to recolor unasked, and can reserve the station and dye");
                story.Check(RefusedAtReservations(pawn, RecolorJob(pawn, ordered: false, names: parked)),
                            "but recoloring the clothes on the stand is still refused");
            }
        }

        // What the parked-clothes pair builds and reads.

        /// <summary>The first garment the scene's swap parked.</summary>
        internal static Apparel ParkedGarment(Scene scene)
        {
            if (scene == null || scene.Comp == null)
            {
                return null;
            }
            List<Apparel> parked = scene.Comp.StoredOwnerApparelForReading;
            for (int i = 0; i < parked.Count; i++)
            {
                if (parked[i] != null)
                {
                    return parked[i];
                }
            }
            return null;
        }

        /// <summary>
        /// A sound copy of a garment: same def, stuff and quality, full hit points. Quality
        /// especially, or the copy scores differently from its original and decides both tests for
        /// the wrong reason.
        /// </summary>
        internal static Apparel Duplicate(Apparel source)
        {
            if (source == null)
            {
                return null;
            }
            Apparel copy = ThingMaker.MakeThing(source.def, source.Stuff) as Apparel;
            if (copy == null)
            {
                return null;
            }
            CompQuality sourceQuality = source.TryGetComp<CompQuality>();
            CompQuality copyQuality = copy.TryGetComp<CompQuality>();
            if (sourceQuality != null && copyQuality != null)
            {
                copyQuality.SetQuality(sourceQuality.Quality, ArtGenerationContext.Colony);
            }
            if (copy.def.useHitPoints)
            {
                copy.HitPoints = copy.MaxHitPoints;
            }
            return copy;
        }

        /// <summary>
        /// A spare that provably displaces nothing the scene parked, picked by asking
        /// <c>CanWearTogether</c> rather than assuming a layer. Null when none qualifies; the
        /// caller asserts on that, since a control that cannot be built must fail the test.
        /// </summary>
        internal static Apparel NonConflictingSpare(Scene scene)
        {
            List<Apparel> parked = scene.Comp.StoredOwnerApparelForReading;
            string[] candidates =
            {
                "Apparel_ShieldBelt", "Apparel_SimpleHelmet",
                "Apparel_Pants", "Apparel_BasicShirt",
            };
            for (int c = 0; c < candidates.Length; c++)
            {
                Apparel spare = MakeOne(candidates[c]);
                if (spare == null)
                {
                    continue;
                }
                bool clashes = false;
                for (int i = 0; i < parked.Count && !clashes; i++)
                {
                    if (parked[i] == null)
                    {
                        continue;
                    }
                    clashes = !ApparelUtility.CanWearTogether(
                        parked[i].def, spare.def, scene.Colonist.RaceProps.body);
                }
                if (!clashes)
                {
                    return spare;
                }
            }
            return null;
        }

        internal static Apparel MakeOne(string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null || def.apparel == null)
            {
                return null;
            }
            ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            return ThingMaker.MakeThing(def, stuff) as Apparel;
        }

        /// <summary>
        /// Whether our postfix refused this garment, read by magnitude: vanilla's own refusals
        /// inside <c>ApparelScoreRaw</c> are -10, far shallower than <c>RefusedScore</c>. A mod
        /// that multiplies scores could push one past it, so if this fails on a full modlist, look
        /// for other postfixes on the scorer before blaming the patch.
        /// </summary>
        internal static bool Refused(Pawn pawn, Apparel apparel)
        {
            return JobGiver_OptimizeApparel.ApparelScoreRaw(pawn, apparel)
                   <= Patch_ParkedKitOutscoresSpares.RefusedScore;
        }

        // What the recolor test builds and reads.

        /// <summary>The optimizer's answer for this pawn now, its retry delay cleared.</summary>
        internal static ThinkResult OptimizerJob(Pawn pawn)
        {
            pawn.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame;
            return new JobGiver_OptimizeApparel().TryIssueJobPackage(pawn, default(JobIssueParams));
        }

        /// <summary>
        /// The recolor job the station builds, optionally marked as an order and pointed at one
        /// garment in place of the worn ones.
        /// </summary>
        internal static Job RecolorJob(Pawn pawn, bool ordered, Apparel names)
        {
            if (!JobGiver_OptimizeApparel.TryCreateRecolorJob(pawn, out Job job) || job == null)
            {
                return null;
            }
            job.playerForced = ordered;
            if (names != null)
            {
                List<LocalTargetInfo> queue = job.GetTargetQueue(TargetIndex.B);
                queue.Clear();
                queue.Add(names);
                job.count = 1;
            }
            return job;
        }

        /// <summary>Whether the job's reservations succeed, releasing whatever they took.</summary>
        internal static bool Reserves(Pawn pawn, Job job)
        {
            if (job == null)
            {
                return false;
            }
            bool reserved = job.TryMakePreToilReservations(pawn, errorOnFailed: false);
            pawn.ClearReservationsForJob(job);
            return reserved;
        }

        /// <summary>
        /// Whether a job was built and then failed its reservations. A job that could not be
        /// built is no refusal, so it does not count as one.
        /// </summary>
        internal static bool RefusedAtReservations(Pawn pawn, Job job)
        {
            return job != null && !Reserves(pawn, job);
        }
    }
}
