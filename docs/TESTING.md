# Testing

This mod's behaviour is covered by a suite that runs **inside RimWorld**, against
the real engine, in about twenty seconds. This document says what it covers, what
it does not, and why it is built the way it is.

The short version: every bug found in review has a case that fails without its
fix, and the rules the store description promises players are asserted against
the code. Neither of those was true a day before this was written.

## Run it

```bash
devtools/run-harness.sh              # minimal mod list — the iteration loop
devtools/run-harness.sh --full       # your real mod list — the release gate
devtools/run-harness.sh --alongside  # a second instance beside a live game
```

It builds Release, launches RimWorld with `-quicktest -shiftchange-harness`,
waits for the game to run every case and quit itself, prints the report, and
exits non-zero if anything failed.

It runs **isolated**: `-savedatafolder` gives the test instance its own
`ModsConfig.xml`, `Saves/` and prefs, and Unity's `-logfile` moves `Player.log`
there too. Nothing in your installation is read or written, and `--full` copies
your mod list in rather than swapping the live file. It will not touch a game it
did not start.

| Mod list | Wall clock |
|---|---|
| Minimal (4 mods) | ~20 s |
| Development (233 entries) | ~125 s |
| Minimal, alongside a live colony | 25 s paused, several minutes if actively ticking |

By hand: dev mode → **Shift Change** → **Run lifecycle harness**, then click a
clear 7×7 area. The debug menu has a search box; type `lifecycle`.

## What is covered

Thirteen cases. The kinds matter more than the count.

### Regression — a bug that happened, and must not again

| Case | Guards |
|---|---|
| `a stand that displaces nothing still hands the uniform back` | A stand whose stock shared no apparel layer with what the pawn wore donated its uniform permanently and emptied itself forever. Silent and cumulative. |
| `gravship flight keeps the ledger` | A flight released the ledger for every `DestroyMode`, so a colonist landed permanently in uniform and their own clothes became the room's uniform. |
| `borrower banishment reaps the ledger` | `PawnBanishUtility.Banish` never reaches `UnclaimAll`, so the stand held a ledger for a colonist who was gone. Includes the harder half: it must survive that pawn being recruited back. |
| `repeated faults disable interception, a load re-arms it` | One exception disabled the mod for the whole process — through save loads and new colonies — with nothing shown to the player. |
| `a freed stand catches up a colonist working bare` | The freed-stand announcement fired before the tracker released the pawn's reservation, so the catch-up could never fire at all. |

### Driver — the ledger is built correctly in the first place

| Case | Covers |
|---|---|
| `the driver returns own clothes and their forced flags` | A full round trip through `JobDriver_SwapAtStand` on the pawn's own tracker: clothes parked and returned, and the force-worn flag captured before removal and restored after. |

### Functional — the rules we promise players

| Case | Promise |
|---|---|
| `the rules the description promises hold` | Automatic work only; emergencies never delayed; drafted pawns left alone; work the stand does not serve is ignored. |
| `a meal break gets them out of uniform first` | A meal is a sit-down break, so the uniform comes off wherever the food is stored — unless it is already in hand or pack, which is just eaten. |

### Lifecycle, integrity, and the harness itself

| Case | Covers |
|---|---|
| `plain despawn (deconstruct, minify) releases the ledger` | Deconstruction releases the ledger and clears forced flags. |
| `borrower death reaps the ledger` | Death routes through `UnclaimAll` and the reaper fires. |
| `gravship flight without the borrower releases it` | A colonist left behind cannot walk back, so the ledger goes and the forced flags with it. |
| `the room-role table resolves` | Every `RoomRoleDef` and `WorkTypeDef` the mod names still exists. A rename empties the table silently and the mod then does nothing at all. |
| `the harness counts its own results correctly` | `Expect` returns the condition and reports; `Case` counts. Without this the runner can grep a green log out of a suite that checks nothing. |

## What is not covered

Stated plainly, because a coverage claim without this is worth very little.

- **Save and load.** No case writes a save and reads it back. Scribing, and the
  repair branches in `PostSpawnSetup`, are exercised only in play.
- **A real gravship launch.** The flight case drives `DeSpawn(WillReplace)` →
  `Spawn` → `PostSwapMap`, which is what the engine does, but no gravship is
  ever built or flown.
- **The walk.** Fixtures stage the pawn on the stand's interaction cell, so
  pathing, reachability and interruption during the walk are untested. See the
  first engine trap below for why.
- **Mod compatibility.** `--full` proves the suite passes with 233 mods loaded.
  It does not prove Shift Change behaves correctly alongside any particular one.
- **UI.** No case draws a gizmo, opens the work-type dialog, or checks an
  inspect string.
- **Performance.** Nothing is timed.
- **Everything not listed above.** Pooling on/off, the optimizer pause, the
  recolor guard, `SwapPlan`'s rollback path, the change-back latch and the retry
  cooldown all have no case yet.

Play observation is still required. A green run is a floor, not a certificate.

## The rules the suite follows

**Drive the engine's real entry points, never our own handlers.** Cases call
`Thing.DeSpawn`, `GenSpawn.Spawn`, `Pawn.Kill`, `PawnBanishUtility.Banish`, the
real `Patch_JobInterception.Prefix` and the real `JobDriver_SwapAtStand`. A
harness that hand-rolls the call sequence tests the author's model of the engine
and certifies whatever that model got wrong — which, given the two traps below,
it would have.

**Every negative assertion needs a positive control.** "Did not divert" is
worthless alone: a stand that was never eligible satisfies it too. So the
drafted assertion is bracketed by an undrafted one, and the decision table opens
by proving the baseline fires. Three separate assertions in this suite were
tautologies in draft and passed against broken code.

**A timeout reports state, not just failure.** Both engine traps below were
diagnosed in one run each because the pump prints the toil index, tick budget,
driver type and pawn position when it gives up. A bare "timed out" would have
cost several ten-minute cycles apiece.

**Known gaps are counted apart from failures.** A case can be marked `GAP` with
its reason recorded — it still runs, and it reports `FIXED` if it ever starts
passing. There are none today. The mechanism exists because a suite that always
prints one failure gets ignored within a week.

## Two engine traps, paid for once each

Neither is inferable from the API surface. Both are recorded at their call
sites.

**Pathfinding in 1.6 is asynchronous.** `Pawn_PathFollower.PatherTick` waits on
a path request served by a job system that the game's own update loop drives —
not by `Pawn.DoTick()`. Ticking a single pawn therefore leaves it flagged
`moving` forever, one cell short of the stand. Fixtures stage the pawn *on* the
interaction cell so no path is ever requested.

**Jobs are pooled.** When a job ends it returns to `JobMaker`'s pool and is
handed straight back out for the pawn's next job. A `Job` reference held across
its own completion silently becomes a different job — the first version of the
pump spent 5000 ticks watching a `JobDriver_WaitMaintainPosture` wearing our job
object. The pump watches `jobs.curDriver`, which is built per job and never
pooled.

## Static checks

`devtools/check-invariants.py` runs locally and in CI. No game, no build, no
network.

| Check | Why |
|---|---|
| Hot-reload invariants | A `private` member or an auto-property compiles clean and throws `FieldAccessException` in a hot-swapped session. |
| Translation keys, both directions | A key used but not defined renders as the raw key on the player's screen, with no log line. |
| XML-to-C# type bindings | XML names types as strings; rename one and the comp never attaches, the mod loads, and nothing happens. |
| `<Patch>` root, recursively | A plural root discards every operation in the file, and `xmllint` still passes. |
| Preview.png size | Steam rejects a Workshop preview over 1 MiB and the game does not check, so it fails mid-publish as a bare result code. |

CI also runs the BBCode validator over the store description, because an
unclosed tag makes Steam render the rest of the page as literal text.

Worth knowing what these replaced: two shell greps, anchored to the start of a
line and to a single line. Measured against a file of realistic declarations —
`static private int x;`, `[Attr] private int y;`, and multi-line auto-properties,
which is the style this codebase actually writes — the greps found **0 of 7**.

## Why this exists

The mod was played extensively in a live colony before any of this was written,
and play did find real bugs: the reservation carry, the optimizer pause, the
recolor guard.

Then an adversarial review read the whole thing against the decompiled engine
and found three release blockers that play had not. One of them had been sitting
on the demo film set: the researcher's stand held a Shell-layer coat over an
OnSkin tunic, displaced nothing, and donated its uniform on every take. It was
noticed only as "the change reads as nothing happening" and fixed with a
costume change.

Two of those fixes then shipped on engine reasoning alone, with no test. Both
turned out to be correct — but that was not known at the time, and the gravship
one had been reasoned about once already and got it wrong.

That is the argument for the suite. Not that reasoning is unreliable, but that
it is unfalsifiable until something runs.

## Adding a case

Register it in `RunHarness`. Use `Case(map, pad, name, body)` for a fixture with
a checked-out ledger, `Case(map, pad, name, stage, body)` to stage your own, or
`Case(name, body)` for one that needs no map.

Two staging helpers: `Build()` hand-assembles a checked-out state for lifecycle
cases; `Stage(kit, enclose, capableOf)` produces an undressed pawn for cases that
want to watch the driver work, and can wall the pad into a proper room when the
case depends on room scoping.

Assert with `Expect(condition, what)`. Pair every negative with a control.
