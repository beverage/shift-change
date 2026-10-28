using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// The settings window for one stand: a checkbox per visible work type, because a stand covers
    /// a set of work (a workshop runs crafting, tailoring, smithing and art).
    ///
    /// Three states, kept canonical by <see cref="CompShiftStand.ToggleWork"/>: automatic (empty
    /// override; the room's defaults apply and follow the room if its role changes), a custom set,
    /// or excluded. The checkboxes always show the effective set, so ticking one while automatic
    /// seeds the custom set from the room's defaults rather than starting from nothing.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Dialog_SetStandWorkTypes : Window
    {
        internal const float RowHeight = 26f;
        internal const float CheckboxSize = 24f;
        internal const float LabelGap = 10f;
        internal const float MaxLabelWidth = 260f;
        internal const float ColumnGutter = 36f;
        internal const float ScrollbarAllowance = 20f;
        /// <summary>
        /// Estimated height of the fixed rows above the grid (title, radios, full-change, trade,
        /// explainer, recreation and sleep rows), used by <see cref="InitialSize"/> until the body
        /// has been measured. Raise it when a fixed row is added above the grid. Err high: too much
        /// costs whitespace for one frame, too little clips a row until the body has been measured.
        /// </summary>
        internal const float HeaderAllowance = 282f;
        internal const float MinWindowWidth = 420f;

        /// <summary>
        /// Width the automatic row's label gives up on the right for its radio button, plus a gap.
        /// </summary>
        internal const float RadioButtonWidth = 28f;

        /// <summary>
        /// Vanilla's unchosen radio, which <c>Widgets</c> keeps private: the automatic row draws
        /// it, with no click target, when it cannot be chosen.
        /// </summary>
        internal static readonly Texture2D RadioButOffTex =
            ContentFinder<Texture2D>.Get("UI/Widgets/RadioButOff");

        /// <summary>Extra height the automatic label's wrap needed beyond one row.</summary>
        internal float autoLabelOverflow;

        /// <summary>Rows a column aims for before the dialog grows another column.</summary>
        internal const int TargetRows = 14;

        /// <summary>Grid rows reserved for the note that replaces an empty grid.</summary>
        internal const int EmptyNoteRows = 3;

        /// <summary>
        /// Mirrors the default of <c>Window.Margin</c> (18) instead of reading it: Margin is
        /// protected, and a hot-swapped method body in a Debug build runs on a twin type that the
        /// protected-access rule does not treat as a subclass, so reading it there throws
        /// MethodAccessException. We never override Margin, so the default is exact.
        /// </summary>
        internal const float WindowMargin = 18f;

        internal readonly CompShiftStand comp;
        internal readonly List<WorkTypeDef> works;
        internal readonly float cellWidth;
        internal readonly int columns;
        internal readonly int rowsPerColumn;
        internal Vector2 scroll;

        /// <summary>
        /// Height the body took when it last drew; zero until the first frame.
        /// <see cref="InitialSize"/> prefers it to the constants, which are only an estimate: every
        /// row is a wrapping label, so heights depend on the window width, and the window is
        /// clamped to 85% of the screen.
        /// </summary>
        internal float measuredBody;

        internal static string LabelOf(WorkTypeDef w)
        {
            return WorkTypeLabels.Cap(w);
        }

        public Dialog_SetStandWorkTypes(CompShiftStand comp)
        {
            this.comp = comp;
            doCloseX = true;
            doCloseButton = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;

            // Alphabetical by the label shown. Only types the work tab shows
            // (WorkTypeDef.visible): every vanilla type is, Patient and BasicWorker included, but
            // another mod can hide some (see the note for an empty grid in DrawBody).
            works = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .Where(w => w.visible)
                .OrderBy(LabelOf)
                .ToList();

            // The widest label sets the cell width, capped so one very long modded label cannot
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

            int wanted = Mathf.Max(1, Mathf.CeilToInt(works.Count / (float)TargetRows));
            float usableWidth = UI.screenWidth * 0.9f - WindowMargin * 2f - ScrollbarAllowance;
            int fitting = Mathf.Max(1, Mathf.FloorToInt((usableWidth + ColumnGutter) / (cellWidth + ColumnGutter)));
            columns = Mathf.Clamp(wanted, 1, fitting);
            // With no work types the grid draws a note instead of rows (see DrawBody), so reserve
            // the note's lines (it wraps at the minimum width) rather than the one row the row
            // maths gives.
            rowsPerColumn = works.Count > 0
                ? Mathf.Max(1, Mathf.CeilToInt(works.Count / (float)columns))
                : EmptyNoteRows;
        }

        /// <summary>Title plus the two mode radios: everything an excluded stand shows.</summary>
        internal const float ModeOnlyAllowance = 96f;

        /// <summary>True for an excluded stand: only the title and the two radios show.</summary>
        internal bool ModeOnly => comp.IsExcluded;

        /// <summary>
        /// Height for the two conditional rows: the deposit-only checkbox on a sleep stand, and the
        /// note under the grid for a recreation or sleep trigger. Kept out of
        /// <see cref="HeaderAllowance"/> so other stands reserve nothing for them.
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
                if (!comp.HidesWorkList && (comp.HandlesRecreation() || comp.HandlesRest()))
                {
                    // Two rows: the note wraps, and the English text takes two lines at the minimum
                    // width.
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

                float body = ModeOnly
                    ? ModeOnlyAllowance
                    : (measuredBody > 0f
                        ? measuredBody
                        : HeaderAllowance + TriggerRowsAllowance + ServesAllowance
                            + rowsPerColumn * RowHeight + autoLabelOverflow);

                float height = WindowMargin * 2f + CloseButSize.y + 10f + body;
                height = Mathf.Min(height, UI.screenHeight * 0.85f);

                return new Vector2(width, height);
            }
        }

        /// <summary>
        /// Sets the open window's height to <see cref="InitialSize"/>'s. The engine reads it only
        /// when the window opens (PreOpen) and on a resolution change, so without this a mode
        /// change leaves the window at the height it opened with.
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

            // One scroll view around the whole body, and only when it does not fit: it absorbs any
            // overflow, so no row is drawn outside the window.
            bool scrolling;
            Rect body = BodyRect(measuredBody, content, out scrolling);
            if (scrolling)
            {
                Widgets.BeginScrollView(content, ref scroll, body);
            }
            measuredBody = DrawBody(body);
            if (scrolling)
            {
                Widgets.EndScrollView();
            }
            FitToMode();
        }

        /// <summary>
        /// The rect the body listing draws into, and whether it scrolls. The listing is never
        /// handed a rect shorter than the body it has to draw.
        ///
        /// <para>Static and free of GUI calls so the harness can assert it: the harness runs from
        /// <c>Game.FinalizeInit</c>, where there is no GUI context to draw in.</para>
        /// </summary>
        internal static Rect BodyRect(float measuredBody, Rect content, out bool scrolling)
        {
            scrolling = measuredBody > content.height + 1f;
            return scrolling
                ? new Rect(0f, 0f, content.width - ScrollbarAllowance, measuredBody)
                : content;
        }

        /// <summary>Draws every control; returns the height used, which sizes the window.</summary>
        internal float DrawBody(Rect content)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(content);

            // Never let the listing wrap. Listing.GetRect calls NewColumnIfNeeded on every row,
            // which starts a second column at curX + ColumnWidth + 17 as soon as a row does not
            // fit. This listing is full width, so that column begins outside the window and every
            // later row is clipped away. Starting a column also resets curY to 0, and CurHeight
            // reads curY, so a wrapped listing reports the last column's height, not the total, and
            // the measured body comes back too small.
            listing.maxOneColumn = true;

            Text.Font = GameFont.Medium;
            listing.Label("ShiftChange.WorkTypesTitle".Translate());
            Text.Font = GameFont.Small;
            listing.Gap(4f);

            RoomRoleDef roomRole = comp.parent.Spawned ? comp.parent.GetRoom()?.Role : null;
            // The comp's AutomaticWorkTypes, not RoomWorkTypes.ForRole: automatic work also comes
            // from the room's contents (RoomContentsWork), not only its role. Not HandlesWork or
            // HandlesRecreation either: those answer for the current mode, and this label shows
            // what automatic would give even while a custom set is in force.
            List<WorkTypeDef> roomDefaults = comp.parent.Spawned
                ? comp.AutomaticWorkTypes
                : CompShiftStand.NoWork;
            List<string> autoParts = roomDefaults
                .Select(w => WorkTypeLabels.Of(w)).ToList();
            // Work wins, as in HandlesRecreation and HandlesRest: mirror that guard or the label
            // would promise a trigger the stand will not fire.
            bool autoTriggers = roomDefaults.Count == 0;
            if (autoTriggers && RoomWorkTypes.RecreationForRole(roomRole))
            {
                autoParts.Add("ShiftChange.Recreation".Translate().RawText);
            }
            if (autoTriggers && RoomWorkTypes.RestForRole(roomRole))
            {
                autoParts.Add("ShiftChange.Rest".Translate().RawText);
            }
            string autoLabel = "ShiftChange.WorkTypeAuto".Translate();
            autoLabel += ": " + (autoParts.Count > 0
                ? autoParts.ToCommaList()
                : "ShiftChange.None".Translate().RawText);
            if (roomDefaults.Count == 0)
            {
                // Name the role the game inferred, so "nothing" does not read as a detection
                // failure: the room-role scorer has quirks a player cannot guess (a humanlike
                // prisoner bed in or next to the room zeroes its Hospital score when
                // RoomRoleWorker_Hospital.GetScore meets it before any medical bed).
                autoLabel += "; " + (roomRole != null
                    ? (string)"ShiftChange.RoomReadsAs".Translate(roomRole.label)
                    : (string)"ShiftChange.NoRoomRole".Translate());
            }

            // Measure the label rather than assume one row: it can wrap, and a wrapped line is
            // clipped at a fixed RowHeight.
            float autoLabelWidth = content.width - RadioButtonWidth;
            float autoRowHeight = Mathf.Max(RowHeight, Text.CalcHeight(autoLabel, autoLabelWidth));
            autoLabelOverflow = autoRowHeight - RowHeight;
            // Not Widgets.RadioButtonLabeled: it draws the label across the whole rect and then
            // paints the button over it at xMax - 24, reserving no width. A wrapping label would
            // run under the button and lose its last characters, so the label and the button are
            // composed separately.
            Rect autoRect = listing.GetRect(autoRowHeight);
            // Automatic reads the room, so it only works in room mode.
            bool autoAvailable = comp.serves == CatchmentKind.Room;
            if (!autoAvailable)
            {
                GUI.color = Color.gray;
            }
            Widgets.Label(new Rect(autoRect.x, autoRect.y,
                                   autoRect.width - RadioButtonWidth, autoRect.height),
                          autoLabel);
            float radioX = autoRect.xMax - 24f;
            float radioY = autoRect.y + autoRect.height / 2f - 12f;
            bool autoClicked = false;
            if (autoAvailable)
            {
                autoClicked = Widgets.RadioButton(radioX, radioY, comp.IsAutomatic)
                              | Widgets.ButtonInvisible(autoRect);
            }
            else
            {
                // Drawn in grey with no click target. Widgets.RadioButton plays its click on any
                // click of an unchosen button, its disabled flag only greys it, and 1.7's GUI
                // ignores GUI.enabled, so calling it here would tick on a row that refuses.
                GUI.DrawTexture(new Rect(radioX, radioY, 24f, 24f),
                                comp.IsAutomatic ? Widgets.RadioButOnTex : RadioButOffTex);
            }
            GUI.color = Color.white;
            if (autoClicked)
            {
                comp.SetAutomatic();
            }
            if (!autoAvailable)
            {
                TooltipHandler.TipRegion(autoRect, "ShiftChange.AutomaticRoomOnly".Translate());
            }
            if (Widgets.RadioButtonLabeled(listing.GetRect(RowHeight),
                    "ShiftChange.WorkTypeNone".Translate(), comp.IsExcluded))
            {
                comp.SetExcluded();
            }

            // An excluded stand shows nothing below the radios. The rest of this window configures
            // shift changes, which it does not do, and a disabled control would still advertise a
            // setting it lacks.
            if (ModeOnly)
            {
                float modeOnlyHeight = listing.CurHeight;
                listing.End();
                return modeOnlyHeight;
            }

            listing.GapLine();

            // Full change is a property of what the rack holds, so it sits outside the mode radios.
            // Under deposit-only it is disabled, not hidden (a deposit-only stand issues no outfit,
            // so SwapPlan returns from its deposit branch before the full-change pass): a hidden
            // row would shift every row below it, and a double-click landing on the shifted
            // Sleeping box would turn the stand off, because ToggleRest on an already-on trigger
            // falls through to SetExcluded.
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

            // Inside full change's separator. The ModeOnly return above hides this row for exactly
            // the stands where CompShiftStand.BlocksTrade stops applying: one condition, not two.
            bool withhold = comp.WithholdFromTrade;
            Rect tradeRect = listing.GetRect(RowHeight);
            Widgets.CheckboxLabeled(tradeRect, "ShiftChange.WithholdFromTrade".Translate(), ref withhold);
            if (withhold != comp.WithholdFromTrade)
            {
                comp.SetWithholdFromTrade(withhold);
            }
            TooltipHandler.TipRegion(tradeRect, "ShiftChange.WithholdFromTradeDesc".Translate());

            listing.GapLine();
            listing.Label(comp.ByCatchment("ShiftChange.WorkTypesExplainer").Translate());
            listing.Gap(4f);

            // Recreation is one trigger, not a work type: the room is the selector, so one row
            // covers every joy activity. It sits above the grid because it is not a WorkTypeDef.
            bool recreationOn = comp.HandlesRecreation();
            bool recreationWas = recreationOn;
            Widgets.CheckboxLabeled(listing.GetRect(RowHeight),
                comp.ByCatchment("ShiftChange.RecreationRow").Translate(), ref recreationOn);
            if (recreationOn != recreationWas)
            {
                comp.ToggleRecreation();
            }

            // Sleep is the third trigger, shaped like recreation: the room is the selector. Medical
            // bed rest is not this row: it is a real work type (vanilla's PatientBedRest, labelled
            // "resting in bed") and stays in the grid, so a hospital stand and a pyjama stand are
            // configured separately.
            bool restOn = comp.HandlesRest();
            bool restWas = restOn;
            Widgets.CheckboxLabeled(listing.GetRect(RowHeight),
                comp.ByCatchment("ShiftChange.RestRow").Translate(), ref restOn);
            if (restOn != restWas)
            {
                comp.ToggleRest();
            }
            listing.Gap(4f);

            // Deposit-only belongs to whichever stand serves sleep, automatic or explicit, and to
            // no other trigger: parking gear without taking any on is only coherent for sleep. The
            // row stays with the trigger rows it controls, though ticking a work type removes it.
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

            // An explicit trigger the room would not suggest hides the work list (HidesWorkList):
            // ticking a work type would silently drop it, so the list is hidden, not greyed, since
            // a list that looks interactable and does that is a trap. An automatic stand's
            // trigger, or a ticked one the room suggests anyway, keeps the list, with the note
            // below the grid saying that ticking a work type replaces it. The test runs after the
            // toggles above, so the list vanishes on the frame a box is ticked.
            if (comp.HidesWorkList)
            {
                GUI.color = Color.gray;
                listing.Label(comp.HandlesRest()
                    ? "ShiftChange.RestExclusiveNote".Translate()
                    : "ShiftChange.RecreationExclusiveNote".Translate());
                GUI.color = Color.white;
                DrawServes(listing);
                float exclusiveHeight = listing.CurHeight;
                listing.End();
                return exclusiveHeight;
            }
            bool roomTrigger = comp.HandlesRecreation() || comp.HandlesRest();

            // The grid takes the height it needs, not what is left of the window; the body's scroll
            // view absorbs any overflow.
            float labelWidth = cellWidth - CheckboxSize;
            Rect grid = listing.GetRect(works.Count > 0
                ? rowsPerColumn * RowHeight
                : EmptyNoteRows * RowHeight);

            // An empty grid has to say so. Another mod can empty the list: Personal Work Categories
            // (densevoid.hui.personalworkcat) sets WorkTypeDef.visible false for every category its
            // selected preset leaves without a work giver, before this code or the work tab reads
            // the database. A blank box would read as this dialog being broken, so the note says
            // the same types are missing from the work tab too.
            if (works.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(grid, "ShiftChange.NoWorkTypes".Translate());
                GUI.color = Color.white;
            }
            else
            {
                for (int i = 0; i < works.Count; i++)
                {
                    WorkTypeDef work = works[i];
                    Rect cell = new Rect(
                        grid.x + i / rowsPerColumn * (cellWidth + ColumnGutter),
                        grid.y + i % rowsPerColumn * RowHeight,
                        cellWidth,
                        RowHeight - 2f);
                    bool on = comp.HandlesWork(work);
                    bool was = on;
                    // Vanilla's CheckboxLabeled puts the box at the cell's right edge; with the
                    // cell sized to the widest label, the boxes form one column just past the text.
                    Widgets.CheckboxLabeled(cell, LabelOf(work).Truncate(labelWidth - 4f), ref on);
                    if (on != was)
                    {
                        comp.ToggleWork(work);
                    }
                }
            }

            // The trigger note goes below the grid, not above it: ticking a work type removes the
            // note and the deposit-only row, and above the grid that would shift every cell up
            // under the cursor. It shows for an automatic stand's trigger and for an explicit one
            // the room would suggest anyway, and says what ticking a work type does.
            if (roomTrigger)
            {
                GUI.color = Color.gray;
                listing.Label(comp.IsAutomatic
                    ? "ShiftChange.TriggerFromRoomNote".Translate()
                    : "ShiftChange.TriggerTickedNote".Translate());
                GUI.color = Color.white;
            }

            DrawServes(listing);
            float bodyHeight = listing.CurHeight;
            listing.End();
            return bodyHeight;
        }

        /// <summary>
        /// Estimated height of the Serves row for <see cref="InitialSize"/>: a separator, the row,
        /// and in radius mode the slider.
        /// </summary>
        internal float ServesAllowance =>
            12f + RowHeight + (comp.serves == CatchmentKind.Radius ? RowHeight + 8f : 0f);

        /// <summary>The Paint button's width, at the end of the Serves row in area mode.</summary>
        internal const float PaintButtonWidth = 90f;

        /// <summary>
        /// Where the stand serves: the row, with a slider in radius mode and the Paint button in
        /// area mode. Last in the window, so nothing above it moves when the slider appears
        /// (docs/DESIGN.md, "Catchments").
        /// </summary>
        internal void DrawServes(Listing_Standard listing)
        {
            listing.GapLine();
            bool canChoose = comp.ServesSomething;
            Rect row = listing.GetRect(RowHeight);
            if (comp.serves == CatchmentKind.Area)
            {
                DrawPaint(row.RightPartPixels(PaintButtonWidth));
                row.width -= PaintButtonWidth + 6f;
            }
            // ButtonText's active flag only refuses the click and draws a live-looking button; the
            // tint is what greys it.
            if (!canChoose)
            {
                GUI.color = Widgets.InactiveColor;
            }
            // Cut to fit, with the whole row first in the tooltip: area names run to 28 characters,
            // and a label too wide for ButtonText is drawn centred and clipped at both ends.
            string label = "ShiftChange.ServesRow".Translate(comp.ServesLabel());
            string shown = label.Truncate(row.width - 10f);
            bool clicked = Widgets.ButtonText(row, shown, active: canChoose);
            GUI.color = Color.white;
            if (clicked && canChoose)
            {
                ServesMenuOption.Open(ServesMenuOptions());
            }
            string tip = canChoose
                ? "ShiftChange.ServesDesc".Translate()
                : "ShiftChange.ServesNeedsTrigger".Translate();
            switch (comp.ServeFault)
            {
                case CatchmentFault.InTheOpen:
                    tip += "\n\n" + "ShiftChange.ServesNothingOutdoorsDesc".Translate();
                    break;
                case CatchmentFault.AreaGone:
                    tip += "\n\n" + "ShiftChange.ServesAreaGoneDesc".Translate();
                    break;
                case CatchmentFault.OutsideArea:
                    tip += "\n\n" + "ShiftChange.ServesAreaWithoutStandDesc".Translate();
                    break;
            }
            if (shown != label)
            {
                tip = label + "\n\n" + tip;
            }
            TooltipHandler.TipRegion(row, tip);

            if (comp.serves == CatchmentKind.Radius)
            {
                int radius = Mathf.RoundToInt(listing.SliderLabeled(
                    "ShiftChange.ServeRadius".Translate(comp.serveRadius),
                    comp.serveRadius, CompShiftStand.MinServeRadius, CompShiftStand.MaxServeRadius));
                if (radius != comp.serveRadius)
                {
                    comp.SetServeRadius(radius);
                }
            }
        }

        /// <summary>
        /// The Paint button, greyed while the stand's area is gone. Binding and painting stay
        /// separate steps: picking an area in the menu never starts a tool.
        /// </summary>
        internal void DrawPaint(Rect rect)
        {
            Area area = comp.LiveServeArea();
            if (area == null)
            {
                GUI.color = Widgets.InactiveColor;
            }
            bool clicked = Widgets.ButtonText(rect, "ShiftChange.ServesPaint".Translate(), active: area != null);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(rect, area != null
                ? "ShiftChange.ServesPaintDesc".Translate(area.Label)
                : "ShiftChange.ServesPaintGone".Translate(comp.serveAreaLabel));
            if (clicked && area != null)
            {
                Paint(area);
            }
        }

        /// <summary>
        /// The Serves menu, in three sections (<see cref="ServesMenuOption"/>): this room and a
        /// radius; every allowed area except Home, each with its colour; then New allowed area and
        /// Manage areas. An area that leaves out the stand's own cell is faded and says why in red,
        /// since a stand outside its area serves nothing. Built apart from the click so the harness
        /// can read and pick the options. It is not vanilla's
        /// <c>AreaUtility.MakeAllowedAreaListFloatMenu</c>, which lists Home, opens the menu
        /// itself, has no swatches, fading or sections, and leaves this window open behind Manage
        /// areas.
        /// </summary>
        internal List<FloatMenuOption> ServesMenuOptions()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new ServesMenuOption(ServesMenuOption.SectionWhere, "ShiftChange.ServesOptionRoom".Translate(),
                                     () => comp.SetServes(CatchmentKind.Room)),
                new ServesMenuOption(ServesMenuOption.SectionWhere, "ShiftChange.ServesOptionRadius".Translate(),
                                     () => comp.SetServes(CatchmentKind.Radius)),
            };
            Map map = comp.parent.Spawned ? comp.parent.Map : null;
            if (map == null)
            {
                return options;
            }
            IntVec3 standCell = comp.parent.Position;
            List<Area> areas = map.areaManager.AllAreas;
            for (int i = 0; i < areas.Count; i++)
            {
                Area area = areas[i];
                // Home stays out of the menu for now, though the binding
                // would take it (docs/DESIGN.md, "Catchments").
                if (!area.AssignableAsAllowed() || area is Area_Home)
                {
                    continue;
                }
                bool holdsStand = area[standCell];
                options.Add(new ServesMenuOption(ServesMenuOption.SectionAreas,
                                                 holdsStand ? area.Label : AreaLeavingStandOut(area.Label),
                                                 () => comp.SetServeArea(area),
                                                 area.ColorTexture, holdsStand ? Color.white : FadedSwatch,
                                                 _ => area.MarkForDraw()));
            }
            // Greyed at the cap rather than hidden, with vanilla's reason. A disabled float-menu
            // option sorts last, below Manage areas, which is where the player goes to make room.
            ServesMenuOption make = new ServesMenuOption(ServesMenuOption.SectionManage,
                                                         "ShiftChange.ServesOptionNewArea".Translate(),
                                                         () => comp.MakeServeArea());
            if (!map.areaManager.CanMakeNewAllowed())
            {
                make.Disabled = true;
                make.tooltip = new TipSignal("MaxAreasReached".Translate(AreaManager.MaxAllowedAreas));
            }
            options.Add(make);
            options.Add(new ServesMenuOption(ServesMenuOption.SectionManage, "ManageAreas".Translate(),
                                             () => OpenManageAreas(map)));
            return options;
        }

        /// <summary>A faded swatch, for an area that leaves the stand out.</summary>
        internal static readonly Color FadedSwatch = new Color(1f, 1f, 1f, 0.35f);

        /// <summary>
        /// How the menu lists an area that leaves out the stand's own cell: its name faded and the
        /// rest in red, the Serves row's own words for a stand outside its area. Rich-text colour
        /// tags, as vanilla's labels use.
        ///
        /// <para><c>Resolve</c>, never a cast: <c>TaggedString</c>'s implicit conversion to string
        /// strips every tag (<c>StripTags</c>), which would turn the faded name red with the
        /// rest.</para>
        /// </summary>
        internal static string AreaLeavingStandOut(string name)
        {
            return "ShiftChange.ServesAreaStandOutside".Translate(name.Colorize(ColoredText.SubtleGrayColor))
                .Resolve()
                .Colorize(ColorLibrary.RedReadable);
        }

        /// <summary>
        /// Starts vanilla's Expand allowed area tool on <paramref name="area"/> the way Manage
        /// areas does (<c>Dialog_ManageAreas.SelectDesignator</c>): the tool paints whichever area
        /// its public static names. This window closes first: it absorbs clicks around itself, so
        /// left open it would swallow every stroke of the brush.
        /// </summary>
        internal void Paint(Area area)
        {
            Close();
            Find.MainTabsRoot.EscapeCurrentTab(playSound: false);
            Designator_AreaAllowed.selectedArea = area;
            Designator expand = DesignationCategoryDefOf.Zone.AllResolvedDesignators
                .OfType<Designator_AreaAllowedExpand>().FirstOrDefault()
                ?? new Designator_AreaAllowedExpand();
            Find.DesignatorManager.Select(expand);
        }

        /// <summary>
        /// Opens vanilla's Manage areas, closing this window first: the paint buttons there close
        /// only their own dialog, and this one, still open behind it, would swallow the strokes
        /// (see <see cref="Paint"/>).
        /// </summary>
        internal void OpenManageAreas(Map map)
        {
            Close();
            Find.WindowStack.Add(new Dialog_ManageAreas(map));
        }

        /// <summary>The stand's ring or area, drawn on the map while the window is open.</summary>
        public override void WindowUpdate()
        {
            base.WindowUpdate();
            comp.DrawCatchment();
        }
    }
}
