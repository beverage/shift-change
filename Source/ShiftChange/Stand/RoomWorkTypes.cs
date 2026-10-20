using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Maps a room's role to the set of work types a stand in that room dresses for by
    /// default. A set, because a room hosts several kinds of work; base-wide work that only
    /// passes through a room (hauling, cleaning) is left out. The table and the reasoning
    /// behind each row: docs/DESIGN.md, "Rooms to work types".
    ///
    /// Resolved by defName rather than through <see cref="RoomRoleDefOf"/>, because
    /// <c>Kitchen</c> has no DefOf field though the def ships in Core. Lookups fail
    /// silently, so a role or work type another mod removed drops out of the table instead
    /// of throwing at startup.
    /// </summary>
    public static class RoomWorkTypes
    {
        internal static readonly List<WorkTypeDef> None = new List<WorkTypeDef>();

        /// <summary>
        /// Role defName to work type defNames, vanilla only. Every name must exist, and the
        /// harness asserts it: a name that stops resolving narrows or empties a role's list with
        /// no log line, and an emptied Hospital or Kitchen row leaves automatic stands in those
        /// rooms with nothing to dress for. Names from other mods go in
        /// <see cref="CompatDefaults"/>, which is allowed to miss.
        /// </summary>
        internal static readonly Dictionary<string, string[]> Defaults =
            new Dictionary<string, string[]>
            {
                { "Hospital",   new[] { "Doctor" } },
                { "Laboratory", new[] { "Research", "Crafting" } },
                { "Kitchen",    new[] { "Cooking" } },
                { "Workshop",   new[] { "Crafting", "Tailoring", "Smithing", "Art" } },
                { "Barn",       new[] { "Handling", "Doctor" } },
            };

        /// <summary>
        /// Work types folded into <see cref="Defaults"/> when another mod supplies them:
        /// [FSF] Complex Jobs (<c>FrozenSnowFox.ComplexJobs</c>), which moves vanilla work
        /// onto finer types of its own. A separate table, because these names may be absent
        /// and the vanilla ones may not, and the harness asserts only the vanilla ones. Why:
        /// docs/DESIGN.md, "Mod compatibility".
        ///
        /// <para>A row belongs only where the type inherits work from <see cref="Defaults"/>
        /// and that work lands in this role's room, checked against each bench's
        /// <c>workTableRoomRole</c>. So the brewery counts as Workshop, not Kitchen, though
        /// brewing is what leaves Cooking, and the drug lab is Laboratory. Left out on
        /// purpose: <c>FSFPaint</c> and <c>FSFSmoothing</c>, which collect the base-wide work
        /// the sets exclude, and <c>FSFMechanoids</c>, whose gestators are Laboratory-roled
        /// but whose work is vanilla Smithing, so a lab stand never dressed for it.</para>
        ///
        /// <para><c>FSFSlaughter</c> exists only when Complex Jobs' own XML Extensions option
        /// is switched on, so it is absent even with that mod installed. That is the normal
        /// case for this table, not a defect.</para>
        ///
        /// <para><c>FSFTaming</c> is absent on purpose, and the reason is the room gate rather
        /// than the work. Taming (<c>WorkGiver_Tame</c>, which reads the Tame designation)
        /// targets a wild animal, and the gate keys on where the target stands. A barn is an
        /// enclosed room scored by its animal beds, so the target is out on the map rather
        /// than in it; a wild animal that wanders inside is the only exception. Vanilla's
        /// <c>Handling</c> row never fired for taming either, and a row here would inherit
        /// that. <c>FSFTraining</c> stays, because training targets a tame animal, which
        /// lives in the barn.</para>
        /// </summary>
        internal static readonly Dictionary<string, string[]> CompatDefaults =
            new Dictionary<string, string[]>
            {
                { "Hospital",   new[] { "FSFNurse", "FSFSurgeon" } },
                { "Laboratory", new[] { "FSFDrugs" } },
                { "Kitchen",    new[] { "FSFButcher" } },
                {
                    "Workshop",
                    new[]
                    {
                        "FSFStoneCut", "FSFSmelt", "FSFMachining",
                        "FSFFabrication", "FSFRefining", "FSFProduction",
                    }
                },
                { "Barn",       new[] { "FSFTraining", "FSFSlaughter" } },
            };

        internal static Dictionary<RoomRoleDef, List<WorkTypeDef>> resolved;

        /// <summary>
        /// Appends each name the loaded def set has, skipping duplicates so a type named by
        /// both tables lands once. A name that does not resolve is a mod that is not
        /// installed, so the row narrows rather than failing.
        /// </summary>
        internal static void AddResolvable(List<WorkTypeDef> into, string[] names)
        {
            if (names == null)
            {
                return;
            }
            foreach (string workName in names)
            {
                WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workName);
                if (work != null && !into.Contains(work))
                {
                    into.Add(work);
                }
            }
        }

        internal static Dictionary<RoomRoleDef, List<WorkTypeDef>> Resolved
        {
            get
            {
                if (resolved == null)
                {
                    resolved = new Dictionary<RoomRoleDef, List<WorkTypeDef>>();
                    foreach (KeyValuePair<string, string[]> pair in Defaults)
                    {
                        RoomRoleDef role = DefDatabase<RoomRoleDef>.GetNamedSilentFail(pair.Key);
                        if (role == null)
                        {
                            continue;
                        }
                        List<WorkTypeDef> works = new List<WorkTypeDef>();
                        AddResolvable(works, pair.Value);
                        string[] compat;
                        if (CompatDefaults.TryGetValue(pair.Key, out compat))
                        {
                            AddResolvable(works, compat);
                        }
                        if (works.Count > 0)
                        {
                            resolved[role] = works;
                        }
                    }
                }
                return resolved;
            }
        }

        /// <summary>
        /// The work types a stand in a room of this role dresses for by default. Empty for
        /// bedrooms, dining rooms and unroled space, which is not an error. Never null; the
        /// list is shared, so treat it as read-only.
        /// </summary>
        public static List<WorkTypeDef> ForRole(RoomRoleDef role)
        {
            if (role == null)
            {
                return None;
            }
            List<WorkTypeDef> works;
            return Resolved.TryGetValue(role, out works) ? works : None;
        }

        /// <summary>
        /// Role defNames whose rooms dress for recreation by default: vanilla RecRoom and the
        /// pool roles of Gerrymon's Hotspring Expanded, which drop out when that mod is
        /// absent. Biotech's Playroom is left out on purpose, since auto-dressing toddlers
        /// should be a choice, not a default. A pure pool room usually has no role (swimming,
        /// <c>GoSwimming</c>, is terrain-driven, so <c>RoomRoleWorker_RecRoom</c> counts
        /// nothing in it) and takes the manual toggle.
        /// </summary>
        internal static readonly string[] RecreationRoles =
        {
            "RecRoom",
            "GM_PrivatePool",
            "GM_PublicPool",
        };

        internal static HashSet<RoomRoleDef> resolvedRecreation;

        internal static HashSet<RoomRoleDef> ResolvedRecreation
        {
            get
            {
                if (resolvedRecreation == null)
                {
                    resolvedRecreation = new HashSet<RoomRoleDef>();
                    foreach (string roleName in RecreationRoles)
                    {
                        RoomRoleDef role = DefDatabase<RoomRoleDef>.GetNamedSilentFail(roleName);
                        // A stand dresses for one purpose, work or recreation. The toggles
                        // enforce that in custom mode, but automatic mode answers straight
                        // from these tables, so a role may sit in only one of them. Work wins
                        // on a collision, so a row added to both cannot produce a
                        // dual-purpose stand.
                        if (role != null && !Resolved.ContainsKey(role))
                        {
                            resolvedRecreation.Add(role);
                        }
                    }
                }
                return resolvedRecreation;
            }
        }

        /// <summary>Whether a room of this role dresses for recreation by default.</summary>
        public static bool RecreationForRole(RoomRoleDef role)
        {
            return role != null && ResolvedRecreation.Contains(role);
        }

        /// <summary>
        /// Role defNames whose rooms dress for sleep by default. Bedroom only, on purpose; why
        /// not Barracks or the prison roles: docs/DESIGN.md, "The sleep branch".
        /// </summary>
        internal static readonly string[] RestRoles =
        {
            "Bedroom",
        };

        internal static HashSet<RoomRoleDef> resolvedRest;

        internal static HashSet<RoomRoleDef> ResolvedRest
        {
            get
            {
                if (resolvedRest == null)
                {
                    resolvedRest = new HashSet<RoomRoleDef>();
                    foreach (string roleName in RestRoles)
                    {
                        RoomRoleDef role = DefDatabase<RoomRoleDef>.GetNamedSilentFail(roleName);
                        // The same rule across all three tables: a role in two of them would
                        // make an automatic stand dress for two purposes. Work wins, then
                        // recreation, then rest, so a collision resolves the same way every
                        // time.
                        if (role != null && !Resolved.ContainsKey(role)
                            && !ResolvedRecreation.Contains(role))
                        {
                            resolvedRest.Add(role);
                        }
                    }
                }
                return resolvedRest;
            }
        }

        /// <summary>Whether a room of this role dresses for sleep by default.</summary>
        public static bool RestForRole(RoomRoleDef role)
        {
            return role != null && ResolvedRest.Contains(role);
        }
    }
}
