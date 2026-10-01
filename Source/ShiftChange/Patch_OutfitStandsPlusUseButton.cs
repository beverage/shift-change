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
    /// Lets Outfit Stands Plus' per-colonist stand button find its stands
    /// without walking every building the player owns.
    ///
    /// <para>Their <c>OutfitStandsPlusUseCommandsComp</c> sits on every pawn,
    /// and its <c>CompGetGizmosExtra</c> runs each time a selected pawn's
    /// command bar is gathered, which vanilla does every frame. It gets the
    /// stands from <c>ListerBuildings.AllBuildingsColonistOfClass</c>, a walk
    /// over every player building with a type test on each, then keeps the
    /// ones whose owner list names the pawn. The cost follows how much has
    /// been built, not how many stands there are: 0.82 ms per selected pawn
    /// per frame on a colony of about nine thousand buildings and 48 stands
    /// (measured 2026-09-07).</para>
    ///
    /// <para>A transpiler changes that one call and nothing else: their method
    /// asks <see cref="Stands"/> instead, which answers from a list of the
    /// colony's stands. Their owner test, their command, and anything they add
    /// to the method later all still run.</para>
    ///
    /// <para><b>The list keeps the walk's order, because the order is
    /// behaviour.</b> A colonist who owns two stands gets two buttons with the
    /// same label and icon, vanilla merges them into one
    /// (<c>Command.GroupsWith</c>), and a click on it sends the colonist to
    /// the stand whose button came first. So the list is built once from the
    /// colony building list and then kept current from <c>ListerBuildings.Add</c>
    /// and <c>Remove</c>, the only two places the engine changes that list.
    /// <c>Add</c> appends, and <c>Building.SetFaction</c> goes through both,
    /// so a stand that changes hands moves to the end of both lists. Reads
    /// sweep out anything no longer spawned or no longer the player's, in case
    /// another mod changed one some other way.</para>
    ///
    /// <para>The lists live in a <c>ConditionalWeakTable</c> keyed by the map's
    /// <c>ListerBuildings</c>, so each goes with its map and a loaded game never
    /// sees an old one's. Nothing is keyed on ids or ticks, which is why this
    /// does not go through <see cref="SessionGuard"/>, and nothing is
    /// saved.</para>
    ///
    /// <para><b>Applying it.</b> The patch goes on only if the original IL of
    /// their method still makes the call. If they fix the lookup, the call is
    /// gone and nothing is patched; if they change the rest of the method, the
    /// change runs as written. One log line says which. With the mod absent
    /// this does nothing and logs nothing. Being a transpiler, it also sits
    /// beside other mods' patches on the method: one that skips their method
    /// skips this with it.</para>
    ///
    /// <para><see cref="Enabled"/> off, or an exception while reading the list,
    /// hands the call back to the walk. <see cref="HarmonyInit"/> applies all
    /// of it by hand rather than by attribute, because its target may not
    /// exist.</para>
    /// </summary>
    public static class Patch_OutfitStandsPlusUseButton
    {
        /// <summary>
        /// Off hands the call back to their walk. A diagnostic like the other
        /// shipped TweakValues, reset at the next launch.
        /// </summary>
        [TweakValue("ShiftChange")]
        public static bool Enabled = true;

        internal static bool applied;

        /// <summary>
        /// Why it stood aside, for the log. Null when it applied, and when the
        /// mod is not loaded at all.
        /// </summary>
        internal static string standingAside;

        internal static Type useCompType;

        /// <summary>Their <c>CompGetGizmosExtra</c>.</summary>
        internal static MethodInfo target;

        /// <summary>
        /// The method that makes the call: their iterator's <c>MoveNext</c>,
        /// since <see cref="target"/> is an iterator and only builds it.
        /// </summary>
        internal static MethodInfo walkSite;

        /// <summary>How many calls the transpiler replaced the last time it ran.</summary>
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
                    // The list's upkeep first. Alone it is inert: it only
                    // touches lists that Stands has built, and nothing calls
                    // Stands until the transpiler is in.
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
            // The count and the method go in the line so a player's log says what
            // was changed, not only that something was: their method makes the
            // walk once today, and any other number means it has changed.
            Log.Message(applied
                ? "[ShiftChange] Outfit Stands Plus: its colonist stand button now reads a list of the colony's "
                  + "stands instead of walking every building (replaced " + callsReplaced
                  + (callsReplaced == 1 ? " call" : " calls") + " in "
                  + walkSite.DeclaringType?.Name + "." + walkSite.Name + ")."
                : "[ShiftChange] Outfit Stands Plus: leaving its colonist stand button alone, because "
                  + standingAside + ".");
        }

        /// <summary>
        /// Finds the method making the call. Null means it may apply; anything
        /// else is the reason it will not, for the log.
        /// </summary>
        internal static string Resolve()
        {
            // DeclaredOnly: a lookup that walks up the hierarchy would find
            // ThingComp's own CompGetGizmosExtra if theirs were ever removed.
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
        /// Compared by name and type argument rather than by reference, so it
        /// does not depend on how the runtime caches a constructed generic
        /// method.
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
                    // The lister is already on the stack as the call's
                    // receiver, and becomes Stands' one argument. The
                    // instruction keeps its labels.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = stands;
                    replaced++;
                }
                yield return instruction;
            }
            callsReplaced = replaced;
        }

        /// <summary>
        /// Called by their method in place of the walk: the same stands, in the
        /// same order.
        /// </summary>
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
        /// The colony's stands on this lister's map, in the colony building
        /// list's order. Built from that list the first time it is asked for,
        /// kept current by the two postfixes afterwards.
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
        /// Walks the list by index, as the engine's own walk does, so a change
        /// to it partway through cannot throw.
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
            // As Add does: the colony list takes a building only if it is the
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
        /// The compiler-generated <c>MoveNext</c> holding an iterator method's
        /// body, or null if the method is not an iterator.
        /// </summary>
        internal static MethodInfo IteratorBody(MethodInfo method)
        {
            Type machine = method.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
            return machine?.GetMethod("MoveNext",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
    }
}
