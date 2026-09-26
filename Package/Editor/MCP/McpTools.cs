using System;
using System.Collections.Generic;

namespace PicoShot.Localization.Editor.Mcp
{
    /// <summary>
    /// MCP tool definitions and dispatch.
    /// </summary>
    public static class McpTools
    {
        public const int DefaultListLimit = 200;

        private static readonly List<object> CachedToolDefinitions = BuildToolDefinitions();

        public static List<object> GetToolDefinitions() => CachedToolDefinitions;

        private static List<object> BuildToolDefinitions()
        {
            return new List<object>
            {
                Tool("list_languages", "List all available language codes.",
                    new Dictionary<string, object>()),
                Tool("list_keys", "List translation keys, optionally filtered by search text or view prefix.",
                    Props(
                        Prop("search", "string", "Substring filter (case-insensitive)."),
                        Prop("view", "string", "View prefix, e.g. 'ui' matches 'ui.play_button'."),
                        Prop("limit", "integer", "Max keys to return."),
                        Prop("offset", "integer", "Keys to skip for pagination."))),
                Tool("get_key", "Get translations for one key, as language -> text (or array of texts).",
                    Props(
                        Prop("key", "string", "Translation key.", true),
                        Prop("langs", "array", "Only these languages (default: all).", false, StringItems()))),
                Tool("get_language", "Read a whole language at once, as key -> text (or array of texts).",
                    Props(Prop("lang", "string", "Language code.", true))),
                Tool("set_translation", "Set the translation of a key in one language. Value may be a string or an array of strings.",
                    Props(
                        Prop("key", "string", "Translation key.", true),
                        Prop("lang", "string", "Language code.", true),
                        ValueProp())),
                Tool("add_key", "Create a new key across all languages.",
                    Props(
                        Prop("key", "string", "New translation key.", true),
                        Prop("type", "string", "'string' (default) or 'array'."),
                        Prop("defaultText", "string", "Initial text for the default language."),
                        Prop("defaultLang", "string", "Language receiving defaultText (defaults to project default)."))),
                Tool("rename_key", "Rename a key, preserving all translations.",
                    Props(
                        Prop("oldKey", "string", "Existing key.", true),
                        Prop("newKey", "string", "New key name.", true))),
                Tool("delete_key", "Delete a key and all its translations.",
                    Props(Prop("key", "string", "Translation key.", true))),
                Tool("add_language", "Add a new language (empty translations, array shapes mirrored).",
                    Props(Prop("lang", "string", "Language code, e.g. 'de'.", true))),
                Tool("remove_language", "Remove a language and its file.",
                    Props(Prop("lang", "string", "Language code.", true))),
                Tool("set_translations", "Set many translations in one call. Each item has key, lang and value (string or array of strings).",
                    Props(
                        Prop("translations", "array", "Translations to apply (max 500 per call).", true, TranslationItems()))),
                Tool("validate", "Check locale files for corruption, filename mismatches and key coverage.",
                    new Dictionary<string, object>()),
            };
        }

        private static Dictionary<string, object> Tool(string name, string description, Dictionary<string, object> properties)
        {
            var required = new List<object>();
            foreach (var kvp in properties)
            {
                if (kvp.Value is Dictionary<string, object> prop &&
                    prop.TryGetValue("required", out object r) && r is bool b && b)
                {
                    required.Add(kvp.Key);
                    prop.Remove("required");
                }
            }
            var schema = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["type"] = "object",
                ["properties"] = properties,
            };
            if (required.Count > 0) schema["required"] = required;
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["description"] = description,
                ["inputSchema"] = schema,
            };
        }

        private static Dictionary<string, object> Props(params KeyValuePair<string, Dictionary<string, object>>[] props)
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var p in props) dict[p.Key] = p.Value;
            return dict;
        }

        private static KeyValuePair<string, Dictionary<string, object>> Prop(string name, string type, string description, bool required = false, Dictionary<string, object> items = null)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal) { ["description"] = description };
            if (type != null) d["type"] = type;
            if (items != null) d["items"] = items;
            if (required) d["required"] = true;
            return new KeyValuePair<string, Dictionary<string, object>>(name, d);
        }

        private static Dictionary<string, object> StringItems()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal) { ["type"] = "string" };
        }

        private static Dictionary<string, object> ValueSchema()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["description"] = "New translation text or array of texts.",
                ["anyOf"] = new List<object>
                {
                    new Dictionary<string, object>(StringComparer.Ordinal) { ["type"] = "string" },
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["type"] = "array",
                        ["items"] = StringItems(),
                    },
                },
            };
        }

        private static Dictionary<string, object> TranslationItems()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["key"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["type"] = "string",
                        ["description"] = "Translation key.",
                    },
                    ["lang"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["type"] = "string",
                        ["description"] = "Language code.",
                    },
                    ["value"] = ValueSchema(),
                },
                ["required"] = new List<object> { "key", "lang", "value" },
            };
        }

        /// <summary> expressed as anyOf instead of omitting "type" helper. </summary>
        private static KeyValuePair<string, Dictionary<string, object>> ValueProp()
        {
            var d = ValueSchema();
            d["required"] = true;
            return new KeyValuePair<string, Dictionary<string, object>>("value", d);
        }

        /// <summary>
        /// Executes a tool. Returns (isError, resultObject); resultObject is
        /// serialized to JSON as the tool's text content.
        /// </summary>
        public static (bool isError, object result) Execute(
            string name,
            Dictionary<string, object> args,
            McpLocalesStore store)
        {
            switch (name)
            {
                case "list_languages":
                    {
                        var langs = store.ListLanguages();
                        var wired = new List<object>(langs.Count);
                        foreach (string l in langs)
                            wired.Add(l);
                        return Ok(new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["languages"] = wired,
                            ["default"] = store.DefaultLanguage,
                        });
                    }

                case "list_keys":
                    return Ok(ListKeys(args, store));

                case "get_key":
                    {
                        string key = McpJson.RequireString(args, "key");
                        var entry = store.GetKey(key);
                        if (entry == null)
                            return Fail(UnknownKeyError(store, key));
                        var wanted = RequestedLanguages(args, store);
                        if (wanted == null)
                            return Fail(UnknownLanguageError(store, RequestedLanguageNames(args)));
                        var outDict = new Dictionary<string, object>(StringComparer.Ordinal);
                        foreach (var kvp in entry)
                        {
                            if (wanted.Count > 0 && !wanted.Contains(kvp.Key))
                                continue;
                            outDict[kvp.Key] = ToWire(kvp.Value);
                        }
                        return Ok(new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["key"] = key,
                            ["translations"] = outDict,
                        });
                    }

                case "get_language":
                    {
                        string lang = McpJson.RequireString(args, "lang");
                        var all = store.LoadAll();
                        string actual = null;
                        foreach (var existing in all.Keys)
                        {
                            if (string.Equals(existing, lang, StringComparison.OrdinalIgnoreCase))
                            {
                                actual = existing;
                                break;
                            }
                        }
                        if (actual == null)
                            return Fail(UnknownLanguageError(store, "'" + lang + "'"));
                        var wire = new Dictionary<string, object>(StringComparer.Ordinal);
                        foreach (var kvp in all[actual])
                            wire[kvp.Key] = ToWire(kvp.Value);
                        return Ok(new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["lang"] = actual,
                            ["count"] = (long)wire.Count,
                            ["translations"] = wire,
                        });
                    }

                case "set_translation":
                    {
                        string key = McpJson.RequireString(args, "key");
                        string lang = McpJson.RequireString(args, "lang");
                        if (!args.TryGetValue("value", out object raw) || raw == null)
                            return Fail("Missing required parameter 'value'.");
                        object value = FromWire(raw);
                        if (value == null)
                            return Fail("'value' must be a string or an array of strings.");
                        try
                        {
                            store.SetTranslation(key, lang, value);
                        }
                        catch (InvalidOperationException ex)
                        {
                            return Fail(ex.Message);
                        }
                        return Ok(new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["ok"] = true,
                            ["key"] = key,
                            ["lang"] = lang,
                        });
                    }

                case "add_key":
                    {
                        string key = McpJson.RequireString(args, "key")?.Trim();
                        if (string.IsNullOrEmpty(key))
                            return Fail("Key must not be empty.");
                        string type = McpJson.GetString(args, "type", "string");
                        bool isArray = string.Equals(type, "array", StringComparison.OrdinalIgnoreCase);
                        if (!isArray && !string.Equals(type, "string", StringComparison.OrdinalIgnoreCase))
                            return Fail("'type' must be 'string' or 'array'.");
                        string defaultText = McpJson.GetString(args, "defaultText");
                        string defaultLang = McpJson.GetString(args, "defaultLang", store.DefaultLanguage);
                        try
                        {
                            store.AddKey(key, isArray, defaultText, defaultLang);
                        }
                        catch (InvalidOperationException ex)
                        {
                            return Fail(ex.Message);
                        }
                        return Ok(new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["ok"] = true,
                            ["key"] = key,
                            ["type"] = isArray ? "array" : "string",
                        });
                    }

                case "rename_key":
                    {
                        string oldKey = McpJson.RequireString(args, "oldKey");
                        string newKey = McpJson.RequireString(args, "newKey")?.Trim();
                        if (string.IsNullOrEmpty(newKey))
                            return Fail("New key must not be empty.");
                        try
                        {
                            if (!store.RenameKey(oldKey, newKey))
                                return Fail(UnknownKeyError(store, oldKey));
                        }
                        catch (InvalidOperationException ex)
                        {
                            return Fail(ex.Message);
                        }
                        return Ok(new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["ok"] = true,
                            ["oldKey"] = oldKey,
                            ["newKey"] = newKey,
                        });
                    }

                case "delete_key":
                    {
                        string key = McpJson.RequireString(args, "key");
                        if (!store.DeleteKey(key))
                            return Fail(UnknownKeyError(store, key));
                        return Ok(new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["ok"] = true,
                            ["key"] = key,
                        });
                    }

                case "add_language":
                    {
                        string lang = McpJson.RequireString(args, "lang")?.Trim();
                        try
                        {
                            store.AddLanguage(lang);
                        }
                        catch (InvalidOperationException ex)
                        {
                            return Fail(ex.Message);
                        }
                        return Ok(new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["ok"] = true,
                            ["lang"] = lang,
                        });
                    }

                case "remove_language":
                    {
                        string lang = McpJson.RequireString(args, "lang");
                        try
                        {
                            if (!store.RemoveLanguage(lang))
                                return Fail(UnknownLanguageError(store, "'" + lang + "'"));
                        }
                        catch (InvalidOperationException ex)
                        {
                            return Fail(ex.Message);
                        }
                        return Ok(new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["ok"] = true,
                            ["lang"] = lang,
                        });
                    }

                case "set_translations":
                    return SetTranslations(args, store);

                case "validate":
                    return Ok(Validate(store));

                default:
                    return Fail($"Unknown tool '{name}'.");
            }
        }

        private static (bool, object) Ok(object result) => (false, result);

        private static (bool, object) Fail(string message) => (true,
            new Dictionary<string, object>(StringComparer.Ordinal) { ["error"] = message });

        private static string UnknownKeyError(McpLocalesStore store, string key)
        {
            var suggestions = store.SuggestSimilarKeys(key);
            string hint = suggestions.Count > 0 ? $" Did you mean: {string.Join(", ", suggestions)}?" : string.Empty;
            return $"Unknown key '{key}'. Use add_key first.{hint}";
        }

        private static HashSet<string> RequestedLanguages(Dictionary<string, object> args, McpLocalesStore store)
        {
            var raw = McpJson.GetArray(args, "langs");
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (raw == null) return wanted;
            var all = store.LoadAll();
            foreach (object item in raw)
            {
                if (!(item is string s)) return null;
                string actual = null;
                foreach (var existing in all.Keys)
                {
                    if (string.Equals(existing, s, StringComparison.OrdinalIgnoreCase))
                    {
                        actual = existing;
                        break;
                    }
                }
                if (actual == null) return null;
                wanted.Add(actual);
            }
            return wanted;
        }

        private static string RequestedLanguageNames(Dictionary<string, object> args)
        {
            var raw = McpJson.GetArray(args, "langs");
            if (raw == null) return string.Empty;
            var names = new List<string>(raw.Count);
            foreach (object item in raw)
                names.Add(item is string s ? s : "?");
            return "'" + string.Join(", ", names) + "'";
        }

        private static string UnknownLanguageError(McpLocalesStore store, string what)
        {
            return $"Unknown language {what}. Available: {string.Join(", ", store.ListLanguages())}.";
        }

        private static object ToWire(object value)
        {
            if (value is List<string> list)
            {
                var wired = new List<object>(list.Count);
                foreach (string s in list)
                    wired.Add((object)(s ?? string.Empty));
                return wired;
            }
            if (value is string[] arr)
            {
                var wired = new List<object>(arr.Length);
                foreach (string s in arr)
                    wired.Add((object)(s ?? string.Empty));
                return wired;
            }
            return value?.ToString() ?? string.Empty;
        }

        /// <summary>Converts wire JSON (string | array) to storage value. Null when invalid.</summary>
        internal static object FromWire(object raw)
        {
            if (raw is string s) return s;
            if (raw is List<object> list)
            {
                var outList = new List<string>(list.Count);
                foreach (object item in list)
                {
                    if (item is string str) outList.Add(str);
                    else return null;
                }
                return outList;
            }
            return null;
        }

        private static object ListKeys(Dictionary<string, object> args, McpLocalesStore store)
        {
            string search = McpJson.GetString(args, "search", string.Empty)?.ToLowerInvariant() ?? string.Empty;
            string view = McpJson.GetString(args, "view", string.Empty) ?? string.Empty;
            int limit = McpJson.GetInt(args, "limit", DefaultListLimit);
            int offset = McpJson.GetInt(args, "offset", 0);
            if (limit <= 0) limit = DefaultListLimit;
            if (limit > 2000) limit = 2000;
            if (offset < 0) offset = 0;

            var all = store.LoadAll();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var keys = new List<string>();
            foreach (var lang in all.Values)
            {
                foreach (var k in lang.Keys)
                {
                    if (seen.Add(k))
                        keys.Add(k);
                }
            }
            keys.Sort(StringComparer.Ordinal);

            var page = new List<object>(limit);
            int total = 0;
            int skipped = 0;
            foreach (string k in keys)
            {
                if (search.Length != 0 && k.ToLowerInvariant().Contains(search) == false)
                    continue;
                if (view.Length != 0 && IsInView(k, view) == false)
                    continue;
                total++;
                if (skipped < offset)
                {
                    skipped++;
                    continue;
                }
                if (page.Count < limit)
                    page.Add(k);
            }
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["keys"] = page,
                ["total"] = (long)total,
                ["offset"] = (long)offset,
                ["limit"] = (long)limit,
            };
        }

        private static bool IsInView(string key, string view)
        {
            return key.StartsWith(view + ".", StringComparison.OrdinalIgnoreCase) ||
                   key.StartsWith(view + "_", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, view, StringComparison.OrdinalIgnoreCase);
        }

        private const int MaxBatchItems = 500;

        /// <summary> Applies many translations in one call. </summary>
        private static (bool, object) SetTranslations(Dictionary<string, object> args, McpLocalesStore store)
        {
            var items = McpJson.GetArray(args, "translations");
            if (items == null || items.Count == 0)
                return Fail("Missing required parameter 'translations' (non-empty array).");
            if (items.Count > MaxBatchItems)
                return Fail($"Too many items ({items.Count}). Max {MaxBatchItems} per call; split into multiple calls.");

            var batch = new List<McpBatchItem>(items.Count);
            var results = new List<object>(items.Count);
            int failed = 0;
            foreach (object item in items)
            {
                if (!(item is Dictionary<string, object> entry))
                {
                    failed++;
                    results.Add(BatchError(null, null, "Item must be an object with key, lang and value."));
                    continue;
                }
                string key = McpJson.GetString(entry, "key");
                string lang = McpJson.GetString(entry, "lang");
                entry.TryGetValue("value", out object raw);
                object value = raw != null ? FromWire(raw) : null;
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(lang) || value == null)
                {
                    failed++;
                    results.Add(BatchError(key, lang, "Each item needs key (string), lang (string) and value (string or string array)."));
                    continue;
                }
                batch.Add(new McpBatchItem(key, lang, value));
                results.Add(null);
            }

            var outcomes = store.SetMany(batch);
            int applied = 0;
            int outcomeIndex = 0;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] != null) continue;
                var outcome = outcomes[outcomeIndex++];
                if (outcome.Ok)
                {
                    applied++;
                    results[i] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["ok"] = true,
                        ["key"] = outcome.Key,
                        ["lang"] = outcome.Lang,
                    };
                }
                else
                {
                    failed++;
                    results[i] = BatchError(outcome.Key, outcome.Lang, outcome.Error);
                }
            }

            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["applied"] = (long)applied,
                ["failed"] = (long)failed,
                ["results"] = results,
            };
            return (failed > 0, (object)result);
        }

        private static Dictionary<string, object> BatchError(string key, string lang, string error)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["ok"] = false,
                ["key"] = key,
                ["lang"] = lang,
                ["error"] = error,
            };
        }

        private static object Validate(McpLocalesStore store)
        {
            var all = store.LoadAll();
            var issues = new List<object>();
            var keyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kvp in all)
            {
                foreach (var key in kvp.Value.Keys)
                {
                    keyCounts.TryGetValue(key, out int n);
                    keyCounts[key] = n + 1;
                }
            }
            foreach (var kvp in keyCounts)
            {
                if (kvp.Value < all.Count)
                {
                    var missing = new List<string>();
                    foreach (var lang in all)
                    {
                        if (!lang.Value.ContainsKey(kvp.Key))
                            missing.Add(lang.Key);
                    }
                    issues.Add($"Key '{kvp.Key}' missing in: {string.Join(", ", missing)}.");
                }
            }
            var empty = new List<object>();
            foreach (var lang in all)
            {
                foreach (var kvp in lang.Value)
                {
                    if (IsEmptyCell(kvp.Value))
                        empty.Add($"{lang.Key}:{kvp.Key}");
                }
            }
            var languages = new List<object>(all.Count);
            foreach (var lang in all.Keys)
                languages.Add(lang);
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["languages"] = languages,
                ["totalKeys"] = (long)keyCounts.Count,
                ["issues"] = issues,
                ["emptyCells"] = empty,
            };
        }

        private static bool IsEmptyCell(object value)
        {
            if (value is List<string> list)
            {
                foreach (string s in list)
                {
                    if (!string.IsNullOrWhiteSpace(s))
                        return false;
                }
                return true;
            }
            return string.IsNullOrWhiteSpace(value?.ToString());
        }
    }
}
