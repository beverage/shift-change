using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Fixture primitives shared by the lifecycle harness and the SCENES-only
    /// stage builders: make a thing, make a pawn, make a garment, dress them.
    ///
    /// <para><b>Why this file is not <c>#if SCENES</c>.</b> The harness BODY
    /// ships in every configuration on purpose — <c>-shiftchange-harness</c>
    /// (<see cref="Patch_HarnessAutoRun"/>) is the release gate, and a gate
    /// that ran against a build nobody installs would assert nothing. The
    /// harness builds its fixtures out of these six members, so gating them
    /// out with the stage scripts would simply break the Release build. They
    /// live here, always compiled; everything that BUILDS A SCENE from them
    /// stays in <c>DebugTools_DemoStage</c> / <c>DebugTools_PreviewStage</c>,
    /// which do compile out.</para>
    ///
    /// <para><b>This adds no player-facing surface.</b> Nothing here carries a
    /// <c>[DebugAction]</c>, and in Release the only caller is the harness,
    /// which is unreachable without the launch flag. <see cref="Spawn"/> and
    /// <see cref="AveragePawn"/> do make player-faction things and colonists —
    /// that is what a fixture is — so keep it that way: if a shipped code path
    /// ever wants one of these, that is a new resident and needs its own
    /// go/no-go.</para>
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
                 mustBeCapableOf != null && pawn.WorkTypeIsDisabled(mustBeCapableOf) && tries < 30;
                 tries++)
            {
                pawn.Destroy();
                pawn = PawnGenerator.GeneratePawn(request);
            }

            pawn.Name = new NameTriple(gender == Gender.Female ? "Jane" : "John", nick, "Doe");
            pawn.story.traits.allTraits.Clear();
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
