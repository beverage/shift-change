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
not.** Anything that builds a scene — the demo and preview stages, the
harness's `[DebugAction]`, the whole debug menu — goes behind `#if SCENES` and
must never reach a player. These are not harmless menu entries: each stage
builder clears a 200–320 cell footprint, destroying every building, item and
pawn inside it, then leaves permanent colonists and buildings behind.

**The harness BODY and `-shiftchange-harness` ship in every configuration**, and
that is deliberate — `run-harness.sh` builds plain Release and drives it through
the flag, so the release gate asserts against the literal dll players install.
Over-gating silently deletes the gate; `check-shipped-dll.py` fails on that too,
not just on the reverse.

**The five `[TweakValue]` fields also ship, on purpose.** The bar is
destructiveness, not reachability: they are how a player is walked through
diagnosing a report. Do not "clean them up".

There is no unit test suite. Behaviour is verified in game, on a clean Release
restart. Do not claim a behavioural change works without saying how it was
checked.

One exception, and it is narrow — the lifecycle harness:

```bash
devtools/run-harness.sh          # ~20s, a four-mod list, no hands
devtools/run-harness.sh --full   # whatever mod list is active; run before a release
```

It asserts where the ledger lands after each despawn/death/banishment event, by
firing the engine's real entry points at a throwaway fixture, and exits non-zero
if a case fails. Touch `PostDeSpawn`, `PostSwapMap`, `ReleaseBorrower`,
`AbandonLedger` or `Patch_UnclaimStands` and run it.

A harness pass is not a play observation, and a `GAP` line is a known failure
being tracked, not a pass. There are no gaps today. It does now run
`JobDriver_SwapAtStand` for real, through the pawn's own tracker, so "the
driver builds a correct ledger" is covered, and the player-facing rules have
functional cases. Save/load round trips are covered by three cases that load
through the engine's synchronous loader (`SavedGameLoaderNow`;
`GameDataSaveLoader.LoadGame` is async and never returns control) and assert on
the written file as well as the loaded state. They run last, since they replace
`Current.Game`. A real gravship launch is the one thing still out of reach.

**Two engine traps live in the driver cases**, both invisible from the API and
both already paid for once. Pathfinding in 1.6 is asynchronous, so ticking a
single pawn never completes a walk — fixtures stage the pawn on the stand's
interaction cell. And Jobs are pooled, so a `Job` reference held across its own
completion silently becomes the pawn's *next* job — watch `jobs.curDriver`,
never the job.

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

**Comp and `JobDriver` class names are scribed into save files.** Renaming one
breaks every existing save. Harmony patch classes carry no such constraint and
rename freely.

**A patch file's root element is `<Patch>`, singular.** The plural form
silently discards every operation in the file, and `xmllint` still passes.

**Never hot reload defs.** Vanilla's own command and the community mod both
corrupt live state. Restart the game.

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
pull request when the thread is accepted — PRs are also what runs CI, which
does not fire on plain branch pushes. Small fixes and docs may still land on
main directly. Workshop publishing is manual and deliberately not automated
(`.github/workflows/ci.yml` says so in as many words); a `v*` tag
additionally produces a GitHub release zip.

One machine-local consequence: the game's `Mods/` folder holds a symlink to
this checkout, so a running game loads WHATEVER BRANCH is checked out —
check out main before a play session that should see only released
behavior, and mind that two simultaneous code threads need two checkouts
(`git worktree`), not two branches of one tree.

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
