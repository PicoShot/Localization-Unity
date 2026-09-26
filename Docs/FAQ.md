<div dir="ltr" align=center>

[**Usage**](Usage.md) / [**Keybinds**](Keybinds.md) / [**BLOC Format**](BLOC_FORMAT.md) / [**FAQ**](FAQ.md) / [**How It Works**](HowItWorks.md) / [**MCP**](MCP.md)

</div>

# Frequently Asked Questions (FAQ)

Common questions and answers about PicoShot Localization system.

---

## General Questions

### What is PicoShot Localization?

PicoShot Localization is a lightweight, high-performance localization system for Unity games. It uses a custom binary format (BLOC) for fast loading and supports 100+ languages with automatic RTL (Right-to-Left) text handling.

### Is it free to use?

Yes! The package is open-source and licensed under MIT License. You can use it in both personal and commercial projects.

### What Unity versions are supported?

Unity 2022.3 (LTS) and higher.

---

## Installation & Setup

### How do I install the package?

Add via Unity Package Manager:

```
https://github.com/PicoShot/Localization-Unity.git?path=/Package
```

### Where are language files stored?

Language files (`.bloc`) are stored in the `Locales` folder at your project root (next to Assets folder). The system creates this automatically.

### Do I need TextMeshPro?

Yes, TextMeshPro is required for the UI components. It's included with Unity by default.

---

## Usage Questions

### How do I add a new language?

1. Open Language Editor (`Tools > Localization > Language Editor`)
2. Go to the "Languages" tab
3. Check the checkbox next to your desired language
4. Click Save

### How do I translate my text?

**Option 1: Manual Translation**

1. Open Language Editor
2. Create keys in the "Keys" tab
3. Add translations for each language

**Option 2: DeepL/Gemini Translation**

1. Get a DeepL/Gemini API key
2. Enter it in Settings tab
3. Select a key and press `Ctrl + T`

### Can I use format parameters?

Yes! Use `{0}`, `{1}`, etc. in your translation text:

```csharp
// Translation: "Hello, {0}! You have {1} coins."
LocalizationManager.GetText("welcome", "Player", "100");
// Result: "Hello, Player! You have 100 coins."
```

### How do I detect the user's system language?

```csharp
string systemLang = LocalizationManager.DetectSystemLanguage();
LocalizationManager.SetLanguage(systemLang);
```

---

## RTL (Right-to-Left) Questions

### Which languages are supported for RTL?

- Arabic (`ar`)
- Hebrew (`he`)
- Persian/Farsi (`fa`)
- Pashto (`ps`)
- Urdu (`ur`)
- Yiddish (`yi`)
- Dari (`prs`)
- Kurdish (Sorani) (`ckb`)

### Is RTL handled automatically?

Yes! When you switch to an RTL language, text is automatically processed for proper display. The `RtlTextHandler.Fix()` method handles character shaping and bidirectional text.

### Do I need to change my UI layout for RTL?

The text is handled automatically, but you may want to adjust UI layout (anchors, pivots) for a better RTL experience. See the RTL Layout Adjustment example in the Usage Guide.

---

## Performance Questions

### Is the binary format faster than JSON?

Yes! BLOC format offers:

- **50-70% smaller** file sizes
- **O(1) lookups** - direct access, no parsing
- **String deduplication** - reduces memory usage
- **Optional compression** - even smaller files

### How much memory does it use?

The system uses:

- One language loaded at a time in memory
- Fallback language cached for missing keys
- Array cache for repeated `GetArray()` calls
- Minimal overhead for the component bindings

### Can I load languages at runtime?

Yes, languages are loaded on-demand when you call `SetLanguage()`. Only the current language and fallback are kept in memory.

---

## Troubleshooting

### "No language files found" error

1. Make sure you've saved your languages in the Language Editor
2. Check that `.bloc` files exist in the `Locales` folder
3. Verify the folder is at project root (same level as Assets)

### Translations not showing up

1. Check that the key exists (case-sensitive)
2. Verify the language file has the translation
3. Check `LocalizationManager.IsInitialized`
4. Look for errors in the console

### DeepL/Gemini translation not working

1. Verify your API key in Settings tab
2. Check your DeepL/Gemini account has available credits
3. Ensure you have internet connection
4. Look for error messages in console

### RTL text looks wrong

1. Make sure you're using a font that supports Arabic/Hebrew characters
2. Check that TextMeshPro font asset includes the glyphs
3. Verify the language code is correct

---

## Advanced Questions

### Can I protect my translation files?

Yes! Enable protection in the config:

1. Set Protection Mode to "Anti-Tamper" or "Both"
2. Build your project
3. Hash verification will check file integrity at runtime

### Can I add custom languages?

The system supports any ISO language code. Add custom languages by:

1. Using the language code in your translation files
2. Adding display names to `LanguageDefinitions` (optional)

### How do I update translations at runtime?

The system loads from files at startup. To update:

1. Replace the `.bloc` file
2. Call `LocalizationManager.Initialize()` to reload

### Can I use this with Addressables?

The current version loads from local files. For Addressables support, you would need to:

1. Download the `.bloc` file to the `Locales` folder
2. Call `LocalizationManager.RefreshAvailableLanguages()`
3. Then switch language

---

## Still Have Questions?

- Check the [Usage Guide](Usage.md) for detailed API documentation
- Review the [BLOC Format](BLOC_FORMAT.md) specification
- See [Keybinds](Keybinds.md) for editor shortcuts
- Open an issue on GitHub for bugs or feature requests
