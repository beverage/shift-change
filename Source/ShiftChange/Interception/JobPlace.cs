using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Where a job happens, as both the dressing and the change-back directions
    /// read it: the cell its target names, and the cell with a room that stands
    /// in for it. The two differ only for a solid target, a rock face, a wall or
    /// a solid machine, whose own cells belong to no room
    /// (<see cref="Patch_JobInterception.RoomBearing"/>).
    /// <see cref="CompShiftStand.Covers(JobPlace, Map)"/> says which of the two
    /// each kind of catchment reads.
    /// </summary>
    internal readonly struct JobPlace
    {
        /// <summary>The cell the job's target names.</summary>
        internal readonly IntVec3 Cell;

        /// <summary>
        /// <see cref="Cell"/> when it has a room. For a solid target, a cell
        /// beside it that has one, or <see cref="Cell"/> again when none does.
        /// </summary>
        internal readonly IntVec3 RoomCell;

        internal JobPlace(IntVec3 cell, IntVec3 roomCell)
        {
            Cell = cell;
            RoomCell = roomCell;
        }

        /// <summary>A place at one ordinary cell, which is its own room cell.</summary>
        internal static JobPlace At(IntVec3 cell)
        {
            return new JobPlace(cell, cell);
        }

        /// <summary>The job names nowhere this mod can read.</summary>
        internal static JobPlace Invalid => At(IntVec3.Invalid);

        internal bool IsValid => Cell.IsValid;

        public override string ToString()
        {
            return Cell == RoomCell ? Cell.ToString() : Cell + " (room cell " + RoomCell + ")";
        }
    }
}
