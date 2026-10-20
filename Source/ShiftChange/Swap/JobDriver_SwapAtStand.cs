using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShiftChange
{
    /// <summary>
    /// Swaps a pawn into, or back out of, their own stand's outfit, moving only what the stand's
    /// ledger says, in one direction at a time.
    ///
    /// Vanilla's <c>JobDriver_UseOutfitStand</c> cannot be reused: it claims every wearable item
    /// in the stand and pushes displaced clothing back into the same anonymous bag, so two pawns
    /// sharing a stand would swap each other's clothes.
    ///
    /// Dressing sets the forced-apparel flag, because <c>JobGiver_OptimizeApparel</c> would undo
    /// the swap otherwise (vanilla's own driver sets it too). Undressing must clear it, or the
    /// uniform stays pinned to the pawn.
    ///
    /// Every failure path must leave the pawn in the wrong clothes at worst, never in none. A swap
    /// may remove a garment that a royal title or ideology role requires; that is deliberately
    /// neither guarded nor warned about (docs/DESIGN.md, "The forced-apparel lifecycle").
    /// </summary>
    public class JobDriver_SwapAtStand : JobDriver
    {
        internal int duration;
        internal bool undressing;

        /// <summary>
        /// The stand this trip freed, for <see cref="AnnounceFreedStand"/>; set by
        /// <see cref="DoTransfer"/> or <see cref="NothingToWear"/>. Not scribed: the finish action
        /// that reads it runs in the same tick.
        /// </summary>
        internal CompShiftStand freedStand;
        internal List<Apparel> toWear = new List<Apparel>();
        internal List<Apparel> toStore = new List<Apparel>();

        internal Building_OutfitStand Stand => job.targetA.Thing as Building_OutfitStand;

        internal CompShiftStand Comp => Stand?.TryGetComp<CompShiftStand>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref duration, "duration", 0);
            Scribe_Values.Look(ref undressing, "undressing", defaultValue: false);
            Scribe_Collections.Look(ref toWear, "toWear", LookMode.Reference);
            Scribe_Collections.Look(ref toStore, "toStore", LookMode.Reference);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            toWear = new List<Apparel>();
            toStore = new List<Apparel>();
            duration = 0;

            CompShiftStand comp = Comp;
            if (comp == null)
            {
                return;
            }

            undressing = comp.OnShift;
            if (undressing)
            {
                PlanUndress(comp);
            }
            else
            {
                PlanDress(comp);
            }

            foreach (Apparel apparel in toWear)
            {
                duration += (int)(apparel.GetStatValue(StatDefOf.EquipDelay) * 60f);
            }
            foreach (Apparel apparel in toStore)
            {
                duration += (int)(apparel.GetStatValue(StatDefOf.EquipDelay) * 60f);
            }
            // Outfit Stands Plus scales a swap at any stand carrying its comp, vanilla's included;
            // apply its factor so a shift change is never slower than a manual swap there. No-op
            // without that mod.
            duration = Interop_OutfitStandsPlus.ApplySwapSpeedFactor(Stand, duration);
        }

        /// <summary>
        /// Going on shift: stand's clothes on, own clothes into the stand. <see cref="SwapPlan"/>
        /// decides what moves, shared with the stand selector so the two agree on wearability.
        /// </summary>
        internal void PlanDress(CompShiftStand comp)
        {
            SwapPlan.BuildDress(pawn, Stand, toWear, toStore);
        }

        /// <summary>Going off shift: uniform back in the stand, own clothes on.</summary>
        internal void PlanUndress(CompShiftStand comp)
        {
            foreach (Apparel apparel in comp.IssuedUniformForReading)
            {
                // Only what the pawn still wears; anything shed, burnt or stolen since is gone.
                if (apparel != null && pawn.apparel.WornApparel.Contains(apparel) && !pawn.apparel.IsLocked(apparel))
                {
                    toStore.Add(apparel);
                }
            }
            foreach (Apparel apparel in comp.StoredOwnerApparelForReading)
            {
                // SwapPlan.CanWear, not a local predicate: wearability is decided only there. It
                // also checks body parts and biocoding, which Wear() enforces only after the
                // stand has let go.
                if (apparel != null && apparel.ParentHolder == Stand && SwapPlan.CanWear(pawn, apparel))
                {
                    toWear.Add(apparel);
                }
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // A global finish action, not a toil one: Pawn_JobTracker.CleanupCurrentJob releases
            // this job's reservations before it runs the driver's global finish actions, while a
            // toil's finish action runs earlier, with this pawn still holding the stand
            // (docs/DESIGN.md, "The mid-job catch-up"). Registered here, not in Notify_Starting,
            // because SetupToils re-enumerates this method on load, so a swap interrupted by a
            // save still announces the stand when it finishes.
            AddFinishAction(AnnounceFreedStand);

            this.FailOnBurningImmobile(TargetIndex.A);
            // Both conditions are driver-level so they also cover the wait toil: a stand
            // deconstructed or burnt there has dumped its contents, and the transfer must not run.
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            Toil wait = ToilMaker.MakeToil("ShiftChangeSwapDelay");
            wait.WithProgressBarToilDelay(TargetIndex.A);
            wait.defaultCompleteMode = ToilCompleteMode.Delay;
            wait.defaultDuration = duration;
            yield return wait;

            Toil transfer = ToilMaker.MakeToil("ShiftChangeSwapTransfer");
            transfer.AddFinishAction(DoTransfer);
            transfer.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return transfer;
        }

        internal void DoTransfer()
        {
            Building_OutfitStand stand = Stand;
            CompShiftStand comp = Comp;
            if (stand == null || comp == null || !stand.Spawned || stand.Destroyed
                || pawn?.apparel == null || pawn.Dead)
            {
                // Nothing has moved yet; clothes posted into a despawned container would be lost.
                return;
            }

            // Re-validate before anything comes off: the walk is long enough for the uniform to
            // be hauled away or a garment to burn, and stripping first would leave the pawn naked.
            toWear.RemoveAll(a => a == null || a.ParentHolder != stand || !SwapPlan.CanWear(pawn, a));

            // Dress direction only: toWear may have just shrunk, so the decency pass runs again
            // on what is left, and may hold more garments back or refuse. The return trip's guard
            // below asks a different question (see WearingAnythingBesides), and deposit-only
            // issues nothing, so the test below covers it.
            if (!undressing && !comp.DepositOnly
                && !SwapPlan.KeepThemDecent(pawn, toWear, toStore))
            {
                NothingToWear(comp);
                return;
            }
            // On the return trip and at a deposit-only stand an empty incoming set is expected, so
            // both ask whether the pawn stays dressed. On the return trip it can mean the dress
            // trip displaced nothing (a stand sharing no apparel layer with the pawn's own
            // clothes), and the uniform should still come off; giving up would donate it
            // permanently. Deposit-only is judged by coverage (WouldBeNude, as BuildDeposit
            // planned it), not by WearingAnythingBesides's garment count: a shield belt alone
            // passes the count and leaves a pawn who has no shirt or trousers bare.
            bool stillDressed = comp.DepositOnly
                ? !SwapPlan.WouldBeNude(pawn, toStore)
                : WearingAnythingBesides(toStore);
            bool issuesNothingByDesign = undressing || comp.DepositOnly;
            if (toWear.Count == 0 && (!issuesNothingByDesign || !stillDressed))
            {
                NothingToWear(comp);
                return;
            }

            // The pawn's garments come off first, as in vanilla's own driver: Wear() drops any
            // worn garment the new one conflicts with on the floor, not into the stand. They are
            // posted into the stand last, since it holds one outfit and what leaves it must go
            // before they arrive.
            List<Apparel> stored = new List<Apparel>();
            List<Apparel> storedForced = new List<Apparel>();
            foreach (Apparel apparel in toStore)
            {
                if (apparel != null && pawn.apparel.WornApparel.Contains(apparel))
                {
                    if (undressing)
                    {
                        pawn.outfits?.forcedHandler?.SetForced(apparel, forced: false);
                    }
                    else if (pawn.outfits != null && pawn.outfits.forcedHandler.IsForced(apparel))
                    {
                        // Capture before Remove: Pawn_ApparelTracker.Notify_ApparelRemoved clears
                        // the forced flag on every removal, so this is the last moment it is known.
                        storedForced.Add(apparel);
                    }
                    pawn.apparel.Remove(apparel);
                    stored.Add(apparel);
                }
            }

            List<Apparel> issued = new List<Apparel>();
            foreach (Apparel apparel in toWear)
            {
                if (apparel == null || apparel.ParentHolder != stand)
                {
                    continue;
                }
                if (!stand.RemoveApparel(apparel))
                {
                    continue;
                }
                pawn.apparel.Wear(apparel);
                // Wear() returns nothing and can decline silently: it calls DeSpawnOrDeselect on
                // the garment first, then checks body parts, biocoding and PawnCanWear, logging a
                // warning and returning on each. The stand has already let go, so an unchecked
                // call deletes the garment. Trust the outcome, not SwapPlan.CanWear: a body part
                // can be lost on the walk.
                if (!pawn.apparel.WornApparel.Contains(apparel))
                {
                    PutBack(stand, apparel);
                    continue;
                }
                if (!undressing)
                {
                    // Forced, or JobGiver_OptimizeApparel undoes the swap at once.
                    pawn.outfits?.forcedHandler?.SetForced(apparel, forced: true);
                }
                else if (comp.WasForcedWhenStored(apparel))
                {
                    // Force-worn before the shift and cleared at check-in, so restore it or the
                    // player's choice becomes policy-managed. Must precede NotifyUndressed, which
                    // clears the ledger WasForcedWhenStored reads.
                    pawn.outfits?.forcedHandler?.SetForced(apparel, forced: true);
                }
                issued.Add(apparel);
            }

            // Nothing went on (typically Wear() declined every incoming garment late) and the
            // pawn's own clothes are off, held by nothing. Posting them into the stand would put
            // the pawn on shift with nothing issued, since NotifyDressed claims the stand for
            // anything parked, and they would work without those clothes, bare under full
            // change, until the return trip: the optimizer is paused while they are on shift,
            // so nothing re-dresses them. So re-dress them and record nothing. Re-wearing checks
            // CanWear first, since Wear() detaches before it checks; what cannot go back on is
            // put in the stand, where a player can retrieve it.
            //
            // Deposit-only is exempt: it issues nothing by design, so this would put the armour
            // straight back on the pawn each time it is taken off. BuildDeposit and the arrival
            // check above keep that pawn dressed instead.
            if (!undressing && !comp.DepositOnly && issued.Count == 0 && stored.Count > 0)
            {
                foreach (Apparel apparel in stored)
                {
                    if (SwapPlan.CanWear(pawn, apparel))
                    {
                        pawn.apparel.Wear(apparel);
                        if (pawn.apparel.WornApparel.Contains(apparel))
                        {
                            if (storedForced.Contains(apparel))
                            {
                                // Remove() cleared the forced flag; nothing else restores it.
                                pawn.outfits?.forcedHandler?.SetForced(apparel, forced: true);
                            }
                            continue;
                        }
                    }
                    PutBack(stand, apparel);
                }
                return;
            }

            foreach (Apparel apparel in stored)
            {
                // Vanilla's eviction: drops anything in the stand that cannot be worn with this
                // garment, near the stand.
                stand.TryDropThingsToMakeRoomForThingOfDef(apparel.def);
                PutBack(stand, apparel);
            }

            if (undressing)
            {
                comp.NotifyUndressed(pawn);
                // Announced from the global finish action, once the pawn's claim is released.
                freedStand = comp;
            }
            else
            {
                comp.NotifyDressed(pawn, stored, issued, storedForced);
            }
        }

        /// <summary>
        /// The plan came up empty on arrival, with nothing moved yet. Dressing records nothing.
        /// The return trip lands here only as a last resort, when taking the uniform off would
        /// leave the pawn bare (<see cref="WearingAnythingBesides"/>, or coverage at a
        /// deposit-only stand, as <see cref="DoTransfer"/> checks).
        /// </summary>
        internal void NothingToWear(CompShiftStand comp)
        {
            if (!undressing)
            {
                // Nothing this pawn can put on: no state changed, so there is nothing to record.
                // This backstops the stand selector's WouldDress check.
                return;
            }

            // Their own clothes burnt, were hauled off, or no longer fit, and stripping the
            // uniform would leave the pawn naked. So hand it over as ordinary clothing (forced
            // cleared, so the optimizer re-dresses them from the stockpile) and free the stand.
            List<Apparel> uniform = comp.IssuedUniformForReading;
            for (int i = 0; i < uniform.Count; i++)
            {
                if (uniform[i] != null && pawn.apparel.WornApparel.Contains(uniform[i]))
                {
                    pawn.outfits?.forcedHandler?.SetForced(uniform[i], forced: false);
                }
            }
            // Announced later through freedStand, as on the normal return trip: this runs in a
            // toil finish action, so the pawn still holds the stand and nobody else can reserve it.
            comp.AbandonLedger(pawn, announce: false);
            freedStand = comp;
        }

        /// <summary>
        /// Tells the catch-up this stand is back in the pool, from the job's teardown, after the
        /// tracker has released this pawn's claim, so a colonist already working bare in the room
        /// can reserve it. Runs for any end condition, but only a return trip that freed the stand
        /// sets <see cref="freedStand"/>, so a dress trip, or a swap interrupted before its
        /// transfer, announces nothing.
        /// </summary>
        internal void AnnounceFreedStand(JobCondition condition)
        {
            CompShiftStand freed = freedStand;
            freedStand = null;
            if (freed == null || freed.parent == null || freed.parent.Destroyed)
            {
                return;
            }
            Patch_JobInterception.Notify_StandFreed(freed, pawn);
        }

        /// <summary>
        /// Is the pawn wearing anything that is not in <paramref name="set"/>? Asked of the
        /// outgoing set on the return trip: false means the uniform is all they have on. It counts
        /// garments, not body coverage, because the rule is only that a pawn is never left naked,
        /// and one surviving shirt satisfies it.
        /// </summary>
        internal bool WearingAnythingBesides(List<Apparel> set)
        {
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                if (!set.Contains(worn[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Puts a garment somewhere real, for any transfer step that can leave it held by neither
        /// the stand nor the pawn: the stand first, then the ground beside it, then the ground
        /// beside the pawn. Doing nothing there deletes the item from the world, so this never
        /// gives up silently (vanilla's own stand driver takes that gamble).
        /// </summary>
        internal void PutBack(Building_OutfitStand stand, Apparel apparel)
        {
            if (apparel == null || apparel.ParentHolder != null)
            {
                return;
            }
            if (stand.Spawned && stand.AddApparel(apparel))
            {
                return;
            }
            if (stand.Spawned
                && GenPlace.TryPlaceThing(apparel, stand.Position, stand.Map, ThingPlaceMode.Near))
            {
                return;
            }
            if (pawn.Spawned
                && GenPlace.TryPlaceThing(apparel, pawn.Position, pawn.Map, ThingPlaceMode.Near))
            {
                return;
            }
            Log.Error("[ShiftChange] could not return " + apparel.LabelCap
                      + " to the stand, the floor or " + pawn.LabelShort + " — it may be lost.");
        }
    }
}
