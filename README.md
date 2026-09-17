# Shift Change

> **Requires RimWorld 1.6, the Odyssey expansion, and Harmony.** The outfit
> stand this mod builds on is Odyssey content, so without it the mod does
> nothing at all.

A RimWorld mod.

In a game with so many choices of arms, armor, and apparel, why limit yourself
to just one set for all jobs? There is different gear best for work, play,
combat - use all of them!

With Shift Change, colonists can change into the right clothes for the room
they are going to, whether they are there to work, to enjoy themselves, or to
sleep, and back out again afterwards, using the vanilla outfit stand.

Put an outfit stand in a room and put an outfit on it. Each stand dresses for
one of three things:

Your doctor operates in combat armor. It's always been on, and never taken
off.

**Work.** A colonist taking on **automatic** work of that room's kind
(doctoring in the hospital, researching or synthesizing drugs in the lab,
cooking in the kitchen) changes into the stand's outfit before starting, and
changes back when their work takes them elsewhere.

Somewhere in your stockpile is probably not a set of formal wear that nobody
has ever put on. So go make some, and put them in the rec room.

**Recreation.** A stand set to recreation dresses anyone who arrives in that
room to enjoy themselves, and changes them back when they leave.

Your space marine has been sleeping in their cataphract armor since the day
you got it.

**Rest.** A stand in the bedroom hands out whatever is on it at bedtime and
holds their day clothes until morning. Set it to **Deposit only** and it
issues nothing at all: the armor goes onto the rack on the way to bed and
comes back in the morning.

Their own clothes wait in the stand every time, and come back exactly as they
were, force-worn markers included.

![A doctor, researcher and cook changing into work clothes and back again](media/demo.gif)

*Three colonists arrive for work in their own clothes, change into what their
room's stand holds, and change back on the way to dinner. Each stand keeps its
borrower's civvies while they are on shift. Sped up 3–6×.*

## How to use it

1. **Build a vanilla outfit stand** (Odyssey) in a hospital, laboratory,
   kitchen, workshop, barn or rec room, and put an outfit on it.
2. That's it, for the common case. The stand reads its room and dresses
   whoever comes to work or to relax there.
3. Optional, per stand:
   - **Shared (set owners)** restricts the stand to the colonists you list.
     Name one and it is their personal kit, off-limits to everyone else; name
     several and it serves that group and nobody outside it. Left unassigned,
     the stand is **shared**: any capable colonist may use whichever stand is
     free, like beds. A kitchen needs one stand per cook working at the
     same time, not one stand per cook.
   - **The stand's switch**, labeled with what it currently dresses for
     ("Shift stand: doctoring", "Shift stand: recreation", "Not used for
     shift changes"), opens the checklist when the room's reading isn't what
     you want: a multi-purpose room, a room the game scores oddly, a place
     the game doesn't score as recreation at all, or a stand you don't want
     used. The dialog names what the game currently reads the room as, which
     explains the surprises: a crib in a hospital, for instance, makes the
     room a barracks as far as the game is concerned, and the stand goes idle
     until the checklist says otherwise.

**"Change the whole outfit" is off by default, and belongs to the rack rather
than the room.** Normally the kit goes on over whatever it doesn't conflict
with, so a lab coat sits over ordinary clothes. Tick this and the colonist
undresses completely and wears only what the stand holds. That is right for a
sauna robe, wrong for a lab coat. It costs the equip time of every
garment in both directions, and everything their own clothes were providing
comes off with them: warmth, armor, any bonuses those garments carried. They
have exactly what the stand holds and nothing more, which is worth a thought
before putting a robe rack in a cold biome.

A stand holding nothing they can wear still won't undress them.

**Keep colonists decent**, in the mod settings, is off by default and changes
that. Enable it and a stand will not leave a colonist without basic clothing:
if what it hands out does not cover them, their innermost garments stay on and
the rest of the swap happens as normal. RimWorld asks for trousers on everyone
and a covered torso as well on women, so the same stand can hold a shirt back
for one colonist and not for another.

It ships off so that updating the mod does not change how a colony you are
already playing behaves. This arrives in mod packs, where nobody reads a
change note. Turn it on if you would rather a stand never left anyone indecent.

**Nudists and nudist ideoligions are exempt either way.** For them being dressed
is the penalty, so nothing is ever held back and the stand does what it was set
to do.

**"Allow removing items" is held off while a stand is in service.**
Shift changes never need it, and turning it on hands the stand's contents to
every colonist's outfit optimizer, which may take the uniform and wear it as
everyday clothes, and assigning an owner does not prevent that. The stand
clears it on load and again whenever it goes back into service, so a stand
switched on while it was set aside does not stay that way. To take one garment
back, use the eject button beside it in the Contents tab, which drops it next
to the stand. To decommission a stand and let haulers empty it, set "Not used
for shift changes" first; the toggle unlocks with it and stays unlocked.

**"Keep contents out of trade" is on by default.** Traders will otherwise buy
anything sitting on an outfit stand: the uniform, and the owner's own clothes
parked there while they're on shift. That's vanilla behavior, it reaches both
caravans at the gate and ships in orbit, and nothing else about the stand
prevents it. "Allow removing items" doesn't cover trade, and neither does an
assigned owner. With this on, the stand's contents never reach a trade window,
and nothing else changes: colonists still stock it, shift changes still work,
and you can still load the kit into a caravan yourself. Turn it off for a stand
you keep as a shop shelf; a stand set to "Not used for shift changes" is
tradeable either way.

A stand that is dressing anyone says so in its inspect pane: what it dresses
for, who owns or is currently wearing it, and whether it's empty. A stand in a
room with no role says nothing at all, and behaves like ordinary furniture.

## Dressing for recreation

Tick **Recreation: any joy activity in this room** on a stand and it dresses
whoever comes to that room to enjoy themselves. Everything downstream is the
same: change on the way in, change back on the way out, own clothes waiting in
the stand.

![Eight guests arrive in work clothes, change at the stands, and settle in to play in evening dress](media/demo-recroom.gif)

One switch covers all of it, because the room is the selector. A robe stand
dresses for the sauna by standing in the sauna. RimWorld's joy kinds are no
help (a single kind spans prayer, stargazing, building snowmen and visiting a
grave), so a list of activities would only offer you categories your rooms do
not have.

Recreation and work types are mutually exclusive on a stand, because a stand
holds one outfit. Tick recreation and the work
checklist goes away; tick any work type and recreation drops. A room that does
both wants two stands.

**Gendered clothing wants an owner list.** A stand serves anyone who can wear
*something* on it. Fine for a lab coat, a trap for a gown: put a prestige robe,
a ladies hat and a formal shirt on a shared stand, and a man will take the robe
and leave the hat. Of Royalty's formal wear the vest and top hat are male, the
ladies hat is female, and the robe and formal shirt are neither.

Owner lists exist for this. **All / Men / Women** filter tabs, an **Assign all**
button, and a stand becomes the women's stand in two clicks, with four
colonists or forty. Whoever is on the list is who it serves.

A stand configures itself from the room here exactly as it does for work: a
hospital gives it doctoring, a kitchen gives it cooking. Only a room scoring as
a rec room turns recreation on by itself, though, and plenty of places people
obviously go to enjoy themselves do not. A throne room scores as a throne room.
A dining room with a chess table in the corner is still a dining room. A room
doing two jobs resolves to whichever one wins, and a pool usually scores as
nothing at all, since swimming happens on terrain rather than furniture. Set
those by hand, once, and they behave like any other recreation stand.

This is also where **Change the whole outfit** earns its keep. A lab coat goes
over ordinary clothes; a sauna robe does not.

### What it deliberately will not do

- **Drinking and drug-taking are invisible to it.** Fetching a beer runs the
  same job whether it ends at a bar or in a corridor, and nothing in that job
  says recreation, so nothing dresses anyone for it. Sitting down to socialize
  at a table or a counter *is* caught, and the drink comes along.
- **Reading is left alone.** A colonist picks their reading spot after setting
  off. The only room known at the start is wherever the book sits on a shelf,
  and dressing them for the library because that is where the novel lives would
  be the wrong room.
- **Outdoors is excluded, for now.** Every outdoor cell on the map belongs to
  the same map-spanning room, so a recreation stand in open ground would dress
  colonists for every walk and every bit of stargazing anywhere on the map. A
  walled but roofless yard is its own room and still counts. Serving open
  ground needs a boundary of its own: a zone, or a radius around the stand.
  That is planned.
- **Nobody is pulled out of bed.** Vanilla hands patients recreation they can
  take lying down precisely so they stay put.
- **A drink from the rec room's own stock does not end the visit.** The
  meal-break rule below is a work-room rule, and a rec room stocks its own bar.

## Dressing for sleep

Tick **Sleeping: going to bed in this room** and the stand dresses whoever
turns in there. A stand in a bedroom does it by itself, because the room's own
role is enough: pyjamas on a rack beside the bed is the whole setup.

![Two soldiers in prestige cataphract turn in; one parks her armor on a deposit-only rack, the other swaps his for a duster and helmet](media/demo-sleep.gif)

*Two bedrooms off one corridor. On the left the stand holds nothing and is set
to Deposit only, so it takes the armor in and issues nothing; on the right it holds a light kit and does a swap. Both
collect their gear again in the morning. Sped up 8× through the walk and the
changes.*

A stand with an outfit on it swaps, exactly as it does for work. **Deposit
only, issue no outfit** does the other half: the stand hands out nothing and
takes in whatever its storage filter accepts, so a marine parks their power
armor by the bed and sleeps in what was underneath.

**The storage filter is the control on a deposit-only stand**, and it wants
narrowing. A newly built stand already accepts nearly all clothing, so set it
to the pieces you actually want parked (armor and helmet, say) and leave the
rest out.

It will not send anyone to bed naked. If what the filter would take leaves a
colonist without basic clothing, the stand is skipped for that colonist
entirely. A soldier who wears nothing under their armor therefore needs
something underneath before a deposit-only stand will do anything for them.

**"Resting in bed" in the work list is not this.** That is vanilla's own work
type, for a colonist recovering from injury or illness, and it behaves like any
other work type. Ordinary sleep is the **Sleeping** row. Tick the work type for
a hospital gown; tick Sleeping for pyjamas.

Bedrooms turn this on by themselves. Barracks deliberately do not: a stand
serves one colonist at a time, so in a ten-bunk barracks the first sleeper to
reach it would hold it all night and the other nine would go to bed in what
they were already wearing. A shared sleeping room takes the switch by hand.

## The rules it follows

- **Automatic only**, for work, recreation and sleeping alike. Right-click orders
  execute immediately, in both directions: a doctor ordered to tend *right
  now* goes straight there, and a pawn in uniform given a direct order keeps
  it on and returns it later.
- **Emergencies are never delayed.** A colonist bleeding out is not kept
  waiting for a wardrobe trip.
- **Nobody changes into a uniform while the map is under threat.** A colonist
  already wearing one still changes back out of it, on their own next job.
  Getting dressed is a trip nobody should make in a firefight; getting changed
  back is a colonist heading toward their own gear, which on a stand set to
  change the whole outfit is where their armor is.
- **Personal kit stays personal.** A stand with owners serves only them,
  nobody takes clothes out of a stand someone else is using, and whoever
  checked a uniform out is whom it goes back to.
- Passing through a room changes nothing; only doing the room's work does.
- **A meal break gets them out of uniform first**, wherever the food is
  stored. The exception is food already in their hands or their pack: that
  they just eat. Otherwise a cook would carry a meal across the base in
  whites to reach a chair, which is the walk this mod exists to prevent.
  A sleeping stand does not take that exception, because a colonist wakes up
  standing beside it, so changing first costs them no walking at all.
- If a stand frees up while someone is already working in its room out of
  uniform, they'll step over and change, unless they're mid-treatment on a
  patient.
- **Change back** appears on any colonist currently in a uniform, for when you
  want them out of it now rather than at their next job. They will get there on
  their own, raid or no raid, but "on their own next job" can be a long time if
  the job they just started was sleep.

One mod setting: **"Unassigned stands are shared"** (default on). Turn it off
and only stands with an explicit owner list ever dress anyone.

## Requirements

- RimWorld 1.6
- **Odyssey** (the outfit stand is Odyssey content)
- Harmony

The kid outfit stand (Biotech) is not used, by design.

## Mod compatibility

**[Outfit Stands Plus](https://steamcommunity.com/workshop/filedetails/?id=3545172389)**
works alongside this mod, in either load order (tested with both). The two
divide a stand cleanly: its mechanized and mending stands are full shift
stands here (shift changes run at their boosted swap speeds, and the mending
stand repairs a borrower's parked clothes while they work), and each stand
shows exactly one Set owner control: this mod's while the stand is in
service, theirs when the stand is set to "Not used for shift changes". Its
wardrobe features, its "allow adding items" toggle and its research are all
untouched.

Two of its behaviors are worth knowing about, because they can look like
faults here: it switches off its own "allow adding items" after a manual
swap, which quietly stops haulers restocking that stand until it is switched
back on; and its right-click "Return to stand", used on a colonist who is
mid-shift, moves clothes the shift system was tracking. Nothing is lost,
and the next change-back sorts it out, but the walk is wasted.

**[Gerrymon's Hotspring Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=3717051546)**
needs no setup. Its private and public pool rooms are named in the room table,
so a stand in one turns itself on for recreation the same way a hospital turns
one on for doctoring.

**[Standalone Hot Spring](https://steamcommunity.com/sharedfiles/filedetails/?id=2205980094)**
works too, and for the general reason rather than a special case: its bathing
job carries a joy kind, which is the whole of what the trigger looks for. Any
modded recreation that does the same is caught. Its room does not score as a
pool, though, so that stand takes the switch by hand.

**[Complex Jobs](https://steamcommunity.com/sharedfiles/filedetails/?id=2069684319)**
works from v1.3.3. It splits vanilla's work types into finer ones and moves
the tasks across, so surgery stops being Doctor work, butchering stops being
Cooking work, and taming and training stop being Handling. A stand that matched
only the vanilla names served part of its room's work and passed over the rest,
which read as the stand working intermittently. The room table now names its
work types as well: hospital picks up Nurse and Surgeon, laboratory Drugs,
kitchen Butcher, workshop Stone Cut, Smelt, Machining, Fabricate, Refine and
Production, barn Train and slaughter. Slaughter exists only if you switch it on
in Complex Jobs' own settings, which needs XML Extensions; anything absent is
passed over, so nothing changes for a game without it.

Taming is the one piece deliberately left out. A stand is chosen by where the
job happens, and taming happens wherever the wild animal is standing, which is
not the barn. Vanilla's own Handling row never fired for it either, so no barn
stand has ever dressed anyone for taming.

**[Dubs Rimatomics](https://steamcommunity.com/sharedfiles/filedetails/?id=1127530465)**
needs no setup. A stand arms itself in any room holding a reactor core, the
plutonium processor or the spent fuel pool, so stocking it with rad suits is the
whole job. Put one where the fuel rods are kept as well as one in the reactor
hall: a colonist changes where the job begins, and fetching fuel begins at the
fuel rather than at the core. The row to tick by hand, if you ever want to, is
Nuclear loading: Rimatomics calls that work type Nuclear in the work tab and
Loading in the field a list like this one reads, so the grid shows both halves
of the name.

Rimatomics sets no room role on any of its buildings, which is why this needed
anything at all. Its machining table and research bench now count as a workshop
and a laboratory, the way the same benches from any other mod already did. A
reactor hall has no bench to key on, so a stand there reads the room's contents
instead and recognises the reactor cores, the plutonium processor and the spent
fuel pool. The benches are read that way too, which is what lets one room hold
both of them and still arm for both.

One layout will not arm, and it is worth knowing about. A plutonium processor
built into a wall so that it faces two enclosed rooms equally belongs to
neither, and a stand in either one stays quiet. Setting it against a wall rather
than through one avoids that, and so does a stand on both sides.

Fetching the chemfuel that goes into the plutonium processor is left alone on
purpose, and it is the one part of that loop that will not change anyone.
Chemfuel is inert, and it lives wherever your colony keeps chemfuel rather than
beside the reactor, so suiting up for that haul costs a walk to the wardrobe and
buys nothing. Carrying the spent rods themselves still changes them, at the
rods.

Research counts too, in the rooms where it can hurt you. The steps run at a
research reactor or a plutonium processor are the ones that can spring a
radiation leak, with your researcher standing at the machine when it does, so
those rooms turn a stand on for research as well as for fuel work. A spent fuel
pool arms for fuel work alone, and a weapons bench for research alone, because
that is what happens in each.

Construction is left off on purpose, and the reason is worth knowing. A reactor
leaks nothing at full health. The leak scales with damage, so an intact core is
safe to build beside no matter how much fuel is in it, and the two Rimatomics
research steps that count as construction work fail with fire and glare rather
than radiation. What exposes a builder is repairing a damaged core, which is a
job you only send someone on after a raid or a fire. Arming construction would
put a colonist through a change of clothes for every wall they lay in the hall
to cover that one case. Tick Constructing by hand on the reactor stand when you
have damage to repair, and untick it afterwards.

Working the reactor console is the exception, and it is not a gap we can close:
no work type sits behind it, it arrives as a right-click order, and a shift
change never interrupts an order you gave. In practice the colonist who loaded
the fuel is already wearing the suit when you send them to the console.

**An apparel mod is worth having.** Not required and not a dependency, but
vanilla has no scrubs and no chef's whites, and the one lab coat it does have
is Anomaly's, so in a pure vanilla game there is very little to actually dress
anyone *in*. Any apparel mod fixes that;
[Vanilla Apparel Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=1814987817)
adds all three and is what all the footage here uses.

**[Apparel Painter](https://steamcommunity.com/sharedfiles/filedetails/?id=3792795811)
is the companion piece**, from the same author, and was built for exactly the
wardrobe walls this mod creates: fine-grained painting of the apparel already
on the stands, one garment or a whole stand at once, with live preview on the
map and a per-item reset back to the natural material color. It earns its
keep on the recreation side, where the outfit is the point: black tie in one
matched palette, sauna robes in the house color. Bulk repainting tools
cannot see inside a stand at all.

## Save safety

Add it to an existing save freely: your existing outfit stands gain the new
controls on load, nothing needs rebuilding. Removing it is also safe: stands
revert to ordinary vanilla furniture, and a colonist who was mid-shift
keeps the uniform (**Clear forced apparel** on the Assign tab un-forces it)
with their own clothes waiting in the stand.

## Status

Live on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3783456242).
Played daily in a live colony, and the lifecycle paths that are impractical
to arrange by hand (gravship flights, death, banishment, repeated faults)
are covered by an automated harness (`devtools/run-harness.sh`) that runs
before every release.

## How this is built

This mod is built with AI assistance and it is worth being precise about where.

**The code, the defs and these documents** are written with
[Claude Code](https://claude.com/claude-code). The repository is MIT-licensed
and contains all of it, so none of this has to be taken on trust.

**All the art here is captured in game.** The mod itself ships no textures and
no apparel of any kind. It adds behavior to a building the base game already
draws, its icons come from vanilla's own UI atlas, and the only image inside the
mod folder is the Workshop preview, which is a screenshot.

Everything else is a screenshot or a screen recording of RimWorld running this
mod, cropped and composited with ImageMagick and ffmpeg. The sets were built by
the debug fixtures in `Source/`, so they rebuild on demand, and
[media/README.md](media/README.md) records the crops, ramps and timings that
produced each one. No diffusion model and no image pipeline are involved
anywhere.

**The engine claims are checkable.** Every assertion in
[docs/DESIGN.md](docs/DESIGN.md) about how RimWorld behaves cites the decompiled
assembly by file and line, at a stated game version.

**Some of it was found in play, and some of it was not.** The reservation carry,
the forced-flag capture, the optimizer pause and the recolor guard are all fixes
for things that went wrong in a live colony. Others never surfaced that way and
were caught by reading the decompiled engine instead, including one that had
been running unnoticed on the demo film set for days. Both kinds are real, and
neither method finds the other's.

**The behavior is tested, and you can run the tests.**
`devtools/run-harness.sh` runs a suite inside RimWorld itself, against the real
engine rather than mocks, in about thirty-five seconds (game launch, mod
load and quit included). Cases arrive from four
places: a bug that happened, a claim this page makes, an engine behavior worth
pinning down before an update moves it, and a feature that shipped with its own.
The standing rule is that anything which goes wrong leaves a case behind that
fails without its fix. It does not cover everything:
[docs/TESTING.md](docs/TESTING.md) says plainly what it does not, and play
observation is still required.

## For modders

| Doc | Contents |
|---|---|
| [docs/DESIGN.md](docs/DESIGN.md) | What the mod interfaces with inside the game and why each piece took its shape: the job-interception point and its traps, what the vanilla stand does and does not provide, the ownership and forced-apparel lifecycles, room-role scoring, the state model. Assumes programming, not RimWorld modding. |
| [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) | Build, engine navigation, the hot-reload rig and the rules it imposes, CI, file map. |
| [docs/TESTING.md](docs/TESTING.md) | What the test suite covers, what it deliberately does not, the rules it follows, and two engine traps it had to pay for. |
| [AGENTS.md](AGENTS.md) | Short form of the invariants, for coding agents. |

## Building from source

```
dotnet build Source/ShiftChange/ShiftChange.csproj -c Release
```

Output goes to `Assemblies/`, which must contain only `ShiftChange.dll`; the
game loads every DLL it finds there, and all package references are
compile-time only. A `-c Debug` build additionally wires in a hot-reload rig
for UI iteration (dev use only; see [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)).
Always build Release before shipping or committing, which also sweeps the dev
artifacts.

## Credit

[Automatic Swap Outfit](https://github.com/aedbia/AutomaticSwapForStand) by
aedbia (MIT) covers adjacent ground: automatic swapping at an outfit stand,
triggered by allowed-area boundaries. Shift Change is an independent
implementation with a different trigger (the room and its work type) and
per-stand ownership, but that mod is why several dead ends were cheap to
avoid.

## License

MIT. See [LICENSE](LICENSE).

Portions of the materials used to create this content/mod are trademarks and/or
copyrighted works of Ludeon Studios Inc. All rights reserved by Ludeon. This
content/mod is not official and is not endorsed by Ludeon.
