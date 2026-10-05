using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// What a swap at a given stand would actually move, for a given pawn: one
    /// function for the stand selector and the job driver both, because as two
    /// they disagreed (docs/DESIGN.md, "Wearability: one authority").
    ///
    /// Any predicate about wearability belongs here and nowhere else.
    /// </summary>
    internal static class SwapPlan
    {
        internal static readonly List<Apparel> ScratchWear = new List<Apparel>();
        internal static readonly List<Apparel> ScratchStore = new List<Apparel>();

        /// <summary>
        /// Every gate vanilla's own <c>Wear</c> applies, asked BEFORE anything
        /// is committed.
        ///
        /// <c>Pawn_ApparelTracker.Wear</c> is void and bails on each of these —
        /// no body parts, biocoded to someone else, <c>PawnCanWear</c> false —
        /// but it calls <c>newApparel.DeSpawnOrDeselect()</c> first
        /// (<c>Pawn_ApparelTracker.cs:434-450</c>). So a garment that fails one
        /// of them is ALREADY detached when the check fires: taken out of the
        /// stand, never put on the pawn, held by nothing, gone from the world
        /// with a single dev-log warning. Asking in advance is what stops that;
        /// the driver verifies the outcome afterwards regardless, because the
        /// pawn can lose a body part during the walk.
        /// </summary>
        public static bool CanWear(Pawn pawn, Apparel apparel)
        {
            return apparel != null
                   && pawn?.apparel != null
                   && apparel.PawnCanWear(pawn)
                   && ApparelUtility.HasPartsToWear(pawn, apparel.def)
                   && !(CompBiocodable.IsBiocoded(apparel) && !CompBiocodable.IsBiocodedFor(apparel, pawn));
        }

        /// <summary>
        /// Fills <paramref name="toWear"/> with what the pawn would take off the
        /// stand and <paramref name="toStore"/> with what they would take off
        /// themselves to make room.
        /// </summary>
        /// <returns>true if the swap would move anything at all.</returns>
        public static bool BuildDress(Pawn pawn, Building_OutfitStand stand,
                                      List<Apparel> toWear, List<Apparel> toStore)
        {
            toWear.Clear();
            toStore.Clear();

            if (pawn?.apparel == null || stand == null)
            {
                return false;
            }

            // The deposit-only stand issues nothing, so it cannot go through
            // the pass below — which is built around "what could this pawn put
            // on" and returns false the moment the answer is nothing. See
            // BuildDeposit for why that is a whole separate plan rather than a
            // flag threaded through this one.
            if (IsDepositOnly(stand))
            {
                return BuildDeposit(pawn, stand, toStore);
            }

            List<Apparel> worn = pawn.apparel.WornApparel;
            IReadOnlyList<Thing> held = stand.HeldItems;

            for (int i = 0; i < held.Count; i++)
            {
                Apparel candidate = held[i] as Apparel;
                if (!CanWear(pawn, candidate))
                {
                    continue;
                }

                bool canTake = true;

                // Against candidates already accepted this pass, not only the
                // worn list. The stand's one-outfit invariant normally makes
                // two conflicting garments impossible to stock — but that
                // invariant lives in Building_OutfitStand's own code
                // (HasRoomForApparelOfDef), not here, and if a storage mod
                // relaxes it, Wear() would silently drop the earlier garment
                // on the floor. Checked first because it needs no
                // displacement bookkeeping to roll back.
                for (int k = 0; k < toWear.Count; k++)
                {
                    if (!ApparelUtility.CanWearTogether(candidate.def, toWear[k].def, pawn.RaceProps.body))
                    {
                        canTake = false;
                        break;
                    }
                }
                if (!canTake)
                {
                    continue;
                }

                int storeCountBefore = toStore.Count;
                for (int j = 0; j < worn.Count; j++)
                {
                    Apparel wornItem = worn[j];
                    if (ApparelUtility.CanWearTogether(candidate.def, wornItem.def, pawn.RaceProps.body))
                    {
                        continue;
                    }
                    // Vanilla's own gate, and the only one it applies
                    // (JobDriver_UseOutfitStand.cs:49-53).
                    if (pawn.apparel.IsLocked(wornItem))
                    {
                        canTake = false;
                        break;
                    }
                    if (!toStore.Contains(wornItem))
                    {
                        toStore.Add(wornItem);
                    }
                }

                if (!canTake)
                {
                    // Undo anything this candidate provisionally displaced —
                    // it is not coming off after all.
                    toStore.RemoveRange(storeCountBefore, toStore.Count - storeCountBefore);
                    continue;
                }
                if (!toWear.Contains(candidate))
                {
                    toWear.Add(candidate);
                }
            }

            // Full change: everything else comes off too, so the pawn's
            // insulation becomes entirely the stand's kit rather than the kit
            // layered over their own clothes.
            //
            // Gated on toWear being non-empty: a stand that would issue NOTHING
            // must still decline, so the selector never sends a pawn on a trip
            // that cannot dress them. Undressing into a rack with nothing
            // issued is deposit-only's plan (BuildDeposit, behind the sleep
            // trigger), never a side effect of this pass, and DoTransfer
            // refuses on its own anyway, before a garment comes off, when a
            // dress trip's incoming set is empty.
            //
            // IsLocked is the one garment that stays, matching the conflict
            // pass above and vanilla's own stand driver
            // (JobDriver_UseOutfitStand.cs:49-53).
            if (toWear.Count > 0 && IsFullChange(stand))
            {
                for (int j = 0; j < worn.Count; j++)
                {
                    Apparel wornItem = worn[j];
                    if (!pawn.apparel.IsLocked(wornItem) && !toStore.Contains(wornItem))
                    {
                        toStore.Add(wornItem);
                    }
                }
            }

            // LAST, so it sees the finished plan — the conflict pass and the
            // full-change pass both feed it. Not a full-change guard: an
            // ordinary swap can strip a colonist too (docs/DESIGN.md, "The
            // dress path asks the same question and answers it differently").
            if (!KeepThemDecent(pawn, toWear, toStore))
            {
                toWear.Clear();
                toStore.Clear();
                return false;
            }

            return toWear.Count > 0;
        }

        /// <summary>
        /// Read from the stand rather than passed in by the caller, so the
        /// selector and the driver cannot form different plans for the same
        /// stand — the disagreement this whole file exists to prevent.
        /// </summary>
        internal static bool IsFullChange(Building_OutfitStand stand)
        {
            return stand?.TryGetComp<CompShiftStand>()?.FullChange == true;
        }

        /// <summary>
        /// Read from the stand for the same reason as <see cref="IsFullChange"/>
        /// — one source, so the selector and the driver cannot disagree.
        /// <see cref="CompShiftStand.DepositOnly"/> is itself gated on the
        /// stand being a SLEEP stand, which is what keeps the undress-only
        /// plan out of the work and recreation arms.
        /// </summary>
        internal static bool IsDepositOnly(Building_OutfitStand stand)
        {
            return stand?.TryGetComp<CompShiftStand>()?.DepositOnly == true;
        }

        /// <summary>
        /// The plan for a stand that hands nothing out: the pawn parks the
        /// garments this stand's STORAGE FILTER accepts and keeps the rest on.
        ///
        /// <para>The filter is the whole control surface, and a fresh stand's
        /// filter accepts almost everything worn, so the safety of this path
        /// rests ENTIRELY on <see cref="WouldBeNude"/> below (docs/DESIGN.md,
        /// "The sleep branch").</para>
        ///
        /// <para>Conflicts with whatever the stand already holds are NOT
        /// resolved here, matching the dress path: the driver calls
        /// <c>TryDropThingsToMakeRoomForThingOfDef</c> before each PutBack, so
        /// eviction is vanilla's and happens once, at transfer time.</para>
        /// </summary>
        /// <returns>true if anything would be deposited.</returns>
        internal static bool BuildDeposit(Pawn pawn, Building_OutfitStand stand, List<Apparel> toStore)
        {
            StorageSettings settings = stand.GetStoreSettings();
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                Apparel item = worn[i];
                if (item == null)
                {
                    continue;
                }
                // Vanilla's own gate, the same one the conflict pass and
                // vanilla's stand driver apply (JobDriver_UseOutfitStand.cs:49-53).
                if (pawn.apparel.IsLocked(item))
                {
                    continue;
                }
                // Both halves of "would this stand take it": the player's
                // filter, and the def's fixed settings underneath it — a
                // modded stand may refuse a category outright, and depositing
                // into one would only bounce the garment back onto the floor.
                if (settings != null && !settings.AllowedToAccept(item))
                {
                    continue;
                }
                if (!stand.CanEverStoreThing(item))
                {
                    continue;
                }
                toStore.Add(item);
            }

            if (toStore.Count == 0)
            {
                return false;
            }

            // NEVER STRIP THEM BARE. The standing rule on every path through
            // this file is that the worst case is a pawn in the WRONG clothes,
            // never a pawn without them. Asked by coverage, not by counting
            // garments: utility apparel (a shield belt, a smokepop belt, a jump
            // pack) covers neither Torso nor Legs, and is exactly what the
            // stand's DEFAULT filter excludes. Declining outright rather than
            // holding a garment back is deliberate, unlike the dress path.
            // Both: docs/DESIGN.md, "The sleep branch".
            if (WouldBeNude(pawn, toStore))
            {
                toStore.Clear();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Would depositing <paramref name="leaving"/> make this pawn
        /// psychologically nude?
        ///
        /// <para>A transcription of <c>Pawn_ApparelTracker.PsychologicallyNude</c>
        /// (<c>:186-228</c>) and the <c>HasBasicApparel</c> it calls
        /// (<c>:667-688</c>), evaluated against the apparel that would REMAIN
        /// rather than against what is worn now. Vanilla's standard, not a
        /// stricter one: docs/DESIGN.md, "The sleep branch".</para>
        ///
        /// <para>Transcribed rather than called because the engine's version
        /// asks about <c>wornApparel</c> and the question here is
        /// hypothetical. Keep the two in step: this is what decides whether a
        /// colonist takes the Naked thought every night for the rest of the
        /// game.</para>
        /// </summary>
        internal static bool WouldBeNude(Pawn pawn, List<Apparel> leaving)
        {
            return WouldBeNude(pawn, leaving, null);
        }

        /// <summary>
        /// The same question, asked about a plan that ALSO puts garments on:
        /// the set to judge is <c>worn - leaving + arriving</c>. The deposit
        /// form above would report every ordinary uniform swap as nudity,
        /// because the uniform is not on the pawn yet.
        /// </summary>
        internal static bool WouldBeNude(Pawn pawn, List<Apparel> leaving, List<Apparel> arriving)
        {
            // Vanilla's two exemptions, in its order.
            if (pawn.gender == Gender.None || pawn.IsWildMan())
            {
                return false;
            }

            Coverage(pawn, leaving, arriving, out bool hasShirt, out bool hasPants);

            // A pawn with no legs left cannot be trouserless (vanilla, :198-213).
            if (!hasPants)
            {
                bool anyLegs = false;
                foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
                {
                    if (part.IsInGroup(BodyPartGroupDefOf.Legs))
                    {
                        anyLegs = true;
                        break;
                    }
                }
                if (!anyLegs)
                {
                    hasPants = true;
                }
            }

            return pawn.gender == Gender.Male ? !hasPants : !hasPants || !hasShirt;
        }

        /// <summary>
        /// Which of vanilla's two decency groups a hypothetical outfit covers.
        /// Layer is deliberately not consulted: <c>HasBasicApparel</c>
        /// (<c>:667-688</c>) scans every worn garment for <c>Torso</c> and
        /// <c>Legs</c> regardless of layer, so a Belt-layer item declaring
        /// Torso counts as a shirt. Ours has to agree with it, not improve on
        /// it.
        /// </summary>
        internal static void Coverage(Pawn pawn, List<Apparel> leaving, List<Apparel> arriving,
                                      out bool hasShirt, out bool hasPants)
        {
            hasShirt = false;
            hasPants = false;
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                if (leaving == null || !leaving.Contains(worn[i]))
                {
                    Count(worn[i], ref hasShirt, ref hasPants);
                }
            }
            if (arriving == null)
            {
                return;
            }
            for (int i = 0; i < arriving.Count; i++)
            {
                Count(arriving[i], ref hasShirt, ref hasPants);
            }
        }

        internal static void Count(Apparel item, ref bool hasShirt, ref bool hasPants)
        {
            List<BodyPartGroupDef> groups = item?.def?.apparel?.bodyPartGroups;
            if (groups == null)
            {
                return;
            }
            for (int j = 0; j < groups.Count; j++)
            {
                if (groups[j] == BodyPartGroupDefOf.Torso)
                {
                    hasShirt = true;
                }
                else if (groups[j] == BodyPartGroupDefOf.Legs)
                {
                    hasPants = true;
                }
            }
        }

        /// <summary>
        /// Hold back whatever the plan must leave on to keep a dressed colonist
        /// dressed. Returns false only when that is impossible, in which case
        /// the caller must abandon the swap.
        ///
        /// <para>The rule is "never make it worse": a colonist already nude is
        /// left alone, and only a plan that takes a decent colonist and leaves
        /// them indecent is intervened on. The innermost garment is held back,
        /// rather than the swap declined as the deposit path does; why:
        /// docs/DESIGN.md, "The dress path asks the same question and answers
        /// it differently".</para>
        ///
        /// <para>Two passes at most: vanilla asks about exactly two body part
        /// groups, so at most one garment is needed for each.</para>
        /// </summary>
        internal static bool KeepThemDecent(Pawn pawn, List<Apparel> toWear, List<Apparel> toStore)
        {
            if (pawn?.apparel == null || toStore.Count == 0)
            {
                return true;
            }
            // The global override. This guard can contradict a deliberate
            // configuration, and for a colony built around nudity it would do
            // so constantly, so it is defeatable outright.
            if (!ShiftChangeMod.DecencyEnabled)
            {
                return true;
            }
            // And the per-pawn exemption, which is the common case: one nudist
            // in an otherwise ordinary colony should not cost the whole
            // setting.
            if (PrefersNudity(pawn))
            {
                return true;
            }
            if (!WouldBeNude(pawn, toStore, toWear))
            {
                return true;
            }
            // Already indecent before we touched them: not ours to fix, and not
            // ours to refuse over.
            if (WouldBeNude(pawn, null, null))
            {
                return true;
            }

            for (int pass = 0; pass < 2; pass++)
            {
                Apparel best = null;
                int bestLayer = int.MaxValue;
                for (int i = 0; i < toStore.Count; i++)
                {
                    Apparel candidate = toStore[i];
                    if (candidate == null || !ReducesExposure(pawn, candidate, toWear, toStore))
                    {
                        continue;
                    }
                    if (ConflictsWithIssued(pawn, candidate, toWear))
                    {
                        continue;
                    }
                    int layer = InnermostLayer(candidate);
                    if (layer < bestLayer)
                    {
                        bestLayer = layer;
                        best = candidate;
                    }
                }
                if (best == null)
                {
                    break;
                }
                toStore.Remove(best);
                if (!WouldBeNude(pawn, toStore, toWear))
                {
                    return true;
                }
            }

            return !WouldBeNude(pawn, toStore, toWear);
        }

        /// <summary>
        /// Is being undressed what this colonist actually wants? Vanilla's own
        /// answer, the trait test <c>JobGiver_PrisonerGetDressed:15</c> uses,
        /// plus Ideology's nudity precepts, which that test does not reach and
        /// which are read per pawn, by gender. Why each half, and why vanilla's
        /// warmth clause is not copied: docs/DESIGN.md, "The dress path asks
        /// the same question and answers it differently".
        /// </summary>
        internal static bool PrefersNudity(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }
            if (pawn.story?.traits != null
                && ThoughtUtility.CanGetThought(pawn, ThoughtDefOf.ClothedNudist,
                                                checkIfNullified: true))
            {
                return true;
            }
            return ModsConfig.IdeologyActive
                   && pawn.Ideo != null
                   && pawn.Ideo.IdeoPrefersNudityForGender(pawn.gender);
        }

        /// <summary>
        /// Would keeping this garment on cover something the plan currently
        /// leaves bare? Asked by trial rather than by reasoning about groups,
        /// so it cannot drift from <see cref="Coverage"/>.
        /// </summary>
        internal static bool ReducesExposure(Pawn pawn, Apparel candidate,
                                             List<Apparel> toWear, List<Apparel> toStore)
        {
            Coverage(pawn, toStore, toWear, out bool hasShirt, out bool hasPants);
            bool withShirt = hasShirt;
            bool withPants = hasPants;
            Count(candidate, ref withShirt, ref withPants);
            return withShirt != hasShirt || withPants != hasPants;
        }

        internal static bool ConflictsWithIssued(Pawn pawn, Apparel candidate, List<Apparel> toWear)
        {
            for (int k = 0; k < toWear.Count; k++)
            {
                if (toWear[k] != null
                    && !ApparelUtility.CanWearTogether(candidate.def, toWear[k].def,
                                                      pawn.RaceProps.body))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The lowest draw order this garment occupies. Vanilla's layers are a
        /// clean total order (OnSkin 0, Middle 100, Shell 200, Belt 300,
        /// Overhead 400, EyeCover 500) and modded layers slot in by their own
        /// value, so this ranks any garment from any mod without a table.
        /// </summary>
        internal static int InnermostLayer(Apparel apparel)
        {
            List<ApparelLayerDef> layers = apparel?.def?.apparel?.layers;
            if (layers == null || layers.Count == 0)
            {
                return int.MaxValue;
            }
            int lowest = int.MaxValue;
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i] != null && layers[i].drawOrder < lowest)
                {
                    lowest = layers[i].drawOrder;
                }
            }
            return lowest;
        }

        /// <summary>
        /// Cheap yes/no for the stand selector: would sending this pawn here
        /// accomplish anything? Uses shared scratch lists — safe because the
        /// selector runs to completion before any job is started.
        /// </summary>
        public static bool WouldDress(Pawn pawn, Building_OutfitStand stand)
        {
            return BuildDress(pawn, stand, ScratchWear, ScratchStore);
        }
    }
}
