using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ShiftChange
{
    /// <summary>
    /// The stand's owner list. Replaces vanilla's
    /// <see cref="Dialog_AssignBuildingOwner"/> for outfit stands only — our
    /// own gizmo opens this instead, and no other assignable building in the
    /// load order is touched.
    ///
    /// <para><b>Why not the vanilla dialog.</b> Three things were wanted and
    /// none of them can be reached from outside it. It closes the window after
    /// a single assignment, which is correct for a one-owner building and
    /// wrong for a list (<c>Dialog_AssignBuildingOwner.cs</c>: the
    /// <c>Close()</c> is gated on <c>MaxAssignedPawnsCount == 1</c>); its row
    /// drawers are <c>private</c>, so a gender column cannot be added by
    /// subclassing; and a filter needs a header row it has no seam for. The
    /// alternative was a Harmony patch on the vanilla dialog, which would have
    /// changed beds, thrones and graves for every mod in the load order to
    /// give outfit stands a feature. Owning ~100 lines of layout is the
    /// cheaper of the two.</para>
    ///
    /// <para><b>Why a filter at all.</b> A stand serves whoever can wear
    /// something on it, and a lot of apparel is gender-locked — Royalty's
    /// formal vest and top hat are Male, its ladies hat Female. Without a
    /// restriction a shared stand holding a gown dresses a man in it and skips
    /// the hat. Restricting by hand is fine for four colonists and prohibitive
    /// for forty, which is what <see cref="Filter"/> and Assign all shown are
    /// for: two clicks to make a stand the women's stand.</para>
    /// </summary>
    public class Dialog_AssignStandOwners : Window
    {
        internal const float RowHeight = 35f;
        internal const float ButtonWidth = 165f;
        internal const float GenderWidth = 24f;
        internal const float HeaderHeight = 36f;
        internal const float SeparatorHeight = 7f;
        internal const float ButtonPadding = 26f;

        /// <summary>Which candidates the list offers. Assigned pawns are never
        /// filtered out — hiding an owner you might want to remove is how a
        /// filter becomes a trap.</summary>
        internal enum Filter
        {
            All,
            Male,
            Female,
        }

        internal readonly CompAssignableToPawn assignable;
        internal Filter filter = Filter.All;
        internal Vector2 scrollPosition;

        internal static readonly List<Pawn> Sorted = new List<Pawn>(16);

        public override Vector2 InitialSize => new Vector2(560f, 560f);

        public Dialog_AssignStandOwners(CompAssignableToPawn assignable)
        {
            this.assignable = assignable;
            doCloseButton = true;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        internal bool Passes(Pawn pawn)
        {
            switch (filter)
            {
                case Filter.Male: return pawn.gender == Gender.Male;
                case Filter.Female: return pawn.gender == Gender.Female;
                default: return true;
            }
        }

        internal List<Pawn> Candidates()
        {
            List<Pawn> assigned = assignable.AssignedPawnsForReading;
            return assignable.AssigningCandidates
                .Where(p => !assigned.Contains(p) && Passes(p))
                .ToList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;

            Rect header = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);
            DoHeader(header);

            Rect outRect = new Rect(inRect);
            outRect.yMin += HeaderHeight + 6f;
            outRect.yMax -= 40f;

            List<Pawn> assigned = assignable.AssignedPawnsForReading;
            List<Pawn> candidates = Candidates();

            float height = (assigned.Count + candidates.Count) * RowHeight + SeparatorHeight;
            Rect viewRect = new Rect(0f, 0f, outRect.width, height);
            Widgets.AdjustRectsForScrollView(inRect, ref outRect, ref viewRect);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            float y = 0f;
            Sort(assigned);
            for (int i = 0; i < Sorted.Count; i++)
            {
                DrawRow(Sorted[i], ref y, viewRect, i, isAssigned: true);
            }
            if (assigned.Count > 0)
            {
                using (new TextBlock(Widgets.SeparatorLineColor))
                {
                    Widgets.DrawLineHorizontal(0f, y + SeparatorHeight / 2f, viewRect.width);
                }
                y += SeparatorHeight;
            }
            Sort(candidates);
            for (int i = 0; i < Sorted.Count; i++)
            {
                DrawRow(Sorted[i], ref y, viewRect, i, isAssigned: false);
            }
            Sorted.Clear();
            Widgets.EndScrollView();
        }

        /// <summary>
        /// Filter on the left, bulk actions on the right. "Assign all shown"
        /// respects the filter, which is the whole point — it is the two-click
        /// path to a women's stand in a colony of forty.
        /// </summary>
        internal void DoHeader(Rect rect)
        {
            float x = rect.x;
            const float tabWidth = 74f;
            DrawFilterTab(new Rect(x, rect.y, tabWidth, 30f), Filter.All,
                          "ShiftChange.FilterAll".Translate());
            x += tabWidth + 4f;
            DrawFilterTab(new Rect(x, rect.y, tabWidth, 30f), Filter.Male,
                          "ShiftChange.FilterMen".Translate());
            x += tabWidth + 4f;
            DrawFilterTab(new Rect(x, rect.y, tabWidth, 30f), Filter.Female,
                          "ShiftChange.FilterWomen".Translate());

            List<Pawn> shown = Candidates();

            // Width from the text, not a guessed constant: "Remove all owners"
            // already overflowed 130 px in English, and every translation is a
            // different length.
            string clearLabel = "ShiftChange.ClearOwners".Translate();
            float clearWidth = Text.CalcSize(clearLabel).x + ButtonPadding;
            Rect clear = new Rect(rect.xMax - clearWidth, rect.y, clearWidth, 30f);
            if (assignable.AssignedPawnsForReading.Count > 0
                && Widgets.ButtonText(clear, clearLabel))
            {
                foreach (Pawn pawn in assignable.AssignedPawnsForReading.ToList())
                {
                    assignable.TryUnassignPawn(pawn);
                }
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            string allLabel = "ShiftChange.AssignAllShown".Translate(shown.Count);
            float allWidth = Text.CalcSize(allLabel).x + ButtonPadding;
            Rect all = new Rect(clear.xMin - allWidth - 8f, rect.y, allWidth, 30f);
            if (shown.Count > 0 && Widgets.ButtonText(all, allLabel))
            {
                foreach (Pawn pawn in shown)
                {
                    assignable.TryAssignPawn(pawn);
                }
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        internal void DrawFilterTab(Rect rect, Filter value, string label)
        {
            bool on = filter == value;
            if (on)
            {
                Widgets.DrawHighlightSelected(rect);
            }
            if (Widgets.ButtonText(rect, label, drawBackground: !on,
                                  overrideTextAnchor: TextAnchor.MiddleCenter))
            {
                filter = value;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Portrait, gender, name, action — the gender glyph in a column of its
        /// own rather than appended to the label, so the eye can run down it
        /// while scanning a long colony.
        /// </summary>
        internal void DrawRow(Pawn pawn, ref float y, Rect viewRect, int i, bool isAssigned)
        {
            Rect rect = new Rect(0f, y, viewRect.width, RowHeight);
            y += RowHeight;
            if (i % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }

            Rect icon = rect;
            icon.width = rect.height;
            Widgets.ThingIcon(icon, pawn);

            Rect gender = new Rect(icon.xMax + 4f, rect.y + (RowHeight - GenderWidth) / 2f,
                                   GenderWidth, GenderWidth);
            Texture2D glyph = pawn.gender.GetIcon();
            if (glyph != null)
            {
                GUI.DrawTexture(gender, glyph);
                TooltipHandler.TipRegion(gender, pawn.gender.GetLabel().CapitalizeFirst());
            }

            Rect button = rect;
            button.xMin = rect.xMax - ButtonWidth - 10f;
            button = button.ContractedBy(2f);
            string label = isAssigned ? "BuildingUnassign" : "BuildingAssign";
            if (Widgets.ButtonText(button, label.Translate()))
            {
                // No Close() either way. Vanilla shuts itself after one
                // assignment because a bed has one slot; a stand's owner list
                // is a set, and closing after each pick is what made building
                // one prohibitive.
                if (isAssigned)
                {
                    assignable.TryUnassignPawn(pawn);
                }
                else
                {
                    assignable.TryAssignPawn(pawn);
                }
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            Rect name = rect;
            name.xMin = gender.xMax + 6f;
            name.xMax = button.xMin - 10f;
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                Widgets.LabelEllipses(name, pawn.LabelCap);
            }
        }

        internal static void Sort(IEnumerable<Pawn> collection)
        {
            Sorted.Clear();
            Sorted.AddRange(collection);
            Sorted.SortBy(p => p.LabelShort);
        }
    }
}
