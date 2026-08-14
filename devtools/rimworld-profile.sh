#!/usr/bin/env bash
#
# Swap RimWorld's active mod list, so the lifecycle harness can be run against
# a four-mod profile instead of the development one.
#
# WHY
#
# A harness run on the full development profile costs about two and a half
# minutes to reach a map and then needs three other mods' welcome dialogs
# dismissed before anything can be clicked. Worse, it is not deterministic:
# on 2026-08-14 the harness fixture failed once and passed once on identical
# code, because `Pawn_HealthTracker.Notify_Spawned` threw `Collection was
# modified` under seven third-party postfixes on `Pawn.SpawnSetup`.
#
# The minimal profile is the smallest set that still loads the mod and the
# building it patches, so the harness measures our code and not the modlist.
#
# READ THIS BEFORE MAKING MINIMAL THE DEFAULT
#
# Minimal is for ITERATION. It is not the release gate, and it must not
# silently become one. Shift Change's whole job is patching a vanilla building
# that other mods also touch, so a full-profile run is the only evidence we
# ever get that the mod behaves in the environment players actually run. Do
# both before an upload: minimal to iterate, full to sign off.
#
# USAGE
#
#   devtools/rimworld-profile.sh show      # which profile is live
#   devtools/rimworld-profile.sh minimal   # back up, then swap to minimal
#   devtools/rimworld-profile.sh restore   # put the development profile back
#
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG="/Users/alexbeverage/Library/Application Support/RimWorld/Config/ModsConfig.xml"
STORE="$REPO/devtools/profiles"
PRESWAP="$STORE/.pre-swap.xml"

# The smallest list that still exercises the real thing: Harmony because we
# patch, Core, Odyssey because the outfit stand is Odyssey content, and us.
#
# Vanilla Apparel Expanded is deliberately NOT here. The fixture falls back to
# vanilla apparel without it (`Apparel_TribalA` under `Apparel_BasicShirt` plus
# `Apparel_Pants`), which displaces two garments instead of one and exercises
# the same lifecycle paths — and dropping it also drops the VEF Core dependency
# it drags in. A full-profile run covers the VAE branch.
MINIMAL_MODS=(
  brrainz.harmony
  ludeon.rimworld
  ludeon.rimworld.odyssey
  mrbeverage.shiftchange
)

die() { printf 'error: %s\n' "$1" >&2; exit 1; }

assert_game_closed() {
  # RimWorld rewrites ModsConfig.xml when it exits. Swapping underneath a
  # running game means the game's in-memory list wins on quit and silently
  # undoes the swap — or worse, writes the minimal list over the real one.
  if pgrep -f "RimWorldMac.app/Contents/MacOS/RimWorld" >/dev/null; then
    die "RimWorld is running. Quit it first — it rewrites ModsConfig.xml on exit."
  fi
}

cmd_show() {
  [ -f "$CONFIG" ] || die "no ModsConfig.xml at $CONFIG"
  local count
  count="$(grep -c '<li>' "$CONFIG" || true)"
  printf 'active entries (incl. knownExpansions): %s\n' "$count"
  if [ -f "$PRESWAP" ]; then
    printf 'state: SWAPPED — development profile is parked at %s\n' "$PRESWAP"
    printf 'run "%s restore" to put it back\n' "$0"
  else
    printf 'state: development profile live (no swap in effect)\n'
  fi
  printf '\nactive mods:\n'
  awk '/<activeMods>/,/<\/activeMods>/' "$CONFIG" | grep '<li>' | sed 's/.*<li>/  /; s/<\/li>.*//'
}

cmd_minimal() {
  assert_game_closed
  [ -f "$CONFIG" ] || die "no ModsConfig.xml at $CONFIG"
  mkdir -p "$STORE"

  # Park the development profile ONCE. Swapping twice without restoring must
  # not overwrite the real list with an already-minimal one.
  if [ ! -f "$PRESWAP" ]; then
    cp "$CONFIG" "$PRESWAP"
    printf 'parked the development profile at %s\n' "$PRESWAP"
  else
    printf 'already swapped; development profile stays parked at %s\n' "$PRESWAP"
  fi
  cp "$CONFIG" "$STORE/live-$(date +%Y%m%d-%H%M%S).xml"

  # Carry the version and knownExpansions across verbatim: the first stops
  # RimWorld warning the config is from another build, the second is the list
  # of expansions the player OWNS and has nothing to do with what is enabled.
  local version expansions
  version="$(grep -m1 '<version>' "$CONFIG")"
  expansions="$(awk '/<knownExpansions>/,/<\/knownExpansions>/' "$CONFIG")"

  {
    printf '<?xml version="1.0" ?>\n'
    printf '<ModsConfigData>\n'
    printf '%s\n' "$version"
    printf '  <activeMods>\n'
    for mod in "${MINIMAL_MODS[@]}"; do
      printf '    <li>%s</li>\n' "$mod"
    done
    printf '  </activeMods>\n'
    printf '%s\n' "$expansions"
    printf '</ModsConfigData>\n'
  } > "$CONFIG.tmp"

  xmllint --noout "$CONFIG.tmp" || die "generated config is not well-formed; left $CONFIG untouched"
  mv "$CONFIG.tmp" "$CONFIG"
  printf 'swapped to the minimal profile (%s mods)\n' "${#MINIMAL_MODS[@]}"
  printf 'launch: open "/Users/alexbeverage/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app" --args -quicktest\n'
}

cmd_restore() {
  assert_game_closed
  [ -f "$PRESWAP" ] || die "nothing parked — the development profile is already live"
  xmllint --noout "$PRESWAP" || die "parked profile is not well-formed; refusing to restore it"
  cp "$PRESWAP" "$CONFIG"
  rm "$PRESWAP"
  printf 'restored the development profile\n'
}

case "${1:-show}" in
  show)    cmd_show ;;
  minimal) cmd_minimal ;;
  restore) cmd_restore ;;
  *)       die "unknown command: $1 (show | minimal | restore)" ;;
esac
