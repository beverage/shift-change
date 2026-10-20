using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Resets the mod's session-scoped statics when the loaded game changes. Route any new
    /// one through <see cref="Ensure"/>.
    ///
    /// Statics outlive a save load, and state keyed by thingIDNumber or stamped with TicksGame
    /// is wrong after one, not merely leaked: both restart per save, so a stale entry can sit
    /// in the future and block a pawn.
    ///
    /// Not a GameComponent: a component writes its class name into every save, which costs the
    /// player a load error after uninstalling the mod. Comparing game identity at the entry
    /// points leaves nothing in the save.
    /// </summary>
    internal static class SessionGuard
    {
        internal static Game current;

        public static void Ensure()
        {
            if (!ReferenceEquals(current, Current.Game))
            {
                current = Current.Game;
                CompShiftStand.ResetSessionState();
                Patch_JobInterception.ResetSessionState();
            }
        }
    }
}
