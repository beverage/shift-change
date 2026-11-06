// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
#if HARNESS
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    internal static partial class HarnessCatchments
    {
        /// <summary>The room's far corner, relative to the pad's corner.</summary>
        internal static readonly IntVec3 FarCorner = new IntVec3(5, 0, 5);

        /// <summary>
        /// Where a stonecutter's table and its bill put the stack, and what the stand does about
        /// it, through a real <c>DoBill</c> driven to its end by the engine. The game stores a
        /// finished bill's product with a new haul that names no giver, started with the stack
        /// already in the pawn's hands, so nothing short of the real driver has this shape.
        ///
        /// <para>An area stand's room is painted whole, less the stockpile when
        /// <paramref name="stockpileInside"/> is false. A radius stand's ring reaches the far
        /// corner at 8 and stops short of it at 5, with the bench inside both. A room stand's
        /// outside stockpile is past its door. Keeping the uniform means hauling on shift with the
        /// stack in hand; changing back means the swap started, the haul waits behind it and the
        /// stack is on the ground.</para>
        /// </summary>
        internal static bool BillProductHaul(Fixture fix, CatchmentKind kind, bool tickHauling,
                                             bool stockpileInside, bool keepsUniform)
        {
            BillStage stage = StageBill(fix);
            if (stage == null)
            {
                return false;
            }
            WorkTypeDef hauling = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Hauling");
            if (fix.Comp.HandlesWork(hauling) != tickHauling)
            {
                fix.Comp.ToggleWork(hauling);
            }
            IntVec3 store = stage.Corner + FarCorner;
            if (kind == CatchmentKind.Room && !stockpileInside)
            {
                store = new IntVec3(fix.Outside.minX, 0, fix.Outside.minZ + 3);
            }
            if (kind == CatchmentKind.Area)
            {
                fix.Comp.SetServeArea(PaintRoom(fix, stage, stockpileInside ? IntVec3.Invalid : store));
            }
            else if (kind == CatchmentKind.Radius)
            {
                fix.Comp.SetServes(CatchmentKind.Radius);
                fix.Comp.SetServeRadius(stockpileInside ? 8 : 5);
            }

            Zone_Stockpile stockpile = StageStockpile(fix.Map, store);
            try
            {
                bool ok = Expect(fix.Comp.serves == kind
                                 && fix.Comp.HandlesWork(hauling) == tickHauling
                                 && fix.Comp.Covers(stage.Table.InteractionCell, fix.Map)
                                 && fix.Comp.Covers(store, fix.Map) == stockpileInside,
                                 "the stand serves as staged, covering the bench and "
                                 + (stockpileInside ? "the stockpile" : "not the stockpile")
                                 + " (control)")
                        & Expect(RunSwap(fix) && fix.Comp.OnShift, "the crafter is on shift (control)");
                if (!ok)
                {
                    return false;
                }
                ok &= FinishBill(fix, stage);
                return ok & (keepsUniform ? KeptTheUniform(fix, stage) : ChangedBackFirst(fix, stage));
            }
            finally
            {
                Settle(fix.Pawn);
                stockpile.Delete();
            }
        }

        /// <summary>
        /// A player painting the bench and not the cell in front of it, where the crafter stands
        /// and the stack is made. Only the stockpile decides the haul, so the uniform stays on.
        /// </summary>
        internal static bool BillProductIsJudgedAtTheStockpile(Fixture fix)
        {
            BillStage stage = StageBill(fix);
            if (stage == null)
            {
                return false;
            }
            IntVec3 store = stage.Corner + FarCorner;
            fix.Comp.SetServeArea(PaintRoom(fix, stage, stage.Table.InteractionCell));
            Zone_Stockpile stockpile = StageStockpile(fix.Map, store);
            try
            {
                bool ok = Expect(!fix.Comp.Covers(stage.Table.InteractionCell, fix.Map)
                                 && fix.Comp.Covers(stage.Table.Position, fix.Map)
                                 && fix.Comp.Covers(store, fix.Map),
                                 "the area holds the bench and the stockpile, not the crafter's cell (control)")
                        & Expect(RunSwap(fix) && fix.Comp.OnShift, "the crafter is on shift (control)");
                if (!ok)
                {
                    return false;
                }
                ok &= FinishBill(fix, stage);
                return ok & KeptTheUniform(fix, stage);
            }
            finally
            {
                Settle(fix.Pawn);
                stockpile.Delete();
            }
        }

        /// <summary>
        /// The bill must be work the stand serves: the same haul after a bill it no longer serves
        /// goes by the ordinary rule for a job with no work type, and changes them back with the
        /// stockpile inside the area. Hauling stays ticked throughout, both because it decides
        /// nothing here and because a set emptied of its last work excludes the stand.
        /// </summary>
        internal static bool BillProductOfUnservedWorkChangesThemBack(Fixture fix)
        {
            BillStage stage = StageBill(fix);
            if (stage == null)
            {
                return false;
            }
            IntVec3 store = stage.Corner + FarCorner;
            WorkTypeDef hauling = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Hauling");
            if (!fix.Comp.HandlesWork(hauling))
            {
                fix.Comp.ToggleWork(hauling);
            }
            fix.Comp.SetServeArea(PaintRoom(fix, stage, IntVec3.Invalid));
            Zone_Stockpile stockpile = StageStockpile(fix.Map, store);
            try
            {
                bool staged = Expect(fix.Comp.Covers(store, fix.Map),
                                     "the area holds the stockpile (control)")
                            & Expect(RunSwap(fix) && fix.Comp.OnShift,
                                     "the crafter is on shift (control)");
                if (!staged)
                {
                    return false;
                }
                bool ok = FinishBill(fix, stage, () => fix.Comp.ToggleWork(stage.Crafting));
                return ok & Expect(!fix.Comp.HandlesWork(stage.Crafting),
                                   "the stand stopped serving Crafting during the bill (control)")
                          & ChangedBackFirst(fix, stage);
            }
            finally
            {
                Settle(fix.Pawn);
                stockpile.Delete();
            }
        }

        /// <summary>
        /// A haul that begins by walking to a stack is no bill's product, even with a served bill
        /// under way and the stockpile inside the area, so it goes by the ordinary rule.
        /// </summary>
        internal static bool BillProductMustBeInHand(Fixture fix)
        {
            BillStage stage = StageBill(fix);
            if (stage == null)
            {
                return false;
            }
            Pawn pawn = fix.Pawn;
            fix.Comp.SetServeArea(PaintRoom(fix, stage, IntVec3.Invalid));
            Zone_Stockpile stockpile = StageStockpile(fix.Map, stage.Corner + FarCorner);
            try
            {
                if (!Expect(RunSwap(fix) && fix.Comp.OnShift, "the crafter is on shift (control)")
                    || StartBill(fix, stage) == null)
                {
                    return false;
                }
                Thing stack = GenSpawn.Spawn(ThingMaker.MakeThing(stage.Blocks),
                                             stage.Corner + new IntVec3(3, 0, 3), fix.Map);
                Job haul = HaulAIUtility.HaulToStorageJob(pawn, stack, false);
                return Expect(haul != null && haul.def == JobDefOf.HaulToCell
                              && haul.workGiverDef == null
                              && pawn.carryTracker.CarriedThing == null,
                              "a giver-less haul of a stack on the ground, hands empty (control)")
                     & Expect(haul != null && Probe(fix, haul), "changes them back first");
            }
            finally
            {
                Settle(pawn);
                stockpile.Delete();
            }
        }

        /// <summary>
        /// Only a bill's own haul counts: the same stack in hand, after other work the stand
        /// serves, goes by the ordinary rule.
        /// </summary>
        internal static bool BillProductMustComeFromABill(Fixture fix)
        {
            BillStage stage = StageBill(fix);
            if (stage == null)
            {
                return false;
            }
            Pawn pawn = fix.Pawn;
            IntVec3 store = stage.Corner + FarCorner;
            fix.Comp.SetServeArea(PaintRoom(fix, stage, IntVec3.Invalid));
            Zone_Stockpile stockpile = StageStockpile(fix.Map, store);
            try
            {
                if (!Expect(RunSwap(fix) && fix.Comp.OnShift, "the crafter is on shift (control)"))
                {
                    return false;
                }
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                pawn.jobs.ClearQueuedJobs();
                Job served = JobMaker.MakeJob(JobDefOf.Wait, 600);
                served.workGiverDef = stage.Giver;
                pawn.jobs.StartJob(served, JobCondition.None, null, resumeCurJobAfterwards: false,
                                   cancelBusyStances: true, null, null);
                Thing stack = ThingMaker.MakeThing(stage.Blocks);
                pawn.carryTracker.TryStartCarry(stack);
                Job haul = JobMaker.MakeJob(JobDefOf.HaulToCell, stack, store);
                return Expect(pawn.CurJobDef == JobDefOf.Wait && fix.Comp.OnShift
                              && pawn.carryTracker.CarriedThing == stack,
                              "served work that is no bill, stack in hand (control)")
                     & Expect(Probe(fix, haul), "changes them back first");
            }
            finally
            {
                Settle(pawn);
                pawn.carryTracker.DestroyCarriedThing();
                stockpile.Delete();
            }
        }

        /// <summary>
        /// The other direction is untouched: a crafter in their own clothes who finishes a bill at
        /// a stand ticked for Hauling alone is not sent to dress for storing what they made.
        /// </summary>
        internal static bool BillProductDressesNobody(Fixture fix)
        {
            WorkTypeDef hauling = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Hauling");
            BillStage stage = StageBill(fix);
            if (stage == null)
            {
                return false;
            }
            if (!fix.Comp.HandlesWork(hauling))
            {
                fix.Comp.ToggleWork(hauling);
            }
            fix.Comp.ToggleWork(stage.Crafting);
            fix.Comp.SetServeArea(PaintRoom(fix, stage, IntVec3.Invalid));
            Zone_Stockpile stockpile = StageStockpile(fix.Map, stage.Corner + FarCorner);
            try
            {
                bool ok = Expect(!fix.Comp.OnShift && !fix.Comp.HandlesWork(stage.Crafting),
                                 "the crafter starts off shift at a stand that serves Hauling alone (control)")
                        & FinishBill(fix, stage);
                return ok & Expect(fix.Pawn.CurJobDef == JobDefOf.HaulToCell,
                                   "they haul in their own clothes, sent nowhere to dress")
                          & Expect(!fix.Comp.OnShift, "and are still off shift");
            }
            finally
            {
                Settle(fix.Pawn);
                stockpile.Delete();
            }
        }

        /// <summary>What <see cref="StageBill"/> put on the pad.</summary>
        internal sealed class BillStage
        {
            internal IntVec3 Corner;
            internal Building_WorkTable Table;
            internal Bill_Production Bill;
            internal WorkGiverDef Giver;
            internal WorkTypeDef Crafting;
            internal ThingDef Chunk;
            internal ThingDef Blocks;
        }

        /// <summary>
        /// Ticks Crafting, then stages a stonecutter's table along the room's south wall with one
        /// granite block bill. Ticked before the table lands: it would make an automatic stand
        /// read the room as a workshop.
        /// </summary>
        internal static BillStage StageBill(Fixture fix)
        {
            WorkTypeDef crafting = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting");
            WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsStonecut");
            ThingDef tableDef = DefDatabase<ThingDef>.GetNamedSilentFail("TableStonecutter");
            ThingDef chunkDef = DefDatabase<ThingDef>.GetNamedSilentFail("ChunkGranite");
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("Make_StoneBlocksGranite");
            if (crafting == null || giver == null || tableDef == null || chunkDef == null
                || recipe == null || fix.Outside.Area == 0)
            {
                Expect(false, "the stonecutting defs resolve and the doorway fixture staged");
                return null;
            }
            if (!fix.Comp.HandlesWork(crafting))
            {
                fix.Comp.ToggleWork(crafting);
            }
            MakeCalm(fix.Map);

            // The doorway strip pins the pad's corner: the stand is at (1, 1), the door at (0, 1).
            IntVec3 corner = new IntVec3(fix.Outside.minX + 1, 0, fix.Outside.minZ);
            // Facing south, so its interaction cell is inside the room rather than in the wall.
            Building_WorkTable table = DebugTools_Fixtures.Spawn(
                fix.Map, tableDef, ThingDefOf.WoodLog, corner + new IntVec3(4, 0, 1), Rot4.South)
                as Building_WorkTable;
            if (table == null)
            {
                Expect(false, "a stonecutter's table could be staged in the stand's room");
                return null;
            }
            Bill_Production bill = new Bill_Production(recipe);
            table.BillStack.AddBill(bill);
            return new BillStage
            {
                Corner = corner,
                Table = table,
                Bill = bill,
                Giver = giver,
                Crafting = crafting,
                Chunk = chunkDef,
                Blocks = recipe.products[0].thingDef,
            };
        }

        /// <summary>The room's whole floor as an area, less <paramref name="except"/>.</summary>
        internal static Area_Allowed PaintRoom(Fixture fix, BillStage stage, IntVec3 except)
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
            return NewArea(fix, "Bill product", floor.ToArray());
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

        /// <summary>
        /// Starts a bill through the real driver from the crafter's own cell, with its chunk
        /// already placed, so nothing walks: a pumped pawn's path requests go unanswered under the
        /// stopped clock. Returns the driver, or null if the bill did not start.
        /// </summary>
        internal static JobDriver_DoBill StartBill(Fixture fix, BillStage stage)
        {
            Pawn pawn = fix.Pawn;
            if (pawn.CurJob != null)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }
            pawn.jobs.ClearQueuedJobs();
            pawn.pather?.StopDead();
            pawn.Position = stage.Table.InteractionCell;

            Thing chunk = GenSpawn.Spawn(ThingMaker.MakeThing(stage.Chunk),
                                         stage.Table.InteractionCell + IntVec3.North, fix.Map);
            Job job = JobMaker.MakeJob(JobDefOf.DoBill, stage.Table);
            job.bill = stage.Bill;
            job.workGiverDef = stage.Giver;
            job.haulMode = HaulMode.ToCellNonStorage;
            job.placedThings = new List<ThingCountClass> { new ThingCountClass(chunk, 1) };
            pawn.jobs.StartJob(job, JobCondition.None, null, resumeCurJobAfterwards: false,
                               cancelBusyStances: true, null, null);

            JobDriver_DoBill bench = pawn.jobs.curDriver as JobDriver_DoBill;
            Expect(bench != null, "the bill started in uniform or out of it, as staged (control)");
            return bench;
        }

        /// <summary>
        /// Starts a bill and pumps it until the driver changes, cutting the recipe's work short.
        /// <paramref name="midBill"/> runs once with the bill under way.
        /// </summary>
        internal static bool FinishBill(Fixture fix, BillStage stage, System.Action midBill = null)
        {
            Pawn pawn = fix.Pawn;
            // Watch the driver, never the Job: jobs are pooled, drivers are not.
            JobDriver_DoBill bench = StartBill(fix, stage);
            if (bench == null)
            {
                return false;
            }
            midBill?.Invoke();
            for (int i = 0; i < 2000; i++)
            {
                if (pawn.jobs.curDriver != bench)
                {
                    return true;
                }
                if (bench.workLeft > 1f)
                {
                    bench.workLeft = 1f;
                }
                pawn.DoTick();
            }
            return Expect(false, "the bill finished within the pump");
        }

        /// <summary>The crafter is hauling the stack in their hands, still on shift.</summary>
        internal static bool KeptTheUniform(Fixture fix, BillStage stage)
        {
            Pawn pawn = fix.Pawn;
            Thing carried = pawn.carryTracker.CarriedThing;
            return Expect(pawn.CurJobDef == JobDefOf.HaulToCell,
                          "the haul starts, with no swap ahead of it (" + pawn.CurJobDef?.defName + ")")
                 & Expect(carried != null && carried.def == stage.Blocks, "with the stack in hand")
                 & Expect(fix.Comp.OnShift, "still on shift");
        }

        /// <summary>The swap started, the stack is down and the haul waits behind it.</summary>
        internal static bool ChangedBackFirst(Fixture fix, BillStage stage)
        {
            Pawn pawn = fix.Pawn;
            JobQueue queue = pawn.jobs.jobQueue;
            return Expect(pawn.CurJobDef == ShiftChangeDefOf.ShiftChange_SwapAtStand,
                          "the crafter goes to change back first (" + pawn.CurJobDef?.defName + ")")
                 & Expect(queue.Count > 0 && queue[0].job.def == JobDefOf.HaulToCell,
                          "with the haul waiting behind the swap")
                 & Expect(pawn.carryTracker.CarriedThing == null
                          && fix.Map.listerThings.ThingsOfDef(stage.Blocks).Count > 0,
                          "and the stack on the ground, to fetch in their own clothes");
        }
    }
}
#endif
