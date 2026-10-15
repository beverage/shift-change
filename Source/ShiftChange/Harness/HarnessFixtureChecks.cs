// HARNESS only: dev tooling, compiled out of a plain Release build (the
// configuration table is in ShiftChange.csproj). The guard is whole-file,
// always, and check-invariants.py enforces it; why: docs/DESIGN.md,
// "Development tooling".
#if HARNESS
using RimWorld;
using Verse;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Checks on the fixtures themselves: what a case can take for granted
    /// about the colonist it is handed and the ground past the pad. Each was a
    /// failure that moved from case to case at random before it was arranged.
    /// </summary>
    internal static class HarnessFixtureChecks
    {
        /// <summary>
        /// Calls <c>Pawn.DoTick</c> the way <see cref="RunSwap"/> does, with
        /// the game clock standing still, until the pawn dies or
        /// <paramref name="ticks"/> run out.
        /// </summary>
        internal static void Pump(Pawn pawn, int ticks)
        {
            for (int i = 0; i < ticks && !pawn.Dead && !pawn.Destroyed; i++)
            {
                pawn.DoTick();
            }
        }

        /// <summary>
        /// THE FIXTURE COLONIST HOLDS STILL, and the ground past the pad is
        /// ground. Forty colonists in a row from
        /// <see cref="DebugTools_Fixtures.AveragePawn"/>: none has needs that
        /// tick while the harness pumps it, and none needs tending. One of
        /// them, pumped 1,500 ticks, keeps its food, while a colonist the check
        /// rejects loses its food under the same pump (control). Asthma makes a
        /// colonist an urgent patient until the fixture removes it (control).
        /// Natural rock past the pad becomes open ground with a room under
        /// <see cref="HarnessFixtures.OpenGround"/>.
        /// </summary>
        internal static bool FixtureColonistHoldsStill(Fixture fix)
        {
            Map map = fix.Map;
            IntVec3 cell = fix.Stand.Position + new IntVec3(2, 0, 2);

            int moving = 0;
            int tending = 0;
            for (int i = 0; i < 40; i++)
            {
                Pawn p = DebugTools_Fixtures.AveragePawn(Gender.Male, "Check");
                if (DebugTools_Fixtures.NeedsTickWhilePumped(p))
                {
                    moving++;
                }
                if (p.health.HasHediffsNeedingTend())
                {
                    tending++;
                }
                p.Destroy();
            }
            bool ok = Expect(moving == 0,
                             "forty fixture colonists in a row: none has needs that tick under the stopped clock")
                    & Expect(tending == 0, "and none needs tending");

            Pawn held = SpawnExtra(fix, Gender.Male, "Held", cell);
            if (held?.needs?.food == null)
            {
                return ok & Expect(false, "a fixture colonist spawns with a food need");
            }
            float heldBefore = held.needs.food.CurLevel;
            Pump(held, 1500);
            ok &= Expect(!held.Dead && held.needs.food.CurLevel == heldBefore,
                         "pumped 1,500 ticks under the stopped clock, a fixture colonist keeps its food");

            // The control: the same pump on a colonist the check rejects.
            PawnGenerationRequest request = new PawnGenerationRequest(
                PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                fixedBiologicalAge: 30f, fixedChronologicalAge: 30f,
                fixedGender: Gender.Male);
            Pawn rejected = null;
            for (int i = 0; i < 300 && rejected == null; i++)
            {
                Pawn p = PawnGenerator.GeneratePawn(request);
                if (DebugTools_Fixtures.NeedsTickWhilePumped(p))
                {
                    rejected = p;
                }
                else
                {
                    p.Destroy();
                }
            }
            if (rejected == null)
            {
                return ok & Expect(false, "a colonist the check rejects turns up in 300 rolls (precondition)");
            }
            GenSpawn.Spawn(rejected, cell, map, Rot4.North);
            fix.Extras.Add(rejected);
            float rejectedBefore = rejected.needs.food.CurLevel;
            Pump(rejected, 1500);
            ok &= Expect(rejected.Dead || rejected.needs.food.CurLevel < rejectedBefore,
                         "while a colonist the check rejects loses food under the same pump (control)");

            // Asthma, which is what the bed cases met at random.
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
#endif
