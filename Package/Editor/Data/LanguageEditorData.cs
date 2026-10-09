using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PicoShot.Localization.Config;
using PicoShot.Localization.Editor.Mcp;

namespace PicoShot.Localization.Editor.Data
{
    public enum TranslationProvider
    {
        DeepL,
        Gemini
    }

    public enum KeyStatusFilter
    {
        All,
        Missing,
        Problems
    }

    /// <summary>
    /// Translation state of one key: how many languages are empty and how many have
    /// placeholder or rich-text tag mismatches against the default language.
    /// </summary>
    public readonly struct KeyStatus
    {
        public readonly int Missing;
        public readonly int Problems;

        public KeyStatus(int missing, int problems)
        {
            Missing = missing;
            Problems = problems;
        }
    }

    public enum ViewDelimiter
    {
        Dot,
        Underscore
    }

    /// <summary>
    /// Centralized data model for the Localization Editor.
    /// Holds all state, keys, languages and editor preferences.
    /// </summary>
    [Serializable]
    public sealed class LanguageEditorData
    {
        // State
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set
            {
                _hasUnsavedChanges = value;
                MarkKeysChanged();
            }
        }

        private bool _hasUnsavedChanges;
        public string SelectedKey { get; set; }
        public string SelectedView { get; set; } = "";

        // Search & Filters
        public string KeySearchFilter { get; set; } = "";
        public string LanguageFilter { get; set; } = "";
        public bool ShowArrayKeysOnly { get; set; }
        public bool ShowStringKeysOnly { get; set; }
        public bool SortKeysByName { get; set; }

        /// <summary>
        /// When set, the keys list only shows keys that are not yet translated in this language.
        /// </summary>
        public string UntranslatedLanguageFilter { get; set; }

        public KeyStatusFilter StatusFilter { get; set; }

        /// <summary>
        /// When true, the key search also matches translation text.
        /// </summary>
        public bool SearchInTranslations { get; set; } = true;

        // Foldouts
        public bool ShowStatusSection { get; set; } = true;
        public bool ShowTestingTools { get; set; } = true;
        public bool ShowParameterList { get; set; }

        // UI State
        public float KeysListPanelWidth { get; set; } = 200f;
        public Vector2 KeysListScroll { get; set; }
        public Vector2 KeyDetailsScroll { get; set; }
        public Vector2 LanguageScrollPos { get; set; }
        public Vector2 MainScrollPosition { get; set; }
        public Vector2 ToolsScrollPosition { get; set; }
        public Vector2 CharsetLanguageScrollPos { get; set; }

        // Test Data
        public string TestKey { get; set; } = "";
        public string TestRtl { get; set; } = "";
        public string TestKeyWithParams { get; set; } = "";
        public string TestResult { get; set; } = "";
        public List<string> ParameterList { get; set; } = new();

        // Component Management
        public GameObject SelectedGameObject { get; set; }

        // Core Data
        public List<string> LanguageCodes { get; } = new() { "en" };
        public List<string> Keys { get; set; } = new();
        public Dictionary<string, Dictionary<string, object>> LanguageData { get; private set; } = new();
        public Dictionary<string, bool> KeyFoldouts { get; } = new();
        public Dictionary<string, bool> LanguageSelectionForCharset { get; } = new();
        public string GeneratedCharset { get; private set; } = "";
        public bool HasGeneratedCharset { get; private set; }

        /// <summary>
        /// Languages removed in the editor whose locale files are deleted on the next save.
        /// </summary>
        public HashSet<string> PendingRemovedLanguages { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Incremented whenever keys or translations change; use it to invalidate cached derived data.
        /// </summary>
        public int DataVersion => _dataVersion;

        private KeyEditHistory _history;

        /// <summary>
        /// Undo history for key and translation edits.
        /// </summary>
        public KeyEditHistory History => _history ??= new KeyEditHistory(this);

        public void DisposeHistory()
        {
            _history?.Dispose();
            _history = null;
        }

        // Translation Provider Settings
        public const string TranslationProviderPref = "PicoShot_Localization_TranslationProvider";
        public TranslationProvider ActiveTranslationProvider
        {
            get => (TranslationProvider)PlayerPrefs.GetInt(TranslationProviderPref, (int)TranslationProvider.DeepL);
            set => PlayerPrefs.SetInt(TranslationProviderPref, (int)value);
        }

        // Key View Settings
        public const string ViewDelimiterPref = "PicoShot_Localization_ViewDelimiter";
        public ViewDelimiter ActiveViewDelimiter
        {
            get => (ViewDelimiter)PlayerPrefs.GetInt(ViewDelimiterPref, (int)ViewDelimiter.Dot);
            set
            {
                PlayerPrefs.SetInt(ViewDelimiterPref, (int)value);
                _cachedViewDelimiter = DelimiterChar(value);
            }
        }

        public char CurrentViewDelimiter => GetCurrentViewDelimiter();

        private static char _cachedViewDelimiter;

        public static char GetCurrentViewDelimiter()
        {
            if (_cachedViewDelimiter == '\0')
                _cachedViewDelimiter = DelimiterChar((ViewDelimiter)PlayerPrefs.GetInt(ViewDelimiterPref, (int)ViewDelimiter.Dot));
            return _cachedViewDelimiter;
        }

        private static char DelimiterChar(ViewDelimiter delimiter)
        {
            return delimiter == ViewDelimiter.Underscore ? '_' : '.';
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

        private int _dataVersion;
        private int _filteredVersion = -1;
        private List<string> _filteredKeysSource;
        private string _filteredSearch;
        private string _filteredView;
        private char _filteredDelimiter;
        private bool _filteredArraysOnly;
        private bool _filteredStringsOnly;
        private bool _filteredSorted;
        private string _filteredUntranslated;
        private KeyStatusFilter _filteredStatus;
        private bool _filteredSearchInTranslations;
        private string _filteredDefaultLanguage;

        private int _statusVersion = -1;
        private string _statusDefaultLanguage;
        private readonly Dictionary<string, KeyStatus> _statusCache = new();
        private readonly List<string> _filteredKeys = new();

        private int _viewsVersion = -1;
        private List<string> _viewsSource;
        private char _viewsDelimiter;
        private readonly List<string> _views = new();

        public void MarkKeysChanged()
        {
            _dataVersion++;
        }

        /// <summary>
        /// Gets all keys filtered by current search and type filters.
        /// The returned list is cached; copy it before modifying <see cref="Keys"/> while iterating.
        /// </summary>
        public IReadOnlyList<string> GetFilteredKeys()
        {
            string search = KeySearchFilter ?? string.Empty;
            string view = SelectedView ?? string.Empty;
            char delimiter = CurrentViewDelimiter;

            if (_filteredVersion == _dataVersion &&
                ReferenceEquals(_filteredKeysSource, Keys) &&
                _filteredSearch == search &&
                _filteredView == view &&
                _filteredDelimiter == delimiter &&
                _filteredArraysOnly == ShowArrayKeysOnly &&
                _filteredStringsOnly == ShowStringKeysOnly &&
                _filteredSorted == SortKeysByName &&
                _filteredUntranslated == UntranslatedLanguageFilter &&
                _filteredStatus == StatusFilter &&
                _filteredSearchInTranslations == SearchInTranslations &&
                _filteredDefaultLanguage == LocalizationConfigProvider.Config.DefaultLanguage)
            {
                return _filteredKeys;
            }

            _filteredKeys.Clear();
            string viewPrefix = view.Length > 0 ? view + delimiter : null;

            foreach (var key in Keys)
            {
                if (viewPrefix != null && !key.StartsWith(viewPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (search.Length > 0 && key.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    !(SearchInTranslations && TranslationsContain(key, search)))
                    continue;

                if (ShowArrayKeysOnly && !IsArrayKey(LanguageData[key]))
                    continue;

                if (!ShowArrayKeysOnly && ShowStringKeysOnly && IsArrayKey(LanguageData[key]))
                    continue;

                if (!string.IsNullOrEmpty(UntranslatedLanguageFilter) && IsTranslated(key, UntranslatedLanguageFilter))
                    continue;

                if (StatusFilter == KeyStatusFilter.Missing && GetKeyStatus(key).Missing == 0)
                    continue;

                if (StatusFilter == KeyStatusFilter.Problems && GetKeyStatus(key).Problems == 0)
                    continue;

                _filteredKeys.Add(key);
            }

            if (SortKeysByName)
                _filteredKeys.Sort(StringComparer.CurrentCulture);

            _filteredVersion = _dataVersion;
            _filteredKeysSource = Keys;
            _filteredSearch = search;
            _filteredView = view;
            _filteredDelimiter = delimiter;
            _filteredArraysOnly = ShowArrayKeysOnly;
            _filteredStringsOnly = ShowStringKeysOnly;
            _filteredSorted = SortKeysByName;
            _filteredUntranslated = UntranslatedLanguageFilter;
            _filteredStatus = StatusFilter;
            _filteredSearchInTranslations = SearchInTranslations;
            _filteredDefaultLanguage = LocalizationConfigProvider.Config.DefaultLanguage;
            return _filteredKeys;
        }

        /// <summary>
        /// Gets all unique views from existing keys (case-insensitive, preserving first-seen casing).
        /// The returned list is cached.
        /// </summary>
        public IReadOnlyList<string> GetViews()
        {
            char delimiter = CurrentViewDelimiter;
            if (_viewsVersion == _dataVersion && ReferenceEquals(_viewsSource, Keys) && _viewsDelimiter == delimiter)
                return _views;

            _views.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in Keys)
            {
                int delimiterIndex = key.IndexOf(delimiter);
                if (delimiterIndex < 0)
                    continue;

                string view = key.Substring(0, delimiterIndex);
                if (seen.Add(view))
                    _views.Add(view);
            }

            _viewsVersion = _dataVersion;
            _viewsSource = Keys;
            _viewsDelimiter = delimiter;
            return _views;
        }

        /// <summary>
        /// Helper to extract the local name of a key by stripping its view prefix if present.
        /// </summary>
        public string GetLocalKeyName(string fullKey)
        {
            if (string.IsNullOrEmpty(fullKey)) return fullKey;
            int delimiterIndex = fullKey.IndexOf(CurrentViewDelimiter);
            return delimiterIndex >= 0 ? fullKey.Substring(delimiterIndex + 1) : fullKey;
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
        /// Whether a key has a non-empty value in the given language.
        /// Array keys count as translated only when every element is filled.
        /// </summary>
        public bool IsTranslated(string key, string language)
        {
            if (!LanguageData.TryGetValue(key, out var keyData) || !keyData.TryGetValue(language, out var value))
                return false;

            switch (value)
            {
                case string str:
                    return !string.IsNullOrWhiteSpace(str);
                case IList<string> list:
                    if (list.Count == 0)
                        return false;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(list[i]))
                            return false;
                    }
                    return true;
                default:
                    return false;
            }
        }

        private bool TranslationsContain(string key, string search)
        {
            if (!LanguageData.TryGetValue(key, out var keyData))
                return false;

            foreach (var value in keyData.Values)
            {
                switch (value)
                {
                    case string str when str.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0:
                        return true;
                    case IList<string> list:
                        foreach (var item in list)
                        {
                            if (item != null && item.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                                return true;
                        }
                        break;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the cached translation status of a key.
        /// </summary>
        public KeyStatus GetKeyStatus(string key)
        {
            string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;
            if (_statusVersion != _dataVersion || _statusDefaultLanguage != defaultLang)
            {
                _statusCache.Clear();
                _statusVersion = _dataVersion;
                _statusDefaultLanguage = defaultLang;
            }

            if (_statusCache.TryGetValue(key, out var status))
                return status;

            int missing = 0;
            int problems = 0;
            foreach (var lang in LanguageCodes)
            {
                if (!IsTranslated(key, lang))
                    missing++;
                else if (lang != defaultLang && GetTranslationIssue(key, lang) != null)
                    problems++;
            }

            status = new KeyStatus(missing, problems);
            _statusCache[key] = status;
            return status;
        }

        /// <summary>
        /// Describes placeholder or rich-text tag differences between a translation and the
        /// default language, or returns null when they match. Array keys are compared element by element.
        /// </summary>
        public string GetTranslationIssue(string key, string language)
        {
            string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;
            if (language == defaultLang || !LanguageData.TryGetValue(key, out var keyData) ||
                !keyData.TryGetValue(defaultLang, out var source) || !keyData.TryGetValue(language, out var target))
                return null;

            if (source is string sourceText && target is string targetText)
            {
                if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(targetText))
                    return null;
                return McpTextChecks.FindMismatch(sourceText, targetText);
            }

            if (source is IList<string> sourceList && target is IList<string> targetList)
            {
                int count = Math.Min(sourceList.Count, targetList.Count);
                for (int i = 0; i < count; i++)
                {
                    if (string.IsNullOrWhiteSpace(sourceList[i]) || string.IsNullOrWhiteSpace(targetList[i]))
                        continue;

                    string issue = McpTextChecks.FindMismatch(sourceList[i], targetList[i]);
                    if (issue != null)
                        return $"element {i}: {issue}";
                }
            }

            return null;
        }

        /// <summary>
        /// Project languages with the default language first, then by display name.
        /// </summary>
        public List<string> GetLanguagesDefaultFirst()
        {
            string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;
            return LanguageCodes
                .OrderByDescending(code => string.Equals(code, defaultLang, StringComparison.OrdinalIgnoreCase))
                .ThenBy(code => PicoShot.Localization.Data.LanguageDefinitions.GetDisplayName(code), StringComparer.CurrentCulture)
                .ToList();
        }

        /// <summary>
        /// Counts the keys that are translated in the given language.
        /// </summary>
        public int CountTranslated(string language)
        {
            int count = 0;
            foreach (var key in Keys)
            {
                if (IsTranslated(key, language))
                    count++;
            }
            return count;
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
        /// Resets all data to initial state.
        /// </summary>
        public void Reset()
        {
            HasUnsavedChanges = false;
            SelectedKey = null;
            Keys.Clear();
            LanguageData.Clear();
            KeyFoldouts.Clear();
            LanguageCodes.Clear();
            LanguageCodes.Add(LocalizationConfigProvider.Config.DefaultLanguage);
            PendingRemovedLanguages.Clear();
            UntranslatedLanguageFilter = null;
            _history?.Clear();
            GeneratedCharset = "";
            HasGeneratedCharset = false;
        }

        /// <summary>
        /// Adds a new language code.
        /// </summary>
        public bool AddLanguage(string language)
        {
            if (LanguageCodes.Contains(language))
                return false;

            LanguageCodes.Add(language);
            PendingRemovedLanguages.Remove(language);

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
            PendingRemovedLanguages.Add(language);

            foreach (var key in Keys.Where(key => LanguageData[key].ContainsKey(language)))
            {
                LanguageData[key].Remove(language);
            }

            if (string.Equals(UntranslatedLanguageFilter, language, StringComparison.OrdinalIgnoreCase))
                UntranslatedLanguageFilter = null;

            HasUnsavedChanges = true;
            return true;
        }

        /// <summary>
        /// Copies every value of a language so a removal can be undone.
        /// </summary>
        public Dictionary<string, object> CaptureLanguage(string language)
        {
            var snapshot = new Dictionary<string, object>();
            foreach (var key in Keys)
            {
                if (LanguageData[key].TryGetValue(language, out var value))
                    snapshot[key] = value is List<string> list ? new List<string>(list) : value;
            }
            return snapshot;
        }

        /// <summary>
        /// Re-adds a removed language with the values captured by <see cref="CaptureLanguage"/>.
        /// Keys created after the capture get empty values.
        /// </summary>
        public bool RestoreLanguage(string language, int index, Dictionary<string, object> snapshot)
        {
            if (LanguageCodes.Contains(language))
                return false;

            LanguageCodes.Insert(Mathf.Clamp(index, 0, LanguageCodes.Count), language);
            PendingRemovedLanguages.Remove(language);

            foreach (var key in Keys)
            {
                if (snapshot != null && snapshot.TryGetValue(key, out var value))
                    LanguageData[key][language] = value;
                else
                    AddLanguageToKey(key, language);
            }

            HasUnsavedChanges = true;
            return true;
        }

        /// <summary>
        /// Adds a new key to the data. Key names must be unique regardless of casing.
        /// </summary>
        public bool AddKey(string key, bool isArray)
        {
            key = key?.Trim();
            if (string.IsNullOrEmpty(key) || Keys.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)))
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
        /// Removes a view and all its keys.
        /// </summary>
        public void RemoveView(string view)
        {
            if (string.IsNullOrEmpty(view)) return;

            string prefix = view + CurrentViewDelimiter;
            var keysToRemove = Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var k in keysToRemove)
            {
                RemoveKey(k);
            }

            if (SelectedView == view)
            {
                SelectedView = "";
                SelectedKey = null;
            }
        }

        /// <summary>
        /// Renames a key while preserving its data. Key names must be unique regardless of casing.
        /// </summary>
        public bool RenameKey(string oldKey, string newKey)
        {
            newKey = newKey?.Trim();
            if (!Keys.Contains(oldKey))
                return false;

            if (Keys.Any(k => !k.Equals(oldKey, StringComparison.OrdinalIgnoreCase) &&
                               k.Equals(newKey, StringComparison.OrdinalIgnoreCase)))
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
        /// Moves an array element to another index in every language.
        /// </summary>
        public void MoveArrayElement(string key, int from, int to)
        {
            if (from == to || !LanguageData.TryGetValue(key, out var keyData))
                return;

            foreach (var lang in LanguageCodes)
            {
                if (keyData.TryGetValue(lang, out var value) && value is List<string> list &&
                    from >= 0 && from < list.Count && to >= 0 && to < list.Count)
                {
                    string item = list[from];
                    list.RemoveAt(from);
                    list.Insert(to, item);
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
        /// Generates one deduplicated, consistently ordered character set for all selected languages.
        /// </summary>
        public void GenerateCharset()
        {
            var charSet = new HashSet<char>();

            foreach (var lang in LanguageCodes.Where(l => LanguageSelectionForCharset[l]))
            {
                foreach (var key in Keys)
                {
                    if (LanguageData[key].TryGetValue(lang, out var value))
                        AddValueToCharset(value, charSet);
                }
            }

            GeneratedCharset = new string(charSet.OrderBy(c => c).ToArray());
            HasGeneratedCharset = true;
        }

        /// <summary>
        /// Clears the generated charset after its language selection changes.
        /// </summary>
        public void ClearGeneratedCharset()
        {
            GeneratedCharset = "";
            HasGeneratedCharset = false;
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
