// HARNESS only: dev tooling, compiled out of a plain Release build (the
// configuration table is in ShiftChange.csproj). The guard is whole-file,
// always, and check-invariants.py enforces it; why: docs/DESIGN.md,
// "Development tooling".
#if HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Harness cases for code of ours that runs inside another mod's.
    /// Today that is <see cref="Patch_OutfitStandsPlusUseButton"/>, whose case
    /// needs Outfit Stands Plus loaded: on the default list it is a known gap,
    /// and <c>run-harness.sh --with=khamenman.outfitstandsplus</c> adds the mod
    /// to that list.
    /// </summary>
    internal static class HarnessInterop
    {
        /// <summary>
        /// Their stand button, reading our stand list, offers the same stands
        /// in the same order as it does walking every building.
        ///
        /// <para>First the patch itself: the walk is in their original IL, and
        /// what runs calls the list in its place.</para>
        ///
        /// <para>Then a world built to show either half going wrong. The
        /// fixture's stand belongs to a second colonist. The owner gets a
        /// mechanized stand of theirs and then a second vanilla stand, spawned
        /// in that order. A third stand names the owner but belongs to no
        /// faction, which both lookups must leave out. Owners go straight into
        /// their lists through the base class, so the case asserts the lookup
        /// and nothing about how our copy fills those lists. The mechanized
        /// stand then goes to no faction and back, which moves it to the end of
        /// the colony building list, and the list has to follow.</para>
        ///
        /// <para>Order is asserted because it is behaviour: the owner's two
        /// buttons merge into one, and a click goes to the first. The case
        /// checks the merge happens, so the reason is tested as well as the
        /// order.</para>
        ///
        /// <para>Their method runs with the switch off and on. That the off arm
        /// is their walk rests on two checks: their method calls the list (the
        /// IL above), and the list hands back the walk when switched off
        /// (asked directly, at the end).</para>
        /// </summary>
        internal static bool OutfitStandsPlusButtonMatchesTheirs(Fixture fix)
        {
            if (Patch_OutfitStandsPlusUseButton.useCompType == null)
            {
                return ExpectKnownGap(false, "their stand button gives what their own walk gives",
                    "Outfit Stands Plus is not on this mod list; --with=khamenman.outfitstandsplus loads it");
            }
            if (!Expect(Patch_OutfitStandsPlusUseButton.applied,
                        Patch_OutfitStandsPlusUseButton.applied
                            ? "the patch applied at startup"
                            : "the patch applied at startup; it stood aside because "
                              + Patch_OutfitStandsPlusUseButton.standingAside))
            {
                return false;
            }

            MethodInfo standsCall = AccessTools.Method(typeof(Patch_OutfitStandsPlusUseButton),
                nameof(Patch_OutfitStandsPlusUseButton.Stands));
            List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(
                Patch_OutfitStandsPlusUseButton.walkSite);
            List<CodeInstruction> running = PatchProcessor.GetCurrentInstructions(
                Patch_OutfitStandsPlusUseButton.walkSite);
            bool ok = Expect(original.Count(Patch_OutfitStandsPlusUseButton.IsTheWalk) == 1,
                             "their original walks the colony's buildings, once");
            ok &= Expect(!running.Any(Patch_OutfitStandsPlusUseButton.IsTheWalk)
                         && running.Count(i => i.Calls(standsCall)) == 1,
                         "what runs asks the stand list instead, once");
            ok &= Expect(Patch_OutfitStandsPlusUseButton.callsReplaced == 1,
                         "the startup line's count agrees: " + Patch_OutfitStandsPlusUseButton.callsReplaced
                         + " replaced");

            Type commandType = GenTypes.GetTypeInAnyAssembly("OutfitStandsPlus.Commands.OutfitStandsPlusUseCommand");
            Type ownerCompType = GenTypes.GetTypeInAnyAssembly(
                "OutfitStandsPlus.ThingComps.CompAssignableToPawn_OutfitStandsPlusBase");
            FieldInfo standField = commandType == null ? null : AccessTools.Field(commandType, "OutfitStand");
            ThingDef powered = DefDatabase<ThingDef>.GetNamedSilentFail("OutfitStandsPlus_MechanizedOutfitStand");
            if (!Expect(standField != null && ownerCompType != null && powered != null,
                        "their command, owner comp and mechanized stand resolve (control)"))
            {
                return false;
            }

            Map map = fix.Map;
            Pawn owner = fix.Pawn;
            Pawn other = SpawnExtra(fix, Gender.Female, "Other");
            IntVec3 origin = fix.Stand.Position;
            Building_OutfitStand poweredStand = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                map, powered, ThingDefOf.Steel, origin + new IntVec3(4, 0, 0), Rot4.North);
            Building_OutfitStand secondVanilla = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                map, fix.Stand.def, ThingDefOf.WoodLog, origin + new IntVec3(4, 0, 2), Rot4.North);
            Building_OutfitStand unowned = (Building_OutfitStand)DebugTools_Fixtures.Spawn(
                map, fix.Stand.def, ThingDefOf.WoodLog, origin + new IntVec3(4, 0, 4), Rot4.North);
            unowned.SetFaction(null);

            CompAssignableToPawn[] lists =
            {
                ForeignAssignableOn(fix.Stand), ForeignAssignableOn(poweredStand),
                ForeignAssignableOn(secondVanilla), ForeignAssignableOn(unowned),
            };
            if (!Expect(lists.All(c => c != null && ownerCompType.IsInstanceOfType(c)),
                        "every stand carries their owner comp (control)"))
            {
                return false;
            }
            lists[0].ForceAddPawn(other);
            lists[1].ForceAddPawn(owner);
            lists[2].ForceAddPawn(owner);
            lists[3].ForceAddPawn(owner);

            ThingComp ownerUse = UseCompOn(owner);
            ThingComp otherUse = UseCompOn(other);
            if (!Expect(ownerUse != null && otherUse != null, "both colonists carry their use comp (control)"))
            {
                return false;
            }

            ok &= BothAgree(ownerUse, otherUse, standField, new[] { poweredStand, secondVanilla }, fix.Stand,
                            "as spawned", out List<Gizmo> ownerButtons);
            ok &= Expect(ownerButtons.Count == 2 && ownerButtons[0].GroupsWith(ownerButtons[1]),
                         "the owner's two buttons merge into one, so their order decides where a click goes (control)");

            poweredStand.SetFaction(null);
            poweredStand.SetFaction(Faction.OfPlayer);
            lists[1].ForceAddPawn(owner);
            ok &= BothAgree(ownerUse, otherUse, standField, new[] { secondVanilla, poweredStand }, fix.Stand,
                            "after the mechanized stand changed hands and came back", out _);

            bool before = Patch_OutfitStandsPlusUseButton.Enabled;
            try
            {
                Patch_OutfitStandsPlusUseButton.Enabled = true;
                Type on = Patch_OutfitStandsPlusUseButton.Stands(map.listerBuildings).GetType();
                Patch_OutfitStandsPlusUseButton.Enabled = false;
                Type off = Patch_OutfitStandsPlusUseButton.Stands(map.listerBuildings).GetType();
                ok &= Expect(on.DeclaringType == typeof(Patch_OutfitStandsPlusUseButton)
                             && off.DeclaringType == typeof(ListerBuildings),
                             "the switch hands back the list when on and the walk when off");
            }
            finally
            {
                Patch_OutfitStandsPlusUseButton.Enabled = before;
            }
            return ok;
        }

        /// <summary>
        /// Gathers both colonists' buttons with the switch off, then on, and
        /// asserts the walk gives what the world says it should (the control)
        /// and the list gives exactly the same.
        /// </summary>
        internal static bool BothAgree(ThingComp ownerUse, ThingComp otherUse, FieldInfo standField,
                                       Building_OutfitStand[] ownerExpected, Building_OutfitStand otherExpected,
                                       string when, out List<Gizmo> ownerButtons)
        {
            List<Building_OutfitStand> walkOwner, walkOther, listOwner, listOther;
            bool before = Patch_OutfitStandsPlusUseButton.Enabled;
            try
            {
                Patch_OutfitStandsPlusUseButton.Enabled = false;
                walkOwner = Gather(ownerUse, standField, out _);
                walkOther = Gather(otherUse, standField, out _);
                Patch_OutfitStandsPlusUseButton.Enabled = true;
                listOwner = Gather(ownerUse, standField, out ownerButtons);
                listOther = Gather(otherUse, standField, out _);
            }
            finally
            {
                Patch_OutfitStandsPlusUseButton.Enabled = before;
            }

            bool ok = Expect(walkOwner.SequenceEqual(ownerExpected),
                             when + ": their walk gives the owner " + Labels(ownerExpected)
                             + ", and not the stand with no faction (control; got " + Labels(walkOwner) + ")");
            ok &= Expect(listOwner.SequenceEqual(walkOwner),
                         when + ": the list gives the owner the same stands in the same order (got "
                         + Labels(listOwner) + ")");
            ok &= Expect(walkOther.SequenceEqual(new[] { otherExpected }) && listOther.SequenceEqual(walkOther),
                         when + ": both give the other colonist their own stand and nothing else");
            return ok;
        }

        /// <summary>Their use comp on a pawn, found by type.</summary>
        internal static ThingComp UseCompOn(Pawn pawn)
        {
            return pawn.AllComps.FirstOrDefault(c => Patch_OutfitStandsPlusUseButton.useCompType.IsInstanceOfType(c));
        }

        /// <summary>One gather, as the command bar does it: the buttons, and the stand behind each.</summary>
        internal static List<Building_OutfitStand> Gather(ThingComp use, FieldInfo standField, out List<Gizmo> buttons)
        {
            buttons = use.CompGetGizmosExtra()?.ToList() ?? new List<Gizmo>();
            return buttons.Select(g => standField.GetValue(g) as Building_OutfitStand).ToList();
        }

        internal static string Labels(IList<Building_OutfitStand> stands)
        {
            return stands.Count == 0
                ? "nothing"
                : string.Join(", ", stands.Select(s => s == null ? "null" : s.def.defName + "#" + s.thingIDNumber));
        }
    }
}
#endif
