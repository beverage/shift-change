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
    /// pretending otherwise was a design flaw (principal, 2026-08-08): a
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
        private static readonly List<WorkTypeDef> None = new List<WorkTypeDef>();

        /// <summary>role defName → work type defNames.</summary>
        private static readonly Dictionary<string, string[]> Defaults =
            new Dictionary<string, string[]>
            {
                { "Hospital",   new[] { "Doctor" } },
                { "Laboratory", new[] { "Research", "Crafting" } },
                { "Kitchen",    new[] { "Cooking" } },
                { "Workshop",   new[] { "Crafting", "Tailoring", "Smithing", "Art" } },
                { "Barn",       new[] { "Handling", "Doctor" } },
            };

        private static Dictionary<RoomRoleDef, List<WorkTypeDef>> resolved;

        private static Dictionary<RoomRoleDef, List<WorkTypeDef>> Resolved
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
                        foreach (string workName in pair.Value)
                        {
                            WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workName);
                            if (work != null)
                            {
                                works.Add(work);
                            }
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
    }
}
