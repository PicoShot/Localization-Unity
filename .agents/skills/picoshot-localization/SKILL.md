---
name: picoshot-localization
description: Read, add, rename, delete, or translate game localization keys through the PicoShot Localization Unity package MCP tools, preferring bulk reads and writes over per-key loops.
---

# PicoShot Localization (Unity) — MCP tools

These MCP tools come from the **PicoShot Localization Unity package** (`com.picoshot.localization`). The MCP server runs inside the Unity Editor (loopback HTTP) and edits the same `Locales/*.bloc` files as the Language Editor. You translate with your own judgment; there is no machine-translation tool — you are the translator.

Prerequisites: Unity must be open and the server started (Language Editor > Settings > MCP > Start Server, or Tools > Localization > MCP Server). If tools fail with connection errors, tell the user to start it instead of working around it.

## Golden rules (efficiency)

1. **Bulk reads, never per-key loops.** `get_language` dumps a whole language in one call. `get_key` accepts a `langs` filter for one key in specific languages. Looping `get_key` over hundreds of keys is slow and burns context — don't do it.
2. **Bulk writes.** `set_translations` writes up to 500 `{key, lang, value}` cells per call and reports per-item results. Always check `failed`; each failure names its reason.
3. **Create before you fill.** `add_key` creates a key in every language at once (with `defaultText` in the default language). Then fill translations with `set_translations`.
4. **Find gaps with `validate`.** Its `emptyCells` list (`lang:key`) is the exact work list. Re-run it at the end to confirm zero gaps.
5. **Parallel calls are safe.** Fire independent calls (e.g. several `add_key`) in one block — no delays needed. The server serializes file access; you will not corrupt or lose keys.
6. **Trust error hints.** "Unknown key" replies include did-you-mean candidates; unknown languages list the available codes. Follow the hint instead of re-reading the whole index.
7. **Values are strings or string arrays.** Array keys must keep equal length across languages. Keys are unique case-insensitively.

## Tools

- `list_languages` — language codes + project default. Start here.
- `list_keys` — keys with `search`, `view`, `limit`, `offset`. Paginate large sets.
- `get_key` — one key; optional `langs` filter (e.g. `["en", "de"]`).
- `get_language` — one whole language (`key` → text or array) plus `count`. The bulk-read tool.
- `set_translation` — one cell. Prefer the batch form below.
- `set_translations` — up to 500 cells per call. The bulk-write tool.
- `add_key` — new key everywhere (`type`: `string`|`array`, `defaultText`, `defaultLang`).
- `rename_key` / `delete_key` — rename preserves translations; delete is permanent.
- `add_language` / `remove_language` — new languages start empty (array shapes mirrored).
- `validate` — coverage gaps, empty cells, file problems.

## Canonical flows

**Dump one language as JSON:** `get_language({lang})` — one call, done.

**Add N new keys and translate to all languages:**
1. `list_languages` for the target set.
2. One `add_key` per key (parallel-safe) with English `defaultText`.
3. Translate, then write everything with `set_translations` (≤500 cells/call).
4. `validate` — `emptyCells` must be empty.

**Translate all missing cells:** `validate` → work through `emptyCells` with `set_translations` → `validate` again.

**Check a hunch about one key:** `get_key`, with `langs` when only some languages matter.

## Limits

- `set_translations` rejects batches over 500 items — split them.
- `remove_language` refuses to delete the last language; `delete_key` cannot be undone.
- The server is Editor-only and loopback-only; it cannot run without Unity open.
