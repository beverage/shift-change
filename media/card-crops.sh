#!/bin/bash
#
# Cuts the raw game captures in media/cards/ into the pieces the description
# cards use. Run from media/, or set SRC/OUT to absolute paths.
#
# WHY THIS EXISTS: card-controls.png is a 1700x800 contact sheet of three
# dialogs. Steam renders a description image at 640, so the sheet arrives on the
# page at 38% and every label in it is unreadable. Each dialog is only ~418 px
# wide on its own, which a 640 card shows at essentially 1:1 — so the fix is not
# a bigger capture, it is one dialog per image.
#
# The source is a lossless 2x capture, so these crops are native-resolution
# pixels, not upscales. Do not re-scale them here; let the card's CSS decide the
# display size.
#
# ---------------------------------------------------------------------------
# THE BORDER RULE
# ---------------------------------------------------------------------------
# A crop is either a SLICE OF A PANEL or a WHOLE WIDGET, and they are bordered
# differently:
#
#   Panel slice  -> crop strictly INSIDE the game's own frame, carrying no
#                   game chrome at all. The card's CSS draws the only border.
#   Whole widget -> keep the game frame, because on a gizmo or a tooltip the
#                   frame IS the widget and cutting it makes a button stop
#                   looking like a button.
#
# The rule exists because the first pass did neither consistently: three
# dialogs kept a frame on three sides and lost it on the fourth where the crop
# cut through the panel, and the row strips had no frame at all. A partial
# border reads as damage — worse than either having one or not.
#
# Every panel slice from one source also shares an x-window, so the crops stack
# in exact alignment on the card instead of stepping in and out by a few pixels.
#
# ---------------------------------------------------------------------------
# MEASURED GEOMETRY — card-controls.png, 1493x819
# ---------------------------------------------------------------------------
# Measured, not eyeballed: `magick SRC -crop 1x819+1100+0 +repage txt:-` and
# friends, reading the runs off the pixel dump. Re-measure if it is re-shot.
#
# RE-MEASURED 2026-10-13, when the middle panel became the Serves menu
# (catchments) and the work panel grew its Serves row. Still 1.0x UI scale,
# still a derived canvas; the work panel's top and its section rules did not
# move, everything to its right did. The menu is as wide as its longest row,
# so a wording change there moves the owner list: "(stand is outside it)"
# took it from 199 to 213.
#
#   backdrop  #262B30      frame  1 px      interior  #15191D
#
#   panel            frame x      frame y      INTERIOR / WIDGET
#   1 work types     75,494       60,758       x 76 w 418    y 61 h 697
#   2 Serves menu    570,782      314,504      widget 213x191 at +570+314
#   3 owners         858,1417     129,688      x 859 w 558   y 130 h 558
#
# The work-types panel's section rules land at y 171, y 235 and y 659; the last
# sits above the Serves row. The menu's own frame is #424549, and its section
# gaps arrive already filled with the backdrop by shoot-controls-card.py.
set -eu

SRC=${SRC:-cards/card-controls.png}
SRCD=${SRCD:-cards/src}
OUT=${OUT:-cards/parts}
mkdir -p "$OUT"

P1_X=76 ; P3_X=859
PW=418  ; P3_W=558

# Generated directory: wipe it, so a renamed or retired part cannot linger and
# go on being referenced by a card that should have stopped using it.
rm -f "$OUT"/*.png

# ---- the stand dialog, cut into its three sections ----------------------
# The dialog stacks three groups, separated by rules: what the stand dresses
# FOR, two flags, then the activity checklist. Shipping the whole panel as one
# shot meant the checklist image repeated the two groups above it, each of
# which was already on the card as its own strip — 725 px of card spent saying
# a thing twice. Cut at the rules instead and the same content costs 632.
#
#   y 105  type        Automatic (from the room) / Not used for shift changes
#   y 178  flags       Change the whole outfit / Keep contents out of trade
#   y 237  activities  the ticked-activities blurb, Recreation, Sleeping, and
#                      the work list, stopping a row short of the y 659 rule
#
# Full interior width on all three, so they stack in exact alignment.
magick "$SRC" -crop ${PW}x59+${P1_X}+105  +repage "$OUT/sec-type.png"
magick "$SRC" -crop ${PW}x52+${P1_X}+178  +repage "$OUT/sec-flags.png"
magick "$SRC" -crop ${PW}x421+${P1_X}+237 +repage "$OUT/sec-activities.png"

# ---- the Serves menu ----------------------------------------------------
# A WHOLE WIDGET by the border rule: a float menu's frame is what makes it read
# as a menu, so it is kept, all 213x191 of it.
magick "$SRC" -crop 213x191+570+314 +repage "$OUT/menu-serves.png"

# ---- whole dialogs ------------------------------------------------------
# Stops below its last meaningful row rather than at the panel's bottom frame:
# six colonists end at y 399, under 300 px of empty panel and a Close button,
# and on a 640 card every wasted pixel is spent shrinking the text. The count is
# the demo stage's four staff plus however many the quicktest colony started
# with, which varies by run (three on one shoot, six on the next), so measure
# the last row on every shoot.
magick "$SRC" -crop ${P3_W}x275+${P3_X}+130 +repage "$OUT/dlg-owners.png"

# ---- gizmo buttons ------------------------------------------------------
# The four gizmos a stand can show, from four separate hand-made captures in
# cards/src/. They arrive 144-148 x 158-170 because each was cropped by eye, and
# laid side by side at those sizes they read as four screenshots rather than one
# control set. So each is cut to a single box, positioned on the BUTTON rather
# than on the image: the button measures 133x134 in all four (verified with a
# median-extent scan for its dark blue interior against the warm colony
# backdrop), so anchoring there makes the set uniform whatever the crop was.
#
# 2 px of margin on three sides, 18 on the bottom. The bottom is not symmetry,
# it is a requirement: a gizmo's label OVERFLOWS its button, and a box cut to
# the button alone decapitates every caption — "Doctoring", "Change back",
# "owners)" and "items" all vanish. 18 is what the tightest capture can spare.
#
# The backdrop that survives in that bottom strip cannot be removed, because the
# label is drawn straight onto the game world. Trimming the other three margins
# to 2 keeps it to the caption band.
#
# The label is the GAME's own text rendering, kept rather than retypeset:
# RimWorld's UI font lives inside Unity's asset bundles (Data/Core ships only
# About, Defs and Languages), so it cannot be reproduced here. A hard constraint
# on this whole file, not a preference.
#
#   capture                  button at    -> crop 137x154 at
#   giz-mode.png       148x170   +7+15       +5+13
#   giz-changeback.png 146x170   +5+13       +3+11
#   giz-owners.png     148x166   +5+9        +3+7
#   giz-removing.png   148x158   +5+6        +3+4
GIZ=137x154
magick "$SRCD"/giz-mode.png       -crop ${GIZ}+5+13 +repage "$OUT/giz-mode.png"
magick "$SRCD"/giz-changeback.png -crop ${GIZ}+3+11 +repage "$OUT/giz-changeback.png"
magick "$SRCD"/giz-owners.png     -crop ${GIZ}+3+7  +repage "$OUT/giz-owners.png"
magick "$SRCD"/giz-removing.png   -crop ${GIZ}+3+4  +repage "$OUT/giz-removing.png"

magick identify "$OUT"/*.png
