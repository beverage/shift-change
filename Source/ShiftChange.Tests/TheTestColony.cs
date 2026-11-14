using System.Collections;
using DevTools.Testing;
using RimWorld;
using Verse;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// What every test takes for granted about the colony it runs in: a healthy colonist, and open ground past the pad.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.TheTestColony)]
    internal class TheTestColony : StoryFixture
    {
        /// <summary>
        /// What every test takes for granted about the colonist it is handed and the ground past
        /// the pad. Forty colonists in a row from <see cref="DebugTools_Fixtures.AveragePawn"/>
        /// need no tending (<see cref="DebugTools_Fixtures.RemoveTendableConditions"/>). Untended
        /// asthma makes a colonist an urgent patient until the test colony removes it, which shows
        /// the removal is what keeps them out of bed. A cell of natural rock past the pad becomes
        /// open ground with a room under <see cref="HarnessFixtures.OpenGround"/>. The margin
        /// around the pad is in rooms that touch the map edge, which the tests that need a stand
        /// in the open rely on.
        /// </summary>
        [Test]
        [TestDescription("a test colonist arrives needing no doctor, and the ground around the pad is open "
                         + "to the map's edge")]
        internal IEnumerator HealthyColonistsAndOpenGround()
        {
            return Staged(Build,
                          "a colonist on shift at a stand: he wears what it held, and it keeps the clothes "
                          + "of his that made way",
                          Body);

            void Body(Fixture fix, Story story)
            {
                Map map = fix.Map;
                int enclosed = 0;
                foreach (IntVec3 margin in Pad.ExpandedBy(1).EdgeCells)
                {
                    Room room = margin.GetRoom(map);
                    if (room == null || !room.TouchesMapEdge)
                    {
                        enclosed++;
                    }
                }
                story.Check(enclosed == 0,
                            "the ring of cells around the pad reaches the map's edge, so a stand just outside "
                            + "the pad stands in the open (" + enclosed + " of those cells do not)");

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
                story.Check(tending == 0, "forty new test colonists in a row: none needs a doctor's tending");

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
                    story.Check(false, "asthma and the lung can be found");
                    return;
                }
                patient.health.AddHediff(HediffMaker.MakeHediff(asthma, patient, lung), lung);
                bool urgentWithIt = HealthAIUtility.ShouldSeekMedicalRestUrgent(patient);
                DebugTools_Fixtures.RemoveTendableConditions(patient);
                story.Note("another test colonist is given asthma in a lung, and then has it taken away "
                           + "as every test colonist does");
                story.Check(urgentWithIt, "untended asthma made her an urgent patient");
                story.Check(!HealthAIUtility.ShouldSeekMedicalRestUrgent(patient)
                            && patient.health.hediffSet.GetFirstHediffOfDef(asthma) == null,
                            "and with it taken away, she is not one");
                patient.Destroy();

                // Natural rock past the pad, as some maps have it.
                ThingDef granite = DefDatabase<ThingDef>.GetNamedSilentFail("Granite");
                IntVec3 past = fix.Stand.Position + new IntVec3(8, 0, 4);
                if (granite == null || !past.InBounds(map))
                {
                    story.Check(false, "granite can be found, and a cell past the pad is on the map");
                    return;
                }
                GenDebug.ClearArea(CellRect.SingleCell(past), map);
                GenSpawn.Spawn(ThingMaker.MakeThing(granite), past, map);
                bool rock = past.GetEdifice(map)?.def == granite && past.GetRoom(map) == null;
                OpenGround(fix, past);
                story.Note("a cell past the pad is made natural granite, then cleared for a test to use");
                story.Check(rock, "as natural rock, the cell was in no room");
                story.Check(past.GetEdifice(map) == null && past.Standable(map) && past.GetRoom(map) != null,
                            "cleared, it is open ground to stand on, with a room");
            }
        }
    }
}
