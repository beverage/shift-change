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
        /// Owner names for the inspect pane, capped. Bulk assignment is the
        /// point of the owner list — "Assign all" on a colony of forty is two
        /// clicks — so the unbounded version of this line was not a corner
        /// case, it was the feature working as intended printing forty short
        /// names into a pane sized for three.
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
        /// Display helper: "doctoring, researching" etc. The recreation
        /// trigger joins the same list, so every surface that names the
        /// stand's purpose names all of it.
        /// </summary>
        public string WorkTypesLabel()
        {
            List<string> labels = ServedLabels();
            return labels.Count == 0 ? (string)"ShiftChange.None".Translate() : labels.ToCommaList();
        }

        /// <summary>
        /// One name per thing this stand dresses for: its work types, then
        /// recreation, then sleep. Empty when it dresses for nothing.
        ///
        /// <para>The one list both the inspect pane and the switch's face are
        /// built from, so the two cannot disagree again (docs/DESIGN.md, "Rooms
        /// to work types").</para>
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

            // Only where the catchment is not an ordinary room, by the
            // full-change line's rule below: report what differs from the
            // default. A stand that serves nothing says why here.
            string catchment = CatchmentInspectLine();
            if (catchment != null)
            {
                line += "\n" + catchment;
            }

            // Say who owns or holds it here as well as on the gizmo: the base
            // comp reports ownership nowhere, and a stand that looks unassigned
            // is indistinguishable from one that simply never fires.
            List<Pawn> owners = AssignedOwners;
            line += "\n" + (owners.Count == 1
                ? "ShiftChange.InspectOwner".Translate(owners[0].LabelShort)
                : owners.Count > 1
                ? "ShiftChange.InspectOwners".Translate(owners.Count, OwnerNames(owners))
                : (ShiftChangeMod.PoolingEnabled
                    ? "ShiftChange.InspectPool".Translate()
                    // Keep the UI honest: with pooling off, "shared" would be
                    // a lie the player debugs for an evening.
                    : "ShiftChange.InspectPoolDisabled".Translate()));

            // Worth a line of its own: a full change is the difference between
            // a pawn keeping their own clothes under the uniform and wearing
            // nothing but the stand's kit, and nothing else on this pane would
            // tell them which stand they are looking at. Suppressed while
            // excluded, matching the dialog — the flag does nothing there.
            if (fullChange && !excluded)
            {
                line += "\n" + "ShiftChange.InspectFullChange".Translate();
            }

            // Only the OFF state earns a line, and it earns one because it is
            // the state that loses clothes. Same idiom as the full-change line
            // above — report the setting that DIFFERS from the default, since
            // "protected" stamped on every stand in the colony is noise, and
            // noise is what the player learns to stop reading. Excluded stands
            // returned above, so this is already an in-service stand.
            if (!withholdFromTrade)
            {
                line += "\n" + "ShiftChange.InspectTradeable".Translate();
            }

            if (OnShift && borrower != null)
            {
                line += "\n" + "ShiftChange.InspectOnShift".Translate(borrower.LabelShort);
                // The one case where the return trip stands aside on its own
                // and nothing else on screen says so: the stand is outside the
                // borrower's allowed area, so they keep the outfit on (see
                // Patch_JobInterception.StandForbiddenTo). Asked live with the
                // same predicate, so it disappears the moment the area does.
                if (Patch_JobInterception.StandForbiddenTo(parent, borrower))
                {
                    line += "\n" + "ShiftChange.InspectOutsideArea".Translate(borrower.LabelShort);
                }
            }
            else if (DepositOnly)
            {
                // NOT the empty line. An empty deposit-only stand is the
                // normal, correct, fully-working state — it hands nothing out
                // — so "nothing to change into" would report the feature as a
                // fault. Say what it does instead, since the storage filter
                // is the setting that makes it work and the one a player who
                // sees nothing happen needs pointing at.
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
            // The label reads the regime out loud: this one button decides
            // whether the stand is a shift stand or a regular one — and,
            // beside another outfit-stand mod, whose owner control it shows
            // — so its face carries the state instead of a static caption.
            //
            // ONE served purpose on the face, "(+N)" for the rest (decided
            // 2026-08-18): a workshop's four gerunds overflow a gizmo label
            // into unreadability. The full set stays on the hover desc and in
            // the inspect pane. Recreation and sleep count as purposes here
            // exactly as they do there (ServedLabels).
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
                    // Set to serve something that dresses nobody: its area
                    // is gone or leaves it out, or it serves its room in the
                    // open. The inspect pane and the alert say which.
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
                // The pencil, not ForbidOff — a forbid glyph on a
                // configuration gizmo reads as "this stand is disabled".
                // Zero-art rule: icons come from vanilla's atlas only.
                icon = TexButton.Rename,
                // One button per stand when several are selected: merged, it
                // set up one stand while looking as if it set them all
                // (docs/DESIGN.md, "Rooms to work types").
                groupable = false,
                action = () => Find.WindowStack.Add(new Dialog_SetStandWorkTypes(this)),
            };
        }
    }
}
