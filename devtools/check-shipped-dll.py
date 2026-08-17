#!/usr/bin/env python3
"""Assert the committed assembly is a shippable Release build.

    ./devtools/check-shipped-dll.py [path/to/ShiftChange.dll]

Two properties, and they pull in opposite directions:

  1. NO SCENES fixtures. The debug stage builders each GenDebug.ClearArea a
     200-320 cell footprint — destroying buildings, stock and any pawn standing
     in them — and then leave permanent player-faction colonists, owned
     buildings and rewritten terrain behind. A Debug or Media dll staged by
     mistake puts that in a player's debug menu, one unconfirmed click away.

  2. The harness DOES ship. -shiftchange-harness is the release gate, and the
     gate is only worth running if it asserts against the literal assembly
     players install. Over-gating would delete the gate silently, leaving CI
     green and the test asserting nothing.

Run it before pushing; CI runs the same script.

--------------------------------------------------------------------------
THE TRAP THIS SCRIPT EXISTS TO AVOID (verified 2026-08-17)

A .NET assembly keeps names in THREE places with DIFFERENT encodings:

    #Strings  type and member names       UTF-8
    #US       string literals in code     UTF-16
    #Blob     attribute ARGUMENTS         UTF-8, length-prefixed

So `grep -a "DebugTools_DemoStage"` works (a type name, UTF-8) while
`grep -a "Run lifecycle harness"` and `grep -a "shiftchange-harness"` find
NOTHING WHATEVER THEY SHIP — they are literals, stored as UTF-16. Measured on
the real Release dll: the launch flag is present five times and an ASCII grep
reports zero.

The third case is the sneaky one, because it splits strings that LOOK alike.
Measured on the Media dll: the debug action's own label `"Dev tools"` is
utf8=1/utf16=0 (it is an attribute argument, so it lives in the blob heap),
while `"Shift Change"` is utf8=1/utf16=3 — once as the attribute's category
argument and three times as ordinary literals elsewhere. Guard on a debug-action
LABEL and you need UTF-8; guard on the same text written in a method body and
you need UTF-16. Which is the whole argument for keying on TYPE NAMES instead.

A guard written on literals therefore passes forever and asserts nothing. Key
every check on a TYPE NAME where possible; when a literal is genuinely the
only handle, search utf-16-le explicitly, as blob() below does.
--------------------------------------------------------------------------
"""

import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_DLL = os.path.join(ROOT, "Assemblies", "ShiftChange.dll")

# Type names, so UTF-8 — see the heap note above. These compile out under
# Release because their files are wrapped in #if SCENES.
FORBIDDEN_TYPES = [
    "DebugTools_DemoStage",
    "DebugTools_PreviewStage",
    "DebugTools_PoolStage",
    "DebugTools_Menu",
]

# The release gate. Patch_HarnessAutoRun is a type name (UTF-8);
# DebugTools_LifecycleHarness holds the shared Run body that both entry points
# call, and DebugTools_Fixtures the primitives it builds fixtures from — all
# three ship in every configuration, by design.
REQUIRED_TYPES = [
    "Patch_HarnessAutoRun",
    "DebugTools_LifecycleHarness",
    "DebugTools_Fixtures",
]

# A literal, so UTF-16 — the one place we cannot key on a type.
REQUIRED_LITERALS = [
    "shiftchange-harness",
]

failures = []


def fail(message):
    failures.append(message)


def utf8_count(blob, needle):
    return blob.count(needle.encode("utf-8"))


def utf16_count(blob, needle):
    return blob.count(needle.encode("utf-16-le"))


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_DLL
    if not os.path.exists(path):
        print("FAIL no assembly at %s" % path)
        return 1
    blob = open(path, "rb").read()

    for name in FORBIDDEN_TYPES:
        if utf8_count(blob, name):
            fail("%s is present — a SCENES build (Debug or Media) was "
                 "committed. Rebuild with -c Release, which also sweeps "
                 "Assemblies/." % name)

    for name in REQUIRED_TYPES:
        if not utf8_count(blob, name):
            fail("%s is MISSING — the release gate cannot run against this "
                 "assembly. Something over-gated it behind #if SCENES." % name)

    for literal in REQUIRED_LITERALS:
        if not utf16_count(blob, literal):
            fail("the \"%s\" launch flag is MISSING (searched UTF-16, which is "
                 "where literals live) — run-harness.sh would launch the game "
                 "and never trigger a run." % literal)

    for failure in failures:
        print("FAIL %s" % failure)
    if failures:
        print("\n%d problem(s) in %s" % (len(failures), path))
        return 1
    print("shipped dll: no debug scenes, release gate intact")
    return 0


if __name__ == "__main__":
    sys.exit(main())
