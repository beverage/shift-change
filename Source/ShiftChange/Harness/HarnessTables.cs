using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Harness cases for the static tables and the room resolver — the ones
    /// that assert a def name still resolves, or that a work target with no
    /// room of its own still finds one.
    ///
    /// <para>Split out of <see cref="DebugTools_LifecycleHarness"/> on
    /// 2026-09-15. These are separate TYPES rather than partials on purpose: a
    /// decompiler merges partials back into one class, so partials would have
    /// left the shipped dll reading exactly as it did before. The registration
    /// list that decides case ORDER stays in
    /// <see cref="DebugTools_LifecycleHarness.Run"/> and must not be
    /// scattered.</para>
    /// </summary>
    internal static class HarnessTables
    {
        /// <summary>
        /// Every def the room-role table names still exists.
        ///
        /// <c>RoomWorkTypes</c> resolves through <c>GetNamedSilentFail</c> and
        /// drops whatever is missing, so a renamed def empties a role's work
        /// list, <c>HandlesWork</c> returns false everywhere, and the mod does
        /// nothing at all — with a green harness and no log line. Most likely
        /// to fire on a game update rather than on an edit.
        ///
        /// <para>That assertion is hard for <c>Defaults</c> and must NOT be
        /// for <c>CompatDefaults</c>, whose names come from other mods and are
        /// absent on any list that does not carry them — the four-mod minimal
        /// list included. What is still checked there is the merge: whatever
        /// DOES resolve has to reach <c>ForRole</c>, so a compat row that
        /// silently fails to fold in is caught on a list that has the mod
        /// while a row naming a def nobody ships stays invisible. That is the
        /// intended asymmetry, not a weaker test.</para>
        /// </summary>
        /// <summary>
        /// The work-type dialog's body must never be drawn into a rect shorter
        /// than the body itself.
        ///
        /// <para>WHY THIS IS A CASE AND NOT A COMMENT. A player on a high UI
        /// scale photographed the dialog with its trigger rows and work grid
        /// simply absent. Nothing had thrown and nothing was in the log: the
        /// window is clamped to 85% of screen height, the rows above the grid
        /// needed more than the clamp left, and <c>Listing.GetRect</c> runs
        /// <c>NewColumnIfNeeded</c> on EVERY row — so at the first row that did
        /// not fit it did <c>curX += ColumnWidth + 17</c> and carried on
        /// drawing in a column outside the window. Captured at an effective
        /// 640x360: a 479-wide group, header rows at x=0, and the Recreation
        /// row at x=496.</para>
        ///
        /// <para>WHAT THIS COVERS, AND WHAT IT DOES NOT. The harness runs from
        /// <c>Game.FinalizeInit</c>, with no GUI context, so it cannot draw a
        /// listing and watch it wrap; <c>Listing.Begin</c> calls
        /// <c>Widgets.BeginGroup</c>. It asserts the arithmetic that decides
        /// the rect, which is the half a refactor is likely to get wrong. The
        /// other half — <c>maxOneColumn</c>, and the measured feedback that
        /// makes the number honest — is only exercised by drawing, and was
        /// verified by opening the dialog at four effective UI sizes
        /// (960x540, 853x480, 640x360, 480x270) on 2026-09-11.</para>
        ///
        /// <para>The heights are the ones really measured in that sweep rather
        /// than invented: bodies of 533, 665 and 847 against window content of
        /// 143, 220, 306 and 373.</para>
        /// </summary>
        internal static bool WorkTypeDialogBodyRectFitsTheBody()
        {
            float[] bodies = { 180f, 533f, 665f, 847f, 1200f };
            float[] contents = { 143f, 220f, 306f, 373f, 900f };
            bool ok = true;
            for (int b = 0; b < bodies.Length; b++)
            {
                for (int c = 0; c < contents.Length; c++)
                {
                    Rect content = new Rect(0f, 0f, 480f, contents[c]);
                    bool scrolling;
                    Rect given = Dialog_SetStandWorkTypes.BodyRect(bodies[b], content, out scrolling);

                    ok &= Expect(given.height >= bodies[b] - 0.5f,
                        "body " + bodies[b].ToString("0") + " into a "
                        + contents[c].ToString("0") + " window gets "
                        + given.height.ToString("0"));

                    // The converse matters too: a body that fits must NOT open
                    // a scroll view. Every clipped region is one more thing
                    // that has to clip the way we assume, and the grid's own
                    // scroll view was removed for that reason.
                    bool shouldScroll = bodies[b] > contents[c] + 1f;
                    ok &= Expect(scrolling == shouldScroll,
                        "body " + bodies[b].ToString("0") + " in "
                        + contents[c].ToString("0") + (shouldScroll ? " scrolls" : " does not scroll"));
                }
            }
            return ok;
        }

        internal static bool RoomRoleTableResolves()
        {
            bool ok = Expect(RoomWorkTypes.Defaults.Count > 0, "the table is not empty");
            foreach (KeyValuePair<string, string[]> entry in RoomWorkTypes.Defaults)
            {
                RoomRoleDef role = DefDatabase<RoomRoleDef>.GetNamedSilentFail(entry.Key);
                ok &= Expect(role != null, "room role " + entry.Key + " resolves");
                int resolved = 0;
                for (int i = 0; i < entry.Value.Length; i++)
                {
                    if (DefDatabase<WorkTypeDef>.GetNamedSilentFail(entry.Value[i]) != null)
                    {
                        resolved++;
                    }
                    else
                    {
                        Expect(false, "work type " + entry.Value[i] + " resolves");
                        ok = false;
                    }
                }

                // Absent is the ordinary case here, so count without asserting.
                string[] compat;
                if (RoomWorkTypes.CompatDefaults.TryGetValue(entry.Key, out compat))
                {
                    for (int i = 0; i < compat.Length; i++)
                    {
                        if (DefDatabase<WorkTypeDef>.GetNamedSilentFail(compat[i]) != null)
                        {
                            resolved++;
                        }
                    }
                }

                if (role != null)
                {
                    ok &= Expect(RoomWorkTypes.ForRole(role).Count == resolved,
                                 entry.Key + " maps to all " + resolved + " of its work types");
                }
            }
            return ok;
        }

        /// <summary>
        /// A building that is impassable AND full-fillage has no region, so its
        /// own cells belong to no room at all
        /// (<c>RegionTypeUtility.GetExpectedRegionType</c> returns
        /// <c>RegionType.None</c>). Every arm reads the job's room from the
        /// target cell, so before <see cref="Patch_JobInterception.RoomBearing"/>
        /// such a work target was invisible and pawns never changed at it.
        ///
        /// <para>Uses a plain WALL as the stand-in, because it is Core, it is
        /// exactly that combination, and one dropped inside a room does not
        /// split it. The multi-cell part of the real case needs no separate
        /// test: <c>GenAdj.CellsAdjacent8Way(thing)</c> sizes its ring from
        /// <c>def.size</c>, which is engine code.</para>
        ///
        /// <para>The precondition is asserted rather than assumed. If a wall's
        /// cell ever DID have a room, this case would otherwise pass without
        /// exercising anything.</para>
        /// </summary>
        internal static bool SolidTargetResolvesARoom(Map map, CellRect pad)
        {
            IntVec3 cell = pad.CenterCell;
            Room before = cell.GetRoom(map);
            Thing wall = null;
            try
            {
                wall = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel),
                                      cell, map);
                bool ok = Expect(wall != null && wall.Spawned, "the stand-in wall spawned");
                if (!ok)
                {
                    return false;
                }
                ok &= Expect(wall.def.passability == Traversability.Impassable
                             && wall.def.Fillage == FillCategory.Full,
                             "the stand-in is impassable and full-fillage");
                ok &= Expect(cell.GetRoom(map) == null,
                             "its own cell has no room, which is the bug's precondition");

                IntVec3 bearing = Patch_JobInterception.RoomBearing(
                    cell, new LocalTargetInfo(wall), map);
                Room after = bearing.GetRoom(map);
                ok &= Expect(after != null, "RoomBearing returns a cell that has a room");
                ok &= Expect(before == null || after == before,
                             "and it is the room the target sits in");
                return ok;
            }
            finally
            {
                if (wall != null && wall.Spawned)
                {
                    wall.Destroy();
                }
            }
        }

        /// <summary>
        /// <summary>
        /// A work target sunk into an EXTERIOR wall must resolve to the room it
        /// encloses, whichever side of that room it sits in.
        ///
        /// <para>The ring around such a target splits evenly: three cells in
        /// the room, three outdoors, and the two flanking cells are wall and
        /// carry no room at all. Nothing wins on count, so the answer is
        /// whatever breaks the tie. <c>GenAdj.CellsAdjacent8Way</c> enumerates
        /// the SOUTH row first (<c>GenAdj.cs:190</c>) and <c>RoomBearing</c>
        /// promotes only on a strict <c>&gt;</c>, so a tie goes to whichever
        /// room the walk reached first, which is always the southern one.</para>
        ///
        /// <para>The same building in the north wall and in the south wall of
        /// one room therefore resolves two different ways: the room in the
        /// first case, the map-wide outdoor room in the second. That is the
        /// shipped v1.4.0 defect. A plutonium processor is 4x4 rather than
        /// 1x1, but the tie is identical and a vanilla wall reproduces it
        /// without Rimatomics loaded, so this runs on the minimal list.</para>
        ///
        /// <para>The even split is ASSERTED, not assumed. If the ring ever
        /// stopped tying, the case would pass while exercising nothing.</para>
        /// </summary>
        internal static bool ExteriorWallTargetResolvesTheRoom(Map map, CellRect pad)
        {
            try
            {
                GenDebug.ClearArea(pad, map);
                EnclosePad(map, pad);

                Room inside = pad.CenterCell.GetRoom(map);
                bool ok = Expect(inside != null && !inside.PsychologicallyOutdoors,
                                 "the pad encloses a real indoor room");
                if (!ok)
                {
                    return false;
                }

                int x = pad.CenterCell.x;
                ok &= EdgeWallBears(map, new IntVec3(x, 0, pad.maxZ), inside, "north");
                ok &= EdgeWallBears(map, new IntVec3(x, 0, pad.minZ), inside, "south");
                return ok;
            }
            finally
            {
                foreach (IntVec3 cell in pad)
                {
                    map.roofGrid.SetRoof(cell, null);
                }
                GenDebug.ClearArea(pad, map);
            }
        }

        /// <summary>
        /// One edge of <see cref="ExteriorWallTargetResolvesTheRoom"/>: counts
        /// how the wall's ring divides between the enclosed room and everything
        /// else, requires that division to be even (otherwise there is no tie
        /// to break and the case proves nothing), then asserts the bearing
        /// lands in the room rather than outdoors.
        /// </summary>
        internal static bool EdgeWallBears(Map map, IntVec3 cell, Room inside, string edge)
        {
            Thing wall = cell.GetEdifice(map);
            if (!Expect(wall != null, "the " + edge + " edge cell holds a wall"))
            {
                return false;
            }

            int inRoom = 0;
            int elsewhere = 0;
            foreach (IntVec3 ring in GenAdj.CellsAdjacent8Way(wall))
            {
                if (!ring.InBounds(map))
                {
                    continue;
                }
                Room room = ring.GetRoom(map);
                if (room == null)
                {
                    continue;
                }
                if (room == inside)
                {
                    inRoom++;
                }
                else
                {
                    elsewhere++;
                }
            }

            bool ok = Expect(inRoom > 0 && inRoom == elsewhere,
                             "the " + edge + " ring splits evenly (" + inRoom + " in the room, "
                             + elsewhere + " out), so the answer is a tie-break");

            IntVec3 bearing = Patch_JobInterception.RoomBearing(
                cell, new LocalTargetInfo(wall), map);
            ok &= Expect(bearing.GetRoom(map) == inside,
                         "a target in the " + edge + " wall resolves to the room it encloses");
            return ok;
        }

        /// Whether the mod those compat tables are written against is loaded.
        /// Asked by resolving a type we already depend on rather than by name,
        /// so the answer is the same question the tables themselves ask.
        /// </summary>
        internal static bool RimatomicsLoaded
        {
            get { return AccessTools.TypeByName("Rimatomics.reactorCore") != null; }
        }

        /// <summary>
        /// <see cref="JobRoomTargets"/> holds two lists of another mod's
        /// defNames. Both are ALLOWED to miss — that is what a compat table is
        /// — so the standing assertions are structural, and the resolution
        /// assertions only fire when the mod is actually loaded.
        ///
        /// <para>The load-bearing one is the official-def guard. Retargeting a
        /// vanilla job to its targetB, or making the mod ignore a vanilla work
        /// giver, would change core behaviour for everyone and there is no
        /// symptom that points at a table.</para>
        /// </summary>
        internal static bool JobTablesHold()
        {
            bool ok = Expect(JobRoomTargets.RoomIsTargetB.Count > 0, "the targetB list is not empty");
            ok &= Expect(JobRoomTargets.IgnoredGivers.Count > 0, "the ignored-giver list is not empty");
            ok &= Expect(!JobRoomTargets.RoomIsTargetB.Any(string.IsNullOrEmpty)
                         && !JobRoomTargets.IgnoredGivers.Any(string.IsNullOrEmpty),
                         "no entry is blank");
            ok &= Expect(!JobRoomTargets.UsesTargetB(null) && !JobRoomTargets.Ignored(null),
                         "a null def matches nothing");

            foreach (JobDef job in DefDatabase<JobDef>.AllDefsListForReading)
            {
                if (job.modContentPack != null && job.modContentPack.IsOfficialMod
                    && JobRoomTargets.RoomIsTargetB.Contains(job.defName))
                {
                    ok &= Expect(false, "official job " + job.defName + " is NOT retargeted");
                }
            }
            foreach (WorkGiverDef giver in DefDatabase<WorkGiverDef>.AllDefsListForReading)
            {
                if (giver.modContentPack != null && giver.modContentPack.IsOfficialMod
                    && JobRoomTargets.IgnoredGivers.Contains(giver.defName))
                {
                    ok &= Expect(false, "official giver " + giver.defName + " is NOT ignored");
                }
            }

            if (!RimatomicsLoaded)
            {
                ok &= ExpectKnownGap(false, "the table's defNames resolve against a loaded Rimatomics",
                                     "Rimatomics is not on this mod list, so every name check above was skipped");
                return ok;
            }
            foreach (string name in JobRoomTargets.RoomIsTargetB)
            {
                ok &= Expect(DefDatabase<JobDef>.GetNamedSilentFail(name) != null,
                             "job " + name + " resolves");
            }
            foreach (string name in JobRoomTargets.IgnoredGivers)
            {
                ok &= Expect(DefDatabase<WorkGiverDef>.GetNamedSilentFail(name) != null,
                             "giver " + name + " resolves");
            }
            return ok;
        }

        /// <summary>
        /// <see cref="RoomContentsWork"/>'s markers, same contract: structure
        /// always, resolution only when the mod is there.
        ///
        /// <para>The assertion worth having is the HALF-RESOLVED one. A marker
        /// whose type is present but whose work types are not is the exact
        /// shape of a typo in a work-type name, and it fails silently — the
        /// marker drops out of <c>Active</c> and every room it should have
        /// armed simply does nothing, with no log line.</para>
        /// </summary>
        internal static bool ContentsMarkersHold()
        {
            bool ok = Expect(RoomContentsWork.Markers.Length > 0, "there is at least one marker");
            ok &= Expect(RoomContentsWork.Markers.Select(m => m.typeName).Distinct().Count()
                         == RoomContentsWork.Markers.Length,
                         "no marker type is listed twice");
            foreach (RoomContentsWork.Marker marker in RoomContentsWork.Markers)
            {
                ok &= Expect(!marker.typeName.NullOrEmpty()
                             && marker.workNames != null && marker.workNames.Length > 0,
                             marker.typeName + " names a type and at least one work type");
            }

            // HasComp, against the def database rather than a named def, so the
            // check does not go stale when a def is renamed out from under it.
            ThingDef withComps = DefDatabase<ThingDef>.AllDefsListForReading
                .FirstOrDefault(d => d.comps != null && d.comps.Any(c => c != null && c.compClass != null));
            ThingDef without = DefDatabase<ThingDef>.AllDefsListForReading
                .FirstOrDefault(d => d.comps == null || d.comps.Count == 0);
            if (withComps != null)
            {
                ok &= Expect(RoomContentsWork.HasComp(withComps, typeof(ThingComp)),
                             "HasComp sees a comp on " + withComps.defName);
            }
            if (without != null)
            {
                ok &= Expect(!RoomContentsWork.HasComp(without, typeof(ThingComp)),
                             "HasComp sees none on " + without.defName);
            }
            ok &= Expect(!RoomContentsWork.HasComp(null, typeof(ThingComp)),
                         "HasComp tolerates a null def");

            foreach (RoomContentsWork.Marker marker in RoomContentsWork.Markers)
            {
                if (AccessTools.TypeByName(marker.typeName) == null)
                {
                    continue;   // that mod is not installed; the row simply drops
                }
                ok &= Expect(RoomContentsWork.Active.Contains(marker),
                             marker.typeName + " resolved its work types too, not just its type");
            }
            if (!RimatomicsLoaded)
            {
                ok &= ExpectKnownGap(false, "every marker resolves against a loaded Rimatomics",
                                     "Rimatomics is not on this mod list, so no marker could resolve");
            }
            return ok;
        }
    }
}
