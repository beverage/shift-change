using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using static ShiftChange.Tests.SceneKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// What the tests of the engine lifecycle events a ledger has to survive mid-shift use
    /// (<see cref="FlyingTheGravship"/>, <see cref="LosingAStandOrAColonist"/>,
    /// <see cref="HeadingIntoVacuum"/>): a gravship flight for one thing, and whether a meal break
    /// would send the borrower back to the stand.
    /// </summary>
    internal static class LifecycleKit
    {
        /// <summary>
        /// One thing's gravship flight, in the engine's order: <c>PreSwapMap</c> and a
        /// <c>WillReplace</c> despawn at takeoff (<c>GravshipUtility.GenerateGravship</c>), a
        /// spawn on the landing map, then <c>PostSwapMap</c>
        /// (<c>GravshipPlacementUtility.PostSwapMap</c>). For a flight carrying one of the pair;
        /// <see cref="FlyingTheGravship.StandAndColonistFlyTogether"/> interleaves the two as the
        /// engine does with both aboard, without the <c>PreSwapMap</c> step, which nothing of ours
        /// overrides.
        /// </summary>
        internal static void Fly(Thing thing, Map to, IntVec3 cell)
        {
            Rot4 rot = thing.Rotation;
            thing.PreSwapMap();
            thing.DeSpawn(DestroyMode.WillReplace);
            GenSpawn.Spawn(thing, cell, to, rot);
            thing.PostSwapMap();
        }

        /// <summary>
        /// Whether a meal break would send the borrower to the stand right now: the automatic
        /// return trip, asked of the real interception and left with no trace
        /// (<see cref="SendsToChange"/>). A meal on the ground is a sit-down break, which changes
        /// the pawn out first wherever they eat (<c>MealBreakChangesOut</c>), so this asks of a
        /// flight only whether the stand is within reach. Asking clears the retry cooldown a
        /// refusal stamps, which in play can delay the first change back after a reunion by up to
        /// <see cref="Patch_JobInterception.RetryCooldownTicks"/>.
        /// </summary>
        internal static bool MealBreakSendsThemBack(Scene scene)
        {
            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
            if (mealDef == null || !scene.Colonist.Spawned)
            {
                return false;
            }
            Thing meal = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), scene.Colonist.Position, scene.Colonist.Map);
            bool sent = SendsToChange(scene, IngestJob(meal));
            if (!meal.Destroyed)
            {
                meal.Destroy();
            }
            return sent;
        }

        /// <summary>Whether the borrower's uniform still carries the forced flag.</summary>
        internal static bool StillForced(Scene scene, Apparel uniform)
        {
            return scene.Colonist.outfits != null && scene.Colonist.outfits.forcedHandler.IsForced(uniform);
        }

        /// <summary>The first garment the scene's stand issued, or null.</summary>
        internal static Apparel FirstIssued(Scene scene)
        {
            return scene.Comp.IssuedUniformForReading.Count > 0
                ? scene.Comp.IssuedUniformForReading[0]
                : null;
        }

    }
}
