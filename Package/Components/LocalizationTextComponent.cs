using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PicoShot.Localization
{
    /// <summary>
    /// Component that binds text components (TMP_Text, TMP_Dropdown, Text, Dropdown, TextMesh) to the localization system.
    /// Automatically updates when language changes.
    /// </summary>
    [AddComponentMenu("UI/Localized Text")]
    [DisallowMultipleComponent]
    public class LocalizationTextComponent : MonoBehaviour
    {
        #region Inspector Fields

        [Header("Localization")]
        [Tooltip("The translation key to look up")]
        [SerializeField] private string translationKey;

        [Tooltip("For array values: -1 = use first element as string, >= 0 = specific array index")]
        [SerializeField] private int arrayIndex = -1;

        [Tooltip("For dropdowns: maximum number of array elements to use (0 = unlimited)")]
        [SerializeField] private int arraySizeLimit;

        [Header("Formatting")]
        [Tooltip("Optional format parameters. Use {0}, {1}, etc. in translation text")]
        [SerializeField] private string[] formatParameters = Array.Empty<string>();

        [Header("Events")]
        [Tooltip("Called when the text is updated")]
        [SerializeField] private UnityEvent<string> onTextUpdated;

        #endregion

        #region Properties

        /// <summary>
        /// The translation key used for lookup.
        /// </summary>
        public string TranslationKey
        {
            get => translationKey;
            set
            {
                if (translationKey == value) return;
                translationKey = value;
                if (Application.isPlaying)
                    UpdateText();
            }
        }

        /// <summary>
        /// Array index for array-type translations. -1 for string values.
        /// </summary>
        public int ArrayIndex
        {
            get => arrayIndex;
            set
            {
                if (arrayIndex == value) return;
                arrayIndex = value;
                if (Application.isPlaying)
                    UpdateText();
            }
        }

        /// <summary>
        /// Maximum number of options for dropdown components.
        /// </summary>
        public int ArraySizeLimit
        {
            get => arraySizeLimit;
            set
            {
                if (arraySizeLimit == value) return;
                arraySizeLimit = value;
                if (Application.isPlaying)
                    UpdateText();
            }
        }

        /// <summary>
        /// Format parameters for string.Format().
        /// </summary>
        public string[] FormatParameters
        {
            get => formatParameters;
            set
            {
                formatParameters = value ?? Array.Empty<string>();
                if (Application.isPlaying)
                    UpdateText();
            }
        }

        /// <summary>
        /// Returns the type of text component attached.
        /// </summary>
        public TextComponentType ComponentType
        {
            get
            {
                if (_tmpDropdown != null) return TextComponentType.TMPDropdown;
                if (_tmpText != null) return TextComponentType.TMPText;
                if (_legacyDropdown != null) return TextComponentType.LegacyDropdown;
                if (_legacyText != null) return TextComponentType.LegacyText;
                if (_textMesh != null) return TextComponentType.TextMesh;
                return TextComponentType.None;
            }
        }

        /// <summary>
        /// Checks if this component is attached to a dropdown.
        /// </summary>
        public bool IsDropdown => _tmpDropdown != null || _legacyDropdown != null;

        /// <summary>
        /// Gets the current displayed text.
        /// </summary>
        public string CurrentText => _lastText;

        #endregion

        #region Private Fields

        private TMP_Text _tmpText;
        private TMP_Dropdown _tmpDropdown;
        private Text _legacyText;
        private Dropdown _legacyDropdown;
        private TextMesh _textMesh;

        private string _lastText;
        private bool _isInitialized;
        private readonly List<Func<string, string>> _textProcessors = new();

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            LocalizationManager.OnLanguageChanged += UpdateText;
            LocalizationManager.OnFontChanged += UpdateFont;
            if (_isInitialized && Application.isPlaying)
            {
                UpdateText();
            }

            // Apply the current font immediately in case the font-changed event was fired before this component subscribed.
            LocalizationManager.GetCurrentFonts(out var tmpFont, out var legacyFont);
            UpdateFont(tmpFont, legacyFont);
        }

        private void OnDisable()
        {
            LocalizationManager.OnLanguageChanged -= UpdateText;
            LocalizationManager.OnFontChanged -= UpdateFont;
        }

        private void OnDestroy()
        {
            LocalizationManager.OnLanguageChanged -= UpdateText;
            LocalizationManager.OnFontChanged -= UpdateFont;
            _textProcessors.Clear();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying) return;
            if (!_isInitialized) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (!Application.isPlaying) return;
                if (!_isInitialized) return;
                if (this != null) UpdateText();
            };
        }
#endif

        #endregion

        #region Initialization

        private void Initialize()
        {
            if (_isInitialized) return;

            _tmpText = GetComponent<TMP_Text>();
            _tmpDropdown = GetComponent<TMP_Dropdown>();
            _legacyText = GetComponent<Text>();
            _legacyDropdown = GetComponent<Dropdown>();
            _textMesh = GetComponent<TextMesh>();

            if (_tmpText == null && _tmpDropdown == null &&
                _legacyText == null && _legacyDropdown == null &&
                _textMesh == null)
            {
                Debug.LogError($"[LocalizationTextComponent] No supported text component found on {gameObject.name}. " +
                    "Supported: TMP_Text, TMP_Dropdown, Text (Legacy), Dropdown (Legacy), TextMesh", this);
            }

            _isInitialized = true;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Updates the text component with the current translation.
        /// </summary>
        public void UpdateText()
        {
            if (!isActiveAndEnabled) return;
            if (string.IsNullOrEmpty(translationKey)) return;
            if (!_isInitialized)
            {
                Initialize();
            }

            try
            {
                if (IsDropdown)
                {
                    UpdateDropdown();
                }
                else
                {
                    UpdateTextComponent();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalizationTextComponent] Error updating text on {gameObject.name}: {ex.Message}", this);
            }
        }

        /// <summary>
        /// Updates the font based on the language configuration.
        /// </summary>
        private void UpdateFont(TMP_FontAsset tmpFont, Font legacyFont)
        {
            if (!isActiveAndEnabled) return;
            if (!_isInitialized)
            {
                Initialize();
            }

            if (tmpFont != null)
            {
                if (_tmpText != null) _tmpText.font = tmpFont;
                if (_tmpDropdown != null)
                {
                    if (_tmpDropdown.captionText != null) _tmpDropdown.captionText.font = tmpFont;
                    if (_tmpDropdown.itemText != null) _tmpDropdown.itemText.font = tmpFont;
                }
            }

            if (legacyFont != null)
            {
                if (_legacyText != null) _legacyText.font = legacyFont;
                if (_legacyDropdown != null)
                {
                    if (_legacyDropdown.captionText != null) _legacyDropdown.captionText.font = legacyFont;
                    if (_legacyDropdown.itemText != null) _legacyDropdown.itemText.font = legacyFont;
                }
                if (_textMesh != null) _textMesh.font = legacyFont;
            }
        }

        /// <summary>
        /// Sets format parameters and updates the text.
        /// </summary>
        public void SetFormatParameters(params string[] parameters)
        {
            FormatParameters = parameters;
        }

        /// <summary>
        /// Sets a single format parameter at the specified index.
        /// </summary>
        public void SetFormatParameter(int index, string value)
        {
            if (formatParameters == null)
            {
                formatParameters = new string[index + 1];
            }
            else if (index >= formatParameters.Length)
            {
                Array.Resize(ref formatParameters, index + 1);
            }

            if (formatParameters[index] != value)
            {
                formatParameters[index] = value;
                UpdateText();
            }
        }

        /// <summary>
        /// Adds a text processor that can modify the text before display.
        /// </summary>
        public void AddTextProcessor(Func<string, string> processor)
        {
            if (processor != null && !_textProcessors.Contains(processor))
            {
                _textProcessors.Add(processor);
                UpdateText();
            }
        }

        /// <summary>
        /// Removes a text processor.
        /// </summary>
        public void RemoveTextProcessor(Func<string, string> processor)
        {
            if (_textProcessors.Remove(processor))
            {
                UpdateText();
            }
        }

        /// <summary>
        /// Clears all text processors.
        /// </summary>
        public void ClearTextProcessors()
        {
            _textProcessors.Clear();
            UpdateText();
        }

        /// <summary>
        /// Forces a refresh of the text.
        /// </summary>
        public void ForceRefresh()
        {
            _lastText = null;
            Initialize();
            UpdateText();
        }

        #endregion

        #region Private Methods

        private void UpdateTextComponent()
        {
            string text = GetTranslatedText();
            text = ApplyProcessors(text);

            if (_lastText == text) return;

            if (_tmpText != null)
            {
                _tmpText.text = text;
            }
            else if (_legacyText != null)
            {
                _legacyText.text = text;
            }
            else if (_textMesh != null)
            {
                _textMesh.text = text;
            }

            _lastText = text;
            onTextUpdated?.Invoke(text);
        }

        private void UpdateDropdown()
        {
            var options = LocalizationManager.GetArray(translationKey);
            if (options == null || options.Length == 0)
            {
                Debug.LogWarning($"[LocalizationTextComponent] No array data found for key '{translationKey}'", this);
                return;
            }

            // Filter out empty options
            options = options.Where(opt => !string.IsNullOrEmpty(opt)).ToArray();

            // Apply size limit
            if (arraySizeLimit > 0 && options.Length > arraySizeLimit)
            {
                options = options.Take(arraySizeLimit).ToArray();
            }

            // Apply text processors
            for (int i = 0; i < options.Length; i++)
            {
                options[i] = ApplyProcessors(options[i]);
            }

            if (_tmpDropdown != null)
            {
                UpdateTMPDropdown(_tmpDropdown, options);
            }
            else if (_legacyDropdown != null)
            {
                UpdateLegacyDropdown(_legacyDropdown, options);
            }
        }

        private static void UpdateTMPDropdown(TMP_Dropdown dropdown, string[] options)
        {
            int selectedValue = dropdown.value;
            dropdown.ClearOptions();
            dropdown.AddOptions(options.ToList());

            // Restore selection if valid, otherwise select first
            if (selectedValue < options.Length)
            {
                dropdown.value = selectedValue;
            }
            else if (options.Length > 0)
            {
                dropdown.value = 0;
            }

            dropdown.RefreshShownValue();
        }

        private static void UpdateLegacyDropdown(Dropdown dropdown, string[] options)
        {
            int selectedValue = dropdown.value;
            dropdown.ClearOptions();
            dropdown.AddOptions(options.ToList());

            if (selectedValue < options.Length)
            {
                dropdown.value = selectedValue;
            }
            else if (options.Length > 0)
            {
                dropdown.value = 0;
            }

            dropdown.RefreshShownValue();
        }

        private string GetTranslatedText()
        {
            if (arrayIndex >= 0)
            {
                return LocalizationManager.GetArrayText(translationKey, arrayIndex);
            }

            if (formatParameters != null && formatParameters.Length > 0)
            {
                return LocalizationManager.GetText(translationKey, formatParameters);
            }

            return LocalizationManager.GetText(translationKey);
        }

        private string ApplyProcessors(string text)
        {
            if (_textProcessors.Count == 0) return text;

            foreach (var processor in _textProcessors)
            {
                if (processor != null)
                {
                    text = processor(text);
                }
            }
            return text;
        }

        #endregion

        [ContextMenu("Switch language")]
        private void ChangeLanguage()
        {
            var languages = LocalizationManager.GetAvailableLanguageCodes().OrderBy(l => l).ToList();
            if (languages.Count == 0) return;

            LocalizationManager.Initialize();

            string current = LocalizationManager.CurrentLanguage;
            int currentIndex = languages.FindIndex(l => string.Equals(l, current, System.StringComparison.OrdinalIgnoreCase));
            
            int nextIndex = (currentIndex + 1) % languages.Count;
            LocalizationManager.SetLanguage(languages[nextIndex]);
            
            if (!Application.isPlaying)
            {
                UpdateText();
            }
        }
    }

    /// <summary>
    /// Enum representing the type of text component.
    /// </summary>
    public enum TextComponentType
    {
        None,
        TMPText,
        TMPDropdown,
        LegacyText,
        LegacyDropdown,
        TextMesh
    }
}
