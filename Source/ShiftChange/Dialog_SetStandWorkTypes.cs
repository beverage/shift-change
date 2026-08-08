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
    /// Sized to the MODLIST, not to vanilla (same day): heavily modded games
    /// carry 40+ work types, so the window derives its column count from the
    /// list length and its height from the resulting rows, clamped to the
    /// screen — the scrollbar appears only when the clamp bites. Checkboxes
    /// sit at a FIXED x just after the widest label: a straight, scannable
    /// line per column, close enough to its own labels that a box cannot read
    /// as belonging to the next column (the failure mode of far-right
    /// alignment). Box-before-label would be clearer still, but vanilla never
    /// does it, so neither do we.
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
        private const float CheckboxSize = 24f;
        private const float LabelGap = 10f;
        private const float MaxLabelWidth = 260f;
        private const float ColumnGutter = 36f;
        private const float ScrollbarAllowance = 20f;
        private const float HeaderAllowance = 160f;
        private const float MinWindowWidth = 420f;

        /// <summary>Rows a column aims for before the dialog grows another column.</summary>
        private const int TargetRows = 14;

        private readonly CompShiftStand comp;
        private readonly List<WorkTypeDef> works;
        private readonly float cellWidth;
        private readonly int columns;
        private readonly int rowsPerColumn;
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

            // Alphabetical by the label actually shown — scanning for a known
            // name beats work-tab priority order once the list is long.
            // Invisible types (Patient, BasicWorker) are noise.
            works = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .Where(w => w.visible)
                .OrderBy(LabelOf)
                .ToList();

            // The checkbox column sits just past the widest label, so measure
            // them all once. Capped so one modded novel of a label cannot
            // stretch every column; the draw side truncates to match.
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Small;
            float widestLabel = 0f;
            for (int i = 0; i < works.Count; i++)
            {
                widestLabel = Mathf.Max(widestLabel, Text.CalcSize(LabelOf(works[i])).x);
            }
            Text.Font = previousFont;
            cellWidth = Mathf.Min(widestLabel + LabelGap, MaxLabelWidth) + CheckboxSize;

            // Enough columns to keep each near TargetRows, clamped to what the
            // screen can hold side by side.
            int wanted = Mathf.Max(1, Mathf.CeilToInt(works.Count / (float)TargetRows));
            float usableWidth = UI.screenWidth * 0.9f - Margin * 2f - ScrollbarAllowance;
            int fitting = Mathf.Max(1, Mathf.FloorToInt((usableWidth + ColumnGutter) / (cellWidth + ColumnGutter)));
            columns = Mathf.Clamp(wanted, 1, fitting);
            rowsPerColumn = Mathf.Max(1, Mathf.CeilToInt(works.Count / (float)columns));
        }

        public override Vector2 InitialSize
        {
            get
            {
                float width = Margin * 2f + columns * cellWidth
                    + (columns - 1) * ColumnGutter + ScrollbarAllowance;
                width = Mathf.Clamp(width, MinWindowWidth, UI.screenWidth * 0.9f);

                float height = Margin * 2f + HeaderAllowance
                    + rowsPerColumn * RowHeight + CloseButSize.y + 10f;
                height = Mathf.Min(height, UI.screenHeight * 0.85f);

                return new Vector2(width, height);
            }
        }

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

            Rect outRect = listing.GetRect(content.height - listing.CurHeight);
            Rect viewRect = new Rect(0f, 0f,
                columns * cellWidth + (columns - 1) * ColumnGutter,
                rowsPerColumn * RowHeight);
            float labelWidth = cellWidth - CheckboxSize;

            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            for (int i = 0; i < works.Count; i++)
            {
                WorkTypeDef work = works[i];
                Rect cell = new Rect(
                    i / rowsPerColumn * (cellWidth + ColumnGutter),
                    i % rowsPerColumn * RowHeight,
                    cellWidth,
                    RowHeight - 2f);
                bool on = comp.HandlesWork(work);
                bool was = on;
                // Default placement puts the box at the cell's right edge —
                // which, with the cell sized to the widest label, is a fixed
                // aligned column just past the text, and the gutter beyond it
                // is dead space no box can wander into.
                Widgets.CheckboxLabeled(cell, LabelOf(work).Truncate(labelWidth - 4f), ref on);
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
