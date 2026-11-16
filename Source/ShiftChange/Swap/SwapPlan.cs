using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// What a swap at a given stand would move for a given pawn. The stand selector and the
    /// job driver both ask here, so they cannot form different plans for the same stand.
    /// Any predicate about wearability belongs here and nowhere else.
    /// </summary>
    internal static class SwapPlan
    {
        internal static readonly List<Apparel> ScratchWear = new List<Apparel>();
        internal static readonly List<Apparel> ScratchStore = new List<Apparel>();

        /// <summary>
        /// Every gate vanilla's own <c>Wear</c> applies, asked before anything is committed.
        /// <c>Pawn_ApparelTracker.Wear</c> is void and bails on each of these (no body parts,
        /// biocoded to someone else, <c>PawnCanWear</c> false), but it calls
        /// <c>newApparel.DeSpawnOrDeselect()</c> first. A garment that fails one is already
        /// detached when the check fires: out of the stand, not on the pawn, held by nothing,
        /// and gone from the world with a single dev-log warning.
        /// </summary>
        public static bool CanWear(Pawn pawn, Apparel apparel)
        {
            return apparel != null
                   && pawn?.apparel != null
                   && apparel.PawnCanWear(pawn)
                   && ApparelUtility.HasPartsToWear(pawn, apparel.def)
                   && !(CompBiocodable.IsBiocoded(apparel) && !CompBiocodable.IsBiocodedFor(apparel, pawn));
        }

        /// <summary>Fills toWear (off the stand) and toStore (off the pawn, for room).</summary>
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

            // A deposit-only stand issues nothing, so it cannot use the pass below, which gives
            // up as soon as nothing can be worn.
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

                // Against candidates already accepted this pass, not only the worn list: a
                // storage mod can relax the stand's one-outfit invariant
                // (Building_OutfitStand.HasRoomForApparelOfDef), and Wear would then silently
                // drop the earlier garment on the floor. Checked first because it has no
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
                    // Vanilla's stand driver (JobDriver_UseOutfitStand) applies this gate and no
                    // other when taking worn items off.
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
                    // Undo what this candidate provisionally displaced; it is not going on.
                    toStore.RemoveRange(storeCountBefore, toStore.Count - storeCountBefore);
                    continue;
                }
                if (!toWear.Contains(candidate))
                {
                    toWear.Add(candidate);
                }
            }

            // Full change: everything else comes off too, so the pawn's insulation is entirely
            // the stand's kit. Gated on toWear being non-empty: a stand that would issue nothing
            // must decline, so the selector never sends a pawn on a trip that cannot dress them
            // (undressing with nothing issued is BuildDeposit's plan). A locked garment stays
            // on, as in the conflict pass.
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

            // Last, so it sees the finished plan from both passes above. It guards every swap,
            // not only a full change: an ordinary conflict swap can strip a colonist too.
            if (!KeepThemDecent(pawn, toWear, toStore))
            {
                toWear.Clear();
                toStore.Clear();
                return false;
            }

            return toWear.Count > 0;
        }

        /// <summary>Read from the stand, so the selector and the driver plan alike.</summary>
        internal static bool IsFullChange(Building_OutfitStand stand)
        {
            return stand?.TryGetComp<CompShiftStand>()?.FullChange == true;
        }

        /// <summary>
        /// Read from the stand, like <see cref="IsFullChange"/>.
        /// <see cref="CompShiftStand.DepositOnly"/> is true only for a sleep stand, which keeps
        /// the undress-only plan out of the work and recreation arms.
        /// </summary>
        internal static bool IsDepositOnly(Building_OutfitStand stand)
        {
            return stand?.TryGetComp<CompShiftStand>()?.DepositOnly == true;
        }

        /// <summary>
        /// The plan for a stand that hands nothing out: the pawn parks the garments this stand's
        /// storage filter accepts and keeps the rest on. A new stand's filter accepts nearly
        /// everything worn, so the safety of this path rests entirely on
        /// <see cref="WouldBeNude(Pawn, List{Apparel})"/> (docs/DESIGN.md, "The sleep branch").
        /// Conflicts with what the stand already holds are not resolved here, as on the dress
        /// path: the driver calls <c>TryDropThingsToMakeRoomForThingOfDef</c> before each
        /// PutBack, so vanilla evicts once, at transfer time.
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
                // Locked garments stay on: the one gate vanilla's stand driver applies.
                if (pawn.apparel.IsLocked(item))
                {
                    continue;
                }
                // Both halves of "would this stand take it": the player's filter, and the def's
                // fixed settings underneath it. A modded stand may refuse a category outright,
                // and depositing into one would only bounce the garment back onto the floor.
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

            // Never strip the pawn bare: on every path through this file the worst case is a
            // pawn in the wrong clothes, not a pawn without them. Judged by coverage, not by
            // garment count: utility apparel (a shield belt, a jump pack) covers neither Torso
            // nor Legs and is what the stand's default filter excludes, so a count would call
            // the pawn dressed on a belt alone.
            if (WouldBeNude(pawn, toStore))
            {
                toStore.Clear();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Would depositing <paramref name="leaving"/> make this pawn psychologically nude?
        /// A transcription of <c>Pawn_ApparelTracker.PsychologicallyNude</c> and the
        /// <c>HasBasicApparel</c> it calls, evaluated against the apparel that would remain,
        /// because the engine's version asks about <c>wornApparel</c> and the question here is
        /// hypothetical. It is vanilla's standard, not a stricter one: a test requiring torso
        /// and legs for everyone would refuse a man left in trousers, which vanilla accepts.
        /// Keep the two in step: the engine's answer is what gives a colonist the Naked thought.
        /// </summary>
        internal static bool WouldBeNude(Pawn pawn, List<Apparel> leaving)
        {
            return WouldBeNude(pawn, leaving, null);
        }

        /// <summary>
        /// The same question for a plan that also puts garments on: the set to judge is
        /// <c>worn - leaving + arriving</c>. The deposit form above would report every ordinary
        /// uniform swap as nudity, because the uniform is not on the pawn yet.
        /// </summary>
        internal static bool WouldBeNude(Pawn pawn, List<Apparel> leaving, List<Apparel> arriving)
        {
            // Vanilla never counts a pawn with no gender, or a wild man, as nude.
            if (pawn.gender == Gender.None || pawn.IsWildMan())
            {
                return false;
            }

            Coverage(pawn, leaving, arriving, out bool hasShirt, out bool hasPants);

            // Vanilla counts a pawn with no legs left as trousered.
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
        /// Which of vanilla's two decency groups a hypothetical outfit covers. Layer is
        /// deliberately not consulted: <c>HasBasicApparel</c> scans every worn garment for
        /// <c>Torso</c> and <c>Legs</c> regardless of layer, so a Belt-layer item declaring
        /// Torso counts as a shirt. Ours has to agree with it, not improve on it.
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
        /// Holds back whatever the plan must leave on to keep a dressed colonist dressed.
        /// Returns false only when that is impossible, in which case the caller must abandon
        /// the swap. The rule is "never make it worse": a colonist already nude is left alone.
        /// The innermost garment is held back, rather than the swap declined as the deposit path
        /// does (docs/DESIGN.md, "The dress path asks the same question and answers it
        /// differently"). Two passes at most: vanilla asks about exactly two body part groups,
        /// so at most one garment is needed for each.
        /// </summary>
        internal static bool KeepThemDecent(Pawn pawn, List<Apparel> toWear, List<Apparel> toStore)
        {
            if (pawn?.apparel == null || toStore.Count == 0)
            {
                return true;
            }
            // The mod setting: a colony built around nudity would fight this guard constantly.
            if (!ShiftChangeMod.DecencyEnabled)
            {
                return true;
            }
            // The per-pawn exemption, so one nudist does not force the setting off for everyone.
            if (PrefersNudity(pawn))
            {
                return true;
            }
            if (!WouldBeNude(pawn, toStore, toWear))
            {
                return true;
            }
            // Already nude before the swap: not this plan's doing, so not ours to fix or refuse.
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
        /// Is being undressed what this colonist wants? Vanilla's own answer, the test
        /// <c>JobGiver_PrisonerGetDressed</c> uses (can the pawn get the <c>ClothedNudist</c>
        /// thought), plus Ideology's nudity precepts: a pawn with no Nudist trait in a nudist
        /// ideoligion gets no such thought, so the precepts are read per pawn, by gender.
        /// Vanilla's prisoner check also requires the pawn to be warm enough; that clause is
        /// left out on purpose (docs/DESIGN.md, "The dress path asks the same question and
        /// answers it differently").
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

        /// <summary>Would keeping this garment on cover something the plan leaves bare?</summary>
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
        /// The lowest <c>drawOrder</c> of the layers this garment occupies. Vanilla numbers its
        /// layers from the skin outward (OnSkin 0, Middle 100, Shell 200, then Belt, Overhead and
        /// EyeCover), so the lowest is the innermost. Modded layers carry their own, so this ranks
        /// any garment from any mod without a table.
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

        /// <summary>What these garments add to their wearer's vacuum resistance.</summary>
        internal static float VacuumResistanceOf(List<Apparel> items)
        {
            float sum = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null)
                {
                    sum += StatWorker.StatOffsetFromGear(items[i], StatDefOf.VacuumResistance);
                }
            }
            return sum;
        }

        /// <summary>
        /// The pawn's vacuum resistance before the stat clamps it to 1. Genes and implants add
        /// to it as gear does, so a pawn immune without their suit still reads at 1 or over
        /// once the suit's share is taken off, where the clamped stat would hide that.
        /// </summary>
        internal static float UnclampedVacuumResistance(Pawn pawn)
        {
            return StatDefOf.VacuumResistance.Worker.GetValueUnfinalized(StatRequest.For(pawn));
        }

        /// <summary>
        /// Vacuum never harms this pawn, whatever they wear (<c>Pawn.HarmedByVacuum</c>).
        /// </summary>
        internal static bool ImmuneToVacuum(Pawn pawn)
        {
            return pawn.RaceProps.IsMechanoid || (pawn.IsMutant && !pawn.mutant.Def.breathesAir);
        }

        /// <summary>
        /// Swapping <paramref name="leaving"/> for <paramref name="arriving"/> leaves the pawn
        /// less protected from vacuum than they are, and below a resistance of 1, where vacuum
        /// stops harming. Harm grows all the way to 1, so the line is 1, not the 0.75 at which
        /// vanilla starts routing work around vacuum. docs/DESIGN.md, "Vacuum".
        /// </summary>
        internal static bool LeavesThemLessProtected(Pawn pawn, List<Apparel> leaving, List<Apparel> arriving)
        {
            if (ImmuneToVacuum(pawn))
            {
                return false;
            }
            float change = VacuumResistanceOf(arriving) - VacuumResistanceOf(leaving);
            return change < 0f && UnclampedVacuumResistance(pawn) + change < 1f;
        }

        /// <summary>
        /// The same question for a pawn already on shift: the clothes they parked would protect
        /// them from vacuum better than the uniform they wear, and they are below a resistance
        /// of 1. Read off the ledger, so it answers exactly as
        /// <see cref="LeavesThemLessProtected"/> answered before the swap.
        /// </summary>
        internal static bool UniformLeavesThemLessProtected(Pawn pawn, List<Apparel> issued, List<Apparel> parked)
        {
            if (ImmuneToVacuum(pawn))
            {
                return false;
            }
            float gain = VacuumResistanceOf(parked) - VacuumResistanceOf(issued);
            return gain > 0f && UnclampedVacuumResistance(pawn) < 1f;
        }

        /// <summary>
        /// Can this pawn take this weapon in hand? The five checks vanilla's stand job makes
        /// before it hands a weapon over, plus what our transfer needs: an equipment tracker, a
        /// wardrobe, and a weapon that can be equipped at all, since
        /// <c>Pawn_EquipmentTracker.AddEquipment</c> reads its <c>CompEquippable</c> unchecked.
        ///
        /// <para>Stricter than vanilla's stand in one place: a persona weapon goes only to the
        /// pawn it is bonded to. Equipping one bonds it for good, and the player's equip order
        /// asks first (<c>EquipmentUtility.GetPersonaWeaponConfirmationText</c>, whose test this
        /// is). A stand has nobody to ask.</para>
        ///
        /// <para>A weapon that stacks stays put as well: <see cref="WeaponHands"/> tracks one
        /// thing, and a stack can merge on the ground or come off the stand whole.</para>
        /// </summary>
        internal static bool CanWield(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn?.equipment == null || pawn.apparel == null || weapon == null
                || weapon.TryGetComp<CompEquippable>() == null || weapon.def.stackLimit > 1)
            {
                return false;
            }
            CompBladelinkWeapon persona = weapon.TryGetComp<CompBladelinkWeapon>();
            if (persona != null && persona.Biocodable && persona.CodedPawn != pawn)
            {
                return false;
            }
            if (weapon.def.IsWeapon && pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                return false;
            }
            if (weapon.def.IsRangedWeapon && pawn.WorkTagIsDisabled(WorkTags.Shooting))
            {
                return false;
            }
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                return false;
            }
            if (pawn.IsQuestLodger() && !EquipmentUtility.QuestLodgerCanEquip(weapon, pawn))
            {
                return false;
            }
            return EquipmentUtility.CanEquip(weapon, pawn, out _);
        }

        /// <summary>
        /// May the pawn's hand give way to <paramref name="issued"/>, its weapon going to their
        /// pack? Not for one that <c>GenDrop</c> destroys or merges into a stack, since a hand is
        /// emptied by a drop, and not for the same def, which would gain nothing. A persona weapon
        /// gives way like any other; what a jealous one costs is <see cref="NeedsAssigning"/>'s.
        /// A quest lodger's weapon they may not unequip is <see cref="CanWield"/>'s to refuse.
        /// </summary>
        internal static bool HandAllows(Pawn pawn, ThingWithComps issued)
        {
            ThingWithComps hand = pawn.equipment.Primary;
            return hand == null
                   || (!hand.def.destroyOnDrop
                       && hand.def.stackLimit == 1
                       && hand.def != issued.def);
        }

        /// <summary>
        /// The stand's weapon this pawn would take in hand, or null. A deposit-only stand hands
        /// nothing out, and a shared stand holds back what <see cref="NeedsAssigning"/> names.
        /// The selector and the driver both ask here, the driver again on arrival, so they agree
        /// on who gets it.
        /// </summary>
        internal static ThingWithComps WeaponToIssue(Pawn pawn, Building_OutfitStand stand)
        {
            if (stand == null || IsDepositOnly(stand))
            {
                return null;
            }
            ThingWithComps weapon = stand.HeldWeapon;
            if (!CanWield(pawn, weapon) || !HandAllows(pawn, weapon))
            {
                return null;
            }
            return !NeedsAssigning(pawn, weapon)
                   || stand.TryGetComp<CompShiftStand>()?.IsAssignedTo(pawn) == true
                ? weapon
                : null;
        }

        /// <summary>
        /// Would handing this weapon over want the player's say-so? A stand has nobody to ask, so
        /// assigning it to the pawn is the answer. An armed slave rebels four times as soon
        /// (<c>SlaveRebellionUtility</c>, tools included), a Brawler holding a ranged weapon
        /// loses mood, which vanilla's stand warns of in its equip order, and so does a pawn whose
        /// jealous persona weapon sees them take up another (<see cref="AngersJealousPersona"/>).
        /// </summary>
        internal static bool NeedsAssigning(Pawn pawn, ThingWithComps weapon)
        {
            return pawn.IsSlave
                   || (weapon.def.IsRangedWeapon
                       && pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true)
                   || AngersJealousPersona(pawn, weapon);
        }

        /// <summary>
        /// Would a loan of this weapon have the pawn take up one that is not their bonded persona
        /// weapon, while that weapon has a trait that answers it? While Royalty is active,
        /// <c>Pawn_EquipmentTracker.Notify_EquipmentAdded</c> tells the pawn's bonded weapon of
        /// every primary they equip, wherever the bonded one lies, and each trait's worker hears it
        /// through <c>WeaponTraitWorker.Notify_OtherWeaponWielded</c>. Royalty's Jealous trait
        /// answers with a mood penalty that lasts a day or until the bonded weapon is back in hand,
        /// and only for a pawn with a mood. A worker from another mod that overrides the same hook
        /// counts whatever it does. A loan equips the stand's weapon, then the pawn's own on a
        /// draft and on the change back, so only the persona weapon itself, lent to an empty hand,
        /// equips nothing else. Traits are read by their worker class, which instantiates nothing.
        /// </summary>
        internal static bool AngersJealousPersona(Pawn pawn, ThingWithComps weapon)
        {
            Thing bonded = pawn.equipment?.bondedWeapon;
            if (!ModsConfig.RoyaltyActive || bonded == null || bonded.Destroyed
                || (bonded == weapon && pawn.equipment.Primary == null))
            {
                return false;
            }
            List<WeaponTraitDef> traits = bonded.TryGetComp<CompBladelinkWeapon>()?.TraitsListForReading;
            if (traits == null)
            {
                return false;
            }
            for (int i = 0; i < traits.Count; i++)
            {
                Type worker = traits[i].workerClass;
                if (AnswersAnotherWeapon(worker)
                    && (pawn.needs?.mood != null || !typeof(WeaponTraitWorker_Jealous).IsAssignableFrom(worker)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Each trait worker class read by <see cref="AnswersAnotherWeapon"/>.</summary>
        internal static readonly Dictionary<Type, bool> WorkersThatAnswer = new Dictionary<Type, bool>();

        /// <summary>
        /// Does this trait worker class override <c>WeaponTraitWorker.Notify_OtherWeaponWielded</c>,
        /// the hook the equipment tracker calls when the bonded pawn equips another weapon? A class
        /// that only inherits it does nothing there. Read once per class.
        /// </summary>
        internal static bool AnswersAnotherWeapon(Type worker)
        {
            if (worker == null)
            {
                return false;
            }
            if (!WorkersThatAnswer.TryGetValue(worker, out bool answers))
            {
                MethodInfo hook = worker.GetMethod(nameof(WeaponTraitWorker.Notify_OtherWeaponWielded),
                                                   BindingFlags.Public | BindingFlags.Instance, null,
                                                   new[] { typeof(CompBladelinkWeapon) }, null);
                answers = hook != null && hook.DeclaringType != typeof(WeaponTraitWorker);
                WorkersThatAnswer[worker] = answers;
            }
            return answers;
        }

        /// <summary>
        /// The pack weapon a change of draft brings out: the pawn's own on drafting, the stand's
        /// on undrafting. It takes over from the other one or an empty hand, and anything else
        /// in the hand was the pawn's choice and stays. A persona weapon trades like any other: a
        /// loan that would anger a jealous one opened only on a stand assigned to the pawn
        /// (<see cref="AngersJealousPersona"/>). Null when nothing should move. The setter also
        /// runs inside downing and on a pawn mid-break, so only a spawned pawn on their feet and
        /// in their right mind trades weapons.
        /// </summary>
        internal static ThingWithComps WeaponForDraft(Pawn pawn, CompShiftStand comp, bool drafted)
        {
            if (!pawn.Spawned || pawn.Downed || pawn.InMentalState)
            {
                return null;
            }
            ThingWithComps incoming = drafted ? comp.ownWeapon : comp.issuedWeapon;
            ThingWithComps outgoing = drafted ? comp.issuedWeapon : comp.ownWeapon;
            ThingWithComps hand = pawn.equipment?.Primary;
            bool inPack = incoming != null && pawn.inventory != null
                          && pawn.inventory.innerContainer.Contains(incoming);
            return inPack && (hand == null || hand == outgoing) ? incoming : null;
        }

        /// <summary>
        /// Cheap yes/no for the stand selector: would sending this pawn here accomplish
        /// anything? Builds the plan into the shared scratch lists, so it stands only until the
        /// next call. Safe because the selector runs to completion before any job is started.
        /// The lists hold apparel alone, so the vacuum check that reads them sees a weapon-only
        /// plan as changing nothing.
        /// </summary>
        public static bool WouldDress(Pawn pawn, Building_OutfitStand stand)
        {
            // BuildDress first and always: the callers read what it leaves in the lists.
            bool dresses = BuildDress(pawn, stand, ScratchWear, ScratchStore);
            return dresses || WeaponToIssue(pawn, stand) != null;
        }
    }
}
