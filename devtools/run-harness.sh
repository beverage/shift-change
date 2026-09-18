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
# Historically the usual cause of a multi-minute run was NOT slowness: a
# backgrounded instance stalled at its loading screen, and a four-mod run was
# once observed at 715s that way (2026-08-17). That is fixed — the Prefs.xml
# block below seeds runInBackground, and an unfocused run now loads normally.
# The ceiling stays high because --full on a large list legitimately takes
# minutes.
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
# SO THE TEST IS "HAS THE LOG STOPPED GROWING", NOT "HAS IT REACHED A MARKER".
# This was elapsed-only until 2026-09-05, and it killed a perfectly healthy
# --full run at 120s: a large mod list takes minutes to reach `with mods:`,
# and that run was 761 lines deep and still printing mod banners when the
# guard shot it. The marker measures MOD-LIST SIZE as much as health, which is
# exactly what a release gate runs against. A frozen log is the signal; a
# growing one is a slow load and must be allowed to finish. STARTUP_GRACE is
# now a floor before the question is asked at all, not a deadline.
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

# How long the log must be COMPLETELY SILENT, past that floor, before the run
# is called stalled. A loading game writes constantly; the stall writes nothing
# ever again.
STALL_QUIET=60

# The seeded runInBackground value, overridable ONLY so the A/B that justified
# it can be re-run without editing this file:
#
#   HARNESS_RUN_IN_BACKGROUND=False devtools/run-harness.sh
#
# True is the shipped default and the one every ordinary run should use. See
# the Prefs.xml block below for what it does and does not explain.
RUN_IN_BACKGROUND="${HARNESS_RUN_IN_BACKGROUND:-True}"

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

# WHAT THE STALL LOOKED LIKE, PRINTED RATHER THAN ASSERTED.
#
# Every session that has debugged this paid twenty minutes to learn the log had
# stopped around line 49 — a fact available in one second. The bail-out message
# used to state a cause and show no evidence for it, so the next reader had to
# reproduce the stall to see anything at all.
#
# Sampled BEFORE the kill, because `ps` needs the process alive and "blocked,
# not slow" is the entire diagnosis: a stalled instance sits near 0% CPU with
# real CPU time already banked, which is a different shape from a slow load.
#
# The frontmost reading is here because every previous record of this failure
# says "focus state unknown" or "self-reported". A run that stalls should say
# what had the foreground at the time, without anyone having to remember.
stall_evidence() {
  lines=$(wc -l < "$LOG" 2>/dev/null | tr -d ' ' || printf 0)
  # ps pads its columns, and the point of this block is that it can be read at
  # a glance. Trim to one space so the three labels line up.
  cpu=$(ps -o %cpu=,time= -p "$GAME_PID" 2>/dev/null | sed 's/^ *//;s/  */ /g' \
    || printf '(process gone)')
  [ -n "$cpu" ] || cpu='(process gone)'
  front=$(osascript -e \
    'tell application "System Events" to get name of first process whose frontmost is true' \
    2>/dev/null || printf '(unavailable)')
  printf '       log lines:    %s\n' "$lines"
  printf '       cpu / time:   %s\n' "$cpu"
  printf '       frontmost:    %s\n' "$front"
  printf '       last 5 lines:\n'
  tail -n 5 "$LOG" 2>/dev/null | sed 's/^/         | /'
}

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
#
# -p:Harness=true is what compiles the harness and -shiftchange-harness back
# in. They are NOT in a shipping build (see the configuration table in the
# csproj), so plain Release would launch a game that ignores the flag and sits
# there until the timeout. Release codegen otherwise, exactly as shipped.
dotnet build "$REPO/Source/ShiftChange/ShiftChange.csproj" -c Release -p:Harness=true >/dev/null \
  || die "harness build failed — fix that first"

# Leave Assemblies/ holding the SHIPPING dll again, whatever happens below.
# The game's Mods entry is a symlink to this checkout, so an un-swept harness
# build is what the next play session loads and what a careless `git add`
# commits. Same philosophy as the csproj's CleanDevArtifacts target: hygiene by
# construction, not by memory. It never changes this script's exit status.
restore_shipping_dll() {
  dotnet build "$REPO/Source/ShiftChange/ShiftChange.csproj" -c Release >/dev/null \
    || printf 'WARNING: Assemblies/ still holds the HARNESS build. Rebuild with:
         dotnet build Source/ShiftChange/ShiftChange.csproj -c Release\n' >&2
}
trap restore_shipping_dll EXIT

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
# and is left with no compositing window at all: the log stops around line 50
# and the process sits near 0% CPU until something kills it.
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
# runInBackground, AND WHY IT IS HERE DESPITE NOT ANSWERING THE STARTUP STALL.
#
# `runInBackground` is one of RimWorld's own preferences (Verse/PrefsData.cs:68)
# and it DEFAULTS TO FALSE — the field has no initializer, and PrefsData.Apply()
# hands it straight to Unity as `Application.runInBackground` (:153). A fresh
# -savedatafolder therefore produced a test instance with it OFF, while a real
# player's config, having been through the options screen, may well have it on.
# That was an uncontrolled difference between the instance that stalls and the
# instance that does not, and it went unnoticed through every session that
# debugged this.
#
# MEASURED 2026-09-11, and it is THE FIX. Six runs on the minimal list with
# focus deliberately held on another application for the whole of every run:
#
#   True  -> PASSED 3/3, startup in 20s
#   False -> STALLED 3/3, log frozen at 48 lines, ~0.8% CPU, killed at 120s
#
# Then --full against the real mod list, unfocused and unattended end to end:
# 35 passed, 0 failed, 3 known gaps in 166s, with the game never once holding
# the foreground. That is the whole point of the flag being here.
#
# WHY IT LANDS EARLY ENOUGH TO MATTER, because a wrong prediction was written
# in this very comment three days before the measurement. Prefs.Init() ENDS
# with an unqualified Apply() (Prefs.cs:861) — a grep for `Prefs.Apply` does not
# find it — and Prefs.Init() runs inside Root.CheckGlobalInit() (Root.cs:104),
# right after the version banner. PrefsData.Apply() then sets the flag (:153)
# and IMMEDIATELY reconfigures the resolution (:154-161). So the flag is set
# just before a Unity window/surface rebuild, which is exactly the operation
# that needs a live update loop — and exactly what the asset-unload block at
# the end of every stalled log is.
#
# Root.cs:72 sets the same flag true unconditionally, which is what the earlier
# prediction leaned on. It is TOO LATE: it sits after CheckGlobalInit() returns,
# and with the pref false an unfocused instance never gets that far.
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
  printf '  <runInBackground>%s</runInBackground>\n' "$RUN_IN_BACKGROUND"
  printf '  <volumeMaster>0</volumeMaster>\n'
  printf '</PrefsData>\n'
} > "$TESTDATA/Config/Prefs.xml"
xmllint --noout "$TESTDATA/Config/Prefs.xml" || die "generated Prefs.xml is not well-formed"
printf 'display: windowed 1280x720, muted, runInBackground=%s\n' "$RUN_IN_BACKGROUND"

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
logsize=0
quiet=0
until [ "$elapsed" -ge "$TIMEOUT" ]
do
  sleep 5
  elapsed=$((elapsed + 5))

  # Growth, sampled every tick. A load that is progressing writes on every one
  # of these; the stall writes on none.
  size=$(wc -c < "$LOG" 2>/dev/null || printf 0)
  if [ "$size" -gt "$logsize" ]
  then
    logsize=$size
    quiet=0
  else
    quiet=$((quiet + 5))
  fi

  if [ "$started" -eq 0 ] && grep -q "with mods:" "$LOG" 2>/dev/null
  then
    started=1
    printf 'reached RimWorld startup after ~%ss\n' "$elapsed"
  fi

  # Short-circuit the stall. Bail here rather than at TIMEOUT: a game whose log
  # has gone silent before its own startup is blocked, not slow, and no stalled
  # run has ever recovered. Both conditions are required — see STARTUP_GRACE.
  if [ "$started" -eq 0 ] && [ "$elapsed" -ge "$STARTUP_GRACE" ] && [ "$quiet" -ge "$STALL_QUIET" ]
  then
    evidence="$(stall_evidence)"
    kill "$GAME_PID" 2>/dev/null || true

    # A GAME THROWING EVERY FRAME ALSO GOES SILENT, AND LOOKS IDENTICAL.
    #
    # RimWorld stops logging entirely after "Reached max messages limit", so a
    # per-frame exception storm presents to a liveness check exactly as a stall
    # does. Bailing out is the right move either way — but blaming the window
    # for a mod fault sends the next reader somewhere there is nothing to find.
    # This mode was known on 2026-09-05 and went unhandled until now.
    if grep -q "Reached max messages limit" "$LOG" 2>/dev/null
    then
      die "the log went silent because RimWorld STOPPED LOGGING, not because the
       game is blocked: it hit its own message cap, which is what happens when
       something throws every frame. That is a fault in the mod list under test.
       This is NOT the focus stall, whatever the timing looks like.

$evidence
       Log: $LOG"
    fi

    die "stalled before RimWorld started (log silent ${quiet}s, ${elapsed}s in).

       This is NOT a mod-wiring problem: Unity's preamble finished and the game
       stopped before loading any assembly. A near-zero CPU reading below
       confirms it is blocked rather than slow; a busy one means look elsewhere.

$evidence
       This USED to mean the window was not frontmost, and that cause is fixed:
       the seeded Prefs.xml sets runInBackground, which makes an unfocused
       instance load normally (measured, 3/3 either way). So if you are seeing
       this, check that first — HARNESS_RUN_IN_BACKGROUND is not set to False,
       and the Prefs.xml written above really does carry the key. Fronting the
       window by hand is still a valid way to get unblocked once.

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
