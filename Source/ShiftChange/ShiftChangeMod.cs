using UnityEngine;
using Verse;

namespace ShiftChange
{
    public class ShiftChangeSettings : ModSettings
    {
        /// <summary>
        /// When true (the default), an unassigned stand in a work room is a
        /// pool stand any capable colonist may claim. When false, only stands
        /// explicitly assigned to a colonist ever dress anyone — every
        /// unassigned stand behaves exactly like vanilla furniture. The one
        /// global knob, for players who want participation strictly opt-in
        /// per stand.
        /// </summary>
        public bool poolUnassignedStands = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref poolUnassignedStands, "poolUnassignedStands", defaultValue: true);
        }
    }

    public class ShiftChangeMod : Mod
    {
        // Explicit backing field, NOT an auto-property: the compiler names an
        // auto-property's backing field <Settings>k__BackingField and makes it
        // PRIVATE with no syntax to widen it — unreachable through the IVT
        // grants, so a hot-swapped getter throws FieldAccessException (found
        // in play 2026-08-08, the one member the internal sweep couldn't
        // touch). Auto-properties are banned in this codebase for that reason.
        internal static ShiftChangeSettings settings;

        public static ShiftChangeSettings Settings => settings;

        /// <summary>
        /// The single read point for the toggle. Null-tolerant so comp code
        /// can never crash on ordering; defaults to pooling on.
        /// </summary>
        public static bool PoolingEnabled => Settings == null || Settings.poolUnassignedStands;

        public ShiftChangeMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<ShiftChangeSettings>();
        }

        public override string SettingsCategory()
        {
            // The Options → Mod settings entry name. A proper noun; not keyed.
            return "Shift Change";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.CheckboxLabeled(
                "ShiftChange.SettingPoolUnassigned".Translate(),
                ref Settings.poolUnassignedStands,
                "ShiftChange.SettingPoolUnassignedDesc".Translate());
            listing.End();
        }
    }
}
