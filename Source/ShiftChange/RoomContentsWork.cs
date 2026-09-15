using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Work types a room earns from WHAT IS STANDING IN IT, for rooms the
    /// engine gives no useful role to.
    ///
    /// <para><b>Why a second source beside <see cref="RoomWorkTypes"/>.</b>
    /// Automatic mode asks the room's <see cref="RoomRoleDef"/>, and the two
    /// roles that would matter here — Workshop and Laboratory — are scored by
    /// exactly one thing: <c>def.building.workTableRoomRole</c>
    /// (<c>RoomRoleWorker_Workshop.GetScore</c>). It is a declarative opt-in
    /// and a mod has to set it. Dubs Rimatomics sets it nowhere, so a reactor
    /// hall scores zero for every role and comes back as the generic
    /// <c>Room</c>. Verified in game 2026-09-13 against a vanilla control:
    /// two identical 9x9 rooms, a Rimatomics machining table in one and a
    /// vanilla one in the other, gave "nothing; the game reads this room as:
    /// Room" and "crafting, tailoring, smithing, art" respectively.</para>
    ///
    /// <para>The bench case is fixed in XML — <c>Patches/ShiftChange_Rimatomics.xml</c>
    /// hands those two defs the role field they are missing. This class is for
    /// the case XML cannot reach: a reactor hall holds no work table at all, so
    /// there is no <c>workTableRoomRole</c> to set on anything. Nuclear work
    /// happens at the cores and the plutonium processor, and those are plain
    /// buildings.</para>
    ///
    /// <para><b>Why we do not ship a RoomRoleDef for it.</b> A new role would
    /// enter the global <c>MaxBy</c> scoring against every vanilla role and
    /// change what the game CALLS that room for every other mod reading roles.
    /// Deciding what a stand dresses for is our business; renaming somebody
    /// else's architecture is not. Nothing in the UI needs the role either: the
    /// dialog only names it in the empty case, so a contents-armed stand reads
    /// "Automatic (from the room): nuclear loading" with no wording change.</para>
    ///
    /// <para><b>Detecting by TYPE is the vanilla idiom</b>, not a workaround —
    /// Storeroom is <c>thing is Building_Storage</c>, Tomb is
    /// <c>is Building_Sarcophagus</c>, Barn and Bedroom are <c>is Building_Bed</c>
    /// plus def flags. We key on <c>Rimatomics.IFuelFilter</c>, which their own
    /// code uses for "this building holds nuclear fuel" and which exactly three
    /// classes implement: <c>reactorCore</c> (so CoreA, CoreB and CoreC),
    /// <c>Building_PlutoniumProc</c> and <c>Building_storagePool</c>. Nothing
    /// else in the mod — not the turbines, not the weapons, not the research
    /// bench. An interface they maintain beats a def-name list we would have to,
    /// and it picks up whatever fuel-handling machine they add next.</para>
    ///
    /// <para><b>What this deliberately does NOT cover.</b> Operating the reactor
    /// console is not work. <c>UseReactorConsole</c> is a JobDef with no
    /// WorkGiverDef anywhere; <c>ReactorControl.GetFloatMenuOptions</c> issues it
    /// through <c>TryTakeOrderedJob</c>, which sets <c>playerForced</c>
    /// (Pawn_JobTracker.cs:897), and we skip player-forced jobs on purpose in
    /// both the interception and the catch-up loop. No room detection can reach
    /// it, and puncturing the player-forced rule for one mod's job def is not
    /// worth it — "go do X now" should mean X. In practice the colonist who
    /// loaded the fuel is already suited when they are sent to the console.</para>
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
        /// Deliberately two entries rather than one, because the two questions
        /// have different answers per building and a room can want both.
        ///
        /// <para><c>IFuelFilter</c> is "holds nuclear fuel": the reactor cores,
        /// the plutonium processor, the spent fuel pool.
        /// <c>CompResearchFacility</c> is "Rimatomics research happens here",
        /// and it is the comp the mod itself keys on — it adds its own parent
        /// to <c>map.Rimatomics().Facilities</c> on spawn, which is exactly the
        /// list <c>WorkGiver_SuperviseResearch</c> scans. Four defs carry it:
        /// the abstract reactor base (so every core), the plutonium processor,
        /// the research reactor and the weapons bench.</para>
        ///
        /// <para>So a reactor hall and a processor room match both and arm both
        /// work types; a spent fuel pool arms nuclear work only; a research
        /// reactor or weapons bench arms research only. No room ends up armed
        /// for work that cannot happen in it, which a single combined marker
        /// could not manage.</para>
        ///
        /// <para>Research matters here for the same reason the suits do: the
        /// steps run at the research reactor and the plutonium processor are
        /// the ones whose <c>FacilityFailures</c> include
        /// <c>Failure_RadiationLeak</c>, which sets the facility radiating at
        /// strength 2 out to 8 cells for up to 2000 ticks, with the researcher
        /// standing at it. The weapons bench steps fail electrically instead,
        /// so a stand there is the player's call rather than a hazard.</para>
        /// </summary>
        internal static readonly Marker[] Markers =
        {
            new Marker("Rimatomics.IFuelFilter", false, new[] { "NuclearWork" }),
            new Marker("Rimatomics.CompResearchFacility", true, new[] { "Research" }),
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
        /// Appends the work types this room's contents earn, skipping ones
        /// already present so a type named by both sources lands once.
        ///
        /// <para>CALLERS MUST CACHE. <see cref="Room.ContainedAndAdjacentThings"/>
        /// clears and rebuilds a set and a list on every single call, and the
        /// property that wants this answer is read every frame to draw a gizmo
        /// label. <see cref="CompShiftStand"/> holds the cache.</para>
        /// </summary>
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
