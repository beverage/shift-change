// Compiled only with SCENES (Debug and Media builds; see ShiftChange.csproj). This file builds
// a scene and must never reach a player: it clears its footprint, destroying any pawn standing
// in it, rewrites terrain and roof, unlocks the whole research tree, and leaves player-faction
// colonists and buildings behind. Fixture primitives come from DebugTools_Fixtures; TopUpNeeds
// from DebugTools_DemoStage, which is SCENES-only too.
#if SCENES
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using static ShiftChange.DebugTools_Fixtures;

namespace ShiftChange
{
    /// <summary>
    /// A black-tie games room for the recreation trigger: billiards, poker, a harp and a
    /// television, eight guests, and eight stands that dress them for it. Dev tools menu:
    /// Shift Change, Dev tools, Build rec room stage, then click a cell.
    ///
    /// <para>The pool stage exercises one joy giver, Odyssey's <c>GoSwimming</c>
    /// (<c>baseChance</c> 1, joyKind Meditative), and a kind whose tolerance passes 0.5 is
    /// skipped outright, so that fixture can stall. Here four kinds are on offer, so boredom
    /// on one cannot stall the room: billiards (Gaming_Dexterity, <c>baseChance</c> 4), poker
    /// (Gaming_Cerebral, 4), television (3.5) and the harp (HighCulture, 2).</para>
    ///
    /// <para>Each joy job caps its own table through <c>joyMaxParticipants</c>:
    /// <c>Play_Billiards</c> 2, <c>Play_Poker</c> 4, <c>Play_MusicalInstrument</c> 1,
    /// <c>WatchTelevision</c> 8. A watcher also needs a sittable inside the set's watch rect,
    /// and the two couches supply four, so eight guests have eleven places and the harp's is
    /// the likeliest to stay empty.</para>
    /// </summary>
    internal static class DebugTools_RecRoomStage
    {
        internal const int InteriorWidth = 15;
        internal const int InteriorHeight = 11;
        internal const int Width = InteriorWidth + 2;
        internal const int Height = InteriorHeight + 2;

        /// <summary>Door offset along the south wall.</summary>
        internal const int DoorX = 7;

        /// <summary>
        /// Low enough that the joy think node runs on the next roll, not so low
        /// the guest reads as distressed.
        /// </summary>
        internal const float DrainedJoy = 0.15f;

        internal const float HeatPush = 3000f;

        // Evening wear is dyed, not styled: the game has no dress apparel in Core or any DLC,
        // so the women wear Apparel_RobeRoyal (Royalty), the nearest thing to a gown. ATH's
        // Female Dresses style (Anthitei.ATHsStyleFemaleDresses.Style) would render real gowns
        // if set as the piece's StyleDef, but it is not a dependency of anything here.
        //
        // BlackTie is 22% lightness: apparel tint multiplies the texture, so darker flattens
        // the tonal range until the vest's shadows merge with the pawn outline, and lighter
        // stops reading as black against marble.
        internal static readonly Color BlackTie = new Color(0.22f, 0.22f, 0.27f);
        internal static readonly Color DressWhite = new Color(0.95f, 0.94f, 0.92f);
        internal static readonly Color Scarlet = new Color(0.72f, 0.09f, 0.15f);
        internal static readonly Color Gold = new Color(0.85f, 0.68f, 0.24f);
        internal static readonly Color Emerald = new Color(0.07f, 0.44f, 0.29f);
        internal static readonly Color Sapphire = new Color(0.15f, 0.29f, 0.63f);

        /// <summary>
        /// Builds the stage at the clicked cell. Registered from <see cref="DebugTools_Menu"/>,
        /// not by its own attribute, so the dev tools stay one collapsing entry.
        /// </summary>
        internal static void BuildRecRoomStage()
        {
            Map map = Find.CurrentMap;
            IntVec3 origin = UI.MouseCell();

            // Three cells wider than the room, to the west: the power cell sits outside the
            // west wall, so it never appears in a shot of the room.
            CellRect footprint = new CellRect(origin.x - 3, origin.z, Width + 3, Height);
            if (!footprint.InBounds(map))
            {
                Messages.Message("Rec room stage does not fit here — click further from the map edge.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
            if (standDef == null)
            {
                Messages.Message("Odyssey outfit stand def not found — cannot build the rec room stage.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            GenDebug.ClearArea(footprint, map);
            Current.Game.playSettings.useWorkPriorities = true;

            UnlockEverything();

            BuildShell(map, origin);
            CellRect interior = new CellRect(origin.x + 1, origin.z + 1, InteriorWidth, InteriorHeight);

            Power(map, origin, interior);
            Amusements(map, origin);

            // One stand per guest down the west wall, alternating by sex so the row reads as
            // a party; guest i is assigned stand i (see Patron). Eight stands fit between the
            // corner lamps at z 1 and 11.
            (string Nick, Gender Sex, string Outer, string Hat, Color Frock)[] guests =
            {
                ("Ash",   Gender.Male,   "Apparel_VestRoyal", "Apparel_HatTop",    BlackTie),
                ("Cleo",  Gender.Female, "Apparel_RobeRoyal", "Apparel_HatLadies", Scarlet),
                ("Bram",  Gender.Male,   "Apparel_VestRoyal", "Apparel_HatTop",    BlackTie),
                ("Della", Gender.Female, "Apparel_RobeRoyal", "Apparel_HatLadies", Gold),
                ("Emil",  Gender.Male,   "Apparel_VestRoyal", "Apparel_HatTop",    BlackTie),
                ("Greta", Gender.Female, "Apparel_RobeRoyal", "Apparel_HatLadies", Emerald),
                ("Finn",  Gender.Male,   "Apparel_VestRoyal", "Apparel_HatTop",    BlackTie),
                ("Hana",  Gender.Female, "Apparel_RobeRoyal", "Apparel_HatLadies", Sapphire),
            };

            for (int i = 0; i < guests.Length; i++)
            {
                Building_OutfitStand stand = Dress(map, standDef,
                    new IntVec3(interior.minX, 0, interior.maxZ - 1 - i),
                    guests[i].Outer, guests[i].Hat, guests[i].Frock, DressWhite);
                Patron(map, new IntVec3(interior.minX + 3 + i, 0, interior.minZ),
                       guests[i].Sex, guests[i].Nick, stand);
            }

            GenTemperature.PushHeat(new IntVec3(origin.x + DoorX, 0, interior.minZ + 1), map, HeatPush);

            Messages.Message("Rec room stage built; all eight stands dress for recreation, and the "
                + "research tree is unlocked for dev-mode editing. Use Drain recreation on a guest "
                + "(or wait for a joy roll): they should change into formal wear at their own stand "
                + "FIRST, play, and change back on their first job outside the room. Export scene "
                + "turns whatever you build here into code.",
                MessageTypeDefOf.TaskCompletion, historical: false);
        }

        /// <summary>
        /// Finishes every research project, so the architect menu offers the whole catalogue
        /// while the set is dressed by hand. Permanent for the colony; DebugTools_SleepStage
        /// calls it too.
        /// </summary>
        internal static void UnlockEverything()
        {
            ResearchManager research = Find.ResearchManager;
            if (research == null)
            {
                return;
            }
            // FinishProject finishes prerequisites itself, so the only guard needed is
            // IsFinished (asking the manager for a category's current project would return
            // null for most categories). Letters and dialogs are off: ~150 completions would
            // otherwise bury the screen in popups.
            foreach (ResearchProjectDef project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                if (!project.IsFinished)
                {
                    research.FinishProject(project, doCompletionDialog: false, null,
                                           doCompletionLetter: false);
                }
            }
        }

        /// <summary>
        /// Walls, door, roof and floor: <c>TileMarble</c> under the walls, <c>FineTileMarble</c>
        /// across the interior (fine tile is what draws the chequerboard, not two alternating
        /// defs) and two <c>CarpetFineAuburn</c> insets under the games.
        /// </summary>
        internal static void BuildShell(Map map, IntVec3 origin)
        {
            Floor(map, origin, "TileMarble", 0, 0, Width - 1, Height - 1);
            Floor(map, origin, "FineTileMarble", 1, 1, InteriorWidth, InteriorHeight);
            Floor(map, origin, "CarpetFineAuburn", 10, 2, 6, 6);
            Floor(map, origin, "CarpetFineAuburn", 5, 6, 4, 5);

            for (int rx = 0; rx < Width; rx++)
            {
                for (int rz = 0; rz < Height; rz++)
                {
                    IntVec3 cell = origin + new IntVec3(rx, 0, rz);
                    map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                    bool wall = rx == 0 || rx == Width - 1 || rz == 0 || rz == Height - 1;
                    if (!wall)
                    {
                        continue;
                    }
                    Spawn(map, origin, rx == DoorX && rz == 0 ? "Door" : "Wall",
                          "BlocksMarble", rx, rz, Rot4.North);
                }
            }

            Doormats(map, origin);
        }

        /// <summary>
        /// A doormat on the door tile, facing out: guests arrive across open ground in evening
        /// dress, and the mat takes the filth they carry at the threshold instead of the marble.
        ///
        /// <para>The mat shares the door's cell because the def is built for it
        /// (<c>isEdifice false</c>, <c>clearBuildingArea false</c>), so nothing in
        /// <c>GenSpawn.SpawningWipes</c> fires for it against the door. It acts only on
        /// <c>IsHashIntervalTick(10)</c>, and a pawn waiting for the door dwells at the
        /// threshold longer than it would crossing open ground.</para>
        ///
        /// <para>Rotation does not change the cleaning (<c>LT.Building_DoorMat.Tick</c> acts on
        /// any pawn standing on its own cells) but decides which edge of the cell the mat
        /// hugs: the art fills only the upper ~60% of its texture and the <c>Graphic_Single</c>
        /// spins with the building. South puts it on the outward half; North would tuck it
        /// inside the room.</para>
        ///
        /// <para>Mod content (<c>dracoix.doormat.r12a</c>), skipped silently on a lighter
        /// modlist. The plain mat is stuffable, so it takes a material; the coloured variants
        /// would take null.</para>
        /// </summary>
        internal static void Doormats(Map map, IntVec3 origin)
        {
            Spawn(map, origin, "LT_DoorMatLeather", "Cloth", DoorX, 0, Rot4.South);
        }

        /// <summary>
        /// Lighting and power. The vanometric cell sits outside the west wall and feeds hidden
        /// conduits, so no power fixture appears in a shot and no fuel, sun or flat battery can
        /// be mistaken for the mod failing.
        ///
        /// <para>The conduit runs under every wall, not only the feed row: a consumer connects
        /// to the nearest transmitter within six cells of it on each axis
        /// (<c>PowerConnectionMaker.BestTransmitterForConnector</c>) and the room is thirteen
        /// cells deep, so one run along the south side left the north wall's television dark.
        /// The ring puts a transmitter within one cell of every edge. Conduits are not
        /// edifices, so they share the wall cells instead of wiping the walls, and a wall
        /// attachment measures from the wall it hangs on, which is where the ring is.</para>
        /// </summary>
        internal static void Power(Map map, IntVec3 origin, CellRect interior)
        {
            ThingDef cellDef = DefDatabase<ThingDef>.GetNamedSilentFail("VanometricPowerCell");
            ThingDef conduit = DefDatabase<ThingDef>.GetNamedSilentFail("HiddenConduit")
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("PowerConduit");

            IntVec3 supply = new IntVec3(origin.x - 2, 0, origin.z + 1);
            if (cellDef != null)
            {
                DebugTools_Fixtures.Spawn(map, cellDef, null, supply, Rot4.North);
            }

            if (conduit != null)
            {
                // The feed, from the cell in to the west wall.
                for (int x = supply.x; x <= origin.x; x++)
                {
                    DebugTools_Fixtures.Spawn(map, conduit, null,
                                              new IntVec3(x, 0, supply.z), Rot4.North);
                }
                for (int rx = 0; rx < Width; rx++)
                {
                    Spawn(map, origin, conduit.defName, null, rx, 0, Rot4.North);
                    Spawn(map, origin, conduit.defName, null, rx, Height - 1, Rot4.North);
                }
                for (int rz = 0; rz < Height; rz++)
                {
                    Spawn(map, origin, conduit.defName, null, 0, rz, Rot4.North);
                    Spawn(map, origin, conduit.defName, null, Width - 1, rz, Rot4.North);
                }
            }

            // Corner lamps, plus a wall lamp on every other cell of the stands' wall. A wall
            // lamp is an attachment, not an edifice, so it shares an interior cell with a
            // stand and draws onto the wall it hangs off.
            Spawn(map, origin, "StandingLamp", null, 1, 1, Rot4.North);
            Spawn(map, origin, "StandingLamp", null, 15, 1, Rot4.North);
            Spawn(map, origin, "StandingLamp", null, 1, 11, Rot4.North);
            Spawn(map, origin, "StandingLamp", null, 15, 11, Rot4.North);
            for (int z = 4; z <= 10; z += 2)
            {
                Spawn(map, origin, "WallLamp", null, 1, z, Rot4.West);
            }
        }

        /// <summary>
        /// The set, as dressed by hand and exported: poker, billiards, a harp and stool, and two
        /// couches facing a flatscreen on the north wall, which works only because the conduit
        /// ring reaches that wall.
        /// </summary>
        internal static void Amusements(Map map, IntVec3 origin)
        {
            Spawn(map, origin, "PokerTable", "WoodLog", 12, 4, Rot4.North);
            Spawn(map, origin, "Armchair", "Leather_Thrumbo", 12, 3, Rot4.North);
            Spawn(map, origin, "Armchair", "Leather_Thrumbo", 14, 4, Rot4.West);
            Spawn(map, origin, "Armchair", "Leather_Thrumbo", 11, 5, Rot4.East);
            Spawn(map, origin, "Armchair", "Leather_Thrumbo", 13, 6, Rot4.South);

            Spawn(map, origin, "BilliardsTable", "WoodLog", 6, 8, Rot4.North);

            // The harp faces East so that its interaction cell, one cell west, is the stool at
            // (6,3): InteractionCell rotates the harp's interactionCellOffset (0,0,-1) with the
            // building, so North would send the musician to the bare cell south of it. A harp
            // placed from the build menu faces North unless rotated, and the scene export copies
            // each thing's rotation, so keep East even if an export of a hand-dressed room says
            // North.
            Spawn(map, origin, "Stool", "WoodLog", 6, 3, Rot4.North);
            Spawn(map, origin, "Harp", null, 7, 3, Rot4.East);

            // The couches sit at z 9, two cells south of the screen, and the pair spans x 11-14
            // against the screen's x 12-13, centred under it. A flatscreen's watch rect starts
            // two cells out (watchBuildingStandDistanceRange 2~6) and is six wide, and
            // WatchBuildingUtility.TryFindBestWatchCell only takes a seat whose rotation faces
            // the screen, so a couch anywhere else is furniture nobody sits on.
            Spawn(map, origin, "Couch", "Leather_Thrumbo", 11, 9, Rot4.North);
            Spawn(map, origin, "Couch", "Leather_Thrumbo", 13, 9, Rot4.North);
            Spawn(map, origin, "FlatscreenTelevision", null, 13, 11, Rot4.South);
        }

        /// <summary>Paints a terrain rectangle offset from the room's south-west corner.</summary>
        internal static void Floor(Map map, IntVec3 origin, string defName,
                                   int x, int z, int width, int height)
        {
            TerrainDef def = DefDatabase<TerrainDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return;
            }
            foreach (IntVec3 cell in new CellRect(origin.x + x, origin.z + z, width, height))
            {
                if (cell.InBounds(map))
                {
                    map.terrainGrid.SetTerrain(cell, def);
                }
            }
        }

        /// <summary>
        /// Spawns one thing with the arguments the scene export writes for it, so exported
        /// lines paste in (a colour argument the export may add is not taken). A missing def is
        /// skipped, not thrown: half this set is DLC or mod content, and a stage that builds
        /// slightly emptier on a lighter modlist beats one that refuses to build.
        /// </summary>
        internal static void Spawn(Map map, IntVec3 origin, string defName, string stuff,
                                   int x, int z, Rot4 rot)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return;
            }
            ThingDef material = stuff == null ? null : DefDatabase<ThingDef>.GetNamedSilentFail(stuff);
            if (def.MadeFromStuff && material == null)
            {
                material = GenStuff.DefaultStuffFor(def);
            }
            DebugTools_Fixtures.Spawn(map, def, material, origin + new IntVec3(x, 0, z), rot);
        }

        /// <summary>
        /// One stand, declared for recreation, holding a formal shirt plus the outer piece (the
        /// Male-only vest or the unrestricted prestige robe) and hat it is given, dyed to the
        /// set's colour. Full change stays off: a dress code replaces the outer layers and does
        /// not strip the guest.
        /// </summary>
        /// <param name="shirt">
        /// Tint of the formal shirt under the outer piece, or null for none. Every guest gets
        /// one, or the shirt they arrived in shows at the collar and cuffs.
        /// </param>
        internal static Building_OutfitStand Dress(Map map, ThingDef standDef, IntVec3 cell,
                                                   string gendered, string hat,
                                                   Color colour, Color? shirt)
        {
            // Qualified: this class's own Spawn takes the export's signature and would hide
            // the fixture one.
            Building_OutfitStand stand = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                map, standDef, ThingDefOf.WoodLog, cell, Rot4.East);

            if (shirt.HasValue)
            {
                Put(stand, "Apparel_ShirtRuffle", shirt.Value);
            }
            Put(stand, gendered, colour);
            Put(stand, hat, colour);

            CompShiftStand comp = stand.TryGetComp<CompShiftStand>();
            if (comp != null && !comp.HandlesRecreation())
            {
                comp.ToggleRecreation();
            }
            return stand;
        }

        internal static void Put(Building_OutfitStand stand, string defName, Color colour)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                Messages.Message(defName + " not found — is Royalty loaded? That stand is short a "
                    + "piece.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            if (!stand.AddApparel(MakeGarment(def, colour)))
            {
                Messages.Message(defName + " did not fit on the stand — it conflicts with a piece "
                    + "already on it.", MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        /// <summary>
        /// A guest: own clothes on, every work priority off so nothing competes with joy, needs
        /// topped up except joy, and their own stand assigned.
        ///
        /// <para>One stand per guest, assigned: a stand holds one outfit, and the frocks differ
        /// in colour. A stand also serves anyone who can wear something from it
        /// (<c>SwapPlan.WouldDress</c>), and each garment is then filtered on its own
        /// (<c>PawnCanWear</c>). Only the vest and top hat (Male) and the ladies hat (Female)
        /// are gender-locked, so a man at a women's stand would wear the formal shirt and the
        /// prestige robe and skip only the hat. Assigned stands are exclusive
        /// (<c>CompShiftStand.CanBeClaimedBy</c>), which prevents that.</para>
        /// </summary>
        internal static void Patron(Map map, IntVec3 cell, Gender gender, string nick,
                                    Building_OutfitStand stand)
        {
            if (!cell.InBounds(map))
            {
                return;
            }

            Pawn pawn = AveragePawn(gender, nick);
            DressInStartingKit(pawn, researcher: false);
            GenSpawn.Spawn(pawn, cell, map);

            pawn.workSettings.EnableAndInitialize();
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (work.visible && !pawn.WorkTypeIsDisabled(work))
                {
                    pawn.workSettings.SetPriority(work, 0);
                }
            }

            stand?.TryGetComp<CompAssignableToPawn_ShiftStand>()?.TryAssignPawn(pawn);

            DebugTools_DemoStage.TopUpNeeds(pawn, 1f);
            if (pawn.needs?.joy != null)
            {
                pawn.needs.joy.CurLevel = DrainedJoy;
            }
        }
    }
}
#endif
