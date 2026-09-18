#!/usr/bin/env python3
"""Assert the committed assembly is a shippable Release build.

    ./devtools/check-shipped-dll.py [path/to/ShiftChange.dll]

Three properties:

  1. NO SCENES fixtures. The debug stage builders each GenDebug.ClearArea a
     200-320 cell footprint — destroying buildings, stock and any pawn standing
     in them — and then leave permanent player-faction colonists, owned
     buildings and rewritten terrain behind. A Debug or Media dll staged by
     mistake puts that in a player's debug menu, one unconfirmed click away.

  2. NO HARNESS, and no -shiftchange-harness launch flag. Dev tooling is not
     a player's to carry: the flag clears a pad, spawns colonists, writes save
     files and then quits the game. It is compiled out of Release entirely
     (ShiftChange.csproj's HARNESS switch), so this asserts an ABSENCE, and
     the absence is the whole point of the switch.

     It used to be the opposite check — the harness was REQUIRED to ship, on
     the argument that a gate asserting against a build nobody installs
     asserts nothing. What replaced that argument: HARNESS is only ever a
     whole-file guard, so the harness build and the shipping build differ by
     the presence of harness types and by nothing else. check-invariants.py
     enforces that half; this file enforces this one.

  3. The feature surface IS there. Absence checks pass trivially on an empty
     or truncated file, so something has to be asserted present. These are the
     types that make the mod do anything at all.

Run it before pushing; CI runs the same script.

--------------------------------------------------------------------------
THE TRAP THIS SCRIPT EXISTS TO AVOID (verified 2026-08-17)

A .NET assembly keeps names in THREE places with DIFFERENT encodings:

    #Strings  type and member names       UTF-8
    #US       string literals in code     UTF-16
    #Blob     attribute ARGUMENTS         UTF-8, length-prefixed

So `grep -a "DebugTools_DemoStage"` works (a type name, UTF-8) while
`grep -a "shiftchange-harness"` finds NOTHING WHATEVER SHIPS — it is a
literal, stored as UTF-16. Measured on the harness build: the launch flag is
present 13 times and an ASCII grep reports zero. A guard written the obvious
way would therefore have passed on a dll that carried the flag.

The third case is the sneaky one, because it splits strings that LOOK alike.
Measured on the Media dll: the debug action's own label `"Dev tools"` is
utf8=1/utf16=0 (it is an attribute argument, so it lives in the blob heap),
while `"Shift Change"` is utf8=1/utf16=3 — once as the attribute's category
argument and three times as ordinary literals elsewhere. Guard on a debug-action
LABEL and you need UTF-8; guard on the same text written in a method body and
you need UTF-16. Which is the whole argument for keying on TYPE NAMES instead,
and for searching both encodings whenever a literal is the only handle.
--------------------------------------------------------------------------
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_DLL = os.path.join(ROOT, "Assemblies", "ShiftChange.dll")

# Type names, so UTF-8 — see the heap note above. The scene builders compile
# out under Release because their files are wrapped in #if SCENES; the harness
# types because theirs are wrapped in #if HARNESS.
FORBIDDEN_TYPES = [
    "DebugTools_DemoStage",
    "DebugTools_PreviewStage",
    "DebugTools_PoolStage",
    "DebugTools_Menu",
    "DebugTools_Explain",
    "DebugTools_RecRoomStage",
    "DebugTools_ExportScene",
    "Patch_HarnessAutoRun",
    "DebugTools_LifecycleHarness",
    "DebugTools_SaveRoundTrip",
    "DebugTools_Fixtures",
    "HarnessFixtures",
]

# A literal, so UTF-16 — the one place we cannot key on a type.
FORBIDDEN_LITERALS = [
    "shiftchange-harness",
]

# The catch-all, and the only check here that covers code nobody has written
# yet: whatever a future harness file is called, it will contain this word
# somewhere. Measured on the shipping build, the word appears zero times in
# either encoding, so the bar is exact rather than a threshold.
#
# If a player-facing string ever legitimately contains "harness" — an apparel
# name, a translation key — this fires, and the fix is to narrow THIS list,
# not to delete the check.
FORBIDDEN_TOKENS = [
    "harness",
]

# Present, or the absence checks above mean nothing. The mod without these is
# not a mod.
REQUIRED_TYPES = [
    "CompShiftStand",
    "JobDriver_SwapAtStand",
    "SwapPlan",
    "Patch_JobInterception",
    "Dialog_SetStandWorkTypes",
]

failures = []


def fail(message):
    failures.append(message)


def utf8_count(blob, needle):
    return blob.count(needle.encode("utf-8"))


def utf16_count(blob, needle):
    return blob.count(needle.encode("utf-16-le"))


def token_hits(blob, token):
    """Every readable run containing the token, in both encodings.

    The UTF-16 pass strips the interleaved NULs first rather than searching for
    a decorated pattern, so a hit reports the same way whichever heap it came
    from.
    """
    pattern = re.compile(br"[\x20-\x7e]{0,40}" + re.escape(token.encode("utf-8"))
                         + br"[\x20-\x7e]{0,40}", re.IGNORECASE)
    hits = [match.decode("ascii", "replace") for match in pattern.findall(blob)]
    wide = blob.decode("utf-16-le", "ignore").encode("ascii", "ignore")
    hits += [match.decode("ascii", "replace") for match in pattern.findall(wide)]
    return hits


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_DLL
    if not os.path.exists(path):
        print("FAIL no assembly at %s" % path)
        return 1
    blob = open(path, "rb").read()

    for name in FORBIDDEN_TYPES:
        if utf8_count(blob, name):
            fail("%s is present — this is a Debug, Media or -p:Harness=true "
                 "build. Rebuild with plain -c Release, which also sweeps "
                 "Assemblies/." % name)

    for literal in FORBIDDEN_LITERALS:
        if utf16_count(blob, literal):
            fail("the \"%s\" launch flag is present (found in UTF-16, which is "
                 "where literals live) — dev tooling does not ship. Rebuild "
                 "with plain -c Release." % literal)

    for token in FORBIDDEN_TOKENS:
        hits = token_hits(blob, token)
        if hits:
            fail("the word \"%s\" survives in the assembly (%d place(s), e.g. "
                 "%r) — something dev-only is not behind #if HARNESS."
                 % (token, len(hits), hits[0]))

    for name in REQUIRED_TYPES:
        if not utf8_count(blob, name):
            fail("%s is MISSING — this assembly is not the mod. Something "
                 "over-gated it, or the build wrote somewhere else." % name)

    for failure in failures:
        print("FAIL %s" % failure)
    if failures:
        print("\n%d problem(s) in %s" % (len(failures), path))
        return 1
    print("shipped dll: no debug scenes, no harness, feature surface intact")
    return 0


if __name__ == "__main__":
    sys.exit(main())
