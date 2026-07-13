# Localization for Unity

<div align="center">
  <img src="Docs/overview.gif" alt="PicoShot Localization Overview" width="400" />
</div>

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-blue)](https://unity.com)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

A lightweight, zero-runtime-dependency, high-performance localization system for Unity with support for multiple languages, RTL (Right-to-Left) text, binary format (BLOC), and DeepL/Gemini translation integration.

## Features

- **High Performance** - Binary BLOC format for fast loading and minimal memory footprint
- **100+ Languages** - Support for over 100 languages with native name display
- **RTL Support** - Automatic Right-to-Left text handling for Arabic, Hebrew, Persian, and more
- **Zero Runtime Dependencies** - Only requires TextMeshPro (optional for basic usage)
- **Anti-Tamper** - Optional file hash verification to protect translation files
- **Editor Tools** - Built-in Language Editor with DeepL/Gemini translation integration
- **Multiple Component Support** - Works with TMP_Text, TMP_Dropdown, Legacy UI, and TextMesh
- **Format Parameters** - String formatting support with `{0}`, `{1}` placeholders
- **Mixed LTR/RTL Support** - Smart token-based bi-directional text processing for seamless Arabic/Persian integration in English contexts and vice versa
- **Language-Specific Fonts** - Automatic fallback and language-specific font switching for all localized text components
- **Virtual Tables** - Organize translation keys into grouped tables via dot-notation with dedicated export/import
- **Smart Component Inspector** - Searchable dropdown menus for selecting tables and keys directly in the Unity Inspector
- **Inline Key Initialization** - Set default language values immediately when creating new keys in the Editor

## Localization App

I also developed a separate localization management app built with modern web technologies (**React + Vite + Tauri**) to make working with translations much easier than using the default Unity UI.
It provides a cleaner interface for managing, editing, and translating localization data, which significantly speeds up the workflow.
I highly recommend using this app when working with the localization system.

Repository: [Localization-UI](https://github.com/PicoShot/Localization-UI)

## Quick Start

### 1. Installation

Add the package via Unity Package Manager:

```
https://github.com/PicoShot/Localization-Unity.git?path=/Package
```

### 2. Create Language Files

Open the Language Editor from `Tools > Localization > Language Editor` to:

- Add supported languages
- Create translation keys
- Add translations for each language
- Click on save to export BLOC files to project

### 3. Add to Scene

Add the `Localized Text` component to any GameObject with a text component:

### Via Inspector

1. Add "Localized Text" component
2. Set Translation Key to your key

### 4. Access Translations in Code

```csharp
// Get a simple translation
string greeting = LocalizationManager.GetText("greeting");

// With format parameters
string welcome = LocalizationManager.GetText("welcome_message", "John");

// Get an array of strings
string[] options = LocalizationManager.GetArray("menu_options");
```

## Documentation

- [Usage Guide](Docs/Usage.md) - Complete API documentation and usage examples
- [Keybinds](Docs/Keybinds.md) - Language Editor keyboard shortcuts
- [BLOC Format](Docs/BLOC_FORMAT.md) - Binary localization file format
- [FAQ](Docs/FAQ.md) - Frequently asked questions
- [How It Works](Docs/HowItWorks.md) - Architecture and implementation details

## Requirements

- Unity 2022.3 or higher
- TextMeshPro (included with Unity)

## Special Thanks

Special thanks to my friends for their hot-fixes, suggestions, and help with implementing this project:

- [Xeirel](https://github.com/Xeirel)
- [Fero](https://github.com/FeroKro)
- [RyTiex](https://github.com/rytiex)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---
