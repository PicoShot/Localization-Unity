using System;
using System.Collections.Generic;
using System.Text;

namespace PicoShot.Localization.Rtl
{
    /// <summary>
    /// Bridges the logical text used by localization and the visual text expected by
    /// TextMeshPro when its native RTL mode is disabled.
    /// </summary>
    internal static class RtlTextMeshProHandler
    {
        private const int PlaceholderStart = 0xE000;
        private const int PlaceholderEnd = 0xF8FF;

        private static readonly HashSet<string> StandaloneTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "br", "nbsp", "page", "shy", "space", "sprite", "zwsp"
        };

        internal sealed class MarkupState
        {
            internal readonly List<Tag> ActiveTags = new();
        }

        internal static string ShapeForMeasurement(
            string logicalText,
            bool supportMixedText,
            bool isMainRtl,
            bool richTextEnabled)
        {
            if (string.IsNullOrEmpty(logicalText)) return logicalText;

            if (!richTextEnabled || logicalText.IndexOf('<') < 0)
                return RtlTextHandler.Shape(logicalText, supportMixedText, isMainRtl);

            ProtectedText protectedText = ProtectTags(
                logicalText,
                swapPairedTags: false,
                preserveJoiningAcrossTags: true);
            string shaped = RtlTextHandler.Shape(protectedText.Text, supportMixedText, isMainRtl);
            return RestoreTags(shaped, protectedText.Replacements);
        }

        internal static string ReverseMeasuredLine(
            string shapedLine,
            bool supportMixedText,
            bool isMainRtl,
            bool richTextEnabled,
            MarkupState markupState)
        {
            shapedLine = RemoveLineBreaks(shapedLine);

            if (!richTextEnabled || shapedLine.IndexOf('<') < 0 && markupState.ActiveTags.Count == 0)
            {
                return supportMixedText
                    ? RtlTextHandler.ReverseMixed(shapedLine, isMainRtl)
                    : RtlTextHandler.Reverse(shapedLine);
            }

            string balancedLine = BalanceLineMarkup(shapedLine, markupState);
            ProtectedText protectedText = ProtectTags(
                balancedLine,
                swapPairedTags: true,
                preserveJoiningAcrossTags: false);
            string reversed = supportMixedText
                ? RtlTextHandler.ReverseMixed(protectedText.Text, isMainRtl)
                : RtlTextHandler.Reverse(protectedText.Text);
            return RestoreTags(reversed, protectedText.Replacements);
        }

        private static string BalanceLineMarkup(string line, MarkupState state)
        {
            var output = new StringBuilder(line.Length + state.ActiveTags.Count * 16);
            for (int i = 0; i < state.ActiveTags.Count; i++)
                output.Append(state.ActiveTags[i].Raw);

            for (int i = 0; i < line.Length;)
            {
                if (!TryReadTag(line, i, out Tag tag))
                {
                    output.Append(line[i]);
                    i++;
                    continue;
                }

                // TMP already reported <br> as a line boundary. The wrapper inserts
                // its own newline, so retaining the tag would create a blank line.
                if (!string.Equals(tag.Name, "br", StringComparison.OrdinalIgnoreCase))
                    output.Append(tag.Raw);
                UpdateActiveTags(state.ActiveTags, tag);
                i += tag.Raw.Length;
            }

            for (int i = state.ActiveTags.Count - 1; i >= 0; i--)
                output.Append(state.ActiveTags[i].ClosingText);

            return output.ToString();
        }

        private static ProtectedText ProtectTags(
            string text,
            bool swapPairedTags,
            bool preserveJoiningAcrossTags)
        {
            var output = new StringBuilder(text.Length);
            var replacements = new Dictionary<char, string>();
            var openTags = new List<Tag>();
            int placeholderCandidate = PlaceholderStart;
            int punctuationCandidate = 0x2000;

            for (int i = 0; i < text.Length;)
            {
                if (!TryReadTag(text, i, out Tag tag))
                {
                    output.Append(text[i]);
                    i++;
                    continue;
                }

                bool useTransparentPlaceholder = preserveJoiningAcrossTags &&
                                                 !tag.IsStandalone &&
                                                 HasRtlLettersOnBothSides(text, i, tag.Raw.Length);
                char placeholder = useTransparentPlaceholder
                    ? FindPlaceholder(text, replacements, ref placeholderCandidate)
                    : preserveJoiningAcrossTags
                        ? FindPunctuationPlaceholder(text, replacements, ref punctuationCandidate)
                        : FindPlaceholder(text, replacements, ref placeholderCandidate);
                string replacement = tag.Raw;

                if (swapPairedTags && !tag.IsStandalone)
                {
                    if (tag.IsClosing)
                    {
                        int matchingIndex = FindMatchingTag(openTags, tag.Name);
                        if (matchingIndex >= 0)
                        {
                            replacement = openTags[matchingIndex].Raw;
                            openTags.RemoveAt(matchingIndex);
                        }
                    }
                    else
                    {
                        openTags.Add(tag);
                        replacement = tag.ClosingText;
                    }
                }

                replacements.Add(placeholder, replacement);
                output.Append(placeholder);
                i += tag.Raw.Length;
            }

            return new ProtectedText(output.ToString(), replacements);
        }

        private static string RestoreTags(string text, Dictionary<char, string> replacements)
        {
            if (replacements.Count == 0) return text;

            var output = new StringBuilder(text.Length + replacements.Count * 8);
            for (int i = 0; i < text.Length; i++)
            {
                if (replacements.TryGetValue(text[i], out string replacement))
                    output.Append(replacement);
                else
                    output.Append(text[i]);
            }
            return output.ToString();
        }

        private static char FindPlaceholder(
            string source,
            Dictionary<char, string> replacements,
            ref int candidate)
        {
            while (candidate <= PlaceholderEnd)
            {
                char value = (char)candidate++;
                if (source.IndexOf(value) < 0 && !replacements.ContainsKey(value))
                    return value;
            }

            throw new InvalidOperationException("RTL rich text contains too many tags to process safely.");
        }

        private static char FindPunctuationPlaceholder(
            string source,
            Dictionary<char, string> replacements,
            ref int candidate)
        {
            while (candidate < char.MaxValue)
            {
                char value = (char)candidate++;
                if (char.IsPunctuation(value) &&
                    source.IndexOf(value) < 0 &&
                    !replacements.ContainsKey(value))
                {
                    return value;
                }
            }

            throw new InvalidOperationException("RTL rich text contains too many boundary tags to process safely.");
        }

        private static bool HasRtlLettersOnBothSides(string text, int tagIndex, int tagLength)
        {
            int previousIndex = FindVisibleCharacter(text, tagIndex - 1, step: -1);
            int nextIndex = FindVisibleCharacter(text, tagIndex + tagLength, step: 1);
            return previousIndex >= 0 && nextIndex >= 0 &&
                   IsRtlLetter(text[previousIndex]) && IsRtlLetter(text[nextIndex]);
        }

        private static int FindVisibleCharacter(string text, int index, int step)
        {
            while (index >= 0 && index < text.Length)
            {
                if (text[index] == '<' && TryReadTag(text, index, out Tag forwardTag))
                {
                    index += forwardTag.Raw.Length;
                    continue;
                }

                if (text[index] == '>')
                {
                    int openingIndex = text.LastIndexOf('<', index);
                    if (openingIndex >= 0 && TryReadTag(text, openingIndex, out _))
                    {
                        index = openingIndex - 1;
                        continue;
                    }
                }

                return index;
            }
            return -1;
        }

        private static bool IsRtlLetter(char value)
        {
            return value >= 0x0590 && value <= 0x08FF ||
                   value >= 0xFB1D && value <= 0xFDFF ||
                   value >= 0xFE70 && value <= 0xFEFF;
        }

        private static void UpdateActiveTags(List<Tag> activeTags, Tag tag)
        {
            if (tag.IsStandalone) return;

            if (!tag.IsClosing)
            {
                activeTags.Add(tag);
                return;
            }

            int matchingIndex = FindMatchingTag(activeTags, tag.Name);
            if (matchingIndex >= 0)
                activeTags.RemoveRange(matchingIndex, activeTags.Count - matchingIndex);
        }

        private static int FindMatchingTag(List<Tag> tags, string name)
        {
            for (int i = tags.Count - 1; i >= 0; i--)
            {
                if (string.Equals(tags[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static bool TryReadTag(string text, int startIndex, out Tag tag)
        {
            tag = default;
            if (startIndex >= text.Length || text[startIndex] != '<') return false;

            int endIndex = FindTagEnd(text, startIndex + 1);
            if (endIndex < 0) return false;

            string raw = text.Substring(startIndex, endIndex - startIndex + 1);
            int cursor = 1;
            while (cursor < raw.Length - 1 && char.IsWhiteSpace(raw[cursor])) cursor++;

            bool isClosing = cursor < raw.Length - 1 && raw[cursor] == '/';
            if (isClosing) cursor++;

            int nameStart = cursor;
            while (cursor < raw.Length - 1 &&
                   !char.IsWhiteSpace(raw[cursor]) &&
                   raw[cursor] != '=' && raw[cursor] != '/' && raw[cursor] != '>')
            {
                cursor++;
            }

            if (cursor == nameStart) return false;

            string name = raw.Substring(nameStart, cursor - nameStart);
            if (name[0] == '#') name = "color";

            bool isStandalone = raw.EndsWith("/>", StringComparison.Ordinal) || StandaloneTags.Contains(name);
            tag = new Tag(raw, name, isClosing, isStandalone);
            return true;
        }

        private static int FindTagEnd(string text, int startIndex)
        {
            char quote = '\0';
            for (int i = startIndex; i < text.Length; i++)
            {
                char value = text[i];
                if (quote != '\0')
                {
                    if (value == quote) quote = '\0';
                    continue;
                }

                if (value == '\'' || value == '"')
                {
                    quote = value;
                    continue;
                }

                if (value == '>') return i;
            }
            return -1;
        }

        private static string RemoveLineBreaks(string value)
        {
            return value.IndexOfAny(new[] { '\r', '\n' }) < 0
                ? value
                : value.Replace("\r", string.Empty).Replace("\n", string.Empty);
        }

        internal readonly struct Tag
        {
            internal readonly string Raw;
            internal readonly string Name;
            internal readonly bool IsClosing;
            internal readonly bool IsStandalone;

            internal Tag(string raw, string name, bool isClosing, bool isStandalone)
            {
                Raw = raw;
                Name = name;
                IsClosing = isClosing;
                IsStandalone = isStandalone;
            }

            internal string ClosingText => $"</{Name}>";
        }

        private readonly struct ProtectedText
        {
            internal readonly string Text;
            internal readonly Dictionary<char, string> Replacements;

            internal ProtectedText(string text, Dictionary<char, string> replacements)
            {
                Text = text;
                Replacements = replacements;
            }
        }
    }
}
