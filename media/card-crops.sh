#!/bin/bash
#
# Cuts the raw game captures in media/cards/ into the pieces the description
# cards use. Run from media/, or set SRC/TIP/OUT to absolute paths.
#
# WHY THIS EXISTS: card-controls.png is a 2000x900 contact sheet of three
# dialogs. Steam renders a description image at 640, so the sheet arrives on the
# page at 32% and every label in it is unreadable. Each dialog is only ~520 px
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
#   Panel slice  -> crop strictly INSIDE the game's 2 px frame, carrying no
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
# MEASURED GEOMETRY — card-controls.png, 2000x900
# ---------------------------------------------------------------------------
# Measured, not eyeballed: `magick SRC -crop 36x1+44+400 +repage txt:-` and
# friends, reading the runs off the pixel dump. Re-measure if it is re-shot.
#
#   backdrop  #262B30      frame  #57616E (exactly 2 px)    interior  #15181B
#
#   panel            frame x        frame y        INTERIOR
#   1 work types     50,573         43,854         x 52 w 521   y 45 h 809
#   2 recreation     650,1173       43,854         x 652 w 521  y 45 h 809
#   3 owners         1250,1948      100,797        x 1252 w 696 y 102 h 695
#
# The two Work types panels are the same window captured twice, hence the
# identical y and width; only x differs.
set -eu

SRC=${SRC:-cards/card-controls.png}
TIP=${TIP:-cards/card-tooltip.png}
SRCD=${SRCD:-cards/src}
OUT=${OUT:-cards/parts}
mkdir -p "$OUT"

P1_X=52 ; P2_X=652 ; P3_X=1252
PW=521  ; P3_W=696

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
#   y 100  type        Automatic (from the room) / Not used for shift changes
#   y 192  flags       Change the whole outfit / Keep contents out of trade
#   y 266  activities  the ticked-activities blurb, Recreation, the work list
#
# Full interior width on all three, so they stack in exact alignment.
magick "$SRC" -crop ${PW}x74+${P1_X}+100  +repage "$OUT/sec-type.png"
magick "$SRC" -crop ${PW}x64+${P1_X}+192  +repage "$OUT/sec-flags.png"
magick "$SRC" -crop ${PW}x494+${P1_X}+266 +repage "$OUT/sec-activities.png"

# ---- whole dialogs ------------------------------------------------------
# Each stops below its last meaningful row rather than at the panel's bottom
# frame: both carry 400+ px of empty panel below their content, and on a 640
# card every wasted pixel is spent shrinking the text. The Close button carries
# no information either.
magick "$SRC" -crop ${PW}x360+${P2_X}+45   +repage "$OUT/dlg-recreation.png"
magick "$SRC" -crop ${P3_W}x268+${P3_X}+102 +repage "$OUT/dlg-owners.png"

# The recreation row carries its greyed note, which is the informative half —
# it is the game saying work types no longer apply. Both note lines or neither;
# a strip that clips the second one mid-sentence reads as a mistake.
magick "$SRC" -crop ${PW}x104+${P2_X}+320 +repage "$OUT/row-recreation.png"

# ---- the Allow removing items tooltip -----------------------------------
# Text only, and only the first half of it. The tooltip runs on for two more
# paragraphs after the yellow line, and those two say — at greater length —
# exactly what the card's own copy underneath says. Printing both is asking the
# reader to read the same warning twice in two different voices.
#
# The button is NOT taken from this file any more. card-tooltip.png predates the
# purpose-shot captures in cards/src/, so its gizmo is the older, worse
# rendering; the card pairs this text with giz-removing.png below instead.
#
# A PANEL SLICE by the border rule, so it is cut inside the game frame and the
# card draws the only edge. Measured on card-tooltip.png the same way as
# card-controls.png:
#
#   frame #59626F (2 px)   left 550   right 1218   top 94
#   -> interior x 552 w 666, y 96
#
# 265 tall stops about 20 px under the yellow line's descenders, clear of the
# next paragraph, which begins near y 395.
magick "$TIP" -crop 666x265+552+96 +repage "$OUT/tip-text.png"

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
