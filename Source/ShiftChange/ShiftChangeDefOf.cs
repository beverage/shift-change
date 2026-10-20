using RimWorld;
using Verse;

namespace ShiftChange
{
    [DefOf]
    public static class ShiftChangeDefOf
    {
        public static JobDef ShiftChange_SwapAtStand;

        /// <summary>
        /// Vanilla's medical bed-rest work type, which
        /// <see cref="Patch_JobInterception.MedicalRestWorkType"/> charges a tagged lay-down to.
        /// It is a Core def, so no MayRequire, and <c>WorkTypeDefOf</c> does not carry it.
        /// </summary>
        public static WorkTypeDef PatientBedRest;

        static ShiftChangeDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ShiftChangeDefOf));
        }
    }
}
