using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using PicoShot.Localization.Config;
using PicoShot.Localization.Data;
using PicoShot.Localization.Editor.Data;
using PicoShot.Localization.Editor.Services;
using PicoShot.Localization.Rtl;
using Styles = PicoShot.Localization.Editor.LocalizationEditorStyles;

namespace PicoShot.Localization.Editor.Tabs
{
    /// <summary>
    /// Project-wide tools (character sets, translation health, bulk translation) and runtime debugging.
    /// </summary>
    public sealed class ToolsTab : LocalizationEditorTabBase
    {
        private enum ToolsSubTab
        {
            Tools,
            Debug
        }

        private enum ResultState
        {
            None,
            Found,
            Missing,
            Error
        }

        private const string StripTagsPref = "PicoShot_Localization_Charset_StripTags";
        private const string ShapeRtlPref = "PicoShot_Localization_Charset_ShapeRtl";
        private const string IncludeAsciiPref = "PicoShot_Localization_Charset_IncludeAscii";
        private const int CharsetPreviewLength = 120;
        private const int DuplicateTextMenuLength = 40;

        private static readonly string[] SubTabNames = { "Tools", "Debug" };

        private readonly TranslationService _translationService;
        private ToolsSubTab _activeSubTab = ToolsSubTab.Tools;
        private Vector2 _toolsScroll;
        private Vector2 _debugScroll;
        private float _contentWidth = 400f;
        private float _pendingContentWidth;

        private int _charsetVersion = -1;
        private string _collectOptions;
        private string _charsetOptions;
        private readonly Dictionary<string, HashSet<int>> _languageCharacters = new();
        private List<int> _charset = new();

        private int _healthVersion = -1;
        private string _healthDefaultLanguage;
        private int _missingKeys;
        private int _problemKeys;
        private int _emptySourceKeys;
        private readonly List<(string text, List<string> keys)> _duplicateGroups = new();

        private string _bulkTarget;
        private bool _bulkRunning;
        private bool _bulkCancel;
        private int _bulkDone;
        private int _bulkTotal;
        private string _bulkCurrentKey;

        private string _resultCacheKey;
        private ResultState _resultState;
        private string _result = "";
        private string _resultNote = "";
        private int _reloadCount;
        private string _rtlInput;
        private string _rtlOutput = "";

        public ToolsTab(LocalizationEditor editor, LanguageEditorData data) : base(editor, data)
        {
            _translationService = new TranslationService(data);
        }

        public override string TabName => "Tools";

        private static string DefaultLanguage => LocalizationConfigProvider.Config.DefaultLanguage;

        public override void OnEnter()
        {
            _resultCacheKey = null;
        }

        public override void Draw()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            {

                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                int selected = GUILayout.Toolbar((int)_activeSubTab, SubTabNames, EditorStyles.toolbarButton);
                if (selected != (int)_activeSubTab)
                {
                    _activeSubTab = (ToolsSubTab)selected;
                    GUIUtility.keyboardControl = 0;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);

                if (_activeSubTab == ToolsSubTab.Tools)
                    DrawToolsSubTab();
                else
                    DrawDebugSubTab();
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Tracks the usable width for wrapping; applied on Layout so Layout and Repaint stay consistent.
        /// </summary>
        private void TrackContentWidth()
        {
            var probe = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
            var evt = Event.current;

            if (evt.type == EventType.Repaint && probe.width > 1f && Mathf.Abs(probe.width - _pendingContentWidth) > 0.5f)
            {
                _pendingContentWidth = probe.width;
                Editor.Repaint();
            }
            else if (evt.type == EventType.Layout && _pendingContentWidth > 1f)
            {
                _contentWidth = _pendingContentWidth;
            }
        }

        #region Tools Sub-Tab

        private void DrawToolsSubTab()
        {
            _toolsScroll = EditorGUILayout.BeginScrollView(_toolsScroll, GUILayout.ExpandHeight(true));
            {
                TrackContentWidth();
                DrawCharacterSetCard();
                EditorGUILayout.Space(14);
                DrawHealthCard();
                EditorGUILayout.Space(14);
                DrawBulkTranslateCard();
                EditorGUILayout.Space(6);
            }
            EditorGUILayout.EndScrollView();
        }

        #region Character Set

        private static bool StripTags
        {
            get => EditorPrefs.GetBool(StripTagsPref, true);
            set => EditorPrefs.SetBool(StripTagsPref, value);
        }

        private static bool ShapeRtl
        {
            get => EditorPrefs.GetBool(ShapeRtlPref, true);
            set => EditorPrefs.SetBool(ShapeRtlPref, value);
        }

        private static bool IncludeAscii
        {
            get => EditorPrefs.GetBool(IncludeAsciiPref, true);
            set => EditorPrefs.SetBool(IncludeAsciiPref, value);
        }

        private void DrawCharacterSetCard()
        {
            Styles.DrawSectionTitle("Character Set", "Characters to bake into static TMP font assets");

            Data.SyncCharsetLanguageSelection();
            EnsureCharset();

            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Label("Languages", Styles.MutedLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(40)))
                    SetAllCharsetLanguages(true);
                if (GUILayout.Button("None", EditorStyles.miniButtonRight, GUILayout.Width(44)))
                    SetAllCharsetLanguages(false);
            }
            EditorGUILayout.EndHorizontal();

            DrawLanguageChips();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            {
                bool strip = EditorGUILayout.ToggleLeft(new GUIContent("Strip rich-text tags", "Ignore characters inside tags like <color=red>"),
                    StripTags, GUILayout.Width(140));
                bool shape = EditorGUILayout.ToggleLeft(new GUIContent("Shape RTL text", "Use the joined letter forms Arabic text is drawn with"),
                    ShapeRtl, GUILayout.Width(115));
                bool ascii = EditorGUILayout.ToggleLeft(new GUIContent("Include ASCII", "Always include A-Z, a-z, 0-9 and punctuation, e.g. for player names"),
                    IncludeAscii, GUILayout.Width(110));

                if (strip != StripTags) StripTags = strip;
                if (shape != ShapeRtl) ShapeRtl = shape;
                if (ascii != IncludeAscii) IncludeAscii = ascii;
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUILayout.Label($"{_charset.Count:N0} unique characters", EditorStyles.boldLabel);

                string preview = CharacterSetCollector.ToText(_charset.Take(CharsetPreviewLength));
                if (_charset.Count > CharsetPreviewLength)
                    preview += " …";
                GUILayout.Label(preview.Length > 0 ? preview : "Select at least one language.", Styles.EmptyState);

                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(_charset.Count == 0))
                {
                    if (GUILayout.Button("Copy", EditorStyles.miniButtonLeft, GUILayout.Width(60)))
                        CopyToClipboard(CharacterSetCollector.ToText(_charset), $"Copied {_charset.Count:N0} characters");
                    if (GUILayout.Button(new GUIContent("Copy as Unicode Ranges", "Hex ranges for TMP Font Asset Creator > Unicode Range (Hex)"),
                            EditorStyles.miniButtonMid, GUILayout.Width(150)))
                        CopyToClipboard(CharacterSetCollector.ToUnicodeRanges(_charset), "Copied Unicode ranges");
                    if (GUILayout.Button(new GUIContent("Save .txt…", "For TMP Font Asset Creator > Characters from File"),
                            EditorStyles.miniButtonRight, GUILayout.Width(80)))
                        SaveCharset();
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Language toggles that wrap onto new lines to fit the window.
        /// </summary>
        private void DrawLanguageChips()
        {
            float x = 0f;
            EditorGUILayout.BeginHorizontal();
            foreach (var lang in Data.GetLanguagesDefaultFirst())
            {
                int count = _languageCharacters.TryGetValue(lang, out var set) ? set.Count : 0;
                string name = LanguageDefinitions.GetDisplayName(lang);
                var content = new GUIContent($"{name}  {count:N0}", $"{count:N0} unique characters in {name}");
                float width = EditorStyles.miniButton.CalcSize(content).x + 8f;

                if (x > 0f && x + width > _contentWidth)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    x = 0f;
                }

                bool selected = Data.LanguageSelectionForCharset[lang];
                bool newSelected = GUILayout.Toggle(selected, content, EditorStyles.miniButton, GUILayout.Width(width - 4f));
                if (newSelected != selected)
                    Data.LanguageSelectionForCharset[lang] = newSelected;

                x += width;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void SetAllCharsetLanguages(bool selected)
        {
            foreach (var lang in Data.LanguageCodes)
                Data.LanguageSelectionForCharset[lang] = selected;
        }

        private void EnsureCharset()
        {
            bool strip = StripTags;
            bool shape = ShapeRtl;
            bool ascii = IncludeAscii;

            string collectOptions = $"{strip}|{shape}";
            bool languagesChanged = false;
            if (_charsetVersion != Data.DataVersion || _collectOptions != collectOptions)
            {
                _languageCharacters.Clear();
                foreach (var lang in Data.LanguageCodes)
                    _languageCharacters[lang] = CharacterSetCollector.CollectLanguage(Data, lang, strip, shape);
                _charsetVersion = Data.DataVersion;
                _collectOptions = collectOptions;
                languagesChanged = true;
            }

            string selection = string.Join(",", Data.LanguageCodes.Where(l => Data.LanguageSelectionForCharset.TryGetValue(l, out var on) && on));
            string options = $"{ascii}|{selection}";
            if (!languagesChanged && options == _charsetOptions)
                return;

            var union = new HashSet<int>();
            if (selection.Length > 0)
            {
                foreach (var lang in selection.Split(','))
                {
                    if (_languageCharacters.TryGetValue(lang, out var set))
                        union.UnionWith(set);
                }
                union.Add(' ');
            }

            if (ascii)
            {
                for (int c = 0x20; c <= 0x7E; c++)
                    union.Add(c);
            }

            _charset = union.OrderBy(c => c).ToList();
            _charsetOptions = options;
        }

        private void SaveCharset()
        {
            string path = EditorUtility.SaveFilePanel("Save Character Set", Application.dataPath, "charset.txt", "txt");
            if (string.IsNullOrEmpty(path))
                return;

            File.WriteAllText(path, CharacterSetCollector.ToText(_charset), new UTF8Encoding(false));
            if (Path.GetFullPath(path).StartsWith(Path.GetFullPath(Application.dataPath), StringComparison.OrdinalIgnoreCase))
                AssetDatabase.Refresh();

            Editor.ShowNotification(new GUIContent($"Saved {_charset.Count:N0} characters"));
            GUIUtility.ExitGUI();
        }

        #endregion

        #region Translation Health

        private void DrawHealthCard()
        {
            EnsureHealth();

            int issues = (_missingKeys > 0 ? 1 : 0) + (_problemKeys > 0 ? 1 : 0) + (_emptySourceKeys > 0 ? 1 : 0) + (_duplicateGroups.Count > 0 ? 1 : 0);
            Styles.DrawSectionTitle("Translation Health", Data.Keys.Count == 0 ? "No keys yet" : issues == 0 ? "All good" : $"{Data.Keys.Count:N0} keys checked");

            if (Data.Keys.Count == 0)
                return;

            if (issues == 0)
            {
                var rect = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
                float w = Styles.DrawBadge(new Rect(rect.x + 2f, rect.y, 0f, rect.height), "OK", Styles.Success);
                GUI.Label(new Rect(rect.x + w + 10f, rect.y, rect.width - w - 10f, rect.height),
                    "Every key is translated and all placeholders and tags match.", Styles.RowLabel);
                return;
            }

            int row = 0;
            if (_missingKeys > 0)
                DrawHealthRow(row++, _missingKeys, Styles.Warning, KeysLabel(_missingKeys, "missing translations in at least one language"),
                    "Show", r => Editor.ShowKeysFiltered(KeyStatusFilter.Missing));

            if (_problemKeys > 0)
                DrawHealthRow(row++, _problemKeys, Styles.Danger, KeysLabel(_problemKeys, "with placeholder or rich-text tag mismatches"),
                    "Show", r => Editor.ShowKeysFiltered(KeyStatusFilter.Problems));

            if (_emptySourceKeys > 0)
                DrawHealthRow(row++, _emptySourceKeys, Styles.Warning,
                    KeysLabel(_emptySourceKeys, $"with empty {LanguageDefinitions.GetDisplayName(DefaultLanguage)} source text"),
                    "Show", r => Editor.ShowKeysFiltered(KeyStatusFilter.All, DefaultLanguage));

            if (_duplicateGroups.Count > 0)
            {
                int duplicateKeys = _duplicateGroups.Sum(g => g.keys.Count);
                DrawHealthRow(row, _duplicateGroups.Count, Styles.MutedText,
                    $"{(_duplicateGroups.Count == 1 ? "1 source text is" : $"{_duplicateGroups.Count} source texts are")} shared by {duplicateKeys} keys (possible duplicates)",
                    "Show…", ShowDuplicatesMenu);
            }
        }

        private static string KeysLabel(int count, string suffix) => $"{(count == 1 ? "1 key" : $"{count:N0} keys")} {suffix}";

        private void DrawHealthRow(int index, int count, Color color, string text, string buttonText, Action<Rect> onClick)
        {
            var rect = GUILayoutUtility.GetRect(0f, Styles.RowHeight, GUILayout.ExpandWidth(true));
            Styles.DrawRowBackground(rect, index, rect.Contains(Event.current.mousePosition));

            const float badgeColumn = 52f;
            const float buttonWidth = 60f;
            Styles.DrawBadge(new Rect(rect.x + 6f, rect.y, badgeColumn, rect.height), count.ToString("N0"), color);

            var buttonRect = new Rect(rect.xMax - buttonWidth - 6f, rect.y + 3f, buttonWidth, rect.height - 6f);
            GUI.Label(new Rect(rect.x + 6f + badgeColumn, rect.y, buttonRect.x - rect.x - badgeColumn - 12f, rect.height),
                new GUIContent(text, text), Styles.RowLabel);

            if (GUI.Button(buttonRect, buttonText, EditorStyles.miniButton))
                onClick(buttonRect);
        }

        private void ShowDuplicatesMenu(Rect rect)
        {
            var menu = new GenericMenu();
            foreach (var (text, keys) in _duplicateGroups)
            {
                string label = text.Replace('/', '∕').Replace('\n', ' ');
                if (label.Length > DuplicateTextMenuLength)
                    label = label.Substring(0, DuplicateTextMenuLength) + "…";

                foreach (var key in keys)
                {
                    string captured = key;
                    menu.AddItem(new GUIContent($"\"{label}\" ({keys.Count})/{key}"), false, () => Editor.ShowKey(captured));
                }
            }
            menu.DropDown(rect);
        }

        private void EnsureHealth()
        {
            string defaultLang = DefaultLanguage;
            if (_healthVersion == Data.DataVersion && _healthDefaultLanguage == defaultLang)
                return;

            _missingKeys = _problemKeys = _emptySourceKeys = 0;
            _duplicateGroups.Clear();
            var byText = new Dictionary<string, List<string>>();

            foreach (var key in Data.Keys)
            {
                var status = Data.GetKeyStatus(key);
                if (status.Missing > 0) _missingKeys++;
                if (status.Problems > 0) _problemKeys++;
                if (!Data.IsTranslated(key, defaultLang)) _emptySourceKeys++;

                if (Data.LanguageData[key].TryGetValue(defaultLang, out var value) && value is string text && !string.IsNullOrWhiteSpace(text))
                {
                    text = text.Trim();
                    if (!byText.TryGetValue(text, out var keys))
                        byText[text] = keys = new List<string>();
                    keys.Add(key);
                }
            }

            foreach (var pair in byText.Where(p => p.Value.Count > 1).OrderByDescending(p => p.Value.Count))
                _duplicateGroups.Add((pair.Key, pair.Value));

            _healthVersion = Data.DataVersion;
            _healthDefaultLanguage = defaultLang;
        }

        #endregion

        #region Bulk Translate

        private void DrawBulkTranslateCard()
        {
            string provider = Data.ActiveTranslationProvider.ToString();
            Styles.DrawSectionTitle("Bulk Translate", $"Uses {provider} (change in Settings)");

            string defaultLang = DefaultLanguage;
            var targets = Data.GetLanguagesDefaultFirst().Where(l => l != defaultLang).ToList();
            if (targets.Count == 0)
            {
                GUILayout.Label("Add a second language in the Localization tab first.", Styles.EmptyState);
                return;
            }

            if (_bulkTarget != null && !targets.Contains(_bulkTarget))
                _bulkTarget = null;

            using (new EditorGUI.DisabledScope(_bulkRunning))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Fill missing translations in", Styles.MutedLabel, GUILayout.ExpandWidth(false));
                var names = new[] { "All languages" }.Concat(targets.Select(l => $"{LanguageDefinitions.GetDisplayName(l)} ({l})")).ToArray();
                int index = _bulkTarget == null ? 0 : targets.IndexOf(_bulkTarget) + 1;
                int newIndex = EditorGUILayout.Popup(index, names, GUILayout.MaxWidth(220));
                if (newIndex != index)
                    _bulkTarget = newIndex == 0 ? null : targets[newIndex - 1];
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            var languages = _bulkTarget != null ? new List<string> { _bulkTarget } : targets;
            var plan = GetBulkPlan(languages, out int texts, out int skipped);
            bool hasApiKey = Data.ActiveTranslationProvider == TranslationProvider.DeepL
                ? !string.IsNullOrEmpty(Data.DeeplApiKey)
                : !string.IsNullOrEmpty(Data.GeminiApiKey);

            if (!hasApiKey)
                EditorGUILayout.HelpBox($"No {provider} API key is set. Add one in the Settings tab.", MessageType.Warning);

            EditorGUILayout.Space(2);

            if (_bulkRunning)
            {
                EditorGUILayout.BeginHorizontal();
                var barRect = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
                float progress = _bulkTotal > 0 ? (float)_bulkDone / _bulkTotal : 0f;
                EditorGUI.ProgressBar(barRect, progress, $"{_bulkDone} / {_bulkTotal}   {_bulkCurrentKey}");
                using (new EditorGUI.DisabledScope(_bulkCancel))
                {
                    if (GUILayout.Button(_bulkCancel ? "Stopping…" : "Cancel", EditorStyles.miniButton, GUILayout.Width(70), GUILayout.Height(20)))
                        _bulkCancel = true;
                }
                EditorGUILayout.EndHorizontal();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            {
                string summary = plan.Count == 0
                    ? "Nothing to translate"
                    : $"{(plan.Count == 1 ? "1 key" : $"{plan.Count:N0} keys")} · {texts:N0} {(texts == 1 ? "translation" : "translations")} to fill";
                if (skipped > 0)
                    summary += $" · {skipped:N0} skipped (no source text)";
                GUILayout.Label(summary, Styles.MutedLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(plan.Count == 0 || !hasApiKey))
                {
                    if (GUILayout.Button(new GUIContent("Translate", "One undo step for the whole run"), EditorStyles.miniButton, GUILayout.Width(80)))
                        RunBulkTranslate(plan, _bulkTarget != null ? new[] { _bulkTarget } : null);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private List<string> GetBulkPlan(List<string> languages, out int texts, out int skipped)
        {
            string defaultLang = DefaultLanguage;
            var plan = new List<string>();
            texts = 0;
            skipped = 0;

            foreach (var key in Data.Keys)
            {
                int missing = languages.Count(lang => !Data.IsTranslated(key, lang));
                if (missing == 0)
                    continue;

                if (!Data.IsTranslated(key, defaultLang))
                {
                    skipped++;
                    continue;
                }

                plan.Add(key);
                texts += missing;
            }

            return plan;
        }

        private async void RunBulkTranslate(List<string> keys, string[] onlyLanguages)
        {
            if (_bulkRunning)
                return;

            _bulkRunning = true;
            _bulkCancel = false;
            _bulkDone = 0;
            _bulkTotal = keys.Count;
            int failed = 0;

            Data.History.RecordAll("Bulk Translate");

            try
            {
                foreach (var key in keys)
                {
                    if (_bulkCancel || Editor == null)
                        break;

                    _bulkCurrentKey = key;
                    Editor.Repaint();

                    if (Data.LanguageData.ContainsKey(key))
                    {
                        try
                        {
                            await _translationService.TranslateAndFill(key, onlyLanguages);
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            Debug.LogError($"[Localization] Bulk translate failed for '{key}': {ex.Message}");
                        }
                    }

                    _bulkDone++;
                    Data.MarkKeysChanged();
                }
            }
            finally
            {
                bool cancelled = _bulkCancel;
                _bulkRunning = false;
                _bulkCancel = false;
                _bulkCurrentKey = null;

                if (Editor != null)
                {
                    string message = cancelled ? $"Stopped after {_bulkDone} of {_bulkTotal} keys" : $"Translated {_bulkDone} keys";
                    if (failed > 0)
                        message += $" ({failed} failed, see Console)";
                    Editor.ShowNotification(new GUIContent(message));
                    Editor.Repaint();
                }
            }
        }

        #endregion

        #endregion

        #region Debug Sub-Tab

        private void DrawDebugSubTab()
        {
            _debugScroll = EditorGUILayout.BeginScrollView(_debugScroll, GUILayout.ExpandHeight(true));
            {
                TrackContentWidth();
                DrawRuntimeCard();
                EditorGUILayout.Space(14);
                DrawTryKeyCard();
                EditorGUILayout.Space(14);
                DrawRtlCard();
                EditorGUILayout.Space(6);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawRuntimeCard()
        {
            var controls = Styles.DrawSectionTitleWithControls("Runtime");
            float right = controls.xMax;
            if (Data.HasUnsavedChanges)
            {
                float w = Styles.GetBadgeWidth("UNSAVED");
                Styles.DrawBadge(new Rect(right - w, controls.y, w, controls.height), "UNSAVED", Styles.Warning,
                    "The editor has unsaved changes. Save to test them here.");
                right -= w + 6f;
            }
            GUI.Label(new Rect(controls.x, controls.y, Mathf.Max(0f, right - controls.x), controls.height),
                new GUIContent("Reads the saved locale files", "Lookups here behave exactly like the game: they use the saved .bloc files, not unsaved edits."),
                Styles.MutedLabelRight);

            bool initialized = LocalizationManager.IsInitialized;

            var badgeRect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            float x = badgeRect.x + 2f;
            x += Styles.DrawBadge(new Rect(x, badgeRect.y, 0f, badgeRect.height),
                initialized ? "INITIALIZED" : "NOT INITIALIZED", initialized ? Styles.Success : Styles.MutedText,
                initialized ? "Locale data is loaded" : "Locale data loads on the first lookup") + 4f;
            if (Application.isPlaying)
                x += Styles.DrawBadge(new Rect(x, badgeRect.y, 0f, badgeRect.height), "PLAY MODE", Styles.Accent,
                    "Language changes apply to the running game") + 4f;
            if (initialized && LocalizationManager.IsRightToLeft)
                Styles.DrawBadge(new Rect(x, badgeRect.y, 0f, badgeRect.height), "RTL", Styles.MutedText, "Current language is right-to-left");

            string current = LocalizationManager.CurrentLanguage;
            string system = LanguageDefinitions.FromSystemLanguage(Application.systemLanguage);

            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Label("Language", Styles.MutedLabel, GUILayout.Width(64));
                var content = new GUIContent(string.IsNullOrEmpty(current) ? "Not loaded" : $"{LanguageDefinitions.GetDisplayName(current)} ({current})");
                var dropdownRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true));
                if (EditorGUI.DropdownButton(dropdownRect, content, FocusType.Keyboard))
                    ShowLanguageMenu(dropdownRect, current);

                if (GUILayout.Button(new GUIContent($"Use System ({system ?? "?"})", "Switch to the language detected from the OS"),
                        EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                {
                    LocalizationManager.SetLanguage(LocalizationManager.DetectSystemLanguage());
                    Editor.Repaint();
                }

                if (GUILayout.Button(new GUIContent(initialized ? "Reload" : "Load", "Load the saved locale files again"),
                        EditorStyles.miniButton, GUILayout.Width(56)))
                {
                    LocalizationManager.Reload();
                    _reloadCount++;
                    Editor.Repaint();
                }
            }
            EditorGUILayout.EndHorizontal();

            int available = initialized ? LocalizationManager.GetAvailableLanguageCodes().Count() : 0;
            GUILayout.Label($"Default {LanguageDefinitions.GetDisplayName(LocalizationManager.DefaultLanguage ?? DefaultLanguage)} · " +
                            $"System {LanguageDefinitions.GetDisplayName(system ?? "")} · " +
                            $"{(initialized ? available.ToString() : "?")} languages available", Styles.MutedLabel);
        }

        private void ShowLanguageMenu(Rect rect, string current)
        {
            var menu = new GenericMenu();
            foreach (var lang in LocalizationManager.GetAvailableLanguageCodes())
            {
                string captured = lang;
                menu.AddItem(new GUIContent($"{LanguageDefinitions.GetDisplayName(lang)} ({lang})"), current == lang, () =>
                {
                    LocalizationManager.SetLanguage(captured);
                    GUIUtility.keyboardControl = 0;
                    Editor.Repaint();
                });
            }
            menu.DropDown(rect);
        }

        private void DrawTryKeyCard()
        {
            Styles.DrawSectionTitle("Try a Key", "Resolves a key the way the game does");

            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Label("Key", Styles.MutedLabel, GUILayout.Width(40));
                Data.TestKey = EditorGUILayout.TextField(Data.TestKey);
                var pickRect = GUILayoutUtility.GetRect(new GUIContent("Pick"), EditorStyles.miniPullDown, GUILayout.Width(48));
                if (EditorGUI.DropdownButton(pickRect, new GUIContent("Pick", "Choose from the project keys"), FocusType.Passive, EditorStyles.miniPullDown))
                {
                    var keys = Data.Keys.ToArray();
                    LocalizationSearchablePopup.Show(pickRect, keys, Array.IndexOf(keys, Data.TestKey), i =>
                    {
                        if (i < 0 || i >= keys.Length)
                            return;
                        Data.TestKey = keys[i];
                        GUIUtility.keyboardControl = 0;
                        Editor.Repaint();
                    }, "Select Key", i => Data.GetPreviewText(keys[i]));
                }
            }
            EditorGUILayout.EndHorizontal();

            DrawArguments();
            EnsureTryResult();

            EditorGUILayout.Space(4);

            if (_resultState == ResultState.None)
            {
                GUILayout.Label("Enter or pick a key to see how it resolves.", Styles.EmptyState);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                if (_resultState == ResultState.Found)
                {
                    var style = EditorStyles.wordWrappedLabel;
                    float height = style.CalcHeight(new GUIContent(_result), Mathf.Max(100f, _contentWidth - 16f));
                    EditorGUILayout.SelectableLabel(_result, style, GUILayout.Height(Mathf.Max(EditorGUIUtility.singleLineHeight, height)));
                }

                EditorGUILayout.BeginHorizontal();
                {
                    var noteStyle = _resultState == ResultState.Found ? Styles.MutedLabel : Styles.WarningLabel;
                    GUILayout.Label(new GUIContent(_resultNote, _resultNote), noteStyle);
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(_resultState != ResultState.Found))
                    {
                        if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(50)))
                            CopyToClipboard(_result, "Copied result");
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawArguments()
        {
            var args = Data.ParameterList;
            float x = 44f;
            int remove = -1;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Args", "Values for {0}, {1}, … in the text"), Styles.MutedLabel, GUILayout.Width(40));

            for (int i = 0; i < args.Count; i++)
            {
                const float fieldWidth = 90f;
                if (x + fieldWidth + 22f > _contentWidth && x > 44f)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(44f);
                    x = 44f;
                }

                GUI.SetNextControlName($"Tools.Arg.{i}");
                args[i] = EditorGUILayout.TextField(args[i] ?? "", GUILayout.Width(fieldWidth - 22f));
                if (GUILayout.Button(new GUIContent("×", $"Remove {{{i}}}"), EditorStyles.miniButton, GUILayout.Width(20)))
                    remove = i;
                x += fieldWidth + 4f;
            }

            if (GUILayout.Button(new GUIContent(args.Count == 0 ? "+ Add Argument" : "+", "Add a value for the next {n} placeholder"),
                    EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
            {
                args.Add("");
                GUIUtility.keyboardControl = 0;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (remove >= 0)
            {
                args.RemoveAt(remove);
                GUIUtility.keyboardControl = 0;
            }
        }

        /// <summary>
        /// Looks the key up only when the inputs change, so missing keys don't log every repaint.
        /// </summary>
        private void EnsureTryResult()
        {
            if (Event.current.type != EventType.Layout)
                return;

            string key = Data.TestKey?.Trim() ?? "";
            string cacheKey = $"{key}\u0001{string.Join("\u0001", Data.ParameterList)}\u0001{LocalizationManager.CurrentLanguage}\u0001{_reloadCount}";
            if (cacheKey == _resultCacheKey)
                return;
            _resultCacheKey = cacheKey;

            if (key.Length == 0)
            {
                _resultState = ResultState.None;
                return;
            }

            try
            {
                if (!LocalizationManager.IsInitialized)
                    LocalizationManager.Initialize();

                string current = LocalizationManager.CurrentLanguage;
                string currentName = LanguageDefinitions.GetDisplayName(current ?? "");
                bool inCurrent = LocalizationManager.HasKey(key);
                bool inDefault = LocalizationManager.HasKeyInDefault(key);

                if (!inCurrent && !inDefault)
                {
                    _resultState = ResultState.Missing;
                    _resultNote = Data.LanguageData.ContainsKey(key)
                        ? "This key isn't in the saved files yet. Save, then press Reload."
                        : "Key not found.";
                    return;
                }

                bool isArray = Data.LanguageData.TryGetValue(key, out var keyData) && LanguageEditorData.IsArrayKey(keyData);
                if (isArray)
                {
                    var items = LocalizationManager.GetArray(key) ?? Array.Empty<string>();
                    _result = string.Join("\n", items.Select((item, i) => $"[{i}] {item}"));
                }
                else
                {
                    _result = LocalizationManager.GetText(key, Data.ParameterList.Cast<object>().ToArray());
                }

                _resultState = ResultState.Found;
                _resultNote = inCurrent
                    ? $"From {currentName}"
                    : $"Not in {currentName}, so it falls back to {LanguageDefinitions.GetDisplayName(LocalizationManager.DefaultLanguage ?? "")}";
            }
            catch (Exception ex)
            {
                _resultState = ResultState.Error;
                _resultNote = $"Lookup failed: {ex.Message}";
            }
        }

        private void DrawRtlCard()
        {
            Styles.DrawSectionTitle("RTL Shaping", "Joins and reorders Arabic-script text for display");

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Input", Styles.MutedLabel, GUILayout.Width(40));
            Data.TestRtl = EditorGUILayout.TextField(Data.TestRtl);
            EditorGUILayout.EndHorizontal();

            if (Data.TestRtl != _rtlInput)
            {
                _rtlInput = Data.TestRtl;
                _rtlOutput = string.IsNullOrEmpty(_rtlInput) ? "" : RtlTextHandler.Fix(_rtlInput);
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Output", Styles.MutedLabel, GUILayout.Width(40));
            EditorGUILayout.SelectableLabel(_rtlOutput, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_rtlOutput)))
            {
                if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(50)))
                    CopyToClipboard(_rtlOutput, "Copied shaped text");
            }
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        private void CopyToClipboard(string text, string message)
        {
            EditorGUIUtility.systemCopyBuffer = text;
            Editor.ShowNotification(new GUIContent(message));
        }
    }
}
