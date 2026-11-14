using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DevTools.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using static ShiftChange.Tests.DebugTools_LifecycleHarness;
using static ShiftChange.Tests.HarnessFixtures;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Work, errands, orders and emergencies that take a colonist in uniform through vacuum, and
    /// when that sends them to change back first.
    ///
    /// <para>Most tests stand on the ship of <see cref="KeepingTheirVacsuitOn"/>, on a second map
    /// in space (<see cref="HarnessVacuum.StageShip(Map, CellRect)"/>), and first change their
    /// crafter out of the vacsuit into the stand's duster, which steps the game's clock. Odyssey's
    /// vacuum simulation (<c>VacuumComponent</c>) exchanges air between neighbouring rooms every
    /// 250 ticks, and the ship has no oxygen pump to keep its air in, so its builder holds each
    /// room's vacuum where it set it while the clock runs (<see cref="HarnessVacuum.HoldVacuum"/>).</para>
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(HarnessDevTools.First - (int)CaseGroup.HeadingIntoVacuum)]
    internal class HeadingIntoVacuum : StoryFixture
    {
        /// <summary>What <see cref="HarnessVacuum.StageCrafterAtHome"/> sets up.</summary>
        internal const string CrafterAtHome =
            "a stand holding a duster and serving crafting, in a walled, roofed room on the colony's map, "
            + "and beside it a crafter in a shirt and trousers";

        /// <summary>
        /// A crafter already in the duster, their vacsuit parked, changes back before a bill whose
        /// fetch goes through vacuum and keeps the duster on for one that stays inside (the
        /// control). Dressing asked the same question, so the two directions agree.
        /// </summary>
        [Test]
        [TestDescription("a crafter in the stand's duster goes back to change before a bill whose steel "
                         + "lies out in vacuum, and not for one whose steel is in the room")]
        internal IEnumerator ChangesBackBeforeABillThroughVacuum()
        {
            return Staged(HarnessVacuum.StageShip, KeepingTheirVacsuitOn.OpenShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                if (!story.Check(SwapNow(story, fix), name + " changes out of the vacsuit into the stand's duster")
                    || !story.Check(fix.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                HarnessVacuum.Ship ship = HarnessVacuum.ShipOn(fix.Map);
                Job outside = HarnessVacuum.BillFetching(fix, ship.OnFloor);
                Job inside = HarnessVacuum.BillFetching(fix, ship.InRoom);
                if (outside == null || inside == null)
                {
                    story.Check(false, "a bill fetching steel from the open floor, and one fetching it "
                                       + "from the room, could be set up");
                    return;
                }
                story.Note("ten steel lie on the open floor, and ten more in the room");
                story.Check(Probe(fix, outside),
                            name + " is sent back to the stand to change before a bill fetching steel "
                            + "from the open floor");
                story.Check(!Probe(fix, inside),
                            "but keeps the duster on for the same bill fetching steel from the room");
            }
        }

        /// <summary>
        /// An order runs as given, in uniform; only a message says the uniform protects less, and
        /// it goes up on screen. A second order within the hour says nothing more, since vanilla
        /// answers a repeated message by flashing it and playing its sound again, and with the
        /// last warning forgotten it speaks again (the control). A drafted move, which Shift
        /// Change never stops to change, is warned about the same way.
        /// </summary>
        [Test]
        [TestDescription("ordered through vacuum in work clothes, a colonist goes as told, "
                         + "with a message as the only warning")]
        internal IEnumerator AnOrderThroughVacuumOnlyWarns()
        {
            return Staged(HarnessVacuum.StageShip, KeepingTheirVacsuitOn.OpenShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                if (!story.Check(SwapNow(story, fix), name + " changes out of the vacsuit into the stand's duster")
                    || !story.Check(fix.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                HarnessVacuum.Ship ship = HarnessVacuum.ShipOn(fix.Map);
                // Two cells of the strip: an item spawned onto another pushes it aside.
                Job ordered = HarnessVacuum.BillFetching(fix, ship.OnFloor);
                Job again = HarnessVacuum.BillFetching(fix, ship.OnFloor + IntVec3.East);
                if (ordered == null || again == null)
                {
                    story.Check(false, "two bills fetching steel from the open floor could be set up");
                    return;
                }
                ordered.playerForced = true;
                again.playerForced = true;
                story.Note("ten steel lie on each of two cells of the open floor, and " + name
                           + " is ordered to do a bill fetching each");
                int id = fix.Pawn.thingIDNumber;
                Patch_JobInterception.LastVacuumWarningTick.Remove(id);
                string text = "ShiftChange.OrderIntoVacuum".Translate(fix.Pawn.LabelShort);
                List<Message> before = HarnessVacuum.LiveMessages();
                story.Check(!Probe(fix, ordered),
                            "ordered to do the first bill, " + name + " is not sent to change first");
                story.Check(HarnessVacuum.PostedSince(before, text),
                            "and a message goes up: " + name + " is heading through vacuum in work clothes "
                            + "that protect them less than their own");
                story.Check(!Patch_JobInterception.WarnIfOrderedIntoVacuum(again, fix.Pawn),
                            "a second order within the hour puts up no second message");
                Patch_JobInterception.LastVacuumWarningTick.Remove(id);
                story.Note("Shift Change forgets when it last warned " + name);
                story.Check(Patch_JobInterception.WarnIfOrderedIntoVacuum(again, fix.Pawn),
                            "and then the same order warns again");

                Patch_JobInterception.LastVacuumWarningTick.Remove(id);
                Job move = JobMaker.MakeJob(JobDefOf.Goto, ship.OnFloor);
                move.playerForced = true;
                fix.Pawn.drafter.Drafted = true;
                story.Note(name + " is drafted and ordered to move onto the open floor, the last warning "
                           + "forgotten again");
                try
                {
                    story.Check(Patch_JobInterception.MapToDecideOn(move, fix.Pawn) == null,
                                "drafted, " + name + " is left to their orders: Shift Change does not weigh them");
                    story.Check(!Probe(fix, move), "so the move onto the open floor does not send them to change");
                    story.Check(Patch_JobInterception.WarnedLately(fix.Pawn), "but " + name + " is warned all the same");
                }
                finally
                {
                    fix.Pawn.drafter.Drafted = false;
                }
            }
        }

        /// <summary>
        /// The vacuum rule overrides the exemption emergencies have: an emergency job through
        /// vacuum sends a colonist in uniform back to change first, where the same job inside the
        /// room still goes straight ahead (the control). The emergency is firefighting, because
        /// the Medical emergencies change first setting never covers it.
        /// </summary>
        [Test]
        [TestDescription("even a fire out in vacuum sends a colonist in work clothes to change back first, "
                         + "where a fire in the room does not")]
        internal IEnumerator AFireOutInVacuumWaitsForAChange()
        {
            return Staged(HarnessVacuum.StageShip, KeepingTheirVacsuitOn.OpenShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkGiverDef fires = DefDatabase<WorkGiverDef>.GetNamedSilentFail("FightFires");
                if (!story.Check(fires != null && fires.emergency, "the game treats fighting fires as an emergency"))
                {
                    return;
                }
                if (!story.Check(SwapNow(story, fix), name + " changes out of the vacsuit into the stand's duster")
                    || !story.Check(fix.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                HarnessVacuum.Ship ship = HarnessVacuum.ShipOn(fix.Map);
                Job outside = JobMaker.MakeJob(JobDefOf.Wait, ship.OnFloor);
                outside.workGiverDef = fires;
                Job inside = JobMaker.MakeJob(JobDefOf.Wait, ship.InRoom);
                inside.workGiverDef = fires;
                story.Check(Probe(fix, outside),
                            "a fire to fight out on the open floor sends " + name + " back to the stand to change first");
                story.Check(!Probe(fix, inside), "while a fire in the room is fought straight away, in the duster");
            }
        }

        /// <summary>
        /// A stand whose outfit raises protection still dresses, for a bill fetching through
        /// vacuum: the crafter has no suit of their own, and the stand holds one. Once they wear
        /// it, the vacuum rule leaves them be, since the uniform protects better than what they
        /// parked.
        /// </summary>
        [Test]
        [TestDescription("a stand holding a vacsuit dresses a crafter with no suit of their own for a bill "
                         + "out in vacuum, and keeps them in it")]
        internal IEnumerator AVacsuitStandDressesThemForVacuum()
        {
            return Staged(HarnessVacuum.StageShip, KeepingTheirVacsuitOn.OpenShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                HarnessVacuum.StripVacsuit(fix.Pawn);
                ClearStand(fix.Stand);
                story.Note(name + "'s vacsuit and helmet are taken away, and so is the stand's duster");
                if (!story.Check(StockOne(fix.Stand, "Apparel_Vacsuit") && StockOne(fix.Stand, "Apparel_VacsuitHelmet"),
                                 "the stand holds a vacsuit and its helmet"))
                {
                    return;
                }
                HarnessVacuum.Ship ship = HarnessVacuum.ShipOn(fix.Map);
                Job outside = HarnessVacuum.BillFetching(fix, ship.OnFloor);
                if (outside == null)
                {
                    story.Check(false, "a bill fetching steel from the open floor could be set up");
                    return;
                }
                story.Note("ten steel lie on the open floor");
                story.Check(Probe(fix, outside),
                            name + ", with no suit of their own, is sent to the stand to change into its vacsuit "
                            + "for a bill fetching steel from the open floor");
                story.Check(SwapNow(story, fix), name + " changes into the stand's vacsuit and helmet");
                story.Check(fix.Comp.OnShift, "and is on shift");
                if (!fix.Comp.OnShift)
                {
                    return;
                }
                Job again = HarnessVacuum.BillFetching(fix, ship.OnFloor + IntVec3.East);
                story.Note("ten more steel lie on the open floor, for the next bill");
                story.Check(Patch_JobInterception.UniformIntoVacuum(again, fix.Pawn, fix.Comp, fix.Map)
                            == Patch_JobInterception.UniformInVacuum.Fine,
                            "in the stand's vacsuit, " + name + " may head into vacuum: it protects better "
                            + "than their own clothes");
                story.Check(!Probe(fix, again), "and the next bill through vacuum keeps them in it");
            }
        }

        /// <summary>
        /// An errand queued ahead of the stand's own work rides along in uniform, but not into
        /// vacuum: on the connected ship, a haul from the far room with a bill queued behind it
        /// keeps the duster on (the control), and with the corridor open to vacuum it sends them
        /// back to change first.
        /// </summary>
        [Test]
        [TestDescription("a haul ahead of a bill the stand serves keeps the duster on down a corridor "
                         + "with air, but not once the corridor is open to vacuum")]
        internal IEnumerator AHaulAheadOfABillStillChangesBackForVacuum()
        {
            return Staged(HarnessVacuum.StageConnectedShip, KeepingTheirVacsuitOn.ConnectedShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsUseCraftingSpot");
                if (giver == null)
                {
                    story.Check(false, "the game hands out bills at a crafting spot");
                    return;
                }
                if (!story.Check(SwapNow(story, fix), name + " changes out of the vacsuit into the stand's duster")
                    || !story.Check(fix.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                Map map = fix.Map;
                HarnessVacuum.Ship ship = HarnessVacuum.ShipOn(map);
                Thing steelFar = HarnessCatchments.Steel(map, ship.InFarRoom);
                JobQueue queue = fix.Pawn.jobs.jobQueue;
                queue.EnqueueFirst(HarnessRideAlong.QueuedWork(giver, ship.Bench));
                story.Note("twenty steel lie in the far room, and a bill at the crafting spot waits in "
                           + name + "'s queue");
                story.Check(!Probe(fix, HarnessRideAlong.GiverlessHaul(steelFar, ship.InRoom)),
                            "hauling the steel in from the far room down the corridor, ahead of the bill, "
                            + name + " keeps the duster on");
                HarnessVacuum.HoldVacuum(fix, map, ship.OnFloor, 1f);
                queue.EnqueueFirst(HarnessRideAlong.QueuedWork(giver, ship.Bench));
                story.Note("the corridor is opened to vacuum, and the bill waits in the queue again");
                story.Check(Probe(fix, HarnessRideAlong.GiverlessHaul(steelFar, ship.InRoom)),
                            "now the same haul sends " + name + " back to the stand to change first");
            }
        }

        /// <summary>
        /// A colonist in uniform cut off from their stand by vacuum is not sent back across it to
        /// change: a job in the far room keeps the duster on, and so does one out on the open
        /// floor, which a message reports instead. From the stand's room, the far-room job changes
        /// them back first (the control).
        /// </summary>
        [Test]
        [TestDescription("a colonist with vacuum between them and their stand is not sent back across it "
                         + "to change, and a message says why")]
        internal IEnumerator CutOffByVacuumTheyAreNotSentBack()
        {
            return Staged(HarnessVacuum.StageShip, KeepingTheirVacsuitOn.OpenShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsUseCraftingSpot");
                if (giver == null)
                {
                    story.Check(false, "the game hands out bills at a crafting spot");
                    return;
                }
                if (!story.Check(SwapNow(story, fix), name + " changes out of the vacsuit into the stand's duster")
                    || !story.Check(fix.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                Map map = fix.Map;
                HarnessVacuum.Ship ship = HarnessVacuum.ShipOn(map);
                int z = ship.OnFloor.z;
                Job local = HarnessCatchments.WorkAt(giver, new IntVec3(ship.FarRoom.minX + 3, 0, z + 1));
                Job outOnFloor = HarnessCatchments.WorkAt(giver, ship.OnFloor);
                Patch_JobInterception.LastVacuumWarningTick.Remove(fix.Pawn.thingIDNumber);
                string text = "ShiftChange.VacuumStandAcrossVacuum".Translate(fix.Pawn.LabelShort);

                fix.Pawn.Position = new IntVec3(ship.FarRoom.minX + 1, 0, z);
                story.Note(name + " is put in the far room, with the open floor between them and the stand");
                List<Message> before = HarnessVacuum.LiveMessages();
                story.Check(fix.Pawn.GetRoom() == ship.InFarRoom.GetRoom(map), name + " stands in the far room");
                story.Check(!Probe(fix, local),
                            "cut off by vacuum, " + name + " is not sent back across it to change for crafting "
                            + "in the far room");
                story.Check(!Probe(fix, outOnFloor), "nor for crafting out on the open floor");
                story.Check(HarnessVacuum.PostedSince(before, text),
                            "and a message says their own clothes are across vacuum too, so they will not "
                            + "go back to change first");

                fix.Pawn.Position = fix.Stand.InteractionCell;
                story.Note(name + " is put back beside the stand");
                story.Check(Probe(fix, HarnessCatchments.WorkAt(giver, new IntVec3(ship.FarRoom.minX + 3, 0, z + 1))),
                            "from there, the same crafting in the far room sends them to change first");
            }
        }

        /// <summary>
        /// The cooldown a failed change leaves does not wave the next job into vacuum: with it in
        /// force, a meal break that would change them back does not (the control), and a bill
        /// through vacuum still does.
        /// </summary>
        [Test]
        [TestDescription("while a colonist waits to try the stand again, a meal break does not send them "
                         + "to change, but a bill out in vacuum still does")]
        internal IEnumerator WaitingToRetryStillChangesBackForVacuum()
        {
            return Staged(HarnessVacuum.StageShip, KeepingTheirVacsuitOn.OpenShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                if (!story.Check(SwapNow(story, fix), name + " changes out of the vacsuit into the stand's duster")
                    || !story.Check(fix.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                int id = fix.Pawn.thingIDNumber;
                Patch_JobInterception.LastBlockedTick[id] = Find.TickManager.TicksGame;
                story.Note("a change for " + name + " could not start, a moment ago");
                story.Check(Patch_JobInterception.CoolingDown(fix.Pawn),
                            "so " + name + " waits a while before trying the stand again");
                story.Check(!HarnessLifecycle.MealBreakSendsThemBack(fix),
                            "and meanwhile a meal break does not send them back to change, as it would");
                // Asking about the meal break cleared the wait, so it starts again.
                Patch_JobInterception.LastBlockedTick[id] = Find.TickManager.TicksGame;
                story.Note("again a change for " + name + " could not start, a moment ago");
                Job outside = HarnessVacuum.BillFetching(fix, HarnessVacuum.ShipOn(fix.Map).OnFloor);
                story.Check(outside != null && Probe(fix, outside),
                            "but a bill fetching steel from the open floor still sends " + name
                            + " back to change first");
                story.Check(HarnessLifecycle.MealBreakSendsThemBack(fix),
                            "and, the wait cleared, a meal break sends them back to change too");
            }
        }

        /// <summary>
        /// The return trip's refusal holds in vacuum too: an urgent patient goes straight to a
        /// medical bed across vacuum in uniform, rather than to the stand to change, where
        /// recuperating there with no such wound changes them back first (the control).
        /// </summary>
        [Test]
        [TestDescription("a colonist with an untended wound walks to a medical bed across vacuum "
                         + "in work clothes, rather than stopping to change")]
        internal IEnumerator AnUrgentPatientGoesStraightToBedAcrossVacuum()
        {
            return Staged(HarnessVacuum.StageShip, KeepingTheirVacsuitOn.OpenShip, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                if (bedDef == null)
                {
                    story.Check(false, "the game has a bed");
                    return;
                }
                if (!story.Check(SwapNow(story, fix), name + " changes out of the vacsuit into the stand's duster")
                    || !story.Check(fix.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                Map map = fix.Map;
                HarnessVacuum.Ship ship = HarnessVacuum.ShipOn(map);
                Building_Bed bed = DebugTools_Fixtures.Spawn(map, bedDef, ThingDefOf.WoodLog,
                    new IntVec3(ship.FarRoom.minX + 2, 0, ship.OnFloor.z), Rot4.South) as Building_Bed;
                if (bed == null)
                {
                    story.Check(false, "a bed could be put in the far room");
                    return;
                }
                bed.Medical = true;
                story.Note("a medical bed stands in the far room, across the open floor");
                bool was = ShiftChangeMod.Settings.medicalEmergenciesChangeFirst;
                Hediff wound = null;
                try
                {
                    ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = false;
                    story.Note("Medical emergencies change first is off");
                    story.Check(!HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn),
                                name + " needs no doctor to begin with");
                    story.Check(Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed), JobTag.RestingForMedicalReasons),
                                "so recuperating in the medical bed across vacuum sends them to change first");
                    BodyPartRecord part = fix.Pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault();
                    wound = HediffMaker.MakeHediff(HediffDefOf.Cut, fix.Pawn, part);
                    wound.Severity = 6f;
                    fix.Pawn.health.AddHediff(wound, part);
                    story.Note(name + " gets a deep cut, untended");
                    story.Check(HealthAIUtility.ShouldSeekMedicalRestUrgent(fix.Pawn) && !fix.Pawn.Downed,
                                "the untended cut makes " + name + " an urgent case, still on their feet");
                    story.Check(!Probe(fix, JobMaker.MakeJob(JobDefOf.LayDown, bed), JobTag.RestingForMedicalReasons),
                                "so " + name + " goes straight to the medical bed across vacuum, in the duster");
                }
                finally
                {
                    ShiftChangeMod.Settings.medicalEmergenciesChangeFirst = was;
                    if (wound != null)
                    {
                        fix.Pawn.health.RemoveHediff(wound);
                    }
                }
            }
        }

        /// <summary>
        /// A colonist whose stand is on another map cannot change back, and is not left silent
        /// about it: flown to a vacuum map in a duster, their vacsuit parked at a stand on the
        /// colony's map, a job through vacuum puts up a message that their own clothes are not on
        /// this map, and so does an order.
        /// </summary>
        [Test]
        [TestDescription("a colonist flown to space with their own clothes left at a stand back home "
                         + "is warned before heading into vacuum")]
        internal IEnumerator WithTheirClothesOnAnotherMapTheyAreWarned()
        {
            return Staged(HarnessVacuum.StageCrafterAtHome, CrafterAtHome, Body);

            void Body(Fixture fix, Story story)
            {
                string name = fix.Pawn.LabelShort;
                WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoBillsUseCraftingSpot");
                if (giver == null)
                {
                    story.Check(false, "the game hands out bills at a crafting spot");
                    return;
                }
                if (!story.Check(WearOne(fix.Pawn, "Apparel_Vacsuit") && WearOne(fix.Pawn, "Apparel_VacsuitHelmet"),
                                 name + " puts on a vacsuit and its helmet")
                    || !story.Check(SwapNow(story, fix),
                                    "and changes out of the vacsuit into the duster at the stand on the colony's map")
                    || !story.Check(fix.Comp.OnShift, "and is on shift"))
                {
                    return;
                }
                Map home = fix.Map;
                Map away = SecondMap(home);
                if (away == null)
                {
                    story.Check(false, "a second map, out in space, could be made");
                    return;
                }
                fix.Pocket = away;
                HarnessVacuum.VacuumScene scene = HarnessVacuum.StageVacuumScene(away);
                story.Note("on a second map, out in space, a walled, roofed room with a door onto open floor");
                int id = fix.Pawn.thingIDNumber;
                string text = "ShiftChange.VacuumStandElsewhere".Translate(fix.Pawn.LabelShort);
                HarnessLifecycle.Fly(fix.Pawn, away, scene.InsideCell);
                story.Note(name + " flies there in the duster, into the room");
                try
                {
                    Patch_JobInterception.LastVacuumWarningTick.Remove(id);
                    List<Message> before = HarnessVacuum.LiveMessages();
                    story.Check(fix.Pawn.Map == away && CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                                name + " is on the space map, still on shift at the stand back home");
                    story.Check(!Probe(fix, HarnessCatchments.WorkAt(giver, scene.OutsideCell)),
                                "crafting out on the open floor does not send " + name + " after a stand on another map");
                    story.Check(HarnessVacuum.PostedSince(before, text),
                                "and a message says their own clothes are not on this map");

                    Patch_JobInterception.LastVacuumWarningTick.Remove(id);
                    Job ordered = HarnessCatchments.WorkAt(giver, scene.OutsideCell);
                    ordered.playerForced = true;
                    story.Check(Patch_JobInterception.WarnIfOrderedIntoVacuum(ordered, fix.Pawn),
                                "and an order to work out there warns the same");
                }
                finally
                {
                    HarnessLifecycle.Fly(fix.Pawn, home, fix.Stand.InteractionCell);
                }
            }
        }
    }
}
