# AGENTS.md

A RimWorld 1.6 mod: one C# assembly, a job def, and XML patches that add two
components across the outfit-stand family — the vanilla Odyssey stand, and
Outfit Stands Plus' powered stands when that mod is present. No art, no
apparel, no new buildings.

| Doc | Contents |
|---|---|
| [README.md](README.md) | what the mod does, for players |
| [docs/DESIGN.md](docs/DESIGN.md) | engine interfaces and why each took its shape |
| [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) | build, debug, test loop, file map |
| [CHANGELOG.md](CHANGELOG.md) | what changed in each release; the Workshop change notes are made from it |

## Verify your work

```bash
dotnet build Source/ShiftChange/ShiftChange.csproj -c Release
```

Release is not optional at the end of a session: Debug and Media builds write to
the same output path, Debug is instrumented with a hot-reload rig, and both
carry the `SCENES` fixtures. Committing either ships a broken or a dangerous
mod. CI rejects it, but only after you have pushed, so run this too:

```bash
python3 devtools/check-shipped-dll.py
python3 devtools/check-invariants.py
```

The second one is not about the dll and applies to **every** commit, docs and
media included: among other things it rejects a private tracker id appearing in
any tracked file. It runs in CI on every push, so skipping it locally only moves
the failure to a place where fixing it costs a second commit.

**Three configurations: `Debug` and `Media` define `SCENES`, `Release` does
not.** Anything that builds a scene — the demo and preview stages, the whole
debug menu — goes behind `#if SCENES` and must never reach a player. These are not harmless menu entries: each stage
builder clears a 200–320 cell footprint, destroying every building, item and
pawn inside it, then leaves permanent colonists and buildings behind.

**The tests ship, and load only with DevTools.** The harness is a project of
its own, `Source/ShiftChange.Tests` (.NET 10, for 1.7, against DevTools), built
against the Release dll into `Tests/Assemblies/`, which `LoadFolders.xml` loads
only while DevTools is active. Both packaging steps ship `Tests/` and
`LoadFolders.xml`; a player who has not installed DevTools loads none of it,
and the mod's dll carries nothing of the harness but an `InternalsVisibleTo`
for it. A harness run clears a pad, spawns colonists and writes save files, so
the gate is what keeps it from any player who has not opted in.
`run-harness.sh` builds both and runs the tests.

**Commit the test dll with the mod's.** No CI job can build the tests without
DevTools, so `Tests/Assemblies/ShiftChange.Tests.dll` ships as committed. It
carries a fingerprint of every source under `Source/`, and
`check-shipped-tests.py` fails a push whose dll was built from other sources,
a `LoadFolders.xml` that loads `Tests/` ungated or drops the root for a
version, and a test dll anywhere else. After any change under `Source/`,
rebuild both dlls (`run-harness.sh` leaves them in place) and commit both.

**Harness code goes in the tests' project, never the mod's.** No file under
`Source/ShiftChange/` may be named for the harness, use DevTools or read
`CommandLineArgPassed`. `HARNESS` is defined only by the tests' project; in the
mod's it may guard, whole-file, only a file the tests also compile (today
`Dev/DebugTools_Fixtures.cs`, as `SCENES || HARNESS`). So the tests run against
the very dll that ships, which is what the old arrangement, where the harness
shipped inside it, wanted. `check-invariants.py` enforces the layout and
`check-shipped-dll.py` the absence in the artifact, so breaking either is a
failed check rather than a surprise at the next release.

**The six `[TweakValue]` fields also ship, on purpose.** The bar is
destructiveness, not reachability: they are how a player is walked through
diagnosing a report. Do not "clean them up".

There is no unit test suite. Behaviour is verified in game, on a clean Release
restart. Do not claim a behavioural change works without saying how it was
checked.

One exception, and it is narrow — the lifecycle harness:

```bash
devtools/run-harness.sh          # ~30s, a four-mod list, no hands
devtools/run-harness.sh --full   # whatever mod list is active; run before a release
devtools/run-harness.sh --with=khamenman.outfitstandsplus   # the four, plus one mod a case needs
devtools/run-harness.sh --with-after=khamenman.outfitstandsplus   # the same, loaded after us
```

Each case runs as a test under SmashPhil's DevTools, so 1.7 only, with a
DevTools build that has SmashPhil/DevTools#17. Each is a `[Test]` told as a
story, in a fixture under `Source/ShiftChange.Tests/` named for what a player
sees (docs/TESTING.md, "Adding a case").

It asserts where the ledger lands after each despawn/death/banishment event, by
firing the engine's real entry points at a throwaway fixture, and exits non-zero
if a case fails. Touch `PostDeSpawn`, `PostSwapMap`, `Notify_MapRemoved`,
`ReleaseBorrower`, `AbandonLedger` or `Patch_UnclaimStands` and run it.

A harness pass is not a play observation, and a skipped test (a `GAP` line in
its story) is a known failure being tracked, not a pass. **Six gaps are expected on the default list**: the
Rimatomics table and marker cases skip themselves when Rimatomics is not
loaded, and the Outfit Stands Plus, Common Sense and Pick Up And Haul cases
when their mod is not (`--with=` loads each). The sixth is Complex Jobs'
two delivery givers, probed by the delivery case only when that mod is loaded
(`--with=FrozenSnowFox.ComplexJobs`). So a clean default run reads
`0 Failed` with 6 skipped and exits 0, or 5 skipped under
`--with=FrozenSnowFox.ComplexJobs`, where the delivery gap closes. Any other gap, or any failure, is real. It does now run
`JobDriver_SwapAtStand` for real, through the pawn's own tracker, so "the
driver builds a correct ledger" is covered, and the player-facing rules have
functional cases. Save/load round trips are covered by four cases that load
through the engine's synchronous loader (`SavedGameLoaderNow`;
`GameDataSaveLoader.LoadGame` is async and never returns control) and assert on
the written file as well as the loaded state. They run last, since they replace
`Current.Game`. A real gravship launch is the one thing still out of reach.

**Five engine traps live in the driver cases**, all invisible from the API and
all already paid for once. Pathfinding is asynchronous, so ticking a
single pawn never completes a walk — step the game's clock (`GameClock`), never
`Pawn.DoTick`. Jobs are pooled, so a `Job` reference held across its own
completion silently becomes the pawn's *next* job — watch `jobs.curDriver`,
never the job. The whole map ticks on the clock, so what a case arranged moves
while a swap runs — Odyssey's air drifts between rooms, a spawned colonist
wanders off, a Goto ends — and the case holds it (`Scene.Hold`, `StayPut`,
`WorkingAt`). A generated colonist is not tended, so one with asthma is an
urgent patient — staging removes what it arrives needing tended. And the
game brings pawns of its own: a test that kills or kidnaps the colony's last
colonist brings the stranger in black, who hauls a test's items first, and the
map spawns wild animals, which eat them — `ReadyPad` sends away every pawn a
test did not stage.
docs/TESTING.md, "Five engine traps".

**Every colony exit must reach the reaper.** Vanilla routes death, trade,
kidnap and map exit through `Pawn_Ownership.UnclaimAll`, which
`Patch_UnclaimStands` hooks. **Banishment does not** — it runs
`pawn.SetFaction(null)` and stops — so `Patch_BanishStands` calls the same
reaper directly. Reap eagerly, never by disbelieving the ledger at the read
points: a departed pawn can be recruited back, and a ledger that was only being
disbelieved comes back with them.

## Rules that compile fine and fail later

**Declare members `internal`, never `private`, and never write an
auto-property.** Hot-swapped method bodies execute in a separate assembly, and
Unity's Mono honours only `InternalsVisibleTo`. A `private` field or an
auto-property's backing field throws `FieldAccessException` at runtime. CI greps
for both.

**Assigning a `TaggedString` to a `string` silently strips rich text.**
`TaggedString`'s implicit conversion to `string` calls `StripTags()`
(`TaggedString.cs:120-123`), so `someCommand.defaultDesc = "Key".Translate()`
deletes every `<b>` and `<color>` in the string — no error, no literal tags on
screen, just quietly unformatted text. Use `.Translate().RawText` when the
markup is meant to survive. Rich text is `<b>`, `<i>`, `<size>`, `<color>`;
`<u>` is TextMeshPro-only and does not work in RimWorld's IMGUI at all.

**Every wearability predicate belongs in `SwapPlan.cs`.** "Can this pawn wear
what this stand holds" is asked by the selector and again by the driver. It was
once two implementations, they disagreed, and pawns walked across the base to
swap nothing. One answer, one file.

**Route new session-scoped statics through `SessionGuard`.** Anything keyed on
`thingIDNumber` or stamped with `TicksGame` is wrong across a save load, not
merely leaky: both counters restart per save, so a stale entry can sit in the
future and silently block a pawn.

**Do not bind hotkeys on gizmos.** On a storage building N, J and F are taken by
settings copy, settings paste and forbid. The trap is inherited: vanilla's
`CompAssignableToPawn` hardcodes `Misc4` (N), so a subclass has to strip it.

**A `JobDriver`'s class name is scribed into the save; a comp's is not.** A
pawn saved mid-job records its driver by class name, so renaming the driver
costs any such pawn that job on the next load, with errors in the player's log,
and nothing more. Comps are rebuilt from the def on load and read back only
their scribed keys, so a comp class renames freely but a renamed key loses its
value. Harmony patch classes carry no constraint and rename freely.

**A patch file's root element is `<Patch>`, singular.** The plural form
discards every operation in the file with a single load error that is easy to
miss, and `xmllint` still passes.

**Never hot reload defs.** Vanilla's own command and the community mod both
corrupt live state. Restart the game.

**The job-room resolver is shared, so it must not read the map.** Dressing and
changing back both call `TargetCell`, and they have to answer identically for
the same job. Resolve on the job def and its targets only. Make the answer
depend on which room happens to hold a free stand and the two arms disagree,
which is a pawn walking between the stand and the work forever.

**Anything reading `Room.ContainedAndAdjacentThings` needs a cache.** It clears
and rebuilds a set and a list on every call, and `WorkTypes` — which
`RoomContentsWork` feeds — is read every frame to draw a gizmo label. Cache on
room ID and role plus a tick interval, as `CompShiftStand.AutomaticWorkTypes`
does: a bed set to medical changes the role and keeps the ID.

**The Outfit Stands Plus button patch changes one call, and keeps its
order.** `Patch_OutfitStandsPlusUseButton` is a transpiler on their stand
button's iterator body: it swaps their
`AllBuildingsColonistOfClass<Building_OutfitStand>()` walk for `Stands`, and
applies only while their original IL still makes that call. Keep it that
narrow. Replacing their whole method would need a gate that turns off on any
change they make; this one retires itself when the call goes. And keep the
stand list in the colony building list's order: a colonist's two buttons
merge into one, and a click sends them to whichever stand came first. After
touching it, run the harness with `--with=khamenman.outfitstandsplus` and again
with `--with-after=`, since `loadAfter` cannot make players load it after OSP.

**Never patch a `[MustTranslate]` field on another mod's def.** Def patches
apply while defs load; `InjectIntoData_AfterImpliedDefs`
(`PlayDataLoader.cs:333`) runs afterwards and stamps the mod's own DefInjected
translation back over yours. The patch works in English and silently does
nothing in every language that mod ships. Override it on our side instead, in
our own keyed strings — `WorkTypeLabels` is the pattern.

## Scope rules

**The mod ships no art and no apparel.** That is positioning, not laziness. If a
change seems to need a texture, question the change first.

**Odyssey is a hard requirement, and the fallback you are about to suggest is
not allowed.** The outfit stand's C# class ships inside the base game's assembly
for every player, so a def pointing `thingClass` at `Building_OutfitStand` would
work without the expansion. Ludeon's published modding rules forbid exactly
that: expansion code may not run for players who do not own the expansion. A
non-Odyssey version means writing our own rack from scratch.

## Branching — the mod is released

Since v1.0.0 (2026-08-14), **main is the shippable branch**: every commit on
it must be releasable as-is, because the Workshop upload is staged from a
local build of it. Long-running feature threads live on `feature/<thread>`
branches (`feature/recreation`, `feature/enclosures`, …) and merge back via
pull request when the thread is accepted. CI (`ci.yml`) runs its checks on
every push and pull request, and the tests on Fly on every push to a branch
here, never on a pull request. Small fixes and docs may still land on
main directly. Workshop publishing is manual and deliberately not automated
(`.github/workflows/ci.yml` says so in as many words); a `v*` tag
additionally produces a GitHub release zip.

One machine-local consequence: the game's `Mods/` folder holds a symlink to
this checkout, so a running game loads WHATEVER BRANCH is checked out —
check out main before a play session that should see only released
behavior, and mind that two simultaneous code threads need two checkouts
(`git worktree`), not two branches of one tree.

## Comments

Write comments for a reader who has this repository and nothing else: no
decompiled game, and none of the history.

- A comment states the fact this code depends on, in plain words, and stops
  there: what a reader needs to understand the code and not to break it. Leave
  out what is true but secondary: consequences elsewhere, background,
  alternatives not taken.
- A fact about code this mod does not own (the game, a DLC, another mod) stays
  at the site that depends on it, wherever a reader who did not know it would
  "fix" the code into a bug. State it in words, naming the types and methods
  involved, with no `File.cs:123` citation: line numbers move with every game
  update, and a contributor has no decompile to follow them into.
- A member summary says what the member is for, in one line where possible. An
  inline comment explains a line that is not obvious, and nothing that is.
- No history: no dates, no "used to" or "was once", no story of the bug or the
  review that produced the code, no capitals or bold for emphasis. History
  belongs in git and CHANGELOG.md. A test may say in one sentence
  that it guards a past bug. `check-invariants.py` rejects a date or a
  `File.cs:123` citation in any comment under `Source/`.
- The reason lives at the site. Point at `docs/DESIGN.md` only for an argument
  that spans several files and is already written there, and never restate it.
- Aim for about one comment line per five or six lines of code, and keep lines
  within 100 columns.

Many older comments do not follow this yet and are being rewritten, comments
only. Do not copy their style into new code.

## Commits

One line, no body: an emoji, then an imperative subject.

```
✨ Add the change-back gizmo
🐛 Fix the ledger losing forced-apparel flags
📝 Document the F12 reference setup
```

`✨` feature, `🐛` fix, `♻️` refactor, `🔥` removal, `🔧` tooling, `📝` docs,
`✅` tests, `⬆️` dependency bump. Pick by the nature of the change, not the
files touched. Match the existing log.
