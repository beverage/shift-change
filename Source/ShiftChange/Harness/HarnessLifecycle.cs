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
    /// <para>Split out of <see cref="DebugTools_LifecycleHarness"/> on
    /// 2026-09-15. These are separate TYPES rather than partials on purpose: a
    /// decompiler merges partials back into one class, so partials would have
    /// left the shipped dll reading exactly as it did before. The registration
    /// list that decides case ORDER stays in
    /// <see cref="DebugTools_LifecycleHarness.Run"/> and must not be
    /// scattered.</para>
    /// </summary>
    internal static class HarnessLifecycle
    {
        /// <summary>
        /// The gravship case, and the reason the harness exists.
        ///
        /// A launch despawns everything aboard with
        /// <c>DestroyMode.WillReplace</c> (<c>GravshipUtility.cs:389,397</c>)
        /// and <c>Building_OutfitStand.DeSpawn</c> deliberately KEEPS its
        /// contents in that mode (<c>:392</c>), so the stand, the parked
        /// civvies and the borrower all survive the flight. The ledger must
        /// survive with them or the pawn lands in a uniform with no way out.
        /// </summary>
        internal static bool GravshipFlight(Fixture fix)
        {
            IntVec3 standCell = fix.Stand.Position;
            IntVec3 pawnCell = fix.Pawn.Position;

            fix.Stand.DeSpawn(DestroyMode.WillReplace);
            fix.Pawn.DeSpawn(DestroyMode.WillReplace);
            GenSpawn.Spawn(fix.Stand, standCell, fix.Map, Rot4.North);
            GenSpawn.Spawn(fix.Pawn, pawnCell, fix.Map, Rot4.North);
            fix.Stand.PostSwapMap();
            fix.Pawn.PostSwapMap();

            return Expect(fix.Comp.OnShift, "stand still reads on-shift")
                 & Expect(fix.Comp.Borrower == fix.Pawn, "borrower survived the flight")
                 & Expect(fix.Comp.StoredOwnerApparelForReading.Count == fix.StoredCount,
                          "parked civvies still in the ledger")
                 & Expect(CompShiftStand.OnShiftStandFor(fix.Pawn) == fix.Comp,
                          "registry points back at the stand (Change back works)");
        }

        /// <summary>
        /// The other half of the flight: the stand came, the borrower did not.
        /// Nobody can walk back for their clothes, so the ledger has to go —
        /// and critically the FORCED flags have to come off with it, or the
        /// pawn is pinned into the uniform with every route out closed.
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
        /// by calling the counter directly — the same rule as every other case
        /// here. A test that pokes <c>NoteFault</c> would prove the arithmetic
        /// and nothing about whether an exception in interception reaches it.
        ///
        /// Three claims, and the third is the bug this was written for. Until
        /// 2026-08-14 a single throw disabled the mod for the whole PROCESS —
        /// through a save load, a new colony, everything, until RimWorld was
        /// restarted — and said nothing to the player, because
        /// <c>Log.Error</c> does not open the log window outside dev mode.
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
