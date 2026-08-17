#!/usr/bin/env python3
"""Static checks that need no game, no build and no network.

    ./devtools/check-invariants.py

Run it before pushing; CI runs the same script, so a green run here is a green
run there. Exits non-zero on the first category that fails, after reporting
every failure it found.

Each check exists because something broke that way, or because the failure is
silent in game — which is worse, since the symptom then arrives as a player's
screenshot rather than a red line.
"""

import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, "Source")
KEYED = os.path.join(ROOT, "Languages", "English", "Keyed", "ShiftChange.xml")
PREVIEW = os.path.join(ROOT, "About", "Preview.png")

# Steam rejects a Workshop preview over 1 MiB. The game does not check:
# Workshop.cs:266-272 tests only that the file exists before handing it to
# SteamUGC.SetItemPreview, so an oversized one fails mid-publish as a bare
# result code, after the mod is already staged in Mods/.
PREVIEW_MAX = 1048576
PREVIEW_WARN = 1000000

# Keys we use that vanilla defines. Ours are all ShiftChange.*; anything else
# here is deliberate reuse and must be justified in this list.
VANILLA_KEYS = {
    "CommandThingSetOwnerLabel",
    # Patch_AllowRemovingTooltip matches the stand's "Allow removing items"
    # toggle by its vanilla LABEL (the command is an anonymous Command_Toggle
    # with no other handle) and APPENDS to its vanilla DESC rather than
    # replacing it, so vanilla's half stays translated in every language.
    "CommandAllowRemovingApparel",
    "CommandAllowRemovingApparelDesc",
}

failures = []
notes = []


def fail(category, message):
    failures.append("%s: %s" % (category, message))


def cs_files():
    for base, _, names in os.walk(SOURCE):
        if "obj" in base or "bin" in base:
            continue
        for name in names:
            if name.endswith(".cs"):
                yield os.path.join(base, name)


def rel(path):
    return os.path.relpath(path, ROOT)


# ---------------------------------------------------------------- hot reload

# Hot-swapped method bodies execute in a separate assembly and Unity's Mono
# honours only InternalsVisibleTo, so a `private` member or an auto-property's
# backing field throws FieldAccessException the first time a swapped body
# touches it. Both compile clean.
#
# These replace two shell greps that were anchored to the start of a line and
# to a single line respectively, and so missed `static private int x;`,
# `[Attr] private int y;`, `class N { private int z; }`, and EVERY multi-line
# auto-property — the `{` and the `get;` are never on the same line in the
# style this codebase actually writes.
PRIVATE_RE = re.compile(
    r"(?:^|[{;]|\]\s*)\s*(?:(?:static|readonly|volatile|unsafe|new|sealed|override|virtual|extern|partial|async)\s+)*"
    r"private\s+(?!protected)",
    re.M,
)
AUTOPROP_RE = re.compile(r"\{[^{}]*\b(?:get|set)\s*;[^{}]*\}", re.S)


def check_hot_reload():
    for path in cs_files():
        text = open(path, encoding="utf-8").read()
        stripped = re.sub(r"//[^\n]*", "", text)
        stripped = re.sub(r"/\*.*?\*/", "", stripped, flags=re.S)
        for match in PRIVATE_RE.finditer(stripped):
            line = stripped.count("\n", 0, match.start()) + 1
            fail("hot-reload", "%s:%d private member — this codebase is "
                 "internal-by-default" % (rel(path), line))
        for match in AUTOPROP_RE.finditer(stripped):
            body = match.group(0)
            # An expression-bodied or full property is fine; only `get;`/`set;`
            # with no body is an auto-property.
            if re.search(r"\b(?:get|set)\s*;", body) and "=>" not in body:
                line = stripped.count("\n", 0, match.start()) + 1
                fail("hot-reload", "%s:%d auto-property — its backing field is "
                     "private beyond reach of the IVT grants" % (rel(path), line))


# ------------------------------------------------------------ translation keys

# LoadedLanguage.TryGetTextFromKey sets translated = key and returns false with
# no log line, so a key that exists in code but not in the XML renders as the
# raw key on the player's screen and nothing anywhere says so.
def check_translation_keys():
    if not os.path.exists(KEYED):
        fail("keys", "no keyed file at %s" % rel(KEYED))
        return
    defined = set()
    for child in ET.parse(KEYED).getroot():
        if isinstance(child.tag, str):
            defined.add(child.tag)

    used = {}
    for path in cs_files():
        text = open(path, encoding="utf-8").read()
        for match in re.finditer(r'"([A-Za-z0-9_.]+)"\s*\.Translate\s*\(', text):
            used.setdefault(match.group(1), rel(path))

    for key, where in sorted(used.items()):
        if key in defined or key in VANILLA_KEYS:
            continue
        fail("keys", "%s uses \"%s\" — not in %s, and not an allowed vanilla key"
             % (where, key, rel(KEYED)))

    for key in sorted(defined - set(used) - VANILLA_KEYS):
        fail("keys", "%s defines \"%s\" — nothing uses it" % (rel(KEYED), key))


# ---------------------------------------------------------------- XML bindings

# XML names C# types as strings. Rename the type and the def keeps the old
# name: the comp never attaches, the mod loads without error, the stands look
# entirely normal, and nothing happens. AGENTS.md notes these names are scribed
# into saves, which makes renaming them a deliberate multi-file act — exactly
# the edit that drops one.
def check_xml_bindings():
    declared = set()
    for path in cs_files():
        text = open(path, encoding="utf-8").read()
        for match in re.finditer(r"\b(?:class|struct)\s+([A-Za-z0-9_]+)", text):
            declared.add(match.group(1))

    # Only where XML actually names a TYPE: a Class="" attribute, or an
    # element whose name ends in Class (compClass, driverClass, thingClass,
    # workerClass). Deliberately NOT every "ShiftChange.*" string — the keyed
    # language file is full of ShiftChange.-prefixed translation KEYS, which
    # are not types and are checked separately by check_translation_keys.
    binding = re.compile(
        r'(?:Class\s*=\s*"ShiftChange\.([A-Za-z0-9_]+)"'
        r'|<[A-Za-z0-9_]*[Cc]lass>\s*ShiftChange\.([A-Za-z0-9_]+)\s*</)'
    )
    for base, _, names in os.walk(ROOT):
        if any(part in base for part in (".git", "obj", "bin", "dist", "Languages")):
            continue
        for name in names:
            if not name.endswith(".xml"):
                continue
            path = os.path.join(base, name)
            try:
                text = open(path, encoding="utf-8").read()
            except OSError:
                continue
            for match in binding.finditer(text):
                named = match.group(1) or match.group(2)
                if named not in declared:
                    line = text.count("\n", 0, match.start()) + 1
                    fail("bindings", "%s:%d names ShiftChange.%s — no such type in Source/"
                         % (rel(path), line, named))


# -------------------------------------------------------------------- preview

def check_preview():
    if not os.path.exists(PREVIEW):
        fail("preview", "no About/Preview.png")
        return
    size = os.path.getsize(PREVIEW)
    if size > PREVIEW_MAX:
        fail("preview", "About/Preview.png is %d bytes, over Steam's %d limit"
             % (size, PREVIEW_MAX))
    elif size > PREVIEW_WARN:
        notes.append("About/Preview.png is %d bytes — %.1f%% of Steam's %d "
                     "limit, so any re-export is likely to cross it"
                     % (size, 100.0 * size / PREVIEW_MAX, PREVIEW_MAX))


# ------------------------------------------------------------ private tracking

# This repository is public. The backlog and decision log that drive it are not,
# and their identifiers mean nothing to a reader here — a bare "as the tracker
# decided" reference in a source comment points at a document nobody outside
# can open, and leaks how the work is organised into a mod's shipped source.
#
# No literal identifier appears in this file, deliberately: it is on the skip
# list below, so an example written here would be the one place the check
# cannot see.
#
# Write the REASON in the comment instead; the tracking item is where the
# argument lives, not what a stranger reading the code needs.
#
# Caught by hand in review after eight of them reached a pushed branch in one
# session (2026-08-17). Everything below the repo root is scanned except this
# script and the untracked agent context file.
TRACKING_ID = re.compile(r"\b(?:BL|DEC)-\d{3}\b")
TRACKING_SKIP_DIRS = {".git", "obj", "bin", "dist", "node_modules"}
TRACKING_SKIP_FILES = {"check-invariants.py", "CLAUDE.md"}
TRACKING_EXTS = (".cs", ".py", ".sh", ".md", ".xml", ".yml", ".yaml", ".csproj", ".bbcode")


def check_private_tracking_refs():
    for base, dirs, names in os.walk(ROOT):
        dirs[:] = [d for d in dirs if d not in TRACKING_SKIP_DIRS]
        for name in names:
            if name in TRACKING_SKIP_FILES or not name.endswith(TRACKING_EXTS):
                continue
            path = os.path.join(base, name)
            try:
                text = open(path, encoding="utf-8").read()
            except (UnicodeDecodeError, OSError):
                continue
            for number, line in enumerate(text.splitlines(), 1):
                match = TRACKING_ID.search(line)
                if match:
                    fail("tracking-ref",
                         "%s:%d references %s — the tracker is private; state the "
                         "reason in the comment instead"
                         % (rel(path), number, match.group(0)))


# ----------------------------------------------------------------- patch roots

# ModContentPack.LoadPatches discards EVERY operation in a file whose root is
# not exactly <Patch>, with one log line as the only symptom. The engine walks
# Patches/ with SearchOption.AllDirectories, so this must recurse too — the
# shell check it replaces globbed Patches/*.xml and would miss a compat patch
# in a subfolder.
def check_patch_roots():
    patches = os.path.join(ROOT, "Patches")
    if not os.path.isdir(patches):
        return
    for base, _, names in os.walk(patches):
        for name in names:
            if not name.endswith(".xml"):
                continue
            path = os.path.join(base, name)
            root = ET.parse(path).getroot().tag
            if root != "Patch":
                fail("patch-root", "%s root is <%s> — must be <Patch>, or every "
                     "operation in the file is silently discarded" % (rel(path), root))


def main():
    check_hot_reload()
    check_translation_keys()
    check_xml_bindings()
    check_preview()
    check_patch_roots()
    check_private_tracking_refs()

    for note in notes:
        print("note: %s" % note)
    for failure in failures:
        print("FAIL %s" % failure)
    if failures:
        print("\n%d problem(s)" % len(failures))
        return 1
    print("static invariants: all clear")
    return 0


if __name__ == "__main__":
    sys.exit(main())
