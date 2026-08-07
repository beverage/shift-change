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
    /// dresses for, and the checkout ledger for the one owner's clothes.
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

        /// <summary>Player override; null means "infer from the room's role".</summary>
        private WorkTypeDef workTypeOverride;

        /// <summary>
        /// Stands that currently have a uniform out on a pawn, so the return
        /// trip can be found without sweeping the map. Rebuilt on load, since
        /// <see cref="PostSpawnSetup"/> runs for every comp on both paths.
        /// </summary>
        private static readonly Dictionary<Pawn, CompShiftStand> OnShiftStands =
            new Dictionary<Pawn, CompShiftStand>();

        public Building_OutfitStand Stand => parent as Building_OutfitStand;

        public bool OnShift => issuedUniform.Count > 0;

        public Pawn Owner
        {
            get
            {
                CompAssignableToPawn comp = parent.TryGetComp<CompAssignableToPawn>();
                List<Pawn> assigned = comp?.AssignedPawnsForReading;
                return assigned != null && assigned.Count > 0 ? assigned[0] : null;
            }
        }

        /// <summary>
        /// The work type this stand dresses for: the explicit override if the
        /// player set one, otherwise the enclosing room's role. Null means the
        /// stand is inert — an ordinary vanilla outfit stand.
        /// </summary>
        public WorkTypeDef WorkType
        {
            get
            {
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

        public static CompShiftStand OnShiftStandFor(Pawn pawn)
        {
            CompShiftStand comp;
            if (!OnShiftStands.TryGetValue(pawn, out comp))
            {
                return null;
            }
            // A stand that was destroyed or reassigned while its uniform was
            // out leaves a stale entry; drop it rather than hand back a comp
            // whose parent is gone.
            if (comp == null || comp.parent == null || comp.parent.Destroyed || !comp.OnShift)
            {
                OnShiftStands.Remove(pawn);
                return null;
            }
            return comp;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            Pawn owner = Owner;
            if (owner != null && OnShift)
            {
                OnShiftStands[owner] = this;
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            Pawn owner = Owner;
            if (owner != null)
            {
                OnShiftStands.Remove(owner);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref storedOwnerApparel, "storedOwnerApparel", LookMode.Reference);
            Scribe_Collections.Look(ref issuedUniform, "issuedUniform", LookMode.Reference);
            Scribe_Defs.Look(ref workTypeOverride, "workTypeOverride");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Anything burnt, stolen or removed by hand while we were not
                // looking comes back null. Dropping it here is the whole of
                // the orphan story on this side: the ledger simply forgets,
                // and vanilla's own eviction handles the leftovers in the
                // container.
                storedOwnerApparel = storedOwnerApparel ?? new List<Apparel>();
                issuedUniform = issuedUniform ?? new List<Apparel>();
                storedOwnerApparel.RemoveAll(a => a == null);
                issuedUniform.RemoveAll(a => a == null);
            }
        }

        /// <summary>
        /// Records a completed dressing: <paramref name="stored"/> went into
        /// the stand, <paramref name="issued"/> came out onto the pawn.
        /// </summary>
        public void NotifyDressed(Pawn pawn, List<Apparel> stored, List<Apparel> issued)
        {
            storedOwnerApparel.Clear();
            storedOwnerApparel.AddRange(stored);
            issuedUniform.Clear();
            issuedUniform.AddRange(issued);
            if (issuedUniform.Count > 0)
            {
                OnShiftStands[pawn] = this;
            }
        }

        public void NotifyUndressed(Pawn pawn)
        {
            storedOwnerApparel.Clear();
            issuedUniform.Clear();
            OnShiftStands.Remove(pawn);
        }

        public List<Apparel> StoredOwnerApparelForReading => storedOwnerApparel;

        public List<Apparel> IssuedUniformForReading => issuedUniform;

        /// <summary>
        /// Clears the ledger without a return trip — the owner is gone (dead,
        /// traded, kidnapped, off the map). Their clothes stay in the stand as
        /// ordinary contents, and the next owner's first swap lets vanilla's
        /// <c>TryDropThingsToMakeRoomForThingOfDef</c> evict whatever conflicts.
        /// </summary>
        public void AbandonLedger(Pawn formerOwner)
        {
            if (formerOwner != null)
            {
                OnShiftStands.Remove(formerOwner);
            }
            storedOwnerApparel.Clear();
            issuedUniform.Clear();
        }

        public override string CompInspectStringExtra()
        {
            WorkTypeDef work = WorkType;
            if (work == null)
            {
                return null;
            }
            Pawn owner = Owner;
            string line = "ShiftChange.InspectWork".Translate(work.gerundLabel ?? work.labelShort ?? work.defName);
            if (owner == null)
            {
                return line + "\n" + "ShiftChange.InspectUnassigned".Translate();
            }
            return line + (OnShift ? "\n" + "ShiftChange.InspectOnShift".Translate(owner.LabelShort) : "");
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            WorkTypeDef current = WorkType;
            yield return new Command_Action
            {
                defaultLabel = "ShiftChange.SetWorkTypeLabel".Translate(),
                defaultDesc = "ShiftChange.SetWorkTypeDesc".Translate(
                    current?.gerundLabel ?? current?.defName ?? "ShiftChange.None".Translate().RawText,
                    workTypeOverride == null
                        ? "ShiftChange.FromRoom".Translate().RawText
                        : "ShiftChange.Manual".Translate().RawText),
                icon = TexCommand.ForbidOff,
                action = OpenWorkTypeMenu,
            };
        }

        private void OpenWorkTypeMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("ShiftChange.WorkTypeAuto".Translate(), () => workTypeOverride = null),
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
                    () => workTypeOverride = local));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
