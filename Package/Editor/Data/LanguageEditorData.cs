using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PicoShot.Localization.Config;

namespace PicoShot.Localization.Editor.Data
{
    public enum TranslationProvider
    {
        DeepL,
        Gemini
    }

    /// <summary>
    /// Centralized data model for the Localization Editor.
    /// Holds all state, keys, languages and editor preferences.
    /// </summary>
    [Serializable]
    public sealed class LanguageEditorData
    {
        // State
        public bool HasUnsavedChanges { get; set; }
        public string SelectedKey { get; set; }
        public string LastSelectedKey { get; set; }
        public string SelectedTable { get; set; } = "";

        // Search & Filters
        public string KeySearchFilter { get; set; } = "";
        public string LanguageFilter { get; set; } = "";
        public string ComponentSearchFilter { get; set; } = "";
        public bool ShowArrayKeysOnly { get; set; }
        public bool ShowStringKeysOnly { get; set; }
        public bool SortKeysByName { get; set; }

        // Foldouts
        public bool ShowStatusSection { get; set; } = true;
        public bool ShowTestingTools { get; set; } = true;
        public bool ShowParameterList { get; set; }
        public bool ShowExistingComponents { get; set; } = true;

        // UI State
        public float KeysListPanelWidth { get; set; } = 200f;
        public Vector2 KeysListScroll { get; set; }
        public Vector2 KeyDetailsScroll { get; set; }
        public Vector2 LanguageScrollPos { get; set; }
        public Vector2 ComponentsScrollPos { get; set; }
        public Vector2 ExistingComponentsScrollPos { get; set; }
        public Vector2 MainScrollPosition { get; set; }
        public Vector2 ComponentsScrollPosition { get; set; }
        public Vector2 ToolsScrollPosition { get; set; }
        public Vector2 CharsetLanguageScrollPos { get; set; }

        // Translation Hint
        public string CurrentKeyHint { get; set; } = "";

        // Test Data
        public string TestKey { get; set; } = "";
        public string TestRtl { get; set; } = "";
        public string TestKeyWithParams { get; set; } = "";
        public string TestResult { get; set; } = "";
        public List<string> ParameterList { get; set; } = new();

        // Component Management
        public GameObject SelectedGameObject { get; set; }
        public LocalizationTextComponent PendingKeySelection { get; set; }

        // Core Data
        public List<string> LanguageCodes { get; } = new() { "en" };
        public List<string> Keys { get; set; } = new();
        public Dictionary<string, Dictionary<string, object>> LanguageData { get; private set; } = new();
        public Dictionary<string, bool> KeyFoldouts { get; } = new();
        public Dictionary<string, bool> LanguageSelectionForCharset { get; } = new();
        public Dictionary<string, string> GeneratedCharsets { get; } = new();

        // Translation Provider Settings
        public const string TranslationProviderPref = "PicoShot_Localization_TranslationProvider";
        public TranslationProvider ActiveTranslationProvider
        {
            get => (TranslationProvider)PlayerPrefs.GetInt(TranslationProviderPref, (int)TranslationProvider.DeepL);
            set => PlayerPrefs.SetInt(TranslationProviderPref, (int)value);
        }

        // DeepL Settings (stored in preferences, not this data)
        public const string DefaultDeeplApiUrl = "https://api-free.deepl.com/v2/translate";
        public const string DeeplApiUrlPref = "PicoShot_Localization_DeepLApiUrl";
        public const string DeeplApiKeyPref = "PicoShot_Localization_DeepLApiKey";
        public const string DeeplContextPref = "PicoShot_Localization_DeepLContext";
        public const int DeeplRequestDelayMs = 350;
        public const string DefaultDeepLContext = "This is a game localization text. The translation should be concise and suitable for game UI.";

        public string DeeplApiUrl
        {
            get => PlayerPrefs.GetString(DeeplApiUrlPref, DefaultDeeplApiUrl);
            set => PlayerPrefs.SetString(DeeplApiUrlPref, value);
        }

        public string DeeplApiKey
        {
            get => UnityEditor.EditorPrefs.GetString(DeeplApiKeyPref, "");
            set => UnityEditor.EditorPrefs.SetString(DeeplApiKeyPref, value);
        }

        public string DeeplContext
        {
            get => PlayerPrefs.GetString(DeeplContextPref, DefaultDeepLContext);
            set => PlayerPrefs.SetString(DeeplContextPref, value);
        }

        // Gemini Settings
        public const string GeminiApiKeyPref = "PicoShot_Localization_GeminiApiKey";
        public const string GeminiModelPref = "PicoShot_Localization_GeminiModel";
        public const string GeminiCustomModelPref = "PicoShot_Localization_GeminiCustomModel";
        public const string GeminiContextPref = "PicoShot_Localization_GeminiContext";

        public const string DefaultGeminiModel = "gemini-2.5-flash";
        public const string DefaultGeminiContext = "You are a specialized game localization translator. Your task is to accurately translate text for game UI, dialogues, and system messages while preserving tone and brevity. Translate the provided source text from the source language into all specified target languages. Return only a valid JSON object where keys are the target language codes and values are the translated text.";

        public string GeminiApiKey
        {
            get => UnityEditor.EditorPrefs.GetString(GeminiApiKeyPref, "");
            set => UnityEditor.EditorPrefs.SetString(GeminiApiKeyPref, value);
        }

        public string GeminiModel
        {
            get => PlayerPrefs.GetString(GeminiModelPref, DefaultGeminiModel);
            set => PlayerPrefs.SetString(GeminiModelPref, value);
        }

        public string GeminiCustomModel
        {
            get => PlayerPrefs.GetString(GeminiCustomModelPref, "");
            set => PlayerPrefs.SetString(GeminiCustomModelPref, value);
        }

        public string GeminiContext
        {
            get => PlayerPrefs.GetString(GeminiContextPref, DefaultGeminiContext);
            set => PlayerPrefs.SetString(GeminiContextPref, value);
        }

        // Constants
        public const float KeyItemHeight = 22f;
        public const float MinKeysListWidth = 150f;
        public const float MaxKeysListWidthRatio = 0.5f;

        /// <summary>
        /// Gets all keys filtered by current search and type filters.
        /// </summary>
        public IEnumerable<string> GetFilteredKeys()
        {
            string lowercaseFilter = KeySearchFilter?.ToLower() ?? "";
            bool hasFilter = !string.IsNullOrEmpty(lowercaseFilter);

            var query = Keys.AsEnumerable();

            // Filter by table first
            if (!string.IsNullOrEmpty(SelectedTable))
            {
                string tablePrefix = SelectedTable + ".";
                query = query.Where(key => key.StartsWith(tablePrefix));
            }

            if (hasFilter)
                query = query.Where(key => key.ToLower().Contains(lowercaseFilter));

            if (ShowArrayKeysOnly)
                query = query.Where(key => IsArrayKey(LanguageData[key]));
            else if (ShowStringKeysOnly)
                query = query.Where(key => !IsArrayKey(LanguageData[key]));

            if (SortKeysByName)
                query = query.OrderBy(key => key);

            return query;
        }

        /// <summary>
        /// Gets all unique tables from existing keys.
        /// </summary>
        public IEnumerable<string> GetTables()
        {
            return Keys.Where(k => k.Contains('.')).Select(k => k.Substring(0, k.IndexOf('.'))).Distinct().OrderBy(t => t);
        }

        /// <summary>
        /// Helper to extract the local name of a key by stripping its table prefix if present.
        /// </summary>
        public string GetLocalKeyName(string fullKey)
        {
            if (string.IsNullOrEmpty(fullKey)) return fullKey;
            int dotIndex = fullKey.IndexOf('.');
            return dotIndex >= 0 ? fullKey.Substring(dotIndex + 1) : fullKey;
        }

        /// <summary>
        /// Checks if a key's data represents an array type.
        /// </summary>
        public static bool IsArrayKey(Dictionary<string, object> keyData)
        {
            if (keyData == null || keyData.Count == 0)
                return false;
            var firstValue = keyData.Values.FirstOrDefault();
            return firstValue is List<string> || firstValue is string[];
        }

        /// <summary>
        /// Gets the first available value for a key, checking default language first.
        /// </summary>
        public object GetFirstValue(string key)
        {
            if (!LanguageData.TryGetValue(key, out var keyData) || keyData.Count == 0)
                return null;

            string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;
            if (keyData.TryGetValue(defaultLang, out var value))
                return value;

            return keyData.Values.FirstOrDefault();
        }

        /// <summary>
        /// Gets the first value from a key data dictionary.
        /// </summary>
        public static object GetFirstValue(Dictionary<string, object> keyData)
        {
            if (keyData == null || keyData.Count == 0)
                return null;

            string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;
            if (keyData.TryGetValue(defaultLang, out var value))
                return value;

            return keyData.Values.FirstOrDefault();
        }

        /// <summary>
        /// Converts an object value to List<string> if it's an array type.
        /// </summary>
        public static List<string> ConvertToList(object value)
        {
            if (value is List<string> list)
                return list;
            if (value is string[] arr)
                return arr.ToList();
            return null;
        }

        /// <summary>
        /// Clears hint and keyboard focus when switching keys.
        /// </summary>
        public void ClearKeyHint()
        {
            CurrentKeyHint = "";
            GUIUtility.keyboardControl = 0;
        }

        /// <summary>
        /// Resets all data to initial state.
        /// </summary>
        public void Reset()
        {
            HasUnsavedChanges = false;
            SelectedKey = null;
            LastSelectedKey = null;
            CurrentKeyHint = "";
            Keys.Clear();
            LanguageData.Clear();
            KeyFoldouts.Clear();
            LanguageCodes.Clear();
            LanguageCodes.Add(LocalizationConfigProvider.Config.DefaultLanguage);
            GeneratedCharsets.Clear();
        }

        /// <summary>
        /// Adds a new language code.
        /// </summary>
        public bool AddLanguage(string language)
        {
            if (LanguageCodes.Contains(language))
                return false;

            LanguageCodes.Add(language);

            foreach (var key in Keys)
            {
                AddLanguageToKey(key, language);
            }

            HasUnsavedChanges = true;
            return true;
        }

        /// <summary>
        /// Adds a language to an existing key with appropriate default value.
        /// </summary>
        private void AddLanguageToKey(string key, string language)
        {
            var firstValue = GetFirstValue(key);

            if (firstValue is List<string> arr)
            {
                var newArray = new List<string>(new string[arr.Count]);
                LanguageData[key][language] = newArray;
            }
            else
            {
                LanguageData[key][language] = "";
            }
        }

        /// <summary>
        /// Removes a language and all its translations.
        /// </summary>
        public bool RemoveLanguage(string language)
        {
            if (!LanguageCodes.Contains(language))
                return false;

            LanguageCodes.Remove(language);

            foreach (var key in Keys.Where(key => LanguageData[key].ContainsKey(language)))
            {
                LanguageData[key].Remove(language);
            }

            HasUnsavedChanges = true;
            return true;
        }

        /// <summary>
        /// Adds a new key to the data.
        /// </summary>
        public bool AddKey(string key, bool isArray)
        {
            key = key?.Trim();
            if (string.IsNullOrEmpty(key) || Keys.Contains(key))
                return false;

            Keys.Add(key);
            LanguageData[key] = new Dictionary<string, object>();

            foreach (var lang in LanguageCodes)
            {
                LanguageData[key][lang] = isArray ? new List<string>() : "";
            }

            SelectedKey = key;
            HasUnsavedChanges = true;
            return true;
        }

        /// <summary>
        /// Removes a key and all its translations.
        /// </summary>
        public bool RemoveKey(string key)
        {
            if (!Keys.Contains(key))
                return false;

            Keys.Remove(key);
            LanguageData.Remove(key);
            KeyFoldouts.Remove(key);
            HasUnsavedChanges = true;
            return true;
        }

        /// <summary>
        /// Removes a table and all its keys.
        /// </summary>
        public void RemoveTable(string table)
        {
            if (string.IsNullOrEmpty(table)) return;
            
            string prefix = table + ".";
            var keysToRemove = Keys.Where(k => k.StartsWith(prefix)).ToList();
            
            foreach (var k in keysToRemove)
            {
                RemoveKey(k);
            }
            
            if (SelectedTable == table)
            {
                SelectedTable = "";
                SelectedKey = null;
            }
        }

        /// <summary>
        /// Renames a key while preserving its data.
        /// </summary>
        public bool RenameKey(string oldKey, string newKey)
        {
            newKey = newKey?.Trim();
            if (!Keys.Contains(oldKey) || Keys.Contains(newKey))
                return false;

            int index = Keys.IndexOf(oldKey);
            Keys[index] = newKey;
            LanguageData[newKey] = LanguageData[oldKey];
            LanguageData.Remove(oldKey);
            SelectedKey = newKey;
            HasUnsavedChanges = true;
            return true;
        }

        /// <summary>
        /// Clears all translations for a key.
        /// </summary>
        public void ClearKeyTranslations(string key)
        {
            if (!LanguageData.TryGetValue(key, out var keyData))
                return;

            foreach (var lang in keyData.Keys.ToList())
            {
                keyData[lang] = keyData[lang] switch
                {
                    string => "",
                    List<string> list => Enumerable.Repeat("", list.Count).ToList(),
                    _ => keyData[lang]
                };
            }

            HasUnsavedChanges = true;
        }

        /// <summary>
        /// Adds an element to an array key for all languages.
        /// </summary>
        public void AddArrayElement(string key)
        {
            foreach (var lang in LanguageCodes)
            {
                if (LanguageData[key][lang] is List<string> langArray)
                {
                    langArray.Add("");
                }
            }
            HasUnsavedChanges = true;
        }

        /// <summary>
        /// Removes an element from an array key for all languages.
        /// </summary>
        public void RemoveArrayElement(string key, int index)
        {
            foreach (var lang in LanguageCodes)
            {
                if (LanguageData[key][lang] is List<string> langArray && index < langArray.Count)
                {
                    langArray.RemoveAt(index);
                }
            }
            HasUnsavedChanges = true;
        }

        /// <summary>
        /// Clears empty array elements from an array key.
        /// </summary>
        public void ClearEmptyArrayElements(string key)
        {
            var firstValue = GetFirstValue(key);
            if (firstValue is not List<string> firstArray)
                return;

            for (int i = firstArray.Count - 1; i >= 0; i--)
            {
                bool isEmpty = LanguageCodes.All(lang =>
                    LanguageData[key][lang] is not List<string> langArray ||
                    string.IsNullOrWhiteSpace(langArray[i]));

                if (isEmpty)
                    RemoveArrayElement(key, i);
            }
        }

        /// <summary>
        /// Updates charset language selection to match current languages.
        /// </summary>
        public void SyncCharsetLanguageSelection()
        {
            // Add new languages
            foreach (var lang in LanguageCodes.Where(lang => !LanguageSelectionForCharset.ContainsKey(lang)))
            {
                LanguageSelectionForCharset[lang] = false;
            }

            // Remove old languages
            var currentLanguages = new HashSet<string>(LanguageCodes);
            foreach (var lang in LanguageSelectionForCharset.Keys.ToList().Where(lang => !currentLanguages.Contains(lang)))
            {
                LanguageSelectionForCharset.Remove(lang);
            }
        }

        /// <summary>
        /// Generates character sets for selected languages.
        /// </summary>
        public void GenerateCharsets()
        {
            GeneratedCharsets.Clear();

            foreach (var lang in LanguageCodes.Where(l => LanguageSelectionForCharset[l]))
            {
                var charSet = new HashSet<char>();

                foreach (var key in Keys)
                {
                    if (LanguageData[key].TryGetValue(lang, out var value))
                    {
                        AddValueToCharset(value, charSet);
                    }
                }

                GeneratedCharsets[lang] = new string(charSet.ToArray());
            }
        }

        private static void AddValueToCharset(object value, HashSet<char> charSet)
        {
            switch (value)
            {
                case string str:
                    foreach (var c in str)
                        charSet.Add(c);
                    break;
                case List<string> list:
                    foreach (var c in list.Where(item => item != null).SelectMany(item => item))
                        charSet.Add(c);
                    break;
            }
        }
    }
}
