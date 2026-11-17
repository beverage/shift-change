using System.Collections.Generic;
using System.Reflection;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Shift Change's settings and the interception's switches as a test found them, put back by
    /// <see cref="FeatureFixture.RunScenario"/> whatever the test did or threw. A setting a test left
    /// changed would hold for every test after it, which would then test something nobody chose,
    /// and a fault count left standing would add up across tests until one stopped the mod.
    ///
    /// <para>Every setting is kept, read from <see cref="ShiftChangeSettings"/>'s fields, so one
    /// added later is kept too. A test may still put back what it changed itself, when doing so
    /// is part of its scenario.</para>
    /// </summary>
    internal sealed class SettingsAsFound
    {
        internal readonly Dictionary<FieldInfo, object> Settings = new Dictionary<FieldInfo, object>();
        internal readonly bool Intercepting = Patch_JobInterception.Enabled;
        internal readonly bool Faulted = Patch_JobInterception.faulted;
        internal readonly int FaultCount = Patch_JobInterception.faultCount;
        internal readonly int FaultLimit = Patch_JobInterception.FaultLimit;
        internal readonly bool OutfitStandsPlusButton = Patch_OutfitStandsPlusUseButton.Enabled;

        internal SettingsAsFound()
        {
            if (ShiftChangeMod.Settings == null)
            {
                return;
            }
            foreach (FieldInfo field in typeof(ShiftChangeSettings).GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType.IsValueType || field.FieldType == typeof(string))
                {
                    Settings[field] = field.GetValue(ShiftChangeMod.Settings);
                }
            }
        }

        internal void PutBack()
        {
            if (ShiftChangeMod.Settings != null)
            {
                foreach (KeyValuePair<FieldInfo, object> setting in Settings)
                {
                    setting.Key.SetValue(ShiftChangeMod.Settings, setting.Value);
                }
            }
            Patch_JobInterception.Enabled = Intercepting;
            Patch_JobInterception.faulted = Faulted;
            Patch_JobInterception.faultCount = FaultCount;
            Patch_JobInterception.FaultLimit = FaultLimit;
            Patch_OutfitStandsPlusUseButton.Enabled = OutfitStandsPlusButton;
        }
    }
}
