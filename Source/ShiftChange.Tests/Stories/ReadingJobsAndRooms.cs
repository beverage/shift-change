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

namespace ShiftChange.Tests
{
    /// <summary>
    /// How Shift Change reads the game: which jobs are which, where they happen, and what a room is for.
    ///
    /// <para>Most of these read the game's own tables and need no map: a game update, not an edit,
    /// is what breaks them. The last two put walls up on the pad and ask where a job at them
    /// happens.</para>
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.ReadingJobsAndRooms)]
    internal class ReadingJobsAndRooms : StoryFixture
    {
        /// <summary>
        /// What Shift Change counts as recreation, and which rooms dress for it, read from the
        /// game's tables with no map: a game update, not an edit, breaks it.
        /// <see cref="Patch_JobInterception.IsRecreationJob"/> keys on <c>joyKind</c> plus a
        /// driver-class exclusion. Reading carries a joyKind but picks its spot mid-job (at
        /// StartJob its target is the book), so it is excluded by driver. VisitSickPawn is Doctor
        /// work with joyKind Social, the one vanilla overlap; it stays recreation so every place
        /// that finds a job's room reads it from its second target first, and they cannot
        /// disagree.
        /// </summary>
        [Test]
        [TestDescription("chess counts as recreation and reading does not, "
                         + "and a rec room's stand dresses for it without being told")]
        internal IEnumerator WhatCountsAsRecreation()
        {
            return Unstaged(Body);

            void Body(Story story)
            {
                JobDef chess = DefDatabase<JobDef>.GetNamedSilentFail("Play_Chess");
                JobDef reading = DefDatabase<JobDef>.GetNamedSilentFail("Reading");
                JobDef visitSick = DefDatabase<JobDef>.GetNamedSilentFail("VisitSickPawn");
                if (chess == null || reading == null || visitSick == null)
                {
                    story.Check(false, "the game has its chess, reading and visiting-the-sick jobs");
                    return;
                }

                story.Check(chess.joyKind != null, "chess is still a joy activity to the game");
                story.Check(Patch_JobInterception.IsRecreationJob(JobMaker.MakeJob(chess)),
                            "and Shift Change counts it as recreation");
                story.Check(!Patch_JobInterception.IsRecreationJob(JobMaker.MakeJob(JobDefOf.Wait)),
                            "standing about, which is no joy activity, does not count");

                story.Check(reading.joyKind != null,
                            "reading is a joy activity to the game as well, "
                            + "so what leaves it out is how it runs, not its kind");
                story.Check(!Patch_JobInterception.IsRecreationJob(JobMaker.MakeJob(reading)),
                            "and it does not count, because a reader picks their spot only once the book is in hand");

                story.Check(visitSick.joyKind != null,
                            "visiting the sick is still a joy activity to the game");
                story.Check(Patch_JobInterception.IsRecreationJob(JobMaker.MakeJob(visitSick)),
                            "and counts as recreation, so wherever Shift Change looks for its room, "
                            + "it finds the same one");

                RoomRoleDef recRoom = DefDatabase<RoomRoleDef>.GetNamedSilentFail("RecRoom");
                RoomRoleDef hospital = DefDatabase<RoomRoleDef>.GetNamedSilentFail("Hospital");
                story.Check(recRoom != null && RoomWorkTypes.RecreationForRole(recRoom),
                            "a stand in a rec room dresses for recreation without being told");
                story.Check(hospital == null || !RoomWorkTypes.RecreationForRole(hospital),
                            "one in a hospital, a room for work, does not");
                story.Check(!RoomWorkTypes.RecreationForRole(null),
                            "and neither does one in a room with no role");
            }
        }

        /// <summary>
        /// What Shift Change counts as sleep, and which rooms dress for it, read from the game's
        /// tables with no map, as <see cref="WhatCountsAsRecreation"/> is: every check is a fact
        /// of the game's defs, so a game update, not an edit, breaks it. Going to bed in a
        /// bedroom is tested where a sleep job dresses at a bedroom's stand.
        /// </summary>
        [Test]
        [TestDescription("lying down counts as sleep only in a bed, "
                         + "and a bedroom's stand dresses for sleep without being told")]
        internal IEnumerator WhatCountsAsSleep()
        {
            return Unstaged(Body);

            void Body(Story story)
            {
                JobDef laydown = DefDatabase<JobDef>.GetNamedSilentFail("LayDown");
                JobDef groundSleep = DefDatabase<JobDef>.GetNamedSilentFail("Wait_Asleep");
                WorkTypeDef bedRest = WorkType("PatientBedRest");
                WorkGiverDef recuperate = Giver("PatientGoToBedRecuperate");
                if (laydown == null || groundSleep == null)
                {
                    story.Check(false, "the game has its jobs for lying down and for sleeping on the ground");
                    return;
                }

                story.Check(laydown.driverClass != null
                            && typeof(JobDriver_LayDown).IsAssignableFrom(laydown.driverClass),
                            "lying down still runs the game's lying-down routine");
                story.Check(groundSleep.driverClass != null
                            && typeof(JobDriver_LayDown).IsAssignableFrom(groundSleep.driverClass),
                            "so does sleeping on the ground, which is why Shift Change asks for a bed");
                story.Check(!Patch_JobInterception.IsRestJob(JobMaker.MakeJob(laydown)),
                            "and lying down with no bed does not count as sleep");

                // The medical half: facts of the game's defs and nothing more, all true while the
                // tickbox does nothing. Bed rest is the work type MedicalRestWorkType charges a
                // tagged lay-down to; that it dresses is tested where medical bed rest dresses at
                // a stand holding a gown.
                story.Check(bedRest != null, "the game still has its bed rest work type");
                story.Check(bedRest == null || bedRest.visible,
                            "still shown, so it still appears in the stand's own Work types grid");
                story.Check(recuperate == null || recuperate.workType == bedRest,
                            "and recuperating in bed is still bed rest work, so charging medical rest "
                            + "to it matches what the work tab shows a player");

                RoomRoleDef bedroom = DefDatabase<RoomRoleDef>.GetNamedSilentFail("Bedroom");
                RoomRoleDef recRoom = DefDatabase<RoomRoleDef>.GetNamedSilentFail("RecRoom");
                story.Check(bedroom != null && RoomWorkTypes.RestForRole(bedroom),
                            "a stand in a bedroom dresses for sleep without being told");
                story.Check(recRoom == null || !RoomWorkTypes.RestForRole(recRoom),
                            "one in a rec room does not: no kind of room dresses for more than one of "
                            + "work, recreation and sleep");
                story.Check(!RoomWorkTypes.RestForRole(null),
                            "and neither does one in a room with no role");
            }
        }

        /// <summary>
        /// Every room and work type Shift Change's table of rooms names still exists.
        ///
        /// <para><see cref="RoomWorkTypes"/> resolves through <c>GetNamedSilentFail</c> and drops
        /// what is missing, so a renamed def empties a room's work list and the mod does nothing,
        /// with no log line. A game update is the likely cause.</para>
        ///
        /// <para>The check is hard for <see cref="RoomWorkTypes.Defaults"/> and soft for
        /// <see cref="RoomWorkTypes.CompatDefaults"/>, whose names come from other mods and are
        /// absent on any list without them, the four-mod list included. For those it checks only
        /// the merge: whatever resolves must reach <see cref="RoomWorkTypes.ForRole"/>. A compat
        /// row that fails to fold in is caught on a list that has the mod, and a row naming a def
        /// nobody ships stays invisible, by design.</para>
        /// </summary>
        [Test]
        [TestDescription("every kind of room Shift Change dresses for, from hospital to barn, "
                         + "still names work the game has")]
        internal IEnumerator EveryKindOfRoomFindsItsWork()
        {
            return Unstaged(Body);

            void Body(Story story)
            {
                story.Check(RoomWorkTypes.Defaults.Count > 0,
                            "Shift Change knows which work belongs in which kind of room");
                foreach (KeyValuePair<string, string[]> entry in RoomWorkTypes.Defaults)
                {
                    string kind = entry.Key.ToLowerInvariant();
                    RoomRoleDef role = DefDatabase<RoomRoleDef>.GetNamedSilentFail(entry.Key);
                    story.Check(role != null, "the game still knows a " + kind + " when it sees one");
                    int resolved = 0;
                    for (int i = 0; i < entry.Value.Length; i++)
                    {
                        if (WorkType(entry.Value[i]) != null)
                        {
                            resolved++;
                        }
                        else
                        {
                            story.Check(false, "the game still has " + entry.Value[i]
                                               + ", work a " + kind + "'s stand dresses for");
                        }
                    }

                    // Absent is the ordinary case here, so count without checking.
                    string[] compat;
                    if (RoomWorkTypes.CompatDefaults.TryGetValue(entry.Key, out compat))
                    {
                        for (int i = 0; i < compat.Length; i++)
                        {
                            if (WorkType(compat[i]) != null)
                            {
                                resolved++;
                            }
                        }
                    }

                    if (role != null)
                    {
                        story.Check(RoomWorkTypes.ForRole(role).Count == resolved,
                                    "an automatic stand in a " + kind + " dresses for "
                                    + (resolved == 1 ? "its one work type" : "all " + resolved + " of its work types"));
                    }
                }
            }
        }

        /// <summary>
        /// Each patient giver's work is served where its patients lie, by a hospital's or a barn's
        /// automatic stand: the stand dresses for tending and operating there, and a meal run keeps
        /// the uniform on only at a stand serving the giver's work. A prison cell's roles arm
        /// nothing, so its stand serves what is ticked: Warden in vanilla, and the work a mod moves
        /// prisoner feeding to, which the README tells the player to tick beside it.
        /// </summary>
        [Test]
        [TestDescription("the tending, feeding and operating done on patients is served where they lie, "
                         + "and feeding prisoners is Warden work")]
        internal IEnumerator PatientsAreServedWhereTheyLie()
        {
            return Unstaged(Body);

            void Body(Story story)
            {
                WorkTypeDef warden = WorkType("Warden");
                story.Check(warden != null, "the game still has its Warden work");
                foreach (KeyValuePair<string, string> row in GiverTables.PatientGiverRoles)
                {
                    WorkGiverDef giver = Giver(row.Key);
                    RoomRoleDef role = DefDatabase<RoomRoleDef>.GetNamedSilentFail(row.Value);
                    if (GiverTables.BiotechGiverWithoutBiotech(giver, row.Key))
                    {
                        continue;
                    }
                    if (giver?.workType == null || role == null)
                    {
                        story.Check(false, "the game still has " + row.Key + ", its work type and the "
                                           + row.Value.ToLowerInvariant() + " room");
                        continue;
                    }
                    story.Check(RoomWorkTypes.ForRole(role).Contains(giver.workType),
                                "'" + Called(giver, row.Key) + "' is " + WorkCalled(giver.workType, giver.workType.defName)
                                + " work, which a " + role.label + "'s automatic stand serves");
                }
                foreach (string name in GiverTables.DeliberatelyIgnoredOfficialGivers)
                {
                    WorkGiverDef giver = Giver(name);
                    if (GiverTables.PatientGiverRoles.ContainsKey(name)
                        || GiverTables.BiotechGiverWithoutBiotech(giver, name))
                    {
                        continue;
                    }
                    WorkTypeDef work = giver?.workType;
                    if (work == null)
                    {
                        story.Check(false, "the game still has " + name + " and its work type");
                        continue;
                    }
                    if (work != warden && GiverTables.IsModWork(work))
                    {
                        story.Note("'" + Called(giver, name) + "' is " + work.defName + " work from "
                                   + (work.modContentPack?.Name ?? "a patch")
                                   + ", which a prison cell's stand serves once ticked beside Warden");
                        continue;
                    }
                    story.Check(work == warden,
                                "'" + Called(giver, name) + "' is Warden work, so a stand ticked for Warden serves it"
                                + (work == warden ? "" : " (it is " + work.defName + ")"));
                }
            }
        }

        /// <summary>
        /// Shift Change's lists of jobs and givers (<see cref="JobRoomTargets"/>: loading jobs
        /// placed where their load lies, givers whose jobs dress nobody, follow-ups the outfit
        /// stays on for) are mostly another mod's defNames, and those rows are allowed to miss. So
        /// the standing checks are structural, and the checks that those names resolve run only
        /// when the mod is loaded; without Rimatomics they are a known gap.
        ///
        /// <para>The checks that matter most guard the game's own defs. Placing a vanilla job by
        /// its targetB, or making the mod ignore a vanilla work giver, would change core behaviour
        /// for everyone with no symptom that points at a list. Some of the game's own rows are
        /// deliberate, so the tests name them: a def of the game's own on a list passes only if it
        /// is one of those, and each of those must still resolve, since a renamed vanilla def would
        /// drop its row with no error anywhere.</para>
        /// </summary>
        [Test]
        [TestDescription("meal runs, deliveries and the jobs that finish a haul are told apart, "
                         + "and no other job of the game's own is singled out")]
        internal IEnumerator MealRunsDeliveriesAndFollowUpsAreToldApart()
        {
            return Unstaged(Body);

            void Body(Story story)
            {
                story.Check(JobRoomTargets.RoomIsTargetB.Count > 0,
                            "Shift Change lists loading jobs it places where their load lies, not where it goes");
                story.Check(JobRoomTargets.IgnoredGivers.Count > 0, "work givers whose jobs dress nobody");
                story.Check(JobRoomTargets.RideAlongJobs.Count > 0, "and follow-up jobs the outfit stays on for");
                story.Check(!JobRoomTargets.RoomIsTargetB.Any(string.IsNullOrEmpty)
                            && !JobRoomTargets.IgnoredGivers.Any(string.IsNullOrEmpty)
                            && !JobRoomTargets.RideAlongJobs.Any(string.IsNullOrEmpty),
                            "no row on those lists is blank");
                story.Check(!JobRoomTargets.UsesTargetB(null) && !JobRoomTargets.Ignored(null)
                            && !JobRoomTargets.RidesAlong(null),
                            "and a job of no kind is on none of them");

                // The giver separates a fresh haul from a follow-up: a listed job rides along only
                // without one.
                Job unload = JobMaker.MakeJob(JobDefOf.UnloadYourInventory);
                story.Check(JobRoomTargets.RidesAlong(unload),
                            "unloading their inventory, as a haul leaves it with no work giver, keeps the outfit on");
                unload.workGiverDef = Giver("HaulGeneral");
                story.Check(unload.workGiverDef == null || !JobRoomTargets.RidesAlong(unload),
                            "and the same unloading handed out as hauling work does not");
                story.Check(JobRoomTargets.RidesAlong(JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 1)),
                            "the one-tick pause between jobs keeps it on, built as the game builds it");
                // Both built as the game builds them, with no giver: the carry a feeding or a play
                // session ends with (ChildcareUtility), and the rescue the childcare giver's NonScanJob
                // and the think tree hand out.
                if (JobDefOf.BringBabyToSafety != null)
                {
                    story.Check(JobRoomTargets.RidesAlong(JobMaker.MakeJob(JobDefOf.BringBabyToSafety)),
                                "carrying a baby to safety, as a feeding or a play session ends, keeps it on");
                }
                if (JobDefOf.BringBabyToSafetyUnforced != null)
                {
                    story.Check(JobRoomTargets.RidesAlong(JobMaker.MakeJob(JobDefOf.BringBabyToSafetyUnforced)),
                                "and so does rescuing a baby left somewhere unsafe");
                }

                foreach (JobDef job in DefDatabase<JobDef>.AllDefsListForReading)
                {
                    if (job.modContentPack == null || !job.modContentPack.IsOfficialMod)
                    {
                        continue;
                    }
                    if (JobRoomTargets.RoomIsTargetB.Contains(job.defName))
                    {
                        story.Check(false, "the game's own job " + job.defName
                                           + " is not placed where its load lies");
                    }
                    if (JobRoomTargets.RideAlongJobs.Contains(job.defName)
                        && !GiverTables.DeliberatelyRiddenOfficialJobs.Contains(job.defName))
                    {
                        story.Check(false, "the game's own job " + job.defName
                                           + " is not a follow-up the outfit stays on for");
                    }
                }
                foreach (WorkGiverDef giver in DefDatabase<WorkGiverDef>.AllDefsListForReading)
                {
                    if (giver.modContentPack != null && giver.modContentPack.IsOfficialMod
                        && JobRoomTargets.IgnoredGivers.Contains(giver.defName)
                        && !GiverTables.DeliberatelyIgnoredOfficialGivers.Contains(giver.defName))
                    {
                        story.Check(false, "the game's own work giver " + giver.defName
                                           + " still dresses whoever does its work");
                    }
                    if (giver.modContentPack != null && giver.modContentPack.IsOfficialMod
                        && giver.giverClass != null
                        && GiverTables.FeedingGiverClasses.Any(c => c.IsAssignableFrom(giver.giverClass))
                        && !(GiverTables.DeliberatelyIgnoredOfficialGivers.Contains(giver.defName)
                             && JobRoomTargets.Ignored(giver)))
                    {
                        story.Check(false, "the game's meal run " + giver.defName + " (" + giver.giverClass.Name
                                           + ") dresses nobody, and these tests name it");
                    }
                }
                foreach (string name in GiverTables.DeliberatelyIgnoredOfficialGivers)
                {
                    WorkGiverDef giver = Giver(name);
                    if (GiverTables.BiotechGiverWithoutBiotech(giver, name))
                    {
                        continue;
                    }
                    story.Check(giver != null && JobRoomTargets.Ignored(giver),
                                "the game's meal run " + name + " is there, and dresses nobody");
                }
                story.Check(DefDatabase<JobDef>.GetNamedSilentFail("UnloadYourInventory") != null,
                            "the game's job for unloading an inventory, UnloadYourInventory, is still there");

                // The delivery givers go by class, so they are held to their named official givers the
                // same way. Only the giver's delivery job is a delivery; the site jobs it also hands
                // out are ordinary work.
                foreach (WorkGiverDef giver in JobRoomTargets.DeliveryGivers)
                {
                    if (giver.modContentPack != null && giver.modContentPack.IsOfficialMod
                        && !GiverTables.OfficialDeliveryGivers.Contains(giver.defName))
                    {
                        story.Check(false, "the game's own work giver " + giver.defName
                                           + " is not taken for one that delivers to a site");
                    }
                }
                foreach (string name in GiverTables.OfficialDeliveryGivers)
                {
                    WorkGiverDef giver = Giver(name);
                    if (!story.Check(giver != null && JobRoomTargets.DeliveryGivers.Contains(giver)
                                     && !JobRoomTargets.Ignored(giver),
                                     "the game's " + name + " is there, and delivers to a site "
                                     + "rather than dressing nobody"))
                    {
                        continue;
                    }
                    Job delivery = JobMaker.MakeJob(GiverTables.DeliveryDefFor(giver));
                    delivery.workGiverDef = giver;
                    Job site = JobMaker.MakeJob(JobDefOf.RemoveFloor);
                    site.workGiverDef = giver;
                    story.Check(JobRoomTargets.IsDelivery(delivery) && !JobRoomTargets.IsDelivery(site),
                                "its " + delivery.def.defName + " counts as a delivery, "
                                + "and a floor removal it hands out does not");
                }
                WorkGiverDef seeds = Giver(JobRoomTargets.SeedPlanter);
                if (seeds != null)
                {
                    Job planting = JobMaker.MakeJob(JobDefOf.PlantSeed);
                    planting.workGiverDef = seeds;
                    Job cutting = JobMaker.MakeJob(JobDefOf.CutPlant);
                    cutting.workGiverDef = seeds;
                    Job unstamped = JobMaker.MakeJob(JobDefOf.PlantSeed);
                    story.Check(!JobRoomTargets.Ignored(seeds) && JobRoomTargets.IsDelivery(planting)
                                && !JobRoomTargets.IsDelivery(cutting) && !JobRoomTargets.IsDelivery(unstamped),
                                "sowing a seed counts as a delivery, but cutting a plant in the way, "
                                + "or sowing no work handed out, does not");
                }

                if (!GiverTables.RimatomicsLoaded)
                {
                    story.KnownGap(false, "the lists' rows from other mods resolve against a loaded Rimatomics",
                                   "Rimatomics is not on this mod list, so every name check above was skipped");
                    return;
                }
                foreach (string name in JobRoomTargets.RoomIsTargetB)
                {
                    story.Check(DefDatabase<JobDef>.GetNamedSilentFail(name) != null,
                                "the listed job " + name + " is in the game");
                }
                foreach (string name in JobRoomTargets.IgnoredGivers)
                {
                    if (GiverTables.DeliberatelyIgnoredOfficialGivers.Contains(name))
                    {
                        continue;
                    }
                    story.Check(Giver(name) != null,
                                "the listed work giver " + name + " is in the game");
                }
            }
        }

        /// <summary>
        /// The tables a radius stand stays dressed by. Pass-through work is read from work tags,
        /// so a modded work type carrying the Hauling tag under a name of its own passes through,
        /// and one named Hauling without the tag does not. The list of odd jobs (idling,
        /// wandering, walking somewhere allowed or safe) has six rows that all resolve, so none is
        /// a typo that can never match, and each counts only without a giver.
        /// </summary>
        [Test]
        [TestDescription("hauling, cleaning, building and firefighting pass through a stand's radius in uniform, "
                         + "and so do six odd jobs no work handed out")]
        internal IEnumerator WhatPassesThroughAStandsRadius()
        {
            return Unstaged(Body);

            void Body(Story story)
            {
                foreach (string name in new[] { "Hauling", "Cleaning", "Construction", "Firefighter" })
                {
                    WorkTypeDef work = WorkType(name);
                    story.Check(work != null && JobRoomTargets.PassesThrough(work),
                                WorkCalled(work, name).CapitalizeFirst() + " passes through a work stand's radius");
                }
                foreach (string name in new[] { "Doctor", "Cooking", "BasicWorker", "PlantCutting", "Growing", "Mining" })
                {
                    WorkTypeDef work = WorkType(name);
                    story.Check(work != null && !JobRoomTargets.PassesThrough(work),
                                WorkCalled(work, name).CapitalizeFirst() + " does not");
                }
                WorkTypeDef tagged = new WorkTypeDef
                {
                    defName = "Test_FSFHauling",
                    workTags = WorkTags.ManualDumb | WorkTags.Hauling,
                };
                WorkTypeDef untagged = new WorkTypeDef { defName = "Hauling", workTags = WorkTags.ManualDumb };
                story.Note("two made-up work types: one tagged as hauling under a name of its own, "
                           + "one called Hauling with no such tag");
                story.Check(JobRoomTargets.PassesThrough(tagged),
                            "a mod's work type tagged as hauling passes through, whatever it is called");
                story.Check(!JobRoomTargets.PassesThrough(untagged),
                            "and one called Hauling without the tag does not: the tag is the test");
                story.Check(!JobRoomTargets.PassesThrough(null), "nor does a job of no work type");

                WorkGiverDef giver = Giver("HaulGeneral");
                story.Check(JobRoomTargets.IncidentalJobs.Count == 6,
                            "Shift Change lists six odd jobs that keep the outfit on inside a stand's radius");
                story.Check(giver != null, "the game's general hauling work is there");
                foreach (string name in JobRoomTargets.IncidentalJobs)
                {
                    JobDef def = DefDatabase<JobDef>.GetNamedSilentFail(name);
                    if (!story.Check(def != null, "the game has its " + name + " job, so the row can match"))
                    {
                        continue;
                    }
                    Job job = JobMaker.MakeJob(def);
                    story.Check(JobRoomTargets.IsIncidental(job),
                                name + ", when no work handed it out, is one of them");
                    job.workGiverDef = giver;
                    story.Check(!JobRoomTargets.IsIncidental(job), "and handed out as hauling work, it is not");
                }
                foreach (JobDef def in new[] { JobDefOf.LayDown, JobDefOf.Ingest, JobDefOf.HaulToCell,
                                               JobDefOf.Wait_Combat })
                {
                    story.Check(!JobRoomTargets.IsIncidental(JobMaker.MakeJob(def)),
                                def.defName + " is no odd job");
                }
            }
        }

        /// <summary>
        /// The buildings whose presence gives a room its work (<see cref="RoomContentsWork"/>), as
        /// in <see cref="MealRunsDeliveriesAndFollowUpsAreToldApart"/>: structure always,
        /// resolution only when the mod is there, and a known gap without Rimatomics.
        ///
        /// <para>The check worth having is the half-resolved one. A marker whose type is present
        /// but whose work types are not is the shape of a typo in a work-type name, and it fails
        /// silently: the marker drops out of <see cref="RoomContentsWork.Active"/> and every room
        /// it should have armed does nothing, with no log line.</para>
        /// </summary>
        [Test]
        [TestDescription("the buildings that give a room its work, Rimatomics' reactor parts and benches, "
                         + "each find their work types")]
        internal IEnumerator BuildingsThatGiveARoomItsWork()
        {
            return Unstaged(Body);

            void Body(Story story)
            {
                story.Check(RoomContentsWork.Markers.Length > 0,
                            "Shift Change lists buildings whose presence gives a room its work");
                story.Check(RoomContentsWork.Markers.Select(m => m.typeName).Distinct().Count()
                            == RoomContentsWork.Markers.Length,
                            "and lists none of them twice");
                foreach (RoomContentsWork.Marker marker in RoomContentsWork.Markers)
                {
                    story.Check(!marker.typeName.NullOrEmpty()
                                && marker.workNames != null && marker.workNames.Length > 0,
                                marker.typeName + " names what to look for and at least one work type");
                }

                // HasComp, against the def database rather than a named def, so the check does not go
                // stale when a def is renamed out from under it.
                ThingDef withComps = DefDatabase<ThingDef>.AllDefsListForReading
                    .FirstOrDefault(d => d.comps != null && d.comps.Any(c => c != null && c.compClass != null));
                ThingDef without = DefDatabase<ThingDef>.AllDefsListForReading
                    .FirstOrDefault(d => d.comps == null || d.comps.Count == 0);
                if (withComps != null)
                {
                    story.Check(RoomContentsWork.HasComp(withComps, typeof(ThingComp)),
                                "Shift Change sees the components " + withComps.defName + " is built with");
                }
                if (without != null)
                {
                    story.Check(!RoomContentsWork.HasComp(without, typeof(ThingComp)),
                                "and sees none on " + without.defName + ", which has none");
                }
                story.Check(!RoomContentsWork.HasComp(null, typeof(ThingComp)),
                            "and asking about no building at all is harmless");

                foreach (RoomContentsWork.Marker marker in RoomContentsWork.Markers)
                {
                    if (AccessTools.TypeByName(marker.typeName) == null)
                    {
                        continue;   // that mod is not installed; the row simply drops
                    }
                    story.Check(RoomContentsWork.Active.Contains(marker),
                                "with its mod loaded, " + marker.typeName
                                + " found its work types too, not just its building");
                }
                if (!GiverTables.RimatomicsLoaded)
                {
                    story.KnownGap(false, "every listed building finds its work against a loaded Rimatomics",
                                   "Rimatomics is not on this mod list, so no marker could resolve");
                }
            }
        }

        /// <summary>
        /// The stand's Work types window never draws its body into a rect shorter than the body.
        ///
        /// <para>Guards a past bug: at a high UI scale the window was clamped below the body and
        /// the listing started a second column outside the window, blanking the trigger rows and
        /// the work grid (<c>Listing.GetRect</c> runs <c>NewColumnIfNeeded</c> on every
        /// row).</para>
        ///
        /// <para>Checks only the arithmetic that sizes the rect: a headless run has no GUI context
        /// to draw a listing in (<c>Listing.Begin</c> calls <c>Widgets.BeginGroup</c>), so
        /// <c>maxOneColumn</c> and the measured-height feedback are covered only by drawing the
        /// window. The bodies 533, 665 and 847 and the contents 143 to 373 were measured in play at
        /// four UI scales; the other sizes are extremes.</para>
        /// </summary>
        [Test]
        [TestDescription("the stand's Work types window always gives its list all the room it needs, "
                         + "and scrolls only when the list is taller than the window")]
        internal IEnumerator WorkTypesWindowFitsItsList()
        {
            return Unstaged(Body);

            void Body(Story story)
            {
                // Called by reflection: the mod's dll names Rect's 1.6 assembly, which 1.7 maps to its
                // own Rect when it loads the dll, so the compiler sees two types where the game has one.
                System.Reflection.MethodInfo bodyRect = AccessTools.Method(
                    typeof(Dialog_SetStandWorkTypes), nameof(Dialog_SetStandWorkTypes.BodyRect));
                if (bodyRect == null)
                {
                    story.Check(false, "the Work types window's sizing is there to call");
                    return;
                }
                float[] bodies = { 180f, 533f, 665f, 847f, 1200f };
                float[] contents = { 143f, 220f, 306f, 373f, 900f };
                for (int b = 0; b < bodies.Length; b++)
                {
                    for (int c = 0; c < contents.Length; c++)
                    {
                        Rect content = new Rect(0f, 0f, 480f, contents[c]);
                        object[] args = { bodies[b], content, null };
                        Rect given = (Rect)bodyRect.Invoke(null, args);
                        bool scrolling = (bool)args[2];

                        story.Check(given.height >= bodies[b] - 0.5f,
                                    "a list " + bodies[b].ToString("0") + " tall, in a window "
                                    + contents[c].ToString("0") + " tall, is given "
                                    + given.height.ToString("0") + ", room for all of it");

                        // A body that fits must not open a scroll view: every clipped region is one
                        // more thing that has to clip the way we assume.
                        bool shouldScroll = bodies[b] > contents[c] + 1f;
                        story.Check(scrolling == shouldScroll,
                                    shouldScroll
                                        ? "and it scrolls, being taller than the window"
                                        : "and it does not scroll, since it fits");
                    }
                }
            }
        }

        /// <summary>
        /// A work target whose own cell has no room still finds one.
        ///
        /// <para>A building that is impassable and full-fillage has no region, so its cells belong
        /// to no room (<c>RegionTypeUtility.GetExpectedRegionType</c> returns
        /// <c>RegionType.None</c>), and every arm reads a job's room from its target cell.
        /// <see cref="Patch_JobInterception.RoomBearing"/> finds one for such a target. A plain
        /// wall stands in, since it is that combination and one dropped inside a room does not
        /// split it. A multi-cell target needs no test of its own:
        /// <c>GenAdj.CellsAdjacent8Way(thing)</c> sizes its ring from <c>def.size</c>.</para>
        ///
        /// <para>That the wall's own cell has no room is checked: if it ever did, the test would
        /// pass without exercising anything.</para>
        /// </summary>
        [Test]
        [TestDescription("a job at a wall, which has no room of its own, is placed in the room the wall stands in")]
        internal IEnumerator JobAtAWallFindsTheRoom()
        {
            return OnThePad(Body);

            void Body(Story story)
            {
                Map map = Map;
                CellRect pad = Pad;
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
                    if (!story.Check(wall != null && wall.Spawned,
                                     "a steel wall goes up in the middle of the pad, standing in for a solid machine"))
                    {
                        return;
                    }
                    story.Check(wall.def.passability == Traversability.Impassable
                                && wall.def.Fillage == FillCategory.Full,
                                "nobody can walk through it, and it fills its cell");
                    story.Check(cell.GetRoom(map) == null,
                                "so its own cell belongs to no room, the case this test is about");

                    IntVec3 bearing = Patch_JobInterception.RoomBearing(
                        cell, new LocalTargetInfo(wall), map);
                    Room after = bearing.GetRoom(map);
                    story.Check(after != null, "a job at the wall is placed in a cell that has a room");

                    // Read from the probe after the spawn, so both sides come from the same generation.
                    story.Check(before == null || probe.IsValid,
                                "a cell beside the wall was in its room before it went up");
                    Room expected = probe.IsValid ? probe.GetRoom(map) : null;
                    bool sameRoom = before == null || expected == null || after == expected;
                    if (!sameRoom)
                    {
                        // Name both rooms: a bare mismatch cannot tell a genuinely split room from a
                        // rebuilt object carrying the same cells. `before` is read after the spawn, so
                        // it reports whatever its abandoned object last cached.
                        story.Note("expected room " + (expected == null ? -1 : expected.ID)
                                   + " (" + (expected == null ? -1 : expected.CellCount)
                                   + " cells), stale before room " + before.ID
                                   + " (" + before.CellCount + " cells, outdoors " + before.PsychologicallyOutdoors
                                   + ", edge " + before.TouchesMapEdge + ")"
                                   + " vs after room " + (after == null ? -1 : after.ID)
                                   + " (" + (after == null ? -1 : after.CellCount)
                                   + " cells, outdoors " + (after != null && after.PsychologicallyOutdoors)
                                   + ", edge " + (after != null && after.TouchesMapEdge)
                                   + "), bearing " + bearing + " from " + cell + ", pad " + pad
                                   + ", regions dirty on entry " + dirtyOnEntry);
                    }
                    story.Check(sameRoom, "and it is that room, the one the wall stands in");
                }
                finally
                {
                    if (wall != null && wall.Spawned)
                    {
                        wall.Destroy();
                    }
                }
            }
        }

        /// <summary>
        /// A work target sunk into an outer wall is placed in the room it encloses, whichever side
        /// of that room it sits in.
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
        /// The even split is checked, not assumed: if the ring ever stopped tying, the test would
        /// pass while exercising nothing.</para>
        /// </summary>
        [Test]
        [TestDescription("a job at an outer wall is placed in the room inside, from the north wall and the south alike")]
        internal IEnumerator JobInAnOuterWallFindsTheRoomInside()
        {
            return OnThePad(Body);

            void Body(Story story)
            {
                Map map = Map;
                CellRect pad = Pad;
                // Clear a one-cell margin too: the edge walls' rings reach outside the pad, and a
                // generated map's natural rock there has no room, which turns the 3-3 split into 3-2.
                // Clipped, so a pad against the map edge does not expand out of bounds.
                CellRect surround = pad.ExpandedBy(1).ClipInsideMap(map);
                try
                {
                    GenDebug.ClearArea(surround, map);
                    SceneKit.EnclosePad(map, pad);
                    story.Note("the pad and a cell around it are cleared, then the pad is walled in wood, "
                               + "floored and roofed");

                    Room inside = pad.CenterCell.GetRoom(map);
                    if (!story.Check(inside != null && !inside.PsychologicallyOutdoors,
                                     "the walled pad makes a real indoor room"))
                    {
                        return;
                    }

                    int x = pad.CenterCell.x;
                    EdgeWallFindsTheRoom(story, map, new IntVec3(x, 0, pad.maxZ), inside, "north");
                    EdgeWallFindsTheRoom(story, map, new IntVec3(x, 0, pad.minZ), inside, "south");
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
        }

        /// <summary>
        /// One side of <see cref="JobInAnOuterWallFindsTheRoomInside"/>: the wall's ring must split
        /// evenly between the enclosed room and everything else (otherwise there is no tie to break
        /// and the test proves nothing), and a job at the wall must be placed in the room, not
        /// outdoors.
        /// </summary>
        internal static void EdgeWallFindsTheRoom(Story story, Map map, IntVec3 cell, Room inside, string edge)
        {
            Thing wall = cell.GetEdifice(map);
            if (!story.Check(wall != null, "there is a wall on the pad's " + edge + " side"))
            {
                return;
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

            story.Check(inRoom > 0 && inRoom == elsewhere,
                        "the cells around the " + edge + " wall split evenly, " + inRoom + " in the room and "
                        + elsewhere + " outside, so a tie-break decides");

            IntVec3 bearing = Patch_JobInterception.RoomBearing(
                cell, new LocalTargetInfo(wall), map);
            story.Check(bearing.GetRoom(map) == inside,
                        "a job at the " + edge + " wall is placed in the room it encloses");
        }

        /// <summary>What the game calls a work giver's work, or its defName when it has no label.</summary>
        internal static string Called(WorkGiverDef giver, string defName)
        {
            return giver == null || giver.label.NullOrEmpty() ? defName : giver.label;
        }

        /// <summary>What the game calls a kind of work ("hauling"), or its defName when it is missing.</summary>
        internal static string WorkCalled(WorkTypeDef work, string defName)
        {
            return work == null || work.gerundLabel.NullOrEmpty() ? defName : work.gerundLabel;
        }
    }
}
