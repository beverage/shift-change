// HARNESS only: dev tooling, compiled out of a plain Release build (the
// configuration table is in ShiftChange.csproj). The guard is whole-file,
// always, and check-invariants.py enforces it; why: docs/DESIGN.md,
// "Development tooling".
#if HARNESS
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Harness cases on a vacuum map. The second map
    /// (<see cref="HarnessFixtures.SecondMap"/>) is Odyssey's <c>SpacePocket</c>,
    /// whose biome is <c>Orbit</c>, so the game treats it as space: a room on it
    /// holds a vacuum value, and open floor reads 1.
    ///
    /// <para>The clock stands still while the harness runs, and the vacuum
    /// simulation exchanges only on map ticks, so nothing moves a value a case
    /// sets. Each case sets every room it reads, the open ones too: a doorway
    /// averages its neighbours' stored values (<c>Room.UnsanitizedVacuum</c>),
    /// and an open room's stored value is brought to 1 only when something reads
    /// or exchanges it.</para>
    /// </summary>
    internal static class HarnessVacuum
    {
        /// <summary>
        /// A walled, roofed room on the second map with a door onto a strip of
        /// open floor, the room set to air and the strip to vacuum.
        /// </summary>
        internal struct VacuumScene
        {
            internal Room Inside;
            internal Room Outside;
            internal IntVec3 InsideCell;
            internal IntVec3 OutsideCell;
            internal IntVec3 DoorCell;
        }

        /// <summary>
        /// Build a <see cref="VacuumScene"/> at the centre of <paramref name="away"/>.
        /// The space map's own terrain is impassable, so the strip is laid as
        /// floor: walkable, unroofed, and so open to space.
        /// </summary>
        internal static VacuumScene StageVacuumScene(Map away)
        {
            VacuumScene scene = new VacuumScene();
            TerrainDef floor = DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor");
            if (floor == null)
            {
                return scene;
            }
            CellRect room = CellRect.CenteredOn(away.Center, 3);
            EnclosePad(away, room);

            scene.DoorCell = new IntVec3(room.maxX, 0, room.CenterCell.z);
            scene.DoorCell.GetEdifice(away)?.Destroy();
            GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Door, ThingDefOf.WoodLog), scene.DoorCell, away);
            for (int x = room.maxX + 1; x <= room.maxX + 3; x++)
            {
                away.terrainGrid.SetTerrain(new IntVec3(x, 0, scene.DoorCell.z), floor);
            }

            scene.InsideCell = room.CenterCell;
            scene.OutsideCell = new IntVec3(room.maxX + 2, 0, scene.DoorCell.z);
            scene.Inside = scene.InsideCell.GetRoom(away);
            scene.Outside = scene.OutsideCell.GetRoom(away);
            if (scene.Inside != null)
            {
                scene.Inside.Vacuum = 0f;
            }
            if (scene.Outside != null)
            {
                scene.Outside.Vacuum = 1f;
            }
            return scene;
        }

        /// <summary>
        /// The ground every vacuum case stands on, checked before any of them
        /// relies on it: the second map is a vacuum map; the staged room is a
        /// room of its own and holds air; the strip and the doorway read at or
        /// over the harm line; and a region walk that may only step into air
        /// stays in the room, where an unrestricted walk from the same start
        /// reaches the strip (the control, so the negative means something).
        /// </summary>
        internal static bool VacuumMapStages(Map home)
        {
            Map away = SecondMap(home);
            if (away == null)
            {
                return Expect(false, "a second map was made");
            }
            try
            {
                VacuumScene scene = StageVacuumScene(away);
                float harm = VacuumUtility.MinVacuumForDamage;
                bool ok = Expect(away.Biome.inVacuum,
                                 "the second map's biome (" + away.Biome.defName + ") is in vacuum")
                        & Expect(scene.Inside != null && !scene.Inside.TouchesMapEdge
                                 && !scene.Inside.ExposedToSpace,
                                 "the walled, roofed room is a room of its own, closed to space")
                        & Expect(scene.Outside != null && scene.Outside.ExposedToSpace,
                                 "the open strip beyond its door is open to space")
                        & Expect(scene.InsideCell.GetVacuum(away) < harm, "the room reads as air")
                        & Expect(scene.OutsideCell.GetVacuum(away) >= harm, "the strip reads as vacuum")
                        & Expect(scene.DoorCell.GetRoom(away)?.Vacuum >= harm,
                                 "and the doorway between them reads at the harm line");

                Region start = scene.InsideCell.GetRegion(away);
                Region strip = scene.OutsideCell.GetRegion(away);
                HashSet<Region> anywhere = Walk(start, to => true);
                HashSet<Region> airOnly = Walk(start, to => to.Room != null && to.Room.Vacuum < harm);
                ok &= Expect(strip != null && anywhere.Contains(strip),
                             "a walk from the room that may step anywhere reaches the strip (the control)")
                    & Expect(start != null && airOnly.Contains(start) && !airOnly.Contains(strip),
                             "and one that may only step into air stays in the room");
                return ok;
            }
            finally
            {
                RemoveSecondMap(away);
            }
        }

        /// <summary>Every region a walk from <paramref name="start"/> reaches.</summary>
        internal static HashSet<Region> Walk(Region start, Predicate<Region> mayEnter)
        {
            HashSet<Region> reached = new HashSet<Region>();
            if (start == null)
            {
                return reached;
            }
            RegionTraverser.BreadthFirstTraverse(start, (from, to) => mayEnter(to),
                region =>
                {
                    reached.Add(region);
                    return false;
                }, 9999);
            return reached;
        }
    }
}
#endif
