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
    /// compatibility".
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
        /// WorkGiverDefs this mod ignores completely, in BOTH directions: no
        /// dressing for them, and no changing back out either. The uniform
        /// rides along and the next ordinary job settles it, which is the same
        /// answer the player-forced and emergency gate already gives. Keyed on
        /// the GIVER because one job def can carry two givers that want
        /// opposite answers. Why each row: docs/DESIGN.md, "Mod
        /// compatibility".
        ///
        /// <para>The rows: Rimatomics' chemfuel run into the plutonium
        /// processor (its spent-rod run shares the job def, is not listed, and
        /// still dresses at the rod), and vanilla's six feeding givers, the
        /// only official rows. Vanilla feeds animals and hands out hemogen
        /// under Doctor, which is why those sit beside the patient's;
        /// FeedHemogen and DeliverHemogenToPrisoner are Biotech's and simply
        /// never match without it.</para>
        /// </summary>
        internal static readonly HashSet<string> IgnoredGivers =
            new HashSet<string>
            {
                "LoadProcChemFuel",
                "DoctorFeedHumanlikes",
                "DoctorFeedAnimals",
                "FeedHemogen",
                "FeedPrisoner",
                "DeliverFoodToPrisoner",
                "DeliverHemogenToPrisoner",
            };

        /// <summary>Whether this giver's jobs are invisible to the mod.</summary>
        internal static bool Ignored(WorkGiverDef giver)
        {
            return giver != null && IgnoredGivers.Contains(giver.defName);
        }

        /// <summary>
        /// JobDefs whose jobs ride along when they carry NO giver: the
        /// follow-ups a haul leaves behind it, which finish the haul rather than
        /// start anything new. Same answer as <see cref="IgnoredGivers"/>, keyed
        /// on the job because these have no giver to key on.
        ///
        /// <para>The test is "no giver", not the def alone, because the giver
        /// is what separates a fresh <c>HaulToInventory</c> from a
        /// continuation. Which mod queues which row, and why each read as
        /// leaving the room: docs/DESIGN.md, "Mod compatibility".</para>
        /// </summary>
        internal static readonly HashSet<string> RideAlongJobs =
            new HashSet<string>
            {
                "UnloadYourHauledInventory",
                "HaulToInventory",
                "UnloadYourInventory",
            };

        /// <summary>Whether this job is a giver-less follow-up the uniform rides along on.</summary>
        internal static bool RidesAlong(Job job)
        {
            return job?.def != null && job.workGiverDef == null
                   && RideAlongJobs.Contains(job.def.defName);
        }
    }
}
