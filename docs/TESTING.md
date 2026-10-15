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
It is not a compatibility run: the rest of the list stays minimal. Four mods
have cases waiting for them: `khamenman.outfitstandsplus`,
`avilmask.commonsense`, `mehni.pickupandhaul` and `FrozenSnowFox.ComplexJobs`.
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

A hundred and four cases. This section is a map: the detail lives with each case, in
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
re-trigger and the wake-up. It strips anything tendable the colonist arrived
with first, as the bed-rest case below does: an untagged lay-down by a colonist
who needs a doctor is medical bed rest, which no sleep stand dresses.

**Bed rest, deposit only and fishing** (`HarnessTriggers` too). A stand ticked
for vanilla's bed-rest work type dresses a patient going to recuperate, never
one who needs a doctor first (`MedicalBedRestDressesAtAGownStand`, which first
strips anything tendable the generated colonist arrived with, such as asthma,
and names it), and the
medical emergency setting covers doctors and patients but never firefighting
(`MedicalEmergencySettingCoversTheRightWork`). A deposit-only stand parks what
its filter accepts and hands it back (`DepositOnlyParksAndReturns`), and
`StoreUtility`'s own search walks haulers past it while an ordinary stand still
takes deliveries (`DepositOnlyStandIsNoHaulTarget`). Fishing, which Odyssey
hands out with no giver, dresses as it is handed out, as the same job carrying
its giver does (control), but not with a player order's tag; a radius stand by
the door dresses for fishing out past it, which room mode does not, and not
past its ring. Untagged, as vanilla puts it back in the queue behind a haul, it
still dresses, and on shift that haul keeps the uniform on, where the same haul
with nothing queued changes them back (`FishingDressesAtAStandTickedForIt`).

**The job classifier** (`HarnessJobKinds`). One table, in a fixture because
medical rest depends on the colonist's health and on a bed:
`ClassifierSortsEveryJobShape` gives each job shape the other cases build, and
the kind and work type `Classify` must return for it. Rows cover work from a
giver, VisitSickPawn from its work giver (Work) and from its joy giver
(Recreation), reading and eating, ordinary and ground sleep, medical lay-downs
tagged and untagged for a patient who is and is not urgent with the
medical-emergency setting off and on, a delivery and a site job from every
delivery giver on the list, a meal run from every feeding giver, the jobs that
ride along, a direct order, the emergencies, the incidental walks, and fishing
stamped and unstamped by tag. The cases that drive jobs through the prefix test
what each reader does with the answer, while this one tests the answer, so a
change to the order of the classifier's tests fails here with the row named.

**Gates** (`HarnessGates`). `PromisesHold` is the decision table for the
promises: automatic work only, emergencies never delayed, drafted pawns left
alone, work the stand does not serve ignored. The meal-break policy has a case
on a work stand and one on a sleep stand, where a meal already in the pawn's
inventory gets the opposite answer (`MealBreakChangesOut`,
`SleepwearMealBreakChangesOut`). Under threat a colonist may change back but
not in (`DangerGateIsOneDirectional`); a lord duty shuts both arms until it
ends (`DutyGateShutsBothArms`); a job handed to a pawn mid-swap is not deferred
on top of it (`MidSwapJobIsNotDeferredAgain`); and a freed stand catches up a
colonist already working bare in its room (`FreedStandCatchesUp`). Change back
keeps a colonist out of their stand's room until they leave it, drafting drops
that, and the catch-up honours it (`ChangeBackLatchHoldsUntilTheyLeave`). That
case was written against the room latch as it shipped, before catchments
reworked it, and it still passes on the per-stand latch. Its second stand, out
past the door, serves the smallest radius, since a room stand in the open
serves nothing.

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
button for a pawn in uniform, with interception switched off and again with it
latched off by its faults (`ChangeBackGateStillAppends`).

**Riding along** (`HarnessRideAlong`). Jobs a uniform stays on for, each one a
change-out the return trip used to make for nothing: feeding a patient or a
prisoner at a stand serving that work, where pyjamas still change back first
(`FeedingRidesAlong`), but only for a patient inside, a haul vanilla slips in
ahead of such a meal run keeping it on too, and in pyjamas a meal carried out of
the room changing them back (`MealRunKeepsTheUniformForAPatientInside`), an
errand queued ahead of work the stand serves
(`DetourAheadOfServedWorkKeepsTheUniform`), and vanilla's own opportunistic
haul on the way to a bill, which the engine builds
(`VanillaOpportunisticHaulKeepsTheUniform`). Two run another mod's real code
and are known gaps without it: Common Sense's bill haul
(`CommonSenseBillHaulKeepsTheUniform`) and Pick Up And Haul's follow-up jobs
(`PickUpAndHaulFollowUpsKeepTheUniform`).

**Catchments** (`HarnessCatchments`). A stand serving a radius or an allowed
area instead of its room. The ring is inclusive, exactly the cells it draws
(`RadiusCoversItsRing`, which also holds the slider's top at the 79 the README
gives); a stand leaves room mode only with something ticked and returns to it
on leaving shift use, an automatic stand taken to a radius and back is automatic
again unless its ticks changed meanwhile, and its Serves row greys exactly where
the setter refuses (`CatchmentNeedsSomethingTicked`); and a freed radius stand catches nobody up,
where a room stand records the job it interrupts as the one they dressed for
(`FreedRadiusStandCatchesNobody`). The registry forgets a stand that is no
longer spawned at the next stand search (`RegistryForgetsAStandNoLongerSpawned`).
A room stand beside a door serves its own room only: the doorway's room lists
both the stand inside and one outside, and work in the doorway dresses nobody
(`RoomStandBesideADoorServesItsOwnRoom`). Dressing happens within
the ring, past the stand's own room and never beyond the ring
(`RadiusStandDressesWithinItsRing`), outdoors for recreation, which a room
stand in the open may not serve (`RadiusServesOutdoorRecreation`), and at an
owned radius stand before a nearer shared one
(`OwnedRadiusStandBeatsANearerSharedOne`). A room stand in the open serves no
work either: it covers no cell, the alert lists it, and a colonist already in
its uniform changes back at their next job (`RoomStandInTheOpenServesNothing`).

An allowed area covers its painted cells only while the stand stands in one:
past the walls, and not on an unpainted cell of the stand's own room
(`AreaCoversItsCells`). The dressing decision agrees
(`AreaStandDressesWithinItsArea`). A construction delivery dresses a bare builder
only at an area stand whose area holds the steel and every site the trip
supplies; steel or a site outside it, and room and radius modes over the same
cells, dress nobody, and so does the same fetch filed under Hauling at a stand
ticking construction alone, though the colonist does construction. On shift a
delivery to a site in the area keeps the uniform on wherever the
steel lies, filed under Hauling or not, where one to a site outside it changes
them back (`AreaStandDressesForADeliveryItHolds`). A bedroom stand moved to an area has
its Sleeping ticked for real and still shows its work list, where Growing then
replaces Sleeping with the stand still on the area; once the bed is gone a ticked
Sleeping hides the list (`BedroomStandMovedToAnAreaKeepsItsWorkList`). A deleted area leaves its stand in area mode,
serving nothing under the area's last name, and a new area under that name is
not adopted (`DeletedAreaServesNobody`). A gravship landing does adopt one, the
area the landing carried under the stand's label, with nobody on shift so that
`PostSwapMap`'s early return is in play, and still refuses a namesake that
leaves out the stand's own cell (`GravshipLandingRebindsTheArea`; the same
re-creation without a flight is the control). The alert that vanilla made from its
subclass lists a stand whose area is gone or that stands outside it, naming the
area, and a colonist in that stand's uniform changes back once its area is
deleted (`ServesNothingAlertListsTheStand`). A solid target counts as inside
an area when the area covers either its own cell or the floor beside it, for
dressing, for staying dressed, and for a wall repair as pass-through work
(`SolidTargetIsJudgedAtItsOwnCellFirst`).

The area UI is asserted on what the window builds, since no case draws it. The
Serves menu lists every allowed area but Home in a section of its own, each
with its colour, fades and marks in red one that leaves the stand out, opens
each section after the first with a gap the menu's layout really sees, and
binds whichever area is picked (`ServesMenuOffersTheAllowedAreas`). New allowed
area copies the stand's room, or its own cell in the open, names the area for
the stand's work and numbers a second, and greys at vanilla's cap of ten with
vanilla's reason, the gap then moving to Manage areas
(`NewAllowedAreaCopiesTheRoom`). Paint and Manage areas close the settings
window before vanilla's tool or dialog takes over, with both windows really
opened on the stack (`PaintAndManageAreasCloseTheWindowFirst`). The row, the
inspect pane and the switch name the area, and say when the stand serves
nothing: outside its area, with the area gone, or as a room stand in the open
(`AreaWordsNameTheArea`).

Staying dressed goes by what the stand is for, with a case per trigger
(`RadiusWorkStandKeepsTheUniformInItsRing`,
`RadiusSleepStandKeepsItOnlyForBed`, `RadiusRecreationStandKeepsItForPlay`).
Pass-through work is judged at both ends, and an errand ahead of the stand's
own work rides along (`PassThroughWorkIsJudgedAtBothEnds`). The two lists that
decide it are checked as tables, with no map
(`PassThroughAndIncidentalTablesHold`). Each case that dresses its colonist
then asserts that the job tracker's pause after the swap did not send them
straight back, which is how that pause was found.
`JustDressedForNeverSendsThemBack` dresses through the real `Insert`, moves the
patient away while the colonist changes, and lets the engine's own queue start
the tending, once at a radius stand and once at a room stand; a haul vanilla
starts ahead of the tending, putting it back in the queue, keeps the uniform on,
and so does the tending starting again. The per-stand latch holds its stand and
its trigger only, and lifts at a cell with no covered cell beside it, where one
cell past the ring still holds it (`LatchHoldsItsStandAndTriggerOnly`); at a
sleep rack it lasts the night and lifts on waking, a haul ahead of a queued
lay-down not counting as the waking (`SleepRackLatchLiftsOnWaking`); and at an
area stand it holds on the unpainted cell beside the stand, for work in the
area done from outside it, and through the job tracker's pause after that work
(`AreaLatchHoldsBesideItsStand`). A delivery dresses nobody at a room or radius
stand and keeps the uniform of its own work on to a site inside the ring,
wherever the stack lies, each built with the job def its giver uses, a seed
planting resumed with its seed stack in targetC included; to a site past the
ring it changes them back now. The record of the delivery they were dressed for
lets that job through and not the same Job object handed out again with a new
id. Removing an old floor, a site job from the same giver, dresses and changes
back like any work, and a haul ahead of a queued delivery keeps the uniform on,
where one ahead of a delivery to a site past the ring does not
(`DeliveriesKeepTheirOwnWorkOn`, a known gap for Complex Jobs' two givers
without that mod). Pyjamas and kitchen whites change back before a delivery from
steel out past the door, to a site there or in the room, where steel and site
both in the room are the room's. A construction stand's uniform stays on for a
site in the room, the same fetch filed under Hauling included and one filed
under a work type that is not hauling excluded, and comes off now for a first
drop, a site or a further site out past the door, each delivery built with its
first drop and site apart as the giver builds it
(`DeliveryOutsideTheStandsWorkChangesThemBack`). An urgent patient on
shift goes straight to bed with the medical emergency setting off: in room mode
to a bed past the door, tagged or as vanilla puts the lay-down back untagged,
with a haul vanilla starts ahead of it, and at a radius stand
(`UrgentPatientGoesStraightToBed`). The Serves row, the inspect pane and the
switch's tooltip follow the mode (`CatchmentWordsFollowTheMode`).

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
table (`RoomRoleTableResolves`), the job-target, ignored-giver and ride-along
tables, delivery givers by class, deliveries by job, and every official giver of
a feeding class on the ignored list included (`JobTablesHold`), and the
room-contents markers (`ContentsMarkersHold`). The last two check Rimatomics'
names only when it is loaded, and count as known gaps without it. A fourth asserts the arithmetic that sizes the work-type
dialog's body (`WorkTypeDialogBodyRectFitsTheBody`). Two need a map and resolve
a work target's room: one with no room of its own
(`SolidTargetResolvesARoom`), and one in an exterior wall, from either side
(`ExteriorWallTargetResolvesTheRoom`).

**Round trips** (`DebugTools_SaveRoundTrip`). Save, load back through the
engine's own synchronous loader, and assert on the written file as well as on
the loaded state: a plain trip, which also sweeps vanilla's removal flag and
keeps a radius catchment (`RoundTrip`); a save from before v1.0.2, with the
legacy keys and none of the catchment keys (`LegacyMigration`); an older save whose stand carries a foreign
assignable with an owner of its own (`ForeignAssignable`); and three area
stands, one whose area was renamed before the save, one whose area was deleted,
and one boxed and carried off every map, which keeps its binding
(`AreaRoundTrip`). On the four-mod list the legacy-key case takes the key
migration, and beside a foreign assignable it takes the decline and the
reconcile instead; no other case reaches the decline. These cases replace
`Current.Game`, so they run last among the map cases; the class doc says what
else follows from that.

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
  built or flown, so the area rebind is tested against an area re-created by
  hand on the same map, not one the landing copied.
- **The walk.** Fixtures stage the pawn on the stand's interaction cell, so
  pathing and interruption mid-walk are untested. See the traps below.
- **Mod compatibility.** `--full` proves the suite passes with one large mod
  list loaded — whichever one is active on the machine that ran it. It proves
  nothing about correct interaction with any particular mod, and nothing at all
  about a mod that was not installed. Two cases reach further when Outfit
  Stands Plus is loaded: the interop case asserts against it, and the
  legacy-key case takes the branch that declines the generic keys to its comp.
  Common Sense and Pick Up And Haul each have one case that runs their code.
- **UI.** No case draws a gizmo or a window. One case opens the settings
  window and vanilla's Manage areas on the window stack and asserts which are
  open (`PaintAndManageAreasCloseTheWindowFirst`). The button cases read
  gizmo labels and grouping, one allowed-area case reads an inspect string, and
  the catchment wording case reads the Serves row's label, the inspect line and
  the switch's tooltip, and the alert case its report and explanation, all
  without drawing. The test the Serves row greys on
  is checked against the setter, but the grey itself, the radius slider and
  the ring are verified in play only.
- **Trade.** No case opens a trade session or builds a `TradeDeal`, so the
  withhold-from-trade postfix is verified in play only. A moved
  `PlayerSellableNow` is at least loud, since Harmony reports a target it cannot
  find on startup — but nothing pins the two collectors that reach stand
  contents in the first place, so a route quietly rerouted around the deal would
  take the protection with it and say nothing.
- **Pooling on and off, the optimizer pause, the recolor guard, `SwapPlan`'s
  rollback.** No cases yet. The retry cooldown is asserted on the allowed-area
  path only.
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
gaps are a case whose mod is not on the list. Until v1.5.0 one was a real gap,
kept on purpose on every list: fishing never dressed, because Odyssey hands
fishing out with no giver, and the fix waited for stands that can serve beyond
their room. The mechanism exists because a suite that always prints one failure
is ignored within a week.

## Three engine traps

None is visible from the API surface. All are recorded at their call sites.

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

**The clock stands still.** The harness runs inside one frame, so `TicksGame`
never moves while the pump ticks a pawn, and the pawn's own interval gates read
it (`Pawn.IsHashIntervalTick`). For some pawns the needs then tick on every
pumped tick instead of once per 150, rest drains in a few hundred ticks, and
vanilla's involuntary sleep cut a swap short at random: two of three 1.7 runs on
2026-10-13, in a different case each time. The pump holds the pawn's rest
full.

## Static checks

`devtools/check-invariants.py` runs locally and in CI. No game, no build, no
network.

| Check | What it prevents |
|---|---|
| Hot-reload invariants | A `private` member or an auto-property compiles clean and throws `FieldAccessException` in a hot-swapped session. |
| Translation keys, both directions | A key used but not defined renders as the raw key on screen, with no log line. A key passed to `CompShiftStand.ByCatchment` counts as every catchment variant, since a missing variant shows only in that mode. |
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
