using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using TMPro;
using PicoShot.Localization.Config;
using PicoShot.Localization.Data;
using PicoShot.Localization.Editor.Data;
using PicoShot.Localization.Rtl;
using Styles = PicoShot.Localization.Editor.LocalizationEditorStyles;

namespace PicoShot.Localization.Editor.Tabs
{
    /// <summary>
    /// Tab for managing project languages and language-specific fonts.
    /// </summary>
    public sealed partial class LocalizationTab : LocalizationEditorTabBase
    {
        private enum LocalizationSubTab
        {
            Languages,
            Fonts
        }

        private static readonly string[] SubTabNames = { "Languages", "Fonts" };
        private LocalizationSubTab _activeSubTab = LocalizationSubTab.Languages;

        private static readonly HashSet<string> CommonLanguages = new(StringComparer.OrdinalIgnoreCase)
        {
            "en", "fr", "de", "es", "it", "pt-br", "pt-pt", "ru", "ja", "ko", "zh-hans", "zh-hant",
            "ar", "tr", "pl", "nl", "sv", "th", "vi", "id", "uk", "hi"
        };

        private static List<KeyValuePair<string, string>> _sortedLanguages;
        private static readonly Dictionary<string, string> NativeNameCache = new(StringComparer.OrdinalIgnoreCase);
        private static float _nameColumnWidth;

        private const float RowPadding = 6f;
        private const float MenuButtonWidth = 20f;
        private const float AddButtonWidth = 52f;

        private SearchField _searchField;
        private bool _showAllLanguages;

        private int _statsVersion = -1;
        private int _statsLanguageCount = -1;
        private readonly Dictionary<string, int> _translatedCounts = new(StringComparer.OrdinalIgnoreCase);
        private int _totalTranslated;

        private string _removedCode;
        private int _removedIndex;
        private int _removedTranslatedCount;
        private Dictionary<string, object> _removedSnapshot;

        public LocalizationTab(LocalizationEditor editor, LanguageEditorData data) : base(editor, data) { }

        public override string TabName => "Localization";

        public override void OnEnter()
        {
            _statsVersion = -1;
        }

        public override void Draw()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            {
                DrawSectionHeader("Localization");
                DrawSubTabToolbar();

                EditorGUILayout.Space(5);

                switch (_activeSubTab)
                {
                    case LocalizationSubTab.Languages:
                        DrawLanguagesSubTab();
                        break;
                    case LocalizationSubTab.Fonts:
                        DrawFontsSubTab();
                        break;
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSubTabToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            int selected = GUILayout.Toolbar((int)_activeSubTab, SubTabNames, EditorStyles.toolbarButton);
            if (selected != (int)_activeSubTab)
            {
                _activeSubTab = (LocalizationSubTab)selected;
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
        }

        #region Languages Sub-Tab

        private void DrawLanguagesSubTab()
        {
            // Row hover highlights need repaints on mouse move
            Editor.wantsMouseMove = true;
            if (Event.current.type == EventType.MouseMove)
                Editor.Repaint();

            EnsureStats();
            DrawUndoRemovalBanner();

            Data.LanguageScrollPos = EditorGUILayout.BeginScrollView(Data.LanguageScrollPos, GUILayout.ExpandHeight(true));
            {
                DrawProjectLanguages();
                EditorGUILayout.Space(12);
                DrawAddLanguage();
                EditorGUILayout.Space(4);
            }
            EditorGUILayout.EndScrollView();

            DrawLanguagesFooter();
        }

        private void EnsureStats()
        {
            if (_statsVersion == Data.DataVersion && _statsLanguageCount == Data.LanguageCodes.Count)
                return;

            _translatedCounts.Clear();
            _totalTranslated = 0;
            foreach (var lang in Data.LanguageCodes)
            {
                int count = Data.CountTranslated(lang);
                _translatedCounts[lang] = count;
                _totalTranslated += count;
            }

            _statsVersion = Data.DataVersion;
            _statsLanguageCount = Data.LanguageCodes.Count;
        }

        private int GetTranslatedCount(string code)
        {
            return _translatedCounts.TryGetValue(code, out int count) ? count : 0;
        }

        #region Project Languages

        private void DrawProjectLanguages()
        {
            int count = Data.LanguageCodes.Count;
            Styles.DrawSectionTitle("Project Languages", count == 1 ? "1 language" : $"{count} languages");

            string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;
            var languages = GetOrderedProjectLanguages(defaultLang);

            for (int i = 0; i < languages.Count; i++)
            {
                DrawProjectLanguageRow(languages[i], i, defaultLang);
            }

            if (count <= 1)
            {
                GUILayout.Label("Only the default language is set up. Add the languages you want to ship from the list below.",
                    Styles.EmptyState);
            }
        }

        /// <summary>
        /// Project languages with the default first, then by name.
        /// </summary>
        private List<string> GetOrderedProjectLanguages(string defaultLang)
        {
            return Data.LanguageCodes
                .OrderByDescending(code => string.Equals(code, defaultLang, StringComparison.OrdinalIgnoreCase))
                .ThenBy(code => LanguageDefinitions.GetDisplayName(code), StringComparer.CurrentCulture)
                .ToList();
        }

        private void DrawProjectLanguageRow(string code, int index, string defaultLang)
        {
            var rowRect = GUILayoutUtility.GetRect(0f, Styles.RowHeight, GUILayout.ExpandWidth(true));
            var evt = Event.current;
            bool hover = rowRect.Contains(evt.mousePosition);
            bool isDefault = string.Equals(code, defaultLang, StringComparison.OrdinalIgnoreCase);

            Styles.DrawRowBackground(rowRect, index, hover);

            var content = new Rect(rowRect.x + RowPadding, rowRect.y, rowRect.width - RowPadding * 2f, rowRect.height);
            float right = content.xMax;

            // Options menu
            var menuRect = new Rect(right - MenuButtonWidth, content.y + 2f, MenuButtonWidth, content.height - 4f);
            right = menuRect.x - 6f;

            // Progress
            float progressWidth = content.width >= 460f ? 150f : content.width >= 340f ? 100f : 0f;
            if (progressWidth > 0f)
            {
                var progressRect = new Rect(right - progressWidth, content.y, progressWidth, content.height);
                DrawLanguageProgress(progressRect, code);
                right = progressRect.x - 10f;
            }

            // Badges
            if (isDefault)
            {
                float w = Styles.GetBadgeWidth("DEFAULT");
                Styles.DrawBadge(new Rect(right - w, content.y, w, content.height), "DEFAULT", Styles.Accent,
                    "Source language. Other languages fall back to it.");
                right -= w + 4f;
            }

            if (LanguageDefinitions.IsRightToLeft(code))
            {
                float w = Styles.GetBadgeWidth("RTL");
                Styles.DrawBadge(new Rect(right - w, content.y, w, content.height), "RTL", Styles.MutedText,
                    "Right-to-left language");
                right -= w + 4f;
            }

            DrawLanguageLabels(new Rect(content.x, content.y, right - content.x - 4f, content.height), code, isDefault);

            if (GUI.Button(menuRect, EditorGUIUtility.IconContent("_Menu"), EditorStyles.iconButton))
            {
                ShowLanguageMenu(code, isDefault, menuRect);
            }

            if (evt.type == EventType.ContextClick && rowRect.Contains(evt.mousePosition))
            {
                ShowLanguageMenu(code, isDefault, null);
                evt.Use();
            }
        }

        private void DrawLanguageProgress(Rect rect, string code)
        {
            int total = Data.Keys.Count;
            if (total == 0)
            {
                GUI.Label(rect, "No keys yet", Styles.MutedLabelRight);
                return;
            }

            int translated = GetTranslatedCount(code);
            Styles.DrawProgressBar(rect, (float)translated / total, $"{translated} / {total} keys translated");
        }

        /// <summary>
        /// Draws "Name  code  NativeName" left to right, dropping columns that don't fit.
        /// </summary>
        private static void DrawLanguageLabels(Rect rect, string code, bool bold)
        {
            if (rect.width <= 0f)
                return;

            string name = LanguageDefinitions.GetDisplayName(code);
            string native = GetNativeName(code);
            var nameStyle = bold ? Styles.RowLabelBold : Styles.RowLabel;

            // Fixed column width so code and native name line up across every row of both lists
            float nameWidth = Mathf.Min(GetNameColumnWidth(), Mathf.Max(rect.width * 0.5f, 80f));
            var nameRect = new Rect(rect.x, rect.y, Mathf.Min(nameWidth, rect.width), rect.height);
            GUI.Label(nameRect, new GUIContent(name, name), nameStyle);

            float x = nameRect.xMax + 6f;
            const float codeWidth = 56f;
            if (rect.xMax - x < codeWidth)
                return;

            GUI.Label(new Rect(x, rect.y, codeWidth, rect.height), code, Styles.MutedLabel);
            x += codeWidth + 4f;

            if (!string.IsNullOrEmpty(native) && rect.xMax - x > 40f)
                GUI.Label(new Rect(x, rect.y, rect.xMax - x, rect.height), native, Styles.MutedLabel);
        }

        /// <summary>
        /// Width of the longest language name in bold, measured once.
        /// </summary>
        private static float GetNameColumnWidth()
        {
            if (_nameColumnWidth > 0f)
                return _nameColumnWidth;

            var content = new GUIContent();
            foreach (var name in LanguageDefinitions.LanguageNames.Values)
            {
                content.text = name;
                _nameColumnWidth = Mathf.Max(_nameColumnWidth, Styles.RowLabelBold.CalcSize(content).x);
            }

            _nameColumnWidth = Mathf.Ceil(_nameColumnWidth) + 8f;
            return _nameColumnWidth;
        }

        private void ShowLanguageMenu(string code, bool isDefault, Rect? dropDownRect)
        {
            var menu = new GenericMenu();
            string name = LanguageDefinitions.GetDisplayName(code);
            int total = Data.Keys.Count;
            int untranslated = total - GetTranslatedCount(code);

            if (isDefault)
                menu.AddDisabledItem(new GUIContent("Set as Default Language"), true);
            else
                menu.AddItem(new GUIContent("Set as Default Language"), false, () => SetDefaultLanguage(code));

            var untranslatedLabel = new GUIContent(untranslated > 0
                ? $"Show Untranslated Keys ({untranslated})"
                : "Show Untranslated Keys");
            if (untranslated > 0)
                menu.AddItem(untranslatedLabel, false, () => Editor.ShowUntranslatedKeys(code));
            else
                menu.AddDisabledItem(untranslatedLabel);

            string filePath = LocalizationManager.GetLanguageFilePath(code);
            if (System.IO.File.Exists(filePath))
                menu.AddItem(new GUIContent("Reveal Locale File"), false, () => EditorUtility.RevealInFinder(filePath));
            else
                menu.AddDisabledItem(new GUIContent("Reveal Locale File (not saved yet)"));

            menu.AddItem(new GUIContent("Copy Language Code"), false, () => EditorGUIUtility.systemCopyBuffer = code);

            menu.AddSeparator("");

            if (isDefault)
                menu.AddDisabledItem(new GUIContent("Remove Language (default can't be removed)"));
            else
                menu.AddItem(new GUIContent($"Remove {name}…"), false, () => RemoveLanguage(code));

            if (dropDownRect.HasValue)
                menu.DropDown(dropDownRect.Value);
            else
                menu.ShowAsContext();
        }

        private void SetDefaultLanguage(string code)
        {
            var config = LocalizationConfigProvider.Config;
            config.SetDefaultLanguage(code);
            LocalizationConfigProvider.SaveConfig();
            Editor.ShowNotification(new GUIContent($"{LanguageDefinitions.GetDisplayName(code)} is now the default language"));
            Editor.Repaint();
        }

        private void RemoveLanguage(string code)
        {
            string name = LanguageDefinitions.GetDisplayName(code);
            int translated = GetTranslatedCount(code);
            string fileName = System.IO.Path.GetFileName(LocalizationManager.GetLanguageFilePath(code));

            if (translated > 0 && !EditorUtility.DisplayDialog(
                    $"Remove {name}?",
                    $"{name} ({code}) has {translated} translated {(translated == 1 ? "string" : "strings")} that will be removed.\n\n" +
                    $"The locale file \"{fileName}\" is deleted when you save. Until then you can undo this.",
                    "Remove", "Cancel"))
            {
                return;
            }

            var snapshot = Data.CaptureLanguage(code);
            int index = Data.LanguageCodes.IndexOf(code);

            if (!Data.RemoveLanguage(code))
                return;

            _removedCode = code;
            _removedIndex = index;
            _removedTranslatedCount = translated;
            _removedSnapshot = snapshot;
            Editor.Repaint();
        }

        private void DrawUndoRemovalBanner()
        {
            if (_removedCode == null)
                return;

            if (Data.LanguageCodes.Contains(_removedCode))
            {
                ClearRemovedLanguage();
                return;
            }

            string name = LanguageDefinitions.GetDisplayName(_removedCode);
            bool pending = Data.PendingRemovedLanguages.Contains(_removedCode);
            string message = pending
                ? $"Removed {name} ({_removedTranslatedCount} translated). Its file is deleted on save."
                : $"Removed {name} and deleted its locale file.";

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            {
                GUILayout.Label(EditorGUIUtility.IconContent("console.infoicon.sml"), GUILayout.Width(18), GUILayout.Height(18));
                GUILayout.Label(message, EditorStyles.wordWrappedMiniLabel, GUILayout.MinHeight(18));

                if (GUILayout.Button(new GUIContent("Undo", "Restore the language and its translations"), EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    if (Data.RestoreLanguage(_removedCode, _removedIndex, _removedSnapshot))
                        Editor.ShowNotification(new GUIContent($"Restored {name}"));
                    ClearRemovedLanguage();
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button(new GUIContent("×", "Dismiss"), EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    ClearRemovedLanguage();
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        private void ClearRemovedLanguage()
        {
            _removedCode = null;
            _removedSnapshot = null;
        }

        #endregion

        #region Add Language

        private void DrawAddLanguage()
        {
            var available = GetSortedLanguages()
                .Where(lang => !Data.LanguageCodes.Contains(lang.Key))
                .ToList();

            Styles.DrawSectionTitle("Add Language", $"{available.Count} available");

            string filter = Data.LanguageFilter ?? string.Empty;
            bool searching = filter.Trim().Length > 0;

            var results = available
                .Where(lang => searching ? MatchesSearch(lang.Key, lang.Value, filter.Trim()) : _showAllLanguages || CommonLanguages.Contains(lang.Key))
                .ToList();

            DrawAddLanguageToolbar(results);

            if (results.Count == 0)
            {
                string message = available.Count == 0
                    ? "Every supported language is already in the project."
                    : searching
                        ? $"No languages match \"{filter.Trim()}\"."
                        : "All common languages are already added. Switch to \"All\" to see the rest.";
                GUILayout.Label(message, Styles.EmptyState);
                return;
            }

            for (int i = 0; i < results.Count; i++)
            {
                DrawAvailableLanguageRow(results[i].Key, results[i].Value, i);
            }
        }

        private void DrawAddLanguageToolbar(List<KeyValuePair<string, string>> results)
        {
            _searchField ??= new SearchField();

            var evt = Event.current;
            bool submit = _searchField.HasFocus() && evt.type == EventType.KeyDown &&
                          (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                var searchRect = GUILayoutUtility.GetRect(100f, 400f, 18f, 18f, EditorStyles.toolbarSearchField, GUILayout.ExpandWidth(true));
                searchRect.y += 1f;
                Data.LanguageFilter = _searchField.OnToolbarGUI(searchRect, Data.LanguageFilter);

                GUILayout.Space(6);

                bool searching = !string.IsNullOrWhiteSpace(Data.LanguageFilter);
                using (new EditorGUI.DisabledScope(searching))
                {
                    bool common = GUILayout.Toggle(!_showAllLanguages, new GUIContent("Common", "Languages most games ship with"),
                        EditorStyles.toolbarButton, GUILayout.Width(64));
                    bool all = GUILayout.Toggle(_showAllLanguages, new GUIContent("All", "Every supported language"),
                        EditorStyles.toolbarButton, GUILayout.Width(40));

                    if (common && _showAllLanguages)
                        _showAllLanguages = false;
                    else if (all && !_showAllLanguages)
                        _showAllLanguages = true;
                }
            }
            EditorGUILayout.EndHorizontal();

            // Enter adds the only match
            if (submit && results.Count == 1)
            {
                AddLanguage(results[0].Key);
                Data.LanguageFilter = string.Empty;
                evt.Use();
            }

            EditorGUILayout.Space(2);
        }

        private void DrawAvailableLanguageRow(string code, string name, int index)
        {
            var rowRect = GUILayoutUtility.GetRect(0f, Styles.RowHeight, GUILayout.ExpandWidth(true));
            var evt = Event.current;
            bool hover = rowRect.Contains(evt.mousePosition);

            Styles.DrawRowBackground(rowRect, index, hover);

            var content = new Rect(rowRect.x + RowPadding, rowRect.y, rowRect.width - RowPadding * 2f, rowRect.height);
            var addRect = new Rect(content.xMax - AddButtonWidth, content.y + 3f, AddButtonWidth, content.height - 6f);
            float right = addRect.x - 8f;

            if (LanguageDefinitions.IsRightToLeft(code))
            {
                float w = Styles.GetBadgeWidth("RTL");
                Styles.DrawBadge(new Rect(right - w, content.y, w, content.height), "RTL", Styles.MutedText, "Right-to-left language");
                right -= w + 4f;
            }

            DrawLanguageLabels(new Rect(content.x, content.y, right - content.x - 4f, content.height), code, false);

            if (GUI.Button(addRect, new GUIContent("Add", $"Add {name} to the project"), EditorStyles.miniButton))
            {
                AddLanguage(code);
                GUIUtility.ExitGUI();
            }

            if (evt.type == EventType.MouseDown && evt.clickCount == 2 && evt.button == 0 && rowRect.Contains(evt.mousePosition))
            {
                AddLanguage(code);
                evt.Use();
            }
        }

        private void AddLanguage(string code)
        {
            if (!Data.AddLanguage(code))
                return;

            GUI.FocusControl(null);
            Editor.ShowNotification(new GUIContent($"Added {LanguageDefinitions.GetDisplayName(code)}"));
            Editor.Repaint();
        }

        private static bool MatchesSearch(string code, string name, string search)
        {
            return name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   code.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (LanguageDefinitions.NativeLanguageNames.TryGetValue(code, out var native) &&
                    native.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static List<KeyValuePair<string, string>> GetSortedLanguages()
        {
            return _sortedLanguages ??= LanguageDefinitions.LanguageNames
                .OrderBy(lang => lang.Value, StringComparer.CurrentCulture)
                .ToList();
        }

        /// <summary>
        /// Native name prepared for IMGUI display (RTL scripts are shaped), or null when it matches the English name.
        /// </summary>
        private static string GetNativeName(string code)
        {
            if (NativeNameCache.TryGetValue(code, out var cached))
                return cached;

            string native = null;
            if (LanguageDefinitions.NativeLanguageNames.TryGetValue(code, out var raw) &&
                !string.IsNullOrEmpty(raw) &&
                !string.Equals(raw, LanguageDefinitions.GetDisplayName(code), StringComparison.OrdinalIgnoreCase))
            {
                native = LanguageDefinitions.IsRightToLeft(code) ? RtlTextHandler.Fix(raw) : raw;
            }

            NativeNameCache[code] = native;
            return native;
        }

        #endregion

        private void DrawLanguagesFooter()
        {
            Styles.DrawSeparator(0f, 2f);

            int languages = Data.LanguageCodes.Count;
            int keys = Data.Keys.Count;
            string summary = $"{languages} {(languages == 1 ? "language" : "languages")}  ·  {keys} {(keys == 1 ? "key" : "keys")}";

            if (keys > 0 && languages > 0)
            {
                float overall = (float)_totalTranslated / ((long)keys * languages);
                summary += $"  ·  {Mathf.FloorToInt(overall * 100f)}% translated overall";
            }

            int pendingRemovals = Data.PendingRemovedLanguages.Count(code => !Data.LanguageCodes.Contains(code));

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(summary, Styles.MutedLabel);
            GUILayout.FlexibleSpace();
            if (pendingRemovals > 0)
            {
                GUILayout.Label(new GUIContent($"{pendingRemovals} locale {(pendingRemovals == 1 ? "file" : "files")} deleted on save",
                    "Removed languages keep their files until you save"), Styles.MutedLabelRight);
            }
            EditorGUILayout.EndHorizontal();
        }

        #endregion
    }
}
