#!/bin/bash
#
# Rasterises the per-node pages written by `card-mockup.py --export` into the
# PNGs the Steam description points at. Run from media/.
#
#   ./card-mockup.py --export
#   ./card-export.sh
#
# WHY A BROWSER: the cards are CSS. Text reflows, so a card's height is not
# knowable until it is laid out — which is the whole reason they are not built
# with ImageMagick compositing like card-triptych.sh. Chrome lays it out, we
# photograph the result.
#
# HOW THE HEIGHT IS SOLVED: it is not. The page renders TRANSPARENT behind the
# node (EXPORT_CSS plus --default-background-color=00000000), the window is set
# far taller than any card, and `-trim` then snaps to the card's own outer edge.
# Nothing has to know how tall a card came out, so copy can grow a line without
# anyone re-measuring anything.
#
# 2x, because Steam renders a description image at ~640 and these are 640 CSS
# pixels wide: shooting at 1280 means the browser downsamples our text rather
# than the page upsampling it. Flattened onto the card's own panel colour at the
# end — the trim leaves transparent corners otherwise, and Steam does not
# composite PNG alpha predictably against its own background.
set -eu

CHROME=${CHROME:-/Applications/Google Chrome.app/Contents/MacOS/Google Chrome}
SRC=${SRC:-cards/_export}
OUT=${OUT:-cards}
SCALE=${SCALE:-2}
PANEL=${PANEL:-#1f242a}

test -x "$CHROME" || {
  echo "no Chrome at $CHROME — set CHROME=" >&2
  exit 1
}

# cards/parts/ is gitignored, so a fresh clone has none of it. Without this the
# export still succeeds and simply omits every screenshot, which is a far worse
# outcome than stopping: the cards look deliberate and are missing their subject.
test -d "${PARTS:-cards/parts}" || {
  echo "no ${PARTS:-cards/parts} — run card-crops.sh first" >&2
  exit 1
}

# Chrome needs an absolute file:// URL, so resolve these before the loop rather
# than splicing $PWD in per iteration.
case "$SRC" in /*) ;; *) SRC="$PWD/$SRC" ;; esac
case "$OUT" in /*) ;; *) OUT="$PWD/$OUT" ;; esac

for page in "$SRC"/*.html
do
  name=$(basename "$page" .html)
  "$CHROME" --headless --disable-gpu --hide-scrollbars \
            --force-device-scale-factor=$SCALE \
            --default-background-color=00000000 \
            --window-size=640,4000 \
            --screenshot="$SRC/$name.raw.png" \
            "file://$page" 2>/dev/null
  magick "$SRC/$name.raw.png" -trim +repage \
         -background "$PANEL" -alpha remove -alpha off \
         -strip -define png:compression-level=9 "$OUT/$name.png"
  rm -f "$SRC/$name.raw.png"
done

magick identify "$OUT"/banner-*.png "$OUT"/card-faq.png "$OUT"/card-gizmos.png \
                "$OUT"/card-removing.png "$OUT"/card-stand-controls.png
