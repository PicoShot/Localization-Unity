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
        public const int MaxListLimit = 2000;
        public const int DefaultLanguageLimit = 1000;
        public const int MaxLanguageLimit = 5000;
        public const int DefaultUntranslatedLimit = 200;
        public const int MaxUntranslatedLimit = 1000;
        public const int DefaultValidateLimit = 100;
        public const int MaxBatchItems = 500;

        /// <summary>Server-level guidance sent to clients in initialize and server/discover.</summary>
        public const string Instructions =
            "PicoShot Localization edits this Unity project's Locales/*.bloc files (the same data as the Language Editor). " +
            "Start with list_languages. To translate: get_untranslated(lang) returns {key: source text}; translate it and write " +
            "back with set_translations(lang, values) using the same keys (max 500 per call). Create keys with add_keys (it can " +
            "set initial values per language). Prefer batch tools over per-key calls and check the 'errors' list of batch results. " +
            "Keep placeholders such as {0} or {name}, rich-text tags such as <b> or <color=...>, and \\n exactly as in the source. " +
            "Keys use letters, digits, '_' and '.' and are unique case-insensitively; array keys need the same length in every language. " +
            "rename_key and delete_keys do not update scenes, prefabs or scripts that reference keys by string.";

        private sealed class ToolSpec
        {
            public Dictionary<string, object> Definition;
            public HashSet<string> Arguments;
        }

        private static readonly Dictionary<string, ToolSpec> Specs = new(StringComparer.Ordinal);
        private static readonly List<object> CachedToolDefinitions = BuildToolDefinitions();

        public static List<object> GetToolDefinitions() => CachedToolDefinitions;

        #region Definitions

        private static List<object> BuildToolDefinitions()
        {
            var list = new List<object>
            {
                Tool("list_languages", "List languages",
                    "Overview: every language (code, name, rtl, key count, empty-cell count), the project default and the total key count. " +
                    "Also lists locale files that could not be loaded. Call this first.",
                    ReadOnly),
                Tool("list_keys", "List keys",
                    "Key names, sorted, filtered by 'search' (case-insensitive substring) and/or 'view' (prefix: 'ui' matches 'ui.play' and 'ui_play'). " +
                    "Paginated: pass the returned 'nextOffset' as 'offset' to continue.",
                    ReadOnly,
                    Prop("search", "string", "Case-insensitive substring filter."),
                    Prop("view", "string", "View prefix, e.g. 'ui'."),
                    IntProp("limit", $"Max keys to return (default {DefaultListLimit}, max {MaxListLimit}).", 1, MaxListLimit),
                    IntProp("offset", "Keys to skip.", 0, null)),
                Tool("get_key", "Get key",
                    "One key's value in every language (or only 'langs'). For several keys use get_keys; for a whole language use get_language.",
                    ReadOnly,
                    Prop("key", "string", "Translation key.", true),
                    StringArrayProp("langs", "Only these languages (default: all).")),
                Tool("get_keys", "Get keys",
                    $"Several keys (max {MaxBatchItems}) in every language or only 'langs', as key -> {{lang: value}}. Unknown keys are listed with suggestions.",
                    ReadOnly,
                    StringArrayProp("keys", "Translation keys.", true),
                    StringArrayProp("langs", "Only these languages (default: all).")),
                Tool("get_language", "Get language",
                    "One language as key -> value (string, or array of strings for array keys). Filter with view/search/keys/emptyOnly; " +
                    $"paginated (default limit {DefaultLanguageLimit}, max {MaxLanguageLimit}).",
                    ReadOnly,
                    Prop("lang", "string", "Language code.", true),
                    Prop("view", "string", "View prefix, e.g. 'ui'."),
                    Prop("search", "string", "Case-insensitive substring filter on key names."),
                    StringArrayProp("keys", "Only these keys."),
                    BoolProp("emptyOnly", "Only cells that are empty or contain an empty array element."),
                    IntProp("limit", "Max keys to return.", 1, MaxLanguageLimit),
                    IntProp("offset", "Keys to skip.", 0, null)),
                Tool("get_untranslated", "Get untranslated",
                    "Translation work list: keys whose cell in 'lang' is missing, empty, or an array with an empty element, as key -> source text " +
                    "(from 'sourceLang', default: project default language). Translate the values and write them with set_translations " +
                    "{lang, values}. For arrays with some elements already translated, 'current' holds the existing target array. " +
                    "Keys whose source is also empty are only counted in 'noSource'.",
                    ReadOnly,
                    Prop("lang", "string", "Target language code.", true),
                    Prop("sourceLang", "string", "Source language code (default: project default)."),
                    Prop("view", "string", "View prefix, e.g. 'ui'."),
                    Prop("search", "string", "Case-insensitive substring filter on key names."),
                    IntProp("limit", $"Max keys to return (default {DefaultUntranslatedLimit}, max {MaxUntranslatedLimit}).", 1, MaxUntranslatedLimit),
                    IntProp("offset", "Keys to skip.", 0, null)),
                Tool("set_translation", "Set translation",
                    "Set one cell. Prefer set_translations for more than one cell.",
                    Write,
                    Prop("key", "string", "Translation key.", true),
                    Prop("lang", "string", "Language code.", true),
                    ValueProp("value", "Text, or array of texts for array keys.", true)),
                Tool("set_translations", "Set translations",
                    $"Write up to {MaxBatchItems} cells per call, given as 'lang' + 'values' ({{key: value}}) for one language and/or " +
                    "'translations' ([{key, lang, value}], 'lang' defaults to the top-level one). Values are strings, or string arrays for " +
                    "array keys (same length in every language; to resize, set the key in all languages in one call). Cells that already " +
                    "hold the value are skipped. Returns counts and only the failed items.",
                    Write,
                    Prop("lang", "string", "Language for 'values' and the default for 'translations' items."),
                    ObjectProp("values", "Map of key -> value for 'lang'.", ValueSchema(null)),
                    ArrayProp("translations", $"Cells to set (max {MaxBatchItems} in total with 'values').", TranslationItemSchema())),
                Tool("add_key", "Add key",
                    "Create one key in every language. Prefer add_keys for several keys or to set values for several languages at once.",
                    Create,
                    Prop("key", "string", "New key: letters, digits, '_' and '.'; unique case-insensitively.", true),
                    EnumProp("type", "Key type (default 'string').", "string", "array"),
                    Prop("defaultText", "string", "Initial text for 'defaultLang' (first element for arrays)."),
                    Prop("defaultLang", "string", "Language receiving 'defaultText' (default: project default).")),
                Tool("add_keys", "Add keys",
                    $"Create up to {MaxBatchItems} keys in one call, each in every language. Each item: 'key' (letters, digits, '_' and '.'; " +
                    "unique case-insensitively), optional 'type' (inferred from values; default 'string'), optional 'values' {lang: value} " +
                    "to fill translations immediately. Languages without a value start empty (arrays: same length as the given values, else 1).",
                    Create,
                    ArrayProp("keys", "Keys to create.", NewKeyItemSchema(), true)),
                Tool("rename_key", "Rename key",
                    "Rename a key in every language, keeping its translations. Scenes, prefabs and scripts that reference the old key by " +
                    "string are NOT updated; search the project for the old key and confirm with the user first.",
                    Destructive,
                    Prop("oldKey", "string", "Existing key.", true),
                    Prop("newKey", "string", "New key name.", true)),
                Tool("delete_key", "Delete key",
                    "Permanently delete one key from every language. Prefer delete_keys for several. References in scenes, prefabs and " +
                    "scripts are not updated; confirm with the user first.",
                    Destructive,
                    Prop("key", "string", "Translation key.", true)),
                Tool("delete_keys", "Delete keys",
                    $"Permanently delete up to {MaxBatchItems} keys from every language in one call. References in scenes, prefabs and " +
                    "scripts are not updated; confirm with the user first.",
                    Destructive,
                    StringArrayProp("keys", "Keys to delete.", true)),
                Tool("add_language", "Add language",
                    "Add a supported language (codes like 'de', 'pt-br', 'zh-hans'). Every existing key is created empty, with matching array lengths.",
                    Create,
                    Prop("lang", "string", "Language code.", true)),
                Tool("remove_language", "Remove language",
                    "Permanently delete a language and its file. Refuses the project default language and the last language. Confirm with the user first.",
                    Destructive,
                    Prop("lang", "string", "Language code.", true)),
                Tool("validate", "Validate",
                    "Health check: unreadable locale files, keys missing from some languages, string/array type conflicts, array length " +
                    "mismatches, placeholders or rich-text tags that differ from the default language, and empty-cell counts per language. Pass 'lang' to also list that language's empty keys. 'ok' is true " +
                    "when nothing needs fixing besides empty cells.",
                    ReadOnly,
                    Prop("lang", "string", "Also list the empty keys of this language."),
                    IntProp("limit", $"Max entries per list (default {DefaultValidateLimit}, max {MaxListLimit}).", 1, MaxListLimit)),
            };
            return list;
        }

        private enum Hint { ReadOnly, Write, Create, Destructive }

        private const Hint ReadOnly = Hint.ReadOnly;
        private const Hint Write = Hint.Write;
        private const Hint Create = Hint.Create;
        private const Hint Destructive = Hint.Destructive;

        private static Dictionary<string, object> Tool(
            string name, string title, string description, Hint hint,
            params KeyValuePair<string, Dictionary<string, object>>[] props)
        {
            var properties = new Dictionary<string, object>(StringComparer.Ordinal);
            var required = new List<object>();
            var arguments = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in props)
            {
                if (p.Value.Remove("required"))
                    required.Add(p.Key);
                properties[p.Key] = p.Value;
                arguments.Add(p.Key);
            }
            var schema = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["additionalProperties"] = false,
            };
            if (required.Count > 0) schema["required"] = required;

            var annotations = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["title"] = title,
                ["readOnlyHint"] = hint == Hint.ReadOnly,
                ["openWorldHint"] = false,
            };
            if (hint != Hint.ReadOnly)
            {
                annotations["destructiveHint"] = hint == Hint.Destructive;
                annotations["idempotentHint"] = hint == Hint.Write;
            }

            var definition = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["title"] = title,
                ["description"] = description,
                ["inputSchema"] = schema,
                ["annotations"] = annotations,
            };
            Specs[name] = new ToolSpec { Definition = definition, Arguments = arguments };
            return definition;
        }

        private static KeyValuePair<string, Dictionary<string, object>> Prop(string name, string type, string description, bool required = false)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["type"] = type,
                ["description"] = description,
            };
            if (required) d["required"] = true;
            return new KeyValuePair<string, Dictionary<string, object>>(name, d);
        }

        private static KeyValuePair<string, Dictionary<string, object>> IntProp(string name, string description, int? minimum, int? maximum)
        {
            var p = Prop(name, "integer", description);
            if (minimum.HasValue) p.Value["minimum"] = (long)minimum.Value;
            if (maximum.HasValue) p.Value["maximum"] = (long)maximum.Value;
            return p;
        }

        private static KeyValuePair<string, Dictionary<string, object>> BoolProp(string name, string description)
        {
            return Prop(name, "boolean", description);
        }

        private static KeyValuePair<string, Dictionary<string, object>> EnumProp(string name, string description, params string[] values)
        {
            var p = Prop(name, "string", description);
            p.Value["enum"] = new List<object>(values);
            return p;
        }

        private static KeyValuePair<string, Dictionary<string, object>> StringArrayProp(string name, string description, bool required = false)
        {
            return ArrayProp(name, description, StringSchema(), required);
        }

        private static KeyValuePair<string, Dictionary<string, object>> ArrayProp(string name, string description, Dictionary<string, object> items, bool required = false)
        {
            var p = Prop(name, "array", description, required);
            p.Value["items"] = items;
            return p;
        }

        private static KeyValuePair<string, Dictionary<string, object>> ObjectProp(string name, string description, Dictionary<string, object> valueSchema)
        {
            var p = Prop(name, "object", description);
            p.Value["additionalProperties"] = valueSchema;
            return p;
        }

        private static KeyValuePair<string, Dictionary<string, object>> ValueProp(string name, string description, bool required)
        {
            var d = ValueSchema(description);
            if (required) d["required"] = true;
            return new KeyValuePair<string, Dictionary<string, object>>(name, d);
        }

        private static Dictionary<string, object> StringSchema()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal) { ["type"] = "string" };
        }

        private static Dictionary<string, object> ValueSchema(string description)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["anyOf"] = new List<object>
                {
                    StringSchema(),
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["type"] = "array",
                        ["items"] = StringSchema(),
                    },
                },
            };
            if (description != null) d["description"] = description;
            return d;
        }

        private static Dictionary<string, object> TranslationItemSchema()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["key"] = StringSchema(),
                    ["lang"] = StringSchema(),
                    ["value"] = ValueSchema(null),
                },
                ["required"] = new List<object> { "key", "value" },
                ["additionalProperties"] = false,
            };
        }

        private static Dictionary<string, object> NewKeyItemSchema()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["key"] = StringSchema(),
                    ["type"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["type"] = "string",
                        ["enum"] = new List<object> { "string", "array" },
                    },
                    ["values"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["type"] = "object",
                        ["description"] = "Language code -> initial value.",
                        ["additionalProperties"] = ValueSchema(null),
                    },
                },
                ["required"] = new List<object> { "key" },
                ["additionalProperties"] = false,
            };
        }

        #endregion

        #region Dispatch

        /// <summary>
        /// Executes a tool. Returns (isError, resultObject); resultObject is
        /// serialized to JSON as the tool's text content.
        /// </summary>
        public static (bool isError, object result) Execute(
            string name,
            Dictionary<string, object> args,
            McpLocalesStore store)
        {
            if (!Specs.TryGetValue(name, out var spec))
                return Fail($"Unknown tool '{name}'.");
            foreach (string argument in args.Keys)
            {
                if (!spec.Arguments.Contains(argument) && !argument.StartsWith("_", StringComparison.Ordinal))
                {
                    string expected = spec.Arguments.Count > 0 ? string.Join(", ", spec.Arguments) : "none";
                    return Fail($"Unknown argument '{argument}' for {name}. Expected: {expected}.");
                }
            }
            try
            {
                switch (name)
                {
                    case "list_languages": return ListLanguages(store);
                    case "list_keys": return ListKeys(args, store);
                    case "get_key": return GetKey(args, store);
                    case "get_keys": return GetKeys(args, store);
                    case "get_language": return GetLanguage(args, store);
                    case "get_untranslated": return GetUntranslated(args, store);
                    case "set_translation": return SetTranslation(args, store);
                    case "set_translations": return SetTranslations(args, store);
                    case "add_key": return AddKey(args, store);
                    case "add_keys": return AddKeys(args, store);
                    case "rename_key": return RenameKey(args, store);
                    case "delete_key": return DeleteKeys(new List<string> { McpJson.RequireString(args, "key") }, store);
                    case "delete_keys": return DeleteKeys(RequireStringList(args, "keys"), store);
                    case "add_language": return AddLanguage(args, store);
                    case "remove_language": return RemoveLanguage(args, store);
                    case "validate": return Validate(args, store);
                    default: return Fail($"Unknown tool '{name}'.");
                }
            }
            catch (FormatException ex)
            {
                return Fail(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Fail(ex.Message);
            }
        }

        private static (bool, object) Ok(object result) => (false, result);

        private static (bool, object) Fail(string message) => (true, Obj("error", message));

        #endregion

        #region Read tools

        private static (bool, object) ListLanguages(McpLocalesStore store)
        {
            var snap = store.Snapshot();
            var io = store.IO;
            var languages = new List<object>(snap.LanguageCount);
            foreach (string lang in snap.Languages)
            {
                languages.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["code"] = lang,
                    ["name"] = io.GetLanguageName(lang),
                    ["rtl"] = io.IsRightToLeft(lang),
                    ["keys"] = (long)snap.GetLanguage(lang).Count,
                    ["empty"] = (long)snap.CountEmpty(lang),
                });
            }
            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["default"] = snap.ResolveLanguage(store.DefaultLanguage) ?? store.DefaultLanguage,
                ["totalKeys"] = (long)snap.Keys.Count,
                ["languages"] = languages,
            };
            if (snap.FileIssues.Count > 0)
                result["fileIssues"] = FileIssues(snap);
            return Ok(result);
        }

        private static (bool, object) ListKeys(Dictionary<string, object> args, McpLocalesStore store)
        {
            var snap = store.Snapshot();
            var filter = new KeyFilter(args);
            int limit = Limit(args, DefaultListLimit, MaxListLimit);
            int offset = Offset(args);
            var page = new List<object>();
            int total = 0;
            foreach (string key in snap.Keys)
            {
                if (!filter.Matches(key)) continue;
                if (total++ < offset) continue;
                if (page.Count < limit) page.Add(key);
            }
            return Ok(Page(total, offset, page.Count, "keys", page));
        }

        private static (bool, object) GetKey(Dictionary<string, object> args, McpLocalesStore store)
        {
            var snap = store.Snapshot();
            string key = McpJson.RequireString(args, "key");
            if (!snap.HasKey(key))
                return Fail(McpLocalesStore.UnknownKeyMessage(snap, key));
            var langs = RequestedLanguages(args, snap);
            var translations = new Dictionary<string, object>(StringComparer.Ordinal);
            var missing = new List<object>();
            foreach (string lang in langs)
            {
                if (snap.GetLanguage(lang).TryGetValue(key, out object value))
                    translations[lang] = value;
                else
                    missing.Add(lang);
            }
            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["key"] = key,
                ["type"] = McpValues.TypeName(snap.ReferenceValue(key)),
                ["translations"] = translations,
            };
            if (missing.Count > 0) result["missingIn"] = missing;
            return Ok(result);
        }

        private static (bool, object) GetKeys(Dictionary<string, object> args, McpLocalesStore store)
        {
            var snap = store.Snapshot();
            var keys = RequireStringList(args, "keys");
            if (keys.Count > MaxBatchItems)
                return Fail($"Too many keys ({keys.Count}). Max {MaxBatchItems} per call; split into multiple calls.");
            var langs = RequestedLanguages(args, snap);
            var found = new Dictionary<string, object>(StringComparer.Ordinal);
            var unknown = new List<object>();
            foreach (string key in keys)
            {
                if (found.ContainsKey(key)) continue;
                if (!snap.HasKey(key))
                {
                    unknown.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["key"] = key,
                        ["didYouMean"] = new List<object>(McpLocalesStore.SuggestSimilarKeys(snap, key, 3)),
                    });
                    continue;
                }
                var translations = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (string lang in langs)
                {
                    if (snap.GetLanguage(lang).TryGetValue(key, out object value))
                        translations[lang] = value;
                }
                found[key] = translations;
            }
            var result = Obj("translations", found);
            if (unknown.Count > 0) result["unknown"] = unknown;
            return Ok(result);
        }

        private static (bool, object) GetLanguage(Dictionary<string, object> args, McpLocalesStore store)
        {
            var snap = store.Snapshot();
            string lang = RequireLanguage(args, "lang", snap);
            var data = snap.GetLanguage(lang);
            var filter = new KeyFilter(args);
            bool emptyOnly = McpJson.GetBool(args, "emptyOnly");
            int limit = Limit(args, DefaultLanguageLimit, MaxLanguageLimit);
            int offset = Offset(args);

            IEnumerable<string> candidates = snap.Keys;
            var requested = OptionalStringList(args, "keys");
            if (requested != null)
            {
                var unique = new HashSet<string>(StringComparer.Ordinal);
                requested.RemoveAll(k => !unique.Add(k));
                candidates = requested;
            }

            var translations = new Dictionary<string, object>(StringComparer.Ordinal);
            var notFound = new List<object>();
            int total = 0;
            foreach (string key in candidates)
            {
                if (!data.TryGetValue(key, out object value))
                {
                    if (requested != null) notFound.Add(key);
                    continue;
                }
                if (!filter.Matches(key)) continue;
                if (emptyOnly && !McpValues.NeedsTranslation(value)) continue;
                if (total++ < offset) continue;
                if (translations.Count < limit) translations[key] = value;
            }
            var result = Page(total, offset, translations.Count, "translations", translations);
            result["lang"] = lang;
            if (notFound.Count > 0) result["notFound"] = notFound;
            return Ok(result);
        }

        private static (bool, object) GetUntranslated(Dictionary<string, object> args, McpLocalesStore store)
        {
            var snap = store.Snapshot();
            string lang = RequireLanguage(args, "lang", snap);
            string sourceLang = McpJson.GetString(args, "sourceLang") is string s
                ? RequireLanguageCode(s, snap)
                : snap.ResolveLanguage(store.DefaultLanguage) ?? throw new InvalidOperationException(
                    $"The project default language '{store.DefaultLanguage}' has no locale file; pass 'sourceLang'.");
            if (string.Equals(lang, sourceLang, StringComparison.OrdinalIgnoreCase))
                return Fail($"'lang' and 'sourceLang' are both '{lang}'. Pick a different source language.");

            var target = snap.GetLanguage(lang);
            var source = snap.GetLanguage(sourceLang);
            var filter = new KeyFilter(args);
            int limit = Limit(args, DefaultUntranslatedLimit, MaxUntranslatedLimit);
            int offset = Offset(args);

            var items = new Dictionary<string, object>(StringComparer.Ordinal);
            var current = new Dictionary<string, object>(StringComparer.Ordinal);
            int total = 0;
            int noSource = 0;
            foreach (string key in snap.Keys)
            {
                if (!filter.Matches(key)) continue;
                target.TryGetValue(key, out object targetValue);
                if (!McpValues.NeedsTranslation(targetValue)) continue;
                if (!source.TryGetValue(key, out object sourceValue) || McpValues.IsEmpty(sourceValue))
                {
                    noSource++;
                    continue;
                }
                if (total++ < offset) continue;
                if (items.Count >= limit) continue;
                items[key] = sourceValue;
                if (targetValue is List<string> && !McpValues.IsEmpty(targetValue))
                    current[key] = targetValue;
            }
            var result = Page(total, offset, items.Count, "items", items);
            result["lang"] = lang;
            result["sourceLang"] = sourceLang;
            if (current.Count > 0) result["current"] = current;
            if (noSource > 0) result["noSource"] = (long)noSource;
            return Ok(result);
        }

        private static (bool, object) Validate(Dictionary<string, object> args, McpLocalesStore store)
        {
            var snap = store.Snapshot();
            int limit = Limit(args, DefaultValidateLimit, MaxListLimit);
            string emptyLang = McpJson.GetString(args, "lang") is string l ? RequireLanguageCode(l, snap) : null;

            var missing = new BoundedList(limit);
            var typeConflicts = new BoundedList(limit);
            var lengthMismatches = new BoundedList(limit);
            var caseConflicts = new BoundedList(limit);
            var formatMismatches = new BoundedList(limit);
            string sourceLang = snap.ResolveLanguage(store.DefaultLanguage);
            var sourceData = sourceLang != null ? snap.GetLanguage(sourceLang) : null;
            var seenIgnoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var languages = snap.Languages;

            foreach (string key in snap.Keys)
            {
                if (seenIgnoreCase.TryGetValue(key, out string other))
                    caseConflicts.Add($"'{other}' and '{key}' differ only by case.");
                else
                    seenIgnoreCase[key] = key;

                List<object> missingIn = null;
                List<object> stringLangs = null, arrayLangs = null;
                Dictionary<string, object> lengths = null;
                int firstLength = -1;
                bool lengthMismatch = false;
                foreach (string lang in languages)
                {
                    if (!snap.GetLanguage(lang).TryGetValue(key, out object value))
                    {
                        (missingIn ??= new List<object>()).Add(lang);
                        continue;
                    }
                    if (value is List<string> list)
                    {
                        (arrayLangs ??= new List<object>()).Add(lang);
                        (lengths ??= new Dictionary<string, object>(StringComparer.Ordinal))[lang] = (long)list.Count;
                        if (firstLength < 0) firstLength = list.Count;
                        else if (firstLength != list.Count) lengthMismatch = true;
                    }
                    else
                    {
                        (stringLangs ??= new List<object>()).Add(lang);
                    }
                }
                if (missingIn != null)
                    missing.Add(Obj("key", key, "missingIn", missingIn));
                if (stringLangs != null && arrayLangs != null)
                    typeConflicts.Add(Obj("key", key, "string", stringLangs, "array", arrayLangs));
                if (lengthMismatch)
                    lengthMismatches.Add(Obj("key", key, "lengths", lengths));

                if (sourceData != null && sourceData.TryGetValue(key, out object sourceValue))
                    CheckFormat(key, sourceLang, sourceValue, snap, formatMismatches);
            }

            var emptyByLanguage = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (string lang in languages)
                emptyByLanguage[lang] = (long)snap.CountEmpty(lang);

            bool ok = snap.FileIssues.Count == 0 && missing.Total == 0 && typeConflicts.Total == 0 &&
                      lengthMismatches.Total == 0 && caseConflicts.Total == 0 && formatMismatches.Total == 0;
            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["ok"] = ok,
                ["languages"] = new List<object>(languages),
                ["totalKeys"] = (long)snap.Keys.Count,
                ["emptyByLanguage"] = emptyByLanguage,
            };
            if (snap.FileIssues.Count > 0) result["fileIssues"] = FileIssues(snap);
            missing.WriteTo(result, "missingKeys");
            typeConflicts.WriteTo(result, "typeConflicts");
            lengthMismatches.WriteTo(result, "arrayLengthMismatches");
            caseConflicts.WriteTo(result, "caseConflicts");
            formatMismatches.WriteTo(result, "formatMismatches");

            if (emptyLang != null)
            {
                var emptyKeys = new BoundedList(limit);
                var data = snap.GetLanguage(emptyLang);
                foreach (string key in snap.Keys)
                {
                    if (data.TryGetValue(key, out object value) && McpValues.IsEmpty(value))
                        emptyKeys.Add(key);
                }
                result["emptyKeysLang"] = emptyLang;
                emptyKeys.WriteTo(result, "emptyKeys", always: true);
            }
            return Ok(result);
        }

        /// <summary>
        /// Reports translations whose placeholders or rich-text tags differ from the source language.
        /// Array items are compared by index.
        /// </summary>
        private static void CheckFormat(string key, string sourceLang, object sourceValue, McpLocaleSnapshot snap, BoundedList mismatches)
        {
            foreach (string lang in snap.Languages)
            {
                if (lang == sourceLang || !snap.GetLanguage(lang).TryGetValue(key, out object value))
                    continue;

                if (sourceValue is string sourceText && value is string text)
                {
                    string problem = McpTextChecks.FindMismatch(sourceText, text);
                    if (problem != null)
                        mismatches.Add(Obj("key", key, "lang", lang, "problem", problem));
                }
                else if (sourceValue is List<string> sourceItems && value is List<string> items)
                {
                    int count = Math.Min(sourceItems.Count, items.Count);
                    for (int i = 0; i < count; i++)
                    {
                        string problem = McpTextChecks.FindMismatch(sourceItems[i], items[i]);
                        if (problem != null)
                            mismatches.Add(Obj("key", key, "lang", lang, "index", (long)i, "problem", problem));
                    }
                }
            }
        }

        private static List<object> FileIssues(McpLocaleSnapshot snap)
        {
            var list = new List<object>(snap.FileIssues.Count);
            foreach (var issue in snap.FileIssues)
                list.Add(Obj("file", issue.File, "problem", issue.Problem));
            return list;
        }

        #endregion

        #region Write tools

        private static (bool, object) SetTranslation(Dictionary<string, object> args, McpLocalesStore store)
        {
            string key = McpJson.RequireString(args, "key");
            string lang = McpJson.RequireString(args, "lang");
            object value = RequireValue(args, "value");
            var report = store.SetMany(new[] { new McpBatchItem(key, lang, value) });
            if (report.Errors.Count > 0)
                return Fail(report.Errors[0].Error);
            return Ok(Obj("ok", true, "key", key, "lang", lang, "changed", report.Applied > 0));
        }

        private static (bool, object) SetTranslations(Dictionary<string, object> args, McpLocalesStore store)
        {
            string defaultLang = McpJson.GetString(args, "lang");
            var values = OptionalObject(args, "values");
            var translations = McpJson.GetArray(args, "translations");
            if (values != null && defaultLang == null)
                return Fail("'values' needs 'lang'.");
            int count = (values?.Count ?? 0) + (translations?.Count ?? 0);
            if (count == 0)
                return Fail("Nothing to set. Pass 'lang' + 'values' ({key: value}) and/or 'translations' ([{key, lang, value}]).");
            if (count > MaxBatchItems)
                return Fail($"Too many cells ({count}). Max {MaxBatchItems} per call; split into multiple calls.");

            // Items that fail shape checks are reported with their request index; the rest go to the store.
            var batch = new List<McpBatchItem>(count);
            var batchIndex = new List<int>(count);
            var labels = new List<(string key, string lang)>(count);
            var shapeErrors = new List<McpItemError>();
            int index = 0;
            if (values != null)
            {
                foreach (var kvp in values)
                {
                    object value = McpValues.FromWire(kvp.Value);
                    if (value == null)
                        shapeErrors.Add(new McpItemError(index, kvp.Key, defaultLang, "Value must be a string or an array of strings."));
                    else
                    {
                        batch.Add(new McpBatchItem(kvp.Key, defaultLang, value));
                        batchIndex.Add(index);
                    }
                    index++;
                }
            }
            if (translations != null)
            {
                foreach (object raw in translations)
                {
                    string error = null;
                    string key = null, lang = defaultLang;
                    object value = null;
                    if (raw is Dictionary<string, object> entry)
                    {
                        key = entry.TryGetValue("key", out object k) ? k as string : null;
                        if (entry.TryGetValue("lang", out object lg) && lg is string ls) lang = ls;
                        value = entry.TryGetValue("value", out object v) ? McpValues.FromWire(v) : null;
                        if (string.IsNullOrEmpty(key)) error = "Item needs 'key' (string).";
                        else if (string.IsNullOrEmpty(lang)) error = "Item needs 'lang' (or a top-level 'lang').";
                        else if (value == null) error = "Item needs 'value' (string or array of strings).";
                    }
                    else
                    {
                        error = "Item must be an object with key, lang and value.";
                    }
                    if (error != null)
                        shapeErrors.Add(new McpItemError(index, key, lang, error));
                    else
                    {
                        batch.Add(new McpBatchItem(key, lang, value));
                        batchIndex.Add(index);
                    }
                    index++;
                }
            }

            var report = batch.Count > 0 ? store.SetMany(batch) : new McpBatchReport();
            var errors = new List<McpItemError>(shapeErrors);
            foreach (var e in report.Errors)
                errors.Add(new McpItemError(batchIndex[e.Index], e.Key, e.Lang, e.Error));
            errors.Sort((a, b) => a.Index.CompareTo(b.Index));

            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["applied"] = (long)report.Applied,
                ["unchanged"] = (long)report.Unchanged,
                ["failed"] = (long)errors.Count,
            };
            if (errors.Count > 0) result["errors"] = ErrorList(errors, includeLang: true);
            return (report.Applied == 0 && report.Unchanged == 0 && errors.Count > 0, result);
        }

        private static (bool, object) AddKey(Dictionary<string, object> args, McpLocalesStore store)
        {
            string key = McpJson.RequireString(args, "key");
            string type = McpJson.GetString(args, "type") ?? "string";
            string defaultText = McpJson.GetString(args, "defaultText");
            var item = new McpNewKey { Key = key, Type = type };
            if (defaultText != null)
            {
                string defaultLang = McpJson.GetString(args, "defaultLang", store.DefaultLanguage);
                object value = type == "array" ? new List<string> { defaultText } : (object)defaultText;
                item.Values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { [defaultLang] = value };
            }
            var report = store.AddKeys(new[] { item });
            if (report.Errors.Count > 0)
                return Fail(report.Errors[0].Error);
            return Ok(Obj("ok", true, "key", key.Trim(), "type", type));
        }

        private static (bool, object) AddKeys(Dictionary<string, object> args, McpLocalesStore store)
        {
            var raw = McpJson.GetArray(args, "keys");
            if (raw == null || raw.Count == 0)
                return Fail("Missing required parameter 'keys' (non-empty array of {key, type?, values?}).");
            if (raw.Count > MaxBatchItems)
                return Fail($"Too many keys ({raw.Count}). Max {MaxBatchItems} per call; split into multiple calls.");

            var items = new List<McpNewKey>(raw.Count);
            var shapeErrors = new Dictionary<int, string>();
            for (int i = 0; i < raw.Count; i++)
            {
                var item = new McpNewKey();
                items.Add(item);
                if (!(raw[i] is Dictionary<string, object> entry))
                {
                    shapeErrors[i] = "Item must be an object with 'key' and optional 'type' and 'values'.";
                    continue;
                }
                item.Key = entry.TryGetValue("key", out object k) ? k as string : null;
                item.Type = entry.TryGetValue("type", out object t) ? t as string : null;
                if (entry.TryGetValue("values", out object v) && v != null)
                {
                    if (!(v is Dictionary<string, object> map))
                    {
                        shapeErrors[i] = "'values' must be an object of language -> value.";
                        continue;
                    }
                    item.Values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in map)
                    {
                        object value = McpValues.FromWire(kvp.Value);
                        if (value == null)
                        {
                            shapeErrors[i] = $"Value for '{kvp.Key}' must be a string or an array of strings.";
                            break;
                        }
                        item.Values[kvp.Key] = value;
                    }
                }
                foreach (string field in entry.Keys)
                {
                    if (field != "key" && field != "type" && field != "values")
                        shapeErrors[i] = $"Unknown field '{field}'. Items take key, type and values.";
                }
            }

            // Malformed items are not sent to the store.
            var valid = new List<McpNewKey>();
            var validIndex = new List<int>();
            for (int i = 0; i < items.Count; i++)
            {
                if (shapeErrors.ContainsKey(i)) continue;
                valid.Add(items[i]);
                validIndex.Add(i);
            }
            var report = valid.Count > 0 ? store.AddKeys(valid) : new McpBatchReport();
            var errors = new List<McpItemError>();
            foreach (var e in shapeErrors)
                errors.Add(new McpItemError(e.Key, items[e.Key].Key, null, e.Value));
            foreach (var e in report.Errors)
                errors.Add(new McpItemError(validIndex[e.Index], e.Key, null, e.Error));
            errors.Sort((a, b) => a.Index.CompareTo(b.Index));

            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["created"] = (long)report.Applied,
                ["failed"] = (long)errors.Count,
            };
            if (errors.Count > 0) result["errors"] = ErrorList(errors, includeLang: false);
            return (report.Applied == 0 && errors.Count > 0, result);
        }

        private static (bool, object) RenameKey(Dictionary<string, object> args, McpLocalesStore store)
        {
            string oldKey = McpJson.RequireString(args, "oldKey");
            string newKey = McpJson.RequireString(args, "newKey").Trim();
            string error = store.RenameKey(oldKey, newKey);
            if (error != null)
                return Fail(error);
            return Ok(Obj("ok", true, "oldKey", oldKey, "newKey", newKey,
                "note", $"Update scenes, prefabs and scripts that reference '{oldKey}'."));
        }

        private static (bool, object) DeleteKeys(List<string> keys, McpLocalesStore store)
        {
            if (keys.Count == 0)
                return Fail("Missing required parameter 'keys' (non-empty array).");
            if (keys.Count > MaxBatchItems)
                return Fail($"Too many keys ({keys.Count}). Max {MaxBatchItems} per call; split into multiple calls.");
            var report = store.DeleteKeys(keys);
            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["deleted"] = (long)report.Applied,
                ["failed"] = (long)report.Errors.Count,
            };
            if (report.Errors.Count > 0) result["errors"] = ErrorList(report.Errors, includeLang: false);
            return (report.Applied == 0, result);
        }

        private static (bool, object) AddLanguage(Dictionary<string, object> args, McpLocalesStore store)
        {
            string code = store.AddLanguage(McpJson.RequireString(args, "lang"));
            return Ok(Obj("ok", true, "lang", code, "name", store.IO.GetLanguageName(code), "rtl", store.IO.IsRightToLeft(code)));
        }

        private static (bool, object) RemoveLanguage(Dictionary<string, object> args, McpLocalesStore store)
        {
            string code = store.RemoveLanguage(McpJson.RequireString(args, "lang"));
            return Ok(Obj("ok", true, "lang", code));
        }

        #endregion

        #region Helpers

        private static Dictionary<string, object> Obj(params object[] pairs)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                d[(string)pairs[i]] = pairs[i + 1];
            return d;
        }

        private static Dictionary<string, object> Page(int total, int offset, int returned, string field, object items)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["total"] = (long)total,
                ["offset"] = (long)offset,
                [field] = items,
            };
            if (offset + returned < total)
                result["nextOffset"] = (long)(offset + returned);
            return result;
        }

        private static List<object> ErrorList(List<McpItemError> errors, bool includeLang)
        {
            var list = new List<object>(errors.Count);
            foreach (var e in errors)
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal) { ["index"] = (long)e.Index };
                if (e.Key != null) d["key"] = e.Key;
                if (includeLang && e.Lang != null) d["lang"] = e.Lang;
                d["error"] = e.Error;
                list.Add(d);
            }
            return list;
        }

        private static int Limit(Dictionary<string, object> args, int defaultValue, int max)
        {
            int limit = McpJson.GetInt(args, "limit", defaultValue);
            if (limit <= 0) return defaultValue;
            return limit > max ? max : limit;
        }

        private static int Offset(Dictionary<string, object> args)
        {
            int offset = McpJson.GetInt(args, "offset", 0);
            return offset < 0 ? 0 : offset;
        }

        private static string RequireLanguage(Dictionary<string, object> args, string name, McpLocaleSnapshot snap)
        {
            return RequireLanguageCode(McpJson.RequireString(args, name), snap);
        }

        private static string RequireLanguageCode(string code, McpLocaleSnapshot snap)
        {
            return snap.ResolveLanguage(code) ?? throw new InvalidOperationException(McpLocalesStore.UnknownLanguageMessage(snap, code));
        }

        /// <summary>Resolved language codes from the optional 'langs' argument; all languages when absent.</summary>
        private static IReadOnlyList<string> RequestedLanguages(Dictionary<string, object> args, McpLocaleSnapshot snap)
        {
            var raw = OptionalStringList(args, "langs");
            if (raw == null || raw.Count == 0) return snap.Languages;
            var result = new List<string>(raw.Count);
            foreach (string code in raw)
            {
                string actual = RequireLanguageCode(code, snap);
                if (!result.Contains(actual)) result.Add(actual);
            }
            return result;
        }

        private static object RequireValue(Dictionary<string, object> args, string name)
        {
            if (!args.TryGetValue(name, out object raw) || raw == null)
                throw new FormatException($"Missing required parameter '{name}'.");
            return McpValues.FromWire(raw) ?? throw new FormatException($"'{name}' must be a string or an array of strings.");
        }

        private static List<string> RequireStringList(Dictionary<string, object> args, string name)
        {
            return OptionalStringList(args, name) ?? throw new FormatException($"Missing required parameter '{name}'.");
        }

        private static List<string> OptionalStringList(Dictionary<string, object> args, string name)
        {
            var raw = McpJson.GetArray(args, name);
            if (raw == null) return null;
            var list = new List<string>(raw.Count);
            foreach (object item in raw)
            {
                if (!(item is string s))
                    throw new FormatException($"'{name}' must be an array of strings.");
                list.Add(s);
            }
            return list;
        }

        private static Dictionary<string, object> OptionalObject(Dictionary<string, object> args, string name)
        {
            if (!args.TryGetValue(name, out object raw) || raw == null) return null;
            return raw as Dictionary<string, object> ?? throw new FormatException($"'{name}' must be a JSON object.");
        }

        /// <summary>Key filter shared by list_keys, get_language and get_untranslated.</summary>
        private readonly struct KeyFilter
        {
            private readonly string _search;
            private readonly string _view;

            public KeyFilter(Dictionary<string, object> args)
            {
                _search = McpJson.GetString(args, "search");
                _view = McpJson.GetString(args, "view");
                if (_search != null && _search.Length == 0) _search = null;
                if (_view != null)
                {
                    _view = _view.TrimEnd('.', '_');
                    if (_view.Length == 0) _view = null;
                }
            }

            public bool Matches(string key)
            {
                if (_search != null && key.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
                if (_view != null && !IsInView(key, _view))
                    return false;
                return true;
            }

            private static bool IsInView(string key, string view)
            {
                if (key.Length == view.Length)
                    return string.Equals(key, view, StringComparison.OrdinalIgnoreCase);
                if (key.Length < view.Length || !key.StartsWith(view, StringComparison.OrdinalIgnoreCase))
                    return false;
                char next = key[view.Length];
                return next == '.' || next == '_';
            }
        }

        /// <summary>List that keeps the first N entries but counts all of them.</summary>
        private sealed class BoundedList
        {
            private readonly int _limit;
            public readonly List<object> Items = new();
            public int Total;

            public BoundedList(int limit)
            {
                _limit = limit;
            }

            public void Add(object item)
            {
                Total++;
                if (Items.Count < _limit) Items.Add(item);
            }

            public void WriteTo(Dictionary<string, object> result, string name, bool always = false)
            {
                if (Total == 0 && !always) return;
                result[name] = Items;
                if (Total > Items.Count)
                    result[name + "Total"] = (long)Total;
            }
        }

        #endregion
    }
}
