using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Where a job happens, as both directions read it: the cell its target
    /// names, and the cell with a room that stands in for it. The two differ
    /// only for a solid target, a rock face, a wall or a solid machine, whose
    /// own cells belong to no room (<see cref="Patch_JobInterception.RoomBearing"/>).
    /// A room stand reads the room cell; a radius or an area reads either
    /// (<see cref="CompShiftStand.Covers(JobPlace, Map)"/>), so a painted rock
    /// face is the quarry whether or not the floor in front of it is painted.
    /// docs/DESIGN.md, "Catchments".
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
