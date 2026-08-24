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

## cards/

Workshop gallery images. `card-chef`, `card-doc`, `card-lab` and
`card-recreation` are 640×360 scene shots; `card-blacktie` is a 1482×587
three-panel sequence; `card-controls` (2000×900) and `card-tooltip` (1536×864)
show UI instead.

`card-controls` is 2000×900 and carries three panels: the SAME work-types
dialog in its two modes — work on the left (automatic, resolved to doctoring
from the room, grid populated) and recreation in the middle (grid hidden,
exclusivity note showing) — then the owner list on the right.

**Re-shot 2026-08-24**, when the work-types dialog gained "Keep contents out
of trade" under "Change the whole outfit", inside its separator.
`HeaderAllowance` went 226 → 252 and the work panel grew by exactly that one
`RowHeight`, 780 → 813 px, which is 26 logical px at this machine's 1.25× UI
scale. Widths did not move, so the horizontal offsets carried over untouched.

**Both work-types panels are now the same height, and that is correct rather
than lucky.** `FitToMode` sizes the window from `InitialSize.y`, which branches
on `ModeOnly` — the excluded stand — and not on recreation, so the two modes
have always been the same window. The previous take had them at 780 and 795,
which means the old recreation crop was 15 px loose. Width cross-checks the
crop; height never did, and that is what the gap cost. Worth a glance at both
numbers on the next take, not just the 525.

**The owner panel was not re-captured.** Its dialog did not change — the only
commit touching that path since the last card is the reinstall fix, which adds
`CanSetUninstallAssignedPawn` and draws nothing — so it was recovered from the
previous card instead. That composite was at native resolution, so what comes
back out is pixel-identical to the original capture rather than a re-encode of
something scaled. Confirm a recovery landed by sampling the crop's corners and
edge midpoints: all should read `srgb(87,97,110)`, the window border, with no
`srgb(38,43,48)` card background leaking in.

The owner list had been cut from an earlier version of this card, when it was
a bare assign list of three pawns that taught nothing. It earned its place back
once it grew the gender column, the filter tabs and Assign all, which are the
answer to gendered apparel and cannot be shown any other way.

**Composited at NATIVE resolution** on the cards' own background (`#262b30`),
each panel vertically centred, so nothing is resampled. That is why the card is
wider than the others: 525 + 525 + 699 = 1749 px of panel before margins, which
will not fit the 1536 the two-panel version used, and scaling a panel down to
make it fit would cost the text its sharpness.

**Crop each panel to its own window border, not to the capture.** Every RimWorld
window draws a 2 px border at luminance 95, and a screengrab carries 3–6 px of
game world outside it — which reads as a coloured leak against the flat
background once composited. Both work-types panels come out at exactly 525 px
wide when cropped to the border, which is the check that the crop landed on the
same feature in each.

```bash
# panels, cropped to their own window borders (2026-08-24 take)
magick work.png -crop 525x813+6+6 +repage p-work.png
magick rec.png  -crop 525x813+4+8 +repage p-rec.png

# owners recovered from the PREVIOUS card, not re-captured. Must run before
# the composite below, which overwrites the file it reads from.
magick cards/card-controls.png -crop 699x699+1250+100 +repage p-owners.png

magick -size 2000x900 xc:"srgb(38,43,48)" \
  p-work.png   -geometry +50+43   -composite \
  p-rec.png    -geometry +650+43  -composite \
  p-owners.png -geometry +1250+100 -composite \
  -strip -define png:compression-level=9 cards/card-controls.png
```

The crop offsets are per-capture; re-measure them for a new take. The vertical
composite offsets are not free parameters — every panel is centred, so each is
`(900 - height) / 2`, which is where +43 and +100 come from. The owner panel is
square (699×699) because the dialog's `InitialSize` is 560×560 and this machine
renders the UI at about 1.25×.

### The scene cards' treatment

`card-chef`, `card-doc`, `card-lab` and `card-recreation` are before/after
pairs, and the recipe was not recorded when the first three were made — these
numbers were measured back off `card-chef` on 2026-08-21:

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
original setting. The font does not live in this repo; keep a copy wherever the
cards get made, because without it the title cannot be re-typeset, only lifted
from an existing card.

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

The UI cards share one treatment, and it is worth stating because it is what
makes them read as a set: **the interface is lifted out of the game and floated
on a flat field of `srgb(38,43,48)`**, with generous margins and no annotations,
arrows or captions. No game world behind it, nothing pointing at anything.

`card-tooltip` was cut from a windowed screengrab. **Both halves are in it on
purpose** — the gizmo and the tooltip it belongs to. A floating panel of text
does not say which control it describes, and that is the whole subject of the
card; the button also carries its own state, the red ✗ showing the toggle off,
which is the state the text is arguing for.

```bash
# crop each element TO its own edge, not around it. The tooltip's 1px border is
# srgb(89,98,111); stop at x=90 on the button or you take a strip of the tooltip
# with it, since the panel overlaps the gizmo in the live UI.
magick <shot>.png -crop 335x338+91+17 +repage tip.png
magick <shot>.png -crop  88x103+3+298 +repage btn.png
magick -size 1536x864 xc:"srgb(38,43,48)" \
       \( tip.png -filter point -resize 200% \) -geometry +550+94  -composite \
       \( btn.png -filter point -resize 200% \) -geometry +330+564 -composite \
       -strip -define png:compression-level=9 cards/card-tooltip.png
```

The two are **bottom-aligned with the button to the left**, echoing where the
tooltip actually appears when you hover, and framed with the same ~94 px
vertical margin `card-controls` uses.

Unlike a dialog, a gizmo has no opaque backing — RimWorld draws it over the
world, so the button carries a little of the blue map with it and its label
runs to the bottom of the capture with the last row or two of "items" shaved.
Both are artefacts of the source, not the composite.

**`-filter point`, and it is not a stylistic choice — measured 2026-08-17:**

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

The 200% itself is a compromise: the source was a 1× window capture, so the
panel is only 335 px wide and has to be doubled to sit at `card-controls`
scale. A capture at the display's native resolution would not need it, and
would also carry the button's label whole.

## Preview.png

640×360, the Workshop's required size, pulled from the same master:

```
devtools/footage.sh still "<master>.mov" --crop 780:439:55:25 --start 12 --preview
```

Framed on the hospital and lab rather than the whole stage — the building is
portrait and the preview is 16:9, and Steam renders it small in browse views,
so two rooms legible beats four rooms tiny.
