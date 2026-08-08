using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// The work-type picker for one stand — a checkbox per visible work type,
    /// because a stand covers a SET of work: a workshop runs crafting,
    /// tailoring, smithing and art; a lab runs research and drug synthesis
    /// (which is Crafting work). A float menu of single choices could not say
    /// that (principal, 2026-08-08).
    ///
    /// Three states, kept canonical by <see cref="CompShiftStand.ToggleWork"/>:
    /// automatic (empty override — the room's defaults apply, and follow the
    /// room if its role changes), a custom set, or excluded. The checkboxes
    /// always show the EFFECTIVE set, so ticking one while automatic seeds the
    /// custom set from the room's defaults rather than starting from nothing.
    /// </summary>
    public class Dialog_SetStandWorkTypes : Window
    {
        private const float RowHeight = 26f;

        /// <summary>
        /// Two columns halve the height — most modlists fit without
        /// scrolling — and the alphabetical order fills DOWN each column
        /// then across, like a directory.
        /// </summary>
        private const int Columns = 2;

        private readonly CompShiftStand comp;
        private Vector2 scroll;

        private static string LabelOf(WorkTypeDef w)
        {
            return (w.gerundLabel ?? w.labelShort ?? w.defName).CapitalizeFirst();
        }

        public Dialog_SetStandWorkTypes(CompShiftStand comp)
        {
            this.comp = comp;
            doCloseX = true;
            doCloseButton = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
        }

        public override Vector2 InitialSize => new Vector2(520f, 500f);

        public override void DoWindowContents(Rect inRect)
        {
            float footer = CloseButSize.y + 10f;
            Rect content = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - footer);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(content);

            Text.Font = GameFont.Medium;
            listing.Label("ShiftChange.WorkTypesTitle".Translate());
            Text.Font = GameFont.Small;
            listing.Gap(4f);

            // Mode radios. The automatic label names what the room currently
            // resolves to, so "automatic" is never a mystery box.
            List<WorkTypeDef> roomDefaults = RoomWorkTypes.ForRole(
                comp.parent.Spawned ? comp.parent.GetRoom()?.Role : null);
            string autoLabel = "ShiftChange.WorkTypeAuto".Translate();
            autoLabel += ": " + (roomDefaults.Count > 0
                ? roomDefaults.Select(w => w.gerundLabel ?? w.defName).ToCommaList()
                : "ShiftChange.None".Translate().RawText);

            if (Widgets.RadioButtonLabeled(listing.GetRect(RowHeight), autoLabel, comp.IsAutomatic))
            {
                comp.SetAutomatic();
            }
            if (Widgets.RadioButtonLabeled(listing.GetRect(RowHeight),
                    "ShiftChange.WorkTypeNone".Translate(), comp.IsExcluded))
            {
                comp.SetExcluded();
            }

            listing.GapLine();
            listing.Label("ShiftChange.WorkTypesExplainer".Translate());
            listing.Gap(4f);

            // Alphabetical by the label actually shown — scanning for a known
            // name beats work-tab priority order once the list is long
            // (principal, 2026-08-08). Invisible types (Patient, BasicWorker)
            // are noise.
            List<WorkTypeDef> works = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .Where(w => w.visible)
                .OrderBy(LabelOf)
                .ToList();

            int rowsPerColumn = Mathf.Max(1, Mathf.CeilToInt(works.Count / (float)Columns));
            Rect outRect = listing.GetRect(content.height - listing.CurHeight);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, rowsPerColumn * RowHeight);
            float columnWidth = viewRect.width / Columns;
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            for (int i = 0; i < works.Count; i++)
            {
                WorkTypeDef work = works[i];
                Rect cell = new Rect(
                    i / rowsPerColumn * columnWidth,
                    i % rowsPerColumn * RowHeight,
                    columnWidth - 12f,
                    RowHeight - 2f);
                bool on = comp.HandlesWork(work);
                bool was = on;
                // Checkbox hugs its label rather than the column's far edge,
                // so column one's boxes cannot read as column two's.
                Widgets.CheckboxLabeled(cell, LabelOf(work), ref on, placeCheckboxNearText: true);
                if (on != was)
                {
                    comp.ToggleWork(work);
                }
            }
            Widgets.EndScrollView();

            listing.End();
        }
    }
}
