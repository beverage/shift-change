// SCENES only — see the config table in ShiftChange.csproj. This file BUILDS
// SCENES and must never reach a player: they clear their footprint (destroying
// any pawn standing in it), rewrite terrain and roof, unlock the whole research
// tree, and leave permanent player-faction colonists and buildings behind. The
// fixture primitives live in DebugTools_Fixtures, which always compiles because
// the harness needs them in Release; UnlockEverything comes from
// DebugTools_RecRoomStage, SpawnTorch/TopUpNeeds from DebugTools_DemoStage, and
// the card stage's shell from DebugTools_PreviewStage — all SCENES-gated too.
#if SCENES
using RimWorld;
using UnityEngine;
using Verse;
using static ShiftChange.DebugTools_Fixtures;

namespace ShiftChange
{
    /// <summary>
    /// Bedtime in prestige cataphract, in the two shapes the gallery needs.
    ///
    /// <para><b>Build sleep stage</b> is the comparison: two 5×5 bedrooms side
    /// by side off one shared corridor, a soldier in each, both about to turn
    /// in. West DEPOSITS the armour into an empty rack and sleeps in what was
    /// underneath; east SWAPS it for a light kit on the stand. The subject is
    /// the MODE, not the bedtime — they are two answers to one situation, and a
    /// viewer can only weigh them side by side. One build, one capture, one
    /// gif.</para>
    ///
    /// <para><b>Build sleep card stage</b> is the same room alone, at the
    /// preview stage's exact block geometry, for a 320×360 gallery panel. It
    /// borrows <see cref="DebugTools_PreviewStage.BuildShell"/> and
    /// <see cref="DebugTools_PreviewStage.InteriorOf"/> wholesale rather than
    /// reproducing the numbers, so a sleep card and a work card can never drift
    /// apart on framing — the same reason the preview stage borrows its room
    /// dressing from the demo stage.</para>
    ///
    /// <para><b>The deposit half is visually quieter, and that is known.</b> The
    /// west soldier walks up and the armour disappears into the rack; the east
    /// one plays two visible changes. Do not fix that by speeding one side up —
    /// the rooms have to read at the same rate for the comparison to survive,
    /// which is the lesson the rec room gif's ramp already paid for. It is also
    /// why the CARD stage stages the swap rather than the deposit: a card is one
    /// still, and an empty rack in a still reads as nothing happening.</para>
    ///
    /// <para><b>THE TRAP THIS FILE EXISTS TO ENCODE: every soldier wears a shirt
    /// and trousers UNDER the armour.</b> A deposit-only stand refuses outright
    /// when what it would take leaves the colonist psychologically nude
    /// (<see cref="SwapPlan.WouldBeNude"/>) — and prestige cataphract covers
    /// torso and legs, so a soldier wearing nothing underneath is one the west
    /// stand declines forever. Hand-build this scene without the base layer and
    /// the feature looks broken. It is not.</para>
    ///
    /// <para><b>The deposit filter is NARROWED, deliberately.</b> An outfit
    /// stand ships <c>defaultStorageSettings</c> allowing the whole Apparel
    /// category minus ApparelUtility and Weapons, so out of the box it would
    /// try to take the base layer too — and then decline under the rule above,
    /// which reads as "nothing happens". Disallow-all, then allow exactly the
    /// two armour pieces. That is the configuration a player performs, so the
    /// stage performs it.</para>
    ///
    /// <para><b>Both triggers are AUTOMATIC.</b> No stand is told to serve
    /// sleep; each room holds one owned bed, which scores it Bedroom, and the
    /// Bedroom row in the role table does the rest. Worth demonstrating rather
    /// than bypassing — it is the path a player gets by building a stand in a
    /// bedroom and touching nothing.</para>
    /// </summary>
    internal static class DebugTools_SleepStage
    {
        /// <summary>Square rooms, matching the demo stage's.</summary>
        internal const int RoomInterior = 5;

        /// <summary>Shared with both neighbours, so the two rooms open onto ONE corridor.</summary>
        internal const int CorridorHeight = 2;

        /// <summary>Outer wall, interior, party wall, interior, outer wall.</summary>
        internal const int Width = RoomInterior * 2 + 3;

        /// <summary>South wall, corridor, divider wall, interior, north wall.</summary>
        internal const int Height = RoomInterior + CorridorHeight + 3;

        /// <summary>The party wall's column, and the first interior row.</summary>
        internal const int PartyX = RoomInterior + 1;
        internal const int InteriorZ = CorridorHeight + 2;

        /// <summary>Door columns: the centre of each room's five.</summary>
        internal const int WestDoorX = 3;
        internal const int EastDoorX = PartyX + 3;

        /// <summary>
        /// Low enough that JobGiver_GetRest wins the next roll under an Anything
        /// timetable assignment (it needs CurLevel &lt; 0.3), high enough that
        /// the soldier is not staggering.
        /// </summary>
        internal const float DrainedRest = 0.15f;

        internal const float HeatPush = 3000f;

        internal static readonly Color Gunmetal = new Color(0.42f, 0.45f, 0.5f);
        internal static readonly Color Sand = new Color(0.78f, 0.72f, 0.56f);
        internal static readonly Color Underlayer = new Color(0.55f, 0.58f, 0.62f);

        // ------------------------------------------------- the comparison

        internal static void BuildSleepStage()
        {
            Map map = Find.CurrentMap;
            IntVec3 origin = UI.MouseCell();
            CellRect footprint = new CellRect(origin.x, origin.z, Width, Height);
            if (!footprint.InBounds(map))
            {
                Messages.Message("Sleep stage does not fit here — click further from the map edge.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            ThingDef standDef = StandDef();
            if (standDef == null)
            {
                return;
            }

            GenDebug.ClearArea(footprint, map);
            Current.Game.playSettings.useWorkPriorities = true;
            DebugTools_RecRoomStage.UnlockEverything();
            BuildShell(map, origin);

            CellRect west = new CellRect(origin.x + 1, origin.z + InteriorZ,
                                         RoomInterior, RoomInterior);
            CellRect east = new CellRect(origin.x + PartyX + 1, origin.z + InteriorZ,
                                         RoomInterior, RoomInterior);

            Occupant(map, standDef, west, "Vane", Gender.Female, deposit: true,
                     start: origin + new IntVec3(WestDoorX, 0, CorridorHeight));
            Occupant(map, standDef, east, "Roan", Gender.Male, deposit: false,
                     start: origin + new IntVec3(EastDoorX, 0, CorridorHeight));

            GenTemperature.PushHeat(origin + new IntVec3(WestDoorX, 0, InteriorZ), map, HeatPush);
            GenTemperature.PushHeat(origin + new IntVec3(EastDoorX, 0, InteriorZ), map, HeatPush);

            Messages.Message("Sleep stage built: two 5x5 bedrooms off one corridor. West (Vane) "
                + "deposits her armour and sleeps in the base layer; east (Roan) swaps his for a "
                + "duster and helmet. Both triggers came from the room's Bedroom role, not a "
                + "toggle. Rest is drained, so both should walk in on the next roll, change at "
                + "the stand FIRST, and change back on their first job outside the room.",
                MessageTypeDefOf.TaskCompletion, historical: false);
        }

        /// <summary>
        /// Two rooms, one corridor. Rows from the south edge: wall / corridor ×2
        /// / wall with both doors / interiors ×5 / wall.
        ///
        /// <para>The party wall stops at the divider row on purpose — it splits
        /// the two bedrooms and NOT the corridor, so both soldiers start in one
        /// continuous space and walk to their own doors. Two corridors would
        /// read as two separate stages that happen to be adjacent.</para>
        /// </summary>
        internal static void BuildShell(Map map, IntVec3 origin)
        {
            TerrainDef plank = DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor");
            IntVec3 westDoor = origin + new IntVec3(WestDoorX, 0, CorridorHeight + 1);
            IntVec3 eastDoor = origin + new IntVec3(EastDoorX, 0, CorridorHeight + 1);

            for (int rx = 0; rx < Width; rx++)
            {
                for (int rz = 0; rz < Height; rz++)
                {
                    IntVec3 cell = origin + new IntVec3(rx, 0, rz);
                    if (plank != null)
                    {
                        map.terrainGrid.SetTerrain(cell, plank);
                    }
                    map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);

                    bool wall = rx == 0 || rx == Width - 1 || rz == 0 || rz == Height - 1
                        || rz == CorridorHeight + 1
                        || (rx == PartyX && rz > CorridorHeight + 1);
                    if (!wall)
                    {
                        continue;
                    }
                    ThingDef built = cell == westDoor || cell == eastDoor
                        ? ThingDefOf.Door
                        : ThingDefOf.Wall;
                    DebugTools_Fixtures.Spawn(map, built, ThingDefOf.WoodLog, cell, Rot4.North);
                }
            }
        }

        // ------------------------------------------------------- the card

        /// <summary>
        /// One room at the preview stage's block geometry, for a gallery panel.
        /// The swap configuration, because a card is a single still and an empty
        /// rack does not read as a feature.
        /// </summary>
        internal static void BuildSleepCardStage()
        {
            Map map = Find.CurrentMap;
            IntVec3 origin = UI.MouseCell();
            CellRect footprint = new CellRect(origin.x, origin.z,
                                              DebugTools_PreviewStage.BlockWidth,
                                              DebugTools_PreviewStage.BlockHeight);
            if (!footprint.InBounds(map))
            {
                Messages.Message("Sleep card stage does not fit here — click further from the "
                    + "map edge.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            ThingDef standDef = StandDef();
            if (standDef == null)
            {
                return;
            }

            GenDebug.ClearArea(footprint, map);
            Current.Game.playSettings.useWorkPriorities = true;
            DebugTools_RecRoomStage.UnlockEverything();

            // Borrowed, not reproduced: a sleep card and a work card have to
            // crop identically or the gallery stops looking like a set.
            DebugTools_PreviewStage.BuildShell(map, origin);
            CellRect interior = DebugTools_PreviewStage.InteriorOf(origin);

            Occupant(map, standDef, interior, "Roan", Gender.Male, deposit: false,
                     start: origin + new IntVec3(DebugTools_PreviewStage.DoorX, 0, CorridorHeight));

            GenTemperature.PushHeat(interior.CenterCell, map, HeatPush);

            Messages.Message("Sleep card stage built at the preview stage's block geometry — same "
                + "8x9 capture rect, same door column, so it crops with the work cards. Roan "
                + "starts in the corridor with rest drained; he changes into the duster and "
                + "helmet at the stand, then goes to bed.",
                MessageTypeDefOf.TaskCompletion, historical: false);
        }

        // ----------------------------------------------------- the shared

        internal static ThingDef StandDef()
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
            if (def == null)
            {
                Messages.Message("Odyssey outfit stand def not found — cannot build a sleep stage.",
                    MessageTypeDefOf.RejectInput, historical: false);
            }
            return def;
        }

        /// <summary>
        /// One bedroom's contents: a bed in the middle of the floor, a stand on
        /// the west wall, and the soldier who owns both, standing in the
        /// corridor outside their own door.
        ///
        /// <para><b>The bed is centred rather than tucked into a corner</b>
        /// because these rooms are photographed square-on: a corner bed pulls
        /// the eye off-axis and leaves the opposite corner empty, which a 5×5
        /// cannot afford. Two tiles cannot centre exactly in five rows, so it
        /// sits one row north of true centre, leaving the walking space on the
        /// door side where the action is.</para>
        ///
        /// <para><b>Order matters.</b> The bed is spawned and ASSIGNED before
        /// the stand's trigger is read, because the trigger is automatic — the
        /// room only scores Bedroom once it holds a bed, and reading
        /// <c>HandlesRest</c> earlier would see a roleless room and report
        /// false.</para>
        /// </summary>
        internal static void Occupant(Map map, ThingDef standDef, CellRect interior,
                                      string nick, Gender gender, bool deposit, IntVec3 start)
        {
            // CARPET THE ROOM, because every card in the gallery has one. The
            // demo stage lays red, green and blue in its three work rooms and
            // the preview stage inherits them, so a bare plank floor here would
            // put the sleep card visibly outside the set. Dark rather than a
            // fourth primary: it is a bedroom, and the pawns — armour in one
            // beat, pale kit in the next — read far better against it than
            // against brown planks. Measured bonus from the reshoot: a flat
            // field costs a THIRD of the gif file size a plank floor does,
            // because the ditherer has almost nothing to chew on.
            //
            // Silent-fail through the list: these defs are generated by a
            // TerrainTemplateDef crossed with the structural ColorDefs, so one
            // can move between versions while the others survive.
            Carpet(map, interior, "CarpetGreyDark", "CarpetSlate", "CarpetBlack");

            // Rot4.SOUTH, matching the demo stage's hospital, and it is not a
            // free choice. GenAdj.OccupiedRect places a 1x2 building at Position
            // and Position + z whichever way it faces — rotation only swaps the
            // axes for East/West — so North and South occupy the same two cells
            // and differ ONLY in the sprite. North draws the pillow at the foot
            // end, which reads as an upside-down bed and was exactly that on the
            // first take.
            Building_Bed bed = (Building_Bed)DebugTools_Fixtures.Spawn(
                map, ThingDefOf.Bed, ThingDefOf.WoodLog,
                new IntVec3(interior.minX + interior.Width / 2, 0, interior.minZ + 2),
                Rot4.South);

            Building_OutfitStand stand = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                map, standDef, ThingDefOf.WoodLog,
                new IntVec3(interior.minX, 0, interior.minZ + 2), Rot4.East);

            DebugTools_DemoStage.SpawnTorch(map, new IntVec3(interior.maxX, 0, interior.maxZ));

            Pawn pawn = AveragePawn(gender, nick);
            if (start.InBounds(map))
            {
                GenSpawn.Spawn(pawn, start, map);
            }
            else
            {
                GenSpawn.Spawn(pawn, interior.CenterCell, map);
            }

            // THE BASE LAYER IS LOAD-BEARING, not dressing — see the class
            // comment. Without it the deposit stand declines every night.
            pawn.apparel?.DestroyAll();
            Wear(pawn, "Apparel_BasicShirt", Underlayer);
            Wear(pawn, "Apparel_Pants", Underlayer);
            Wear(pawn, "Apparel_ArmorCataphractPrestige", Gunmetal);
            Wear(pawn, "Apparel_ArmorHelmetCataphractPrestige", Gunmetal);

            bed.TryGetComp<CompAssignableToPawn>()?.TryAssignPawn(pawn);
            stand.TryGetComp<CompAssignableToPawn_ShiftStand>()?.TryAssignPawn(pawn);

            if (deposit)
            {
                DepositOnly(stand);
            }
            else
            {
                // "Flak helmet" is Apparel_AdvancedHelmet — the def name and the
                // label disagree, and reaching for Apparel_SimpleHelmet gets the
                // plain one instead.
                Put(stand, "Apparel_Duster", Sand);
                Put(stand, "Apparel_AdvancedHelmet", Sand);
            }

            // Read AFTER the bed is down and owned, so the room has a role.
            CompShiftStand comp = stand.TryGetComp<CompShiftStand>();
            if (comp != null && !comp.HandlesRest())
            {
                // Should not fire: one owned bed scores Bedroom, and Bedroom is
                // in the rest table. Kept because a modded room-role worker can
                // outscore it, and a stage that silently does nothing is worse
                // than one that quietly declares the trigger.
                comp.ToggleRest();
                if (deposit)
                {
                    DepositOnly(stand);
                }
                Messages.Message("Room did not score Bedroom for " + nick
                    + " — declared the sleep trigger by hand.",
                    MessageTypeDefOf.CautionInput, historical: false);
            }

            pawn.workSettings.EnableAndInitialize();
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (work.visible && !pawn.WorkTypeIsDisabled(work))
                {
                    pawn.workSettings.SetPriority(work, 0);
                }
            }

            DebugTools_DemoStage.TopUpNeeds(pawn, 1f);
            if (pawn.needs?.rest != null)
            {
                pawn.needs.rest.CurLevel = DrainedRest;
            }
        }

        /// <summary>
        /// Deposit-only, with the filter narrowed to exactly the armour.
        ///
        /// <para><c>SetDisallowAll</c> first is the whole point: the stand's
        /// shipped default accepts nearly all apparel, which would sweep the
        /// base layer into the plan and trip the never-naked refusal, and the
        /// stage would then demonstrate nothing at all.</para>
        ///
        /// <para>Called again after a fallback <c>ToggleRest</c> because
        /// <see cref="CompShiftStand.DepositOnly"/> is gated on
        /// <c>HandlesRest()</c> — setting it while the trigger is off stores the
        /// bit but reads back false, and the filter work would be invisible.</para>
        /// </summary>
        internal static void DepositOnly(Building_OutfitStand stand)
        {
            stand.TryGetComp<CompShiftStand>()?.SetDepositOnly(true);

            StorageSettings settings = stand.GetStoreSettings();
            if (settings == null)
            {
                return;
            }
            settings.filter.SetDisallowAll();
            Allow(settings, "Apparel_ArmorCataphractPrestige");
            Allow(settings, "Apparel_ArmorHelmetCataphractPrestige");
        }

        /// <summary>
        /// Lay the first carpet def that resolves over a room's interior.
        /// </summary>
        internal static void Carpet(Map map, CellRect interior, params string[] defNames)
        {
            foreach (string defName in defNames)
            {
                if (DefDatabase<TerrainDef>.GetNamedSilentFail(defName) == null)
                {
                    continue;
                }
                DebugTools_DemoStage.Carpet(map, interior, defName);
                return;
            }
            Messages.Message("no carpet def resolved — the room keeps its plank floor and the "
                + "card will not match the set.", MessageTypeDefOf.CautionInput, historical: false);
        }

        internal static void Allow(StorageSettings settings, string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                Messages.Message(defName + " not found — is Royalty loaded? The deposit stand's "
                    + "filter is short a piece.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            settings.filter.SetAllow(def, allow: true);
        }

        internal static void Wear(Pawn pawn, string defName, Color colour)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null || pawn.apparel == null)
            {
                Messages.Message(defName + " not found — is Royalty loaded? A soldier is short a "
                    + "piece.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            pawn.apparel.Wear(MakeGarment(def, colour), dropReplacedApparel: false);
        }

        internal static void Put(Building_OutfitStand stand, string defName, Color colour)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return;
            }
            if (!stand.AddApparel(MakeGarment(def, colour)))
            {
                Messages.Message(defName + " did not fit on the stand — it conflicts with a piece "
                    + "already on it.", MessageTypeDefOf.RejectInput, historical: false);
            }
        }
    }
}
#endif
