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
        /// <para><b>OFF by default, permanently, and that is a distribution
        /// decision rather than a view about which behaviour is better</b>
        /// (settled 2026-09-12, replacing the 2026-09-08 intent to revisit it
        /// at the next minor). Most of this mod's players meet it inside a mod
        /// pack: they did not choose it, they will not read a change note, and
        /// a default that changes how their colonists undress is not ours to
        /// flip on their behalf. That reasoning does not expire with a version
        /// bump, so there is no version at which this becomes on. Do not
        /// re-open it as release sequencing; it is not waiting for a bump.</para>
        ///
        /// <para>The cost, stated plainly and accepted: the guard covers a case
        /// the player cannot see coming, and while it is off it covers only the
        /// players who found the checkbox. The README and the setting
        /// description therefore both lead with the default rather than burying
        /// it, which is the whole mitigation.</para>
        ///
        /// <para>Individual nudists and nudism ideoligions are exempt either
        /// way (<see cref="SwapPlan.PrefersNudity"/>) — that detection is not
        /// what this switch controls.</para>
        /// </summary>
        public bool keepColonistsDecent = false;

        /// <summary>
        /// When true, a MEDICAL emergency no longer exempts a colonist from
        /// dressing: a doctor takes an emergency tend in scrubs, and a patient
        /// in critical condition stops for the gown on the way to bed. Off by
        /// default, which is the behaviour every version up to now had.
        ///
        /// <para><b>Firefighting is never covered, and that is why this is not
        /// simply "ignore the emergency flag".</b> Vanilla sets
        /// <c>emergency: true</c> on <c>FightFires</c> as well as the two
        /// medical givers, so a switch keyed on the flag would send colonists
        /// to a wardrobe while the base burns. The covered work types are
        /// named in <see cref="Patch_JobInterception.MedicalWorkTypeNames"/>.</para>
        ///
        /// <para>OFF by default on the same distribution reasoning recorded
        /// for <see cref="keepColonistsDecent"/>: most of this mod's players
        /// meet it inside a mod pack, they did not choose it and will not read
        /// a change note, and changing how their doctors answer a bleeding
        /// colonist is not ours to flip on their behalf.</para>
        ///
        /// <para>The cost of turning it on, stated rather than buried: the
        /// emergency exemption is one test covering both directions, so a
        /// doctor in another room's uniform may now change BACK before
        /// answering the call as well as changing into scrubs. That is the
        /// setting meaning what it says — medical emergencies become ordinary
        /// work for dressing purposes.</para>
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

        /// <summary>
        /// The single read point for the medical-emergency carve-out. Defaults
        /// OFF when settings have not loaded, for the same reason
        /// <see cref="DecencyEnabled"/> does: unloaded must read as the
        /// conservative answer, and here that is "an emergency is never
        /// delayed".
        /// </summary>
        public static bool MedicalEmergencyDressingEnabled =>
            Settings != null && Settings.medicalEmergenciesChangeFirst;

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
            listing.CheckboxLabeled(
                "ShiftChange.SettingMedicalEmergencies".Translate(),
                ref Settings.medicalEmergenciesChangeFirst,
                "ShiftChange.SettingMedicalEmergenciesDesc".Translate());
            listing.End();
        }
    }
}
