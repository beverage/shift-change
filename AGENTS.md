# AGENTS.md

A RimWorld 1.6 mod: one C# assembly, a job def, and an XML patch that adds two
components to the vanilla Odyssey outfit stand. No art, no apparel, no new
buildings.

| Doc | Contents |
|---|---|
| [README.md](README.md) | what the mod does, for players |
| [docs/DESIGN.md](docs/DESIGN.md) | engine interfaces and why each took its shape |
| [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) | build, debug, test loop, file map |

## Verify your work

```bash
dotnet build Source/ShiftChange/ShiftChange.csproj -c Release
```

Release is not optional at the end of a session: Debug builds are instrumented
with a hot-reload rig and write to the same output path. Committing one ships a
broken mod. CI rejects it, but only after you have pushed.

There is no unit test suite. Behaviour is verified in game, on a clean Release
restart. Do not claim a behavioural change works without saying how it was
checked.

One exception, and it is narrow — the lifecycle harness:

```bash
devtools/run-harness.sh          # ~30s, four-mod profile, no hands
devtools/run-harness.sh --full   # the development profile; run before a release
```

It asserts where the ledger lands after each despawn/death/banishment event, by
firing the engine's real entry points at a throwaway fixture, and exits non-zero
if a case fails. Touch `PostDeSpawn`, `PostSwapMap`, `ReleaseBorrower`,
`AbandonLedger` or `Patch_UnclaimStands` and run it.

A harness pass is not a play observation, and a `GAP` line is a known failure
being tracked, not a pass. There are no gaps today. It does now run
`JobDriver_SwapAtStand` for real, through the pawn's own tracker, so "the
driver builds a correct ledger" is covered; a save/load round trip and a real
gravship launch are still out of reach.

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
