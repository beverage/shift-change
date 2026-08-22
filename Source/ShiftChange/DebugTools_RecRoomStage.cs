// SCENES only — see the config table in ShiftChange.csproj. This file BUILDS A
// SCENE and must never reach a player: it clears its footprint (destroying any
// pawn standing in it), rewrites terrain and roof, unlocks the whole research
// tree, and leaves permanent player-faction colonists and buildings behind. The
// fixture primitives live in DebugTools_Fixtures, which always compiles because
// the harness needs them in Release; SpawnTorch/TopUpNeeds come from
// DebugTools_DemoStage, which is SCENES-gated too.
#if SCENES
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using static ShiftChange.DebugTools_Fixtures;

namespace ShiftChange
{
    /// <summary>
    /// A black-tie games room: billiards, poker, a harp and a television, eight
    /// guests, and eight stands that dress them for it.
    ///
    /// <para><b>Why a second recreation fixture.</b> The pool stage exercises
    /// exactly one giver — Odyssey's <c>GoSwimming</c>, at <c>baseChance</c> 1
    /// and <c>joyKind Meditative</c>, the most crowded kind in the game. That
    /// makes it slow to trigger and, once Meditative tolerance passes 0.5, unable
    /// to trigger at all. This room offers <b>four kinds at higher weights</b> —
    /// Gaming_Dexterity (billiards, 4), Gaming_Cerebral (poker, 4), Television
    /// (3.5) and HighCulture (harp, 2) — so boredom on one cannot stall the
    /// fixture.</para>
    ///
    /// <para><b>The dress code is the point.</b> These stands hold formal wear,
    /// not a uniform: a guest changes to go and enjoy themselves. That is the
    /// venue case rather than the workplace one, and it is the shape a bar or a
    /// lounge would use.</para>
    ///
    /// <para><b>Eight guests, eleven places.</b> Every joy job caps its own
    /// table through <c>joyMaxParticipants</c>: <c>Play_Billiards</c> reserves
    /// the table for 2, <c>Play_Poker</c> for 4,
    /// <c>Play_MusicalInstrument</c> for 1, and <c>WatchTelevision</c> for 8 —
    /// though a watcher also needs a sittable inside the set's watch rect,
    /// which the two couches supply four of. Eleven places for eight guests
    /// means three sit empty in any one frame, and the harp is the likeliest
    /// of them: at <c>baseChance</c> 2 it is the lowest-weighted giver in the
    /// room. That is the deliberate trade — the set is dressed for the camera
    /// first, an empty corner reads worse than an empty chair, and the harp
    /// buys a fourth joyKind the three tables cannot.</para>
    ///
    /// <para><b>One stand per guest.</b> The men's piece is gendered —
    /// <c>Apparel_VestRoyal</c> is <c>Male</c> in its own def and
    /// <c>PawnCanWear</c> enforces it — so a stand shared across the room would
    /// silently dress only half of it. The women's <c>Apparel_RobeRoyal</c> has
    /// no gender restriction, but they still get a stand each because the two
    /// frocks are different colours, and a stand holds one outfit.</para>
    ///
    /// <para><b>On the women's frocks.</b> RimWorld has no dress apparel at all
    /// — not in Core, not in any DLC, and not in any mod on this machine.
    /// <c>Apparel_RobeRoyal</c> ("prestige robe", Royalty) is the closest thing:
    /// long, flowing, embroidered, and unisex. Dyed scarlet and gold it reads as
    /// evening wear. If ATH's style Female Dresses
    /// (<c>Anthitei.ATHsStyleFemaleDresses.Style</c>) is ever enabled, it maps
    /// several garments to dress-shaped styles and setting <c>StyleDef</c> on
    /// the piece would render an actual gown with no other change here.</para>
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

        // Black tie, and four frocks. Dyed rather than styled, because the style
        // pack is not a dependency of anything here.
        //
        // BlackTie sits at 22% lightness rather than the 13% it started on.
        // Apparel tint MULTIPLIES the texture, so the surviving tonal range is
        // proportional to the tint's own luminance: measured off a capture, the
        // vest at 13% kept a 43-level spread against the scarlet robe's 167,
        // and its shadow tones had merged with the pawn outline. 22% roughly
        // doubles that and still reads as black against marble, which sits
        // around 65-70%.
        internal static readonly Color BlackTie = new Color(0.22f, 0.22f, 0.27f);
        internal static readonly Color DressWhite = new Color(0.95f, 0.94f, 0.92f);
        internal static readonly Color Scarlet = new Color(0.72f, 0.09f, 0.15f);
        internal static readonly Color Gold = new Color(0.85f, 0.68f, 0.24f);
        internal static readonly Color Emerald = new Color(0.07f, 0.44f, 0.29f);
        internal static readonly Color Sapphire = new Color(0.15f, 0.29f, 0.63f);

        /// <summary>
        /// Registered from <see cref="DebugTools_Menu"/>, not by its own
        /// attribute — one collapsing entry, never a category of loose ones.
        /// </summary>
        internal static void BuildRecRoomStage()
        {
            Map map = Find.CurrentMap;
            IntVec3 origin = UI.MouseCell();

            // The footprint is wider than the room: the power cell sits OUTSIDE
            // it, west of the wall, so it never appears in a shot of the room.
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

            // Dev mode needs the whole tree available to redress this scene by
            // hand — a build menu missing half its furniture is not a scene you
            // can iterate on.
            UnlockEverything();

            BuildShell(map, origin);
            CellRect interior = new CellRect(origin.x + 1, origin.z + 1, InteriorWidth, InteriorHeight);

            Power(map, origin, interior);
            Amusements(map, origin);

            // Stands run contiguously down the west wall, one per guest,
            // alternating so the row reads as a party rather than four black
            // suits followed by four frocks. The pairing is not cosmetic: the
            // men's piece is gendered, so a guest sent to the wrong stand
            // would find nothing wearable.
            //
            // Eight stands fit between the corner lamps at z 1 and 11, and the
            // four wall lamps share cells with four of them — a wall lamp is
            // an attachment, not an edifice, so it occupies the same interior
            // cell it hangs off and neither wipes the other.
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
        /// Every research project finished, so the architect menu offers the
        /// whole catalogue while dressing the set by hand.
        /// </summary>
        internal static void UnlockEverything()
        {
            ResearchManager research = Find.ResearchManager;
            if (research == null)
            {
                return;
            }
            // FinishProject recurses into prerequisites itself (:405), so the
            // only guard needed is "already done" — and asking the manager for
            // the CURRENT project of a category would return null on any
            // category with nothing selected, which is most of them.
            //
            // Letters and dialogs off: unlocking ~150 projects would otherwise
            // bury the screen in completion popups.
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
        /// The shell, floored as exported: <c>TileMarble</c> under the walls,
        /// <c>FineTileMarble</c> across the interior — fine tile is what draws
        /// the chequerboard, it is not two alternating defs — and two
        /// <c>CarpetFineAuburn</c> insets under the games.
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
        /// A doormat ON the door tile, facing out. Guests arrive across open
        /// ground in evening dress and track soil onto the marble all the way
        /// to the billiards table; the mat takes the carried filth at the
        /// threshold.
        ///
        /// <para><b>It shares the door's cell, which is what the def is built
        /// for.</b> <c>isEdifice false</c> and <c>clearBuildingArea false</c>
        /// are exactly the flags that let a building sit in an occupied cell,
        /// and nothing in <c>GenSpawn.SpawningWipes</c> fires for a Standable
        /// non-edifice against a door. The threshold is also the best cell
        /// available for the job: the mat acts only on
        /// <c>IsHashIntervalTick(10)</c>, and a pawn waiting for a door to open
        /// dwells there longer than it would crossing open ground.</para>
        ///
        /// <para><b>Rotation is cosmetic to the cleaning and load-bearing to
        /// the look.</b> <c>LT.Building_DoorMat.Tick</c> iterates
        /// <c>this.OccupiedRect()</c> and calls <c>HavePawnsDropFilth</c> on
        /// any pawn standing on its OWN cells, so the cell cleans however the
        /// mat is turned. But the art occupies only the upper ~60% of its 64x64
        /// texture, and <c>Graphic_Single</c> with <c>rotatable true</c> and no
        /// <c>drawRotated false</c> spins with the building — so rotation
        /// decides which EDGE of the cell the mat hugs. <c>South</c> puts it on
        /// the outward half of the threshold, where a guest wipes their feet
        /// before stepping in; <c>North</c> would tuck it inside the room and
        /// leave the approach bare.</para>
        ///
        /// <para>Mod content (<c>dracoix.doormat.r12a</c>), so silently absent
        /// on a lighter modlist — the same rule the rest of this set follows.
        /// The plain mat is the stuffable one, so it takes a material; the
        /// coloured variants are fixed-cost cloth and would take null.</para>
        /// </summary>
        internal static void Doormats(Map map, IntVec3 origin)
        {
            Spawn(map, origin, "LT_DoorMatLeather", "Cloth", DoorX, 0, Rot4.South);
        }

        /// <summary>
        /// Lighting and power. The vanometric cell sits OUTSIDE the west wall
        /// and feeds HIDDEN conduits, so nothing in a shot of the room is a
        /// power fixture and there is no fuel, sun or flat battery to be
        /// mistaken for the mod failing.
        ///
        /// <para><b>The conduit runs under EVERY wall, not just the feed row.</b>
        /// A consumer searches a SIX-cell box around itself for a transmitter
        /// (<c>PowerConnectionMaker.BestTransmitterForConnector:103</c>) and
        /// this room is thirteen cells deep, so a single run along the south
        /// side powered the near half and left the north wall's television —
        /// ten cells away — dark. An unlit fixture in a fixture room reads as
        /// this mod failing rather than as a missing watt, which is the whole
        /// confusion the stage exists to avoid.
        ///
        /// A ring puts a transmitter within one cell of every edge. Conduits
        /// are not edifices, so they share the wall cells rather than wiping
        /// the walls, and a wall attachment measures from the WALL it hangs on
        /// (<c>:43</c>) — which is exactly where the ring is.</para>
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
                // Then the ring, under all four walls.
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

            // Four corners, plus a wall lamp on every other cell of the
            // changing wall so the row of stands is lit for the camera. They
            // sit in the same cells as four of the eight stands, which is what
            // a wall attachment does — it occupies the interior cell and draws
            // onto the wall it hangs off.
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
        /// The set, as dressed by hand and exported: poker in the east corner
        /// with four armchairs, billiards on the second carpet, a harp and
        /// stool on the open floor, and two couches facing a flatscreen along
        /// the north wall.
        ///
        /// <para>Four joyKinds on purpose — Gaming_Dexterity (billiards),
        /// Gaming_Cerebral (poker), Television and HighCulture (harp) — so a
        /// bored tolerance on any one cannot stall the fixture. The television
        /// works here only because the conduit ring reaches the north wall.</para>
        ///
        /// <para><b>The harp faces EAST, and that is not cosmetic.</b> Its
        /// <c>interactionCellOffset</c> is <c>(0,0,-1)</c> and
        /// <c>ThingUtility.InteractionCell</c> rotates that offset with the
        /// building (<c>IntVec3Utility.RotatedBy</c>, <c>AsInt 1</c> maps
        /// <c>(x,z)</c> to <c>(z,-x)</c>), so North puts the musician one cell
        /// SOUTH of the harp and East puts them one cell WEST — on the stool.
        /// The build menu starts a harp at South, since <c>defaultPlacingRot</c>
        /// defaults there, so a hand-placed harp has been rotated and the
        /// rotation is the whole reason the stool is furniture rather than
        /// scenery. A scene export of this room reported <c>Rot4.North</c>;
        /// transcribing that verbatim sends the guest to the bare cell south of
        /// the harp and the stool is never sat on.</para>
        ///
        /// <para>The couches sit at z 9, two cells south of the screen, and the
        /// pair spans x 11-14 against the screen's x 12-13 — centred under it.
        /// Both facts are load-bearing: a flatscreen's watch rect starts two
        /// cells out (<c>watchBuildingStandDistanceRange 2~6</c>) and is six
        /// wide, and <c>WatchBuildingUtility.TryFindBestWatchCell</c> only
        /// takes a seat whose rotation faces the screen. A couch parked
        /// anywhere else is furniture nobody ever sits on.</para>
        /// </summary>
        internal static void Amusements(Map map, IntVec3 origin)
        {
            Spawn(map, origin, "PokerTable", "WoodLog", 12, 4, Rot4.North);
            Spawn(map, origin, "Armchair", "Leather_Thrumbo", 12, 3, Rot4.North);
            Spawn(map, origin, "Armchair", "Leather_Thrumbo", 14, 4, Rot4.West);
            Spawn(map, origin, "Armchair", "Leather_Thrumbo", 11, 5, Rot4.East);
            Spawn(map, origin, "Armchair", "Leather_Thrumbo", 13, 6, Rot4.South);

            Spawn(map, origin, "BilliardsTable", "WoodLog", 6, 8, Rot4.North);

            // East, not the exported North — the stool at (6,3) is the harp's
            // interaction cell only under East. See the note above.
            Spawn(map, origin, "Stool", "WoodLog", 6, 3, Rot4.North);
            Spawn(map, origin, "Harp", null, 7, 3, Rot4.East);

            Spawn(map, origin, "Couch", "Leather_Thrumbo", 11, 9, Rot4.North);
            Spawn(map, origin, "Couch", "Leather_Thrumbo", 13, 9, Rot4.North);
            Spawn(map, origin, "FlatscreenTelevision", null, 13, 11, Rot4.South);
        }

        /// <summary>
        /// Paint a rectangle of terrain, in the export's own coordinates.
        /// </summary>
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
        /// Spawn one thing in the export's own coordinates and signature, so a
        /// fresh `Export scene` pastes in here verbatim. A missing def is
        /// skipped rather than thrown: half this set is DLC or mod content, and
        /// a stage that refuses to build on a lighter modlist is worth less
        /// than one that builds slightly emptier.
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
        /// One stand, declared for recreation, holding a formal shirt plus the
        /// gendered piece and hat it is given, dyed to the set's colour. Full
        /// change stays OFF: a dress code replaces the outer layers, it does not
        /// strip the guest.
        /// </summary>
        /// <param name="shirt">
        /// The formal base layer, under the outer piece. Everyone gets one:
        /// without it a guest wears evening dress over the shirt they arrived
        /// in, which is visible at the collar and cuffs and is exactly the
        /// half-changed look the dress code exists to avoid.
        /// </param>
        internal static Building_OutfitStand Dress(Map map, ThingDef standDef, IntVec3 cell,
                                                   string gendered, string hat,
                                                   Color colour, Color? shirt)
        {
            // Qualified: the local Spawn overload below takes the export's
            // signature and would otherwise shadow the fixture primitive.
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
        /// A guest: own clothes on, every work priority off so nothing competes
        /// with joy, needs topped up except joy, and their own stand assigned.
        ///
        /// <para>The assignment is what keeps the set coherent, and the failure
        /// it prevents is not "finds nothing to wear" — it is worse than that.
        /// A stand serves anyone who can wear SOMETHING on it
        /// (<c>SwapPlan.WouldDress</c>), and each garment is then filtered on
        /// its own. Only three of the five pieces are gender-locked — the vest
        /// and top hat to Male, the ladies hat to Female — so a man claiming a
        /// shared women's stand would wear the formal shirt AND the prestige
        /// robe, and skip only the hat. Assigned stands are exclusive
        /// (<c>CompShiftStand.CanBeClaimedBy</c>), which is the fix.</para>
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
