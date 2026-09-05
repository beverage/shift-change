using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShiftChange
{
    /// <summary>
    /// Swaps a pawn into, or back out of, their own stand's outfit.
    ///
    /// Vanilla's <c>JobDriver_UseOutfitStand</c> cannot be reused for this even
    /// though it looks identical: it claims every wearable item in the stand
    /// and pushes displaced clothing back into the same anonymous bag, so two
    /// pawns sharing a stand swap each other's clothes. This driver moves only
    /// what the stand's ledger says, in one direction at a time.
    ///
    /// The forced-apparel flag is what makes the swap stick —
    /// <c>JobGiver_OptimizeApparel</c> would otherwise undo it at the next
    /// opportunity, and vanilla's own driver sets the same flag
    /// (<c>JobDriver_UseOutfitStand.cs:91</c>). We must therefore also CLEAR it
    /// on the way back out, or the uniform is pinned to the pawn forever.
    /// </summary>
    public class JobDriver_SwapAtStand : JobDriver
    {
        internal int duration;
        internal bool undressing;

        /// <summary>
        /// Set by <see cref="DoTransfer"/> when this trip actually returned a
        /// uniform, and consumed by the global finish action that announces
        /// it. Not scribed, and does not need to be: both run inside the same
        /// job teardown, in the same tick, with no save point between them.
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
            // Outfit Stands Plus' powered stands advertise faster swaps —
            // honor their factor so a shift change is never slower than a
            // manual swap at the same stand. No-op everywhere else.
            duration = Interop_OutfitStandsPlus.ApplySwapSpeedFactor(Stand, duration);
        }

        /// <summary>
        /// Going on shift: stand's clothes on, own clothes into the stand. The
        /// decision of what moves lives in <see cref="SwapPlan"/>, shared with
        /// the stand selector so the two can never disagree about what counts
        /// as wearable.
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
                // Only what the pawn still actually wears — anything shed,
                // burnt or stolen in between is simply not there to return.
                if (apparel != null && pawn.apparel.WornApparel.Contains(apparel) && !pawn.apparel.IsLocked(apparel))
                {
                    toStore.Add(apparel);
                }
            }
            foreach (Apparel apparel in comp.StoredOwnerApparelForReading)
            {
                // SwapPlan.CanWear, not a local predicate: this asked only
                // PawnCanWear and so admitted garments vanilla's Wear() then
                // refused for want of a body part or a biocode — after the
                // stand had already let go of them. Wearability lives in
                // SwapPlan and nowhere else (SwapPlan.cs:17); this line was
                // the one place that had quietly grown a second opinion.
                if (apparel != null && apparel.ParentHolder == Stand && SwapPlan.CanWear(pawn, apparel))
                {
                    toWear.Add(apparel);
                }
            }
        }

        // Royal titles and ideology roles can REQUIRE a garment, and nothing in
        // vanilla stops a stand swap removing it — JobGiver_OptimizeApparel
        // only scores requirements (×25/×10) and vanilla's own stand driver
        // checks the much narrower IsLocked. We deliberately do not guard it
        // (decided 2026-08-07). Blocking could only ever mean
        // refusing the uniform, since the guard's lever is "don't remove the
        // robe" — so a titled pawn would silently never change, which is worse
        // than the mood hit. Nor do we warn: assigning this pawn to this stand
        // and stocking it are deliberate player acts, and vanilla already
        // surfaces the consequence where a player would look for it, via
        // ThoughtWorker_RoyalTitleApparelRequirementNotMet and
        // Thought_IdeoRoleApparelRequirementNotMet.

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // GLOBAL finish action, deliberately — not a toil one. The tracker
            // releases this job's reservations at
            // Pawn_JobTracker.CleanupCurrentJob:492 and only then runs the
            // driver's global finish actions at :497
            // (JobDriver.Cleanup:274-280). A toil finish action runs earlier
            // still, from TryActuallyStartNextToil, so anything announced there
            // is announced while this pawn still holds the stand.
            //
            // Registered here rather than in Notify_Starting because
            // SetupToils re-enumerates this on load, and a swap interrupted by
            // a save should still announce the stand when it finishes.
            AddFinishAction(AnnounceFreedStand);

            this.FailOnBurningImmobile(TargetIndex.A);
            // Driver-level, not toil-level. Scoped to the Goto alone, a stand
            // deconstructed or burnt down during the WAIT toil left the job
            // running to its transfer, which then posted the pawn's clothes
            // into a container that had already dumped its contents.
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
                // The stand or the pawn stopped existing between the last toil
                // and this finish action. Nothing has moved yet, so there is
                // nothing to unwind — and posting clothes into a despawned
                // container would lose them.
                return;
            }

            // RE-VALIDATE BEFORE ANYTHING COMES OFF. The plan was built back in
            // Notify_Starting and the walk is long enough for the world to
            // change under it: the uniform hauled away, a garment burnt, the
            // pawn's jaw lost to a raider on the way. Stripping first and only
            // then discovering the incoming set is empty is what left a naked
            // colonist standing at a stand that now advertised their own
            // civvies as the room's uniform.
            //
            // An empty incoming set does NOT by itself mean the swap has to be
            // abandoned. On the return trip it also happens when the dress trip
            // displaced nothing at all — a stand whose stock shares no apparel
            // layer with what the pawn arrived in (a Shell-layer lab coat over
            // a shirt and trousers, a headgear-only stand on a bare head)
            // stores nothing, so there is nothing to hand back. The pawn is
            // still wearing their own clothes UNDER the uniform, and taking the
            // uniform off is both safe and the whole point of the trip.
            // Abandoning here instead donated the uniform permanently: forced
            // cleared, ledger dropped, and the stand left reading empty so it
            // never dressed anyone again.
            //
            // So the test is what is underneath, not what the ledger says.
            // Give up only when the uniform is all the pawn has on.
            toWear.RemoveAll(a => a == null || a.ParentHolder != stand || !SwapPlan.CanWear(pawn, a));
            // A DEPOSIT-ONLY stand hands nothing out by design, so an empty
            // incoming set is not evidence that anything went wrong — it
            // belongs on the same side of this test as the return trip. Both
            // then answer the one question that actually matters here: will
            // the pawn still be dressed when this is over.
            //
            // DEPOSIT-ONLY ASKS THAT QUESTION THE STRICT WAY. WearingAnythingBesides
            // is a garment count, which is fine where it has always been used —
            // the return trip's last-resort recovery, where the alternative is
            // donating the uniform permanently. On the deposit path it is the
            // arrival re-check for a plan SwapPlan built with WouldBeNude, and
            // a comment here used to claim the two tested the same thing. They
            // did not: a pawn who lost their shirt and trousers during the walk
            // would pass the count on a shield belt alone and be stripped by
            // the very check meant to stop it (verification pass, 2026-09-03).
            // One predicate, one answer, on both sides of the walk.
            bool stillDressed = comp.DepositOnly
                ? !SwapPlan.WouldBeNude(pawn, toStore)
                : WearingAnythingBesides(toStore);
            bool issuesNothingByDesign = undressing || comp.DepositOnly;
            if (toWear.Count == 0 && (!issuesNothingByDesign || !stillDressed))
            {
                NothingToWear(comp);
                return;
            }

            // Off the pawn first, exactly as vanilla orders it — the stand has
            // room for one outfit, so the outgoing set must leave before the
            // incoming set can be deposited later in the same pass.
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
                        // Capture BEFORE Remove: Notify_ApparelRemoved clears
                        // the forced flag on every removal
                        // (Pawn_ApparelTracker.cs:784-790), so this is the last
                        // moment the fact exists anywhere.
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
                // Wear() returns nothing and can decline silently: it calls
                // DeSpawnOrDeselect() and only THEN checks body parts,
                // biocoding and PawnCanWear (Pawn_ApparelTracker.cs:434-450),
                // logging a warning and returning on each. By that point the
                // stand has already let go, so an unchecked call deletes a
                // colonist's clothing from the world. SwapPlan.CanWear asked
                // all three above, but the pawn can lose a body part during
                // the walk — so the outcome is the only thing worth trusting.
                if (!pawn.apparel.WornApparel.Contains(apparel))
                {
                    PutBack(stand, apparel);
                    continue;
                }
                if (!undressing)
                {
                    // Forced, or JobGiver_OptimizeApparel undoes this at once.
                    pawn.outfits?.forcedHandler?.SetForced(apparel, forced: true);
                }
                else if (comp.WasForcedWhenStored(apparel))
                {
                    // Restore what check-in destroyed: this garment was
                    // force-worn before the shift, so it comes back force-worn
                    // — otherwise the player's explicit choice is silently
                    // downgraded to policy-managed and the optimizer swaps it
                    // away. Must run before NotifyUndressed clears the ledger.
                    pawn.outfits?.forcedHandler?.SetForced(apparel, forced: true);
                }
                issued.Add(apparel);
            }

            // NOTHING WENT ON, AND THE CLOTHES ARE ALREADY OFF. Every incoming
            // garment was declined late by Wear() — a body part lost during the
            // walk, a biocode that changed hands. At this point `stored` is held
            // by nothing: off the pawn, not yet in the stand.
            //
            // Falling through would post it into the stand and record a ledger
            // with no borrower against it (NotifyDressed only sets a borrower
            // when something was issued), so the pawn's own clothes end up
            // locked in a rack nothing will ever hand back. With FULL CHANGE
            // that is every garment they own, and it leaves them naked — which
            // breaks the standing rule that the worst case on any failure path
            // here is a pawn in the WRONG clothes, never a pawn without them.
            //
            // So dress them again and record nothing: the trip accomplished
            // exactly as much as it should have. Re-wear goes through CanWear
            // first, because the same late refusal that got us here would
            // otherwise delete the garment — Wear() detaches before it checks.
            // Anything that genuinely cannot go back on goes into the stand
            // rather than nowhere; that is still a garment the player can
            // retrieve, which an unheld one is not.
            // Deposit-only is exempt: issuing nothing is the whole point, and
            // this recovery would put the armour straight back on the pawn it
            // was just taken off, every night, forever. The invariant it
            // protects still holds there by a different route — BuildDeposit
            // refuses a plan that would strip the pawn bare, and the guard
            // above re-checks the same thing on arrival.
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
                                // Remove() cleared the forced flag on the way
                                // off and nothing else restores it.
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
                // Vanilla's own eviction: anything already in the stand that
                // cannot be worn together with this gets dropped near it.
                stand.TryDropThingsToMakeRoomForThingOfDef(apparel.def);
                PutBack(stand, apparel);
            }

            if (undressing)
            {
                comp.NotifyUndressed(pawn);
                // Announced later, from the global finish action, once this
                // pawn's claim on the stand has actually been released.
                freedStand = comp;
            }
            else
            {
                comp.NotifyDressed(pawn, stored, issued, storedForced);
            }
        }

        /// <summary>
        /// The plan came up empty on arrival. Nothing has been moved yet, so
        /// the two directions want opposite things.
        ///
        /// On the return trip this is the LAST RESORT only. An empty incoming
        /// set has two causes and they want opposite handling: the pawn's own
        /// clothes are gone (burnt, hauled off, no longer fit), or the dress
        /// trip displaced nothing in the first place and there was never
        /// anything to give back. The caller separates them with
        /// <see cref="WearingAnythingBesides"/> and only sends the first case
        /// here — see the guard in <see cref="DoTransfer"/> for why the second
        /// must not arrive.
        /// </summary>
        internal void NothingToWear(CompShiftStand comp)
        {
            if (!undressing)
            {
                // Dressing at a stand with nothing this pawn can put on. No
                // state changed, so there is nothing to record — they work in
                // their own clothes, which is exactly what the selector's
                // WouldDress check exists to avoid and this is its backstop.
                return;
            }

            // The return trip with nothing to return to: their own clothes
            // burnt, were hauled off, or no longer fit. Stripping the uniform
            // regardless would leave the pawn naked — and the standing rule is
            // that the worst case of any failure path here is a pawn in the
            // WRONG clothes, never a pawn without them. So hand the uniform
            // over as ordinary clothing (forced cleared, so vanilla's
            // optimizer will re-dress them from the stockpile in the normal
            // way) and free the stand back into the pool.
            List<Apparel> uniform = comp.IssuedUniformForReading;
            for (int i = 0; i < uniform.Count; i++)
            {
                if (uniform[i] != null && pawn.apparel.WornApparel.Contains(uniform[i]))
                {
                    pawn.outfits?.forcedHandler?.SetForced(uniform[i], forced: false);
                }
            }
            // Deferred, for the same reason as the normal return trip: this
            // runs from a toil finish action, so the pawn still holds the
            // stand and nobody else could reserve it yet.
            comp.AbandonLedger(pawn, announce: false);
            freedStand = comp;
        }

        /// <summary>
        /// Tell the catch-up that this stand is back in the pool — from the
        /// job's teardown, after the tracker has released this pawn's claim on
        /// it, so a colonist already working bare in the room can actually
        /// reserve it.
        ///
        /// Fires for any end condition, because the only thing that arms it is
        /// <see cref="DoTransfer"/> having genuinely returned a uniform. A swap
        /// that failed, was interrupted, or never reached its last toil leaves
        /// <see cref="freedStand"/> null and announces nothing.
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
        /// Is the pawn wearing anything that is not in <paramref name="set"/>?
        /// Asked of the outgoing set on the return trip: true means the uniform
        /// can come off without stripping them, false means it is all they have
        /// on. Deliberately counts garments rather than body coverage — the
        /// standing rule is only that a failure path never leaves a pawn
        /// NAKED, and one surviving shirt satisfies it.
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
        /// Put a garment somewhere real. Called wherever a transfer step can
        /// leave an item held by neither the stand nor the pawn — the stand
        /// first, then the ground beside it, then the ground beside the pawn.
        /// Doing nothing in that state deletes the item from the world, so
        /// this never gives up silently. (Vanilla's own stand driver takes the
        /// unchecked gamble; we do not.)
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
