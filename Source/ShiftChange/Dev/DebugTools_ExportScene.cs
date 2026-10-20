// SCENES only (see the config table in ShiftChange.csproj). Pure inspection: this file reads the
// map and writes a text file, and changes nothing in the game. It is dev-only because its output
// is source code for a fixture.
#if SCENES
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Turns a room built by hand in dev mode into the C# that rebuilds it. Click inside an
    /// enclosed room and the terrain, things and pawn notes go to scene-export.cs in the save
    /// data folder, overwriting any earlier export. It is a file because the log truncates and
    /// a scene runs to hundreds of lines.
    ///
    /// <para>Coordinates are relative to the exported rectangle's south-west corner, so a stage
    /// builder can place the scene from any <c>origin</c>. Thing lines follow the argument order
    /// of <c>DebugTools_RecRoomStage.Spawn</c>, with a trailing Color when the thing has one;
    /// terrain comes out as a cell list per def, not as calls.</para>
    ///
    /// <para>It runs in the game rather than reading the save because a save stores terrain as
    /// a deflated array of ushort short hashes (<c>topGridDeflate</c>), and a short hash is
    /// derived from the whole loaded def set, so only the running game can turn it back into a
    /// def. Things are plain XML in the save.</para>
    /// </summary>
    internal static class DebugTools_ExportScene
    {
        /// <summary>
        /// Things that are scenery rather than set, and would bury the real furniture. Pawns
        /// are exported as notes instead (<see cref="Pawns"/>).
        /// </summary>
        internal static bool Ignore(Thing thing)
        {
            return thing is Plant
                || thing is Filth
                || thing is Mote
                || thing.def == ThingDefOf.Fire
                || thing.def.category == ThingCategory.Pawn;
        }

        internal static void ExportScene()
        {
            Map map = Find.CurrentMap;
            IntVec3 clicked = UI.MouseCell();
            if (map == null || !clicked.InBounds(map))
            {
                Messages.Message("Click inside the map.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Room room = clicked.GetRoom(map);
            if (room == null || room.TouchesMapEdge)
            {
                Messages.Message("Click inside an ENCLOSED room — an outdoor or unroofed area has "
                    + "no bounds to export.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            // Room cells stop at the walls, half the scene: grow the bounding box by one.
            List<IntVec3> cells = room.Cells.ToList();
            if (cells.Count == 0)
            {
                Messages.Message("That room has no cells.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            int minX = cells.Min(c => c.x) - 1;
            int maxX = cells.Max(c => c.x) + 1;
            int minZ = cells.Min(c => c.z) - 1;
            int maxZ = cells.Max(c => c.z) + 1;
            CellRect rect = CellRect.FromLimits(minX, minZ, maxX, maxZ).ClipInsideMap(map);

            StringBuilder code = new StringBuilder();
            code.Append("// Exported from ").Append(map.Parent?.Label ?? "map")
                .Append(" — ").Append(rect.Width).Append("x").Append(rect.Height)
                .AppendLine(" cells.");
            code.AppendLine("// Coordinates are RELATIVE to the room's south-west corner, so the");
            code.AppendLine("// builder can place it anywhere: pass the clicked cell as `origin`.");
            code.AppendLine();

            Terrain(map, rect, code);
            Things(map, rect, code);
            Pawns(map, rect, code);

            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "scene-export.cs");
            try
            {
                File.WriteAllText(path, code.ToString());
            }
            catch (IOException e)
            {
                Log.Error("[ShiftChange] could not write the scene export: " + e.Message);
                Messages.Message("Could not write the export — see the dev log.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Log.Message("[ShiftChange] scene exported to " + path);
            Messages.Message("Scene exported to " + path, MessageTypeDefOf.TaskCompletion,
                historical: false);
        }

        /// <summary>
        /// Terrain, grouped by def and emitted as explicit cell lists rather than runs: a list
        /// survives reordering and is obvious to hand-edit, which is the point of exporting.
        /// </summary>
        internal static void Terrain(Map map, CellRect rect, StringBuilder code)
        {
            Dictionary<TerrainDef, List<IntVec3>> byDef = new Dictionary<TerrainDef, List<IntVec3>>();
            foreach (IntVec3 cell in rect)
            {
                TerrainDef def = map.terrainGrid.TerrainAt(cell);
                if (def == null || def.natural)
                {
                    continue;
                }
                if (!byDef.TryGetValue(def, out List<IntVec3> list))
                {
                    byDef[def] = list = new List<IntVec3>();
                }
                list.Add(cell - rect.Min);
            }

            code.AppendLine("// ---- terrain ----------------------------------------------------");
            foreach (KeyValuePair<TerrainDef, List<IntVec3>> pair in byDef.OrderByDescending(p => p.Value.Count))
            {
                code.Append("Floor(map, origin, \"").Append(pair.Key.defName).Append("\", ")
                    .Append(pair.Value.Count).AppendLine(" cells:");
                foreach (IntVec3 c in pair.Value)
                {
                    code.Append("    (").Append(c.x).Append(",").Append(c.z).Append(")");
                }
                code.AppendLine();
                code.AppendLine();
            }
        }

        /// <summary>
        /// Every building and item, with what reproducing a look needs: rotation, stuff and
        /// colour. Colour is written for any thing with a <c>CompColorable</c> and no stuff (the
        /// def's own colour when none was set) and never for a stuff-made thing, so a dye on a
        /// stuff-made thing, which draws over its stuff's colour, is lost. A multi-cell thing is
        /// listed under every cell it covers, so each is emitted once.
        /// </summary>
        internal static void Things(Map map, CellRect rect, StringBuilder code)
        {
            code.AppendLine("// ---- things -----------------------------------------------------");
            HashSet<Thing> seen = new HashSet<Thing>();
            foreach (IntVec3 cell in rect)
            {
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (Ignore(thing) || !seen.Add(thing))
                    {
                        continue;
                    }
                    IntVec3 at = thing.Position - rect.Min;
                    code.Append("Spawn(map, origin, \"").Append(thing.def.defName).Append("\", ");
                    code.Append(thing.Stuff != null ? "\"" + thing.Stuff.defName + "\"" : "null");
                    code.Append(", ").Append(at.x).Append(", ").Append(at.z);
                    code.Append(", Rot4.").Append(RotName(thing.Rotation));

                    Color? colour = thing.TryGetComp<CompColorable>()?.Color;
                    if (colour.HasValue && thing.Stuff == null)
                    {
                        code.Append(", new Color(")
                            .Append(colour.Value.r.ToString("0.###")).Append("f, ")
                            .Append(colour.Value.g.ToString("0.###")).Append("f, ")
                            .Append(colour.Value.b.ToString("0.###")).Append("f)");
                    }
                    code.AppendLine(");");
                }
            }
            code.AppendLine();
        }

        /// <summary>
        /// Pawns are listed as a comment, not as code: reproducing a colonist means their kit,
        /// needs, work priorities and stand assignment, which the stage builders already do
        /// properly, and a copied generated pawn would differ anyway.
        /// </summary>
        internal static void Pawns(Map map, CellRect rect, StringBuilder code)
        {
            code.AppendLine("// ---- pawns (for reference; build these with Patron()) ------------");
            foreach (IntVec3 cell in rect)
            {
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (!(thing is Pawn pawn) || !pawn.RaceProps.Humanlike)
                    {
                        continue;
                    }
                    IntVec3 at = pawn.Position - rect.Min;
                    code.Append("// ").Append(pawn.LabelShort).Append(" (").Append(pawn.gender)
                        .Append(") at (").Append(at.x).Append(",").Append(at.z).Append(") wearing: ");
                    List<Apparel> worn = pawn.apparel?.WornApparel;
                    if (worn == null || worn.Count == 0)
                    {
                        code.AppendLine("nothing");
                        continue;
                    }
                    code.AppendLine(string.Join(", ", worn.Select(a =>
                        a.def.defName + ColourSuffix(a))));
                }
            }
        }

        internal static string ColourSuffix(Apparel garment)
        {
            Color? colour = garment.TryGetComp<CompColorable>()?.Color;
            return colour.HasValue
                ? " #" + ColorUtility.ToHtmlStringRGB(colour.Value)
                : "";
        }

        internal static string RotName(Rot4 rot)
        {
            if (rot == Rot4.North) { return "North"; }
            if (rot == Rot4.East) { return "East"; }
            if (rot == Rot4.South) { return "South"; }
            return "West";
        }
    }
}
#endif
