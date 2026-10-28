// HARNESS only, compiled out of a plain Release build. The guard is whole-file, always
// (check-invariants.py enforces it); why: docs/DESIGN.md, "Development tooling".
#if HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static ShiftChange.DebugTools_LifecycleHarness;
using static ShiftChange.HarnessFixtures;

namespace ShiftChange
{
    /// <summary>
    /// Harness cases for the engine lifecycle events a ledger has to survive: gravship
    /// flight, map removal, teardown, reinstall, death and banishment, and the fault latch
    /// that disables interception after repeated throws. Also holds the flight and meal-break
    /// helpers <see cref="HarnessVacuum"/> reuses. Cases are registered, and ordered, in
    /// <see cref="DebugTools_LifecycleHarness.Run"/>.
    /// </summary>
    internal static class HarnessLifecycle
    {
        /// <summary>
        /// A flight carrying the stand and its borrower together. The stand, the parked civvies
        /// and the borrower all survive a takeoff's <c>WillReplace</c> despawn, so the ledger must
        /// survive too, or the pawn lands in a uniform with no way out (<c>Building_OutfitStand</c>
        /// keeps its contents in that mode, see <see cref="CompShiftStand.PostDeSpawn"/>). A
        /// gravship in flight is a world object with no gear tab, so nothing happens to the pawn's
        /// clothes meanwhile: after landing the next meal break sends them to the stand, and the
        /// return leg ends the shift normally.
        /// </summary>
        internal static bool GravshipFlight(Fixture fix)
        {
            IntVec3 standCell = fix.Stand.Position;
            IntVec3 pawnCell = fix.Pawn.Position;
            Apparel uniform = FirstIssued(fix);
            List<Apparel> parked = new List<Apparel>(fix.Comp.StoredOwnerApparelForReading);
            if (uniform == null || parked.Count == 0)
            {
                return Expect(false, "the fixture is mid-shift with clothes parked");
            }

            fix.Stand.DeSpawn(DestroyMode.WillReplace);
            fix.Pawn.DeSpawn(DestroyMode.WillReplace);
            GenSpawn.Spawn(fix.Stand, standCell, fix.Map, Rot4.North);
            GenSpawn.Spawn(fix.Pawn, pawnCell, fix.Map, Rot4.North);
            fix.Stand.PostSwapMap();
            fix.Pawn.PostSwapMap();

            bool ok = Expect(fix.Comp.OnShift, "stand still reads on-shift")
                    & Expect(fix.Comp.Borrower == fix.Pawn, "borrower survived the flight")
                    & Expect(fix.Comp.StoredOwnerApparelForReading.Count == fix.StoredCount,
                             "parked civvies still in the ledger")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                             "registry points back at the stand (Change back works)")
                    & Expect(MealBreakSendsThemBack(fix),
                             "and the next meal break sends them to the stand on its own");
            return ok & ShiftEndsNormally(fix, uniform, parked);
        }

        /// <summary>
        /// The stand flies and the borrower stays, spawned, on a map the takeoff keeps because it
        /// has a grav anchor (<c>WorldComponent_GravshipController.TakeoffEnded</c>). The shift
        /// stays suspended while they are apart, uniform forced and wardrobe optimizer paused,
        /// and resumes once they share a map (docs/DESIGN.md, "Ownership and the ledger"). The
        /// pause keys on the registry entry, which is what is asserted; the optimizer is not run.
        /// </summary>
        internal static bool StandFliesWithoutItsBorrower(Fixture fix)
        {
            Apparel uniform = FirstIssued(fix);
            List<Apparel> parked = new List<Apparel>(fix.Comp.StoredOwnerApparelForReading);
            if (uniform == null || parked.Count == 0)
            {
                return Expect(false, "the fixture is mid-shift with clothes parked");
            }
            Map home = fix.Map;
            IntVec3 standCell = fix.Stand.Position;
            Map away = SecondMap(home);
            if (away == null)
            {
                return Expect(false, "a second map was made");
            }
            fix.Pocket = away;

            Fly(fix.Stand, away, away.Center);
            bool ok = Expect(fix.Stand.Map == away, "the stand landed on the second map")
                    & Expect(fix.Pawn.Spawned && fix.Pawn.Map == home,
                             "and the borrower is still spawned on the first, which the takeoff kept")
                    & Expect(fix.Comp.OnShift && fix.Comp.Borrower == fix.Pawn,
                             "the shift is kept while they are apart")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                             "and the registry still sends the borrower to the stand")
                    & Expect(StillForced(fix, uniform), "the uniform stays forced while they are apart")
                    & Expect(!MealBreakSendsThemBack(fix),
                             "and a meal break does not send them after a stand on another map");
            for (int i = 0; i < parked.Count; i++)
            {
                ok &= Expect(fix.Stand.HeldItems.Contains(parked[i]),
                             parked[i].def.defName + " flew with the stand");
            }

            Fly(fix.Stand, home, standCell);
            ok &= Expect(fix.Stand.Map == home, "the stand flew back")
                & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                         "and the registry still sends the borrower to it")
                & Expect(MealBreakSendsThemBack(fix),
                         "and the next meal break sends them to the stand on its own");
            return ok & ShiftEndsNormally(fix, uniform, parked);
        }

        /// <summary>
        /// The mirror of <see cref="StandFliesWithoutItsBorrower"/>: the borrower flies and the
        /// stand stays on the anchored map. The shift is kept while they are apart, the uniform
        /// still forced and no meal break sending them after a stand on another map, and it
        /// resumes once the borrower flies back. The stand is never told, since it does not move
        /// and nothing of ours hears a pawn's <c>PostSwapMap</c>.
        /// </summary>
        internal static bool BorrowerFliesWithoutTheirStand(Fixture fix)
        {
            Apparel uniform = FirstIssued(fix);
            List<Apparel> parked = new List<Apparel>(fix.Comp.StoredOwnerApparelForReading);
            if (uniform == null || parked.Count == 0)
            {
                return Expect(false, "the fixture is mid-shift with clothes parked");
            }
            Map home = fix.Map;
            Map away = SecondMap(home);
            if (away == null)
            {
                return Expect(false, "a second map was made");
            }
            fix.Pocket = away;

            Fly(fix.Pawn, away, away.Center);
            bool ok = Expect(fix.Pawn.Map == away, "the borrower landed on the second map")
                    & Expect(fix.Stand.Spawned && fix.Stand.Map == home,
                             "and the stand is still on the first, which the takeoff kept")
                    & Expect(fix.Comp.OnShift && fix.Comp.Borrower == fix.Pawn,
                             "the shift is kept while they are apart")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                             "and the registry still sends the borrower to the stand")
                    & Expect(fix.Pawn.apparel.WornApparel.Contains(uniform),
                             "and the borrower still wears the uniform")
                    & Expect(StillForced(fix, uniform), "forced, while they are apart")
                    & Expect(!MealBreakSendsThemBack(fix),
                             "and a meal break does not send them after a stand on another map");

            Fly(fix.Pawn, home, fix.Stand.InteractionCell);
            ok &= Expect(fix.Pawn.Map == home, "the borrower flew back")
                & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                         "and the registry still sends them to the stand")
                & Expect(MealBreakSendsThemBack(fix),
                         "and the next meal break sends them to the stand on its own");
            return ok & ShiftEndsNormally(fix, uniform, parked);
        }

        /// <summary>
        /// The borrower flies and the stand stays on a map with no grav anchor, which the takeoff
        /// abandons (<c>GravshipUtility.AbandonMap</c>). Map removal never despawns a building:
        /// <c>Thing.Notify_MyMapRemoved</c> marks it discarded, so
        /// <see cref="CompShiftStand.PostDeSpawn"/> never runs and
        /// <see cref="CompShiftStand.Notify_MapRemoved"/> is the stand's only notice. The parked
        /// clothes go with the map; the case asserts the stand discarded, its ledger and registry
        /// entry released, and the uniform still worn but no longer forced, since a forced flag
        /// would pin the borrower into it (<see cref="CompShiftStand.ReleaseBorrower"/>).
        /// Built on the second map (<see cref="BuildOnSecondMap"/>) and removed through the same
        /// <c>Game.DeinitAndRemoveMap</c> an abandoned map reaches.
        /// </summary>
        internal static bool StandLostWithItsMap(Fixture fix, Map home, CellRect pad)
        {
            Apparel uniform = FirstIssued(fix);
            if (uniform == null)
            {
                return Expect(false, "the fixture is mid-shift");
            }
            Map origin = fix.Pocket;

            Fly(fix.Pawn, home, new IntVec3(pad.minX + 3, 0, pad.minZ + 1));
            bool ok = Expect(fix.Pawn.Map == home, "the borrower landed on the harness's map")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                             "and is still on shift at the stand left behind");

            RemoveSecondMap(origin);
            fix.Pocket = null;
            ok &= Expect(!Find.Maps.Contains(origin), "the map the stand was left on is gone")
                & Expect(fix.Stand.Discarded, "and the stand with it, discarded rather than despawned")
                & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == null,
                         "the registry no longer sends the borrower to it")
                & Expect(fix.Pawn.apparel.WornApparel.Contains(uniform),
                         "the uniform stays on, now as their own clothing");
            if (fix.Pawn.outfits != null)
            {
                ok &= Expect(!fix.Pawn.outfits.forcedHandler.IsForced(uniform),
                             "and is no longer force-worn, so their wardrobe can replace it");
            }
            return ok & Expect(!fix.Comp.OnShift && fix.Comp.Borrower == null,
                               "the lost stand's own ledger was released, not merely forgotten");
        }

        /// <summary>
        /// <see cref="StandLostWithItsMap"/> with the stand minified mid-shift and the box left on
        /// the map that is removed. Map removal reaches things inside containers
        /// (<c>Map.GetChildHolders</c>), so the boxed stand hears it too. The minify already took
        /// the forced flag off; this asserts the ledger is released with the box.
        /// </summary>
        internal static bool BoxedStandLostWithItsMap(Fixture fix, Map home, CellRect pad)
        {
            Apparel uniform = FirstIssued(fix);
            if (uniform == null)
            {
                return Expect(false, "the fixture is mid-shift");
            }
            Map origin = fix.Pocket;
            IntVec3 boxCell = fix.Stand.Position;
            MinifiedThing box = fix.Stand.MakeMinified();
            if (box == null)
            {
                return Expect(false, "the stand minified");
            }
            GenSpawn.Spawn(box, boxCell, origin);

            Fly(fix.Pawn, home, new IntVec3(pad.minX + 3, 0, pad.minZ + 1));
            bool ok = Expect(box.Spawned && box.Map == origin, "the box is on the map that will go")
                    & Expect(fix.Comp.OnShift && fix.Comp.Borrower == fix.Pawn,
                             "and its stand still holds the ledger (control)");

            RemoveSecondMap(origin);
            fix.Pocket = null;
            ok &= Expect(box.Discarded && fix.Stand.Discarded, "the box and the stand in it went with the map")
                & Expect(!fix.Comp.OnShift && fix.Comp.Borrower == null,
                         "and the boxed stand's ledger was released")
                & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == null,
                         "the registry no longer sends the borrower to it");
            if (fix.Pawn.outfits != null)
            {
                ok &= Expect(!fix.Pawn.outfits.forcedHandler.IsForced(uniform),
                             "and the uniform is not force-worn");
            }
            return ok;
        }

        /// <summary>
        /// Every stand def carrying our comp asks to hear when its map is removed
        /// (<see cref="CompProperties_ShiftStand.ResolveReferences"/>). Without
        /// <c>notifyMapRemoved</c> the engine never calls
        /// <see cref="CompShiftStand.Notify_MapRemoved"/>, and the two map-loss cases cover only
        /// the vanilla stand; under <c>--with=khamenman.outfitstandsplus</c> this reaches that
        /// mod's stands too.
        /// </summary>
        internal static bool StandDefsHearMapRemoval()
        {
            List<ThingDef> defs = Patch_JobInterception.StandDefs;
            bool ok = Expect(defs.Count > 0, "the stand family resolves (control)");
            for (int i = 0; i < defs.Count; i++)
            {
                ok &= Expect(defs[i].notifyMapRemoved,
                             defs[i].defName + " asks to hear when its map is removed");
            }
            return ok;
        }

        /// <summary>
        /// One thing's gravship flight, in the engine's order: <c>PreSwapMap</c> and a
        /// <c>WillReplace</c> despawn at takeoff (<c>GravshipUtility.GenerateGravship</c>), a
        /// spawn on the landing map, then <c>PostSwapMap</c>
        /// (<c>GravshipPlacementUtility.PostSwapMap</c>). For a flight carrying one of the pair;
        /// <see cref="GravshipFlight"/> interleaves the two as the engine does with both aboard,
        /// without the <c>PreSwapMap</c> step, which nothing of ours overrides.
        /// </summary>
        internal static void Fly(Thing thing, Map to, IntVec3 cell)
        {
            Rot4 rot = thing.Rotation;
            thing.PreSwapMap();
            thing.DeSpawn(DestroyMode.WillReplace);
            GenSpawn.Spawn(thing, cell, to, rot);
            thing.PostSwapMap();
        }

        /// <summary>
        /// The return leg, run for real at the stand: the uniform must end up back on the stand
        /// and every parked garment back on the borrower.
        /// </summary>
        internal static bool ShiftEndsNormally(Fixture fix, Apparel uniform, List<Apparel> parked)
        {
            // On the interaction cell: pathing is asynchronous, so a ticked pawn never finishes a
            // walk (docs/TESTING.md, "Four engine traps").
            fix.Pawn.Position = fix.Stand.InteractionCell;
            if (!RunSwap(fix))
            {
                return Expect(false, "the return leg ran to completion");
            }
            bool ok = Expect(!fix.Comp.OnShift, "the return leg ended the shift");
            for (int i = 0; i < parked.Count; i++)
            {
                ok &= Expect(fix.Pawn.apparel.WornApparel.Contains(parked[i]),
                             parked[i].def.defName + " is back on the pawn");
            }
            return ok & Expect(fix.Stand.HeldItems.Contains(uniform),
                               "and the uniform is back on the stand");
        }

        /// <summary>
        /// Whether a meal break would send the borrower to the stand right now: the automatic
        /// return trip, asked of the real interception and left with no trace
        /// (<see cref="Probe"/>). A meal on the ground is a sit-down break, which changes the pawn
        /// out first wherever they eat (<c>MealBreakChangesOut</c>), so this asks of a flight only
        /// whether the stand is within reach. The probe clears the retry cooldown a refusal
        /// stamps, which in play can delay the first change back after a reunion by up to
        /// <see cref="Patch_JobInterception.RetryCooldownTicks"/>.
        /// </summary>
        internal static bool MealBreakSendsThemBack(Fixture fix)
        {
            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
            if (mealDef == null || !fix.Pawn.Spawned)
            {
                return false;
            }
            Thing meal = GenSpawn.Spawn(ThingMaker.MakeThing(mealDef), fix.Pawn.Position, fix.Pawn.Map);
            bool sent = Probe(fix, IngestJob(meal));
            if (!meal.Destroyed)
            {
                meal.Destroy();
            }
            return sent;
        }

        /// <summary>Whether the borrower's uniform still carries the forced flag.</summary>
        internal static bool StillForced(Fixture fix, Apparel uniform)
        {
            return fix.Pawn.outfits != null && fix.Pawn.outfits.forcedHandler.IsForced(uniform);
        }

        /// <summary>The first garment the fixture issued, or null.</summary>
        internal static Apparel FirstIssued(Fixture fix)
        {
            return fix.Comp.IssuedUniformForReading.Count > 0
                ? fix.Comp.IssuedUniformForReading[0]
                : null;
        }

        /// <summary>
        /// The stand flies and the borrower is left on a map the takeoff abandoned, so nobody can
        /// walk back for their clothes. The ledger must go, and the forced flags with it, or the
        /// pawn is pinned into the uniform with every route out closed.
        /// </summary>
        internal static bool GravshipFlightLeftBehind(Fixture fix)
        {
            IntVec3 standCell = fix.Stand.Position;
            Apparel uniform = fix.Comp.IssuedUniformForReading.Count > 0
                ? fix.Comp.IssuedUniformForReading[0]
                : null;

            // A despawn with no respawn stands in for a borrower left on an abandoned map: on no
            // map, which is what the stand's PostSwapMap checks for.
            fix.Stand.DeSpawn(DestroyMode.WillReplace);
            fix.Pawn.DeSpawn(DestroyMode.Vanish);
            GenSpawn.Spawn(fix.Stand, standCell, fix.Map, Rot4.North);
            fix.Stand.PostSwapMap();

            bool ok = Expect(!fix.Comp.OnShift, "ledger dropped for an absent borrower")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == null,
                             "registry entry cleared");
            if (uniform != null && fix.Pawn.outfits != null)
            {
                ok &= Expect(!fix.Pawn.outfits.forcedHandler.IsForced(uniform),
                             "uniform no longer force-worn");
            }
            return ok;
        }

        /// <summary>
        /// Deconstruct or burn down. The stand is really gone, and unlike a flight
        /// (<c>WillReplace</c>) or a minify (<c>Vanish</c>) a <c>Deconstruct</c> despawn makes
        /// <c>Building_OutfitStand</c> drop its container, so the civvies are already on the
        /// floor, the ledger cannot be honoured, and the forced flags must come off. The case uses
        /// <c>Deconstruct</c> because <c>Vanish</c>, the minify, keeps the ledger: testing the
        /// release through it would assert the opposite of what ships.
        /// </summary>
        internal static bool TeardownReleases(Fixture fix)
        {
            Apparel uniform = fix.Comp.IssuedUniformForReading.Count > 0
                ? fix.Comp.IssuedUniformForReading[0]
                : null;

            fix.Stand.DeSpawn(DestroyMode.Deconstruct);

            bool ok = Expect(!fix.Comp.OnShift, "ledger released")
                    & Expect(fix.Comp.Borrower == null, "borrower cleared")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == null,
                             "registry entry cleared");
            if (uniform != null && fix.Pawn.outfits != null)
            {
                ok &= Expect(!fix.Pawn.outfits.forcedHandler.IsForced(uniform),
                             "uniform no longer force-worn");
            }
            return ok;
        }

        /// <summary>
        /// Moving a stand while somebody has its uniform out: they must still be able to hand it
        /// back afterwards. Guards a past bug where a move released the ledger and left the pawn
        /// stuck in the uniform.
        ///
        /// <para>A minify is a <c>Vanish</c> despawn and <c>Building_OutfitStand</c> keeps its
        /// container through one, so the borrower's civvies travel inside the box. The ledger has
        /// to travel with them, or the stand lands reading those civvies as its own kit; dropped
        /// on the floor instead, they leave the pawn just as stuck in the uniform. The forced
        /// flag is the exception: it must not survive the boxed window, so the case asserts it
        /// off while boxed and on again on landing.</para>
        /// </summary>
        internal static bool ReinstallKeepsTheLedger(Fixture fix)
        {
            Apparel uniform = fix.Comp.IssuedUniformForReading.Count > 0
                ? fix.Comp.IssuedUniformForReading[0]
                : null;
            List<Apparel> parked = new List<Apparel>(fix.Comp.StoredOwnerApparelForReading);
            if (uniform == null || parked.Count == 0)
            {
                return Expect(false, "the fixture is mid-shift with clothes parked");
            }

            Map map = fix.Map;
            IntVec3 to = new IntVec3(fix.Stand.Position.x, 0, fix.Stand.Position.z + 2);
            Rot4 rot = fix.Stand.Rotation;

            MinifiedThing box = fix.Stand.MakeMinified();
            if (box == null)
            {
                return Expect(false, "the stand minified");
            }
            bool ok = Expect(fix.Comp.OnShift, "the ledger rode into the box")
                    & Expect(fix.Comp.Borrower == fix.Pawn, "and still names the borrower");
            for (int i = 0; i < parked.Count; i++)
            {
                ok &= Expect(fix.Stand.HeldItems.Contains(parked[i]),
                             parked[i].def.defName + " travelled with the stand");
            }
            if (fix.Pawn.outfits != null)
            {
                ok &= Expect(!fix.Pawn.outfits.forcedHandler.IsForced(uniform),
                             "the uniform is NOT pinned while the stand is boxed");
            }

            box.InnerThing = null;
            box.Destroy();
            GenSpawn.Spawn(fix.Stand, to, map, rot);

            ok &= Expect(fix.Stand.Spawned, "the stand is back on the map")
                & Expect(fix.Comp.OnShift, "the ledger survived the move")
                & Expect(fix.Comp.Borrower == fix.Pawn, "the borrower survived it")
                & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                         "the registry points back at the stand — Change back works")
                & Expect(fix.Comp.StoredOwnerApparelForReading.Count == parked.Count,
                         "every parked garment is still in the ledger");
            if (fix.Pawn.outfits != null)
            {
                ok &= Expect(fix.Pawn.outfits.forcedHandler.IsForced(uniform),
                             "and the uniform is force-worn again on landing");
            }

            // The return trip actually runs and the pawn gets their own clothes back: the
            // behaviour, where everything above is state. The pawn stands on the interaction cell
            // because pathing is asynchronous (docs/TESTING.md, "Four engine traps").
            fix.Pawn.Position = fix.Stand.InteractionCell;
            if (!RunSwap(fix))
            {
                return ok & Expect(false, "the return leg ran to completion");
            }
            ok &= Expect(!fix.Comp.OnShift, "the uniform went back into the stand");
            for (int i = 0; i < parked.Count; i++)
            {
                ok &= Expect(fix.Pawn.apparel.WornApparel.Contains(parked[i]),
                             parked[i].def.defName + " is back on the pawn");
            }
            return ok & Expect(fix.Stand.HeldItems.Contains(uniform),
                               "and the uniform is back on the stand");
        }

        /// <summary>
        /// Relocating a stand with the Reinstall designator is a minify and a respawn of the same
        /// thing, so everything the player configured must come out the other side: owner, work
        /// types and full-change flag. The removal flag, staged on, must be held off again on
        /// landing, as it is for any stand in service.
        ///
        /// <para>Owners are the half that was broken: the base comp parks them on despawn and
        /// offers them back on spawn, but only past <c>CanSetUninstallAssignedPawn</c>, whose base
        /// answer is false, and it clears the parked list either way, so a move silently unowned
        /// the stand. The other fields stay on the comp, which rides in the box unsaved, so this
        /// case proves no scribing; <c>PostSpawnSetup</c> holds the removal flag off again.</para>
        ///
        /// <para>The minify goes through <c>MakeMinified</c>, not a bare <c>DeSpawn</c>, because
        /// the wrap is what the real path does and would notice a stand left holding a live
        /// reference. The respawn has the shape of <c>Blueprint_Install.MakeSolidThing</c>: take
        /// the inner thing out of the box, destroy the box, then spawn the thing.</para>
        /// </summary>
        internal static bool ReinstallKeepsConfiguration(Fixture fix)
        {
            CompAssignableToPawn assignable =
                fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
            if (assignable == null)
            {
                return Expect(false, "the stand carries our assignable comp");
            }

            AccessTools.FieldRef<Building_OutfitStand, bool> flag =
                Patch_AllowRemovingToggle.AllowRemovingItemsRef;
            WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
            if (flag == null || work == null)
            {
                return Expect(false, "allowRemovingItems and the Doctor work type both resolve");
            }

            assignable.TryAssignPawn(fix.Pawn);
            fix.Comp.ToggleWork(work);
            fix.Comp.SetFullChange(true);
            // Set last: ToggleWork enters service, which clears the flag, so setting it earlier
            // would leave nothing to assert about the landing.
            flag(fix.Stand) = true;

            Map map = fix.Map;
            IntVec3 from = fix.Stand.Position;
            IntVec3 to = new IntVec3(from.x, 0, from.z + 2);
            Rot4 rot = fix.Stand.Rotation;

            MinifiedThing box = fix.Stand.MakeMinified();
            if (box == null)
            {
                return Expect(false, "the stand minified");
            }
            bool ok = Expect(!fix.Stand.Spawned, "the stand is off the map while boxed");

            box.InnerThing = null;
            box.Destroy();
            GenSpawn.Spawn(fix.Stand, to, map, rot);

            return ok
                 & Expect(fix.Stand.Spawned, "the stand is back on the map")
                 & Expect(fix.Stand.Position == to, "and in its new cell")
                 & Expect(assignable.AssignedPawnsForReading.Contains(fix.Pawn),
                          "the owner survived the move")
                 & Expect(fix.Comp.WorkTypes.Contains(work),
                          "the custom work set survived the move")
                 & Expect(fix.Comp.FullChange, "the full-change flag survived the move")
                 & Expect(!flag(fix.Stand), "the removal flag is held off on landing");
        }

        /// <summary>
        /// An owner who dies while their stand is boxed must not own it again when it is set
        /// down. Guards a past bug. Death reaches the reaper, but the reaper scans spawned stands
        /// only, so the boxed stand's parked owner list keeps the pawn; the base comp then offers
        /// each parked owner back through <c>CanSetUninstallAssignedPawn</c>, and a dead colonist
        /// still passes <c>IsColonist</c>. Minified and set down as
        /// <see cref="ReinstallKeepsConfiguration"/> does it.
        /// </summary>
        internal static bool DeadOwnerNotRestoredOnReinstall(Fixture fix)
        {
            CompAssignableToPawn assignable =
                fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
            if (assignable == null)
            {
                return Expect(false, "the stand carries our assignable comp");
            }

            assignable.TryAssignPawn(fix.Pawn);
            bool ok = Expect(assignable.AssignedPawnsForReading.Contains(fix.Pawn),
                             "the colonist owns the stand (control)");

            Map map = fix.Map;
            IntVec3 from = fix.Stand.Position;
            IntVec3 to = new IntVec3(from.x, 0, from.z + 2);
            Rot4 rot = fix.Stand.Rotation;

            MinifiedThing box = fix.Stand.MakeMinified();
            if (box == null)
            {
                return ok & Expect(false, "the stand minified");
            }
            ok &= Expect(assignable.AssignedPawnsForReading.Count == 0,
                         "boxed, the owner is parked off the list (control)");

            fix.Pawn.Kill(null);
            ok &= Expect(fix.Pawn.Dead, "the owner dies while the stand is boxed (control)")
                & Expect(fix.Pawn.IsColonist, "and still counts as a colonist, dead (control)");

            box.InnerThing = null;
            box.Destroy();
            GenSpawn.Spawn(fix.Stand, to, map, rot);

            ok &= Expect(fix.Stand.Spawned, "the stand is set down again (control)")
                & Expect(!assignable.AssignedPawnsForReading.Contains(fix.Pawn),
                         "the dead colonist does not own the reinstalled stand");

            assignable.TryUnassignPawn(fix.Pawn);
            return ok;
        }

        /// <summary>
        /// A colonist kidnapped while their stand is boxed neither owns it nor keeps their shift
        /// at it when it is set down, as they would not had it stood on a map. Kidnapping reaches
        /// the reaper, which cannot find a boxed stand, and a kidnapped pawn keeps the player
        /// faction, so <c>IsColonist</c> and <see cref="CompShiftStand.StillOurs"/> both pass.
        /// The fixture's borrower is the owner; the kidnapping is undone before teardown.
        /// Minified and set down as <see cref="ReinstallKeepsConfiguration"/> does it.
        /// </summary>
        internal static bool KidnappedOwnerNotRestoredOnReinstall(Fixture fix)
        {
            CompAssignableToPawn assignable =
                fix.Stand.TryGetComp<CompAssignableToPawn_ShiftStand>();
            Faction kidnappers = Find.FactionManager.AllFactionsListForReading
                .FirstOrDefault(f => !f.IsPlayer && f.def.humanlikeFaction);
            if (assignable == null || kidnappers == null)
            {
                return Expect(false, "the stand carries our assignable comp, and a faction exists "
                                     + "to kidnap with");
            }

            assignable.TryAssignPawn(fix.Pawn);
            Apparel uniform = fix.Comp.IssuedUniformForReading.FirstOrDefault();
            bool ok = Expect(assignable.AssignedPawnsForReading.Contains(fix.Pawn),
                             "the colonist owns the stand (control)")
                    & Expect(fix.Comp.OnShift && fix.Comp.Borrower == fix.Pawn,
                             "and is on shift at it (control)")
                    & Expect(uniform != null, "wearing its uniform (control)");

            Map map = fix.Map;
            IntVec3 from = fix.Stand.Position;
            IntVec3 to = new IntVec3(from.x, 0, from.z + 2);
            Rot4 rot = fix.Stand.Rotation;

            MinifiedThing box = fix.Stand.MakeMinified();
            if (box == null)
            {
                return ok & Expect(false, "the stand minified");
            }

            kidnappers.kidnapped.Kidnap(fix.Pawn, null);
            try
            {
                ok &= Expect(fix.Pawn.IsKidnapped(), "the owner is kidnapped while the stand is "
                                                     + "boxed (control)")
                    & Expect(fix.Pawn.IsColonist, "and still counts as a colonist (control)");

                box.InnerThing = null;
                box.Destroy();
                GenSpawn.Spawn(fix.Stand, to, map, rot);

                ok &= Expect(fix.Stand.Spawned, "the stand is set down again (control)")
                    & Expect(!assignable.AssignedPawnsForReading.Contains(fix.Pawn),
                             "the kidnapped colonist does not own the reinstalled stand")
                    & Expect(!fix.Comp.OnShift, "and their shift there is released")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == null,
                             "so no stand still has them checked out");
                if (uniform != null && fix.Pawn.outfits != null)
                {
                    ok &= Expect(!fix.Pawn.outfits.forcedHandler.IsForced(uniform),
                                 "the uniform they were taken in is ordinary clothing");
                }
            }
            finally
            {
                kidnappers.kidnapped.RemoveKidnappedPawn(fix.Pawn);
            }

            assignable.TryUnassignPawn(fix.Pawn);
            return ok;
        }

        /// <summary>
        /// Death runs <c>Pawn.Kill</c>, then <c>Pawn.Destroy(KillFinalize)</c>, then
        /// <c>Pawn_Ownership.UnclaimAll</c>, which <see cref="Patch_UnclaimStands"/> hooks. The
        /// case asserts the ledger is reaped and the registry entry cleared on that path.
        /// </summary>
        internal static bool DeathReaps(Fixture fix)
        {
            fix.Pawn.Kill(null);
            return Expect(!fix.Comp.OnShift, "ledger reaped on death")
                 & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == null,
                          "registry entry cleared");
        }

        /// <summary>
        /// The exit vanilla does not route through <c>UnclaimAll</c>: on a spawned colonist
        /// <c>PawnBanishUtility.Banish</c> clears guest status and sets the faction to null, and
        /// reaches no unclaim, so <see cref="Patch_BanishStands"/> calls the reaper itself.
        /// Asserted as deeply as <see cref="DeathReaps"/>, plus the forced flag, which matters here
        /// because a banished pawn stays alive on the map: a ledger cleared without it pins them
        /// into the uniform with every route out closed.
        /// </summary>
        internal static bool BanishmentReaps(Fixture fix)
        {
            Apparel uniform = fix.Comp.IssuedUniformForReading.Count > 0
                ? fix.Comp.IssuedUniformForReading[0]
                : null;

            PawnBanishUtility.Banish(fix.Pawn, giveThoughts: false);

            bool ok = Expect(!fix.Comp.OnShift, "ledger reaped on banishment")
                    & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == null,
                             "registry entry cleared");
            if (uniform != null && fix.Pawn.outfits != null)
            {
                ok &= Expect(!fix.Pawn.outfits.forcedHandler.IsForced(uniform),
                             "uniform no longer force-worn");
            }
            // The reap must survive the pawn being recruited back. A fix that only disbelieved a
            // departed borrower's ledger would pass everything above and fail here, since the
            // ledger was never emptied.
            fix.Pawn.SetFaction(Faction.OfPlayer);
            ok &= Expect(!fix.Comp.OnShift, "still reaped after re-recruitment");
            return ok;
        }

        /// <summary>
        /// The fault latch, driven by real throws through the catch block of the real
        /// <see cref="Patch_JobInterception.Prefix"/> rather than by calling the counter (the seam
        /// is <see cref="Patch_JobInterception.injectFaults"/>). Three claims: throws below the
        /// limit are counted and the mod keeps serving, the limit latches interception off, and a
        /// game change re-arms it (<see cref="Patch_JobInterception.faulted"/>). The third guards
        /// a past bug in which only quitting the game re-armed it. A save load cannot be run
        /// here, so the case resets the game reference under <see cref="SessionGuard"/>, the
        /// trigger a load pulls.
        /// </summary>
        internal static bool FaultLatchRecovers(Fixture fix)
        {
            int limitBefore = Patch_JobInterception.FaultLimit;
            bool enabledBefore = Patch_JobInterception.Enabled;
            Patch_JobInterception.ResetSessionState();
            Patch_JobInterception.FaultLimit = 3;

            bool ok = Expect(!Patch_JobInterception.faulted, "starts armed");

            // Below the limit: counted, still serving. The catch also sees other mods' exceptions
            // thrown through our frame, so one throw must not disable the mod for the colony.
            Fault(fix.Pawn, 2);
            ok &= Expect(Patch_JobInterception.faultCount == 2, "throws are counted")
                & Expect(!Patch_JobInterception.faulted, "still armed below the limit");

            Fault(fix.Pawn, 1);
            ok &= Expect(Patch_JobInterception.faulted, "latched at the limit");

            // A game change, as a save load makes one.
            SessionGuard.current = null;
            SessionGuard.Ensure();
            ok &= Expect(!Patch_JobInterception.faulted, "a game change re-arms it")
                & Expect(Patch_JobInterception.faultCount == 0, "the count resets with it");

            // Enabled is a standing decision, the player's or the hot-reload quarantine's, and the
            // quarantine must survive a load because the twin JobDriver that wedges a tracker is
            // still loaded. Re-arming it along with the latch would bring the wedge back.
            ok &= Expect(Patch_JobInterception.Enabled == enabledBefore,
                         "Enabled is left alone");

            Patch_JobInterception.FaultLimit = limitBefore;
            return ok;
        }
    }
}
#endif
