using System.Collections.Generic;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// One place to turn a <see cref="WorkTypeDef"/> into the name we show the
    /// player.
    ///
    /// <para>The expression was inlined at four sites and had already drifted:
    /// three read <c>gerundLabel ?? labelShort ?? defName</c> while the
    /// automatic-line one read <c>gerundLabel ?? defName</c> and skipped
    /// labelShort, so a modded type with no gerund showed its raw defName in
    /// one place and a readable name in the others. Folded into one call so
    /// the override table below lands everywhere at once.</para>
    ///
    /// <para><b>Why an override table exists at all.</b> A WorkTypeDef carries
    /// four name fields and we show <c>gerundLabel</c>, because it is the one
    /// that reads as an activity in a list — "Doctoring", "Cooking" — which is
    /// what the grid is a list OF. Vanilla makes the gerund the -ing form of
    /// <c>labelShort</c> every time, so the choice is invisible there. Nothing
    /// in the engine requires that. Dubs Rimatomics names the DOMAIN in
    /// labelShort ("Nuclear" — which is what the work tab column shows, see
    /// <c>PawnColumnWorker_WorkPriority</c>) and the ACTION in gerundLabel
    /// ("Loading"), so our grid offered a row called "Loading" to a player
    /// hunting for the "Nuclear" they had seen everywhere else. Verified in
    /// game 2026-09-13: the row worked, ticking it armed the stand. It was
    /// simply unfindable, which reads as "this mod is not supported".</para>
    ///
    /// <para><b>Why this is not a def patch.</b> <c>gerundLabel</c> is
    /// <c>[MustTranslate]</c>, and Rimatomics ships DefInjected translations
    /// for that exact field (Chinese simplified and traditional, Polish,
    /// Czech). Def patches apply while defs load;
    /// <c>InjectIntoData_AfterImpliedDefs</c> runs afterwards
    /// (PlayDataLoader.cs:333) and would stamp the localized "loading" straight
    /// back over ours. A patch would work in English and silently stop working
    /// in those four languages, which is the worst of both. The name WE show is
    /// ours to choose, so it is chosen here, in our own keyed strings, and
    /// nothing of theirs is touched.</para>
    /// </summary>
    public static class WorkTypeLabels
    {
        /// <summary>
        /// Work type defName → OUR translation key. Silent-fail twice over: a
        /// defName no loaded mod supplies is never asked about, and a key a
        /// translation has not filled in falls back to the vanilla chain rather
        /// than showing a raw key to the player.
        /// </summary>
        internal static readonly Dictionary<string, string> Overrides =
            new Dictionary<string, string>
            {
                { "NuclearWork", "ShiftChange.WorkTypeLabel.NuclearWork" },
            };

        /// <summary>
        /// Sentence form, lower case — for the inspect string, the gizmo and
        /// the automatic line, which read "Outfit for: nuclear loading".
        /// </summary>
        public static string Of(WorkTypeDef work)
        {
            if (work == null)
            {
                return string.Empty;
            }
            string key;
            if (Overrides.TryGetValue(work.defName, out key) && key.CanTranslate())
            {
                return key.Translate().RawText;
            }
            return work.gerundLabel ?? work.labelShort ?? work.defName;
        }

        /// <summary>
        /// Capitalised form — for the dialog grid, which also SORTS on this, so
        /// an overridden name moves to where its new spelling belongs. That is
        /// the point rather than a side effect: "nuclear loading" sorts under N,
        /// next to Mining, which is where someone looking for "Nuclear" looks.
        /// </summary>
        public static string Cap(WorkTypeDef work)
        {
            string label = Of(work);
            return label.NullOrEmpty() ? label : label.CapitalizeFirst();
        }
    }
}
