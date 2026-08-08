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

        private readonly CompShiftStand comp;
        private Vector2 scroll;

        public Dialog_SetStandWorkTypes(CompShiftStand comp)
        {
            this.comp = comp;
            doCloseX = true;
            doCloseButton = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
        }

        public override Vector2 InitialSize => new Vector2(400f, 600f);

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

            // Work types in the work tab's own order, so the list reads
            // familiar. Invisible types (Patient, BasicWorker) are noise.
            List<WorkTypeDef> works = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .Where(w => w.visible)
                .OrderByDescending(w => w.naturalPriority)
                .ToList();

            Rect outRect = listing.GetRect(content.height - listing.CurHeight);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, works.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float y = 0f;
            foreach (WorkTypeDef work in works)
            {
                bool on = comp.HandlesWork(work);
                bool was = on;
                Widgets.CheckboxLabeled(
                    new Rect(0f, y, viewRect.width, RowHeight - 2f),
                    (work.gerundLabel ?? work.labelShort ?? work.defName).CapitalizeFirst(),
                    ref on);
                if (on != was)
                {
                    comp.ToggleWork(work);
                }
                y += RowHeight;
            }
            Widgets.EndScrollView();

            listing.End();
        }
    }
}
