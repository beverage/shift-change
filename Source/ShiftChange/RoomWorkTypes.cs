using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Maps a room's role to the SET of work types a stand in that room
    /// dresses for by default.
    ///
    /// A set, not a single work type, because rooms host families of work and
    /// pretending otherwise was a design flaw (decided 2026-08-08): a
    /// Workshop runs crafting, tailoring, smithing and art; a Laboratory runs
    /// research AND drug synthesis — which arrives as Crafting work
    /// (`DoBillsProduceDrugs` is `workType Crafting`, fixed to the DrugLab,
    /// WorkGivers.xml:1139-1148); a barn's sick animals are tended under
    /// Doctor, not Handling. The defaults are our judgment of "work plausibly
    /// done under this role" — the per-stand dialog narrows or widens them.
    ///
    /// Deliberately excluded from every set: the base-wide work types that
    /// merely PASS THROUGH a room — Hauling, Cleaning, Construction,
    /// Firefighting. A hauler carrying meals into the hospital should not
    /// scrub in.
    ///
    /// Resolved by defName rather than through <see cref="RoomRoleDefOf"/>
    /// because <c>Kitchen</c> has no DefOf field even though the def ships in
    /// Core, and silent-fail lookups mean a role or work type removed by
    /// another mod drops out of the table instead of throwing at startup.
    /// </summary>
    public static class RoomWorkTypes
    {
        internal static readonly List<WorkTypeDef> None = new List<WorkTypeDef>();

        /// <summary>
        /// role defName → work type defNames. VANILLA ONLY: every name here
        /// is required to exist, and the harness asserts it, because a name
        /// that stops resolving empties a role's list and the mod then does
        /// nothing at all — green harness, no log line. Mod-supplied types
        /// belong in <see cref="CompatDefaults"/>, which is allowed to miss.
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
        /// Work types folded into <see cref="Defaults"/> when another mod
        /// supplies them. A SEPARATE table, because these are allowed to be
        /// absent and those are not: missing here is the ordinary case (the
        /// mod is not installed), missing there means a vanilla def was
        /// renamed under us. One table cannot say both, and the harness case
        /// that guards the second meaning is what makes the split load-bearing
        /// rather than tidy.
        ///
        /// <para>Today that is [FSF] Complex Jobs
        /// (<c>FrozenSnowFox.ComplexJobs</c>), which does not so much add work
        /// as MOVE it: it repoints the <c>workType</c> field on vanilla
        /// WorkGiverDefs at its own finer-grained types, leaving the vanilla
        /// type in place but hollowed out. Surgery stops being Doctor work,
        /// butchering stops being Cooking work, taming and training stop being
        /// Handling work. A stand keyed to the vanilla name alone then dresses
        /// for some of its room's work and silently not the rest — which
        /// reads as flakiness rather than as a missing mod patch.</para>
        ///
        /// <para>A row earns its place only where the type inherits work from
        /// <see cref="Defaults"/> AND that work lands in this role's room,
        /// checked against each bench's <c>workTableRoomRole</c> rather than
        /// assumed. Hence the brewery counts as Workshop, not Kitchen, even
        /// though brewing is what leaves Cooking; hence the drug lab is
        /// Laboratory. Deliberately absent: <c>FSFPaint</c> and
        /// <c>FSFSmoothing</c>, which collect the base-wide pass-through work
        /// these sets exclude on purpose, and <c>FSFMechanoids</c>, whose
        /// gestators are Laboratory-roled but whose work is vanilla SMITHING —
        /// no lab stand dressed for it in vanilla either, so covering it here
        /// would be a new feature wearing a compatibility fix's clothes.</para>
        ///
        /// <para><c>FSFTaming</c> and <c>FSFSlaughter</c> exist only when
        /// Complex Jobs' own XML Extensions options are switched on, so they
        /// are absent even with that mod installed and left unconfigured. That
        /// is the normal case for this table, not a defect.</para>
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
                {
                    "Barn",
                    new[] { "FSFTraining", "FSFTaming", "FSFSlaughter" }
                },
            };

        internal static Dictionary<RoomRoleDef, List<WorkTypeDef>> resolved;

        /// <summary>
        /// Appends each name the loaded def set actually has, skipping
        /// duplicates so a type named by both tables lands once. Silent-fail
        /// is the contract: an absent name is a mod that is not installed, and
        /// the row simply narrows.
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
        /// The work types a stand in a room of this role dresses for by
        /// default. Empty is the ordinary case for bedrooms, dining rooms and
        /// unroled space — not an error. Never null; treat as read-only.
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
        /// Role defNames whose rooms dress for RECREATION by default — the
        /// joy-branch parallel of <see cref="Defaults"/>. Vanilla
        /// RecRoom plus the third-party pool roles already sighted in the
        /// wild (Gerrymon's Hotspring Expanded); silent-fail as ever, so an
        /// absent mod just drops its rows. Biotech's Playroom is deliberately
        /// NOT here — auto-dressing toddlers is its own decision, not a
        /// default. Pure pool rooms are typically ROLELESS (GoSwimming is
        /// terrain-driven, so RoomRoleWorker_RecRoom counts nothing in them)
        /// and use the manual toggle instead.
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
                        // DISJOINTNESS IS LOAD-BEARING: automatic-mode
                        // exclusivity (work XOR recreation, decided
                        // 2026-08-16) holds only because no role appears in
                        // BOTH tables — the toggles enforce it for custom
                        // mode, but automatic mode answers straight from
                        // these tables. Work wins on a collision, so a
                        // future row added to both cannot silently
                        // resurrect the dual-purpose stand (verified
                        // impossible today; guarded anyway).
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
        /// Role defNames whose rooms dress for SLEEP by default — the third
        /// trigger's parallel of <see cref="Defaults"/> and
        /// <see cref="RecreationRoles"/>.
        ///
        /// <para>Bedroom only. <c>Barracks</c> is deliberately absent: it
        /// would make a shared pool stand the default for every colonist
        /// sleeping in the room, and a barracks of ten cycling through one
        /// pyjama stand at lights-out is churn rather than charm. Called out
        /// as a deliberate design-time choice rather than an oversight: a
        /// player who wants it ticks the row by hand, which is one click and
        /// states the intent.</para>
        ///
        /// <para>Prison roles are absent for the same reason they are absent
        /// everywhere else here — the interception's faction gate never
        /// reaches a prisoner, so a row would be decoration.</para>
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
                        // Same load-bearing disjointness as the recreation
                        // table, now three-way: automatic mode answers
                        // straight from these tables, so a role appearing in
                        // two of them would resurrect the dual-purpose stand
                        // the exclusivity rule exists to prevent. Work wins,
                        // then recreation, then rest — the order matters only
                        // because a collision must resolve the same way every
                        // time, and today none exists.
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
