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
| A test that is slow is not run | One command, no hands, about half a minute on the four-mod list. Anything requiring a human to click through menus stops happening within a week. |
| A test that is flaky is worse than none | Every negative assertion carries a positive control; every timeout reports state; a test checks that every case runs once, in order. Three assertions in draft were satisfiable by the thing they were meant to exclude. |
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

Builds the shipping Release dll, then the tests against it, and launches
RimWorld 1.7 with `--pid MrBeverage.ShiftChange --test -e`: SmashPhil's DevTools
test framework runs every case as a test of its own and quits the game, and the
script prints DevTools' `Test.log` and exits non-zero on any failure.
`RIMWORLD_APP` names the app when it is not the Steam install's. The instance
has no window (`-NoWindow`) and a `HOME` of its own under `dist/testhome`, which
keeps its log and caches off the player's.

The tests are an assembly of their own, `Source/ShiftChange.Tests`, built into
`Tests/Assemblies/`, and the game loads `Tests/` only while DevTools is active
(`LoadFolders.xml`). So they run against the dll that ships, and they ship
too: a player who installs DevTools can run them against their own mod list,
and one who has not loads none of them. The test dll is committed, like the
mod's: after a change under `Source/`, rebuild and commit both (a run of
`run-harness.sh` leaves both in place), or CI's `check-shipped-tests.py` fails
the push, since the dll was built from other sources.
DevTools runs on 1.7 only, so the tests do too: they target .NET 10 and compile
against 1.7, while the mod's own dll stays net472.

It needs a DevTools build in `Mods/DevTools`, from DevTools' `develop` with
SmashPhil/DevTools#17 (until it merges, beverage/DevTools `8966779`), where a
save of DevTools' own test map loads, the warnings the game logs by itself are
allowed, and an allowance can require a frame on the line's stack
(`WarningsAllowedFrom`, `ErrorsAllowedFrom`, repeatable). The script refuses a
build without them. The tests compile against that dll, and DevTools joins the
mod list just ahead of this mod. The verdict is the game's exit code
read with the result line DevTools writes last in `Test.log` (0 passed, 1 a test
failed, 2 nothing ran). A game stopped any other way can still exit 0, so an
exit without that line is no verdict, and the script exits 3. An exception that
escapes a coroutine is written to the game's stderr (`dist/testdata/stderr.log`)
and never to `Player.log`, and it ends the coroutine. When that is DevTools'
test runner, nothing is left to quit the game, so the script stops it as soon as
stderr shows it and prints the exception.

Each case is a DevTools test of its own, written as a story, in a fixture under
`Source/ShiftChange.Tests/` named for what a player sees, which holds its tests'
bodies too ([Adding a case](#adding-a-case)). A test's `[TestDescription]` is
the sentence its story opens with in `Test.log`, and its tooltip in DevTools'
Test Explorer. DevTools runs the fixtures by `ExecutionPriority`, each at its
place in `CaseGroup`, except `SavingAndLoading`, which runs last, and a
fixture's tests in declaration order. It sorts a fixture's tests by priority
with an unstable sort, which keeps equal tests in declaration order only up to
16, so no fixture holds more than 16; one that would is split, which takes a new
`CaseGroup` value and a fixture for it. One more DevTools test,
`HarnessTestOrder`, reads the tests DevTools registered and fails unless every
fixture runs exactly the tests it declares, in declaration order, every fixture
that plays the game runs at a priority of its own and `SavingAndLoading` at the
lowest, and every test has a sentence of its own. Another, `HarnessLoadNote`,
checks how a save/load test's load notes an abort: which log entries it counts
as its own, on queues it builds itself, and that the note applies only to the
game that load made. A test runs on a pad only if it needs one, and the pad is
found once per map and named in Test.log, so a test that spoils it fails the
tests after it rather than moving them elsewhere. Most tests run within one
frame, but DevTools' game is unpaused and its clock runs between tests. Pausing
it would change how the job tracker counts a pawn's jobs per tick, which it does
only while unpaused or within one frame.

Every test that ticks runs on the game's own clock. `GameClock` steps
`TickManager.DoSingleTick` itself, so every map, pawn and comp ticks as in
play, and a colonist can walk ("Four engine traps"). It pauses the game's own
speed while it steps, turns the storyteller and random mental breaks off, and
puts them back after. The tests leave autosave alone: holding it is DevTools'
job, for every mod's tests (SmashPhil/DevTools#18 skips the game's autosave tick
while tests run), since an autosave of its world would go into the player's
Saves folder and, once the game's Autosave names are all taken, replace the
player's oldest. Until a DevTools with that change is in use, nothing holds it:
at the default interval a full pass writes no autosave, but at a player's
shortest setting it writes all five. The tests in
`ChangingAtAStand` and `KeepingThemDecent` let a frame pass every 250 ticks
(`StoryFixture.Swap`); every other test that ticks steps the clock within its
frame, through `SwapNow` (`HarnessFixtures.RunSwap`) or a bill's own steps
(`GameClock.Step`). Three kinds of test needed what they arranged held in place
first ("Four engine traps"). A change at the stand takes its garments' equip
delays to the tick, 180 to 570 in these tests, the same in every run; with the
walk, 429 to 436 so far.

The tests are written for a player to read (`Story`): each check is a line in
the game's words, and the first that fails fails the test with every line so far
and its own marked `>>  FAIL`, which DevTools shows in the Test Explorer's
summary and under the test's row in Test.log. A check does not stop the test, so
the story in Test.log shows every check. A test that stages a scene opens with a
check saying what was set up. A test also fails when it throws, or when its
scene cannot be set up. A test with a mod of its own to test against is a known
gap without it (`story.KnownGap`, a line marked `GAP`), which DevTools reports
as a skipped test unless a check failed too.

DevTools fails a test on any warning it does not allow, apart from the few the
game logs by itself, which DevTools allows everywhere. The repeated-errors test
(`RepeatedErrorsStopShiftChangeUntilTheNextGame`) allows, by text alone, the
errors its own injected faults log. Every other
allowance also needs a frame on the line's stack, so the same text from anywhere
else still fails. Every fixture derives from `HarnessFixture`, which allows
lines other mods log by themselves on a full list, in whichever test is running,
from those mods' own code: Melee Animation's late-load reports of patch
conflicts and of weapons without its tweak data, Map Preview's note on another
mod's map component drawing random numbers, the game's warning when VE
Framework's memes give a colonist a test generates a trait it already has, and
two from Immersive Filter: the engine's warnings for the unimplemented Unity
calls it makes setting up its screen filter, and the error the engine logs each
frame its update throws between games (its class names the line, and the
engine's call of the update is on the stack). `SavingAndLoading` allows the
warnings and errors for stale pawn and corpse references that Vanilla Food
Variety Expanded logs on load from its save code (the mod keeps an earlier
game's colonists). It also allows Vehicle Framework's FinalizeLoading abort with
this mod's own load call on the stack, when the message names that mod's
map-component cache; the test records the abort as a known gap.
`SavingAndLoading` runs last, outside `CaseGroup`: it asks DevTools for a world
of its own, and DevTools then generates one again for whichever fixture follows.

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

The cases whose givers Complex Jobs moves tick each giver's own work type, not
its vanilla name, so they test the same thing with that mod loaded: the bill
products and the feeding cases. The hospital catchment ticks whatever its room
reads. Complex Jobs files stonecutting under Stone Cut, feeding patients under
Nurse and surgery under Surgeon, and prisoner feeding under Nurse (or under its
Prison Care, an option that needs XML Extensions). Shift Change's compatibility
rows arm a hospital and a barn for Nurse and Surgeon beside Doctor. A case that
ticks a vanilla name still breaks under a mod that moves its giver.

**The run needs no focus.** It has no window. A windowed instance once stalled
on its loading screen when unfocused: the log froze around line 49, the process
sat near 0% CPU, and it never recovered. The cause was RimWorld's own
`runInBackground` preference, which is off in a fresh save-data folder. The
script seeds it on, which was measured: with focus held on another app for the
whole run, 3 of 3 seeded runs passed with startup in 20 s, and 3 of 3 unseeded
runs stalled. A run that has not reached RimWorld's own startup by 120 s, and
whose log has then been silent for 60 s, is stopped with a message saying which
failure it was, rather than left to the 1200 s ceiling.

**The minimal list is for iterating; `--full` is what a release is signed off
on.** `--full` copies whatever mod list is active on the machine running it, so
it is only ever as good as that list — it is not a compatibility matrix, and it
proves nothing about a mod you do not have installed. What it does prove is that
the suite still passes with a large list loaded, which the four-mod list by
construction cannot: that is precisely the environment in which a conflict
cannot appear. It is also what surfaced the fixture flakiness described below.

## Isolation

`-savedatafolder` moves the test instance's `ModsConfig.xml`, `Saves/` and prefs
under `dist/testdata` (`GenFilePaths.cs:93-110, :179`). On 1.6 Unity's
`-logfile` moves `Player.log` there too; 1.7 has no `-logfile`, so there the
`HOME` under `dist/testhome` moves `Player.log`, DevTools' `Test.log` and the
caches. Nothing under the live installation is read or written,
and `--full` *copies* the active mod list in rather than swapping it.

An earlier version swapped the live `ModsConfig.xml` back and forth. It worked,
but it edited a player's installation in order to run a test, and the day
something went wrong mid-run it would have been their mod list. The swapper
still exists for interactive sessions; the harness no longer uses it.

The runner launches the game binary directly rather than through `open`, so it
has a real pid, and only ever waits on — or signals — that one. Another instance
already running is somebody's colony with unsaved progress in it: the default is
to stop, and `--alongside` is the deliberate opt-in.

With `--alongside`, the running game keeps the dlls it loaded.
`Mods/ShiftChange` is a symlink to this checkout, so that game loaded
`Assemblies/ShiftChange.dll` from here, and with DevTools active also
`Tests/Assemblies/ShiftChange.Tests.dll`. Neither build writes those files:
both compile into `dist/build/`, and each result is copied in as `<name>.new`
and renamed over the target. A rename swaps the directory entry and nothing
else, so the running game keeps the file it opened and the next one to start
loads the new one. Before the game starts, the script runs
`check-shipped-dll.py` on the Release build and stops if `Assemblies/` holds
anything but the one shipping dll. Both dlls are what a game would load from
this checkout anyway, so nothing is put back when the run ends.

Beside a Debug session the hot-reload watchdog takes nothing from the run: it
polls `ShiftChange.dll_orig`, which only a Debug build writes, and the run's
builds are Release. The sweep deletes that file, though, so reload ends for
that session, exactly as [building Release during a hot session](DEVELOPMENT.md#hot-reload)
does.

## What the cases assert

About a hundred and fifty cases. This section is a map: the detail lives with each case, in
the comment on its method under `Source/ShiftChange.Tests/`, and Test.log
prints the cases in the order they run: the fixtures in `CaseGroup`'s order,
`SavingAndLoading` last, and each fixture's tests in the order its file declares
them. Every case is a story, in a fixture named for what a player sees
(`WhoUsesAStand`, `ServingAnArea`, ...). The `Harness*` and `DebugTools_*` files
hold what the stories share: scenes, jobs and the pad.

Most guard a bug that happened, and fail without its fix. Some instead assert a
rule the store description promises players. That kind exists because all
three descriptions once said eating in a room changed nothing, while the code
had always done the opposite on purpose, and nothing caught it until
`AMealBreakChangesThemOutOfUniform` was written. The table and classifier cases are a third
kind: facts about the def database, most likely to fail on a game update rather
than on an edit.

**Lifecycle** (`LosingAStandOrAColonist` and `FlyingTheGravship`). Engine events fired at a hand-assembled,
checked-out stand, asserting where the ledger lands: a gravship flight with the
borrower aboard, after which the return leg must end the shift normally
(`StandAndColonistFlyTogether`), and without them (`LeftBehindByTheGravship`). Three more
flights leave the borrower and the stand on different maps: the stand flies
alone and comes back (`StandFliesWithoutItsColonist`), the borrower flies alone
and comes back (`ColonistFliesWithoutTheirStand`), and the borrower flies while
the stand is lost with the map it stayed on, standing or boxed
(`StandLeftOnAnAbandonedMap`, `UninstalledStandLostWithItsMap`). `EveryStandHearsItsMapGo`
checks that every stand def asks for the map-removed hook that last pair relies
on. Then teardown (`DeconstructedStandLetsTheColonistGo`), a reinstall, which must keep the ledger
and the return trip (`ReinstallingMidShiftKeepsTheirClothes`) and the stand's owner, mode and
flag (`ReinstalledStandKeepsItsSettings`), but not an owner who died while it was boxed
(`DeadOwnerDoesNotGetTheStandBack`), nor one kidnapped while it was boxed, whose
shift there ends too (`KidnappedOwnerDoesNotGetTheStandBack`), and the borrower's
death and banishment
(`DeathEndsTheShift`, `BanishmentEndsTheShift`). `RepeatedErrorsStopShiftChangeUntilTheNextGame` drives real throws
through the interception prefix until the fault latch trips, then re-arms it
the way a load does.

**Vacuum** (`KeepingTheirVacsuitOn` and `HeadingIntoVacuum`). The second map the flights use is Odyssey's
`SpacePocket`, an `Orbit` map, so the game treats it as space. `ARoomInSpaceHoldsAir`
checks the ground every vacuum case stands on: a walled, roofed room there holds
air, while the open floor past its door, and the doorway itself, read as vacuum.
A region walk that may only step into air stays in the room. Its control is a
walk from the same start that may step anywhere, which reaches the floor
outside.

The rest stage a ship there (`StageShip`): the room with a stand holding a
duster, a crafter in a vacsuit and its helmet, a crafting spot, and a door onto
three cells of open floor leading to a second room. A bill fetching from the open
floor leaves the crafter in their own gear, and the same bill fetching from the
room dresses them (`SteelOutInVacuumKeepsTheVacsuitOn`). So does a bill fetching
from the second room, safe but only reached through vacuum
(`ARoomOnlyVacuumLeadsToKeepsItOnToo`). On a ship whose floor is a walled, roofed
corridor holding air, that bill dresses them, which is the promise that a
connected ship changes nothing (`ACorridorWithAirChangesNothing`). Once dressed, a
crafter changes back before a bill through vacuum and keeps the duster on for one
inside (`ChangesBackBeforeABillThroughVacuum`). A crafter with no protection to lose
is dressed as before (`WithoutAVacsuitTheyChangeAsUsual`). An order through vacuum
runs as given, and its message goes up on screen once: a second order within the
hour says nothing more, and a drafted move is warned about too
(`AnOrderThroughVacuumOnlyWarns`). An emergency through vacuum sends a colonist in
uniform back to change, and the same emergency inside does not
(`AFireOutInVacuumWaitsForAChange`). On the harness's own map, the crafter in
a vacsuit is dressed for a bill fetching from outside the room, as before
(`OnAPlanetNothingChanges`).

The rest came out of a review of the gate. A stand holding a vacsuit dresses an
unsuited crafter for a bill through vacuum, and the gate then leaves them be in
it (`AVacsuitStandDressesThemForVacuum`). The stand search passes over the nearer duster
stand for a vacsuit stand across the room, where without the job the nearer one
wins (`TheVacsuitStandWinsOverANearerOne`). An area stand painted over the open
floor does not dress for a delivery from out there
(`AnAreaOverOpenFloorLeavesTheVacsuitOn`), and a freed stand does not catch up
a crafter already on a job fetching through vacuum
(`AStandComingFreeLeavesThemBe`). On the connected ship, an errand ahead of
the stand's work rides along until the corridor is opened to vacuum
(`AHaulAheadOfABillStillChangesBackForVacuum`), and a forbidden door is no way
through for the walk (`AForbiddenDoorIsNoWayThrough`). A colonist cut off from
their stand by vacuum is not sent across it to change, and is warned instead
(`CutOffByVacuumTheyAreNotSentBack`). A cooldown holds a meal break's
change back but not the one before vacuum (`WaitingToRetryStillChangesBackForVacuum`).
An urgent patient goes straight to a bed across vacuum
(`AnUrgentPatientGoesStraightToBedAcrossVacuum`). And a colonist flown to a vacuum
map without their stand is warned before vacuum, by job or by order
(`WithTheirClothesOnAnotherMapTheyAreWarned`).

**The swap** (`ChangingAtAStand`, on the game's clock). These stage an
undressed pawn and run `JobDriver_SwapAtStand` for real, through the pawn's own
tracker. `IntoUniformAndBack` checks that the driver builds a correct ledger
and gives everything back, and it is the one test that drives the driver's
capture and restore of forced flags. `UniformOverOwnClothesGoesBack` checks the
same for a stand whose stock displaces nothing, `WalksToTheStand` with the walk,
and `WholeOutfitChangesAndComesBack` with Change the whole outfit ticked.

**Keep colonists decent** (`KeepingThemDecent`, on the game's clock). With the
whole outfit ticked, a stand holding only a belt keeps the shirt and trousers
on and takes only the parka (`WholeOutfitKeepsThemDressed`). Trousers alone
leave a man decent and a woman not, so `LegsOnlyStandLeavesAManDressed` is the
control for `LegsOnlyStandKeepsAWomansShirtOn`; a nudist is exempt
(`NudistIsLeftUndressed`); and the setting ships off, read from fresh settings
rather than the running game's, and turning it on takes effect
(`GuardIsOffUntilTurnedOn`).

**Recreation and sleep** (`ReadingJobsAndRooms` and `WhatSendsThemToChange`). Both classifiers are asserted as
tables against the def database, with no map (`WhatCountsAsRecreation`,
`WhatCountsAsSleep`). A real joy job in the stand's room dresses
(`ChessInTheRoomSendsThemToChange`), and work and recreation clear each other on one
stand (`AStandServesWorkOrRecreationNotBoth`). `GoingToBedInTheRoomSendsThemToChange` puts a bed
in the stand's room and covers going to bed there, every refusal, the mid-sleep
re-trigger and the wake-up. Its precondition is a colonist who needs no doctor,
which the fixture arranges ("Four engine traps"): an untagged lay-down by a
colonist who needs a doctor is medical bed rest, which no sleep stand dresses.

**Bed rest, deposit only and fishing** (`WhatSendsThemToChange`). A stand ticked
for vanilla's bed-rest work type dresses a patient going to recuperate, never
one who needs a doctor first (`BedRestSendsAPatientToChange`), and the
medical emergency setting covers doctors and patients but never firefighting
(`MedicalEmergenciesChangeFirstButFiresDoNot`). A deposit-only stand parks what
its filter accepts and hands it back (`DepositOnlyTakesTheParkaAndGivesItBack`), and
`StoreUtility`'s own search walks haulers past it while an ordinary stand still
takes deliveries (`HaulersLeaveADepositOnlyStandAlone`). Fishing, which Odyssey
hands out with no giver, dresses as it is handed out, as the same job carrying
its giver does (control), but not with a player order's tag; a radius stand by
the door dresses for fishing out past it, which room mode does not, and not
past its ring. Untagged, as vanilla puts it back in the queue behind a haul, it
still dresses, and on shift that haul keeps the uniform on, where the same haul
with nothing queued changes them back (`FishingSendsThemToChange`).

**The job classifier** (`WhatSendsThemToChange`). One table, staged on the pad
because medical rest depends on the colonist's health and on a bed:
`WhatEachJobCountsAs` gives each job shape the other cases build, and
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

**Gates** (`MealsDangerAndDuties` and `AfterChangingBack`, and `WhatSendsThemToChange` for
the promises and a job mid-change). `OrdersEmergenciesAndDraftsAreNeverHeldUp` is the decision table for the
promises: automatic work only, emergencies never delayed, drafted pawns left
alone, work the stand does not serve ignored. The meal-break policy has a case
on a work stand and one on a sleep stand, where a meal already in the pawn's
inventory gets the opposite answer (`AMealBreakChangesThemOutOfUniform`,
`BreakfastChangesThemOutOfSleepwear`). Under threat a colonist may change back but
not in (`DangerKeepsThemFromChangingInButNotBack`); a lord duty shuts both arms until it
ends (`ADutyLeavesThemAloneUntilItEnds`); a job handed to a pawn mid-swap is not deferred
on top of it (`AJobMidChangeIsNotPutOffAgain`); and a freed stand catches up a
colonist already working bare in its room (`ChangingBackFreesTheStandForTheNext`). Change back
keeps a colonist out of their stand's room until they leave it, drafting drops
that, and the catch-up honours it (`ChangeBackHoldsTheRoomUntilTheyLeave`). That
case was written against the room latch as it shipped, before catchments
reworked it, and it still passes on the per-stand latch. Its second stand, out
past the door, serves the smallest radius, since a room stand in the open
serves nothing.

**Allowed areas** (`ServingAnArea`). A stand outside the colonist's allowed
area dresses nobody by any of the three paths that dress, and an allowed shared
stand beats their own stand outside it (`StandOutsideTheirAreaDressesNobody`).
The recreation and sleep arms pass it over through the same search
(`StandOutsideTheirAreaServesNoRecreationOrSleep`). A colonist already in its
outfit keeps it on, with the retry cooldown set and an inspect line saying why
(`OutfitStaysOnWhenTheirAreaLeavesTheStandOut`). Every refusal sits beside the same
setup with the stand inside the area.

**Ownership** (`WhoUsesAStand`). The owner list is a set:
`OnlyItsOwnersMayUseAStand` walks a stand from pool to one owner, two and back.
`OwnerWindowFiltersByGender` drives the owner dialog's candidate list,
which must never hide a current owner. Two cases stage a foreign assignable
beside ours, the shape Outfit Stands Plus gives its stands: the copy follows
every change to our list (`AnotherModsOwnerListFollowsOurs`), and the one-time reconcile
for older saves keeps the list the player could see
(`OlderSavesKeepTheOwnersYouSaw`). Three cover a stand not used for shift
changes: a group stand keeps its owners whichever of four ways it leaves shift
use (`AGroupKeepsItsStandOutOfService`), one pick there replaces a kept group
(`OnePickReplacesAKeptGroup`), and a kept group reaches nobody through the copy
and survives a move (`AKeptGroupSurvivesAMove`).

**The removal flag and the gizmo gates** (`WhoUsesAStand` too).
`AllowRemovingStaysOffInService` holds vanilla's removal flag off through every
entry into service, the invariant the contents protection rests on, and pins
the engine fact that the optimizer's gate is that same field. Three cases cover
the gizmo postfixes, which must hand the upstream sequence back untouched when
they have nothing to add (`NoChangeBackOffShift`,
`AllowRemovingButtonOnlyInService`), and still add the change-back
button for a pawn in uniform, with interception switched off and again with it
latched off by its faults (`ChangeBackOfferedOnShift`).

**Riding along** (`RidingAlong`). Jobs a uniform stays on for, each one a
change-out the return trip used to make for nothing: feeding a patient or a
prisoner at a stand serving that work, where pyjamas still change back first
(`FeedingAPatientKeepsTheUniformOn`), but only for a patient inside, a haul vanilla slips in
ahead of such a meal run keeping it on too, and in pyjamas a meal carried out of
the room changing them back (`MealForAPatientInTheRoomKeepsTheUniformOn`), an
errand queued ahead of work the stand serves
(`HaulAheadOfACookingBillKeepsTheUniformOn`), and vanilla's own opportunistic
haul on the way to a bill, which the engine builds
(`HaulOnTheWayToABillKeepsTheUniformOn`). Two run another mod's real code
and are known gaps without it: Common Sense's bill haul
(`CommonSenseIngredientHaulKeepsTheUniformOn`) and Pick Up And Haul's follow-up jobs
(`PickUpAndHaulNextStepsKeepTheUniformOn`).

**Catchments** (`ServingARoomOrARing`, `ServingAnArea`, `StayingInUniform`,
`StoringWhatTheyMade` and `AfterChangingBack`). A stand serving a radius or an allowed
area instead of its room. The ring is inclusive, exactly the cells it draws
(`TheRingShowsWhatARadiusCovers`, which also holds the slider's top at the largest whole
radius below the pattern's edge, 79 on the game's own pattern as the README
gives); a stand leaves room mode only with something ticked and returns to it on
leaving shift use, an automatic stand taken to a radius and back is automatic
again unless its ticks changed meanwhile, and its Serves row greys exactly where
the setter refuses (`ARadiusNeedsSomethingTicked`). With the room-contents
scan on, which caches an automatic stand's reading, the stand still reads its
room turning into a hospital on the same tick
(`AnAutomaticStandSeesItsRoomBecomeAHospital`, which swaps in a marker that matches
nothing so it runs without Rimatomics); and a freed radius stand catches nobody
up,
where a room stand records the job it interrupts as the one they dressed for
(`AFreedRadiusStandInterruptsNobody`). The registry forgets a stand that is no
longer spawned at the next stand search (`AStandGoneFromItsMapIsForgotten`).
A room stand beside a door serves its own room only: the doorway's room lists
both the stand inside and one outside, and work in the doorway dresses nobody
(`AStandBesideADoorServesItsOwnRoom`). Dressing happens within
the ring, past the stand's own room and never beyond the ring
(`TheRingReplacesTheRoom`), outdoors for recreation, which a room
stand in the open may not serve (`ARadiusServesRecreationInTheOpen`), and at an
owned radius stand before a nearer shared one
(`TheirOwnRadiusStandBeatsANearerSharedOne`). A room stand in the open serves no
work either: it covers no cell, the alert lists it, and a colonist already in
its uniform changes back at their next job (`AStandInTheOpenServesNoWork`).

An allowed area covers its painted cells only while the stand stands in one:
past the walls, and not on an unpainted cell of the stand's own room
(`CoversWhatIsPaintedInTheArea`). The dressing decision agrees
(`DressesForWorkInTheAreaOnly`). A construction delivery dresses a bare builder
only at an area stand whose area holds the steel and every site the trip
supplies; steel or a site outside it, and room and radius modes over the same
cells, dress nobody, and so does the same fetch filed under Hauling at a stand
ticking construction alone, though the colonist does construction. On shift a
delivery to a site in the area keeps the uniform on wherever the
steel lies, filed under Hauling or not, where one to a site outside it changes
them back (`AnAreaStandDressesForADeliveryItHoldsWhole`). A bedroom stand moved to an area has
its Sleeping ticked for real and still shows its work list, where Growing then
replaces Sleeping with the stand still on the area; once the bed is gone a ticked
Sleeping hides the list (`BedroomStandKeepsItsWorkListOnAnArea`). A deleted area leaves its stand in area mode,
serving nothing under the area's last name, and a new area under that name is
not adopted (`DeletedAreaLeavesTheStandServingNothing`). A gravship landing does adopt one, the
area the landing carried under the stand's label, with nobody on shift so that
`PostSwapMap`'s early return is in play, and still refuses a namesake that
leaves out the stand's own cell (`GravshipLandingFindsTheAreaAgain`; the same
re-creation without a flight is the control). The alert that vanilla made from its
subclass lists a stand whose area is gone or that stands outside it, naming the
area, and a colonist in that stand's uniform changes back once its area is
deleted (`ServesNothingAlertNamesTheArea`). A solid target counts as inside
an area when the area covers either its own cell or the floor beside it, for
dressing, for staying dressed, and for a wall repair as pass-through work
(`MiningARockCountsTheRockThenTheFloor`).

The area UI is asserted on what the window builds, since no case draws it. The
Serves menu lists every allowed area but Home in a section of its own, each
with its colour, fades and marks in red one that leaves the stand out, opens
each section after the first with a gap the menu's layout really sees, and
binds whichever area is picked (`ServesMenuListsTheAllowedAreas`). New allowed
area copies the stand's room, or its own cell in the open, names the area for
the stand's work and numbers a second, and greys at vanilla's cap of ten with
vanilla's reason, the gap then moving to Manage areas
(`NewAllowedAreaStartsAsTheRoom`). Paint and Manage areas close the settings
window before vanilla's tool or dialog takes over, with both windows really
opened on the stack (`PaintAndManageAreasCloseTheSettingsFirst`). The row, the
inspect pane and the switch name the area, and say when the stand serves
nothing: outside its area, with the area gone, or as a room stand in the open
(`RowPaneAndButtonNameTheArea`).

Staying dressed goes by what the stand is for, with a case per trigger
(`AWorkStandsRingKeepsTheUniformOn`,
`ASleepStandKeepsThePyjamasOnOnlyForBed`, `ARecreationStandKeepsTheOutfitOnForPlay`).
Pass-through work is judged at both ends, and an errand ahead of the stand's
own work rides along (`AHaulIsJudgedAtBothEnds`). A finished bill's
product is driven through the real `DoBill` at an area, a radius and a room
stand, with the stockpile inside and outside, and Hauling ticked and not
(`BillProductHaul`). It is judged at the stockpile and not at the crafter's cell
(`TheStockpileDecidesNotTheSpotAtTheTable`), goes by the ordinary rule once the stand
stops serving the bill (`ABillTheStandStopsServingChangesThemBack`), needs the
stack in hand and a bill behind it (`BlocksPickedUpOffTheFloorAreNoBillsProduct`,
`BlocksInHandFromOtherWorkAreNoBillsProduct`), and sends nobody to dress
(`ACrafterInTheirOwnClothesIsNotDressedToStoreIt`). The two lists that
decide it are checked as tables, with no map
(`WhatPassesThroughAStandsRadius`). Each case that dresses its colonist
then asserts that the job tracker's pause after the swap did not send them
straight back, which is how that pause was found.
`TheJobTheyDressedForKeepsItOn` dresses through the real `Insert`, moves the
patient away while the colonist changes, and lets the engine's own queue start
the tending, once at a radius stand and once at a room stand; a haul vanilla
starts ahead of the tending, putting it back in the queue, keeps the uniform on,
and so does the tending starting again. The per-stand latch holds its stand and
its trigger only, and lifts at a cell with no covered cell beside it, where one
cell past the ring still holds it (`ChangeBackHoldsOnlyItsStandAndWork`); at a
sleep rack it lasts the night and lifts on waking, a haul ahead of a queued
lay-down not counting as the waking (`ChangeBackAtASleepStandLastsTheNight`); and at an
area stand it holds on the unpainted cell beside the stand, for work in the
area done from outside it, and through the job tracker's pause after that work
(`ChangeBackHoldsBesideAnAreaStand`). A delivery dresses nobody at a room or radius
stand and keeps the uniform of its own work on to a site inside the ring,
wherever the stack lies, each built with the job def its giver uses, a seed
planting resumed with its seed stack in targetC included; to a site past the
ring it changes them back now. The record of the delivery they were dressed for
lets that job through and not the same Job object handed out again with a new
id. Removing an old floor, a site job from the same giver, dresses and changes
back like any work, and a haul ahead of a queued delivery keeps the uniform on,
where one ahead of a delivery to a site past the ring does not
(`ADeliveryKeepsItsOwnWorksUniformOn`, a known gap for Complex Jobs' two givers
without that mod). Pyjamas and kitchen whites change back before a delivery from
steel out past the door, to a site there or in the room, where steel and site
both in the room are the room's. A construction stand's uniform stays on for a
site in the room, the same fetch filed under Hauling included and one filed
under a work type that is not hauling excluded, and comes off now for a first
drop, a site or a further site out past the door, each delivery built with its
first drop and site apart as the giver builds it
(`ADeliveryForOtherWorkChangesThemBackFirst`). An urgent patient on
shift goes straight to bed with the medical emergency setting off: in room mode
to a bed past the door, tagged or as vanilla puts the lay-down back untagged,
with a haul vanilla starts ahead of it, and at a radius stand
(`AnUrgentPatientGoesStraightToBed`). The Serves row, the inspect pane and the
switch's tooltip follow the mode (`ItsWordsFollowWhatItServes`).

**Interop** (`WhoUsesAStand`). `OutfitStandsPlusButtonsMatchTheirOwn` covers
code of ours that runs inside another mod: Outfit Stands Plus' stand button,
reading our stand list, must offer what its own walk would, in the same order
(see [DESIGN.md](DESIGN.md#outfit-stands-plus-stand-button)). It is a known
gap until `--with=` loads that mod.

**Buttons and spares** (`ButtonsAndOrders`). The stand's buttons are read the way
the gizmo bar gets them, without drawing (`HarnessGizmos`): the switch names what
the stand serves (`TheSwitchSaysWhatTheStandServes`), and stands selected
together keep their own buttons (`EachSelectedStandKeepsItsOwnButtons`). The
parked-clothes tests are a pair that only means anything as one: a checked-out pawn refuses a spare duplicating the kit
parked in their stand (`OnShiftTheyLeaveSparesOfTheirParkedClothes`), and takes one once that kit
is worn through (`WornOutParkedClothesCanBeReplaced`).

**Recolor** (`ButtonsAndOrders` too). `ARecolorOrderIsCarriedOutInUniform` drives
Ideology's apparel recolor through the wardrobe optimizer, a job's own
reservations, and `TryTakeOrderedJob` as the styling station calls it. On
shift, the optimizer gives nothing and a recolor job the player did not order
is refused, while the station's order is taken and the pawn starts it in
uniform. An order naming a garment parked in the stand is refused. Off shift,
the optimizer's recolor comes back and reserves, and one naming the parked
garment is still refused. The job, the station and the order are Ideology's,
so the case is a known gap on a mod list without it. The minimal list
`run-harness.sh` writes names only Odyssey as a known expansion, and the game
turns on every installed expansion it has not seen, so that list has Ideology
whenever it is installed. `--full` has it when the copied list does, and a
runner that names every expansion as known has only the ones it activates.
The case does not check that the order finishes and dyes anything: the pawn
never walks under the harness, so it stops the job once it has started.

**Tables and rooms** (`ReadingJobsAndRooms`, the tables in `HarnessTables`). Four cases need no map and check that
the static tables still match the def database, where every lookup is
silent-fail and a renamed def empties its row with no error: the room-role
table (`EveryKindOfRoomFindsItsWork`), the work of each giver that tends, feeds or
operates on a patient in a hospital or barn bed against that room's automatic
stand, and of each prisoner feeding giver against a stand ticked for Warden
(`PatientsAreServedWhereTheyLie`), the job-target, ignored-giver and
ride-along tables, delivery givers by class, deliveries by job, and every
official giver of a feeding class on the ignored list included
(`MealRunsDeliveriesAndFollowUpsAreToldApart`), and the room-contents markers (`BuildingsThatGiveARoomItsWork`). The
patient case notes, and passes, a prisoner giver a mod moves to work of its
own: a cell's stand serves what the player ticks, and the README says to tick
that work beside Warden. `MealRunsDeliveriesAndFollowUpsAreToldApart` and `BuildingsThatGiveARoomItsWork` check
Rimatomics' names only when it is loaded, and count as known gaps without it. A
fifth asserts the arithmetic that sizes the work-type dialog's body
(`WorkTypesWindowFitsItsList`). Two need a map
and resolve a work target's room: one with no room of its own
(`JobAtAWallFindsTheRoom`), and one in an exterior wall, from either side
(`JobInAnOuterWallFindsTheRoomInside`).

**Round trips** (`SavingAndLoading`). Save, load back through the
engine's own synchronous loader, and check the written file as well as on
the loaded state: a plain trip, which also sweeps vanilla's removal flag and
keeps a radius catchment (`AStandInUseComesBackFromASave`); a save from before v1.0.2, with the
legacy keys and none of the catchment keys (`ASaveFromAnOlderShiftChangeGetsItsOwnerBack`); an older save whose stand carries a foreign
assignable with an owner of its own (`AnotherModsOwnerComesThroughALoad`); and three area
stands, one whose area was renamed before the save, one whose area was deleted,
and one boxed and carried off every map, which keeps its binding
(`AStandsAreaComesBackFromASave`). On the four-mod list the legacy-key test takes the key
migration, and beside a foreign assignable it takes the decline and the
reconcile instead; no other test reaches the decline. These tests replace
`Current.Game`, so they run last of all; the summary of `DebugTools_SaveRoundTrip`,
which holds what they share, says what else follows from that. They write five
saves (`shiftchange-harness-*`) into the Saves folder of the game they run in,
which from the Test Explorer is the player's own, and delete them once all four
have run, except those a failed test used: those stay for a bug report, and
Test.log names them and their folder (`SavingAndLoading.RemoveSaves`).

**The harness itself.** `HarnessTestOrder` and `HarnessLoadNote`, above, run at
the main menu before any test that plays. Each check signals DevTools as it is made,
so the counts are DevTools' own and no test checks them.
`HealthyColonistsAndOpenGround` (`TheTestColony`), run first, asserts what
every other case takes for granted about its fixture. Forty colonists from the
fixture in a row need no tending. Asthma makes a colonist an urgent patient
until the fixture removes it. A cell of natural rock past the
pad becomes ground with a room under `OpenGround`. "Four engine traps" says why
each is there. It also checks that the margin around the pad is in rooms that
touch the map edge. Six cases put a stand in the open, on the pad left unwalled
or on the margin outside its door, and the mod tests "in the open" with
`Room.TouchesMapEdge`. A generated map can put its centre in open ground cut off
from the edge by rock, deep water or walls, where all six fail at once, so the
pad finder (`DebugTools_LifecycleHarness.TryFindPad`) requires it, and this
check names the cause should a pad ever land in such a place. Test.log says
where the pad is.

## What is not covered

- **The repair branches in `PostSpawnSetup`.** The round-trip cases cover
  scribing itself — what gets written, and what survives coming back — but not
  the paths that fix up a ledger found inconsistent on spawn. Those are still
  exercised only in play.
- **A real gravship launch.** The flight cases drive `DeSpawn(WillReplace)` →
  `Spawn` → `PostSwapMap`, the engine's order (`Fly` runs the engine's
  `PreSwapMap` first, `StandAndColonistFlyTogether` does not), but no gravship is
  built or flown, so the area rebind is tested against an area re-created by
  hand on the same map, not one the landing copied. The cases that need a
  second map use a pocket map (`HarnessFixtures.SecondMap`), and remove it with
  `PocketMapUtility.DestroyPocketMap` where a takeoff would abandon the origin.
  Both reach the same map removal, but neither is a takeoff.
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
  open (`PaintAndManageAreasCloseTheSettingsFirst`). The button cases read
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
- **Pooling on and off, `SwapPlan`'s rollback.** No cases yet. The retry
  cooldown is asserted on the allowed-area path only. The recolor case does not
  reach a recolor job that was already running when the game saved, which
  resumes without asking for reservations.
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
own comp handlers directly. Decision functions such as `TryInsertSwap`, the one
the prefix calls, are driven directly where the decision is the claim. A harness
that hand-rolls the call sequence tests the author's model of the engine, and
the first two traps below are cases where that model would have been wrong.

**Pair every negative with a positive control.** "Did not divert" is worthless
alone, because a stand that was never eligible satisfies it too. The drafted
assertion is bracketed by an undrafted one; the decision table opens by proving
the baseline fires. This is not hypothetical caution: three assertions passed
against broken code in draft, including one that checked a job was "not a swap"
when any job at all satisfied it.

**Make a timeout say where it stopped.** `RunSwap` prints the toil index, tick
budget, driver type and pawn position when it gives up. The first two traps
below were diagnosed in one run each because of it; a bare "timed out" would have cost
several ten-minute cycles apiece.

**Count known gaps apart from failures.** A case can be marked `GAP` with its
reason recorded — it still runs, and reports `FIXED` if it starts passing.
DevTools counts such a case as skipped. Most
gaps are a case whose mod is not on the list. Until v1.5.0 one was a real gap,
kept on purpose on every list: fishing never dressed, because Odyssey hands
fishing out with no giver, and the fix waited for stands that can serve beyond
their room. The mechanism exists because a suite that always prints one failure
is ignored within a week.

## Four engine traps

None is visible from the API surface. All are recorded at their call sites.

**Pathfinding is asynchronous.** `Pawn_PathFollower.PatherTick` waits on a path
request, which the map's pathfinder serves a tick later in `Map.MapPreTick`,
called from `TickManager.DoSingleTick`, never from `Pawn.DoTick()`. Ticking a
single pawn therefore leaves it flagged `moving` forever, one cell short of the
stand. The cases once ticked their colonist that way and staged it *on* the
interaction cell so no path was ever requested. They now step the game's clock
(`GameClock`), which serves the request, so never tick a pawn alone. Fixtures
still stage the colonist on the interaction cell, so a case's checks are about
the change alone; `ChangingAtAStand.WalksToTheStand` covers the walk, which is
vanilla's `Toils_Goto` rather than ours.

**Jobs are pooled.** When a job ends it returns to `JobMaker`'s pool and is
handed straight back out for the pawn's next job, so a `Job` reference held
across its own completion silently becomes a different job. `RunSwap` watches
`jobs.curDriver`, which is built per job and never pooled — and it checks the
`JobCondition` the job ended with, because "the driver changed" is equally true
of a job that failed its reservations and died before its transfer toil.

**The whole map ticks.** Stepping the clock ticks everything on the map, not
only the colonist a case is about, so what a case arranged moves while a swap
runs. Three kinds of case broke when they moved onto the clock on 2026-11-14,
all for this reason. Odyssey's vacuum simulation (`VacuumComponent`)
exchanges air between neighbouring rooms every 250 ticks, so the ship cases'
rooms drifted from the values the case set, and seven vacuum cases failed. The
ship has no oxygen pump, so the fixture holds its values instead: a fixture's
`Hold` runs before every tick and after the last (`HoldVacuum`). A colonist
spawned for a case picks jobs of its own, and the meal-run case's patient
walked out of the room mid-swap; a stand-in patient now stays where it is put
on a posture wait (`StayPut`). And a Goto ends when the pawn gets there, so
the catch-up cases' second colonist finished the job a freed stand was meant
to interrupt; their work is now a posture wait stamped with the work's giver
(`WorkingAt`), which the catch-up may interrupt and which stays open.

**A new colonist is not tended.** Vanilla tends a generated pawn's conditions
only when it is not joining the player (`PawnGenerator.cs:1297`). A fixture
colonist who rolled asthma, roughly one in seventy by thirty, starts untended,
and anything tendable makes a pawn an urgent patient
(`HealthAIUtility.ShouldSeekMedicalRestUrgent`). Nobody dresses an urgent
patient for bed or changes one back before it, so the five cases that send
their colonist to bed could each fail at random, and the two radius cases did.
The fixture
removes what a generated colonist needs tended
(`DebugTools_Fixtures.RemoveTendableConditions`); the bed-rest, sleep and
urgent-patient cases each did that for themselves before.

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
| Comment history | A date or a `File.cs:123` citation in a comment under `Source/`. AGENTS.md keeps history out of comments, and a line number into the game moves with every update. |

CI also runs the BBCode validator over the store description, because an
unclosed tag makes Steam render the remainder of the page as literal text.

These replaced two shell greps anchored to the start of a line and to a single
line. Measured against a file of realistic declarations — `static private int x;`,
`[Attr] private int y;`, and the multi-line auto-properties this codebase
actually writes — the greps found none of seven.

## Adding a case

Add a test to the story fixture it belongs with, at the point it should run: a
`[Test]` named for what a player would recognise, with a sentence in the game's
words as its `[TestDescription]`. Its body is a local function, and the test
returns one of `StoryFixture`'s frames:

| Frame | The body gets |
|---|---|
| `Staged(body, kit, gender)` | a stand holding a duster and a colonist beside it, from `Stage`; a body that changes at the stand (`Swap`) is an `IEnumerator` and steps the game's clock |
| `Staged(build, scene, body)` | the fixture `build`, a `(map, pad) => Fixture`, sets up on the pad; `scene` says what that is, as the story's first line |
| `OnThePad(body)` | DevTools' map and the pad, as `Map` and `Pad`, and no fixture |
| `Told(body)` | no map, no pad and no camera move |

A body that steps the clock within its frame changes at the stand with
`SwapNow(story, fix)`, which notes where a change that gave up got stuck, and
every fixture has `NotSentStraightBack(story, fix)` and `AnotherColonist(story,
fix, gender, nick)` to hand. `SavingAndLoading` runs its bodies as the game runs
a load (`Wrap`).

Every frame clears the pad after the test, whatever it did. A fixture holds at
most 16 tests ([Running it](#running-it) says why), and runs at its place in
`CaseGroup`.

Each check is `story.Check(condition, text)`, its text what a player would see
or understand: colonists by name, garments, rooms and settings by what the game
or the stand's window calls them. The first check that fails names the test's
failure; a failed check does not stop the test, so the story shows every check,
and `Check` returns its condition for a test to stop where the rest would be
moot. `story.Note(text)` adds a line that is not a check, such as a
setting turned on or a job handed out. `story.KnownGap(condition, what, why)`
marks something not yet true, which skips the test, and says so when it holds.
Pair every negative with a line showing the same thing going the other way.

`Build()` hand-assembles a checked-out state for lifecycle tests. `Stage(map,
pad, kit, enclose, capableOf, gender)` produces an undressed pawn for tests that
watch the driver work (the decency pair turns on its gender), and can wall the
pad into a proper room — which functional tests need,
because `FindAvailableStand` walks `room.ContainedThings` and an open pad is the
whole outdoors.

A case that puts something past the pad takes the cell with
`OpenGround(fix, cell)`, which clears it to bare soil and has `Teardown` clear
it again. The pad finder checks one cell of margin around the pad, for ground in
a room that touches the map edge, and no more, so further out a map can have
natural rock, and anything left there stays on the map for every case after.
