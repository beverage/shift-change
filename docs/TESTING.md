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
devtools/run-harness.sh --with=khamenman.outfitstandsplus   # the four, plus one
devtools/run-harness.sh --with-after=khamenman.outfitstandsplus   # loaded after us
```

Builds Release, launches with `-quicktest -shiftchange-harness`, waits for the
game to run every case and quit itself, prints the report, exits non-zero on any
failure. By hand: dev mode → **Shift Change** → **Run lifecycle harness**, then
click a clear 7×7 area.

`--with=<packageId>` adds a mod to the minimal list, ahead of this one, for a
case that tests against that mod and is a known gap without it. It can be given
more than once; the mod's own dependencies are not followed, so name those too.
It is not a compatibility run: the rest of the list stays minimal. Three mods
have cases waiting for them: `khamenman.outfitstandsplus`,
`avilmask.commonsense` and `mehni.pickupandhaul`.
`--with-after=<packageId>` does the same with the mod loaded after this one.
That is the order `loadAfter` in About.xml warns against, and a warning is all
it is: the game loads whatever order the mod list says. So a case that depends
on another mod runs both ways before it is trusted.

**The window does not need focus.** An unfocused instance used to stall on its
loading screen: the log froze around line 49, the process sat near 0% CPU, and
it never recovered. The cause was RimWorld's own `runInBackground` preference,
which is off in a fresh save-data folder. The script now seeds it on, and that
is measured: with focus held on another app for the whole run, 3 of 3 seeded
runs passed with startup in 20 s, and 3 of 3 unseeded runs stalled. A run that
has not reached RimWorld's own startup by 120 s, and whose log has then been
silent for 60 s, is stopped with a message saying which failure it was, rather
than left to the 1200 s ceiling.

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

With `--alongside`, the running game keeps the dll it loaded.
`Mods/ShiftChange` is a symlink to this checkout, so that game loaded
`Assemblies/ShiftChange.dll` from here. Neither build writes that file: both
compile into `dist/build/`, and each result is copied in as
`ShiftChange.dll.new` and renamed over the target. A rename swaps the directory
entry and nothing else, so the running game keeps the file it opened and the
next one to start loads the new one. On the way out the script runs
`check-shipped-dll.py` and lists `Assemblies/`, and warns if that directory
holds anything but the one shipping dll.

Beside a Debug session the run swaps nothing in: the hot-reload watchdog polls
`ShiftChange.dll_orig`, which only a Debug build writes, and both harness builds
are Release. Their sweep does delete that file, though, so reload ends for that
session, exactly as [building Release during a hot session](DEVELOPMENT.md#hot-reload)
does.

## What the cases assert

Sixty-seven cases. This section is a map: the detail lives with each case, in
the comment on its method under `Source/ShiftChange/Harness/`, and the report
prints the cases in the order `DebugTools_LifecycleHarness.Run` registers them.

Most guard a bug that happened, and fail without its fix. Some instead assert a
rule the store description promises players. That kind exists because all
three descriptions once said eating in a room changed nothing, while the code
had always done the opposite on purpose, and nothing caught it until
`MealBreakChangesOut` was written. The table and classifier cases are a third
kind: facts about the def database, most likely to fail on a game update rather
than on an edit.

**Lifecycle** (`HarnessLifecycle`). Engine events fired at a hand-assembled,
checked-out stand, asserting where the ledger lands: a gravship flight with the
borrower aboard and without (`GravshipFlight`, `GravshipFlightLeftBehind`),
teardown (`TeardownReleases`), a reinstall, which must keep the ledger and the
return trip (`ReinstallKeepsTheLedger`) and the stand's owner, mode and flag
(`ReinstallKeepsConfiguration`), and the borrower's death and banishment
(`DeathReaps`, `BanishmentReaps`). `FaultLatchRecovers` drives real throws
through the interception prefix until the fault latch trips, then re-arms it
the way a load does.

**The swap** (`HarnessSwap`). These stage an undressed pawn, and every one but
`DecencyGuardIsOptIn` runs `JobDriver_SwapAtStand` for real, through the pawn's
own tracker. `DriverRoundTrip` checks that the driver builds a correct ledger
and gives everything back, and it is the one case that drives the driver's
capture and restore of forced flags. `NonDisplacingReturns` checks the same for
a stand whose stock displaces nothing. Full change has a pair: the swap, and
the refusal to strip a colonist bare (`FullChangeSwapsEverything`,
`FullChangeRefusesToStripThemBare`). Four cases decide what a stand may take
for decency. Trousers alone leave a man decent and a woman not, so
`LegsOnlyStandLeavesAManDecent` is the control for
`LegsOnlyStandStripsAWoman`; a nudist is exempt (`NudistIsExemptFromDecency`);
and the guard ships off, and turning it on takes effect
(`DecencyGuardIsOptIn`).

**Recreation and sleep** (`HarnessTriggers`). Both classifiers are asserted as
tables against the def database, with no map (`RecreationClassifierHolds`,
`RestClassifierHolds`). A real joy job in the stand's room dresses
(`JoyJobInTheRoomDresses`), and work and recreation clear each other on one
stand (`WorkAndRecreationAreExclusive`). `SleepJobInTheRoomDresses` puts a bed
in the stand's room and covers going to bed there, every refusal, the mid-sleep
re-trigger and the wake-up.

**Bed rest, deposit only and fishing** (`HarnessTriggers` too). A stand ticked
for vanilla's bed-rest work type dresses a patient going to recuperate, never
one who needs a doctor first (`MedicalBedRestDressesAtAGownStand`), and the
medical emergency setting covers doctors and patients but never firefighting
(`MedicalEmergencySettingCoversTheRightWork`). A deposit-only stand parks what
its filter accepts and hands it back (`DepositOnlyParksAndReturns`), and
`StoreUtility`'s own search walks haulers past it while an ordinary stand still
takes deliveries (`DepositOnlyStandIsNoHaulTarget`). `FishingNeverDresses` is a
known gap on every list, kept on purpose (see
[Rules the suite follows](#rules-the-suite-follows)).

**Gates** (`HarnessGates`). `PromisesHold` is the decision table for the
promises: automatic work only, emergencies never delayed, drafted pawns left
alone, work the stand does not serve ignored. The meal-break policy has a case
on a work stand and one on a sleep stand, where a meal already in the pawn's
inventory gets the opposite answer (`MealBreakChangesOut`,
`SleepwearMealBreakChangesOut`). Under threat a colonist may change back but
not in (`DangerGateIsOneDirectional`); a lord duty shuts both arms until it
ends (`DutyGateShutsBothArms`); a job handed to a pawn mid-swap is not deferred
on top of it (`MidSwapJobIsNotDeferredAgain`); and a freed stand catches up a
colonist already working bare in its room (`FreedStandCatchesUp`).

**Allowed areas** (`HarnessGates` too). A stand outside the colonist's allowed
area dresses nobody by any of the three paths that dress, and an allowed shared
stand beats their own stand outside it (`StandOutsideTheAreaDressesNobody`).
The recreation and sleep arms pass it over through the same search
(`StandOutsideTheAreaServesNoRecreationOrSleep`). A colonist already in its
outfit keeps it on, with the retry cooldown set and an inspect line saying why
(`StandOutsideTheAreaKeepsTheOutfitOn`). Every refusal sits beside the same
setup with the stand inside the area.

**Ownership** (`HarnessOwnership`). The owner list is a set:
`OwnerListRestricts` walks a stand from pool to one owner, two and back.
`OwnerFilterOffersTheRightPawns` drives the owner dialog's candidate list,
which must never hide a current owner. Two cases stage a foreign assignable
beside ours, the shape Outfit Stands Plus gives its stands: the copy follows
every change to our list (`ForeignOwnersCopyOurs`), and the one-time reconcile
for older saves keeps the list the player could see
(`ReconcileKeepsTheVisibleList`). Three cover a stand not used for shift
changes: a group stand keeps its owners whichever of four ways it leaves shift
use (`GroupSurvivesLeavingShiftUse`), one pick there replaces a kept group
(`PickReplacesAKeptGroup`), and a kept group reaches nobody through the copy
and survives a move (`KeptGroupSurvivesAMove`).

**The removal flag and the gizmo gates** (`HarnessOwnership` too).
`RemovalFlagHeldOffInService` holds vanilla's removal flag off through every
entry into service, the invariant the contents protection rests on, and pins
the engine fact that the optimizer's gate is that same field. Three cases cover
the gizmo postfixes, which must hand the upstream sequence back untouched when
they have nothing to add (`ChangeBackGateHandsTheChainBack`,
`RemovalToggleGateLeavesForeignStandsAlone`), and still add the change-back
button for a pawn in uniform, even with interception latched off
(`ChangeBackGateStillAppends`).

**Riding along** (`HarnessRideAlong`). Jobs a uniform stays on for, each one a
change-out the return trip used to make for nothing: feeding a patient or a
prisoner (`FeedingRidesAlong`), an errand queued ahead of work the stand serves
(`DetourAheadOfServedWorkKeepsTheUniform`), and vanilla's own opportunistic
haul on the way to a bill, which the engine builds
(`VanillaOpportunisticHaulKeepsTheUniform`). Two run another mod's real code
and are known gaps without it: Common Sense's bill haul
(`CommonSenseBillHaulKeepsTheUniform`) and Pick Up And Haul's follow-up jobs
(`PickUpAndHaulFollowUpsKeepTheUniform`).

**Interop** (`HarnessInterop`). `OutfitStandsPlusButtonMatchesTheirs` covers
code of ours that runs inside another mod: Outfit Stands Plus' stand button,
reading our stand list, must offer what its own walk would, in the same order
(see [DESIGN.md](DESIGN.md#outfit-stands-plus-stand-button)). It is a known
gap until `--with=` loads that mod.

**Buttons and spares.** `HarnessGizmos` reads the stand's buttons the way the
gizmo bar gets them, without drawing: the switch names what the stand serves
(`SwitchNamesWhatTheStandServes`), and stands selected together keep their own
buttons (`StandButtonsStaySeparate`). `HarnessScoring` is a pair that only
means anything as one: a checked-out pawn refuses a spare duplicating the kit
parked in their stand (`ParkedKitRefusesSpares`), and takes one once that kit
is worn through (`WornOutParkedKitStillGoesShopping`).

**Tables and rooms** (`HarnessTables`). Three cases need no map and check that
the static tables still match the def database, where every lookup is
silent-fail and a renamed def empties its row with no error: the room-role
table (`RoomRoleTableResolves`), the job-target and ignored-giver tables
(`JobTablesHold`) and the room-contents markers (`ContentsMarkersHold`). The
last two check Rimatomics' names only when it is loaded, and count as known
gaps without it. A fourth asserts the arithmetic that sizes the work-type
dialog's body (`WorkTypeDialogBodyRectFitsTheBody`). Two need a map and resolve
a work target's room: one with no room of its own
(`SolidTargetResolvesARoom`), and one in an exterior wall, from either side
(`ExteriorWallTargetResolvesTheRoom`).

**Round trips** (`DebugTools_SaveRoundTrip`). Save, load back through the
engine's own synchronous loader, and assert on the written file as well as on
the loaded state: a plain trip, which also sweeps vanilla's removal flag
(`RoundTrip`); a save from before v1.0.2, with the legacy keys
(`LegacyMigration`); and an older save whose stand carries a foreign assignable
with an owner of its own (`ForeignAssignable`). On the four-mod list the
legacy-key case takes the key migration, and beside a foreign assignable it
takes the decline and the reconcile instead; no other case reaches the
decline. These cases replace `Current.Game`, so they run last among the map
cases; the class doc says what else follows from that.

**The harness itself.** `HarnessAccounting`, registered last, asserts that
`Expect` reports and returns, that `Case` counts, and that a known gap is
counted apart from a failure. Without it the runner can grep a green log out of
a suite that checks nothing.

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
  about a mod that was not installed. Two cases reach further when Outfit
  Stands Plus is loaded: the interop case asserts against it, and the
  legacy-key case takes the branch that declines the generic keys to its comp.
  Common Sense and Pick Up And Haul each have one case that runs their code.
- **UI.** No case draws a gizmo or opens a window. The button cases read
  gizmo labels and grouping, and one allowed-area case reads an inspect
  string, all without drawing.
- **Trade.** No case opens a trade session or builds a `TradeDeal`, so the
  withhold-from-trade postfix is verified in play only. A moved
  `PlayerSellableNow` is at least loud, since Harmony reports a target it cannot
  find on startup — but nothing pins the two collectors that reach stand
  contents in the first place, so a route quietly rerouted around the deal would
  take the protection with it and say nothing.
- **Pooling on and off, the optimizer pause, the recolor guard, `SwapPlan`'s
  rollback, the change-back latch.** No cases yet. The retry cooldown is
  asserted on the allowed-area path only.
- **A revert of `StaysInBed` to `pawn.InBed()`** — the specific defect the
  wake-up case was written for. The fixture reaches "on a bed" by assigning
  `Position`, never by running a lay-down driver, so posture stays `NotLaying`
  and `InBed()` is false there for a second reason as well; a reverted guard
  would not fire in the harness either, and the case would still pass. Catching
  it needs a ticked lay-down job with a live driver, which these cases avoid
  because 1.6 pathing is async. The case does assert the two predicates'
  disagreeing answers side by side, so the trap is visible in the report to a
  reader even though it cannot fail for a runner. This is the clearest example
  on the page of a green assertion that is narrower than it looks.

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
reason recorded — it still runs, and reports `FIXED` if it starts passing. Most
gaps are a case whose mod is not on the list. One is a real gap, kept on purpose
on every list: fishing never dresses, because Odyssey hands fishing out with no
giver, and the fix waits for stands that can serve beyond their room. Its
control, the same job carrying its giver, does dress. The mechanism exists
because a suite that always prints one failure is ignored within a week.

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
| Workshop comment length | Steam takes at most 1,000 characters in a Workshop comment, so a longer `media/comment-v*.txt` cannot be posted as written and the file then disagrees with what went out. Three drafts here did. |

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
