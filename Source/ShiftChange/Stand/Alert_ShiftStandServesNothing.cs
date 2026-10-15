using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// "Shift stand serves nothing": every stand set to serve something that
    /// dresses nobody, because its allowed area is gone, it stands outside that
    /// area, or it serves its room while standing in the open, where the room is
    /// all the open ground on the map (docs/DESIGN.md, "Catchments"). Deleting
    /// an area tells pawns and nothing else, and a stand that has quietly
    /// stopped dressing anyone looks like one with no work nearby.
    ///
    /// <para>An alert rather than a message: it stays up until the stand is
    /// fixed, and it needs no hook on the area's removal, since vanilla asks
    /// every alert about once every 24 frames. Vanilla also creates every
    /// alert subclass itself, so nothing registers this one, and alerts are
    /// not saved, so it can be renamed freely.</para>
    ///
    /// <para>It reads every stand on every map rather than
    /// <see cref="CompShiftStand.CatchmentStands"/>: a stand in the open serves
    /// its room, so that registry never holds it.</para>
    /// </summary>
    public class Alert_ShiftStandServesNothing : Alert
    {
        internal readonly List<Thing> stands = new List<Thing>();

        internal readonly List<string> lines = new List<string>();

        public Alert_ShiftStandServesNothing()
        {
            defaultLabel = "ShiftChange.AlertServesNothing".Translate();
            defaultPriority = AlertPriority.Medium;
        }

        public override AlertReport GetReport()
        {
            stands.Clear();
            lines.Clear();
            List<ThingDef> defs = Patch_JobInterception.StandDefs;
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                for (int d = 0; d < defs.Count; d++)
                {
                    List<Thing> things = maps[m].listerThings.ThingsOfDef(defs[d]);
                    for (int i = 0; i < things.Count; i++)
                    {
                        CompShiftStand comp = things[i].TryGetComp<CompShiftStand>();
                        string line = comp == null ? null : LineFor(comp);
                        if (line != null)
                        {
                            stands.Add(things[i]);
                            lines.Add(line);
                        }
                    }
                }
            }
            return AlertReport.CulpritsAre(stands);
        }

        /// <summary>The stand's line in the explanation, or null when it serves.</summary>
        internal static string LineFor(CompShiftStand comp)
        {
            switch (comp.ServeFault)
            {
                case CatchmentFault.AreaGone:
                    return "ShiftChange.AlertAreaGone".Translate(
                        comp.parent.LabelCap, comp.WorkTypesLabel(), comp.serveAreaLabel);
                case CatchmentFault.OutsideArea:
                    return "ShiftChange.AlertOutsideArea".Translate(
                        comp.parent.LabelCap, comp.WorkTypesLabel(), comp.serveAreaLabel);
                case CatchmentFault.InTheOpen:
                    return "ShiftChange.AlertInTheOpen".Translate(comp.parent.LabelCap, comp.WorkTypesLabel());
                default:
                    return null;
            }
        }

        public override string GetLabel()
        {
            return stands.Count > 1
                ? (string)"ShiftChange.AlertServesNothingPlural".Translate()
                : defaultLabel;
        }

        public override TaggedString GetExplanation()
        {
            return "ShiftChange.AlertServesNothingDesc".Translate(lines.ToLineList("  - "));
        }
    }
}
