<div dir="ltr" align=center>

[**Usage**](Usage.md) / [**Keybinds**](Keybinds.md) / [**BLOC Format**](BLOC_FORMAT.md) / [**FAQ**](FAQ.md) / [**How It Works**](HowItWorks.md) / [**MCP**](MCP.md)
</div>

# Language Editor Keyboard Shortcuts

Complete reference for keyboard shortcuts available in the Language Editor window (`Tools > Localization > Language Editor`).

## Navigation

| Shortcut         | Action                                       |
| ---------------- | -------------------------------------------- |
| `↑` (Up Arrow)   | Select previous key in the list              |
| `↓` (Down Arrow) | Select next key in the list                  |
| `Ctrl + ↑`       | Move selected key up in the list (reorder)   |
| `Ctrl + ↓`       | Move selected key down in the list (reorder) |
| `Escape`         | Deselect current key                         |

## Key Management

| Shortcut                | Action                                             |
| ----------------------- | -------------------------------------------------- |
| `Delete` or `Backspace` | Delete the selected key (with confirmation dialog) |
| `Ctrl + R`              | Rename the selected key                            |
| `Ctrl + S`              | Save all changes to language files                 |

## Translation & Copy

| Shortcut   | Action                                        |
| ---------- | --------------------------------------------- |
| `Ctrl + T` | Translate the selected key using DeepL/Gemini |
| `Ctrl + C` | Copy the selected key name to clipboard       |

## How to Use

### Navigating Keys

1. Open the Language Editor (`Tools > Localization > Language Editor`)
2. Click on the Keys tab
3. Use `↑` and `↓` arrows to navigate through keys
4. The key details panel updates automatically as you navigate

### Reordering Keys

Keys can be reordered to organize your translations:

1. Select a key using `↑` or `↓`
2. Hold `Ctrl` and press `↑` or `↓` to move the key
3. The new order is saved when you save the files (`Ctrl + S`)

### Translating with DeepL/Gemini

Quickly translate a key using DeepL/Gemini integration:

1. Select a key
2. Set up your DeepL/Gemini API key in the Settings tab
3. Press `Ctrl + T` to translate the selected key
4. The translation will be applied to all target languages

> **Note:** DeepL/Gemini translation requires an API key configured in the Settings tab.

### Copying Key Names

Copy key names for use in code:

1. Select a key
2. Press `Ctrl + C`
3. The key name is copied to clipboard
4. Paste into your code: `LocalizationManager.GetText("copied_key")`

### Renaming Keys

Rename keys without losing translations:

1. Select a key
2. Press `Ctrl + R`
3. Enter the new name in the dialog
4. All translations are preserved under the new key name

### Deleting Keys

Remove unused keys:

1. Select a key
2. Press `Delete` or `Backspace`
3. Confirm deletion in the dialog

> **Warning:** Deleted keys cannot be recovered. Make sure to save backups.

## Tips

- **Quick Save**: Use `Ctrl + S` frequently to avoid losing changes
- **Batch Operations**: The editor prompts for auto-save before compiling or entering Play Mode
- **Keyboard Focus**: Shortcuts work when the Language Editor window is focused
- **Reorder Organization**: Use `Ctrl + ↑/↓` to group related keys together
