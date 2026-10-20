using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Lets Outfit Stands Plus' per-colonist stand button find its stands without walking every
    /// building the player owns on every frame a colonist is selected. A transpiler swaps that
    /// one call for <see cref="Stands"/>, a per-map list kept in the walk's order. The order is
    /// behaviour: a colonist who owns two stands gets two buttons that merge into one, and a
    /// click sends them to the stand whose button came first. Why, how the list stays current
    /// and when the patch applies: docs/DESIGN.md, "Outfit Stands Plus' stand button".
    ///
    /// <para><see cref="HarmonyInit"/> applies the patch by hand rather than by attribute,
    /// because its target may not exist.</para>
    /// </summary>
    public static class Patch_OutfitStandsPlusUseButton
    {
        /// <summary>Off hands the call back to their walk; reset at the next launch.</summary>
        [TweakValue("ShiftChange")]
        public static bool Enabled = true;

        internal static bool applied;

        /// <summary>
        /// Why the patch stood aside, for the log; null when it applied or the mod is absent.
        /// </summary>
        internal static string standingAside;

        internal static Type useCompType;

        /// <summary>Their use comp's <c>CompGetGizmosExtra</c>.</summary>
        internal static MethodInfo target;

        /// <summary>
        /// The method holding the call: the compiler-generated <c>MoveNext</c> of their iterator,
        /// since <see cref="target"/> only builds the iterator.
        /// </summary>
        internal static MethodInfo walkSite;

        internal static int callsReplaced;

        internal static readonly ConditionalWeakTable<ListerBuildings, List<Building_OutfitStand>> standsByLister =
            new ConditionalWeakTable<ListerBuildings, List<Building_OutfitStand>>();

        internal static void TryApply(Harmony harmony)
        {
            try
            {
                useCompType = GenTypes.GetTypeInAnyAssembly(
                    "OutfitStandsPlus.ThingComps.OutfitStandsPlusUseCommandsComp");
                if (useCompType == null)
                {
                    return;
                }
                standingAside = Resolve();
                if (standingAside == null)
                {
                    // Upkeep first: the postfixes only touch lists that Stands has built, and
                    // nothing calls Stands until the transpiler is in.
                    harmony.Patch(AccessTools.Method(typeof(ListerBuildings), nameof(ListerBuildings.Add)),
                        postfix: new HarmonyMethod(typeof(Patch_OutfitStandsPlusUseButton), nameof(ListerAddPostfix)));
                    harmony.Patch(AccessTools.Method(typeof(ListerBuildings), nameof(ListerBuildings.Remove)),
                        postfix: new HarmonyMethod(typeof(Patch_OutfitStandsPlusUseButton), nameof(ListerRemovePostfix)));
                    harmony.Patch(walkSite,
                        transpiler: new HarmonyMethod(typeof(Patch_OutfitStandsPlusUseButton), nameof(Transpiler)));
                    if (callsReplaced > 0)
                    {
                        applied = true;
                    }
                    else
                    {
                        standingAside = "another mod's patch had already replaced the walk";
                    }
                }
            }
            catch (Exception e)
            {
                standingAside = "patching it failed (" + e.GetType().Name + ": " + e.Message + ")";
            }
            // The line names the method and the count so a log shows what changed. Their method
            // makes the call once, so any other count means it has changed.
            Log.Message(applied
                ? "[ShiftChange] Outfit Stands Plus: its colonist stand button now reads a list of the colony's "
                  + "stands instead of walking every building (replaced " + callsReplaced
                  + (callsReplaced == 1 ? " call" : " calls") + " in "
                  + walkSite.DeclaringType?.Name + "." + walkSite.Name + ")."
                : "[ShiftChange] Outfit Stands Plus: leaving its colonist stand button alone, because "
                  + standingAside + ".");
        }

        /// <summary>
        /// Finds the method making the call. Null means the patch may apply; anything else is
        /// the reason it will not, for the log.
        /// </summary>
        internal static string Resolve()
        {
            // DeclaredOnly: a lookup that walks up the hierarchy would find ThingComp's own
            // CompGetGizmosExtra if their comp ever stopped declaring it.
            target = useCompType.GetMethod("CompGetGizmosExtra",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null);
            if (target == null)
            {
                return "its use comp no longer declares CompGetGizmosExtra";
            }
            MethodInfo body = IteratorBody(target);
            MethodInfo[] candidates = body == null ? new[] { target } : new[] { body, target };
            foreach (MethodInfo candidate in candidates)
            {
                if (PatchProcessor.GetOriginalInstructions(candidate).Any(IsTheWalk))
                {
                    walkSite = candidate;
                    return null;
                }
            }
            return "its stand button no longer walks the colony's buildings";
        }

        /// <summary>
        /// The call this replaces:
        /// <c>ListerBuildings.AllBuildingsColonistOfClass&lt;Building_OutfitStand&gt;()</c>.
        /// Compared by name and type argument rather than by reference, so it does not depend
        /// on how the runtime caches a constructed generic method.
        /// </summary>
        internal static bool IsTheWalk(CodeInstruction instruction)
        {
            return (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                   && instruction.operand is MethodInfo method
                   && method.DeclaringType == typeof(ListerBuildings)
                   && method.Name == nameof(ListerBuildings.AllBuildingsColonistOfClass)
                   && method.IsGenericMethod
                   && method.GetGenericArguments()[0] == typeof(Building_OutfitStand);
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo stands = AccessTools.Method(typeof(Patch_OutfitStandsPlusUseButton), nameof(Stands));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (IsTheWalk(instruction))
                {
                    // The lister is already on the stack as the call's receiver and becomes
                    // Stands' one argument. Editing in place keeps the instruction's labels.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = stands;
                    replaced++;
                }
                yield return instruction;
            }
            callsReplaced = replaced;
        }

        /// <summary>Called in place of their walk: the same stands, in the same order.</summary>
        public static IEnumerable<Building_OutfitStand> Stands(ListerBuildings lister)
        {
            if (Enabled)
            {
                try
                {
                    return ByIndex(StandsOf(lister));
                }
                catch (Exception e)
                {
                    Enabled = false;
                    Log.Warning("[ShiftChange] Outfit Stands Plus: the stand list failed, so its stand button "
                                + "walks every building again until the game restarts. " + e);
                }
            }
            return lister.AllBuildingsColonistOfClass<Building_OutfitStand>();
        }

        /// <summary>
        /// The colony's stands on this lister's map, in the colony building list's order. Built
        /// from that list on first use, kept current by the two postfixes, swept on each read.
        /// </summary>
        internal static List<Building_OutfitStand> StandsOf(ListerBuildings lister)
        {
            if (!standsByLister.TryGetValue(lister, out List<Building_OutfitStand> stands))
            {
                stands = new List<Building_OutfitStand>();
                List<Building> all = lister.allBuildingsColonist;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] is Building_OutfitStand stand)
                    {
                        stands.Add(stand);
                    }
                }
                standsByLister.Add(lister, stands);
                return stands;
            }
            for (int i = stands.Count - 1; i >= 0; i--)
            {
                Building_OutfitStand stand = stands[i];
                if (stand == null || !stand.Spawned || stand.Faction != Faction.OfPlayer)
                {
                    stands.RemoveAt(i);
                }
            }
            return stands;
        }

        /// <summary>
        /// Walks by index, as the engine's own walk does, so a mid-walk change cannot throw.
        /// </summary>
        internal static IEnumerable<Building_OutfitStand> ByIndex(List<Building_OutfitStand> stands)
        {
            for (int i = 0; i < stands.Count; i++)
            {
                yield return stands[i];
            }
        }

        // ReSharper disable once InconsistentNaming — Harmony injection.
        public static void ListerAddPostfix(ListerBuildings __instance, Building b)
        {
            // Mirrors ListerBuildings.Add: the colony list takes a building only if it is the
            // player's, and appends it.
            if (b is Building_OutfitStand stand && b.Faction == Faction.OfPlayer
                && standsByLister.TryGetValue(__instance, out List<Building_OutfitStand> stands)
                && !stands.Contains(stand))
            {
                stands.Add(stand);
            }
        }

        // ReSharper disable once InconsistentNaming — Harmony injection.
        public static void ListerRemovePostfix(ListerBuildings __instance, Building b)
        {
            if (b is Building_OutfitStand stand
                && standsByLister.TryGetValue(__instance, out List<Building_OutfitStand> stands))
            {
                stands.Remove(stand);
            }
        }

        /// <summary>
        /// The compiler-generated <c>MoveNext</c> of an iterator method, or null if it is not one.
        /// </summary>
        internal static MethodInfo IteratorBody(MethodInfo method)
        {
            Type machine = method.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
            return machine?.GetMethod("MoveNext",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
    }
}
