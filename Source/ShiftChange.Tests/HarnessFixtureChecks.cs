using RimWorld;
using Verse;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Checks on the fixtures themselves: what a case can take for granted about the colonist
    /// it is handed and the ground past the pad.
    /// </summary>
    internal static class HarnessFixtureChecks
    {
        /// <summary>
        /// What every case takes for granted about its fixture. Forty colonists in a row from
        /// <see cref="DebugTools_Fixtures.AveragePawn"/> need no tending
        /// (<see cref="DebugTools_Fixtures.RemoveTendableConditions"/>). Untended asthma makes a
        /// colonist an urgent patient until the fixture removes it (control). A cell of natural
        /// rock past the pad becomes open ground with a room under
        /// <see cref="HarnessFixtures.OpenGround"/>. The margin around the pad is in rooms that
        /// touch the map edge, which the cases that need a stand in the open rely on.
        /// </summary>
        internal static bool FixtureColonistIsHealthy(Fixture fix, CellRect pad)
        {
            Map map = fix.Map;
            int enclosed = 0;
            foreach (IntVec3 margin in pad.ExpandedBy(1).EdgeCells)
            {
                Room room = margin.GetRoom(map);
                if (room == null || !room.TouchesMapEdge)
                {
                    enclosed++;
                }
            }
            bool open = Expect(enclosed == 0,
                               "the margin around the pad reaches the map edge, so a stand outside the pad "
                               + "is in the open (" + enclosed + " margin cells do not)");

            int tending = 0;
            for (int i = 0; i < 40; i++)
            {
                Pawn p = DebugTools_Fixtures.AveragePawn(Gender.Male, "Check");
                if (p.health.HasHediffsNeedingTend())
                {
                    tending++;
                }
                p.Destroy();
            }
            bool ok = open
                    & Expect(tending == 0, "forty fixture colonists in a row: none needs tending");

            // Asthma stands for any condition a generated colonist arrives needing tended.
            HediffDef asthma = DefDatabase<HediffDef>.GetNamedSilentFail("Asthma");
            BodyPartDef lungDef = DefDatabase<BodyPartDef>.GetNamedSilentFail("Lung");
            Pawn patient = DebugTools_Fixtures.AveragePawn(Gender.Female, "Asthma");
            BodyPartRecord lung = null;
            if (lungDef != null)
            {
                foreach (BodyPartRecord part in patient.RaceProps.body.GetPartsWithDef(lungDef))
                {
                    lung = part;
                    break;
                }
            }
            if (asthma == null || lung == null)
            {
                patient.Destroy();
                return ok & Expect(false, "the asthma and lung defs resolve");
            }
            patient.health.AddHediff(HediffMaker.MakeHediff(asthma, patient, lung), lung);
            bool urgentWithIt = HealthAIUtility.ShouldSeekMedicalRestUrgent(patient);
            DebugTools_Fixtures.RemoveTendableConditions(patient);
            ok &= Expect(urgentWithIt, "untended asthma makes a colonist an urgent patient (control)")
                & Expect(!HealthAIUtility.ShouldSeekMedicalRestUrgent(patient)
                         && patient.health.hediffSet.GetFirstHediffOfDef(asthma) == null,
                         "and the fixture removes it, so the colonist is not");
            patient.Destroy();

            // Natural rock past the pad, as some maps have it.
            ThingDef granite = DefDatabase<ThingDef>.GetNamedSilentFail("Granite");
            IntVec3 past = fix.Stand.Position + new IntVec3(8, 0, 4);
            if (granite == null || !past.InBounds(map))
            {
                return ok & Expect(false, "granite resolves and a cell past the pad is on the map");
            }
            GenDebug.ClearArea(CellRect.SingleCell(past), map);
            GenSpawn.Spawn(ThingMaker.MakeThing(granite), past, map);
            bool rock = past.GetEdifice(map)?.def == granite && past.GetRoom(map) == null;
            OpenGround(fix, past);
            return ok
                & Expect(rock, "a cell of natural rock has no room (control)")
                & Expect(past.GetEdifice(map) == null && past.Standable(map) && past.GetRoom(map) != null,
                         "and OpenGround makes it standable ground with a room");
        }
    }
}
