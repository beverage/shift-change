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
                if (apparel != null && apparel.ParentHolder == Stand && apparel.PawnCanWear(pawn))
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
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A);

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
            if (stand == null || comp == null)
            {
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
                if (!stand.AddApparel(apparel))
                {
                    // The garment is detached from the pawn and refused by the
                    // stand — held by nothing. Drop it on the floor, where it
                    // survives; doing nothing here would delete it from the
                    // world. (Vanilla's driver takes the same gamble
                    // unchecked; a mod relaxing the stand's storage
                    // invariants would lose items through it.)
                    if (!GenPlace.TryPlaceThing(apparel, stand.Position, stand.Map, ThingPlaceMode.Near))
                    {
                        Log.Error("[ShiftChange] could not store or place " + apparel.LabelCap + " — it may be lost.");
                    }
                }
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
    }
}
