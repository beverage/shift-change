# Shift Change

A RimWorld mod. Colonists change into the right clothes for the room they work
in, and back out again afterwards — using the vanilla outfit stand.

Assign a stand to a colonist and put it in a work room. When that colonist takes
on **automatic** work of the room's kind — doctoring in the hospital, researching
in the laboratory — they change into whatever the stand holds before starting,
and change back when they leave.

Deliberately narrow:

- **Automatic work only.** Ordering a doctor to tend someone *right now* never
  sends them across the base to change first.
- **One stand, one owner.** Their uniform is their own. Nobody walks off in
  someone else's clothes.
- **Never during a raid** or other threat.

It adds **no clothing and no art of its own** — it uses the vanilla outfit stand
and whatever apparel you put on it.

## Status

**Spike.** Not playable. The repo currently contains one Harmony patch that
observes job assignment and writes to the log; nothing swaps apparel yet. It
exists to prove out the one genuinely uncertain part of the design — where to
intercept automatic job assignment — before the rest is built on top of it.

## Requirements

- RimWorld 1.6
- **Odyssey** — the outfit stand is Odyssey content
- Harmony

## Building

```
dotnet build Source/ShiftChange/ShiftChange.csproj -c Release
```

Output goes to `Assemblies/`, which must contain only `ShiftChange.dll` — the
game loads every DLL it finds there, and both package references are
compile-time only.

## Credit

[Automatic Swap Outfit](https://github.com/aedbia/AutomaticSwapForStand) by
aedbia (MIT) covers adjacent ground — automatic swapping at an outfit stand,
triggered by allowed-area boundaries. Shift Change is an independent
implementation with a different trigger (the room and its work type) and
per-stand ownership, but that mod is why several dead ends were cheap to avoid.

## License

MIT — see [LICENSE](LICENSE).
