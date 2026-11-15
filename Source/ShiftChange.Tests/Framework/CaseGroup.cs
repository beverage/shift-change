

namespace ShiftChange.Tests
{
    /// <summary>
    /// The fixtures that play the game, in run order: DevTools runs a fixture with a higher
    /// <c>ExecutionPriority</c> first, and each runs at <see cref="RunOrder.First"/> less its
    /// place here. <see cref="SavingAndLoading"/> runs after all of them
    /// (<see cref="RunOrder.SaveLoadPriority"/>), so it has no place here.
    /// </summary>
    internal enum CaseGroup
    {
        /// <summary>
        /// What every test takes for granted about the colony it runs in, so first.
        /// </summary>
        TheTestColony,
        ChangingAtAStand,
        KeepingThemDecent,
        WhatSendsThemToChange,
        WhoUsesAStand,
        ServingARoomOrARing,
        ServingAnArea,
        KeepingTheirVacsuitOn,
        LosingAStandOrAColonist,
        ButtonsAndOrders,
        ReadingJobsAndRooms,
        FlyingTheGravship,
        HeadingIntoVacuum,
        MealsDangerAndDuties,
        AfterChangingBack,
        StayingInUniform,
        StoringWhatTheyMade,
        RidingAlong,
    }

    /// <summary>
    /// The priorities the fixtures that play the game run at (<see cref="CaseGroup"/>).
    /// </summary>
    internal static class RunOrder
    {
        internal const int First = 100;

        /// <summary>
        /// The save/load fixture's priority: DevTools' lowest, so it runs after every fixture of
        /// its test type at a higher one, the default included. A tie keeps registration order.
        /// </summary>
        internal const int SaveLoadPriority = (int)DevTools.Testing.Priority.Last;
    }
}
