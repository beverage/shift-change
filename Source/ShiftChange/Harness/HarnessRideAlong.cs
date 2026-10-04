// HARNESS only — see the configuration table in ShiftChange.csproj. The
// harness is dev tooling and does not ship: a Release build compiles this
// file out entirely, and devtools/run-harness.sh asks for it back with
// -p:Harness=true on top of Release codegen.
//
// The guard is whole-file, always. Never put an #if HARNESS inside a file
// that ships — a shipping build and a harness build must differ by the
// presence of these types and by nothing else, or a harness run stops saying
// anything about the assembly that goes out. check-invariants.py enforces it.
#if HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Harness cases for jobs the uniform RIDES ALONG on: errands that are
    /// part of work already under way, where changing out and back in again
    /// costs two wardrobe walks and buys nothing. Feeding a patient or a
    /// prisoner, and a haul slotted in ahead of a bill.
    ///
    /// <para>Each one reached this file the same way: the return trip judged
    /// the errand by where its first target stands, which is the storeroom
    /// for a meal or an ingredient, and sent a colonist who was about to come
    /// straight back to the stand to change first.</para>
    /// </summary>
    internal static class HarnessRideAlong
    {
        /// <summary>
        /// The six vanilla feeding givers, and the job each one builds. Every
        /// one of them puts the FOOD in targetA and the patient or prisoner in
        /// targetB (<c>WorkGiver_FeedPatient.JobOnThing</c>,
        /// <c>WorkGiver_Warden_Feed</c>, <c>WorkGiver_Warden_DeliverFood</c>,
        /// <c>Workgiver_AdministerHemogen</c>,
        /// <c>WorkGiver_Warden_DeliverHemogen</c>), so the room the job reads
        /// as happening in is wherever the meal is stored. The last two rows
        /// are Biotech's and resolve only with it loaded.
        /// </summary>
        internal static readonly string[] FeedPatientGivers =
        {
            "DoctorFeedHumanlikes", "DoctorFeedAnimals", "FeedPrisoner", "FeedHemogen",
        };

        internal static readonly string[] DeliverFoodGivers =
        {
            "DeliverFoodToPrisoner", "DeliverHemogenToPrisoner",
        };

        /// <summary>A feeding job, built the way its giver builds one.</summary>
        internal static Job FeedingJob(JobDef def, Thing food, Pawn patient, WorkGiverDef giver)
        {
            Job job = JobMaker.MakeJob(def, food, patient);
            job.count = 1;
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>
        /// FEEDING RIDES ALONG, in both directions: a meal run neither dresses
        /// anyone nor sends anyone back to change.
        ///
        /// <para>The doctor's half of it: in scrubs, handed a patient to feed,
        /// they changed out to fetch the meal from the freezer and changed
        /// back in for the next tend. The warden's half is the same with a
        /// Warden stand in a prison cell, and delivering food can never read as
        /// happening in the cell, because its giver refuses food already stored
        /// there.</para>
        ///
        /// <para><b>Why not read targetB instead.</b> That would put the job in
        /// the patient's room, which is right for where it ends and wrong for
        /// where it starts: a bare doctor would dress before a trip whose first
        /// leg goes to the freezer. Ignoring the giver is what the chemfuel row
        /// in the same table already does, for the same reason.</para>
        ///
        /// <para>Every refusal is paired with the SAME job shape under a giver
        /// that is not on the list, which still dresses and still changes them
        /// back. Without that pair, a feeding job failing its reservation dry run
        /// would satisfy every negative here.</para>
        /// </summary>
        internal static bool FeedingRidesAlong(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkTypeDef warden = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Warden");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
            if (doctor == null || warden == null || tend == null || mealDef == null)
            {
                return Expect(false, "the doctor, warden and meal defs resolve");
            }
            fix.Comp.ToggleWork(doctor);
            fix.Comp.ToggleWork(warden);
            MakeCalm(fix.Map);

            Map map = fix.Map;
            Room room = fix.Stand.GetRoom();
            Pawn patient = SpawnExtra(fix, Gender.Female, "Patient");
            Thing mealHere = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef),
                                            fix.Stand.Position + new IntVec3(3, 0, 3), map);
            IntVec3 outside = fix.Stand.Position + new IntVec3(8, 0, 0);
            if (!outside.InBounds(map) || outside.GetRoom(map) == room)
            {
                return Expect(false, "a cell outside the stand's room resolves for the test");
            }
            Thing mealAway = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), outside, map);

            List<KeyValuePair<WorkGiverDef, JobDef>> rows = new List<KeyValuePair<WorkGiverDef, JobDef>>();
            foreach (string name in FeedPatientGivers)
            {
                WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(name);
                if (giver != null)
                {
                    rows.Add(new KeyValuePair<WorkGiverDef, JobDef>(giver, JobDefOf.FeedPatient));
                }
            }
            foreach (string name in DeliverFoodGivers)
            {
                WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(name);
                if (giver != null)
                {
                    rows.Add(new KeyValuePair<WorkGiverDef, JobDef>(giver, JobDefOf.DeliverFood));
                }
            }

            bool ok = Expect(fix.Comp.HandlesWork(doctor) && fix.Comp.HandlesWork(warden),
                             "the stand serves doctoring and wardening")
                    & Expect(rows.Count >= 4, "the four Core feeding givers resolve ("
                             + rows.Count + " of six; Biotech supplies the hemogen pair)")
                    & Expect(Patch_JobInterception.TargetCell(
                                 FeedingJob(JobDefOf.FeedPatient, mealAway, patient, tend), map)
                             == mealAway.Position,
                             "a feeding job reads as happening where the FOOD is (the mechanism)");

            // Not yet dressed. The meal is stored IN the stand's room, so the
            // job reads as this room's doctoring or wardening.
            ok &= Expect(Probe(fix, FeedingJob(JobDefOf.FeedPatient, mealHere, patient, tend)),
                         "the same job under a giver NOT on the list dresses (control: the shape can dress)");
            foreach (KeyValuePair<WorkGiverDef, JobDef> row in rows)
            {
                ok &= Expect(!Probe(fix, FeedingJob(row.Value, mealHere, patient, row.Key)),
                             row.Key.defName + " dresses nobody, even with the meal in the stand's room");
            }

            ok &= Expect(RunSwap(fix), "dressed for the shift")
                & Expect(fix.Comp.OnShift, "and is on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }

            // Dressed, and the meal is in storage elsewhere: the trip that
            // used to change them out first.
            ok &= Expect(Probe(fix, FeedingJob(JobDefOf.FeedPatient, mealAway, patient, tend)),
                         "the same job under a giver NOT on the list still changes them back "
                         + "(control: the return trip is live)");
            foreach (KeyValuePair<WorkGiverDef, JobDef> row in rows)
            {
                ok &= Expect(!Probe(fix, FeedingJob(row.Value, mealAway, patient, row.Key)),
                             row.Key.defName + " keeps the uniform on while they fetch a meal from storage");
            }
            return ok & Expect(fix.Comp.OnShift, "and they are still on shift at the end");
        }

        /// <summary>
        /// A job of the given giver at a cell, for the queue. Never started: it
        /// only has to be what sits at the head of the queue when the errand
        /// in front of it arrives.
        /// </summary>
        internal static Job QueuedWork(WorkGiverDef giver, IntVec3 at)
        {
            Job job = JobMaker.MakeJob(JobDefOf.DoBill, at);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>A vanilla haul with no giver, as an opportunistic hauler builds one.</summary>
        internal static Job GiverlessHaul(Thing thing, IntVec3 to)
        {
            Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, thing, to);
            job.count = thing.stackCount;
            job.haulMode = HaulMode.ToCellStorage;
            return job;
        }

        /// <summary>
        /// AN ERRAND SLOTTED IN AHEAD OF WORK THE STAND SERVES KEEPS THE
        /// UNIFORM ON. The shape, with vanilla jobs only, so it runs on every
        /// mod list.
        ///
        /// <para>The shape is a job with no giver arriving while the head of
        /// the queue is a work job this stand serves, in this stand's room.
        /// Vanilla makes it itself: <c>Pawn_JobTracker.StartJob</c> puts the
        /// work job at the front of the queue and starts an opportunistic haul
        /// in its place (<c>TryOpportunisticJob</c>). Common Sense makes it for
        /// bills, and its own case below drives that. Either way the colonist
        /// is coming straight back to the queued work, so the change-out the
        /// return trip used to insert was a round trip to the stand and back
        /// for nothing.</para>
        ///
        /// <para>The rule is narrow on purpose, and the controls are the
        /// narrowness. Nothing queued, work this stand does not serve, served
        /// work in another room, and another errand at the head all still
        /// change them back. A meal break still does too, since it has its own
        /// policy. And it never dresses anyone: a colonist not on shift is
        /// left alone by every job without a giver.</para>
        /// </summary>
        internal static bool DetourAheadOfServedWorkKeepsTheUniform(Fixture fix)
        {
            WorkTypeDef cooking = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cooking");
            WorkGiverDef cookBills = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsCook");
            WorkGiverDef tailorBills = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsMakeApparel");
            ThingDef potatoDef = DefDatabase<ThingDef>.GetNamedSilentFail("RawPotatoes");
            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
            if (cooking == null || cookBills == null || tailorBills == null
                || potatoDef == null || mealDef == null)
            {
                return Expect(false, "the cooking, tailoring, potato and meal defs resolve");
            }
            fix.Comp.ToggleWork(cooking);
            MakeCalm(fix.Map);

            Map map = fix.Map;
            Room room = fix.Stand.GetRoom();
            IntVec3 bench = fix.Stand.Position + new IntVec3(3, 0, 3);
            IntVec3 outside = fix.Stand.Position + new IntVec3(8, 0, 0);
            IntVec3 store = fix.Stand.Position + new IntVec3(8, 0, 2);
            if (!outside.InBounds(map) || !store.InBounds(map) || outside.GetRoom(map) == room)
            {
                return Expect(false, "cells outside the stand's room resolve for the test");
            }
            Thing potatoes = ThingMaker.MakeThing(potatoDef);
            potatoes.stackCount = 10;
            GenSpawn.Spawn(potatoes, outside, map);
            Thing meal = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), outside + new IntVec3(0, 0, 1), map);

            bool ok = Expect(RunSwap(fix), "dressed for the shift")
                    & Expect(fix.Comp.OnShift, "and is on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            JobQueue queue = fix.Pawn.jobs.jobQueue;

            fix.Pawn.jobs.ClearQueuedJobs();
            ok &= Expect(Probe(fix, GiverlessHaul(potatoes, store)),
                         "a haul out of the room with nothing queued behind it changes them back (control)");

            queue.EnqueueFirst(QueuedWork(cookBills, bench));
            ok &= Expect(!Probe(fix, GiverlessHaul(potatoes, store)),
                         "a haul slotted in ahead of a cooking bill in this room keeps the uniform on");

            queue.EnqueueFirst(QueuedWork(tailorBills, bench));
            ok &= Expect(Probe(fix, GiverlessHaul(potatoes, store)),
                         "but not ahead of work this stand does not serve");

            queue.EnqueueFirst(QueuedWork(cookBills, outside));
            ok &= Expect(Probe(fix, GiverlessHaul(potatoes, store)),
                         "nor ahead of the stand's own work in another room");

            queue.EnqueueFirst(GiverlessHaul(potatoes, store));
            ok &= Expect(Probe(fix, GiverlessHaul(potatoes, store)),
                         "nor ahead of another errand");

            queue.EnqueueFirst(QueuedWork(cookBills, bench));
            ok &= Expect(Probe(fix, IngestJob(meal)),
                         "and a meal break still changes them out, bill queued or not");

            ok &= Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.UnloadYourInventory)),
                         "vanilla's UnloadYourInventory keeps the uniform on (it carries no target at all)");

            // Off shift: the rule is about STAYING dressed and nothing else.
            ok &= Expect(RunSwap(fix), "changed back for the dressing half")
                & Expect(!fix.Comp.OnShift, "and is off shift (control)");
            queue.EnqueueFirst(QueuedWork(cookBills, bench));
            ok &= Expect(!Probe(fix, GiverlessHaul(potatoes, store)),
                         "an errand with no giver dresses nobody, whatever is queued behind it");
            return ok & Expect(Probe(fix, QueuedWork(cookBills, bench)),
                               "while the bill itself dresses them (control)");
        }

        /// <summary>
        /// VANILLA'S OWN OPPORTUNISTIC HAUL, driven for real: a crafter in
        /// uniform who picks something up on the way to a bill does it in
        /// uniform.
        ///
        /// <para><see cref="DetourAheadOfServedWorkKeepsTheUniform"/> builds the
        /// shape by hand. This case lets the engine build it. The bill is started
        /// through the pawn's own tracker, so <c>Pawn_JobTracker.StartJob</c>
        /// runs <c>TryOpportunisticJob</c> itself, puts the bill at the front of
        /// the queue and starts the haul in its place, from inside its own
        /// <c>StartJob</c> and therefore through our prefix a second time.</para>
        ///
        /// <para><b>The geometry is vanilla's, and it is tight.</b> The search
        /// takes an item only when it is within half the distance to the bench,
        /// its storage is within 0.6 of that distance of the bench, and the whole
        /// detour stays within 1.7 times the straight walk
        /// (<c>Pawn_JobTracker.TryOpportunisticJob</c>). In a five-cell room with
        /// the item outside the wall, the walk to the bench has to be the room's
        /// full diagonal: the pawn in the north-west corner beside the west wall,
        /// the bench in the south-east corner, the item straight outside the wall
        /// from the pawn, and the stockpile beside the bench. The stand's own cell
        /// would sit nearer the door, but it is pass-through rather than
        /// standable. The two detour limits have about 0.4 of a cell to spare.
        /// The case asks vanilla's search directly first, so a geometry that stops
        /// qualifying fails there with a reason, not later as our bug.</para>
        ///
        /// <para>A crafting spot is the bench because it is one cell: a stove's
        /// position is the middle of three, which cannot reach the corner.</para>
        /// </summary>
        internal static bool VanillaOpportunisticHaulKeepsTheUniform(Fixture fix)
        {
            WorkTypeDef crafting = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting");
            WorkGiverDef spotBills = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsUseCraftingSpot");
            ThingDef spotDef = DefDatabase<ThingDef>.GetNamedSilentFail("CraftingSpot");
            ThingDef potatoDef = DefDatabase<ThingDef>.GetNamedSilentFail("RawPotatoes");
            RecipeDef recipe = spotDef?.AllRecipes?.FirstOrDefault();
            if (crafting == null || spotBills == null || spotDef == null || potatoDef == null
                || recipe == null || fix.Outside.Area == 0)
            {
                return Expect(false, "the crafting and potato defs resolve and the doorway fixture staged");
            }
            Map map = fix.Map;
            Pawn pawn = fix.Pawn;
            fix.Comp.ToggleWork(crafting);
            MakeCalm(map);

            // Relative to the pad's corner, which the doorway strip pins: it runs
            // up the pad's west side, one column out. The stand is at (1, 1) and
            // the door at (0, 1).
            IntVec3 corner = new IntVec3(fix.Outside.minX + 1, 0, fix.Outside.minZ);
            IntVec3 waitAt = corner + new IntVec3(1, 0, 5);
            IntVec3 itemAt = corner + new IntVec3(-1, 0, 5);
            IntVec3 benchAt = corner + new IntVec3(5, 0, 1);
            IntVec3 storeAt = corner + new IntVec3(4, 0, 2);

            // Facing south, so its interaction cell is inside the room rather
            // than in the south wall.
            Building_WorkTable spot = DebugTools_Fixtures.Spawn(map, spotDef, null, benchAt, Rot4.South)
                as Building_WorkTable;
            if (spot == null)
            {
                return Expect(false, "a crafting spot could be staged in the stand's room");
            }
            Bill_Production bill = new Bill_Production(recipe);
            spot.BillStack.AddBill(bill);

            Zone_Stockpile stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,
                                                          map.zoneManager);
            map.zoneManager.RegisterZone(stockpile);
            try
            {
                stockpile.AddCell(storeAt);
                stockpile.settings.Priority = StoragePriority.Critical;
                Thing potatoes = ThingMaker.MakeThing(potatoDef);
                potatoes.stackCount = 10;
                GenSpawn.Spawn(potatoes, itemAt, map);

                bool ok = Expect(RunSwap(fix), "the crafter dressed for the shift")
                        & Expect(fix.Comp.OnShift, "and is on shift (control)");
                if (!fix.Comp.OnShift)
                {
                    return false;
                }
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                pawn.jobs.ClearQueuedJobs();
                pawn.pather?.StopDead();
                pawn.Position = waitAt;

                Job billJob = JobMaker.MakeJob(JobDefOf.DoBill, spot);
                billJob.bill = bill;
                billJob.workGiverDef = spotBills;

                // The engine half, asked directly: vanilla's own search offers a
                // giver-less haul of the potatoes for this bill from here.
                Job offered = pawn.jobs.TryOpportunisticJob(null, billJob);
                bool shaped = offered != null && offered.def == JobDefOf.HaulToCell
                              && offered.targetA.Thing == potatoes && offered.workGiverDef == null;
                ok &= Expect(shaped, "vanilla's opportunistic search offers a haul of the potatoes "
                                     + "ahead of the bill (the shape, from the engine)");
                if (!shaped)
                {
                    Report.Append("      offered: ")
                          .Append(offered == null ? "nothing" : offered.def.defName + " of " + offered.targetA)
                          .Append("; pawn ").Append(pawn.Position).Append(", bench ").Append(benchAt)
                          .Append(", item ").Append(itemAt).Append(", store ").Append(storeAt)
                          .AppendLine();
                    return false;
                }

                // Our half: start the bill as the think tree would, and let
                // vanilla's StartJob make the swap itself.
                pawn.jobs.StartJob(billJob, JobCondition.None, null,
                    resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);
                JobQueue queue = pawn.jobs.jobQueue;
                ok &= Expect(queue.Count > 0 && queue[0].job == billJob,
                             "vanilla put the bill at the front of the queue and started the haul in its place")
                    & Expect(pawn.CurJobDef != ShiftChangeDefOf.ShiftChange_SwapAtStand,
                             "the crafter is NOT sent to change out for it")
                    & Expect(pawn.CurJobDef == JobDefOf.HaulToCell && Hauls(pawn.CurJob, potatoes),
                             "they fetch the potatoes in uniform")
                    & Expect(fix.Comp.OnShift, "and are still on shift for the bill behind it");

                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                pawn.jobs.ClearQueuedJobs();
                Job lone = HaulAIUtility.HaulToStorageJob(pawn, potatoes, false);
                return ok & Expect(lone != null && Probe(fix, lone),
                                   "the same haul with nothing queued behind it still changes them back (control)");
            }
            finally
            {
                stockpile.Delete();
            }
        }

        /// <summary>The job's first target or its first queued one is this thing.</summary>
        internal static bool Hauls(Job job, Thing thing)
        {
            if (job == null || thing == null)
            {
                return false;
            }
            if (job.targetA.Thing == thing)
            {
                return true;
            }
            return job.targetQueueA != null && job.targetQueueA.Any(t => t.Thing == thing);
        }

        /// <summary>
        /// COMMON SENSE'S BILL HAUL KEEPS A COOK IN UNIFORM.
        ///
        /// <para>With its "haul ingredients over doing bills" setting on, which
        /// is how it ships, Common Sense's prefix on <c>StartJob</c> takes a
        /// bill that arrives with an empty queue, and when one of the bill's
        /// ingredients lies outside the bench's room and can be hauled to
        /// storage nearer the cook than the bench is, it queues the haul and
        /// then the bill, and skips the start. The haul carries no giver. Its
        /// prefix runs before ours on any load order: it patches from its
        /// <c>Mod</c> constructor, ours from a static constructor, and a bool
        /// prefix that returns false makes Harmony skip every later one.</para>
        ///
        /// <para>So a cook already in whites took the haul at the next job
        /// boundary, was judged leaving the kitchen, changed out, fetched the
        /// ingredient, and changed back in for the bill.</para>
        ///
        /// <para>Driven end to end through the pawn's own tracker, so Common
        /// Sense's real prefix builds the queue and ours then judges the haul
        /// it hands out. A known gap without the mod.</para>
        /// </summary>
        internal static bool CommonSenseBillHaulKeepsTheUniform(Fixture fix)
        {
            if (!ModsConfig.IsActive("avilmask.CommonSense"))
            {
                return ExpectKnownGap(false, "Common Sense's bill haul keeps a cook in uniform",
                    "Common Sense is not on this mod list; --with=avilmask.commonsense loads it");
            }
            Type settings = AccessTools.TypeByName("CommonSense.Settings");
            FieldInfo setting = settings == null ? null : AccessTools.Field(settings, "hauling_over_bills");
            if (!Expect(setting != null && (bool)setting.GetValue(null),
                        "Common Sense's \"haul ingredients over doing bills\" is on, as it ships"))
            {
                return false;
            }

            WorkTypeDef cooking = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cooking");
            WorkGiverDef cookBills = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsCook");
            ThingDef stoveDef = DefDatabase<ThingDef>.GetNamedSilentFail("FueledStove");
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("CookMealSimple");
            ThingDef potatoDef = DefDatabase<ThingDef>.GetNamedSilentFail("RawPotatoes");
            if (cooking == null || cookBills == null || stoveDef == null || recipe == null
                || potatoDef == null || fix.Outside.Area == 0)
            {
                return Expect(false, "the kitchen defs resolve and the doorway fixture staged");
            }
            Map map = fix.Map;
            fix.Comp.ToggleWork(cooking);
            MakeCalm(map);

            // A kitchen: the stove across the room from the stand, the
            // potatoes on the ground outside, and a stockpile just outside the
            // door, nearer the cook than the stove is. That distance is Common
            // Sense's own condition for queueing the haul at all.
            IntVec3 stoveAt = fix.Stand.Position + new IntVec3(2, 0, 3);
            Building_WorkTable stove = DebugTools_Fixtures.Spawn(map, stoveDef, null, stoveAt, Rot4.North)
                as Building_WorkTable;
            if (stove == null)
            {
                return Expect(false, "a stove could be staged in the stand's room");
            }
            Bill_Production bill = new Bill_Production(recipe);
            stove.BillStack.AddBill(bill);

            IntVec3 storeCell = new IntVec3(fix.Outside.minX, 0, fix.Stand.Position.z);
            IntVec3 potatoCell = new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 1);
            Zone_Stockpile stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,
                                                          map.zoneManager);
            map.zoneManager.RegisterZone(stockpile);
            stockpile.AddCell(storeCell);
            stockpile.settings.Priority = StoragePriority.Critical;
            Thing potatoes = ThingMaker.MakeThing(potatoDef);
            potatoes.stackCount = 10;
            GenSpawn.Spawn(potatoes, potatoCell, map);

            bool ok = Expect(RunSwap(fix), "the cook dressed for the shift")
                    & Expect(fix.Comp.OnShift, "and is on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }

            Job billJob = JobMaker.MakeJob(JobDefOf.DoBill, stove);
            billJob.bill = bill;
            billJob.workGiverDef = cookBills;
            billJob.targetQueueB = new List<LocalTargetInfo> { potatoes };
            billJob.countQueue = new List<int> { potatoes.stackCount };

            Pawn pawn = fix.Pawn;
            pawn.jobs.ClearQueuedJobs();
            pawn.jobs.StartJob(billJob, JobCondition.InterruptForced, null,
                resumeCurJobAfterwards: false, cancelBusyStances: true, null, null);

            // The engine half: what Common Sense really does with that bill.
            JobQueue queue = pawn.jobs.jobQueue;
            Job haul = queue.Count > 0 ? queue[0].job : null;
            bool shaped = queue.Count == 2 && haul != null && haul.workGiverDef == null
                          && Hauls(haul, potatoes) && queue[1].job == billJob;
            ok &= Expect(shaped, "Common Sense queued a haul of the out-of-room ingredient, with no "
                                 + "giver, ahead of the bill (the shape this case is about)");
            if (!shaped)
            {
                Report.Append("      queue: ").AppendLine(string.Join(", ",
                    queue.Select(q => q.job.def.defName + (q.job.workGiverDef == null
                        ? "" : "/" + q.job.workGiverDef.defName)).ToArray()));
                return false;
            }
            JobDef haulDef = haul.def;

            // Our half: the haul starts at the next job boundary, from the
            // queue, through every prefix in their real order.
            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: true);
            ok &= Expect(pawn.CurJobDef != ShiftChangeDefOf.ShiftChange_SwapAtStand,
                         "the cook is NOT sent to change out for Common Sense's haul")
                & Expect(pawn.CurJobDef == haulDef && Hauls(pawn.CurJob, potatoes),
                         "they fetch the ingredient in uniform")
                & Expect(fix.Comp.OnShift, "and are still on shift for the bill behind it");

            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            pawn.jobs.ClearQueuedJobs();
            Job lone = HaulAIUtility.HaulToStorageJob(pawn, potatoes, false);
            return ok & Expect(lone != null && Probe(fix, lone),
                               "the same haul with no bill behind it still changes them back (control)");
        }
    }
}
#endif
