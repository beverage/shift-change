#!/bin/bash
#
# Builds media/cards/card-blacktie.png — the three-beat rec-room card: the
# room before the change, the changing wall, and the room after, read left to
# right. Run from media/.
#
# The three source captures are NOT in this repo; they are screengrabs of the
# rec room stage (dev mode -> Shift Change -> Build rec room stage). EVERY
# constant below was measured off those particular captures and has to be
# re-measured for a new take — see "card-blacktie" in README.md for how, and
# for why each number is the value it is.
#
set -eu

BEFORE=${1:?before shot}
STRIP=${2:?changing-wall strip}
AFTER=${3:?after shot}

OUT=${OUT:-cards/card-blacktie.png}

# Beside this script, not in ~/Downloads. The font used to be defaulted out of a
# downloads folder, which is a place things get cleared out of — and it meant
# every card tool had to be pointed back at it by hand. It is a media asset, so
# it lives with the media. FONT still overrides.
HERE=$(dirname "$0")
FONT=${FONT:-$HERE/RimWordFont.ttf}
BG="#262b30"

W=${TMPDIR:-/tmp}/card-triptych
mkdir -p "$W"

PANEL_W=733
PANEL_H=587
GUT=16
CANVAS_W=$(( PANEL_W * 2 + GUT ))
MID=$(( CANVAS_W / 2 ))

# ---- panels -------------------------------------------------------------
# Both room shots were taken at the SAME zoom on this take: the room rectangle
# measures 924x677 in each, at (22,47) in the before and (15,49) in the after
# — a 7 px pan, no rescale. Each is cropped 954x732 around its own rectangle so
# the room lands on identical pixels, then both are pulled 40 px in at the
# join: the east wall is dead weight in the before, the stripped stands are
# dead weight in the after, so the band eats only inactive scene.
#
# Do NOT assume the zoom matches. An earlier take measured 977x716 and 957x700
# — 2.1% apart — and needed `-resize` on the after shot before cropping.
magick "$BEFORE" -crop 914x732+7+7  +repage -resize ${PANEL_W}x${PANEL_H}! "$W/l.png"
magick "$AFTER"  -crop 914x732+40+9 +repage -resize ${PANEL_W}x${PANEL_H}! "$W/r.png"

# ---- the middle beat ----------------------------------------------------
# The changing wall at its own tighter zoom, so it reads as a step rather than
# as a third room. Cropped from the building's outer edge (x=34, measured off
# a clear floor row).
#
# The window sits HIGH in the source, which pushes the content DOWN in the
# band. The stands begin at source y=154, and the title fade is opaque for the
# band's top 139 px: framed from the north wall at y=85 the first stand lands
# at card y=55 and loses 60% of itself to the fade, while a standing lamp and
# 100 px of bare floor idle at the bottom. Starting at y=43 spends half of that
# dead floor on the top of the column — the first stand moves to card y=89 and
# the fade over it drops to 36%.
#
# y=0 is available and clears the fade almost entirely, but it overshoots: the
# band then opens on ground outside the building and the air below the last
# pawn goes thinner than the air above the first, which inverts the symmetry
# rather than fixing it. Half the travel keeps both ends breathing.
magick "$STRIP" -crop 232x730+34+43 +repage -resize x${PANEL_H} "$W/band-raw.png"
magick "$W/band-raw.png" -bordercolor black -border 4x0 "$W/band.png"
BW=$(magick identify -format "%w" "$W/band.png")
BX=$(( MID - BW / 2 ))

magick -size ${CANVAS_W}x${PANEL_H} xc:"$BG" \
       "$W/l.png" -geometry +0+0 -composite \
       "$W/r.png" -geometry +$(( PANEL_W + GUT ))+0 -composite \
       "$W/s1.png"

# ---- separation ---------------------------------------------------------
# The panels fall off into shadow as they approach the band, so the three read
# as three. Hard black keyline, soft ramp either side: the band's edge is 100%
# black against a ramp that tops out at 75%, so the line stays a line instead
# of dissolving into the shadow.
FADE=120
magick -size ${PANEL_H}x${FADE} gradient:black-none -rotate 90 \
       -channel A -evaluate multiply 0.75 +channel "$W/fade-l.png"
magick -size ${PANEL_H}x${FADE} gradient:none-black -rotate 90 \
       -channel A -evaluate multiply 0.75 +channel "$W/fade-r.png"
magick "$W/s1.png" \
       "$W/fade-l.png" -geometry +$(( BX - FADE ))+0 -composite \
       "$W/fade-r.png" -geometry +$(( BX + BW ))+0 -composite \
       "$W/s2.png"

magick "$W/s2.png" "$W/band.png" -geometry +${BX}+0 -composite "$W/s3.png"

# ---- fade and title -----------------------------------------------------
# The scene cards' own treatment scaled by 587/360.
magick "$W/s3.png" \( -size ${CANVAS_W}x139 gradient:black-none \) \
       -geometry +0+0 -composite "$W/s4.png"
magick -background none -fill "#f5f0e7" -font "$FONT" -pointsize 72 \
       label:"SHIFT CHANGE" -trim +repage "$W/title.png"
TW=$(magick identify -format "%w" "$W/title.png")
magick "$W/s4.png" "$W/title.png" \
       -geometry +$(( (CANVAS_W - TW) / 2 ))+21 -composite \
       -strip -define png:compression-level=9 "$OUT"

magick identify "$OUT"
