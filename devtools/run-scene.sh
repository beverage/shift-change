#!/usr/bin/env bash
#
# Launch an INTERACTIVE, fully isolated RimWorld instance for driving the
# debug stages by hand — the observation twin of run-harness.sh.
#
#   devtools/run-scene.sh              # minimal four-mod list, refuses if a game runs
#   devtools/run-scene.sh --alongside  # start it BESIDE a running game (deliberate opt-in)
#   devtools/run-scene.sh --full       # your own mod list, copied (not swapped)
#   devtools/run-scene.sh --media      # the badge-free filming build
#
# Builds DEBUG by default, because the stages only exist under SCENES and
# Release does not define it — a Release build here launches a game whose
# debug menu has no stages in it at all. `--media` selects the Media config
# instead: same SCENES fixtures, but no ECR instrumentation and no lab badge,
# which Patch_ModeBadge otherwise draws into every frame of the footage.
#
# run-harness.sh stays on Release, deliberately: its whole value is asserting
# against the assembly players install.
#
# Generates an isolated save-data folder with the minimal mod
# list, seeds Prefs (dev mode ON, run-in-background ON, master volume 0 so it
# does not blare over the live game), launches with -quicktest, prints the PID
# and log path, and EXITS — the instance is yours to drive. In-game: Debug
# actions → Shift Change → Build pool room stage (or the demo/preview stages).
#
# ISOLATION is run-harness.sh's, verbatim: -savedatafolder gives the instance
# its own ModsConfig.xml, Saves and Prefs (GenFilePaths.cs:93-110), -logfile
# moves Player.log. Nothing under ~/Library/Application Support/RimWorld or
# ~/Library/Logs is read or written.
#
# IT WILL NOT TOUCH A RUNNING GAME. Without --alongside it refuses to start
# while any RimWorld runs; with it, it starts a second instance and never
# signals anything but the PID it spawned. Do not reach for pkill — a running
# instance is somebody's colony.
#
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="/Users/alexbeverage/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app"
LIVE_CONFIG="/Users/alexbeverage/Library/Application Support/RimWorld/Config/ModsConfig.xml"
SCENEDATA="$REPO/dist/scenedata"
LOG="$SCENEDATA/Player.log"
PROC="RimWorld by Ludeon Studios"

MINIMAL_MODS=(
  brrainz.harmony
  ludeon.rimworld
  ludeon.rimworld.odyssey
  mrbeverage.shiftchange
)

FULL=0
ALONGSIDE=0
BRIDGE=0
CONFIG=Debug
for arg in "$@"
do
  case "$arg" in
    --full) FULL=1 ;;
    --alongside) ALONGSIDE=1 ;;
    --media) CONFIG=Media ;;
    --bridge) BRIDGE=1 ;;
    *) printf 'unknown option: %s (--full | --alongside | --media | --bridge)\n' "$arg" >&2; exit 2 ;;
  esac
done

die() { printf 'error: %s\n' "$1" >&2; exit 1; }

# --bridge appends the RimBridgeServer FORK (mrbeverage.rimbridgeserverfork),
# which carries the input tools the released bridge lacks. It builds the scene
# list, so it cannot ride a copied live one.
if [ "$BRIDGE" = "1" ] && [ "$FULL" = "1" ]
then
  die "--bridge builds the scene list; it cannot ride the copied live list"
fi
if [ "$BRIDGE" = "1" ]
then
  MINIMAL_MODS+=(mrbeverage.rimbridgeserverfork)
fi

if pgrep -x "$PROC" >/dev/null && [ "$ALONGSIDE" = "0" ]
then
  die "RimWorld is already running, and this script will not touch it.
       Quit it by hand, or pass --alongside to start a second, fully
       isolated instance beside it."
fi

# The dll the game loads is the one on disk, not the one in your editor.
#
# BOTH configs write the same Assemblies/ShiftChange.dll, which is also the
# committed artifact — so a session that ends here leaves a SCENES build on
# disk. Rebuild Release before committing or publishing; check-shipped-dll.py
# is what catches it if you forget.
dotnet build "$REPO/Source/ShiftChange/ShiftChange.csproj" -c "$CONFIG" >/dev/null \
  || die "$CONFIG build failed — fix that first"
printf 'build: %s\n' "$CONFIG"

rm -rf "$SCENEDATA"
mkdir -p "$SCENEDATA/Config"

if [ "$FULL" = "1" ]
then
  [ -f "$LIVE_CONFIG" ] || die "no live ModsConfig.xml to copy from"
  cp "$LIVE_CONFIG" "$SCENEDATA/Config/ModsConfig.xml"
  printf 'mod list: yours, copied (not swapped)\n'
else
  version="$(grep -m1 '<version>' "$LIVE_CONFIG" 2>/dev/null || printf '  <version>1.6.4871 rev595</version>')"
  {
    printf '<?xml version="1.0" ?>\n<ModsConfigData>\n'
    printf '%s\n  <activeMods>\n' "$version"
    for mod in "${MINIMAL_MODS[@]}"
    do
      printf '    <li>%s</li>\n' "$mod"
    done
    printf '  </activeMods>\n  <knownExpansions>\n'
    printf '    <li>ludeon.rimworld.odyssey</li>\n'
    printf '  </knownExpansions>\n</ModsConfigData>\n'
  } > "$SCENEDATA/Config/ModsConfig.xml"
  xmllint --noout "$SCENEDATA/Config/ModsConfig.xml" || die "generated mod list is not well-formed"
  printf 'mod list: minimal (%s mods, isolated)\n' "${#MINIMAL_MODS[@]}"
fi

# Seed Prefs so the instance is drivable with zero setup: dev mode on (the
# debug stages live behind it), keep simulating while the live game has
# focus, and stay silent beside it. Field names are PrefsData's own
# (PrefsData.cs:10,68,108); anything omitted takes its default.
#
# WINDOWED AT A FIXED SIZE, and that is a capture requirement rather than a
# preference. screenshot_cell_rect crops to cell bounds, so a captured cell's
# pixel size is screenHeight / (2 * rootSize) — fullscreen on whatever display
# is attached makes every shoot's output dimensions a property of the monitor,
# and a card re-shot on a different screen would not composite against the
# numbers written down for the last one. Pin it and the arithmetic is stable.
# (Windowed also sidesteps the fullscreen-transition mess that stalls a second
# instance; see run-harness.sh.)
{
  printf '<?xml version="1.0" encoding="utf-8"?>\n'
  printf '<PrefsData>\n'
  printf '  <devMode>True</devMode>\n'
  printf '  <runInBackground>True</runInBackground>\n'
  printf '  <volumeMaster>0</volumeMaster>\n'
  printf '  <screenWidth>1920</screenWidth>\n'
  printf '  <screenHeight>1080</screenHeight>\n'
  printf '  <fullscreen>False</fullscreen>\n'
  printf '</PrefsData>\n'
} > "$SCENEDATA/Config/Prefs.xml"

# The GABS env handshake, read by Lib.GAB.GabpServerBuilder, FORCES the port
# per instance. It has to: the bridge's standalone default is 5174 and a live
# game carrying the bridge already owns it, and apparel-painter's scene bridge
# took 5175 — so this one is 5176. A fixed token beats scraping the log for a
# fresh one, and nothing here is reachable off-host.
if [ "$BRIDGE" = "1" ]
then
  export GABP_SERVER_PORT=5176
  export GABP_TOKEN=shiftchange-scene-bridge
fi

printf 'save data: %s\n' "$SCENEDATA"
printf 'launching…\n'

# The binary directly, not `open`: $! must be OUR pid and nobody else's.
"$APP/Contents/MacOS/$PROC" -quicktest \
  "-savedatafolder=$SCENEDATA" -logfile "$LOG" >/dev/null 2>&1 &
GAME_PID=$!

# Ten seconds of grace: an instantly dead instance (bad dll, bad config)
# should be reported here, not discovered as a missing window.
sleep 10
if ! kill -0 "$GAME_PID" 2>/dev/null
then
  die "instance exited within 10s — check $LOG"
fi

printf 'pid: %s (leave it to the player; this script does not manage it)\n' "$GAME_PID"
printf 'log: %s\n' "$LOG"
if [ "$BRIDGE" = "1" ]
then
  printf 'bridge: 127.0.0.1:%s  token %s\n' "$GABP_SERVER_PORT" "$GABP_TOKEN"
  printf 'bridge: devtools/bridge/gabp.py %s %s   # lists the tool surface\n' \
    "$GABP_SERVER_PORT" "$GABP_TOKEN"
fi
printf 'in-game: enable nothing — dev mode is pre-seeded. Debug actions → Shift Change → Dev tools…\n'
