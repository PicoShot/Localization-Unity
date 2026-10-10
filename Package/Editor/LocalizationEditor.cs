using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using PicoShot.Localization.Config;
using PicoShot.Localization.Data;
using PicoShot.Localization.Editor;
using PicoShot.Localization.Editor.Data;
using PicoShot.Localization.Editor.Services;
using PicoShot.Localization.Editor.Tabs;
using PicoShot.Localization.Bloc;

namespace PicoShot.Localization
{
    /// <summary>
    /// Main editor window for managing localization data.
    /// Uses a tab-based architecture for clean separation of concerns.
    /// </summary>
    public sealed class LocalizationEditor : EditorWindow
    {
        // Data
        private LanguageEditorData _data;

        // Tabs
        private Dictionary<EditorTab, ILocalizationEditorTab> _tabs;
        private EditorTab _currentTab = EditorTab.Localization;

        private enum EditorTab
        {
            Localization,
            Keys,
            Components,
            Tools,
            Config
        }

        #region Unity Entry Points

        [MenuItem("Tools/Localization/Language Editor")]
        public static void OpenWindow()
        {
            GetWindow<LocalizationEditor>("Localization");
        }   

        private void OnEnable()
        {
            _data = new LanguageEditorData();
            _data.History.Changed += OnHistoryChanged;
            InitializeTabs();

            LoadLanguages(out var oldVersions);

            if (oldVersions?.Length > 0)
            {
                if (EditorUtility.DisplayDialog("Bloc versions is old",
                    $"These language files `{string.Join(", ", oldVersions)}` use an older version of the bloc format. Would you like to upgrade these files to the newer version?",
                    "Upgrade", "Use old version"))
                {
                    UpgradeLocaleFiles();
                }
            }

            CompilationPipeline.compilationStarted += OnBeforeCompile;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Editor.Mcp.LocalizationMcpServer.ExternalChanged += OnMcpExternalChanged;
        }

        private void OnMcpExternalChanged()
        {
            if (_data == null) return;
            if (_data.HasUnsavedChanges)
            {
                ShowNotification(new GUIContent("The MCP server changed the locale files. Use ☰ > Reload from Disk to load them (saving now would overwrite them)."));
                return;
            }
            LoadLanguages(out _);
            ShowNotification(new GUIContent("Reloaded locale files changed via MCP."));
            Repaint();
        }

        private void OnHistoryChanged()
        {
            GUIUtility.keyboardControl = 0;
            Repaint();
        }

        private void OnDisable()
        {
            _data?.DisposeHistory();
            UnregisterEventHandlers();
            CompilationPipeline.compilationStarted -= OnBeforeCompile;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnBeforeCompile(object _)
        {
            PromptAutoSave("before compiling");
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    PromptAutoSave("before entering Play Mode");
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    PromptAutoSave("before exiting Play Mode");
                    break;
            }
        }

        private void OnDestroy()
        {
            PromptAutoSave("before closing the editor");
        }

        private void OnGUI()
        {
            HandleKeyboardInput();
            UpdateTitle();

            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            {
                DrawTabs();
                EditorGUILayout.Space(4);

                if (_tabs.TryGetValue(_currentTab, out var tab))
                {
                    tab.Draw();
                }

                DrawStatusBar();
            }
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Window Frame

        private static readonly EditorTab[] TabOrder = (EditorTab[])Enum.GetValues(typeof(EditorTab));
        private static GUIStyle _saveButtonStyle;
        private DateTime? _lastSaved;
        private bool _titleDirty;
        private bool _titleInitialized;

        private static string ActionKeyName => Application.platform == RuntimePlatform.OSXEditor ? "Cmd" : "Ctrl";

        /// <summary>
        /// Window title gets a trailing * while there are unsaved changes, like Unity's scene tabs.
        /// </summary>
        private void UpdateTitle()
        {
            bool dirty = _data.HasUnsavedChanges;
            if (_titleInitialized && dirty == _titleDirty)
                return;

            _titleInitialized = true;
            _titleDirty = dirty;
            var icon = EditorGUIUtility.IconContent("Font Icon").image;
            titleContent = new GUIContent(dirty ? "Localization*" : "Localization", icon,
                dirty ? "Localization (unsaved changes)" : "Localization");
        }

        private void DrawTabs()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();

            foreach (var tab in TabOrder)
            {
                GUI.backgroundColor = _currentTab == tab ? Color.gray : Color.white;
                if (GUILayout.Button(GetTabDisplayName(tab), EditorStyles.toolbarButton))
                    SwitchToTab(tab);
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private void ShowWindowMenu(Rect rect)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Reload from Disk"), false, ReloadFromDisk);
            menu.AddItem(new GUIContent("Open Locale Folder"), false, () =>
            {
                string path = LocalizationManager.LanguagesPath;
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                string fullPath = Path.GetFullPath(path).Replace('\\', '/');
                Application.OpenURL((fullPath.StartsWith("/") ? "file://" : "file:///") + fullPath);
            });
            menu.DropDown(rect);
        }

        /// <summary>
        /// Loads the locale files again, for example after the MCP server or version control changed them.
        /// </summary>
        private void ReloadFromDisk()
        {
            if (_data.HasUnsavedChanges && !EditorUtility.DisplayDialog("Reload from Disk",
                    "You have unsaved changes. Reloading replaces them with what's in the locale files.", "Discard and Reload", "Cancel"))
                return;

            LoadLanguages(out _);
            GUIUtility.keyboardControl = 0;
            ShowNotification(new GUIContent("Reloaded locale files"));
            Repaint();
        }

        private void DrawStatusBar()
        {
            var rect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), LocalizationEditorStyles.Separator);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + 1f, rect.width, rect.height - 1f), LocalizationEditorStyles.RowEven);
            }

            var content = new Rect(rect.x + 6f, rect.y + 1f, rect.width - 12f, rect.height - 1f);
            float x = content.x;

            if (_data.HasUnsavedChanges)
            {
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(new Rect(x, content.center.y - 3f, 6f, 6f), LocalizationEditorStyles.Warning);
                x += 12f;
                GUI.Label(new Rect(x, content.y, 260f, content.height),
                    new GUIContent($"Unsaved changes · {ActionKeyName}+S to save"), LocalizationEditorStyles.MutedLabel);
            }
            else
            {
                string saved = _lastSaved.HasValue ? $"Saved {FormatAge(DateTime.Now - _lastSaved.Value)}" : "All changes saved";
                GUI.Label(new Rect(x, content.y, 260f, content.height), saved, LocalizationEditorStyles.MutedLabel);
            }

            float right = content.xMax;

            var menuRect = new Rect(right - 22f, content.y + 1f, 22f, content.height - 2f);
            if (GUI.Button(menuRect, EditorGUIUtility.IconContent("_Menu"), EditorStyles.iconButton))
                ShowWindowMenu(menuRect);
            right = menuRect.x - 4f;

            bool dirty = _data.HasUnsavedChanges;
            _saveButtonStyle ??= new GUIStyle(EditorStyles.miniButton) { fontStyle = FontStyle.Bold };
            var saveRect = new Rect(right - 64f, content.y + 2f, 64f, content.height - 4f);
            using (new EditorGUI.DisabledScope(!dirty))
            {
                var saveContent = new GUIContent(dirty ? "Save" : "Saved",
                    dirty ? $"Write all changes to the locale files ({ActionKeyName}+S)" : "Everything is saved");
                if (GUI.Button(saveRect, saveContent, dirty ? _saveButtonStyle : EditorStyles.miniButton))
                {
                    SaveLanguages();
                    GUIUtility.ExitGUI();
                }
            }
            right = saveRect.x - 10f;

            if (Editor.Mcp.LocalizationMcpServer.IsRunning)
            {
                float w = LocalizationEditorStyles.GetBadgeWidth("MCP");
                LocalizationEditorStyles.DrawBadge(new Rect(right - w, content.y, w, content.height), "MCP", LocalizationEditorStyles.Success,
                    $"MCP server running at {Editor.Mcp.LocalizationMcpServer.Url}");
                right -= w + 8f;
            }

            int languages = _data.LanguageCodes.Count;
            int keys = _data.Keys.Count;
            string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;
            GUI.Label(new Rect(x + 260f, content.y, Mathf.Max(0f, right - x - 260f), content.height),
                new GUIContent($"{languages} {(languages == 1 ? "language" : "languages")} · {keys:N0} {(keys == 1 ? "key" : "keys")} · source {defaultLang}"),
                LocalizationEditorStyles.MutedLabelRight);
        }

        private static string FormatAge(TimeSpan age)
        {
            if (age.TotalSeconds < 60) return "just now";
            if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes} min ago";
            return $"at {DateTime.Now - age:HH:mm}";
        }

        #endregion

        #region Tab Management

        private void InitializeTabs()
        {
            _tabs = new Dictionary<EditorTab, ILocalizationEditorTab>
            {
                { EditorTab.Localization, new LocalizationTab(this, _data) },
                { EditorTab.Keys, new KeysTab(this, _data) },
                { EditorTab.Components, new ComponentsTab(this, _data) },
                { EditorTab.Tools, new ToolsTab(this, _data) },
                { EditorTab.Config, new ConfigTab(this, _data) }
            };
        }

        private void SwitchToTab(EditorTab newTab)
        {
            if (_currentTab == newTab) return;

            if (_tabs.TryGetValue(_currentTab, out var currentTabInstance))
            {
                currentTabInstance.OnExit();
            }

            _currentTab = newTab;

            if (_tabs.TryGetValue(newTab, out var newTabInstance))
            {
                newTabInstance.OnEnter();
            }

            GUI.FocusControl(null);
            Repaint();
        }

        /// <summary>
        /// Opens the Keys tab filtered to keys that are untranslated in the given language.
        /// </summary>
        public void ShowUntranslatedKeys(string languageCode)
        {
            _data.UntranslatedLanguageFilter = languageCode;
            SwitchToTab(EditorTab.Keys);
        }

        /// <summary>
        /// Opens the Keys tab showing all keys that match a status filter, or that are untranslated in one language.
        /// </summary>
        public void ShowKeysFiltered(KeyStatusFilter status, string untranslatedLanguage = null)
        {
            _data.KeySearchFilter = "";
            _data.SelectedView = "";
            _data.ShowArrayKeysOnly = false;
            _data.ShowStringKeysOnly = false;
            _data.StatusFilter = status;
            _data.UntranslatedLanguageFilter = untranslatedLanguage;
            SwitchToTab(EditorTab.Keys);
        }

        /// <summary>
        /// Opens the Keys tab with the given key selected, clearing filters that would hide it.
        /// </summary>
        public void ShowKey(string key)
        {
            if (string.IsNullOrEmpty(key) || !_data.LanguageData.ContainsKey(key))
                return;

            _data.SelectedKey = key;
            if (!_data.GetFilteredKeys().Contains(key))
            {
                _data.KeySearchFilter = "";
                _data.ShowArrayKeysOnly = false;
                _data.ShowStringKeysOnly = false;
                _data.StatusFilter = KeyStatusFilter.All;
                _data.UntranslatedLanguageFilter = null;
                if (!_data.GetFilteredKeys().Contains(key))
                    _data.SelectedView = "";
            }

            SwitchToTab(EditorTab.Keys);
        }

        private static string GetTabDisplayName(EditorTab tab)
        {
            return tab switch
            {
                EditorTab.Localization => "Localization",
                EditorTab.Keys => "Keys",
                EditorTab.Components => "Components",
                EditorTab.Tools => "Tools",
                EditorTab.Config => "Settings",
                _ => tab.ToString()
            };
        }

        #endregion

        #region Input Handling

        private void HandleKeyboardInput()
        {
            if (Event.current.type != EventType.KeyDown) return;

            if (EditorGUI.actionKey && Event.current.keyCode == KeyCode.S)
            {
                SaveLanguages();
                Event.current.Use();
                return;
            }

            if (GUIUtility.keyboardControl != 0) return;

            if (_tabs.TryGetValue(_currentTab, out var tab))
            {
                if (tab.HandleKeyboardInput(Event.current))
                {
                    return;
                }
            }
        }

        #endregion

        #region Data Management

        /// <summary>
        /// Loads all language data from BLOC files.
        /// </summary>
        private void LoadLanguages(out string[] oldVersions)
        {
            try
            {
                string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;

                _data.Reset();

                if (!Directory.Exists(LocalizationManager.LanguagesPath))
                {
                    Debug.Log("[LocalizationEditor] Languages directory not found. Creating new data.");
                    oldVersions = Array.Empty<string>();
                    return;
                }

                var blocFiles = Directory.GetFiles(LocalizationManager.LanguagesPath, "*.bloc", SearchOption.TopDirectoryOnly);
                oldVersions = new string[blocFiles.Length];
                int oldVersionPtr = 0;

                foreach (var file in blocFiles)
                {
                    try
                    {
                        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);

                        if (!BlocFormat.Validate(stream, out var version, out var langCode, out var exception) || string.IsNullOrEmpty(langCode))
                        {
                            Debug.LogWarning($"[LocalizationEditor] Skipping invalid/corrupted file: {Path.GetFileName(file)}");

                            if (exception != null)
                                Debug.LogError($"BLOC validation error: {exception}");

                            continue;
                        }
                        stream.Position = 0;

                        if (!LanguageDefinitions.IsValidLanguage(langCode))
                        {
                            Debug.LogError($"[LocalizationEditor] Rejecting file '{Path.GetFileName(file)}' - unsupported language code: '{langCode}'");
                            continue;
                        }

                        string fileNameLanguage = Path.GetFileNameWithoutExtension(file);
                        if (!string.Equals(fileNameLanguage, langCode, StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.LogError($"[LocalizationEditor] Rejecting file '{Path.GetFileName(file)}' - filename mismatch: " +
                                $"expected '{langCode}.bloc' but filename is '{Path.GetFileName(file)}'. " +
                                $"Filename must match the language code stored in the file header.");
                            continue;
                        }

                        var localeData = BlocFormat.Deserialize(stream, out var info);

                        if (info.Version < BlocFormat.LatestVersion)
                            oldVersions[oldVersionPtr++] = info.LanguageCode;

                        if (!_data.LanguageCodes.Contains(langCode))
                        {
                            _data.LanguageCodes.Add(langCode);
                        }

                        foreach (var entry in localeData.Translations)
                        {
                            string key = entry.Key;
                            object value = entry.Value;

                            if (string.IsNullOrEmpty(key))
                                continue;

                            if (!_data.LanguageData.TryGetValue(key, out var keyData))
                            {
                                keyData = new Dictionary<string, object>(_data.LanguageCodes.Count);
                                _data.LanguageData[key] = keyData;
                                _data.Keys.Add(key);
                            }

                            keyData[langCode] = value;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[LocalizationEditor] Error loading file '{file}': {ex}");
                    }
                }

                Array.Resize(ref oldVersions, oldVersionPtr);
                SyncMissingLanguageEntries();
                SyncProtectionOnLoad();
            }
            catch (Exception ex)
            {
                oldVersions = Array.Empty<string>();
                Debug.LogError($"[LocalizationEditor] Error loading language data: {ex}");
                _data.Reset();
            }
        }

        /// <summary>
        /// Upgrades all BLOC language files.
        /// </summary>
        private void UpgradeLocaleFiles()
        {
            string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;

            if (!Directory.Exists(LocalizationManager.LanguagesPath))
            {
                Debug.Log("[LocalizationEditor] Languages directory not found. Creating new data.");
                return;
            }

            var blocFiles = Directory.GetFiles(LocalizationManager.LanguagesPath, "*.bloc", SearchOption.TopDirectoryOnly);
            foreach (var file in blocFiles)
            {
                string tempFile = $"{file}.tmp";
                try
                {
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (!BlocFormat.Validate(stream, out var version, out var langCode, out var exception) || string.IsNullOrEmpty(langCode))
                        {
                            Debug.LogWarning($"[LocalizationEditor] Skipping invalid/corrupted file: {Path.GetFileName(file)}");

                            if (exception != null)
                                Debug.Log($"BLOC validation error: {exception}");

                            continue;
                        }
                        stream.Position = 0;

                        if (!LanguageDefinitions.IsValidLanguage(langCode))
                        {
                            Debug.LogError($"[LocalizationEditor] Rejecting file '{Path.GetFileName(file)}' - unsupported language code: '{langCode}'");
                            continue;
                        }

                        string fileNameLanguage = Path.GetFileNameWithoutExtension(file);
                        if (!string.Equals(fileNameLanguage, langCode, StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.LogError($"[LocalizationEditor] Rejecting file '{Path.GetFileName(file)}' - filename mismatch: " +
                                $"expected '{langCode}.bloc' but filename is '{Path.GetFileName(file)}'. " +
                                $"Filename must match the language code stored in the file header.");
                            continue;
                        }

                        if (version >= BlocFormat.LatestVersion)
                            continue;

                        using (var destStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            BlocFormat.Upgrade(stream, destStream, BlocFormat.LatestVersion, LocaleBlocSerializer.CompressionLevel);

                            destStream.Flush(true);
                        }
                    }

                    if (File.Exists(file))
                        File.Replace(tempFile, file, $"{file}.bak");
                    else
                        File.Move(tempFile, file);

                    Debug.Log($"[LocalizationEditor] Locale file upgraded '{file}'");
                }
                catch (Exception ex)
                {
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);

                    Debug.LogError($"[LocalizationEditor] Error upgrading file '{file}': {ex}");
                }
            }

            LocalizationManager.DeleteJunkFiles();
            LocaleHashSync.SyncIfEnabled("file upgrade");
        }

        /// <summary>
        /// Fills in missing language entries for all keys.
        /// </summary>
        private void SyncMissingLanguageEntries()
        {
            foreach (var key in _data.Keys)
            {
                var firstValue = LanguageEditorData.GetFirstValue(_data.LanguageData[key]);
                bool isArray = firstValue is List<string>;

                foreach (var lang in _data.LanguageCodes)
                {
                    if (!_data.LanguageData[key].ContainsKey(lang))
                    {
                        if (isArray && firstValue is List<string> list)
                        {
                            _data.LanguageData[key][lang] = new List<string>(new string[list.Count]);
                        }
                        else
                        {
                            _data.LanguageData[key][lang] = "";
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Syncs protection settings with loaded languages.
        /// </summary>
        private void SyncProtectionOnLoad()
        {
            var config = LocalizationConfigProvider.Config;
            bool changed = false;

            foreach (var lang in _data.LanguageCodes)
            {
                if (!config.SelectedLanguages.Contains(lang))
                {
                    config.AddSelectedLanguage(lang);
                    changed = true;
                }
            }

            if (changed)
            {
                LocalizationConfigProvider.SaveConfig();
            }
        }

        /// <summary>
        /// Saves all language data to BLOC files.
        /// </summary>
        public void SaveLanguages()
        {
            try
            {
                ApplyCompressionSettings();

                if (!Directory.Exists(LocalizationManager.LanguagesPath))
                {
                    Directory.CreateDirectory(LocalizationManager.LanguagesPath);
                }

                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                foreach (var lang in _data.LanguageCodes)
                {
                    var localeData = new LocaleData
                    {
                        Version = BlocFormat.LatestVersion,
                        LanguageCode = lang,
                        Timestamp = timestamp,
                        Translations = new Dictionary<string, object>(),
                    };

                    foreach (var key in _data.Keys)
                    {
                        if (_data.LanguageData[key].TryGetValue(lang, out var value))
                        {
                            localeData.Translations[key] = value;
                        }
                    }

                    string filePath = LocalizationManager.GetLanguageFilePath(lang);
                    LocaleBlocSerializer.SaveFile(filePath, localeData);
                }

                foreach (var lang in _data.PendingRemovedLanguages)
                {
                    if (_data.LanguageCodes.Contains(lang))
                        continue;

                    string filePath = LocalizationManager.GetLanguageFilePath(lang);
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
                _data.PendingRemovedLanguages.Clear();

                var config = LocalizationConfigProvider.Config;
                config.SetSelectedLanguages(new List<string>(_data.LanguageCodes));
                LocalizationConfigProvider.SaveConfig();
                LocaleHashSync.SyncIfEnabled("save");

                _data.HasUnsavedChanges = false;
                _lastSaved = DateTime.Now;
                ShowNotification(new GUIContent("Saved"));

                if (LocalizationManager.IsInitialized)
                {
                    LocalizationManager.Reload();
                }

                if (config.GenerateTypedKeys)
                {
                    TypedKeyGenerator.Generate(_data);
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to save language data: {ex.Message}", "OK");
                Debug.LogError($"[LocalizationEditor] Error saving language data: {ex}");
            }
        }

        /// <summary>
        /// Applies compression settings from config to the serializer.
        /// </summary>
        private static void ApplyCompressionSettings()
        {
            var config = LocalizationConfigProvider.Config;
            LocaleBlocSerializer.CompressionLevel = config.CompressionMode switch
            {
                CompressionMode.Disabled => System.IO.Compression.CompressionLevel.NoCompression,
                CompressionMode.Fastest => System.IO.Compression.CompressionLevel.Fastest,
                CompressionMode.Optimal => System.IO.Compression.CompressionLevel.Optimal,
                _ => System.IO.Compression.CompressionLevel.Optimal
            };
        }

        /// <summary>
        /// Shows a dialog to save unsaved changes.
        /// </summary>
        private void PromptAutoSave(string context)
        {
            if (!_data.HasUnsavedChanges) return;

            if (EditorUtility.DisplayDialog("Unsaved Changes",
                    $"You have unsaved changes. Would you like to save them {context}?",
                    "Save", "Don't Save"))
            {
                SaveLanguages();
            }
            else
            {
                _data.HasUnsavedChanges = false;
            }
        }

        /// <summary>
        /// Deletes all language data permanently.
        /// </summary>
        public void PurgeAllData(bool confirm = true)
        {
            if (confirm && !EditorUtility.DisplayDialog("Purge All Data",
                    "Are you sure you want to delete all language data?\n\n" +
                    "This action cannot be undone!",
                    "Yes, Delete All", "Cancel")) return;

            var config = LocalizationConfigProvider.Config;
            string defaultLang = config.DefaultLanguage;

            _data.Reset();

            if (Directory.Exists(LocalizationManager.LanguagesPath))
            {
                var files = Directory.GetFiles(LocalizationManager.LanguagesPath, "*.bloc");
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }

            config.SetSelectedLanguages(new List<string> { defaultLang });
            LocalizationConfigProvider.SaveConfig();

            SaveLanguages();
            Repaint();
        }

        private void UnregisterEventHandlers()
        {
            Editor.Mcp.LocalizationMcpServer.ExternalChanged -= OnMcpExternalChanged;
        }

        #endregion

        #region Public API for Tabs

        /// <summary>
        /// Gets the current editor data. Used by tabs.
        /// </summary>
        public LanguageEditorData GetData() => _data;

        #endregion
    }
}
