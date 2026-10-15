using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Work types a room earns from WHAT IS STANDING IN IT, for rooms the
    /// engine gives no useful role to. A room's role is scored from its work
    /// tables' <c>workTableRoomRole</c>, which Dubs Rimatomics sets nowhere, so
    /// a reactor hall reads as the generic <c>Room</c>; and a role is
    /// winner-take-all, so a room holding both of its benches arms only one
    /// role's work. Why contents rather than a role or a shipped
    /// <c>RoomRoleDef</c>, and what is deliberately left out (the reactor
    /// console): docs/DESIGN.md, "Mod compatibility".
    /// </summary>
    public static class RoomContentsWork
    {
        /// <summary>
        /// A marker type whose presence in a room arms the named work types.
        /// Resolved by NAME so we take no assembly reference and no dependency:
        /// an absent mod leaves <see cref="type"/> null and the row drops out,
        /// exactly like <see cref="RoomWorkTypes.CompatDefaults"/>.
        /// </summary>
        internal sealed class Marker
        {
            internal readonly string typeName;

            /// <summary>
            /// Match the thing's COMPS against <see cref="typeName"/> rather
            /// than the thing itself. Some mods mark a capability with a comp
            /// instead of a class, and the comp is then the honest hook: it is
            /// what the mod's own code keys on.
            /// </summary>
            internal readonly bool isComp;

            internal readonly string[] workNames;
            internal Type type;
            internal List<WorkTypeDef> works;

            internal Marker(string typeName, bool isComp, string[] workNames)
            {
                this.typeName = typeName;
                this.isComp = isComp;
                this.workNames = workNames;
            }
        }

        /// <summary>
        /// Four markers rather than one, because the questions have different
        /// answers per building and a room can want several. Which building
        /// arms which work type, and the one overreach left in on purpose:
        /// docs/DESIGN.md, "Mod compatibility".
        ///
        /// <para>The last two overlap <c>Patches/ShiftChange_Rimatomics.xml</c>,
        /// which gives the same benches a <c>workTableRoomRole</c>, and that is
        /// deliberate: a room role is winner-take-all, so only reading the
        /// benches from the contents as well arms both in a shared room.</para>
        /// </summary>
        internal static readonly Marker[] Markers =
        {
            new Marker("Rimatomics.IFuelFilter", false, new[] { "NuclearWork" }),
            new Marker("Rimatomics.CompResearchFacility", true, new[] { "Research" }),
            new Marker("Rimatomics.Building_RimatomicsWorkbench", false, new[] { "Smithing" }),
            new Marker("Rimatomics.Building_RimatomicsResearchBench", false,
                       new[] { "Research", "Crafting" }),
        };

        internal static List<Marker> active;

        /// <summary>
        /// Markers whose type AND at least one of whose work types resolved.
        /// Built once. Empty is the ordinary case — nobody has Rimatomics
        /// installed — and it is what makes the per-room scan free for
        /// everyone else.
        /// </summary>
        internal static List<Marker> Active
        {
            get
            {
                if (active == null)
                {
                    active = new List<Marker>();
                    foreach (Marker marker in Markers)
                    {
                        Type resolved = AccessTools.TypeByName(marker.typeName);
                        if (resolved == null)
                        {
                            continue;
                        }
                        List<WorkTypeDef> works = new List<WorkTypeDef>();
                        RoomWorkTypes.AddResolvable(works, marker.workNames);
                        if (works.Count == 0)
                        {
                            continue;
                        }
                        marker.type = resolved;
                        marker.works = works;
                        active.Add(marker);
                    }
                }
                return active;
            }
        }

        /// <summary>Whether any marker resolved, so callers can skip the scan.</summary>
        public static bool Any => Active.Count > 0;

        /// <summary>
        /// Whether this DEF declares a comp of the marker type.
        ///
        /// <para>Read off the def's comp properties rather than a spawned
        /// instance's comp list: the answer is the same, it needs no cast to
        /// <c>ThingWithComps</c>, it does not walk live comps for every thing
        /// in a room, and it can be asserted without a map.</para>
        /// </summary>
        internal static bool HasComp(ThingDef def, Type compType)
        {
            List<CompProperties> comps = def?.comps;
            if (comps == null)
            {
                return false;
            }
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] != null && comps[i].compClass != null
                    && compType.IsAssignableFrom(comps[i].compClass))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Appends the work types this room's contents earn, skipping ones
        /// already present so a type named by both sources lands once.
        ///
        /// <para>CALLERS MUST CACHE. <see cref="Room.ContainedAndAdjacentThings"/>
        /// clears and rebuilds a set and a list on every single call, and the
        /// property that wants this answer is read every frame to draw a gizmo
        /// label. <see cref="CompShiftStand"/> holds the cache.</para>
        /// </summary>
        public static void Collect(Room room, List<WorkTypeDef> into)
        {
            if (room == null || into == null)
            {
                return;
            }
            List<Marker> markers = Active;
            if (markers.Count == 0)
            {
                return;
            }
            List<Thing> things = room.ContainedAndAdjacentThings;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null)
                {
                    continue;
                }
                for (int m = 0; m < markers.Count; m++)
                {
                    Marker marker = markers[m];
                    // IsInstanceOfType, not ==: a marker may be an INTERFACE
                    // and the cores reach it through a shared base class.
                    bool matched = marker.isComp
                        ? HasComp(thing.def, marker.type)
                        : marker.type.IsInstanceOfType(thing);
                    if (!matched)
                    {
                        continue;
                    }
                    for (int w = 0; w < marker.works.Count; w++)
                    {
                        WorkTypeDef work = marker.works[w];
                        if (!into.Contains(work))
                        {
                            into.Add(work);
                        }
                    }
                }
            }
        }
    }
}
