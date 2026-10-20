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
        /// Which of the three triggers a stand is asked to serve. One value, not a bool each,
        /// because a stand serves at most one trigger: the comp holds that rule, and this keeps
        /// the selector from being a place to break it.
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
        /// The stand dresses for this trigger at all, any work type for work: what the
        /// change-back latch means by "the same trigger".
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
        /// The stand this pawn should use for a job at <paramref name="place"/>, or null: the
        /// room stands in the room of its room cell, and the radius and area stands whose
        /// catchment covers it, ranked by <see cref="Consider"/>, which also uses the
        /// <paramref name="job"/> to pass over a stand that would strip vacuum protection it
        /// needs. No free stand means no swap, never a wait: dressing is a nicety and must not
        /// hold up the work.
        ///
        /// <para>A room touching the map edge is skipped for every trigger: it is the outdoors,
        /// which spans the whole map, so a stand in it would serve every outdoor job. A radius
        /// or an area is bounded and needs no such guard (docs/DESIGN.md, "Catchments").</para>
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
                    // ContainedThings also lists a stand standing just outside a door, in the
                    // doorway's region, so each is asked Covers, as the return trip and the
                    // catch-up ask it.
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
        /// <see cref="FindAvailableStand(JobPlace, Map, Pawn, WorkTypeDef, StandTrigger, Job)"/>
        /// for a job at one ordinary cell.
        /// </summary>
        internal static CompShiftStand FindAvailableStand(IntVec3 cell, Map map, Pawn pawn,
            WorkTypeDef work, StandTrigger trigger = StandTrigger.Work)
        {
            return FindAvailableStand(JobPlace.At(cell), map, pawn, work, trigger);
        }

        /// <summary>
        /// One candidate through every gate of
        /// <see cref="FindAvailableStand(JobPlace, Map, Pawn, WorkTypeDef, StandTrigger, Job)"/>,
        /// kept if it beats the best so far: a personal stand (assigned to this pawn) over a pool
        /// stand (unassigned, open to any capable pawn), then the nearer. Given the
        /// <paramref name="job"/> it is dressing for, a candidate that would strip vacuum
        /// protection the job needs is not kept, and the search goes on to the next.
        /// </summary>
        internal static void Consider(CompShiftStand comp, Pawn pawn, WorkTypeDef work, StandTrigger trigger,
            ref CompShiftStand best, ref bool bestIsPersonal, ref int bestDistance, Job job = null)
        {
            Thing thing = comp.parent;
            if (comp.OnShift || !Handles(comp, trigger, work) || !comp.CanBeClaimedBy(pawn))
            {
                return;
            }
            // The player stopped this cycle: the candidate is the stand they changed back at, or
            // covers that stand for the same trigger.
            if (LatchBlocks(pawn, comp, trigger))
            {
                if (Verbose)
                {
                    Log.Message($"[ShiftChange] {pawn.LabelShort} stays changed out at " +
                                $"{thing.LabelShort}: the change-back order still holds");
                }
                return;
            }
            // The question the swap driver asks on arrival: a stand that holds some apparel may
            // hold none this pawn can put on.
            if (!SwapPlan.WouldDress(pawn, comp.Stand))
            {
                return;
            }
            // Reads the plan WouldDress has just built, so it comes before anything builds
            // another. The walk it may lead to is asked last, below.
            bool lessProtected = job != null && PlanLowersVacuumProtection(pawn, thing.Map);
            // Outside the pawn's allowed area the swap could not start (see StandForbiddenTo),
            // and a forbidden personal stand would outrank an allowed shared one beside it.
            if (StandForbiddenTo(thing, pawn))
            {
                return;
            }
            // Reservation is part of "available", not a check after choosing: OnShift turns true
            // only when a swap completes, so a stand someone is walking to still reads as free,
            // can win, and then fails to reserve in Insert, putting the pawn on the retry cooldown
            // instead of moving on to the free stand beside it. Asked last, as CanReserveAndReach
            // is much the costliest gate.
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
            // A region walk, so asked only of a stand that would otherwise win and whose plan
            // lowers protection on a vacuum map. Here, not after the search, so another stand
            // can win: the vacsuit stand by the airlock, behind the nearer apron.
            if (lessProtected && DressingTripGoesThroughVacuum(job, pawn, comp, thing.Map))
            {
                return;
            }
            best = comp;
            bestIsPersonal = personal;
            bestDistance = distance;
        }

        /// <summary>
        /// The stand is off limits to this pawn: outside their allowed area, in practice, since
        /// an outfit stand has no forbid toggle of its own. It is the swap driver's own
        /// predicate, so the two cannot disagree; <c>CanReserveAndReach</c> ignores allowed
        /// areas, and the driver does not. Same map first: another map's area grid throws out of
        /// range and reads the wrong bit inside it. docs/DESIGN.md, "Allowed areas".
        /// </summary>
        internal static bool StandForbiddenTo(Thing stand, Pawn pawn)
        {
            return stand != null && pawn != null
                   && stand.MapHeld != null && stand.MapHeld == pawn.MapHeld
                   && stand.IsForbidden(pawn);
        }
    }
}
