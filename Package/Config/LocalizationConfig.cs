using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace PicoShot.Localization.Config
{
    /// <summary>
    /// Compression mode for BLOC files.
    /// </summary>
    public enum CompressionMode
    {
        /// <summary>
        /// No compression. Fastest save/load but largest file size.
        /// </summary>
        Disabled,

        /// <summary>
        /// Fast compression. Good balance between speed and size.
        /// </summary>
        Fastest,

        /// <summary>
        /// Optimal compression. Best file size but slower save/load.
        /// </summary>
        Optimal
    }

    /// <summary>
    /// Protection mode for localization files.
    /// </summary>
    public enum ProtectionMode
    {
        /// <summary>
        /// No protection, all language files can be loaded.
        /// </summary>
        Disabled,

        /// <summary>
        /// Only selected languages can be loaded.
        /// </summary>
        SelectionOnly,

        /// <summary>
        /// Full anti-tampering with SHA256 hash verification.
        /// </summary>
        AntiTamper,

        /// <summary>
        /// Both selection and anti-tamper protection enabled.
        /// </summary>
        Both
    }

    /// <summary>
    /// Configuration asset for localization system.
    /// </summary>
    public class LocalizationConfig : ScriptableObject
    {
        [Tooltip("Default language code to use when system language is not available")]
        [SerializeField]
        private string _defaultLanguage = "en";

        [Tooltip("Compression mode for BLOC files")]
        [SerializeField]
        private CompressionMode _compressionMode = CompressionMode.Optimal;

        [Tooltip("Protection mode for localization files")]
        [SerializeField]
        private ProtectionMode _protectionMode = ProtectionMode.Disabled;

        [Tooltip("Languages allowed to load when protection is enabled")]
        [SerializeField]
        private List<string> _selectedLanguages = new() { "en" };

        [Tooltip("SHA256 hashes for anti-tamper verification (filename -> hash)")]
        [SerializeField]
        private List<HashEntry> _fileHashes = new();

        [Header("Font System")]
        [Tooltip("Enable language-specific fonts system")]
        [SerializeField]
        private bool _isFontSystemEnabled = false;

        [Tooltip("Default TextMeshPro font asset")]
        [SerializeField]
        private TMP_FontAsset _defaultTMPFont;

        [Tooltip("Default Legacy Text font")]
        [SerializeField]
        private Font _defaultLegacyFont;

        [Tooltip("Font overrides per language")]
        [SerializeField]
        private List<LanguageFontMapping> _fontMappings = new();

        /// <summary>
        /// Default language code.
        /// </summary>
        public string DefaultLanguage => _defaultLanguage;

        /// <summary>
        /// Current compression mode for BLOC files.
        /// </summary>
        public CompressionMode CompressionMode => _compressionMode;

        /// <summary>
        /// Current protection mode.
        /// </summary>
        public ProtectionMode ProtectionMode => _protectionMode;

        /// <summary>
        /// Whether any protection is enabled.
        /// </summary>
        public bool IsProtectionEnabled => _protectionMode != ProtectionMode.Disabled;

        /// <summary>
        /// Whether anti-tamper mode is enabled (requires hash verification).
        /// </summary>
        public bool IsAntiTamperEnabled => _protectionMode == ProtectionMode.AntiTamper || _protectionMode == ProtectionMode.Both;

        /// <summary>
        /// Languages allowed to load when protection is enabled.
        /// </summary>
        public IReadOnlyList<string> SelectedLanguages => _selectedLanguages;

        /// <summary>
        /// Whether the font system is enabled.
        /// </summary>
        public bool IsFontSystemEnabled => _isFontSystemEnabled;

        /// <summary>
        /// Gets the default TMP Font.
        /// </summary>
        public TMP_FontAsset DefaultTMPFont => _defaultTMPFont;

        /// <summary>
        /// Gets the default Legacy Font.
        /// </summary>
        public Font DefaultLegacyFont => _defaultLegacyFont;

        /// <summary>
        /// Gets font mappings per language.
        /// </summary>
        public IReadOnlyList<LanguageFontMapping> FontMappings => _fontMappings;

        /// <summary>
        /// Gets the stored hash for a file.
        /// </summary>
        public bool TryGetFileHash(string fileName, out string hash)
        {
            foreach (var entry in _fileHashes)
            {
                if (entry.fileName == fileName)
                {
                    hash = entry.hash;
                    return true;
                }
            }
            hash = null;
            return false;
        }

        #region Editor Only

#if UNITY_EDITOR

        /// <summary>
        /// Sets the default language (Editor only).
        /// </summary>
        public void SetDefaultLanguage(string languageCode)
        {
            _defaultLanguage = languageCode;
        }

        /// <summary>
        /// Sets the compression mode (Editor only).
        /// </summary>
        public void SetCompressionMode(CompressionMode mode)
        {
            _compressionMode = mode;
        }

        /// <summary>
        /// Sets the protection mode (Editor only).
        /// </summary>
        public void SetProtectionMode(ProtectionMode mode)
        {
            _protectionMode = mode;
        }

        /// <summary>
        /// Sets the selected languages list (Editor only).
        /// </summary>
        public void SetSelectedLanguages(List<string> languages)
        {
            _selectedLanguages = new List<string>(languages);
        }

        /// <summary>
        /// Adds a language to selected languages (Editor only).
        /// </summary>
        public void AddSelectedLanguage(string languageCode)
        {
            if (!_selectedLanguages.Contains(languageCode))
            {
                _selectedLanguages.Add(languageCode);
            }
        }

        /// <summary>
        /// Removes a language from selected languages (Editor only).
        /// </summary>
        public void RemoveSelectedLanguage(string languageCode)
        {
            _selectedLanguages.Remove(languageCode);
        }

        /// <summary>
        /// Updates or adds a file hash (Editor only).
        /// </summary>
        public void SetFileHash(string fileName, string hash)
        {
            for (int i = 0; i < _fileHashes.Count; i++)
            {
                if (_fileHashes[i].fileName == fileName)
                {
                    _fileHashes[i] = new HashEntry { fileName = fileName, hash = hash };
                    return;
                }
            }
            _fileHashes.Add(new HashEntry { fileName = fileName, hash = hash });
        }

        /// <summary>
        /// Removes a file hash (Editor only).
        /// </summary>
        public void RemoveFileHash(string fileName)
        {
            _fileHashes.RemoveAll(h => h.fileName == fileName);
        }

        /// <summary>
        /// Clears all file hashes (Editor only).
        /// </summary>
        public void ClearFileHashes()
        {
            _fileHashes.Clear();
        }

        /// <summary>
        /// Gets all stored file hashes (Editor only).
        /// </summary>
        public IReadOnlyList<HashEntry> GetFileHashes()
        {
            return _fileHashes;
        }

        public void SetFontSystemEnabled(bool enabled)
        {
            _isFontSystemEnabled = enabled;
        }

        public void SetDefaultFonts(TMP_FontAsset tmpFont, Font legacyFont)
        {
            _defaultTMPFont = tmpFont;
            _defaultLegacyFont = legacyFont;
        }

        public void SetFontMapping(string languageCode, TMP_FontAsset tmpFont, Font legacyFont)
        {
            for (int i = 0; i < _fontMappings.Count; i++)
            {
                if (_fontMappings[i].languageCode.Equals(languageCode, StringComparison.OrdinalIgnoreCase))
                {
                    _fontMappings[i] = new LanguageFontMapping
                    {
                        languageCode = languageCode,
                        tmpFont = tmpFont,
                        legacyFont = legacyFont
                    };
                    return;
                }
            }
            
            _fontMappings.Add(new LanguageFontMapping
            {
                languageCode = languageCode,
                tmpFont = tmpFont,
                legacyFont = legacyFont
            });
        }

        public void RemoveFontMapping(string languageCode)
        {
            _fontMappings.RemoveAll(m => m.languageCode.Equals(languageCode, StringComparison.OrdinalIgnoreCase));
        }

#endif

        #endregion
    }

    /// <summary>
    /// Serializable hash entry for anti-tamper verification.
    /// </summary>
    [Serializable]
    public struct HashEntry
    {
        public string fileName;
        public string hash;
    }

    /// <summary>
    /// Serializable mapping of language to specific fonts.
    /// </summary>
    [Serializable]
    public struct LanguageFontMapping
    {
        public string languageCode;
        public TMP_FontAsset tmpFont;
        public Font legacyFont;
    }
}
