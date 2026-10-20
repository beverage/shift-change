using System.Collections.Generic;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Turns a <see cref="WorkTypeDef"/> into the name shown to the player, with an override
    /// table for work types whose own label is hard to find. Every site that shows a work
    /// type's name goes through here, so the table lands everywhere at once.
    ///
    /// <para>A work type carries four name fields, and the grid shows <c>gerundLabel</c>
    /// because it reads as an activity in a list: "Doctoring", "Cooking". Vanilla's gerund
    /// stays close to its <c>labelShort</c> ("doctor", "doctoring"), so the choice is hard to
    /// see there, but nothing in the engine requires that. Dubs Rimatomics puts the domain in
    /// <c>labelShort</c> ("Nuclear", which the work tab column shows via
    /// <c>PawnColumnWorker_WorkPriority</c>) and the action in <c>gerundLabel</c>
    /// ("Loading"), so the grid offered a row called "Loading" to a player looking for the
    /// "Nuclear" they had seen elsewhere.</para>
    ///
    /// <para>The override is not a def patch. <c>gerundLabel</c> is <c>[MustTranslate]</c>,
    /// and Rimatomics ships DefInjected translations for that field in several languages.
    /// Def patches apply while defs load, and <c>InjectIntoData_AfterImpliedDefs</c> runs
    /// afterwards, so it would put the localized text back over a patched one: the patch
    /// would work in English and silently stop working in those languages. The override
    /// lives in our own keyed strings and touches nothing of theirs.</para>
    /// </summary>
    public static class WorkTypeLabels
    {
        /// <summary>
        /// Work type defName to our translation key. A defName no loaded mod supplies is
        /// never asked about, and a key with no text falls back to the vanilla chain rather
        /// than showing a raw key.
        /// </summary>
        internal static readonly Dictionary<string, string> Overrides =
            new Dictionary<string, string>
            {
                { "NuclearWork", "ShiftChange.WorkTypeLabel.NuclearWork" },
            };

        /// <summary>
        /// Lower-case form for sentences (the inspect string, the gizmo, the automatic line):
        /// "Outfit for: nuclear loading".
        /// </summary>
        public static string Of(WorkTypeDef work)
        {
            if (work == null)
            {
                return string.Empty;
            }
            string key;
            if (Overrides.TryGetValue(work.defName, out key) && Renderable(key))
            {
                return key.Translate().RawText;
            }
            return work.gerundLabel ?? work.labelShort ?? work.defName;
        }

        /// <summary>
        /// Whether <c>Translate()</c> can render this key into something readable.
        /// <c>CanTranslate</c> alone is not that test: it asks only the active language,
        /// while <c>Translate</c> falls back to the default language. This mod ships English
        /// keys only, so <c>CanTranslate</c> would reject keys that <c>Translate</c> renders
        /// fine. A key missing from both languages is not renderable, and falls through to
        /// the vanilla chain rather than printing itself.
        /// </summary>
        internal static bool Renderable(string key)
        {
            if (key.CanTranslate())
            {
                return true;
            }
            LoadedLanguage fallback = LanguageDatabase.defaultLanguage;
            return fallback != null && fallback.HaveTextForKey(key);
        }

        /// <summary>
        /// Capitalised form for the dialog grid, which sorts on it, so an overridden name
        /// sorts under its new spelling on purpose: "nuclear loading" lands under N, next to
        /// Mining, where someone looking for "Nuclear" looks.
        /// </summary>
        public static string Cap(WorkTypeDef work)
        {
            string label = Of(work);
            return label.NullOrEmpty() ? label : label.CapitalizeFirst();
        }
    }
}
