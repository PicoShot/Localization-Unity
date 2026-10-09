using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
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
    /// Project, translation, file/build and MCP settings.
    /// </summary>
    public sealed class ConfigTab : LocalizationEditorTabBase
    {
        private enum ConfigSubTab
        {
            General,
            Translation,
            FilesAndBuild,
            Mcp
        }

        private enum TestState
        {
            None,
            Testing,
            Ok,
            Failed
        }

        private const float LabelWidth = 130f;
        private const double FileInfoRefreshSeconds = 3d;
        private const string DeeplProUrl = "https://api.deepl.com/v2/translate";

        private const string ScopeProject = "Shared · saved in the project config";
        private const string ScopeUser = "Only you · this project";
        private const string ScopeMachine = "Only you · every project on this machine";

        private static readonly string[] SubTabNames = { "General", "Translation", "Files & Build", "MCP" };
        private static readonly string[] ProviderNames = { "DeepL", "Gemini" };
        private static readonly string[] DelimiterNames = { "Dot  .", "Underscore  _" };
        private static readonly string[] GeminiModels =
        {
            "gemini-2.5-flash",
            "gemini-2.5-flash-lite",
            "gemini-3-flash-preview",
            "gemini-3.1-flash-lite-preview",
            "gemini-3.5-flash",
            "custom"
        };

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        private readonly JsonService _jsonService;
        private ConfigSubTab _activeSubTab = ConfigSubTab.General;
        private Vector2 _scroll;

        // Translation
        private bool _showApiKey;
        private TestState _testState;
        private string _testMessage = "";
        private string _testedFor;

        // Files & Build, refreshed every few seconds
        private double _fileInfoTime = -1d;
        private int _fileCount;
        private long _fileBytes;
        private int _hashesOutOfSync = -1;

        public ConfigTab(LocalizationEditor editor, LanguageEditorData data) : base(editor, data)
        {
            _jsonService = new JsonService(data);
        }

        public override string TabName => "Settings";

        public override void OnEnter()
        {
            _fileInfoTime = -1d;
        }

        public override void Draw()
        {
            var config = LocalizationConfigProvider.Config;

            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            {

                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                int selected = GUILayout.Toolbar((int)_activeSubTab, SubTabNames, EditorStyles.toolbarButton);
                if (selected != (int)_activeSubTab)
                {
                    _activeSubTab = (ConfigSubTab)selected;
                    _fileInfoTime = -1d;
                    GUIUtility.keyboardControl = 0;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);

                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
                {
                    switch (_activeSubTab)
                    {
                        case ConfigSubTab.General:
                            DrawGeneral(config);
                            break;
                        case ConfigSubTab.Translation:
                            DrawTranslation();
                            break;
                        case ConfigSubTab.FilesAndBuild:
                            DrawFilesAndBuild(config);
                            break;
                        case ConfigSubTab.Mcp:
                            DrawMcp();
                            break;
                    }
                    EditorGUILayout.Space(6);
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        #region Layout Helpers

        private static void BeginRow(string label, string tooltip = null)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(label, tooltip), GUILayout.Width(LabelWidth));
        }

        private static void EndRow()
        {
            EditorGUILayout.EndHorizontal();
        }

        private static void Description(string text, GUIStyle style = null)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(LabelWidth + 4f);
            GUILayout.Label(text, style ?? Styles.Description);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);
        }

        private static void SectionGap()
        {
            EditorGUILayout.Space(14);
        }

        private static void ChangeConfig(LocalizationConfig config, string undoName, Action change)
        {
            Undo.RecordObject(config, undoName);
            change();
            LocalizationConfigProvider.SaveConfig();
        }

        private static Rect BadgeRect(string text)
        {
            return GUILayoutUtility.GetRect(Styles.GetBadgeWidth(text), 18f, GUILayout.ExpandWidth(false));
        }

        #endregion

        #region General

        private void DrawGeneral(LocalizationConfig config)
        {
            Styles.DrawSectionTitle("Project", ScopeProject);

            BeginRow("Default language", "The source language of the project");
            {
                string current = config.DefaultLanguage;
                var content = new GUIContent($"{LanguageDefinitions.GetDisplayName(current)} ({current})");
                var rect = EditorGUILayout.GetControlRect(GUILayout.MaxWidth(260));
                if (EditorGUI.DropdownButton(rect, content, FocusType.Keyboard))
                    ShowDefaultLanguageMenu(rect, config);
            }
            EndRow();
            Description("Other languages are translated from it, and missing text falls back to it. Also shown as SOURCE in the Keys tab.");

            BeginRow("Mixed LTR/RTL text", "Bidirectional handling of mixed text");
            {
                bool mixed = EditorGUILayout.Toggle(config.SupportMixedText);
                if (mixed != config.SupportMixedText)
                    ChangeConfig(config, "Change Mixed Text Support", () => config.SetSupportMixedText(mixed));
            }
            EndRow();
            Description("Fixes Arabic, Persian and other RTL words inside LTR text (and the other way around) separately, instead of reversing the whole string.");

            SectionGap();
            Styles.DrawSectionTitle("Editor", ScopeUser);

            BeginRow("View delimiter", "Character that separates a view from the rest of the key name");
            {
                int index = Data.ActiveViewDelimiter == ViewDelimiter.Dot ? 0 : 1;
                int newIndex = EditorGUILayout.Popup(index, DelimiterNames, GUILayout.MaxWidth(160));
                if (newIndex != index)
                {
                    Data.ActiveViewDelimiter = newIndex == 0 ? ViewDelimiter.Dot : ViewDelimiter.Underscore;
                    Data.SelectedView = "";
                    GUIUtility.keyboardControl = 0;
                }
            }
            EndRow();
            Description($"Keys are grouped into views by the text before the first '{Data.CurrentViewDelimiter}', e.g. 'menu{Data.CurrentViewDelimiter}play' is in the 'menu' view. " +
                        "Only affects how the editor groups keys; key names don't change.");
        }

        private void ShowDefaultLanguageMenu(Rect rect, LocalizationConfig config)
        {
            var menu = new GenericMenu();
            foreach (var lang in Data.GetLanguagesDefaultFirst())
            {
                string captured = lang;
                menu.AddItem(new GUIContent($"{LanguageDefinitions.GetDisplayName(lang)} ({lang})"), config.DefaultLanguage == lang, () =>
                {
                    ChangeConfig(config, "Change Default Language", () => config.SetDefaultLanguage(captured));
                    Editor.Repaint();
                });
            }
            menu.DropDown(rect);
        }

        #endregion

        #region Translation

        private void DrawTranslation()
        {
            Styles.DrawSectionTitle("Provider", ScopeUser);

            BeginRow("Service", "Used by Translate in the Keys tab and by Bulk Translate in Tools");
            {
                int index = (int)Data.ActiveTranslationProvider;
                int newIndex = GUILayout.Toolbar(index, ProviderNames, EditorStyles.miniButton, GUILayout.Width(160));
                if (newIndex != index)
                {
                    Data.ActiveTranslationProvider = (TranslationProvider)newIndex;
                    GUIUtility.keyboardControl = 0;
                }
            }
            EndRow();
            Description(Data.ActiveTranslationProvider == TranslationProvider.DeepL
                ? "DeepL translates one language at a time and keeps placeholders intact. Needs a DeepL API key (Free or Pro)."
                : "Gemini translates into all missing languages in one request, guided by your prompt. Needs a Google AI Studio API key.");

            SectionGap();

            bool deepL = Data.ActiveTranslationProvider == TranslationProvider.DeepL;
            Styles.DrawSectionTitle(deepL ? "DeepL" : "Gemini", "API key: " + ScopeMachine.ToLowerInvariant());

            DrawApiKeyRow(deepL);

            if (deepL)
                DrawDeepLSettings();
            else
                DrawGeminiSettings();
        }

        private void DrawApiKeyRow(bool deepL)
        {
            string key = deepL ? Data.DeeplApiKey : Data.GeminiApiKey;

            // A different key, URL or model makes the last test result stale
            string testTarget = deepL ? $"deepl|{key}|{Data.DeeplApiUrl}" : $"gemini|{key}|{GetGeminiModel()}";
            if (_testedFor != testTarget && _testState != TestState.Testing)
            {
                _testState = TestState.None;
                _testMessage = "";
            }

            BeginRow("API key");
            {
                string newKey = _showApiKey ? EditorGUILayout.TextField(key) : EditorGUILayout.PasswordField(key);
                if (newKey != key)
                {
                    newKey = newKey.Trim();
                    if (deepL)
                        Data.DeeplApiKey = newKey;
                    else
                        Data.GeminiApiKey = newKey;
                    key = newKey;
                }

                var eye = _showApiKey
                    ? Styles.Icon("animationvisibilitytoggleon", "Hide", "Hide the key")
                    : Styles.Icon("animationvisibilitytoggleoff", "Show", "Show the key");
                if (GUILayout.Button(eye, EditorStyles.miniButton, GUILayout.Width(eye.image != null ? 26 : 44)))
                    _showApiKey = !_showApiKey;

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(key) || _testState == TestState.Testing))
                {
                    if (GUILayout.Button(new GUIContent("Test", "Check the key with the service"), EditorStyles.miniButton, GUILayout.Width(44)))
                    {
                        _testedFor = testTarget;
                        if (deepL)
                            TestDeepL(key, Data.DeeplApiUrl);
                        else
                            TestGemini(key, GetGeminiModel());
                    }
                }

                var (text, color, tooltip) = string.IsNullOrEmpty(key) ? ("NOT SET", Styles.MutedText, "Paste your API key")
                    : _testState switch
                    {
                        TestState.Testing => ("TESTING…", Styles.MutedText, "Contacting the service"),
                        TestState.Ok => ("CONNECTED", Styles.Success, _testMessage),
                        TestState.Failed => ("FAILED", Styles.Danger, _testMessage),
                        _ => ("SET", Styles.Accent, "Key is set. Press Test to check it.")
                    };
                Styles.DrawBadge(BadgeRect(text), text, color, tooltip);
            }
            EndRow();

            if (_testState is TestState.Ok or TestState.Failed && !string.IsNullOrEmpty(_testMessage))
                Description(_testMessage, _testState == TestState.Failed ? Styles.WarningLabel : null);
            else
                Description("Stored in your editor preferences, never in the project or the build.");
        }

        private void DrawDeepLSettings()
        {
            BeginRow("API URL");
            {
                string url = EditorGUILayout.TextField(Data.DeeplApiUrl);
                if (url != Data.DeeplApiUrl)
                    Data.DeeplApiUrl = url.Trim();

                using (new EditorGUI.DisabledScope(Data.DeeplApiUrl == LanguageEditorData.DefaultDeeplApiUrl))
                {
                    if (GUILayout.Button("Reset", EditorStyles.miniButton, GUILayout.Width(48)))
                    {
                        Data.DeeplApiUrl = LanguageEditorData.DefaultDeeplApiUrl;
                        GUIUtility.keyboardControl = 0;
                    }
                }
            }
            EndRow();

            // Free keys end with ":fx" and only work on api-free.deepl.com
            string key = Data.DeeplApiKey ?? "";
            bool freeKey = key.EndsWith(":fx", StringComparison.Ordinal);
            bool freeUrl = Data.DeeplApiUrl.Contains("api-free.deepl.com");
            if (key.Length > 0 && freeKey != freeUrl)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(LabelWidth + 4f);
                GUILayout.Label(freeKey
                        ? "This is a Free key (ends with :fx) but the URL is for DeepL Pro."
                        : "This looks like a Pro key but the URL is for DeepL Free.",
                    Styles.WarningLabel);
                if (GUILayout.Button(freeKey ? "Use Free URL" : "Use Pro URL", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                {
                    Data.DeeplApiUrl = freeKey ? LanguageEditorData.DefaultDeeplApiUrl : DeeplProUrl;
                    GUIUtility.keyboardControl = 0;
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(6);
            }
            else
            {
                Description("Free keys use api-free.deepl.com, Pro keys use api.deepl.com.");
            }

            DrawPromptField("Context", "Sent with every request to steer tone and length",
                Data.DeeplContext, LanguageEditorData.DefaultDeepLContext, v => Data.DeeplContext = v);
        }

        private void DrawGeminiSettings()
        {
            BeginRow("Model");
            {
                int index = Array.IndexOf(GeminiModels, Data.GeminiModel);
                if (index < 0)
                    index = GeminiModels.Length - 1;

                int newIndex = EditorGUILayout.Popup(index, GeminiModels, GUILayout.MaxWidth(240));
                if (newIndex != index)
                {
                    Data.GeminiModel = GeminiModels[newIndex];
                    GUIUtility.keyboardControl = 0;
                }

                if (Data.GeminiModel == "custom")
                {
                    string custom = EditorGUILayout.TextField(Data.GeminiCustomModel);
                    if (custom != Data.GeminiCustomModel)
                        Data.GeminiCustomModel = custom.Trim();
                }
            }
            EndRow();
            Description(Data.GeminiModel == "custom"
                ? "Enter any model id from Google AI Studio, e.g. gemini-2.5-pro."
                : "Flash models are fast and cheap; Lite is the cheapest.");

            DrawPromptField("Prompt", "System instructions for the translation request",
                Data.GeminiContext, LanguageEditorData.DefaultGeminiContext, v => Data.GeminiContext = v);
        }

        private static void DrawPromptField(string label, string tooltip, string value, string defaultValue, Action<string> setValue)
        {
            BeginRow(label, tooltip);
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(value == defaultValue))
                {
                    if (GUILayout.Button("Reset to Default", EditorStyles.miniButton, GUILayout.Width(110)))
                    {
                        setValue(defaultValue);
                        GUIUtility.keyboardControl = 0;
                    }
                }
            }
            EndRow();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(LabelWidth + 4f);
            string newValue = EditorGUILayout.TextArea(value, Styles.TextArea, GUILayout.MinHeight(60));
            if (newValue != value)
                setValue(newValue);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);
        }

        private string GetGeminiModel()
        {
            return Data.GeminiModel == "custom" ? Data.GeminiCustomModel : Data.GeminiModel;
        }

        private async void TestDeepL(string key, string translateUrl)
        {
            _testState = TestState.Testing;
            Editor.Repaint();

            try
            {
                var uri = new Uri(translateUrl);
                var request = new HttpRequestMessage(HttpMethod.Get, $"{uri.Scheme}://{uri.Authority}/v2/usage");
                request.Headers.Add("Authorization", $"DeepL-Auth-Key {key}");

                var response = await Http.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var usage = JsonUtility.FromJson<DeepLUsage>(body);
                    string plan = key.EndsWith(":fx", StringComparison.Ordinal) ? "DeepL Free" : "DeepL Pro";
                    SetTestResult(TestState.Ok, usage != null && usage.character_limit > 0
                        ? $"{plan} · {usage.character_count:N0} of {usage.character_limit:N0} characters used this billing period"
                        : $"{plan} · key accepted");
                }
                else
                {
                    SetTestResult(TestState.Failed, (int)response.StatusCode switch
                    {
                        403 => "The API key was rejected. Check the key and whether it matches the Free/Pro URL.",
                        456 => "The key works, but the character quota for this period is used up.",
                        _ => $"DeepL answered {(int)response.StatusCode} {response.ReasonPhrase}."
                    });
                }
            }
            catch (UriFormatException)
            {
                SetTestResult(TestState.Failed, "The API URL isn't a valid address.");
            }
            catch (Exception ex)
            {
                SetTestResult(TestState.Failed, $"Couldn't reach DeepL: {ex.GetBaseException().Message}");
            }
        }

        private async void TestGemini(string key, string model)
        {
            _testState = TestState.Testing;
            Editor.Repaint();

            if (string.IsNullOrWhiteSpace(model))
            {
                SetTestResult(TestState.Failed, "Enter a custom model id first.");
                return;
            }

            try
            {
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}?key={Uri.EscapeDataString(key)}";
                var response = await Http.GetAsync(url);
                string body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var info = JsonUtility.FromJson<GeminiModelInfo>(body);
                    string name = !string.IsNullOrEmpty(info?.displayName) ? info.displayName : model;
                    SetTestResult(TestState.Ok, $"Key accepted · {name} is available");
                }
                else
                {
                    SetTestResult(TestState.Failed, response.StatusCode switch
                    {
                        HttpStatusCode.NotFound => $"The key works, but the model '{model}' wasn't found.",
                        HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The API key was rejected.",
                        _ => $"Gemini answered {(int)response.StatusCode} {response.ReasonPhrase}."
                    });
                }
            }
            catch (Exception ex)
            {
                SetTestResult(TestState.Failed, $"Couldn't reach Gemini: {ex.GetBaseException().Message}");
            }
        }

        private void SetTestResult(TestState state, string message)
        {
            _testState = state;
            _testMessage = message;
            if (Editor != null)
                Editor.Repaint();
        }

#pragma warning disable CS0649 // Filled by JsonUtility
        [Serializable]
        private class DeepLUsage
        {
            public long character_count;
            public long character_limit;
        }

        [Serializable]
        private class GeminiModelInfo
        {
            public string displayName;
        }
#pragma warning restore CS0649

        #endregion

        #region Files & Build

        private void DrawFilesAndBuild(LocalizationConfig config)
        {
            RefreshFileInfo(config);
            string path = LocalizationManager.LanguagesPath;

            Styles.DrawSectionTitle("Locale Files", ScopeProject);

            BeginRow("Folder");
            {
                GUILayout.Label(new GUIContent(path, path), Styles.MutedLabel, GUILayout.MinWidth(60));
                if (GUILayout.Button("Open", EditorStyles.miniButtonLeft, GUILayout.Width(48)))
                    OpenLanguagesFolder();
                if (GUILayout.Button("Copy Path", EditorStyles.miniButtonRight, GUILayout.Width(72)))
                    CopyToClipboard(path, "Copied folder path");
            }
            EndRow();
            Description(_fileCount == 0
                ? "No locale files yet. They're written when you save."
                : $"{_fileCount} locale {(_fileCount == 1 ? "file" : "files")} · {EditorUtility.FormatBytes(_fileBytes)}");

            BeginRow("Compression");
            {
                var mode = (CompressionMode)EditorGUILayout.EnumPopup(config.CompressionMode, GUILayout.MaxWidth(160));
                if (mode != config.CompressionMode)
                    ChangeConfig(config, "Change Compression", () => config.SetCompressionMode(mode));

                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Re-save Files", "Write every locale file again with the current settings (also saves unsaved edits)"),
                        EditorStyles.miniButton, GUILayout.Width(90)))
                {
                    Editor.SaveLanguages();
                    _fileInfoTime = -1d;
                }
            }
            EndRow();
            Description(config.CompressionMode switch
            {
                CompressionMode.Disabled => "No compression: fastest to save and load, largest files.",
                CompressionMode.Fastest => "Light compression: quick saves, good while developing.",
                CompressionMode.Optimal => "Smallest files, slower saves. Recommended for builds.",
                _ => ""
            } + " Applies the next time files are saved.");

            BeginRow("Typed keys", "Generate StringKeys and ArrayKeys for type-safe lookups");
            {
                bool typed = EditorGUILayout.Toggle(config.GenerateTypedKeys, GUILayout.Width(18));
                if (typed != config.GenerateTypedKeys)
                    ChangeConfig(config, "Change Typed Keys", () => config.SetGenerateTypedKeys(typed));

                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!config.GenerateTypedKeys))
                {
                    if (GUILayout.Button("Regenerate", EditorStyles.miniButton, GUILayout.Width(90)))
                    {
                        TypedKeyGenerator.Generate(Data, showSuccessDialog: true);
                        GUIUtility.ExitGUI();
                    }
                }
            }
            EndRow();
            Description(config.GenerateTypedKeys
                ? $"Regenerated on every save into {TypedKeyGenerator.GeneratedCodePath}. Hashes are computed ahead of time for fast lookups."
                : "Off. Existing generated files are kept, so code that uses them still compiles.");

            BeginRow("Protection", "Experimental runtime checks on the locale files");
            {
                var mode = (ProtectionMode)EditorGUILayout.EnumPopup(config.ProtectionMode, GUILayout.MaxWidth(160));
                if (mode != config.ProtectionMode)
                {
                    ChangeConfig(config, "Change Protection", () => config.SetProtectionMode(mode));
                    LocaleHashSync.SyncIfEnabled("enabling anti-tamper");
                    _fileInfoTime = -1d;
                }

                GUILayout.Space(6);
                Styles.DrawBadge(BadgeRect("EXPERIMENTAL"), "EXPERIMENTAL", Styles.MutedText, "This feature may change");

                if (config.IsAntiTamperEnabled && _hashesOutOfSync >= 0)
                {
                    GUILayout.Space(4);
                    string text = _hashesOutOfSync == 0 ? "IN SYNC" : $"{_hashesOutOfSync} OUT OF SYNC";
                    Styles.DrawBadge(BadgeRect(text), text, _hashesOutOfSync == 0 ? Styles.Success : Styles.Warning,
                        _hashesOutOfSync == 0
                            ? "Every locale file matches its stored hash"
                            : "These files would fail verification at runtime. Sync the hashes after editing files outside the editor.");
                }

                GUILayout.FlexibleSpace();
                if (config.IsAntiTamperEnabled && GUILayout.Button("Sync Hashes", EditorStyles.miniButton, GUILayout.Width(90)))
                {
                    SyncFileHashes(config);
                    GUIUtility.ExitGUI();
                }
            }
            EndRow();
            Description(config.ProtectionMode switch
            {
                ProtectionMode.Disabled => "Off. Any locale file in the folder can be loaded.",
                ProtectionMode.SelectionOnly => "Only the project's languages can be loaded at runtime.",
                ProtectionMode.AntiTamper => "Files are checked against stored hashes at runtime. Hashes update automatically when you save.",
                ProtectionMode.Both => "Only project languages load, and their files are checked against stored hashes.",
                _ => ""
            });

            SectionGap();
            Styles.DrawSectionTitle("JSON", "Every key in every language");

            BeginRow("Backup & exchange");
            {
                if (GUILayout.Button("Export All…", EditorStyles.miniButtonLeft, GUILayout.Width(90)))
                {
                    _jsonService.ExportToJson();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("Import…", EditorStyles.miniButtonRight, GUILayout.Width(90)))
                {
                    Data.History.RecordAll("Import JSON");
                    _jsonService.ImportFromJson();
                    Editor.Repaint();
                    GUIUtility.ExitGUI();
                }
                GUILayout.FlexibleSpace();
            }
            EndRow();
            Description($"Import adds or overwrites keys from a JSON export. {UndoShortcut} undoes it.");

            SectionGap();
            DrawDangerZone();
        }

        private void DrawDangerZone()
        {
            Styles.DrawSectionTitle("Danger Zone");

            var rect = GUILayoutUtility.GetRect(0f, 26f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                var tint = Styles.Danger;
                tint.a = 0.08f;
                EditorGUI.DrawRect(rect, tint);
            }

            int languages = Data.LanguageCodes.Count;
            int keys = Data.Keys.Count;
            var buttonRect = new Rect(rect.xMax - 86f, rect.y + 4f, 80f, rect.height - 8f);

            GUI.Label(new Rect(rect.x + 6f, rect.y, 200f, rect.height), "Delete all language data", EditorStyles.boldLabel);
            GUI.Label(new Rect(rect.x + 206f, rect.y, Mathf.Max(0f, buttonRect.x - rect.x - 212f), rect.height),
                $"{languages} {(languages == 1 ? "language" : "languages")} · {keys:N0} {(keys == 1 ? "key" : "keys")}", Styles.MutedLabel);

            if (GUI.Button(buttonRect, "Delete…", EditorStyles.miniButton))
            {
                DeleteAllData(languages, keys);
                GUIUtility.ExitGUI();
            }

            Description("Removes every key, every translation and every .bloc file, leaving only the default language. This can't be undone.");
        }

        private void DeleteAllData(int languages, int keys)
        {
            int choice = EditorUtility.DisplayDialogComplex("Delete All Language Data",
                $"This deletes {keys:N0} keys in {languages} languages and every locale file in:\n{LocalizationManager.LanguagesPath}\n\n" +
                "It can't be undone. Export a JSON backup first?",
                "Export Backup & Delete", "Cancel", "Delete Without Backup");

            if (choice == 1)
                return;

            if (choice == 0 && !_jsonService.ExportToJson())
            {
                EditorUtility.DisplayDialog("Delete All Language Data", "The backup wasn't saved, so nothing was deleted.", "OK");
                return;
            }

            Editor.PurgeAllData(confirm: false);
            _fileInfoTime = -1d;
        }

        private void RefreshFileInfo(LocalizationConfig config)
        {
            if (Event.current.type != EventType.Layout)
                return;

            double now = EditorApplication.timeSinceStartup;
            if (_fileInfoTime >= 0d && now - _fileInfoTime < FileInfoRefreshSeconds)
                return;
            _fileInfoTime = now;

            _fileCount = 0;
            _fileBytes = 0;
            _hashesOutOfSync = -1;

            string path = LocalizationManager.LanguagesPath;
            if (!Directory.Exists(path))
                return;

            try
            {
                foreach (var file in Directory.GetFiles(path, "*" + LocalizationManager.FileExtension, SearchOption.TopDirectoryOnly))
                {
                    _fileCount++;
                    _fileBytes += new FileInfo(file).Length;
                }

                if (config.IsAntiTamperEnabled)
                    _hashesOutOfSync = LocaleHashSync.CountOutOfSync(config);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Localization] Couldn't read the locale folder: {ex.Message}");
            }
        }

        private void SyncFileHashes(LocalizationConfig config)
        {
            try
            {
                if (!Directory.Exists(LocalizationManager.LanguagesPath))
                {
                    EditorUtility.DisplayDialog("Sync Hashes", "The locale folder doesn't exist yet. Save first.", "OK");
                    return;
                }

                LocaleHashSync.Sync(config, out int synced, out int removed);
                _fileInfoTime = -1d;
                Editor.ShowNotification(new GUIContent($"Synced {synced} {(synced == 1 ? "hash" : "hashes")}" + (removed > 0 ? $", removed {removed}" : "")));
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Sync Hashes", $"Failed to sync hashes: {ex.Message}", "OK");
                Debug.LogError($"[LocalizationEditor] Hash sync failed: {ex}");
            }
        }

        private static void OpenLanguagesFolder()
        {
            string path = LocalizationManager.LanguagesPath;
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            string fullPath = Path.GetFullPath(path).Replace('\\', '/');
            Application.OpenURL((fullPath.StartsWith("/") ? "file://" : "file:///") + fullPath);
        }

        #endregion

        #region MCP

        private void DrawMcp()
        {
            bool running = LocalizationMcpServer.IsRunning;
            string endpoint = running ? LocalizationMcpServer.Url : LocalizationMcpServer.ConfiguredUrl;

            Styles.DrawSectionTitle("MCP Server", "Lets AI agents read and edit your keys");

            EditorGUILayout.BeginHorizontal();
            {
                string status = running ? "RUNNING" : "STOPPED";
                Styles.DrawBadge(BadgeRect(status), status, running ? Styles.Success : Styles.MutedText,
                    running ? "Agents can connect" : "Not accepting connections");
                GUILayout.Space(6);

                EditorGUILayout.SelectableLabel(endpoint, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

                if (GUILayout.Button("Copy", EditorStyles.miniButtonLeft, GUILayout.Width(48)))
                    CopyToClipboard(endpoint, "Copied endpoint");

                if (running)
                {
                    if (GUILayout.Button("Restart", EditorStyles.miniButtonMid, GUILayout.Width(56)))
                        LocalizationMcpServer.RestartFromMenu();
                    if (GUILayout.Button("Stop", EditorStyles.miniButtonRight, GUILayout.Width(48)))
                        LocalizationMcpServer.Stop();
                }
                else if (GUILayout.Button("Start", EditorStyles.miniButtonRight, GUILayout.Width(56)))
                {
                    LocalizationMcpServer.StartFromMenu();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button(new GUIContent("Copy Agent Config", "Ready-to-paste MCP config for Claude, Cursor and other agents"),
                        EditorStyles.miniButtonLeft, GUILayout.Width(130)))
                    LocalizationMcpServer.CopyAgentConfig();

                using (new EditorGUI.DisabledScope(!running))
                {
                    if (GUILayout.Button(new GUIContent("Open Status Page", running ? "Open the server's status page in a browser" : "Start the server first"),
                            EditorStyles.miniButtonRight, GUILayout.Width(130)))
                        LocalizationMcpServer.OpenStatusPage();
                }
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
            GUILayout.Label(running
                    ? "Agents connect to this address. Changes they make show up here after a reload."
                    : "Start the server so AI agents can list, add and translate keys in this project.",
                Styles.Description);

            SectionGap();
            Styles.DrawSectionTitle("Server Settings", ScopeMachine);

            BeginRow("Start automatically");
            {
                bool autoStart = EditorGUILayout.Toggle(LocalizationMcpServer.AutoStart);
                if (autoStart != LocalizationMcpServer.AutoStart)
                    LocalizationMcpServer.AutoStart = autoStart;
            }
            EndRow();
            Description("Starts the server when the editor opens.");

            BeginRow("Port");
            {
                int port = LocalizationMcpServer.PortPrefValue;
                int newPort = Mathf.Clamp(EditorGUILayout.DelayedIntField(port, GUILayout.Width(80)), 1, 65535);
                if (newPort != port)
                    LocalizationMcpServer.PortPrefValue = newPort;
            }
            EndRow();

            if (running && LocalizationMcpServer.PortPrefValue != LocalizationMcpServer.Port)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(LabelWidth + 4f);
                GUILayout.Label($"Still running on {LocalizationMcpServer.Port}. Restart to switch to {LocalizationMcpServer.PortPrefValue}; agents must reconnect.",
                    Styles.WarningLabel);
                if (GUILayout.Button("Restart Now", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    LocalizationMcpServer.RestartFromMenu();
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                Description("Local port the server listens on (127.0.0.1 only).");
            }
        }

        #endregion

        private static string UndoShortcut => Application.platform == RuntimePlatform.OSXEditor ? "Cmd+Z" : "Ctrl+Z";

        private void CopyToClipboard(string text, string message)
        {
            EditorGUIUtility.systemCopyBuffer = text;
            Editor.ShowNotification(new GUIContent(message));
        }
    }
}
