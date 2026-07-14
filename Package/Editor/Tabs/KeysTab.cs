using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PicoShot.Localization.Data;
using PicoShot.Localization.Editor.Data;
using PicoShot.Localization.Editor.Services;

namespace PicoShot.Localization.Editor.Tabs
{
    /// <summary>
    /// Tab for managing translation keys with split-pane view.
    /// </summary>
    public sealed class KeysTab : LocalizationEditorTabBase
    {
        private readonly TranslationService _translationService;
        private readonly JsonService _jsonService;
        private bool _isResizingKeysList;
        private string _newKey = "";
        private string _newValue = "";
        private bool _pendingDelete;
        private bool _isTranslating;
        private float _keysListViewportHeight;

        private static Texture2D _transparentTexture;
        private static GUIStyle _keyButtonStyleNormal;
        private static GUIStyle _keyButtonStyleSelected;

        public KeysTab(LocalizationEditor editor, LanguageEditorData data) : base(editor, data)
        {
            _translationService = new TranslationService(data);
            _jsonService = new JsonService(data);
        }

        public override string TabName => "Keys";

        public override void Draw()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));

            using (BeginBox())
            {
                DrawViewSelectionSection();
                DrawAddKeySection();
                DrawSearchAndFilterSection();
            }

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal(
                GUILayout.ExpandHeight(true),
                GUILayout.MinHeight(120f),
                GUILayout.MaxHeight(float.MaxValue));
            DrawKeysListPanel();
            DrawResizeHandle();
            DrawKeyDetailsPanel();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawViewSelectionSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Views", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            var views = new List<string> { "All Keys" };
            views.AddRange(Data.GetViews());

            if (!string.IsNullOrEmpty(Data.SelectedView) && !views.Any(v => v.Equals(Data.SelectedView, StringComparison.OrdinalIgnoreCase)))
            {
                views.Add(Data.SelectedView);
            }

            int currentIndex = string.IsNullOrEmpty(Data.SelectedView) ? 0 : views.FindIndex(v => v.Equals(Data.SelectedView, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0) currentIndex = 0;

            string[] displayNames = views.Select(v => v == "All Keys" ? v : v.ToUpperInvariant()).ToArray();

            int newIndex = EditorGUILayout.Popup(currentIndex, displayNames);
            if (newIndex != currentIndex)
            {
                Data.SelectedView = newIndex == 0 ? "" : views[newIndex];
                Data.SelectedKey = null;
                GUI.FocusControl(null);
            }

            if (!string.IsNullOrEmpty(Data.SelectedView))
            {
                if (GUILayout.Button("Export JSON", GUILayout.Width(85)))
                {
                    _jsonService.ExportViewToJson(Data.SelectedView);
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("Import JSON", GUILayout.Width(85)))
                {
                    _jsonService.ImportViewFromJson(Data.SelectedView);
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawAddKeySection()
        {
            EditorGUILayout.BeginVertical("box");
            string title = string.IsNullOrEmpty(Data.SelectedView)
                ? "Add New Key"
                : $"Add New Key to '{Data.SelectedView}'";
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Key Name:", GUILayout.Width(130));
            Rect keyNameRect = EditorGUILayout.GetControlRect();
            _newKey = LocalizationTextEditorPopup.FilterKeyName(EditorGUI.TextField(keyNameRect, _newKey));
            if (string.IsNullOrEmpty(_newKey) && Event.current.type == EventType.Repaint)
            {
                var placeholderRect = new Rect(keyNameRect.x + 4f, keyNameRect.y, keyNameRect.width - 8f, keyNameRect.height);
                Color previousColor = GUI.contentColor;
                GUI.contentColor = new Color(0.55f, 0.55f, 0.55f);
                GUI.Label(placeholderRect, "e.g. UI.Settings.Shadow_Quality", EditorStyles.miniLabel);
                GUI.contentColor = previousColor;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            string defaultLang = PicoShot.Localization.Config.LocalizationConfigProvider.Config.DefaultLanguage;
            EditorGUILayout.LabelField($"Default Value ({defaultLang}):", GUILayout.Width(130));
            _newValue = EditorGUILayout.TextField(_newValue);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Add String Key", GUILayout.Width(120), GUILayout.Height(25)))
                AddKey(false);
            if (GUILayout.Button("Add Array Key", GUILayout.Width(120), GUILayout.Height(25)))
                AddKey(true);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawSearchAndFilterSection()
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search Keys:", GUILayout.Width(80));
            Data.KeySearchFilter = EditorGUILayout.TextField(Data.KeySearchFilter);
            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                Data.KeySearchFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Filter:", GUILayout.Width(80));

            var newShowArrayKeys = EditorGUILayout.ToggleLeft("Array Keys", Data.ShowArrayKeysOnly, GUILayout.Width(100));
            var newShowStringKeys = EditorGUILayout.ToggleLeft("String Keys", Data.ShowStringKeysOnly, GUILayout.Width(100));
            Data.SortKeysByName = EditorGUILayout.ToggleLeft("Sort by Name", Data.SortKeysByName, GUILayout.Width(100));

            if (newShowArrayKeys != Data.ShowArrayKeysOnly)
            {
                Data.ShowArrayKeysOnly = newShowArrayKeys;
                Data.ShowStringKeysOnly = false;
            }

            if (newShowStringKeys != Data.ShowStringKeysOnly)
            {
                Data.ShowStringKeysOnly = newShowStringKeys;
                Data.ShowArrayKeysOnly = false;
            }

            EditorGUILayout.EndHorizontal();

            var totalKeys = Data.Keys.Count;
            var filteredCount = Data.GetFilteredKeys().Count();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Found:", GUILayout.Width(80));
            EditorGUILayout.LabelField($"{filteredCount} / {totalKeys} keys", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawKeysListPanel()
        {
            EditorGUILayout.BeginVertical(
                "box",
                GUILayout.Width(Data.KeysListPanelWidth),
                GUILayout.ExpandHeight(true),
                GUILayout.MaxHeight(float.MaxValue));
            EditorGUILayout.LabelField("Keys", EditorStyles.boldLabel);

            var filteredKeys = Data.GetFilteredKeys().ToList();
            int totalKeyCount = filteredKeys.Count;

            Rect scrollViewRect = GUILayoutUtility.GetRect(
                0f,
                float.MaxValue,
                0f,
                float.MaxValue,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true)
            );
            scrollViewRect.height = Mathf.Max(scrollViewRect.height, 50f);
            _keysListViewportHeight = scrollViewRect.height;

            int maxVisibleItems = Mathf.CeilToInt(scrollViewRect.height / LanguageEditorData.KeyItemHeight) + 1;

            float totalContentHeight = totalKeyCount * LanguageEditorData.KeyItemHeight;
            Data.KeysListScroll = GUI.BeginScrollView(
                scrollViewRect,
                Data.KeysListScroll,
                new Rect(0, 0, scrollViewRect.width - 20, totalContentHeight)
            );

            if (totalKeyCount > 0)
            {
                int startIndex = Mathf.FloorToInt(Data.KeysListScroll.y / LanguageEditorData.KeyItemHeight);
                startIndex = Mathf.Max(0, startIndex);
                int endIndex = Mathf.Min(startIndex + maxVisibleItems, totalKeyCount);

                for (int i = startIndex; i < endIndex; i++)
                {
                    DrawKeyListItem(filteredKeys[i], i, scrollViewRect.width);
                }
            }

            GUI.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawKeyListItem(string key, int index, float width)
        {
            Rect keyRect = new Rect(4, index * LanguageEditorData.KeyItemHeight, width - 8, LanguageEditorData.KeyItemHeight);

            GUIStyle keyStyle = GetKeyButtonStyle(key == Data.SelectedKey);

            string typeIndicator = "Aa";
            if (Data.LanguageData.TryGetValue(key, out var keyData) && keyData.Count > 0)
            {
                var firstValue = keyData.Values.FirstOrDefault();
                if (firstValue is List<string> || firstValue is string[])
                    typeIndicator = "[ ]";
            }

            string displayKeyName = string.IsNullOrEmpty(Data.SelectedView) ? key : Data.GetLocalKeyName(key);
            string buttonLabel = $"<color=#888888>{typeIndicator}</color> {displayKeyName}";

            if (GUI.Button(keyRect, buttonLabel, keyStyle))
            {
                Data.SelectedKey = key;
            }
        }

        private void DrawResizeHandle()
        {
            const float handleWidth = 5f;
            Rect handleRect = EditorGUILayout.GetControlRect(
                false,
                0f,
                GUILayout.Width(handleWidth),
                GUILayout.ExpandHeight(true),
                GUILayout.MaxHeight(float.MaxValue)
            );
            handleRect.height = Mathf.Max(handleRect.height, 50f);

            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);

            if (Event.current.type == EventType.MouseDown && handleRect.Contains(Event.current.mousePosition))
            {
                _isResizingKeysList = true;
            }

            if (_isResizingKeysList)
            {
                if (Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseLeaveWindow)
                {
                    _isResizingKeysList = false;
                }
                else if (Event.current.type == EventType.MouseDrag)
                {
                    float maxWidth = WindowPosition.width * LanguageEditorData.MaxKeysListWidthRatio;
                    Data.KeysListPanelWidth = Mathf.Clamp(
                        Data.KeysListPanelWidth + Event.current.delta.x,
                        LanguageEditorData.MinKeysListWidth,
                        maxWidth
                    );
                    Event.current.Use();
                    Editor.Repaint();
                }
            }

            Color prevColor = GUI.color;
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            GUI.DrawTexture(handleRect, EditorGUIUtility.whiteTexture);
            GUI.color = prevColor;
        }

        private void DrawKeyDetailsPanel()
        {
            EditorGUILayout.BeginVertical(
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true),
                GUILayout.MaxHeight(float.MaxValue));

            if (!string.IsNullOrEmpty(Data.SelectedKey))
            {
                EditorGUILayout.LabelField($"Key Details: {Data.SelectedKey}", EditorStyles.boldLabel);
                Data.KeyDetailsScroll = EditorGUILayout.BeginScrollView(
                    Data.KeyDetailsScroll,
                    GUILayout.ExpandHeight(true),
                    GUILayout.MaxHeight(float.MaxValue));

                if (Data.LanguageData.TryGetValue(Data.SelectedKey, out var text))
                {
                    if (LanguageEditorData.IsArrayKey(text))
                        DrawArrayKeyContent();
                    else
                        DrawStringKeyContent();

                    EditorGUILayout.Space();
                    DrawKeyActionButtons();
                }
                else
                {
                    EditorGUILayout.LabelField("Selected key not found.");
                }

                EditorGUILayout.EndScrollView();
            }
            else
            {
                DrawHelpBox("Select a key from the list to view and edit its details.");
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawStringKeyContent()
        {
            EditorGUILayout.BeginVertical("box");
            foreach (var lang in Data.LanguageCodes)
            {
                EditorGUILayout.BeginVertical();
                var langName = LanguageDefinitions.GetDisplayName(lang);
                EditorGUILayout.LabelField($"{langName}:", GUILayout.Width(120));

                var currentText = Data.LanguageData[Data.SelectedKey][lang]?.ToString() ?? "";
                Rect textRect = EditorGUILayout.GetControlRect();

                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.TextField(textRect, currentText);
                EditorGUI.EndDisabledGroup();

                if (Event.current.type == EventType.MouseDown &&
                    Event.current.clickCount == 2 &&
                    textRect.Contains(Event.current.mousePosition))
                {
                    OpenTextEditor(currentText, newText =>
                    {
                        Data.LanguageData[Data.SelectedKey][lang] = newText;
                        Data.HasUnsavedChanges = true;
                        Editor.Repaint();
                    });
                    Event.current.Use();
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawArrayKeyContent()
        {
            EditorGUILayout.BeginVertical("box");

            var firstValue = Data.GetFirstValue(Data.SelectedKey);
            var array = firstValue as List<string> ?? new List<string>();
            DrawArrayElements(array);

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add New Element", GUILayout.Width(120), GUILayout.Height(25)))
            {
                Data.AddArrayElement(Data.SelectedKey);
            }

            if (GUILayout.Button("Clear Empty Elements", GUILayout.Width(140), GUILayout.Height(25)))
            {
                Data.ClearEmptyArrayElements(Data.SelectedKey);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawArrayElements(List<string> array)
        {
            bool elementDeleted = false;
            int deleteIndex = -1;

            for (int i = 0; i < array.Count; i++)
            {
                if (elementDeleted)
                    break;

                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Element {i}", EditorStyles.boldLabel);

                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("×", GUILayout.Width(25)) && EditorUtility.DisplayDialog("Delete Element",
                        $"Are you sure you want to delete element {i}?", "Yes", "No"))
                {
                    deleteIndex = i;
                    elementDeleted = true;
                }

                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                DrawArrayElementTranslations(i);

                EditorGUILayout.EndVertical();
            }

            if (elementDeleted && deleteIndex >= 0)
            {
                Data.RemoveArrayElement(Data.SelectedKey, deleteIndex);
            }
        }

        private void DrawArrayElementTranslations(int index)
        {
            foreach (var lang in Data.LanguageCodes)
            {
                var langName = LanguageDefinitions.GetDisplayName(lang);
                EditorGUILayout.LabelField($"{langName}:", GUILayout.Width(120));

                var langArray = (List<string>)Data.LanguageData[Data.SelectedKey][lang];
                var currentText = langArray[index] ?? "";

                Rect textRect = EditorGUILayout.GetControlRect();
                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.TextField(textRect, currentText);
                EditorGUI.EndDisabledGroup();

                if (Event.current.type == EventType.MouseDown &&
                    Event.current.clickCount == 2 &&
                    textRect.Contains(Event.current.mousePosition))
                {
                    int capturedIndex = index;
                    OpenTextEditor(currentText, (newText) =>
                    {
                        langArray[capturedIndex] = newText;
                        Data.HasUnsavedChanges = true;
                        Editor.Repaint();
                    });
                    Event.current.Use();
                }
            }
        }

        private void DrawKeyActionButtons()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Rename", GUILayout.Width(65)))
                RenameKey();

            EditorGUI.BeginDisabledGroup(_isTranslating);
            if (GUILayout.Button(_isTranslating ? "Translating..." : "Translate", GUILayout.Width(85)))
                ExecuteTranslation();
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Copy", GUILayout.Width(55)))
                ShowCopyKeyMenu();

            if (GUILayout.Button("JSON", GUILayout.Width(50)))
                ShowJsonOptionsMenu();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = Color.red;

            if (GUILayout.Button("Clear", GUILayout.Width(65)))
                ClearKeyData();

            if (GUILayout.Button("Delete", GUILayout.Width(65)))
                ConfirmDeleteKey();

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private void ShowCopyKeyMenu()
        {
            GenericMenu menu = new GenericMenu();

            menu.AddItem(new GUIContent("Copy Key Name"), false, () =>
            {
                EditorGUIUtility.systemCopyBuffer = Data.SelectedKey;
                Editor.ShowNotification(new GUIContent($"Key '{Data.SelectedKey}' copied to clipboard!"));
            });

            menu.AddItem(new GUIContent("Copy with GetText()"), false, () =>
            {
                EditorGUIUtility.systemCopyBuffer = $"LocalizationManager.GetText(\"{Data.SelectedKey}\")";
                Editor.ShowNotification(new GUIContent("GetText() snippet copied!"));
            });

            menu.AddItem(new GUIContent("Copy with GetArray()"), false, () =>
            {
                EditorGUIUtility.systemCopyBuffer = $"LocalizationManager.GetArray(\"{Data.SelectedKey}\")";
                Editor.ShowNotification(new GUIContent("GetArray() snippet copied!"));
            });

            menu.ShowAsContext();
        }

        private void ShowJsonOptionsMenu()
        {
            GenericMenu menu = new GenericMenu();

            menu.AddItem(new GUIContent("Copy as JSON"), false, () =>
            {
                _jsonService.CopyKeyAsJson(Data.SelectedKey, Editor);
            });

            menu.AddItem(new GUIContent("Paste from JSON"), false, () =>
            {
                _jsonService.PasteKeyFromJson(Data.SelectedKey, Editor);
            });

            menu.ShowAsContext();
        }

        private async void ExecuteTranslation()
        {
            if (_isTranslating || string.IsNullOrEmpty(Data.SelectedKey)) return;

            _isTranslating = true;
            Editor.Repaint();

            try
            {
                await _translationService.TranslateAndFill(Data.SelectedKey);
                Editor.ShowNotification(new GUIContent("Translation completed successfully!"));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Translation failed: {ex.Message}");
                Editor.ShowNotification(new GUIContent("Translation failed!"));
            }
            finally
            {
                _isTranslating = false;
                Editor.Repaint();
            }
        }

        private void AddKey(bool isArray)
        {
            string cleanKey = _newKey?.Trim();
            if (string.IsNullOrEmpty(cleanKey)) return;

            string fullKey = string.IsNullOrEmpty(Data.SelectedView) ? cleanKey : $"{Data.SelectedView}{Data.CurrentViewDelimiter}{cleanKey}";
            if (Data.AddKey(fullKey, isArray))
            {
                if (!string.IsNullOrEmpty(_newValue))
                {
                    string defaultLang = Config.LocalizationConfigProvider.Config.DefaultLanguage;
                    if (isArray)
                    {
                        foreach (var lang in Data.LanguageCodes)
                        {
                            var list = (List<string>)Data.LanguageData[fullKey][lang];
                            list.Add(lang == defaultLang ? _newValue : "");
                        }
                    }
                    else
                    {
                        Data.LanguageData[fullKey][defaultLang] = _newValue;
                    }
                }

                _newKey = "";
                _newValue = "";
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
                EditorUtility.SetDirty(Editor);
                Editor.Repaint();
            }
            else
            {
                Editor.ShowNotification(new GUIContent($"Key '{fullKey}' already exists."));
            }
        }

        private void RenameKey()
        {
            var key = Data.SelectedKey;
            string localName = Data.GetLocalKeyName(key);
            string prefix = string.IsNullOrEmpty(Data.SelectedView) ? "" : $"{Data.SelectedView}{Data.CurrentViewDelimiter}";

            OpenTextEditor(localName, (newLocalKey) =>
            {
                if (string.IsNullOrEmpty(newLocalKey))
                {
                    Editor.ShowNotification(new GUIContent("Key name cannot be empty."));
                    return;
                }

                newLocalKey = LocalizationTextEditorPopup.FilterKeyName(newLocalKey);
                if (string.IsNullOrEmpty(newLocalKey))
                {
                    Editor.ShowNotification(new GUIContent("Key name cannot be empty."));
                    return;
                }

                string newFullKey = prefix + newLocalKey;

                if (!Data.RenameKey(key, newFullKey))
                {
                    EditorUtility.DisplayDialog("Error", $"Key '{newFullKey}' already exists (key names are case-insensitive).", "OK");
                    return;
                }

                Editor.Repaint();
            }, isKeyName: true);
        }

        private void ClearKeyData()
        {
            if (!EditorUtility.DisplayDialog("Clear Key Data",
                    $"Are you sure you want to clear all translations for key '{Data.SelectedKey}'?\nThis cannot be undone!",
                    "Yes, Clear", "Cancel"))
                return;

            Data.ClearKeyTranslations(Data.SelectedKey);
            Editor.ShowNotification(new GUIContent("All translations cleared."));
            Editor.Repaint();
        }

        private void ConfirmDeleteKey()
        {
            if (EditorUtility.DisplayDialog("Delete Key",
                    $"Are you sure you want to delete the key '{Data.SelectedKey}'?", "Yes", "No"))
            {
                Data.RemoveKey(Data.SelectedKey);
                Data.SelectedKey = "";
                Editor.Repaint();
            }
        }

        public override bool HandleKeyboardInput(Event evt)
        {
            if (evt.type != EventType.KeyDown || string.IsNullOrEmpty(Data.SelectedKey))
                return false;

            bool ctrlPressed = (evt.modifiers & EventModifiers.Control) != 0;
            var filteredKeys = Data.GetFilteredKeys().ToList();
            int filteredIndex = filteredKeys.IndexOf(Data.SelectedKey);
            int masterIndex = Data.Keys.IndexOf(Data.SelectedKey);

            switch (evt.keyCode)
            {
                case KeyCode.UpArrow:
                    if (ctrlPressed && masterIndex > 0)
                    {
                        string key = Data.SelectedKey;
                        Data.Keys.RemoveAt(masterIndex);
                        Data.Keys.Insert(masterIndex - 1, key);
                        Data.HasUnsavedChanges = true;
                    }
                    else if (filteredIndex > 0)
                    {
                        Data.SelectedKey = filteredKeys[filteredIndex - 1];
                    }
                    AutoScrollToSelectedKey();
                    evt.Use();
                    Editor.Repaint();
                    return true;

                case KeyCode.DownArrow:
                    if (ctrlPressed && masterIndex < Data.Keys.Count - 1)
                    {
                        string key = Data.SelectedKey;
                        Data.Keys.RemoveAt(masterIndex);
                        Data.Keys.Insert(masterIndex + 1, key);
                        Data.HasUnsavedChanges = true;
                    }
                    else if (filteredIndex < filteredKeys.Count - 1)
                    {
                        Data.SelectedKey = filteredKeys[filteredIndex + 1];
                    }
                    AutoScrollToSelectedKey();
                    evt.Use();
                    Editor.Repaint();
                    return true;

                case KeyCode.Backspace:
                case KeyCode.Delete:
                    if (_pendingDelete)
                        return false;
                    _pendingDelete = true;
                    EditorApplication.delayCall += () =>
                    {
                        if (EditorUtility.DisplayDialog("Delete Key",
                                $"Are you sure you want to delete the key '{Data.SelectedKey}'?", "Yes", "No"))
                        {
                            Data.RemoveKey(Data.SelectedKey);
                        }
                        _pendingDelete = false;
                        Editor.Repaint();
                    };
                    evt.Use();
                    return true;

                case KeyCode.T:
                    if (ctrlPressed)
                    {
                        ExecuteTranslation();
                        evt.Use();
                        return true;
                    }
                    break;

                case KeyCode.R:
                    if (ctrlPressed)
                    {
                        RenameKey();
                        evt.Use();
                        return true;
                    }
                    break;

                case KeyCode.C:
                    if (ctrlPressed)
                    {
                        EditorGUIUtility.systemCopyBuffer = Data.SelectedKey;
                        Editor.ShowNotification(new GUIContent($"Key '{Data.SelectedKey}' copied to clipboard!"));
                        evt.Use();
                        return true;
                    }
                    break;

                case KeyCode.Escape:
                    Data.SelectedKey = null;
                    evt.Use();
                    Editor.Repaint();
                    return true;
            }

            return false;
        }

        private void AutoScrollToSelectedKey()
        {
            if (string.IsNullOrEmpty(Data.SelectedKey))
                return;

            var filteredKeys = Data.GetFilteredKeys().ToList();
            int selectedIndex = filteredKeys.IndexOf(Data.SelectedKey);
            if (selectedIndex < 0)
                return;

            float viewportHeight = Mathf.Max(_keysListViewportHeight, LanguageEditorData.KeyItemHeight);
            float itemTop = selectedIndex * LanguageEditorData.KeyItemHeight;
            float itemBottom = itemTop + LanguageEditorData.KeyItemHeight;

            if (itemTop < Data.KeysListScroll.y)
            {
                Data.KeysListScroll = new Vector2(Data.KeysListScroll.x, itemTop);
            }
            else if (itemBottom > Data.KeysListScroll.y + viewportHeight)
            {
                Data.KeysListScroll = new Vector2(Data.KeysListScroll.x, itemBottom - viewportHeight);
            }
        }

        private static void OpenTextEditor(string text, System.Action<string> onSave, bool isKeyName = false)
        {
            LocalizationTextEditorPopup.Open(text, onSave, isKeyName);
        }

        private static GUIStyle GetKeyButtonStyle(bool isSelected)
        {
            if (_transparentTexture == null)
            {
                _transparentTexture = new Texture2D(1, 1);
                _transparentTexture.SetPixel(0, 0, new Color(0, 0, 0, 0));
                _transparentTexture.Apply();
            }

            if (_keyButtonStyleNormal == null)
            {
                _keyButtonStyleNormal = new GUIStyle(EditorStyles.miniButton)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(0, 0, 2, 2),
                    margin = new RectOffset(0, 0, 2, 2),
                    normal =
                    {
                        textColor = EditorStyles.label.normal.textColor,
                        background = _transparentTexture
                    },
                    hover = { textColor = Color.green },
                    active = { textColor = Color.green },
                    richText = true
                };
            }

            if (_keyButtonStyleSelected == null)
            {
                _keyButtonStyleSelected = new GUIStyle(_keyButtonStyleNormal)
                {
                    normal =
                    {
                        textColor = Color.green,
                        background = _transparentTexture
                    }
                };
            }

            return isSelected ? _keyButtonStyleSelected : _keyButtonStyleNormal;
        }
    }
}
