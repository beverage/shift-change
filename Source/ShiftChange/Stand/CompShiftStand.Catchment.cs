using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// What a stand's catchment is: its room, every cell within its radius, or
    /// the cells painted in an allowed area. Scribed by name, so a kind added
    /// later loads in older builds' saves. docs/DESIGN.md, "Catchments".
    /// </summary>
    internal enum CatchmentKind : byte
    {
        Room,
        Radius,
        Area,
    }

    /// <summary>
    /// Why a stand set to serve something serves nothing: its allowed area is
    /// gone, it stands outside that area, or it serves its room while standing
    /// in the open, where the room is all the open ground on the map.
    /// <see cref="Alert_ShiftStandServesNothing"/> lists all three.
    /// </summary>
    internal enum CatchmentFault : byte
    {
        None,
        AreaGone,
        OutsideArea,
        InTheOpen,
    }

    public partial class CompShiftStand
    {
        /// <summary>
        /// Where this stand serves: its room, which is the default and how
        /// every older save loads, within <see cref="serveRadius"/> of it, or
        /// in <see cref="serveArea"/>. Only a stand with something ticked
        /// leaves room mode, and leaving
        /// shift use brings it back (<see cref="SetServes"/>). Written through
        /// <see cref="ApplyServes"/>, which keeps <see cref="CatchmentStands"/>
        /// in step. docs/DESIGN.md, "Catchments".
        /// </summary>
        internal CatchmentKind serves = CatchmentKind.Room;

        /// <summary>
        /// The radius, in cells, of a radius catchment. Stored raw and read only
        /// in radius mode, like <see cref="depositOnly"/>, so switching modes and
        /// back keeps the number.
        /// </summary>
        internal int serveRadius = DefaultServeRadius;

        internal const int DefaultServeRadius = 12;
        internal const int MinServeRadius = 3;

        /// <summary>
        /// The largest radius the ring can outline exactly. Past the radial
        /// pattern's last cell, <c>GenDraw.DrawRadiusRing</c> logs an error and
        /// draws nothing (<c>GenDraw.cs:455-462</c>), so the slider stops short
        /// of it and the ring always shows what the stand serves.
        /// </summary>
        internal static int MaxServeRadius => Mathf.CeilToInt(GenRadial.MaxRadialPatternRadius) - 1;

        /// <summary>
        /// The allowed area an area catchment serves. Kept while the stand
        /// serves something else, like <see cref="serveRadius"/>, and read only
        /// through <see cref="LiveServeArea"/>: deleting an area tells pawns and
        /// nothing else, so a deleted one stays referenced here.
        /// docs/DESIGN.md, "Catchments".
        /// </summary>
        internal Area serveArea;

        /// <summary>
        /// The bound area's name when it was last seen alive. It names an area
        /// that has since gone, and a gravship landing finds the area again by
        /// it. Nothing else looks an area up by name: default names repeat
        /// ("Area 1"), so a later area under the same name is another area.
        /// </summary>
        internal string serveAreaLabel;

        /// <summary>
        /// Every spawned stand serving a catchment other than its room, so the
        /// stand search finds them without walking the map. Kept by spawn,
        /// despawn and <see cref="ApplyServes"/>, and cleared with the session.
        /// Removing a map despawns no building, so a stand left on an
        /// abandoned map stays listed, and through its bound area would keep
        /// that whole map in memory; the walks prune it first
        /// (<see cref="PruneCatchmentStands"/>).
        /// </summary>
        internal static readonly HashSet<CompShiftStand> CatchmentStands = new HashSet<CompShiftStand>();

        internal static readonly System.Predicate<CompShiftStand> NotSpawned =
            comp => comp.parent == null || !comp.parent.Spawned;

        /// <summary>
        /// Drops every <see cref="CatchmentStands"/> entry whose stand is no
        /// longer spawned. Only a removed map leaves one: a despawn removes
        /// its own entry.
        /// </summary>
        internal static void PruneCatchmentStands()
        {
            CatchmentStands.RemoveWhere(NotSpawned);
        }

        /// <summary>
        /// Whether a job at <paramref name="cell"/> is inside this stand's
        /// catchment, the cells it dresses for and keeps a uniform on in: the
        /// stand's own room, every cell within its radius, or the cells painted
        /// in its allowed area. Both directions ask this, so they cannot
        /// disagree about where the stand's work is.
        ///
        /// <para>An area covers nothing while the stand stands outside it, so
        /// an area stand on unpainted ground serves nothing, and nothing once
        /// the area is gone. A stand serving its room while
        /// standing in the open covers nothing either: that room is all the
        /// open ground on the map.</para>
        ///
        /// <para>False for any map but the stand's own, before anything else
        /// is read: a borrower who left with a caravan keeps the ledger, and
        /// their job boundaries on another map must not be measured against
        /// this one.</para>
        ///
        /// <para>A radius is INCLUSIVE, the cells <c>DrawRadiusRing</c>
        /// outlines. The bill radius compares strictly, and copied here it
        /// would leave the ring's outermost cells drawn but not served
        /// (docs/DESIGN.md, "Catchments").</para>
        /// </summary>
        internal bool Covers(IntVec3 cell, Map map)
        {
            if (map == null || !parent.Spawned || parent.Map != map
                || !cell.IsValid || !cell.InBounds(map))
            {
                return false;
            }
            if (serves == CatchmentKind.Radius)
            {
                return cell.DistanceToSquared(parent.Position) <= serveRadius * serveRadius;
            }
            if (serves == CatchmentKind.Area)
            {
                Area area = LiveServeArea();
                return area != null && area[parent.Position] && area[cell];
            }
            Room room = parent.GetRoom();
            return room != null && !room.TouchesMapEdge && cell.GetRoom(map) == room;
        }

        /// <summary>
        /// Whether a job at <paramref name="place"/> is inside the catchment. A
        /// room stand asks about the cell with a room, as it always has. A
        /// radius or an area asks about the cell the job names, then that room
        /// cell: a painted rock face is the quarry, but a mining job's room cell
        /// is the floor beside the rock, which the player may not have painted
        /// (docs/DESIGN.md, "Catchments").
        /// </summary>
        internal bool Covers(JobPlace place, Map map)
        {
            if (serves == CatchmentKind.Room)
            {
                return Covers(place.RoomCell, map);
            }
            return Covers(place.Cell, map)
                || (place.RoomCell != place.Cell && Covers(place.RoomCell, map));
        }

        /// <summary>
        /// The bound area while it still exists on the map the stand is held
        /// on, else null, refreshing <see cref="serveAreaLabel"/> on the way so
        /// a renamed area is named right once it is gone. Held, not spawned: a
        /// stand boxed for a move has no <c>Map</c>, and testing that would let
        /// an autosave drop its binding.
        /// </summary>
        internal Area LiveServeArea()
        {
            if (!IsLiveHere(serveArea))
            {
                return null;
            }
            serveAreaLabel = serveArea.Label;
            return serveArea;
        }

        /// <summary>
        /// Whether a save writes <paramref name="area"/> in full, so a
        /// reference to it resolves on load: it is still in its own map's area
        /// list, and that map is still in the game. Whatever map the stand is
        /// on, or none.
        /// </summary>
        internal static bool AreaSavedInFull(Area area)
        {
            Map map = area?.Map;
            return map != null && Find.Maps.Contains(map) && map.areaManager.AllAreas.Contains(area);
        }

        /// <summary>
        /// Whether a stand here can serve <paramref name="area"/>: an allowed
        /// area, or Home, still in the area list of the map the stand is held
        /// on. A deleted area keeps its map, so the list is the test.
        /// </summary>
        internal bool IsLiveHere(Area area)
        {
            Map map = parent.MapHeld;
            return area != null && map != null && area.AssignableAsAllowed()
                && map.areaManager.AllAreas.Contains(area);
        }

        /// <summary>
        /// Why this spawned stand, set to serve something, serves nothing, or
        /// <see cref="CatchmentFault.None"/> when it serves. A stand with
        /// nothing ticked has no fault: it was never set to serve anything.
        /// </summary>
        internal CatchmentFault ServeFault
        {
            get
            {
                if (!parent.Spawned || !ServesSomething)
                {
                    return CatchmentFault.None;
                }
                if (serves == CatchmentKind.Room)
                {
                    return StandsInTheOpen ? CatchmentFault.InTheOpen : CatchmentFault.None;
                }
                if (serves != CatchmentKind.Area)
                {
                    return CatchmentFault.None;
                }
                Area area = LiveServeArea();
                if (area == null)
                {
                    return CatchmentFault.AreaGone;
                }
                return area[parent.Position] ? CatchmentFault.None : CatchmentFault.OutsideArea;
            }
        }

        /// <summary>
        /// The stand's ticks are exactly what its room would give an automatic
        /// stand: the same work types, or the trigger the room suggests
        /// (<see cref="TriggerIsRoomsOwn"/>). That is what
        /// <see cref="TickAutomaticSet"/> writes when an automatic stand leaves
        /// room mode, so <see cref="SetServes"/> reads it as Automatic coming
        /// back. Live from the room, like <see cref="TriggerIsRoomsOwn"/>:
        /// nothing is saved for it, ticks the player changed while away no
        /// longer match, and a room whose role changed meanwhile keeps the
        /// ticks.
        /// </summary>
        internal bool TicksAreRoomsOwn
        {
            get
            {
                if (excluded || !parent.Spawned || serves != CatchmentKind.Room)
                {
                    return false;
                }
                if (IsTriggerOverridden)
                {
                    return TriggerIsRoomsOwn;
                }
                if (workTypeOverrides == null || workTypeOverrides.Count == 0)
                {
                    return false;
                }
                List<WorkTypeDef> automatic = AutomaticWorkTypes;
                if (automatic.Count != workTypeOverrides.Count)
                {
                    return false;
                }
                for (int i = 0; i < automatic.Count; i++)
                {
                    if (!workTypeOverrides.Contains(automatic[i]))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Switch where the stand serves. Leaving room mode needs something
        /// ticked: an automatic stand's set becomes ticks first, so nothing
        /// visibly changes, and a stand with nothing to tick stays where it is
        /// (docs/DESIGN.md, "Catchments"). Coming back, ticks that are exactly
        /// the room's own reading (<see cref="TicksAreRoomsOwn"/>), which is
        /// what leaving ticked, go back to Automatic, so the stand follows its
        /// room again. Area mode also needs a live area, the one
        /// <see cref="SetServeArea"/> bound. A stand whose area goes while it
        /// serves it stays in area mode, serving nothing, but is never put
        /// there that way.
        /// </summary>
        internal void SetServes(CatchmentKind kind)
        {
            if (kind == CatchmentKind.Room)
            {
                ApplyServes(kind);
                // Asked once back in the room: only a stand serving its room
                // has a room reading to compare against.
                if (TicksAreRoomsOwn)
                {
                    SetAutomatic();
                }
                return;
            }
            if (kind == CatchmentKind.Area && LiveServeArea() == null)
            {
                return;
            }
            if (kind != CatchmentKind.Room && !HasCustomSet)
            {
                if (excluded)
                {
                    return;
                }
                TickAutomaticSet();
                if (!HasCustomSet)
                {
                    return;
                }
            }
            ApplyServes(kind);
        }

        /// <summary>
        /// Bind the stand to <paramref name="area"/> and serve it. Refused, with
        /// the old binding kept, for an area a stand here cannot serve and for
        /// a stand with nothing ticked. <see cref="ServesSomething"/> answers
        /// as <see cref="SetServes"/>' own test does, and is asked first so that
        /// a refusal changes nothing.
        /// </summary>
        internal void SetServeArea(Area area)
        {
            if (!ServesSomething || !IsLiveHere(area))
            {
                return;
            }
            serveArea = area;
            serveAreaLabel = area.Label;
            SetServes(CatchmentKind.Area);
        }

        /// <summary>
        /// The Serves menu's New allowed area: a new allowed area on the
        /// stand's map, bound and returned, or null with nothing ticked or with
        /// the map at vanilla's cap of ten. It starts as a copy of the stand's
        /// room, or just the stand's own cell in the open: an empty area would
        /// leave the stand outside it, serving nothing, where a copy of the
        /// room keeps it serving what it served until the player paints more.
        /// </summary>
        internal Area_Allowed MakeServeArea()
        {
            Map map = parent.Spawned ? parent.Map : null;
            Area_Allowed area;
            if (map == null || !ServesSomething)
            {
                return null;
            }
            string label = NewAreaLabel(map);
            if (!map.areaManager.TryMakeNewAllowed(out area))
            {
                return null;
            }
            area.SetLabel(label);
            Room room = parent.GetRoom();
            if (room != null && !room.TouchesMapEdge)
            {
                foreach (IntVec3 cell in room.Cells)
                {
                    area[cell] = true;
                }
            }
            area[parent.Position] = true;
            SetServeArea(area);
            return area;
        }

        /// <summary>
        /// A new area's name: the first thing the stand serves, "Doctoring
        /// area", numbered from 2 while the map already has one by that name.
        /// The served work rather than the stand's label, which would make every
        /// area "Outfit stand area" and a number. Asked before the stand leaves
        /// room mode, so an automatic stand is named for what its room gives it,
        /// which is what it goes on to tick.
        /// </summary>
        internal string NewAreaLabel(Map map)
        {
            List<string> served = ServedLabels();
            string purpose = served.Count > 0 ? served[0].CapitalizeFirst() : parent.LabelCap;
            string label = "ShiftChange.NewAreaLabel".Translate(purpose).RawText;
            // Bounded: a translation of the numbered string without its {1}
            // gives the same name every pass, and the map holds only so many
            // areas, so past that many tries the number is appended here.
            int tries = map.areaManager.AllAreas.Count + 2;
            for (int n = 2; map.areaManager.GetLabeled(label) != null; n++)
            {
                label = n <= tries
                    ? "ShiftChange.NewAreaLabelNumbered".Translate(purpose, n).RawText
                    : "ShiftChange.NewAreaLabel".Translate(purpose).RawText + " " + n;
            }
            return label;
        }

        /// <summary>
        /// Turn an automatic stand's reading into explicit ticks: its room's
        /// work types, or else its recreation or sleep trigger. Asked while the
        /// stand still serves its room, so the reading is the room's.
        /// </summary>
        internal void TickAutomaticSet()
        {
            List<WorkTypeDef> automatic = parent.Spawned ? AutomaticWorkTypes : NoWork;
            if (automatic.Count > 0)
            {
                workTypeOverrides = workTypeOverrides ?? new List<WorkTypeDef>();
                workTypeOverrides.Clear();
                workTypeOverrides.AddRange(automatic);
            }
            else if (HandlesRecreation())
            {
                recreationOverride = true;
            }
            else if (HandlesRest())
            {
                restOverride = true;
            }
        }

        /// <summary>Sets the mode and keeps <see cref="CatchmentStands"/> in step.</summary>
        internal void ApplyServes(CatchmentKind kind)
        {
            serves = kind;
            if (kind != CatchmentKind.Room && parent.Spawned)
            {
                CatchmentStands.Add(this);
            }
            else
            {
                CatchmentStands.Remove(this);
            }
        }

        internal void SetServeRadius(int radius)
        {
            serveRadius = Mathf.Clamp(radius, MinServeRadius, MaxServeRadius);
        }

        /// <summary>
        /// The stand dresses for something: a work type, recreation or sleep.
        /// The one inert test for the inspect pane, the owner button and the
        /// Serves row, which stays greyed until it holds.
        /// </summary>
        internal bool ServesSomething => WorkTypes.Count > 0 || HandlesRecreation() || HandlesRest();

        /// <summary>
        /// A keyed string's variant for this stand's catchment: the key itself
        /// for a room, the key plus "Radius" for a radius, plus "Area" for an
        /// allowed area. For the strings that say where a stand serves
        /// (docs/DESIGN.md, "Catchments").
        /// </summary>
        internal string ByCatchment(string key)
        {
            switch (serves)
            {
                case CatchmentKind.Radius:
                    return key + "Radius";
                case CatchmentKind.Area:
                    return key + "Area";
                default:
                    return key;
            }
        }

        /// <summary>
        /// What the settings window's Serves row says the stand covers. A room
        /// in the open is the one map-wide outdoor room, so a stand serving it
        /// serves nothing, whatever it is for, and the row says so rather than
        /// "this room". An area goes by its name, with the reason it serves
        /// nothing when it does: it is gone, or it leaves the stand out
        /// (docs/DESIGN.md, "Catchments").
        /// </summary>
        internal string ServesLabel()
        {
            switch (serves)
            {
                case CatchmentKind.Radius:
                    return "ShiftChange.ServesRadius".Translate(serveRadius);
                case CatchmentKind.Area:
                {
                    Area area = LiveServeArea();
                    if (area == null)
                    {
                        return "ShiftChange.ServesAreaGone".Translate(serveAreaLabel);
                    }
                    return parent.Spawned && !area[parent.Position]
                        ? (string)"ShiftChange.ServesAreaStandOutside".Translate(area.Label)
                        : area.Label;
                }
                default:
                    return StandsInTheOpen
                        ? "ShiftChange.ServesNothingOutdoors".Translate()
                        : "ShiftChange.ServesRoom".Translate();
            }
        }

        /// <summary>The stand's room is the map-wide outdoor one.</summary>
        internal bool StandsInTheOpen
        {
            get
            {
                Room room = parent.Spawned ? parent.GetRoom() : null;
                return room != null && room.TouchesMapEdge;
            }
        }

        /// <summary>
        /// Draws what a radius or area stand serves: its ring, or its area's
        /// painted cells. On selection, and while its settings window is open,
        /// as a bill's ingredient radius is drawn. Nothing for a room stand,
        /// whose walls are its outline. The area is left undrawn while a float
        /// menu is open, as vanilla's paint tool does with its own
        /// (<c>Designator_AreaAllowed.SelectedUpdate</c>), so an area hovered in
        /// the Serves menu is the only one drawn.
        /// </summary>
        internal void DrawCatchment()
        {
            if (!parent.Spawned || parent.Map != Find.CurrentMap)
            {
                return;
            }
            if (serves == CatchmentKind.Radius)
            {
                GenDraw.DrawRadiusRing(parent.Position, serveRadius);
            }
            else if (serves == CatchmentKind.Area && Find.WindowStack.FloatMenu == null)
            {
                LiveServeArea()?.MarkForDraw();
            }
        }

        /// <summary>
        /// After a gravship landing, find the bound area again on the map the
        /// stand landed on. The landing spawns things first and copies the
        /// ship's areas after (<c>GravshipPlacementUtility.cs:35-38</c>), and
        /// <c>PostSwapMap</c> runs after both, so the area the ship carried is
        /// there by now: Home by type, an allowed area under its label, found
        /// as the landing itself finds it (<c>MoveableArea_Allowed.TryCreateArea</c>,
        /// <c>AreaManager.GetLabeled</c>).
        ///
        /// <para>Only an area holding the stand's own cell is taken. One that
        /// flew with the stand on the deck always does, since the ship carries
        /// every painted cell of its floors. One that does not is a
        /// destination area that merely shares the name, "Area 1" say, and
        /// binding it would rebind behind the player's back. The stand then
        /// stays bound to the area it left, which is not live here, so it
        /// serves nothing and the alert lists it, as when an area is not
        /// carried at all. The binding is mended whatever the stand serves
        /// now, as a radius is kept while it serves an area. docs/DESIGN.md,
        /// "Catchments".</para>
        /// </summary>
        internal void RebindAfterLanding()
        {
            Map map = parent.Spawned ? parent.Map : null;
            if (map == null || (serveArea == null && serveAreaLabel == null) || IsLiveHere(serveArea))
            {
                return;
            }
            // The departed area keeps its own label, renamed or not; the
            // stored one is the fallback for a reference dropped on saving.
            string label = serveArea?.Label ?? serveAreaLabel;
            Area found = serveArea is Area_Home ? map.areaManager.Home : map.areaManager.GetLabeled(label);
            if (found == null || !IsLiveHere(found) || !found[parent.Position])
            {
                return;
            }
            serveArea = found;
            serveAreaLabel = found.Label;
        }

        /// <summary>
        /// The inspect pane's catchment line, or null for a stand serving an
        /// ordinary room: why the stand serves nothing, else its radius or its
        /// area's name.
        /// </summary>
        internal string CatchmentInspectLine()
        {
            switch (ServeFault)
            {
                case CatchmentFault.InTheOpen:
                    return "ShiftChange.InspectServesNothingOutdoors".Translate();
                case CatchmentFault.AreaGone:
                    return "ShiftChange.InspectAreaGone".Translate(serveAreaLabel);
                case CatchmentFault.OutsideArea:
                    return "ShiftChange.InspectStandOutsideArea".Translate(serveAreaLabel);
            }
            switch (serves)
            {
                case CatchmentKind.Radius:
                    return "ShiftChange.InspectServesRadius".Translate(serveRadius);
                case CatchmentKind.Area:
                    return "ShiftChange.InspectServesArea".Translate(serveAreaLabel);
                default:
                    return null;
            }
        }
    }
}
