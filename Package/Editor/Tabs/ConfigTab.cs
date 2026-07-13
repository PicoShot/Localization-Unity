using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PicoShot.Localization.Config;
using PicoShot.Localization.Editor.Data;
using PicoShot.Localization.Editor.Services;
using PicoShot.Localization.Data;

namespace PicoShot.Localization.Editor.Tabs
{
    /// <summary>
    /// Tab for configuration settings and file operations.
    /// </summary>
    public sealed class ConfigTab : LocalizationEditorTabBase
    {
        private readonly JsonService _jsonService;

        private enum ConfigSubTab
        {
            General,
            Translation,
            Data
        }

        private static readonly string[] SubTabNames = { "General", "Translation", "Data" };
        private ConfigSubTab _activeSubTab = ConfigSubTab.General;

        public ConfigTab(LocalizationEditor editor, LanguageEditorData data) : base(editor, data)
        {
            _jsonService = new JsonService(data);
        }

        public override string TabName => "Settings";

        public override void Draw()
        {
            var config = LocalizationConfigProvider.Config;

            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            {
                DrawSectionHeader("Settings");
                DrawSubTabToolbar();

                EditorGUILayout.Space(5);

                using (BeginBox())
                {
                    switch (_activeSubTab)
                    {
                        case ConfigSubTab.General:
                            DrawGeneralTab(config);
                            break;
                        case ConfigSubTab.Translation:
                            DrawTranslationTab();
                            break;
                        case ConfigSubTab.Data:
                            DrawDataTab(config);
                            break;
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSubTabToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            for (int i = 0; i < SubTabNames.Length; i++)
            {
                bool isActive = _activeSubTab == (ConfigSubTab)i;
                GUI.backgroundColor = isActive ? new Color(0.7f, 0.7f, 0.7f) : Color.white;
                if (GUILayout.Button(SubTabNames[i], EditorStyles.toolbarButton))
                {
                    _activeSubTab = (ConfigSubTab)i;
                    GUI.FocusControl(null);
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawGeneralTab(LocalizationConfig config)
        {
            DrawDefaultLanguageSection(config);
            DrawTextProcessingSettings(config);
            DrawCompressionSettings(config);
            DrawProtectionSettings(config);
        }

        private void DrawTranslationTab()
        {
            DrawTranslationSettings();
        }

        private void DrawDataTab(LocalizationConfig config)
        {
            DrawFileOperations();
            DrawPathInfo();
        }

        private void DrawDefaultLanguageSection(LocalizationConfig config)
        {
            EditorGUILayout.LabelField("Default Language", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Default:", GUILayout.Width(80));

            var currentDefault = config.DefaultLanguage;
            var content = new GUIContent(LanguageDefinitions.GetDisplayName(currentDefault));
            var dropdownRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true));

            if (EditorGUI.DropdownButton(dropdownRect, content, FocusType.Keyboard))
            {
                ShowDefaultLanguageDropdown(dropdownRect, config, currentDefault);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
        }

        private void DrawTextProcessingSettings(LocalizationConfig config)
        {
            EditorGUILayout.LabelField("Text Processing", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Mixed LTR/RTL Support:", "Enable to automatically extract and fix Arabic/Persian/RTL words mixed inside LTR text (and vice-versa) instead of reversing the entire string."), GUILayout.Width(150));
            bool newSupport = EditorGUILayout.Toggle(config.SupportMixedText);
            if (newSupport != config.SupportMixedText)
            {
                config.SetSupportMixedText(newSupport);
                LocalizationConfigProvider.SaveConfig();
            }
            EditorGUILayout.EndHorizontal();

            if (config.SupportMixedText)
            {
                EditorGUILayout.HelpBox("Token-based Bi-Directional text rendering is enabled. This will dynamically isolate and fix RTL strings without breaking LTR words and punctuation.", MessageType.Info);
            }

            EditorGUILayout.Space();
        }

        private void DrawCompressionSettings(LocalizationConfig config)
        {
            EditorGUILayout.LabelField("File Compression", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Compression:", GUILayout.Width(120));
            var newMode = (CompressionMode)EditorGUILayout.EnumPopup(config.CompressionMode);
            if (newMode != config.CompressionMode)
            {
                config.SetCompressionMode(newMode);
                LocalizationConfigProvider.SaveConfig();
                GUI.changed = true;
            }
            EditorGUILayout.EndHorizontal();

            string compressionHelp = config.CompressionMode switch
            {
                CompressionMode.Disabled => "No compression. Fastest save/load but largest file sizes.",
                CompressionMode.Fastest => "Fast compression. Good for development with quick iteration times.",
                CompressionMode.Optimal => "Best compression ratio. Smaller files but slower saves. Recommended for builds.",
                _ => ""
            };
            EditorGUILayout.HelpBox(compressionHelp, MessageType.Info);

            EditorGUILayout.Space();
        }

        private void DrawProtectionSettings(LocalizationConfig config)
        {
            EditorGUILayout.LabelField("Protection Settings (experimental)", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Protection Mode:", GUILayout.Width(120));
            var newMode = (ProtectionMode)EditorGUILayout.EnumPopup(config.ProtectionMode);
            if (newMode != config.ProtectionMode)
            {
                config.SetProtectionMode(newMode);
                LocalizationConfigProvider.SaveConfig();
                GUI.changed = true;
            }
            EditorGUILayout.EndHorizontal();

            if (config.IsAntiTamperEnabled)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("", GUILayout.Width(120));
                if (GUILayout.Button("Sync File Hashes", GUILayout.Height(25)))
                {
                    SyncFileHashes(config);
                }
                EditorGUILayout.EndHorizontal();

                DrawHelpBox(
                    "Anti-Tamper: File hashes are verified at runtime. " +
                    "Click 'Sync File Hashes' after modifying language files.");
            }

            string protectionHelp = config.ProtectionMode switch
            {
                ProtectionMode.Disabled => "Protection is disabled. All language files can be loaded.",
                ProtectionMode.SelectionOnly => "Only selected languages can be loaded at runtime.",
                ProtectionMode.AntiTamper => "Anti-tamper protection with hash verification.",
                ProtectionMode.Both => "Full protection: Only selected languages can be loaded AND file hashes are verified.",
                _ => ""
            };
            DrawHelpBox(protectionHelp);

            EditorGUILayout.Space();
        }

        private void DrawTranslationSettings()
        {
            EditorGUILayout.LabelField("Translation API", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Provider:", GUILayout.Width(120));
            var newProvider = (TranslationProvider)EditorGUILayout.EnumPopup(Data.ActiveTranslationProvider);
            if (newProvider != Data.ActiveTranslationProvider)
            {
                Data.ActiveTranslationProvider = newProvider;
                GUI.changed = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (Data.ActiveTranslationProvider == TranslationProvider.DeepL)
            {
                DrawDeepLSettings();
            }
            else if (Data.ActiveTranslationProvider == TranslationProvider.Gemini)
            {
                DrawGeminiSettings();
            }
        }

        private void DrawDeepLSettings()
        {
            EditorGUILayout.LabelField("DeepL Settings", EditorStyles.boldLabel);

            DrawApiUrlField();
            DrawApiKeyField();
            DrawDeepLContextField();

            EditorGUILayout.Space();
        }

        private void DrawGeminiSettings()
        {
            EditorGUILayout.LabelField("Gemini API Settings", EditorStyles.boldLabel);

            DrawGeminiApiKeyField();
            DrawGeminiModelField();

            if (Data.GeminiModel == "custom")
            {
                DrawGeminiCustomModelField();
            }

            DrawGeminiContextField();

            EditorGUILayout.Space();
        }

        private void DrawApiUrlField()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("API URL:", GUILayout.Width(70));
            Data.DeeplApiUrl = EditorGUILayout.TextField(Data.DeeplApiUrl);

            GUI.enabled = Data.DeeplApiUrl != LanguageEditorData.DefaultDeeplApiUrl;
            if (GUILayout.Button("Reset", GUILayout.Width(60)))
            {
                Data.DeeplApiUrl = LanguageEditorData.DefaultDeeplApiUrl;
                GUI.FocusControl(null);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawApiKeyField()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("API Key:", GUILayout.Width(70));
            Data.DeeplApiKey = EditorGUILayout.PasswordField(Data.DeeplApiKey);

            GUI.enabled = !string.IsNullOrEmpty(Data.DeeplApiKey);
            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                Data.DeeplApiKey = "";
                GUI.FocusControl(null);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDeepLContextField()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Translation Context:", GUILayout.Width(150));

            GUI.enabled = Data.DeeplContext != LanguageEditorData.DefaultDeepLContext;
            if (GUILayout.Button("Reset to Default", GUILayout.Width(120)))
            {
                Data.DeeplContext = LanguageEditorData.DefaultDeepLContext;
                GUI.FocusControl(null);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            Data.DeeplContext = EditorGUILayout.TextArea(Data.DeeplContext, GUILayout.MinHeight(60));
        }

        private void DrawGeminiApiKeyField()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("API Key:", GUILayout.Width(70));
            Data.GeminiApiKey = EditorGUILayout.PasswordField(Data.GeminiApiKey);

            GUI.enabled = !string.IsNullOrEmpty(Data.GeminiApiKey);
            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                Data.GeminiApiKey = "";
                GUI.FocusControl(null);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawGeminiModelField()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Model:", GUILayout.Width(70));

            string[] defaultModels = { 
                "gemini-2.5-flash", 
                "gemini-2.5-flash-lite",
                "gemini-3-flash-preview",
                "gemini-3.1-flash-lite-preview",
                "gemini-3.5-flash",
                "custom" 
            };

            int selectedIndex = System.Array.IndexOf(defaultModels, Data.GeminiModel);
            if (selectedIndex == -1)
                selectedIndex = defaultModels.Length - 1; // "custom" if unknown

            int newIndex = EditorGUILayout.Popup(selectedIndex, defaultModels);
            if (newIndex != selectedIndex)
            {
                Data.GeminiModel = defaultModels[newIndex];
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawGeminiCustomModelField()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Custom Model:", GUILayout.Width(100));
            Data.GeminiCustomModel = EditorGUILayout.TextField(Data.GeminiCustomModel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawGeminiContextField()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Translation prompt/context:", GUILayout.Width(200));

            GUI.enabled = Data.GeminiContext != LanguageEditorData.DefaultGeminiContext;
            if (GUILayout.Button("Reset to Default", GUILayout.Width(120)))
            {
                Data.GeminiContext = LanguageEditorData.DefaultGeminiContext;
                GUI.FocusControl(null);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            Data.GeminiContext = EditorGUILayout.TextArea(Data.GeminiContext, GUILayout.MinHeight(80));
        }

        private void DrawFileOperations()
        {
            EditorGUILayout.LabelField("File Operations", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("Delete All Language Data", GUILayout.Height(25)))
            {
                Editor.PurgeAllData();
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("Open Languages Folder", GUILayout.Height(25)))
            {
                OpenLanguagesFolder();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Export to JSON", GUILayout.Height(25)))
            {
                _jsonService.ExportToJson();
                Editor.Repaint();
            }

            if (GUILayout.Button("Import from JSON", GUILayout.Height(25)))
            {
                _jsonService.ImportFromJson();
                Editor.Repaint();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
        }

        private void DrawPathInfo()
        {
            DrawHelpBox($"Languages Path: {LocalizationManager.LanguagesPath}");
        }

        private void ShowDefaultLanguageDropdown(Rect dropdownRect, LocalizationConfig config, string currentDefault)
        {
            var menu = new GenericMenu();

            foreach (var lang in Data.LanguageCodes)
            {
                menu.AddItem(
                    new GUIContent(LanguageDefinitions.GetDisplayName(lang)),
                    currentDefault == lang,
                    () =>
                    {
                        config.SetDefaultLanguage(lang);
                        LocalizationConfigProvider.SaveConfig();
                        GUI.changed = true;
                        Editor.Repaint();
                    }
                );
            }

            menu.DropDown(dropdownRect);
        }

        private void SyncFileHashes(LocalizationConfig config)
        {
            try
            {
                if (!Directory.Exists(LocalizationManager.LanguagesPath))
                {
                    EditorUtility.DisplayDialog("Error", "Languages directory not found.", "OK");
                    return;
                }

                var files = Directory.GetFiles(LocalizationManager.LanguagesPath, "*.bloc");
                int syncedCount = 0;
                int removedCount = 0;

                var existingHashes = new HashSet<string>(config.GetFileHashes().Select(h => h.fileName));

                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);
                    string hash = LocalizationManager.CalculateFileHash(file);
                    config.SetFileHash(fileName, hash);
                    syncedCount++;
                    existingHashes.Remove(fileName);
                }

                foreach (var oldFile in existingHashes)
                {
                    config.RemoveFileHash(oldFile);
                    removedCount++;
                }

                LocalizationConfigProvider.SaveConfig();

                EditorUtility.DisplayDialog("Hashes Synced",
                    $"Successfully synced {syncedCount} file hashes.\n" +
                    $"Removed {removedCount} outdated hashes.", "OK");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to sync hashes: {ex.Message}", "OK");
                Debug.LogError($"[LocalizationEditor] Hash sync failed: {ex}");
            }
        }

        private static void OpenLanguagesFolder()
        {
            string path = LocalizationManager.LanguagesPath;

            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", path);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error opening languages folder: {e}");
            }
        }
    }
}
