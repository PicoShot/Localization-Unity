using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using TMPro;
using PicoShot.Localization.Config;
using PicoShot.Localization.Data;
using PicoShot.Localization.Rtl;
using Object = UnityEngine.Object;
using Styles = PicoShot.Localization.Editor.LocalizationEditorStyles;

namespace PicoShot.Localization.Editor.Tabs
{
    public sealed partial class LocalizationTab
    {
        private const string ShowLegacyFontsPref = "PicoShot_Localization_ShowLegacyFonts";
        private const float CoverageColumnWidth = 92f;
        private const float FieldHeight = 18f;
        private const float ObjectPickerWidth = 19f;
        private const int MaxMissingInTooltip = 40;
        private const int PreviewSampleCount = 3;
        private const int PreviewSampleMaxLength = 80;

        private static readonly Regex RichTextTag = new("<[^<>]+>", RegexOptions.Compiled);

        private Vector2 _fontsScrollPos;
        private string _previewLanguage;
        private GUIStyle _previewStyle;
        private readonly Dictionary<string, FontCoverage> _coverageCache = new(StringComparer.OrdinalIgnoreCase);

        private sealed class FontCoverage
        {
            public int DataVersion;
            public TMP_FontAsset TmpFont;
            public Font LegacyFont;
            public bool TmpChecked;
            public bool LegacyChecked;
            public List<int> TmpMissing = new();
            public List<int> LegacyMissing = new();
            public int DynamicCount;
            public List<int> UsedCharacters = new();

            public int MissingCount => (TmpChecked ? TmpMissing.Count : 0) + (LegacyChecked ? LegacyMissing.Count : 0);
        }

        private struct FontRowLayout
        {
            public Rect Name;
            public Rect Tmp;
            public Rect Legacy;
            public Rect Coverage;
            public Rect Menu;
        }

        private static bool ShowLegacyPref
        {
            get => EditorPrefs.GetBool(ShowLegacyFontsPref, false);
            set => EditorPrefs.SetBool(ShowLegacyFontsPref, value);
        }

        #region Fonts Sub-Tab

        private void DrawFontsSubTab()
        {
            var config = LocalizationConfigProvider.Config;

            Editor.wantsMouseMove = true;
            if (Event.current.type == EventType.MouseMove)
                Editor.Repaint();

            bool legacyInUse = IsLegacyFontInUse(config);
            bool showLegacy = legacyInUse || ShowLegacyPref;

            DrawFontsHeader(config, legacyInUse, showLegacy);

            if (!config.IsFontSystemEnabled)
            {
                DrawDisabledFontState(config);
                return;
            }

            _fontsScrollPos = EditorGUILayout.BeginScrollView(_fontsScrollPos, GUILayout.ExpandHeight(true));
            {
                DrawDefaultFontsSection(config, showLegacy);
                EditorGUILayout.Space(12);
                DrawLanguageFontsSection(config, showLegacy);
                DrawOrphanedFontMappings(config);
                EditorGUILayout.Space(12);
                DrawFontPreview(config, showLegacy);
                EditorGUILayout.Space(4);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawFontsHeader(LocalizationConfig config, bool legacyInUse, bool showLegacy)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                GUILayout.Label(config.IsFontSystemEnabled
                        ? "Fonts swap automatically when the language changes."
                        : "Font system is off. Text components keep their own fonts.",
                    Styles.MutedLabel);
                GUILayout.FlexibleSpace();

                if (config.IsFontSystemEnabled)
                {
                    using (new EditorGUI.DisabledScope(legacyInUse))
                    {
                        var legacyContent = new GUIContent("Legacy Text Fonts", legacyInUse
                            ? "Shown because a legacy font is assigned"
                            : "Also assign fonts for Unity UI Text (non-TextMesh Pro) components");
                        bool newShowLegacy = GUILayout.Toggle(showLegacy, legacyContent, EditorStyles.toolbarButton);
                        if (!legacyInUse && newShowLegacy != showLegacy)
                            ShowLegacyPref = newShowLegacy;
                    }
                }

                var systemContent = new GUIContent(config.IsFontSystemEnabled ? "Font System: On" : "Font System: Off",
                    "Swap TMP and Text fonts per language at runtime");
                bool enabled = GUILayout.Toggle(config.IsFontSystemEnabled, systemContent, EditorStyles.toolbarButton);
                if (enabled != config.IsFontSystemEnabled)
                    SetFontSystemEnabled(config, enabled);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        private static void SetFontSystemEnabled(LocalizationConfig config, bool enabled)
        {
            Undo.RecordObject(config, enabled ? "Enable Font System" : "Disable Font System");
            config.SetFontSystemEnabled(enabled);
            LocalizationConfigProvider.SaveConfig();
        }

        private static void DrawDisabledFontState(LocalizationConfig config)
        {
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(GUILayout.MaxWidth(340));
            {
                var titleStyle = new GUIStyle(Styles.SectionTitle) { alignment = TextAnchor.MiddleCenter };
                GUILayout.Label("Language-Specific Fonts", titleStyle);
                GUILayout.Label("Assign a font per language (for example CJK or Arabic fonts) and text components switch to it automatically when the language changes.",
                    Styles.EmptyState);

                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Enable Font System", GUILayout.Width(160), GUILayout.Height(26)))
                    SetFontSystemEnabled(config, true);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
        }

        private static bool IsLegacyFontInUse(LocalizationConfig config)
        {
            return config.DefaultLegacyFont != null || config.FontMappings.Any(m => m.legacyFont != null);
        }

        #endregion

        #region Default Fonts

        private void DrawDefaultFontsSection(LocalizationConfig config, bool showLegacy)
        {
            Styles.DrawSectionTitle("Default Fonts", "Used by every language without an override");

            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 90f;

            EditorGUI.BeginChangeCheck();
            var newTmp = (TMP_FontAsset)EditorGUILayout.ObjectField(
                new GUIContent("TMP Font", "Fallback font for TextMesh Pro components"),
                config.DefaultTMPFont, typeof(TMP_FontAsset), false);

            var newLegacy = config.DefaultLegacyFont;
            if (showLegacy)
            {
                newLegacy = (Font)EditorGUILayout.ObjectField(
                    new GUIContent("Legacy Font", "Fallback font for Unity UI Text components"),
                    config.DefaultLegacyFont, typeof(Font), false);
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(config, "Change Default Font");
                config.SetDefaultFonts(newTmp, newLegacy);
                LocalizationConfigProvider.SaveConfig();
            }

            EditorGUIUtility.labelWidth = oldLabelWidth;
        }

        #endregion

        #region Language Fonts

        private void DrawLanguageFontsSection(LocalizationConfig config, bool showLegacy)
        {
            int overrides = config.FontMappings.Count(m => Data.LanguageCodes.Contains(m.languageCode) && (m.tmpFont != null || m.legacyFont != null));
            Styles.DrawSectionTitle("Language Fonts", overrides == 1 ? "1 override" : $"{overrides} overrides");

            string defaultLang = config.DefaultLanguage;
            var languages = GetOrderedProjectLanguages(defaultLang);

            if (_previewLanguage == null || !Data.LanguageCodes.Contains(_previewLanguage))
                _previewLanguage = languages.FirstOrDefault(code => !string.Equals(code, defaultLang, StringComparison.OrdinalIgnoreCase)) ?? defaultLang;

            DrawFontColumnHeaders(showLegacy);

            for (int i = 0; i < languages.Count; i++)
            {
                DrawLanguageFontRow(config, languages[i], i, showLegacy);
            }
        }

        private FontRowLayout LayoutFontRow(Rect rowRect, bool showLegacy)
        {
            var content = new Rect(rowRect.x + RowPadding, rowRect.y, rowRect.width - RowPadding * 2f, rowRect.height);
            var layout = new FontRowLayout();

            layout.Menu = new Rect(content.xMax - MenuButtonWidth, content.y + 2f, MenuButtonWidth, content.height - 4f);
            layout.Coverage = new Rect(layout.Menu.x - 6f - CoverageColumnWidth, content.y, CoverageColumnWidth, content.height);

            float nameWidth = Mathf.Min(GetNameColumnWidth() + 50f, content.width * 0.35f);
            layout.Name = new Rect(content.x, content.y, nameWidth, content.height);

            float fieldsX = layout.Name.xMax + 6f;
            float fieldsWidth = Mathf.Max(0f, layout.Coverage.x - 8f - fieldsX);
            float fieldY = content.y + (content.height - FieldHeight) * 0.5f;

            if (showLegacy)
            {
                float half = (fieldsWidth - 6f) * 0.5f;
                layout.Tmp = new Rect(fieldsX, fieldY, half, FieldHeight);
                layout.Legacy = new Rect(fieldsX + half + 6f, fieldY, half, FieldHeight);
            }
            else
            {
                layout.Tmp = new Rect(fieldsX, fieldY, fieldsWidth, FieldHeight);
            }

            return layout;
        }

        private void DrawFontColumnHeaders(bool showLegacy)
        {
            var rect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
            var layout = LayoutFontRow(rect, showLegacy);

            GUI.Label(new Rect(layout.Name.x, rect.y, layout.Name.width, rect.height), "Language", Styles.MutedLabel);
            GUI.Label(new Rect(layout.Tmp.x, rect.y, layout.Tmp.width, rect.height), "TMP Font", Styles.MutedLabel);
            if (showLegacy)
                GUI.Label(new Rect(layout.Legacy.x, rect.y, layout.Legacy.width, rect.height), "Legacy Font", Styles.MutedLabel);
            GUI.Label(new Rect(layout.Coverage.x, rect.y, layout.Coverage.width, rect.height),
                new GUIContent("Coverage", "Whether the font has every character used by this language's translations"), Styles.MutedLabel);
        }

        private void DrawLanguageFontRow(LocalizationConfig config, string code, int index, bool showLegacy)
        {
            var rowRect = GUILayoutUtility.GetRect(0f, Styles.RowHeight, GUILayout.ExpandWidth(true));
            var layout = LayoutFontRow(rowRect, showLegacy);
            var evt = Event.current;
            bool hover = rowRect.Contains(evt.mousePosition);
            bool selected = string.Equals(code, _previewLanguage, StringComparison.OrdinalIgnoreCase);

            Styles.DrawRowBackground(rowRect, index, hover, selected);

            // Clicking the row (outside its controls) selects it for the preview
            if (evt.type == EventType.MouseDown && evt.button == 0 && hover &&
                !layout.Tmp.Contains(evt.mousePosition) &&
                !(showLegacy && layout.Legacy.Contains(evt.mousePosition)) &&
                !layout.Menu.Contains(evt.mousePosition))
            {
                _previewLanguage = code;
                GUI.FocusControl(null);
                evt.Use();
            }

            DrawFontRowLabel(layout.Name, code, string.Equals(code, config.DefaultLanguage, StringComparison.OrdinalIgnoreCase));

            var mapping = GetFontMapping(config, code);

            EditorGUI.BeginChangeCheck();
            var newTmp = DrawInheritableFontField(layout.Tmp, mapping.tmpFont, config.DefaultTMPFont);
            var newLegacy = showLegacy
                ? DrawInheritableFontField(layout.Legacy, mapping.legacyFont, config.DefaultLegacyFont)
                : mapping.legacyFont;

            if (EditorGUI.EndChangeCheck())
                SetFontOverride(config, code, newTmp, newLegacy);

            var coverage = GetCoverage(config, code, showLegacy);
            DrawCoverageCell(layout.Coverage, config, code, coverage, showLegacy);

            if (GUI.Button(layout.Menu, EditorGUIUtility.IconContent("_Menu"), EditorStyles.iconButton))
                ShowFontMenu(config, code, coverage, layout.Menu);

            if (evt.type == EventType.ContextClick && hover)
            {
                ShowFontMenu(config, code, coverage, null);
                evt.Use();
            }
        }

        private static void DrawFontRowLabel(Rect rect, string code, bool isDefault)
        {
            const float codeWidth = 46f;
            var nameStyle = isDefault ? Styles.RowLabelBold : Styles.RowLabel;
            string name = LanguageDefinitions.GetDisplayName(code);

            float nameWidth = Mathf.Max(0f, Mathf.Min(GetNameColumnWidth(), rect.width - codeWidth));
            GUI.Label(new Rect(rect.x, rect.y, nameWidth, rect.height), new GUIContent(name, name), nameStyle);
            GUI.Label(new Rect(rect.x + nameWidth + 4f, rect.y, Mathf.Max(0f, rect.width - nameWidth - 4f), rect.height), code, Styles.MutedLabel);
        }

        /// <summary>
        /// Object field that shows the inherited default in muted text while no override is set.
        /// </summary>
        private static T DrawInheritableFontField<T>(Rect rect, T value, T inherited) where T : Object
        {
            var result = (T)EditorGUI.ObjectField(rect, GUIContent.none, value, typeof(T), false);

            bool dragging = DragAndDrop.objectReferences.Length > 0 && rect.Contains(Event.current.mousePosition);
            if (value == null && !dragging && Event.current.type == EventType.Repaint)
            {
                var textRect = new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - ObjectPickerWidth), rect.height);
                var content = inherited != null
                    ? new GUIContent($"Default · {inherited.name}", "No override. Drop a font here to override the default.")
                    : new GUIContent("None", "No override and no default font");
                Styles.InheritedField.Draw(textRect, content, false, false, false, false);
            }

            return result;
        }

        private static LanguageFontMapping GetFontMapping(LocalizationConfig config, string code)
        {
            foreach (var mapping in config.FontMappings)
            {
                if (string.Equals(mapping.languageCode, code, StringComparison.OrdinalIgnoreCase))
                    return mapping;
            }
            return default;
        }

        private static void SetFontOverride(LocalizationConfig config, string code, TMP_FontAsset tmpFont, Font legacyFont)
        {
            Undo.RecordObject(config, "Change Language Font");
            if (tmpFont == null && legacyFont == null)
                config.RemoveFontMapping(code);
            else
                config.SetFontMapping(code, tmpFont, legacyFont);
            LocalizationConfigProvider.SaveConfig();
        }

        private void ShowFontMenu(LocalizationConfig config, string code, FontCoverage coverage, Rect? dropDownRect)
        {
            var menu = new GenericMenu();
            var mapping = GetFontMapping(config, code);
            bool hasOverride = mapping.tmpFont != null || mapping.legacyFont != null;

            if (hasOverride)
                menu.AddItem(new GUIContent("Use Default Fonts"), false, () => SetFontOverride(config, code, null, null));
            else
                menu.AddDisabledItem(new GUIContent("Use Default Fonts"), true);

            var tmpFont = mapping.tmpFont != null ? mapping.tmpFont : config.DefaultTMPFont;
            if (tmpFont != null)
                menu.AddItem(new GUIContent($"Select {tmpFont.name}"), false, () =>
                {
                    Selection.activeObject = tmpFont;
                    EditorGUIUtility.PingObject(tmpFont);
                });

            menu.AddItem(new GUIContent("Preview This Language"), false, () => _previewLanguage = code);

            menu.AddSeparator("");

            int missing = coverage?.MissingCount ?? 0;
            if (missing > 0)
                menu.AddItem(new GUIContent($"Copy Missing Characters ({DistinctMissing(coverage).Count})"), false,
                    () => EditorGUIUtility.systemCopyBuffer = ToCharacterString(DistinctMissing(coverage)));
            else
                menu.AddDisabledItem(new GUIContent("Copy Missing Characters"));

            int used = coverage?.UsedCharacters.Count ?? 0;
            if (used > 0)
                menu.AddItem(new GUIContent($"Copy Used Characters ({used})"), false,
                    () => EditorGUIUtility.systemCopyBuffer = ToCharacterString(coverage.UsedCharacters));
            else
                menu.AddDisabledItem(new GUIContent("Copy Used Characters"));

            menu.AddItem(new GUIContent("Recheck Coverage"), false, () =>
            {
                _coverageCache.Clear();
                Editor.Repaint();
            });

            if (dropDownRect.HasValue)
                menu.DropDown(dropDownRect.Value);
            else
                menu.ShowAsContext();
        }

        private void DrawOrphanedFontMappings(LocalizationConfig config)
        {
            var orphans = config.FontMappings
                .Where(m => !Data.LanguageCodes.Contains(m.languageCode))
                .Select(m => m.languageCode)
                .ToList();

            if (orphans.Count == 0)
                return;

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            {
                GUILayout.Label(EditorGUIUtility.IconContent("console.warnicon.sml"), GUILayout.Width(18), GUILayout.Height(18));
                string list = string.Join(", ", orphans);
                GUILayout.Label(orphans.Count == 1
                        ? $"1 font override belongs to a language that isn't in the project ({list})."
                        : $"{orphans.Count} font overrides belong to languages that aren't in the project ({list}).",
                    EditorStyles.wordWrappedMiniLabel, GUILayout.MinHeight(18));

                if (GUILayout.Button(new GUIContent("Clean Up", "Remove these unused overrides"), EditorStyles.miniButton, GUILayout.Width(64)))
                {
                    Undo.RecordObject(config, "Remove Unused Font Overrides");
                    foreach (var code in orphans)
                        config.RemoveFontMapping(code);
                    LocalizationConfigProvider.SaveConfig();
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Coverage

        private void DrawCoverageCell(Rect rect, LocalizationConfig config, string code, FontCoverage coverage, bool showLegacy)
        {
            if (coverage == null)
            {
                GUI.Label(rect, new GUIContent("No font", "No TMP font is assigned, so text components keep their own font."), Styles.MutedLabel);
                return;
            }

            if (coverage.UsedCharacters.Count == 0)
            {
                GUI.Label(rect, new GUIContent("No text", "This language has no translations to check yet."), Styles.MutedLabel);
                return;
            }

            var mapping = GetFontMapping(config, code);
            var tooltip = new StringBuilder();
            AppendCoverageTooltip(tooltip, "TMP", mapping.tmpFont != null ? mapping.tmpFont : config.DefaultTMPFont, coverage.TmpChecked, coverage.TmpMissing);
            if (showLegacy)
                AppendCoverageTooltip(tooltip, "Legacy", mapping.legacyFont != null ? mapping.legacyFont : config.DefaultLegacyFont, coverage.LegacyChecked, coverage.LegacyMissing);
            if (coverage.DynamicCount > 0)
                tooltip.Append($"\n{coverage.DynamicCount} characters are not in the atlas yet and are added at runtime from the source font.");

            int missing = DistinctMissing(coverage).Count;
            if (missing == 0)
                Styles.DrawBadge(rect, "OK", Styles.Success, tooltip.ToString().Trim());
            else
                Styles.DrawBadge(rect, $"{missing} missing", Styles.Warning, tooltip.ToString().Trim());
        }

        private static void AppendCoverageTooltip(StringBuilder sb, string label, Object font, bool isChecked, List<int> missing)
        {
            if (!isChecked || font == null)
            {
                sb.AppendLine($"{label}: no font assigned");
                return;
            }

            if (missing.Count == 0)
            {
                sb.AppendLine($"{label} ({font.name}): all characters available");
                return;
            }

            var shown = ToCharacterString(missing.Take(MaxMissingInTooltip), " ");
            string more = missing.Count > MaxMissingInTooltip ? $" … +{missing.Count - MaxMissingInTooltip} more" : string.Empty;
            sb.AppendLine($"{label} ({font.name}): {missing.Count} missing\n{shown}{more}");
        }

        private static List<int> DistinctMissing(FontCoverage coverage)
        {
            var set = new SortedSet<int>();
            if (coverage.TmpChecked)
                set.UnionWith(coverage.TmpMissing);
            if (coverage.LegacyChecked)
                set.UnionWith(coverage.LegacyMissing);
            return set.ToList();
        }

        private static string ToCharacterString(IEnumerable<int> codepoints, string separator = "")
        {
            return string.Join(separator, codepoints.Select(char.ConvertFromUtf32));
        }

        /// <summary>
        /// Returns cached coverage for a language, recomputing when translations or fonts change.
        /// Null when no font would be applied.
        /// </summary>
        private FontCoverage GetCoverage(LocalizationConfig config, string code, bool checkLegacy)
        {
            var mapping = GetFontMapping(config, code);
            var tmpFont = mapping.tmpFont != null ? mapping.tmpFont : config.DefaultTMPFont;
            var legacyFont = checkLegacy ? (mapping.legacyFont != null ? mapping.legacyFont : config.DefaultLegacyFont) : null;

            if (tmpFont == null && legacyFont == null)
                return null;

            if (_coverageCache.TryGetValue(code, out var cached) &&
                cached.DataVersion == Data.DataVersion && cached.TmpFont == tmpFont && cached.LegacyFont == legacyFont)
                return cached;

            var coverage = new FontCoverage { DataVersion = Data.DataVersion, TmpFont = tmpFont, LegacyFont = legacyFont };
            var used = CollectUsedCharacters(code);
            coverage.UsedCharacters = used.OrderBy(c => c).ToList();

            if (tmpFont != null)
            {
                coverage.TmpChecked = true;
                coverage.TmpMissing = FindMissingTmpCharacters(tmpFont, used, out coverage.DynamicCount);
            }

            if (legacyFont != null)
            {
                coverage.LegacyChecked = true;
                coverage.LegacyMissing = coverage.UsedCharacters
                    .Where(c => c > char.MaxValue || !legacyFont.HasCharacter((char)c))
                    .ToList();
            }

            _coverageCache[code] = coverage;
            return coverage;
        }

        /// <summary>
        /// Every renderable character in a language's translations, after rich-text tags are
        /// stripped and RTL text is shaped (so Arabic is checked in its presentation forms).
        /// </summary>
        private HashSet<int> CollectUsedCharacters(string code)
        {
            var result = new HashSet<int>();
            bool rtl = LanguageDefinitions.IsRightToLeft(code);

            foreach (var key in Data.Keys)
            {
                if (!Data.LanguageData.TryGetValue(key, out var keyData) || !keyData.TryGetValue(code, out var value))
                    continue;

                switch (value)
                {
                    case string str:
                        CollectCharacters(str, rtl, result);
                        break;
                    case IList<string> list:
                        foreach (var item in list)
                            CollectCharacters(item, rtl, result);
                        break;
                }
            }

            return result;
        }

        private static void CollectCharacters(string text, bool rtl, HashSet<int> into)
        {
            if (string.IsNullOrEmpty(text))
                return;

            text = RichTextTag.Replace(text, string.Empty);
            if (rtl)
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

                if (!IsIgnorableCharacter(codepoint))
                    into.Add(codepoint);
            }
        }

        private static bool IsIgnorableCharacter(int codepoint)
        {
            if (codepoint <= char.MaxValue && (char.IsWhiteSpace((char)codepoint) || char.IsControl((char)codepoint)))
                return true;

            return codepoint is >= 0x200B and <= 0x200F  // zero-width spaces and direction marks
                or >= 0x202A and <= 0x202E                // bidi embedding controls
                or >= 0x2060 and <= 0x206F                // invisible operators
                or >= 0xFE00 and <= 0xFE0F                // variation selectors
                or 0xFEFF;
        }

        /// <summary>
        /// Checks characters against a TMP font, its fallback chain and the global TMP fallbacks.
        /// Characters missing from the atlas but present in a dynamic font's source file count as covered.
        /// </summary>
        private static List<int> FindMissingTmpCharacters(TMP_FontAsset font, HashSet<int> characters, out int dynamicCount)
        {
            dynamicCount = 0;
            var remaining = new HashSet<int>(characters);
            var chain = GetFallbackChain(font);

            foreach (var asset in chain)
            {
                var table = asset.characterLookupTable;
                if (table != null)
                    remaining.RemoveWhere(c => table.ContainsKey((uint)c));
                if (remaining.Count == 0)
                    return new List<int>();
            }

            foreach (var asset in chain)
            {
                if (asset.atlasPopulationMode == AtlasPopulationMode.Static || asset.sourceFontFile == null)
                    continue;

                if (FontEngine.LoadFontFace(asset.sourceFontFile) != FontEngineError.Success)
                    continue;

                int before = remaining.Count;
                remaining.RemoveWhere(c => FontEngine.TryGetGlyphWithUnicodeValue((uint)c, GlyphLoadFlags.LOAD_NO_BITMAP, out Glyph _));
                dynamicCount += before - remaining.Count;

                if (remaining.Count == 0)
                    break;
            }

            return remaining.OrderBy(c => c).ToList();
        }

        private static List<TMP_FontAsset> GetFallbackChain(TMP_FontAsset font)
        {
            var chain = new List<TMP_FontAsset>();
            var visited = new HashSet<TMP_FontAsset>();
            var queue = new Queue<TMP_FontAsset>();
            queue.Enqueue(font);

            // Avoid TMP_Settings.instance when the settings asset is missing; it prompts to import TMP resources
            if (Resources.Load<TMP_Settings>("TMP Settings") != null && TMP_Settings.fallbackFontAssets != null)
            {
                foreach (var globalFallback in TMP_Settings.fallbackFontAssets)
                    queue.Enqueue(globalFallback);
            }

            while (queue.Count > 0)
            {
                var asset = queue.Dequeue();
                if (asset == null || !visited.Add(asset))
                    continue;

                chain.Add(asset);
                if (asset.fallbackFontAssetTable == null)
                    continue;

                foreach (var fallback in asset.fallbackFontAssetTable)
                    queue.Enqueue(fallback);
            }

            return chain;
        }

        #endregion

        #region Preview

        private void DrawFontPreview(LocalizationConfig config, bool showLegacy)
        {
            if (string.IsNullOrEmpty(_previewLanguage))
                return;

            string name = LanguageDefinitions.GetDisplayName(_previewLanguage);
            var mapping = GetFontMapping(config, _previewLanguage);
            var tmpFont = mapping.tmpFont != null ? mapping.tmpFont : config.DefaultTMPFont;
            var legacyFont = mapping.legacyFont != null ? mapping.legacyFont : config.DefaultLegacyFont;

            Font previewFont = tmpFont != null && tmpFont.sourceFontFile != null ? tmpFont.sourceFontFile
                : showLegacy || tmpFont == null ? legacyFont : null;

            string caption = previewFont != null
                ? $"{name} · {(tmpFont != null && previewFont == tmpFont.sourceFontFile ? tmpFont.name : previewFont.name)}"
                : name;
            Styles.DrawSectionTitle("Preview", caption);

            if (previewFont == null)
            {
                GUILayout.Label(tmpFont != null
                        ? $"{tmpFont.name} has no source font file, so it can't be previewed here."
                        : "Assign a font to preview this language.",
                    Styles.EmptyState);
                return;
            }

            _previewStyle ??= new GUIStyle(EditorStyles.label) { fontSize = 18, wordWrap = true, richText = false };
            _previewStyle.font = previewFont;
            _previewStyle.alignment = LanguageDefinitions.IsRightToLeft(_previewLanguage) ? TextAnchor.UpperRight : TextAnchor.UpperLeft;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                foreach (var sample in GetPreviewSamples(_previewLanguage))
                    GUILayout.Label(sample, _previewStyle);
            }
            EditorGUILayout.EndVertical();

            GUILayout.Label("Approximate: the editor may fill missing glyphs from system fonts. Trust the Coverage column.", Styles.MutedLabel);
        }

        private IEnumerable<string> GetPreviewSamples(string code)
        {
            bool rtl = LanguageDefinitions.IsRightToLeft(code);
            var samples = new List<string>();

            if (LanguageDefinitions.NativeLanguageNames.TryGetValue(code, out var native) && !string.IsNullOrEmpty(native))
                samples.Add(native);

            foreach (var key in Data.Keys)
            {
                if (samples.Count > PreviewSampleCount)
                    break;

                if (!Data.LanguageData.TryGetValue(key, out var keyData) || !keyData.TryGetValue(code, out var value) || value is not string text)
                    continue;

                text = RichTextTag.Replace(text, string.Empty).Replace('\n', ' ').Trim();
                if (text.Length == 0 || samples.Contains(text))
                    continue;

                if (text.Length > PreviewSampleMaxLength)
                    text = text.Substring(0, PreviewSampleMaxLength).TrimEnd() + "…";
                samples.Add(text);
            }

            return rtl ? samples.Select(s => RtlTextHandler.Fix(s)) : samples;
        }

        #endregion
    }
}
