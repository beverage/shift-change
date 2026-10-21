// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
#if HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Cases for the static tables, the room resolver and the work-type dialog's sizing: a def name
    /// a table holds still resolves, and a work target with no room of its own still finds one.
    ///
    /// <para>Case order is decided in one place,
    /// <see cref="DebugTools_LifecycleHarness.Run"/>.</para>
    /// </summary>
    internal static class HarnessTables
    {
        /// <summary>
        /// The work-type dialog never draws its body into a rect shorter than the body.
        ///
        /// <para>Guards a past bug: at a high UI scale the window was clamped below the body and
        /// the listing started a second column outside the window, blanking the trigger rows and
        /// the work grid (<c>Listing.GetRect</c> runs <c>NewColumnIfNeeded</c> on every
        /// row).</para>
        ///
        /// <para>Asserts only the arithmetic that sizes the rect: the headless run has no GUI
        /// context to draw a listing in (<c>Listing.Begin</c> calls <c>Widgets.BeginGroup</c>), so
        /// <c>maxOneColumn</c> and the measured-height feedback are covered only by drawing the
        /// dialog. The bodies 533, 665 and 847 and the contents 143 to 373 were measured in play at
        /// four UI scales; the other sizes are extremes.</para>
        /// </summary>
        internal static bool WorkTypeDialogBodyRectFitsTheBody()
        {
            float[] bodies = { 180f, 533f, 665f, 847f, 1200f };
            float[] contents = { 143f, 220f, 306f, 373f, 900f };
            bool ok = true;
            for (int b = 0; b < bodies.Length; b++)
            {
                for (int c = 0; c < contents.Length; c++)
                {
                    Rect content = new Rect(0f, 0f, 480f, contents[c]);
                    bool scrolling;
                    Rect given = Dialog_SetStandWorkTypes.BodyRect(bodies[b], content, out scrolling);

                    ok &= Expect(given.height >= bodies[b] - 0.5f,
                        "body " + bodies[b].ToString("0") + " into a "
                        + contents[c].ToString("0") + " window gets "
                        + given.height.ToString("0"));

                    // A body that fits must not open a scroll view: every clipped region is one
                    // more thing that has to clip the way we assume.
                    bool shouldScroll = bodies[b] > contents[c] + 1f;
                    ok &= Expect(scrolling == shouldScroll,
                        "body " + bodies[b].ToString("0") + " in "
                        + contents[c].ToString("0") + (shouldScroll ? " scrolls" : " does not scroll"));
                }
            }
            return ok;
        }

        /// <summary>
        /// Every def the room-role table names still exists.
        ///
        /// <para><c>RoomWorkTypes</c> resolves through <c>GetNamedSilentFail</c> and drops what is
        /// missing, so a renamed def empties a role's work list and the mod does nothing, with no
        /// log line. A game update is the likely cause.</para>
        ///
        /// <para>The check is hard for <c>Defaults</c> and soft for <c>CompatDefaults</c>, whose
        /// names come from other mods and are absent on any list without them, the four-mod list
        /// included. For those it checks only the merge: whatever resolves must reach
        /// <c>ForRole</c>. A compat row that fails to fold in is caught on a list that has the mod,
        /// and a row naming a def nobody ships stays invisible, by design.</para>
        /// </summary>
        internal static bool RoomRoleTableResolves()
        {
            bool ok = Expect(RoomWorkTypes.Defaults.Count > 0, "the table is not empty");
            foreach (KeyValuePair<string, string[]> entry in RoomWorkTypes.Defaults)
            {
                RoomRoleDef role = DefDatabase<RoomRoleDef>.GetNamedSilentFail(entry.Key);
                ok &= Expect(role != null, "room role " + entry.Key + " resolves");
                int resolved = 0;
                for (int i = 0; i < entry.Value.Length; i++)
                {
                    if (DefDatabase<WorkTypeDef>.GetNamedSilentFail(entry.Value[i]) != null)
                    {
                        resolved++;
                    }
                    else
                    {
                        Expect(false, "work type " + entry.Value[i] + " resolves");
                        ok = false;
                    }
                }

                // Absent is the ordinary case here, so count without asserting.
                string[] compat;
                if (RoomWorkTypes.CompatDefaults.TryGetValue(entry.Key, out compat))
                {
                    for (int i = 0; i < compat.Length; i++)
                    {
                        if (DefDatabase<WorkTypeDef>.GetNamedSilentFail(compat[i]) != null)
                        {
                            resolved++;
                        }
                    }
                }

                if (role != null)
                {
                    ok &= Expect(RoomWorkTypes.ForRole(role).Count == resolved,
                                 entry.Key + " maps to all " + resolved + " of its work types");
                }
            }
            return ok;
        }

        /// <summary>
        /// A work target whose own cell has no room still resolves one.
        ///
        /// <para>A building that is impassable and full-fillage has no region, so its cells belong
        /// to no room (<c>RegionTypeUtility.GetExpectedRegionType</c> returns
        /// <c>RegionType.None</c>), and every arm reads a job's room from its target cell.
        /// <see cref="Patch_JobInterception.RoomBearing"/> finds one for such a target. A plain
        /// wall stands in, since it is that combination and one dropped inside a room does not
        /// split it. A multi-cell target needs no case of its own:
        /// <c>GenAdj.CellsAdjacent8Way(thing)</c> sizes its ring from <c>def.size</c>.</para>
        ///
        /// <para>The precondition, that the wall's own cell has no room, is asserted: if it ever
        /// did, the case would pass without exercising anything.</para>
        /// </summary>
        internal static bool SolidTargetResolvesARoom(Map map, CellRect pad)
        {
            IntVec3 cell = pad.CenterCell;
            // The wall's spawn rebuilds the room into a new Room object (the engine reuses the old
            // one often enough that a reference comparison passes on most runs, which makes a
            // failure intermittent), so nothing may be compared across the spawn by reference.
            // `probe` is a neighbouring cell that shares `before`, read again after the spawn.
            bool dirtyOnEntry = map.regionAndRoomUpdater.AnythingToRebuild;
            Room before = cell.GetRoom(map);
            IntVec3 probe = IntVec3.Invalid;
            for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 neighbour = cell + GenAdj.AdjacentCells[i];
                if (neighbour.InBounds(map) && neighbour.GetRoom(map) == before)
                {
                    probe = neighbour;
                    break;
                }
            }
            Thing wall = null;
            try
            {
                wall = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel),
                                      cell, map);
                bool ok = Expect(wall != null && wall.Spawned, "the stand-in wall spawned");
                if (!ok)
                {
                    return false;
                }
                ok &= Expect(wall.def.passability == Traversability.Impassable
                             && wall.def.Fillage == FillCategory.Full,
                             "the stand-in is impassable and full-fillage");
                ok &= Expect(cell.GetRoom(map) == null,
                             "its own cell has no room, which is the bug's precondition");

                IntVec3 bearing = Patch_JobInterception.RoomBearing(
                    cell, new LocalTargetInfo(wall), map);
                Room after = bearing.GetRoom(map);
                ok &= Expect(after != null, "RoomBearing returns a cell that has a room");

                // Read from the probe after the spawn, so both sides come from the same generation.
                ok &= Expect(before == null || probe.IsValid,
                             "a neighbouring cell shares the target's room (control)");
                Room expected = probe.IsValid ? probe.GetRoom(map) : null;
                bool sameRoom = before == null || expected == null || after == expected;
                if (!sameRoom)
                {
                    // Name both rooms: a bare mismatch cannot tell a genuinely split room from a
                    // rebuilt object carrying the same cells. `before` is read after the spawn, so
                    // it reports whatever its abandoned object last cached.
                    Report.Append("      expected room ")
                          .Append(expected == null ? -1 : expected.ID)
                          .Append(" (").Append(expected == null ? -1 : expected.CellCount)
                          .Append(" cells), stale before room ").Append(before.ID)
                          .Append(" (").Append(before.CellCount).Append(" cells, outdoors ")
                          .Append(before.PsychologicallyOutdoors).Append(", edge ")
                          .Append(before.TouchesMapEdge).Append(")")
                          .Append(" vs after room ")
                          .Append(after == null ? -1 : after.ID)
                          .Append(" (").Append(after == null ? -1 : after.CellCount)
                          .Append(" cells, outdoors ")
                          .Append(after != null && after.PsychologicallyOutdoors)
                          .Append(", edge ").Append(after != null && after.TouchesMapEdge)
                          .Append("), bearing ").Append(bearing)
                          .Append(" from ").Append(cell)
                          .Append(", pad ").Append(pad)
                          .Append(", regions dirty on entry ").Append(dirtyOnEntry)
                          .AppendLine();
                }
                ok &= Expect(sameRoom, "and it is the room the target sits in");
                return ok;
            }
            finally
            {
                if (wall != null && wall.Spawned)
                {
                    wall.Destroy();
                }
            }
        }

        /// <summary>
        /// A work target sunk into an exterior wall resolves to the room it encloses, whichever
        /// side of that room it sits in.
        ///
        /// <para>The ring around such a target splits evenly: three cells in the room, three
        /// outdoors, and the two flanking cells are wall and carry no room. Nothing wins on count,
        /// so the tie-break in <see cref="Patch_JobInterception.RoomBearing"/> decides, and it must
        /// pick the enclosed room over the outdoor one. A tie broken by whichever room the ring
        /// walk reaches first would answer differently in the north and south walls of one room,
        /// because <c>GenAdj.CellsAdjacent8Way</c> enumerates the south row first; hence both
        /// walls.</para>
        ///
        /// <para>A vanilla wall stands in, so this runs on the minimal list: a 1x1 target in a
        /// one-cell wall is the shape whose ring ties. Rimatomics' 4x4 plutonium processor set into
        /// such a wall never splits its ring evenly, so the count decides it, not the tie-break.
        /// The even split is asserted, not assumed: if the ring ever stopped tying, the case would
        /// pass while exercising nothing.</para>
        /// </summary>
        internal static bool ExteriorWallTargetResolvesTheRoom(Map map, CellRect pad)
        {
            // Clear a one-cell margin too: the edge walls' rings reach outside the pad, and a
            // generated map's natural rock there has no room, which turns the 3-3 split into 3-2.
            // Clipped, so a pad against the map edge does not expand out of bounds.
            CellRect surround = pad.ExpandedBy(1).ClipInsideMap(map);
            try
            {
                GenDebug.ClearArea(surround, map);
                EnclosePad(map, pad);

                Room inside = pad.CenterCell.GetRoom(map);
                bool ok = Expect(inside != null && !inside.PsychologicallyOutdoors,
                                 "the pad encloses a real indoor room");
                if (!ok)
                {
                    return false;
                }

                int x = pad.CenterCell.x;
                ok &= EdgeWallBears(map, new IntVec3(x, 0, pad.maxZ), inside, "north");
                ok &= EdgeWallBears(map, new IntVec3(x, 0, pad.minZ), inside, "south");
                return ok;
            }
            finally
            {
                foreach (IntVec3 cell in pad)
                {
                    map.roofGrid.SetRoof(cell, null);
                }
                GenDebug.ClearArea(surround, map);
            }
        }

        /// <summary>
        /// One edge of <see cref="ExteriorWallTargetResolvesTheRoom"/>: the wall's ring must split
        /// evenly between the enclosed room and everything else (otherwise there is no tie to break
        /// and the case proves nothing), and the bearing must land in the room, not outdoors.
        /// </summary>
        internal static bool EdgeWallBears(Map map, IntVec3 cell, Room inside, string edge)
        {
            Thing wall = cell.GetEdifice(map);
            if (!Expect(wall != null, "the " + edge + " edge cell holds a wall"))
            {
                return false;
            }

            int inRoom = 0;
            int elsewhere = 0;
            foreach (IntVec3 ring in GenAdj.CellsAdjacent8Way(wall))
            {
                if (!ring.InBounds(map))
                {
                    continue;
                }
                Room room = ring.GetRoom(map);
                if (room == null)
                {
                    continue;
                }
                if (room == inside)
                {
                    inRoom++;
                }
                else
                {
                    elsewhere++;
                }
            }

            bool ok = Expect(inRoom > 0 && inRoom == elsewhere,
                             "the " + edge + " ring splits evenly (" + inRoom + " in the room, "
                             + elsewhere + " out), so the answer is a tie-break");

            IntVec3 bearing = Patch_JobInterception.RoomBearing(
                cell, new LocalTargetInfo(wall), map);
            ok &= Expect(bearing.GetRoom(map) == inside,
                         "a target in the " + edge + " wall resolves to the room it encloses");
            return ok;
        }

        /// <summary>
        /// Whether Rimatomics is loaded, asked the way the marker table asks: by resolving one of
        /// its types by name.
        /// </summary>
        internal static bool RimatomicsLoaded
        {
            get { return AccessTools.TypeByName("Rimatomics.reactorCore") != null; }
        }

        /// <summary>
        /// The official givers <see cref="JobRoomTargets.IgnoredGivers"/> carries on purpose, and
        /// the only ones it may: vanilla's seven feeding givers, whose jobs put the meal in targetA
        /// (<see cref="HarnessRideAlong.FeedingRidesAlong"/> has the reason). Any other official
        /// giver on the list fails the guard below, as does an official feeding giver missing from
        /// it.
        /// </summary>
        internal static readonly HashSet<string> DeliberatelyIgnoredOfficialGivers =
            new HashSet<string>(HarnessRideAlong.FeedPatientGivers.Concat(HarnessRideAlong.DeliverFoodGivers));

        /// <summary>
        /// The classes vanilla feeds a patient or a prisoner with. Every official giver built on
        /// one is a meal run and must be on <see cref="JobRoomTargets.IgnoredGivers"/>. The guard
        /// goes by class because a giver def can inherit its class from a parent def, as Core's
        /// Handling giver does from the Doctor one, and a list of names alone would miss it.
        /// </summary>
        internal static readonly System.Type[] FeedingGiverClasses =
        {
            typeof(WorkGiver_FeedPatient), typeof(WorkGiver_Warden_Feed), typeof(WorkGiver_Warden_DeliverFood),
            typeof(Workgiver_AdministerHemogen), typeof(WorkGiver_Warden_DeliverHemogen),
        };

        /// <summary>
        /// The official givers <see cref="JobRoomTargets.DeliveryGivers"/> takes by class: Core's
        /// four construction deliveries and its replanting giver, which derives from the same
        /// class. An official giver joining the class later fails the guard below, since the class
        /// would take it without a row anyone reviewed.
        /// </summary>
        internal static readonly HashSet<string> OfficialDeliveryGivers =
            new HashSet<string>
            {
                "ConstructDeliverResourcesToFrames",
                "ConstructDeliverResourcesToBlueprints",
                "DeliverResourcesToFrames",
                "DeliverResourcesToBlueprints",
                "Replant",
            };

        /// <summary>
        /// The job def a delivery giver builds its delivery with: <c>Replant</c> for the replanting
        /// giver (<c>WorkGiver_Replant.JobOnThing</c>), <c>PlantSeed</c> for the seed planter, and
        /// <c>HaulToContainer</c> for the rest
        /// (<c>WorkGiver_ConstructDeliverResources.ResourceDeliverJobFor</c>).
        /// </summary>
        internal static JobDef DeliveryDefFor(WorkGiverDef giver)
        {
            if (giver.defName == JobRoomTargets.SeedPlanter)
            {
                return JobDefOf.PlantSeed;
            }
            return giver.giverClass != null && typeof(WorkGiver_Replant).IsAssignableFrom(giver.giverClass)
                ? JobDefOf.Replant
                : JobDefOf.HaulToContainer;
        }

        /// <summary>
        /// The official jobs <see cref="JobRoomTargets.RideAlongJobs"/> carries on purpose, and the
        /// only ones it may: vanilla's target-less unload, the job tracker's pause between jobs,
        /// the carry Biotech's feeding and playing end with, and its rescue of a baby left
        /// somewhere unsafe.
        /// </summary>
        internal static readonly HashSet<string> DeliberatelyRiddenOfficialJobs =
            new HashSet<string>
            {
                "UnloadYourInventory", "Wait_MaintainPosture", "BringBabyToSafety", "BringBabyToSafetyUnforced",
            };

        /// <summary>
        /// <see cref="JobRoomTargets"/>' job-target, ignored-giver and ride-along lists are mostly
        /// another mod's defNames, and those rows are allowed to miss. So the standing assertions
        /// are structural, and the resolution assertions fire only when the mod is loaded; without
        /// Rimatomics the resolution half is reported as a known gap.
        ///
        /// <para>The load-bearing assertion is the official-def guard. Retargeting a vanilla job to
        /// its targetB, or making the mod ignore a vanilla work giver, would change core behaviour
        /// for everyone with no symptom that points at a table. Some official rows are deliberate,
        /// so the guard names them: an official def on a list passes only if it is one of those,
        /// and each of those must still resolve, since a renamed vanilla def would drop its row
        /// with no error anywhere.</para>
        /// </summary>
        internal static bool JobTablesHold()
        {
            bool ok = Expect(JobRoomTargets.RoomIsTargetB.Count > 0, "the targetB list is not empty");
            ok &= Expect(JobRoomTargets.IgnoredGivers.Count > 0, "the ignored-giver list is not empty");
            ok &= Expect(JobRoomTargets.RideAlongJobs.Count > 0, "the ride-along list is not empty");
            ok &= Expect(!JobRoomTargets.RoomIsTargetB.Any(string.IsNullOrEmpty)
                         && !JobRoomTargets.IgnoredGivers.Any(string.IsNullOrEmpty)
                         && !JobRoomTargets.RideAlongJobs.Any(string.IsNullOrEmpty),
                         "no entry is blank");
            ok &= Expect(!JobRoomTargets.UsesTargetB(null) && !JobRoomTargets.Ignored(null)
                         && !JobRoomTargets.RidesAlong(null),
                         "a null def matches nothing");

            // The giver separates a fresh haul from a follow-up: a listed job rides along only
            // without one.
            Job unload = JobMaker.MakeJob(JobDefOf.UnloadYourInventory);
            ok &= Expect(JobRoomTargets.RidesAlong(unload), "a listed job with no giver rides along");
            unload.workGiverDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulGeneral");
            ok &= Expect(unload.workGiverDef == null || !JobRoomTargets.RidesAlong(unload),
                         "and the same job carrying a giver does not");
            ok &= Expect(JobRoomTargets.RidesAlong(JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1)),
                         "the job tracker's one-tick pause between jobs rides along, as it builds it");
            // Both built as the game builds them, with no giver: the carry a feeding or a play
            // session ends with (ChildcareUtility), and the rescue the childcare giver's NonScanJob
            // and the think tree hand out.
            if (JobDefOf.BringBabyToSafety != null)
            {
                ok &= Expect(JobRoomTargets.RidesAlong(JobMaker.MakeJob(JobDefOf.BringBabyToSafety)),
                             "the carry a feeding or a play session ends with rides along");
            }
            if (JobDefOf.BringBabyToSafetyUnforced != null)
            {
                ok &= Expect(JobRoomTargets.RidesAlong(JobMaker.MakeJob(JobDefOf.BringBabyToSafetyUnforced)),
                             "and so does the rescue of a baby left somewhere unsafe");
            }

            foreach (JobDef job in DefDatabase<JobDef>.AllDefsListForReading)
            {
                if (job.modContentPack == null || !job.modContentPack.IsOfficialMod)
                {
                    continue;
                }
                if (JobRoomTargets.RoomIsTargetB.Contains(job.defName))
                {
                    ok &= Expect(false, "official job " + job.defName + " is NOT retargeted");
                }
                if (JobRoomTargets.RideAlongJobs.Contains(job.defName)
                    && !DeliberatelyRiddenOfficialJobs.Contains(job.defName))
                {
                    ok &= Expect(false, "official job " + job.defName + " does NOT ride along");
                }
            }
            foreach (WorkGiverDef giver in DefDatabase<WorkGiverDef>.AllDefsListForReading)
            {
                if (giver.modContentPack != null && giver.modContentPack.IsOfficialMod
                    && JobRoomTargets.IgnoredGivers.Contains(giver.defName)
                    && !DeliberatelyIgnoredOfficialGivers.Contains(giver.defName))
                {
                    ok &= Expect(false, "official giver " + giver.defName + " is NOT ignored");
                }
                if (giver.modContentPack != null && giver.modContentPack.IsOfficialMod
                    && giver.giverClass != null
                    && FeedingGiverClasses.Any(c => c.IsAssignableFrom(giver.giverClass))
                    && !(DeliberatelyIgnoredOfficialGivers.Contains(giver.defName) && JobRoomTargets.Ignored(giver)))
                {
                    ok &= Expect(false, "official feeding giver " + giver.defName + " (" + giver.giverClass.Name
                                        + ") is on the ignored list and named in the harness");
                }
            }
            foreach (string name in DeliberatelyIgnoredOfficialGivers)
            {
                WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(name);
                bool biotech = name.Contains("Hemogen");
                if (giver == null && biotech && !ModsConfig.BiotechActive)
                {
                    continue;
                }
                ok &= Expect(giver != null && JobRoomTargets.Ignored(giver),
                             "vanilla's " + name + " resolves and is ignored");
            }
            ok &= Expect(DefDatabase<JobDef>.GetNamedSilentFail("UnloadYourInventory") != null,
                         "vanilla's UnloadYourInventory resolves");

            // The delivery givers go by class, so they are held to their named official givers the
            // same way. Only the giver's delivery job is a delivery; the site jobs it also hands
            // out are ordinary work.
            foreach (WorkGiverDef giver in JobRoomTargets.DeliveryGivers)
            {
                if (giver.modContentPack != null && giver.modContentPack.IsOfficialMod
                    && !OfficialDeliveryGivers.Contains(giver.defName))
                {
                    ok &= Expect(false, "official giver " + giver.defName + " is NOT a delivery giver");
                }
            }
            foreach (string name in OfficialDeliveryGivers)
            {
                WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(name);
                if (!Expect(giver != null && JobRoomTargets.DeliveryGivers.Contains(giver)
                            && !JobRoomTargets.Ignored(giver),
                            "vanilla's " + name + " resolves and is a delivery giver, not an ignored one"))
                {
                    ok = false;
                    continue;
                }
                Job delivery = JobMaker.MakeJob(DeliveryDefFor(giver));
                delivery.workGiverDef = giver;
                Job site = JobMaker.MakeJob(JobDefOf.RemoveFloor);
                site.workGiverDef = giver;
                ok &= Expect(JobRoomTargets.IsDelivery(delivery) && !JobRoomTargets.IsDelivery(site),
                             "its " + delivery.def.defName + " is a delivery, and a RemoveFloor it hands out is not");
            }
            WorkGiverDef seeds = DefDatabase<WorkGiverDef>.GetNamedSilentFail(JobRoomTargets.SeedPlanter);
            if (seeds != null)
            {
                Job planting = JobMaker.MakeJob(JobDefOf.PlantSeed);
                planting.workGiverDef = seeds;
                Job cutting = JobMaker.MakeJob(JobDefOf.CutPlant);
                cutting.workGiverDef = seeds;
                Job unstamped = JobMaker.MakeJob(JobDefOf.PlantSeed);
                ok &= Expect(!JobRoomTargets.Ignored(seeds) && JobRoomTargets.IsDelivery(planting)
                             && !JobRoomTargets.IsDelivery(cutting) && !JobRoomTargets.IsDelivery(unstamped),
                             "the seed planter's PlantSeed is a delivery, its CutPlant and an unstamped PlantSeed are not");
            }

            if (!RimatomicsLoaded)
            {
                ok &= ExpectKnownGap(false, "the table's defNames resolve against a loaded Rimatomics",
                                     "Rimatomics is not on this mod list, so every name check above was skipped");
                return ok;
            }
            foreach (string name in JobRoomTargets.RoomIsTargetB)
            {
                ok &= Expect(DefDatabase<JobDef>.GetNamedSilentFail(name) != null,
                             "job " + name + " resolves");
            }
            foreach (string name in JobRoomTargets.IgnoredGivers)
            {
                if (DeliberatelyIgnoredOfficialGivers.Contains(name))
                {
                    continue;
                }
                ok &= Expect(DefDatabase<WorkGiverDef>.GetNamedSilentFail(name) != null,
                             "giver " + name + " resolves");
            }
            return ok;
        }

        /// <summary>
        /// <see cref="RoomContentsWork"/>'s markers, as in <see cref="JobTablesHold"/>: structure
        /// always, resolution only when the mod is there, and a known gap without Rimatomics.
        ///
        /// <para>The assertion worth having is the half-resolved one. A marker whose type is
        /// present but whose work types are not is the shape of a typo in a work-type name, and it
        /// fails silently: the marker drops out of <c>Active</c> and every room it should have
        /// armed does nothing, with no log line.</para>
        /// </summary>
        internal static bool ContentsMarkersHold()
        {
            bool ok = Expect(RoomContentsWork.Markers.Length > 0, "there is at least one marker");
            ok &= Expect(RoomContentsWork.Markers.Select(m => m.typeName).Distinct().Count()
                         == RoomContentsWork.Markers.Length,
                         "no marker type is listed twice");
            foreach (RoomContentsWork.Marker marker in RoomContentsWork.Markers)
            {
                ok &= Expect(!marker.typeName.NullOrEmpty()
                             && marker.workNames != null && marker.workNames.Length > 0,
                             marker.typeName + " names a type and at least one work type");
            }

            // HasComp, against the def database rather than a named def, so the check does not go
            // stale when a def is renamed out from under it.
            ThingDef withComps = DefDatabase<ThingDef>.AllDefsListForReading
                .FirstOrDefault(d => d.comps != null && d.comps.Any(c => c != null && c.compClass != null));
            ThingDef without = DefDatabase<ThingDef>.AllDefsListForReading
                .FirstOrDefault(d => d.comps == null || d.comps.Count == 0);
            if (withComps != null)
            {
                ok &= Expect(RoomContentsWork.HasComp(withComps, typeof(ThingComp)),
                             "HasComp sees a comp on " + withComps.defName);
            }
            if (without != null)
            {
                ok &= Expect(!RoomContentsWork.HasComp(without, typeof(ThingComp)),
                             "HasComp sees none on " + without.defName);
            }
            ok &= Expect(!RoomContentsWork.HasComp(null, typeof(ThingComp)),
                         "HasComp tolerates a null def");

            foreach (RoomContentsWork.Marker marker in RoomContentsWork.Markers)
            {
                if (AccessTools.TypeByName(marker.typeName) == null)
                {
                    continue;   // that mod is not installed; the row simply drops
                }
                ok &= Expect(RoomContentsWork.Active.Contains(marker),
                             marker.typeName + " resolved its work types too, not just its type");
            }
            if (!RimatomicsLoaded)
            {
                ok &= ExpectKnownGap(false, "every marker resolves against a loaded Rimatomics",
                                     "Rimatomics is not on this mod list, so no marker could resolve");
            }
            return ok;
        }
    }
}
#endif
