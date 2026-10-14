Thread title: [Released] Shift Change

----- message 1 -----

I wanted colonists to wear the right gear for the job instead of one outfit for everything, so Shift Change turns Odyssey's outfit stand into a wardrobe. Put an outfit on a stand, and colonists change into it before they work, relax or sleep in that room, then change back when they leave. Their own clothes wait on the stand and come back exactly as they were.

- **Work**: a stand in a hospital, lab, kitchen, workshop or barn reads its room and dresses whoever does that room's work as automatic work. Nothing to assign.
- **Recreation**: a rec room stand dresses anyone who comes in to relax.
- **Sleep**: a bedroom stand hands out sleepwear at bedtime. Set it to Deposit only and it takes the armor off them overnight instead, and hands it back in the morning.

What it deliberately doesn't do:
- It ships no apparel and no art. The outfits are whatever you put on the stands.
- Direct orders and emergencies never wait for a wardrobe trip.
- Nobody changes into a uniform while the map is under threat.

Needs **Odyssey** (the outfit stand is Odyssey content, so without it the mod does nothing) and Harmony. RimWorld 1.6 and 1.7.
Workshop: <https://steamcommunity.com/sharedfiles/filedetails/?id=3783456242>
Source: <https://github.com/beverage/shift-change>
Written with AI assistance (Claude Code), as the Workshop page says, and no generated art anywhere. MIT licensed.

----- message 2 -----

**New in 1.5.0**: a stand can serve a radius or an allowed area instead of its room, for the places a room can't cover: a shed in the fields, a fishing hut on the shore, a cell block, a build site.
- **Within a radius** covers every cell within 3 to 79 cells of the stand, walls or no walls, and shows its ring while the stand is selected.
- **An allowed area** covers whatever you paint. The Serves menu lists your areas and can start a new one from the stand's room, and Paint and Manage areas open vanilla's own tools.
- A stand standing in the open and serving its room now serves nothing. Outdoors is one map-wide room, so it used to dress for every outdoor job on the map. An alert lists those stands; give them a radius or an area.
- Fishing works now, at a stand ticked for it whose radius or area covers the water.
- Builders no longer change for the walk to the stockpile. The hardhat goes on at the frame, unless one allowed area covers the stand, the stockpile and the site and the builder ranks construction above hauling.

Existing stands stay on This room.

If you use Outfit Stands Plus, two of its behaviors can look like faults here. It switches off its own "allow adding items" after a manual swap, so haulers stop restocking that stand until you switch it back on. And its "Return to stand", used on a colonist mid-shift, moves clothes the shift system was tracking: nothing is lost, but the walk is wasted.

Reports and ideas are welcome in this thread. If a colonist changes when they shouldn't, or doesn't when they should, say what they were doing and where.
