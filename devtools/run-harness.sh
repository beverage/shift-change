#!/usr/bin/env bash
#
# Run the lifecycle harness end to end, in an ISOLATED game instance.
#
#   devtools/run-harness.sh          # a four-mod list — the iteration loop
#   devtools/run-harness.sh --full   # your own mod list, copied — the release gate
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
# Derived from $HOME, never written out. These were absolute paths carrying a
# home directory, in a public repository — and they made the scripts run on
# exactly one machine. Override either for a non-default Steam library.
APP="${RIMWORLD_APP:-$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app}"
LIVE_CONFIG="${RIMWORLD_CONFIG:-$HOME/Library/Application Support/RimWorld/Config/ModsConfig.xml}"
TESTDATA="$REPO/dist/testdata"
LOG="$TESTDATA/Player.log"
PROC="RimWorld by Ludeon Studios"
# Generous, because it has to cover the slow case: --alongside a live colony
# has been measured at 300s, and once at more than 600s. A run that is going to
# pass takes ~20s, so a high ceiling costs nothing except when something is
# genuinely wrong.
#
# The usual cause of a multi-minute run is NOT slowness: RimWorld's loading
# screen stalls while its window is backgrounded, and resumes when it comes
# forward. A four-mod run was observed at 715s that way (2026-08-17). Leave the
# window in front, or expect the wall clock to measure your attention rather
# than the game.
TIMEOUT=1200

# How long to allow for the game to reach RIMWORLD'S OWN startup, as opposed to
# finishing the run. Separate from TIMEOUT because the two failures are nothing
# alike, and conflating them cost a 20-minute wait to learn a fact that was
# available after sixty seconds (2026-09-04).
#
# The stall is entirely characteristic: Unity's preamble completes, the log
# stops around line 49 in the PhysX/asset-unload block, and the process sits
# near 0% CPU forever. It never recovers on its own, so waiting out TIMEOUT
# learns nothing that the first minute did not already say.
#
# WHAT WE GREP FOR, AND TWO WRONG ANSWERS BEFORE THIS ONE.
#
# Not the version banner: `RimWorld 1.6.4871 rev597` IS printed before the
# stall (line 21 of a stalled log), so it looks like startup and is not.
#
# Not `Loaded assemblies` either, though it sits at line 68 of a large
# modlist's log and looks perfect there. THAT LINE IS PRINTED BY A MOD, not by
# RimWorld — it does not exist on the four-mod minimal list, so it reported a
# PASSING run as a stall the first time it ran (2026-09-04). Picking a marker
# off a heavily-modded log and calling it native is the trap; the control has
# to be a log from the mod list the check will actually run against.
#
# `with mods:` is Verse's own, from both "Initializing new game with mods:" and
# "Loading game from file … with mods:", so it covers the -quicktest path and
# the save-load cases alike, on any mod list. Verified present in a minimal-list
# log AND the full-list one, and absent from every stalled log.
STARTUP_GRACE=120

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

# THE BUILD IS NOT THE THING THE GAME LOADS.
#
# -savedatafolder isolates Config, Saves and Prefs, but NOT the mod itself:
# the game reads Mods/ShiftChange out of the app bundle, and whatever that
# resolves to is what gets tested. Three ways it has pointed somewhere else:
# a release-staging COPY left in place (twice, and a green run silently
# asserted against pre-fix bits), and a git worktree, where the build lands in
# one checkout while the symlink still names another.
#
# Without this check the failure is a PASS, which is the worst shape a test
# result can take. Compare canonical paths and refuse.
MODS_ENTRY="$APP/Mods/ShiftChange"
[ -e "$MODS_ENTRY" ] || die "no Mods/ShiftChange entry — the game cannot load this mod at all"
realpath_of() { python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$1"; }
ENTRY_REAL="$(realpath_of "$MODS_ENTRY")"
REPO_REAL="$(realpath_of "$REPO")"
if [ "$ENTRY_REAL" != "$REPO_REAL" ]
then
  die "the game would NOT load the build this script just made.

       built:  $REPO_REAL
       loads:  $ENTRY_REAL

       Point Mods/ShiftChange at the checkout under test and run again. If the
       entry is a real directory rather than a symlink, it is release-staging
       residue — park it, do not delete it, and restore the symlink."
fi
printf 'load path: %s\n' "$ENTRY_REAL"

rm -rf "$TESTDATA"
mkdir -p "$TESTDATA/Config"

if [ "$FULL" = "1" ]
then
  [ -f "$LIVE_CONFIG" ] || die "no live ModsConfig.xml to copy from"
  # Read-only copy. The live file is never written.
  cp "$LIVE_CONFIG" "$TESTDATA/Config/ModsConfig.xml"
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
  } > "$TESTDATA/Config/ModsConfig.xml"
  xmllint --noout "$TESTDATA/Config/ModsConfig.xml" || die "generated mod list is not well-formed"
  printf 'mod list: minimal (%s mods, isolated)\n' "${#MINIMAL_MODS[@]}"
fi

# SEED Prefs.xml, AND THE REASON IS THE STALL.
#
# A fresh -savedatafolder has no Prefs.xml, so the test instance starts on
# RimWorld's DEFAULTS — and the default is fullscreen. On macOS that instance
# then asks for a fullscreen space it cannot have, logs
#
#   setPresentationOptions called with NSApplicationPresentationFullScreen
#   when there is no visible fullscreen window; this call will be ignored
#
# and is left with no compositing window at all. LongEventHandler advances the
# loading screen off the main-thread update, so a window that never composites
# never progresses: the log stops around line 50 and the process sits near 0%
# CPU until something kills it.
#
# THIS IS NOT A PROVEN FIX, and the first version of this comment claimed it
# was. Seeding windowed prefs was followed by one clean pass and then, on the
# very next run, an identical stall (2026-09-04, 1 for 2). The pass also came
# straight after an attempt where the window was being fronted by hand, so
# focus was never controlled for and the two explanations remain tangled. It is
# kept because windowed-and-muted is the right shape for a throwaway test
# instance regardless, and because it plausibly removes one failure mode — not
# because the stall is understood. See the tracker item for the live state.
#
# Written into the throwaway folder
# that is rm -rf'd at the top of every run; the real Prefs.xml is never read or
# touched. Only the keys that matter are set — RimWorld fills in every absent
# field with its own default.
{
  printf '<?xml version="1.0" encoding="utf-8"?>\n<PrefsData>\n'
  printf '  <screenWidth>1280</screenWidth>\n'
  printf '  <screenHeight>720</screenHeight>\n'
  printf '  <fullscreen>False</fullscreen>\n'
  printf '  <volumeMaster>0</volumeMaster>\n'
  printf '</PrefsData>\n'
} > "$TESTDATA/Config/Prefs.xml"
xmllint --noout "$TESTDATA/Config/Prefs.xml" || die "generated Prefs.xml is not well-formed"
printf 'display: windowed 1280x720 (fullscreen stalls a second instance)\n'

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
started=0
until [ "$elapsed" -ge "$TIMEOUT" ]
do
  sleep 5
  elapsed=$((elapsed + 5))

  if [ "$started" -eq 0 ] && grep -q "with mods:" "$LOG" 2>/dev/null
  then
    started=1
    printf 'reached RimWorld startup after ~%ss\n' "$elapsed"
  fi

  # Short-circuit the stall. Bail here rather than at TIMEOUT: a game that has
  # not reached its own startup by now is blocked, not slow, and no stalled run
  # has ever recovered.
  if [ "$started" -eq 0 ] && [ "$elapsed" -ge "$STARTUP_GRACE" ]
  then
    kill "$GAME_PID" 2>/dev/null || true
    die "stalled before RimWorld started (no startup within ${STARTUP_GRACE}s).

       This is NOT a mod-wiring problem. Unity's preamble finished and the game
       stopped before loading any assembly, which is what happens when its
       window never comes to the front — the loading screen advances off the
       main-thread update, so a window that is not compositing never progresses.

       Bring the new RimWorld window to the front and run again. Note that
       --alongside cannot do this for you: the instance already running owns the
       foreground.

       Log: $LOG"
  fi

  kill -0 "$GAME_PID" 2>/dev/null || break
done

if kill -0 "$GAME_PID" 2>/dev/null
then
  kill "$GAME_PID" 2>/dev/null || true
  die "timed out after ${TIMEOUT}s; stopped pid $GAME_PID — check $LOG"
fi
printf 'game exited after ~%ss\n\n' "$elapsed"

[ -f "$LOG" ] || die "no log at $LOG — did -logfile take?"

# TWO FAILURES, TWO MESSAGES. These were one line until 2026-09-04, and it
# blamed mod wiring for a stall that happens before any mod is loaded — which
# misdirected an entire debugging session. The log already knows which happened.
grep -q "with mods:" "$LOG" \
  || die "the game exited without ever reaching RimWorld's own startup — the
       fullscreen stall, not a wiring problem. See $LOG"
grep -q "harness auto-run" "$LOG" \
  || die "the game started but the harness never ran — is -shiftchange-harness
       still wired up? See $LOG"

sed -n '/\[ShiftChange\] lifecycle harness/,/harness auto-run/p' "$LOG"

grep -q "harness auto-run: PASSED" "$LOG"
