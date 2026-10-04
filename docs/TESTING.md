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

Sixty-seven cases.

**Regression cases** guard a bug that happened. A stand whose stock shares no
apparel layer with what the pawn wears once donated its uniform permanently and
emptied itself forever; a gravship flight released every ledger aboard and
inverted uniform and civvies; banishment never reached the reaper because
`PawnBanishUtility.Banish` never reaches `UnclaimAll`; a single exception
disabled interception for the whole process; the freed-stand announcement
fired before the tracker released the pawn's reservation, so the mid-job
catch-up could never fire at all; and a stand that reached service with
vanilla's removal flag already on stayed open to every colonist's optimizer,
because refusing the toggle's transition never emptied the state behind it.
Each has a case that fails without its fix.

That last one carries a second job. Its closing assertions read
`ApparelSourceEnabled` and `HaulSourceEnabled` off the stand directly, pinning
the engine fact that both are the same field. The decision not to patch the
optimizer's gate rests on that being true, so if Ludeon ever decouples them the
case fails and names the decision that needs revisiting, instead of the
protection quietly evaporating.

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

**Sleep cases** cover the third arm, and lean heavily on negatives because
ordinary sleep and MEDICAL bed rest share one driver class and one JobDef. One
asserts the classifier and the room table against the def database — that
`Wait_Asleep` still runs the same driver as `LayDown`, which is why the
classifier demands a bed; that `PatientBedRest` is still a visible WorkTypeDef
whose giver still reports it, which is what keeps the two controls from merging;
and that Bedroom dresses for sleep while a rec room does not, so the three role
tables stay disjoint. The other drives the arm for real: a bed is spawned into
the pad, and the case asserts the stand picks up sleep from the room's role
rather than declaring it — because a single unowned humanlike bed scores the
room as a Bedroom at 100000, so calling `ToggleRest()` here would turn the
trigger OFF and fall through to excluded. It then walks every refusal (ground
sleep, both medical routes, player-forced), the mid-sleep re-trigger, and the
WAKE-UP end to end: an on-shift pawn on a bed must have the change-back
inserted BEFORE a job that leaves the room, and must not for one that keeps
them in it.

A third covers **deposit only** — that a freshly built stand's filter accepts
ordinary apparel out of the box (the fact the whole safety story rests on), that
a narrowed filter parks exactly what it names and issues nothing, that the trip
claims the stand so a return exists, that the parked gear comes back, and that a
deposit which would leave a colonist in nothing but a shield belt is refused.

Its neighbour covers **hauling into** such a stand, and drives `StoreUtility`'s
own search rather than reading our flag back, because the claim is "a hauler
does not come" and not "the flag is set". It stages a SECOND, ordinary stand
further from the garment, so the assertion can be that the hauler switches to it
rather than merely that it stops choosing the first — "nowhere to put it" is a
result a broken search produces too, and it has to fail. Three states in the one
run: an empty ordinary stand outbids a Normal stockpile and is chosen, the same
stand set to deposit only is walked past while still ACCEPTING the garment (the
narrowing is the destination flag and nothing else, so a deposit and a
right-click delivery are untouched), and a stand already holding a conflicting
garment refuses it by vanilla's own `HasRoomForApparelOfDef`. That last is the
state that made the bug so hard to see in play: three identically configured
stands, and only the deposit-only one afflicted, because only its resting state
is empty.

**Ownership cases** guard the owner list, which went from one pawn to a set.
One walks a stand through pool, one owner, two owners and back, asserting who
may claim it at each step; its load-bearing assertion is the SECOND owner,
because a single-owner reader passes everything before that and fails from
there. The other drives the owner dialog's candidate list through all three
filter states without opening a window, and asserts the one property a filter
must have: an assigned pawn leaves the candidate list but stays visible as an
owner even under a filter that excludes them. A filter that hid the owner you
wanted to remove would be a trap.

Two more stage a second, foreign assignable beside ours, the shape Outfit Stands
Plus gives its stands. One asserts the copy follows every way our list changes:
one owner is copied, a second that does not fit the foreign comp's single slot
empties the copy rather than truncating it, and the reaper clears it. The same
case checks that the foreign Set owner stays hidden in both modes. The other
drives the one-time reconcile through each arm of its rule, and ends on the arm
that makes it one-time: once reconciled, a copy that drifted is rewritten, never
adopted.

Three more cover a stand not used for shift changes. The first takes a group stand
out of shift use by all four ways in (the row itself, and unticking the last work
type, Recreation or Sleeping, which land in the same state) and asserts the group
is still listed after each and whole when the stand goes back. The second drives
the owner dialog's own Assign: in shift use it adds, which is the control, and out
of it one pick replaces a kept group and the copy then names that colonist. The
third minifies and relands a stand holding a kept group, and asserts the group
comes back whole with the copy still empty. That is the trap it was written for:
vanilla restores parked owners through `TryAssignPawn` one at a time, so an
override that replaced there returned the group as its last member. The last two
stage the foreign assignable as well.

**An interop case** covers code of ours that runs inside another mod's: the
Outfit Stands Plus stand button (see
[DESIGN.md](DESIGN.md#outfit-stands-plus-stand-button)). It needs that mod
loaded, so on the default list it is a known gap and `--with=` runs it.

It checks the patch first: the walk is in their original IL once, and what
runs calls the stand list in its place. Then it builds a world in which either
half going wrong would show. The fixture's stand belongs to a second colonist.
The owner gets a mechanized stand of theirs and then a second vanilla one,
spawned in that order, plus a third stand that names the owner but belongs to
no faction. Owners go straight into their lists through the base class, so the
case asserts the lookup, not how our copy fills them.

Their method runs with the switch off and then on, and both answers must match
the world and each other, in order. That is asserted twice: as spawned, and
after the mechanized stand changes hands and comes back, which moves it to the
end of the colony building list and so tests the list's upkeep. The case also
checks that the owner's two buttons really merge into one, which is why order
is asserted at all. And it asks the list directly whether the switch hands back
the walk when off. Together with the IL check, that is what shows the off arm
was their walk; without it, a list answering both arms would compare equal to
itself.

**Ride-along cases** cover the jobs a uniform stays on for, each one a
change-out the return trip used to make for nothing. Feeding drives every
vanilla feeding giver with the meal stored in the stand's room, where nobody may
be dressed for it, and in storage elsewhere, where nobody may be changed out for
it, each beside the same job under a giver that is not on the list, which still
dresses and still changes them back. The errand case puts a giver-less haul in
front of the stand's own work in the queue and asserts the uniform stays on;
its controls are the narrowness: nothing queued, work the stand does not
serve, its work in another room, another errand at the head and a meal break
all still change them back, and the errand never dresses anyone. Vanilla's own
opportunistic haul makes the same shape, and its case lets the engine make it:
the bill is started through the pawn's tracker, and vanilla's `StartJob` queues
it and starts the haul in its place, back through our prefix. Vanilla's detour
limits leave the layout in the pad's room about 0.4 of a cell to spare, so the
case asks vanilla's search directly first, and a layout that stops qualifying
fails there with the positions in the report.

Two more run another mod's real code and are known gaps without it. In one,
Common Sense's own prefix builds its bill haul through the pawn's tracker and
ours then judges it, so the case asserts the shape Common Sense produced before
asserting what we did with it. In the other, Pick Up And Haul's three follow-up
jobs are started from the queue the way its driver hands them over, beside a
haul from its own work giver, which still changes them back.

**Allowed-area cases** cover a stand outside the colonist's allowed area. It
dresses nobody, the catch-up interrupts nobody for it, and a shared stand
inside the area beats the colonist's own stand outside it. The recreation and
sleep arms pass it over as well, through the same search. A colonist already in
its outfit keeps it on, the retry cooldown is set and an inspect line says why,
and once the stand is back inside the area the next job out of the room changes
them back as usual. Every refusal sits beside the same setup with the area
lifted.

**Button cases** read the stand's buttons the way the gizmo bar gets them,
without drawing anything. The switch names what the stand serves, recreation
and sleep included. Two stands selected together keep separate switches and
separate Set owner buttons, against two plain commands with the same face,
which do merge.

**Round-trip cases** save the game, load it back through the engine's own
synchronous loader, and assert on what came out. There are three: a plain trip
that carries the owner, the ledger and the forced flags, and that also stages
the removal flag ON before saving to prove the load sweeps it back off; a
legacy-key save, which must get its owner back and re-save it under the prefixed
key; and a stand from an older save carrying a foreign `CompAssignableToPawn`
with an owner of its own, which must load without a contest and then be adopted
into ours.

The legacy-key case takes whichever route the loaded stand allows. With no
foreign assignable on it, the owner arrives by the key migration. With one (and
Outfit Stands Plus puts one on every vanilla stand) the generic keys belong to
that comp, so the migration declines them: the case asserts the decline left no
trace, and that the owner came back through the one-time reconcile instead. The
four-mod list therefore covers the migration, and the decline needs a list that
puts a foreign assignable on the stand, such as one carrying Outfit Stands Plus.
No other case reaches the decline, because the older-save case writes our
prefixed keys, and a present key turns the migration off before the contest is
consulted. The rewrite that produces the legacy file also strips the reconciled
marker: it arrived in v1.4.7, the generic keys were retired in v1.0.2, and no
save carries both. Left in, it made a file no version ever wrote, and beside a
foreign assignable that file lost the owner from both lists.

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
