using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Work types a room earns from what stands in it, for rooms the game's role scoring
    /// serves badly. A room's role is scored from its work tables' <c>workTableRoomRole</c>,
    /// which Dubs Rimatomics sets nowhere, so a reactor hall reads as the generic
    /// <c>Room</c>. A role is also winner-take-all, so a room holding one workshop bench and
    /// one laboratory bench arms only the winning role's work. Why contents rather than a
    /// role or a shipped <c>RoomRoleDef</c>, and what is left out (the reactor console):
    /// docs/DESIGN.md, "Mod compatibility".
    /// </summary>
    public static class RoomContentsWork
    {
        /// <summary>
        /// A marker type whose presence in a room arms the named work types. Resolved by
        /// name, so the mod takes no assembly reference or dependency: an absent mod leaves
        /// <see cref="type"/> null and the row drops out, as with
        /// <see cref="RoomWorkTypes.CompatDefaults"/>.
        /// </summary>
        internal sealed class Marker
        {
            internal readonly string typeName;

            /// <summary>
            /// Match <see cref="typeName"/> against the comp classes the thing's def declares,
            /// rather than against the thing itself. Some mods mark a capability with a comp
            /// instead of a class, and the comp is what their own code keys on.
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
        /// Four markers rather than one, because the answer differs per building and a room
        /// can want several. Which building arms which work type, and the one overreach left
        /// in on purpose: docs/DESIGN.md, "Mod compatibility".
        ///
        /// <para>The last two markers are benches that <c>Patches/ShiftChange_Rimatomics.xml</c>
        /// also gives a <c>workTableRoomRole</c>, and both are needed: a role is
        /// winner-take-all, so only reading the benches from the contents arms both in a
        /// shared room.</para>
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
        /// Markers whose type and at least one of whose work types resolved, built once.
        /// Empty when no marker mod is installed, which is what makes the per-room scan free.
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
        /// Whether the def declares a comp of the given type, read from the def's comp
        /// properties so it needs no spawned instance.
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
        /// Appends the work types this room's contents earn, skipping ones already present.
        ///
        /// <para>Callers must cache. <see cref="Room.ContainedAndAdjacentThings"/> clears and
        /// rebuilds a set and a list on every call, and the property that wants this answer
        /// is read every frame to draw a gizmo label. <see cref="CompShiftStand"/> holds the
        /// cache.</para>
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
                    // IsInstanceOfType rather than a type comparison: a marker may be an
                    // interface, and the reactor cores reach it through a shared base class.
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
