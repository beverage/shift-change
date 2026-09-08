using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Keeps haulers out of a deposit-only stand. Such a stand takes in what
    /// the sleep change parks on it, and nothing else.
    ///
    /// <para><b>Vanilla does not merely permit stocking an outfit stand, it
    /// goes looking.</b> <c>OutfitStandBase</c> ships
    /// <c>defaultStorageSettings</c> at priority <c>Important</c>, above an
    /// ordinary stockpile, so an EMPTY stand outbids the shelf a garment is
    /// already sitting on: haulers walk apparel out of storage and into the
    /// stand until <c>HasRoomForApparelOfDef</c> refuses, one garment per body
    /// part group. On a display stand that is the feature. On a deposit-only
    /// stand it is the exact inverse of the point — empty is the correct
    /// resting state, every slot a hauler fills is a slot the colonist's own
    /// armour cannot land in, and
    /// <see cref="Patch_AllowRemovingToggle"/> holds <c>allowRemovingItems</c>
    /// off while the stand is in service, so what a hauler puts in cannot be
    /// hauled back out.</para>
    ///
    /// <para><b>Which is why it presents as one cursed stand.</b> Found in play
    /// 2026-09-08 on exactly one of three identically configured stands, and
    /// the difference was their CONTENTS: <c>Accepts</c> ends at
    /// <c>HasRoomForApparelOfDef</c>, which refuses anything conflicting with
    /// what the stand already holds (<c>Building_OutfitStand.cs:332</c>), so a
    /// stand with a spare outfit parked on it is immune — every slot is taken.
    /// The other two held an alternate gear set. The third was deposit-only,
    /// whose resting state is empty, and it had a duplicate of its owner's
    /// armour sitting in a Normal-priority stockpile for the stand to outbid.
    /// Both conditions are needed, neither is visible in the stand's settings,
    /// and deposit-only is the one mode that guarantees the first one forever.
    /// A player diagnosing this by comparing configurations finds nothing.</para>
    ///
    /// <para><b>One question gates the automatic path and nothing else.</b>
    /// <c>StoreUtility.TryFindBestBetterNonSlotGroupStorageFor</c> skips any
    /// destination whose <c>HaulDestinationEnabled</c> is false
    /// (<c>StoreUtility.cs:252</c>), and that property is
    /// <c>Building_OutfitStand</c>'s one unconditional <c>true</c>
    /// (<c>Building_OutfitStand.cs:100</c>). Answering false removes the stand
    /// from the search; the two EXPLICIT player routes — the "put apparel on
    /// stand" targeter (<c>Building_OutfitStand.cs:675</c>) and
    /// <c>FloatMenuOptionProvider_DressOtherPawn</c> — build the
    /// <c>PutApparelOnOutfitStand</c> job directly and never consult it, so an
    /// ordered delivery still works. Refusing those too would have made the
    /// stand unstockable by any means, and the player asking for a specific
    /// garment on a specific stand is not the problem being solved.</para>
    ///
    /// <para><b>Nothing needs invalidating when the mode changes.</b> The
    /// engine reads this property inside the search loop rather than caching it
    /// — its only two readers are <c>StoreUtility.cs:193</c> (slot group
    /// parents, which a stand is not) and <c>:252</c> — so
    /// <c>haulDestinationManager</c> keeps listing the stand and simply passes
    /// over it. Ticking deposit-only on or off therefore lands on the next haul
    /// search with no lister to drive, which matters because the answer is
    /// keyed on <see cref="CompShiftStand.DepositOnly"/> and that gates itself
    /// on <c>HandlesRest()</c>: carry the stand out of the bedroom and it is
    /// ordinary storage again, immediately.</para>
    ///
    /// <para><b>Stands already stocked heal themselves.</b> This stops future
    /// deliveries and moves nothing, so a stand a hauler filled before the fix
    /// stays filled — until the next sleep change, where
    /// <c>TryDropThingsToMakeRoomForThingOfDef</c>
    /// (<c>JobDriver_SwapAtStand.cs:384</c>) evicts whatever is in the way of
    /// the deposit. The difference is where the evicted garment goes: back to a
    /// stockpile now, rather than straight back onto the stand it was just
    /// dropped from.</para>
    ///
    /// <para><b>The target has no C# name to reference.</b> It is an explicit
    /// interface implementation, which the compiler mangles to
    /// <c>RimWorld.IHaulDestination.get_HaulDestinationEnabled</c>; the
    /// interface map is asked for it instead, so the patch depends on the
    /// interface rather than on a naming convention. It is accepted only when
    /// declared on <c>Building_OutfitStand</c> itself, since the postfix binds
    /// <c>__instance</c> to that type. If the type ever stops implementing
    /// <c>IHaulDestination</c>, <see cref="TargetMethods"/> yields nothing, the
    /// patch does not apply, and stands go back to being stocked by haulers —
    /// which is where they started. <c>Building_KidOutfitStand</c> and Outfit
    /// Stands Plus' powered stands inherit the implementation, so the one patch
    /// covers the whole family.</para>
    /// </summary>
    [HarmonyPatch]
    public static class Patch_DepositOnlyHauling
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            MethodInfo getter = ResolveHaulDestinationEnabledGetter();
            if (getter != null)
            {
                yield return getter;
            }
        }

        /// <summary>
        /// <c>Building_OutfitStand</c>'s own implementation of
        /// <c>IHaulDestination.HaulDestinationEnabled</c>, or null if it is not
        /// declared there any more.
        /// </summary>
        internal static MethodInfo ResolveHaulDestinationEnabledGetter()
        {
            try
            {
                InterfaceMapping map = typeof(Building_OutfitStand).GetInterfaceMap(typeof(IHaulDestination));
                for (int i = 0; i < map.InterfaceMethods.Length; i++)
                {
                    if (map.InterfaceMethods[i].Name != "get_" + nameof(IHaulDestination.HaulDestinationEnabled))
                    {
                        continue;
                    }
                    MethodInfo target = map.TargetMethods[i];
                    return target?.DeclaringType == typeof(Building_OutfitStand) ? target : null;
                }
            }
            catch (Exception e)
            {
                // GetInterfaceMap throws rather than returning empty when the
                // type does not implement the interface. Not fatal: no patch,
                // vanilla hauling, one line in the log saying why.
                Log.Warning("[ShiftChange] could not resolve Building_OutfitStand's IHaulDestination implementation; "
                            + "deposit-only stands will be stocked by haulers as in vanilla: " + e);
            }
            return null;
        }

        // ReSharper disable once InconsistentNaming — Harmony injection.
        public static void Postfix(Building_OutfitStand __instance, ref bool __result)
        {
            if (!__result)
            {
                return;
            }
            CompShiftStand comp = __instance?.TryGetComp<CompShiftStand>();
            if (comp != null && comp.DepositOnly)
            {
                __result = false;
            }
        }
    }
}
