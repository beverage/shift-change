#!/bin/bash
#
# Builds a before/after scene card from one recording: the SAME crop at two
# moments, side by side, with the scene cards' title treatment (README.md,
# "The scene cards' treatment"). The convention: the pawn starts outside the
# room in their own clothes and ends inside in the stand's outfit, so the crop
# has to take in ground beyond the doorway.
#
#   media/card-pair.sh <recording> <crop WxH+X+Y> <before s> <after s> <out.png>
#
# card-warden.png, from OBS's 2026-10-13 15:56 take of the media sandbox's
# prison (2560x1440, the same take as demo-prison.gif; not in this repo):
#
#   media/card-pair.sh "<15-56-18>.mov" 400x453+900+400 4.6 6.8 cards/card-warden.png
#
#   crop    400x453 from (900,400): Sam's spot outside the front door at the
#           left, Mullen's cell and the three stands to the right, and 70 px
#           of ground above the block for the title's fade. The crop's aspect
#           is the panel's, 318:360.
#   before  4.6 s: Sam outside the door in their own clothes; at 4.4 they are
#           still west of the crop
#   after   6.8 s: Sam in Mullen's cell in the guard kit, mid-chat with the
#           speech bubble showing, their own clothes on the west stand
#
# PANEL and GUTTER make a wider card: card-farm.png, from the 2026-10-13 14:51
# take of the farm (the same take as demo-farm.gif), at the yard card's 1.83:1
# shape and at native resolution, since that take is zoomed out far enough
# that its pawns, shown at the gallery's width, come out the yard card's size:
#
#   PANEL=470x516 GUTTER=6 media/card-pair.sh "<14-51-38>.mov" \
#       470x516+860+419 1.0 9.5 cards/card-farm.png
#
#   crop    470x516 from (860,419): the farmhouse and the corn row north of
#           it, with paving and grass above the corn for the title's fade
#   before  1.0 s: Char and Whistler in the farmhouse in their own clothes,
#           beside the two dressed stands, before the game is unpaused
#   after   9.5 s: both deep in the corn, sickles out, piles along the row,
#           their own clothes on the stands; at 10.5 the two overlap
#
# The title and its fade scale with the card's width, as on the yard card:
# Steam's gallery shows every image at one width. At 640 wide the numbers are
# the scene cards' own (44 pt at +121+13, a 90 px fade).
set -eu

SRC=${1:?recording}
CROP=${2:?crop WxH+X+Y}
T_BEFORE=${3:?before seconds}
T_AFTER=${4:?after seconds}
OUT=${5:?output png}
HERE=$(dirname "$0")
FONT=${FONT:-$HERE/RimWordFont.ttf}

W=${TMPDIR:-/tmp}/card-pair
mkdir -p "$W"

# WxH+X+Y as ffmpeg's crop=W:H:X:Y.
CW=${CROP%%x*}
REST=${CROP#*x}
CH=${REST%%+*}
REST=${REST#*+}
CX=${REST%%+*}
CY=${REST#*+}

# By default 318x360 panels and a 4 px gutter, the after panel at x 322: the
# measurements in README.md, taken back off card-chef.
PANEL=${PANEL:-318x360}
PW=${PANEL%x*}
PH=${PANEL#*x}
GUTTER=${GUTTER:-4}
CARD_W=$(( PW * 2 + GUTTER ))
for beat in before after
do
  case $beat in
    before) t=$T_BEFORE ;;
    after) t=$T_AFTER ;;
  esac
  ffmpeg -y -v error -ss "$t" -i "$SRC" \
         -vf "crop=${CW}:${CH}:${CX}:${CY},scale=${PW}:${PH}:flags=lanczos" -frames:v 1 "$W/$beat.png"
done

PT=$(( 44 * CARD_W / 640 ))
TX=$(( 121 * CARD_W / 640 ))
TY=$(( 13 * CARD_W / 640 ))
FADE=$(( 90 * CARD_W / 640 ))
magick -background none -fill "#f5f0e7" -font "$FONT" -pointsize $PT \
       label:"SHIFT CHANGE" -trim +repage "$W/title.png"
magick -size ${CARD_W}x${PH} xc:black \
       "$W/before.png" -geometry +0+0 -composite \
       "$W/after.png" -geometry +$(( PW + GUTTER ))+0 -composite \
       \( -size ${CARD_W}x${FADE} gradient:black-none \) -geometry +0+0 -composite \
       "$W/title.png" -geometry +${TX}+${TY} -composite \
       -depth 8 -strip -define png:compression-level=9 "$OUT"
magick identify "$OUT"
