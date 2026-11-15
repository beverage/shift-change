using DevTools.Testing;

namespace ShiftChange.Tests
{
    /// <summary>
    /// What every fixture in this assembly allows: lines other mods log by themselves on a full
    /// list, which land in whichever test is running. Each passes only with that mod's own code
    /// on its stack, so the same text from this mod still fails. How a story test runs:
    /// <see cref="StoryFixture"/>.
    /// </summary>
    [WarningsAllowedFrom(ShiftChangeFixture.MeleeAnimationFrame, ShiftChangeFixture.MeleeAnimationLine)]
    [WarningsAllowedFrom(ShiftChangeFixture.MapPreviewFrame, ShiftChangeFixture.MapPreviewRandomness)]
    [WarningsAllowedFrom(ShiftChangeFixture.ImmersiveFilterFrame, ShiftChangeFixture.EngineStub)]
    [ErrorsAllowedFrom(ShiftChangeFixture.EngineCallFrame, ShiftChangeFixture.ImmersiveFilterNoWorld)]
    [WarningsAllowedFrom(ShiftChangeFixture.MemeTraitFrame, ShiftChangeFixture.TraitAlreadyHeld)]
    internal abstract class ShiftChangeFixture
    {
        /// <summary>
        /// Melee Animation's late-load checks, which land in the first test: other mods' patches
        /// on the methods it patches, and weapons with no tweak data of its own.
        /// </summary>
        internal const string MeleeAnimationFrame =
            @"AM\.Core\.(LogPotentialConflicts|LoadAllTweakData)\(";

        /// <summary>Any warning with Melee Animation's tag, which it puts in colour.</summary>
        internal const string MeleeAnimationLine = @"^(<color=#\w+>)?\[MeleeAnim\]";

        /// <summary>Map Preview's check of each map component as a test's map is built.</summary>
        internal const string MapPreviewFrame =
            @"MapPreview\.Patches\.Patch_Verse_Map\.FillComponents_CheckRand";

        /// <summary>Its note that another mod's map component draws random numbers.</summary>
        internal const string MapPreviewRandomness =
            @"^\[Map Preview v[\d.]+\] Detected RNG usage in constructor of map component ";

        /// <summary>Immersive Filter's set-up of its screen filter on the first map.</summary>
        internal const string ImmersiveFilterFrame =
            @"Og_ImmersiveFilter\.OgImmersiveFilter\.(Start|EnsureFilterBuffer)\(";

        /// <summary>The engine's warning for a Unity call it leaves unimplemented.</summary>
        internal const string EngineStub =
            @"^\w+ \([^()]*\): Stub! You may not get expected results!";

        /// <summary>
        /// The engine running a component's frame method, which logs what that method throws under
        /// the component's class name, its own trace in the message.
        /// </summary>
        internal const string EngineCallFrame =
            @"Waylith\.UnityCompat\.UnityWorld\.InvokeMessage\(";

        /// <summary>Immersive Filter's update reading the world view between games.</summary>
        internal const string ImmersiveFilterNoWorld =
            @"^\[Og_ImmersiveFilter\.OgImmersiveFilter\] LateUpdate threw: "
            + @"System\.NullReferenceException[\s\S]*RimWorld\.Planet\.WorldRendererUtility\.";

        /// <summary>
        /// VE Framework giving a new colonist the traits its ideoligion's memes require, as a test
        /// generates one, whatever traits the colonist already rolled.
        /// </summary>
        internal const string MemeTraitFrame =
            @"VEF\.Memes\.VanillaExpandedFramework_Pawn_IdeoTracker_SetIdeo_Patch\."
            + @"ForceTraitAndAbilities\(";

        /// <summary>The game's warning when a pawn is given a trait it has.</summary>
        internal const string TraitAlreadyHeld = @"^[^\n]* already has trait [\w-]+$";
    }
}
