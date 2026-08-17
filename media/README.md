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

## cards/

Workshop gallery images. `card-chef`, `card-doc` and `card-lab` are 640×360
scene shots off the preview stage; `card-controls` and `card-tooltip` are
1536×864 and show UI instead.

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
