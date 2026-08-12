# Media

Assets for the README and the Workshop page. **Not shipped to players** — the
release zip copies `About Assemblies Defs Patches Languages docs LICENSE
README.md` and deliberately not this folder, so a 2 MB gif never lands in
anyone's mod directory.

## demo.gif

Filmed on the demo stage (dev mode → Shift Change → Build demo stage), captured
in OBS at 900×1050 against the 2560×1440 display, and cut with
[`devtools/footage.sh`](../devtools/footage.sh):

```
devtools/footage.sh gif "<master>.mov" --autocrop --ramp 1:11:3,11:23:6,23:47:3
```

Everything else is the tool's defaults — 480 wide, 20 fps, 256 colours, bayer
dither, gifsicle pass. Output is 480×580, 13.3 s, ~2.1 MB.

The ramp is the point: 3× over the arrival and the two changes, 6× through the
stretch where the pawns are only working, 3× again for the change-out and the
walk to dinner. Fast where nothing is being demonstrated, slow where it is.

`--autocrop` strips the black pillarboxing OBS leaves when the canvas does not
exactly match the captured region (16 px each side on this take).

## Preview.png

640×360, the Workshop's required size, pulled from the same master:

```
devtools/footage.sh still "<master>.mov" --crop 780:439:55:25 --start 12 --preview
```

Framed on the hospital and lab rather than the whole stage — the building is
portrait and the preview is 16:9, and Steam renders it small in browse views,
so two rooms legible beats four rooms tiny.
