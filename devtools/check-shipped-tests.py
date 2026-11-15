#!/usr/bin/env python3
"""Assert a mod folder ships its DevTools tests gated, and built from this source.

    ./devtools/check-shipped-tests.py [MOD_FOLDER]   # the checkout, or a staged copy
    ./devtools/check-shipped-tests.py --fingerprint  # what the tests' build embeds

The tests ship: Tests/Assemblies/ShiftChange.Tests.dll, which LoadFolders.xml
loads only while DevTools is active, so a player who has not installed DevTools
loads none of it, and installing it is the opt-in. This checks the folder a
player gets (both packaging steps run it on their staged copy, and CI on the
checkout):

  1. LOADFOLDERS keeps the root for both versions and gates Tests/ on DevTools,
     under 1.7 only. Once the file exists it owns all folder resolution, so a
     version without "/" loads no mod at all, and an ungated Tests/ loads the
     tests for every player.

  2. THE TEST DLL sits only under the gated folder: Tests/ holds that one dll,
     and Assemblies/ holds ShiftChange.dll alone.

  3. THE MOD'S DLL references no DevTools, so it loads without it, and grants
     ShiftChange.Tests its internals, without which every test that touches
     the mod fails in the player's game on an access check.

  4. THE TEST DLL WAS BUILT FROM THESE SOURCES. The mod's dll ships rebuilt by
     each packaging step, but the tests' cannot be: CI's release job has no
     DevTools to compile against, so the committed one ships. A build is not
     byte-identical from one machine to another (the game's own assemblies or
     Krafs' references, measured 2026-11-13), so instead the build embeds a
     fingerprint of every .cs and .csproj under Source/ (see fingerprint()),
     and this compares it with the checkout's. A change to the mod or its
     tests without a rebuild of the tests fails here, before it ships a dll
     that calls a member the mod no longer has.

The sweep for the word "harness" (check-shipped-dll.py) covers the mod's dll
only.
"""

import hashlib
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# The projects whose sources decide the test dll's bytes and what it calls.
FINGERPRINTED = ["Source/ShiftChange", "Source/ShiftChange.Tests"]

# The AssemblyMetadata key ShiftChange.Tests.csproj embeds the fingerprint under.
FINGERPRINT_KEY = "ShiftChange.SourceFingerprint"

DEVTOOLS_ID = "SmashPhil.DevTools"
TESTS_FOLDER = "Tests"
TESTS_DLL = "ShiftChange.Tests.dll"
MOD_DLL = "ShiftChange.dll"

failures = []


def fail(message):
    failures.append(message)


def fingerprint(root=ROOT):
    """SHA-256 over each source file's path and content hash, in path order.

    bin/ and obj/ are build output, so skipped. The build calls this script for
    the value, so it has one definition.
    """
    files = []
    for top in FINGERPRINTED:
        for dirpath, dirnames, filenames in os.walk(os.path.join(root, top)):
            dirnames[:] = [d for d in dirnames
                           if d not in ("bin", "obj") and not d.startswith(".")]
            for name in filenames:
                if name.endswith((".cs", ".csproj")):
                    path = os.path.join(dirpath, name)
                    files.append(os.path.relpath(path, root).replace(os.sep, "/"))
    if not files:
        raise SystemExit("no sources under %s" % ", ".join(FINGERPRINTED))
    digest = hashlib.sha256()
    for rel in sorted(files):
        with open(os.path.join(root, rel), "rb") as handle:
            content = handle.read()
        digest.update(rel.encode("utf-8") + b"\0" + hashlib.sha256(content).digest())
    return digest.hexdigest()


def embedded_fingerprint(blob):
    """The value of the dll's [AssemblyMetadata(FINGERPRINT_KEY, ...)], or None.

    An attribute's arguments live in the #Blob heap as length-prefixed UTF-8,
    the key's string straight before the value's, so the 64 hex digits follow
    the key and a length byte of 64.
    """
    key = FINGERPRINT_KEY.encode("utf-8")
    found = re.findall(re.escape(bytes([len(key)]) + key) + rb"\x40([0-9a-f]{64})", blob)
    return found[0].decode("ascii") if len(found) == 1 else None


def check_load_folders(folder):
    path = os.path.join(folder, "LoadFolders.xml")
    if not os.path.isfile(path):
        fail("no LoadFolders.xml, so Tests/ is never loaded (or, with the file "
             "gone and the dll still there, nothing gates it)")
        return
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as error:
        fail("LoadFolders.xml does not parse: %s" % error)
        return
    versions = {child.tag: [(li.text or "").strip() for li in child] for child in root}
    gates = {child.tag: [li.get("IfModActive") for li in child] for child in root}
    if sorted(versions) != ["v1.6", "v1.7"]:
        fail("LoadFolders.xml lists %s; it must list v1.6 and v1.7, each with the root"
             % sorted(versions))
    for version, entries in versions.items():
        for entry, gate in zip(entries, gates[version]):
            if entry in ("/", "") and gate:
                fail("%s loads the root only with %s; the root must load always" % (version, gate))
    if versions.get("v1.6") != ["/"]:
        fail("v1.6 must load the root and nothing else (DevTools is 1.7 only), "
             "found %s" % versions.get("v1.6"))
    v17 = list(zip(versions.get("v1.7", []), gates.get("v1.7", [])))
    if ("/", None) not in v17:
        fail("v1.7 does not load the root, so under 1.7 the mod would not load at all")
    if (TESTS_FOLDER, DEVTOOLS_ID) not in v17:
        fail("v1.7 does not load %s gated on IfModActive=\"%s\"" % (TESTS_FOLDER, DEVTOOLS_ID))
    for entry, gate in v17:
        if entry not in ("/",) and gate != DEVTOOLS_ID:
            fail("v1.7 loads %s for every player: only the root may load ungated" % entry)


def check_layout(folder):
    tests = os.path.join(folder, TESTS_FOLDER)
    found = sorted(os.path.relpath(os.path.join(dirpath, name), folder).replace(os.sep, "/")
                   for dirpath, _, names in os.walk(tests) for name in names
                   if name != ".DS_Store")
    expected = ["%s/Assemblies/%s" % (TESTS_FOLDER, TESTS_DLL)]
    if found != expected:
        fail("%s/ must hold %s alone, found %s" % (TESTS_FOLDER, expected[0], found or "nothing"))
    assemblies = os.path.join(folder, "Assemblies")
    names = sorted(n for n in os.listdir(assemblies)) if os.path.isdir(assemblies) else []
    if names != [MOD_DLL]:
        fail("Assemblies/ must hold %s alone, found %s (the test dll loads only "
             "from the gated folder)" % (MOD_DLL, names))


def check_mod_dll(folder):
    path = os.path.join(folder, "Assemblies", MOD_DLL)
    if not os.path.isfile(path):
        return  # check_layout reported it
    blob = open(path, "rb").read()
    for encoding in ("utf-8", "utf-16-le"):
        if "DevTools".encode(encoding) in blob:
            fail("%s mentions DevTools (%s): the mod must load without it"
                 % (MOD_DLL, encoding))
    if b"InternalsVisibleTo" not in blob or b"ShiftChange.Tests" not in blob:
        fail("%s grants ShiftChange.Tests no internals (InternalsVisibleTo in "
             "ShiftChange.csproj), so the tests fail an access check in-game" % MOD_DLL)


def check_tests_dll(folder):
    path = os.path.join(folder, TESTS_FOLDER, "Assemblies", TESTS_DLL)
    if not os.path.isfile(path):
        return  # check_layout reported it
    blob = open(path, "rb").read()
    if b"DevTools" not in blob or b"ShiftChange" not in blob:
        fail("%s references neither DevTools nor the mod: not the tests' build" % TESTS_DLL)
    built = embedded_fingerprint(blob)
    current = fingerprint()
    if built is None:
        fail("%s carries no %s: build it with Source/ShiftChange.Tests/"
             "ShiftChange.Tests.csproj, which embeds it" % (TESTS_DLL, FINGERPRINT_KEY))
    elif built != current:
        fail("%s was built from other sources than this checkout's (%s…, now %s…): "
             "rebuild the mod, then its tests, and commit both dlls "
             "(devtools/run-harness.sh does both)" % (TESTS_DLL, built[:12], current[:12]))


def main(argv):
    if argv[1:] == ["--fingerprint"]:
        print(fingerprint())
        return 0
    if len(argv) > 2 or (len(argv) == 2 and argv[1].startswith("-")):
        print(__doc__.split("\n\n")[1])
        return 2
    folder = os.path.abspath(argv[1]) if len(argv) == 2 else ROOT
    check_load_folders(folder)
    check_layout(folder)
    check_mod_dll(folder)
    check_tests_dll(folder)
    for failure in failures:
        print("FAIL %s" % failure)
    if failures:
        print("\n%d problem(s) in %s" % (len(failures), folder))
        return 1
    print("shipped tests: gated on DevTools under 1.7, root loads for both "
          "versions, built from this source")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
