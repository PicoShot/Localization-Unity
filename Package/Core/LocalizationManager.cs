using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using PicoShot.Localization.Config;
using PicoShot.Localization.Data;
using PicoShot.Localization.Hashing;
using PicoShot.Localization.Rtl;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PicoShot.Localization
{
    /// <summary>
    /// Central manager for localization system.
    /// Handles language loading, switching, and text retrieval.
    /// </summary>
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public static class LocalizationManager
    {
        #region Events

        public static event Action OnLanguageChanged;
        public static event Action<TMP_FontAsset, Font> OnFontChanged;
        public static event Action<string> OnLanguageLoadError;
        public static event Action<string> OnMissingTranslation;

        #endregion

        #region Configuration

        public const string LanguagesDirectory = "Locales";
        public const string FileExtension = ".bloc";

        /// <summary>
        /// Gets the path to the locales directory.
        /// </summary>
        public static string LanguagesPath
        {
            get
            {
#if UNITY_STANDALONE || UNITY_EDITOR
                string projectPath = Path.GetDirectoryName(Application.dataPath);
                if (projectPath != null)
                {
                    string normalizedPath = projectPath.Replace('\\', '/');
                    int searchIndex = 0;
                    while (true)
                    {
                        int libraryIndex = normalizedPath.IndexOf("/Library/", searchIndex, StringComparison.OrdinalIgnoreCase);
                        if (libraryIndex < 0)
                            break;

                        string candidatePath = projectPath.Substring(0, libraryIndex);
                        if (Directory.Exists(Path.Combine(candidatePath, "Assets")) &&
                            Directory.Exists(Path.Combine(candidatePath, "ProjectSettings")))
                        {
                            projectPath = candidatePath;
                            break;
                        }
                        searchIndex = libraryIndex + 1;
                    }
                }
                return Path.Combine(projectPath ?? string.Empty, LanguagesDirectory);
#else
                return Path.Combine(Application.streamingAssetsPath, LanguagesDirectory);
#endif
            }
        }

        /// <summary>
        /// Gets the default language from config.
        /// </summary>
        public static string DefaultLanguage => LocalizationConfigProvider.Config.DefaultLanguage;

        /// <summary>
        /// Gets whether anti-tamper mode is enabled.
        /// </summary>
        public static bool IsAntiTamperEnabled => LocalizationConfigProvider.Config.IsAntiTamperEnabled;

        /// <summary>
        /// Gets the selected languages from config (used when protection is enabled).
        /// </summary>
        public static IReadOnlyList<string> SelectedLanguages => LocalizationConfigProvider.Config.SelectedLanguages;

        private static bool IsCloneEditor()
        {
#if UNITY_EDITOR
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            if (projectPath == null) return false;

            string normalized = projectPath.Replace('\\', '/');
            int searchIndex = 0;
            while (true)
            {
                int libraryIndex = normalized.IndexOf("/Library/", searchIndex, StringComparison.OrdinalIgnoreCase);
                if (libraryIndex < 0)
                    break;

                string candidatePath = projectPath.Substring(0, libraryIndex);
                if (Directory.Exists(Path.Combine(candidatePath, "Assets")) &&
                    Directory.Exists(Path.Combine(candidatePath, "ProjectSettings")))
                {
                    return true;
                }
                searchIndex = libraryIndex + 1;
            }
            return false;
#else
            return false;
#endif
        }

        #endregion

        #region State

        private static string _currentLanguageCode;
        private static bool _isInitialized;
        private static bool _initializationAttempted;

        private static LanguageDictionary _currentLanguageData;
        private static LanguageDictionary _fallbackLanguageData;

        private static HashSet<string> _allTranslationKeys;
        private static HashSet<string> _availableLanguages;

        private static readonly Dictionary<long, string[]> _arrayCache = new();

        #endregion

        #region Properties

        public static string CurrentLanguage => _currentLanguageCode;
        public static bool IsInitialized => _isInitialized;
        public static bool IsRightToLeft => LanguageDefinitions.IsRightToLeft(_currentLanguageCode);

#if UNITY_EDITOR
        public static IEnumerable<string> AllTranslationKeys
        {
            get
            {
                if (!_isInitialized) Initialize();
                return _allTranslationKeys ?? Enumerable.Empty<string>();
            }
        }
#endif

        #endregion

        #region Initialization

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void AutoInitialize()
        {
            if (!_isInitialized)
                Initialize();
        }

#if UNITY_EDITOR
        static LocalizationManager()
        {
            if (!_isInitialized)
                Initialize();
        }
#endif

        /// <summary>
        /// Initializes the localization system.
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;
            if (_initializationAttempted) return;

            _initializationAttempted = true;
            _isInitialized = true;

            try
            {
#if UNITY_EDITOR
                DeleteJunkFiles();
#endif

                ScanAvailableLanguages();

                if (_availableLanguages.Count == 0)
                {
#if UNITY_EDITOR
                    return;
#else
                    string error = $"[LocalizationManager] No language files found in: {LanguagesPath}";
                    if (IsCloneEditor())
                    {
                        Debug.LogWarning($"[LocalizationManager] (Clone Editor) No language files found in: {LanguagesPath}");
                    }
                    else
                    {
                        Debug.LogError(error);
                    }
                    OnLanguageLoadError?.Invoke(error);
                    _isInitialized = false;
                    return;
#endif
                }

#if UNITY_EDITOR
                string targetLanguage = !string.IsNullOrEmpty(_currentLanguageCode) && _availableLanguages.Contains(_currentLanguageCode)
                    ? _currentLanguageCode
                    : (!Application.isPlaying && _availableLanguages.Contains(DefaultLanguage) ? DefaultLanguage : DetectSystemLanguage());
#else
                string targetLanguage = !string.IsNullOrEmpty(_currentLanguageCode) && _availableLanguages.Contains(_currentLanguageCode)
                    ? _currentLanguageCode
                    : DetectSystemLanguage();
#endif

                SetLanguage(targetLanguage, useFallback: false);

                Application.quitting += Dispose;
                OnLanguageChanged?.Invoke();
            }
            catch (Exception ex)
            {
                string error = $"[LocalizationManager] Initialization failed: {ex}";
                if (IsCloneEditor())
                {
                    Debug.LogWarning($"[LocalizationManager] (Clone Editor) Initialization failed: {ex.Message}");
                }
                else
                {
                    Debug.LogError(error);
                }
                OnLanguageLoadError?.Invoke(error);
                _isInitialized = false;
            }
        }

        public static void DeleteJunkFiles()
        {
            try
            {
                if (!Directory.Exists(LanguagesPath))
                    return;

                File.GetAttributes(LanguagesPath);

                var junkExtensions = new[] { ".bak", ".tmp" };

                var filesToDelete = Directory.EnumerateFiles(LanguagesPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => junkExtensions.Contains(Path.GetExtension(f).ToLower()));

                foreach (var file in filesToDelete)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalizationManager] Failed to delete junk files: {ex.Message}");
            }
        }

        private static void ScanAvailableLanguages()
        {
            _availableLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _allTranslationKeys = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                if (!Directory.Exists(LanguagesPath))
                {
#if !UNITY_EDITOR
                    Debug.LogWarning($"[LocalizationManager] Languages directory not found: {LanguagesPath}");
#endif
                    return;
                }

                File.GetAttributes(LanguagesPath);

                var blocFiles = Directory.GetFiles(LanguagesPath, $"*{FileExtension}", SearchOption.TopDirectoryOnly);
                var config = LocalizationConfigProvider.Config;

                foreach (var file in blocFiles)
                {
                    string fileName = Path.GetFileName(file);

                    if (!LocaleBlocSerializer.ValidateFile(file, out _, out string languageCode) || string.IsNullOrEmpty(languageCode))
                    {
                        Debug.LogWarning($"[LocalizationManager] Skipping invalid/corrupted file: {fileName}");
                        continue;
                    }

                    if (!LanguageDefinitions.IsValidLanguage(languageCode))
                    {
                        Debug.LogError($"[LocalizationManager] Rejecting file '{fileName}' - unsupported language code: '{languageCode}'");
                        OnLanguageLoadError?.Invoke($"Unsupported language: {languageCode}");
                        continue;
                    }

                    if (config.ProtectionMode == ProtectionMode.SelectionOnly ||
                        config.ProtectionMode == ProtectionMode.Both)
                    {
                        if (!config.SelectedLanguages.Contains(languageCode))
                        {
                            Debug.LogWarning($"[LocalizationManager] Skipping unauthorized language: {languageCode} ({fileName})");
                            continue;
                        }
                    }

                    if (IsAntiTamperEnabled)
                    {
                        if (!VerifyFileHash(file, fileName, config))
                        {
                            Debug.LogError($"[LocalizationManager] Hash verification failed for: {languageCode} ({fileName})");
                            OnLanguageLoadError?.Invoke($"File tampering detected: {languageCode}");
                            continue;
                        }
                    }

                    string fileNameLanguage = Path.GetFileNameWithoutExtension(file);
                    if (!string.Equals(fileNameLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.LogError($"[LocalizationManager] Rejecting file '{fileName}' - filename mismatch: " +
                            $"expected '{languageCode}{FileExtension}' but found '{fileName}'. " +
                            $"Filename must match the language code stored in the file header.");
                        OnLanguageLoadError?.Invoke($"Invalid filename: {fileName}");
                        continue;
                    }

                    _availableLanguages.Add(languageCode);
                }

                if (_availableLanguages.Contains(config.DefaultLanguage))
                {
                    try
                    {
                        var defaultData = LoadLocaleFile(config.DefaultLanguage);
                        foreach (var key in defaultData.Keys)
                        {
                            _allTranslationKeys.Add(key);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[LocalizationManager] Failed to load default language keys: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalizationManager] Languages directory is not accessible: {ex.Message}");
            }
        }

        #endregion

        #region Language Management

        /// <summary>
        /// Sets the current language.
        /// </summary>
        public static void SetLanguage(string languageCode, bool useFallback = true)
        {
            if (string.IsNullOrEmpty(languageCode))
            {
                Debug.LogError("[LocalizationManager] SetLanguage called with null or empty code");
                return;
            }

            if (!_isInitialized)
            {
                Initialize();
                return;
            }

            string targetLanguage = ResolveTargetLanguage(languageCode, useFallback);

            if (_currentLanguageCode == targetLanguage && _currentLanguageData != null)
                return;

            try
            {
                LoadLanguageData(targetLanguage);
                _currentLanguageCode = targetLanguage;

                _arrayCache.Clear();
                OnLanguageChanged?.Invoke();
                TriggerFontChanged();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalizationManager] Failed to set language to '{targetLanguage}': {ex}");
                OnLanguageLoadError?.Invoke($"Failed to load language '{targetLanguage}'");
            }
        }

        private static string ResolveTargetLanguage(string requestedCode, bool useFallback)
        {
            if (_availableLanguages.Contains(requestedCode))
                return requestedCode;

            if (useFallback)
            {
                string fallback = LanguageDefinitions.GetFallbackLanguage(requestedCode);
                if (_availableLanguages.Contains(fallback))
                    return fallback;
            }

            return DefaultLanguage;
        }

        /// <summary>
        /// Resolves the currently active TMP and legacy fonts based on the config and current language.
        /// </summary>
        public static void GetCurrentFonts(out TMP_FontAsset tmpFont, out Font legacyFont)
        {
            var config = LocalizationConfigProvider.Config;
            if (config == null || !config.IsFontSystemEnabled)
            {
                tmpFont = null;
                legacyFont = null;
                return;
            }

            tmpFont = config.DefaultTMPFont;
            legacyFont = config.DefaultLegacyFont;

            foreach (var mapping in config.FontMappings)
            {
                if (mapping.languageCode.Equals(_currentLanguageCode, StringComparison.OrdinalIgnoreCase))
                {
                    if (mapping.tmpFont != null) tmpFont = mapping.tmpFont;
                    if (mapping.legacyFont != null) legacyFont = mapping.legacyFont;
                    break;
                }
            }
        }

        private static void TriggerFontChanged()
        {
            GetCurrentFonts(out var tmpFont, out var legacyFont);
            OnFontChanged?.Invoke(tmpFont, legacyFont);
        }

        private static void LoadLanguageData(string languageCode)
        {
            _currentLanguageData = LoadLocaleFile(languageCode);

            if (languageCode != DefaultLanguage)
            {
                _fallbackLanguageData = LoadLocaleFile(DefaultLanguage);
            }
            else
            {
                _fallbackLanguageData = _currentLanguageData;
            }
        }

        private static LanguageDictionary LoadLocaleFile(string languageCode)
        {
            string filePath = GetLocaleFilePath(languageCode);

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Locale file not found for language '{languageCode}'", filePath);
            }

            var localeData = LocaleBlocSerializer.LoadFile(filePath, out var info);

            if (localeData?.Translations == null)
            {
                return new LanguageDictionary(new Dictionary<string, object>());
            }

            return new LanguageDictionary(localeData.Translations);
        }

        private static string GetLocaleFilePath(string languageCode)
        {
            return Path.Combine(LanguagesPath, $"{languageCode}{FileExtension}");
        }

        private static bool VerifyFileHash(string filePath, string fileName, LocalizationConfig config)
        {
            if (!config.TryGetFileHash(fileName, out string expectedHash))
            {
                Debug.LogWarning($"[LocalizationManager] No hash stored for file: {fileName}");
                return false;
            }

            string actualHash = CalculateFileHash(filePath);
            return string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Calculates SHA256 hash of a file.
        /// </summary>
        public static string CalculateFileHash(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Detects system language from Unity settings.
        /// </summary>
        public static string DetectSystemLanguage()
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            string systemLanguage = LanguageDefinitions.FromSystemLanguage(Application.systemLanguage);
            return ResolveTargetLanguage(systemLanguage, useFallback: true);
        }

        #endregion

        #region Text Retrieval

        /// <summary>
        /// Gets a translated string by key with optional formatting arguments.
        /// Strings are treated as literals, Key structs are resolved as localization keys,
        /// other types are converted via ToString().
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetText(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[LocalizationManager] GetText called with null or empty key");
                return string.Empty;
            }

            if (!_isInitialized)
            {
                Initialize();
            }

            string text = GetRawText(key);

            if (args != null && args.Length > 0)
            {
                var resolvedArgs = new string[args.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    resolvedArgs[i] = args[i] switch
                    {
                        Key k => GetRawText(k.Value),
                        null => string.Empty,
                        string s => s,
                        _ => args[i].ToString()
                    };
                }

                text = string.Format(text, resolvedArgs);
            }

            if (LocalizationConfigProvider.Config.SupportMixedText)
            {
                text = RtlTextHandler.FixMixed(text, IsRightToLeft);
            }
            else if (IsRightToLeft)
            {
                text = RtlTextHandler.Fix(text);
            }

            return text;
        }

        internal static string GetLogicalText(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (!_isInitialized) Initialize();
            
            string text = GetRawText(key);

            if (args != null && args.Length > 0)
            {
                var resolvedArgs = new string[args.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    resolvedArgs[i] = args[i] switch
                    {
                        Key k => GetRawText(k.Value),
                        null => string.Empty,
                        string s => s,
                        _ => args[i].ToString()
                    };
                }
                text = string.Format(text, resolvedArgs);
            }

            // We do NOT shape or reverse the text here. 
            // We want the pure, raw, unshaped text so TMP can calculate line breaks accurately.
            return text;
        }

        internal static string GetLogicalArrayText(string key, int index)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (!_isInitialized) Initialize();

            string[] array = GetArrayInternal(Key.FromKey(key));
            if (array == null || array.Length == 0)
            {
                Debug.LogWarning($"[LocalizationManager] Key '{key}' is not an array or is empty");
                return $"[{key}]";
            }

            if (index >= 0 && index < array.Length)
                return array[index] ?? string.Empty;

            Debug.LogWarning($"[LocalizationManager] Array index {index} out of range for key '{key}'");
            return $"[{key}:{index}]";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetText(long keyHash, params object[] args)
        {
            if (keyHash == 0)
            {
                Debug.LogError("[LocalizationManager] GetText called with null or empty key");
                return string.Empty;
            }

            if (!_isInitialized)
            {
                Initialize();
            }

            string text = GetRawText(keyHash);

            if (args != null && args.Length > 0)
            {
                var resolvedArgs = new string[args.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    resolvedArgs[i] = args[i] switch
                    {
                        Key k => GetRawText(k.Value),
                        null => string.Empty,
                        string s => s,
                        _ => args[i].ToString()
                    };
                }
                text = string.Format(text, resolvedArgs);
            }

            if (LocalizationConfigProvider.Config.SupportMixedText)
            {
                text = RtlTextHandler.FixMixed(text, IsRightToLeft);
            }
            else if (IsRightToLeft)
            {
                text = RtlTextHandler.Fix(text);
            }

            return text;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long GetKeyHash(string key)
        {
            return Hash64.CreateIgnoreCase(key);
        }

        /// <summary>
        /// Gets a translated string with all parameters resolved as localization keys.
        /// </summary>
        public static string GetTextWithParamKeys(string key, params string[] args)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[LocalizationManager] GetText called with null or empty key");
                return string.Empty;
            }

            if (!_isInitialized)
            {
                Initialize();
            }

            string text = GetRawText(key);

            //* Rytiex
            if (args != null && args.Length > 0)
            {
                string[] keyArgs = args;
                for (int i = 0; i < args.Length; i++)
                {
                    string argKey = args[i];
                    keyArgs[i] = GetRawText(argKey);
                }

                text = string.Format(text, keyArgs);
            }
            //*

            if (LocalizationConfigProvider.Config.SupportMixedText)
            {
                text = RtlTextHandler.FixMixed(text, IsRightToLeft);
            }
            else if (IsRightToLeft)
            {
                text = RtlTextHandler.Fix(text);
            }

            return text;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string GetRawText(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            ReadOnlySpan<char> keySpan = key.AsSpan();
            long keyHash = Hash64.CreateIgnoreCase(keySpan);

            int foundIndex = -1;
            if (keySpan[keySpan.Length - 1] == ']')
            {
                for (int i = keySpan.Length - 1; i >= 0; i--)
                {
                    if (keySpan[i] != '[')
                        continue;

                    if (int.TryParse(keySpan.Slice(i + 1, keySpan.Length - (i + 2)), out int idx))
                    {
                        keyHash = Hash64.CreateIgnoreCase(keySpan.Slice(0, i));
                        foundIndex = idx;
                    }

                    break;
                }
            }

            if (_currentLanguageData != null && _currentLanguageData.TryGetValue(keyHash, out var value))
            {
                return value switch
                {
                    List<string> list => list.Count > 0 ? list[foundIndex > -1 && foundIndex < list.Count ? foundIndex : 0] : key,
                    _ => value?.ToString() ?? key
                };
            }

            if (_fallbackLanguageData != null && _fallbackLanguageData.TryGetValue(keyHash, out var fallbackValue))
            {
                OnMissingTranslation?.Invoke($"Using fallback for key '{key}' in '{_currentLanguageCode}'");
                return fallbackValue switch
                {
                    List<string> list => list.Count > 0 ? list[foundIndex > -1 && foundIndex < list.Count ? foundIndex : 0] : key,
                    _ => fallbackValue?.ToString() ?? key
                };
            }

            OnMissingTranslation?.Invoke($"Missing translation for key '{key}' in '{_currentLanguageCode}'");
            return key;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string GetRawText(long keyHash)
        {
            if (_currentLanguageData != null && _currentLanguageData.TryGetValue(keyHash, out var value))
            {
                return value switch
                {
                    List<string> list => list.Count > 0 ? list[0] : keyHash.ToString(),
                    _ => value?.ToString() ?? keyHash.ToString()
                };
            }

            if (_fallbackLanguageData != null && _fallbackLanguageData.TryGetValue(keyHash, out var fallbackValue))
            {
                OnMissingTranslation?.Invoke($"Using fallback for key '{keyHash}' in '{_currentLanguageCode}'");
                return fallbackValue switch
                {
                    List<string> list => list.Count > 0 ? list[0] : keyHash.ToString(),
                    _ => fallbackValue?.ToString() ?? keyHash.ToString()
                };
            }

            OnMissingTranslation?.Invoke($"Missing translation for key '{keyHash}' in '{_currentLanguageCode}'");
            return keyHash.ToString();
        }

        /// <summary>
        /// Gets an array of strings by key.
        /// </summary>
        public static string[] GetArray(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[LocalizationManager] GetArray called with null or empty key");
                return null;
            }

            if (!_isInitialized)
            {
                Initialize();
            }

            long keyHash = Hash64.CreateIgnoreCase(key);

            if (_arrayCache.TryGetValue(keyHash, out var cached))
                return cached;

            var array = GetArrayInternal(Key.FromHash(keyHash));

            if (array == null)
                return null;

            if (LocalizationConfigProvider.Config.SupportMixedText)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    array[i] = RtlTextHandler.FixMixed(array[i], IsRightToLeft);
                }
            }
            else if (IsRightToLeft)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    array[i] = RtlTextHandler.Fix(array[i]);
                }
            }

            _arrayCache[keyHash] = array;
            return array;
        }

        /// <summary>
        /// Gets an array of strings by key.
        /// </summary>
        public static string[] GetArray(Key key) => GetArray(key.Value);

        /// <summary>
        /// Gets a single element from an array by key and index.
        /// </summary>
        public static string GetArrayText(string key, int index)
        {
            var array = GetArray(key);

            if (array == null || array.Length == 0)
            {
                Debug.LogWarning($"[LocalizationManager] Key '{key}' is not an array or is empty");
                return $"[{key}]";
            }

            if (index >= 0 && index < array.Length)
            {
                return array[index] ?? string.Empty;
            }

            Debug.LogWarning($"[LocalizationManager] Array index {index} out of range for key '{key}'");
            return $"[{key}:{index}]";
        }

        /// <summary>
        /// Gets a single element from an array by key and index.
        /// </summary>
        public static string GetArrayText(Key key, int index) => GetArrayText(key.Value, index);

        private static string[] GetArrayInternal(Key key)
        {
            object value = null;
            bool found = false;

            if (_currentLanguageData != null)
            {
                found = _currentLanguageData.TryGetValue(key.Hash, out value);
            }

            if (!found && _fallbackLanguageData != null)
            {
                found = _fallbackLanguageData.TryGetValue(key.Hash, out value);
                if (found)
                {
                    OnMissingTranslation?.Invoke($"Using fallback for array key '{key}' in '{_currentLanguageCode}'");
                }
            }

            if (!found)
            {
                OnMissingTranslation?.Invoke($"Missing array translation for key '{key}' in '{_currentLanguageCode}'");
                return null;
            }

            return ConvertToStringArray(value);
        }

        private static string[] ConvertToStringArray(object value)
        {
            return value switch
            {
                List<string> list => list.ToArray(),
                string single => new[] { single },
                _ => null
            };
        }

        #endregion

        #region Language Information

        /// <summary>
        /// Gets available languages.
        /// </summary>
        public static IEnumerable<string> GetAvailableLanguages(bool withNativeNames = false)
        {
            if (_availableLanguages == null || _availableLanguages.Count == 0)
            {
                return Enumerable.Empty<string>();
            }

            return _availableLanguages.Select(code =>
                LanguageDefinitions.GetDisplayName(code, withNativeNames));
        }

        /// <summary>
        /// Gets available language codes.
        /// </summary>
        public static IEnumerable<string> GetAvailableLanguageCodes()
        {
            return _availableLanguages ?? Enumerable.Empty<string>();
        }

        /// <summary>
        /// Checks if a language is available.
        /// </summary>
        public static bool IsLanguageAvailable(string languageCode)
        {
            return _availableLanguages?.Contains(languageCode) ?? false;
        }

        /// <summary>
        /// Gets the display name for a language code.
        /// </summary>
        public static string GetLanguageDisplayName(string languageCode, bool native = false)
        {
            return LanguageDefinitions.GetDisplayName(languageCode, native);
        }

        /// <summary>
        /// Gets the language code from a display name.
        /// </summary>
        public static string GetLanguageCode(string displayName, bool nativeName = false)
        {
            return LanguageDefinitions.GetLanguageCode(displayName, nativeName);
        }

        /// <summary>
        /// Checks if a key exists in the current language.
        /// </summary>
        public static bool HasKey(string key)
        {
            return _currentLanguageData?.ContainsKey(Hash64.CreateIgnoreCase(key)) ?? false;
        }

        /// <summary>
        /// Checks if a key exists in the default language.
        /// </summary>
        public static bool HasKeyInDefault(string key)
        {
            return _fallbackLanguageData?.ContainsKey(Hash64.CreateIgnoreCase(key)) ?? false;
        }

        /// <summary>
        /// Gets all available translation keys.
        /// </summary>
        public static IEnumerable<string> GetAllKeys()
        {
            return _allTranslationKeys ?? Enumerable.Empty<string>();
        }

        #endregion

        #region Editor Support

#if UNITY_EDITOR

        /// <summary>
        /// Gets the file path for a language code (for editor use).
        /// </summary>
        public static string GetLanguageFilePath(string languageCode)
        {
            return GetLocaleFilePath(languageCode);
        }

        /// <summary>
        /// Refreshes available languages (for editor use).
        /// </summary>
        public static void RefreshAvailableLanguages()
        {
            ScanAvailableLanguages();
        }

#endif

        #endregion

        #region Bind Functions

        /// <summary>
        /// Binds a TMP_Text component to a translation key.
        /// </summary>
        /// <param name="textComponent">The TMP_Text component to bind.</param>
        /// <param name="key">The translation key.</param>
        /// <param name="arrayIndex">For array values: -1 = use as string, >= 0 = specific array index.</param>
        /// <param name="textProcessor">Optional function to process text before display.</param>
        /// <param name="args">Format parameters for the translation.</param>
        public static void BindText(TMP_Text textComponent, string key, int arrayIndex = -1, Func<string, string> textProcessor = null, params object[] args)
        {
            if (textComponent == null)
            {
                Debug.LogError("[LocalizationManager] BindText called with null TMP_Text component");
                return;
            }

            var localizedComponent = textComponent.GetComponent<LocalizationTextComponent>();
            if (localizedComponent == null)
            {
                localizedComponent = textComponent.gameObject.AddComponent<LocalizationTextComponent>();
            }

            localizedComponent.TranslationKey = key;
            localizedComponent.ArrayIndex = arrayIndex;

            if (args != null && args.Length > 0)
            {
                localizedComponent.SetFormatParameters(args.Select(a => a?.ToString() ?? string.Empty).ToArray());
            }

            if (textProcessor != null)
            {
                localizedComponent.AddTextProcessor(textProcessor);
            }
        }

        /// <summary>
        /// Binds a TMP_Dropdown component to a translation key (array type).
        /// </summary>
        /// <param name="dropdown">The TMP_Dropdown component to bind.</param>
        /// <param name="key">The translation key for the array values.</param>
        /// <param name="arrayMaxSize">Maximum number of array elements to use (0 = unlimited).</param>
        /// <param name="textProcessor">Optional function to process each option before display.</param>
        /// <param name="args">Format parameters for each option (applied to all).</param>
        public static void BindText(TMP_Dropdown dropdown, string key, int arrayMaxSize = 0, Func<string, string> textProcessor = null, params object[] args)
        {
            if (dropdown == null)
            {
                Debug.LogError("[LocalizationManager] BindText called with null TMP_Dropdown component");
                return;
            }

            var localizedComponent = dropdown.GetComponent<LocalizationTextComponent>();
            if (localizedComponent == null)
            {
                localizedComponent = dropdown.gameObject.AddComponent<LocalizationTextComponent>();
            }

            localizedComponent.TranslationKey = key;
            localizedComponent.ArraySizeLimit = arrayMaxSize;

            if (args != null && args.Length > 0)
            {
                localizedComponent.SetFormatParameters(args.Select(a => a?.ToString() ?? string.Empty).ToArray());
            }

            if (textProcessor != null)
            {
                localizedComponent.AddTextProcessor(textProcessor);
            }
        }

        /// <summary>
        /// Binds a Legacy Text component to a translation key.
        /// </summary>
        /// <param name="textComponent">The Legacy Text component to bind.</param>
        /// <param name="key">The translation key.</param>
        /// <param name="arrayIndex">For array values: -1 = use as string, >= 0 = specific array index.</param>
        /// <param name="textProcessor">Optional function to process text before display.</param>
        /// <param name="args">Format parameters for the translation.</param>
        public static void BindText(Text textComponent, string key, int arrayIndex = -1, Func<string, string> textProcessor = null, params object[] args)
        {
            if (textComponent == null)
            {
                Debug.LogError("[LocalizationManager] BindText called with null Text component");
                return;
            }

            var localizedComponent = textComponent.GetComponent<LocalizationTextComponent>();
            if (localizedComponent == null)
            {
                localizedComponent = textComponent.gameObject.AddComponent<LocalizationTextComponent>();
            }

            localizedComponent.TranslationKey = key;
            localizedComponent.ArrayIndex = arrayIndex;

            if (args != null && args.Length > 0)
            {
                localizedComponent.SetFormatParameters(args.Select(a => a?.ToString() ?? string.Empty).ToArray());
            }

            if (textProcessor != null)
            {
                localizedComponent.AddTextProcessor(textProcessor);
            }
        }

        /// <summary>
        /// Binds a Legacy Dropdown component to a translation key (array type).
        /// </summary>
        /// <param name="dropdown">The Legacy Dropdown component to bind.</param>
        /// <param name="key">The translation key for the array values.</param>
        /// <param name="arrayMaxSize">Maximum number of array elements to use (0 = unlimited).</param>
        /// <param name="textProcessor">Optional function to process each option before display.</param>
        /// <param name="args">Format parameters for each option (applied to all).</param>
        public static void BindText(Dropdown dropdown, string key, int arrayMaxSize = 0, Func<string, string> textProcessor = null, params object[] args)
        {
            if (dropdown == null)
            {
                Debug.LogError("[LocalizationManager] BindText called with null Dropdown component");
                return;
            }

            var localizedComponent = dropdown.GetComponent<LocalizationTextComponent>();
            if (localizedComponent == null)
            {
                localizedComponent = dropdown.gameObject.AddComponent<LocalizationTextComponent>();
            }

            localizedComponent.TranslationKey = key;
            localizedComponent.ArraySizeLimit = arrayMaxSize;

            if (args != null && args.Length > 0)
            {
                localizedComponent.SetFormatParameters(args.Select(a => a?.ToString() ?? string.Empty).ToArray());
            }

            if (textProcessor != null)
            {
                localizedComponent.AddTextProcessor(textProcessor);
            }
        }

        /// <summary>
        /// Binds a TextMesh component to a translation key.
        /// </summary>
        /// <param name="textMesh">The TextMesh component to bind.</param>
        /// <param name="key">The translation key.</param>
        /// <param name="arrayIndex">For array values: -1 = use as string, >= 0 = specific array index.</param>
        /// <param name="textProcessor">Optional function to process text before display.</param>
        /// <param name="args">Format parameters for the translation.</param>
        public static void BindText(TextMesh textMesh, string key, int arrayIndex = -1, Func<string, string> textProcessor = null, params object[] args)
        {
            if (textMesh == null)
            {
                Debug.LogError("[LocalizationManager] BindText called with null TextMesh component");
                return;
            }

            var localizedComponent = textMesh.GetComponent<LocalizationTextComponent>();
            if (localizedComponent == null)
            {
                localizedComponent = textMesh.gameObject.AddComponent<LocalizationTextComponent>();
            }

            localizedComponent.TranslationKey = key;
            localizedComponent.ArrayIndex = arrayIndex;

            if (args != null && args.Length > 0)
            {
                localizedComponent.SetFormatParameters(args.Select(a => a?.ToString() ?? string.Empty).ToArray());
            }

            if (textProcessor != null)
            {
                localizedComponent.AddTextProcessor(textProcessor);
            }
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Disposes resources and unsubscribes from events.
        /// </summary>
        public static void Dispose()
        {
            OnLanguageChanged = null;
            OnFontChanged = null;
            OnLanguageLoadError = null;
            OnMissingTranslation = null;

            _currentLanguageData = null;
            _fallbackLanguageData = null;

            _allTranslationKeys = null;
            _availableLanguages = null;

            _isInitialized = false;
            _initializationAttempted = false;
        }

        #endregion
    }
}
