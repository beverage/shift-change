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
        private int duration;
        private bool undressing;
        private List<Apparel> toWear = new List<Apparel>();
        private List<Apparel> toStore = new List<Apparel>();

        private Building_OutfitStand Stand => job.targetA.Thing as Building_OutfitStand;

        private CompShiftStand Comp => Stand?.TryGetComp<CompShiftStand>();

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

        /// <summary>Going on shift: stand's clothes on, own clothes into the stand.</summary>
        private void PlanDress(CompShiftStand comp)
        {
            List<Apparel> worn = pawn.apparel.WornApparel;
            foreach (Thing thing in Stand.HeldItems)
            {
                Apparel candidate = thing as Apparel;
                if (candidate == null
                    || !candidate.PawnCanWear(pawn)
                    || !ApparelUtility.HasPartsToWear(pawn, candidate.def)
                    || (CompBiocodable.IsBiocoded(candidate) && !CompBiocodable.IsBiocodedFor(candidate, pawn)))
                {
                    continue;
                }

                bool canTake = true;
                List<Apparel> displaced = new List<Apparel>();
                foreach (Apparel wornItem in worn)
                {
                    if (ApparelUtility.CanWearTogether(candidate.def, wornItem.def, pawn.RaceProps.body))
                    {
                        continue;
                    }
                    // Vanilla's own gate, and the only one it applies.
                    if (pawn.apparel.IsLocked(wornItem))
                    {
                        canTake = false;
                        break;
                    }
                    displaced.Add(wornItem);
                }

                if (!canTake)
                {
                    continue;
                }
                foreach (Apparel item in displaced)
                {
                    if (!toStore.Contains(item))
                    {
                        WarnIfRequired(item);
                        toStore.Add(item);
                    }
                }
                if (!toWear.Contains(candidate))
                {
                    toWear.Add(candidate);
                }
            }
        }

        /// <summary>Going off shift: uniform back in the stand, own clothes on.</summary>
        private void PlanUndress(CompShiftStand comp)
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

        /// <summary>
        /// A royal title or ideology role can REQUIRE a garment, and nothing in
        /// vanilla stops a stand swap removing it — the optimizer only scores
        /// requirements (×25/×10) and the vanilla driver checks the much
        /// narrower <c>IsLocked</c>. Principal's call (2026-08-07) is that the
        /// penalty is not worth blocking the feature over, so this warns once
        /// rather than refusing.
        /// </summary>
        private void WarnIfRequired(Apparel apparel)
        {
            foreach (ApparelRequirementWithSource requirement in pawn.apparel.AllRequirements)
            {
                if (requirement.requirement.RequiredForPawn(pawn, apparel.def))
                {
                    Log.WarningOnce(
                        $"[ShiftChange] {pawn.LabelShort} is swapping out of {apparel.LabelCap}, which a title or "
                        + "role requires. The uniform wins; expect the usual unmet-requirement penalty while on shift.",
                        Gen.HashCombineInt(pawn.thingIDNumber, apparel.def.shortHash));
                    return;
                }
            }
        }

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

        private void DoTransfer()
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
            foreach (Apparel apparel in toStore)
            {
                if (apparel != null && pawn.apparel.WornApparel.Contains(apparel))
                {
                    if (undressing)
                    {
                        pawn.outfits?.forcedHandler?.SetForced(apparel, forced: false);
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
                issued.Add(apparel);
            }

            foreach (Apparel apparel in stored)
            {
                // Vanilla's own eviction: anything already in the stand that
                // cannot be worn together with this gets dropped near it.
                stand.TryDropThingsToMakeRoomForThingOfDef(apparel.def);
                stand.AddApparel(apparel);
            }

            if (undressing)
            {
                comp.NotifyUndressed(pawn);
            }
            else
            {
                comp.NotifyDressed(pawn, stored, issued);
            }
        }
    }
}
