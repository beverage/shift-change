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
        // (principal's call, 2026-08-07). Blocking could only ever mean
        // refusing the uniform, since the guard's lever is "don't remove the
        // robe" — so a titled pawn would silently never change, which is worse
        // than the mood hit. Nor do we warn: assigning this pawn to this stand
        // and stocking it are deliberate player acts, and vanilla already
        // surfaces the consequence where a player would look for it, via
        // ThoughtWorker_RoyalTitleApparelRequirementNotMet and
        // Thought_IdeoRoleApparelRequirementNotMet.

        protected override IEnumerable<Toil> MakeNewToils()
        {
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
            toWear.RemoveAll(a => a == null || a.ParentHolder != stand || !SwapPlan.CanWear(pawn, a));
            if (toWear.Count == 0)
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
            }
            else
            {
                comp.NotifyDressed(pawn, stored, issued, storedForced);
            }
        }

        /// <summary>
        /// The plan came up empty on arrival. Nothing has been moved yet, so
        /// the two directions want opposite things.
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
            comp.AbandonLedger(pawn);
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
