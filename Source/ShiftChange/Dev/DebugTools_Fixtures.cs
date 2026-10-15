// Dev tooling, under either guard — see the configuration table in
// ShiftChange.csproj. Both callers compile out of a shipping build, so this
// does too.
#if SCENES || HARNESS
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Fixture primitives shared by the lifecycle harness and the SCENES-only
    /// stage builders: make a thing, make a pawn, make a garment, dress them.
    ///
    /// <para><b>Why the guard is <c>SCENES || HARNESS</c> and not either one
    /// alone.</b> Two dev-only callers gate independently — the stage builders
    /// on SCENES, the harness on HARNESS — and each builds its fixtures out of
    /// these members. Naming one guard would break the other's build. The
    /// OR is the whole reason this file is not simply part of one of them;
    /// everything that BUILDS A SCENE from these stays in
    /// <c>DebugTools_DemoStage</c> / <c>DebugTools_PreviewStage</c>.</para>
    ///
    /// <para><b>This reaches no player.</b> Nothing here carries a
    /// <c>[DebugAction]</c>, and a shipping build has neither constant, so the
    /// type is not in the assembly at all. <see cref="Spawn"/> and
    /// <see cref="AveragePawn"/> do make player-faction things and colonists —
    /// that is what a fixture is — so keep it that way: if a shipped code path
    /// ever wants one of these, that is a new resident, it needs its own
    /// go/no-go, and it does not live in this file.</para>
    /// </summary>
    internal static class DebugTools_Fixtures
    {
        /// <summary>Steel helmet grey, and the researcher's duster.</summary>
        internal static readonly Color DusterGreen = new Color(0.62f, 0.9f, 0.7f);

        internal static Thing Spawn(Map map, ThingDef def, ThingDef stuff, IntVec3 cell, Rot4 rot)
        {
            Thing thing = ThingMaker.MakeThing(def, stuff);
            thing.SetFactionDirect(Faction.OfPlayer);
            // A consistent set: no ideology styling, no random graphic
            // variants — every torch, chair and table is the default sprite.
            thing.SetStyleDef(null);
            thing.overrideGraphicIndex = 0;
            Thing spawned = GenSpawn.Spawn(thing, cell, map, rot);
            // …and clear AGAIN after spawning: vanilla's torch is a
            // Graphic_Single with exactly one graphic, so the mixed torches
            // in play could only come from a restyling mod on the modlist
            // applying at SpawnSetup — after the pre-spawn clear. Post-spawn
            // wins, and Notify_ColorChanged drops the cached graphic.
            spawned.SetStyleDef(null);
            spawned.overrideGraphicIndex = 0;
            spawned.Notify_ColorChanged();
            return spawned;
        }

        /// <summary>
        /// John and Jane Q. Pawn: a 30-year-old baseliner with a standard
        /// body, natural hair, no beard, no tattoos, NO TRAITS (a randomly
        /// rolled Nudist or Pyromaniac hijacks a take), and a role nickname
        /// so the on-screen label reads Doc / Lab / Chef / Patient.
        ///
        /// Traits are cleared, but BACKSTORIES are rolled and kept — and a
        /// backstory can disable a work type outright. A chef whose roll
        /// disabled Cooking gets priority 0 in everything (SpawnStaff zeroes
        /// all non-specialty work) and wanders the set for the whole take
        /// (found in play, 2026-08-08). So reroll until the specialty is
        /// enabled; relations are off so the discards leave nothing behind.
        ///
        /// <para>Two more rolls are taken out, for the lifecycle harness: a pawn
        /// whose needs would move while the harness pumps it
        /// (<see cref="NeedsTickWhilePumped"/>) is rerolled too, and whatever
        /// the pawn arrived needing tended is removed
        /// (<see cref="RemoveTendableConditions"/>).</para>
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
            // THE IDEO HALF OF THE SAME PROBLEM, and it cost three red gate
            // runs before anyone looked (2026-09-21). Traits are cleared one
            // line above because a rolled Nudist hijacks a take; the decency
            // cases then ask SwapPlan.PrefersNudity, whose SECOND arm is the
            // ideoligion. IdeoUtility.IdeoPrefersNudityForGender walks the
            // precepts for def.prefersNudity and matches when
            // genderPrefersNudity is None or this pawn's gender — and a
            // quicktest colony rolls its ideo at random. On the runs where
            // that roll carried a nudity precept, the decency fixtures were
            // staging a colonist the guard is supposed to exempt, so the
            // assertions failed on a premise nobody had arranged. The tell was
            // that the male and female halves of one pair disagreed, which
            // only a gender-aware precept can do once traits are gone.
            //
            // The precept is removed rather than the ideo replaced: the pawn
            // keeps a normal colony ideoligion, and only the axis these cases
            // are blind to is pinned. NudistIsExemptFromDecency still arranges
            // its own nudity, by trait, and is unaffected.
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

        /// <summary>
        /// The largest delta <c>Thing.DoTick</c> hands to <c>TickInterval</c>:
        /// <c>Thing.MaxTickIntervalRate</c> (<c>Thing.cs:606</c>, used at
        /// <c>:727-732</c>), which is protected.
        /// </summary>
        internal const int MaxTickIntervalDelta = 15;

        /// <summary>
        /// This pawn's needs, and its random mental-break check, would fire
        /// while the lifecycle harness pumps it. The harness runs every case
        /// inside one frame, so the game clock stands still while
        /// <c>RunSwap</c> and the other pumps call <c>Pawn.DoTick</c> by hand.
        /// Both are gated on <c>IsHashIntervalTick(150, delta)</c>
        /// (<c>Pawn_NeedsTracker.cs:112</c>, <c>MentalBreaker.cs:253</c>), which
        /// reads that clock and the pawn's id, with a delta of at most
        /// <see cref="MaxTickIntervalDelta"/>. With the clock stopped the
        /// answer never changes. Most pawns' needs never move, about one pawn
        /// in ten gets 150 ticks of need every 15 pumped ticks, and about one
        /// in 150 gets them on every pumped tick.
        ///
        /// <para>Measured on 1.7.4979, 2026-10-15, pumping each kind 6,000
        /// ticks: a colonist of the last kind ran out of food within 500
        /// ticks, was downed by malnutrition, and died at tick 1,114 in one run
        /// and 1,334 in another. One of the middle kind ran out of food by
        /// tick 5,000. A case that pumps such a colonist through a few swaps
        /// sees one cut short with nothing taking over, and then a dead pawn.
        /// With the clock running, as the stage builders have it, this costs a
        /// reroll now and then and nothing else.</para>
        /// </summary>
        internal static bool NeedsTickWhilePumped(Pawn pawn)
        {
            return pawn.IsHashIntervalTick(150, MaxTickIntervalDelta);
        }

        /// <summary>
        /// Removes every condition the pawn needs tended now. Vanilla tends a
        /// new pawn's conditions only when it is not joining the player
        /// (<c>PawnGenerator.cs:1297</c>), so a colonist who rolled asthma
        /// starts untended, and is an urgent patient
        /// (<c>HealthAIUtility.ShouldSeekMedicalRestUrgent</c>) from the start.
        /// Nobody dresses an urgent patient for bed, and nobody changes one
        /// back before it, so a case that sends its colonist to bed failed at
        /// random. Asthma comes to roughly one colonist in seventy by thirty.
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
        /// Cloth for anything stuffable, default sprite always — staged
        /// garments should read identically take after take. The explicit
        /// SetColor matters: CompColorable.Initialize rolls the def's
        /// colorGenerator (random clothing colours — the pink chef's
        /// uniform), so "default" has to be imposed.
        /// </summary>
        internal static Apparel MakeGarment(ThingDef def, Color? tint)
        {
            // Cloth for anything that takes it, but NOT unconditionally: the
            // simple helmet is Metallic-only, and handing ThingMaker a stuff
            // outside the def's own categories is not a thing the game has to
            // tolerate. Fabric-capable pieces keep Cloth so every garment
            // staged before this change still looks identical.
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
        /// The room's work gear from Vanilla Apparel Expanded when its defs
        /// are loaded; a colour-tinted vanilla outfit otherwise, so the swap
        /// still reads on camera without the apparel mod.
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
        /// Strips generated apparel and dresses the pawn in the starting kit
        /// the shift change is filmed AGAINST. PawnGenerator rolls random
        /// colonist clothing, which both muddies the take (a chef can spawn
        /// already wearing a toque in whatever fabric rolled) and pads the
        /// change-in with one removal delay per displaced garment.
        ///
        /// Every piece here is chosen for what it does ON CAMERA when the
        /// swap fires, and the choices are conflict rules, not costume:
        ///
        /// - **Tunic** — one OnSkin torso piece. Scrubs and chef's whites
        ///   displace exactly this, so the change-in stays short.
        /// - **Simple helmet** on all three (`Overhead`/`UpperHead`). The
        ///   doctor's surgical mask and the chef's toque occupy that same
        ///   slot, so both of them visibly LOSE the helmet at the stand —
        ///   a metallic head going bare is the clearest frame-to-frame
        ///   signal the mod produces. The researcher's stand holds no
        ///   headgear, so theirs stays on, which is the point: it shows the
        ///   swap moves what the stand names and nothing else.
        /// - **Duster, tinted green, researcher only** — the lab coat is
        ///   `Shell` and so is a duster, so the coat displaces it. Without
        ///   this the researcher's change was a tunic vanishing under a coat,
        ///   which read as nothing happening at all. Green because it is the
        ///   civvies that should sink into the green carpet; the stand's coat
        ///   is default white and steps forward out of it.
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

            // Last, so a conflicting roll can never displace it silently.
            ThingDef helmet = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_SimpleHelmet");
            if (helmet != null)
            {
                pawn.apparel.Wear(MakeGarment(helmet, null));
            }
        }
    }
}
#endif
