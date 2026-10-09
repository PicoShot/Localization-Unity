<div dir="ltr" align=center>

[**Usage**](Usage.md) / [**Keybinds**](Keybinds.md) / [**BLOC Format**](BLOC_FORMAT.md) / [**FAQ**](FAQ.md) / [**How It Works**](HowItWorks.md) / [**MCP**](MCP.md)
</div>

# MCP Server

The package ships a built-in [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server, hosted inside the Unity Editor. It lets AI agents (Claude, Cursor, Copilot, …) list, read, add, translate and modify your localization keys directly — no extra installs, no sidecar process.

- Transport: Streamable HTTP (stateless POST), served by the Editor itself.
- Endpoint: `http://127.0.0.1:8123/mcp` (port configurable).
- Source of truth: the same `Locales/*.bloc` files the Language Editor uses.
- Loopback only: the server binds `127.0.0.1`/`localhost`, so only processes on your machine can connect.

## Quick start

1. Open your Unity project and enable the server: Language Editor > Settings > MCP > Start Server (it is off by default).
2. Check it: open `http://127.0.0.1:8123/` in a browser (status page) or `http://127.0.0.1:8123/health`.
3. Register the endpoint in your agent (examples below).
4. Ask the agent to e.g. "list untranslated German keys" or "add key `ui.quit` with English text 'Quit'".

If the Language Editor window is open, it auto-reloads files changed via MCP — unless it has unsaved changes, in which case it warns instead of overwriting your work.

## Settings and status (Language Editor > Settings > MCP)

The MCP subtab shows the server status (running/stopped, endpoint URL, tool count)
and holds every setting in one place:

- Start / Stop / Restart buttons, Open Status Page, Copy Agent Config.
- Start automatically: opt in to launching the server with the editor (off by default).
- Port (default `8123`): saved as you type and used on the next start; if the server is running, restart it to switch ports.

## Menu reference (`Tools > Localization > MCP Server`)

| Item                          | What it does                                                        |
| ----------------------------- | ------------------------------------------------------------------- |
| Start / Stop / Restart Server | Manual lifecycle control.                                           |
| Copy Agent Config             | Copies a generic `{"mcpServers": …}` JSON snippet to the clipboard. |
| Open Status Page              | Opens the `/` status page in a browser.                             |

The port is stored in EditorPrefs (default `8123`) and editable in the MCP settings subtab.

## Client setup

All clients use the same URL: `http://127.0.0.1:8123/mcp`.

### Claude Code CLI

```bash
claude mcp add --transport http picoshot-localization http://127.0.0.1:8123/mcp
```

### Codex CLI

```bash
codex mcp add picoshot-localization --url http://127.0.0.1:8123/mcp
```

### opencode CLI

```bash
opencode mcp add picoshot-localization --url http://127.0.0.1:8123/mcp
```

**Cursor** (`.cursor/mcp.json` in the project)

```json
{
  "mcpServers": {
    "picoshot-localization": { "url": "http://127.0.0.1:8123/mcp" }
  }
}
```

**VS Code** (`mcp.json`)

```json
{
  "servers": {
    "picoshot-localization": { "url": "http://127.0.0.1:8123/mcp", "type": "http" }
  }
}
```

**Claude Desktop** (`claude_desktop_config.json`) claude desktop requires nodejs (with npx) installed.

```json
{
  "mcpServers": {
    "picoshot-localization": {
      "command": "npx",
      "args": [
        "-y",
        "mcp-remote",
        "http://127.0.0.1:8123/mcp",
        "--transport",
        "http-only"
      ]
    }
  }
}
```

Use `Tools > Localization > MCP Server > Copy Agent Config` for a paste-ready snippet.

## Tools

| Tool               | Description                                                                                                  |
| ------------------ | ------------------------------------------------------------------------------------------------------------ |
| `list_languages`   | Overview: languages (name, RTL, key/empty counts), project default, total keys, unreadable files.           |
| `list_keys`        | Key names with `search`, `view`, `limit`, `offset` pagination.                                               |
| `get_key`          | One key, optional `langs` filter.                                                                            |
| `get_keys`         | Up to 500 keys at once (`key` → `{lang: value}`), unknown keys with suggestions.                             |
| `get_language`     | One language, filtered by `view` / `search` / `keys` / `emptyOnly`, paginated.                               |
| `get_untranslated` | Translation work list for one language: `key` → source text, for missing or empty cells.                    |
| `set_translation`  | Set one cell.                                                                                                |
| `set_translations` | Set up to 500 cells: `{lang, values: {key: value}}` and/or `[{key, lang, value}]`. Returns only failures.    |
| `add_key`          | New key across all languages (`type`, `defaultText`, `defaultLang`).                                         |
| `add_keys`         | Up to 500 new keys at once, optionally with values per language.                                             |
| `rename_key`       | Rename a key, preserving translations.                                                                       |
| `delete_key(s)`    | Delete one key / up to 500 keys everywhere.                                                                  |
| `add_language`     | Add a language (empty cells, array lengths mirrored).                                                        |
| `remove_language`  | Remove a language and its file (refuses the default and the last language).                                  |
| `validate`         | Unreadable files, missing keys, string/array conflicts, array length mismatches, empty counts per language. |

Resources: every language is also readable as `locales://{lang}` (JSON).

Tools carry MCP annotations (`readOnlyHint`, `destructiveHint`, …) so clients can auto-approve reads and confirm destructive calls. Unknown arguments are rejected with the list of expected ones.


## Agent skill

The repo ships a skill file (`.agents/skills/picoshot-localization/SKILL.md`) that teaches agents these tools, the efficient flows (`get_untranslated` → `set_translations`, `add_keys` with values), translation-quality rules (keep placeholders and rich-text tags) and safe rename/delete practice. To install it to your project, run:

```bash
npx skills add PicoShot/Localization-Unity
```

## Security notes

- The server listens on loopback only; LAN machines cannot reach it.
- There is no auth token (localhost is the boundary). Do not expose the port via tunnels without adding your own auth.
- The server runs Editor-only and is never included in builds.

## Troubleshooting

- **Port already in use** (e.g. two Unity instances): the Console shows an error on start; change the port pref and restart the server.
- **Agent can't connect**: confirm `…/health` loads in a browser and that Unity (not just the agent) is running — the server lives in the Editor process.
- **Old MCP clients** that only speak the legacy SSE transport are not supported; use a client with Streamable HTTP support.
- **Stale translations in Play mode**: the runtime loads locales at startup; re-enter Play mode after MCP edits.
- **Server stops after recompilation**: domain reload drops the listener; it auto-starts again if auto-start is enabled, otherwise use Restart Server.
