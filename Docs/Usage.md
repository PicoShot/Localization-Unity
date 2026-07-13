<div dir="ltr" align=center>
    
 [**Usage**](Usage.md) / [**Keybinds**](Keybinds.md) / [**BLOC Format**](BLOC_FORMAT.md) / [**FAQ**](FAQ.md) / [**How It Works**](HowItWorks.md)

</div>

# Usage Guide

Complete API documentation for PicoShot Localization system.

## Table of Contents

- [Initialization](#initialization)
- [Getting System Language](#getting-system-language)
- [Enumerating Available Languages](#enumerating-available-languages)
- [Getting Native Language Names](#getting-native-language-names)
- [Setting Language](#setting-language)
- [Text Retrieval](#text-retrieval)
  - [Parameter Key Resolution](#parameter-key-resolution-new)
  - [Extension Methods](#extension-methods-new)
- [Bind Functions (Components)](#bind-functions-components)
  - [Quick Bind Methods](#quick-bind-methods)
  - [Manual Component Usage](#manual-component-usage)
- [Events](#events)
- [Advanced Usage](#advanced-usage)

---

## Initialization

The localization system initializes automatically on startup. No manual initialization is required.

```csharp
// Check if initialized
if (LocalizationManager.IsInitialized)
{
    // System is ready
}

// Manual initialization (optional, system auto initialize itself)
LocalizationManager.Initialize();
```

---

## Getting System Language

Detect the user's system language and convert it to a supported language code.

```csharp
// Get system language as ISO code (e.g., "en", "es", "ja")
string systemLanguage = LocalizationManager.DetectSystemLanguage();

// Direct conversion from Unity's SystemLanguage
string langCode = LanguageDefinitions.FromSystemLanguage(Application.systemLanguage);
```

### Language Code Examples

| System Language      | Code      |
| -------------------- | --------- |
| English              | `en`      |
| Spanish              | `es`      |
| French               | `fr`      |
| German               | `de`      |
| Japanese             | `ja`      |
| Chinese (Simplified) | `zh-hans` |
| Arabic               | `ar`      |

---

## Enumerating Available Languages

Get all languages that have translation files available at runtime.

```csharp
// Get language codes only
IEnumerable<string> codes = LocalizationManager.GetAvailableLanguageCodes();
foreach (string code in codes)
{
    Debug.Log($"Available: {code}");
}

// Get display names (English names)
IEnumerable<string> names = LocalizationManager.GetAvailableLanguages();
foreach (string name in names)
{
    Debug.Log($"Available: {name}");  // e.g., "English", "Spanish"
}

// Get display names with native names
IEnumerable<string> namesWithNative = LocalizationManager.GetAvailableLanguages(withNativeNames: true);
foreach (string name in namesWithNative)
{
    Debug.Log($"Available: {name}");  // e.g., "English (English)", "Spanish (español)"
}
```

### Check Language Availability

```csharp
// Check if a specific language is available
bool hasSpanish = LocalizationManager.IsLanguageAvailable("es");
bool hasJapanese = LocalizationManager.IsLanguageAvailable("ja");
```

---

## Getting Native Language Names

Display language names in their native form (e.g., "日本語" for Japanese).

```csharp
// Get display name in English (default)
string englishName = LocalizationManager.GetLanguageDisplayName("ja");     // "Japanese"
string spanishName = LocalizationManager.GetLanguageDisplayName("es");     // "Spanish"

// Get native display name
string nativeJapanese = LocalizationManager.GetLanguageDisplayName("ja", native: true);  // "日本語"
string nativeSpanish = LocalizationManager.GetLanguageDisplayName("es", native: true);   // "español"
```

### Get Language Code from Name

```csharp
// Convert display name back to code
string code = LocalizationManager.GetLanguageCode("Japanese");           // "ja"
string codeNative = LocalizationManager.GetLanguageCode("日本語", nativeName: true);  // "ja"
```

### Supported Language Properties

```csharp
// Check if language is RTL (Right-to-Left)
bool isRtl = LanguageDefinitions.IsRightToLeft("ar");  // true for Arabic
bool isRtl = LanguageDefinitions.IsRightToLeft("he");  // true for Hebrew

// Get fallback language
string fallback = LanguageDefinitions.GetFallbackLanguage("en-US");  // "en"
```

---

## Setting Language

Change the current language at runtime. All bound text components will update automatically.

```csharp
// Set language by code
LocalizationManager.SetLanguage("es");  // Switch to Spanish
LocalizationManager.SetLanguage("ja");  // Switch to Japanese

// Set with fallback option (default: true)
// If the requested language isn't available, it will try fallback or use default
LocalizationManager.SetLanguage("en-GB", useFallback: true);
```

### Current Language Info

```csharp
// Get current language code
string current = LocalizationManager.CurrentLanguage;  // e.g., "en"

// Check if current language is RTL
bool isRtl = LocalizationManager.IsRightToLeft;  // true for Arabic, Hebrew, etc.

// Get default language from config
string defaultLang = LocalizationManager.DefaultLanguage;
```

---

## Text Retrieval

### Get Simple Text

```csharp
// Basic translation
string text = LocalizationManager.GetText("greeting");

// With format parameters
string welcome = LocalizationManager.GetText("welcome", "Player");
string stats = LocalizationManager.GetText("stats", "100", "50");

// Translation file example:
// "welcome": "Welcome, {0}!"
// "stats": "Health: {0}, Mana: {1}"
```

### Parameter Key Resolution

The system supports resolving parameters as localization keys using the `Key` struct.

```csharp
// Localization data:
// "welcome": "Welcome to {0}"
// "game_name": "My Game Name"

// All of these output: "Welcome to My Game Name"

// 1. Pass literal string (no resolution)
LocalizationManager.GetText("welcome", "My Game Name");

// 2. Cast string to Key (resolves as localization key)
LocalizationManager.GetText("welcome", (Key)"game_name");

// 3. Use Key.From() method
LocalizationManager.GetText("welcome", Key.From("game_name"));

// 4. Implicit conversion from string
Key gameKey = "game_name";
LocalizationManager.GetText("welcome", gameKey);

// 5. All parameters resolved as keys (convenience method)
LocalizationManager.GetTextWithParamKeys("welcome", "game_name");

// 6. Mixed parameters - some literal, some keys
// "player_welcome": "Welcome {0}, you have {1} new messages"
// "player_name": "Hero"
LocalizationManager.GetText("player_welcome", (Key)"player_name", "5");
// Output: "Welcome Hero, you have 5 new messages"
```

### Extension Methods

Cleaner syntax using string extensions:

```csharp
// String extension for GetText
"welcome".Localized("My Game Name");           // literal
"welcome".Localized((Key)"game_name");         // key resolved

// Array extensions
string[] options = "graphics".LocalizedArray();           // GetArray
string quality = "graphics".LocalizedArrayElement(0);     // GetArrayText
```

### Get Array Text

For translations stored as arrays (e.g., dropdown options).

```csharp
// Get entire array
string[] options = LocalizationManager.GetArray("difficulty_options");
// Returns: ["Easy", "Normal", "Hard", "Expert"]

// Get specific element
string difficulty = LocalizationManager.GetArrayText("difficulty_options", 2);  // "Hard"

// Get with bounds checking (returns placeholder if out of range)
string invalid = LocalizationManager.GetArrayText("difficulty_options", 10);  // "[difficulty_options:10]"

// Using Key struct (supports implicit conversion)
string[] graphics = LocalizationManager.GetArray((Key)"graphics_options");
string quality = LocalizationManager.GetArrayText(Key.From("graphics_options"), 0);

// Extension methods
string[] difficulties = "difficulty_options".LocalizedArray();
string easy = "difficulty_options".LocalizedArrayElement(0);
```

### Check Key Existence

```csharp
// Check if key exists in current language
bool hasKey = LocalizationManager.HasKey("greeting");

// Check if key exists in default language
bool hasInDefault = LocalizationManager.HasKeyInDefault("greeting");

// Get all available keys
IEnumerable<string> allKeys = LocalizationManager.GetAllKeys();
```

---

## Bind Functions (Components)

The `LocalizationTextComponent` automatically binds text components to the localization system.

### Quick Bind Methods

The easiest way to bind text components is using the `BindText` methods on `LocalizationManager`:

#### TMP_Text

```csharp
// Simple binding
LocalizationManager.BindText(myTextComponent, "greeting");

// With format parameters
LocalizationManager.BindText(myTextComponent, "welcome_message", args: "Player");
LocalizationManager.BindText(myTextComponent, "stats", args: new object[] { 100, 50 });

// With array index (for array-type translations)
LocalizationManager.BindText(myTextComponent, "difficulty_options", arrayIndex: 2);

// With text processor
LocalizationManager.BindText(myTextComponent, "greeting", textProcessor: text => text.ToUpper());

// Full example
LocalizationManager.BindText(
    myTextComponent,
    "welcome_message",
    arrayIndex: -1,
    textProcessor: text => $"<color=green>{text}</color>",
    args: "Player"
);
```

#### TMP_Dropdown

```csharp
// Bind dropdown to array translation
LocalizationManager.BindText(myDropdown, "difficulty_options");

// Limit number of options
LocalizationManager.BindText(myDropdown, "difficulty_options", arrayMaxSize: 5);

// With text processor applied to each option
LocalizationManager.BindText(
    myDropdown,
    "menu_items",
    arrayMaxSize: 10,
    textProcessor: text => text.ToUpper()
);
```

#### Legacy Text (UnityEngine.UI.Text)

```csharp
// Same API as TMP_Text
LocalizationManager.BindText(legacyText, "greeting");
LocalizationManager.BindText(legacyText, "welcome", args: "Player");
LocalizationManager.BindText(legacyText, "stats", arrayIndex: -1, args: new object[] { 100, 50 });
```

#### Legacy Dropdown (UnityEngine.UI.Dropdown)

```csharp
// Same API as TMP_Dropdown
LocalizationManager.BindText(legacyDropdown, "difficulty_options");
LocalizationManager.BindText(legacyDropdown, "menu_items", arrayMaxSize: 5);
```

#### TextMesh (3D Text)

```csharp
// Same API as TMP_Text
LocalizationManager.BindText(textMesh, "greeting");
LocalizationManager.BindText(textMesh, "welcome", args: "Player");
LocalizationManager.BindText(textMesh, "title", arrayIndex: 0);
```

### Manual Component Usage

For more control, you can work with `LocalizationTextComponent` directly.

#### Inspector Integration
The Unity Inspector for `LocalizationTextComponent` provides a smart interface:
- **Table & Key Dropdowns:** Instead of manually typing keys, you can select the target table and key from searchable dropdown menus to prevent typos.
- **Context Menu:** Right-click the component and select `Switch language` to dynamically cycle through available languages and instantly preview the text directly in Edit Mode.

### Properties

```csharp
// Set translation key
component.TranslationKey = "welcome_message";

// For array values: get specific index (-1 for string value)
component.ArrayIndex = 2;  // Get 3rd element from array

// For dropdowns: limit number of options
component.ArraySizeLimit = 5;  // Show max 5 options

// Format parameters
component.FormatParameters = new[] { "Player", "100" };
// Or use the method:
component.SetFormatParameters("Player", "100");

// Set individual parameter
component.SetFormatParameter(0, "New Name");
```

### Methods

```csharp
// Force text update
component.UpdateText();

// Force complete refresh (re-initializes components)
component.ForceRefresh();

// Add text processor (modifies text before display)
component.AddTextProcessor(text => text.ToUpper());
component.AddTextProcessor(text => $"[ {text} ]");

// Remove processor
component.RemoveTextProcessor(myProcessor);

// Clear all processors
component.ClearTextProcessors();
```

### Text Processors

Text processors allow you to modify the translated text before it's displayed.

```csharp
// Example: Add color tags
component.AddTextProcessor(text => $"<color=green>{text}</color>");

// Example: Truncate long text
component.AddTextProcessor(text =>
    text.Length > 20 ? text.Substring(0, 17) + "..." : text);

// Example: Add prefix based on language
component.AddTextProcessor(text =>
    LocalizationManager.CurrentLanguage == "ja" ? $"〞{text}】" : text);
```

### Events

```csharp
// Subscribe to text update event
component.onTextUpdated.AddListener(updatedText =>
{
    Debug.Log($"Text updated to: {updatedText}");
});
```

### Component Type

```csharp
// Check component type
TextComponentType type = component.ComponentType;
// Values: TMPText, TMPDropdown, LegacyText, LegacyDropdown, TextMesh, None

// Check if attached to dropdown
bool isDropdown = component.IsDropdown;

// Get current displayed text
string currentText = component.CurrentText;
```

---

## Events

Subscribe to localization events to react to language changes.

### OnLanguageChanged

Triggered when the language is changed. All localized components update automatically.

```csharp
// Subscribe
LocalizationManager.OnLanguageChanged += OnLanguageChanged;

// Handler
void OnLanguageChanged()
{
    Debug.Log($"Language changed to: {LocalizationManager.CurrentLanguage}");
    // Update UI, reload textures, etc.
}

// Unsubscribe
LocalizationManager.OnLanguageChanged -= OnLanguageChanged;
```

### OnLanguageLoadError

Triggered when there's an error loading a language file.

```csharp
LocalizationManager.OnLanguageLoadError += OnLanguageError;

void OnLanguageError(string errorMessage)
{
    Debug.LogError($"Language load error: {errorMessage}");
}
```

### OnMissingTranslation

Triggered when a translation key is not found.

```csharp
LocalizationManager.OnMissingTranslation += OnMissingKey;

void OnMissingKey(string key)
{
    Debug.LogWarning($"Missing translation: {key}");
}
```

---

## Advanced Usage

### Manual File Operations (Editor Only)

```csharp
#if UNITY_EDITOR
// Save locale data
LocalizationManager.SaveLocaleToFile(path, localeData, compress: true);

// Load locale data
LocaleData data = LocalizationManager.LoadLocaleFromFile(path);

// Get file path for language
string filePath = LocalizationManager.GetLanguageFilePath("en");

// Refresh available languages
LocalizationManager.RefreshAvailableLanguages();
#endif
```

### Configuration Properties

```csharp
// Get languages directory path
string path = LocalizationManager.LanguagesPath;

// Check anti-tamper status
bool antiTamper = LocalizationManager.IsAntiTamperEnabled;

// Get selected languages (when protection is enabled)
IReadOnlyList<string> selected = LocalizationManager.SelectedLanguages;
```

### Cleanup

```csharp
// Dispose resources (called automatically on application quit)
LocalizationManager.Dispose();
```

---

## Examples

### Language Selection Dropdown

```csharp
public class LanguageSelector : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown dropdown;

    void Start()
    {
        // Get available languages
        var languages = LocalizationManager.GetAvailableLanguages().ToList();
        var codes = LocalizationManager.GetAvailableLanguageCodes().ToList();

        // Populate dropdown
        dropdown.ClearOptions();
        dropdown.AddOptions(languages);

        // Set current selection
        dropdown.value = codes.IndexOf(LocalizationManager.CurrentLanguage);

        // Handle selection
        dropdown.onValueChanged.AddListener(index =>
        {
            LocalizationManager.SetLanguage(codes[index]);
        });
    }
}
```

### Dynamic Text Update

```csharp
public class ScoreDisplay : MonoBehaviour
{
    [SerializeField] private LocalizationTextComponent localizedText;

    void Update()
    {
        // Update score display with current values
        localizedText.SetFormatParameters(
            ScoreManager.CurrentScore.ToString(),
            ScoreManager.HighScore.ToString()
        );
    }
}
```

### Using BindText Methods

```csharp
public class MainMenuBinder : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text playButtonText;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Dropdown difficultyDropdown;
    [SerializeField] private TextMesh versionText3D;

    void Start()
    {
        // Simple bindings
        LocalizationManager.BindText(titleText, "game_title");
        LocalizationManager.BindText(playButtonText, "play_button");
        LocalizationManager.BindText(versionText3D, "version_info", args: "1.0.0");

        // Score with format parameters
        UpdateScore(0, 0);

        // Dropdown with difficulty options
        LocalizationManager.BindText(difficultyDropdown, "difficulty_options", arrayMaxSize: 4);

        // Subscribe to score changes
        ScoreManager.OnScoreChanged += UpdateScore;
    }

    void UpdateScore(int current, int high)
    {
        LocalizationManager.BindText(
            scoreText,
            "score_display",
            args: new object[] { current, high }
        );
    }
}
```

### BindText with Text Processor

```csharp
public class StyledTextBinder : MonoBehaviour
{
    [SerializeField] private TMP_Text headerText;
    [SerializeField] private TMP_Text warningText;

    void Start()
    {
        // Header with color styling
        LocalizationManager.BindText(
            headerText,
            "level_header",
            textProcessor: text => $"<size=32><b>{text}</b></size>"
        );

        // Warning with color and icon
        LocalizationManager.BindText(
            warningText,
            "warning_message",
            textProcessor: text => $"<color=red>⚠ {text}</color>"
        );
    }
}
```

### RTL Layout Adjustment

```csharp
public class RtlLayoutHandler : MonoBehaviour
{
    [SerializeField] private RectTransform contentPanel;

    void Start()
    {
        LocalizationManager.OnLanguageChanged += AdjustLayout;
        AdjustLayout();
    }

    void AdjustLayout()
    {
        // Adjust anchor/pivot for RTL languages
        if (LocalizationManager.IsRightToLeft)
        {
            contentPanel.anchorMin = new Vector2(1, 0);
            contentPanel.anchorMax = new Vector2(1, 1);
            contentPanel.pivot = new Vector2(1, 0.5f);
        }
        else
        {
            contentPanel.anchorMin = new Vector2(0, 0);
            contentPanel.anchorMax = new Vector2(0, 1);
            contentPanel.pivot = new Vector2(0, 0.5f);
        }
    }
}
```

### Using Key Resolution for Dynamic Content

```csharp
public class GameMessageSystem : MonoBehaviour
{
    [SerializeField] private TMP_Text welcomeText;
    [SerializeField] private TMP_Text equipText;
    [SerializeField] private TMP_Dropdown graphicsDropdown;

    void Start()
    {
        // Localization data:
        // "welcome": "Welcome to {0}!"
        // "game_name": "Epic Adventure"
        // "player_equip": "{0} equipped {1}"
        // "player_name": "Hero"
        // "weapon_sword": "Iron Sword"
        // "graphics_options": ["Low", "Medium", "High", "Ultra"]

        // Welcome with resolved game name
        welcomeText.text = "welcome".Localized((Key)"game_name");
        // Output: "Welcome to Epic Adventure!"

        // Mixed parameters - player name as key, weapon as literal
        equipText.text = LocalizationManager.GetText(
            "player_equip",
            (Key)"player_name",  // Resolved: "Hero"
            "Wooden Staff"       // Literal
        );
        // Output: "Hero equipped Wooden Staff"

        // Using convenience method when all params are keys
        equipText.text = LocalizationManager.GetTextWithParamKeys(
            "player_equip",
            "player_name",   // Both resolved as keys
            "weapon_sword"
        );
        // Output: "Hero equipped Iron Sword"

        // Populate dropdown using array extension
        string[] options = "graphics_options".LocalizedArray();
        graphicsDropdown.ClearOptions();
        graphicsDropdown.AddOptions(options.ToList());
    }

    void ShowDynamicMessage(string messageKey, params object[] args)
    {
        // Pass mixed args directly - Key objects resolved, others treated as literals
        string message = LocalizationManager.GetText(messageKey, args);
        Debug.Log(message);
    }

    void ExampleUsage()
    {
        // Can mix types in the array
        ShowDynamicMessage(
            "reward_message",
            (Key)"player_name",     // Resolved as key
            100,                    // Converted to string "100"
            "Gold"                  // Literal string
        );
    }
}
```

### Using TextNodes
TextNode is a node-based text system designed for localization and networking.

Instead of storing text as a plain string, messages are built from composable nodes:
- Plain text
- Localized entries
- Rich text modifiers
- Nested formatted arguments

This allows text to be transferred over the network while preserving localization data.
When received, the message is automatically rebuilt using the target player's selected language.

> If your project uses Netcode, add `NETCODE` to your Scripting Define Symbols.
```csharp
[InitializeOnLoad]
public static class TestTextNode
{
    static TestTextNode()
    { 
        // Create plain text nodes
        TextNode testArg0 = TextNode.Text("Player001");
        TextNode testArg1 = "Enemy"; // Implicit conversion from string is also supported

        // Apply rich text modifiers
        testArg0 = testArg0.BoldModifier();
        testArg1 = testArg1.ColorModifier(Color.red);

        // Create a localized text node
        // "test_text" (en): "{0} is {1}"
        TextNode localized = TextNode.Localized("test_text", testArg0, testArg1);

        // Create a formatted text node
        TextNode formatted = TextNode.Formatted(
            "{0}: {1}",
            "Kill".ColorModifier(Color.yellow),
            localized
        );

        Debug.Log(formatted);

        // Output:
        // <color=#ffeb04ff>Kill</color>: <b>Player001</b> is <color=#ff0000ff>Enemy</color>
    }
}
```

---

## RTL and Mixed Text Support

PicoShot Localization natively supports Right-to-Left (RTL) languages like Arabic, Persian, and Hebrew. Text reshaping and reversal are automatically handled when the current system language is marked as RTL.

### Mixed LTR/RTL Text Handling

When working with dynamic strings that contain both Arabic/Persian words and English words (or vice versa), you can enable the **Mixed LTR/RTL Support** feature in the `Tools > Localization > Language Editor > Settings` tab.

When enabled, the `RtlTextHandler` uses a smart token-based algorithm to:
1. Parse the string into contiguous LTR, RTL, and Neutral blocks (e.g., punctuation and spaces).
2. Apply reshaping and reversal **only** to the Arabic/Persian blocks.
3. Automatically resolve the alignment of punctuation marks depending on their surrounding text.
4. Render the entire string in the correct visual order based on the main language context (left-to-right for English, right-to-left for Arabic/Persian).

**Example in LTR Context (English):**
- Logical Input: `Hello (مرحبا) World`
- Rendered Output: `Hello (ابحرم) World`
*(The Arabic word is properly reshaped and connected inside the parentheses without breaking the English sentence structure)*

**Example in RTL Context (Persian):**
- Logical Input: `قیمت 500 تومان`
- Rendered Output: `ناموت 500 تمیق`
*(The Persian words are reshaped and placed in correct RTL flow, while the English numbers remain LTR)*