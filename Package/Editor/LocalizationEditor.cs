using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using PicoShot.Localization.Config;
using PicoShot.Localization.Data;
using PicoShot.Localization.Editor.Data;
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
        private EditorTab _currentTab = EditorTab.Languages;

        private enum EditorTab
        {
            Languages,
            Keys,
            Components,
            Fonts,
            Tools,
            Debug,
            Config
        }

        #region Unity Entry Points

        [MenuItem("Tools/Localization/Language Editor")]
        public static void OpenWindow()
        {
            GetWindow<LocalizationEditor>("Language Editor");
        }

        private void OnEnable()
        {
            _data = new LanguageEditorData();
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
        }

        private void OnDisable()
        {
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

            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            {
                DrawHeader();
                DrawTabs();

                EditorGUILayout.Space();

                if (_tabs.TryGetValue(_currentTab, out var tab))
                {
                    tab.Draw();
                }

                EditorGUILayout.Space();
                DrawSaveButton();
            }
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region UI Components

        private static void DrawHeader()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Language Manager", EditorStyles.boldLabel);
            EditorGUILayout.Space();
        }

        private void DrawTabs()
        {
            EditorGUILayout.BeginHorizontal();

            foreach (EditorTab tab in Enum.GetValues(typeof(EditorTab)))
            {
                bool isActive = _currentTab == tab;
                GUI.backgroundColor = isActive ? Color.gray : Color.white;

                if (GUILayout.Button(GetTabDisplayName(tab), EditorStyles.toolbarButton))
                {
                    SwitchToTab(tab);
                }
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSaveButton()
        {
            EditorGUILayout.Space();
            GUI.backgroundColor = _data.HasUnsavedChanges ? Color.red : Color.white;
            if (GUILayout.Button("Save Changes", GUILayout.Height(30)))
            {
                SaveLanguages();
            }
            GUI.backgroundColor = Color.white;
        }

        #endregion

        #region Tab Management

        private void InitializeTabs()
        {
            _tabs = new Dictionary<EditorTab, ILocalizationEditorTab>
            {
                { EditorTab.Languages, new LanguagesTab(this, _data) },
                { EditorTab.Keys, new KeysTab(this, _data) },
                { EditorTab.Components, new ComponentsTab(this, _data) },
                { EditorTab.Fonts, new FontsTab(this, _data) },
                { EditorTab.Tools, new ToolsTab(this, _data) },
                { EditorTab.Debug, new DebugTab(this, _data) },
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

        private static string GetTabDisplayName(EditorTab tab)
        {
            return tab switch
            {
                EditorTab.Languages => "Languages",
                EditorTab.Keys => "Keys",
                EditorTab.Components => "Components",
                EditorTab.Fonts => "Fonts",
                EditorTab.Tools => "Tools",
                EditorTab.Debug => "Debug",
                EditorTab.Config => "Settings",
                _ => tab.ToString()
            };
        }

        #endregion

        #region Input Handling

        private void HandleKeyboardInput()
        {
            if (Event.current.type != EventType.KeyDown) return;
            if (GUIUtility.keyboardControl != 0) return;

            bool ctrlPressed = (Event.current.modifiers & EventModifiers.Control) != 0;

            if (ctrlPressed && Event.current.keyCode == KeyCode.S)
            {
                SaveLanguages();
                Event.current.Use();
                return;
            }

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
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);

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

                var config = LocalizationConfigProvider.Config;
                config.SetSelectedLanguages(new List<string>(_data.LanguageCodes));
                LocalizationConfigProvider.SaveConfig();

                _data.HasUnsavedChanges = false;
                ShowNotification(new GUIContent("Language data saved successfully!"));
                Debug.Log("[LocalizationEditor] Language data saved.");

                if (LocalizationManager.IsInitialized)
                {
                    LocalizationManager.Dispose();
                    LocalizationManager.Initialize();
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
        public void PurgeAllData()
        {
            if (!EditorUtility.DisplayDialog("Purge All Data",
                    "Are you sure you want to delete all language data?\n\n" +
                    "This action cannot be undone!",
                    "Yes, Delete All", "Cancel")) return;

            var config = LocalizationConfigProvider.Config;
            string defaultLang = config.DefaultLanguage;

            _data.Reset();
            _data.LanguageCodes.Add(defaultLang);

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

        private static void UnregisterEventHandlers()
        {
            LocalizationManager.Dispose();
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
