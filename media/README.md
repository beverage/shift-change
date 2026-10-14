# Media

Assets for the README and the Workshop page. **Kept out of the shipped mod on
purpose** — both the CI release zip and `devtools/publish-workshop.sh` copy an
allowlist (`About Assemblies Defs Patches Languages docs LICENSE README.md`)
and not this folder, so a 2 MB gif never lands in anyone's mod directory.

That exclusion is the allowlist's doing and nothing else's. RimWorld's in-game
uploader publishes the mod folder verbatim — `Mods/ShiftChange` is a symlink to
this repository, so uploading through it would ship `media/`, `Source/`,
`.github/` and the whole `.git` directory to every subscriber. Always publish
via `devtools/publish-workshop.sh`; never through the dev symlink.

## demo.gif

Filmed on the demo stage (dev mode → Shift Change → Build demo stage), captured
in OBS at 900×1050 against the 2560×1440 display, and cut with
[`devtools/footage.sh`](../devtools/footage.sh):

```
devtools/footage.sh gif "<master>.mov" --autocrop --ramp 1:11:3,11:23:6,23:47:3
```

Everything else is the tool's defaults — 480 wide, 20 fps, 256 colours, bayer
dither, gifsicle pass. Output is 480×580, 13.2 s, ~2.2 MB.

The ramp is the point: 3× over the arrival and the two changes, 6× through the
stretch where the pawns are only working, 3× again for the change-out and the
walk to dinner. Fast where nothing is being demonstrated, slow where it is.

`--autocrop` strips the black pillarboxing OBS leaves when the canvas does not
exactly match the captured region (16 px each side on this take).

## demo-recroom.gif

The black-tie rec room demo. It lives in two places on purpose: this copy is
what the repository README embeds, and `https://i.imgur.com/QgM2QRr.gif` is the
mirror the Steam description embeds, because a Workshop page cannot reference a
repository path. Neither reaches subscribers — the allowlist at the top of this
file excludes `media/` from both the release zip and the Workshop upload.

**Its recipe is here because the last one's was not** — see the section below
for what that cost.

| | |
|---|---|
| master | `~/Movies/2026-08-22 11-53-35.mov` (1920x1080, 30 fps, 26.0 s) |
| output | 480x446, 193 frames, 9.7 s, 2.87 MB |

```bash
devtools/footage.sh gif "<master>.mov" --autocrop --ramp 3:8:1.5,8:15:3,15:21:1.5
```

**Both walking segments run at the same speed, and that is the point.** The
arrival and the mingle are the same action — pawns moving at normal speed — so
a ramp that compressed one and not the other made locomotion visibly change
between the opening and the close. Only the wardrobe trip in the middle is
sped up, at 3x, which is the rate `demo.gif` uses for its changes; the two
gifs read as a set because of it.

**`--autocrop` is correct here and was wrong on the pool footage**, which is
worth knowing before reaching for it. It detects the black pillarboxing OBS
leaves when the canvas does not match the captured region — this take has some
(`1162:1080:378:0`), the pool take had none, and on that one autocrop was being
asked to find a room edge it had no way to see. Check it against a still before
committing to a whole gif.

**Do not trim the bottom margin.** It reads as dead grass in a mid-clip still,
but at t=3 it holds all eight pawns lined up outside the door — the entire
before-state of a before/after gif. The frame is already tight on every side:
roughly 20 source px from the room's outer wall on the top and sides, and about
11 display px below the pawn name labels.

## demo-recreation.gif — deliberately not in this repository

A recreation gif (480×390, 255 frames, 8.3 MB) was cut for this feature and
shipped in the branch briefly. It was removed and **the branch history was
rewritten**, so no clone carries the 8.3 MB — a plain deletion would not have
achieved that, since the blob stays reachable in history and every clone still
pays for it. Do not go looking for it in `git log`; it is not there.

It lives off-repo instead. The master capture is
`~/Movies/2026-08-20 22-49-20.mov` (69 MB), with the cut gif beside it.

**Its cut parameters were never recorded, and that is the actual loss.** No
ramp, no crop, no timings — the commit that added it recorded the card's recipe
and not the gif's. A re-cut means re-deriving the ramps against that master. A
re-shoot means a live take: the pool stage rebuilds deterministically (dev mode
→ Shift Change → Build pool room stage) and `footage.sh` will cut whatever it
is given, but the performance in between — a pawn rolling the joy job, walking
in, changing and swimming — is an OBS capture that nothing here automates.

Write the `footage.sh` invocation down at the time. That is the same lesson the
scene cards' treatment section below had to learn by measuring three finished
cards backwards.

## demo-sleep.gif

The sleep comparison: two 5×5 bedrooms off one corridor, both soldiers in
prestige cataphract, both turning in. West (Vane) DEPOSITS her armour into an
empty rack and sleeps in the base layer; east (Roan) SWAPS his for a duster and
helmet. Filmed on the sleep stage (dev mode → Shift Change → Dev tools… → Build
sleep stage).

**The recipe is here because it is the first thing this file asks for twice.**

| | |
|---|---|
| master | `~/Movies/2026-09-04 17-19-16.mov` (1920×1080, 30 fps, 39.8 s) |
| output | 480×378, 235 frames, 11.8 s, 1.1 MB |

```bash
devtools/footage.sh probe "<master>.mov"
devtools/footage.sh gif "<master>.mov" \
  --crop 822:647:995:172 --ramp 5:8:1,8:30:8,30:36:1
```

**Reshot 2026-09-04** with carpeted floors, a grass surround and a daytime sky,
replacing a night take on bare wood. Two things came out of that beyond looks:
the flat carpet costs **a third of the file size** the plank floor did (1.1 MB
against 3.0 at identical dimensions and settings — dithering has far less to
chew on), and the pawns read against a dark floor in a way they did not against
brown planks.

**MEASURE THE BUILDING'S BOX BEFORE CHOOSING A CROP.** In this master it is
**x 1012–1800, y 189–802** — 788×613 source px, ~61 px per map cell for a 13×10
structure. The previous take framed it at x 816–1642, y 268–905, so **a reshoot
moves the box and every crop number with it**; none of them carry over. Three
cuts were wrong on the first master before it was measured rather than
estimated from a contact sheet: one tight to the wall on every side, and one
whose right margin was 40 px against the left's 16 while the bottom edge was
actually *clipping* 21 px off the building — which reads as "no bottom border"
rather than as a crop error. The recipe:

```bash
ffmpeg -y -ss 20 -i "<master>.mov" -frames:v 1 full.png
# then stamp a labelled 50px grid in SOURCE coords and read the edges off it
```

Allowance is ~10 px on ALL FOUR SIDES at 480 wide, which is `demo.gif`'s (L10
R11 T7 B10, measured off its first frame) and what makes the two read as one
set. (`demo-recroom.gif` measures L6 R7 T5 **B83** — that bottom is the
deliberate exception documented above, not the house style.)

**Equal means equal; do not talk yourself into a larger top.** This cut shipped
briefly at 38 px top against 17 at the sides, on the theory that RimWorld draws
a wall's upper face and shadow above its geometric edge and a bigger top would
therefore read as even. It does not — at 2× it is plainly heavier than the
bottom, and it was spotted immediately in review. Set all four from the same
number.

Solve the margins rather than guessing them. For a building `BW` px wide and a
480 px output, a side margin of `m` source px lands at `m × 480 / (BW + 2m)`
output px, so **`m = BW / 46`** gives the ~10 px house allowance at any zoom.
Here `BW` is 788, so `m` is 17 on every side. Check the top against the colonist
bar before committing — the portrait names sit at y≈124 in this master, and a
generous top allowance reaches them.

Do not use `--autocrop` here. There is no pillarboxing for it to find, so it
would be hunting a room edge it cannot see — the trap the pool footage hit.

**Judge margins against a contrasting matte, never with a colour threshold.**
A detector keyed on "ground is greener or brighter than the wall" works on
`demo.gif`'s daylit grass and fails completely on this scene's dark night dirt,
reporting L0 R1 T0 B1 on a frame that plainly had margin. One line settles it:
`magick still.png -bordercolor "#c02020" -border 10 matte.png`.

**The ramp is 1× / 8× / 1×, and the slow bookends are the content.** Seconds
5–8 hold both soldiers arriving in the corridor in full armour, and 30–36 hold
both asleep with the armour visibly parked on the racks. The 8× middle is the
walk and the two changes — the mechanism, which does not need to be watched at
speed. The first five seconds are dropped: they are the stage settling.

**Known weakness, stated so a re-shoot does not chase it.** Once both are in
bed the two modes look alike — armour on both racks, a sleeper in both beds —
because Roan's duster is hidden under the blanket. The deposit/swap difference
is only legible in the middle beat where both stand changed beside their
stands, and that is exactly the stretch the 8× compresses. A version that
holds ~18–22 s at 1× would show the distinction better at the cost of a longer
gif; this cut favours the bookends instead.

## demo-construction.gif, demo-prison.gif, demo-farm.gif

The catchment scenes (v1.5.0): a stand serving an allowed area. Filmed with OBS
on 2026-10-13 in the constellation's media sandbox, a separate RimWorld profile
staged in god mode; the takes are not in this repository. All three are 7 s at
12 fps and 540 px tall, cut from the 2560×1440 take with one ffmpeg pass:

```bash
ffmpeg -ss <in> -to <out> -i "<take>.mov" -an -filter_complex \
  "crop=<crop>,scale=<size>:flags=lanczos,fps=12,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle" \
  -loop 0 <name>.gif
```

| gif | take | in, out | crop | size |
|---|---|---|---|---|
| `demo-construction` | 12:36 | 3.2, 10.2 | `1920:1080:60:208` | 960:540 |
| `demo-prison` | 15:56 | 2.5, 9.5 | `846:810:720:363` | 564:540 |
| `demo-farm` | 14:51 | 2.0, 9.0 | `1440:900:479:417` | 864:540 |

The construction cut also sets `palettegen=max_colors=192`, and its 16:9 crop
centres the changing area and the formation on one line. The prison and farm
crops take in only what the scene uses: the prison to the edge of its
sidewalks, through the generator, and the farm to where the green grass starts.

**Turn off Options → Graphics → Plant wind sway before a take.** Swaying plants
redraw the whole frame every frame, so a GIF cannot shrink a frame to the patch
that changed: the farm cut at 1280 wide came out 17.8 MB with sway on and
6.4 MB with it off. The yard was filmed with sway on, and its grass edges are
part of why it is the largest of the three.

The stands in the construction and prison scenes are Outfit Stands Plus's
mechanised ones, which swap in a fraction of a vanilla stand's time, so the
change does not dominate a 7 s cut. The construction crates and the farm's
storehouse barrels are Reel's Expanded Storage, credited in the README.

## cards/

Workshop gallery images. `card-chef`, `card-doc`, `card-lab`, `card-recreation`,
`card-sleep` and `card-warden` are 640×360 scene shots; `card-farm` is a
946×516 pair; `card-blacktie` (1482×587) and `card-construction` (1518×830)
are three-beat sequences; `card-controls` (1493×819) shows UI instead.

`card-controls` carries three panels: the work-types dialog on the left
(automatic, resolved to doctoring from the room, grid populated, the Serves row
at its foot), the Serves menu in the middle, then the owner list on the right.
The menu lists This room, Within a radius, the quicktest map's own empty
"Area 1" faded as outside the stand, the Doctoring area the shoot makes, New
allowed area and Manage areas, so every section shows. Until 2026-10-13 the
middle panel was the same dialog with recreation ticked; the scene cards
already show recreation, and the menu is the control catchments added. The
dialog still shows the **Sleeping** row, which is how the card advertises that
a rest configuration exists; `card-sleep` immediately precedes it in the
gallery and shows the feature itself.

**Shot and composited by script since 2026-09-04** — the second fully
automated asset, after `card-sleep`:

```bash
devtools/run-scene.sh --media --bridge
devtools/bridge/shoot-controls-card.py 5176 shiftchange-scene-bridge
```

It boots a clean colony, builds the demo stage, selects the hospital stand,
opens its work-types dialog and captures it, makes the stand an allowed area
through the menu's own New allowed area, captures the dialog again in area mode
(Paint and all; kept for the record, not composited), opens the Serves menu and
captures that, then opens the owner list and captures that. No crops to
measure and no offsets to carry forward. `--recompose` rebuilds the card from
the last captures without a game.

**`clipTargetId` is what retired the hand-cropping.** `take_screenshot` clips to
a window's own rect, so the 3-6 px of game world a screengrab carries outside
the border simply never appears. The old recipe's per-capture crop offsets
(`525x813+6+6`, `525x813+4+8`) existed only to remove it, and had to be
re-measured every take.

**The Serves menu takes two tricks.** Its button opens the menu only with the
bridge's virtual cursor on it, so the script hovers it (`set_hover_target`)
before clicking; a bare click reports success and opens nothing. And the menu
draws nothing in the gaps between its sections, so a window capture shows a
strip of whatever lay behind it there, map floor in one and dialog in the
other. The script paints every row whose left pixel is not the menu's frame
colour (#424549) with the card backdrop before compositing.

**A stand in a bedroom grows the dialog.** Its auto label wraps to two lines
and adds 12 px, which is why the shoot always uses the hospital stand.

**One instance means one UI scale, by construction.** Panels captured at 1.0x
and 1.25x cannot be composited — cropped to their borders the same dialog is
420 px wide at one and 525 at the other, and the owner dialog 560 against 699.
The old card was 1.25x throughout; this one is 1.0x throughout. Never mix.

The canvas is derived, not fixed: `sum(widths) + gap x (n+1)` by
`max(height) + 2 x margin`, at gap 75 and margin 60. The old 2000x900 was sized
for 1.25x panels and leaves 600 px of dead background at 1.0x. Vertical offsets
are still `(height - panel) / 2` — every panel is centred.

**Start from a fresh colony, and WAIT for the menu.** The demo stage spawns
four staff on every build and they accumulate; a second run in one instance put
Chef, Doc, Lab and Patient in the owner list twice and read "Assign all 11".
`go_to_main_menu` returns `{"status": "queued"}` and schedules the transition,
so calling `start_debug_game_ready` straight after finds the OLD game still
loaded, reports it playable and starts nothing. Poll `get_ui_state` for
`inEntryScene` first. Pass `--keep-game` to skip the reset.

**Two more bridge traps this card paid for.** `close_window` with no argument
closes the TOPMOST window, which is nearly always a `Verse.ImmediateWindow`
tooltip — a loop that calls it bare closes tooltips forever while the dialog
underneath goes on absorbing map clicks, and selection then fails as "nothing
selected", which reads like a stage build failure. Close by `windowType`. And
`click_cell` injects at the screen position a cell maps to, so a cell outside
the camera's `viewRect` cannot be clicked at all — frame it first.


### The scene cards' treatment

`card-chef`, `card-doc`, `card-lab`, `card-recreation` and `card-sleep` are
before/after pairs, and the recipe was not recorded when the first three were
made — these numbers were measured back off `card-chef` on 2026-08-21, and
`card-sleep` is the first to be cut from them by script rather than by hand:

| | |
|---|---|
| canvas | 640×360, two 318 px panels with a 4 px gutter, split at x=319 |
| title | `#f5f0e7`, cap height 37 px, bbox x 121–509 (centred on 320), y 13–49 |
| fade | black at y=0, ramping to nothing by y≈85 |

**Both panels use the SAME crop at two moments**, so only the pawn changes —
that is what makes a before/after read as one. The convention across the set is
that the pawn starts OUTSIDE the room in their own clothes and ends inside in
the stand's outfit, so the crop has to include ground beyond the doorway. For
`card-recreation` the moments are **t=0** (on the grass, walking in, helmet and
armour) and **t=10.0** (on the deck in the robe, hair down), both cropped
`318:360:1158:312` — chosen so BOTH pawn name labels clear the bottom edge,
which is what fixes the crop's vertical offset.

**The typeface is `RimWordFont.ttf` at `-pointsize 44`**, which renders a
390×38 ink box against the 389×37 measured off `card-chef` — i.e. it is the
original setting. Confirmed 2026-09-04 by re-typesetting: 390×38, the
documented box exactly.

**It lives here, at `media/RimWordFont.ttf`.** It used to live nowhere, which
this file warned about and which meant a title could only be lifted off a card
that already carried it. It then briefly lived outside this repository, which
was worse in a different way: every tool had to reach out of its own checkout,
and the line that did so was an absolute path carrying a home directory, in a
public repository. Media assets belong beside the media that uses them. Set
`RIMWORD_FONT` to override.

```bash
magick -size 640x360 xc:black before.png -geometry +0+0 -composite \
       after.png -geometry +322+0 -composite s1.png
magick s1.png \( -size 640x90 gradient:black-none \) -geometry +0+0 -composite s2.png
magick -background none -fill "#f5f0e7" -font RimWordFont.ttf -pointsize 44 \
       label:"SHIFT CHANGE" -trim +repage title.png
magick s2.png title.png -geometry +121+13 -composite cards/card-recreation.png
```

Fallback if the font is ever missing: the top band is essentially black behind
the letters, so `-crop 640x90+0+0 -level 25%,100%` on an existing card isolates
the lettering, and screen-compositing that reproduces it exactly at the same
position. That only copies what is already there — it cannot set new text.

### card-sleep — the first fully automated card

The only card in the set that is not an OBS capture. `devtools/bridge/` drives
the game over RimBridgeServer: build the stage, settle, capture the before
panel, play a measured interval, capture the after panel, composite. One
command, no hands, and the framing is not a crop found by eye — it is the
stage's own block geometry.

```bash
devtools/run-scene.sh --media --bridge          # port 5176, fixed token
devtools/bridge/shoot-sleep-card.py 5176 shiftchange-scene-bridge --survey
devtools/bridge/shoot-sleep-card.py 5176 shiftchange-scene-bridge --after 2000
```

| | |
|---|---|
| stage | Dev tools… → Build sleep card stage, at map cell (120, 120) |
| capture | `screenshot_cell_rect` on the 8×9 block, `rootSize` 12 → 480×540 |
| moments | before at t=0, after at 2000 ms of Ultrafast play |

**Survey before committing.** The after-moment is the one free parameter and
cannot be derived — it depends on how long the pawn takes to roll the rest job,
walk in and finish the swap. `--survey` plays once and captures a ladder, so
one stage build yields every candidate instead of one guess per build.

**Settle 15 ticks before t=0.** The stage writes its roof during the build but
the glow grid catches up over the following ticks, so a capture at tick 1 shows
the room still taking DAYLIGHT and every later frame shows it torch-lit and
darker. The first survey had exactly that — panel one a stop brighter than
panel two, which cannot composite as a pair.

**Watch for a wanderer.** A quicktest map has its own colonists, and one walked
into the t1500 frame of the survey trailing a speech bubble. Nothing stops it;
check the chosen frame before compositing rather than trusting the ladder.

**The after moment is the pawn ASLEEP, not the pawn standing changed.** Both
exist — the swap finishes around t800 and the bed is reached by t1500 — and the
standing frame shows the new clothes more plainly. It was still the wrong
choice: pawn and stand are two similar silhouettes against the same wall and
only the name label separates them, while the in-bed frame has the armour
visibly parked on the rack doing the narrating. The work cards show a colonist
DOING the work in the uniform; the sleep equivalent is being asleep.

### card-blacktie — the three-beat card

The only card in the set that is a SEQUENCE rather than a pair: the black-tie
rec room before the change, the changing wall, and the room after, read left to
right. Two full-room panels with a tall inset band on the join, the band being
the changing wall at its own tighter zoom so it reads as a step rather than as
a third room.

Built by [`card-triptych.sh`](card-triptych.sh) from three screengrabs of the
rec room stage (dev mode → Shift Change → Build rec room stage). The captures
are not in this repo, and **every constant in the script was measured off those
particular ones**. For a new take they all have to be measured again. What the
measurements are, and why:

**1. Match the zoom before anything else.** A before/after only reads as one
place if the room lands on identical pixels in both panels. Find the room
rectangle in each shot by thresholding luminance > 78, taking the median over
~30 rows and columns so pawns and foliage at the edges cannot skew it. On the
shipped take both came out 924×677, at (22,47) and (15,49) — a 7 px pan, no
rescale. On an earlier take they were 977×716 and 957×700, 2.1% apart, and the
after shot had to be resized before cropping. Two captures minutes apart can
differ; never assume.

**2. Pull both panels 40 px in at the join.** The east wall is dead weight in
the before and the stripped stands are dead weight in the after, so the band
covers only inactive scene. Burying those empty stands is the point: left
showing, they sit beside the band's full ones and read as a doubling rather
than as a story.

**3. Crop the band off measured edges too.** The building's outer edge comes
from a clear floor row (x=34 on this take), not from eyeballing the wall.

**4. Displace the band's window UP so its content sits DOWN.** The title fade
is opaque for the band's top 139 px. Framed from the north wall the first stand
lands at card y=55 and loses 60% of itself to the fade, while a standing lamp
and 100 px of bare floor idle at the bottom. A window at y=43 moves it to y=89
at 36%. y=0 is available and drops the fade to 11%, but it overshoots — the
band then opens on ground outside the building and the air below the last pawn
goes thinner than the air above the first, which inverts the imbalance instead
of fixing it. Half the travel keeps both ends of the column running past the
frame, which is the effect worth having.

**5. Separation is a hard edge plus a soft ramp.** 4 px of solid black either
side of the band, then a 120 px black ramp at 75% falling into each panel from
that border. The band's edge is 100% against a ramp topping out at 75%, so the
line stays a line instead of dissolving into its own shadow.

Fade and title are the scene cards' treatment scaled by 587/360 — fade to
y=139, `RimWordFont.ttf` at `-pointsize 72`, centred at y=21. The title crosses
the band. It lands on dark wall and reads, and moving it off centre would break
with the other four cards.

The right third of the after panel is empty, which is deliberate: the guests
cluster around the harp and billiards, and cropping the poker corner out would
push the after panel's window well away from the before panel's, which is what
makes the pair read as one room.

**Its title is smaller than the others' in the gallery.** It was scaled by
height (587/360), which puts it at 43% of the card's width, where the scene
cards' spans 61%. Steam's gallery shows every image at one width, so the share
of the width is what decides how big a title looks; the cards since scale by
width.

### card-warden and card-farm — before/after by script

Both are built by [`card-pair.sh`](card-pair.sh): the same crop of one take at
two moments, side by side, with the scene cards' title. Its header carries each
card's exact command, crop and moments, and why each moment was chosen.

`card-warden` is a standard 640×360 scene card: Sam outside the cell block's
front door in their own clothes, then in Mullen's cell in the guard kit, mid
chat, their clothes on the stand. The prison take is zoomed out further than
the original scene cards' footage, so its pawns come out slightly smaller.

`card-farm` is wider, two 470×516 panels at the take's native resolution and a
6 px gutter, the same 1.83:1 shape as `card-construction`: the farmers in the
farmhouse in their own clothes, then in the corn in their ranch clothes.
`PANEL` and `GUTTER` make the wide card, and the title and fade scale with the
card's width. The farm take is zoomed out a third further than the yard's, so at
native resolution its pawns, shown at the gallery's width, come out the yard
card's size. Two moments are enough here: the farmhouse and the corn make the
order of events obvious, so the pair needs no seams.

### card-construction — three beats in one frame

Built by [`card-yard-triptych.sh`](card-yard-triptych.sh) from the 12:36 yard
take. The yard runs west to east in the order things happen (formation,
changing area, crates and site) and the camera never moves, so the card is one
framing cut into three equal thirds, each from its own moment: the crew in
formation, all twelve at their stands, and the crew carrying granite out to the
site. The thirds line up into one picture of the yard.

**The seams carry the direction of time.** Each is a line shaped like a wave
crest travelling right: shading that builds slowly and then steepens over
128 px from the left, a firm edge on the line, and a fade back to full light
within 12 px ahead of it. A symmetric band read as a panel border and a linear
ramp as a plain shadow. Each line is a 12 px chevron pointing at the yard's
centre line (the alcoves' open aisle, the formation's middle row, the crates'
middle), not the card's, which sits 54 px higher for the title's headroom. The
shading is the cards' slate (#262b30) at 45%, and a sideways motion blur fades
in on the same profile, strongest on the line.

The script's comments hold every measurement: why the thirds are 506 px and
start where they do (each seam's shadow needs bare ground in both moments it
joins), why each moment was picked frame by frame (two frames earlier, Toni is
still walking to her stand), and the variants tried and dropped (a sine-wave
boundary, a lit rim, motion ghosts). Every look setting is an environment
variable, so a variant needs no edit, and `SEAMS=none` gives the seamless join.

### The UI cards

The UI cards share one treatment, and it is worth stating because it is what
makes them read as a set: **the interface is lifted out of the game and floated
on a flat field of `srgb(38,43,48)`**, with generous margins and no annotations,
arrows or captions. No game world behind it, nothing pointing at anything.

**Upscale a UI capture with `-filter point`, and it is not a stylistic choice.**
Measured 2026-08-17 on `card-tooltip` (retired 2026-10-13 with the Allow
removing items card), a 1× window capture doubled to sit at `card-controls`
scale:

| upscale | file |
|---|---|
| Lanczos | 821 kB |
| Lanczos, quantised to 256 colours | 374 kB |
| **point (nearest)** | **55 kB** |

Same dimensions, and the nearest-neighbour version looks marginally *sharper*.
The text is already antialiased in the capture, so doubling pixels preserves
that antialiasing intact, while Lanczos interpolates thousands of new
intermediate values across every glyph edge — gradients PNG cannot pack. Do not
"improve" this to a smoother filter; it costs 15× the bytes for a softer image.
Better still, capture at the display's native resolution and skip the upscale.

## Preview.png

640×360, the Workshop's required size, pulled from the same master:

```
devtools/footage.sh still "<master>.mov" --crop 780:439:55:25 --start 12 --preview
```

Framed on the hospital and lab rather than the whole stage — the building is
portrait and the preview is 16:9, and Steam renders it small in browse views,
so two rooms legible beats four rooms tiny.
