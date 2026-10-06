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
            if (Overrides.TryGetValue(work.defName, out key) && Renderable(key))
            {
                return key.Translate().RawText;
            }
            return work.gerundLabel ?? work.labelShort ?? work.defName;
        }

        /// <summary>
        /// Whether <c>Translate()</c> can render this key into SOMETHING a
        /// player should read.
        ///
        /// <para><c>CanTranslate</c> alone is not that test. It is
        /// <c>activeLanguage.HaveTextForKey</c> (<c>Translator.cs:9</c>), which
        /// never consults the default language, while <c>Translate</c> itself
        /// falls back to it (<c>Translator.cs:58</c>). We ship English keys
        /// only, so guarding on <c>CanTranslate</c> rejected a key that
        /// <c>Translate</c> would have rendered perfectly well, and every
        /// non-English player kept seeing the unfindable vanilla label this
        /// file exists to replace. Shipped that way in v1.4.0.</para>
        ///
        /// <para>Both languages are asked, so a key missing everywhere still
        /// falls through to the vanilla chain rather than printing itself.</para>
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

        /// <summary>
        /// Names of the mods supplying work types whose shown label the ACTIVE
        /// language has not translated. First-appearance order, no duplicates,
        /// empty when every listed type is covered.
        ///
        /// <para>Always empty in English, because English def XML IS the source
        /// text: nothing is injected, so nothing can be missing. An English
        /// player is told nothing at all.</para>
        ///
        /// <para><b>Why detect rather than warn blindly.</b> A work type's name
        /// belongs to whichever mod supplies it, so a player whose language
        /// that mod never translated gets English rows mixed into OUR grid,
        /// which reads as this dialog being broken. It is the failure
        /// <see cref="Overrides"/> exists for — a row nobody can find reads as
        /// "this mod is not supported" — except that this one is not ours to
        /// fix, only to explain. A notice shown to every non-English player
        /// regardless would greet the majority whose screen is entirely fine,
        /// and reads as an apology for a fault that is usually not
        /// occurring.</para>
        /// </summary>
        internal static List<string> UntranslatedSources(List<WorkTypeDef> works)
        {
            List<string> sources = new List<string>();
            if (works == null)
            {
                return sources;
            }

            LoadedLanguage active = LanguageDatabase.activeLanguage;
            if (active == null || active == LanguageDatabase.defaultLanguage)
            {
                return sources;
            }

            DefInjectionPackage package = null;
            for (int i = 0; i < active.defInjections.Count; i++)
            {
                if (active.defInjections[i].defType == typeof(WorkTypeDef))
                {
                    package = active.defInjections[i];
                    break;
                }
            }

            for (int i = 0; i < works.Count; i++)
            {
                WorkTypeDef work = works[i];
                if (work == null || Translated(package, work))
                {
                    continue;
                }
                string source = work.modContentPack == null
                    ? null
                    : work.modContentPack.Name;
                if (source.NullOrEmpty())
                {
                    source = work.defName;
                }
                if (!sources.Contains(source))
                {
                    sources.Add(source);
                }
            }
            return sources;
        }

        /// <summary>
        /// Whether the active language supplies the name we actually show for
        /// this work type.
        ///
        /// <para>Tests the field <see cref="Of"/> would read, not a fixed one:
        /// a type with no gerund shows labelShort, and one with neither shows
        /// its raw defName, which no translation can reach. An injection that
        /// failed to apply, or that is a translator's placeholder stub, leaves
        /// the English text on screen and so counts as untranslated.</para>
        ///
        /// <para>A row we override answers for itself, on the same
        /// <see cref="Renderable"/> test <see cref="Of"/> uses to decide it. The
        /// gap that leaves — one of OUR keys missing from a language we ship —
        /// is ours, and is caught by checking key parity against the English
        /// file, not by this notice.</para>
        /// </summary>
        internal static bool Translated(DefInjectionPackage package, WorkTypeDef work)
        {
            string ours;
            if (Overrides.TryGetValue(work.defName, out ours) && Renderable(ours))
            {
                return true;
            }

            string field = null;
            if (work.gerundLabel != null)
            {
                field = "gerundLabel";
            }
            else if (work.labelShort != null)
            {
                field = "labelShort";
            }
            if (field == null || package == null)
            {
                return false;
            }

            DefInjectionPackage.DefInjection injection;
            return package.injections.TryGetValue(work.defName + "." + field, out injection)
                && injection.injected
                && !injection.isPlaceholder;
        }
    }
}
