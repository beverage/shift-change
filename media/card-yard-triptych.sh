#!/bin/bash
#
# Builds media/cards/card-construction.png — the construction yard in three
# beats, read left to right: the crew in formation in their own clothes, the
# crew at the changing area's stands mid-swap, and the crew carrying granite
# out to the site in hardhats.
#
#   media/card-yard-triptych.sh "<recording>.mov"
#   SEAMS=none media/card-yard-triptych.sh "<recording>.mov"   # no shaded seams
#
# The yard is laid out west to east in the order things happen (formation,
# changing area, crates and site) and the camera never moves, so each third is
# the SAME framing cut into three equal strips, each taken at its own moment.
# They line up into one picture of the yard, the crew appearing once per beat.
#
# The recording is NOT in this repo: OBS's 2026-10-13 12:36 take of the media
# sandbox's mechanised yard (2560x1440, 30 fps), the same take as
# demo-construction.gif. EVERY constant below was measured off it and has to be
# re-measured for a new take.
#
# Every look setting can be overridden from the environment, for trying
# variants without editing the file: SEAMS, SHAPE, BOW, PEAK, PEAK_Y, REACH,
# FRONT, CURVE, DEPTH, CREST, BLUR, BLUR_ANGLE, and the three moments.
set -eu

SRC=${1:?the yard recording}
HERE=$(dirname "$0")
OUT=${OUT:-$HERE/cards/card-construction.png}
FONT=${FONT:-$HERE/RimWordFont.ttf}
SEAMS=${SEAMS:-wave}

W=${TMPDIR:-/tmp}/card-yard-triptych
mkdir -p "$W"

# ---- the band -----------------------------------------------------------
# Three equal thirds of 506 px, in source pixels, from x 84 - BOW. Each seam
# points BOW px forward at row PEAK_Y, trails a shadow REACH px behind its
# line, and ends FRONT px past it. At its point the first seam sits at source
# x 590, as close to the changing area's west wall (602) as its front allows;
# its 128 px tail still reaches into the formation's last column (which ends
# ~501), shading their right sides by about 10% at the defaults, fading to
# nothing across them. The gap between the two is only ~100 px, so a tail this
# long cannot clear them. The second seam, at 1096 at its point, keeps its
# tail clear of the changing frame's last pawn (inside the area, by ~950) and
# its front clear of the working frame's builders (from ~1170 at 6.6 s). The
# total stays even: ffmpeg rounds an odd crop width down for 4:2:0 video, and
# the masks would then be a pixel wider than the frames.
#
# Vertically, y 280-1110 holds everything with room above for the title's fade:
# the crates' tops are at 470, the formation 610-900, the area's shadow and the
# lowest builder at the crates end by 1100.
BOW=${BOW:-12}
THIRD=506
X0=$(( 84 - BOW ))
Y0=280
H=830
WIDTH=$(( THIRD * 3 ))
SEAM1=$THIRD
SEAM2=$(( THIRD * 2 ))

# ---- the moments --------------------------------------------------------
# start   1.0 s  everyone still in formation, before the game is unpaused
# change  5.16 s (the frame at 5.167) all twelve at their mechanised stands:
#                eleven in front of the uniform still on theirs, Bender just
#                changed in front of his, his own clothes now on it; two frames
#                earlier, at 5.1, Toni was still walking to her stand across
#                Peter's
# work    6.6 s  ten in hardhats carrying granite, spread from the crates out
#                to the site's wall; at 6.3 they were still clustered west of
#                the crates, and from 7.1 half the crew is building along the
#                wall outside the band
T_START=${T_START:-1.0}
T_CHANGE=${T_CHANGE:-5.16}
T_WORK=${T_WORK:-6.6}

for beat in start change work
do
  case $beat in
    start) t=$T_START ;;
    change) t=$T_CHANGE ;;
    work) t=$T_WORK ;;
  esac
  ffmpeg -y -v error -ss "$t" -i "$SRC" -vf "crop=${WIDTH}:${H}:${X0}:${Y0}" -frames:v 1 "$W/$beat.png"
done

# ---- the seam lines -----------------------------------------------------
# Each seam's line, as an -fx expression for its x at row j, pushed BOW px
# forward at row PEAK_Y so it reads as a front moving ahead rather than a
# ruled border. SHAPE picks how: chevron (two straight lines meeting at
# PEAK_Y), arc (a quarter cosine either side) or peak (flanks that steepen
# into a point, PEAK the exponent, like a wave crest). Tried 2026-10-13 at 24
# px and settled on a 12 px chevron: subtle, since there is little room.
#
# PEAK_Y is the yard's centre line, not the card's: the open aisle between
# the two alcoves, where the crew walks in, the formation's middle row and the
# crate column's middle all sit at source y ~749, band row 469. The card's
# middle is 54 px higher because of the headroom left above for the title's
# fade. Each flank runs from PEAK_Y to its own edge, so the line meets the top
# and bottom of the card unpushed.
SHAPE=${SHAPE:-chevron}
PEAK=${PEAK:-1.8}
PEAK_Y=${PEAK_Y:-469}
front() {
  # u: 0 on the peak row, 1 at the card's top or bottom edge.
  local u="(j < $PEAK_Y ? ($PEAK_Y - j)/$PEAK_Y : (j - $PEAK_Y)/($H - $PEAK_Y))"
  case $SHAPE in
    arc) echo "($1 + $BOW*cos(pi/2*$u))" ;;
    peak) echo "($1 + $BOW*pow(1 - $u, $PEAK))" ;;
    *) echo "($1 + $BOW*(1 - $u))" ;;
  esac
}
F1=$(front $SEAM1)
F2=$(front $SEAM2)
# White where the later moment shows: right of the line, anti-aliased.
magick -size ${WIDTH}x${H} xc: -fx "clamp(i - $F1 + 0.5)" "$W/switch-1.png"
magick -size ${WIDTH}x${H} xc: -fx "clamp(i - $F2 + 0.5)" "$W/switch-2.png"
magick "$W/start.png" "$W/change.png" "$W/switch-1.png" -composite \
       "$W/work.png" "$W/switch-2.png" -composite "$W/joined.png"

# ---- the seams ----------------------------------------------------------
# Time flows left to right, so each seam is a line shaped like a wave crest
# travelling that way: a long, gentle back that builds slowly and then
# steepens (an ease-in, CURVE the exponent) over REACH px towards the line, a
# strong front edge on the line itself, and a fall back to full light within
# FRONT px ahead of it. A symmetric band read as a panel border, and a linear
# ramp as a plain shadow; the asymmetry is what reads as movement.
#
# CREST is the shading's colour: the cards' own slate backdrop (#262b30, as on
# card-controls) reads as a haze rather than a shadow, chosen over black.
# BLUR smears the frame sideways inside each seam, a motion blur (sigma BLUR,
# direction BLUR_ANGLE, 180 trailing behind the motion) mixed in on the
# crest's own profile at full strength, so it is strongest on the line and
# gone at the tail's end.
#
# The moments switch exactly on the line, which the darkest part of the crest
# covers. The scenery is identical in all three frames; only the pawns differ,
# and the bare ground each seam sits on keeps them clear of the shading.
#
# Tried and dropped 2026-10-13: a sine-wave boundary, a lit rim ahead of the
# line, and motion ghosts of the builders from a few frames earlier (busy:
# their name labels ghost too, and the crates smear where builders passed).
REACH=${REACH:-128}
FRONT=${FRONT:-12}
CURVE=${CURVE:-1.6}
DEPTH=${DEPTH:-0.45}
CREST=${CREST:-#262b30}
BLUR=${BLUR:-20}
BLUR_ANGLE=${BLUR_ANGLE:-180}
if [ "$SEAMS" = "wave" ]
then
  # $1: the line, $2: the strength at it (DEPTH unless given). d is the
  # distance behind the line, written out each time: this ImageMagick's -fx
  # takes no assignments.
  crest() {
    local d="($1 - i)"
    local k=${2:-$DEPTH}
    echo "$d < -$FRONT ? 0 : ($d < 0 ? $k*(1 + $d/$FRONT) : ($d <= $REACH ? $k*pow(1 - $d/$REACH, $CURVE) : 0))"
  }
  if [ "$BLUR" != "0" ]
  then
    magick "$W/joined.png" -motion-blur 0x${BLUR}+${BLUR_ANGLE} "$W/blurred.png"
    magick -size ${WIDTH}x${H} xc: -fx "max($(crest "$F1" 1), $(crest "$F2" 1))" "$W/blur-mask.png"
    magick "$W/joined.png" "$W/blurred.png" "$W/blur-mask.png" -composite "$W/joined.png"
  fi
  magick -size ${WIDTH}x${H} xc: -fx "max($(crest "$F1"), $(crest "$F2"))" "$W/shade.png"
  magick "$W/joined.png" \
         \( -size ${WIDTH}x${H} xc:"$CREST" "$W/shade.png" -alpha off -compose CopyOpacity -composite \) \
         -compose over -composite "$W/seamed.png"
else
  cp "$W/joined.png" "$W/seamed.png"
fi

# ---- title --------------------------------------------------------------
# The scene cards' treatment, scaled by WIDTH: Steam's gallery shows every
# image at one width, so the title's share of the width is what makes it the
# same size as theirs (389 of 640 px, 61%). card-blacktie scaled its title by
# height instead (587/360), which leaves it at 43% of its width and smaller in
# the gallery. 44 pt, top at y 13 and a fade to y 85 at 640 wide.
PT=$(( 44 * WIDTH / 640 ))
TITLE_Y=$(( 13 * WIDTH / 640 ))
FADE=$(( 85 * WIDTH / 640 ))
magick -background none -fill "#f5f0e7" -font "$FONT" -pointsize $PT \
       label:"SHIFT CHANGE" -trim +repage "$W/title.png"
TW=$(magick identify -format %w "$W/title.png")
magick "$W/seamed.png" \
       \( -size ${WIDTH}x${FADE} gradient:black-none \) -geometry +0+0 -composite \
       "$W/title.png" -geometry +$(( (WIDTH - TW) / 2 ))+${TITLE_Y} -composite \
       -depth 8 -strip -define png:compression-level=9 "$OUT"
magick identify "$OUT"
