using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShiftChange
{
    /// <summary>
    /// Lists of jobs whose targets would mislead this mod: which end of a job is the place it
    /// happens, which jobs dress nobody, and which jobs a pawn may do in uniform. Some rows are
    /// other mods' jobs, some vanilla's: the feeding givers, the deliveries, the incidental walks
    /// and most ride-alongs. Rows are defNames matched as strings, so a row for a mod that is not
    /// installed never matches. Why each list exists, and why a list rather than a rule:
    /// docs/DESIGN.md, "Mod compatibility" and "Catchments".
    ///
    /// <para>Nothing here may consult the map. The dressing and change-back arms share these
    /// answers and must agree on any job, or a pawn shuttles between stand and work, so they
    /// key on the job and giver defs and never on, say, which room has a free stand.</para>
    /// </summary>
    internal static class JobRoomTargets
    {
        /// <summary>
        /// Job defNames whose place is targetB. Each puts the destination in targetA and the
        /// item in targetB, and its toils walk to the item first.
        ///
        /// <para><c>HaulModuletoCore</c> is fuel into a reactor (<c>WorkGiver_LoadFuelModule</c>).
        /// <c>LoadSpentFuel</c> is used by two givers: <c>WorkGiver_LoadPlutoniumProc</c> for
        /// spent rods and <c>WorkGiver_LoadPuProcChems</c> for the chemfuel that goes with them.
        /// <c>LoadSilo</c> (<c>WorkGiver_LoadSilo</c>) and <c>LoadRailgunMagazine</c>
        /// (<c>WorkGiver_LoadMagazine</c>) load the missile silo and the railgun, the round
        /// being the item. Both are Hauling work, which no room arms by default, so only a stand
        /// with Hauling ticked dresses for them.</para>
        ///
        /// <para>Deliberately absent: <c>UnloadPlutonium</c> and <c>RemoveFuelModule</c>. They
        /// take the material out of a single building, and their targetA is that building,
        /// which is already where the work happens.</para>
        /// </summary>
        internal static readonly HashSet<string> RoomIsTargetB =
            new HashSet<string>
            {
                "HaulModuletoCore",
                "LoadSpentFuel",
                "LoadSilo",
                "LoadRailgunMagazine",
            };

        /// <summary>Whether this job's room should be read from targetB.</summary>
        internal static bool UsesTargetB(JobDef def)
        {
            return def != null && RoomIsTargetB.Contains(def.defName);
        }

        /// <summary>
        /// WorkGiverDefs whose jobs dress nobody; what a colonist already in uniform does for
        /// one is <c>Patch_JobInterception.KeepsTheUniformFor</c>. Keyed on the giver because
        /// one job def can carry two givers that want opposite answers.
        ///
        /// <para>The rows: Rimatomics' chemfuel run into the plutonium processor (its spent-rod
        /// run shares the job def, is not listed, and still dresses at the rod), and vanilla's
        /// seven feeding givers, the only official rows (the harness fails on any other).
        /// Vanilla feeds animals under Doctor, and Biotech administers hemogen to patients under
        /// Doctor (<c>FeedHemogen</c>), so those rows sit beside the patient-feeding one; Handling
        /// feeds animals too, from the same giver class. <c>DeliverHemogenToPrisoner</c> is Warden
        /// work, beside the two prisoner rows. Both hemogen rows are Biotech's and never match
        /// without it.</para>
        ///
        /// <para>Deliveries are not here: they are judged by job, not by giver
        /// (<see cref="IsDelivery"/>).</para>
        /// </summary>
        internal static readonly HashSet<string> IgnoredGivers =
            new HashSet<string>
            {
                "LoadProcChemFuel",
                "DoctorFeedHumanlikes",
                "DoctorFeedAnimals",
                "HandlingFeedPatientAnimals",
                "FeedHemogen",
                "FeedPrisoner",
                "DeliverFoodToPrisoner",
                "DeliverHemogenToPrisoner",
            };

        /// <summary>Whether this giver's jobs dress nobody.</summary>
        internal static bool Ignored(WorkGiverDef giver)
        {
            return giver != null && IgnoredGivers.Contains(giver.defName);
        }

        /// <summary>
        /// A meal carried to a patient or a prisoner: <c>FeedPatient</c> or <c>DeliverFood</c>
        /// from one of the feeding givers in <see cref="IgnoredGivers"/>. Each has the meal in
        /// targetA and the patient or prisoner in targetB; <c>DeliverFood</c> has the spot it
        /// leaves the meal at in targetC, and <c>FeedPatient</c> a colonist carrying the meal,
        /// when one is.
        /// </summary>
        internal static bool IsMealRun(Job job)
        {
            return job?.def != null && Ignored(job.workGiverDef)
                   && (job.def == JobDefOf.FeedPatient || job.def == JobDefOf.DeliverFood);
        }

        /// <summary>
        /// The seed planter's giver. Its <c>PlantSeed</c> job is a delivery
        /// (<see cref="IsDelivery"/>); the <c>CutPlant</c> it hands out for a plant in the way
        /// is ordinary work at the planting cell.
        /// </summary>
        internal const string SeedPlanter = "PlantSeed";

        /// <summary>
        /// A job that starts at a stack and carries it to a site: a construction delivery
        /// (<c>HaulToContainer</c> from a <see cref="DeliveryGivers"/> giver), a replanting
        /// (<c>Replant</c>, from the same class), or a seed carried to its planting cell
        /// (<c>PlantSeed</c> from <see cref="SeedPlanter"/>). Its targetA is the stack, wherever
        /// it lies, which is not where the work happens.
        ///
        /// <para>Keyed on the job def as well as the giver, because the same givers hand out
        /// the site's own jobs: clearing what blocks a blueprint (cutting, hauling aside,
        /// deconstructing, mining), removing an old floor, placing a frame that costs nothing.
        /// Those happen at the site and are ordinary work.</para>
        /// </summary>
        internal static bool IsDelivery(Job job)
        {
            WorkGiverDef giver = job?.workGiverDef;
            if (giver == null || job.def == null)
            {
                return false;
            }
            if (DeliveryGivers.Contains(giver))
            {
                return job.def == JobDefOf.HaulToContainer || job.def == JobDefOf.Replant;
            }
            return giver.defName == SeedPlanter && job.def == JobDefOf.PlantSeed;
        }

        /// <summary>
        /// Work givers whose jobs come only through <c>NonScanJob</c>, by the JobDef they make.
        /// <c>JobGiver_Work</c> stamps <c>workGiverDef</c> on its scanner paths alone, so these
        /// jobs reach <c>StartJob</c> with no giver and the work arm would not see them; this
        /// hands them the giver back, as <c>MedicalRestWorkType</c> does for medical bed rest.
        ///
        /// <para>A job matches only with no giver stamped, not player-forced, and with the tag
        /// the giver's non-scan path gives (<c>JobGiver_Work</c> passes the giver's own
        /// <c>tagToGive</c> along), so the same job ordered from anywhere else is left alone.
        /// No tag at all matches too: vanilla's <c>StartJob</c> puts a job back in the queue
        /// with none when it slips a haul in ahead of it, so that is how the job starts again
        /// (<c>Pawn_JobTracker.TryOpportunisticJob</c>).</para>
        ///
        /// <para><c>Fish</c> is Odyssey's fishing (<c>WorkGiver_Fish</c>, whose
        /// <c>tagToGive</c> is Fieldwork). The zone's own "fish here" order makes the same job
        /// with <c>JobTag.Misc</c>, player-forced, which no stand acts on anyway. Fishing water
        /// is outdoors on nearly every map, so the row mostly matters to radius and area
        /// stands: docs/DESIGN.md, "Catchments".</para>
        /// </summary>
        internal static readonly Dictionary<string, string> NonScanGivers =
            new Dictionary<string, string>
            {
                { "Fish", "Fish" },
            };

        /// <summary>
        /// The giver a giver-less <see cref="NonScanGivers"/> job came from, or null when this
        /// job is not one, the giver's mod is absent or the giver is ignored.
        /// </summary>
        internal static WorkGiverDef NonScanGiver(Job job, JobTag? tag)
        {
            if (job?.def == null || job.workGiverDef != null || job.playerForced
                || !NonScanGivers.TryGetValue(job.def.defName, out string giverName))
            {
                return null;
            }
            WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(giverName);
            if (giver == null || (tag != null && tag != giver.tagToGive) || Ignored(giver))
            {
                return null;
            }
            return giver;
        }

        internal static HashSet<WorkGiverDef> deliveryGivers;

        /// <summary>
        /// Every giver whose class derives from <c>WorkGiver_ConstructDeliverResources</c>,
        /// whose deliveries <see cref="IsDelivery"/> recognises. By class, so a mod's delivery
        /// giver built on vanilla's classes needs no row, and vanilla's replanting giver, which
        /// derives from the same class, comes with them. Resolved once: the def set is fixed
        /// for the process.
        /// </summary>
        internal static HashSet<WorkGiverDef> DeliveryGivers
        {
            get
            {
                if (deliveryGivers == null)
                {
                    deliveryGivers = new HashSet<WorkGiverDef>();
                    List<WorkGiverDef> all = DefDatabase<WorkGiverDef>.AllDefsListForReading;
                    for (int i = 0; i < all.Count; i++)
                    {
                        Type giverClass = all[i].giverClass;
                        if (giverClass != null
                            && typeof(WorkGiver_ConstructDeliverResources).IsAssignableFrom(giverClass))
                        {
                            deliveryGivers.Add(all[i]);
                        }
                    }
                }
                return deliveryGivers;
            }
        }

        /// <summary>
        /// JobDefs whose jobs ride along when they carry no giver: the follow-ups a haul leaves
        /// behind it, which finish the haul rather than start anything new. They ride along in
        /// both directions, as a direct order does. The test is "no giver", not the def alone,
        /// because the giver is what separates a fresh <c>HaulToInventory</c> from a
        /// continuation. Which mod queues which row, and why each reads as leaving the room:
        /// docs/DESIGN.md, "Mod compatibility".
        ///
        /// <para><c>Wait_MaintainPosture</c> is the job tracker's own pause: one tick, started
        /// as a job succeeds and before the queue gets a turn
        /// (<c>Pawn_JobTracker.EndCurrentJob</c>), so it falls between every swap and the job
        /// deferred behind it. It finishes a job rather than starting one.</para>
        ///
        /// <para><c>BringBabyToSafety</c> is how Biotech's feeding and playing end: the carer
        /// is handed the baby to take to safety as the job finishes (<c>ChildcareUtility</c>).
        /// Judged on its own it would send a carer at a radius or area childcare stand to
        /// change, and the swap would put the baby down. <c>BringBabyToSafetyUnforced</c> is the
        /// rescue of a baby left somewhere unsafe, from the childcare giver's <c>NonScanJob</c>
        /// and from the think tree's urgent giver, both with no giver stamped. It is not
        /// dressed for and changes nobody back, as an emergency is not.</para>
        /// </summary>
        internal static readonly HashSet<string> RideAlongJobs =
            new HashSet<string>
            {
                "UnloadYourHauledInventory",
                "HaulToInventory",
                "UnloadYourInventory",
                "Wait_MaintainPosture",
                "BringBabyToSafety",
                "BringBabyToSafetyUnforced",
            };

        /// <summary>Whether this job is a giver-less follow-up the uniform stays on for.</summary>
        internal static bool RidesAlong(Job job)
        {
            return job?.def != null && job.workGiverDef == null
                   && RideAlongJobs.Contains(job.def.defName);
        }

        /// <summary>
        /// Giver-less JobDefs a pawn keeps a radius or area stand's uniform on for, inside its
        /// catchment: idling and wandering (<c>JobGiver_IdleError</c>, <c>JobGiver_Wander</c>),
        /// and walking somewhere allowed or safe (<c>JobGiver_SeekAllowedArea</c>,
        /// <c>JobGiver_SeekSafeTemperature</c>). A list rather than whatever is left over, so
        /// dining, deathrest, reading and sleeping on the ground still send the pawn back first:
        /// docs/DESIGN.md, "Catchments".
        /// </summary>
        internal static readonly HashSet<string> IncidentalJobs =
            new HashSet<string>
            {
                "Wait",
                "Wait_Wander",
                "GotoWander",
                "Goto",
                "GotoSafeTemperature",
                "Wait_SafeTemperature",
            };

        /// <summary>Whether this is a giver-less job from <see cref="IncidentalJobs"/>.</summary>
        internal static bool IsIncidental(Job job)
        {
            return job?.def != null && job.workGiverDef == null
                   && IncidentalJobs.Contains(job.def.defName);
        }

        /// <summary>
        /// Work a work stand serving a radius or an area lets through in uniform when every
        /// place the job names is inside the catchment. Tags, not work type names, because
        /// Complex Jobs splits hauling, repair and deconstruction into work types of its own and
        /// Allow Utilities adds an urgent haul, and all of them carry these tags.
        /// </summary>
        internal const WorkTags PassThroughTags =
            WorkTags.Hauling | WorkTags.Cleaning | WorkTags.Constructing | WorkTags.Firefighting;

        /// <summary>Whether work of this type is <see cref="PassThroughTags"/> work.</summary>
        internal static bool PassesThrough(WorkTypeDef work)
        {
            return work != null && (work.workTags & PassThroughTags) != WorkTags.None;
        }
    }
}
