// Compiled only with SCENES (see the configuration table in ShiftChange.csproj); it must never
// reach a player. The stage builders clear their footprint, destroying any pawn standing in it,
// rewrite terrain and roof, unlock the whole research tree, and leave player-faction colonists
// and buildings behind. They call DebugTools_DemoStage, DebugTools_PreviewStage and
// DebugTools_RecRoomStage, which must stay under SCENES with this file.
#if SCENES
using RimWorld;
using UnityEngine;
using Verse;
using static ShiftChange.DebugTools_Fixtures;

namespace ShiftChange
{
    /// <summary>
    /// Bedtime in prestige cataphract, in the two shapes the gallery needs: a comparison of two
    /// bedrooms off one corridor, and a single-room card.
    ///
    /// <para>In the comparison a soldier in each room is about to turn in. West deposits the
    /// armour into an empty rack and sleeps in what was underneath; east swaps it for a light
    /// kit on the stand. The deposit side is visually quieter, since the armour just disappears
    /// into the rack. Do not speed one side up to compensate: the rooms must read at the same
    /// rate for the comparison to hold.</para>
    /// </summary>
    internal static class DebugTools_SleepStage
    {
        internal const int RoomInterior = 5;

        /// <summary>
        /// Rows of corridor. Must equal <see cref="DebugTools_PreviewStage.CorridorHeight"/>,
        /// because the card stage places its soldier with it inside the preview stage's shell.
        /// </summary>
        internal const int CorridorHeight = 2;

        /// <summary>Outer wall, interior, party wall, interior, outer wall.</summary>
        internal const int Width = RoomInterior * 2 + 3;

        /// <summary>South wall, corridor, divider wall, interior, north wall.</summary>
        internal const int Height = RoomInterior + CorridorHeight + 3;

        /// <summary>The party wall's column, and the first interior row.</summary>
        internal const int PartyX = RoomInterior + 1;
        internal const int InteriorZ = CorridorHeight + 2;

        /// <summary>Door columns, at the centre of each room's five.</summary>
        internal const int WestDoorX = 3;
        internal const int EastDoorX = PartyX + 3;

        /// <summary>
        /// Low enough that JobGiver_GetRest wins the next roll under an Anything timetable
        /// assignment (it needs the rest level below 0.3), high enough to avoid staggering.
        /// </summary>
        internal const float DrainedRest = 0.15f;

        internal const float HeatPush = 3000f;

        internal static readonly Color Gunmetal = new Color(0.42f, 0.45f, 0.5f);
        internal static readonly Color Sand = new Color(0.78f, 0.72f, 0.56f);
        internal static readonly Color Underlayer = new Color(0.55f, 0.58f, 0.62f);

        /// <summary>The comparison at the clicked cell; clears its footprint first.</summary>
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
        /// Rows from the south edge: wall, two corridor rows, the divider wall with both doors,
        /// five interior rows, north wall. The party wall runs only from the divider row up, so
        /// both soldiers start in one continuous corridor; two would read as separate stages.
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

        /// <summary>
        /// The swap room alone, for a 320×360 gallery panel, built at the clicked cell. It clears
        /// the footprint first, and stages the swap rather than the deposit because a card is one
        /// still and an empty rack in a still reads as nothing happening. The preview stage's
        /// shell and interior are used so the capture rect and door column match a work card's;
        /// devtools/bridge/shoot-sleep-card.py selects this entry by its menu label and crops
        /// the preview stage's 8×9 rect from the block.
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
        /// Fills one bedroom: carpet, a bed, a stand on the west wall, and the soldier who owns
        /// both, spawned at <paramref name="start"/> in the corridor outside the room's door.
        /// The bed is centred, not in a corner, because the rooms are photographed square-on;
        /// two tiles cannot centre exactly in five rows, so it covers rows <c>minZ + 1</c> and
        /// <c>minZ + 2</c>, half a row toward the door.
        /// </summary>
        internal static void Occupant(Map map, ThingDef standDef, CellRect interior,
                                      string nick, Gender gender, bool deposit, IntVec3 start)
        {
            // The demo and preview stages carpet their rooms, so a plank floor would put the card
            // outside the set. The names are tried in order because these terrain defs are
            // generated by crossing a TerrainTemplateDef with the structural colours, so one can
            // be missing in a game version while the others survive.
            Carpet(map, interior, "CarpetGreyDark", "CarpetSlate", "CarpetBlack");

            // Facing south, as the demo stage's hospital does. A bed's head, where the pillow is
            // drawn, is always its Position, and the bed runs from there toward its facing
            // (BedUtility.GetSleepingSlotPos, GenAdj.OccupiedRect). South covers Position and
            // the cell south of it, pillow at the top of the shot; North covers Position and the
            // cell north of it, pillow at the bottom, which reads as an upside-down bed.
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

            // The shirt and trousers under the armour are required. A deposit-only stand declines
            // a plan that would leave the soldier psychologically nude (SwapPlan.WouldBeNude),
            // and the armour covers torso and legs, so without a base layer the west stand
            // declines every night and the feature looks broken.
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
                // The flak helmet's def is Apparel_AdvancedHelmet; Apparel_SimpleHelmet is the
                // plain one.
                Put(stand, "Apparel_Duster", Sand);
                Put(stand, "Apparel_AdvancedHelmet", Sand);
            }

            // No stand is told to serve sleep: the room scores Bedroom once the bed is down (one
            // bed scores it, owned or not), and the Bedroom row in the role table supplies the
            // trigger, as it does for a player who builds a stand in a bedroom and touches
            // nothing. So this is read only after the bed spawns, or the room, still a generic
            // Room, would report false.
            CompShiftStand comp = stand.TryGetComp<CompShiftStand>();
            if (comp != null && !comp.HandlesRest())
            {
                // Not expected: one bed scores Bedroom, which the rest table covers. Kept
                // because another mod's room-role worker can outscore Bedroom, and a stage that
                // silently does nothing is worse than one that declares the trigger by hand.
                comp.ToggleRest();
                if (deposit)
                {
                    // ToggleRest keeps the stored bit, so this repeat is only a precaution.
                    DepositOnly(stand);
                }
                Messages.Message("Room did not score Bedroom for " + nick
                    + " — declared the sleep trigger by hand.",
                    MessageTypeDefOf.CautionInput, historical: false);
            }

            // Every work type off, so nothing competes with resting.
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
        /// Makes the stand deposit-only, with its filter narrowed to exactly the two armour
        /// pieces. A stand's shipped filter accepts nearly all apparel, which would put the base
        /// layer in the plan and trip the never-nude refusal, so the stage would demonstrate
        /// nothing. Disallow all, then allow the armour, as a player would.
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
                Messages.Message(defName + " could not be added to the stand: its container "
                    + "refused it.", MessageTypeDefOf.RejectInput, historical: false);
            }
        }
    }
}
#endif
