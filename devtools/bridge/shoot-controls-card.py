#!/usr/bin/env python3
"""Shoot and composite card-controls: the work-types dialog in two modes,
plus the owner list.

    devtools/run-scene.sh --media --bridge
    devtools/bridge/shoot-controls-card.py 5176 shiftchange-scene-bridge

WHY THIS IS AUTOMATED AND THE OLD ONE WAS NOT. The card was three hand-cropped
screengrabs composited by hand, and every crop offset had to be re-measured per
capture because a screengrab carries 3-6 px of game world outside the window's
own border. Two things make that unnecessary now: `take_screenshot` accepts a
`clipTargetId` and crops to the window itself, so there is no border to find;
and driving one stand through both modes guarantees the two dialog panels are
the SAME window, which is the card's whole premise and what keeps their heights
identical.

The hand-assembled version drifted the moment the dialog grew a row — twice, in
fact: once for "Keep contents out of trade" and once for the sleep rows. This
version is re-runnable, so the next row costs one command.

THE PANELS MUST SHARE A UI SCALE. Panels captured at 1.0x and 1.25x cannot be
composited: cropped to their borders the same dialog is 420 px wide at one and
525 at the other, and the larger simply dominates. Shooting all three from one
instance makes that impossible to get wrong, which a mix of old and new
screengrabs did not.
"""
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gabp import connect  # noqa: E402

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                     "..", ".."))

STAGE = "Actions\\Dev tools...\\Build demo stage"
ORIGIN = (120, 120)

# DebugTools_DemoStage: hospital is RoomRect(origin, left, top) =
# (origin.x + 1, origin.z + RoomInterior + CorridorHeight + 3), and SpawnStand
# puts the stand at that rect's (minX, minZ) when the door is south.
STAND = (ORIGIN[0] + 1, ORIGIN[1] + 10)

# The canvas is DERIVED from the panels, not fixed at the old 2000x900. That
# number was chosen for panels captured at 1.25x UI scale (525 + 525 + 699 =
# 1749 px of panel); at 1.0x the same three total 1400 and the old canvas
# leaves 600 px of dead background. Deriving it means a dialog that grows a row
# re-spaces the card instead of drifting inside a frame sized for something
# else.
CARD_GAP = 75
CARD_MARGIN_Y = 60
CARD_BG = "srgb(38,43,48)"

# Substrings, not exact labels, because both gizmos carry STATE in their text:
# the work one reads "Shift stand: Doctoring" (the resolved work types, so it
# changes with the room) and the owner one "Shared (set owners)" when the stand
# is a pool stand and something else once it has owners. Matching on the stable
# prefix survives both.
WORKTYPES_GIZMO = ("shift stand",)
OWNER_GIZMO = ("set owners", "owner")

RECREATION_ROW = "recreation"

WORKTYPES_WINDOW = "Dialog_SetStandWorkTypes"
OWNERS_WINDOW = "Dialog_AssignStandOwners"


def magick(*args):
    subprocess.run(["magick", *[str(a) for a in args]], check=True)


# Windows that are always in the stack and must never be closed: tooltips and
# the inspect pane are not dialogs and closing them achieves nothing.
TRANSIENT_WINDOWS = ("ImmediateWindow", "MainTabWindow")


def close_all(b, limit=6):
    """Clear open dialogs so map clicks land.

    CLOSE BY TYPE, never bare. `close_window` with no argument closes the
    TOPMOST window, and the topmost is almost always a `Verse.ImmediateWindow`
    tooltip — so a naive loop closes tooltips forever while the dialog
    underneath goes on absorbing every click, and selection fails with
    "nothing selected", which reads like the stage failed to build. Cost two
    runs before the window list was read.
    """
    for _ in range(limit):
        state = b.tool("rimworld/get_ui_state", {})
        if not state.get("nonImmediateDialogWindowOpen"):
            return
        types = {w.get("type") for w in (state.get("windows") or []) if w.get("type")}
        dialogs = [t for t in types if not any(s in t for s in TRANSIENT_WINDOWS)]
        if not dialogs:
            return
        for t in dialogs:
            b.tool("rimworld/close_window", {"windowType": t}, ok=False)


def windows(b):
    targets = b.tool("rimworld/get_screen_targets", {})
    return ((targets.get("targets") or {}).get("windows")) or []


def fresh_game(b, wait_s=90):
    """Return to the menu and boot a clean quicktest colony.

    WAIT FOR THE MENU BEFORE STARTING. `go_to_main_menu` answers
    `{"status": "queued", "longEventPending": true}` — it schedules the
    transition and returns immediately. Calling `start_debug_game_ready`
    straight after it finds the OLD game still loaded, reports "RimWorld has a
    loaded playable game", and starts nothing; the fresh colony then arrives on
    its own a moment later, after the shoot has already built its stage into
    the stale one. That is how the owner list ended up with Chef, Doc, Lab and
    Patient twice over and an "Assign all 11".
    """
    b.tool("rimworld/go_to_main_menu", {})
    deadline = time.time() + wait_s
    while time.time() < deadline:
        state = b.tool("rimworld/get_ui_state", {})
        if state.get("inEntryScene") or not state.get("hasCurrentGame"):
            break
        time.sleep(1)
    else:
        raise SystemExit(f"still in a game {wait_s}s after go_to_main_menu")
    b.tool("rimworld/start_debug_game_ready",
           {"readiness": "playable", "pauseIfNeeded": True, "timeoutMs": 180000})


def select_stand(b):
    close_all(b)
    # FRAME THE CELL FIRST. click_cell injects a click at the screen position
    # the cell maps to, so a cell outside the camera's viewRect cannot be
    # clicked at all — it fails as "nothing selected", which reads like a build
    # failure and is not one. The previous shoot left the camera on the card
    # stage, and z=130 sat one row below the visible rect.
    b.tool("rimworld/jump_camera_to_cell", {"x": STAND[0], "z": STAND[1]})
    b.tool("rimworld/clear_selection", {})
    b.tool("rimworld/click_cell", {"x": STAND[0], "z": STAND[1]})
    sel = b.tool("rimworld/list_selected_gizmos", {})
    if not sel.get("hasSelection"):
        raise SystemExit(f"nothing selected at {STAND} — camera framed there, so "
                         "check the demo stage actually built at this origin")
    return sel.get("gizmos") or []


def gizmo_id(gizmos, *needles):
    for g in gizmos:
        label = (g.get("label") or "").lower()
        if any(n in label for n in needles):
            return g.get("gizmoId") or g.get("id")
    raise SystemExit("no gizmo matching " + repr(needles) + " in "
                     + json.dumps([g.get("label") for g in gizmos]))


def window_shot(b, tag, type_needle):
    """Capture one window by TYPE, cropped to itself.

    Not "the topmost window": the stack carries ImmediateWindow tooltips and
    MainTabWindow_Inspect alongside our dialog, and which one is on top depends
    on where the pointer happens to be. Matching the type is deterministic.

    clipPadding 0 is what replaces the old hand-measured border crop — the
    bridge clips to the window's own rect, so there is no 3-6 px of game world
    to find and trim per capture.
    """
    match = [w for w in windows(b) if type_needle in (w.get("type") or "")]
    if not match:
        raise SystemExit(f"no window of type ~{type_needle!r} open for {tag}; saw "
                         + json.dumps([w.get("type") for w in windows(b)]))
    r = b.tool("rimworld/take_screenshot", {
        "fileName": f"controls-{tag}",
        "clipTargetId": match[0]["windowTargetId"],
        "clipPadding": 0,
        "includeTargets": False,
    })
    path = r.get("path")
    print(f"  {tag:<12} {path}")
    return path


def ui_row(b, needle):
    """Find one actionable control by its label.

    Elements are nested per SURFACE, not flat: a capture carries the gizmo bar,
    the inspect pane and every open window as separate surfaces, each with its
    own element list. Walking only the top level finds nothing at all.
    """
    layout = b.tool("rimworld/get_ui_layout", {})
    seen = []
    for surface in layout.get("surfaces") or []:
        for el in surface.get("elements") or []:
            text = (el.get("label") or el.get("valueText") or "")
            if not el.get("actionable"):
                continue
            seen.append(text)
            if needle in text.lower():
                return el["targetId"]
    raise SystemExit(f"no actionable UI row matching {needle!r}; actionable labels: "
                     + json.dumps(seen)[:800])


def main():
    b, rest = connect(sys.argv[1:])
    out = os.path.join(REPO, "media", "cards", "card-controls.png")

    # FRESH GAME FIRST, unless told otherwise. The demo stage spawns four
    # staff every time it is built, and they accumulate: a second run put
    # Chef, Doc, Lab and Patient in the owner list TWICE, and an eleven-row
    # "Assign all 11" is not the control this card is meant to show. Starting
    # from the main menu makes the shoot idempotent rather than dependent on
    # what the instance has been used for.
    if "--keep-game" not in rest:
        print("starting a fresh quicktest colony (use --keep-game to skip)")
        close_all(b)
        fresh_game(b)

    print(f"building demo stage at {ORIGIN}")
    b.tool("rimworld/set_time_speed", {"speed": "Paused"})
    close_all(b)
    b.tool("rimworld/execute_debug_action",
           {"path": STAGE, "x": ORIGIN[0], "z": ORIGIN[1]})
    b.tool("rimworld/step_game_ticks", {"ticks": 15})

    # --- panel 1: work mode, resolved to doctoring from the room
    gizmos = select_stand(b)
    b.tool("rimworld/execute_gizmo", {"gizmoId": gizmo_id(gizmos, *WORKTYPES_GIZMO)})
    p_work = window_shot(b, "work", WORKTYPES_WINDOW)

    # --- panel 2: the SAME window with recreation ticked
    b.tool("rimworld/click_ui_target", {"targetId": ui_row(b, RECREATION_ROW)})
    p_rec = window_shot(b, "recreation", WORKTYPES_WINDOW)
    b.tool("rimworld/close_window", {}, ok=False)

    # --- panel 3: the owner list
    gizmos = select_stand(b)
    b.tool("rimworld/execute_gizmo", {"gizmoId": gizmo_id(gizmos, *OWNER_GIZMO)})
    p_owners = window_shot(b, "owners", OWNERS_WINDOW)
    b.tool("rimworld/close_window", {}, ok=False)

    compose(p_work, p_rec, p_owners, out)


def compose(work, rec, owners, out):
    """Three panels on the cards' background, each vertically centred.

    The vertical offsets are not free parameters — every panel is centred, so
    each is (CARD_H - height) / 2. Horizontal spacing is derived from what is
    left over, so a dialog that grows a row re-spaces itself instead of
    silently overlapping its neighbour.
    """
    panels = (work, rec, owners)
    sizes = []
    for p in panels:
        w, h = subprocess.run(["magick", "identify", "-format", "%w %h", p],
                              capture_output=True, check=True).stdout.split()
        sizes.append((int(w), int(h)))

    # The two work-types panels MUST match in height: FitToMode sizes that
    # window from InitialSize.y, which branches on ModeOnly and not on
    # recreation, so it is the same window in both modes. A mismatch means one
    # capture caught a different dialog — a different room's stand, whose auto
    # label wraps to a second line — and the pair is not a before/after of one
    # control any more.
    if sizes[0][1] != sizes[1][1]:
        raise SystemExit(f"work-types panels differ in height {sizes[0]} vs {sizes[1]} — "
                         "they should be one window in two modes")

    card_w = sum(w for w, _ in sizes) + CARD_GAP * (len(sizes) + 1)
    card_h = max(h for _, h in sizes) + CARD_MARGIN_Y * 2
    print(f"panels: {sizes}  canvas {card_w}x{card_h}")

    args = ["-size", f"{card_w}x{card_h}", f"xc:{CARD_BG}"]
    x = CARD_GAP
    for path, (w, h) in zip(panels, sizes):
        args += [path, "-geometry", f"+{x}+{(card_h - h) // 2}", "-composite"]
        x += w + CARD_GAP
    args += ["-strip", "-define", "png:compression-level=9", out]
    magick(*args)
    print(f"card: {out}")


if __name__ == "__main__":
    main()
