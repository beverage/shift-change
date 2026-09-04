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
        internal const float RowHeight = 26f;
        internal const float CheckboxSize = 24f;
        internal const float LabelGap = 10f;
        internal const float MaxLabelWidth = 260f;
        internal const float ColumnGutter = 36f;
        internal const float ScrollbarAllowance = 20f;
        /// <summary>
        /// Everything above the scrolling work-type grid: title, the two mode
        /// radios, the full-change row, the explainer and the recreation row.
        /// Grow this when a row is added above the grid, or the grid silently
        /// loses the height instead — <see cref="DoWindowContents"/> hands it
        /// whatever is left.
        ///
        /// <para>160 was title + radios + explainer. The full-change row and
        /// the recreation row each cost a row plus its separator, and both
        /// landed in the same merge — hence 226 rather than either branch's
        /// own figure. The withhold-from-trade row added one bare
        /// <see cref="RowHeight"/> on top of that: it shares full change's
        /// separator instead of taking one of its own. The sleep row and its
        /// gap took it to 282.</para>
        ///
        /// <para>This is the MAXIMUM, not the exact figure for every state.
        /// The deposit-only row and the hidden full-change row that goes with
        /// it are both inside the trigger-exclusive branch, which draws no
        /// work grid at all — so they spend slack the grid has already given
        /// up. Over-reserving costs whitespace at the bottom of one layout;
        /// under-reserving clips a control, which is why the const tracks the
        /// tallest arrangement rather than the common one.</para>
        /// </summary>
        internal const float HeaderAllowance = 282f;
        internal const float MinWindowWidth = 420f;

        /// <summary>
        /// What <c>Widgets.RadioButtonLabeled</c> reserves on the right for the
        /// button itself (<c>Widgets.RadioButtonSize</c> plus its gap), so the
        /// label height can be measured against the width it will really get.
        /// </summary>
        internal const float RadioButtonWidth = 28f;

        /// <summary>
        /// Extra height the automatic label needed beyond one row, measured in
        /// <see cref="DoWindowContents"/> and consumed by
        /// <see cref="InitialSize"/>. Zero until the first frame draws, which is
        /// harmless: <see cref="FitToMode"/> re-fits the window every frame.
        /// </summary>
        internal float autoLabelOverflow;

        /// <summary>Rows a column aims for before the dialog grows another column.</summary>
        internal const int TargetRows = 14;

        /// <summary>
        /// Mirror of <c>Verse.Window.Margin</c>'s default (18f,
        /// <c>Window.cs:104</c>), NOT a read of it: Margin is a protected base
        /// member, and hot-swapped bodies live on a twin type that the
        /// protected-access rule does not recognise as a subclass — reading it
        /// post-swap throws MethodAccessException (found in play 2026-08-08).
        /// We never override Margin, so the default is exact.
        /// </summary>
        internal const float WindowMargin = 18f;

        internal readonly CompShiftStand comp;
        internal readonly List<WorkTypeDef> works;
        internal readonly float cellWidth;
        internal readonly int columns;
        internal readonly int rowsPerColumn;
        internal Vector2 scroll;

        internal static string LabelOf(WorkTypeDef w)
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
            float usableWidth = UI.screenWidth * 0.9f - WindowMargin * 2f - ScrollbarAllowance;
            int fitting = Mathf.Max(1, Mathf.FloorToInt((usableWidth + ColumnGutter) / (cellWidth + ColumnGutter)));
            columns = Mathf.Clamp(wanted, 1, fitting);
            rowsPerColumn = Mathf.Max(1, Mathf.CeilToInt(works.Count / (float)columns));
        }

        /// <summary>
        /// Title plus the two mode radios — everything an EXCLUDED stand has.
        /// </summary>
        internal const float ModeOnlyAllowance = 96f;

        /// <summary>
        /// A stand excluded from shift changes has no shift-change settings, so
        /// the window holds nothing but the two radios that let you bring it
        /// back. Deliberately not "greyed out": a disabled control still
        /// advertises a setting, and this stand does not have one.
        /// </summary>
        internal bool ModeOnly => comp.IsExcluded;

        /// <summary>
        /// Height for the two rows that only some stands draw: the
        /// deposit-only checkbox on a sleep stand, and the note explaining
        /// that an AUTOMATIC stand's trigger came from the room.
        ///
        /// <para>Computed rather than folded into
        /// <see cref="HeaderAllowance"/> because both are conditional and the
        /// constant is spent on every stand in the colony — reserving 60px
        /// unconditionally would put a band of dead space under the work grid
        /// of every ordinary workshop stand. <see cref="FitToMode"/> re-reads
        /// this every frame, so a row appearing the moment its box is ticked
        /// grows the window on the same frame.</para>
        /// </summary>
        internal float TriggerRowsAllowance
        {
            get
            {
                if (ModeOnly)
                {
                    return 0f;
                }
                float extra = 0f;
                if (comp.HandlesRest())
                {
                    extra += RowHeight + 4f;
                }
                if (!comp.IsTriggerOverridden && (comp.HandlesRecreation() || comp.HandlesRest()))
                {
                    // TWO rows, not one. The note is drawn with
                    // Listing_Standard.Label, which wraps, and the English
                    // string takes two lines at the clamped minimum width — a
                    // single RowHeight here let it overrun the grid.
                    extra += RowHeight * 2f + 4f;
                }
                return extra;
            }
        }

        public override Vector2 InitialSize
        {
            get
            {
                float width = WindowMargin * 2f + columns * cellWidth
                    + (columns - 1) * ColumnGutter + ScrollbarAllowance;
                width = Mathf.Clamp(width, MinWindowWidth, UI.screenWidth * 0.9f);

                float height = WindowMargin * 2f + CloseButSize.y + 10f + autoLabelOverflow
                    + (ModeOnly
                        ? ModeOnlyAllowance
                        : HeaderAllowance + TriggerRowsAllowance + rowsPerColumn * RowHeight);
                height = Mathf.Min(height, UI.screenHeight * 0.85f);

                return new Vector2(width, height);
            }
        }

        /// <summary>
        /// Toggling the mode while the dialog is OPEN has to resize it — the
        /// engine reads <see cref="InitialSize"/> once, in PreOpen
        /// (<c>Window.cs</c>), so without this the window keeps whatever height
        /// it opened at and the collapse leaves a tall empty box.
        /// </summary>
        internal void FitToMode()
        {
            float wanted = InitialSize.y;
            if (!Mathf.Approximately(windowRect.height, wanted))
            {
                windowRect.height = wanted;
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
            // resolves to — BOTH halves, works and recreation — so
            // "automatic" is never a mystery box.
            RoomRoleDef roomRole = comp.parent.Spawned ? comp.parent.GetRoom()?.Role : null;
            List<WorkTypeDef> roomDefaults = RoomWorkTypes.ForRole(roomRole);
            List<string> autoParts = roomDefaults
                .Select(w => (string)(w.gerundLabel ?? w.defName)).ToList();
            if (RoomWorkTypes.RecreationForRole(roomRole))
            {
                autoParts.Add("ShiftChange.Recreation".Translate().RawText);
            }
            if (RoomWorkTypes.RestForRole(roomRole))
            {
                autoParts.Add("ShiftChange.Rest".Translate().RawText);
            }
            string autoLabel = "ShiftChange.WorkTypeAuto".Translate();
            autoLabel += ": " + (autoParts.Count > 0
                ? autoParts.ToCommaList()
                : "ShiftChange.None".Translate().RawText);
            if (roomDefaults.Count == 0)
            {
                // Name what the game itself thinks this room is, so "nothing"
                // stops reading as a detection failure. The room-role scorer
                // has quirks no player can guess from here — one prisoner bed
                // in or adjacent to a hospital zeroes its Hospital score
                // outright (RoomRoleWorker_Hospital.GetScore) — and this
                // label is where they can at least see WHICH role won.
                autoLabel += "; " + (roomRole != null
                    ? (string)"ShiftChange.RoomReadsAs".Translate(roomRole.label)
                    : (string)"ShiftChange.NoRoomRole".Translate());
            }

            // MEASURE the automatic label, do not assume one row. It names the
            // room's resolved activities AND, when there are none, the role the
            // game actually inferred — which on a two-column dialog wraps to a
            // second line and gets clipped at a fixed RowHeight (the top line
            // vanishes, so the row reads as a truncated fragment). Vanilla's
            // radio draws its button on the right, so the label owns everything
            // but that.
            float autoLabelWidth = content.width - RadioButtonWidth;
            float autoRowHeight = Mathf.Max(RowHeight, Text.CalcHeight(autoLabel, autoLabelWidth));
            // Fed back into InitialSize so the window grows by however much the
            // wrap cost; FitToMode below applies it on the same frame.
            autoLabelOverflow = autoRowHeight - RowHeight;
            // NOT Widgets.RadioButtonLabeled for this row. That helper draws the
            // label across the FULL rect (Widgets.cs:1405) and then paints the
            // button on top at rect.xMax - 24 (:1416) — it reserves nothing. A
            // one-line label is unaffected because it simply ends before the
            // button, but this one wraps, and the first line then runs
            // underneath it and loses its last few characters. Compose the two
            // halves instead so the label owns a rect the button is not in.
            Rect autoRect = listing.GetRect(autoRowHeight);
            Widgets.Label(new Rect(autoRect.x, autoRect.y,
                                   autoRect.width - RadioButtonWidth, autoRect.height),
                          autoLabel);
            if (Widgets.RadioButton(autoRect.xMax - 24f,
                                    autoRect.y + autoRect.height / 2f - 12f,
                                    comp.IsAutomatic)
                | Widgets.ButtonInvisible(autoRect))
            {
                comp.SetAutomatic();
            }
            if (Widgets.RadioButtonLabeled(listing.GetRect(RowHeight),
                    "ShiftChange.WorkTypeNone".Translate(), comp.IsExcluded))
            {
                comp.SetExcluded();
            }

            // NOTHING BELOW HERE ON AN EXCLUDED STAND. The rest of this window
            // configures shift changes, and this stand does not do them — so it
            // has no full-change setting, no trigger, and no work types, and
            // showing them disabled would claim otherwise. The two radios above
            // are the whole control surface until one of them brings it back.
            if (ModeOnly)
            {
                listing.End();
                FitToMode();
                return;
            }
            FitToMode();

            listing.GapLine();

            // Full change is a property of what the rack HOLDS, not of which
            // work it serves, so it sits outside the mode radios rather than
            // becoming a fourth mode.
            // DISABLED under deposit-only, not hidden. A deposit-only stand
            // issues no outfit, so SwapPlan's deposit branch returns before the
            // full-change pass can run and the setting genuinely does nothing —
            // but hiding the row moved every row below it by 26px on the frame
            // the box was ticked, which put the Sleeping checkbox under the
            // cursor mid-double-click. ToggleRest on an already-on trigger
            // falls through to SetExcluded, so one stray click turned the stand
            // off entirely and collapsed the window (adversarial review,
            // 2026-09-03). A greyed row states the same fact and cannot move
            // anything.
            bool full = comp.FullChange;
            bool fullInert = comp.DepositOnly;
            Rect fullRect = listing.GetRect(RowHeight);
            Widgets.CheckboxLabeled(fullRect, "ShiftChange.FullChange".Translate(), ref full, fullInert);
            if (!fullInert && full != comp.FullChange)
            {
                comp.SetFullChange(full);
            }
            TooltipHandler.TipRegion(fullRect, fullInert
                ? "ShiftChange.FullChangeDepositNote".Translate()
                : "ShiftChange.FullChangeDesc".Translate());

            // Directly under full change and inside its separator, because both
            // are properties of what the rack HOLDS rather than of which work it
            // serves. The ModeOnly return above hides the row for an excluded
            // stand, which is also exactly where CompShiftStand.BlocksTrade
            // stops applying — one condition, not two that can drift apart.
            bool withhold = comp.WithholdFromTrade;
            Rect tradeRect = listing.GetRect(RowHeight);
            Widgets.CheckboxLabeled(tradeRect, "ShiftChange.WithholdFromTrade".Translate(), ref withhold);
            if (withhold != comp.WithholdFromTrade)
            {
                comp.SetWithholdFromTrade(withhold);
            }
            TooltipHandler.TipRegion(tradeRect, "ShiftChange.WithholdFromTradeDesc".Translate());

            listing.GapLine();
            listing.Label("ShiftChange.WorkTypesExplainer".Translate());
            listing.Gap(4f);

            // Recreation is one trigger, not a work type — the room is the
            // selector (a robe stand serves the sauna by standing in it), so
            // a single row covers every joy activity. Above the work
            // grid rather than inside it: it is not a WorkTypeDef and must
            // not look like a modded one.
            bool recreationOn = comp.HandlesRecreation();
            bool recreationWas = recreationOn;
            Widgets.CheckboxLabeled(listing.GetRect(RowHeight),
                "ShiftChange.RecreationRow".Translate(), ref recreationOn);
            if (recreationOn != recreationWas)
            {
                comp.ToggleRecreation();
            }

            // Sleep is the third trigger and takes the same shape for the same
            // reason: the room is the selector, so one row covers going to bed
            // however the pawn got there.
            //
            // Medical bed rest is deliberately NOT this row. It is a real
            // WorkTypeDef — vanilla's PatientBedRest, whose gerund label reads
            // "resting in bed" — and it stays down in the grid, so a hospital
            // stand and a pyjama stand remain separately configurable and
            // neither one silently claims the other's jobs.
            bool restOn = comp.HandlesRest();
            bool restWas = restOn;
            Widgets.CheckboxLabeled(listing.GetRect(RowHeight),
                "ShiftChange.RestRow".Translate(), ref restOn);
            if (restOn != restWas)
            {
                comp.ToggleRest();
            }
            listing.Gap(4f);

            // All three triggers are mutually exclusive (principal,
            // 2026-08-16: one stand, one outfit, one purpose) — while
            // recreation or sleep is on the work grid is HIDDEN rather than
            // greyed: an interactable-looking list that would silently untick
            // the trigger is a trap; an absent one is a statement. Re-queried
            // after the toggles above so the grid vanishes the same frame a
            // box is ticked.
            // The deposit-only row belongs to whichever stand currently serves
            // sleep, automatic or explicit. Only under sleep, because this is
            // the only trigger where parking gear without taking any on is a
            // coherent thing to want — the same reason
            // CompShiftStand.DepositOnly gates itself on HandlesRest rather
            // than trusting this row to be its only writer.
            if (comp.HandlesRest())
            {
                bool deposit = comp.DepositOnly;
                Rect depositRect = listing.GetRect(RowHeight);
                Widgets.CheckboxLabeled(depositRect,
                    "ShiftChange.DepositOnly".Translate(), ref deposit);
                if (deposit != comp.DepositOnly)
                {
                    comp.SetDepositOnly(deposit);
                }
                TooltipHandler.TipRegion(depositRect, "ShiftChange.DepositOnlyDesc".Translate());
                listing.Gap(4f);
            }

            // The grid is hidden only when the player EXPLICITLY chose a
            // trigger. It used to be hidden whenever one was active, and since
            // a stand in a bedroom picks up sleep from the room's role, that
            // left every bedroom stand with no reachable path to a work type:
            // grid hidden, unticking Sleeping fell through to SetExcluded and
            // collapsed the window, and Automatic put the room's default back
            // (adversarial review, 2026-09-03). An automatic stand's trigger is
            // the room's suggestion, and ticking a work type is how a player
            // overrides it — ToggleWork clears the other half, so the exclusive
            // switch still happens, it is just reachable now.
            if (comp.IsTriggerOverridden)
            {
                GUI.color = Color.gray;
                listing.Label(comp.HandlesRest()
                    ? "ShiftChange.RestExclusiveNote".Translate()
                    : "ShiftChange.RecreationExclusiveNote".Translate());
                GUI.color = Color.white;
                listing.End();
                return;
            }
            // The from-room note is drawn BELOW the grid, not above it. Ticking
            // a work type overrides the room's trigger, which correctly makes
            // both this note and the deposit-only row vanish — and above the
            // grid that shortened the header by ~80px on the next frame, moving
            // every cell up three rows under a cursor that was mid-click. Below
            // it, the note can appear and disappear without the grid ever
            // moving. The deposit row cannot go here: it is a control for the
            // trigger rows it sits under, so it keeps its place and its 30px
            // of movement.
            bool roomTrigger = comp.HandlesRecreation() || comp.HandlesRest();

            // Reserve the note's own height before the grid claims what is
            // left, or drawing it after EndScrollView would run off the bottom.
            // Measured, not assumed: the string wraps to two lines at this
            // dialog's clamped minimum width.
            float noteHeight = roomTrigger
                ? Text.CalcHeight("ShiftChange.TriggerFromRoomNote".Translate(), content.width) + 4f
                : 0f;
            Rect outRect = listing.GetRect(content.height - listing.CurHeight - noteHeight);
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

            if (roomTrigger)
            {
                GUI.color = Color.gray;
                listing.Label("ShiftChange.TriggerFromRoomNote".Translate());
                GUI.color = Color.white;
            }

            listing.End();
        }
    }
}
