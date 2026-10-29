using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    public partial class CompShiftStand
    {
        /// <summary>
        /// Owner names for the inspect pane, capped: bulk assignment can give a stand every
        /// colonist as an owner, and the pane has room for a few names.
        /// </summary>
        internal static string OwnerNames(List<Pawn> owners)
        {
            const int shown = 3;
            if (owners.Count <= shown)
            {
                return owners.Select(p => p.LabelShort).ToCommaList();
            }
            string head = owners.Take(shown).Select(p => p.LabelShort).ToCommaList();
            return "ShiftChange.InspectOwnersOverflow".Translate(head, owners.Count - shown);
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            DrawCatchment();
        }

        /// <summary>
        /// What the stand dresses for as a comma-separated list, such as "doctoring,
        /// researching", or "nothing" when there is none.
        /// </summary>
        public string WorkTypesLabel()
        {
            List<string> labels = ServedLabels();
            return labels.Count == 0 ? (string)"ShiftChange.None".Translate() : labels.ToCommaList();
        }

        /// <summary>
        /// One name per thing this stand dresses for: its work types, then recreation, then
        /// sleep. Empty when it dresses for nothing.
        ///
        /// <para>The inspect pane and the switch's face are both built from this one list, so
        /// the two cannot disagree.</para>
        /// </summary>
        internal List<string> ServedLabels()
        {
            List<WorkTypeDef> works = WorkTypes;
            List<string> labels = new List<string>(works.Count + 1);
            for (int i = 0; i < works.Count; i++)
            {
                labels.Add(WorkTypeLabels.Of(works[i]));
            }
            if (HandlesRecreation())
            {
                labels.Add("ShiftChange.Recreation".Translate().RawText);
            }
            if (HandlesRest())
            {
                labels.Add("ShiftChange.Rest".Translate().RawText);
            }
            return labels;
        }

        public override string CompInspectStringExtra()
        {
            if (excluded)
            {
                return "ShiftChange.InspectExcluded".Translate();
            }
            if (!ServesSomething)
            {
                return null;
            }

            string line = "ShiftChange.InspectWork".Translate(WorkTypesLabel());

            // A setting gets a line only where it differs from the default (a catchment other than
            // an ordinary room, a full change, withholding from trade off): a line on every stand
            // is noise. A stand that serves nothing says why here.
            string catchment = CatchmentInspectLine();
            if (catchment != null)
            {
                line += "\n" + catchment;
            }

            // The base comp writes no owner into the inspect pane, so say who owns the stand
            // here, or that it is shared: otherwise a stand with no owner looks like one that
            // never fires.
            List<Pawn> owners = AssignedOwners;
            line += "\n" + (owners.Count == 1
                ? "ShiftChange.InspectOwner".Translate(owners[0].LabelShort)
                : owners.Count > 1
                ? "ShiftChange.InspectOwners".Translate(owners.Count, OwnerNames(owners))
                : (ShiftChangeMod.PoolingEnabled
                    ? "ShiftChange.InspectPool".Translate()
                    // With pooling off the stand is not shared, so it says unassigned.
                    : "ShiftChange.InspectPoolDisabled".Translate()));

            // A full change leaves the pawn wearing only the stand's kit, where otherwise their
            // own clothes stay under the uniform. Not shown while excluded, where the flag does
            // nothing.
            if (fullChange && !excluded)
            {
                line += "\n" + "ShiftChange.InspectFullChange".Translate();
            }

            if (!withholdFromTrade)
            {
                line += "\n" + "ShiftChange.InspectTradeable".Translate();
            }

            if (OnShift && borrower != null)
            {
                line += "\n" + "ShiftChange.InspectOnShift".Translate(borrower.LabelShort);
                // A stand outside the borrower's allowed area is passed over by the return trip,
                // so they keep the outfit on, and nothing else on screen says why. Asked live, so
                // the line lifts when the area changes (docs/DESIGN.md, "Allowed areas").
                if (Patch_JobInterception.StandForbiddenTo(parent, borrower))
                {
                    line += "\n" + "ShiftChange.InspectOutsideArea".Translate(borrower.LabelShort);
                }
            }
            else if (DepositOnly)
            {
                // Not the empty line: a deposit-only stand is empty by design and hands nothing
                // out, so "nothing to change into" would read as a fault. This line points at
                // the storage filter, the setting that makes it work.
                line += "\n" + "ShiftChange.InspectDepositOnly".Translate();
            }
            else if (!HasWearable)
            {
                line += "\n" + "ShiftChange.InspectEmpty".Translate();
            }
            return line;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            string source = excluded
                ? "ShiftChange.Excluded".Translate().RawText
                : IsAutomatic
                    ? "ShiftChange.FromRoom".Translate().RawText
                    : "ShiftChange.Manual".Translate().RawText;
            // This button decides whether the stand is a shift stand or a regular one, so its
            // face carries that state. The face names one served purpose and counts the rest
            // ("+N"), because a workshop's four work types overflow a gizmo label; the full set
            // is in the hover text and the inspect pane.
            string regime;
            if (excluded)
            {
                regime = "ShiftChange.RegimeOff".Translate().RawText;
            }
            else
            {
                List<string> served = ServedLabels();
                if (served.Count == 0)
                {
                    regime = "ShiftChange.RegimeIdle".Translate().RawText;
                }
                else if (ServeFault != CatchmentFault.None)
                {
                    // Set to serve something but dressing nobody; the inspect pane and the alert
                    // say why.
                    regime = "ShiftChange.RegimeServesNothing".Translate().RawText;
                }
                else
                {
                    regime = served.Count == 1
                        ? "ShiftChange.RegimeShift".Translate(served[0]).RawText
                        : "ShiftChange.RegimeShiftMore".Translate(served[0], served.Count - 1).RawText;
                }
            }
            yield return new Command_Action
            {
                defaultLabel = regime,
                defaultDesc = ByCatchment("ShiftChange.SetWorkTypeDesc")
                    .Translate(WorkTypesLabel(), source, serveRadius, serveAreaLabel ?? string.Empty),
                // Vanilla's rename pencil, since the mod ships no art; a forbid glyph on a
                // configuration button would read as a disabled stand.
                icon = TexButton.Rename,
                // Not groupable: a merged button runs every selected stand's action, and each
                // opens a dialog that closes the one before, so it would set up one stand while
                // looking as if it set them all.
                groupable = false,
                action = () => Find.WindowStack.Add(new Dialog_SetStandWorkTypes(this)),
                onHover = () => PointAt(parent),
            };
        }

        /// <summary>
        /// Marks a stand on the map while one of its buttons is hovered: with several stands
        /// selected their buttons sit side by side, and none of them says where its stand is.
        /// </summary>
        internal static void PointAt(Thing stand)
        {
            // Vanilla's arrow sits just below a stand near the screen centre, pointing up at it;
            // further out it points from the centre towards it, so the square marks the stand.
            TargetHighlighter.Highlight(stand, arrow: true, colonistBar: false);
            if (stand.Map != Find.CurrentMap)
            {
                return;
            }
            // The targeter's square sits above buildings; the highlighter's circle hides under the
            // sprite. Hover runs in the map interface's update, so it shows this frame.
            GenDraw.DrawTargetHighlight(stand);
        }
    }
}
