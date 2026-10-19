using System;
using System.Collections.Generic;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ShiftChange
{
    public static partial class Patch_JobInterception
    {
        /// <summary>
        /// What a job is to the outfit decision: the first test it passes, in the
        /// order <see cref="Classify"/> tests them. The order is behaviour: a
        /// delivery or an ignored giver's job is never Work, and VisitSickPawn,
        /// Doctor work with a joyKind, is Work. docs/DESIGN.md, "The gates".
        /// </summary>
        internal enum JobKind : byte
        {
            RidesAlong,

            Delivery,

            MealRun,

            IgnoredGiver,

            Work,

            Rest,

            Recreation,

            /// <summary>Anything else: eating, reading, sleeping on the ground, walks.</summary>
            Other,
        }

        /// <summary>A job's classification, made once per decision; each field holds its
        /// predicate's answer whatever the kind.</summary>
        internal readonly struct JobClass
        {
            internal readonly JobKind Kind;

            /// <summary>The giver's work type, or the one charged for giver-less medical rest or
            /// fishing.</summary>
            internal readonly WorkTypeDef Work;

            internal readonly bool PlayerForced;

            /// <summary>An emergency giver's job that the medical-emergency setting does not
            /// open.</summary>
            internal readonly bool ExemptEmergency;

            /// <summary><see cref="IsRecreationJob"/>, so true for VisitSickPawn, which is
            /// Work.</summary>
            internal readonly bool RecreationJob;

            internal readonly bool RestJob;

            internal readonly bool OrdinarySleep;

            internal readonly bool UrgentPatient;

            internal readonly bool Ingest;

            internal readonly bool Incidental;

            internal readonly bool LyingDown;

            internal JobClass(JobKind kind, WorkTypeDef work, bool playerForced, bool exemptEmergency,
                              bool recreationJob, bool restJob, bool ordinarySleep, bool urgentPatient,
                              bool ingest, bool incidental, bool lyingDown)
            {
                Kind = kind;
                Work = work;
                PlayerForced = playerForced;
                ExemptEmergency = exemptEmergency;
                RecreationJob = recreationJob;
                RestJob = restJob;
                OrdinarySleep = ordinarySleep;
                UrgentPatient = urgentPatient;
                Ingest = ingest;
                Incidental = incidental;
                LyingDown = lyingDown;
            }

            /// <summary>A delivery or a meal run, judged by the sites it supplies, not by where it
            /// starts.</summary>
            internal bool HasSites => Kind == JobKind.Delivery || Kind == JobKind.MealRun;

            internal bool DeliveryOrIgnored => HasSites || Kind == JobKind.IgnoredGiver;
        }

        /// <summary>
        /// The one place a job's kind is decided. Never kept across job boundaries:
        /// whether a lay-down is medical rest depends on the pawn's health then, and a setting.
        /// A queued job is classified with its own tag.
        /// </summary>
        internal static JobClass Classify(Pawn pawn, Job job, JobTag? tag)
        {
            WorkGiverDef giver = job.workGiverDef;
            WorkTypeDef work = WorkTypeFor(pawn, job, tag);
            bool restJob = IsRestJob(job);
            bool recreationJob = IsRecreationJob(job);
            JobKind kind = JobRoomTargets.RidesAlong(job) ? JobKind.RidesAlong
                : JobRoomTargets.IsDelivery(job) ? JobKind.Delivery
                : JobRoomTargets.IsMealRun(job) ? JobKind.MealRun
                : JobRoomTargets.Ignored(giver) ? JobKind.IgnoredGiver
                : work != null ? JobKind.Work
                : restJob ? JobKind.Rest
                : recreationJob ? JobKind.Recreation
                : JobKind.Other;
            return new JobClass(
                kind: kind,
                work: work,
                playerForced: job.playerForced,
                exemptEmergency: giver?.emergency == true && !EmergencyDressingAllowed(giver),
                recreationJob: recreationJob,
                restJob: restJob,
                ordinarySleep: IsOrdinarySleep(pawn, job, tag),
                urgentPatient: UrgentPatient(pawn, job, tag),
                ingest: IsIngestJob(job),
                incidental: JobRoomTargets.IsIncidental(job),
                lyingDown: IsLyingDown(job));
        }

        /// <summary>An ingest job, matched by driver class rather than JobDefOf.Ingest so modded
        /// ingest defs count too.</summary>
        internal static bool IsIngestJob(Job job)
        {
            Type driver = job.def?.driverClass;
            return driver != null && typeof(JobDriver_Ingest).IsAssignableFrom(driver);
        }

        /// <summary>
        /// A recreation job whose room is known as it starts: it carries a <c>joyKind</c>, and
        /// its driver is not <see cref="JobDriver_Reading"/> or derived from it, since reading
        /// picks its spot mid-job. A modded reading job with a driver of its own and a
        /// <c>joyKind</c> would pass; Alpha Books' carries no <c>joyKind</c>. VisitSickPawn
        /// passes though it is work, so every room resolver reads it B-first.
        /// </summary>
        internal static bool IsRecreationJob(Job job)
        {
            JobDef def = job.def;
            if (def?.joyKind == null)
            {
                return false;
            }
            Type driver = def.driverClass;
            return driver == null || !typeof(JobDriver_Reading).IsAssignableFrom(driver);
        }

        /// <summary>
        /// A lay-down on a bed. The bed test keeps out ground sleep, mech dormancy and
        /// Odyssey's deactivation, which share the <see cref="JobDriver_LayDown"/> family. A
        /// modded sleep job with a driver of its own does not pass.
        /// </summary>
        internal static bool IsRestJob(Job job)
        {
            Type driver = job.def?.driverClass;
            return driver != null
                   && typeof(JobDriver_LayDown).IsAssignableFrom(driver)
                   && job.targetA.Thing is Building_Bed;
        }

        /// <summary>A rest job the sleep arm takes: from no giver, and not a medical lay-down,
        /// whether or not the work arm takes it. The return trip asks it too, so both
        /// directions agree.</summary>
        internal static bool IsOrdinarySleep(Pawn pawn, Job job, JobTag? tag)
        {
            return IsRestJob(job) && job.workGiverDef == null && !IsMedicalLayDown(pawn, job, tag);
        }

        /// <summary>
        /// A lay-down tagged <c>RestingForMedicalReasons</c> by the patient givers, or untagged
        /// for an urgent patient: vanilla requeues it without its tag when it slips a haul in
        /// ahead (<c>Pawn_JobTracker.TryOpportunisticJob</c>). Urgent only, because
        /// <c>HealthAIUtility.ShouldSeekMedicalRest</c> cannot tell hurt from night.
        /// </summary>
        internal static bool IsMedicalLayDown(Pawn pawn, Job job, JobTag? tag)
        {
            if (!IsRestJob(job))
            {
                return false;
            }
            if (tag == JobTag.RestingForMedicalReasons)
            {
                return true;
            }
            return tag == null && job.workGiverDef == null && !job.playerForced
                   && pawn != null && HealthAIUtility.ShouldSeekMedicalRestUrgent(pawn);
        }

        /// <summary>Any lay-down, on a bed or the ground: what the change-back latch counts as
        /// sleep.</summary>
        internal static bool IsLyingDown(Job job)
        {
            Type driver = job?.def?.driverClass;
            return driver != null && typeof(JobDriver_LayDown).IsAssignableFrom(driver);
        }

        /// <summary>
        /// The work type a medical lay-down is charged to, since the patient givers' jobs
        /// carry no giver: always PatientBedRest, so the Patient row in the stand dialog
        /// stays inert.
        /// Null for a pawn already on a bed: this is the work arm's only bed guard, as no
        /// other work job this mod decides on can start from a bed
        /// (<c>JobDriver.CanBeginNowWhileLyingDown</c>). Null for an urgent patient too,
        /// while medical emergency dressing is off.
        /// </summary>
        internal static WorkTypeDef MedicalRestWorkType(Pawn pawn, Job job, JobTag? tag)
        {
            if (!IsMedicalLayDown(pawn, job, tag))
            {
                return null;
            }
            // Holds whatever the setting: that lets an emergency wait, not a patient be hauled off
            // a bed.
            if (OnABed(pawn))
            {
                return null;
            }
            if (UrgentPatient(pawn, job, tag))
            {
                return null;
            }
            return ShiftChangeDefOf.PatientBedRest;
        }

        /// <summary>
        /// An urgent patient's lay-down with the medical-emergency setting off: nobody is
        /// dressed for it, and nobody on shift is changed back before it.
        /// </summary>
        internal static bool UrgentPatient(Pawn pawn, Job job, JobTag? tag)
        {
            return !ShiftChangeMod.MedicalEmergencyDressingEnabled
                   && IsMedicalLayDown(pawn, job, tag)
                   && HealthAIUtility.ShouldSeekMedicalRestUrgent(pawn);
        }

        /// <summary>The next job is an urgent patient's lay-down, so this one is an errand slipped
        /// in ahead.</summary>
        internal static bool UrgentPatientNext(Pawn pawn, Pawn_JobTracker tracker)
        {
            JobQueue queue = tracker?.jobQueue;
            if (queue == null || queue.Count == 0)
            {
                return false;
            }
            QueuedJob head = queue.Peek();
            return head?.job != null && Classify(pawn, head.job, head.tag).UrgentPatient;
        }

        /// <summary>
        /// Work types whose emergencies the medical-emergency setting opens. Named, as
        /// firefighting is an emergency too. FSFNurse and FSFSurgeon are Complex Jobs'
        /// medical types, in case it ever moves an emergency giver onto them. A name with no
        /// def is a mod that is not installed, and is skipped.
        /// </summary>
        internal static readonly string[] MedicalWorkTypeNames =
        {
            "Doctor", "Patient", "PatientBedRest", "FSFNurse", "FSFSurgeon",
        };

        internal static HashSet<WorkTypeDef> medicalWorkTypes;

        internal static HashSet<WorkTypeDef> MedicalWorkTypes
        {
            get
            {
                if (medicalWorkTypes == null)
                {
                    medicalWorkTypes = new HashSet<WorkTypeDef>();
                    for (int i = 0; i < MedicalWorkTypeNames.Length; i++)
                    {
                        WorkTypeDef work =
                            DefDatabase<WorkTypeDef>.GetNamedSilentFail(MedicalWorkTypeNames[i]);
                        if (work != null)
                        {
                            medicalWorkTypes.Add(work);
                        }
                    }
                }
                return medicalWorkTypes;
            }
        }

        internal static bool EmergencyDressingAllowed(WorkGiverDef giver)
        {
            return ShiftChangeMod.MedicalEmergencyDressingEnabled
                   && giver?.workType != null
                   && MedicalWorkTypes.Contains(giver.workType);
        }

        /// <summary>
        /// A bed stands on the pawn's cell. Not <c>pawn.InBed()</c>, which needs a current
        /// job: at an ordinary job boundary <c>Pawn_JobTracker</c> has cleared it, so
        /// <c>InBed()</c> reads false for a pawn lying in their own bed, and true only at the
        /// wake-up. A pawn walking across a bed passes too, so the return trip asks
        /// <see cref="StaysInBed"/>.
        /// </summary>
        internal static bool OnABed(Pawn pawn)
        {
            Map map = pawn.MapHeld;
            if (!pawn.Spawned || map == null)
            {
                return false;
            }
            List<Thing> here = pawn.Position.GetThingList(map);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i] is Building_Bed)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The pawn is on a bed and this job keeps them there: it may begin while they lie
        /// down, or it targets that bed, since not every in-bed driver says so.
        /// <c>CanBeginNow</c> caches a throwaway driver on the job, which <c>StartJob</c> never
        /// uses and <c>Job.Clear()</c> drops; <see cref="OnABed"/> goes first so none is built
        /// away from a bed. A throwing driver constructor is caught by the prefix's fail-open
        /// handler, which the interceptor's calls run under.
        /// </summary>
        internal static bool StaysInBed(Pawn pawn, Job job)
        {
            return OnABed(pawn)
                   && (job.CanBeginNow(pawn, whileLyingDown: true) || TargetsBedUnder(pawn, job));
        }

        /// <summary>
        /// The job targets the bed under the pawn. Both targets: the lay-downs put the bed in
        /// A, and <c>JobGiver_MeditateInBed</c> puts it in B. <c>OccupiedRect</c>, as one of a
        /// double bed's two slots is not its <c>Position</c>.
        /// </summary>
        internal static bool TargetsBedUnder(Pawn pawn, Job job)
        {
            IntVec3 at = pawn.Position;
            Building_Bed a = job.targetA.Thing as Building_Bed;
            if (a != null && a.Spawned && a.OccupiedRect().Contains(at))
            {
                return true;
            }
            Building_Bed b = job.targetB.Thing as Building_Bed;
            return b != null && b.Spawned && b.OccupiedRect().Contains(at);
        }

        /// <summary>Where a recreation job happens: targetB, where the pawn sits, else
        /// <see cref="TargetPlace"/> (A, the venue, or the queue).</summary>
        internal static JobPlace JoyTargetPlace(Job job, Map map)
        {
            IntVec3 cell = job.targetB.HasThing ? job.targetB.Thing.PositionHeld : job.targetB.Cell;
            if (cell.IsValid && cell.InBounds(map))
            {
                return JobPlace.At(cell);
            }
            return TargetPlace(job, map);
        }

        internal static IntVec3 JoyTargetCell(Job job, Map map)
        {
            return JoyTargetPlace(job, map).RoomCell;
        }

        /// <summary>The pawn already holds the food: in hand, or in inventory by the same test
        /// <see cref="JobDriver_Ingest"/> uses for eating from inventory.</summary>
        internal static bool FoodSourceIsOnPawn(Job job, Pawn pawn)
        {
            Thing food = job.targetA.Thing;
            if (food == null)
            {
                return false;
            }
            if (pawn.inventory != null && pawn.inventory.Contains(food))
            {
                return true;
            }
            return pawn.carryTracker?.CarriedThing == food;
        }
    }
}
