#!/usr/bin/env bash
#
# Run the lifecycle harness end to end, in an ISOLATED game instance.
#
#   devtools/run-harness.sh          # minimal mod list — the iteration loop
#   devtools/run-harness.sh --full   # your real mod list, copied — the release gate
#
# Builds Release, launches RimWorld with -quicktest -shiftchange-harness against
# a throwaway save-data folder, waits for the game to run every case and quit
# itself, prints the report. Exits non-zero if any case failed.
#
# ISOLATION — the point of this script, and why it is not simpler
#
# RimWorld takes `-savedatafolder=<dir>`, and ConfigFolderPath sits under it
# (GenFilePaths.cs:93-110, :179) — so the test instance gets its own
# ModsConfig.xml, its own Saves/, its own Prefs. Unity's own `-logfile` moves
# Player.log too. Nothing under ~/Library/Application Support/RimWorld or
# ~/Library/Logs is read or written.
#
# This replaced a version that swapped the real ModsConfig.xml back and forth.
# That worked, but it edited the live installation to run a test, and the day
# something went wrong mid-run it would have been the player's mod list.
#
# IT WILL NOT TOUCH A RUNNING GAME
#
# If RimWorld is running, this refuses and stops. It does not kill it, and
# nobody should reach for `pkill` to get past it: that instance is somebody's
# colony with unsaved progress in it. Ask, then quit it by hand.
#
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="/Users/alexbeverage/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app"
LIVE_CONFIG="/Users/alexbeverage/Library/Application Support/RimWorld/Config/ModsConfig.xml"
TESTDATA="$REPO/dist/testdata"
LOG="$TESTDATA/Player.log"
PROC="RimWorld by Ludeon Studios"
# Generous, because it has to cover the slow case: --alongside a live colony
# has been measured at 300s, and once at more than 600s. A run that is going to
# pass takes ~20s, so a high ceiling costs nothing except when something is
# genuinely wrong.
TIMEOUT=1200

# Harmony, Core, Odyssey (the outfit stand is Odyssey content) and us. Vanilla
# Apparel Expanded is deliberately absent: the fixture falls back to vanilla
# apparel, displaces two garments instead of one, and drops the VEF Core
# dependency VAE drags in.
MINIMAL_MODS=(
  brrainz.harmony
  ludeon.rimworld
  ludeon.rimworld.odyssey
  mrbeverage.shiftchange
)

FULL=0
ALONGSIDE=0
for arg in "$@"
do
  case "$arg" in
    --full) FULL=1 ;;
    --alongside) ALONGSIDE=1 ;;
    *) printf 'unknown option: %s (--full | --alongside)\n' "$arg" >&2; exit 2 ;;
  esac
done

die() { printf 'error: %s\n' "$1" >&2; exit 1; }

# The instance this script starts is tracked by PID and is the only one it ever
# waits on or signals. Another instance already running is somebody's colony
# with unsaved progress in it — the safe default is to stop, and `--alongside`
# is the deliberate opt-in for when that has been confirmed free or the machine
# can carry both.
#
# Whatever happens, this script never kills a game it did not start. If you are
# about to reach for `pkill` to get past this message: don't. Ask.
if pgrep -x "$PROC" >/dev/null && [ "$ALONGSIDE" = "0" ]
then
  die "RimWorld is already running, and this script will not touch it.
       Confirm that instance is free and quit it by hand, or pass --alongside
       to start a second, fully isolated one beside it."
fi

# The dll the game loads is the one on disk, not the one in your editor.
dotnet build "$REPO/Source/ShiftChange/ShiftChange.csproj" -c Release >/dev/null \
  || die "Release build failed — fix that first"

rm -rf "$TESTDATA"
mkdir -p "$TESTDATA/Config"

if [ "$FULL" = "1" ]
then
  [ -f "$LIVE_CONFIG" ] || die "no live ModsConfig.xml to copy from"
  # Read-only copy. The live file is never written.
  cp "$LIVE_CONFIG" "$TESTDATA/Config/ModsConfig.xml"
  printf 'mod list: your development list (copied, not swapped)\n'
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
  } > "$TESTDATA/Config/ModsConfig.xml"
  xmllint --noout "$TESTDATA/Config/ModsConfig.xml" || die "generated mod list is not well-formed"
  printf 'mod list: minimal (%s mods, isolated)\n' "${#MINIMAL_MODS[@]}"
fi

printf 'save data: %s\n' "$TESTDATA"
printf 'launching…\n'

# The binary directly, not `open`: `open` returns before the child exists and
# gives no PID back, so the only way to wait would be "is ANY RimWorld running",
# which is precisely the check that cannot tell this instance from someone's
# colony. Launching it here makes $! ours, and ours alone.
"$APP/Contents/MacOS/$PROC" -quicktest -shiftchange-harness \
  "-savedatafolder=$TESTDATA" -logfile "$LOG" >/dev/null 2>&1 &
GAME_PID=$!
printf 'pid: %s\n' "$GAME_PID"

# Only ever this pid. If the run times out we stop OUR instance and leave every
# other one alone.
elapsed=0
until [ "$elapsed" -ge "$TIMEOUT" ]
do
  sleep 5
  elapsed=$((elapsed + 5))
  kill -0 "$GAME_PID" 2>/dev/null || break
done

if kill -0 "$GAME_PID" 2>/dev/null
then
  kill "$GAME_PID" 2>/dev/null || true
  die "timed out after ${TIMEOUT}s; stopped pid $GAME_PID — check $LOG"
fi
printf 'game exited after ~%ss\n\n' "$elapsed"

[ -f "$LOG" ] || die "no log at $LOG — did -logfile take?"
grep -q "harness auto-run" "$LOG" \
  || die "the harness never ran — is -shiftchange-harness still wired up? See $LOG"

sed -n '/\[ShiftChange\] lifecycle harness/,/harness auto-run/p' "$LOG"

grep -q "harness auto-run: PASSED" "$LOG"
