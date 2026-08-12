# Shift Change

A RimWorld mod. Colonists change into the right clothes for the room they work
in, and back out again afterwards — using the vanilla outfit stand.

Put an outfit stand in a work room and put an outfit on it. When a colonist
takes on **automatic** work of that room's kind — doctoring in the hospital,
researching or synthesizing drugs in the lab, cooking in the kitchen — they
change into the stand's outfit before starting, and change back when their
work takes them elsewhere. Their own clothes wait in the stand, and come back
exactly as they were, force-worn markers included.

![A doctor, researcher and cook changing into work clothes and back again](media/demo.gif)

*Three colonists arrive for work in their own clothes — steel helmets, a green
duster — change into what their room's stand holds, and change back on the way
to dinner. Each stand keeps its borrower's civvies while they are on shift.
Sped up 3–6×.*

## How to use it

1. **Build a vanilla outfit stand** (Odyssey) in a work room — hospital,
   laboratory, kitchen, workshop or barn — and put a work outfit on it.
2. That's it, for the common case. The stand reads its room and dresses
   whoever comes to work there.
3. Optional, per stand:
   - **Set owner** reserves the stand for one colonist — their personal kit,
     off-limits to everyone else. Left unassigned, the stand is **shared**:
     any capable colonist may use whichever stand is free, like beds. A
     kitchen needs one stand per cook *working at once*, not one per cook.
   - **Set work types** opens a checklist when the room's reading isn't what
     you want — a multi-purpose room, a room the game scores oddly, or a
     stand you don't want used at all ("Not used for shift changes").

The stand's inspect pane always tells you its state: what work it dresses
for, who owns or is currently wearing it, and whether it's empty.

## The rules it follows

Deliberately narrow, so it never fights you:

- **Automatic work only.** Right-click orders execute immediately, in both
  directions — a doctor ordered to tend *right now* goes straight there, and
  a pawn in uniform given a direct order keeps it on and returns it later.
- **Emergencies are never delayed.** A colonist bleeding out is not kept
  waiting for a wardrobe trip.
- **Nothing happens while the map is under threat.**
- **Personal kit stays personal.** Nobody takes clothes out of a stand
  someone else is using; whoever checked a uniform out is whom it goes back
  to.
- Passing through a room, or eating in it, changes nothing — only doing the
  room's work does.
- If a stand frees up while someone is already working bare in its room,
  they'll step over and change — unless they're mid-treatment on a patient.

One mod setting: **"Unassigned stands are shared"** (default on). Turn it off
and only stands with an explicit owner ever dress anyone.

## Requirements

- RimWorld 1.6
- **Odyssey** — the outfit stand is Odyssey content
- Harmony

The kid outfit stand (Biotech) is not used, by design.

## Save safety

Add it to an existing save freely — your existing outfit stands gain the new
controls on load, nothing needs rebuilding. Removing it is also safe: stands
revert to ordinary vanilla furniture, and a colonist who was mid-shift simply
keeps the uniform (un-force it in their gear tab) with their own clothes
waiting in the stand.

## Status

Release candidate. Played extensively in a live colony; not yet on the
Workshop.

## How this is built

This mod is built with AI assistance and it is worth being precise about where.

**The code, the defs and these documents** are written with
[Claude Code](https://claude.com/claude-code). The repository is MIT-licensed
and contains all of it, so none of this has to be taken on trust.

**There is no generated art, because there is no art.** The mod ships no
textures and no apparel of any kind — no diffusion model, no image pipeline, not
a single PNG. It adds behaviour to a building the base game already draws, and
its icons come from vanilla's own UI atlas. That was a deliberate scoping choice
before it was anything else, and it happens to remove the question entirely.

**The engine claims are checkable.** Every assertion in
[docs/DESIGN.md](docs/DESIGN.md) about how RimWorld behaves cites the decompiled
assembly by file and line, at a stated game version. The behaviour itself was
found and corrected in a live colony rather than reasoned about: the reservation
carry, the forced-flag capture, the optimizer pause and the recolor guard are all
fixes for things that went wrong in play.

## For modders

| Doc | Contents |
|---|---|
| [docs/DESIGN.md](docs/DESIGN.md) | What the mod interfaces with inside the game and why each piece took its shape: the job-interception point and its traps, what the vanilla stand does and does not provide, the ownership and forced-apparel lifecycles, room-role scoring, the state model. Assumes programming, not RimWorld modding. |
| [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) | Build, engine navigation, the hot-reload rig and the rules it imposes, CI, file map. |
| [AGENTS.md](AGENTS.md) | Short form of the invariants, for coding agents. |

## Building from source

```
dotnet build Source/ShiftChange/ShiftChange.csproj -c Release
```

Output goes to `Assemblies/`, which must contain only `ShiftChange.dll` — the
game loads every DLL it finds there, and all package references are
compile-time only. A `-c Debug` build additionally wires in a hot-reload rig
for UI iteration (dev use only; see [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)).
Always build Release before shipping or committing, which also sweeps the dev
artifacts.

## Credit

[Automatic Swap Outfit](https://github.com/aedbia/AutomaticSwapForStand) by
aedbia (MIT) covers adjacent ground — automatic swapping at an outfit stand,
triggered by allowed-area boundaries. Shift Change is an independent
implementation with a different trigger (the room and its work type) and
per-stand ownership, but that mod is why several dead ends were cheap to
avoid.

## License

MIT — see [LICENSE](LICENSE).
