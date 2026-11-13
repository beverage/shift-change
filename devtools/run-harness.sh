#!/usr/bin/env bash
#
# Run every harness case as a DevTools test, in an ISOLATED game instance.
#
#   devtools/run-harness.sh          # a four-mod list — the iteration loop
#   devtools/run-harness.sh --full   # your own mod list, copied — the release gate
#   devtools/run-harness.sh --with=khamenman.outfitstandsplus
#                                    # the four, plus one mod a case needs
#   devtools/run-harness.sh --with-after=khamenman.outfitstandsplus
#                                    # the same mod, loaded after this one
#
# Builds the shipping Release dll and the tests against it (their own assembly,
# in Tests/, which LoadFolders.xml loads only with DevTools; both ship, and
# both are committed), launches RimWorld
# 1.7 with `--pid MrBeverage.ShiftChange --test -e` against a throwaway
# save-data folder, and SmashPhil's DevTools test framework runs every case,
# one test each, then quits the game. It needs a DevTools build at Mods/DevTools that has
# SmashPhil/DevTools#17 (DEVTOOLS_NEEDS): the tests compile against that
# same dll, and DevTools joins the mod list just ahead of this mod. The verdict is
# the game's exit code read with the result line DevTools writes last in Test.log
# (0 passed, 1 a test failed, 2 nothing ran); an exit without that line is no
# verdict and exits 3. A test runner that dies on an exception stops the run at
# once. Test.log is the report.
#
# DevTools runs on RimWorld 1.7 only, which keeps Mods/ beside RimWorld.app.
# RIMWORLD_APP names the app; the default is the Steam install's.
#
# ISOLATION — the point of this script, and why it is not simpler
#
# RimWorld takes `-savedatafolder=<dir>`, and ConfigFolderPath sits under it
# (GenFilePaths.cs:93-110, :179) — so the test instance gets its own
# ModsConfig.xml, its own Saves/, its own Prefs. 1.7 has no -logfile: it writes
# its log, its caches and DevTools' Test.log under $HOME, so the game gets a HOME
# of its own at dist/testhome. Nothing under
# ~/Library/Application Support/RimWorld or the player's own logs is written.
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
# AND WITH --alongside IT STILL NEVER WRITES INTO THE DLL THAT GAME LOADED.
# Mods/ShiftChange is a symlink to this checkout, so a colony running beside
# the run loaded Assemblies/ShiftChange.dll from here, and with DevTools also
# Tests/Assemblies/ShiftChange.Tests.dll. Neither build writes those files: both
# compile into dist/build/, and deploy() swaps each one in by rename.
#
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Derived from $HOME, never written out. These were absolute paths carrying a
# home directory, in a public repository — and they made the scripts run on
# exactly one machine. Override either for a non-default Steam library.
STEAM_GAME="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld"
APP="${RIMWORLD_APP:-$STEAM_GAME/RimWorld.app}"
LIVE_CONFIG="${RIMWORLD_CONFIG:-$HOME/Library/Application Support/RimWorld/Config/ModsConfig.xml}"
TESTDATA="$REPO/dist/testdata"
# Kept between runs, so 1.7's caches stay warm; the logs in it are cleared at
# every start.
TESTHOME="$REPO/dist/testhome"
BUILD="$REPO/dist/build"
CSPROJ="$REPO/Source/ShiftChange/ShiftChange.csproj"
DLL="$REPO/Assemblies/ShiftChange.dll"
# The DevTools build the tests need: their allowances use the stack-qualified attributes that
# pull request adds.
DEVTOOLS_NEEDS="DevTools with SmashPhil/DevTools#17 (beverage/DevTools 8966779 until it merges)"
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
# ON 1.7 A FROZEN LOG IS NOT ENOUGH EITHER. A large list's load writes nothing
# between the key-binding warnings and `with mods:`, and a --full run was shot
# 84s into that silence at 97% CPU (2026-11-07). So the process must also be
# idle (idle() below): the stall sits near 1% CPU, a silent load near 100%.
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
# "Loading game from file … with mods:", so it covers DevTools' new games and
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

# --with=<packageId>, repeatable: a mod added to the minimal list, for a case
# that tests us against it and is a known gap without it. It goes in ahead of
# this mod, which sorts after the mods it patches (About.xml's loadAfter). Not
# with --full, whose list is yours as it stands. Its own dependencies are not
# followed, so name those too. Ids are written lowercase, as the game writes
# them: ModsConfig.IsActive lowercases only its argument, so a mixed-case entry
# in the file never matches.
EXTRA_MODS=()

# --with-after=<packageId>, repeatable: the same, but loaded after this mod,
# against its loadAfter. loadAfter only warns, so a hand-sorted list can load
# them this way round; this is how a case shows it works in both orders.
EXTRA_AFTER=()

FULL=0
ALONGSIDE=0
for arg in "$@"
do
  case "$arg" in
    --full) FULL=1 ;;
    --alongside) ALONGSIDE=1 ;;
    --with=?*) EXTRA_MODS+=("$(printf '%s' "${arg#--with=}" | tr '[:upper:]' '[:lower:]')") ;;
    --with-after=?*) EXTRA_AFTER+=("$(printf '%s' "${arg#--with-after=}" | tr '[:upper:]' '[:lower:]')") ;;
    *) printf 'unknown option: %s (--full | --alongside | --with=<packageId> | --with-after=<packageId>)\n' "$arg" >&2; exit 2 ;;
  esac
done

die() { printf 'error: %s\n' "$1" >&2; exit 1; }
realpath_of() { python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$1"; }

[ "$FULL" = "0" ] || [ "$(( ${#EXTRA_MODS[@]} + ${#EXTRA_AFTER[@]} ))" = "0" ] \
  || die "--with and --with-after add to the minimal list, and --full does not use it"

# 1.6's binary is "RimWorld by Ludeon Studios" in RimWorldMac.app, where DevTools
# does not run.
GAME_DIR="$(dirname "$APP")"
BIN="$APP/Contents/MacOS/RimWorld"
[ -x "$BIN" ] \
  || die "no RimWorld 1.7 binary in $APP. DevTools runs on 1.7 only; set RIMWORLD_APP
       to its RimWorld.app."
MODS_DIR="$GAME_DIR/Mods"
LOG="$TESTHOME/Library/Application Support/Ludeon Studios/RimWorld by Ludeon Studios/Logs/Player.log"
TESTLOG="$TESTHOME/Library/Application Support/ludeon.rimworld/Test.log"

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

# kill -0 also succeeds on a child that has exited and not been waited for yet,
# so a zombie counts as gone.
alive() {
  kill -0 "$1" 2>/dev/null || return 1
  case "$(ps -o stat= -p "$1" 2>/dev/null)" in
    Z*) return 1 ;;
  esac
  return 0
}

# Whether a process is using next to no CPU. The stall sits near 1%; a 1.7 load of
# a large list writes nothing for over a minute while it works at ~100%. ps reports
# a decaying average, so a process that has just gone idle reads high for a while,
# which only delays the verdict. A process ps cannot see is not idle: alive() is
# the check for that.
idle() {
  local cpu
  cpu=$(ps -o %cpu= -p "$1" 2>/dev/null | tr -d ' ')
  [ -n "$cpu" ] && [ "${cpu%%.*}" -lt 10 ]
}

# 1.7's coroutine scheduler catches an exception that escapes a coroutine, ends
# that coroutine and writes the exception to stderr only, never to Player.log.
# When the coroutine is DevTools' test runner, the game then idles with nothing
# left to quit it. These print each such exception with its first frames.
coroutine_errors() {
  grep -A8 '^\[Coroutine\]' "$TESTDATA/stderr.log" 2>/dev/null | sed 's/^/  | /'
}
runner_coroutine_died() {
  grep -A40 '^\[Coroutine\]' "$TESTDATA/stderr.log" 2>/dev/null \
    | grep -q 'DevTools\.Testing\.TestRunner\.TestRoutine'
}

# The instance this script starts is tracked by PID and is the only one it ever
# waits on or signals. Another instance already running is somebody's colony
# with unsaved progress in it — the safe default is to stop, and `--alongside`
# is the deliberate opt-in for when that has been confirmed free or the machine
# can carry both.
#
# Whatever happens, this script never kills a game it did not start. If you are
# about to reach for `pkill` to get past this message: don't. Ask.
#
# 1.6's process is "RimWorld by Ludeon Studios" and 1.7's is "RimWorld"; either
# one is somebody's game, whichever layout this run uses.
if { pgrep -x "RimWorld by Ludeon Studios" >/dev/null || pgrep -x RimWorld >/dev/null; } \
  && [ "$ALONGSIDE" = "0" ]
then
  die "RimWorld is already running, and this script will not touch it.
       Confirm that instance is free and quit it by hand, or pass --alongside
       to start a second, fully isolated one beside it."
fi

pgrep -x steam_osx >/dev/null \
  || die "Steam is not running, and without it no Workshop mod loads, Harmony included."

# The build compiles against the DevTools dll the game will load, for the same
# reason the load-path check below exists: what is tested is what the game reads.
[ -e "$MODS_DIR/DevTools" ] \
  || die "the tests need a build of $DEVTOOLS_NEEDS at $MODS_DIR/DevTools"
DEVTOOLS_DLL="$(realpath_of "$MODS_DIR/DevTools")/1.7/Assemblies/DevTools.dll"
[ -f "$DEVTOOLS_DLL" ] \
  || die "no DevTools.dll at $DEVTOOLS_DLL; build $DEVTOOLS_NEEDS"
# An older build would fail in the compiler, far from the reason.
grep -a -q WarningsAllowedFromAttribute "$DEVTOOLS_DLL" \
  || die "$DEVTOOLS_DLL has no WarningsAllowedFrom; build $DEVTOOLS_NEEDS"

# The dlls the game loads are the ones on disk, not the ones in your editor.
#
# Two builds. First the mod's plain Release build, the dll that ships: the
# tests run against it as it is. Then the tests, an assembly of their own
# (Source/ShiftChange.Tests), compiled against that dll, the DevTools dll above
# and the game this run starts. LoadFolders.xml loads their folder, Tests/,
# only while DevTools is active, so the mod list below is what turns them on.
#
# Both into dist/build/, not the folders the game reads: -p:OutputPath moves
# the mod build's copy step and the csproj's CleanDevArtifacts sweep together,
# and -p:TestsDir the tests' copy, so nothing here has touched the load path
# yet. The dll checks are what prove the redirects took. An OutDir set anywhere
# beats OutputPath, and a build sent elsewhere that way still reports success.
TESTS_CSPROJ="$REPO/Source/ShiftChange.Tests/ShiftChange.Tests.csproj"
TESTS_DLL="$REPO/Tests/Assemblies/ShiftChange.Tests.dll"
rm -rf "$BUILD/release" "$BUILD/tests"
dotnet build "$CSPROJ" -c Release "-p:OutputPath=$BUILD/release/" >/dev/null \
  || die "the Release build failed — fix that first"
[ -f "$BUILD/release/ShiftChange.dll" ] \
  || die "the Release build succeeded but left no dll in $BUILD/release/.
       Something overrides OutDir, so the build went somewhere else, possibly
       straight into Assemblies/. Find out where before running this again."
dotnet build "$TESTS_CSPROJ" -c Release "-p:GameDir=$APP/Contents/MacOS/" \
  "-p:DevToolsDll=$DEVTOOLS_DLL" "-p:ShiftChangeDll=$BUILD/release/ShiftChange.dll" \
  "-p:TestsDir=$BUILD/tests/" >/dev/null \
  || die "the tests' build failed — fix that first"
[ -f "$BUILD/tests/ShiftChange.Tests.dll" ] \
  || die "the tests' build succeeded but left no dll in $BUILD/tests/.
       Something overrides TestsDir, so the copy went somewhere else, possibly
       straight into Tests/Assemblies/. Find out where before running this again."
# Release with nothing of the debug scenes or the tests in it, as it ships.
python3 "$REPO/devtools/check-shipped-dll.py" "$BUILD/release/ShiftChange.dll" >/dev/null \
  || die "the Release build is not a shipping build: run
         python3 devtools/check-shipped-dll.py $BUILD/release/ShiftChange.dll"

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
MODS_ENTRY="$MODS_DIR/ShiftChange"
[ -e "$MODS_ENTRY" ] || die "no Mods/ShiftChange entry — the game cannot load this mod at all"
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
printf 'game: RimWorld 1.7, %s\n' "$APP"
printf 'load path: %s\n' "$ENTRY_REAL"
printf 'devtools: %s\n' "$DEVTOOLS_DLL"

# SWAP A BUILT DLL IN BY RENAME, NEVER BY WRITING INTO THE ONE A GAME LOADED.
#
# RimWorld loads Assemblies/ShiftChange.dll with Assembly.LoadFrom, which can
# keep the file mapped for as long as the game runs, and writing into a mapped
# file can change bytes under a live process. mv only swaps the directory
# entry: a game that loaded the old file keeps it, the next one to start gets
# the new one, and one starting mid-swap finds one complete dll or the other.
# The tests' dll is the same case for a game running with DevTools.
#
# A plain build into Assemblies/ promises none of that. On macOS with the .NET 9
# SDK it happens to spare a running game, because MSBuild's copy unlinks the old
# file and clones the new one in its place (measured 2026-09-23), though the
# path is empty for a moment in between. Where the runtime cannot clone (a
# volume without clone support, source and destination on different volumes, a
# symlinked destination) it truncates the existing file and writes into it.
#
# The .new sits beside its target because a rename is only atomic within one
# filesystem; from anywhere else, mv falls back to copying.
deploy() {
  rm -f "$2.new"
  if cp "$1" "$2.new" && mv -f "$2.new" "$2"
  then
    return 0
  fi
  rm -f "$2.new"
  return 1
}

# Both dlls are the ones a player's game would load from this checkout, and
# both are committed and ship: the mod's, and the tests, which load only with
# DevTools. So nothing needs putting back when the run ends, and a
# run that passes leaves the pair to commit.
#
# Then the csproj's own CleanDevArtifacts, pointed back at Assemblies/, which
# must still hold exactly the one dll. That target only deletes, and -t runs it
# without building anything. It also deletes the .dll_orig a hot Debug session
# polls, so a run beside one ends that session's reload, as any Release build
# does (docs/DEVELOPMENT.md).
deploy "$BUILD/release/ShiftChange.dll" "$DLL" \
  || die "could not deploy the Release build into Assemblies/"
dotnet msbuild "$CSPROJ" -nologo -v:q -t:CleanDevArtifacts -p:Configuration=Release >/dev/null \
  || die "CleanDevArtifacts failed; check Assemblies/ by hand"
extra="$(ls -A "$REPO/Assemblies" | grep -vxF 'ShiftChange.dll' || true)"
[ -z "$extra" ] \
  || die "Assemblies/ must hold only ShiftChange.dll, and also holds:
$extra"
mkdir -p "$(dirname "$TESTS_DLL")"
deploy "$BUILD/tests/ShiftChange.Tests.dll" "$TESTS_DLL" \
  || die "could not deploy the tests into Tests/Assemblies/"
# The checkout as CI and the packaging steps check it: the gate, the folders,
# and a test dll built from the sources as they are now.
python3 "$REPO/devtools/check-shipped-tests.py" >/dev/null \
  || die "the checkout's tests are not shippable: run python3 devtools/check-shipped-tests.py"

rm -rf "$TESTDATA"
mkdir -p "$TESTDATA/Config"

if [ "$FULL" = "1" ]
then
  [ -f "$LIVE_CONFIG" ] || die "no live ModsConfig.xml to copy from"
  # Read-only copy. The live file is never written.
  cp "$LIVE_CONFIG" "$TESTDATA/Config/ModsConfig.xml"
  printf 'mod list: yours, copied (not swapped)\n'
  # DevTools goes in just ahead of this mod, unless the list has it already.
  python3 - "$TESTDATA/Config/ModsConfig.xml" <<'PY' || die "could not add DevTools to the copied mod list"
import sys
import xml.etree.ElementTree as ET

path = sys.argv[1]
tree = ET.parse(path)
active = tree.getroot().find("activeMods")
ids = [(li.text or "").strip().lower() for li in active]
if "mrbeverage.shiftchange" not in ids:
    sys.exit("Shift Change is not active in the copied mod list")
if "smashphil.devtools" not in ids:
    at = ids.index("mrbeverage.shiftchange")
    li = ET.Element("li")
    li.text = "smashphil.devtools"
    li.tail = active[at - 1].tail if at > 0 else active.text
    active.insert(at, li)
    tree.write(path, encoding="utf-8", xml_declaration=True)
PY
  printf 'mod list: plus DevTools, ahead of this mod\n'
else
  if [ -f "$GAME_DIR/Version.txt" ]
  then
    version="  <version>$(head -n 1 "$GAME_DIR/Version.txt" | tr -d '\r\n')</version>"
  else
    version="$(grep -m1 '<version>' "$LIVE_CONFIG" 2>/dev/null)" \
      || die "no Version.txt in $GAME_DIR and no version in $LIVE_CONFIG to stamp the mod list with"
  fi
  {
    printf '<?xml version="1.0" ?>\n<ModsConfigData>\n'
    printf '%s\n  <activeMods>\n' "$version"
    for mod in "${MINIMAL_MODS[@]}"
    do
      if [ "$mod" = "mrbeverage.shiftchange" ]
      then
        printf '    <li>smashphil.devtools</li>\n'
        for extra in ${EXTRA_MODS[@]+"${EXTRA_MODS[@]}"}
        do
          printf '    <li>%s</li>\n' "$extra"
        done
      fi
      printf '    <li>%s</li>\n' "$mod"
    done
    for extra in ${EXTRA_AFTER[@]+"${EXTRA_AFTER[@]}"}
    do
      printf '    <li>%s</li>\n' "$extra"
    done
    printf '  </activeMods>\n  <knownExpansions>\n'
    printf '    <li>ludeon.rimworld.odyssey</li>\n'
    printf '  </knownExpansions>\n</ModsConfigData>\n'
  } > "$TESTDATA/Config/ModsConfig.xml"
  xmllint --noout "$TESTDATA/Config/ModsConfig.xml" || die "generated mod list is not well-formed"
  printf 'mod list: minimal (%s mods and DevTools, isolated)\n' \
    "$(( ${#MINIMAL_MODS[@]} + ${#EXTRA_MODS[@]} + ${#EXTRA_AFTER[@]} ))"
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
# focus was never controlled for; the measurement below controls it. Windowed
# and muted is kept because it is the right shape for a throwaway test
# instance, not because it cures anything.
#
# runInBackground, WHICH IS THE STALL FIX.
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
  # The game runs with -NoWindow, and at the system's UI scale it measures
  # itself under its 1024x768 floor and logs "Resolution too small" once, which
  # fails whichever DevTools test is running. Both scaling keys are needed for a
  # UI scale of 1.
  printf '  <uiScale>1</uiScale>\n'
  printf '  <scaleRelativeToSystem>True</scaleRelativeToSystem>\n'
  printf '  <ignoreSystemScaling>True</ignoreSystemScaling>\n'
  printf '  <volumeMaster>0</volumeMaster>\n'
  printf '</PrefsData>\n'
} > "$TESTDATA/Config/Prefs.xml"
xmllint --noout "$TESTDATA/Config/Prefs.xml" || die "generated Prefs.xml is not well-formed"
printf 'display: none (-NoWindow), muted, runInBackground=%s\n' "$RUN_IN_BACKGROUND"

# The game's own HOME. The folder must exist before the game asks for it,
# DevTools' log folder too (without it the run never finishes), and the Steam
# link keeps the Workshop reachable from it. The logs go, so every one read
# below was written by this run.
mkdir -p "$TESTHOME/Library/Application Support/ludeon.rimworld"
ln -sfn "$HOME/Library/Application Support/Steam" "$TESTHOME/Library/Application Support/Steam"
rm -f "$LOG" "$(dirname "$LOG")/Player-prev.log" "$TESTLOG"

printf 'save data: %s\n' "$TESTDATA"
printf 'launching…\n'

# The binary directly, not `open`: `open` returns before the child exists and
# gives no PID back, so the only way to wait would be "is ANY RimWorld running",
# which is precisely the check that cannot tell this instance from someone's
# colony. Launching it here makes $! ours, and ours alone.
#
# The subshell execs the game, so $! is still the game. Started outside Steam,
# it takes its app id from the environment, and it starts from the game folder
# as Steam would start it. Steam's own lines go to stderr.
( cd "$GAME_DIR" && exec env HOME="$TESTHOME" SteamAppId=294100 SteamGameId=294100 \
    "$BIN" --pid MrBeverage.ShiftChange --test -e -NoWindow "-savedatafolder=$TESTDATA" ) \
  >"$TESTDATA/stdout.log" 2>"$TESTDATA/stderr.log" &
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
  # run has ever recovered. All three conditions are required — see STARTUP_GRACE.
  if [ "$started" -eq 0 ] && [ "$elapsed" -ge "$STARTUP_GRACE" ] && [ "$quiet" -ge "$STALL_QUIET" ] \
     && idle "$GAME_PID"
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

  # A dead test runner never quits the game, so stop at once rather than at TIMEOUT.
  if runner_coroutine_died
  then
    kill "$GAME_PID" 2>/dev/null || true
    printf 'Test.log: %s\n' "$TESTLOG" >&2
    die "DevTools' test runner died on an exception, ${elapsed}s in, and the game
       would have idled until the ${TIMEOUT}s ceiling; stopped pid $GAME_PID.
       The game wrote it to stderr only:
$(coroutine_errors)
       stderr: $TESTDATA/stderr.log"
  fi

  alive "$GAME_PID" || break
done

if alive "$GAME_PID"
then
  kill "$GAME_PID" 2>/dev/null || true
  die "timed out after ${TIMEOUT}s; stopped pid $GAME_PID — check $LOG"
fi
CODE=0
wait "$GAME_PID" || CODE=$?
printf 'game exited after ~%ss\n\n' "$elapsed"

[ -f "$LOG" ] || die "no log at $LOG — the game wrote none where this script looks"

# 1.7 prints a stack trace under every log line; the copy this script prints
# from leaves them out.
READABLE="$TESTDATA/Player.readable.log"
grep -v '^    at ' "$LOG" > "$READABLE" || true

# A native crash first, with its own message: it leaves no verdict, and a
# sibling mod once reported one as the stall. By this point the game EXITED by
# itself. A stall never does; the loop above stops those with their own message.
if grep -q "Native Crash Reporting" "$LOG"
then
  printf 'the game CRASHED (native). Top frames:\n' >&2
  grep -E "^#([0-9]|1[0-5]) " "$LOG" | cut -c1-140 >&2 || true
  printf '\n' >&2
  die "native crash: neither the stall nor a wiring problem. See $LOG"
fi

# DevTools' verdict. A pass or a fail needs DevTools' own last line as well as
# the exit code: it writes that line just before it quits, and a game stopped any
# other way can still exit 0.
if [ -f "$TESTLOG" ]
then
  cat "$TESTLOG"
  printf '\nTest.log: %s\n' "$TESTLOG"
else
  printf 'this run wrote no Test.log\n'
fi
printf 'Player.log: %s\n' "$LOG"
VERDICT=""
if grep -q "Test runner finished. Result: Passed" "$TESTLOG" 2>/dev/null
then
  VERDICT=0
elif grep -q "Test runner finished. Result: Failed" "$TESTLOG" 2>/dev/null
then
  VERDICT=1
fi
case "$CODE:$VERDICT" in
  0:0) printf 'devtools run: PASSED\n' ;;
  1:1) printf 'devtools run: FAILED (exit 1): a test failed\n' ;;
  2:*) printf 'devtools run: NOTHING RAN (exit 2); Player.log says why\n' ;;
  *)
    printf 'last lines of the log:\n' >&2
    tail -n 5 "$READABLE" | sed 's/^/  | /' >&2
    if [ -n "$(coroutine_errors)" ]
    then
      printf 'exceptions the game wrote to stderr only:\n%s\n' "$(coroutine_errors)" >&2
    fi
    printf 'devtools run: no verdict (exit %s): the test runner never finished\n' "$CODE"
    [ "$CODE" = "0" ] && CODE=3
    ;;
esac
exit "$CODE"
