<div dir="ltr" align=center>

[**Usage**](Usage.md) / [**Keybinds**](Keybinds.md) / [**BLOC Format**](BLOC_FORMAT.md) / [**FAQ**](FAQ.md) / [**How It Works**](HowItWorks.md) / [**MCP**](MCP.md)
</div>

# How It Works

Deep dive into the architecture and implementation details of PicoShot Localization.

---

## Architecture Overview

```
┌────────────────────────────────────────────────────────────┐
│                      Unity Application                     │
├────────────────────────────────────────────────────────────┤
│  UI Layer                   │  Code Layer                  │
│  ┌──────────────────────┐   │  ┌──────────────────────┐    │
│  │ LocalizationText     │   │  │ LocalizationManager  │    │
│  │ Component            │◄──┼──┤                      │    │
│  └──────────────────────┘   │  └──────────┬───────────┘    │
│           │                 │             │                │
│           ▼                 │             ▼                │
│  ┌──────────────────────┐   │  ┌──────────────────────┐    │
│  │ TMP_Text / Dropdown  │   │  │ LanguageDefinitions  │    │
│  │ Text / TextMesh      │   │  │ (Metadata)           │    │
│  └──────────────────────┘   │  └──────────────────────┘    │
└─────────────────────────────┴──────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                        Data Layer                           │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │ LocaleData  │  │ BLOC Files  │  │ LocalizationConfig  │  │
│  │ (Runtime)   │  │ (.bloc)     │  │ (ScriptableObject)  │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

---

## Initialization Flow

### 1. Auto-Initialization

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
private static void AutoInitialize()
```

The system automatically initializes when your game starts, before the first scene loads.

### 2. Language Scanning

```
Initialize()
    └─► ScanAvailableLanguages()
        └─► Scan Locales/ folder for .bloc files
        └─► Read each file header only (language code); content is checked when loaded
        └─► Verify hashes (if anti-tamper enabled)
        └─► Filter by selected languages (if protection enabled)
        └─► Load the default language once (kept as fallback for every language)
```

### 3. Default Language Loading

```
SetLanguage(DetectSystemLanguage())
    └─► Load current language data (reuses the default language if it is the target)
    └─► Use the already-loaded default language as fallback
    └─► Fire OnLanguageChanged event
```

---

## BLOC File Format

### Why Binary?

| Format | Size   | Load Time     | Memory |
| ------ | ------ | ------------- | ------ |
| JSON   | 100%   | Slow (parse)  | High   |
| BLOC   | 30-50% | Fast (binary) | Low    |

BLOC (version 3) uses:

- **Front-coded keys**: sorted key names store only what differs from the previous key
- **Shared texts**: a repeated text is stored once and referenced with one or two bytes
- **Per-file text encoding**: UTF-8, UTF-16 for CJK, or one byte per letter for alphabets like Cyrillic or Arabic
- **Lazy values**: texts stay encoded until first read
- **Checksums**: a damaged or truncated file is refused instead of loaded
- **Optional compression**: Deflate for smaller files

### File Structure

```
┌─────────────────────────────────────────┐
│ Header (48 bytes)                       │
│ - Magic "BLOC", version 3               │
│ - Language code, entry count            │
│ - Key set id, text encoding             │
│ - Header checksum                       │
├─────────────────────────────────────────┤
│ Section directory (20 bytes each)       │
│ - Tag, codec, sizes, checksum           │
├─────────────────────────────────────────┤
│ DATA section (Deflate or stored)        │
│ - ENTRIES: one varint per key           │
│ - LENGTHS: one varint per unique text   │
│ - TEXT:    unique texts                 │
│ - KEYS:    sorted, front-coded names    │
└─────────────────────────────────────────┘
```

See [BLOC_FORMAT.md](BLOC_FORMAT.md) for complete specification.

---

## Text Retrieval Flow

### Simple Text Lookup

```csharp
string text = LocalizationManager.GetText("greeting");
```

```
GetText("greeting")
    └─► Check _currentLanguageData (O(1) hash lookup)
    │   └─► Found? Return value
    │
    └─► Check _fallbackLanguageData (if different)
    │   └─► Found? Return value + fire OnMissingTranslation
    │
    └─► Return key as fallback + fire OnMissingTranslation
```

### With Format Parameters

```csharp
string text = LocalizationManager.GetText("welcome", "Player");
```

```
GetText("welcome", "Player")
    └─► Get raw text: "Welcome, {0}!"
    └─► string.Format("Welcome, {0}!", "Player")
    └─► If RTL: Apply RtlTextHandler.Fix()
    └─► Return: "Welcome, Player!"
```

### Array Lookup

```csharp
string[] options = LocalizationManager.GetArray("menu_items");
```

```
GetArray("menu_items")
    └─► Check _arrayCache first (performance)
    │   └─► Found? Return cached array
    │
    └─► Lookup in _currentLanguageData
    └─► Convert List<string> to string[]
    └─► If RTL: Fix each element
    └─► Cache in _arrayCache
    └─► Return array
```

---

## RTL Text Processing

### Detection

```csharp
bool isRtl = LanguageDefinitions.IsRightToLeft("ar");
// Checks if language code is in RightToLeftLanguages HashSet
```

### Processing Pipeline (Standard RTL)

```
Arabic Text Input
    └─► TashkeelHandler (optional)
    │   └─► Remove diacritical marks
    │
    └─► ArabicLetterConverter & Connector
    │   └─► Apply contextual forms (initial, medial, final)
    │
    └─► Text Reversal
        └─► Reverse character order for display
```

### Token-Based Mixed Text Processing

When **Mixed LTR/RTL Support** is enabled, a smart tokenizer wraps the standard processing pipeline to safely handle texts containing multiple languages:

```
Mixed Text Input: "Hello (مرحبا) World"
    └─► Tokenizer
    │   └─► Token 1: "Hello ("  (LTR)
    │   └─► Token 2: "مرحبا"    (RTL)
    │   └─► Token 3: ") World"  (LTR)
    │
    └─► Neutral Resolution
    │   └─► Punctuation aligns with surrounding languages
    │
    └─► Processing
    │   └─► Process Token 2 via Standard RTL Pipeline (Reshape & Reverse)
    │   └─► Ignore Token 1 & 3
    │
    └─► Bi-Directional Assembly
        └─► LTR Main Language: [Token 1] + [Token 2 Fixed] + [Token 3]
        └─► Output: "Hello (ابحرم) World"
```

### Why This Matters

Arabic letters change shape based on position in a word:

- **Isolated**: ب (standalone)
- **Beginning**: بـ (at start)
- **Middle**: ـبـ (in middle)
- **End**: ـب (at end)

The RTL processor handles this automatically.

---

## Component Binding System

### Automatic Updates

```
LocalizationTextComponent
    └─► OnEnable()
    │   └─► Subscribe to OnLanguageChanged
    │
    └─► OnLanguageChanged event fires
    │   └─► UpdateText() called automatically
    │
    └─► UpdateText()
        └─► Get translation from LocalizationManager
        └─► Apply format parameters
        └─► Apply text processors
        └─► Update TMP_Text.text (or other component)
```

### Text Processors

Processors allow runtime text transformation:

```csharp
// Add processor
component.AddTextProcessor(text => $"<color=green>{text}</color>");

// Chain multiple processors
component.AddTextProcessor(text => text.ToUpper());
component.AddTextProcessor(text => $"[ {text} ]");

// Result: "[ <COLOR=GREEN>HELLO</COLOR> ]"
```

---

## Protection System

### Anti-Tamper Mode

```
Editor (automatic while anti-tamper is enabled):
    └─► After saving, MCP edits, file upgrades and before every build
    └─► Calculate SHA256 hash of each .bloc file
    └─► Store hashes in LocalizationConfig

Runtime:
    └─► ScanAvailableLanguages()
    └─► For each file:
        └─► Calculate actual hash
        └─► Compare with stored hash
        └─► Mismatch? Log error & skip file
    └─► When a language is loaded, the hash is checked again on the exact bytes parsed
    └─► The fallback (default) language goes through the same checks
```

### Selection-Only Mode

```
Runtime:
    └─► Check file name against SelectedLanguages list
    └─► Not in list? Skip file
    └─► This prevents loading unwanted languages
```

---

## Memory Management

### What's Cached

| Data                | Cache Duration                           | Purpose                    |
| ------------------- | ---------------------------------------- | -------------------------- |
| Current Language    | Until changed                            | Active translations        |
| Fallback Language   | Until changed                            | Missing key fallback       |
| Array Results       | Until language changed                   | Avoid repeated conversions |
| Available Languages | Until RefreshAvailableLanguages() called | File list                  |
| All Keys            | Until RefreshAvailableLanguages() called | Key enumeration            |

### What's NOT Cached

- Individual `GetText()` results (fast enough to calculate)
- Processed text results (processors may change)
- Format parameter results (parameters change)

### Cleanup

```csharp
LocalizationManager.Dispose()
    └─► Clear all events
    └─► Clear language data dictionaries
    └─► Clear cache
    └─► Reset initialization flag
```

Called automatically on `Application.quitting`.

---

## Editor Integration

### Language Editor Window

```text
┌─────────────────────────────────────────┐
│ Language Editor                         │
├─────────────────────────────────────────┤
│ [Localization] [Keys] [Components]      │ ← Tab Navigation
│ [Tools] [Settings]                      │
├─────────────────────────────────────────┤
│                                         │
│  Left Panel        │  Right Panel       │
│  ┌──────────────┐  │  ┌──────────────┐  │
│  │ Table Select │  │  │ Key Details  │  │
│  │ Add Key/Value│  │  │ - Translation│  │
│  │ Keys List    │  │  │ - Actions    │  │
│  └──────────────┘  │  └──────────────┘  │
│                                         │
└─────────────────────────────────────────┘
```

#### Smart Workflows

- **Key Views:** Keys can be organized into views using dot notation (e.g. `UI.MainMenu.Start`) or the legacy underscore delimiter. The editor allows filtering, editing, and exporting/importing specific views in JSON format seamlessly.
- **Inline Initialization:** Creating a new key allows for immediate assignment of the default language value, streamlining the data entry process for both string and array keys.
- **Font System:** The Localization tab includes a Fonts sub-tab for assigning primary and language-specific fallback fonts (supporting both TextMeshPro and Legacy fonts). The `LocalizationManager` automatically handles switching fonts on all components via the `OnFontChanged` event when the language changes.

### Build Processor

```csharp
public class LocalesBuildProcessor : BuildPlayerProcessor, IPostprocessBuildWithReport
```

- **Prepare**: Refresh anti-tamper hashes; for StreamingAssets platforms (including macOS), add the `.bloc` files to StreamingAssets without copying them into `Assets/`
- **Postprocess**: Windows and Linux players get a `Locales` folder next to the executable

---

## Performance Tips

### Do

- Use `GetArray()` for dropdowns (cached)
- Use format parameters instead of string concatenation
- Subscribe to `OnLanguageChanged` for UI updates
- Call `Dispose()` when done (for testing)

### Don't

- Call `GetText()` every frame in `Update()` (cache the result)
- Load all languages at once (only current + fallback needed)
- Use extremely long translation keys (affects memory)
