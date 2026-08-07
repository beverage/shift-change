using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Maps a room's role to the work type a stand in that room dresses for.
    ///
    /// This is the "no configuration in the common case" half of BL-049: a
    /// stand in a hospital is a doctor's stand without the player saying so.
    /// A stand whose room has a role that is not in this table falls back to
    /// an explicit per-stand override (not built yet — see the spike's TODO).
    ///
    /// Resolved by defName rather than through <see cref="RoomRoleDefOf"/>
    /// because <c>Kitchen</c> has no DefOf field even though the def ships in
    /// Core (<c>Core/Defs/Rooms/RoomRoles.xml</c>), and going through the
    /// database uniformly avoids a mixed style. <see cref="MayRequire"/>-style
    /// silent-fail lookups mean a role or work type removed by another mod
    /// drops out of the table instead of throwing at startup.
    /// </summary>
    public static class RoomWorkTypes
    {
        /// <summary>role defName → work type defName.</summary>
        private static readonly Dictionary<string, string> Defaults =
            new Dictionary<string, string>
            {
                { "Hospital",   "Doctor"   },
                { "Laboratory", "Research" },
                { "Kitchen",    "Cooking"  },
                { "Workshop",   "Crafting" },
                { "Barn",       "Handling" },
            };

        private static Dictionary<RoomRoleDef, WorkTypeDef> resolved;

        private static Dictionary<RoomRoleDef, WorkTypeDef> Resolved
        {
            get
            {
                if (resolved == null)
                {
                    resolved = new Dictionary<RoomRoleDef, WorkTypeDef>();
                    foreach (KeyValuePair<string, string> pair in Defaults)
                    {
                        RoomRoleDef role = DefDatabase<RoomRoleDef>.GetNamedSilentFail(pair.Key);
                        WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail(pair.Value);
                        if (role != null && work != null)
                        {
                            resolved[role] = work;
                        }
                    }
                }
                return resolved;
            }
        }

        /// <summary>
        /// The work type a stand in a room of this role dresses for, or null
        /// when the role carries no default. Null is the ordinary case for
        /// bedrooms, dining rooms and unroled space — not an error.
        /// </summary>
        public static WorkTypeDef ForRole(RoomRoleDef role)
        {
            if (role == null)
            {
                return null;
            }
            WorkTypeDef work;
            return Resolved.TryGetValue(role, out work) ? work : null;
        }
    }
}
