using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShiftChange
{
    public class CompProperties_ShiftStand : CompProperties
    {
        public CompProperties_ShiftStand()
        {
            compClass = typeof(CompShiftStand);
        }
    }

    /// <summary>
    /// The Shift Change half of a vanilla outfit stand: which work type it
    /// dresses for, and the checkout ledger for whoever currently has its
    /// uniform out.
    ///
    /// The ledger exists because the stand itself has no idea whose clothes it
    /// holds — <see cref="Building_OutfitStand"/> scribes only its container,
    /// its storage settings and <c>allowRemovingItems</c>, and vanilla's own
    /// <c>JobDriver_UseOutfitStand</c> hands any wearable item to whoever turns
    /// up. Ownership comes from the sibling
    /// <see cref="CompAssignableToPawn_ShiftStand"/>; this comp records which
    /// specific items moved which way, so the return trip puts the right
    /// clothes back on the right pawn.
    /// </summary>
    public class CompShiftStand : ThingComp
    {
        /// <summary>The owner's own clothes, currently parked in the stand.</summary>
        private List<Apparel> storedOwnerApparel = new List<Apparel>();

        /// <summary>The stand's clothes, currently worn by the owner.</summary>
        private List<Apparel> issuedUniform = new List<Apparel>();

        /// <summary>
        /// The subset of <see cref="storedOwnerApparel"/> that was FORCE-WORN
        /// when it was checked in. Vanilla clears the forced flag on every
        /// removal (<c>Pawn_ApparelTracker.Notify_ApparelRemoved:784-790</c>),
        /// so without this record a force-worn duster comes back from a shift
        /// as ordinary policy-managed clothing and the optimizer swaps it away.
        /// </summary>
        private List<Apparel> storedForcedApparel = new List<Apparel>();

        /// <summary>
        /// Who currently has this stand's uniform on. This — not the assigned
        /// owner — is the truth for the return trip, because an unassigned
        /// stand is a POOL stand that anyone capable may borrow.
        /// </summary>
        private Pawn borrower;

        /// <summary>Player override; null means "infer from the room's role".</summary>
        private WorkTypeDef workTypeOverride;

        /// <summary>
        /// Opt-out. A decorative or storage stand standing in a roled room would
        /// otherwise join that room's pool, which is a surprise nobody asked
        /// for.
        /// </summary>
        private bool excluded;

        /// <summary>
        /// Stands that currently have a uniform out on a pawn, so the return
        /// trip can be found without sweeping the map. Keyed by BORROWER and
        /// rebuilt on load, since <see cref="PostSpawnSetup"/> runs for every
        /// comp on both paths.
        /// </summary>
        private static readonly Dictionary<Pawn, CompShiftStand> OnShiftStands =
            new Dictionary<Pawn, CompShiftStand>();

        public Building_OutfitStand Stand => parent as Building_OutfitStand;

        public bool OnShift => issuedUniform.Count > 0;

        public Pawn Borrower => borrower;

        /// <summary>
        /// The pawn this stand is reserved for, or null when it is a pool
        /// stand. Distinct from <see cref="Borrower"/>: assignment is the
        /// player's standing intent, borrowing is who is wearing it now.
        /// </summary>
        public Pawn AssignedOwner
        {
            get
            {
                CompAssignableToPawn comp = parent.TryGetComp<CompAssignableToPawn>();
                List<Pawn> assigned = comp?.AssignedPawnsForReading;
                return assigned != null && assigned.Count > 0 ? assigned[0] : null;
            }
        }

        public bool IsPool => AssignedOwner == null;

        /// <summary>
        /// Unassigned means pool: any pawn may take it — unless the player
        /// turned pooling off in mod settings, in which case an unassigned
        /// stand is inert and only explicit assignment participates. Assigned
        /// means reserved, and nobody else gets a look in — that is what
        /// keeps a surgeon's tailored kit off a passing hauler.
        /// </summary>
        public bool CanBeClaimedBy(Pawn pawn)
        {
            Pawn owner = AssignedOwner;
            if (owner != null)
            {
                return owner == pawn;
            }
            return ShiftChangeMod.PoolingEnabled;
        }

        /// <summary>
        /// Whether the stand holds anything to change into. An empty pool stand
        /// would otherwise be claimed, walked to, and swapped with — a wasted
        /// trip that looks like a bug.
        /// </summary>
        public bool HasWearable
        {
            get
            {
                Building_OutfitStand stand = Stand;
                if (stand == null)
                {
                    return false;
                }
                IReadOnlyList<Thing> held = stand.HeldItems;
                for (int i = 0; i < held.Count; i++)
                {
                    if (held[i] is Apparel)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// The work type this stand dresses for: null when the player excluded
        /// it, otherwise the explicit override, otherwise the enclosing room's
        /// role. Null means inert — an ordinary vanilla outfit stand.
        /// </summary>
        public WorkTypeDef WorkType
        {
            get
            {
                if (excluded)
                {
                    return null;
                }
                if (workTypeOverride != null)
                {
                    return workTypeOverride;
                }
                if (!parent.Spawned)
                {
                    return null;
                }
                return RoomWorkTypes.ForRole(parent.GetRoom()?.Role);
            }
        }

        /// <summary>Called by SessionGuard when the loaded game changes.</summary>
        internal static void ResetSessionState()
        {
            OnShiftStands.Clear();
        }

        public static CompShiftStand OnShiftStandFor(Pawn pawn)
        {
            SessionGuard.Ensure();
            CompShiftStand comp;
            if (!OnShiftStands.TryGetValue(pawn, out comp))
            {
                return null;
            }
            // A stand that was destroyed while its uniform was out leaves a
            // stale entry; drop it rather than hand back a comp whose parent
            // is gone.
            if (comp == null || comp.parent == null || comp.parent.Destroyed
                || !comp.OnShift || comp.borrower != pawn)
            {
                OnShiftStands.Remove(pawn);
                return null;
            }
            return comp;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            SessionGuard.Ensure();

            // Rebuild from the BORROWER, not the assigned owner. Inferring it
            // from ownership was only ever right by coincidence — reassign a
            // stand while its uniform is out and the return trip would have
            // pointed at the wrong pawn.
            if (borrower != null && OnShift)
            {
                OnShiftStands[borrower] = this;
            }
            else if (borrower == null && OnShift)
            {
                // Ledger with no borrower: a save from before pooling, or a
                // borrower who vanished without the reaper firing. Nothing can
                // return these, so free the stand and let vanilla's eviction
                // deal with whatever is inside.
                storedOwnerApparel.Clear();
                issuedUniform.Clear();
                storedForcedApparel.Clear();
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            if (borrower != null)
            {
                OnShiftStands.Remove(borrower);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref storedOwnerApparel, "storedOwnerApparel", LookMode.Reference);
            Scribe_Collections.Look(ref issuedUniform, "issuedUniform", LookMode.Reference);
            Scribe_Collections.Look(ref storedForcedApparel, "storedForcedApparel", LookMode.Reference);
            Scribe_References.Look(ref borrower, "borrower");
            Scribe_Defs.Look(ref workTypeOverride, "workTypeOverride");
            Scribe_Values.Look(ref excluded, "excluded", defaultValue: false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Anything burnt, stolen or removed by hand while we were not
                // looking comes back null. Dropping it here is the whole of
                // the orphan story on this side: the ledger simply forgets,
                // and vanilla's own eviction handles the leftovers in the
                // container.
                storedOwnerApparel = storedOwnerApparel ?? new List<Apparel>();
                issuedUniform = issuedUniform ?? new List<Apparel>();
                storedForcedApparel = storedForcedApparel ?? new List<Apparel>();
                storedOwnerApparel.RemoveAll(a => a == null);
                issuedUniform.RemoveAll(a => a == null);
                storedForcedApparel.RemoveAll(a => a == null);
            }
        }

        /// <summary>
        /// Records a completed dressing: <paramref name="stored"/> went into
        /// the stand, <paramref name="issued"/> came out onto the pawn.
        /// </summary>
        public void NotifyDressed(Pawn pawn, List<Apparel> stored, List<Apparel> issued, List<Apparel> storedForced)
        {
            storedOwnerApparel.Clear();
            storedOwnerApparel.AddRange(stored);
            issuedUniform.Clear();
            issuedUniform.AddRange(issued);
            storedForcedApparel.Clear();
            if (storedForced != null)
            {
                storedForcedApparel.AddRange(storedForced);
            }
            if (issuedUniform.Count > 0)
            {
                borrower = pawn;
                OnShiftStands[pawn] = this;
            }
            else
            {
                // Nothing was actually issued (empty stand, nothing wearable).
                // Do not claim the stand for a swap that did not happen.
                borrower = null;
            }
        }

        public void NotifyUndressed(Pawn pawn)
        {
            storedOwnerApparel.Clear();
            issuedUniform.Clear();
            storedForcedApparel.Clear();
            borrower = null;
            OnShiftStands.Remove(pawn);
        }

        /// <summary>
        /// Whether this garment was force-worn at check-in, and should have the
        /// flag restored when it comes back on. Consult BEFORE
        /// <see cref="NotifyUndressed"/> clears the ledger.
        /// </summary>
        public bool WasForcedWhenStored(Apparel apparel)
        {
            return storedForcedApparel.Contains(apparel);
        }

        public List<Apparel> StoredOwnerApparelForReading => storedOwnerApparel;

        public List<Apparel> IssuedUniformForReading => issuedUniform;

        /// <summary>
        /// Clears the ledger without a return trip — the borrower is gone
        /// (dead, traded, kidnapped, off the map). Their clothes stay in the
        /// stand as ordinary contents, and the next claimant's first swap lets
        /// vanilla's <c>TryDropThingsToMakeRoomForThingOfDef</c> evict whatever
        /// conflicts. Frees the stand back into the pool.
        /// </summary>
        public void AbandonLedger(Pawn formerBorrower)
        {
            if (formerBorrower != null)
            {
                OnShiftStands.Remove(formerBorrower);
            }
            storedOwnerApparel.Clear();
            issuedUniform.Clear();
            storedForcedApparel.Clear();
            borrower = null;
        }

        /// <summary>Every stand on every map that this pawn has out on loan.</summary>
        public static IEnumerable<CompShiftStand> StandsBorrowedBy(Pawn pawn)
        {
            ThingDef standDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_OutfitStand");
            if (standDef == null || pawn == null)
            {
                yield break;
            }
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                List<Thing> stands = maps[i].listerThings.ThingsOfDef(standDef);
                for (int j = 0; j < stands.Count; j++)
                {
                    CompShiftStand comp = stands[j].TryGetComp<CompShiftStand>();
                    if (comp != null && comp.borrower == pawn)
                    {
                        yield return comp;
                    }
                }
            }
        }

        public override string CompInspectStringExtra()
        {
            if (excluded)
            {
                return "ShiftChange.InspectExcluded".Translate();
            }
            WorkTypeDef work = WorkType;
            if (work == null)
            {
                return null;
            }

            string line = "ShiftChange.InspectWork".Translate(work.gerundLabel ?? work.labelShort ?? work.defName);

            // Say who owns or holds it here as well as on the gizmo: the base
            // comp reports ownership nowhere, and a stand that looks unassigned
            // is indistinguishable from one that simply never fires.
            Pawn owner = AssignedOwner;
            line += "\n" + (owner != null
                ? "ShiftChange.InspectOwner".Translate(owner.LabelShort)
                : (ShiftChangeMod.PoolingEnabled
                    ? "ShiftChange.InspectPool".Translate()
                    // Keep the UI honest: with pooling off, "shared" would be
                    // a lie the player debugs for an evening.
                    : "ShiftChange.InspectPoolDisabled".Translate()));

            if (OnShift && borrower != null)
            {
                line += "\n" + "ShiftChange.InspectOnShift".Translate(borrower.LabelShort);
            }
            else if (!HasWearable)
            {
                line += "\n" + "ShiftChange.InspectEmpty".Translate();
            }
            return line;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            WorkTypeDef current = WorkType;
            string source = excluded
                ? "ShiftChange.Excluded".Translate().RawText
                : workTypeOverride == null
                    ? "ShiftChange.FromRoom".Translate().RawText
                    : "ShiftChange.Manual".Translate().RawText;
            yield return new Command_Action
            {
                defaultLabel = "ShiftChange.SetWorkTypeLabel".Translate(),
                defaultDesc = "ShiftChange.SetWorkTypeDesc".Translate(
                    current?.gerundLabel ?? current?.defName ?? "ShiftChange.None".Translate().RawText,
                    source),
                // The pencil, not ForbidOff — a forbid glyph on a
                // configuration gizmo reads as "this stand is disabled".
                // Zero-art rule: icons come from vanilla's atlas only.
                icon = TexButton.Rename,
                action = OpenWorkTypeMenu,
            };
        }

        private void OpenWorkTypeMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("ShiftChange.WorkTypeAuto".Translate(), () =>
                {
                    workTypeOverride = null;
                    excluded = false;
                }),
                // The opt-out. Without it, any unassigned stand standing in a
                // roled room silently joins that room's pool — including one
                // the player keeps for decoration or storage.
                new FloatMenuOption("ShiftChange.WorkTypeNone".Translate(), () =>
                {
                    workTypeOverride = null;
                    excluded = true;
                }),
            };

            // Only work types a colonist can actually be assigned — the
            // never-assignable ones (Patient, PatientBedRest) would be noise.
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading
                         .Where(w => w.visible)
                         .OrderBy(w => w.labelShort ?? w.defName))
            {
                WorkTypeDef local = work;
                options.Add(new FloatMenuOption(
                    local.gerundLabel?.CapitalizeFirst() ?? local.defName,
                    () =>
                    {
                        workTypeOverride = local;
                        excluded = false;
                    }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
