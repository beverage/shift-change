using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Clears session-scoped static state when the loaded game changes.
    ///
    /// Statics span the whole app session, so two registries outlive a save
    /// load if nothing resets them:
    ///
    ///  - <see cref="CompShiftStand"/>'s borrower registry: old-save Pawn keys
    ///    leak. Inert — object identity cannot collide across saves — but a
    ///    leak, and "inert" of that kind stops being true the day anything is
    ///    keyed by name instead.
    ///  - <see cref="Patch_JobInterception"/>'s retry cooldown: NOT inert. It
    ///    is keyed by thingIDNumber and stamped with TicksGame, and BOTH
    ///    restart per save — load an earlier or different save and a stale
    ///    entry can sit in the future, silently blocking a same-ID pawn from
    ///    swapping until the clock catches up to the old save's tick count.
    ///
    /// A GameComponent could do this instead, but a component writes its
    /// Class= name into every save, which costs the player a one-time load
    /// error after uninstalling the mod. Checking game identity at the entry
    /// points has zero save footprint, which keeps the clean-uninstall story
    /// intact. Route any future session-scoped static through here.
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
