using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// The moves every weapon transfer is built from. What may move is <see cref="SwapPlan"/>'s
    /// to decide; these only carry it out. A weapon that comes out of <see cref="Release"/> is
    /// held by nothing, so its caller must put it somewhere.
    /// </summary>
    internal static class WeaponHands
    {
        /// <summary>The pawn has this weapon, in hand or pack.</summary>
        internal static bool Holds(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
            {
                return false;
            }
            return (pawn.equipment != null && pawn.equipment.Contains(weapon))
                   || (pawn.inventory != null && pawn.inventory.innerContainer.Contains(weapon));
        }

        /// <summary>
        /// Takes a weapon off the pawn and returns it held by nothing, or null when the pawn
        /// does not have it or it did not survive the drop.
        ///
        /// <para>From the hand it goes by way of the ground, not
        /// <c>Pawn_EquipmentTracker.Remove</c>: a drop is what vanilla's own stand does and what
        /// other mods hook, Vanilla Expanded Framework clearing a weapon's hediffs among them.
        /// <c>TryDropEquipment</c> reads <c>MapHeld</c>, so the pawn must be on a map.</para>
        /// </summary>
        internal static ThingWithComps Release(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
            {
                return null;
            }
            if (pawn.equipment != null && pawn.equipment.Contains(weapon))
            {
                ThingWithComps dropped;
                // A weapon whose def sets destroyOnDrop comes back null, having been destroyed.
                if (!pawn.equipment.TryDropEquipment(weapon, out dropped, pawn.PositionHeld,
                                                     forbid: false)
                    || dropped == null)
                {
                    return null;
                }
                if (dropped.Spawned)
                {
                    dropped.DeSpawn();
                }
                return dropped;
            }
            if (pawn.inventory != null && pawn.inventory.innerContainer.Remove(weapon))
            {
                return weapon;
            }
            return null;
        }

        /// <summary>
        /// Puts a weapon held by nothing in the pawn's empty hand. False, with the weapon still
        /// held by nothing, when the hand is full: <c>AddEquipment</c> would log an error and
        /// keep nothing.
        /// </summary>
        internal static bool Draw(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn?.equipment == null || weapon == null || pawn.equipment.Primary != null)
            {
                return false;
            }
            pawn.equipment.AddEquipment(weapon);
            return pawn.equipment.Contains(weapon);
        }

        /// <summary>
        /// The weapon in the pawn's hand into their pack. False when it did not get there: it is
        /// back in the hand if the pack refused it, and if the hand then refuses too, on the
        /// ground. A pawn on no map has no ground, so the weapon crosses directly, and a refusal
        /// leaves it in the hand. False, with nothing moved, when it is not in the hand.
        /// </summary>
        internal static bool ToPack(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon == null || pawn?.equipment == null || !pawn.equipment.Contains(weapon))
            {
                return false;
            }
            if (pawn.MapHeld == null)
            {
                return pawn.inventory != null
                       && pawn.equipment.TryTransferEquipmentToContainer(
                           weapon, pawn.inventory.innerContainer);
            }
            ThingWithComps released = Release(pawn, weapon);
            if (released == null)
            {
                return false;
            }
            if (Stow(pawn, released))
            {
                return true;
            }
            if (!Draw(pawn, released))
            {
                PutDown(pawn, released);
            }
            return false;
        }

        /// <summary>
        /// The pawn's own weapon back at the end of a loan: drawn from the pack into an empty
        /// hand, as <see cref="FromPack"/>. A downing drops the pack, so when their own weapon is
        /// no longer in it but lies on the pawn's map and the hand is still empty, the engine's
        /// note of a dropped weapon is pointed at it, and vanilla's fetch
        /// (<c>JobGiver_PickupDroppedWeapon</c>) takes it back up once the pawn is free. A weapon
        /// the draw itself put down is left alone, so the fetch never retries a refused draw.
        /// </summary>
        internal static void DrawOrFetch(Pawn pawn, ThingWithComps own)
        {
            if (own == null || pawn == null)
            {
                return;
            }
            if (pawn.inventory != null && pawn.inventory.innerContainer.Contains(own))
            {
                // A draw the hand and the pack both refused puts the weapon down, and it stays.
                FromPack(pawn, own);
                return;
            }
            if (pawn.equipment == null || pawn.equipment.Primary != null || pawn.mindState == null)
            {
                return;
            }
            if (own.Spawned && own.Map == pawn.Map)
            {
                pawn.mindState.droppedWeapon = own;
            }
        }

        /// <summary>
        /// Drops the engine's note of a weapon to fetch when it names <paramref name="weapon"/>.
        /// At the end of a loan that is the stand's weapon, which is no longer the pawn's to take
        /// up: after a change back made before the fetch, the fetch would make it their own and
        /// leave the stand empty.
        /// </summary>
        internal static void ForgetDropped(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon != null && pawn?.mindState != null && pawn.mindState.droppedWeapon == weapon)
            {
                pawn.mindState.droppedWeapon = null;
            }
        }

        /// <summary>
        /// The weapon in the pawn's pack into their empty hand. False, with nothing moved, when
        /// it is not in the pack or the hand holds something, which the pawn keeps.
        /// </summary>
        internal static bool FromPack(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon == null || pawn?.equipment == null || pawn.equipment.Primary != null
                || pawn.inventory == null || !pawn.inventory.innerContainer.Contains(weapon))
            {
                return false;
            }
            ThingWithComps released = Release(pawn, weapon);
            if (released == null)
            {
                return false;
            }
            if (Draw(pawn, released))
            {
                return true;
            }
            if (!Stow(pawn, released))
            {
                PutDown(pawn, released);
            }
            return false;
        }

        /// <summary>
        /// The weapon in the pack to the hand, and whatever the hand holds to the pack. False when
        /// it did not happen, with the hand back as it was.
        /// </summary>
        internal static bool Exchange(Pawn pawn, ThingWithComps fromPack)
        {
            if (fromPack == null || pawn?.equipment == null)
            {
                return false;
            }
            ThingWithComps hand = pawn.equipment.Primary;
            if (hand != null && !ToPack(pawn, hand))
            {
                return false;
            }
            if (FromPack(pawn, fromPack))
            {
                return true;
            }
            FromPack(pawn, hand);
            return false;
        }

        /// <summary>A weapon held by nothing into the pawn's pack.</summary>
        internal static bool Stow(Pawn pawn, ThingWithComps weapon)
        {
            return pawn.inventory != null
                   && pawn.inventory.innerContainer.TryAdd(weapon,
                                                           canMergeWithExistingStacks: false);
        }

        /// <summary>
        /// A weapon held by nothing onto the ground beside the pawn, unforbidden. The last
        /// resort of every move, since leaving it held by nothing deletes it from the world.
        /// </summary>
        internal static void PutDown(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn.Spawned && PlaceNear(weapon, pawn.Position, pawn.Map))
            {
                return;
            }
            Log.Error("[ShiftChange] could not put " + weapon.LabelCap + " down beside "
                      + pawn.LabelShort + " — it may be lost.");
        }

        /// <summary>A weapon held by nothing onto the ground near a cell, unforbidden, for a
        /// hauler to find.</summary>
        internal static bool PlaceNear(ThingWithComps weapon, IntVec3 cell, Map map)
        {
            if (!GenPlace.TryPlaceThing(weapon, cell, map, ThingPlaceMode.Near))
            {
                return false;
            }
            weapon.SetForbidden(value: false, warnOnFail: false);
            return true;
        }
    }
}
