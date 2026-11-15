using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DevTools.Testing;
using HarmonyLib;
using RimWorld;
using Verse;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.SceneKit;
using static ShiftChange.Tests.LifecycleKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// What happens to a shift when the stand or the colonist goes: torn down, reinstalled, lost with a map, flown off, dead, kidnapped or banished.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.LosingAStandOrAColonist)]
    internal class LosingAStandOrAColonist : StoryFixture
    {
        /// <summary>
        /// What <see cref="SceneKit.Build"/> sets up: a colonist in a tunic, duster and
        /// helmet, put on shift the way the change itself does it. The stand holds Vanilla Apparel
        /// Expanded's lab coat, which displaces the duster, or without that mod a shirt and
        /// trousers, which displace the tunic.
        /// </summary>
        internal const string OnShift =
            "a colonist on shift at a stand: changed into its outfit, which they wear forced, "
            + "and the clothes it displaced kept on the stand";

        /// <summary>The same, on a second map (<see cref="SceneKit.BuildOnSecondMap"/>).</summary>
        internal const string OnShiftOnASecondMap = "on a second map, " + OnShift;

        /// <summary>
        /// Deconstruct or burn down. The stand is really gone, and unlike a flight
        /// (<c>WillReplace</c>) or an uninstall (<c>Vanish</c>) a <c>Deconstruct</c> despawn makes
        /// <c>Building_OutfitStand</c> drop what it holds, so the colonist's own clothes are
        /// already on the floor, the shift cannot be ended at the stand, and the forced flags must
        /// come off. The test uses <c>Deconstruct</c> because <c>Vanish</c>, the uninstall, keeps
        /// the shift: testing the release through it would assert the opposite of what ships.
        /// </summary>
        [Test]
        [TestDescription("a stand deconstructed or burned down under a colonist's shift lets them go, "
                         + "its outfit no longer forced on them")]
        internal IEnumerator DeconstructedStandLetsTheColonistGo()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                Apparel uniform = FirstIssued(scene);

                scene.OutfitStand.DeSpawn(DestroyMode.Deconstruct);
                story.Note("the stand is deconstructed, dropping what it held");

                story.Check(!scene.Comp.OnShift, "the stand's shift is over");
                story.Check(scene.Comp.Borrower == null, "it lends its outfit to nobody");
                story.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == null,
                            "and no stand still has " + name + " on shift");
                if (uniform != null && scene.Colonist.outfits != null)
                {
                    story.Check(!scene.Colonist.outfits.forcedHandler.IsForced(uniform),
                                "the " + uniform.def.label + " " + name + " has on is no longer forced");
                }
            }
        }

        /// <summary>
        /// Every kind of stand carrying Shift Change's comp asks to hear when its map is removed
        /// (<see cref="CompProperties_ShiftStand.ResolveReferences"/>). Without
        /// <c>notifyMapRemoved</c> the engine never calls
        /// <see cref="CompShiftStand.Notify_MapRemoved"/>, and the two map-loss tests cover only
        /// the vanilla stand; under <c>--with=khamenman.outfitstandsplus</c> this reaches that
        /// mod's stands too.
        /// </summary>
        [Test]
        [TestDescription("every kind of outfit stand Shift Change serves is told when its map goes away")]
        internal IEnumerator EveryStandHearsItsMapGo()
        {
            return Unstaged(Body);

            void Body(Story story)
            {
                List<ThingDef> defs = Patch_JobInterception.StandDefs;
                story.Check(defs.Count > 0, "Shift Change knows the outfit stands it serves: "
                                            + (defs.Count == 0 ? "none" : string.Join(", ", defs.Select(d => d.label))));
                for (int i = 0; i < defs.Count; i++)
                {
                    story.Check(defs[i].notifyMapRemoved,
                                "the " + defs[i].label + " (" + defs[i].defName
                                + ") asks to be told when its map goes away");
                }
            }
        }

        /// <summary>
        /// Moving a stand with Reinstall is an uninstall and a set-down of the same thing, so
        /// everything the player configured must come out the other side: owner, work types and
        /// Change the whole outfit. Allow removing items, switched on beforehand, must be held off
        /// again on landing, as it is for any stand in service.
        ///
        /// <para>Owners are the half that was broken: the base comp parks them on despawn and
        /// offers them back on spawn, but only past <c>CanSetUninstallAssignedPawn</c>, whose base
        /// answer is false, and it clears the parked list either way, so a move silently unowned
        /// the stand. The other settings stay on the comp, which rides in the box unsaved, so this
        /// test proves no scribing; <c>PostSpawnSetup</c> holds the removal flag off again.</para>
        ///
        /// <para>The uninstall goes through <c>MakeMinified</c>, not a bare <c>DeSpawn</c>, because
        /// the wrap is what the real path does and would notice a stand left holding a live
        /// reference. The set-down has the shape of <c>Blueprint_Install.MakeSolidThing</c>: take
        /// the inner thing out of the box, destroy the box, then spawn the thing.</para>
        /// </summary>
        [Test]
        [TestDescription("a stand moved with Reinstall keeps its owner, its work types "
                         + "and Change the whole outfit")]
        internal IEnumerator ReinstalledStandKeepsItsSettings()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                CompAssignableToPawn assignable =
                    scene.OutfitStand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                if (assignable == null)
                {
                    story.Check(false, "the stand has Shift Change's owner control");
                    return;
                }

                AccessTools.FieldRef<Building_OutfitStand, bool> flag =
                    Patch_AllowRemovingToggle.AllowRemovingItemsRef;
                WorkTypeDef work = WorkType("Doctor");
                if (flag == null || work == null)
                {
                    story.Check(false, "the stand's Allow removing items switch and the Doctor work type "
                                       + "can both be found");
                    return;
                }

                assignable.TryAssignPawn(scene.Colonist);
                story.Note(name + " is made the stand's owner");
                scene.Comp.ToggleWork(work);
                story.Note(work.labelShort + " is ticked among its work types");
                scene.Comp.SetFullChange(true);
                story.Note("Change the whole outfit is ticked");
                // Set last: ticking a work type puts the stand in service, which switches it off,
                // so switching it on earlier would leave nothing to check on landing.
                flag(scene.OutfitStand) = true;
                story.Note("Allow removing items is switched on");

                Map map = scene.Map;
                IntVec3 from = scene.OutfitStand.Position;
                IntVec3 to = new IntVec3(from.x, 0, from.z + 2);
                Rot4 rot = scene.OutfitStand.Rotation;

                MinifiedThing box = scene.OutfitStand.MakeMinified();
                if (box == null)
                {
                    story.Check(false, "the stand can be uninstalled");
                    return;
                }
                story.Check(!scene.OutfitStand.Spawned, "uninstalled, the stand is off the map");

                box.InnerThing = null;
                box.Destroy();
                GenSpawn.Spawn(scene.OutfitStand, to, map, rot);
                story.Note("it is set down again two cells north");

                story.Check(scene.OutfitStand.Spawned, "the stand is back on the map");
                story.Check(scene.OutfitStand.Position == to, "in its new spot");
                story.Check(assignable.AssignedPawnsForReading.Contains(scene.Colonist), name + " still owns it");
                story.Check(scene.Comp.WorkTypes.Contains(work), work.labelShort + " is still ticked");
                story.Check(scene.Comp.FullChange, "Change the whole outfit is still ticked");
                story.Check(!flag(scene.OutfitStand),
                            "and Allow removing items is held off again, as for any stand in service");
            }
        }

        /// <summary>
        /// An owner who dies while their stand is uninstalled must not own it again when it is
        /// set down. Guards a past bug. Death reaches the reaper, but the reaper scans spawned
        /// stands only, so the uninstalled stand's parked owner list keeps the colonist; the base
        /// comp then offers each parked owner back through <c>CanSetUninstallAssignedPawn</c>, and
        /// a dead colonist still passes <c>IsColonist</c>. Uninstalled and set down as
        /// <see cref="ReinstalledStandKeepsItsSettings"/> does it.
        /// </summary>
        [Test]
        [TestDescription("a colonist who dies while their stand is uninstalled does not own it "
                         + "when it is set down again")]
        internal IEnumerator DeadOwnerDoesNotGetTheStandBack()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                CompAssignableToPawn assignable =
                    scene.OutfitStand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                if (assignable == null)
                {
                    story.Check(false, "the stand has Shift Change's owner control");
                    return;
                }

                assignable.TryAssignPawn(scene.Colonist);
                story.Check(assignable.AssignedPawnsForReading.Contains(scene.Colonist), name + " owns the stand");

                Map map = scene.Map;
                IntVec3 from = scene.OutfitStand.Position;
                IntVec3 to = new IntVec3(from.x, 0, from.z + 2);
                Rot4 rot = scene.OutfitStand.Rotation;

                MinifiedThing box = scene.OutfitStand.MakeMinified();
                if (box == null)
                {
                    story.Check(false, "the stand can be uninstalled");
                    return;
                }
                story.Check(assignable.AssignedPawnsForReading.Count == 0,
                            "uninstalled, it sets its owner aside, its owner list empty");

                scene.Colonist.Kill(null);
                story.Check(scene.Colonist.Dead, name + " dies while the stand is uninstalled");
                story.Check(scene.Colonist.IsColonist, "and the game still counts them a colonist, dead");

                box.InnerThing = null;
                box.Destroy();
                GenSpawn.Spawn(scene.OutfitStand, to, map, rot);

                story.Check(scene.OutfitStand.Spawned, "the stand is set down again");
                story.Check(!assignable.AssignedPawnsForReading.Contains(scene.Colonist),
                            "and the dead " + name + " does not own it");

                assignable.TryUnassignPawn(scene.Colonist);
            }
        }

        /// <summary>
        /// A colonist kidnapped while their stand is uninstalled neither owns it nor keeps their
        /// shift at it when it is set down, as they would not had it stood on a map. Kidnapping
        /// reaches the reaper, which cannot find an uninstalled stand, and a kidnapped colonist
        /// keeps the player's faction, so <c>IsColonist</c> and
        /// <see cref="CompShiftStand.StillOurs"/> both pass. The colonist on shift is the owner;
        /// the kidnapping is undone before the scene is cleared. Uninstalled and set down as
        /// <see cref="ReinstalledStandKeepsItsSettings"/> does it.
        /// </summary>
        [Test]
        [TestDescription("a colonist kidnapped while their stand is uninstalled neither owns it "
                         + "nor is still on shift at it when it is set down again")]
        internal IEnumerator KidnappedOwnerDoesNotGetTheStandBack()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                CompAssignableToPawn assignable =
                    scene.OutfitStand.TryGetComp<CompAssignableToPawn_ShiftStand>();
                Faction kidnappers = Find.FactionManager.AllFactionsListForReading
                    .FirstOrDefault(f => !f.IsPlayer && f.def.humanlikeFaction);
                if (assignable == null || kidnappers == null)
                {
                    story.Check(false, "the stand has Shift Change's owner control, and there is a faction "
                                       + "to kidnap with");
                    return;
                }

                assignable.TryAssignPawn(scene.Colonist);
                Apparel uniform = scene.Comp.IssuedUniformForReading.FirstOrDefault();
                story.Check(assignable.AssignedPawnsForReading.Contains(scene.Colonist), name + " owns the stand");
                story.Check(scene.Comp.OnShift && scene.Comp.Borrower == scene.Colonist, "and is on shift at it");
                story.Check(uniform != null, "wearing its outfit");

                Map map = scene.Map;
                IntVec3 from = scene.OutfitStand.Position;
                IntVec3 to = new IntVec3(from.x, 0, from.z + 2);
                Rot4 rot = scene.OutfitStand.Rotation;

                MinifiedThing box = scene.OutfitStand.MakeMinified();
                if (box == null)
                {
                    story.Check(false, "the stand can be uninstalled");
                    return;
                }

                kidnappers.kidnapped.Kidnap(scene.Colonist, null);
                try
                {
                    story.Check(scene.Colonist.IsKidnapped(), name + " is kidnapped by " + kidnappers.Name
                                                        + " while the stand is uninstalled");
                    story.Check(scene.Colonist.IsColonist, "and the game still counts them a colonist");

                    box.InnerThing = null;
                    box.Destroy();
                    GenSpawn.Spawn(scene.OutfitStand, to, map, rot);

                    story.Check(scene.OutfitStand.Spawned, "the stand is set down again");
                    story.Check(!assignable.AssignedPawnsForReading.Contains(scene.Colonist),
                                "and the kidnapped " + name + " does not own it");
                    story.Check(!scene.Comp.OnShift, "nor is still on shift at it");
                    story.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == null,
                                "so no stand still has " + name + " on shift");
                    if (uniform != null && scene.Colonist.outfits != null)
                    {
                        story.Check(!scene.Colonist.outfits.forcedHandler.IsForced(uniform),
                                    "the " + uniform.def.label + " " + name
                                    + " was taken in is no longer forced: it is ordinary clothing");
                    }
                }
                finally
                {
                    kidnappers.kidnapped.RemoveKidnappedPawn(scene.Colonist);
                }

                assignable.TryUnassignPawn(scene.Colonist);
            }
        }

        /// <summary>
        /// Death runs <c>Pawn.Kill</c>, then <c>Pawn.Destroy(KillFinalize)</c>, then
        /// <c>Pawn_Ownership.UnclaimAll</c>, which <see cref="Patch_UnclaimStands"/> hooks. The
        /// shift must be ended, and the colonist no longer on shift anywhere, on that path.
        /// </summary>
        [Test]
        [TestDescription("a colonist who dies in a stand's outfit is no longer on shift at it")]
        internal IEnumerator DeathEndsTheShift()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                scene.Colonist.Kill(null);
                story.Note(name + " dies in the stand's outfit");
                story.Check(!scene.Comp.OnShift, "the stand's shift is over");
                story.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == null,
                            "and no stand still has " + name + " on shift");
            }
        }

        /// <summary>
        /// The exit vanilla does not route through <c>UnclaimAll</c>: on a spawned colonist
        /// <c>PawnBanishUtility.Banish</c> clears guest status and sets the faction to null, and
        /// reaches no unclaim, so <see cref="Patch_BanishStands"/> calls the reaper itself.
        /// Checked as deeply as <see cref="DeathEndsTheShift"/>, plus the forced flag, which
        /// matters here because a banished colonist stays alive on the map: a shift ended without
        /// it pins them into the outfit with every route out closed.
        /// </summary>
        [Test]
        [TestDescription("a colonist banished in a stand's outfit is no longer on shift at it, "
                         + "nor again on rejoining the colony")]
        internal IEnumerator BanishmentEndsTheShift()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                Apparel uniform = FirstIssued(scene);

                PawnBanishUtility.Banish(scene.Colonist, giveThoughts: false);
                story.Note(name + " is banished in the stand's outfit");

                story.Check(!scene.Comp.OnShift, "the stand's shift is over");
                story.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == null,
                            "and no stand still has " + name + " on shift");
                if (uniform != null && scene.Colonist.outfits != null)
                {
                    story.Check(!scene.Colonist.outfits.forcedHandler.IsForced(uniform),
                                "the " + uniform.def.label + " " + name + " leaves in is no longer forced");
                }
                // The shift must stay over when the colonist is recruited back. A fix that only
                // disbelieved a departed colonist's shift would pass everything above and fail
                // here, since the stand's record was never emptied.
                scene.Colonist.SetFaction(Faction.OfPlayer);
                story.Note(name + " rejoins the colony");
                story.Check(!scene.Comp.OnShift, "and the stand's shift is still over");
            }
        }

        /// <summary>
        /// Shift Change stopping after repeated errors, driven by real throws through the catch
        /// block of the real <see cref="Patch_JobInterception.Prefix"/> rather than by calling the
        /// counter (<see cref="SceneKit.Fault"/>). Three claims: errors below the limit are counted
        /// and the mod keeps working, the limit stops it, and a new game starts it again
        /// (<see cref="Patch_JobInterception.faulted"/>). The third guards a past bug in which only
        /// quitting the game started it again. A save cannot be loaded here, so the test resets the
        /// game reference under <see cref="SessionGuard"/>, the trigger a load pulls.
        ///
        /// <para>The three errors are made on purpose. Each logs an error, and the third also logs
        /// the stop. Only those are allowed: a throw of any other kind logs its own error, which
        /// fails the test.</para>
        /// </summary>
        [Test]
        [TestDescription("Shift Change stops changing colonists after repeated errors, "
                         + "and starts again in the next game")]
        [ErrorsAllowed(@"\[ShiftChange\] injected test fault",
                       @"\[ShiftChange\] interception disabled for this game after \d+ faults")]
        internal IEnumerator RepeatedErrorsStopShiftChangeUntilTheNextGame()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                int limitBefore = Patch_JobInterception.FaultLimit;
                bool enabledBefore = Patch_JobInterception.Enabled;
                Patch_JobInterception.ResetSessionState();
                Patch_JobInterception.FaultLimit = 3;
                story.Note("Shift Change's error count starts from nothing, with a limit of three");

                story.Check(!Patch_JobInterception.faulted, "Shift Change is changing colonists");

                // Below the limit: counted, still working. The catch also sees other mods'
                // exceptions thrown through its frame, so one throw must not stop the mod for the
                // colony.
                Fault(scene.Colonist, 2);
                story.Note("two errors are thrown as it looks at " + name + "'s next job");
                story.Check(Patch_JobInterception.faultCount == 2, "it counts both");
                story.Check(!Patch_JobInterception.faulted, "and keeps changing colonists below its limit");

                Fault(scene.Colonist, 1);
                story.Note("and a third");
                story.Check(Patch_JobInterception.faulted,
                            "at its limit it stops changing colonists for this game");

                // A new game, as loading a save makes one.
                SessionGuard.current = null;
                SessionGuard.Ensure();
                story.Note("the game changes, as it does when a save is loaded");
                story.Check(!Patch_JobInterception.faulted, "a new game starts it changing colonists again");
                story.Check(Patch_JobInterception.faultCount == 0, "its error count back at nothing");

                // Enabled is a standing decision, the player's or the hot-reload quarantine's, and
                // the quarantine must survive a load because the twin JobDriver that wedges a
                // tracker is still loaded. Turning it back on along with the latch would bring the
                // wedge back.
                story.Check(Patch_JobInterception.Enabled == enabledBefore,
                            "and its own off switch, a tweak value, is left as it was");

                Patch_JobInterception.FaultLimit = limitBefore;
            }
        }

        /// <summary>
        /// The stand flies and its colonist is left on a map the takeoff abandoned, so nobody can
        /// walk back for their clothes. The shift must end, and the forced flags with it, or the
        /// colonist is pinned into the outfit with every route out closed.
        /// </summary>
        [Test]
        [TestDescription("a colonist left behind when their stand flies off in a gravship is let go, "
                         + "its outfit no longer forced on them")]
        internal IEnumerator LeftBehindByTheGravship()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                IntVec3 standCell = scene.OutfitStand.Position;
                Apparel uniform = FirstIssued(scene);

                // A despawn with no respawn stands in for a colonist left on an abandoned map: on
                // no map, which is what the stand's PostSwapMap checks for.
                scene.OutfitStand.DeSpawn(DestroyMode.WillReplace);
                scene.Colonist.DeSpawn(DestroyMode.Vanish);
                GenSpawn.Spawn(scene.OutfitStand, standCell, scene.Map, Rot4.North);
                scene.OutfitStand.PostSwapMap();
                story.Note("the stand flies off in a gravship and lands; " + name
                           + " is left behind on a map the gravship abandoned");

                story.Check(!scene.Comp.OnShift, "the stand's shift is over, with " + name + " gone");
                story.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == null,
                            "and no stand still has " + name + " on shift");
                if (uniform != null && scene.Colonist.outfits != null)
                {
                    story.Check(!scene.Colonist.outfits.forcedHandler.IsForced(uniform),
                                "the " + uniform.def.label + " " + name + " has on is no longer forced");
                }
            }
        }

        /// <summary>
        /// The colonist flies and the stand stays on a map with no grav anchor, which the takeoff
        /// abandons (<c>GravshipUtility.AbandonMap</c>). Map removal never despawns a building:
        /// <c>Thing.Notify_MyMapRemoved</c> marks it discarded, so
        /// <see cref="CompShiftStand.PostDeSpawn"/> never runs and
        /// <see cref="CompShiftStand.Notify_MapRemoved"/> is the stand's only notice. The clothes
        /// kept on the stand go with the map; the test checks the stand discarded, its shift
        /// ended and the colonist no longer on shift anywhere, and the outfit still worn but no
        /// longer forced, since a forced flag would pin the colonist into it
        /// (<see cref="CompShiftStand.ReleaseBorrower"/>). Set up on a second map, and that map
        /// removed through the same <c>Game.DeinitAndRemoveMap</c> an abandoned map reaches.
        /// </summary>
        [Test]
        [TestDescription("a stand left on a map that is then abandoned stops forcing its outfit "
                         + "on the colonist who flew off in it")]
        internal IEnumerator StandLeftOnAnAbandonedMap()
        {
            return Staged(BuildOnSecondMap, OnShiftOnASecondMap, Body);

            // The scene is on a second map, so the colonist flies to the test's own map, onto its pad.
            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                Apparel uniform = FirstIssued(scene);
                if (uniform == null)
                {
                    story.Check(false, name + " is on shift, wearing the stand's outfit");
                    return;
                }
                Map origin = scene.SecondMap;

                Fly(scene.Colonist, Map, new IntVec3(Pad.minX + 3, 0, Pad.minZ + 1));
                story.Check(scene.Colonist.Map == Map, name + " flies home in a gravship, leaving the stand behind");
                story.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == scene.Comp,
                            "and is still on shift at the stand left behind");

                RemoveSecondMap(origin);
                scene.SecondMap = null;
                story.Note("the map the stand was left on is abandoned");
                story.Check(!Find.Maps.Contains(origin), "that map is gone");
                story.Check(scene.OutfitStand.Discarded, "and the stand with it, discarded rather than taken down");
                story.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == null,
                            "no stand still has " + name + " on shift");
                story.Check(scene.Colonist.apparel.WornApparel.Contains(uniform),
                            "the " + uniform.def.label + " stays on, now " + name + "'s own clothing");
                if (scene.Colonist.outfits != null)
                {
                    story.Check(!scene.Colonist.outfits.forcedHandler.IsForced(uniform),
                                "and is no longer forced, so their outfit policy can replace it");
                }
                story.Check(!scene.Comp.OnShift && scene.Comp.Borrower == null,
                            "the lost stand ended its own shift too, rather than merely being forgotten");
            }
        }

        /// <summary>
        /// <see cref="StandLeftOnAnAbandonedMap"/> with the stand uninstalled mid-shift and left
        /// on the map that is removed. Map removal reaches things inside containers
        /// (<c>Map.GetChildHolders</c>), so the uninstalled stand hears it too. The uninstall
        /// already took the forced flag off; this checks the shift is ended with it.
        /// </summary>
        [Test]
        [TestDescription("an uninstalled stand lost with its map lets its colonist go too")]
        internal IEnumerator UninstalledStandLostWithItsMap()
        {
            return Staged(BuildOnSecondMap, OnShiftOnASecondMap, Body);

            // The scene is on a second map, so the colonist flies to the test's own map, onto its pad.
            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                Apparel uniform = FirstIssued(scene);
                if (uniform == null)
                {
                    story.Check(false, name + " is on shift, wearing the stand's outfit");
                    return;
                }
                Map origin = scene.SecondMap;
                IntVec3 boxCell = scene.OutfitStand.Position;
                MinifiedThing box = scene.OutfitStand.MakeMinified();
                if (box == null)
                {
                    story.Check(false, "the stand can be uninstalled");
                    return;
                }
                GenSpawn.Spawn(box, boxCell, origin);
                story.Note("the stand is uninstalled where it stood, " + name + " still in its outfit");

                Fly(scene.Colonist, Map, new IntVec3(Pad.minX + 3, 0, Pad.minZ + 1));
                story.Note(name + " flies home in a gravship, leaving the uninstalled stand behind");
                story.Check(box.Spawned && box.Map == origin,
                            "the uninstalled stand lies on the map that will be abandoned");
                story.Check(scene.Comp.OnShift && scene.Comp.Borrower == scene.Colonist,
                            "and still has " + name + " on shift");

                RemoveSecondMap(origin);
                scene.SecondMap = null;
                story.Check(box.Discarded && scene.OutfitStand.Discarded,
                            "the map is abandoned, and the uninstalled stand goes with it");
                story.Check(!scene.Comp.OnShift && scene.Comp.Borrower == null, "its shift ended with it");
                story.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == null,
                            "and no stand still has " + name + " on shift");
                if (scene.Colonist.outfits != null)
                {
                    story.Check(!scene.Colonist.outfits.forcedHandler.IsForced(uniform),
                                "the " + uniform.def.label + " is not forced on " + name);
                }
            }
        }

        /// <summary>
        /// Moving a stand with Reinstall while a colonist wears its outfit: they must still be
        /// able to change back afterwards. Guards a past bug, in which a move ended the shift and
        /// left the colonist stuck in the outfit.
        ///
        /// <para>An uninstall is a <c>Vanish</c> despawn and <c>Building_OutfitStand</c> keeps what
        /// it holds through one, so the colonist's own clothes travel inside the box. The shift
        /// has to travel with them, or the stand lands reading those clothes as its own outfit;
        /// dropped on the floor instead, they leave the colonist just as stuck in the outfit. The
        /// forced flag is the exception: it must not last while the stand is boxed, so the test
        /// checks it off while boxed and on again once the stand is set down. Uninstalled and set
        /// down as <see cref="ReinstalledStandKeepsItsSettings"/> does it.</para>
        /// </summary>
        [Test]
        [TestDescription("a stand moved with Reinstall in the middle of a shift keeps the "
                         + "colonist's own clothes, and changes them back as usual")]
        internal IEnumerator ReinstallingMidShiftKeepsTheirClothes()
        {
            return Staged(Build, OnShift, Body);

            void Body(Scene scene, Story story)
            {
                string name = scene.Colonist.LabelShort;
                Apparel uniform = FirstIssued(scene);
                List<Apparel> parked = new List<Apparel>(scene.Comp.StoredOwnerApparelForReading);
                if (uniform == null || parked.Count == 0)
                {
                    story.Check(false, name + " is on shift in the stand's outfit, "
                                       + "their own clothes kept on the stand");
                    return;
                }

                Map map = scene.Map;
                IntVec3 to = new IntVec3(scene.OutfitStand.Position.x, 0, scene.OutfitStand.Position.z + 2);
                Rot4 rot = scene.OutfitStand.Rotation;

                MinifiedThing box = scene.OutfitStand.MakeMinified();
                if (box == null)
                {
                    story.Check(false, "the stand can be uninstalled");
                    return;
                }
                story.Note("the stand is uninstalled, " + name + " still in its outfit");
                story.Check(scene.Comp.OnShift, "uninstalled, the stand keeps its shift");
                story.Check(scene.Comp.Borrower == scene.Colonist, "and still lends its outfit to " + name);
                for (int i = 0; i < parked.Count; i++)
                {
                    story.Check(scene.OutfitStand.HeldItems.Contains(parked[i]),
                                name + "'s " + parked[i].def.label
                                + " travels in the box with the stand");
                }
                if (scene.Colonist.outfits != null)
                {
                    story.Check(!scene.Colonist.outfits.forcedHandler.IsForced(uniform),
                                "while the stand is uninstalled, the " + uniform.def.label
                                + " is not forced on " + name);
                }

                box.InnerThing = null;
                box.Destroy();
                GenSpawn.Spawn(scene.OutfitStand, to, map, rot);
                story.Note("it is set down again two cells north");

                story.Check(scene.OutfitStand.Spawned, "the stand is back on the map");
                story.Check(scene.Comp.OnShift, "its shift goes on");
                story.Check(scene.Comp.Borrower == scene.Colonist, "still lending its outfit to " + name);
                story.Check(CompShiftStand.OnShiftStandFor(scene.Colonist) == scene.Comp,
                            name + " is still on shift at it, so their Change back button "
                            + "leads there");
                story.Check(scene.Comp.StoredOwnerApparelForReading.Count == parked.Count,
                            "and it still keeps all of " + name + "'s own clothes for them");
                if (scene.Colonist.outfits != null)
                {
                    story.Check(scene.Colonist.outfits.forcedHandler.IsForced(uniform),
                                "and the " + uniform.def.label + " is forced on " + name
                                + " again");
                }

                // The change back really runs and gives the colonist their own clothes back: the
                // behaviour, where everything above is state.
                FlyingTheGravship.ShiftEndsNormally(story, scene, uniform, parked);
            }
        }
    }
}
