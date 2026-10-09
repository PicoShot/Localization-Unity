using System;
using System.Collections.Generic;
using System.Text;

namespace PicoShot.Localization.Editor.Mcp
{
    /// <summary>
    /// Compares a translation with its source text: the same placeholders ({0}, {0:N2}, {name})
    /// and the same rich-text tags (&lt;b&gt;, &lt;/color&gt;, ...) must appear in both.
    /// </summary>
    internal static class McpTextChecks
    {
        /// <summary>
        /// Returns a description of what differs, or null when the translation matches the source
        /// (or is empty, which is reported separately as untranslated).
        /// </summary>
        public static string FindMismatch(string source, string translation)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(translation))
                return null;

            var problems = new List<string>();

            var sourcePlaceholders = Placeholders(source);
            var translationPlaceholders = Placeholders(translation);
            var missing = Difference(sourcePlaceholders, translationPlaceholders);
            var extra = Difference(translationPlaceholders, sourcePlaceholders);
            if (missing.Count > 0)
                problems.Add("missing placeholder " + Join(missing, "{", "}"));
            if (extra.Count > 0)
                problems.Add("unknown placeholder " + Join(extra, "{", "}"));

            var sourceTags = Tags(source);
            var translationTags = Tags(translation);
            var missingTags = Difference(sourceTags, translationTags);
            var extraTags = Difference(translationTags, sourceTags);
            if (missingTags.Count > 0)
                problems.Add("missing tag " + Join(missingTags, "<", ">"));
            if (extraTags.Count > 0)
                problems.Add("extra tag " + Join(extraTags, "<", ">"));

            return problems.Count == 0 ? null : string.Join("; ", problems);
        }

        /// <summary>Distinct placeholder names in string.Format syntax; "{{" and "}}" are literal braces.</summary>
        private static List<string> Placeholders(string text)
        {
            var result = new List<string>();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '}' && i + 1 < text.Length && text[i + 1] == '}')
                {
                    i++;
                    continue;
                }

                if (c != '{')
                    continue;

                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    i++;
                    continue;
                }

                int end = text.IndexOf('}', i + 1);
                if (end < 0)
                    break;

                string inner = text.Substring(i + 1, end - i - 1);
                int cut = inner.IndexOfAny(new[] { ':', ',' });
                string name = (cut >= 0 ? inner.Substring(0, cut) : inner).Trim();
                if (name.Length > 0 && !result.Contains(name))
                    result.Add(name);
                i = end;
            }
            return result;
        }

        /// <summary>Rich-text tag names, one per occurrence; closing tags keep their '/'.</summary>
        private static List<string> Tags(string text)
        {
            var result = new List<string>();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '<')
                    continue;

                int start = i + 1;
                bool closing = start < text.Length && text[start] == '/';
                if (closing)
                    start++;

                int end = start;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '-'))
                    end++;

                // A tag name starts with a letter and ends at '>', '=' or a space: "a < b" is not a tag.
                if (end == start || !char.IsLetter(text[start]) || end >= text.Length ||
                    (text[end] != '>' && text[end] != '=' && text[end] != ' '))
                    continue;

                if (text.IndexOf('>', end) < 0)
                    continue;

                string name = text.Substring(start, end - start).ToLowerInvariant();
                result.Add(closing ? "/" + name : name);
            }
            return result;
        }

        /// <summary>Items of <paramref name="a"/> not matched in <paramref name="b"/>, counting repeats.</summary>
        private static List<string> Difference(List<string> a, List<string> b)
        {
            var remaining = new List<string>(b);
            var result = new List<string>();
            foreach (string item in a)
            {
                int index = remaining.IndexOf(item);
                if (index >= 0)
                    remaining.RemoveAt(index);
                else
                    result.Add(item);
            }
            return result;
        }

        private static string Join(List<string> items, string open, string close)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append(open).Append(items[i]).Append(close);
            }
            return builder.ToString();
        }
    }
}
