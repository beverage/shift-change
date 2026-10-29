// Dev tooling, compiled under either dev guard (configuration table: ShiftChange.csproj).
// A shipping build defines neither, so this compiles out along with both callers.
#if SCENES || HARNESS
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Fixture primitives shared by the lifecycle harness and the SCENES-only stage builders:
    /// make a thing, a pawn, a garment, and dress them. The guard names both constants because
    /// each caller builds its fixtures from these; code that builds a whole scene stays in the
    /// <c>DebugTools_*Stage</c> files. Spawn and AveragePawn make player-faction things and
    /// colonists, so shipped code must never call into this file; a shipped need for one of
    /// them is new code with its own go/no-go.
    /// </summary>
    internal static class DebugTools_Fixtures
    {
        /// <summary>The green tint of the researcher's duster.</summary>
        internal static readonly Color DusterGreen = new Color(0.62f, 0.9f, 0.7f);

        /// <summary>Spawns a player-faction thing with no style and graphic variant 0. A stand
        /// starts in service whatever the new-stand setting says, so a fixture or a stage does not
        /// depend on the profile it runs in.</summary>
        internal static Thing Spawn(Map map, ThingDef def, ThingDef stuff, IntVec3 cell, Rot4 rot)
        {
            Thing thing = ThingMaker.MakeThing(def, stuff);
            CompShiftStand stand = thing.TryGetComp<CompShiftStand>();
            if (stand != null)
            {
                stand.excluded = false;
            }
            thing.SetFactionDirect(Faction.OfPlayer);
            thing.SetStyleDef(null);
            thing.overrideGraphicIndex = 0;
            Thing spawned = GenSpawn.Spawn(thing, cell, map, rot);
            // Cleared again after spawning: a mod that restyles things can apply its style in
            // SpawnSetup, after the clear above, and the post-spawn clear wins.
            // Notify_ColorChanged drops the cached graphic.
            spawned.SetStyleDef(null);
            spawned.overrideGraphicIndex = 0;
            spawned.Notify_ColorChanged();
            return spawned;
        }

        /// <summary>
        /// A generated colonist with a plain, repeatable look: 30 years old, baseliner, standard
        /// body, natural hair, no beard, tattoos or traits (a rolled Nudist or Pyromaniac
        /// hijacks a take), and the nickname <paramref name="nick"/> so the on-screen label
        /// reads as the role. Backstories stay, and one can disable a work type outright; a
        /// staff pawn whose specialty is disabled gets priority 0 in everything
        /// (<c>SpawnStaff</c> zeroes all non-specialty work) and wanders. So the pawn is
        /// rerolled, at most 30 times and keeping the last roll, until
        /// <paramref name="mustBeCapableOf"/>, when given, is enabled. Relations are off so the
        /// discarded rolls leave nothing behind.
        ///
        /// The lifecycle harness also relies on two more rolls being removed: a pawn whose
        /// needs tick under a stopped clock (<see cref="NeedsTickWhilePumped"/>) is rerolled,
        /// and conditions it arrived needing tended are removed
        /// (<see cref="RemoveTendableConditions"/>).
        /// </summary>
        internal static Pawn AveragePawn(Gender gender, string nick, WorkTypeDef mustBeCapableOf = null)
        {
            PawnGenerationRequest request = new PawnGenerationRequest(
                PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                fixedBiologicalAge: 30f, fixedChronologicalAge: 30f,
                fixedGender: gender);
            XenotypeDef baseliner = DefDatabase<XenotypeDef>.GetNamedSilentFail("Baseliner");
            if (baseliner != null)
            {
                request.ForcedXenotype = baseliner;
            }
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            for (int tries = 0;
                 (NeedsTickWhilePumped(pawn)
                  || (mustBeCapableOf != null && pawn.WorkTypeIsDisabled(mustBeCapableOf)))
                 && tries < 30;
                 tries++)
            {
                pawn.Destroy();
                pawn = PawnGenerator.GeneratePawn(request);
            }
            RemoveTendableConditions(pawn);

            pawn.Name = new NameTriple(gender == Gender.Female ? "Jane" : "John", nick, "Doe");
            pawn.story.traits.allTraits.Clear();
            // Traits are cleared above, but the game also counts a pawn as preferring nudity
            // when a precept of its ideoligion sets prefersNudity for the pawn's gender or for
            // any gender (IdeoUtility.IdeoPrefersNudityForGender), and a quicktest colony rolls
            // its ideoligion at random. Left in, such a precept would sometimes make the decency
            // cases stage a colonist that SwapPlan.PrefersNudity exempts. Removing the precept
            // keeps the ideoligion; NudistIsExemptFromDecency arranges its own nudity by trait.
            if (ModsConfig.IdeologyActive && pawn.Ideo != null)
            {
                List<Precept> nudity = new List<Precept>();
                List<Precept> all = pawn.Ideo.PreceptsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].def.prefersNudity)
                    {
                        nudity.Add(all[i]);
                    }
                }
                for (int i = 0; i < nudity.Count; i++)
                {
                    pawn.Ideo.RemovePrecept(nudity[i]);
                }
            }
            pawn.story.HairColor = new Color(0.35f, 0.24f, 0.15f);
            pawn.story.bodyType = gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male;
            if (pawn.style != null)
            {
                pawn.style.beardDef = DefDatabase<BeardDef>.GetNamedSilentFail("NoBeard");
                pawn.style.FaceTattoo = DefDatabase<TattooDef>.GetNamedSilentFail("NoTattoo_Face");
                pawn.style.BodyTattoo = DefDatabase<TattooDef>.GetNamedSilentFail("NoTattoo_Body");
            }
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            return pawn;
        }

        /// <summary>Copy of the protected <c>Thing.MaxTickIntervalRate</c>.</summary>
        internal const int MaxTickIntervalDelta = 15;

        /// <summary>
        /// Whether this pawn's needs, or its random mental-break check, would fire while the
        /// lifecycle harness pumps it. The harness runs every case inside one frame, so the
        /// game clock stands still while <c>RunSwap</c> and the other pumps call
        /// <c>Pawn.DoTick</c> by hand. The needs tracker and the mental breaker run on
        /// <c>IsHashIntervalTick(150, delta)</c>, which reads that clock and the pawn's id with
        /// a delta of at most <see cref="MaxTickIntervalDelta"/>, so for a given pawn the
        /// answer never changes. Most pawns' needs never move, about one in ten gets 150 ticks
        /// of need every 15 pumped ticks, and about one in 150 gets them every pumped tick; a
        /// colonist of the last kind is out of food within 500 pumped ticks and dies, which
        /// cuts a case short. With the clock running, as in the stage builders, this costs a
        /// reroll now and then and nothing else.
        /// </summary>
        internal static bool NeedsTickWhilePumped(Pawn pawn)
        {
            return pawn.IsHashIntervalTick(150, MaxTickIntervalDelta);
        }

        /// <summary>
        /// Removes every condition the pawn needs tended now. The generator tends a new pawn's
        /// conditions only when the pawn is not joining the player, so a colonist who rolled one
        /// (asthma, roughly one in seventy by thirty) starts untended and is an urgent patient
        /// (<c>HealthAIUtility.ShouldSeekMedicalRestUrgent</c>). By default the mod neither
        /// dresses an urgent patient for bed nor changes one back before it, so a case that
        /// sends its colonist to bed would fail at random.
        /// </summary>
        internal static void RemoveTendableConditions(Pawn pawn)
        {
            List<Hediff> all = pawn.health.hediffSet.hediffs;
            List<Hediff> tendable = new List<Hediff>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].TendableNow())
                {
                    tendable.Add(all[i]);
                }
            }
            for (int i = 0; i < tendable.Count; i++)
            {
                pawn.health.RemoveHediff(tendable[i]);
            }
        }

        /// <summary>
        /// A garment in the default sprite and a fixed colour: <c>CompColorable</c> rolls the
        /// def's colorGenerator on initialisation, which would give random clothing colours.
        /// </summary>
        internal static Apparel MakeGarment(ThingDef def, Color? tint)
        {
            // Cloth where the def accepts fabric, otherwise its default stuff: the simple
            // helmet is metallic only, and ThingMaker is not required to tolerate a stuff
            // outside the def's own stuffCategories.
            ThingDef stuff = null;
            if (def.MadeFromStuff)
            {
                stuff = def.stuffCategories != null
                        && def.stuffCategories.Contains(StuffCategoryDefOf.Fabric)
                    ? ThingDefOf.Cloth
                    : GenStuff.DefaultStuffFor(def);
            }
            Apparel garment = (Apparel)ThingMaker.MakeThing(def, stuff);
            garment.SetStyleDef(null);
            garment.overrideGraphicIndex = 0;
            garment.TryGetComp<CompColorable>()?.SetColor(
                tint ?? (stuff != null ? stuff.stuffProps.color : Color.white));
            return garment;
        }

        /// <summary>
        /// Stocks the stand with the named defs that are loaded (the room's work gear from
        /// Vanilla Apparel Expanded). When none is, a vanilla shirt and trousers in
        /// <paramref name="fallbackTint"/>, so the swap still reads without the apparel mod.
        /// </summary>
        internal static void Stock(Building_OutfitStand stand, string[] preferredDefNames, Color fallbackTint)
        {
            bool stockedAny = false;
            foreach (string defName in preferredDefNames)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null)
                {
                    continue;
                }
                stand.AddApparel(MakeGarment(def, null));
                stockedAny = true;
            }
            if (stockedAny)
            {
                return;
            }
            foreach (string defName in new[] { "Apparel_BasicShirt", "Apparel_Pants" })
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null)
                {
                    continue;
                }
                stand.AddApparel(MakeGarment(def, fallbackTint));
            }
        }

        /// <summary>
        /// Strips the pawn's generated apparel and dresses it in the starting kit the shift
        /// change is filmed against: random generated clothing muddies a take (a chef can spawn
        /// already wearing a toque) and adds a removal delay per displaced garment. Each piece
        /// is chosen for what it conflicts with at the stand.
        /// - Tunic: one on-skin torso piece, which scrubs and chef's whites displace exactly,
        ///   so the change-in stays short.
        /// - Simple helmet, on everyone (Overhead layer, UpperHead group): the doctor's mask and
        ///   the chef's toque take its slot, so both visibly lose it at the stand. The
        ///   researcher's stand holds no headgear, so theirs stays on.
        /// - Duster, tinted green, researcher only: the lab coat and a duster are both Shell, so
        ///   the coat displaces it. A swap that displaces nothing stores nothing, and
        ///   <c>HarnessFixtures.Build</c> returns null for one.
        /// </summary>
        internal static void DressInStartingKit(Pawn pawn, bool researcher)
        {
            if (pawn.apparel == null)
            {
                return;
            }
            pawn.apparel.DestroyAll();

            ThingDef tunic = DefDatabase<ThingDef>.GetNamedSilentFail("VAE_Apparel_Tunic")
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_TribalA");
            if (tunic != null)
            {
                pawn.apparel.Wear(MakeGarment(tunic, null));
            }

            if (researcher)
            {
                ThingDef duster = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Duster");
                if (duster != null)
                {
                    pawn.apparel.Wear(MakeGarment(duster, DusterGreen));
                }
            }

            // Worn last, so the helmet displaces any earlier piece sharing its slot instead of
            // being displaced itself.
            ThingDef helmet = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_SimpleHelmet");
            if (helmet != null)
            {
                pawn.apparel.Wear(MakeGarment(helmet, null));
            }
        }
    }
}
#endif
