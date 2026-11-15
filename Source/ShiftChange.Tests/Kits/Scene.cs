using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ShiftChange.Tests
{
    /// <summary>
    /// One test's throwaway world: a stand, the colonist it is staged for, and what a test adds
    /// beyond them. <see cref="SceneKit.Teardown"/> sweeps all of it.
    /// </summary>
    internal class Scene
    {
        internal Map Map;
        internal Building_OutfitStand OutfitStand;
        internal Pawn Colonist;
        internal CompShiftStand Comp;
        internal int StoredCount;

        internal List<Pawn> Extras = new List<Pawn>();

        /// <summary>Allowed areas a test made; a map holds ten at most.</summary>
        internal List<Area> Areas = new List<Area>();

        /// <summary>
        /// The strip outside the west door that
        /// <see cref="SceneKit.StageWithDoorway(Map, CellRect)"/> cuts, past the pad;
        /// empty for every other scene.
        /// </summary>
        internal CellRect Outside = CellRect.Empty;

        /// <summary>Cells past the pad that tests put things on (<c>OpenGround</c>).</summary>
        internal List<IntVec3> Claimed = new List<IntVec3>();

        /// <summary>
        /// A second map (<see cref="SceneKit.SecondMap"/>), so a flight can leave the
        /// borrower and their stand apart.
        /// </summary>
        internal Map SecondMap;

        /// <summary>
        /// What the test holds against the game's own simulation while the clock steps, such
        /// as the air a room was set to: run before every tick and after the last
        /// (<see cref="GameClock.Step"/>). Null when the test holds nothing.
        /// </summary>
        internal Action Hold;
    }

    /// <summary>
    /// What a staged colonist wears and what their stand holds. Core defs only, never the demo
    /// stage's: the default list is four mods and <c>--full</c> copies the runner's own, and a
    /// scene built from Vanilla Apparel Expanded defs would be a different test on each.
    /// </summary>
    internal enum StageKit
    {
        /// <summary>
        /// Stand: a duster (Shell). Pawn: shirt and trousers (OnSkin) plus a parka (Shell), so
        /// the duster displaces the parka and the ledger has something in it.
        /// </summary>
        Displacing,

        /// <summary>
        /// The same without the parka, so the duster (Shell) shares no layer with shirt and
        /// trousers (OnSkin) and displaces nothing. <see cref="SceneKit.Build"/> cannot produce
        /// this shape: it returns null when nothing is displaced.
        /// </summary>
        NonDisplacing,
    }
}
