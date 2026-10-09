---
name: picoshot-localization
description: Translate, add, rename, delete or audit Unity game localization keys and languages (locale / translation / .bloc files) through the PicoShot Localization MCP tools, using batch reads and writes instead of per-key loops.
---

# PicoShot Localization (Unity) — MCP tools

These tools come from the **PicoShot Localization Unity package** (`com.picoshot.localization`). The server runs inside the Unity Editor (loopback HTTP) and edits the same `Locales/*.bloc` files as the Language Editor. There is no machine-translation tool: you are the translator.

Prerequisite: Unity must be open with the server started (Language Editor > Settings > MCP > Start Server, or Tools > Localization > MCP Server). If calls fail with connection errors, ask the user to start it; don't work around it.

## Golden rules

1. **Start with `list_languages`.** One call gives every language (name, RTL, key and empty counts), the default language, total keys, and any locale files that failed to load.
2. **Batch everything.** Use `get_untranslated` / `get_keys` / `get_language` to read, `set_translations` to write (≤500 cells), `add_keys` to create (≤500 keys, values included), and `delete_keys` to delete. Don't loop single-key tools.
3. **Read `errors` in batch results.** Batch tools apply the valid items and return only the failures, by `index` with a reason. Fix those items and resend only them.
4. **Follow error hints.** Unknown keys come with "did you mean" suggestions, and unknown languages list the codes that exist. Don't re-list everything.
5. **Page large results.** When a response has `nextOffset`, pass it as `offset` to get the next page.

## Translation quality (you are the translator)

- Keep these exactly as in the source: placeholders (`{0}`, `{1:N2}`, `{name}`), rich-text and TextMeshPro tags (`<b>`, `<color=#fff>`, `<sprite=3>`, `<br>`), `\n`, and leading or trailing spaces.
- Never translate key names. For array keys, keep the same number of elements in the same order.
- Match the game's existing tone and terms. Read a few existing translations of the target language (`get_language` with `view`) before starting a large batch.
- UI strings have limited space. Prefer short wording close to the source length.
- For RTL languages (`rtl: true`, e.g. `ar`, `he`, `fa`), write normal logical-order text; the package handles shaping.

## Tools

Read (safe):
- `list_languages`: overview, call first.
- `list_keys`: key names with `search`, `view` prefix, paging.
- `get_key`: one key, optional `langs`.
- `get_keys`: up to 500 keys, as key → {lang: value}.
- `get_language`: one language, filtered by `view`/`search`/`keys`/`emptyOnly`, paged.
- `get_untranslated`: the translation work list, as key → source text.
- `validate`: unreadable files, missing keys, type and array-length conflicts, empty counts. Add `lang` to list that language's empty keys.

Write:
- `set_translations`: `{lang, values: {key: value}}` and/or `translations: [{key, lang, value}]`, ≤500 cells. Cells that already hold the value are skipped.
- `set_translation`: one cell.
- `add_keys`: `[{key, type?, values?: {lang: value}}]`. Creates each key in every language.
- `add_key`: one key (`defaultText`, `defaultLang`).
- `add_language`: a code such as `de`, `pt-br` or `zh-hans`. Existing keys are created empty.

Destructive (confirm with the user first):
- `rename_key`, `delete_key`, `delete_keys`, `remove_language`.

## Canonical flows

**Translate a language:**
1. `get_untranslated({lang: "de"})` returns `items` as {key: source text}. If `current` is present, it holds partially translated arrays.
2. Translate the values, then call `set_translations({lang: "de", values: {...same keys...}})`.
3. If the result had `nextOffset`, repeat until it is gone. Then `validate` should show `emptyByLanguage.de` at 0, apart from keys counted in `noSource`, whose source text is empty too.

**Translate into every language:** run the flow above once per language from `list_languages`. Independent languages can run in parallel.

**Add new keys with all translations:** write the source text and the translations yourself, then create everything in one `add_keys` call: `[{key: "ui.quit", values: {en: "Quit", de: "Beenden", fr: "Quitter"}}]`. No separate fill step is needed.

**Review some keys:** `get_keys({keys: [...]})`, or `get_language({lang, view: "ui"})` for one area.

**Rename or delete keys:** components and scripts reference keys by string, and these tools do not update them. First search the project's `Assets` (`*.cs`, `*.unity`, `*.prefab`, `*.asset`) for the exact key. Show the user what references it, get confirmation, rename or delete, then update the references.

## Rules the server enforces

- Key names use letters, digits, `_` and `.` (e.g. `ui.play_button`) and are unique case-insensitively. Use the project's existing prefixes: check `list_keys` with a `view` before inventing new ones.
- Each key is a `string` or an `array` key in every language. Sending the wrong type is rejected.
- An array key has the same length in every language. To resize one, send the new array for **all** languages in the same `set_translations` call.
- Language codes are normalized, so `DE` and `pt_BR` become `de` and `pt-br`.
- `add_language` refuses to overwrite a locale file that exists but failed to load. Report the `fileIssues` entry to the user instead.
- `remove_language` refuses the project default language and the last remaining language.

## Limits

- 500 items per batch call (`set_translations`, `add_keys`, `delete_keys`, `get_keys`).
- Editor-only and loopback-only: nothing works without Unity open.
- The runtime loads locales at startup, so changes show in Play mode only after it is re-entered.
