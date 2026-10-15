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
        /// An ingest-family job — meals, drinks, drugs; anything driven by
        /// <see cref="JobDriver_Ingest"/> or a subclass. By driver class, not
        /// JobDefOf.Ingest, so modded ingest defs are covered too.
        /// </summary>
        internal static bool IsIngestJob(Job job)
        {
            Type driver = job.def?.driverClass;
            return driver != null && typeof(JobDriver_Ingest).IsAssignableFrom(driver);
        }

        /// <summary>
        /// A recreation job the room trigger can honestly serve: it carries a
        /// <c>joyKind</c>, and its driver is not a reading driver, which
        /// picks its spot mid-job. This classifies the JOB CLASS, not the arm
        /// that serves it, so VisitSickPawn (Doctor work with joyKind Social)
        /// is in class and every room-resolver site reads it B-first. Why
        /// joyKind, and why consumption and reading stay outside:
        /// docs/DESIGN.md, "The recreation branch".
        ///
        /// <para><b>The reading exclusion is NARROWER than the principle behind
        /// it, and knowingly so.</b> What disqualifies reading is not that it
        /// is reading — it is that the job CHOOSES ITS SPOT MID-JOB, so the
        /// target it carries at StartJob is not where the activity ends up
        /// happening. Nothing in the engine flags that behaviour, so there is
        /// no way to ask for it directly; the driver class is the only handle,
        /// and it only catches vanilla's own.
        ///
        /// A modded reading job with its own driver would therefore slip
        /// through, and dress a pawn for whichever room the book is shelved
        /// in. As of 1.6 none in practice does: Alpha Books' own read job
        /// (<c>ABooks_Reading</c>, driver <c>JobDriver_ReadAlphaBook</c>)
        /// carries NO joyKind, so it fails the first test and never reaches
        /// this one — excluded a step earlier, by luck rather than by design.
        ///
        /// If a report ever arrives of a stand firing while somebody reads,
        /// this is the shape of it, and the fix is to widen the test — either
        /// by naming the offending driver too, or by deferring the room
        /// decision for any job whose target moves after it starts.</para>
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
        /// A sleep job the room trigger can honestly serve: a lay-down job
        /// whose target is an actual BED. The bed test is what keeps ground
        /// sleep (no bed, and usually no valid target at all), mech dormancy
        /// and Odyssey's deactivation out, since all three run the same
        /// <see cref="JobDriver_LayDown"/> family: docs/DESIGN.md, "The sleep
        /// branch".
        ///
        /// <para>By driver class rather than by JobDef, matching
        /// <see cref="IsIngestJob"/>, so a modded sleep job that reuses
        /// vanilla's driver is covered. One that does not is not — the same
        /// known limit the reading exclusion carries, and the same shape of
        /// fix if a report ever arrives.</para>
        /// </summary>
        internal static bool IsRestJob(Job job)
        {
            Type driver = job.def?.driverClass;
            return driver != null
                   && typeof(JobDriver_LayDown).IsAssignableFrom(driver)
                   && job.targetA.Thing is Building_Bed;
        }

        /// <summary>
        /// A rest job the sleep arm takes: no giver, and not a medical lay-down
        /// (<see cref="IsMedicalLayDown"/>). Medical bed rest is the work arm's,
        /// and a giver with no work type is a modded one the sleep arm leaves
        /// alone (docs/DESIGN.md, "The sleep branch"). The return trip asks it
        /// too (StaysDressed), so both directions answer alike.
        /// </summary>
        internal static bool IsOrdinarySleep(Pawn pawn, Job job, JobTag? tag)
        {
            return IsRestJob(job) && job.workGiverDef == null && !IsMedicalLayDown(pawn, job, tag);
        }

        /// <summary>
        /// A lay-down in a bed for medical reasons: tagged
        /// <c>RestingForMedicalReasons</c>, as the patient givers hand it out,
        /// or the same job put back in the queue with no tag for a patient who
        /// needs a bed urgently. Vanilla's <c>StartJob</c> drops the tag when it
        /// slips a haul in ahead of the lay-down
        /// (<c>Pawn_JobTracker.TryOpportunisticJob</c>). A recuperating
        /// patient's lay-down put back that way cannot be told from ordinary
        /// sleep, and reads as sleep.
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

        /// <summary>
        /// A lay-down of any kind, on a bed or the ground: the
        /// <see cref="JobDriver_LayDown"/> family. What the change-back
        /// latch counts as sleep, so unlike <see cref="IsRestJob"/> it needs
        /// no bed.
        /// </summary>
        internal static bool IsLyingDown(Job job)
        {
            Type driver = job?.def?.driverClass;
            return driver != null && typeof(JobDriver_LayDown).IsAssignableFrom(driver);
        }

        /// <summary>
        /// The work type a medical lay-down should be charged to, or null when
        /// this job is not one or must not divert. This is the whole of the
        /// hospital-gown trigger: the patient givers' jobs arrive with no
        /// giver, so the <c>RestingForMedicalReasons</c> tag is what it
        /// resolves from.
        ///
        /// <para>Two refusals, both load-bearing. An urgent case (vanilla's
        /// own split, <see cref="HealthAIUtility.ShouldSeekMedicalRestUrgent"/>)
        /// never diverts unless the medical-emergency setting is on. And a
        /// pawn already on a bed never diverts: the work arm has no bed guard
        /// of its own, because no other work job can start from a bed. Only
        /// <c>PatientBedRest</c> is ever returned, so the <c>Patient</c> row in
        /// the stand dialog stays inert. The argument, and the known cost of
        /// the urgent refusal: docs/DESIGN.md, "The sleep branch".</para>
        /// </summary>
        internal static WorkTypeDef MedicalRestWorkType(Pawn pawn, Job job, JobTag? tag)
        {
            if (!IsMedicalLayDown(pawn, job, tag))
            {
                return null;
            }
            // The bed guard is UNCONDITIONAL and stays that way. The setting
            // below is about whether an emergency may be delayed; nothing in
            // it argues for hauling a patient off a bed they are already lying
            // on, which is a different behaviour with a different failure.
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
        /// A medical lay-down (<see cref="IsMedicalLayDown"/>) for a patient
        /// who needs a bed urgently
        /// (<see cref="HealthAIUtility.ShouldSeekMedicalRestUrgent"/>), with
        /// the medical-emergency setting off: nobody is dressed for it, and
        /// nobody on shift is changed back before it.
        /// </summary>
        internal static bool UrgentPatient(Pawn pawn, Job job, JobTag? tag)
        {
            return !ShiftChangeMod.MedicalEmergencyDressingEnabled
                   && IsMedicalLayDown(pawn, job, tag)
                   && HealthAIUtility.ShouldSeekMedicalRestUrgent(pawn);
        }

        /// <summary>
        /// The pawn's next queued job is an urgent patient's lay-down
        /// (<see cref="UrgentPatient"/>): whatever starts now is an errand
        /// vanilla slipped in ahead of going to bed, and changing out first
        /// would delay the patient anyway.
        /// </summary>
        internal static bool UrgentPatientNext(Pawn pawn, Pawn_JobTracker tracker)
        {
            JobQueue queue = tracker?.jobQueue;
            if (queue == null || queue.Count == 0)
            {
                return false;
            }
            QueuedJob head = queue.Peek();
            return head?.job != null && UrgentPatient(pawn, head.job, head.tag);
        }

        /// <summary>
        /// Work types whose emergencies the medical-emergency setting covers.
        /// Named, because <c>emergency</c> alone would include firefighting and
        /// walk colonists to a wardrobe while the base burns; the [FSF] Complex
        /// Jobs names are belt and braces against a future repoint. Same
        /// silent-fail contract as <see cref="RoomWorkTypes.CompatDefaults"/>:
        /// an absent name is a mod that is not installed. docs/DESIGN.md,
        /// "The sleep branch".
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

        /// <summary>
        /// May this emergency giver's job be delayed for a change, because the
        /// player asked for that and this is medical work?
        /// </summary>
        internal static bool EmergencyDressingAllowed(WorkGiverDef giver)
        {
            return ShiftChangeMod.MedicalEmergencyDressingEnabled
                   && giver?.workType != null
                   && MedicalWorkTypes.Contains(giver.workType);
        }

        /// <summary>
        /// Is this pawn physically on a bed right now?
        ///
        /// <para><b>Not <c>pawn.InBed()</c>, and the difference is the whole
        /// point.</b> At an ordinary job boundary it reads FALSE for a colonist
        /// lying in their own bed, and true only at the wake-up, the opposite
        /// of what every guard here means (<c>RestUtility.cs:505</c>,
        /// <c>Pawn_JobTracker.cs:501</c>, <c>Toils_LayDown.cs:74</c>).
        /// docs/DESIGN.md, "The sleep branch".</para>
        ///
        /// <para>Position answers the question <c>curJob</c> cannot. A pawn
        /// merely walking across a bed tile also passes, which is why the
        /// return trip pairs this with <see cref="StaysInBed"/> rather than
        /// using it alone.</para>
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
        /// The pawn is on a bed AND this incoming job is one they can do
        /// without getting off it: <c>Job.CanBeginNow(pawn, whileLyingDown:
        /// true)</c> (false on the base driver, <c>JobDriver.cs:303-306</c>),
        /// or a job whose own target is the bed under them
        /// (<see cref="TargetsBedUnder"/>), because the drivers' own list is
        /// incomplete. Staying in bed means stay dressed; getting up means
        /// change back while still beside the stand. docs/DESIGN.md, "The
        /// sleep branch". (Eating still reaches the ingest branch further
        /// down, which has its own deliberate policy; this only stops the
        /// return trip firing first.)
        ///
        /// <para>It caches a driver on the job (<c>Job.GetCachedDriver</c>),
        /// which is what vanilla's own callers do and what <see cref="Insert"/>
        /// already does for its reservation dry run. <c>StartJob</c> builds the
        /// real driver with <c>MakeDriver</c>, never the cached one, and pooled
        /// jobs are <c>Clear()</c>ed before reuse, so the throwaway cannot leak
        /// into a live job. A throwing driver constructor is caught by the
        /// prefix's fail-open handler. Short-circuited behind
        /// <see cref="OnABed"/>, so nothing is constructed at the ordinary job
        /// boundary where the pawn is nowhere near a bed.</para>
        /// </summary>
        internal static bool StaysInBed(Pawn pawn, Job job)
        {
            return OnABed(pawn)
                   && (job.CanBeginNow(pawn, whileLyingDown: true) || TargetsBedUnder(pawn, job));
        }

        /// <summary>
        /// This job's own target is the bed the pawn is lying on — so whatever
        /// its driver claims, doing it does not mean getting up.
        ///
        /// <para>Both A and B, because the in-bed job givers disagree about
        /// which they use: <c>JobGiver_MeditateInBed</c> puts the pawn's cell in
        /// A and the bed in B, while the lay-down family puts the bed in A.
        /// <c>OccupiedRect</c> rather than <c>Position</c> so a double bed's
        /// second slot counts.</para>
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

        /// <summary>
        /// Where a recreation job happens: <c>targetB</c> FIRST, the cell the
        /// pawn occupies while the joy ticks, then the work-style
        /// <see cref="TargetPlace"/>, since where B is unset A is already the
        /// venue. docs/DESIGN.md, "The recreation branch".
        /// </summary>
        internal static JobPlace JoyTargetPlace(Job job, Map map)
        {
            IntVec3 cell = job.targetB.HasThing ? job.targetB.Thing.PositionHeld : job.targetB.Cell;
            if (cell.IsValid && cell.InBounds(map))
            {
                return JobPlace.At(cell);
            }
            return TargetPlace(job, map);
        }

        /// <summary>The room cell of <see cref="JoyTargetPlace"/>.</summary>
        internal static IntVec3 JoyTargetCell(Job job, Map map)
        {
            return JoyTargetPlace(job, map).RoomCell;
        }

        /// <summary>
        /// The job's food source is already on the pawn — in inventory (the
        /// same test the driver itself uses for eatingFromInventory,
        /// JobDriver_Ingest.cs:86) or in their hands.
        /// </summary>
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
