// HARNESS only: dev tooling, compiled out of a plain Release build (the
// configuration table is in ShiftChange.csproj). The guard is whole-file,
// always, and check-invariants.py enforces it; why: docs/DESIGN.md,
// "Development tooling".
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
    /// Harness cases for the engine lifecycle events a ledger has to
    /// survive: gravship flight, teardown, reinstall, death, banishment,
    /// and the fault latch that disables interception after repeated throws.
    ///
    /// <para>Case ORDER is decided in one place,
    /// <see cref="DebugTools_LifecycleHarness.Run"/>.</para>
    /// </summary>
    internal static class HarnessLifecycle
    {
        /// <summary>
        /// The gravship case, and the reason the harness exists. The stand,
        /// the parked civvies and the borrower all survive a launch's
        /// <c>WillReplace</c> despawn; the ledger must survive with them, or
        /// the pawn lands in a uniform with no way out. Why the stand keeps
        /// its contents in that mode: <see cref="CompShiftStand.PostDeSpawn"/>.
        ///
        /// <para>Nothing can touch a pawn's gear during the flight itself: the
        /// gravship in flight is a world object with no gear tab. So the shift
        /// picks up on landing exactly where it stood: the next meal break
        /// sends the borrower to the stand on its own, and the return leg ends
        /// the shift as it would have without the flight.</para>
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
        /// A flight from a map with a grav anchor, which the takeoff keeps
        /// rather than abandons (<c>WorldComponent_GravshipController.TakeoffEnded</c>):
        /// the stand flies, and the borrower stays behind, spawned, on a map
        /// that is still there.
        ///
        /// <para>Decided 2026-10-16: the shift stays suspended while they are
        /// apart, the uniform forced and the wardrobe optimizer paused, since a
        /// player's mistake or an emergency split them and putting things
        /// right is the player's to do. Once they share a map again it resumes
        /// on its own, and the return leg ends it normally. The pause keys on
        /// the registry entry, which is what is asserted here; the optimizer
        /// itself is not run.</para>
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
        /// The mirror of <see cref="StandFliesWithoutItsBorrower"/>: the
        /// borrower flies and the stand stays on the anchored map. The stand
        /// is never told, since it never moved and nothing of ours hears a
        /// pawn's <c>PostSwapMap</c>. Suspended while they are apart and
        /// resumed once the borrower flies back, as decided there.
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
        /// The borrower flies, and the stand stays on a map with no grav
        /// anchor, which the takeoff abandons (<c>GravshipUtility.AbandonMap</c>).
        /// Map removal never despawns a building: <c>Thing.Notify_MyMapRemoved</c>
        /// marks it discarded, so <see cref="CompShiftStand.PostDeSpawn"/>
        /// never runs and nothing releases the ledger. The parked clothes are
        /// gone with the map and the shift cannot end. What must not outlive
        /// it is the forced flag on the uniform, which then pins the borrower
        /// into it with no route out (see <see cref="CompShiftStand.ReleaseBorrower"/>).
        ///
        /// <para>Built on the second map (<see cref="BuildOnSecondMap"/>); the
        /// borrower flies to the harness's own, and the second map is removed
        /// by the same <c>Game.DeinitAndRemoveMap</c> an abandoned map
        /// reaches.</para>
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
        /// <see cref="StandLostWithItsMap"/>, with the stand in a box: minified
        /// mid-shift, and the box left on the map that is removed. Removal
        /// reaches things inside containers (<c>Map.GetChildHolders</c>), so the
        /// boxed stand hears it too. The minify already took the forced flag off
        /// (<see cref="CompShiftStand.PostDeSpawn"/>); what this asserts is the
        /// ledger released with the box rather than left naming the borrower.
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
        /// Every stand def carrying our comp asks to hear when its map is
        /// removed (<see cref="CompProperties_ShiftStand.ResolveReferences"/>).
        /// A def fact rather than a lifecycle one: without the flag the engine
        /// never calls <see cref="CompShiftStand.Notify_MapRemoved"/>, and
        /// <see cref="StandLostWithItsMap"/> covers only the vanilla stand. Under
        /// <c>--with=khamenman.outfitstandsplus</c> this reaches that mod's
        /// stands as well.
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
        /// One thing's gravship flight, in the engine's order:
        /// <c>PreSwapMap</c> and a <c>WillReplace</c> despawn at takeoff
        /// (<c>GravshipUtility.GenerateGravship</c>), <c>GenSpawn.Spawn</c> on
        /// the landing map, then <c>PostSwapMap</c>
        /// (<c>GravshipPlacementUtility.PostSwapMap</c>). For a flight that
        /// carries one of the pair; <see cref="GravshipFlight"/> interleaves
        /// the two, as the engine does when both are aboard.
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
        /// The return leg, run for real at the stand, and what it has to
        /// leave: the uniform back on the stand and every parked garment back
        /// on the borrower. A shift that ends as it would have without the
        /// flight is the behaviour; the ledger checks before it are state.
        /// </summary>
        internal static bool ShiftEndsNormally(Fixture fix, Apparel uniform, List<Apparel> parked)
        {
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
        /// Whether a meal break would send the borrower to the stand right now:
        /// the automatic return trip, asked of the real interception and left
        /// with no trace (<see cref="Probe"/>). A meal on the ground is a
        /// sit-down break, which changes them out first wherever they eat
        /// (<c>MealBreakChangesOut</c>), so the one thing this asks of a flight
        /// is whether the stand is within reach. The probe clears the retry
        /// cooldown a refusal stamps; in play that cooldown delays the first
        /// change back after a reunion by up to
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
        /// The other half of the flight: the stand came, the borrower did not,
        /// left on a map the takeoff abandoned. Nobody can walk back for their
        /// clothes, so the ledger has to go — and critically the FORCED flags
        /// have to come off with it, or the pawn is pinned into the uniform
        /// with every route out closed.
        /// </summary>
        internal static bool GravshipFlightLeftBehind(Fixture fix)
        {
            IntVec3 standCell = fix.Stand.Position;
            Apparel uniform = fix.Comp.IssuedUniformForReading.Count > 0
                ? fix.Comp.IssuedUniformForReading[0]
                : null;

            // The stand flies; the borrower stays on the old map. Despawning
            // the pawn without respawning them is what "left behind" looks
            // like from the stand's side.
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
        /// Deconstruct or burn down. Unlike the flight the stand really is
        /// gone, and unlike a minify it DROPS its container on the way out
        /// (<c>Building_OutfitStand.cs:392</c>), so the civvies are already on
        /// the floor, the ledger cannot be honoured, and the forced flags must
        /// come off.
        ///
        /// <para>Uses <c>Deconstruct</c> rather than <c>Vanish</c>, and the
        /// distinction is the whole point: <c>Vanish</c> is the minify, which
        /// keeps its contents and now keeps its ledger with them. Testing the
        /// release path through <c>Vanish</c> asserted the opposite of what
        /// ships.</para>
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
        /// The case this whole branch exists for, reported from play
        /// 2026-08-23: move a stand while somebody has its uniform out, and
        /// they must still be able to hand it back afterwards.
        ///
        /// <para>A minify is a <c>Vanish</c>, which KEEPS the container
        /// (<c>Building_OutfitStand.cs:390-397</c>), so the borrower's civvies
        /// travel inside the box. The ledger has to travel with them or the
        /// stand lands reading those civvies as its own kit — the pawn is
        /// stuck in the uniform and their own clothes become stand stock.
        /// Releasing here shipped once and did exactly that; ejecting the
        /// civvies to the floor instead shipped once too and was the same
        /// permanent swap with the clothes underfoot.</para>
        ///
        /// <para>The forced flag is the one thing that must NOT survive the
        /// boxed window, so it is asserted OFF while boxed and ON again on
        /// landing.</para>
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

            // The proof that matters: the return trip actually runs and the
            // pawn gets their own clothes back. Everything above is state; this
            // is the behaviour the report was about.
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
        /// Relocating a stand — the Reinstall designator — is a minify and a
        /// respawn of the SAME thing, so everything the player configured has
        /// to be on the other side of it.
        ///
        /// <para>Owners are the half that was actually broken: the base comp
        /// parks them on despawn and offers them back on spawn, but only past
        /// <c>CanSetUninstallAssignedPawn</c>, whose base answer is
        /// <c>false</c> (<c>CompAssignableToPawn.cs:214,233-236</c>) — and it
        /// clears the parked list either way, so a move silently unowned the
        /// stand. The mode and flag halves ride on comp scribing and
        /// <c>PostSpawnSetup</c> and are asserted here as regression cover,
        /// not because they were failing.</para>
        ///
        /// <para>Minifies through <c>MakeMinified</c> rather than a bare
        /// <c>DeSpawn</c>, because the wrap is what the real path does and it
        /// is the step that would notice a stand left holding a live
        /// reference. The re-spawn is <c>Blueprint_Install.MakeSolidThing</c>'s
        /// shape: drop the inner thing out of the box, spawn it, bin the
        /// box.</para>
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
            // Staged LAST: ToggleWork is an entry into service and clears it.
            // Setting it before would assert nothing about the landing.
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
        /// An owner who dies while their stand is boxed must not own it again when it is set down.
        /// Death reaches the reaper, but the reaper scans spawned stands only, so the boxed
        /// stand's parked owner list keeps the pawn; the base comp then offers each parked owner
        /// back through <c>CanSetUninstallAssignedPawn</c>, which tests <c>IsColonist</c>, and a
        /// dead colonist keeps the player faction. Minified and set down as
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
                & ExpectKnownGap(!assignable.AssignedPawnsForReading.Contains(fix.Pawn),
                                 "the dead colonist does not own the reinstalled stand",
                                 "CanSetUninstallAssignedPawn passes a dead colonist");

            assignable.TryUnassignPawn(fix.Pawn);
            return ok;
        }

        /// <summary>
        /// Death runs <c>Pawn.Kill</c> → <c>Pawn.Destroy(KillFinalize)</c> →
        /// <c>Pawn_Ownership.UnclaimAll</c> (<c>Pawn.cs:2350-2352</c>), which
        /// <see cref="Patch_UnclaimStands"/> hooks. Cheap to arrange in play,
        /// but it is the case the whole reaper story rests on, so it is worth
        /// having a regression marker for it.
        /// </summary>
        internal static bool DeathReaps(Fixture fix)
        {
            fix.Pawn.Kill(null);
            return Expect(!fix.Comp.OnShift, "ledger reaped on death")
                 & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == null,
                          "registry entry cleared");
        }

        /// <summary>
        /// The exit vanilla does not route through <c>UnclaimAll</c>, caught
        /// by <see cref="Patch_BanishStands"/> instead.
        ///
        /// Asserted to the same depth as <see cref="DeathReaps"/>, plus the
        /// forced flag — which matters here and not there, because banishment
        /// leaves the pawn ALIVE and standing on the map. A ledger cleared
        /// without clearing forced pins them into the uniform with every route
        /// out already closed.
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
            // The reap must survive the pawn coming back. A liveness-only fix
            // passes everything above and then fails this, because the ledger
            // was never emptied — it was only being disbelieved.
            fix.Pawn.SetFaction(Faction.OfPlayer);
            ok &= Expect(!fix.Comp.OnShift, "still reaped after re-recruitment");
            return ok;
        }

        /// <summary>
        /// The fault latch, driven by real throws through the real
        /// <see cref="Patch_JobInterception.Prefix"/> catch block rather than
        /// by calling the counter directly (why:
        /// <see cref="Patch_JobInterception.injectFaults"/>).
        ///
        /// Three claims: throws below the limit are counted and the mod keeps
        /// serving, the limit latches it off, and a game change re-arms it. The
        /// third is the bug this was written for
        /// (<see cref="Patch_JobInterception.faulted"/>).
        ///
        /// The one thing this cannot do is load an actual save; it changes the
        /// game reference under <see cref="SessionGuard"/> instead, which is
        /// the same trigger a load pulls.
        /// </summary>
        internal static bool FaultLatchRecovers(Fixture fix)
        {
            int limitBefore = Patch_JobInterception.FaultLimit;
            bool enabledBefore = Patch_JobInterception.Enabled;
            Patch_JobInterception.ResetSessionState();
            Patch_JobInterception.FaultLimit = 3;

            bool ok = Expect(!Patch_JobInterception.faulted, "starts armed");

            // Below the limit: counted, still serving. These catch blocks also
            // fire for a NEIGHBOUR's exception thrown through our frame, so
            // one throw must not take the mod out for the rest of the colony.
            Fault(fix.Pawn, 2);
            ok &= Expect(Patch_JobInterception.faultCount == 2, "throws are counted")
                & Expect(!Patch_JobInterception.faulted, "still armed below the limit");

            Fault(fix.Pawn, 1);
            ok &= Expect(Patch_JobInterception.faulted, "latched at the limit");

            // What a save load does. This is the whole fix: before it, the
            // only way back was quitting the game.
            SessionGuard.current = null;
            SessionGuard.Ensure();
            ok &= Expect(!Patch_JobInterception.faulted, "a game change re-arms it")
                & Expect(Patch_JobInterception.faultCount == 0, "the count resets with it");

            // Enabled is a STANDING decision — the player's, or the hot-reload
            // quarantine's — and the quarantine has to survive a load because
            // the wedging twin JobDriver is still loaded. Re-arming it here
            // would resurrect the 2026-08-08 tracker wedge.
            ok &= Expect(Patch_JobInterception.Enabled == enabledBefore,
                         "Enabled is left alone");

            Patch_JobInterception.FaultLimit = limitBefore;
            return ok;
        }
    }
}
#endif
