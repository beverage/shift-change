using System;
using System.Collections.Generic;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ShiftChange
{
    public static partial class Patch_JobInterception
    {
        /// <summary>
        /// Which of the three triggers a stand is being asked to serve. One
        /// value rather than a row of bools, because "recreation and sleep
        /// both true" is not a state any caller is allowed to hold — the
        /// exclusivity rule lives on the comp, and this keeps the selector
        /// from being the place it could be broken.
        /// </summary>
        internal enum StandTrigger
        {
            Work,
            Recreation,
            Rest,
        }

        internal static bool Handles(CompShiftStand comp, StandTrigger trigger, WorkTypeDef work)
        {
            switch (trigger)
            {
                case StandTrigger.Recreation:
                    return comp.HandlesRecreation();
                case StandTrigger.Rest:
                    return comp.HandlesRest();
                default:
                    return comp.HandlesWork(work);
            }
        }

        /// <summary>
        /// The stand dresses for this trigger at all: any work type, for work.
        /// What the change-back latch means by "the same trigger".
        /// </summary>
        internal static bool ServesTrigger(CompShiftStand comp, StandTrigger trigger)
        {
            switch (trigger)
            {
                case StandTrigger.Recreation:
                    return comp.HandlesRecreation();
                case StandTrigger.Rest:
                    return comp.HandlesRest();
                default:
                    return comp.WorkTypes.Count > 0;
            }
        }

        /// <summary>
        /// The stand this pawn should use for a job at <paramref name="place"/>,
        /// or null if there isn't one: the room stands in the room of its room
        /// cell, and the radius and area stands whose catchment covers it.
        ///
        /// An unassigned stand is a POOL stand: any capable pawn may claim it,
        /// so a kitchen needs one stand per CONCURRENT cook rather than one per
        /// cook who might ever cook. A stand assigned to this pawn always wins
        /// over a pool stand — a personal kit is personal — and among equals
        /// the nearest is taken, or two cooks walk past a closer one to reach
        /// the same far one.
        ///
        /// No free stand simply means no swap. Never queue for one: this is a
        /// nicety and must not become a bottleneck on the work itself.
        ///
        /// <para>Room stands in a room touching the map edge are skipped, for
        /// every trigger: the outdoor room spans the whole map, so one standing
        /// in it would serve every outdoor job. A radius or an area is bounded
        /// by construction and needs no such guard (docs/DESIGN.md,
        /// "Catchments").</para>
        ///
        /// <para>A room stand is a candidate only when it covers the place
        /// (<see cref="CompShiftStand.Covers(JobPlace, Map)"/>), the question
        /// the return trip and the catch-up ask: the room's contents also list
        /// a stand standing just outside its door.</para>
        ///
        /// <para>A candidate the change-back latch holds is passed over
        /// (<see cref="LatchBlocks"/>), so the latch never hides a stand it
        /// does not reach.</para>
        ///
        /// <para>Given the <paramref name="job"/>, a candidate whose outfit
        /// would strip vacuum protection the job needs is passed over too
        /// (<see cref="Consider"/>), so a vacsuit stand further off can still
        /// win where the nearest stand would take the suit off.</para>
        /// </summary>
        internal static CompShiftStand FindAvailableStand(JobPlace place, Map map, Pawn pawn,
            WorkTypeDef work, StandTrigger trigger = StandTrigger.Work, Job job = null)
        {
            CompShiftStand best = null;
            bool bestIsPersonal = false;
            int bestDistance = int.MaxValue;

            IntVec3 cell = place.RoomCell;
            Room room = map != null && cell.IsValid && cell.InBounds(map) ? cell.GetRoom(map) : null;
            if (room != null && !room.TouchesMapEdge)
            {
                List<ThingDef> defs = StandDefs;
                for (int i = 0; i < defs.Count; i++)
                {
                    // ContainedThings also lists a stand standing just outside
                    // a door, in the doorway's region, so each one is asked
                    // the same question as the return trip and the catch-up.
                    foreach (Thing thing in room.ContainedThings(defs[i]))
                    {
                        CompShiftStand comp = thing.TryGetComp<CompShiftStand>();
                        if (comp != null && comp.serves == CatchmentKind.Room && comp.Covers(place, map))
                        {
                            Consider(comp, pawn, work, trigger, ref best, ref bestIsPersonal, ref bestDistance,
                                     job);
                        }
                    }
                }
            }

            CompShiftStand.PruneCatchmentStands();
            foreach (CompShiftStand comp in CompShiftStand.CatchmentStands)
            {
                Thing thing = comp.parent;
                if (thing != null && thing.Spawned && thing.Map == map && comp.Covers(place, map))
                {
                    Consider(comp, pawn, work, trigger, ref best, ref bestIsPersonal, ref bestDistance, job);
                }
            }
            return best;
        }

        /// <summary>
        /// <see cref="FindAvailableStand(JobPlace, Map, Pawn, WorkTypeDef, StandTrigger)"/>
        /// for a job at one ordinary cell.
        /// </summary>
        internal static CompShiftStand FindAvailableStand(IntVec3 cell, Map map, Pawn pawn,
            WorkTypeDef work, StandTrigger trigger = StandTrigger.Work)
        {
            return FindAvailableStand(JobPlace.At(cell), map, pawn, work, trigger);
        }

        /// <summary>
        /// One candidate through every gate of <see cref="FindAvailableStand"/>,
        /// kept if it beats the best so far: a personal stand over a shared one,
        /// then the nearer. Given the <paramref name="job"/> it is dressing for,
        /// a candidate that would strip vacuum protection the job needs is not
        /// kept, and the search goes on to the next.
        /// </summary>
        internal static void Consider(CompShiftStand comp, Pawn pawn, WorkTypeDef work, StandTrigger trigger,
            ref CompShiftStand best, ref bool bestIsPersonal, ref int bestDistance, Job job = null)
        {
            Thing thing = comp.parent;
            if (comp.OnShift || !Handles(comp, trigger, work) || !comp.CanBeClaimedBy(pawn))
            {
                return;
            }
            // The change-back latch: the stand they changed back at, or one
            // with its trigger covering it, is the cycle the player stopped.
            if (LatchBlocks(pawn, comp, trigger))
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] {pawn.LabelShort} stays changed out at " +
                                $"{thing.LabelShort}: the change-back order still holds");
                }
                return;
            }
            // Ask the same question the driver will ask on arrival, not a looser
            // one. "Holds some apparel" is not the same as "holds something THIS
            // pawn can put on", and the gap sent pawns on wasted trips that looked
            // like swapping at an empty rack.
            if (!SwapPlan.WouldDress(pawn, comp.Stand))
            {
                return;
            }
            // Read off the plan WouldDress has just built, before anything
            // builds another. The walk it may lead to is asked last, below.
            bool lessProtected = job != null && PlanLowersVacuumProtection(pawn, thing.Map);
            // Outside this pawn's allowed area. The swap could not get there (see
            // StandForbiddenTo), and because a personal stand outranks a shared
            // one below, leaving it in would let a forbidden personal stand beat
            // an allowed one beside it.
            if (StandForbiddenTo(thing, pawn))
            {
                return;
            }
            // Reservation is part of "available", not a formality to check after
            // choosing. OnShift only becomes true when a swap COMPLETES, so a
            // stand someone is currently walking to still reads as free here — it
            // would win the distance tiebreak, fail to reserve back in Insert, and
            // put the pawn on cooldown instead of falling through to the stand
            // next to it that was genuinely free. On a burst arrival — the exact
            // concurrency pooling exists to serve — that degraded pooling to
            // roughly one dress per completed swap however many stands the room
            // had. Asked last of the gates because it is much the most expensive,
            // and it is the same call TryDressMidJob already makes.
            if (!pawn.CanReserveAndReach(thing, PathEndMode.InteractionCell, Danger.Deadly))
            {
                return;
            }

            bool personal = comp.IsAssignedTo(pawn);
            int distance = pawn.Position.DistanceToSquared(thing.Position);
            if (best != null
                && !(personal && !bestIsPersonal)
                && !(personal == bestIsPersonal && distance < bestDistance))
            {
                return;
            }
            // NO STAND THAT STRIPS VACUUM PROTECTION THE JOB NEEDS. A region
            // walk, so asked only of a stand that would otherwise win, and only
            // when its plan lowers the pawn's protection on a vacuum map.
            // Passing it over here, not after the search, lets another stand
            // win: the vacsuit stand by the airlock, behind the nearer apron.
            // docs/DESIGN.md, "Vacuum".
            if (lessProtected && DressingTripGoesThroughVacuum(job, pawn, comp, thing.Map))
            {
                return;
            }
            best = comp;
            bestIsPersonal = personal;
            bestDistance = distance;
        }

        /// <summary>
        /// The stand is off limits to this pawn: outside their allowed area, in
        /// practice, since an outfit stand has no forbid toggle of its own.
        /// It is the swap driver's own predicate, so the two cannot disagree;
        /// <c>CanReserveAndReach</c> ignores allowed areas, and the driver
        /// does not. Same map first: another map's area grid throws out of
        /// range and reads the wrong bit inside it. docs/DESIGN.md, "Allowed
        /// areas".
        /// </summary>
        internal static bool StandForbiddenTo(Thing stand, Pawn pawn)
        {
            return stand != null && pawn != null
                   && stand.MapHeld != null && stand.MapHeld == pawn.MapHeld
                   && stand.IsForbidden(pawn);
        }
    }
}
