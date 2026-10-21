// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
#if HARNESS
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    internal static partial class HarnessCatchments
    {
        /// <summary>
        /// A delivery dresses nobody at a radius stand, and a dressed colonist keeps the
        /// uniform of the delivery's own work on: with the stack past the ring and the site
        /// inside, since the stand serves that work, and changing back now for a site past it.
        /// The site jobs the same givers hand out (removing an old floor) are ordinary work, and
        /// a haul vanilla puts ahead of a queued delivery keeps the uniform on, where the same
        /// haul alone changes them back. Each delivery is built with its giver's job def
        /// (<see cref="HarnessTables.DeliveryDefFor"/>), for Core's five delivery-class givers
        /// and the seed planter; Complex Jobs' two are a known gap without that mod. The stand
        /// ticks each giver's work type, so a refusal is the delivery rule and not an unserved job.
        /// </summary>
        internal static bool DeliveriesKeepTheirOwnWorkOn(Fixture fix)
        {
            WorkTypeDef construction = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Construction");
            WorkTypeDef growing = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Growing");
            WorkGiverDef finish = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructFinishFrames");
            WorkGiverDef sow = DefDatabase<WorkGiverDef>.GetNamedSilentFail("GrowerSow");
            WorkGiverDef blueprints = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToBlueprints");
            if (construction == null || growing == null || finish == null || sow == null || blueprints == null)
            {
                return Expect(false, "the construction and growing defs resolve");
            }
            fix.Comp.ToggleWork(construction);
            fix.Comp.ToggleWork(growing);

            List<WorkGiverDef> givers = new List<WorkGiverDef>();
            List<string> names = new List<string>(HarnessTables.OfficialDeliveryGivers) { JobRoomTargets.SeedPlanter };
            foreach (string name in names)
            {
                WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(name);
                if (giver != null)
                {
                    givers.Add(giver);
                }
            }
            bool complexJobs = ModsConfig.IsActive("FrozenSnowFox.ComplexJobs");
            int complexFound = 0;
            if (complexJobs)
            {
                foreach (string name in new[] { "FSFDeliverResourcesToFrames", "FSFDeliverResourcesToBlueprints" })
                {
                    WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(name);
                    if (giver != null)
                    {
                        givers.Add(giver);
                        complexFound++;
                    }
                }
            }
            // Each giver's own work type, whatever a mod has repointed it to: Complex Jobs moves
            // Core's two hauler deliveries to FSFDeliver.
            foreach (WorkGiverDef giver in givers)
            {
                if (giver.workType != null && !fix.Comp.HandlesWork(giver.workType))
                {
                    fix.Comp.ToggleWork(giver.workType);
                }
            }
            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(3);
            MakeCalm(fix.Map);

            IntVec3 at = fix.Stand.Position;
            IntVec3 inside = at + new IntVec3(1, 0, 2);
            IntVec3 insideSite = at + new IntVec3(2, 0, 1);
            IntVec3 past = at + new IntVec3(4, 0, 4);
            IntVec3 pastSite = at + new IntVec3(4, 0, 3);
            bool ok = Expect(givers.Count >= 6, "Core's five delivery-class givers and the seed planter resolve")
                    & Expect(Probe(fix, WorkAt(finish, inside)),
                             "finishing a frame inside the ring dresses a bare builder (control)")
                    & Expect(Probe(fix, FloorRemovalAt(blueprints, inside)),
                             "and so does removing an old floor inside it, though its giver also hands out deliveries");
            foreach (WorkGiverDef giver in givers)
            {
                ok &= Expect(fix.Comp.HandlesWork(giver.workType) && !Probe(fix, DeliveryAt(giver, inside, insideSite)),
                             giver.defName + "'s delivery dresses nobody, though the stand serves its work and the whole trip is inside the ring");
            }

            ok &= Expect(RunSwap(fix), "dressed for the shift")
                & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(Probe(fix, WorkAt(finish, past)), "a frame past the ring changes them back (control)")
                & Expect(Probe(fix, WorkAt(sow, past)), "and so does sowing past it (control)")
                & Expect(Probe(fix, FloorRemovalAt(blueprints, past)),
                         "and so does removing an old floor past it: a site job, not a delivery");
            foreach (WorkGiverDef giver in givers)
            {
                ok &= Expect(!Probe(fix, DeliveryAt(giver, past, insideSite)),
                             giver.defName + "'s delivery keeps the uniform on, its stack past the ring and its site inside: the stand serves its work");
            }
            // JobDriver_PlantSeed writes the seed stack it splits into targetC, so a planting
            // resumed after an interruption has the stack's end of the trip there, not a site.
            WorkGiverDef seedPlanter = DefDatabase<WorkGiverDef>.GetNamedSilentFail(JobRoomTargets.SeedPlanter);
            if (seedPlanter != null)
            {
                Job resumed = DeliveryAt(seedPlanter, past, insideSite);
                resumed.targetC = past;
                ok &= Expect(!Probe(fix, resumed),
                             "a seed planting resumed with its seed stack past the ring in targetC keeps it on: that is no site");
            }
            // Real things, since this delivery reaches Insert's reservation dry run.
            Thing steelInside = Steel(fix.Map, inside);
            Thing siteOutside = WallBlueprint(fix.Map, pastSite);
            ok &= Expect(siteOutside != null && Probe(fix, Delivery(blueprints, steelInside, siteOutside)),
                         "a delivery to a site past the ring changes them back now, while the stand is near");

            // Jobs are pooled, so the next delivery from the same steel can be the same Job object
            // handed out again with a new id; the record of the job they were dressed for must
            // not let it through.
            Job dressedFor = Delivery(blueprints, steelInside, siteOutside);
            Patch_JobInterception.DressedFor[fix.Pawn] = new Patch_JobInterception.DeferredJob(dressedFor);
            ok &= Expect(!Probe(fix, dressedFor),
                         "the delivery they were dressed for keeps the uniform on, its site past the ring or not (control)");
            Patch_JobInterception.DressedFor[fix.Pawn] = new Patch_JobInterception.DeferredJob(dressedFor);
            dressedFor.loadID = Find.UniqueIDsManager.GetNextJobID();
            ok &= Expect(Probe(fix, dressedFor),
                         "but the same Job object handed out again, its def and steel unchanged, changes them back");
            Patch_JobInterception.DressedFor.Remove(fix.Pawn);

            // The shape vanilla's opportunistic haul leaves: the delivery put back at the head of
            // the queue, untagged, and a haul started first.
            ok &= Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.HaulToCell, past, pastSite)),
                         "a haul past the ring changes them back (control)");
            fix.Pawn.jobs.jobQueue.EnqueueFirst(DeliveryAt(blueprints, past, insideSite));
            ok &= Expect(!Probe(fix, JobMaker.MakeJob(JobDefOf.HaulToCell, past, pastSite)),
                         "the same haul keeps it on with a delivery the uniform stays on for queued behind it");
            fix.Pawn.jobs.jobQueue.EnqueueFirst(DeliveryAt(blueprints, past, pastSite));
            ok &= Expect(Probe(fix, JobMaker.MakeJob(JobDefOf.HaulToCell, past, pastSite)),
                         "but not with a delivery to a site past the ring queued behind it");

            if (!complexJobs)
            {
                return ok & ExpectKnownGap(false, "Complex Jobs' two delivery givers are probed too",
                                           "Complex Jobs is not on this mod list; --with=FrozenSnowFox.ComplexJobs loads it");
            }
            return ok & Expect(complexFound == 2, "Complex Jobs' two delivery givers resolve, and were probed above");
        }

        /// <summary>
        /// A delivery outside the stand's work takes the return trip, judged by the whole trip.
        /// Pyjamas from a bedroom stand and a cook's whites change back first for steel out past
        /// the door, to a site there or in the room; steel and site both in the room are the
        /// room's (control). Once the stand serves construction, a site in the room keeps the
        /// uniform on from steel past the door, the same fetch filed under Hauling included but
        /// not one filed under another work type; a first drop, a site or a further site past
        /// the door each change them back now. A radius sleep stand changes them back for a trip
        /// wholly inside its ring as well: the first job out of bed always does.
        /// </summary>
        internal static bool DeliveryOutsideTheStandsWorkChangesThemBack(Fixture fix)
        {
            WorkTypeDef construction = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Construction");
            WorkTypeDef cooking = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cooking");
            WorkGiverDef deliver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToBlueprints");
            // Vanilla files the same fetch under Hauling when hauling outranks construction, or
            // the blueprint needs more skill.
            WorkGiverDef haulDeliver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DeliverResourcesToBlueprints");
            if (construction == null || cooking == null || deliver == null)
            {
                return Expect(false, "the construction and cooking defs resolve");
            }
            if (fix.Outside.Area == 0)
            {
                return Expect(false, "the fixture has a door and open ground outside it");
            }
            fix.Comp.ToggleRest();
            MakeCalm(fix.Map);

            Map map = fix.Map;
            IntVec3 at = fix.Stand.Position;
            Thing steelOut = Steel(map, new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ));
            Thing siteOut = WallBlueprint(map, new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 1));
            Thing steelIn = Steel(map, at + new IntVec3(1, 0, 2));
            Thing siteIn = WallBlueprint(map, at + new IntVec3(2, 0, 1));
            Thing siteInToo = WallBlueprint(map, at + new IntVec3(3, 0, 2));
            Thing siteOutToo = WallBlueprint(map, new IntVec3(fix.Outside.minX, 0, fix.Outside.maxZ - 2));
            if (siteOut == null || siteIn == null || siteInToo == null || siteOutToo == null)
            {
                return Expect(false, "the wall blueprints place");
            }

            bool ok = Expect(fix.Comp.HandlesRest() && !fix.Comp.HandlesWork(construction),
                             "the stand serves sleep and not construction (control)")
                    & Expect(RunSwap(fix), "dressed for bed")
                    & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(Probe(fix, Delivery(deliver, steelOut, siteOut)),
                         "PYJAMAS: a delivery from steel out past the door changes them back first")
                & Expect(Probe(fix, Delivery(deliver, steelOut, siteIn)),
                         "and so does one to a site in the room: the trip starts at the steel")
                & Expect(!Probe(fix, Delivery(deliver, steelIn, siteIn)),
                         "while steel and site both in the room are the room's, and keep the pyjamas on (control)");

            fix.Comp.ToggleWork(cooking);
            ok &= Expect(fix.Comp.HandlesWork(cooking) && !fix.Comp.HandlesWork(construction) && fix.Comp.OnShift,
                         "the stand now serves cooking, the colonist still on shift (control)")
                & Expect(Probe(fix, Delivery(deliver, steelOut, siteOut)),
                         "WHITES: and so does a cook's");

            fix.Comp.ToggleWork(construction);
            ok &= Expect(fix.Comp.HandlesWork(construction) && fix.Comp.OnShift,
                         "the stand now serves construction too (control)")
                & Expect(!Probe(fix, Delivery(deliver, steelOut, siteIn)),
                         "so a delivery from steel out past the door to a site in the room keeps the uniform on (control)")
                & Expect(Probe(fix, Delivery(deliver, steelIn, siteOut)),
                         "and one to a site out past the door changes them back now, while the stand is near");
            // First drop and site set apart, as the giver sets them (see DeliveryVia).
            ok &= Expect(!Probe(fix, DeliveryVia(deliver, steelOut, siteInToo, siteIn)),
                         "a first drop and a site that differ, both in the room, keep it on (control)")
                & Expect(Probe(fix, DeliveryVia(deliver, steelOut, siteOut, siteIn)),
                         "a first drop out past the door changes them back, the site in the room")
                & Expect(Probe(fix, DeliveryVia(deliver, steelOut, siteIn, siteOut)),
                         "and so does a site out there with the first drop in the room")
                & Expect(Probe(fix, DeliveryVia(deliver, steelOut, siteIn, siteInToo, siteOutToo)),
                         "and a further site out there, queued behind the first drop");
            if (haulDeliver != null)
            {
                ok &= Expect(!fix.Comp.HandlesWork(haulDeliver.workType)
                             && !Probe(fix, Delivery(haulDeliver, steelOut, siteIn)),
                             "the same fetch filed under " + haulDeliver.workType.defName
                             + ", which the stand does not tick, keeps it on too: it supplies construction");
            }
            // A fetch filed under a work type that is not hauling is that work's: VEF's art-skill
            // deliveries go under Art. The replanting giver (Growing, and the delivery class)
            // stands in for them here.
            WorkGiverDef replant = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Replant");
            if (replant != null && replant.workType != null)
            {
                ok &= Expect((replant.workType.workTags & WorkTags.Hauling) == 0
                             && !fix.Comp.HandlesWork(replant.workType)
                             && Probe(fix, Delivery(replant, steelOut, siteIn)),
                             "a fetch filed under " + replant.workType.defName
                             + ", no hauling work, is not kept on for construction");
            }

            fix.Comp.ToggleWork(construction);
            fix.Comp.ToggleWork(cooking);
            fix.Comp.ToggleRest();
            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(3);
            ok &= Expect(fix.Comp.HandlesRest() && fix.Comp.serves == CatchmentKind.Radius && fix.Comp.OnShift,
                         "back to sleep, now within a radius, the colonist still on shift (control)")
                & Expect(fix.Comp.Covers(steelIn.Position, map) && fix.Comp.Covers(siteIn.Position, map),
                         "the ring holds the steel and the site inside (control)")
                & Expect(Probe(fix, Delivery(deliver, steelIn, siteIn)),
                         "and a delivery with the whole trip inside it changes them back: the first job out of bed");
            return ok;
        }

        /// <summary>
        /// An area stand dresses for a delivery its area holds whole: with the steel, the site and
        /// a second site painted in, a bare builder is sent to change first, through the real
        /// dressing decision and its reservation dry run. Steel, a site or a second site outside
        /// the area each dress nobody, and room and radius modes over the same cells are the
        /// controls. The same fetch filed under Hauling dresses nobody either, with the stand
        /// ticking construction alone, though the colonist does construction: a builder it is
        /// filed for seldom has the frame as their next job. On shift, a site in the area keeps
        /// the uniform on wherever the steel lies, filed under Hauling or not, and a site
        /// outside it changes them back now, as a frame outside it does (control).
        /// </summary>
        internal static bool AreaStandDressesForADeliveryItHolds(Fixture fix)
        {
            WorkTypeDef construction = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Construction");
            WorkGiverDef deliver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToBlueprints");
            WorkGiverDef finish = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructFinishFrames");
            // Vanilla files the same fetch under Hauling when hauling outranks construction, or
            // the blueprint needs more skill.
            WorkGiverDef haulDeliver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DeliverResourcesToBlueprints");
            if (construction == null || deliver == null || finish == null)
            {
                return Expect(false, "the construction defs resolve");
            }
            fix.Comp.ToggleWork(construction);
            MakeCalm(fix.Map);

            Map map = fix.Map;
            IntVec3 at = fix.Stand.Position;
            Thing steel = Steel(map, at + new IntVec3(2, 0, 1));
            Thing steelOut = Steel(map, at + new IntVec3(4, 0, 1));
            Thing site = WallBlueprint(map, at + new IntVec3(2, 0, 3));
            Thing nextSite = WallBlueprint(map, at + new IntVec3(3, 0, 3));
            Thing siteOut = WallBlueprint(map, at + new IntVec3(4, 0, 4));
            if (site == null || nextSite == null || siteOut == null)
            {
                return Expect(false, "the wall blueprints place");
            }
            Area_Allowed area = NewArea(fix, "Shift harness yard", at, steel.Position, site.Position, nextSite.Position);
            if (area == null)
            {
                return Expect(false, "the map has room for another allowed area");
            }

            bool ok = Expect(!Probe(fix, Delivery(deliver, steel, site)),
                             "room mode: a delivery inside the room changes nobody (control)");
            fix.Comp.SetServes(CatchmentKind.Radius);
            fix.Comp.SetServeRadius(6);
            ok &= Expect(fix.Comp.Covers(steel.Position, map) && fix.Comp.Covers(site.Position, map),
                         "radius 6 covers the steel and the site (control)")
                & Expect(!Probe(fix, Delivery(deliver, steel, site)),
                         "radius mode: a delivery inside the ring changes nobody (control)");

            fix.Comp.SetServeArea(area);
            ok &= Expect(fix.Comp.serves == CatchmentKind.Area, "the stand now serves the area (control)")
                & Expect(Probe(fix, Delivery(deliver, steel, site)),
                         "a delivery with its steel and its site in the area dresses a bare builder")
                & Expect(Probe(fix, Delivery(deliver, steel, site, nextSite)),
                         "and so does one supplying a second site in the area")
                & Expect(!Probe(fix, Delivery(deliver, steelOut, site)),
                         "steel outside the area changes nobody")
                & Expect(!Probe(fix, Delivery(deliver, steel, siteOut)),
                         "nor does a site outside it")
                & Expect(!Probe(fix, Delivery(deliver, steel, site, siteOut)),
                         "nor a trip that also supplies a site outside it");

            Pawn_WorkSettings work = fix.Pawn.workSettings;
            bool hauled = haulDeliver != null && haulDeliver.workType != construction && work != null;
            if (hauled)
            {
                int priority = work.GetPriority(construction);
                work.SetPriority(construction, 3);
                ok &= Expect(work.WorkIsActive(construction) && !fix.Comp.HandlesWork(haulDeliver.workType),
                             "the builder does construction, and the stand does not tick "
                             + haulDeliver.workType.defName + " (control)")
                    & Expect(!Probe(fix, Delivery(haulDeliver, steel, site)),
                             "the same fetch filed under " + haulDeliver.workType.defName
                             + " dresses nobody, though the stand serves construction");
                work.SetPriority(construction, priority);
            }

            ok &= Expect(RunSwap(fix), "dressed for the shift")
                & Expect(fix.Comp.OnShift, "and on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }
            ok &= NotSentStraightBack(fix);
            ok &= Expect(!Probe(fix, Delivery(deliver, steelOut, site)),
                         "on shift, a delivery from steel outside the area to a site inside keeps the uniform on")
                & Expect(!hauled || !Probe(fix, Delivery(haulDeliver, steelOut, site)),
                         "and so does the same fetch filed under Hauling, the stand ticking construction alone")
                & Expect(Probe(fix, Delivery(deliver, steel, siteOut)),
                         "and one to a site outside the area changes them back now")
                & Expect(Probe(fix, WorkAt(finish, siteOut.Position)),
                         "as a frame outside it does (control)");
            return ok;
        }

        /// <summary>
        /// A construction delivery whose first drop and site are the same needer, the one nearest
        /// the stack. <see cref="DeliveryVia"/> sets them apart and gives the giver's full shape.
        /// </summary>
        internal static Job Delivery(WorkGiverDef giver, Thing stack, Thing site, params Thing[] others)
        {
            return DeliveryVia(giver, stack, site, site, others);
        }

        /// <summary>
        /// A construction delivery shaped as the giver shapes it
        /// (<c>WorkGiver_ConstructDeliverResources.ResourceDeliverJobFor</c>): the stack in
        /// targetA, the first drop (the needer nearest the stack) in targetB, the site asked
        /// about in targetC, and the other sites the trip supplies in targetB's queue.
        /// </summary>
        internal static Job DeliveryVia(WorkGiverDef giver, Thing stack, Thing firstDrop, Thing site,
                                        params Thing[] others)
        {
            Job job = JobMaker.MakeJob(JobDefOf.HaulToContainer, stack, firstDrop, site);
            for (int i = 0; i < others.Length; i++)
            {
                job.AddQueuedTarget(TargetIndex.B, others[i]);
            }
            job.count = stack.stackCount;
            job.haulMode = HaulMode.ToContainer;
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>
        /// A delivery under its giver's job def (<see cref="HarnessTables.DeliveryDefFor"/>), the
        /// stack's cell in A and the site's in B and no targetC: the shape of an install, a seed
        /// planting or a replanting, not of a construction delivery. Reserves nothing.
        /// </summary>
        internal static Job DeliveryAt(WorkGiverDef giver, IntVec3 stack, IntVec3 site)
        {
            Job job = JobMaker.MakeJob(HarnessTables.DeliveryDefFor(giver), stack, site);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>
        /// Removing an old floor under a blueprint, as the blueprint delivery giver hands it out
        /// (<c>RemoveExistingFloorJob</c>): a site job.
        /// </summary>
        internal static Job FloorRemovalAt(WorkGiverDef giver, IntVec3 at)
        {
            Job job = JobMaker.MakeJob(JobDefOf.RemoveFloor, at);
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>A steel wall blueprint on a cell, owned by the colony.</summary>
        internal static Thing WallBlueprint(Map map, IntVec3 at)
        {
            return GenConstruct.PlaceBlueprintForBuild(ThingDefOf.Wall, at, map, Rot4.North, Faction.OfPlayer,
                ThingDefOf.Steel);
        }
    }
}
#endif
