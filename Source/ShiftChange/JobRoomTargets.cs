using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// How this mod reads another mod's jobs: which end of a job is the place
    /// it happens, and which jobs are not ours to act on at all.
    ///
    /// <para>Jobs whose location is their <c>targetB</c> rather than their
    /// <c>targetA</c>.
    ///
    /// <para><see cref="Patch_JobInterception.TargetCell"/> reads targetA,
    /// because that is where vanilla puts the place the work happens, in both
    /// of the shapes that matter: a bill's targetA is the WORKBENCH
    /// (<c>JobDriver_DoBill</c> keeps its ingredients in targetQueueB), and a
    /// haul's targetA is the THING BEING CARRIED
    /// (<c>JobDriver_HaulToCell</c>, <c>HaulToContainer</c>), whose cell is
    /// where the pawn starts. Either way targetA is where the pawn first puts
    /// their hands on the job.</para>
    ///
    /// <para>Dubs Rimatomics inverts that for fuel handling. Its givers build
    /// <c>new Job(def, t, val)</c> with <c>t</c> the DESTINATION — the reactor
    /// core or the plutonium processor, which is the thing its scanner walks —
    /// and <c>val</c> the rod or chemfuel found separately by
    /// <c>ClosestThingReachable</c>. The toils then run
    /// <c>GotoThing(TargetIndex 2)</c>, <c>StartCarryThing</c>,
    /// <c>GotoThing(TargetIndex 1)</c>: the pawn walks to the FUEL first. So
    /// targetB is where the job starts and where the radiation exposure
    /// begins, and targetA is a room the pawn only reaches already carrying a
    /// rod. Reading targetA sent us hunting for a stand in the reactor hall
    /// while the player's suits sat beside the storage pool, and nobody ever
    /// changed (reported from play, 2026-09-14).</para>
    ///
    /// <para><b>Why a table and not a rule.</b> "Prefer targetB when it is
    /// set" is wrong for vanilla hauling, where targetB is the DESTINATION
    /// cell — it would dress pawns at the far end of every haul. Nothing in a
    /// job's shape distinguishes the two conventions; only the job def does.
    /// So this is a list, and it is allowed to be incomplete.</para>
    ///
    /// <para><b>Why it must not consult the map.</b> The resolver is shared by
    /// the dressing arm and the change-back arm on purpose, and the two must
    /// answer identically for the same job or a pawn ping-pongs between the
    /// stand and the work forever. So this keys on the job def and nothing
    /// else — never on which room happens to have a free stand.</para>
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
        /// answer the player-forced and emergency gate already gives.
        ///
        /// <para>Keyed on the GIVER and not the job, because the job def
        /// cannot tell these apart. Rimatomics runs two work givers into one
        /// <c>LoadSpentFuel</c> JobDef: <c>WorkGiver_LoadPlutoniumProc</c>
        /// carries spent rods to the processor, and
        /// <c>WorkGiver_LoadPuProcChems</c> carries the CHEMFUEL that goes in
        /// with them. Their WorkGiverDefs do differ, and <c>JobGiver_Work</c>
        /// stamps <c>workGiverDef</c> on every scanner job, so the giver is
        /// the only thing here that separates them.</para>
        ///
        /// <para>Chemfuel is inert. Nothing about fetching it warrants a rad
        /// suit, and it is stored wherever a colony stores chemfuel rather
        /// than beside a reactor — 146 tiles away on the map this was reported
        /// from. Treating it as nuclear work meant a pawn either detoured to a
        /// wardrobe before a very long haul, or, if already suited, undressed
        /// for the trip and dressed again after: two wardrobe walks for a job
        /// that needed no suit. Changing is the expensive part, not the
        /// wearing.</para>
        ///
        /// <para>The rods themselves are unaffected. <c>LoadSpentFuel</c>'s
        /// other giver is not listed, so carrying spent fuel still dresses at
        /// the rod exactly as before.</para>
        /// </summary>
        internal static readonly HashSet<string> IgnoredGivers =
            new HashSet<string>
            {
                "LoadProcChemFuel",
            };

        /// <summary>Whether this giver's jobs are invisible to the mod.</summary>
        internal static bool Ignored(WorkGiverDef giver)
        {
            return giver != null && IgnoredGivers.Contains(giver.defName);
        }
    }
}
