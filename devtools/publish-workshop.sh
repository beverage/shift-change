#!/usr/bin/env bash
#
# Stage the mod for a Steam Workshop upload, and swap it into the game's Mods
# folder in place of the development symlink.
#
# WHY THIS EXISTS
#
# RimWorld's in-game uploader publishes the mod's folder verbatim. Workshop.cs
# hands `hook.Directory.FullName` straight to `SteamUGC.SetItemContent`, that
# resolves to `ModMetaData.GetWorkshopUploadDirectory()`, and that returns
# `RootDir` with no filtering — the one hook that could strip anything,
# `PrepareForWorkshopUpload()`, has an empty body. `CanToUploadToWorkshop()`
# also requires the mod to sit in `Mods/`.
#
# `Mods/ShiftChange` is a symlink to the working repository. Uploading through
# it would publish `Source/`, `media/`, `.github/`, `.vscode/`, `.DS_Store` and
# the entire `.git` directory to every subscriber. This script builds the same
# file set the CI release zip ships and uploads from that instead.
#
# USAGE
#
#   devtools/publish-workshop.sh            # stage + install, the safe default
#   devtools/publish-workshop.sh stage      # build + assemble dist/ShiftChange
#   devtools/publish-workshop.sh install    # swap it into Mods/, dev link aside
#   devtools/publish-workshop.sh restore    # dev link back, recover the item id
#
# The normal run is (no argument) -> upload in game -> restore.
#
# STAGING ALONE CHANGES NOTHING ABOUT WHAT THE GAME UPLOADS. `stage` only fills
# dist/; until `install` has replaced the dev symlink, the uploader still
# publishes the whole repository through it — 61 MB of .git, dist/ and media/,
# which is what shipped twice on 2026-08-22. That is why the bare invocation
# now does both, and why `stage` says so on its way out.
#
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST="$REPO/dist/ShiftChange"
MODS="/Users/alexbeverage/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods"
LIVE="$MODS/ShiftChange"

# Exactly the set in .github/workflows/ci.yml's "Assemble mod folder" step:
# what the game loads, plus the human-readable documents. Keep the two in step.
CONTENT=(About Assemblies Defs Patches Languages docs LICENSE README.md)

die() { printf 'error: %s\n' "$1" >&2; exit 1; }

# install and restore both replace the path a running game loaded the mod
# from, and the game rewrites ModsConfig.xml on exit over whatever the swap
# did. Same refusal as run-harness.sh: quit first.
ensure_game_closed() {
  if pgrep -x "RimWorld by Ludeon Studios" >/dev/null; then
    die "RimWorld is running — quit it before touching Mods/"
  fi
}

cmd_stage() {
  command -v dotnet >/dev/null || die "dotnet not on PATH"

  # Release, always. A Debug build writes the hot-reload rig to the same
  # Assemblies/ path, and shipping one is a broken mod.
  dotnet build "$REPO/Source/ShiftChange/ShiftChange.csproj" -c Release

  rm -rf "$DIST"
  mkdir -p "$DIST"
  for item in "${CONTENT[@]}"; do
    [ -e "$REPO/$item" ] || die "missing from the repo: $item"
    cp -R "$REPO/$item" "$DIST/"
  done

  # Steam identifies the item by a file the game writes INSIDE the upload root
  # after a successful publish. Without it the uploader takes the create branch
  # and mints a second, duplicate Workshop listing. If a previous publish left
  # one in the repo, it has to travel with the staged copy.
  if [ -f "$REPO/About/PublishedFileId.txt" ]; then
    printf 'carrying existing item id: %s\n' "$(cat "$REPO/About/PublishedFileId.txt")"
  else
    printf 'no PublishedFileId.txt — this will CREATE a new Workshop item\n'
  fi

  # Belt and braces: the allowlist is positive, but a stray dll under
  # Assemblies/ is the one thing that reaches the game's load path.
  find "$DIST" -name '.DS_Store' -delete
  local strays
  strays="$(find "$DIST/Assemblies" -type f ! -name 'ShiftChange.dll' | wc -l | tr -d ' ')"
  [ "$strays" = "0" ] || die "Assemblies/ holds $strays file(s) besides ShiftChange.dll"

  printf '\nstaged %s\n' "$DIST"
  du -sh "$DIST"
  printf '\ncontents:\n'
  ls -1 "$DIST"

  if [ -L "$LIVE" ]; then
    printf '\n!! STAGED ONLY. %s is still the dev symlink, so an upload now\n' "$LIVE"
    printf '!! would publish the whole repository. Run: %s install\n' "$0"
  fi
}

cmd_install() {
  ensure_game_closed
  [ -d "$DIST" ] || die "nothing staged — run 'stage' first"
  [ -d "$MODS" ] || die "game Mods folder not found: $MODS"

  # Two folders sharing packageId MrBeverage.ShiftChange would both appear in
  # the mod list, and ModLister's first-wins race would decide which one the
  # uploader publishes. A dot prefix does NOT hide a directory from the game —
  # ModLister.cs:111 enumerates GetDirectories() unfiltered — so the dev
  # symlink cannot be parked anywhere inside Mods/. Delete it instead; restore
  # recreates it with one ln -s.
  if [ -L "$LIVE" ]; then
    rm "$LIVE"
    printf 'removed the dev symlink (restore recreates it)\n'
  elif [ -d "$LIVE" ]; then
    die "$LIVE is a real directory, not the dev symlink — resolve by hand"
  fi

  cp -R "$DIST" "$LIVE"

  # The confirmation Steam never gives you. The uploader shows no manifest, no
  # size and no file list before publishing — SetItemContent takes the folder
  # verbatim and PrepareForWorkshopUpload() is an empty method — so this is the
  # only chance to see what is about to go out.
  printf '\n=== this is what the game will upload ===\n'
  printf 'from:      %s\n' "$LIVE"
  if [ -f "$LIVE/About/PublishedFileId.txt" ]; then
    printf 'item id:   %s (updates the existing item)\n' "$(cat "$LIVE/About/PublishedFileId.txt")"
  else
    printf 'item id:   NONE — this will CREATE a second Workshop listing\n'
  fi
  printf 'size:      %s\n' "$(du -sh "$LIVE" | cut -f1)"
  printf 'top level: %s\n' "$(ls -1 "$LIVE" | tr '\n' ' ')"
  printf '=========================================\n'
  cat <<'EOF'

Now, in game (launch fresh — Development mode must be ON in Options, or the
upload entry does not exist at all; Page_ModsConfig.cs:718):

  1. Enable Shift Change in the mod list and confirm it loads clean: exactly
     ONE Shift Change entry, and no "same packageId multiple times" error in
     the log.
  2. Select it -> More actions -> Upload to Steam Workshop -> Confirm.
     The confirm dialog has a "Tag as translation" checkbox — leave it
     UNTICKED, or the item trades its Mod tag for Translation and drops out
     of the Workshop's default mod browse.
  3. Wait for the progress dialog to finish and the item page to open.
     Then quit the game.

THEN, IMMEDIATELY, IN THIS ORDER:

  1. BROWSER: set the item's visibility to PRIVATE and confirm it by eye
     (owner controls on the item page). The game never sets visibility —
     SetItemVisibility appears nowhere in the engine — so until checked the
     item sits in whatever state Steam defaulted it to. Everything below
     happens while it is private.

  2. BROWSER: confirm the item actually published — open its page from a
     private window. The game reports success even if the Workshop Legal
     Agreement was never accepted, and an unaccepted agreement leaves an
     item only its owner can see.

  3. TERMINAL: devtools/publish-workshop.sh restore
     Then commit About/PublishedFileId.txt — it is the only record of which
     Workshop item this mod is.

  4. BROWSER: paste media/steam-description.bbcode into the description.

     Do not skip this and plan to do it later from the game. RimWorld calls
     SetItemDescription only on the CREATE branch (Workshop.cs:262-265), from
     About.xml's <description> — so the store page opens showing the in-game
     mod-list blurb, and no later in-game update will ever replace it. The web
     editor is the only route. The BBCode carries the demo gif, the apparel
     section, the source link and the AI-assistance disclosure, none of which
     are in About.xml by design.

  5. BROWSER: add the gallery images from media/cards/. The game uploads only
     About/Preview.png (SetItemPreview, Workshop.cs:266-272) and never calls
     AddItemPreviewFile, so the cards have no path up from inside the game.
     Skip card-doc.png — it is the same shot as the preview tile Steam
     already shows first. It is the 16-bit master; About/Preview.png is the
     same image re-encoded to 8-bit RGB (1016 KB -> 179 KB, no visible
     change). Keep any future preview 8-bit: Steam's item-image limit is
     under 1 MB and a 16-bit 640x360 scene shot lands within 5% of it.

  6. The demo gif stays hotlinked from i.imgur.com: Steam's item-image limit
     is under 1 MB and the gif is 2.26 MB, so it cannot be re-hosted on the
     item as-is. Hotlinked imgur is the community norm; re-encode under 1 MB
     later if that ever changes.

  7. VERIFY AS A SUBSCRIBER, still private: subscribe in the browser, launch
     the game, and enable the STEAM-sourced Shift Change entry (with the dev
     symlink back, both copies appear in the list; leave the local one
     disabled). Confirm a clean load with no red errors. Quit, unsubscribe,
     re-enable the local copy.

  8. Only then set visibility to PUBLIC.
EOF
}

cmd_restore() {
  ensure_game_closed
  # The uploader wrote the new item id into the staged copy. It is the only
  # record of which Workshop item this mod is, and losing it means the next
  # upload creates a duplicate listing instead of updating this one. Guarded
  # on the staged copy actually being present: with the dev symlink already
  # back in place there is nothing to recover, and a repeat run is a no-op,
  # not an error.
  if [ ! -L "$LIVE" ] && [ -f "$LIVE/About/PublishedFileId.txt" ]; then
    local id
    id="$(cat "$LIVE/About/PublishedFileId.txt")"
    if [ -f "$REPO/About/PublishedFileId.txt" ] \
       && ! diff -q "$LIVE/About/PublishedFileId.txt" "$REPO/About/PublishedFileId.txt" >/dev/null; then
      die "item id changed ($id) — a duplicate listing was probably created; resolve by hand"
    fi
    cp "$LIVE/About/PublishedFileId.txt" "$REPO/About/"
    printf 'recovered item id %s into the repo — COMMIT IT\n' "$id"
  elif [ ! -L "$LIVE" ] && [ -d "$LIVE" ]; then
    printf 'no PublishedFileId.txt in the uploaded copy (upload not run, or it failed)\n'
  fi

  if [ -d "$LIVE" ] && [ ! -L "$LIVE" ]; then
    rm -rf "$LIVE"
  fi
  ln -sfn "$REPO" "$LIVE"
  printf 'dev symlink in place\n'
}

case "${1:-publish}" in
  publish) cmd_stage; cmd_install ;;
  stage)   cmd_stage ;;
  install) cmd_install ;;
  restore) cmd_restore ;;
  *)       die "unknown command: $1 (publish | stage | install | restore)" ;;
esac
