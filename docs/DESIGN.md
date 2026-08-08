# Shift Change — technical design

How this mod is built: what it had to interface with inside RimWorld, and why
each interface point took the shape it did.

**Audience**: programmers. No RimWorld modding experience is assumed — the
primer below covers everything the rest of the document uses. Players want the
[README](../README.md) instead.

**Verification**: every engine claim in this document was checked against the
decompiled game assembly, version **1.6.4871**, at the time of writing. File
and line references (`Pawn_JobTracker.cs:338`) point into that decompilation.
Line numbers drift across game versions; method names drift much more slowly.

---

## What the mod does

A vanilla outfit stand placed in a work room dresses colonists for that room's
work. When a colonist takes an **automatic** job whose work type matches the
stand's and whose target is in the stand's room, they change into the stand's
outfit first, do the work, and change back when their next job takes them
elsewhere. Direct player orders and emergencies are never diverted, in either
direction. Nothing happens while the map is under threat.

The mod ships **no art and no apparel**. It adds behaviour to an existing
vanilla building, which is both a design constraint (see below) and the
positioning of the whole project.

## RimWorld in five concepts

Everything below leans on these.

1. **Defs and XML patches.** Game content is XML (`ThingDef`, `JobDef`,
   `WorkGiverDef`, …), loaded into a global database at startup. A mod can add
   its own defs, or patch *other* defs — including vanilla's — with XML patch
   operations. Shift Change adds one `JobDef` and patches two components onto
   vanilla's outfit stand def.
2. **Comps.** A `ThingComp` is a component attached to a thing (building,
   item) via its def — composition, not inheritance. Comps tick, save state,
   contribute gizmos (the buttons on a selected thing) and inspect text.
   Attaching a comp via XML patch means vanilla buildings can gain modded
   behaviour without replacing their class.
3. **The job system.** Pawns decide what to do via a **think tree**; for work,
   a `ThinkNode` walks the pawn's work priorities and asks each
   **WorkGiver** to scan for something to do. A WorkGiver produces a **Job**
   (def + targets), which `Pawn_JobTracker.StartJob` turns into a running
   **JobDriver** — a state machine of *toils* (go here, wait, do the thing).
   Starting a job **reserves** its targets so other pawns won't take them.
4. **Harmony.** The runtime patching library every behaviour mod uses:
   prefixes, postfixes and transpilers on game methods. A prefix returning
   `false` skips the original method — which is how a mod can substitute its
   own behaviour at a decision point.
5. **Scribing.** RimWorld's save system. Objects write fields via
   `Scribe_*.Look` calls; things referenced by multiple owners save as
   references resolved on load. A comp's saved fields live inside its parent
   thing's save node — and unrecognized fields are silently skipped on load,
   which is what makes "remove the mod, keep the save" possible.

## The constraints that shaped everything

- **Automatic work only.** A player-forced job is a direct order; sending the
  pawn to a wardrobe first would make orders feel broken. This rule ended up
  applying in both directions (see interception, below).
- **The room is the boundary on purpose.** Tying the uniform to the room
  guarantees the gear is already where the work is — a pawn is never far from
  their civvies when a crisis breaks. An unbounded "always wear X for work Y"
  mode was considered and rejected because it reintroduces exactly that
  distance problem.
- **Zero new art.** The stand is vanilla's; icons come from vanilla's UI
  atlas; the mod is code and XML only. This is why ownership was *patched
  onto* the vanilla def rather than shipped as a new building.
- **Fail open, always.** This mod patches the method that starts every job for
  every pawn. An unhandled exception there is a bricked colony. Every hook
  wraps its work in a catch that logs once, disables the mod for the session,
  and lets vanilla proceed.

---

## Interception: where to put the hook

**The interface: a Harmony prefix on `Pawn_JobTracker.StartJob`.**

The decision the mod makes — "should this pawn change clothes before this
job?" — needs three facts at decision time: the job's **work type**, whether
it was **player-forced**, and the job's **target location** (to resolve the
room). All three are readable at `StartJob`: the assigning code has already
attached the `WorkGiverDef` (whose `workType` field is the work type), the
`playerForced` flag, and the targets.

### The trap that killed the obvious alternative

The nearest prior-art mod hooked `Pawn_JobTracker.TryOpportunisticJob` — the
slot vanilla uses to insert "haul something on your way" jobs. It looks
perfect: it is called from inside `StartJob` for every job
(`Pawn_JobTracker.cs:338`), and its contract is literally "return a job to do
first." It is a trap, twice over:

- Vanilla bails out of it for drafted pawns and several other states
  (`CanPawnTakeOpportunisticJob`, `Pawn_JobTracker.cs:626-649`).
- It only fires if the incoming job's def opts in via
  `allowOpportunisticPrefix` (`:657`). **164 of 321** JobDefs in 1.6 set it —
  and `TendPatient` is not one of them (`Jobs_Work.xml:370-375`). Doctoring,
  the headline use case, is unreachable through that hook. The prior-art mod's
  source contains a commented-out line forcing the flag — the author found the
  wall and backed off.

### Inserting a job ahead of another job

The insertion pattern is vanilla's own. When vanilla inserts an opportunistic
haul, it starts the other job and puts the displaced job at the front of the
pawn's job queue (`Pawn_JobTracker.cs:331-347`). Shift Change does the same
from the prefix: start the swap job, then `jobQueue.EnqueueFirst(originalJob)`
— the displaced job resumes the moment the pawn finishes changing.

Two details matter more than they look:

- **Order: start the swap first, enqueue second.** If starting the swap
  throws and the original job were already queued, the fail-open catch would
  let the original *also* start — one Job object in two places corrupts the
  tracker. `StartJob` never reads the queue, so enqueueing after is equivalent
  on success and strictly safer on failure.
- **Re-entrancy.** Starting a job from inside a `StartJob` prefix re-enters
  the prefix. A static guard flag makes the inner call pass through.

### Deferred jobs must carry their reservations

Vanilla reserves a job's targets inside `StartJob` — and the prefix skips the
original `StartJob`. Without more, the deferred job's targets (the patient,
the bench) sit unreserved for the whole walk-and-change, and any other pawn's
work scan can take them. In play this appeared as a distant doctor dressing
for a patient a nearer doctor had already tended, then changing straight back.

Vanilla's opportunistic path shows the fix: it makes the reservations *first*,
then enqueues — and deliberately does not release them
(`Pawn_JobTracker.cs:331-347`). Shift Change does the same: build the deferred
job's driver, call `TryMakePreToilReservations`, and only then start the swap.
The pattern is safe to copy because vanilla's queue plumbing already releases
a queued job's reservations on every clearing path
(`QueuedJob.Cleanup` → `ClearReservationsForJob`, `QueuedJob.cs:24-33`), and a
queued job re-reserving its own targets when it finally starts is idempotent.

### The gates, and why each exists

In order, the prefix declines to act when:

| Gate | Why |
|---|---|
| pawn drafted / downed / mental state | being told where to be, or not themselves |
| map danger ≠ None | **no vanilla precedent to copy** — `JobGiver_OptimizeApparel` has no danger check at all; vanilla gets apparel-change safety from *think-tree position* (`Humanlike.xml:302-306`), and this hook sits downstream of the think tree, so the gate has to be explicit |
| `job.playerForced` | direct orders execute immediately — in both directions; a pawn in uniform given a forced order keeps the uniform on and returns it later |
| `workGiverDef.emergency` | emergency givers exist because something cannot wait (`DoctorTendEmergency`); a bleeding pawn must not wait for a wardrobe trip |

The forced/emergency exemptions originally gated only the dressing path;
play revealed the return trip could delay an emergency identically, and the
gates were hoisted above both.

---

## The outfit stand: what vanilla provides, and what it pointedly does not

**The interface: `Building_OutfitStand` (Odyssey), used as-is; two comps
patched onto its def; its container API called from our swap driver.**

What vanilla's stand gives for free:

- A storage building holding apparel, with filters, storage-group support and
  a display of its contents on the stand model.
- **`allowRemovingItems`, default false** — a single flag driving both
  `IHaulSource.HaulSourceEnabled` and `IApparelSource.ApparelSourceEnabled`
  (`Building_OutfitStand.cs:102-104`). This default is load-bearing: it is
  what stops haulers carting uniforms off to stockpiles and stops
  `JobGiver_OptimizeApparel` raiding the stand for a colonist whose outfit
  policy happens to score the uniform highly.
- **One outfit of capacity, structurally.** `HasRoomForApparelOfDef`
  (`:332-342`) is not a count limit — it is a *conflict* check, refusing
  anything that cannot be worn together with what the stand already holds. A
  stand is one outfit's worth, full stop. This single fact killed the
  "shared stand with several pawns' sets" design at the root: it is not
  merely untidy, it is physically impossible. One stand, one outfit, and
  ownership follows from capacity.
- **Eviction.** `TryDropThingsToMakeRoomForThingOfDef` (`:344-364`) drops
  conflicting contents on the floor nearby. This is the entire "dead owner's
  clothes" story: abandon the ledger, leave the garments as ordinary contents,
  and the next user's first deposit evicts them for haulers to collect. No
  custom reclaim logic exists because none is needed.

What vanilla pointedly does not provide:

- **Any concept of whose clothes are inside.** The stand scribes its
  container, settings and the toggle — nothing else (`:878-885`). It is a
  shared bag.
- **A usable swap.** Vanilla's own `JobDriver_UseOutfitStand` is an
  *anonymous* swap: it claims every wearable item in the stand for whoever
  arrives (`:38-63`) and pushes their displaced clothes back into the same
  undifferentiated bag (`:83-96`). Two pawns sharing one stand will walk off
  in each other's clothes. It also force-flags **everything** it hands out
  (`:91`) — including your street clothes on the way back, whether or not
  they were force-worn before. It over-preserves; a correct swap restores the
  exact prior state. Both properties made it unusable as a return trip, which
  is why the mod ships its own driver.

## Ownership, pooling, and the ledger

**The interface: `CompAssignableToPawn`, patched onto the vanilla def, plus a
mod-owned comp holding the ledger.**

`CompAssignableToPawn` is the ownership machinery beds and thrones use, and it
is XML-attachable: the "Set owner" gizmo, the assignment dialog, reference
scribing and stale-owner cleanup all come with the comp
(`CompAssignableToPawn.cs:164-195`). The mod's subclass narrows the candidate
list to pawns capable of the stand's work types and renames the gizmo — the
base comp never displays the owner anywhere; beds only appear to because
`Building_Bed` writes the owner into its own inspect string.

Patching the comp onto **vanilla's def** rather than shipping a new stand
means existing stands in existing saves gain the gizmo on load, and the mod
keeps its zero-art rule. The cost is discipline about touching a shared
building — one instance of which:

> **The hotkey trap.** The base comp hardcodes its gizmo to `Misc4`
> (`CompAssignableToPawn.cs:176`), which is **N**. Harmless on beds — but the
> outfit stand is a *storage* building, and the settings clipboard binds copy
> to the very same `Misc4`/N (`StorageSettingsClipboard.cs:40`). Reusing a
> vanilla comp on a building category it never shipped on can import a hotkey
> collision. The subclass strips the binding.

**Unassigned means pooled.** An unassigned stand may be claimed by any capable
colonist — the bed model, and the answer to "a kitchen has eight possible
cooks": you need one stand per *concurrent* cook, not per cook. An assigned
stand is reserved for its owner. A mod setting (default on) turns pooling off
globally for players who want participation strictly opt-in.

**The borrower, not the owner, is the ledger's truth.** The mod-owned comp
records, scribed by reference: the borrower, the specific garments they parked
(their civvies), the specific garments they took (the uniform), and which of
the parked garments were force-worn at check-in. Rebuilding the return-trip
registry from the *assigned owner* would be wrong the moment a stand is
reassigned mid-shift; the borrower field is authoritative.

**Ownership must end when vanilla thinks it ends.**
`Pawn_Ownership.UnclaimAll()` — called on death, trade, kidnap and map exit
(`Pawn.cs:2341, :2565, :2599, :2645`) — unclaims a **hardcoded list**: bed,
grave, throne, deathrest casket (`Pawn_Ownership.cs:286-292`). It does not
walk `CompAssignableToPawn` buildings, so a postfix extends the same moment to
stands — matching the semantics players already expect from beds, rather than
inventing a new lifecycle. The same postfix reaps *borrowed* stands, which
unassignment alone would miss entirely: a pool borrower was never assigned to
anything.

## Deciding what to wear: one authority

**The interface: `ApparelUtility.CanWearTogether`, `PawnCanWear`, biocoding
checks, `Pawn_ApparelTracker.IsLocked` — wrapped in exactly one place.**

The question "what would this swap move for this pawn?" is asked twice: by the
stand selector (is this trip worth taking?) and by the swap driver on arrival
(what do I actually move?). Early on these were two implementations, and they
diverged: the selector asked "does the stand hold any apparel?", the driver
asked whether *this pawn* could wear it. A stand holding a garment the pawn
could not wear was selected, walked to, and swapped with — moving nothing,
which from the player's side is indistinguishable from a pawn changing at an
empty rack.

The fix is structural, not a patch: a single `SwapPlan` builds the plan for
both callers, so the selector asks precisely the question the driver will ask.
Every wearability predicate lives in that one file.

## The forced-apparel lifecycle

**The interface: `OutfitForcedHandler` — and one destructive vanilla behaviour
the ledger exists to survive.**

The forced flag (`SetForced`) is what exempts apparel from
`JobGiver_OptimizeApparel`. Without it, the optimizer un-swaps the uniform at
its next tick; so the swap forces what it issues, and — the half vanilla's own
driver never does — **clears the flag on the return trip**, or the uniform
would be pinned to the pawn forever.

The subtle part: **every apparel removal destroys the flag.**
`Pawn_ApparelTracker.Notify_ApparelRemoved` calls `SetForced(ap, false)`
unconditionally (`Pawn_ApparelTracker.cs:784-790`). The moment a force-worn
duster is parked in the stand, the fact that it was force-worn ceases to exist
anywhere in the game. The ledger therefore records forced-ness *at check-in* —
the last moment the fact exists — and the return trip restores it exactly.
Garments that were forced come back forced; garments that were not stay
policy-managed. (Vanilla's driver "solves" this by force-flagging everything
it returns, which is the opposite error.)

A deliberate non-feature: royal titles and ideology roles can *require*
apparel, and nothing in vanilla stops a swap removing a required garment — the
optimizer only scores requirements (×25/×10,
`JobGiver_OptimizeApparel.cs:360-398`) and the stand driver checks only the
narrower `IsLocked`. The mod does not block this either, on purpose: the
block's only lever would be refusing the uniform, so a titled pawn would
silently never change — worse than the mood penalty the player already sees in
the needs tab via vanilla's own thoughts. Assigning the stand was the player's
deliberate act; the mod does not second-guess it, and does not even log about
it (a dev-log warning reads as a mod error).

## Rooms to work types

**The interface: `Room.Role` and the `RoomRoleWorker` scoring system — read,
never patched.**

Vanilla continuously scores every room: each `RoomRoleDef` has a worker that
rates the room's contents, highest score wins. Two quirks matter:

- **Hospital is a trump card, not a contestant.** Its worker returns a flat
  100,000 for any non-prisoner *medical* bed and 0 otherwise
  (`RoomRoleWorker_Hospital.cs:8-32`); Laboratory scores 60 per lab bench.
  A hospital full of genetics equipment is still a Hospital — and a ward whose
  beds are not flagged medical scores zero, letting one gene bank flip the
  room to Laboratory.
- **Roles are one-per-room, work is not.** A Workshop hosts crafting,
  tailoring, smithing and art; a Laboratory hosts research *and* drug
  synthesis — which arrives as **Crafting** work (`DoBillsProduceDrugs`,
  `workType Crafting`, `WorkGivers.xml:1139-1148`). A role therefore maps to a
  **set** of work types, not a scalar; the first design had scalars and missed
  the drug lab entirely.

Default sets (Hospital → Doctor; Laboratory → Research, Crafting; Kitchen →
Cooking; Workshop → Crafting, Tailoring, Smithing, Art; Barn → Handling,
Doctor) deliberately exclude base-wide pass-through work — Hauling, Cleaning,
Construction, Firefighting — because a hauler carrying meals into the hospital
should not scrub in. A per-stand dialog overrides the set; the stand can also
be excluded entirely, so a decorative stand in a work room never joins the
pool.

The trigger is the **job**, not the doorway: work-type-in-set AND
job-target-in-room. A doctor crossing the hospital to reach the storeroom, or
anyone walking in to eat, changes nothing.

## The mid-job catch-up

**The interface: `StartJob(..., resumeCurJobAfterwards: true)` — vanilla's own
suspend-and-resume.**

With two workstations and two stands, a pawn can start working bare because
both stands were checked out at the moment their job started — and a stand
frees up seconds later. Event-driven fix: when a stand returns to the pool,
scan for a colonist already doing matching work in its room and interrupt them
to change, resuming their job afterwards.

The interrupt is the same mechanism vomiting uses: `StartJob` with
`resumeCurJobAfterwards` suspends the current job when its def allows and
resumes it from the queue (`Pawn_JobTracker.cs:293-296`). The eligibility gate
is `suspendable && casualInterruptible` — both default true
(`JobDef.cs:24-26`), and both are **false on `TendPatient`** — so bills and
research are caught up (crafting progress lives in the unfinished thing on the
bench; nothing is lost), while a doctor mid-treatment is never pulled off a
patient to fetch scrubs. The gate was not designed; it fell out of reading
what vanilla already declares about its own jobs.

## State: what survives a save, a load, and an uninstall

- **The ledger scribes by reference** inside the stand's own save node —
  vanilla's stand driver does the same for its transfer lists, which is what
  made the pattern obviously safe.
- **Session-scoped statics must reset when the loaded game changes.** Two
  registries live outside the save: the borrower→stand map, and a retry
  cooldown keyed by `thingIDNumber` and stamped with `TicksGame`. Both of
  those counters **restart per save** — load an earlier save in the same
  session and a stale cooldown entry sits in the *future*, silently blocking a
  same-ID pawn until the clock catches up. A guard clears both whenever
  `Current.Game` changes identity. Deliberately not a `GameComponent`: a
  component writes its class name into every save, which costs players a
  one-time load error after uninstalling. The guard has zero save footprint.
- **Uninstalling is clean by construction.** The mod's saved state is comp
  fields inside vanilla buildings' nodes (skipped silently when unrecognized)
  plus vanilla's own forced-apparel flags. Removing the mod reverts every
  stand to plain vanilla furniture; a pawn mid-shift keeps wearing the uniform
  (unforce it by hand) and their own clothes are sitting in the stand.

## Development tooling (brief)

The repo carries a Debug-only hot-reload rig (Zetrith's EditCompileReload,
plus workarounds for three things Unity's Mono does not support) used for UI
iteration. It never ships: Release builds compile none of it and sweep its
artifacts from the mod's load path. A session is either **live** (Release, no
badge, gameplay full) or **lab** (Debug; an on-screen badge that turns orange
at the first reload, at which point all gameplay interception disables itself
for the session). The full findings — including why method-swapped code cannot
touch private members, auto-property backing fields, protected base members,
or abstract bases' implicit constructors — are recorded in
[`CLAUDE.md`](../CLAUDE.md), which is the implementation map this document
narrates.

## The shape of the whole thing

One prefix decides; one driver moves clothes; one comp remembers; one comp
owns; one file judges wearability; one postfix ends ownership when vanilla
does. Every piece that could be vanilla's is vanilla's — the insertion
pattern, the reservations, the eviction, the suspend-resume, the ownership
lifecycle — because each borrowed mechanism arrives already balanced against
ten years of the game's own edge cases. The mod's original code is mostly
bookkeeping about *whose clothes are where*, which is precisely the one thing
vanilla's stand declines to know.
