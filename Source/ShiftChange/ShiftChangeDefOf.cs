using RimWorld;
using Verse;

namespace ShiftChange
{
    [DefOf]
    public static class ShiftChangeDefOf
    {
        public static JobDef ShiftChange_SwapAtStand;

        static ShiftChangeDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ShiftChangeDefOf));
        }
    }
}
