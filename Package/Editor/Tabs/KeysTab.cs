using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using PicoShot.Localization.Config;
using PicoShot.Localization.Data;
using PicoShot.Localization.Editor.Data;
using PicoShot.Localization.Editor.Mcp;
using PicoShot.Localization.Editor.Services;
using Styles = PicoShot.Localization.Editor.LocalizationEditorStyles;

namespace PicoShot.Localization.Editor.Tabs
{
    /// <summary>
    /// Tab for browsing, creating and translating keys in a list + editor split view.
    /// </summary>
    public sealed class KeysTab : LocalizationEditorTabBase
    {
        private const string NewKeyNameControl = "Keys.NewKeyName";
        private const string NewKeyValueControl = "Keys.NewKeyValue";
        private const string ValueControlPrefix = "Keys.Value.";
        private const string ElementControlPrefix = "Keys.Element.";
        private const float RowPadding = 6f;
        private const float FieldMinHeight = 20f;
        private const float ArrayIndexWidth = 24f;
        private const float ArrayButtonWidth = 20f;
        private const float IconButtonSize = 20f;

        private static readonly string[] KeyTypeLabels = { "String", "Array" };

        private readonly TranslationService _translationService;
        private readonly JsonService _jsonService;
        private SearchField _searchField;

        private bool _isResizingKeysList;
        private bool _pendingDelete;
        private bool _isTranslating;
        private float _keysListViewportHeight;
        private float _detailsWidth;
        private string _pendingFocusControl;
        private string _arrayTargetLanguage;

        private string _lastValueControl;
        private string _lastValueKey;

        private bool _showNewKeyForm;
        private bool _newKeyIsArray;
        private string _newKey = "";
        private string _newValue = "";

        public KeysTab(LocalizationEditor editor, LanguageEditorData data) : base(editor, data)
        {
            _translationService = new TranslationService(data);
            _jsonService = new JsonService(data);
        }

        public override string TabName => "Keys";

        public override void OnEnter()
        {
            AutoScrollToSelectedKey();
        }

        private static string DefaultLanguage => LocalizationConfigProvider.Config.DefaultLanguage;
        private static string ActionKeyName => Application.platform == RuntimePlatform.OSXEditor ? "Cmd" : "Ctrl";

        public override void Draw()
        {
            Editor.wantsMouseMove = true;
            var evt = Event.current;
            if (evt.type == EventType.MouseMove)
                Editor.Repaint();

            if (evt.type == EventType.KeyDown && EditorGUI.actionKey && evt.keyCode == KeyCode.E && OpenFocusedValueEditor())
                evt.Use();

            if (evt.type == EventType.Repaint)
            {
                string focused = GUI.GetNameOfFocusedControl();
                if (IsValueControl(focused))
                {
                    _lastValueControl = focused;
                    _lastValueKey = Data.SelectedKey;
                }
            }

            if ((evt.type == EventType.ValidateCommand || evt.type == EventType.ExecuteCommand) && evt.commandName == "Find")
            {
                if (evt.type == EventType.ExecuteCommand)
                    _searchField?.SetFocus();
                evt.Use();
            }

            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            {
                DrawToolbar();
                if (_showNewKeyForm)
                    DrawNewKeyForm();

                EditorGUILayout.Space(2);

                EditorGUILayout.BeginHorizontal(
                    GUILayout.ExpandHeight(true),
                    GUILayout.MinHeight(120f),
                    GUILayout.MaxHeight(float.MaxValue));
                DrawKeysListPanel();
                DrawResizeHandle();
                DrawKeyDetailsPanel();
                EditorGUILayout.EndHorizontal();

                DrawStatusBar();
            }
            EditorGUILayout.EndVertical();

            ApplyPendingFocus();
        }

        private void ApplyPendingFocus()
        {
            if (_pendingFocusControl == null || Event.current.type != EventType.Repaint)
                return;

            EditorGUI.FocusTextInControl(_pendingFocusControl);
            _pendingFocusControl = null;
            Editor.Repaint();
        }

        #region Toolbar

        private void DrawToolbar()
        {
            _searchField ??= new SearchField();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                var viewContent = new GUIContent(string.IsNullOrEmpty(Data.SelectedView) ? "All Keys" : Data.SelectedView,
                    "Show the keys of one view (the part of the key name before the first delimiter).\nRight-click for view JSON import/export.");
                var viewRect = GUILayoutUtility.GetRect(viewContent, EditorStyles.toolbarDropDown, GUILayout.MinWidth(80), GUILayout.MaxWidth(180));

                var evt = Event.current;
                if (evt.type == EventType.ContextClick && viewRect.Contains(evt.mousePosition))
                {
                    ShowViewContextMenu(viewRect);
                    evt.Use();
                }

                if (EditorGUI.DropdownButton(viewRect, viewContent, FocusType.Passive, EditorStyles.toolbarDropDown))
                    ShowViewMenu(viewRect);

                GUILayout.Space(4);

                var searchRect = GUILayoutUtility.GetRect(80f, 2000f, 18f, 18f, EditorStyles.toolbarSearchField, GUILayout.ExpandWidth(true));
                searchRect.y += 1f;
                Data.KeySearchFilter = _searchField.OnToolbarGUI(searchRect, Data.KeySearchFilter);

                GUILayout.Space(4);

                int activeFilters = CountActiveFilters();
                var filterContent = new GUIContent(activeFilters > 0 ? $"Filter ({activeFilters})" : "Filter", "Filter by type and translation status");
                var filterRect = GUILayoutUtility.GetRect(filterContent, EditorStyles.toolbarDropDown, GUILayout.Width(72));
                if (EditorGUI.DropdownButton(filterRect, filterContent, FocusType.Passive, EditorStyles.toolbarDropDown))
                    ShowFilterMenu(filterRect);

                bool showForm = GUILayout.Toggle(_showNewKeyForm, new GUIContent("+ New Key", "Create a new key"),
                    EditorStyles.toolbarButton, GUILayout.Width(70));
                if (showForm != _showNewKeyForm)
                {
                    if (showForm)
                        OpenNewKeyForm();
                    else
                        CloseNewKeyForm();

                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void ShowViewMenu(Rect rect)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent($"All Keys ({Data.Keys.Count})"), string.IsNullOrEmpty(Data.SelectedView), () => SelectView(""));

            var views = Data.GetViews().OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
            if (views.Count > 0)
                menu.AddSeparator("");

            char delimiter = Data.CurrentViewDelimiter;
            foreach (var view in views)
            {
                string prefix = view + delimiter;
                int count = Data.Keys.Count(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                string captured = view;
                menu.AddItem(new GUIContent($"{view} ({count})"),
                    string.Equals(view, Data.SelectedView, StringComparison.OrdinalIgnoreCase),
                    () => SelectView(captured));
            }

            menu.DropDown(rect);
        }

        private void SelectView(string view)
        {
            Data.SelectedView = view;
            if (!string.IsNullOrEmpty(Data.SelectedKey) && !Data.GetFilteredKeys().Contains(Data.SelectedKey))
                Data.SelectedKey = null;
            GUIUtility.keyboardControl = 0;
            Editor.Repaint();
        }

        private void ShowFilterMenu(Rect rect)
        {
            var menu = new GenericMenu();
            bool arrays = Data.ShowArrayKeysOnly;
            bool strings = Data.ShowStringKeysOnly && !arrays;
            bool untranslated = !string.IsNullOrEmpty(Data.UntranslatedLanguageFilter);

            menu.AddItem(new GUIContent("Type/All"), !strings && !arrays, () => SetTypeFilter(false, false));
            menu.AddItem(new GUIContent("Type/Strings"), strings, () => SetTypeFilter(true, false));
            menu.AddItem(new GUIContent("Type/Arrays"), arrays, () => SetTypeFilter(false, true));

            menu.AddItem(new GUIContent("Status/All"), Data.StatusFilter == KeyStatusFilter.All && !untranslated,
                () => SetStatusFilter(KeyStatusFilter.All, null));
            menu.AddItem(new GUIContent("Status/Missing Translations"), Data.StatusFilter == KeyStatusFilter.Missing && !untranslated,
                () => SetStatusFilter(KeyStatusFilter.Missing, null));
            menu.AddItem(new GUIContent("Status/Has Problems"), Data.StatusFilter == KeyStatusFilter.Problems && !untranslated,
                () => SetStatusFilter(KeyStatusFilter.Problems, null));
            foreach (var lang in Data.GetLanguagesDefaultFirst())
            {
                string captured = lang;
                menu.AddItem(new GUIContent($"Status/Untranslated In/{LanguageDefinitions.GetDisplayName(lang)}"),
                    string.Equals(Data.UntranslatedLanguageFilter, lang, StringComparison.OrdinalIgnoreCase),
                    () => SetStatusFilter(KeyStatusFilter.All, captured));
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Search in Translations"), Data.SearchInTranslations,
                () => Data.SearchInTranslations = !Data.SearchInTranslations);
            menu.AddItem(new GUIContent("Sort by Name"), Data.SortKeysByName, () => Data.SortKeysByName = !Data.SortKeysByName);

            menu.AddSeparator("");
            if (CountActiveFilters() > 0)
                menu.AddItem(new GUIContent("Clear Filters"), false, ClearFilters);
            else
                menu.AddDisabledItem(new GUIContent("Clear Filters"));

            menu.DropDown(rect);
        }

        private void SetTypeFilter(bool stringsOnly, bool arraysOnly)
        {
            Data.ShowStringKeysOnly = stringsOnly;
            Data.ShowArrayKeysOnly = arraysOnly;
            Editor.Repaint();
        }

        private void SetStatusFilter(KeyStatusFilter status, string untranslatedLanguage)
        {
            Data.StatusFilter = status;
            Data.UntranslatedLanguageFilter = untranslatedLanguage;
            Editor.Repaint();
        }

        private int CountActiveFilters()
        {
            int count = 0;
            if (Data.ShowArrayKeysOnly || Data.ShowStringKeysOnly)
                count++;
            if (Data.StatusFilter != KeyStatusFilter.All || !string.IsNullOrEmpty(Data.UntranslatedLanguageFilter))
                count++;
            return count;
        }

        private void ClearFilters()
        {
            Data.KeySearchFilter = "";
            Data.ShowArrayKeysOnly = false;
            Data.ShowStringKeysOnly = false;
            Data.StatusFilter = KeyStatusFilter.All;
            Data.UntranslatedLanguageFilter = null;
            GUIUtility.keyboardControl = 0;
            Editor.Repaint();
        }

        private void ShowViewContextMenu(Rect rect)
        {
            var menu = new GenericMenu();
            string view = Data.SelectedView;

            if (!string.IsNullOrEmpty(view))
            {
                menu.AddItem(new GUIContent($"Export '{view}' to JSON…"), false, () => _jsonService.ExportViewToJson(view));
                menu.AddItem(new GUIContent($"Import JSON into '{view}'…"), false, () =>
                {
                    Data.History.RecordAll("Import View JSON");
                    _jsonService.ImportViewFromJson(view);
                    Editor.Repaint();
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Export View to JSON (select a view first)"));
                menu.AddDisabledItem(new GUIContent("Import JSON into View (select a view first)"));
            }

            menu.DropDown(rect);
        }

        #endregion

        #region New Key

        private void OpenNewKeyForm()
        {
            _showNewKeyForm = true;
            _pendingFocusControl = NewKeyNameControl;
            Editor.Repaint();
        }

        private void CloseNewKeyForm()
        {
            _showNewKeyForm = false;
            _newKey = "";
            _newValue = "";
            GUIUtility.keyboardControl = 0;
            Editor.Repaint();
        }

        private string NewKeyPrefix => string.IsNullOrEmpty(Data.SelectedView) ? "" : Data.SelectedView + Data.CurrentViewDelimiter;

        private void DrawNewKeyForm()
        {
            var evt = Event.current;
            string focused = GUI.GetNameOfFocusedControl();
            if (evt.type == EventType.KeyDown && (focused == NewKeyNameControl || focused == NewKeyValueControl))
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    evt.Use();
                    TryCreateKey();
                    GUIUtility.ExitGUI();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    evt.Use();
                    CloseNewKeyForm();
                    GUIUtility.ExitGUI();
                }
            }

            string prefix = NewKeyPrefix;
            string defaultName = LanguageDefinitions.GetDisplayName(DefaultLanguage);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.BeginHorizontal();
                {
                    if (prefix.Length > 0)
                        GUILayout.Label(prefix, Styles.RowLabelMuted, GUILayout.ExpandWidth(false));

                    GUI.SetNextControlName(NewKeyNameControl);
                    var nameRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true));
                    _newKey = LocalizationTextEditorPopup.FilterKeyName(EditorGUI.TextField(nameRect, _newKey));
                    DrawFieldPlaceholder(nameRect, _newKey, "Key name, e.g. settings.shadow_quality");

                    _newKeyIsArray = GUILayout.Toolbar(_newKeyIsArray ? 1 : 0, KeyTypeLabels, EditorStyles.miniButton, GUILayout.Width(110)) == 1;
                }
                EditorGUILayout.EndHorizontal();

                GUI.SetNextControlName(NewKeyValueControl);
                var valueRect = EditorGUILayout.GetControlRect();
                _newValue = EditorGUI.TextField(valueRect, _newValue);
                DrawFieldPlaceholder(valueRect, _newValue, $"{defaultName} text (optional)");

                EditorGUILayout.BeginHorizontal();
                {
                    string error = ValidateNewKey(out string fullKey);
                    if (string.IsNullOrWhiteSpace(_newKey))
                        GUILayout.Label("Enter to create · Esc to close", Styles.MutedLabel);
                    else if (error != null)
                        GUILayout.Label(error, Styles.WarningLabel);
                    else
                        GUILayout.Label($"Creates {fullKey}", Styles.MutedLabel);

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Close", EditorStyles.miniButton, GUILayout.Width(56)))
                    {
                        CloseNewKeyForm();
                        GUIUtility.ExitGUI();
                    }

                    using (new EditorGUI.DisabledScope(error != null))
                    {
                        if (GUILayout.Button("Create", EditorStyles.miniButton, GUILayout.Width(56)))
                        {
                            TryCreateKey();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawFieldPlaceholder(Rect fieldRect, string value, string placeholder)
        {
            if (!string.IsNullOrEmpty(value) || Event.current.type != EventType.Repaint)
                return;

            var rect = new Rect(fieldRect.x + 4f, fieldRect.y, fieldRect.width - 8f, fieldRect.height);
            GUI.Label(rect, placeholder, Styles.MutedLabel);
        }

        private string ValidateNewKey(out string fullKey)
        {
            string name = _newKey?.Trim() ?? "";
            fullKey = NewKeyPrefix + name;

            if (name.Length == 0)
                return "Enter a key name";

            string candidate = fullKey;
            if (Data.Keys.Any(k => k.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                return $"'{fullKey}' already exists";

            return null;
        }

        /// <summary>
        /// Creates the key and keeps the form open with focus on the name field, so several keys can be added in a row.
        /// </summary>
        private void TryCreateKey()
        {
            string error = ValidateNewKey(out string fullKey);
            if (error != null)
            {
                Editor.ShowNotification(new GUIContent(error));
                return;
            }

            Data.History.RecordKeys("Add Key", fullKey);
            if (!Data.AddKey(fullKey, _newKeyIsArray))
                return;

            if (!string.IsNullOrEmpty(_newValue))
            {
                string defaultLang = DefaultLanguage;
                var keyData = Data.LanguageData[fullKey];
                if (_newKeyIsArray)
                {
                    foreach (var lang in Data.LanguageCodes)
                        ((List<string>)keyData[lang]).Add(lang == defaultLang ? _newValue : "");
                }
                else
                {
                    keyData[defaultLang] = _newValue;
                }
                Data.HasUnsavedChanges = true;
            }

            _newKey = "";
            _newValue = "";
            _pendingFocusControl = NewKeyNameControl;
            AutoScrollToSelectedKey();
            Editor.Repaint();
        }

        #endregion

        #region Keys List

        private void DrawKeysListPanel()
        {
            EditorGUILayout.BeginVertical(
                GUILayout.Width(Data.KeysListPanelWidth),
                GUILayout.ExpandHeight(true),
                GUILayout.MaxHeight(float.MaxValue));

            var filteredKeys = Data.GetFilteredKeys();
            int count = filteredKeys.Count;
            float itemHeight = LanguageEditorData.KeyItemHeight;

            Rect viewRect = GUILayoutUtility.GetRect(0f, float.MaxValue, 0f, float.MaxValue,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            viewRect.height = Mathf.Max(viewRect.height, 50f);
            _keysListViewportHeight = viewRect.height;

            float contentHeight = count * itemHeight;
            float contentWidth = contentHeight > viewRect.height ? viewRect.width - 14f : viewRect.width;

            Data.KeysListScroll = GUI.BeginScrollView(viewRect, Data.KeysListScroll, new Rect(0, 0, contentWidth, contentHeight));
            if (count > 0)
            {
                int start = Mathf.Max(0, Mathf.FloorToInt(Data.KeysListScroll.y / itemHeight));
                int end = Mathf.Min(start + Mathf.CeilToInt(viewRect.height / itemHeight) + 1, count);

                for (int i = start; i < end; i++)
                    DrawKeyListItem(filteredKeys[i], i, contentWidth);
            }
            GUI.EndScrollView();

            if (count == 0)
            {
                GUI.Label(viewRect, Data.Keys.Count == 0
                        ? "No keys yet.\nUse + New Key to create one."
                        : "No keys match the search or filters.",
                    Styles.EmptyState);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawKeyListItem(string key, int index, float width)
        {
            var evt = Event.current;
            var rect = new Rect(0f, index * LanguageEditorData.KeyItemHeight, width, LanguageEditorData.KeyItemHeight);
            bool hover = rect.Contains(evt.mousePosition);
            bool selected = key == Data.SelectedKey;

            Styles.DrawRowBackground(rect, index, hover, selected);
            if (selected && evt.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), Styles.Accent);

            bool isArray = Data.LanguageData.TryGetValue(key, out var keyData) && LanguageEditorData.IsArrayKey(keyData);
            var typeRect = new Rect(rect.x + RowPadding, rect.y, 22f, rect.height);
            GUI.Label(typeRect, new GUIContent(isArray ? "[ ]" : "Aa", isArray ? "Array key" : "String key"), Styles.MutedLabel);

            float right = rect.xMax - RowPadding;
            var status = Data.GetKeyStatus(key);
            if (status.Problems > 0)
            {
                float w = Styles.GetBadgeWidth("!");
                Styles.DrawBadge(new Rect(right - w, rect.y, w, rect.height), "!", Styles.Danger, hover ? BuildProblemsTooltip(key) : null);
                right -= w + 3f;
            }
            if (status.Missing > 0)
            {
                string text = status.Missing.ToString();
                float w = Styles.GetBadgeWidth(text);
                Styles.DrawBadge(new Rect(right - w, rect.y, w, rect.height), text, Styles.Warning, hover ? BuildMissingTooltip(key) : null);
                right -= w + 3f;
            }

            DrawKeyName(new Rect(typeRect.xMax, rect.y, Mathf.Max(0f, right - typeRect.xMax - 4f), rect.height), key, selected);

            if (evt.type == EventType.MouseDown && evt.button == 0 && hover)
            {
                SelectKey(key);
                if (evt.clickCount == 2)
                    RenameKey();
                evt.Use();
            }
            else if (evt.type == EventType.ContextClick && hover)
            {
                SelectKey(key);
                ShowKeyMenu(key, null);
                evt.Use();
            }
        }

        /// <summary>
        /// Draws the key name; under "All Keys" the view prefix is muted so the local name stands out.
        /// </summary>
        private void DrawKeyName(Rect rect, string key, bool selected)
        {
            var style = selected ? Styles.RowLabelBold : Styles.RowLabel;

            if (!string.IsNullOrEmpty(Data.SelectedView))
            {
                GUI.Label(rect, new GUIContent(Data.GetLocalKeyName(key), key), style);
                return;
            }

            int delimiterIndex = key.IndexOf(Data.CurrentViewDelimiter);
            if (delimiterIndex <= 0)
            {
                GUI.Label(rect, new GUIContent(key, key), style);
                return;
            }

            var prefix = new GUIContent(key.Substring(0, delimiterIndex + 1));
            float prefixWidth = Mathf.Min(Styles.RowLabelMuted.CalcSize(prefix).x, rect.width * 0.5f);
            GUI.Label(new Rect(rect.x + 2f, rect.y, prefixWidth, rect.height), prefix, Styles.RowLabelMuted);
            GUI.Label(new Rect(rect.x + prefixWidth, rect.y, rect.width - prefixWidth, rect.height),
                new GUIContent(key.Substring(delimiterIndex + 1), key), style);
        }

        private string BuildMissingTooltip(string key)
        {
            var missing = Data.LanguageCodes
                .Where(lang => !Data.IsTranslated(key, lang))
                .Select(lang => LanguageDefinitions.GetDisplayName(lang));
            return "Missing: " + string.Join(", ", missing);
        }

        private string BuildProblemsTooltip(string key)
        {
            var issues = Data.LanguageCodes
                .Select(lang => (lang, issue: Data.GetTranslationIssue(key, lang)))
                .Where(item => item.issue != null)
                .Select(item => $"{LanguageDefinitions.GetDisplayName(item.lang)}: {item.issue}");
            return string.Join("\n", issues);
        }

        private void SelectKey(string key)
        {
            if (Data.SelectedKey != key)
                GUIUtility.keyboardControl = 0;
            Data.SelectedKey = key;
            Editor.Repaint();
        }

        private void DrawResizeHandle()
        {
            const float handleWidth = 5f;
            Rect handleRect = EditorGUILayout.GetControlRect(false, 0f,
                GUILayout.Width(handleWidth), GUILayout.ExpandHeight(true), GUILayout.MaxHeight(float.MaxValue));
            handleRect.height = Mathf.Max(handleRect.height, 50f);

            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);

            var evt = Event.current;
            if (evt.type == EventType.MouseDown && handleRect.Contains(evt.mousePosition))
                _isResizingKeysList = true;

            if (_isResizingKeysList)
            {
                if (evt.type == EventType.MouseUp || evt.type == EventType.MouseLeaveWindow)
                {
                    _isResizingKeysList = false;
                }
                else if (evt.type == EventType.MouseDrag)
                {
                    float maxWidth = WindowPosition.width * LanguageEditorData.MaxKeysListWidthRatio;
                    Data.KeysListPanelWidth = Mathf.Clamp(Data.KeysListPanelWidth + evt.delta.x,
                        LanguageEditorData.MinKeysListWidth, maxWidth);
                    evt.Use();
                    Editor.Repaint();
                }
            }

            if (evt.type == EventType.Repaint)
            {
                var line = new Rect(handleRect.center.x, handleRect.y, 1f, handleRect.height);
                EditorGUI.DrawRect(line, _isResizingKeysList ? Styles.Accent : Styles.Separator);
            }
        }

        #endregion

        #region Key Details

        private void DrawKeyDetailsPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true), GUILayout.MaxHeight(float.MaxValue));

            string key = Data.SelectedKey;
            if (string.IsNullOrEmpty(key) || !Data.LanguageData.TryGetValue(key, out var keyData))
            {
                DrawDetailsEmptyState();
            }
            else
            {
                bool isArray = LanguageEditorData.IsArrayKey(keyData);
                DrawKeyHeader(key, isArray);

                Data.KeyDetailsScroll = EditorGUILayout.BeginScrollView(Data.KeyDetailsScroll,
                    GUILayout.ExpandHeight(true), GUILayout.MaxHeight(float.MaxValue));
                {
                    var probe = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
                    if (Event.current.type == EventType.Repaint && probe.width > 1f && Mathf.Abs(probe.width - _detailsWidth) > 0.5f)
                    {
                        _detailsWidth = probe.width;
                        Editor.Repaint();
                    }

                    if (isArray)
                        DrawArrayEditor(key, keyData);
                    else
                        DrawStringEditor(key, keyData);

                    EditorGUILayout.Space(8);
                }
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawKeyHeader(string key, bool isArray)
        {
            var rect = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
            var menuRect = new Rect(rect.xMax - IconButtonSize - 2f, rect.y + 2f, IconButtonSize, rect.height - 4f);

            string typeText = isArray ? "ARRAY" : "STRING";
            float badgeWidth = Styles.GetBadgeWidth(typeText);

            int delimiterIndex = key.IndexOf(Data.CurrentViewDelimiter);
            var viewContent = delimiterIndex > 0 ? new GUIContent($"view: {key.Substring(0, delimiterIndex)}") : null;
            float viewWidth = viewContent != null ? Styles.MutedLabel.CalcSize(viewContent).x + 4f : 0f;

            float x = rect.x + 2f;
            var nameContent = new GUIContent(key, key);
            float nameWidth = Mathf.Max(40f, Mathf.Min(Styles.SectionTitle.CalcSize(nameContent).x + 2f,
                menuRect.x - x - badgeWidth - viewWidth - 16f));
            GUI.Label(new Rect(x, rect.y, nameWidth, rect.height), nameContent, Styles.SectionTitle);
            x += nameWidth + 6f;

            Styles.DrawBadge(new Rect(x, rect.y, badgeWidth, rect.height), typeText, Styles.MutedText,
                isArray ? "Array key: a list of strings" : "String key");
            x += badgeWidth + 6f;

            if (viewContent != null && x + viewWidth <= menuRect.x)
                GUI.Label(new Rect(x, rect.y, viewWidth, rect.height), viewContent, Styles.MutedLabel);

            if (GUI.Button(menuRect, EditorGUIUtility.IconContent("_Menu"), EditorStyles.iconButton))
                ShowKeyMenu(key, menuRect);

            EditorGUILayout.BeginHorizontal();
            {
                int missing = CountMissingTargets(key);
                string provider = Data.ActiveTranslationProvider.ToString();
                string translateLabel = _isTranslating ? "Translating…"
                    : missing > 0 ? $"Translate Missing ({missing})"
                    : "All Translated";

                using (new EditorGUI.DisabledScope(_isTranslating || missing == 0))
                {
                    if (GUILayout.Button(new GUIContent(translateLabel, $"Fill empty languages with {provider} ({ActionKeyName}+T)"),
                            EditorStyles.miniButton, GUILayout.MinWidth(130)))
                        TranslateMissing(key, null);
                }

                if (GUILayout.Button(new GUIContent("Rename", "Rename this key (F2)"), EditorStyles.miniButton, GUILayout.Width(64)))
                    RenameKey();

                if (isArray && GUILayout.Button(new GUIContent("+ Element", "Add an element to every language"), EditorStyles.miniButton, GUILayout.Width(72)))
                    AddArrayElement(key);

                GUILayout.FlexibleSpace();
                GUILayout.Label(BuildStatusSummary(key), Styles.MutedLabelRight);
            }
            EditorGUILayout.EndHorizontal();

            Styles.DrawSeparator(4f, 4f);
        }

        private int CountMissingTargets(string key)
        {
            string defaultLang = DefaultLanguage;
            return Data.LanguageCodes.Count(lang => lang != defaultLang && !Data.IsTranslated(key, lang));
        }

        private string BuildStatusSummary(string key)
        {
            var status = Data.GetKeyStatus(key);
            int total = Data.LanguageCodes.Count;
            string summary = $"{total - status.Missing}/{total} languages";
            if (status.Problems > 0)
                summary += status.Problems == 1 ? " · 1 problem" : $" · {status.Problems} problems";
            return summary;
        }

        private void DrawDetailsEmptyState()
        {
            GUILayout.FlexibleSpace();

            bool hasKeys = Data.Keys.Count > 0;
            GUILayout.Label(hasKeys ? "No key selected" : "No keys yet", Styles.CenteredTitle);
            GUILayout.Label(hasKeys
                    ? "Select a key on the left to edit its translations."
                    : "Create your first key with + New Key.",
                Styles.EmptyState);

            if (hasKeys)
            {
                int missing = 0;
                int problems = 0;
                foreach (var key in Data.Keys)
                {
                    var status = Data.GetKeyStatus(key);
                    if (status.Missing > 0) missing++;
                    if (status.Problems > 0) problems++;
                }
                GUILayout.Label($"{Data.Keys.Count} keys · {missing} with missing translations · {problems} with problems", Styles.EmptyState);
            }

            EditorGUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(GUILayout.Width(250));
            {
                string mod = ActionKeyName;
                DrawShortcut("↑ / ↓", "Select previous / next key");
                DrawShortcut("Alt + ↑ / ↓", "Move key up / down");
                DrawShortcut("F2 or double-click", "Rename key");
                DrawShortcut($"{mod} + E", "Edit value in a larger window");
                DrawShortcut($"{mod} + T", "Translate missing languages");
                DrawShortcut($"{mod} + F", "Search");
                DrawShortcut($"{mod} + C", "Copy key name");
                DrawShortcut($"{mod} + Z / {mod} + Y", "Undo / redo");
                DrawShortcut("Delete", "Delete key");
            }
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
        }

        private static void DrawShortcut(string keys, string description)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(keys, EditorStyles.miniBoldLabel, GUILayout.Width(120));
            GUILayout.Label(description, Styles.MutedLabel);
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region String Editor

        private void DrawStringEditor(string key, Dictionary<string, object> keyData)
        {
            string defaultLang = DefaultLanguage;
            bool sourceEmpty = !keyData.TryGetValue(defaultLang, out var sourceValue) || string.IsNullOrWhiteSpace(sourceValue as string);

            foreach (var lang in Data.GetLanguagesDefaultFirst())
            {
                bool isDefault = lang == defaultLang;
                string text = keyData.TryGetValue(lang, out var value) ? value as string ?? "" : "";
                string issue = isDefault ? null : Data.GetTranslationIssue(key, lang);
                string captured = lang;

                DrawLanguageFieldHeader(lang, isDefault, issue, text.Length,
                    canTranslate: !sourceEmpty, hasText: text.Length > 0,
                    onTranslate: () => TranslateMissing(key, captured),
                    onExpand: () => OpenValueEditor(key, captured));

                string newValue = DrawTextEditor(ValueControlPrefix + lang, text,
                    isDefault ? "Source text" : "Not translated", GUILayout.ExpandWidth(true));
                if (newValue != text)
                    SetStringValue(key, lang, newValue, true);

                EditorGUILayout.Space(6);
            }
        }

        private void DrawLanguageFieldHeader(string lang, bool isDefault, string issue, int length,
            bool canTranslate, bool hasText, Action onTranslate, Action onExpand)
        {
            var rect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
            float right = rect.xMax;
            string provider = Data.ActiveTranslationProvider.ToString();

            if (onExpand != null)
            {
                var expandRect = new Rect(right - IconButtonSize, rect.y, IconButtonSize, rect.height);
                if (GUI.Button(expandRect, Styles.Icon("editicon.sml", "…", $"Edit in a larger window ({ActionKeyName}+E)"), EditorStyles.iconButton))
                    onExpand();
                right -= IconButtonSize + 2f;
            }

            if (!isDefault && onTranslate != null)
            {
                var translateRect = new Rect(right - IconButtonSize, rect.y, IconButtonSize, rect.height);
                using (new EditorGUI.DisabledScope(_isTranslating || !canTranslate))
                {
                    string tooltip = !canTranslate ? "Add source text first"
                        : hasText ? $"Translate again with {provider}"
                        : $"Translate with {provider}";
                    if (GUI.Button(translateRect, Styles.Icon("Refresh", "↻", tooltip), EditorStyles.iconButton))
                        onTranslate();
                }
                right -= IconButtonSize + 2f;
            }

            if (length > 0)
            {
                var countContent = new GUIContent(length.ToString(), "Characters");
                float w = Styles.MutedLabelRight.CalcSize(countContent).x + 4f;
                GUI.Label(new Rect(right - w, rect.y, w, rect.height), countContent, Styles.MutedLabelRight);
                right -= w + 4f;
            }

            float x = rect.x;
            var nameContent = new GUIContent(LanguageDefinitions.GetDisplayName(lang));
            float nameWidth = Mathf.Min(EditorStyles.boldLabel.CalcSize(nameContent).x, Mathf.Max(0f, right - x));
            GUI.Label(new Rect(x, rect.y, nameWidth, rect.height), nameContent, EditorStyles.boldLabel);
            x += nameWidth + 2f;

            var codeContent = new GUIContent(lang);
            float codeWidth = Styles.MutedLabel.CalcSize(codeContent).x;
            if (x + codeWidth < right)
                GUI.Label(new Rect(x, rect.y, codeWidth, rect.height), codeContent, Styles.MutedLabel);
            x += codeWidth + 6f;

            if (isDefault)
                x += Styles.DrawBadge(new Rect(x, rect.y, 0f, rect.height), "SOURCE", Styles.Accent,
                    "Default language. Other languages are translated from it.") + 4f;

            if (LanguageDefinitions.IsRightToLeft(lang))
                x += Styles.DrawBadge(new Rect(x, rect.y, 0f, rect.height), "RTL", Styles.MutedText, "Right-to-left language") + 4f;

            if (issue != null && right - x > 40f)
                DrawIssue(new Rect(x + 2f, rect.y, right - x - 2f, rect.height), issue);
        }

        private static void DrawIssue(Rect rect, string issue)
        {
            GUI.Label(new Rect(rect.x, rect.y + (rect.height - 16f) * 0.5f, 16f, 16f),
                EditorGUIUtility.IconContent("console.warnicon.sml"));
            GUI.Label(new Rect(rect.x + 17f, rect.y, rect.width - 17f, rect.height), new GUIContent(issue, issue), Styles.WarningLabel);
        }

        /// <summary>
        /// Word-wrapped text area that grows with its content, with a muted placeholder while empty.
        /// </summary>
        private static string DrawTextEditor(string controlName, string text, string placeholder, params GUILayoutOption[] options)
        {
            var allOptions = new List<GUILayoutOption>(options) { GUILayout.MinHeight(FieldMinHeight) };

            GUI.SetNextControlName(controlName);
            string result = EditorGUILayout.TextArea(text, Styles.TextArea, allOptions.ToArray());

            if (string.IsNullOrEmpty(text) && Event.current.type == EventType.Repaint && GUI.GetNameOfFocusedControl() != controlName)
                GUI.Label(GUILayoutUtility.GetLastRect(), placeholder, Styles.Placeholder);

            return result;
        }

        private void SetStringValue(string key, string lang, string text, bool coalesce)
        {
            if (!Data.LanguageData.TryGetValue(key, out var keyData))
                return;

            Data.History.Record("Edit Translation", key, coalesce ? $"{key}|{lang}" : null);
            keyData[lang] = text ?? "";
            Data.HasUnsavedChanges = true;
            Editor.Repaint();
        }

        #endregion

        #region Array Editor

        private void DrawArrayEditor(string key, Dictionary<string, object> keyData)
        {
            string defaultLang = DefaultLanguage;
            var targets = Data.GetLanguagesDefaultFirst().Where(lang => lang != defaultLang).ToList();
            if (targets.Count > 0 && (_arrayTargetLanguage == null || !targets.Contains(_arrayTargetLanguage)))
                _arrayTargetLanguage = targets[0];
            string target = targets.Count > 0 ? _arrayTargetLanguage : null;

            var source = keyData.TryGetValue(defaultLang, out var sourceValue) ? sourceValue as List<string> : null;
            var targetList = target != null && keyData.TryGetValue(target, out var targetValue) ? targetValue as List<string> : null;
            int count = source?.Count ?? LanguageEditorData.ConvertToList(Data.GetFirstValue(key))?.Count ?? 0;

            if (target != null)
                DrawArrayTargetSelector(key, targets, target, source);

            float buttonsWidth = ArrayButtonWidth * 3f;
            float available = Mathf.Max(160f, (_detailsWidth > 1f ? _detailsWidth : 400f) - ArrayIndexWidth - buttonsWidth - 28f);
            float columnWidth = target != null ? (available - 6f) * 0.5f : available;

            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Space(ArrayIndexWidth + 4f);
                GUILayout.Label($"{LanguageDefinitions.GetDisplayName(defaultLang)} (source)", EditorStyles.miniBoldLabel, GUILayout.Width(columnWidth));
                if (target != null)
                    GUILayout.Label(LanguageDefinitions.GetDisplayName(target), EditorStyles.miniBoldLabel, GUILayout.Width(columnWidth));
            }
            EditorGUILayout.EndHorizontal();

            if (count == 0)
                GUILayout.Label("No elements yet. Use + Element to add one.", Styles.EmptyState);

            int moveFrom = -1, moveTo = -1, removeIndex = -1;

            for (int i = 0; i < count; i++)
            {
                string sourceText = source != null && i < source.Count ? source[i] ?? "" : "";
                string targetText = targetList != null && i < targetList.Count ? targetList[i] ?? "" : "";

                EditorGUILayout.BeginHorizontal();
                {
                    GUILayout.Label(i.ToString(), Styles.MutedLabel, GUILayout.Width(ArrayIndexWidth), GUILayout.Height(FieldMinHeight));

                    string newSource = DrawTextEditor($"{ElementControlPrefix}{i}.Source", sourceText, "Source text", GUILayout.Width(columnWidth));
                    if (newSource != sourceText)
                        SetElementValue(key, defaultLang, i, newSource);

                    if (target != null)
                    {
                        string newTarget = DrawTextEditor($"{ElementControlPrefix}{i}.Target", targetText, "Not translated", GUILayout.Width(columnWidth));
                        if (newTarget != targetText)
                            SetElementValue(key, target, i, newTarget);
                    }

                    using (new EditorGUI.DisabledScope(i == 0))
                    {
                        if (GUILayout.Button(new GUIContent("↑", "Move up"), EditorStyles.miniButtonLeft, GUILayout.Width(ArrayButtonWidth)))
                        {
                            moveFrom = i;
                            moveTo = i - 1;
                        }
                    }
                    using (new EditorGUI.DisabledScope(i == count - 1))
                    {
                        if (GUILayout.Button(new GUIContent("↓", "Move down"), EditorStyles.miniButtonMid, GUILayout.Width(ArrayButtonWidth)))
                        {
                            moveFrom = i;
                            moveTo = i + 1;
                        }
                    }
                    if (GUILayout.Button(new GUIContent("×", $"Remove element ({ActionKeyName}+Z to undo)"), EditorStyles.miniButtonRight, GUILayout.Width(ArrayButtonWidth)))
                        removeIndex = i;
                }
                EditorGUILayout.EndHorizontal();

                if (target != null && !string.IsNullOrWhiteSpace(sourceText) && !string.IsNullOrWhiteSpace(targetText))
                {
                    string issue = McpTextChecks.FindMismatch(sourceText, targetText);
                    if (issue != null)
                    {
                        var issueRect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
                        issueRect.xMin += ArrayIndexWidth + columnWidth + 10f;
                        DrawIssue(issueRect, issue);
                    }
                }

                EditorGUILayout.Space(2);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("+ Add Element", EditorStyles.miniButton, GUILayout.Width(100)))
                    AddArrayElement(key);

                if (GUILayout.Button(new GUIContent("Remove Empty Elements", "Remove elements that are empty in every language"),
                        EditorStyles.miniButton, GUILayout.Width(150)))
                {
                    Data.History.Record("Remove Empty Elements", key);
                    Data.ClearEmptyArrayElements(key);
                    GUIUtility.keyboardControl = 0;
                }
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndHorizontal();

            if (moveFrom >= 0)
            {
                Data.History.Record("Move Array Element", key);
                Data.MoveArrayElement(key, moveFrom, moveTo);
                GUIUtility.keyboardControl = 0;
            }
            else if (removeIndex >= 0)
            {
                Data.History.Record("Remove Array Element", key);
                Data.RemoveArrayElement(key, removeIndex);
                GUIUtility.keyboardControl = 0;
                Editor.ShowNotification(new GUIContent($"Element {removeIndex} removed ({ActionKeyName}+Z to undo)"));
            }
        }

        private void DrawArrayTargetSelector(string key, List<string> targets, string target, List<string> source)
        {
            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Label("Translate into", Styles.MutedLabel, GUILayout.Width(80));

                int index = targets.IndexOf(target);
                var names = targets.Select(lang => $"{LanguageDefinitions.GetDisplayName(lang)} ({lang})").ToArray();
                int newIndex = EditorGUILayout.Popup(index, names, GUILayout.MaxWidth(220));
                if (newIndex != index)
                {
                    _arrayTargetLanguage = targets[newIndex];
                    GUIUtility.keyboardControl = 0;
                }

                bool sourceEmpty = source == null || source.All(string.IsNullOrWhiteSpace);
                bool translated = Data.IsTranslated(key, target);
                using (new EditorGUI.DisabledScope(_isTranslating || sourceEmpty || translated))
                {
                    var content = new GUIContent($"Translate {LanguageDefinitions.GetDisplayName(target)}",
                        sourceEmpty ? "Add source text first" : $"Fill empty elements with {Data.ActiveTranslationProvider}");
                    if (GUILayout.Button(content, EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                        TranslateMissing(key, target);
                }

                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// Opens the large text editor for one language of a string key, or one element of an array key.
        /// </summary>
        private void OpenValueEditor(string key, string lang, int elementIndex = -1)
        {
            if (!Data.LanguageData.TryGetValue(key, out var keyData))
                return;

            string defaultLang = DefaultLanguage;
            keyData.TryGetValue(lang, out var value);
            keyData.TryGetValue(defaultLang, out var source);

            string text, reference;
            Action<string> onSave;
            if (elementIndex >= 0)
            {
                var list = value as List<string>;
                var sourceList = source as List<string>;
                if (list == null || elementIndex >= list.Count)
                    return;

                text = list[elementIndex] ?? "";
                reference = sourceList != null && elementIndex < sourceList.Count ? sourceList[elementIndex] : null;
                onSave = newText =>
                {
                    SetElementValue(key, lang, elementIndex, newText, coalesce: false);
                    Editor.Repaint();
                };
            }
            else
            {
                text = value as string ?? "";
                reference = source as string;
                onSave = newText => SetStringValue(key, lang, newText, false);
            }

            // Drop focus so the inline field shows the saved text afterwards
            GUIUtility.keyboardControl = 0;

            LocalizationTextEditorPopup.OpenText(text, onSave,
                title: elementIndex >= 0 ? $"{key} [{elementIndex}]" : key,
                subtitle: $"{LanguageDefinitions.GetDisplayName(lang)} ({lang})",
                referenceText: lang == defaultLang ? null : reference,
                referenceLabel: $"{LanguageDefinitions.GetDisplayName(defaultLang)} (source)",
                rightToLeft: LanguageDefinitions.IsRightToLeft(lang));
        }

        /// <summary>
        /// Ctrl/Cmd+E: opens the large editor for the value field being edited, the last one edited for
        /// this key, or the source language.
        /// </summary>
        private bool OpenFocusedValueEditor()
        {
            string key = Data.SelectedKey;
            if (string.IsNullOrEmpty(key) || !Data.LanguageData.TryGetValue(key, out var keyData))
                return false;

            string control = GUI.GetNameOfFocusedControl();
            if (!IsValueControl(control))
                control = _lastValueKey == key ? _lastValueControl : null;

            string defaultLang = DefaultLanguage;

            if (control != null && control.StartsWith(ValueControlPrefix, StringComparison.Ordinal))
            {
                string lang = control.Substring(ValueControlPrefix.Length);
                if (Data.LanguageCodes.Contains(lang))
                {
                    OpenValueEditor(key, lang);
                    return true;
                }
            }
            else if (control != null && control.StartsWith(ElementControlPrefix, StringComparison.Ordinal))
            {
                var parts = control.Substring(ElementControlPrefix.Length).Split('.');
                if (parts.Length == 2 && int.TryParse(parts[0], out int index))
                {
                    string lang = parts[1] == "Target" && _arrayTargetLanguage != null ? _arrayTargetLanguage : defaultLang;
                    OpenValueEditor(key, lang, index);
                    return true;
                }
            }

            if (LanguageEditorData.IsArrayKey(keyData))
            {
                OpenValueEditor(key, defaultLang, 0);
                return true;
            }

            OpenValueEditor(key, defaultLang);
            return true;
        }

        private static bool IsValueControl(string control)
        {
            return !string.IsNullOrEmpty(control) &&
                   (control.StartsWith(ValueControlPrefix, StringComparison.Ordinal) ||
                    control.StartsWith(ElementControlPrefix, StringComparison.Ordinal));
        }

        private void SetElementValue(string key, string lang, int index, string text, bool coalesce = true)
        {
            if (!Data.LanguageData.TryGetValue(key, out var keyData) || !(keyData.TryGetValue(lang, out var value) && value is List<string> list) || index >= list.Count)
                return;

            Data.History.Record("Edit Translation", key, coalesce ? $"{key}|{lang}|{index}" : null);
            list[index] = text ?? "";
            Data.HasUnsavedChanges = true;
        }

        private void AddArrayElement(string key)
        {
            Data.History.Record("Add Array Element", key);
            Data.AddArrayElement(key);
            Editor.Repaint();
        }

        #endregion

        #region Status Bar

        private void DrawStatusBar()
        {
            Styles.DrawSeparator(2f, 0f);

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Data.KeySearchFilter))
                parts.Add($"search \"{Data.KeySearchFilter}\"");
            if (Data.ShowArrayKeysOnly)
                parts.Add("arrays only");
            else if (Data.ShowStringKeysOnly)
                parts.Add("strings only");
            if (!string.IsNullOrEmpty(Data.UntranslatedLanguageFilter))
                parts.Add($"untranslated in {LanguageDefinitions.GetDisplayName(Data.UntranslatedLanguageFilter)}");
            else if (Data.StatusFilter == KeyStatusFilter.Missing)
                parts.Add("missing translations");
            else if (Data.StatusFilter == KeyStatusFilter.Problems)
                parts.Add("has problems");

            int total = Data.Keys.Count;
            int shown = Data.GetFilteredKeys().Count;
            string text = shown == total ? $"{total} keys" : $"{shown} / {total} keys";
            if (parts.Count > 0)
                text += " · " + string.Join(" · ", parts);

            EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
            {
                GUILayout.Label(text, Styles.MutedLabel);
                GUILayout.FlexibleSpace();
                if (parts.Count > 0 && GUILayout.Button("Clear Filters", EditorStyles.miniButton, GUILayout.Width(80)))
                    ClearFilters();
            }
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Key Actions

        private void ShowKeyMenu(string key, Rect? dropDownRect)
        {
            var menu = new GenericMenu();
            bool isArray = Data.LanguageData.TryGetValue(key, out var keyData) && LanguageEditorData.IsArrayKey(keyData);
            string snippet = isArray ? $"LocalizationManager.GetArray(\"{key}\")" : $"LocalizationManager.GetText(\"{key}\")";

            menu.AddItem(new GUIContent("Copy Key Name"), false, () => CopyToClipboard(key, $"Copied '{key}'"));
            menu.AddItem(new GUIContent(isArray ? "Copy GetArray() Snippet" : "Copy GetText() Snippet"), false,
                () => CopyToClipboard(snippet, "Snippet copied"));

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Copy as JSON"), false, () => _jsonService.CopyKeyAsJson(key, Editor));
            menu.AddItem(new GUIContent("Paste from JSON"), false, () =>
            {
                Data.History.Record("Paste Key JSON", key);
                _jsonService.PasteKeyFromJson(key, Editor);
                GUIUtility.keyboardControl = 0;
                Editor.Repaint();
            });

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Rename…"), false, () =>
            {
                Data.SelectedKey = key;
                RenameKey();
            });
            if (!_isTranslating && CountMissingTargets(key) > 0)
                menu.AddItem(new GUIContent("Translate Missing"), false, () => TranslateMissing(key, null));
            else
                menu.AddDisabledItem(new GUIContent("Translate Missing"));

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Clear Translations"), false, () => ClearKeyData(key));
            menu.AddItem(new GUIContent("Delete Key…"), false, () => ConfirmDeleteKey(key));

            if (dropDownRect.HasValue)
                menu.DropDown(dropDownRect.Value);
            else
                menu.ShowAsContext();
        }

        private void CopyToClipboard(string text, string message)
        {
            EditorGUIUtility.systemCopyBuffer = text;
            Editor.ShowNotification(new GUIContent(message));
        }

        private async void TranslateMissing(string key, string onlyLanguage)
        {
            if (_isTranslating || string.IsNullOrEmpty(key) || !Data.LanguageData.TryGetValue(key, out var keyData))
                return;

            Data.History.Record("Translate Key", key);
            GUIUtility.keyboardControl = 0;

            string previous = null;
            if (onlyLanguage != null && keyData.TryGetValue(onlyLanguage, out var current) &&
                current is string currentText && !string.IsNullOrWhiteSpace(currentText))
            {
                previous = currentText;
                keyData[onlyLanguage] = "";
            }

            _isTranslating = true;
            Editor.Repaint();

            try
            {
                await _translationService.TranslateAndFill(key, onlyLanguage != null ? new[] { onlyLanguage } : null);
                Editor.ShowNotification(new GUIContent("Translation finished"));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Translation failed: {ex.Message}");
                Editor.ShowNotification(new GUIContent("Translation failed. See the Console for details."));
            }
            finally
            {
                if (previous != null && keyData.TryGetValue(onlyLanguage, out var after) && string.IsNullOrWhiteSpace(after as string))
                    keyData[onlyLanguage] = previous;

                _isTranslating = false;
                Data.MarkKeysChanged();
                Editor.Repaint();
            }
        }

        private void RenameKey()
        {
            string key = Data.SelectedKey;
            if (string.IsNullOrEmpty(key))
                return;

            string viewPrefix = string.IsNullOrEmpty(Data.SelectedView) ? "" : Data.SelectedView + Data.CurrentViewDelimiter;
            bool inView = viewPrefix.Length > 0 && key.StartsWith(viewPrefix, StringComparison.OrdinalIgnoreCase);
            string prefix = inView ? key.Substring(0, viewPrefix.Length) : "";
            string editable = inView ? key.Substring(prefix.Length) : key;

            string ValidateName(string name)
            {
                string full = prefix + name;
                return Data.Keys.Any(k => !k.Equals(key, StringComparison.OrdinalIgnoreCase) && k.Equals(full, StringComparison.OrdinalIgnoreCase))
                    ? $"'{full}' already exists (key names are case-insensitive)."
                    : null;
            }

            LocalizationTextEditorPopup.OpenKeyName(editable, newName =>
            {
                newName = LocalizationTextEditorPopup.FilterKeyName(newName?.Trim() ?? "");
                if (string.IsNullOrEmpty(newName))
                {
                    Editor.ShowNotification(new GUIContent("Key name cannot be empty."));
                    return;
                }

                string newFullKey = prefix + newName;
                if (newFullKey == key)
                    return;

                if (Data.Keys.Any(k => !k.Equals(key, StringComparison.OrdinalIgnoreCase) && k.Equals(newFullKey, StringComparison.OrdinalIgnoreCase)))
                {
                    EditorUtility.DisplayDialog("Rename Key", $"Key '{newFullKey}' already exists (key names are case-insensitive).", "OK");
                    return;
                }

                Data.History.RecordKeys("Rename Key", key, newFullKey);
                Data.RenameKey(key, newFullKey);
                Editor.Repaint();
            }, prefix.Length > 0 ? $"Rename Key in '{Data.SelectedView}'" : "Rename Key", ValidateName);
        }

        private void ClearKeyData(string key)
        {
            Data.History.Record("Clear Translations", key);
            Data.ClearKeyTranslations(key);
            GUIUtility.keyboardControl = 0;
            Editor.ShowNotification(new GUIContent($"Translations cleared ({ActionKeyName}+Z to undo)"));
            Editor.Repaint();
        }

        private void ConfirmDeleteKey(string key)
        {
            if (string.IsNullOrEmpty(key) || !EditorUtility.DisplayDialog("Delete Key",
                    $"Delete the key '{key}' and all its translations?\n\nYou can undo this with {ActionKeyName}+Z.", "Delete", "Cancel"))
                return;

            var filtered = Data.GetFilteredKeys();
            int index = IndexOf(filtered, key);
            string next = index >= 0 && index + 1 < filtered.Count ? filtered[index + 1]
                : index > 0 ? filtered[index - 1]
                : null;

            Data.History.RecordKeys("Delete Key", key);
            Data.RemoveKey(key);
            Data.SelectedKey = next;
            GUIUtility.keyboardControl = 0;
            Editor.Repaint();
        }

        private static int IndexOf(IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value)
                    return i;
            }
            return -1;
        }

        #endregion

        #region Keyboard

        public override bool HandleKeyboardInput(Event evt)
        {
            if (evt.type != EventType.KeyDown)
                return false;

            bool action = EditorGUI.actionKey;

            switch (evt.keyCode)
            {
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                    return HandleArrowKey(evt, evt.keyCode == KeyCode.UpArrow ? -1 : 1, evt.alt || action);

                case KeyCode.Escape:
                    if (_showNewKeyForm)
                        CloseNewKeyForm();
                    else if (!string.IsNullOrEmpty(Data.SelectedKey))
                        Data.SelectedKey = null;
                    else
                        return false;
                    evt.Use();
                    Editor.Repaint();
                    return true;
            }

            string key = Data.SelectedKey;
            if (string.IsNullOrEmpty(key))
                return false;

            switch (evt.keyCode)
            {
                case KeyCode.F2:
                    RenameKey();
                    evt.Use();
                    return true;

                case KeyCode.T when action:
                    TranslateMissing(key, null);
                    evt.Use();
                    return true;

                case KeyCode.C when action:
                    CopyToClipboard(key, $"Copied '{key}'");
                    evt.Use();
                    return true;

                case KeyCode.Delete:
                case KeyCode.Backspace:
                    if (_pendingDelete)
                        return false;
                    _pendingDelete = true;
                    EditorApplication.delayCall += () =>
                    {
                        ConfirmDeleteKey(key);
                        _pendingDelete = false;
                    };
                    evt.Use();
                    return true;
            }

            return false;
        }

        private bool HandleArrowKey(Event evt, int direction, bool reorder)
        {
            var filtered = Data.GetFilteredKeys();
            if (filtered.Count == 0)
                return false;

            int index = string.IsNullOrEmpty(Data.SelectedKey) ? -1 : IndexOf(filtered, Data.SelectedKey);

            if (index < 0)
                Data.SelectedKey = filtered[direction > 0 ? 0 : filtered.Count - 1];
            else if (reorder)
                MoveSelectedKey(filtered, index, direction);
            else
                Data.SelectedKey = filtered[Mathf.Clamp(index + direction, 0, filtered.Count - 1)];

            AutoScrollToSelectedKey();
            evt.Use();
            Editor.Repaint();
            return true;
        }

        /// <summary>
        /// Moves the selected key past its visible neighbour in the saved key order.
        /// </summary>
        private void MoveSelectedKey(IReadOnlyList<string> filtered, int index, int direction)
        {
            if (Data.SortKeysByName)
            {
                Editor.ShowNotification(new GUIContent("Turn off Sort by Name to reorder keys"));
                return;
            }

            int neighbourIndex = index + direction;
            if (neighbourIndex < 0 || neighbourIndex >= filtered.Count)
                return;

            string key = filtered[index];
            string neighbour = filtered[neighbourIndex];

            Data.History.RecordKeys("Reorder Key");
            Data.Keys.Remove(key);
            int target = Data.Keys.IndexOf(neighbour);
            Data.Keys.Insert(direction > 0 ? target + 1 : target, key);
            Data.HasUnsavedChanges = true;
        }

        private void AutoScrollToSelectedKey()
        {
            if (string.IsNullOrEmpty(Data.SelectedKey))
                return;

            int selectedIndex = IndexOf(Data.GetFilteredKeys(), Data.SelectedKey);
            if (selectedIndex < 0)
                return;

            float viewportHeight = Mathf.Max(_keysListViewportHeight, LanguageEditorData.KeyItemHeight);
            float itemTop = selectedIndex * LanguageEditorData.KeyItemHeight;
            float itemBottom = itemTop + LanguageEditorData.KeyItemHeight;

            if (itemTop < Data.KeysListScroll.y)
                Data.KeysListScroll = new Vector2(Data.KeysListScroll.x, itemTop);
            else if (itemBottom > Data.KeysListScroll.y + viewportHeight)
                Data.KeysListScroll = new Vector2(Data.KeysListScroll.x, itemBottom - viewportHeight);
        }

        #endregion
    }
}
