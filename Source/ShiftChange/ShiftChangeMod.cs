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

        /// <summary>
        /// When true, a stand will not leave a colonist without basic clothing:
        /// it holds back the innermost garments vanilla's own decency rule asks
        /// for and deposits the rest. When false a stand does exactly what its
        /// settings say, which is how every version up to v1.3.0 behaved.
        ///
        /// <para><b>OFF by default, and that is a release-sequencing decision
        /// rather than a view about which behaviour is better</b> (decided
        /// 2026-09-08). This mod ships inside mod packs, where the player did
        /// not choose it and will not read its change note; a patch release is
        /// the wrong place to change how an existing colony behaves under them.
        /// Opt-in for now; revisit the default at the next MINOR version, where
        /// the bump itself is the notice.</para>
        ///
        /// <para>Note the asymmetry this creates: the guard is what the mod's
        /// own README promises ("there is no configuration in which a colonist
        /// strips for a shift and gets nothing back"), so while this is off,
        /// that promise holds only for players who found the checkbox.</para>
        ///
        /// <para>Individual nudists and nudism ideoligions are exempt either
        /// way (<see cref="SwapPlan.PrefersNudity"/>) — that detection is not
        /// what this switch controls.</para>
        /// </summary>
        public bool keepColonistsDecent = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref poolUnassignedStands, "poolUnassignedStands", defaultValue: true);
            Scribe_Values.Look(ref keepColonistsDecent, "keepColonistsDecent", defaultValue: false);
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

        /// <summary>
        /// The single read point for the decency guard. Null-tolerant for the
        /// same reason as <see cref="PoolingEnabled"/> — but note the fallback
        /// is the OPPOSITE way round: this defaults OFF, so settings that have
        /// not loaded yet must read as off too, or a stand would briefly behave
        /// differently from how the player configured it.
        /// </summary>
        public static bool DecencyEnabled => Settings != null && Settings.keepColonistsDecent;

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
            listing.CheckboxLabeled(
                "ShiftChange.SettingKeepDecent".Translate(),
                ref Settings.keepColonistsDecent,
                "ShiftChange.SettingKeepDecentDesc".Translate());
            listing.End();
        }
    }
}
