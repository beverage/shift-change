using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ShiftChange
{
    /// <summary>
    /// The owner list for outfit stands, opened by our own gizmo in place of vanilla's
    /// <see cref="Dialog_AssignBuildingOwner"/>.
    ///
    /// <para>Vanilla's dialog keeps its row drawers private, has no seam for a header row, and
    /// its Assign calls <c>TryAssignPawn</c>, which on a stand always adds. So the gender
    /// column, filter, bulk buttons and the one-owner pick (<see cref="SingleOwner"/>) need a
    /// window of our own.</para>
    ///
    /// <para>The gender filter exists because apparel can be gender-locked (Royalty's formal
    /// vest and top hat are male, its ladies hat female), so a stand shared by both genders
    /// needs a restriction. <see cref="Filter"/> plus Assign all shown makes a stand a
    /// women's stand in two clicks. On a stand not used for shift changes, Assign replaces
    /// the whole list with one colonist; a group kept from shift use stays until then.</para>
    /// </summary>
    public class Dialog_AssignStandOwners : Window
    {
        internal const float RowHeight = 35f;
        internal const float ButtonWidth = 165f;
        internal const float GenderWidth = 24f;
        internal const float HeaderHeight = 36f;
        internal const float SeparatorHeight = 7f;
        internal const float ButtonPadding = 26f;

        /// <summary>Which candidates the list offers. Assigned pawns are always shown, so an
        /// owner can always be removed.</summary>
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

        /// <summary>True on a stand not used for shift changes, where Assign replaces the owner
        /// list and Assign all is not drawn
        /// (<see cref="CompAssignableToPawn_ShiftStand.SingleOwnerOnly"/>).</summary>
        internal bool SingleOwner =>
            (assignable as CompAssignableToPawn_ShiftStand)?.SingleOwnerOnly ?? false;

        /// <summary>
        /// A row's Assign button: makes <paramref name="pawn"/> the only owner when
        /// <see cref="SingleOwner"/>, otherwise adds them to the list.
        /// </summary>
        internal void Assign(Pawn pawn)
        {
            if (SingleOwner)
            {
                ((CompAssignableToPawn_ShiftStand)assignable).AssignSoleOwner(pawn);
            }
            else
            {
                assignable.TryAssignPawn(pawn);
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
        /// Filter tabs on the left, bulk actions on the right. Assign all shown respects the
        /// filter.
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

            // Sized from the label text, since translations differ in length.
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

            // Not drawn where Assign is a pick: a group on such a stand drives nothing of ours
            // and exceeds Outfit Stands Plus's one slot per stand, so it would reach nobody.
            if (SingleOwner)
            {
                return;
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
        /// One row: portrait, gender glyph, name, and the Assign or Unassign button.
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
                // The window stays open after a pick: a stand takes a list of owners, where
                // vanilla closes after one assignment on a one-slot building.
                if (isAssigned)
                {
                    assignable.TryUnassignPawn(pawn);
                }
                else
                {
                    Assign(pawn);
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
