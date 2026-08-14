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
#   devtools/publish-workshop.sh stage      # build + assemble dist/ShiftChange
#   devtools/publish-workshop.sh install    # swap it into Mods/, dev link aside
#   devtools/publish-workshop.sh restore    # dev link back, recover the item id
#
# The normal run is stage -> install -> upload in game -> restore.
#
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST="$REPO/dist/ShiftChange"
MODS="/Users/alexbeverage/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods"
LIVE="$MODS/ShiftChange"
PARKED="$MODS/.ShiftChange-devlink"

# Exactly the set in .github/workflows/ci.yml's "Assemble mod folder" step:
# what the game loads, plus the human-readable documents. Keep the two in step.
CONTENT=(About Assemblies Defs Patches Languages docs LICENSE README.md)

die() { printf 'error: %s\n' "$1" >&2; exit 1; }

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
}

cmd_install() {
  [ -d "$DIST" ] || die "nothing staged — run 'stage' first"
  [ -d "$MODS" ] || die "game Mods folder not found: $MODS"

  # Two folders sharing packageId MrBeverage.ShiftChange would both appear in
  # the mod list, and the uploader would target whichever the game resolved
  # first. Park the dev symlink so exactly one exists.
  if [ -L "$LIVE" ]; then
    [ -e "$PARKED" ] && die "$PARKED already exists — run 'restore' first"
    mv "$LIVE" "$PARKED"
    printf 'parked the dev symlink at %s\n' "$PARKED"
  elif [ -d "$LIVE" ]; then
    die "$LIVE is a real directory, not the dev symlink — resolve by hand"
  fi

  cp -R "$DIST" "$LIVE"
  printf 'installed the staged copy at %s\n' "$LIVE"
  du -sh "$LIVE"
  cat <<'EOF'

Now, in game:
  1. Enable Shift Change in the mod list and confirm it loads clean.
  2. Select it, then Upload to Steam Workshop.
  3. Quit the game, then run: devtools/publish-workshop.sh restore

THEN, IN THE BROWSER, BEFORE MAKING IT PUBLIC:

  1. Paste media/steam-description.bbcode into the description field.

     Do not skip this and plan to do it later from the game. RimWorld calls
     SetItemDescription only on the CREATE branch (Workshop.cs:262-265), from
     About.xml's <description> — so the store page opens showing the in-game
     mod-list blurb, and no later in-game update will ever replace it. The web
     editor is the only route. The BBCode carries the demo gif, the apparel
     section, the source link and the AI-assistance disclosure, none of which
     are in About.xml by design.

  2. Add the gallery images from media/cards/. The game uploads only
     About/Preview.png (SetItemPreview, Workshop.cs:265-273) and never calls
     AddItemPreviewFile, so the cards have no path up from inside the game.

  3. Re-host the demo gif on the item itself and repoint the description at it.
     It currently hotlinks i.imgur.com, which nothing here controls.

  4. Only then set visibility to public.
EOF
}

cmd_restore() {
  # The uploader wrote the new item id into the staged copy. It is the only
  # record of which Workshop item this mod is, and losing it means the next
  # upload creates a duplicate listing instead of updating this one.
  if [ -f "$LIVE/About/PublishedFileId.txt" ]; then
    local id
    id="$(cat "$LIVE/About/PublishedFileId.txt")"
    if [ -f "$REPO/About/PublishedFileId.txt" ] \
       && ! diff -q "$LIVE/About/PublishedFileId.txt" "$REPO/About/PublishedFileId.txt" >/dev/null; then
      die "item id changed ($id) — a duplicate listing was probably created; resolve by hand"
    fi
    cp "$LIVE/About/PublishedFileId.txt" "$REPO/About/"
    printf 'recovered item id %s into the repo — COMMIT IT\n' "$id"
  else
    printf 'no PublishedFileId.txt in the uploaded copy (upload not run, or it failed)\n'
  fi

  if [ -d "$LIVE" ] && [ ! -L "$LIVE" ]; then
    rm -rf "$LIVE"
  fi
  if [ -L "$PARKED" ]; then
    mv "$PARKED" "$LIVE"
    printf 'restored the dev symlink\n'
  fi
}

case "${1:-stage}" in
  stage)   cmd_stage ;;
  install) cmd_install ;;
  restore) cmd_restore ;;
  *)       die "unknown command: $1 (stage | install | restore)" ;;
esac
