using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// The stand's buttons as the tests read them: from the comps' gizmos, never from the
    /// screen (<see cref="ButtonsAndOrders"/>).
    /// </summary>
    internal static class HarnessGizmos
    {
        /// <summary>The stand's switch: the first <c>Command_Action</c> its comp yields.</summary>
        internal static Command_Action Switch(CompShiftStand comp)
        {
            return comp.CompGetGizmosExtra().OfType<Command_Action>().FirstOrDefault();
        }

        internal static Command_Action OwnerButton(Building_OutfitStand stand)
        {
            CompAssignableToPawn_ShiftStand comp = stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
            return comp?.CompGetGizmosExtra().OfType<Command_Action>().FirstOrDefault();
        }

        internal static string Shift(string what)
        {
            return "ShiftChange.RegimeShift".Translate(what).RawText;
        }
    }
}
