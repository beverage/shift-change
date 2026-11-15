using System;
using System.Collections;
using System.Collections.Generic;
using DevTools.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Carrying what a bill made to its stockpile, and when that trip keeps a crafter in uniform:
    /// a stonecutter's table and its bill for granite blocks, driven to its end by the game's own
    /// bill driver on the game's clock. The game stores a finished bill's product with a new haul
    /// that names no work giver, started with the stack already in the crafter's hands, so nothing
    /// short of the real driver has this shape.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.StoringWhatTheyMade)]
    internal class StoringWhatTheyMade : StoryFixture
    {
        /// <summary>
        /// What <see cref="SceneKit.StageWithDoorway(Map, CellRect, WorkTypeDef)"/> sets up
        /// with a colonist who can do the stonecutting bill's work (<see cref="BillWork"/>).
        /// </summary>
        internal const string BesideADoor =
            "a stand holding a duster in a walled, roofed room, a door beside it to open ground, "
            + "and a colonist able to cut stone blocks beside the stand in a shirt, trousers and a parka";

        /// <summary>
        /// The stonecutting bill's own work: Crafting, or Stone Cut with Complex Jobs.
        /// </summary>
        internal static WorkTypeDef BillWork
            => Giver("DoBillsStonecut")?.workType;

        /// <summary>
        /// An allowed area painted over the whole room, the stockpile in its far corner, and
        /// Hauling ticked: the crafter carries the blocks off in uniform
        /// (<see cref="StoreTheBlocks"/>).
        /// </summary>
        [Test]
        [TestDescription("a crafter in uniform carries the granite blocks they made to a stockpile "
                         + "inside the stand's allowed area without changing back")]
        internal IEnumerator BlocksForAStockpileInTheAreaKeepTheUniformOn()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, BillWork), BesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                StoreTheBlocks(story, scene, CatchmentKind.Area, true, true, true);
            }
        }

        /// <summary>
        /// A radius of 8, which reaches the room's far corner and the stockpile in it, and Hauling
        /// ticked: the crafter carries the blocks off in uniform (<see cref="StoreTheBlocks"/>).
        /// </summary>
        [Test]
        [TestDescription("a crafter in uniform carries the granite blocks they made to a stockpile "
                         + "within the stand's radius without changing back")]
        internal IEnumerator BlocksForAStockpileInTheRadiusKeepTheUniformOn()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, BillWork), BesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                StoreTheBlocks(story, scene, CatchmentKind.Radius, true, true, true);
            }
        }

        /// <summary>
        /// The area again with Hauling not ticked: the bill's own work is what the stand serves,
        /// and storing what it made goes with it (<see cref="StoreTheBlocks"/>).
        /// </summary>
        [Test]
        [TestDescription("and carries them there in uniform with Hauling unticked on the stand, "
                         + "since the bill's own work is what it serves")]
        internal IEnumerator BlocksForAStockpileInTheAreaKeepItOnWithoutHauling()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, BillWork), BesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                StoreTheBlocks(story, scene, CatchmentKind.Area, false, true, true);
            }
        }

        /// <summary>
        /// The area painted over the whole room but the stockpile's cell, and Hauling ticked: the
        /// crafter changes back before the haul (<see cref="StoreTheBlocks"/>).
        /// </summary>
        [Test]
        [TestDescription("a crafter in uniform whose blocks are bound for a stockpile outside the "
                         + "stand's allowed area changes back before carrying them")]
        internal IEnumerator BlocksForAStockpilePastTheAreaChangeThemBack()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, BillWork), BesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                StoreTheBlocks(story, scene, CatchmentKind.Area, true, false, false);
            }
        }

        /// <summary>
        /// A radius of 5, which takes in the stonecutter's table and stops short of the room's far
        /// corner and the stockpile in it: the crafter changes back before the haul
        /// (<see cref="StoreTheBlocks"/>).
        /// </summary>
        [Test]
        [TestDescription("a crafter in uniform whose blocks are bound for a stockpile outside the "
                         + "stand's radius changes back before carrying them")]
        internal IEnumerator BlocksForAStockpilePastTheRadiusChangeThemBack()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, BillWork), BesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                StoreTheBlocks(story, scene, CatchmentKind.Radius, true, false, false);
            }
        }

        /// <summary>
        /// The area that leaves out the stockpile, with Hauling not ticked: the crafter changes
        /// back before the haul all the same (<see cref="StoreTheBlocks"/>).
        /// </summary>
        [Test]
        [TestDescription("and changes back before carrying them with Hauling unticked as well")]
        internal IEnumerator BlocksForAStockpilePastTheAreaChangeThemBackWithoutHauling()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, BillWork), BesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                StoreTheBlocks(story, scene, CatchmentKind.Area, false, false, false);
            }
        }

        /// <summary>
        /// A stand serving its room, the stockpile in the room's far corner: the crafter carries
        /// the blocks off in uniform (<see cref="StoreTheBlocks"/>).
        /// </summary>
        [Test]
        [TestDescription("a stand serving its room keeps a crafter in uniform while they carry "
                         + "their blocks to a stockpile in that room")]
        internal IEnumerator ARoomStandKeepsItOnForAStockpileInTheRoom()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, BillWork), BesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                StoreTheBlocks(story, scene, CatchmentKind.Room, true, true, true);
            }
        }

        /// <summary>
        /// A stand serving its room, the stockpile out past its door: the crafter still carries
        /// the blocks off in uniform, as room stands always have (<see cref="StoreTheBlocks"/>).
        /// </summary>
        [Test]
        [TestDescription("and keeps them in uniform carrying blocks to a stockpile out past the room's "
                         + "door, as room stands always have")]
        internal IEnumerator ARoomStandKeepsItOnForAStockpilePastItsDoor()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, BillWork), BesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                StoreTheBlocks(story, scene, CatchmentKind.Room, true, false, true);
            }
        }

        /// <summary>
        /// A player painting the bench and not the cell in front of it, where the crafter stands
        /// and the stack is made. Only the stockpile decides the haul, so the uniform stays on.
        /// </summary>
        [Test]
        [TestDescription("what a bill made is judged by the stockpile it goes to, so an area that "
                         + "leaves out the spot at the table still keeps the crafter in uniform")]
        internal IEnumerator TheStockpileDecidesNotTheSpotAtTheTable()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, BillWork), BesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                BillStage stage = StageBill(story, scene);
                if (stage == null)
                {
                    return;
                }
                IntVec3 store = stage.Corner + FarCorner;
                scene.Comp.SetServeArea(PaintRoom(scene, stage, stage.Table.InteractionCell));
                story.Note("the stand serves an allowed area painted over the whole room, the stonecutter's "
                           + "table too, all but the spot in front of it where " + name + " works");
                Zone_Stockpile stockpile = StageStockpile(scene.Map, store);
                story.Note("a one-cell stockpile at critical priority stands in the room's far corner");
                try
                {
                    bool painted = story.Check(!scene.Comp.Covers(stage.Table.InteractionCell, scene.Map)
                                               && scene.Comp.Covers(stage.Table.Position, scene.Map)
                                               && scene.Comp.Covers(store, scene.Map),
                                               "the area takes in the stonecutter's table and the stockpile, "
                                               + "and not the spot where " + name + " works");
                    bool onShift = story.Check(SwapNow(story, scene) && scene.Comp.OnShift,
                                               name + " changes into the stand's duster and is on shift");
                    if (!painted || !onShift)
                    {
                        return;
                    }
                    FinishBill(story, scene, stage);
                    KeptTheUniform(story, scene, stage);
                }
                finally
                {
                    ClearSwapDecision(scene.Colonist);
                    stockpile.Delete();
                }
            }
        }

        /// <summary>
        /// The bill must be work the stand serves: the same haul after a bill it no longer serves
        /// goes by the ordinary rule for a job with no work type, and changes them back with the
        /// stockpile inside the area. Hauling stays ticked throughout, both because it decides
        /// nothing here and because a set emptied of its last work excludes the stand.
        /// </summary>
        [Test]
        [TestDescription("once the stand stops serving the bill's work in the middle of the bill, "
                         + "the blocks it made go by the usual rule and the crafter changes back first")]
        internal IEnumerator ABillTheStandStopsServingChangesThemBack()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, BillWork), BesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                BillStage stage = StageBill(story, scene);
                if (stage == null)
                {
                    return;
                }
                IntVec3 store = stage.Corner + FarCorner;
                WorkTypeDef hauling = WorkType("Hauling");
                if (!scene.Comp.HandlesWork(hauling))
                {
                    scene.Comp.ToggleWork(hauling);
                }
                story.Note("Hauling is ticked on the stand too");
                scene.Comp.SetServeArea(PaintRoom(scene, stage, IntVec3.Invalid));
                story.Note("the stand serves an allowed area painted over the whole room");
                Zone_Stockpile stockpile = StageStockpile(scene.Map, store);
                story.Note("a one-cell stockpile at critical priority stands in the room's far corner");
                try
                {
                    bool covered = story.Check(scene.Comp.Covers(store, scene.Map),
                                               "the area takes in the stockpile");
                    bool onShift = story.Check(SwapNow(story, scene) && scene.Comp.OnShift,
                                               name + " changes into the stand's duster and is on shift");
                    if (!covered || !onShift)
                    {
                        return;
                    }
                    FinishBill(story, scene, stage, () =>
                    {
                        scene.Comp.ToggleWork(stage.Work);
                        story.Note(Ticked(stage.Work) + " is unticked on the stand with the bill under way");
                    });
                    story.Check(!scene.Comp.HandlesWork(stage.Work),
                                "the stand no longer serves " + Ticked(stage.Work) + ", the bill's work");
                    ChangedBackFirst(story, scene, stage);
                }
                finally
                {
                    ClearSwapDecision(scene.Colonist);
                    stockpile.Delete();
                }
            }
        }

        /// <summary>
        /// A haul that begins by walking to a stack is no bill's product, even with a served bill
        /// under way and the stockpile inside the area, so it goes by the ordinary rule.
        /// </summary>
        [Test]
        [TestDescription("a haul of blocks picked up off the floor counts as no bill's product, even "
                         + "with a bill the stand serves under way, and changes the crafter back")]
        internal IEnumerator BlocksPickedUpOffTheFloorAreNoBillsProduct()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, BillWork), BesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                BillStage stage = StageBill(story, scene);
                if (stage == null)
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                scene.Comp.SetServeArea(PaintRoom(scene, stage, IntVec3.Invalid));
                story.Note("the stand serves an allowed area painted over the whole room");
                Zone_Stockpile stockpile = StageStockpile(
                    scene.Map, stage.Corner + FarCorner);
                story.Note("a one-cell stockpile at critical priority stands in the room's far corner");
                try
                {
                    if (!story.Check(SwapNow(story, scene) && scene.Comp.OnShift,
                                     name + " changes into the stand's duster and is on shift")
                        || StartBill(story, scene, stage) == null)
                    {
                        return;
                    }
                    Thing stack = GenSpawn.Spawn(ThingMaker.MakeThing(stage.Blocks),
                                                 stage.Corner + new IntVec3(3, 0, 3), scene.Map);
                    story.Note("a stack of granite blocks is put on the floor in the middle of the room");
                    Job haul = HaulAIUtility.HaulToStorageJob(pawn, stack, false);
                    story.Check(haul != null && haul.def == JobDefOf.HaulToCell
                                && haul.workGiverDef == null
                                && pawn.carryTracker.CarriedThing == null,
                                "the game would have " + name + " haul that stack to storage, a haul that "
                                + "names no work, with their hands empty");
                    story.Check(haul != null && SendsToChange(scene, haul),
                                "that haul sends " + name + " to the stand to change back first");
                }
                finally
                {
                    ClearSwapDecision(pawn);
                    stockpile.Delete();
                }
            }
        }

        /// <summary>
        /// Only a bill's own haul counts: the same stack in hand, after other work the stand
        /// serves, goes by the ordinary rule.
        /// </summary>
        [Test]
        [TestDescription("blocks in hand after other work the stand serves, not a bill, count as no "
                         + "bill's product and change the crafter back")]
        internal IEnumerator BlocksInHandFromOtherWorkAreNoBillsProduct()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, BillWork), BesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                BillStage stage = StageBill(story, scene);
                if (stage == null)
                {
                    return;
                }
                Pawn pawn = scene.Colonist;
                string name = pawn.LabelShort;
                IntVec3 store = stage.Corner + FarCorner;
                scene.Comp.SetServeArea(PaintRoom(scene, stage, IntVec3.Invalid));
                story.Note("the stand serves an allowed area painted over the whole room");
                Zone_Stockpile stockpile = StageStockpile(scene.Map, store);
                story.Note("a one-cell stockpile at critical priority stands in the room's far corner");
                try
                {
                    if (!story.Check(SwapNow(story, scene) && scene.Comp.OnShift,
                                     name + " changes into the stand's duster and is on shift"))
                    {
                        return;
                    }
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    pawn.jobs.ClearQueuedJobs();
                    Job served = JobMaker.MakeJob(JobDefOf.Wait, 600);
                    served.workGiverDef = stage.Giver;
                    pawn.jobs.StartJob(served, JobCondition.None, null, resumeCurJobAfterwards: false,
                                       cancelBusyStances: true, null, null);
                    story.Note(name + " is set to stand and wait as stonecutting work, with no bill");
                    Thing stack = ThingMaker.MakeThing(stage.Blocks);
                    pawn.carryTracker.TryStartCarry(stack);
                    story.Note(name + " is handed a stack of granite blocks");
                    Job haul = JobMaker.MakeJob(JobDefOf.HaulToCell, stack, store);
                    story.Check(pawn.CurJobDef == JobDefOf.Wait && scene.Comp.OnShift
                                && pawn.carryTracker.CarriedThing == stack,
                                name + " is on shift, at work the stand serves that is no bill, "
                                + "with the blocks in hand");
                    story.Check(SendsToChange(scene, haul),
                                "a haul of those blocks to the stockpile sends " + name
                                + " to the stand to change back first");
                }
                finally
                {
                    ClearSwapDecision(pawn);
                    pawn.carryTracker.DestroyCarriedThing();
                    stockpile.Delete();
                }
            }
        }

        /// <summary>
        /// The other direction is untouched: a crafter in their own clothes who finishes a bill at
        /// a stand ticked for Hauling alone is not sent to dress for storing what they made.
        /// </summary>
        [Test]
        [TestDescription("a crafter in their own clothes who finishes a bill at a stand serving "
                         + "Hauling alone is not sent to dress to store the blocks")]
        internal IEnumerator ACrafterInTheirOwnClothesIsNotDressedToStoreIt()
        {
            return Staged((map, pad) => StageWithDoorway(map, pad, BillWork), BesideADoor, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                WorkTypeDef hauling = WorkType("Hauling");
                BillStage stage = StageBill(story, scene);
                if (stage == null)
                {
                    return;
                }
                if (!scene.Comp.HandlesWork(hauling))
                {
                    scene.Comp.ToggleWork(hauling);
                }
                scene.Comp.ToggleWork(stage.Work);
                story.Note("Hauling is ticked on the stand and " + Ticked(stage.Work)
                           + " unticked again, so it serves Hauling alone");
                scene.Comp.SetServeArea(PaintRoom(scene, stage, IntVec3.Invalid));
                story.Note("the stand serves an allowed area painted over the whole room");
                Zone_Stockpile stockpile = StageStockpile(
                    scene.Map, stage.Corner + FarCorner);
                story.Note("a one-cell stockpile at critical priority stands in the room's far corner");
                try
                {
                    story.Check(!scene.Comp.OnShift && !scene.Comp.HandlesWork(stage.Work),
                                name + " starts off shift, in their own clothes, at a stand that serves "
                                + "Hauling alone");
                    FinishBill(story, scene, stage);
                    story.Check(scene.Colonist.CurJobDef == JobDefOf.HaulToCell,
                                name + " hauls what they made in their own clothes, sent nowhere to dress");
                    story.Check(!scene.Comp.OnShift, "and is still off shift");
                }
                finally
                {
                    ClearSwapDecision(scene.Colonist);
                    stockpile.Delete();
                }
            }
        }

        /// <summary>
        /// Where a stonecutter's table and its bill put the stack, and what the stand does about
        /// it, through the game's own bill driver run to its end on the game's clock.
        ///
        /// <para>An area stand's room is painted whole, less the stockpile when
        /// <paramref name="stockpileInside"/> is false. A radius stand's ring reaches the far
        /// corner at 8 and stops short of it at 5, with the bench inside both. A room stand's
        /// outside stockpile is past its door. Keeping the uniform means hauling on shift with the
        /// stack in hand; changing back means the change at the stand started, the haul waits
        /// behind it and the stack is on the ground.</para>
        /// </summary>
        internal static void StoreTheBlocks(Story story, Scene scene, CatchmentKind kind, bool tickHauling,
                                            bool stockpileInside, bool keepsUniform)
        {
            string name = scene.Colonist.LabelShort;
            BillStage stage = StageBill(story, scene);
            if (stage == null)
            {
                return;
            }
            WorkTypeDef hauling = WorkType("Hauling");
            if (scene.Comp.HandlesWork(hauling) != tickHauling)
            {
                scene.Comp.ToggleWork(hauling);
            }
            story.Note("Hauling is " + (tickHauling ? "ticked" : "unticked") + " on the stand");
            IntVec3 store = stage.Corner + FarCorner;
            bool pastTheDoor = kind == CatchmentKind.Room && !stockpileInside;
            if (pastTheDoor)
            {
                store = new IntVec3(scene.Outside.minX, 0, scene.Outside.minZ + 3);
            }
            string serving;
            if (kind == CatchmentKind.Area)
            {
                scene.Comp.SetServeArea(PaintRoom(scene, stage, stockpileInside ? IntVec3.Invalid : store));
                serving = "the stand serves the allowed area";
                story.Note(stockpileInside
                               ? "the stand serves an allowed area painted over the whole room"
                               : "the stand serves an allowed area painted over the whole room but its far corner");
            }
            else if (kind == CatchmentKind.Radius)
            {
                scene.Comp.SetServes(CatchmentKind.Radius);
                scene.Comp.SetServeRadius(stockpileInside ? 8 : 5);
                serving = "the stand serves within " + (stockpileInside ? 8 : 5) + " cells";
                story.Note(serving);
            }
            else
            {
                serving = "the stand serves its room";
                story.Note(serving);
            }

            Zone_Stockpile stockpile = StageStockpile(scene.Map, store);
            story.Note("a one-cell stockpile at critical priority stands "
                       + (pastTheDoor ? "outside the room, past the door" : "in the room's far corner"));
            try
            {
                bool asSet = story.Check(scene.Comp.serves == kind
                                         && scene.Comp.HandlesWork(hauling) == tickHauling
                                         && scene.Comp.Covers(stage.Table.InteractionCell, scene.Map)
                                         && scene.Comp.Covers(store, scene.Map) == stockpileInside,
                                         serving + " with Hauling " + (tickHauling ? "ticked" : "unticked")
                                         + ", taking in the spot where " + name + " works the stonecutter's "
                                         + "table " + (stockpileInside ? "and the stockpile" : "but not the stockpile"));
                bool onShift = story.Check(SwapNow(story, scene) && scene.Comp.OnShift,
                                           name + " changes into the stand's duster and is on shift");
                if (!asSet || !onShift)
                {
                    return;
                }
                FinishBill(story, scene, stage);
                if (keepsUniform)
                {
                    KeptTheUniform(story, scene, stage);
                }
                else
                {
                    ChangedBackFirst(story, scene, stage);
                }
            }
            finally
            {
                ClearSwapDecision(scene.Colonist);
                stockpile.Delete();
            }
        }

        /// <summary>
        /// Ticks the bill giver's own work type, then puts a stonecutter's table along the room's
        /// south wall with one bill for granite blocks. The giver's work is Crafting, or Stone Cut
        /// with Complex Jobs, which moves it. Ticked before the table lands: it would make an
        /// automatic stand read the room as a workshop. Null, the test failed, when it cannot.
        /// </summary>
        internal static BillStage StageBill(Story story, Scene scene)
        {
            WorkGiverDef giver = Giver("DoBillsStonecut");
            WorkTypeDef work = giver?.workType;
            ThingDef tableDef = DefDatabase<ThingDef>.GetNamedSilentFail("TableStonecutter");
            ThingDef chunkDef = DefDatabase<ThingDef>.GetNamedSilentFail("ChunkGranite");
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("Make_StoneBlocksGranite");
            if (work == null || tableDef == null || chunkDef == null
                || recipe == null || scene.Outside.Area == 0)
            {
                story.Check(false, "the game has a stonecutter's table, its bill for granite blocks and "
                                   + "granite chunks, and the stand's room has a door to open ground");
                return null;
            }
            if (!scene.Comp.HandlesWork(work))
            {
                scene.Comp.ToggleWork(work);
            }
            MakeCalm(scene.Map);
            story.Note(Ticked(work) + ", the stonecutting bill's work, is ticked on the stand, and the map is calm");

            // The doorway strip pins the pad's corner: the stand is at (1, 1), the door at (0, 1).
            IntVec3 corner = new IntVec3(scene.Outside.minX + 1, 0, scene.Outside.minZ);
            // Facing south, so its interaction cell is inside the room rather than in the wall.
            Building_WorkTable table = DebugTools_Fixtures.Spawn(
                scene.Map, tableDef, ThingDefOf.WoodLog, corner + new IntVec3(4, 0, 1), Rot4.South)
                as Building_WorkTable;
            if (table == null)
            {
                story.Check(false, "a stonecutter's table can be built in the stand's room");
                return null;
            }
            Bill_Production bill = new Bill_Production(recipe);
            table.BillStack.AddBill(bill);
            story.Note("a stonecutter's table stands along the room's south wall, with a bill to make granite blocks");
            return new BillStage
            {
                Corner = corner,
                Table = table,
                Bill = bill,
                Giver = giver,
                Work = work,
                Chunk = chunkDef,
                Blocks = recipe.products[0].thingDef,
            };
        }

        /// <summary>
        /// Starts the bill through the game's own driver from the crafter's own cell, with its
        /// chunk already placed, so the bill goes straight to work with no fetch. Returns the
        /// driver, or null, the test failed, if the bill did not start.
        /// </summary>
        internal static JobDriver_DoBill StartBill(Story story, Scene scene, BillStage stage)
        {
            Pawn pawn = scene.Colonist;
            if (pawn.CurJob != null)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }
            pawn.jobs.ClearQueuedJobs();
            pawn.pather?.StopDead();
            pawn.Position = stage.Table.InteractionCell;

            Thing chunk = GenSpawn.Spawn(ThingMaker.MakeThing(stage.Chunk),
                                         stage.Table.InteractionCell + IntVec3.North, scene.Map);
            Job job = JobMaker.MakeJob(JobDefOf.DoBill, stage.Table);
            job.bill = stage.Bill;
            job.workGiverDef = stage.Giver;
            job.haulMode = HaulMode.ToCellNonStorage;
            job.placedThings = new List<ThingCountClass> { new ThingCountClass(chunk, 1) };
            pawn.jobs.StartJob(job, JobCondition.None, null, resumeCurJobAfterwards: false,
                               cancelBusyStances: true, null, null);

            JobDriver_DoBill bench = pawn.jobs.curDriver as JobDriver_DoBill;
            story.Check(bench != null, pawn.LabelShort + " starts the bill at the stonecutter's table, "
                                       + "a granite chunk set beside them");
            return bench;
        }

        /// <summary>
        /// Starts the bill and steps the game's clock until its driver changes, cutting the
        /// recipe's work short. <paramref name="midBill"/> runs once with the bill under way.
        /// False, the test failed, when the bill did not start or finish.
        /// </summary>
        internal static bool FinishBill(Story story, Scene scene, BillStage stage,
                                        Action midBill = null)
        {
            Pawn pawn = scene.Colonist;
            // Watch the driver, never the Job: jobs are pooled, drivers are not.
            JobDriver_DoBill bench = StartBill(story, scene, stage);
            if (bench == null)
            {
                return false;
            }
            midBill?.Invoke();
            ClockRun run = null;
            if (pawn.jobs.curDriver == bench)
            {
                run = GameClock.Step(() => pawn.jobs?.curDriver != bench, 2000, () =>
                {
                    scene.Hold?.Invoke();
                    if (bench.workLeft > 1f)
                    {
                        bench.workLeft = 1f;
                    }
                });
            }
            if (pawn.jobs?.curDriver != bench)
            {
                story.Note(pawn.LabelShort + " finishes the bill"
                           + (run == null ? "" : ", its work cut short, in " + run.Ticks + " ticks"));
                return true;
            }
            return story.Check(false, "the bill is done within 2,000 ticks");
        }

        /// <summary>The crafter is hauling the stack in their hands, still on shift.</summary>
        internal static void KeptTheUniform(Story story, Scene scene, BillStage stage)
        {
            Pawn pawn = scene.Colonist;
            string name = pawn.LabelShort;
            Thing carried = pawn.carryTracker.CarriedThing;
            story.Check(pawn.CurJobDef == JobDefOf.HaulToCell,
                        name + " sets off to store what they made, with no change at the stand ahead of it ("
                        + Doing(pawn) + ")");
            story.Check(carried != null && carried.def == stage.Blocks, "the granite blocks in hand");
            story.Check(scene.Comp.OnShift, name + " is still on shift");
        }

        /// <summary>The change at the stand started, the stack is down and the haul waits behind it.</summary>
        internal static void ChangedBackFirst(Story story, Scene scene, BillStage stage)
        {
            Pawn pawn = scene.Colonist;
            string name = pawn.LabelShort;
            JobQueue queue = pawn.jobs.jobQueue;
            story.Check(pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                        name + " goes to the stand to change back first (" + Doing(pawn) + ")");
            story.Check(queue.Count > 0 && queue[0].job.def == JobDefOf.HaulToCell,
                        "with storing what they made waiting behind the change");
            story.Check(pawn.carryTracker.CarriedThing == null
                        && scene.Map.listerThings.ThingsOfDef(stage.Blocks).Count > 0,
                        "and the granite blocks set down, to be fetched in their own clothes");
        }

        /// <summary>A work type as the stand's work list shows it, such as "Crafting".</summary>
        internal static string Ticked(WorkTypeDef work)
        {
            return work == null ? "the bill's work" : WorkTypeLabels.Of(work).CapitalizeFirst();
        }

        /// <summary>What a colonist is doing, in the words the game shows under their portrait.</summary>
        internal static string Doing(Pawn pawn)
        {
            JobDriver driver = pawn.jobs?.curDriver;
            return driver == null ? "doing nothing" : "now " + driver.GetReport().TrimEnd('.').UncapitalizeFirst();
        }

        /// <summary>The room's far corner, relative to the pad's corner.</summary>
        internal static readonly IntVec3 FarCorner = new IntVec3(5, 0, 5);

        /// <summary>What <see cref="StageBill"/> put on the pad.</summary>
        internal sealed class BillStage
        {
            internal IntVec3 Corner;
            internal Building_WorkTable Table;
            internal Bill_Production Bill;
            internal WorkGiverDef Giver;
            internal WorkTypeDef Work;
            internal ThingDef Chunk;
            internal ThingDef Blocks;
        }

        /// <summary>The room's whole floor as an area, less <paramref name="except"/>.</summary>
        internal static Area_Allowed PaintRoom(Scene scene, BillStage stage, IntVec3 except)
        {
            List<IntVec3> floor = new List<IntVec3>();
            for (int x = 1; x <= 5; x++)
            {
                for (int z = 1; z <= 5; z++)
                {
                    IntVec3 cell = stage.Corner + new IntVec3(x, 0, z);
                    if (cell != except)
                    {
                        floor.Add(cell);
                    }
                }
            }
            return CatchmentKit.NewArea(scene, "Bill product", floor.ToArray());
        }

        /// <summary>A one-cell stockpile at critical priority, so the product goes there.</summary>
        internal static Zone_Stockpile StageStockpile(Map map, IntVec3 cell)
        {
            Zone_Stockpile stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,
                                                          map.zoneManager);
            map.zoneManager.RegisterZone(stockpile);
            stockpile.AddCell(cell);
            stockpile.settings.Priority = StoragePriority.Critical;
            return stockpile;
        }
    }
}
