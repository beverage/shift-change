# Changelog

Every release of Shift Change, newest first. From 1.0.1 on, each entry carries the change note
posted on the Steam Workshop for that version, sorted into Added, Changed, Removed and Fixed;
1.0.0 lists what the first release did. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- A mod setting, **New stands are used for shift changes**, on by default. Turn it off and every
  outfit stand built from then on starts as "Not used for shift changes": ordinary furniture
  until you set it up with its switch. Stands that already exist keep their own setting, whichever
  way it is set.
- Hovering a stand's switch or its Set owner button now marks that stand on the map with the
  pulsing square vanilla shows on a target you are about to pick. An arrow points to it as well,
  as it does when you hover a storage link. With several stands selected, each stand shows its
  own pair of buttons, and until now nothing on them said which stand they belong to.

### Fixed

- The description of **Unassigned stands are shared** said that with it off, an unassigned stand
  behaves exactly like vanilla furniture. It does not: it dresses nobody, but
  "Allow removing items" stays off and its contents stay out of trade. The description now says
  so, and points at "Not used for shift changes" for a stand that should be ordinary furniture.
  How the setting works is unchanged.

- If a stand's owner was kidnapped while the stand was boxed up for a move, setting it down made
  them its owner again and kept the shift they had open at it, so nobody else could use it until
  they came home. A kidnapped owner is now dropped when the stand is placed, as they already were
  from a stand left standing. In a save where a stand was already set down this way, loading the
  save releases the shift; the owner still needs removing once with Set owner.
- On RimWorld 1.7, clicking the greyed Automatic option in a stand's settings, on a stand
  serving a radius or an allowed area, played a click though nothing changed. It is silent now.

## [1.5.3] - 2026-10-21

### Fixed

- With Ideology, ordering a colonist in uniform to Recolor apparel at a styling station did
  nothing, and left a warning in the log. The order now goes through, and the colonist dyes the
  clothes you picked new colours for in Change style. When those include the stand's outfit, it
  goes back to the stand in its new colours, and the next colonist to borrow it wears them.
- If a stand's owner died while the stand was boxed up for a move, setting it down made them its
  owner again, so nobody else could use it. A dead owner is now dropped when the stand is
  placed. A stand already held for a dead colonist this way needs them removed once with Set
  owner.

Unchanged: a colonist in uniform still never recolors anything on their own; only your order
does. Off shift, in their own clothes, recoloring works as before. A living owner still gets
their stand back after a move.

## [1.5.2] - 2026-10-18

### Fixed

- On a ship in space, a colonist could change out of a vacsuit or power armor at a stand
  indoors, then walk out through vacuum in their work clothes for the job, and be burnt or
  killed. The usual case is a bill whose ingredients lie outside the ship, or in a sealed room
  that only open deck leads to. On a map in space, a colonist is no longer dressed for a job
  that goes through vacuum when the stand's outfit protects them less than what they have on:
  they do the job in their own gear. Someone already in uniform changes back first. Reported by
  a player.
- A stand whose outfit protects as well or better, such as a vacsuit stand by the airlock, still
  dresses them, and now wins over a nearer stand that would take the suit off.
- A colonist in uniform who is cut off from their stand by vacuum is not sent back across it to
  change. When their next job crosses vacuum, a message says their own clothes are out of reach,
  and the same goes for a stand on another map.
- Orders run as given, as always, and a message says when one sends a colonist through vacuum in
  work clothes that protect less than their own. Each colonist gets at most one of these
  messages an in-game hour.

Unchanged: on a planet nothing changes, and on a ship whose rooms all connect through air
nothing changes either. Colonists still walk wherever the game sends them; only what they wear
is different. Where a sealed corridor and open deck both lead somewhere and the deck is shorter,
a colonist may still cut across it in uniform.

## [1.5.1] - 2026-10-16

### Fixed

- A colonist in uniform who flew off on a gravship while their outfit stand stayed behind, on a
  map without a grav anchor, kept the uniform force-worn for good, so their apparel policy never
  replaced it. The takeoff abandons that map, and the stand and the clothes parked in it go with
  it, as before. The uniform now becomes ordinary clothing as the ship leaves. The same holds
  for a stand left on any other map that is abandoned while its colonist is away.
- A colonist already stuck this way keeps the forced flag after the update: press Clr. forced
  for them once, on the Assign tab.

Unchanged: a gravship that carries a colonist and their stand together changes nothing, and they
change back after landing as usual. A flight that parts them while the map left behind is kept
by a grav anchor leaves the colonist in uniform until the two are on one map again, and from
then on they change back as usual.

## [1.5.0] - 2026-10-14

### Added

- A stand can serve a radius or an allowed area instead of its room. Choose under Serves, at the
  bottom of the stand's settings. Within a radius covers every cell within a set distance of the
  stand, from 3 to 79 cells, walls or no walls, and draws the ring while the stand is selected.
  An allowed area covers whatever you paint. The menu lists your areas with their colours, New
  allowed area starts one from the stand's room, and Paint and Manage areas open vanilla's own
  tools. The stand has to stand inside its area. A shed in the fields, a fishing hut on the
  shore, a cell block or a build site can each have its own uniform now.
- Inside a radius or an area, the uniform stays on for what the stand serves, and for idling and
  wandering. At a work stand it also stays on for hauling, cleaning and building where every
  place the job names is inside. Most other jobs change them back first, a sit-down meal or work
  the stand does not serve among them. A sleep stand is stricter: pyjamas stay on only in bed,
  and on the way to a bed inside its ring or area.

### Changed

- A stand standing in the open and serving its room now serves nothing, work included. Every
  outdoor cell belongs to one map-wide room, so a work stand there used to dress colonists for
  every outdoor job on the map. A new alert, "Shift stand serves nothing", lists those stands:
  give them a radius or an allowed area under Serves. A walled but roofless yard is its own room
  and works as before.
- The same alert lists a stand whose allowed area has been deleted, or that stands outside its
  area. It serves nothing until you choose again, rather than falling back to its room, and a
  new area under the old name is not adopted. A gravship landing is the exception: a stand on
  the deck is bound again to the area painted there.
- Fetching building materials, a tree to replant or seeds to plant no longer dresses anyone. A
  builder whose stand serves construction keeps the hardhat on for it, and a gardener whose
  stand serves growing does the same for a tree or seeds, as long as every site the load goes to
  is inside the stand's room, ring or area. The job starts at the stack, so a builder used to
  change for the walk to the stockpile, or change out before it. The hardhat now goes on at the
  frame. A stand serving an allowed area that covers both the stack and the site is the
  exception, and dresses the builder before the first load, unless they rank hauling above
  construction. In pyjamas or another stand's outfit, the outfit stays on only if the whole
  trip, stockpile included, is inside.
- Change back holds at the stand they changed back at, and at any stand serving the same thing
  whose room, radius or area takes that stand in, rather than at every stand in the room. It
  also lifts once they have slept, as well as when they leave.
- The job a colonist was just dressed for no longer sends them straight back to change when its
  target moved while they changed, such as an animal that wandered out of the barn.

### Fixed

- A colonist in pyjamas, or without the armor they parked overnight, carried a meal to a patient
  or a prisoner as they were. They change back first now, unless the whole trip stays inside the
  stand's room. A doctor's, a handler's or a warden's uniform still stays on for it while the
  patient or prisoner is inside the stand's room, ring or area.
- Ticking Fishing did nothing. Odyssey hands fishing out differently from other work, so no
  stand ever saw it. A stand ticked for Fishing now dresses for it, and a fishing hut serving a
  radius that covers the shore was the missing setup.
- With Medical emergencies change first off, a colonist in uniform who needed a doctor urgently,
  with their bed outside the stand's room, went to the stand to change back before lying down.
  They now go straight to bed, and a bedroom stand no longer stops them for sleepwear on the
  way.
- A stand beside a door could dress a colonist for work on the door or in the doorway, such as
  repairing the door, then send them back to change for that same job. A stand now serves only
  work in its own room.
- A colonist in uniform sent to rescue a baby left somewhere unsafe, outside the stand's room,
  stopped at the stand to change first. A rescue now goes straight to the baby.

Every stand still starts on This room.

## [1.4.10] - 2026-10-13

### Changed

- Marked as supporting RimWorld 1.7, so 1.7's mod list no longer shows Shift Change in yellow as
  potentially unsupported. Nothing else changed: the mod is the same as v1.4.9, which already
  runs on 1.7 unchanged.

## [1.4.9] - 2026-10-04

### Fixed

- Feeding a patient or a prisoner changed colonists out of uniform and back. The job starts
  wherever the meal is stored, so a doctor in scrubs changed out before fetching a meal from the
  freezer, then back into scrubs for the next tend, and a warden with a stand in the cell did
  the same. A meal run now leaves the uniform alone in both directions: whoever is wearing one
  keeps it on for the trip, and nobody is dressed for one. That covers feeding patients and sick
  animals, feeding prisoners and delivering their food, and Biotech's two hemogen jobs.
- A colonist whose stand is outside their allowed area is no longer sent to it. They used to be,
  and the trip was called off before they took a step, then tried again at the next job and
  every one after, so they never changed back. On the way in, a stand of their own outside the
  area also beat a shared stand inside it. Stands outside a colonist's allowed area are now
  passed over. Someone already wearing that stand's outfit keeps it on until the stand is back
  inside their area, and the stand's inspect pane says so. Change back still works, since it is
  an order. Better Pawn Control's alert mode can move every colonist's area at once, which is
  the easiest way to run into this.
- Common Sense: with its "haul ingredients over doing bills" setting on, which is how it ships,
  a cook in uniform changed out to fetch an ingredient from outside the kitchen, then changed
  back in for the bill. When the next job in line is the stand's own work in its room, an errand
  slotted in ahead of it is now done in uniform. Vanilla's own opportunistic hauling, which
  picks something up on the way to a bill, lines the jobs up the same way and gets the same
  treatment.
- Pick Up And Haul: a colonist who hauled out of their work room in uniform walked back to the
  stand with full pockets to change before unloading. The unload, and the extra haul that mod
  queues when it spots more nearby, now keep the uniform on, and their next ordinary job changes
  them back.
- The switch on a recreation or sleep stand read "Shift stand (no work here yet)" while the
  stand was working. It now names what the stand dresses for, as the inspect pane always did.
- With several stands selected, each one now shows its own switch and its own Set owner button.
  They used to merge into one button whose click opened a single stand's window, which left the
  other stands as they were while looking as if every one had been set.
- Change back's tooltip said colonists do not change back on their own while the map is under
  threat. They do, at their next job that leaves the room. The button is for when that is not
  soon enough.

## [1.4.8] - 2026-10-01

### Changed

- With Outfit Stands Plus installed, having a colonist selected costs less on a large colony.
  That mod puts an outfit stand button on every colonist, and to find the stands a colonist owns
  it went through every building you had built, every frame the colonist stayed selected. On a
  colony of about 9,000 buildings and 48 stands that came to 0.8 ms per selected colonist per
  frame. It now goes through the colony's outfit stands only, so the cost follows how many
  stands you have rather than how much you have built.
- The button itself works exactly as before, with the same stands in the same order. If a future
  Outfit Stands Plus update changes how it finds them, Shift Change leaves the button to that
  mod.

## [1.4.7] - 2026-09-28

### Changed

- On a stand set to "Not used for shift changes", Set owner now assigns one colonist at a time:
  choosing someone replaces whoever was listed, and Assign all is not shown. A group stand
  switched into that mode keeps its owners for when you switch it back, whether you used the
  switch or unticked its last work type, Recreation or Sleeping. Until you choose one colonist
  there, nobody gets the Outfit Stands Plus button for it.

### Fixed

- With Outfit Stands Plus installed, a colonist could be sent to someone else's stand and take
  the clothes parked there. Each stand kept two owner lists, this mod's and Outfit Stands
  Plus's, and only one of them had a Set owner button at a time. An owner set through Outfit
  Stands Plus before the stand went into shift use stayed on the hidden list and kept that mod's
  "equip outfit" button for the stand, and one click swapped out the new owner's kit. Nothing on
  the stand could show or clear that entry.
- Every stand now has one owner list, set with this mod's Set owner in both modes, and Outfit
  Stands Plus follows it. A colonist who is a stand's only owner gets the Outfit Stands Plus
  button for it, on stands in shift use too, which suits a sleep stand since it holds their own
  clothes. A stand with several owners, or none, gives that button to nobody. Outfit Stands
  Plus's own Set owner no longer appears on these stands, and ours only offers colonists that
  mod would accept, so its adult and child stands stay apart.
- Older saves merge the two lists once, the first time they load. If only one list names anyone,
  that list is kept. If both do and they disagree, the one you could see wins: this mod's on a
  stand in shift use, Outfit Stands Plus's on a stand set to "Not used for shift changes".
  Owners you only ever set through Outfit Stands Plus are kept.

## [1.4.6] - 2026-09-23

### Added

- New setting, "Medical emergencies change first", off by default. Turn it on and the rule above
  goes away for medical work: a doctor answering an emergency tend puts scrubs on first, and a
  colonist in critical condition stops for the gown. Fires are never covered by it, whatever it
  is set to. It applies in both directions, so a doctor already in another room's uniform may
  change out of that before answering as well.

### Fixed

- A stand ticked for "Resting in bed" never dressed anybody. Both patient rows in the work list
  have done nothing since sleepwear arrived in v1.3.0, so a hospital stand set up exactly the
  way the description says was inert, with nothing on screen to say so. Reported by a player who
  had read the notes and configured it correctly.
- Medical bed rest is a work type like any other again. Put a stand in the hospital, tick
  "Resting in bed", stock it with gowns, and a colonist going to bed to recuperate changes there
  on the way. Sleeping is still the separate row for pyjamas, and neither row takes the other's
  jobs.
- A colonist who still needs a doctor goes straight to bed. The gown is for recuperating, so
  bleeding, waiting on surgery or in labour means no wardrobe stop on the way: someone wounded
  in a raid reaches the bed first and changes on their next trip, once a doctor has seen them.
  Nobody is pulled out of a sickbed to change, either.

Both work the same with FrozenSnowFox's Complex Jobs installed. That mod moves a lot of vanilla
work onto its own finer-grained job types, which is why a hospital stand there arms for Nurse
and Surgeon as well as Doctor, but it leaves bed rest alone and leaves emergency tending under
Doctor. Nothing to configure differently either way.

## [1.4.5] - 2026-09-21

### Fixed

- A colonist required at a ritual that began the moment they were called could spend it walking
  between the ceremony and their wardrobe. Stands change a colonist out of uniform when a job
  takes them out of the room, and a ritual looked like an ordinary job. It is not one: the
  ceremony can call them back mid-change, and the stand would send them off again. It also
  needed the leader to arrive before the colonist had finished changing, so a wardrobe across
  the base did it and one next door did not. Colonists held by a ritual, ceremony, party or
  caravan are now left alone. They attend in what they have on and change back afterwards,
  including the ones who used to make it in time.

## [1.4.4] - 2026-09-19

### Fixed

- A colonist changed at a stand walked off and put on a duplicate of the clothing that stand was
  holding for them. A stand keeps a colonist's own kit while they are in uniform, and hides it
  from anything that goes looking for clothes, so a hauler cannot carry it off mid-shift. Mods
  that fetch clothing for a colonist read that hidden slot as an empty one and sent them to
  storage for a replacement, which came straight back off again when they changed out. Power
  armour made it obvious, since the colonist paid its equip time twice and finished where they
  started. A colonist is no longer sent after a garment their own stand is already holding. If
  that parked garment is worn through, they still go and find a better one. Nothing changes
  without a mod that dresses colonists for you.

## [1.4.3] - 2026-09-18

### Removed

- The mod no longer ships its own test suite. It is developer tooling and does not belong in a
  player's install. Every rule about which stand dresses whom, and when, is unchanged.

## [1.4.2] - 2026-09-17

### Fixed

- A colonist who woke with a meal already in their pack ate breakfast in their sleepwear and
  changed back only afterwards. Eating is normally left alone when the food is already on the
  colonist, because on a work shift they are standing at their bench and a trip to the wardrobe
  buys nothing. A bedroom stand is the opposite case. They wake up beside it, so changing costs
  no walking at all, and skipping it sent them across the base in nightclothes and back again to
  change. Waking now changes them back before they go and eat, whether the meal is in a
  stockpile or in their own pack. Work and recreation stands behave exactly as before.

## [1.4.1] - 2026-09-15

### Fixed

- A plutonium processor or reactor core built into a wall could resolve to the room on the wrong
  side of it. A building like that belongs to no room of its own, so the mod reads the cells
  around it, and when those fell evenly between two rooms it took whichever the game looked at
  first, which was always the southern or eastern side. The same build mirrored north to south
  quietly stopped working. An enclosed room now beats the outdoors, and a processor that
  genuinely faces two rooms equally arms neither rather than being right half the time.
- A room holding both the nuclear machining table and the Rimatomics research bench armed for
  only one of them. A room takes one role and a research bench outscores a machining table, so
  the room came out a laboratory and the table's smithing work dressed nobody. Both benches are
  now read from the room's contents as well as from its role, so one room can hold both and arm
  for both.
- The nuclear work type still read "Loading" for anyone not playing in English. The clearer name
  was there, but the mod only looked for it in the active language and it ships in English, so
  every other language fell back to the old unfindable label. It now falls back to the English
  one instead.
- Loading a missile silo or a railgun magazine looked beside the silo rather than beside the
  ammunition. Those jobs are built the same way round as the fuel loaders and the colonist walks
  to the round first. Both are hauling work, which no room turns on by itself, so it only shows
  on a stand you ticked for hauling by hand.
- The setup note was wrong and is corrected. A stand in the reactor hall does not cover fetching
  fuel, because a colonist changes where the job begins and a fuel run begins at the rods. Put a
  stand where the rods are kept as well as one in the hall.

## [1.4.0] - 2026-09-15

### Added

- Works with Dubs Rimatomics. A stand arms itself in any room holding a reactor core, the
  plutonium processor or the spent fuel pool. Stock it with rad suits and there is nothing to
  configure. Put one where the rods are kept as well as one in the reactor hall, because a
  colonist changes where the job begins, and a fuel run begins at the fuel rather than at the
  core.
- Reactor halls needed a new way of being recognised. Rimatomics gives none of its buildings a
  room role, so the game scores a reactor hall exactly the way it scores an empty box, and a
  stand keyed to the room's role had nothing to go on. A stand now also reads what is standing
  in the room with it, which is how it finds the reactor cores, the plutonium processor and the
  spent fuel pool. This is the first time a stand can turn itself on without a room role, and it
  is the part of this release most worth telling you about.
- Nuclear rooms turn themselves on for research as well as for fuel work. The steps you run at a
  research reactor or a plutonium processor are the ones that can spring a radiation leak, with
  your researcher standing at the thing when it does, so a stand in those rooms now dresses them
  for it without you ticking anything. A spent fuel pool arms for fuel work alone, and a weapons
  bench for research alone, because that is what happens in each.
- The nuclear machining table and the Rimatomics research bench now make their rooms count as a
  workshop and a laboratory, which is what the same two benches from any other mod already did.
  Give them a room each if you want both: a room holding one of each scores as a laboratory, so
  the smithing that the machining table takes will not arm there.
- Fetching chemfuel for the plutonium processor does not trigger a change, on purpose. Chemfuel
  is inert, and it lives wherever your colony stores chemfuel rather than beside a reactor, so
  treating that haul as nuclear work meant a trip to a wardrobe before a walk across the map, or
  two of them if the colonist was already suited. They keep whatever they are wearing and get on
  with it. Carrying the spent rods themselves still changes them, at the rods.
- Working the reactor console is deliberately not covered. It is a right click order rather than
  a job, with no work type behind it, and a shift change never interrupts an order you gave. In
  practice whoever loaded the fuel is already wearing the suit when you send them to the
  console.

### Changed

- The nuclear work type is listed as "Nuclear loading". It was always in the list, but under the
  name "Loading", because Rimatomics calls it Nuclear in the work tab and Loading in the field a
  list like this one reads from. The row worked; it was just under a name nobody hunting for
  "Nuclear" would find, which is easy to read as the mod not supporting Rimatomics at all.
- No setting moved. One behaviour changed for every game, Rimatomics or not: a colonist on shift
  who mines a rock face or repairs a wall at the edge of their own room now stays in that room's
  gear, where before they changed out of it first.

### Fixed

- Fetching fuel rods looked in the wrong room. Those jobs name the reactor core as the
  destination and the rod as the thing being carried, and the colonist walks to the rod first,
  so a stand beside the storage pool is the one that should fire. It was looking beside the core
  instead, and a colonist would carry a rod the length of the base with no suit on. Stands
  beside the pool now count.

## [1.3.6] - 2026-09-11

### Fixed

- On a high UI scale, a stand's work-type window drew its lower half outside itself. The trigger
  rows and the whole work grid were missing, the window kept the space it had reserved for them,
  and there was no error anywhere to explain it. If you have ever opened that window and found a
  blank panel under the heading, this was why, and it is why ticking a work type was impossible
  rather than merely awkward.
- It depended on your screen, not your mods. What matters is display height divided by UI scale:
  1920x1080 at 3x, a 1366x768 laptop at 2x, or anything windowed smaller. Below roughly 460 of
  those units the rows above the grid needed more room than the window had, and RimWorld's own
  list widget responds to that by starting a second column, which for a full-width list begins
  past the edge of the window. Everything from that row on was drawn where nobody could see it.
- The window now scrolls as a whole when it does not fit, so nothing can be pushed out of it. On
  a screen where it does fit, the work grid no longer sits in a scrolling panel of its own,
  which means the window has no clipped area in it at all. Checked at four sizes down to
  480x270, the smallest RimWorld's own UI scale settings allow.

Nothing about which work a stand serves has changed, and no setting moved. If the window has
always looked right for you, it still does.

## [1.3.5] - 2026-09-11

### Changed

- A stand's work-type list now tells you when there is nothing in it, instead of showing an
  empty panel under the heading. Mods that rearrange work categories can take every entry out of
  it: Personal Work Categories switches a category off as soon as its preset has no work givers
  left in that column, and the same category leaves RimWorld's own Work tab at that moment. The
  blank panel read as this window having failed, which is the wrong place to go looking.
- That is also the check to run if categories go missing one at a time. Open the Work tab: a
  category that is gone from there as well was hidden by a work-category mod, and the stand list
  is reporting the game rather than losing track of it. Complex Jobs on its own does not do
  this, and neither does XML Extensions; both move work between categories without emptying any
  of them.
- The mod description, several tooltips and the workshop page were rewritten to one voice, and
  three claims in them were wrong. The decency guard ships switched off rather than holding
  garments back until you allow them, Anomaly is where vanilla's only lab coat comes from, and
  Deposit only is the setting that parks armor on a stand that hands out nothing in return.

Nothing about which work a stand serves has changed. A stand whose list you set by hand keeps
the list you gave it, stands left on automatic still follow their room, and a category that
returns to the Work tab returns here with it.

## [1.3.4] - 2026-09-10

### Changed

- Shift Change no longer walks through every other mod's buttons when it has nothing of its own
  to add. Its change-back button is appended to the list of buttons RimWorld builds for each
  selected colonist, and that list is rebuilt every frame; the old version enumerated the whole
  list, other mods' buttons included, before deciding whether to add anything at all. It now
  checks the colonist first and steps aside when they are not wearing a stand's outfit, which is
  nearly always.
- You will not notice this, and it would be dishonest to suggest otherwise. The work removed
  comes to a few microseconds per frame even with a full squad selected, far below anything you
  could see. It is worth fixing because it sat on a path the game runs constantly while doing
  nothing there for most colonists, not because it was costing anyone frames.
- The same change applies to the "Allow removing items" toggle on stands that Shift Change does
  not govern. A stand you have set to "Not used for shift changes" is now left alone entirely,
  rather than inspected every time it is selected.

Nothing about how stands behave has changed. The change-back button appears exactly when it did
before, including while the safety latch that stops automatic changing has tripped, and the
toggle is still held off on stands that are in service. Three new cases in the release gate
cover both, so a future cleanup cannot quietly undo it.

## [1.3.3] - 2026-09-09

### Added

- Stands now dress for the work types added by FrozenSnowFox's Complex Jobs. That mod splits
  vanilla's work into finer pieces and moves the tasks across: surgery leaves Doctor for
  Surgeon, feeding and rescuing patients leave it for Nurse, butchering leaves Cooking for
  Butcher, training leaves Handling for Train, drug synthesis leaves Crafting for Drugs, and
  stonecutting, smelting, machining, fabrication and refining each get a work type of their own.
  Shift Change was matching only the vanilla names, so a stand served whatever stayed behind and
  ignored everything that moved.
- That is why it read as unreliable rather than broken. A hospital stand still dressed a doctor
  to tend a patient, then sat there when the same colonist went to operate. A kitchen stand
  caught cooking and not butchering. A barn stand caught milking and shearing but not training.
- What each room picks up: hospital adds Nurse and Surgeon, laboratory adds Drugs, kitchen adds
  Butcher, workshop adds Stone Cut, Smelt, Machining, Fabricate, Refine and Production, barn
  adds Train and slaughter.
- Slaughter is a separate work type only if you turn it on in Complex Jobs' own settings, which
  needs XML Extensions. Taming is not covered, and was not before this release either: a stand
  is chosen by where the job happens, and taming happens wherever the wild animal is standing
  rather than in the barn.

Nothing changes if you do not run Complex Jobs. A stand serves the same vanilla work types it
served in 1.3.2, and a stand whose work list you set by hand keeps exactly the list you gave it.
Only stands left on automatic pick up the additions.

## [1.3.2] - 2026-09-08

### Fixed

- A stand set to Deposit only is no longer somewhere haulers deliver clothing. Outfit stands sit
  above stockpiles in the storage order, so an empty one had colonists carrying spare apparel to
  it from across the base, and the slots that filled were the ones its owner needed for their
  own gear. With Allow removing items held off while a stand is in service, nothing that arrived
  that way could leave again.
- It hit some stands and not others, which is what made it hard to place. A stand that already
  holds an outfit refuses further deliveries, so among stands set up identically, the
  deposit-only ones collected clothing and the rest did not. Deposit only is the one mode whose
  normal state is an empty stand.
- You can still put a garment on such a stand yourself: right-click the apparel and target the
  stand, as before. Only automatic hauling stopped. The sleep change deposits and returns
  exactly as it did.
- A stand that haulers already filled clears itself. At its owner's next change the deposit
  pushes aside whatever is in the way, and that garment now goes to a stockpile rather than
  straight back onto the stand.

## [1.3.1] - 2026-09-08

### Added

- New mod setting, Keep colonists decent, off by default. Turn it on and a stand will not leave
  a colonist without basic clothing: if what it hands out does not cover them, their innermost
  garments stay on and the rest of the swap happens as normal. The innermost layers are the ones
  kept, so a shirt and trousers stay put while a parka or a duster goes into storage.
- It is off by default on purpose. Nothing changes in a colony you are already playing unless
  you switch it on, which matters most if you got this mod as part of a pack.
- It shows up mainly with Change the whole outfit ticked. That setting takes off everything the
  colonist arrived in, so a stand holding only a hat, a belt or a pair of trousers sends them
  out with nothing else on. With the new setting enabled they keep what they need instead.
- What gets held back depends on the colonist. RimWorld asks for trousers on everyone and a
  covered torso as well on women, so the same stand with the same setting can keep a shirt on
  one colonist and not on another.
- Nudists, and anyone whose ideoligion prefers nudity, are never affected. For them being
  dressed is the penalty, so nothing is held back whatever the setting says. This follows the
  same rule the game uses when deciding whether to put clothes on a prisoner.

## [1.3.0] - 2026-09-05

### Added

- Stands now dress for bed, alongside work and recreation. Tick Sleeping on a stand and whoever
  comes to that room to turn in changes on the way to bed, then changes back in the morning. A
  bedroom switches the row on by itself. A barracks does not and has to be ticked by hand,
  because a stand serves one colonist at a time: in a ten-bunk barracks the first sleeper claims
  it and the other nine go to bed in what they were already wearing. Sleeping, recreation and
  work types stay exclusive, so ticking one clears the other two.
- Deposit only is the setting for a rack beside the bed, and it is off by default. The stand
  issues nothing. A colonist heading for bed parks whatever its storage filter accepts, keeps
  the rest of their clothes on, and collects it in the morning. That is what gets a marine's
  power armour onto a mending stand overnight, with no pyjamas on the rack to swap into.
- Narrow the storage filter on a deposit-only stand. A new stand accepts nearly all apparel, so
  an untouched filter takes almost everything a colonist is wearing. One case is refused
  outright: if what the filter would take leaves a colonist without basic clothing, that
  colonist is skipped and the stand does nothing for them. A soldier who wears nothing under
  their armour needs clothes underneath, or pyjamas on the rack, before deposit only moves
  anything.
- Resting in bed is still a work type, and it is not what the Sleeping row does. That one is
  vanilla's bed rest for recuperating from an injury or illness, and a doctor's orders are what
  put a colonist in bed under it. The Sleeping row fires on ordinary bedtime. A stand set to
  Sleeping ignores medical bed rest; a stand carrying the work type ignores ordinary bedtime.

### Changed

- A stand whose trigger comes from its room now shows the work-type list, with a note naming the
  room as the source. Ticking a work type overrides it. That list used to be hidden on those
  stands, so putting a bedroom or rec-room stand onto a work type meant switching it off first
  and starting again.

### Fixed

- Colonists could be pulled out of bed to change for recreation. Vanilla gives bedridden
  colonists joy they can take lying down so they stay put, and the guard against interrupting
  that asked whether the colonist was in bed. That question reads the colonist's current job,
  and the game clears the current job before handing over the new one, so the answer came back
  no every time. The guard never fired. It now looks for a bed on the cell the colonist is
  standing on.

## [1.2.4] - 2026-08-31

### Changed

- Colonists wearing a stand uniform now change back out of it while a threat is on the map.
  Before this, any threat froze shift changes in both directions, so anyone already in uniform
  stayed in it.
- Changing INTO a uniform is still blocked while there is a threat, which is the half worth
  keeping. Nobody should walk across the base to put on whites in the middle of a firefight.
- Nothing reacts to a raid arriving. A raid does not send anyone to a stand and it does not pull
  anyone out of one. Colonists change back on their own next job, the way they always have, and
  the Change back button is still there for when you want it now rather than then. Drafted
  colonists, right-click orders and emergency work are untouched, as before.

## [1.2.3] - 2026-08-24

### Added

- Stands can now be kept out of trade windows, and every stand starts that way, in existing
  saves as well as new ones. Traders would otherwise buy anything sitting on an outfit stand:
  the uniform, and the owner's own clothes parked there while they are on shift. That is vanilla
  behaviour, it reached caravans at the gate and ships in orbit alike, and nothing about the
  stand prevented it. Allow removing items never covered trade, and neither did an assigned
  owner.
- Nothing else about the stand changes. Colonists still stock it, shift changes still work, and
  you can still load the kit into a caravan yourself. The switch sits in the same panel as
  Change the whole outfit; untick it for a stand you keep as a shop shelf. A stand set to Not
  used for shift changes stays tradeable either way.

## [1.2.2] - 2026-08-23

### Fixed

- Moving a stand keeps its owner. Reinstalling one anywhere else used to clear the owner without
  saying so, leaving what looked like an owned stand open to the whole colony. Its work types,
  its change-the-whole-outfit setting and the Allow removing items lock all come through the
  move as well.
- Moving a stand while somebody has its uniform out no longer strands their clothes. Their own
  clothes travel inside the crate with the stand, and when you set it back down they can change
  back exactly as before. Previously the stand came out of the crate treating those clothes as
  part of its own outfit, so the colonist stayed in the uniform for good and their own clothes
  became stand stock.
- A colonist is never locked into a uniform by a stand that does not come back. While a stand is
  packed up, the uniform stops counting as forced, so the usual clothing rules can replace it if
  the crate is sold, burnt or simply left in storage. Setting the stand back down restores the
  shift as it stood.

## [1.2.1] - 2026-08-23

### Changed

- Allow removing items is now held off on every stand in service, including stands that already
  had it switched on. A stand that reached service with it on stayed open to every colonist's
  outfit optimizer, which could take the uniform and wear it as everyday clothes, and an
  assigned owner never prevented that. The setting is now cleared when a save loads and again
  whenever a stand goes back into service, so a stand cannot be left exposed by accident. The
  automated suite that runs before every release covers this now, in both directions.
- The toggle's description says something useful about getting things out again. To take a
  garment back, use the eject button beside it in the stand's Contents tab, which drops it next
  to the stand. The old text told you to set the stand aside to unlock the toggle, which stopped
  being true the moment you put the stand back to work.
- Reworded the in-game text after feedback on the writing style. Same information, plainer
  punctuation.

## [1.2.0] - 2026-08-22

### Added

- Stands now dress for recreation as well as work. Tick Recreation on a stand and whoever comes
  to that room to enjoy themselves changes on the way in and back on the way out: a robe for the
  sauna, black tie for the games room. A room the game scores as a rec room turns it on by
  itself; a throne room, a dining room or a pool does not, and takes the switch by hand.
  Recreation and work types are mutually exclusive on a stand, because one outfit serves one
  purpose.
- Drinking, drug-taking and reading are not caught, and a recreation stand in open ground is
  refused for now. Every outdoor cell belongs to one enormous room, so it would dress colonists
  for every walk on the map. Serving open ground needs a boundary of its own, and that is
  planned.
- Set owners now takes a list rather than a single colonist, with All / Men / Women filter tabs
  and an Assign all button. This matters most for gendered clothing: a stand serves anyone who
  can wear something on it, so a shared stand holding a prestige robe, a ladies hat and a formal
  shirt will put a man in the robe and leave the hat. Two clicks make it the women's stand,
  whether the colony has four colonists or forty.
- Change the whole outfit, off by default, is the difference between a lab coat worn over
  ordinary clothes and a sauna robe worn instead of them. Ticked, the colonist undresses
  completely and wears only what the stand holds. They lose whatever their own clothes were
  providing, warmth included, which is worth a thought before putting a robe rack in a cold
  biome.

## [1.1.0] - 2026-08-19

### Added

- Outfit Stands Plus' mechanized and mending stands now work as shift stands. Shift changes run
  at their boosted swap speeds, and the mending stand repairs a borrower's parked clothes while
  they are on shift.
- One Set owner per stand when Outfit Stands Plus is installed: a stand in service for shift
  changes shows this mod's owner control, and a stand set to Not used for shift changes shows
  theirs. Each mod keeps its own assignments either way.

### Changed

- The work-type button now reads its state on its face, such as "Shift stand: doctoring" or "Not
  used for shift changes", and its dialog names what the game currently reads the room as. That
  explains the odd cases: a hospital with a crib in it stops dressing doctors because the game
  calls that room a barracks.
- Allow removing items holds itself off while a stand is in service: turned on, the stand's
  contents are exposed to every colonist's outfit optimizer, and owning the stand does not
  protect them. A stand already on stays switchable, and setting Not used for shift changes
  unlocks the toggle for decommissioning.

## [1.0.2] - 2026-08-18

### Fixed

- Compatible with Outfit Stands Plus, in either load order. Before this update, loading Outfit
  Stands Plus before Shift Change stripped its components from the outfit stand, and selecting
  any colonist or stand would then throw errors. Both mods now patch the stand cooperatively,
  and each mod's Set owner keeps its own assignment.
- Stand ownership is now stored under Shift Change's own save keys instead of the game's generic
  ones, so assignments no longer cross over between the two mods on save and load. Existing
  saves convert automatically the first time they load.

## [1.0.1] - 2026-08-17

### Changed

- The outfit stand's **Allow removing items** toggle now describes its effect with Shift Change
  installed. It does not affect shift changes: stands are stocked, and uniforms taken, with it
  off. While it is on, the stand's contents are visible to every colonist's apparel optimizer,
  which may take a uniform and wear it as ordinary clothing; assigning an owner does not prevent
  this. Off is the default and the correct setting, and colonists removing clothes from an
  assigned stand is this toggle rather than a fault.

## [1.0.0] - 2026-08-14

The first release. It needs Odyssey, for the outfit stand, and Harmony.

### Added

- Colonists change into the outfit on a vanilla outfit stand when they take on automatic work
  of their room's kind, such as doctoring in the hospital, research or drug synthesis in the
  lab, or cooking in the kitchen. They change back when their work takes them elsewhere, and
  their own clothes wait in the stand and come back as they were, force-worn markers included.
- Rooms are read from their role, so hospitals, laboratories, kitchens, workshops and barns
  need no setup.
- Set owner reserves a stand for one colonist. An unassigned stand is shared by whoever is free
  to use it, like a bed, so a kitchen needs one stand per cook working at once, not one per cook.
- Set work types opens a checklist for a room the game reads oddly, a room that does two jobs,
  or a decorative stand you do not want used.
- Automatic work only. A direct order never sends a colonist across the base to change first,
  and a colonist already in uniform keeps it on for one.
- Emergencies are never delayed, and nothing happens while the map is under threat.
- Nobody takes clothes out of a stand somebody else is using, and passing through a room
  changes nothing: only doing the room's work does.
- A meal break takes a colonist out of uniform first, unless the food is already in their hands
  or their pack.
- When a stand frees up while someone is already working in its room out of uniform, they step
  over and change, unless they are treating a patient.
- A Change back button on any colonist in uniform takes them out of it at once.
- One setting, Unassigned stands are shared, on by default.

[Unreleased]: https://github.com/beverage/shift-change/compare/v1.5.3...HEAD
[1.5.3]: https://github.com/beverage/shift-change/compare/v1.5.2...v1.5.3
[1.5.2]: https://github.com/beverage/shift-change/compare/v1.5.1...v1.5.2
[1.5.1]: https://github.com/beverage/shift-change/compare/v1.5.0...v1.5.1
[1.5.0]: https://github.com/beverage/shift-change/compare/v1.4.10...v1.5.0
[1.4.10]: https://github.com/beverage/shift-change/compare/v1.4.9...v1.4.10
[1.4.9]: https://github.com/beverage/shift-change/compare/v1.4.8...v1.4.9
[1.4.8]: https://github.com/beverage/shift-change/compare/v1.4.7...v1.4.8
[1.4.7]: https://github.com/beverage/shift-change/compare/v1.4.6...v1.4.7
[1.4.6]: https://github.com/beverage/shift-change/compare/v1.4.5...v1.4.6
[1.4.5]: https://github.com/beverage/shift-change/compare/v1.4.4...v1.4.5
[1.4.4]: https://github.com/beverage/shift-change/compare/v1.4.3...v1.4.4
[1.4.3]: https://github.com/beverage/shift-change/compare/v1.4.2...v1.4.3
[1.4.2]: https://github.com/beverage/shift-change/compare/v1.4.1...v1.4.2
[1.4.1]: https://github.com/beverage/shift-change/compare/v1.4.0...v1.4.1
[1.4.0]: https://github.com/beverage/shift-change/compare/v1.3.6...v1.4.0
[1.3.6]: https://github.com/beverage/shift-change/compare/v1.3.5...v1.3.6
[1.3.5]: https://github.com/beverage/shift-change/compare/v1.3.4...v1.3.5
[1.3.4]: https://github.com/beverage/shift-change/compare/v1.3.3...v1.3.4
[1.3.3]: https://github.com/beverage/shift-change/compare/v1.3.2...v1.3.3
[1.3.2]: https://github.com/beverage/shift-change/compare/v1.3.1...v1.3.2
[1.3.1]: https://github.com/beverage/shift-change/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/beverage/shift-change/compare/v1.2.4...v1.3.0
[1.2.4]: https://github.com/beverage/shift-change/compare/v1.2.3...v1.2.4
[1.2.3]: https://github.com/beverage/shift-change/compare/v1.2.2...v1.2.3
[1.2.2]: https://github.com/beverage/shift-change/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/beverage/shift-change/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/beverage/shift-change/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/beverage/shift-change/compare/v1.0.2...v1.1.0
[1.0.2]: https://github.com/beverage/shift-change/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/beverage/shift-change/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/beverage/shift-change/releases/tag/v1.0.0
