#!/usr/bin/env python3
"""Shoot the sleep gallery card: a before/after pair from the sleep card stage.

    devtools/run-scene.sh --media --bridge
    devtools/bridge/shoot-sleep-card.py 5176 shiftchange-scene-bridge --survey
    devtools/bridge/shoot-sleep-card.py 5176 shiftchange-scene-bridge --after 9000

WHAT THIS PRODUCES. Two 8x9-cell captures of the same crop at two moments, in
the set's convention: the soldier starts in the corridor in prestige cataphract
(before) and ends inside in the stand's duster and helmet (after). The card
itself is composited by media/card-triptych.sh or by hand from the recipe in
media/README.md; this script's job is the two panels and the numbers that made
them.

WHY A SURVEY MODE. The "after" moment is the only free parameter, and it cannot
be derived: it depends on how long the pawn takes to roll the rest job, walk in,
and finish two garment swaps. --survey plays the scene once and captures a
ladder of candidates, so ONE stage build yields every option instead of one
guess per build. Pick the frame that reads best, then pass its millisecond
figure to --after for the real take, and WRITE THAT NUMBER DOWN in
media/README.md. The recreation gif's cut parameters were never recorded and
re-deriving them cost more than the shoot did.

THE CAPTURE RECT IS THE PREVIEW STAGE'S, deliberately. The stage builds at the
preview block's geometry (8 wide, 10 tall) and the shot drops the southmost
row — the corridor's far wall — for an 8x9 rect, which is the 320x360 panel the
work cards were cut to. Sharing it is what lets a sleep card sit beside
card-doc and card-lab without looking like a different set.
"""
import json
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gabp import connect  # noqa: E402

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                     "..", ".."))

# The scene card's geometry, measured off card-chef on 2026-08-21 and recorded
# in media/README.md. Two panels, a 4 px gutter, split at x=319.
CARD_W, CARD_H = 640, 360
PANEL_W, PANEL_H = 318, 360
PANEL2_X = 322
FADE_H = 90

# TYPESET when the font is available, LIFT when it is not.
#
# RimWordFont.ttf lives in THIS repo at media/, beside the cards it sets. It
# briefly lived outside this repository instead, which meant every tool had to
# reach out of its own checkout to find it — and the line that did so was an
# absolute path carrying a home directory, in a public repository. A
# repo-relative asset needs no path at all.
#
# At -pointsize 44 it renders a 390x38 ink box, matching the 389x37 measured off
# card-chef, so this really is the original setting rather than a near miss.
#
# The lift survives as a fallback for a checkout that somehow lacks the file.
# It thresholds the donor band into a stencil rather than screen-compositing
# it, because the donor's lower third has scene colour bleeding through and
# screening drags that in with the letters.
FONT_CANDIDATES = [
    os.environ.get("RIMWORD_FONT", ""),
    os.path.join(REPO, "media", "RimWordFont.ttf"),
]
TITLE_DONOR = os.path.join(REPO, "media", "cards", "card-recreation.png")
TITLE_INK = "#f5f0e7"
TITLE_THRESHOLD = "62%"
TITLE_TEXT = "SHIFT CHANGE"
TITLE_POINTSIZE = 44
TITLE_POS = (121, 13)


def find_font():
    for path in FONT_CANDIDATES:
        if path and os.path.isfile(path):
            return path
    return None


def magick(*args):
    subprocess.run(["magick", *[str(a) for a in args]], check=True)

STAGE = "Actions\\Dev tools...\\Build sleep card stage"

# The stage clears its own footprint, so anywhere in bounds works; this is
# simply somewhere a quicktest map is reliably open ground.
ORIGIN = (120, 120)

# DebugTools_PreviewStage's block, mirrored here rather than guessed: 8 wide,
# 10 tall, and the shot drops the southmost row.
BLOCK_WIDTH = 8
BLOCK_HEIGHT = 10
SHOT_HEIGHT = BLOCK_HEIGHT - 1

# Camera height for the capture. A captured cell is screenHeight / (2 *
# rootSize) pixels, so at the 1080-tall window run-scene.sh seeds, rootSize 12
# gives 45 px per cell — 360x405 for an 8x9 rect, comfortably above the 318x360
# the panel needs, so the downscale in the composite is doing work rather than
# inventing detail.
ROOT_SIZE = 12.0

# Ticks to run after the build before anything is captured, so the glow grid
# has caught up with the roof the stage just wrote. See build().
SETTLE_TICKS = 15

# Survey ladder, in milliseconds of Ultrafast play from the build. Override
# with --ladder 400,800,1200.
#
# SHORT ON PURPOSE, twice over. The whole beat is quick — the first survey had
# the soldier already asleep by its first rung at 3000ms, so the change itself
# fell between t=0 and the first frame and was never captured. And Ultrafast
# burns roughly an in-game hour per 18 seconds, which walked the ladder from
# afternoon into dusk: the late frames were visibly darker than the early ones,
# so they could not be compared, let alone composited into a pair. Keeping the
# whole run inside a few hundred ticks holds the light still.
LADDER = [400, 800, 1200, 1600, 2000, 2400, 2800]


def shot(b, tag, out_dir):
    """One capture of the canonical rect, camera restored afterwards."""
    r = b.tool("rimworld/screenshot_cell_rect", {
        "x": ORIGIN[0],
        "z": ORIGIN[1] + 1,
        "width": BLOCK_WIDTH,
        "height": SHOT_HEIGHT,
        "paddingCells": 0,
        "rootSize": ROOT_SIZE,
        "fileName": f"sleep-card-{tag}",
        "includeTargets": False,
    })
    path = r.get("path")
    print(f"  {tag:>10}  {path}")
    return path


def build(b):
    """Pause, build the stage, and settle a frame so it is drawn before t=0.

    Returns ticksGame at the build, which is the shoot's ONLY lighting
    coordinate: nothing here pins the hour, so a re-shoot lands under whatever
    sun the clock has drifted to. Record it beside the crop — two takes at
    different ticks are two different lightings and will not composite.
    """
    b.tool("rimworld/set_time_speed", {"speed": "Paused"})
    b.tool("rimworld/execute_debug_action",
           {"path": STAGE, "x": ORIGIN[0], "z": ORIGIN[1]})
    # LET THE LIGHT SETTLE BEFORE t=0, or the before panel will not match the
    # after one. The stage writes its roof during the build, but the glow grid
    # catches up over the following ticks — so a capture at tick 1 shows the
    # room still taking DAYLIGHT and every later frame shows it torch-lit and
    # darker. The first fine survey had exactly that: panel one warm, panels
    # two onward a stop down, which is uncompositable as a pair.
    #
    # Ticks, not milliseconds of play, because this has to be the same amount
    # of settling every run. Few enough that the soldier has barely left his
    # spawn cell — he covers about a cell every twenty ticks.
    b.tool("rimworld/step_game_ticks", {"ticks": SETTLE_TICKS})
    info = b.tool("rimworld/get_game_info", {})
    return info.get("ticksGame")


def compose(before, after, out, work):
    """The scene-card composite, to media/README.md's recipe exactly."""
    p1 = os.path.join(work, "_panel-before.png")
    p2 = os.path.join(work, "_panel-after.png")
    # Forced resize: an 8x9 cell capture is 0.889 and the panel is 0.883, a
    # 0.6% squash that no eye finds and that keeps both panels pixel-identical
    # in size, which is what makes a before/after read as one photograph.
    magick(before, "-resize", f"{PANEL_W}x{PANEL_H}!", p1)
    magick(after, "-resize", f"{PANEL_W}x{PANEL_H}!", p2)

    s1 = os.path.join(work, "_s1.png")
    magick("-size", f"{CARD_W}x{CARD_H}", "xc:black",
           p1, "-geometry", "+0+0", "-composite",
           p2, "-geometry", f"+{PANEL2_X}+0", "-composite", s1)

    s2 = os.path.join(work, "_s2.png")
    magick(s1, "(", "-size", f"{CARD_W}x{FADE_H}", "gradient:black-none", ")",
           "-geometry", "+0+0", "-composite", s2)

    font = find_font()
    title = os.path.join(work, "_title.png")
    if font:
        print(f"  title: typeset with {font}")
        magick("-background", "none", "-fill", TITLE_INK, "-font", font,
               "-pointsize", TITLE_POINTSIZE, f"label:{TITLE_TEXT}",
               "-trim", "+repage", title)
        pos = f"+{TITLE_POS[0]}+{TITLE_POS[1]}"
    else:
        print(f"  title: font not found, lifting from {os.path.basename(TITLE_DONOR)}")
        mask = os.path.join(work, "_title-mask.png")
        magick(TITLE_DONOR, "-crop", f"{CARD_W}x{FADE_H}+0+0", "+repage",
               "-colorspace", "gray", "-threshold", TITLE_THRESHOLD, mask)
        magick("-size", f"{CARD_W}x{FADE_H}", f"xc:{TITLE_INK}",
               mask, "-alpha", "off", "-compose", "CopyOpacity", "-composite", title)
        # The stencil is a full-width band already positioned by the donor.
        pos = "+0+0"

    magick(s2, title, "-geometry", pos, "-composite",
           "-strip", "-define", "png:compression-level=9", out)
    print(f"card: {out}")


def main():
    b, rest = connect(sys.argv[1:])
    survey = "--survey" in rest
    after_ms = 1600
    if "--after" in rest:
        after_ms = int(rest[rest.index("--after") + 1])
    ladder = LADDER
    if "--ladder" in rest:
        ladder = [int(v) for v in rest[rest.index("--ladder") + 1].split(",")]

    out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                           "..", "..", "dist", "shoot")
    os.makedirs(out_dir, exist_ok=True)

    print(f"building {STAGE} at {ORIGIN}")
    ticks = build(b)
    print(f"ticksGame at build: {ticks}  (the lighting coordinate — record it)")

    print("before:")
    before = shot(b, "before", out_dir)

    frames = []
    if survey:
        print(f"survey ladder (ms of Ultrafast play): {ladder}")
        played = 0
        for target in ladder:
            step = target - played
            b.tool("rimworld/play_for",
                   {"durationMs": step, "speed": "Ultrafast",
                    "forceRequestedSpeed": True})
            played = target
            frames.append((target, shot(b, f"t{target}", out_dir)))
    else:
        print(f"after: playing {after_ms}ms at Ultrafast")
        b.tool("rimworld/play_for",
               {"durationMs": after_ms, "speed": "Ultrafast",
                "forceRequestedSpeed": True})
        frames.append((after_ms, shot(b, "after", out_dir)))

    b.tool("rimworld/set_time_speed", {"speed": "Paused"})

    print()
    print("panels:")
    print(f"  before  t=0      {before}")
    for ms, path in frames:
        print(f"  after   t={ms:<6} {path}")

    if not survey:
        card = os.path.join(REPO, "media", "cards", "card-sleep.png")
        compose(before, frames[0][1], card, out_dir)

    print()
    print("record the chosen --after value in media/README.md with the crop.")
    print(json.dumps({"origin": ORIGIN, "rect": [ORIGIN[0], ORIGIN[1] + 1,
                                                 BLOCK_WIDTH, SHOT_HEIGHT],
                      "rootSize": ROOT_SIZE, "ticksGameAtBuild": ticks},
                     indent=2))


if __name__ == "__main__":
    main()
