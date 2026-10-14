using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShiftChange
{
    /// <summary>
    /// How this mod reads jobs whose targets would mislead it: which end of a
    /// job is the place it happens, and which jobs are not ours to act on at
    /// all. Mostly other mods' jobs, plus vanilla's feeding givers. Why each
    /// list exists, and why a list rather than a rule: docs/DESIGN.md, "Mod
    /// compatibility". Also which jobs a pawn may do in uniform inside a
    /// radius or area stand's catchment besides the stand's own:
    /// docs/DESIGN.md, "Catchments".
    ///
    /// <para><b>It must not consult the map.</b> The dressing arm and the
    /// change-back arm share this resolver and must answer identically for
    /// the same job, or a pawn walks between the stand and the work forever,
    /// so it keys on the job and giver defs and nothing else: never on which
    /// room happens to have a free stand.</para>
    /// </summary>
    internal static class JobRoomTargets
    {
        /// <summary>
        /// Job defNames whose location is targetB. Matched as strings, so a
        /// game without the mod that supplies them simply never matches.
        ///
        /// <para><c>HaulModuletoCore</c> is fuel into a reactor
        /// (<c>WorkGiver_LoadFuelModule</c>). <c>LoadSpentFuel</c> is used by
        /// TWO givers — <c>WorkGiver_LoadPlutoniumProc</c> for spent rods and
        /// <c>WorkGiver_LoadPuProcChems</c> for the chemfuel that goes with
        /// them — and both build it the same way round.</para>
        ///
        /// <para><c>LoadSilo</c> (<c>WorkGiver_LoadSilo</c>) and
        /// <c>LoadRailgunMagazine</c> (<c>WorkGiver_LoadMagazine</c>) are the
        /// same shape again, for the missile silo and the railgun: destination
        /// in targetA, the round in targetB, toils walking to the round first.
        /// Both are <c>workType Hauling</c>, which no room arms by default, so
        /// they reach this only through a hand-ticked Hauling stand. They were
        /// missed when this table was first written because the survey stopped
        /// at the fuel loop; an adversarial review of v1.4.0 found them.</para>
        ///
        /// <para>Deliberately absent: <c>UnloadPlutonium</c> and
        /// <c>RemoveFuelModule</c>. Those take the material OUT of a single
        /// building and their targetA is that building, which is already
        /// where the work happens.</para>
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
        /// WorkGiverDefs whose jobs dress nobody. A colonist in uniform keeps
        /// it on for one when their stand serves the giver's work, and for a
        /// meal run (<see cref="IsMealRun"/>) only when the patient or prisoner
        /// is inside the stand's catchment too; anyone else takes the return
        /// trip (<c>Patch_JobInterception.TryInsertSwap</c>). Keyed on the
        /// GIVER because one job def can carry two givers that want opposite
        /// answers. Why each row: docs/DESIGN.md, "Mod compatibility".
        ///
        /// <para>The rows: Rimatomics' chemfuel run into the plutonium
        /// processor (its spent-rod run shares the job def, is not listed, and
        /// still dresses at the rod), and vanilla's seven feeding givers, the
        /// only official rows. Vanilla feeds animals and hands out hemogen
        /// under Doctor, which is why those sit beside the patient's, and
        /// feeds animals under Handling too, from the same giver class;
        /// FeedHemogen and DeliverHemogenToPrisoner are Biotech's and simply
        /// never match without it.</para>
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
        /// A meal carried to a patient or a prisoner: <c>FeedPatient</c> or
        /// <c>DeliverFood</c> from one of the feeding givers in
        /// <see cref="IgnoredGivers"/>. Each has the meal in targetA and the
        /// patient or prisoner in targetB; <c>DeliverFood</c> has the spot it
        /// leaves the meal at in targetC, and <c>FeedPatient</c> a colonist
        /// carrying the meal, when one is.
        /// </summary>
        internal static bool IsMealRun(Job job)
        {
            return job?.def != null && Ignored(job.workGiverDef)
                   && (job.def == JobDefOf.FeedPatient || job.def == JobDefOf.DeliverFood);
        }

        /// <summary>
        /// The seed planter's giver. Its <c>PlantSeed</c> job is a delivery
        /// (<see cref="IsDelivery"/>), and the <c>CutPlant</c> it hands out for
        /// a plant in the way is ordinary work at the planting cell.
        /// </summary>
        internal const string SeedPlanter = "PlantSeed";

        /// <summary>
        /// A job that starts at a stack and carries it to a site: a
        /// construction delivery (<c>HaulToContainer</c> from a
        /// <see cref="DeliveryGivers"/> giver), a replanting (<c>Replant</c>,
        /// from the same class), or a seed carried to its planting cell
        /// (<c>PlantSeed</c> from <see cref="SeedPlanter"/>). Read at targetA,
        /// the stack, such a job dressed a bare builder for the walk to the
        /// stockpile and sent a dressed one back to change before it.
        ///
        /// <para>Keyed on the job def as well as the giver, because the same
        /// givers hand out the site's own jobs: clearing what blocks a
        /// blueprint (cutting, hauling aside, deconstructing, mining),
        /// removing an old floor, placing a frame that costs nothing. Those
        /// happen at the site and are ordinary work. How each direction treats
        /// a delivery: <c>Patch_JobInterception.TryInsertSwap</c> and
        /// docs/DESIGN.md, "Mod compatibility".</para>
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

        internal static HashSet<WorkGiverDef> deliveryGivers;

        /// <summary>
        /// Every giver whose class derives from
        /// <c>WorkGiver_ConstructDeliverResources</c>, whose deliveries
        /// <see cref="IsDelivery"/> recognises. By class, so a mod's delivery
        /// giver built on vanilla's classes needs no row, and vanilla's
        /// replanting giver, which derives from the same class, comes with
        /// them. Resolved once: the def set is fixed for the process.
        /// docs/DESIGN.md, "Mod compatibility".
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
        /// JobDefs whose jobs ride along when they carry NO giver: the
        /// follow-ups a haul leaves behind it, which finish the haul rather than
        /// start anything new. They ride along in both directions, as a direct
        /// order does, keyed on the job because these have no giver to key on.
        ///
        /// <para>The test is "no giver", not the def alone, because the giver
        /// is what separates a fresh <c>HaulToInventory</c> from a
        /// continuation. Which mod queues which row, and why each read as
        /// leaving the room: docs/DESIGN.md, "Mod compatibility".</para>
        ///
        /// <para><c>Wait_MaintainPosture</c> is the job tracker's own pause:
        /// one tick, started as a job succeeds and before the queue gets a turn
        /// (<c>Pawn_JobTracker.EndCurrentJob</c>), so it falls between every
        /// swap and the job deferred behind it. It finishes a job rather than
        /// starting one: docs/DESIGN.md, "Mod compatibility".</para>
        ///
        /// <para><c>BringBabyToSafety</c> is how Biotech's feeding and playing
        /// end: the carer is handed the baby to take to safety as the job
        /// finishes (<c>ChildcareUtility</c>). Judged on its own it sent a
        /// carer at a radius or area childcare stand to change, putting the
        /// baby on the floor first. <c>BringBabyToSafetyUnforced</c> is the
        /// rescue of a baby left somewhere unsafe, from the childcare giver's
        /// <c>NonScanJob</c> and from the think tree's urgent giver, both with
        /// no giver stamped. It is not dressed for and changes nobody back, as
        /// an emergency is not.</para>
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

        /// <summary>Whether this job is a giver-less follow-up the uniform rides along on.</summary>
        internal static bool RidesAlong(Job job)
        {
            return job?.def != null && job.workGiverDef == null
                   && RideAlongJobs.Contains(job.def.defName);
        }

        /// <summary>
        /// Giver-less JobDefs a pawn keeps a radius or area stand's uniform on
        /// for, inside its catchment: idling and wandering (<c>JobGiver_IdleError</c>,
        /// <c>JobGiver_Wander</c>), and walking somewhere allowed or safe
        /// (<c>JobGiver_SeekAllowedArea</c>, <c>JobGiver_SeekSafeTemperature</c>).
        /// A list rather than whatever is left over, so dining, deathrest,
        /// reading and sleeping on the ground still send the pawn back first:
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

        /// <summary>Whether this job is one of the giver-less <see cref="IncidentalJobs"/>.</summary>
        internal static bool IsIncidental(Job job)
        {
            return job?.def != null && job.workGiverDef == null
                   && IncidentalJobs.Contains(job.def.defName);
        }

        /// <summary>
        /// Work a work stand serving a radius or an area lets through in
        /// uniform when every place the job names is inside the catchment. Tags, not work type
        /// names, because Complex Jobs splits hauling, repair and
        /// deconstruction into work types of its own and Allow Utilities adds
        /// an urgent haul, and all of them carry these tags.
        /// </summary>
        internal const WorkTags PassThroughTags =
            WorkTags.Hauling | WorkTags.Cleaning | WorkTags.Constructing | WorkTags.Firefighting;

        /// <summary>Whether work of this type is pass-through work (<see cref="PassThroughTags"/>).</summary>
        internal static bool PassesThrough(WorkTypeDef work)
        {
            return work != null && (work.workTags & PassThroughTags) != WorkTags.None;
        }
    }
}
