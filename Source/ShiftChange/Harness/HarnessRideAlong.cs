// HARNESS only — see the configuration table in ShiftChange.csproj. The
// harness is dev tooling and does not ship: a Release build compiles this
// file out entirely, and devtools/run-harness.sh asks for it back with
// -p:Harness=true on top of Release codegen.
//
// The guard is whole-file, always. Never put an #if HARNESS inside a file
// that ships — a shipping build and a harness build must differ by the
// presence of these types and by nothing else, or a harness run stops saying
// anything about the assembly that goes out. check-invariants.py enforces it.
#if HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Harness cases for jobs the uniform RIDES ALONG on: errands that are
    /// part of work already under way, where changing out and back in again
    /// costs two wardrobe walks and buys nothing. So far, feeding a patient
    /// or a prisoner.
    ///
    /// <para>It reached this file because the return trip judged the errand
    /// by where its first target stands, which is the storeroom for a meal,
    /// and sent a colonist who was about to come straight back to the stand
    /// to change first.</para>
    /// </summary>
    internal static class HarnessRideAlong
    {
        /// <summary>
        /// The six vanilla feeding givers, and the job each one builds. Every
        /// one of them puts the FOOD in targetA and the patient or prisoner in
        /// targetB (<c>WorkGiver_FeedPatient.JobOnThing</c>,
        /// <c>WorkGiver_Warden_Feed</c>, <c>WorkGiver_Warden_DeliverFood</c>,
        /// <c>Workgiver_AdministerHemogen</c>,
        /// <c>WorkGiver_Warden_DeliverHemogen</c>), so the room the job reads
        /// as happening in is wherever the meal is stored. The last two rows
        /// are Biotech's and resolve only with it loaded.
        /// </summary>
        internal static readonly string[] FeedPatientGivers =
        {
            "DoctorFeedHumanlikes", "DoctorFeedAnimals", "FeedPrisoner", "FeedHemogen",
        };

        internal static readonly string[] DeliverFoodGivers =
        {
            "DeliverFoodToPrisoner", "DeliverHemogenToPrisoner",
        };

        /// <summary>A feeding job, built the way its giver builds one.</summary>
        internal static Job FeedingJob(JobDef def, Thing food, Pawn patient, WorkGiverDef giver)
        {
            Job job = JobMaker.MakeJob(def, food, patient);
            job.count = 1;
            job.workGiverDef = giver;
            return job;
        }

        /// <summary>
        /// FEEDING RIDES ALONG, in both directions: a meal run neither dresses
        /// anyone nor sends anyone back to change.
        ///
        /// <para>The doctor's half of it: in scrubs, handed a patient to feed,
        /// they changed out to fetch the meal from the freezer and changed
        /// back in for the next tend. The warden's half is the same with a
        /// Warden stand in a prison cell, and delivering food can never read as
        /// happening in the cell, because its giver refuses food already stored
        /// there.</para>
        ///
        /// <para><b>Why not read targetB instead.</b> That would put the job in
        /// the patient's room, which is right for where it ends and wrong for
        /// where it starts: a bare doctor would dress before a trip whose first
        /// leg goes to the freezer. Ignoring the giver is what the chemfuel row
        /// in the same table already does, for the same reason.</para>
        ///
        /// <para>Every refusal is paired with the SAME job shape under a giver
        /// that is not on the list, which still dresses and still changes them
        /// back. Without that pair, a feeding job failing its reservation dry run
        /// would satisfy every negative here.</para>
        /// </summary>
        internal static bool FeedingRidesAlong(Fixture fix)
        {
            WorkTypeDef doctor = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            WorkTypeDef warden = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Warden");
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
            if (doctor == null || warden == null || tend == null || mealDef == null)
            {
                return Expect(false, "the doctor, warden and meal defs resolve");
            }
            fix.Comp.ToggleWork(doctor);
            fix.Comp.ToggleWork(warden);
            MakeCalm(fix.Map);

            Map map = fix.Map;
            Room room = fix.Stand.GetRoom();
            Pawn patient = SpawnExtra(fix, Gender.Female, "Patient");
            Thing mealHere = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef),
                                            fix.Stand.Position + new IntVec3(3, 0, 3), map);
            IntVec3 outside = fix.Stand.Position + new IntVec3(8, 0, 0);
            if (!outside.InBounds(map) || outside.GetRoom(map) == room)
            {
                return Expect(false, "a cell outside the stand's room resolves for the test");
            }
            Thing mealAway = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), outside, map);

            List<KeyValuePair<WorkGiverDef, JobDef>> rows = new List<KeyValuePair<WorkGiverDef, JobDef>>();
            foreach (string name in FeedPatientGivers)
            {
                WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(name);
                if (giver != null)
                {
                    rows.Add(new KeyValuePair<WorkGiverDef, JobDef>(giver, JobDefOf.FeedPatient));
                }
            }
            foreach (string name in DeliverFoodGivers)
            {
                WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(name);
                if (giver != null)
                {
                    rows.Add(new KeyValuePair<WorkGiverDef, JobDef>(giver, JobDefOf.DeliverFood));
                }
            }

            bool ok = Expect(fix.Comp.HandlesWork(doctor) && fix.Comp.HandlesWork(warden),
                             "the stand serves doctoring and wardening")
                    & Expect(rows.Count >= 4, "the four Core feeding givers resolve ("
                             + rows.Count + " of six; Biotech supplies the hemogen pair)")
                    & Expect(Patch_JobInterception.TargetCell(
                                 FeedingJob(JobDefOf.FeedPatient, mealAway, patient, tend), map)
                             == mealAway.Position,
                             "a feeding job reads as happening where the FOOD is (the mechanism)");

            // Not yet dressed. The meal is stored IN the stand's room, so the
            // job reads as this room's doctoring or wardening.
            ok &= Expect(Probe(fix, FeedingJob(JobDefOf.FeedPatient, mealHere, patient, tend)),
                         "the same job under a giver NOT on the list dresses (control: the shape can dress)");
            foreach (KeyValuePair<WorkGiverDef, JobDef> row in rows)
            {
                ok &= Expect(!Probe(fix, FeedingJob(row.Value, mealHere, patient, row.Key)),
                             row.Key.defName + " dresses nobody, even with the meal in the stand's room");
            }

            ok &= Expect(RunSwap(fix), "dressed for the shift")
                & Expect(fix.Comp.OnShift, "and is on shift (control)");
            if (!fix.Comp.OnShift)
            {
                return false;
            }

            // Dressed, and the meal is in storage elsewhere: the trip that
            // used to change them out first.
            ok &= Expect(Probe(fix, FeedingJob(JobDefOf.FeedPatient, mealAway, patient, tend)),
                         "the same job under a giver NOT on the list still changes them back "
                         + "(control: the return trip is live)");
            foreach (KeyValuePair<WorkGiverDef, JobDef> row in rows)
            {
                ok &= Expect(!Probe(fix, FeedingJob(row.Value, mealAway, patient, row.Key)),
                             row.Key.defName + " keeps the uniform on while they fetch a meal from storage");
            }
            return ok & Expect(fix.Comp.OnShift, "and they are still on shift at the end");
        }
    }
}
#endif
