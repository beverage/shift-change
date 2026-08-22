# Testing

How this mod is verified, and why the approach took the shape it did. For build
and debug, see [DEVELOPMENT.md](DEVELOPMENT.md); for what the code interfaces
with, see [DESIGN.md](DESIGN.md).

Engine claims were checked against the decompiled game assembly, version
**1.6.4871**. References like `Pawn_JobTracker.cs:492` point into that
decompilation.

## Constraints

| Constraint | Consequence |
|---|---|
| The engine cannot be mocked | Every claim this mod makes is about how RimWorld behaves. A test double would assert the author's model of the engine, which is the thing most likely to be wrong. The suite therefore runs **inside the game**, and drives the engine's own entry points. |
| A test that is slow is not run | One command, no hands, roughly twenty seconds on the four-mod list. Anything requiring a human to click through menus stops happening within a week. |
| A test that is flaky is worse than none | Every negative assertion carries a positive control; every timeout reports state; the harness asserts its own accounting. Three assertions in draft were satisfiable by the thing they were meant to exclude. |
| It must not touch a live game | Runs against its own save-data folder and its own log, and refuses to signal a process it did not start. The machine this was written on is also the machine somebody plays on. |
| Some things stay out of reach | A real gravship launch cannot be driven in-process, and is named below rather than approximated. The save/load round trip sat in this row until 2026-08-19 — it now runs for real, through the engine's own synchronous loader. |

## Running it

```bash
devtools/run-harness.sh              # a four-mod list — the iteration loop
devtools/run-harness.sh --full       # your own mod list — the release gate
devtools/run-harness.sh --alongside  # a second instance beside a live game
```

Builds Release, launches with `-quicktest -shiftchange-harness`, waits for the
game to run every case and quit itself, prints the report, exits non-zero on any
failure. By hand: dev mode → **Shift Change** → **Run lifecycle harness**, then
click a clear 7×7 area.

**Keep the window focused.** RimWorld is throttled hard in the background, and
a run that loses focus can take many times longer than a focused one — long
enough that a frozen log reads as a hang rather than a stall. A run that appears
stuck at engine startup is usually starved, not broken; the script allows 1200 s
before it gives up. It also means any duration measured from a backgrounded run
is meaningless, so time a run only when the window kept focus throughout.

**The minimal list is for iterating; `--full` is what a release is signed off
on.** `--full` copies whatever mod list is active on the machine running it, so
it is only ever as good as that list — it is not a compatibility matrix, and it
proves nothing about a mod you do not have installed. What it does prove is that
the suite still passes with a large list loaded, which the four-mod list by
construction cannot: that is precisely the environment in which a conflict
cannot appear. It is also what surfaced the fixture flakiness described below.

## Isolation

`-savedatafolder` moves the test instance's `ModsConfig.xml`, `Saves/` and prefs
under `dist/testdata` (`GenFilePaths.cs:93-110, :179`); Unity's `-logfile` moves
`Player.log` there too. Nothing under the live installation is read or written,
and `--full` *copies* the active mod list in rather than swapping it.

An earlier version swapped the live `ModsConfig.xml` back and forth. It worked,
but it edited a player's installation in order to run a test, and the day
something went wrong mid-run it would have been their mod list. The swapper
still exists for interactive sessions; the harness no longer uses it.

The runner launches the game binary directly rather than through `open`, so it
has a real pid, and only ever waits on — or signals — that one. Another instance
already running is somebody's colony with unsaved progress in it: the default is
to stop, and `--alongside` is the deliberate opt-in.

## What the cases assert

Twenty-two cases, in six kinds.

**Regression cases** guard a bug that happened. A stand whose stock shares no
apparel layer with what the pawn wears once donated its uniform permanently and
emptied itself forever; a gravship flight released every ledger aboard and
inverted uniform and civvies; banishment never reached the reaper because
`PawnBanishUtility.Banish` never reaches `UnclaimAll`; a single exception
disabled interception for the whole process; and the freed-stand announcement
fired before the tracker released the pawn's reservation, so the mid-job
catch-up could never fire at all. Each has a case that fails without its fix.

**Driver cases** prove the ledger is built correctly in the first place. They
stage an undressed pawn and run `JobDriver_SwapAtStand` for real through the
pawn's own tracker, both legs. This is where the forced-apparel lifecycle is
verified: `Pawn_ApparelTracker.Notify_ApparelRemoved` clears the forced flag on
every removal, so the driver captures it before removing and restores it after,
and nothing else exercises that path.

**Functional cases** assert the rules the store description promises players —
automatic work only, emergencies never delayed, drafted pawns left alone, work
the stand does not serve ignored, and the meal-break policy in both directions.
That last one is the reason this kind exists: all three descriptions claimed
eating in a room changed nothing, while the code had always done the opposite
deliberately, and nothing caught it until this case was written.

**Recreation cases** cover the joy arm, which shares everything downstream
with the work arm and nothing upstream. One asserts the classifier itself — a
plain joy job carries a `joyKind` and is diverted, reading is excluded by driver
class, and a job that is both work and joy resolves the same way from either
side. One drives a real joy job in a stand's room and asserts the swap. A third
asserts that recreation and work types clear each other on the same stand,
because the exclusivity is what makes the dual-purpose stand unreachable from
the UI rather than merely discouraged.

**Ownership cases** guard the owner list, which went from one pawn to a set.
One walks a stand through pool, one owner, two owners and back, asserting who
may claim it at each step; its load-bearing assertion is the SECOND owner,
because a single-owner reader passes everything before that and fails from
there. The other drives the owner dialog's candidate list through all three
filter states without opening a window, and asserts the one property a filter
must have: an assigned pawn leaves the candidate list but stays visible as an
owner even under a filter that excludes them. A filter that hid the owner you
wanted to remove would be a trap.

**Round-trip cases** save the game, load it back through the engine's own
synchronous loader, and assert on what came out. There are three: a plain trip
that carries the owner, the ledger and the forced flags; a legacy-key save that
must migrate its owner and then re-save under the prefixed key; and a stand
carrying a foreign `CompAssignableToPawn`, whose owner must survive untouched
while ours stays empty.

Each asserts on the written **file** as well as on the loaded comp state. Comp
state alone cannot distinguish a value that scribed correctly from one that
never left the object, and the migration leg's re-save is what shows the
reference was collected rather than only registered. They also assert the
absence of the three engine log lines that mark a contested key — see
`rimworld-docs/gamedata/scribe-system.md`.

Three things about them differ from every other case, all consequences of the
load being real. `GameDataSaveLoader.LoadGame` is unusable here: it queues an
async long event and disposes the game, so control never returns to an
assertion. `SavedGameLoaderNow.LoadGameFromSaveFileNow` is the synchronous
primitive underneath it, and runs both scribe passes inline. These cases
therefore **replace `Current.Game`**, so they run last among the map cases and
register without a fixture — a teardown would clear a pad on a disposed map.
And `Game.LoadGame` ends in `FinalizeInit`, which `Patch_HarnessAutoRun`
postfixes, so a one-shot latch there prevents a nested harness run.

Two further cases cover neither the mod's behaviour nor a past bug, and need no
map at all. One walks the room-role table and asserts every `RoomRoleDef` and
`WorkTypeDef` it names still resolves — the lookups are silent-fail, so a
renamed def empties the table and the mod does nothing at all with no error
anywhere. That table now carries recreation rows as well, including two
third-party pool roles, so the same silence would take the joy arm with it. The
other asserts the harness's own accounting: `Expect` reports and returns, `Case` counts, and a
known gap is counted apart from a failure. Without it the runner can grep a
green log out of a suite that checks nothing.

## What is not covered

- **The repair branches in `PostSpawnSetup`.** The round-trip cases cover
  scribing itself — what gets written, and what survives coming back — but not
  the paths that fix up a ledger found inconsistent on spawn. Those are still
  exercised only in play.
- **A real gravship launch.** The flight case drives `DeSpawn(WillReplace)` →
  `Spawn` → `PostSwapMap`, which is what the engine does, but no gravship is
  built or flown.
- **The walk.** Fixtures stage the pawn on the stand's interaction cell, so
  pathing and interruption mid-walk are untested. See the traps below.
- **Mod compatibility.** `--full` proves the suite passes with one large mod
  list loaded — whichever one is active on the machine that ran it. It proves
  nothing about correct interaction with any particular mod, and nothing at all
  about a mod that was not installed.
- **UI.** No case draws a gizmo, opens the work-type dialog, or reads an inspect
  string.
- **Pooling on and off, the optimizer pause, the recolor guard, `SwapPlan`'s
  rollback, the change-back latch, the retry cooldown.** No cases yet.

A green run is a floor, not a certificate. Play observation is still required,
and the mod's own history is the argument for saying so: days of play found
real bugs that reading never would have, and a later reading found three release
blockers that play had not.

## Rules the suite follows

**Drive the engine's own entry points.** Cases call `Thing.DeSpawn`,
`GenSpawn.Spawn`, `Pawn.Kill`, `PawnBanishUtility.Banish`, the real
`Patch_JobInterception.Prefix` and the real `JobDriver_SwapAtStand` — never our
own handlers directly. A harness that hand-rolls the call sequence tests the
author's model of the engine, and both traps below are cases where that model
would have been wrong.

**Pair every negative with a positive control.** "Did not divert" is worthless
alone, because a stand that was never eligible satisfies it too. The drafted
assertion is bracketed by an undrafted one; the decision table opens by proving
the baseline fires. This is not hypothetical caution: three assertions passed
against broken code in draft, including one that checked a job was "not a swap"
when any job at all satisfied it.

**Make a timeout say where it stopped.** The pump prints the toil index, tick
budget, driver type and pawn position when it gives up. Both traps below were
diagnosed in one run each because of it; a bare "timed out" would have cost
several ten-minute cycles apiece.

**Count known gaps apart from failures.** A case can be marked `GAP` with its
reason recorded — it still runs, and reports `FIXED` if it starts passing. There
are none today. The mechanism exists because a suite that always prints one
failure is ignored within a week.

## Two engine traps

Neither is visible from the API surface. Both are recorded at their call sites.

**Pathfinding is asynchronous.** `Pawn_PathFollower.PatherTick` waits on a path
request served by a job system that the game's own update loop drives, not by
`Pawn.DoTick()`. Ticking a single pawn therefore leaves it flagged `moving`
forever, one cell short of the stand. Fixtures stage the pawn *on* the
interaction cell so no path is ever requested — at the cost of not exercising
the walk, which is vanilla's `Toils_Goto` rather than ours.

**Jobs are pooled.** When a job ends it returns to `JobMaker`'s pool and is
handed straight back out for the pawn's next job, so a `Job` reference held
across its own completion silently becomes a different job. The pump watches
`jobs.curDriver`, which is built per job and never pooled — and it checks the
`JobCondition` the job ended with, because "the driver changed" is equally true
of a job that failed its reservations and died before its transfer toil.

## Static checks

`devtools/check-invariants.py` runs locally and in CI. No game, no build, no
network.

| Check | What it prevents |
|---|---|
| Hot-reload invariants | A `private` member or an auto-property compiles clean and throws `FieldAccessException` in a hot-swapped session. |
| Translation keys, both directions | A key used but not defined renders as the raw key on screen, with no log line. |
| XML-to-C# type bindings | XML names types as strings. Rename one and the comp never attaches: the mod loads, the stands look normal, nothing happens. |
| `<Patch>` root, recursively | A plural root discards every operation in the file, and `xmllint` still passes. The engine walks `Patches/` with `AllDirectories`, so this must too. |
| `About/Preview.png` size | Steam rejects a Workshop preview over 1 MiB and the game does not check, so an oversized one fails mid-publish as a bare result code. |

CI also runs the BBCode validator over the store description, because an
unclosed tag makes Steam render the remainder of the page as literal text.

These replaced two shell greps anchored to the start of a line and to a single
line. Measured against a file of realistic declarations — `static private int x;`,
`[Attr] private int y;`, and the multi-line auto-properties this codebase
actually writes — the greps found none of seven.

## Adding a case

Register it in `RunHarness`. `Case(map, pad, name, body)` gives a fixture with a
checked-out ledger; `Case(map, pad, name, stage, body)` stages its own;
`Case(name, body)` needs no map.

`Build()` hand-assembles a checked-out state for lifecycle cases. `Stage(kit,
enclose, capableOf)` produces an undressed pawn for cases that watch the driver
work, and can wall the pad into a proper room — which functional cases need,
because `FindAvailableStand` walks `room.ContainedThings` and an open pad is the
whole outdoors.

Assert with `Expect(condition, what)`, and pair every negative with a control.
