#!/usr/bin/env bash
#
# footage.sh — turn a raw screen capture into the assets a release needs.
#
# Point it at a fresh master take and it cuts the gif; nothing in here
# remembers a previous take, so a re-film costs one command rather than an
# afternoon of rediscovering ffmpeg flags.
#
#   ./footage.sh probe  <master.mov>
#   ./footage.sh gif    <master.mov> [options]
#   ./footage.sh still  <master.mov> [options]
#
# WHY A SCRIPT AND NOT A NOTE OF THE COMMANDS: the crop is the whole problem.
# A raw capture is the entire game window — architect bar, colonist strip,
# alert stack — and the demo stage occupies a small square in the middle of
# it. Every asset is therefore a crop, and a crop found by hand at 2am is not
# reproducible. `probe` finds it once and prints the exact flag to reuse.
#
# Requires ffmpeg + ffprobe (brew install ffmpeg).

set -euo pipefail

die() { echo "error: $*" >&2; exit 1; }

command -v ffmpeg  >/dev/null || die "ffmpeg not found (brew install ffmpeg)"
command -v ffprobe >/dev/null || die "ffprobe not found (brew install ffmpeg)"

OUT_DIR="${FOOTAGE_OUT:-$(pwd)}"

# ---------------------------------------------------------------- defaults --
# 8 MB is Steam's ceiling for an inline gif; 720 px wide is comfortable in a
# Workshop content pane and in a GitHub README at full width. FPS is where the
# size is won or lost — 20 reads as smooth for RimWorld's animation rate and
# costs a third of 30.
CROP=""
AUTOCROP=0
RAMP=""
START=""
DURATION=""
FPS=20
# 480 wide reads comfortably in a README column without dominating the page,
# and keeps the pawn name labels legible — at 360 they are right at the edge,
# and the labels are what tell a reader the pawn in scrubs is the doctor.
WIDTH=480
SPEED=1
SIZE_LIMIT_MB=8

# Palette and dithering decide a gif's size, and the trade is NOT the obvious
# one. Starving the palette does shrink the file — 96 colours undithered came
# out ~40% under 256/bayer — but on this content it visibly degrades: the
# carpets posterise and the pawns lose their edges, which are the two things
# the footage exists to show. Judged side by side on 2026-08-12, 256/bayer
# won on looks and the size was recovered by scaling instead (720 -> 480 wide
# halved it again, and legibly).
#
# Order of levers when a cut runs big, cheapest-looking first: WIDTH, then
# duration/ramp, then fps. Palette last — it is the one that shows.
COLORS=256
DITHER=bayer
LOSSY=60

# Find the black border a capture picked up and return the crop that removes
# it. OBS canvas rarely matches the captured region to the pixel, so a take
# arrives pillarboxed by a few px and the letterbox ends up baked into the
# gif.
#
# limit=24, not 0: the bars ARE pure black (measured mean 0), but h264 rings
# slightly on the hard edge between black and content, so a handful of border
# pixels decode a little above zero. cropdetect needs the WHOLE row under the
# limit, so a stricter threshold reports "nothing to crop" and quietly leaves
# the bars in. 24 is comfortably under RimWorld's darkest real content.
autocrop() {
  ffmpeg -v info -ss "${1:-5}" -i "$SRC" \
    -vf "cropdetect=limit=24:round=2:reset=0" -frames:v 90 -f null - 2>&1 \
    | grep -o 'crop=[0-9]*:[0-9]*:[0-9]*:[0-9]*' | tail -1 | cut -d= -f2
}

usage() {
  cat <<'EOF'
footage.sh probe <master>
    Print dimensions/duration and write _contact.png — a tiled sheet of the
    whole take, so you can read off the in/out points and the crop region.

footage.sh gif <master> [opts]     -> _demo.gif
footage.sh mp4 <master> [opts]     -> _demo.mp4
    Same cut, h264 instead. Takes every gif option except --colors/--dither.
    Roughly 20x smaller than the gif on this content, because h264's DCT
    encodes the lamps' smooth light gradients almost for free while a
    256-entry palette has to band them and then re-band them every frame.
    The catch is GitHub: it permits <video> in a README but STRIPS autoplay,
    loop and playsinline, so a video needs a click and plays once, where a
    gif runs on its own.
footage.sh still <master> [opts]   -> _still.png  (add --preview for 640x360)

Options (all optional):
    --crop W:H:X:Y   region to keep, in SOURCE pixels (see `probe`)
    --autocrop       detect and strip the black border OBS left around the
                     capture, instead of passing --crop by hand
    --start SS       start time, seconds or hh:mm:ss
    --duration SS    length in seconds
    --fps N          frames per second           (gif, default 20)
    --width N        output width, keeps aspect  (default 480)
    --speed N        playback multiplier, 2 = twice as fast (gif, default 1)
    --ramp SPEC      per-segment speeds instead of one rate, as a comma list
                     of start:end:speed in SOURCE seconds. Sets its own
                     timing, so it replaces --start/--duration/--speed:
                       --ramp 1:11:3,11:23:6,23:47:3
                     Slow over the changes and the walking, fast through the
                     stretches where the pawns just work.
    --colors N       palette size (gif, default 256)
    --dither MODE    none|bayer|sierra2_4a (gif, default bayer)
    --preview        still only: force 640x360, the Workshop Preview.png size
    --out PATH       explicit output path

Environment:
    FOOTAGE_OUT      output directory (default: current directory)
EOF
}

# ------------------------------------------------------------------ parsing --
[ $# -ge 1 ] || { usage; exit 1; }
MODE="$1"; shift

case "$MODE" in
  probe|gif|mp4|still) ;;
  -h|--help|help)  usage; exit 0 ;;
  *) die "unknown mode '$MODE' (probe|gif|mp4|still)" ;;
esac

[ $# -ge 1 ] || die "no input file"
SRC="$1"; shift
[ -f "$SRC" ] || die "no such file: $SRC"

OUT=""
PREVIEW=0
while [ $# -gt 0 ]; do
  case "$1" in
    --crop)     CROP="$2";     shift 2 ;;
    --autocrop) AUTOCROP=1;    shift ;;
    --ramp)     RAMP="$2";     shift 2 ;;
    --colors)   COLORS="$2";   shift 2 ;;
    --dither)   DITHER="$2";   shift 2 ;;
    --start)    START="$2";    shift 2 ;;
    --duration) DURATION="$2"; shift 2 ;;
    --fps)      FPS="$2";      shift 2 ;;
    --width)    WIDTH="$2";    shift 2 ;;
    --speed)    SPEED="$2";    shift 2 ;;
    --out)      OUT="$2";      shift 2 ;;
    --preview)  PREVIEW=1;     shift ;;
    *) die "unknown option '$1'" ;;
  esac
done

# -ss BEFORE -i seeks by keyframe and is near-instant; after -i it decodes
# every frame up to the mark. Accurate enough here because we re-encode.
#
# Expanded as ${SEEK[@]+"${SEEK[@]}"} at every use site, not the obvious
# "${SEEK[@]}": macOS ships bash 3.2, where expanding an EMPTY array under
# `set -u` is an unbound-variable error rather than nothing. Both arrays are
# empty whenever --start/--duration are omitted, which is the common case.
SEEK=()
[ -n "$START" ]    && SEEK=(-ss "$START")
LIMIT=()
[ -n "$DURATION" ] && LIMIT=(-t "$DURATION")

# ------------------------------------------------------------------- probe --
if [ "$MODE" = "probe" ]; then
  echo "== $SRC"
  ffprobe -v error -select_streams v:0 \
    -show_entries stream=width,height,duration,avg_frame_rate,codec_name \
    -of default=noprint_wrappers=1 "$SRC"

  sheet="$OUT_DIR/_contact.png"
  # One frame every 4s, tiled 4 wide. Enough to find the beat you want
  # without decoding the whole take into memory.
  ffmpeg -y -v error -i "$SRC" \
    -vf "fps=1/4,scale=560:-1,tile=4x3" -frames:v 1 "$sheet"
  echo
  echo "wrote $sheet"
  echo
  echo "Next: pick the region you want from the sheet, then measure it in"
  echo "SOURCE pixels and pass it as --crop W:H:X:Y (X,Y = top-left corner)."
  echo "Sanity check a crop before committing to a whole gif:"
  echo "  $0 still \"$SRC\" --crop W:H:X:Y --start 20"
  exit 0
fi

# --------------------------------------------------------------- filtering --
# Order matters: crop in source pixels FIRST, then scale. Reversing them
# would mean measuring the crop against a resolution that does not exist yet.
if [ "$AUTOCROP" = "1" ]; then
  [ -n "$CROP" ] && die "--autocrop and --crop are mutually exclusive"
  CROP="$(autocrop "${START:-5}")"
  [ -n "$CROP" ] || die "--autocrop found no crop (is the source already tight?)"
  echo "autocrop: $CROP"
fi

chain=""
[ -n "$CROP" ] && chain="crop=$CROP"

add_filter() { [ -n "$chain" ] && chain="$chain,$1" || chain="$1"; }

# -------------------------------------------------------------------- still --
if [ "$MODE" = "still" ]; then
  if [ "$PREVIEW" = "1" ]; then
    # Workshop preview is exactly 640x360. Scale to cover, then centre-crop,
    # so an off-ratio source is never letterboxed or squashed.
    add_filter "scale=640:360:force_original_aspect_ratio=increase:flags=lanczos"
    add_filter "crop=640:360"
  else
    add_filter "scale=$WIDTH:-2:flags=lanczos"
  fi
  dest="${OUT:-$OUT_DIR/_still.png}"
  ffmpeg -y -v error ${SEEK[@]+"${SEEK[@]}"} -i "$SRC" -vf "$chain" -frames:v 1 "$dest"
  echo "wrote $dest  ($(ffprobe -v error -select_streams v:0 \
    -show_entries stream=width,height -of csv=p=0:s=x "$dest"))"
  exit 0
fi

# ---------------------------------------------------------------------- gif --
# setpts speeds the clip up by dividing timestamps; do it BEFORE fps so the
# frame decimation happens on the sped-up timeline and we do not pay for
# frames that are about to be dropped.
if [ -n "$RAMP" ]; then
  # One pass of the source, split N ways; each copy trimmed to its own window
  # and given its own setpts divisor, then concatenated back into one stream.
  #
  # setpts is (PTS-STARTPTS)/speed, not PTS/speed: trim keeps the SOURCE
  # timestamps, so a segment starting at 23s would otherwise begin 23/speed
  # seconds into the output and concat would leave a gap the length of
  # everything before it. Subtracting STARTPTS rebases each segment to zero.
  #
  # fps and scale run AFTER the concat so frame decimation happens on the
  # final timeline — decimating per segment would drop a different fraction
  # out of each one and the speed changes would land off the beat.
  [ "$SPEED" = "1" ] || die "--ramp and --speed are mutually exclusive"
  [ -z "$START$DURATION" ] || die "--ramp carries its own timing; drop --start/--duration"

  IFS=',' read -ra SEGMENTS <<< "$RAMP"
  seg_count=${#SEGMENTS[@]}
  [ "$seg_count" -ge 1 ] || die "--ramp needs at least one start:end:speed segment"

  graph="[0:v]"
  [ -n "$chain" ] && graph="[0:v]$chain,"
  graph="${graph}split=$seg_count"
  idx=0
  while [ "$idx" -lt "$seg_count" ]; do
    graph="${graph}[s$idx]"
    idx=$((idx + 1))
  done
  graph="${graph};"

  concat_inputs=""
  idx=0
  total=0
  for segment in "${SEGMENTS[@]}"; do
    IFS=':' read -r seg_start seg_end seg_speed <<< "$segment"
    [ -n "$seg_start" ] && [ -n "$seg_end" ] && [ -n "$seg_speed" ] \
      || die "bad ramp segment '$segment' (want start:end:speed)"
    graph="${graph}[s$idx]trim=${seg_start}:${seg_end},setpts=(PTS-STARTPTS)/${seg_speed}[v$idx];"
    concat_inputs="${concat_inputs}[v$idx]"
    total=$(echo "$total + ($seg_end - $seg_start) / $seg_speed" | bc -l)
    idx=$((idx + 1))
  done
  graph="${graph}${concat_inputs}concat=n=${seg_count}:v=1,fps=$FPS,scale=$WIDTH:-2:flags=lanczos"
  chain="$graph"

  printf 'ramp: %s segments, %.1fs of output\n' "$seg_count" "$total"
else
  if [ "$SPEED" != "1" ]; then
    add_filter "setpts=PTS/$SPEED"
  fi
  add_filter "fps=$FPS"
  add_filter "scale=$WIDTH:-2:flags=lanczos"
fi

if [ "$MODE" = "mp4" ]; then
  dest="${OUT:-$OUT_DIR/_demo.mp4}"
  # yuv420p rather than ffmpeg's preferred yuv444p: every browser and every
  # phone decodes 420, and several decode nothing else. faststart moves the
  # index to the head so it plays before the whole file has downloaded.
  ffmpeg -y -v error ${SEEK[@]+"${SEEK[@]}"} ${LIMIT[@]+"${LIMIT[@]}"} -i "$SRC" \
    -lavfi "$chain" -c:v libx264 -crf 23 -pix_fmt yuv420p -an \
    -movflags +faststart "$dest"
  bytes=$(wc -c < "$dest" | tr -d ' ')
  echo "wrote $dest"
  printf '  %s, %.1f MB\n' \
    "$(ffprobe -v error -select_streams v:0 -show_entries stream=width,height \
       -of csv=p=0:s=x "$dest")" \
    "$(echo "scale=2; $bytes/1048576" | bc)"
  exit 0
fi

dest="${OUT:-$OUT_DIR/_demo.gif}"
palette="$(mktemp -t footage-palette).png"
trap 'rm -f "$palette"' EXIT

# Two passes. A single-pass gif gets ffmpeg's fixed 256-colour web palette and
# bands badly on RimWorld's flat carpet fills; palettegen builds a palette from
# THIS clip's actual colours.
#
# stats_mode=diff weights the palette toward pixels that CHANGE between frames.
# The camera is locked off and most of the frame is static floor, so a global
# palette would spend its 256 slots on carpet and leave none for the pawns —
# which are the entire subject.
# -lavfi rather than -vf: a --ramp chain carries its own stream labels, which
# -vf cannot parse. Equivalent for the simple case (an unlabelled chain reads
# from input 0 either way).
ffmpeg -y -v error ${SEEK[@]+"${SEEK[@]}"} ${LIMIT[@]+"${LIMIT[@]}"} -i "$SRC" \
  -lavfi "$chain,palettegen=max_colors=$COLORS:stats_mode=diff" "$palette"

ffmpeg -y -v error ${SEEK[@]+"${SEEK[@]}"} ${LIMIT[@]+"${LIMIT[@]}"} -i "$SRC" -i "$palette" \
  -lavfi "$chain[x];[x][1:v]paletteuse=dither=$DITHER:diff_mode=rectangle" \
  -loop 0 "$dest"

# gifsicle squeezes the frame-to-frame deltas harder than ffmpeg's writer and
# --lossy lets it merge near-identical pixels. Optional: skipped with a note
# rather than failing, so the script still works on a machine without it.
if command -v gifsicle >/dev/null; then
  gifsicle -O3 --lossy="$LOSSY" "$dest" -o "$dest.opt" && mv "$dest.opt" "$dest"
else
  echo "  (gifsicle not installed — 'brew install gifsicle' for a further ~15%)"
fi

bytes=$(wc -c < "$dest" | tr -d ' ')
mb=$(echo "scale=1; $bytes/1048576" | bc)
dims=$(ffprobe -v error -select_streams v:0 -show_entries stream=width,height \
  -of csv=p=0:s=x "$dest")
frames=$(ffprobe -v error -select_streams v:0 -count_frames \
  -show_entries stream=nb_read_frames -of csv=p=0 "$dest")

echo "wrote $dest"
echo "  $dims, $frames frames, ${mb} MB"

if [ "$(echo "$mb > $SIZE_LIMIT_MB" | bc)" = "1" ]; then
  echo
  echo "  OVER ${SIZE_LIMIT_MB} MB — Steam will reject it. Cheapest fixes, in order:"
  echo "    --fps 15        fewer frames is the biggest single saving"
  echo "    --duration N    trim to the beat that matters"
  echo "    --speed 2       same content, half the frames"
  echo "    --width 560     last resort; costs the detail you filmed for"
fi
