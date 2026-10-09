using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PicoShot.Localization.Data;
using PicoShot.Localization.Editor.Data;
using PicoShot.Localization.Rtl;

namespace PicoShot.Localization.Editor.Services
{
    /// <summary>
    /// Collects the characters a language actually renders: rich-text tags are stripped, RTL text is shaped
    /// (so Arabic yields its presentation forms) and surrogate pairs are kept as single code points.
    /// </summary>
    public static class CharacterSetCollector
    {
        private static readonly Regex RichTextTag = new("<[^<>]+>", RegexOptions.Compiled);

        /// <summary>
        /// Every renderable code point in a language's translations.
        /// </summary>
        public static HashSet<int> CollectLanguage(LanguageEditorData data, string language, bool stripTags = true, bool shapeRtl = true)
        {
            var result = new HashSet<int>();
            bool rtl = shapeRtl && LanguageDefinitions.IsRightToLeft(language);

            foreach (var key in data.Keys)
            {
                if (!data.LanguageData.TryGetValue(key, out var keyData) || !keyData.TryGetValue(language, out var value))
                    continue;

                switch (value)
                {
                    case string str:
                        Collect(str, rtl, stripTags, result);
                        break;
                    case IList<string> list:
                        foreach (var item in list)
                            Collect(item, rtl, stripTags, result);
                        break;
                }
            }

            return result;
        }

        public static void Collect(string text, bool shapeRtl, bool stripTags, HashSet<int> into)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (stripTags)
                text = RichTextTag.Replace(text, string.Empty);
            if (shapeRtl)
                text = RtlTextHandler.Fix(text);

            for (int i = 0; i < text.Length; i++)
            {
                int codepoint;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    codepoint = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                else if (char.IsSurrogate(text[i]))
                {
                    continue;
                }
                else
                {
                    codepoint = text[i];
                }

                if (!IsIgnorable(codepoint))
                    into.Add(codepoint);
            }
        }

        /// <summary>
        /// Whitespace, control characters and invisible formatting marks need no glyph.
        /// </summary>
        public static bool IsIgnorable(int codepoint)
        {
            if (codepoint <= char.MaxValue && (char.IsWhiteSpace((char)codepoint) || char.IsControl((char)codepoint)))
                return true;

            return codepoint is >= 0x200B and <= 0x200F  // zero-width spaces and direction marks
                or >= 0x202A and <= 0x202E                // bidi embedding controls
                or >= 0x2060 and <= 0x206F                // invisible operators
                or >= 0xFE00 and <= 0xFE0F                // variation selectors
                or 0xFEFF;
        }

        public static string ToText(IEnumerable<int> codepoints, string separator = "")
        {
            return string.Join(separator, codepoints.Select(char.ConvertFromUtf32));
        }

        /// <summary>
        /// Comma-separated hex ranges, e.g. "20-7E,A0,3040-309F", as accepted by TMP's Font Asset Creator.
        /// </summary>
        public static string ToUnicodeRanges(IEnumerable<int> codepoints)
        {
            var sorted = codepoints.Distinct().OrderBy(c => c).ToList();
            var sb = new StringBuilder();

            for (int i = 0; i < sorted.Count; i++)
            {
                int start = sorted[i];
                int end = start;
                while (i + 1 < sorted.Count && sorted[i + 1] == end + 1)
                {
                    end = sorted[++i];
                }

                if (sb.Length > 0)
                    sb.Append(',');
                sb.Append(start.ToString("X"));
                if (end != start)
                    sb.Append('-').Append(end.ToString("X"));
            }

            return sb.ToString();
        }
    }
}
