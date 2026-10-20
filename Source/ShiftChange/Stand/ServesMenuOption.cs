using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// A row of the Serves menu, which groups its rows into sections: where the stand serves
    /// (this room, a radius), the map's allowed areas, and making or managing an area.
    /// Vanilla's float menu stacks its rows with no spacing and has no sections, so the first
    /// row of each section is made taller by <see cref="SectionGap"/>
    /// (<see cref="MarkSections"/>) and draws itself below that gap, which stays transparent.
    ///
    /// <para>The extra height goes into the row's cached height, the one figure the menu
    /// reads both to size its window and to lay out its rows. The menu's constructor
    /// recomputes that figure, so <see cref="Open"/> adds the gap after it. If Ludeon renames
    /// the field, the menu draws as one list.</para>
    /// </summary>
    internal class ServesMenuOption : FloatMenuOption
    {
        internal const int SectionWhere = 0;
        internal const int SectionAreas = 1;
        internal const int SectionManage = 2;

        /// <summary>The transparent gap above a section's first row, in pixels.</summary>
        internal const float SectionGap = 8f;

        /// <summary>The row's section; a change between neighbours opens a section.</summary>
        internal readonly int section;

        /// <summary>Transparent height above the row: <see cref="SectionGap"/> or zero.</summary>
        internal float gapAbove;

        internal ServesMenuOption(int section, string label, Action action)
            : base(label, action)
        {
            this.section = section;
        }

        /// <summary>A row with a swatch: a solid texture where vanilla puts an item icon.</summary>
        internal ServesMenuOption(int section, string label, Action action, Texture2D swatch, Color swatchColor,
                                  Action<Rect> mouseover)
            : base(label, action, swatch, swatchColor, MenuOptionPriority.Default, mouseover)
        {
            this.section = section;
        }

        public override bool DoGUI(Rect rect, bool colonistOrdering, FloatMenu floatMenu)
        {
            rect.yMin += gapAbove;
            return base.DoGUI(rect, colonistOrdering, floatMenu);
        }

        /// <summary>Write access to a row's cached height, or null if the field is gone.</summary>
        internal static readonly AccessTools.FieldRef<FloatMenuOption, float> RequiredHeightRef =
            ResolveRequiredHeight();

        internal static AccessTools.FieldRef<FloatMenuOption, float> ResolveRequiredHeight()
        {
            FieldInfo field = AccessTools.DeclaredField(typeof(FloatMenuOption), "cachedRequiredHeight");
            return field == null ? null : AccessTools.FieldRefAccess<FloatMenuOption, float>(field);
        }

        /// <summary>Opens <paramref name="options"/> as a float menu with section gaps.</summary>
        internal static FloatMenu Open(List<FloatMenuOption> options)
        {
            FloatMenu menu = new FloatMenu(options);
            MarkSections(options);
            Find.WindowStack.Add(menu);
            return menu;
        }

        /// <summary>
        /// Gives the first row of every section after the first its gap, walking the rows in
        /// the order the menu draws them. The menu sorts by priority first, and a disabled
        /// row always sorts last, so New allowed area at the cap drops below Manage areas,
        /// and Manage areas then opens the section. Run it after the menu's constructor has
        /// sized the rows.
        /// </summary>
        internal static void MarkSections(List<FloatMenuOption> options)
        {
            if (RequiredHeightRef == null)
            {
                return;
            }
            List<FloatMenuOption> drawn = options
                .OrderByDescending(o => o.Priority)
                .ThenByDescending(o => o.orderInPriority)
                .ToList();
            for (int i = 1; i < drawn.Count; i++)
            {
                ServesMenuOption row = drawn[i] as ServesMenuOption;
                ServesMenuOption above = drawn[i - 1] as ServesMenuOption;
                if (row == null || above == null || row.section == above.section || row.gapAbove > 0f)
                {
                    continue;
                }
                RequiredHeightRef(row) += SectionGap;
                row.gapAbove = SectionGap;
            }
        }
    }
}
