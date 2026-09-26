using System;
using System.Collections.Generic;
using System.Linq;

namespace PicoShot.Localization.Editor.Mcp
{
    /// <summary>
    /// MCP tool definitions and dispatch.
    /// </summary>
    public static class McpTools
    {
        public const int DefaultListLimit = 200;

        public static List<object> GetToolDefinitions()
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
                Tool("get_key", "Get all translations for one key, as language -> text (or array of texts).",
                    Props(Prop("key", "string", "Translation key.", true))),
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
                    return Ok(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["languages"] = store.ListLanguages().Select(l => (object)l).ToList(),
                        ["default"] = store.DefaultLanguage,
                    });

                case "list_keys":
                    return Ok(ListKeys(args, store));

                case "get_key":
                    {
                        string key = McpJson.RequireString(args, "key");
                        var entry = store.GetKey(key);
                        if (entry == null)
                            return Fail($"Unknown key '{key}'.");
                        var outDict = new Dictionary<string, object>(StringComparer.Ordinal);
                        foreach (var kvp in entry) outDict[kvp.Key] = ToWire(kvp.Value);
                        return Ok(new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["key"] = key,
                            ["translations"] = outDict,
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
                                return Fail($"Unknown key '{oldKey}'.");
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
                            return Fail($"Unknown key '{key}'.");
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
                                return Fail($"Unknown language '{lang}'.");
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

        private static object ToWire(object value)
        {
            if (value is List<string> list)
                return list.Select(s => (object)(s ?? string.Empty)).ToList();
            if (value is string[] arr)
                return arr.Select(s => (object)(s ?? string.Empty)).ToList();
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
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var lang in all.Values)
                foreach (var k in lang.Keys)
                    keys.Add(k);

            var filtered = keys.Where(k =>
                (search.Length == 0 || k.ToLowerInvariant().Contains(search)) &&
                (view.Length == 0 || IsInView(k, view))).ToList();

            int total = filtered.Count;
            var page = filtered.Skip(offset).Take(limit).Select(k => (object)k).ToList();
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

            int applied = 0, failed = 0;
            var results = new List<object>(items.Count);
            foreach (object item in items)
            {
                if (!(item is Dictionary<string, object> entry))
                {
                    failed++;
                    results.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["ok"] = false,
                        ["error"] = "Item must be an object with key, lang and value.",
                    });
                    continue;
                }
                string key = McpJson.GetString(entry, "key");
                string lang = McpJson.GetString(entry, "lang");
                entry.TryGetValue("value", out object raw);
                object value = raw != null ? FromWire(raw) : null;
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(lang) || value == null)
                {
                    failed++;
                    results.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["ok"] = false,
                        ["key"] = key,
                        ["lang"] = lang,
                        ["error"] = "Each item needs key (string), lang (string) and value (string or string array).",
                    });
                    continue;
                }
                try
                {
                    store.SetTranslation(key, lang, value);
                    applied++;
                    results.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["ok"] = true,
                        ["key"] = key,
                        ["lang"] = lang,
                    });
                }
                catch (InvalidOperationException ex)
                {
                    failed++;
                    results.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["ok"] = false,
                        ["key"] = key,
                        ["lang"] = lang,
                        ["error"] = ex.Message,
                    });
                }
            }

            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["applied"] = (long)applied,
                ["failed"] = (long)failed,
                ["results"] = results,
            };
            return (failed > 0, result);
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
                    var missing = all.Keys.Where(l => !all[l].ContainsKey(kvp.Key)).ToList();
                    issues.Add($"Key '{kvp.Key}' missing in: {string.Join(", ", missing)}.");
                }
            }
            var empty = new List<object>();
            foreach (var lang in all)
            {
                foreach (var kvp in lang.Value)
                {
                    if (kvp.Value is List<string> list)
                    {
                        if (list.Count == 0 || list.All(string.IsNullOrWhiteSpace))
                            empty.Add($"{lang.Key}:{kvp.Key}");
                    }
                    else if (string.IsNullOrWhiteSpace(kvp.Value?.ToString()))
                    {
                        empty.Add($"{lang.Key}:{kvp.Key}");
                    }
                }
            }
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["languages"] = all.Keys.Select(l => (object)l).ToList(),
                ["totalKeys"] = (long)keyCounts.Count,
                ["issues"] = issues,
                ["emptyCells"] = empty,
            };
        }
    }
}
