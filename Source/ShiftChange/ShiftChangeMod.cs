using UnityEngine;
using Verse;

namespace ShiftChange
{
    public class ShiftChangeSettings : ModSettings
    {
        /// <summary>
        /// When true (the default), an unassigned stand in a work room is a pool stand that any
        /// capable colonist may claim. When false, only a stand assigned to a colonist dresses
        /// anyone, and every unassigned stand behaves like vanilla furniture.
        /// </summary>
        public bool poolUnassignedStands = true;

        /// <summary>
        /// When true, a stand will not leave a colonist without basic clothing: it holds back
        /// the innermost garments vanilla's own decency rule asks for and deposits the rest.
        /// When false a stand does exactly what its settings say.
        ///
        /// <para>Off by default, and it stays off. Most players meet the mod inside a mod pack
        /// and did not choose it, so a default that changes how their colonists dress is not
        /// ours to turn on; this is not a default waiting for a release. The cost accepted with
        /// that: docs/DESIGN.md, "The dress path asks the same question and answers it
        /// differently".</para>
        ///
        /// <para>Nudists and nudist ideoligions are exempt whichever way this is set
        /// (<see cref="SwapPlan.PrefersNudity"/>).</para>
        /// </summary>
        public bool keepColonistsDecent = false;

        /// <summary>
        /// When true, a medical emergency no longer exempts a colonist from dressing: a doctor
        /// takes an emergency tend in scrubs, and a patient in critical condition stops for the
        /// gown on the way to bed. Off by default, on the same reasoning as
        /// <see cref="keepColonistsDecent"/>.
        ///
        /// <para>Never firefighting, though vanilla flags that an emergency too, so the covered
        /// work types are named in <see cref="Patch_JobInterception.MedicalWorkTypeNames"/>. It
        /// applies in both directions: a doctor in another room's uniform may change back first
        /// as well. Both: docs/DESIGN.md, "The sleep branch".</para>
        /// </summary>
        public bool medicalEmergenciesChangeFirst = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref poolUnassignedStands, "poolUnassignedStands", defaultValue: true);
            Scribe_Values.Look(ref keepColonistsDecent, "keepColonistsDecent", defaultValue: false);
            Scribe_Values.Look(ref medicalEmergenciesChangeFirst,
                "medicalEmergenciesChangeFirst", defaultValue: false);
        }
    }

    public class ShiftChangeMod : Mod
    {
        // An explicit field, never an auto-property: the compiler makes an auto-property's
        // backing field private, with no syntax to widen it, so InternalsVisibleTo cannot reach
        // it and a hot-swapped getter throws FieldAccessException.
        internal static ShiftChangeSettings settings;

        public static ShiftChangeSettings Settings => settings;

        /// <summary>
        /// The read point for <see cref="ShiftChangeSettings.poolUnassignedStands"/>.
        /// Null-tolerant, so a read before the settings load cannot throw; it gives the default,
        /// pooling on.
        /// </summary>
        public static bool PoolingEnabled => Settings == null || Settings.poolUnassignedStands;

        /// <summary>
        /// The read point for <see cref="ShiftChangeSettings.keepColonistsDecent"/>. Unloaded
        /// settings read as off, which is its default (that of <see cref="PoolingEnabled"/> is
        /// on): a null-tolerant read has to agree with the default.
        /// </summary>
        public static bool DecencyEnabled => Settings != null && Settings.keepColonistsDecent;

        /// <summary>
        /// The read point for <see cref="ShiftChangeSettings.medicalEmergenciesChangeFirst"/>.
        /// Unloaded settings read as off, the default, as for <see cref="DecencyEnabled"/>: no
        /// emergency is delayed by settings that have not loaded.
        /// </summary>
        public static bool MedicalEmergencyDressingEnabled =>
            Settings != null && Settings.medicalEmergenciesChangeFirst;

        public ShiftChangeMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<ShiftChangeSettings>();
        }

        public override string SettingsCategory()
        {
            // The Mod settings entry name: a proper noun, so it has no translation key.
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
            listing.CheckboxLabeled(
                "ShiftChange.SettingMedicalEmergencies".Translate(),
                ref Settings.medicalEmergenciesChangeFirst,
                "ShiftChange.SettingMedicalEmergenciesDesc".Translate());
            listing.End();
        }
    }
}
