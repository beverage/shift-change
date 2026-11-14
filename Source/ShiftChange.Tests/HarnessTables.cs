using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// The tables of the game's work givers and jobs that the tests read: which givers serve
    /// patients, which are left alone on purpose, which make deliveries, and which jobs ride
    /// along. <see cref="ReadingJobsAndRooms"/> checks each still names what the game has.
    /// </summary>
    internal static class HarnessTables
    {
        /// <summary>
        /// The givers that tend, feed or operate on a patient in a hospital or barn bed, by that
        /// role's defName. Left out: emergency tending dresses nobody, self-tending is the
        /// patient's own, Anomaly's entities lie on holding platforms, and taking a patient to bed
        /// for an operation starts wherever the patient is. The rest of
        /// <see cref="DeliberatelyIgnoredOfficialGivers"/> feed prisoners.
        /// </summary>
        internal static readonly Dictionary<string, string> PatientGiverRoles =
            new Dictionary<string, string>
            {
                { "DoctorTendToHumanlikes", "Hospital" },
                { "DoctorFeedHumanlikes", "Hospital" },
                { "FeedHemogen", "Hospital" },
                { "DoBillsMedicalHumanOperation", "Hospital" },
                { "DoctorTendToAnimals", "Barn" },
                { "DoctorFeedAnimals", "Barn" },
                { "HandlingFeedPatientAnimals", "Barn" },
                { "DoBillsMedicalAnimalOperation", "Barn" },
            };

        /// <summary>Whether a giver is missing only because Biotech, its source, is off.</summary>
        internal static bool BiotechGiverWithoutBiotech(WorkGiverDef giver, string name)
        {
            return giver == null && name.Contains("Hemogen") && !ModsConfig.BiotechActive;
        }

        /// <summary>
        /// Whether a work type comes from a mod rather than the game: a mod's own def, or one a
        /// patch added, which has no content pack.
        /// </summary>
        internal static bool IsModWork(WorkTypeDef work)
        {
            return work != null && work.modContentPack?.IsOfficialMod != true;
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
    }
}
