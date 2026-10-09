using System;
using System.Collections.Generic;

namespace PicoShot.Localization.Editor.Mcp
{
    /// <summary>
    /// File I/O abstraction for locale storage.
    /// </summary>
    public interface IMcpLocaleIO
    {
        /// <summary>
        /// Current locale state. Implementations may cache it, so callers must treat the snapshot
        /// and every dictionary and list inside it as read-only.
        /// </summary>
        McpLocaleSnapshot Load();

        /// <summary>
        /// Writes one language. Takes ownership of <paramref name="keys"/>; callers must not modify it afterwards.
        /// Throws when the write would overwrite a locale file that failed to load.
        /// </summary>
        void SaveLanguage(string languageCode, Dictionary<string, object> keys);

        void DeleteLanguage(string languageCode);

        /// <summary>Canonical spelling of a supported language code, or null when unsupported.</summary>
        string NormalizeLanguage(string languageCode);

        string GetLanguageName(string languageCode);

        bool IsRightToLeft(string languageCode);

        string DefaultLanguage { get; }
    }

    /// <summary>A locale file that exists on disk but could not be loaded.</summary>
    public readonly struct McpFileIssue
    {
        public readonly string File;
        /// <summary>Language code the file name claims (file name without extension).</summary>
        public readonly string FileLanguage;
        public readonly string Problem;

        public McpFileIssue(string file, string fileLanguage, string problem)
        {
            File = file;
            FileLanguage = fileLanguage;
            Problem = problem;
        }
    }

    /// <summary>
    /// Immutable view of all loaded languages. Lookup indexes are built lazily and shared by all readers.
    /// </summary>
    public sealed class McpLocaleSnapshot
    {
        public static readonly McpLocaleSnapshot Empty = new(
            new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase),
            Array.Empty<McpFileIssue>(), null);

        private readonly Dictionary<string, Dictionary<string, object>> _languages;
        private readonly string _defaultLanguage;
        private readonly object _indexLock = new();
        private List<string> _sortedLanguages;
        private List<string> _sortedKeys;
        private HashSet<string> _keySet;
        private Dictionary<string, string> _keysIgnoreCase;
        private Dictionary<string, int> _emptyCounts;

        /// <param name="languages">Language code -> (key -> string or List&lt;string&gt;). Must use a case-insensitive comparer.</param>
        public McpLocaleSnapshot(
            Dictionary<string, Dictionary<string, object>> languages,
            IReadOnlyList<McpFileIssue> fileIssues,
            string defaultLanguage)
        {
            _languages = languages ?? throw new ArgumentNullException(nameof(languages));
            FileIssues = fileIssues ?? Array.Empty<McpFileIssue>();
            _defaultLanguage = defaultLanguage;
        }

        public IReadOnlyList<McpFileIssue> FileIssues { get; }

        public int LanguageCount => _languages.Count;

        /// <summary>Loaded language codes, sorted.</summary>
        public IReadOnlyList<string> Languages
        {
            get { EnsureIndex(); return _sortedLanguages; }
        }

        /// <summary>Union of keys across all languages, sorted ordinally.</summary>
        public IReadOnlyList<string> Keys
        {
            get { EnsureIndex(); return _sortedKeys; }
        }

        /// <summary>Loaded spelling of <paramref name="code"/> (case-insensitive), or null.</summary>
        public string ResolveLanguage(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            EnsureIndex();
            foreach (string lang in _sortedLanguages)
            {
                if (string.Equals(lang, code, StringComparison.OrdinalIgnoreCase))
                    return lang;
            }
            return null;
        }

        /// <summary>Keys of one loaded language (exact code from <see cref="ResolveLanguage"/>), or null.</summary>
        public Dictionary<string, object> GetLanguage(string code)
        {
            if (code == null) return null;
            return _languages.TryGetValue(code, out var data) ? data : null;
        }

        /// <summary>True when some language contains exactly this key.</summary>
        public bool HasKey(string key)
        {
            if (key == null) return false;
            EnsureIndex();
            return _keySet.Contains(key);
        }

        /// <summary>Existing key equal to <paramref name="key"/> ignoring case, or null.</summary>
        public string FindKeyIgnoreCase(string key)
        {
            if (key == null) return null;
            EnsureIndex();
            return _keysIgnoreCase.TryGetValue(key, out string actual) ? actual : null;
        }

        /// <summary>
        /// Value that defines a key's type and array length: the default language's value when present,
        /// otherwise the first language (sorted) that has the key. Null when no language has it.
        /// </summary>
        public object ReferenceValue(string key)
        {
            if (_defaultLanguage != null && _languages.TryGetValue(_defaultLanguage, out var def) &&
                def.TryGetValue(key, out object value))
                return value;
            EnsureIndex();
            foreach (string lang in _sortedLanguages)
            {
                if (_languages[lang].TryGetValue(key, out value))
                    return value;
            }
            return null;
        }

        /// <summary>Empty or whitespace-only cells in one language (keys missing from the file are not counted).</summary>
        public int CountEmpty(string lang)
        {
            lock (_indexLock)
            {
                _emptyCounts ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (_emptyCounts.TryGetValue(lang, out int cached))
                    return cached;
                int count = 0;
                if (_languages.TryGetValue(lang, out var data))
                {
                    foreach (var kvp in data)
                    {
                        if (McpValues.IsEmpty(kvp.Value))
                            count++;
                    }
                }
                _emptyCounts[lang] = count;
                return count;
            }
        }

        private void EnsureIndex()
        {
            lock (_indexLock)
            {
                if (_sortedKeys != null) return;
                var languages = new List<string>(_languages.Keys);
                languages.Sort(StringComparer.OrdinalIgnoreCase);
                var set = new HashSet<string>(StringComparer.Ordinal);
                var ignoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string lang in languages)
                {
                    foreach (string key in _languages[lang].Keys)
                    {
                        if (set.Add(key) && !ignoreCase.ContainsKey(key))
                            ignoreCase[key] = key;
                    }
                }
                var keys = new List<string>(set);
                keys.Sort(StringComparer.Ordinal);
                _sortedLanguages = languages;
                _keySet = set;
                _keysIgnoreCase = ignoreCase;
                _sortedKeys = keys;
            }
        }
    }

    /// <summary>Helpers for translation values: a string, or a List&lt;string&gt; for array keys.</summary>
    public static class McpValues
    {
        public static bool IsArray(object value) => value is List<string>;

        public static int ArrayLength(object value) => value is List<string> list ? list.Count : -1;

        public static bool IsEmpty(object value)
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
            return string.IsNullOrWhiteSpace(value as string);
        }

        /// <summary>True when the cell needs translating: missing, blank, or an array with a blank element.</summary>
        public static bool NeedsTranslation(object value)
        {
            if (value == null) return true;
            if (value is List<string> list)
            {
                if (list.Count == 0) return false;
                foreach (string s in list)
                {
                    if (string.IsNullOrWhiteSpace(s))
                        return true;
                }
                return false;
            }
            return string.IsNullOrWhiteSpace(value as string);
        }

        public static bool AreEqual(object a, object b)
        {
            if (a is List<string> la && b is List<string> lb)
            {
                if (la.Count != lb.Count) return false;
                for (int i = 0; i < la.Count; i++)
                {
                    if (!string.Equals(la[i], lb[i], StringComparison.Ordinal))
                        return false;
                }
                return true;
            }
            if (a is string sa && b is string sb)
                return string.Equals(sa, sb, StringComparison.Ordinal);
            return false;
        }

        /// <summary>Copies a storage value into the canonical shape (string, or List&lt;string&gt; without nulls).</summary>
        public static object Normalize(object value)
        {
            if (value is List<string> list)
            {
                var copy = new List<string>(list.Count);
                foreach (string s in list) copy.Add(s ?? string.Empty);
                return copy;
            }
            if (value is string[] arr)
            {
                var copy = new List<string>(arr.Length);
                foreach (string s in arr) copy.Add(s ?? string.Empty);
                return copy;
            }
            return value?.ToString() ?? string.Empty;
        }

        /// <summary>An empty value with the same type and array length as <paramref name="reference"/>.</summary>
        public static object EmptyLike(object reference)
        {
            if (reference is List<string> list)
                return EmptyArray(list.Count);
            return string.Empty;
        }

        public static List<string> EmptyArray(int length)
        {
            var result = new List<string>(length);
            for (int i = 0; i < length; i++) result.Add(string.Empty);
            return result;
        }

        /// <summary>Converts wire JSON (string | array of strings) to a storage value. Null when invalid.</summary>
        public static object FromWire(object raw)
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

        public static string TypeName(object value) => value is List<string> ? "array" : "string";
    }

    /// <summary>Key naming rules shared with the Language Editor.</summary>
    public static class McpKeyRules
    {
        /// <summary>Returns an error message, or null when <paramref name="key"/> is a valid new key name.</summary>
        public static string Validate(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "Key must not be empty.";
            foreach (char c in key)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '.';
                if (!ok)
                    return $"Invalid key '{key}': character '{c}' is not allowed. Use letters, digits, '_' and '.' only (e.g. 'ui.play_button').";
            }
            if (key[0] == '.' || key[key.Length - 1] == '.' || key.Contains(".."))
                return $"Invalid key '{key}': '.' separates view segments and cannot start, end or repeat.";
            return null;
        }
    }

    /// <summary>One validated set request: key and lang are non-empty, value is a string or List&lt;string&gt;.</summary>
    public readonly struct McpBatchItem
    {
        public readonly string Key;
        public readonly string Lang;
        public readonly object Value;

        public McpBatchItem(string key, string lang, object value)
        {
            Key = key;
            Lang = lang;
            Value = value;
        }
    }

    /// <summary>One add_keys request item.</summary>
    public sealed class McpNewKey
    {
        public string Key;
        /// <summary>"string", "array", or null to infer from <see cref="Values"/>.</summary>
        public string Type;
        /// <summary>Language code -> initial value (string or List&lt;string&gt;). May be null.</summary>
        public Dictionary<string, object> Values;
    }

    /// <summary>A failed batch item, by its index in the request.</summary>
    public readonly struct McpItemError
    {
        public readonly int Index;
        public readonly string Key;
        public readonly string Lang;
        public readonly string Error;

        public McpItemError(int index, string key, string lang, string error)
        {
            Index = index;
            Key = key;
            Lang = lang;
            Error = error;
        }
    }

    public sealed class McpBatchReport
    {
        public int Applied;
        public int Unchanged;
        public readonly List<McpItemError> Errors = new();

        internal void Fail(int index, string key, string lang, string error)
        {
            Errors.Add(new McpItemError(index, key, lang, error));
        }

        internal void SortErrors()
        {
            Errors.Sort((a, b) => a.Index.CompareTo(b.Index));
        }
    }

    /// <summary>
    /// Thread-safe facade over locale storage. All writes are serialized and validated here.
    /// </summary>
    public sealed class McpLocalesStore
    {
        private static readonly object WriteLock = new();

        private readonly IMcpLocaleIO _io;

        public McpLocalesStore(IMcpLocaleIO io)
        {
            _io = io ?? throw new ArgumentNullException(nameof(io));
        }

        public string DefaultLanguage => _io.DefaultLanguage;

        public IMcpLocaleIO IO => _io;

        /// <summary>Current read-only state. Safe to use outside the lock.</summary>
        public McpLocaleSnapshot Snapshot()
        {
            lock (WriteLock)
            {
                return _io.Load();
            }
        }

        /// <summary>
        /// Applies many cell writes with a single load and at most one save per language.
        /// Enforces key type (string vs array) and equal array lengths across languages.
        /// </summary>
        public McpBatchReport SetMany(IList<McpBatchItem> items)
        {
            var report = new McpBatchReport();
            lock (WriteLock)
            {
                var snap = _io.Load();
                // lang -> key -> (value, item index)
                var staged = new Dictionary<string, Dictionary<string, (object value, int index)>>(StringComparer.OrdinalIgnoreCase);
                var stagedArrayKeys = new HashSet<string>(StringComparer.Ordinal);

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    string lang = snap.ResolveLanguage(item.Lang);
                    if (lang == null)
                    {
                        report.Fail(i, item.Key, item.Lang, UnknownLanguageMessage(snap, item.Lang));
                        continue;
                    }
                    if (!snap.HasKey(item.Key))
                    {
                        report.Fail(i, item.Key, lang, UnknownKeyMessage(snap, item.Key));
                        continue;
                    }
                    var langData = snap.GetLanguage(lang);
                    object reference = langData.TryGetValue(item.Key, out object existing) ? existing : snap.ReferenceValue(item.Key);
                    if (reference != null && McpValues.IsArray(reference) != McpValues.IsArray(item.Value))
                    {
                        report.Fail(i, item.Key, lang, McpValues.IsArray(reference)
                            ? $"'{item.Key}' is an array key; value must be an array of strings."
                            : $"'{item.Key}' is a string key; value must be a string.");
                        continue;
                    }
                    if (!staged.TryGetValue(lang, out var cells))
                    {
                        cells = new Dictionary<string, (object, int)>(StringComparer.Ordinal);
                        staged[lang] = cells;
                    }
                    if (cells.TryGetValue(item.Key, out var earlier))
                    {
                        report.Fail(i, item.Key, lang, $"Duplicate of item {earlier.index} (same key and language).");
                        continue;
                    }
                    cells[item.Key] = (item.Value, i);
                    if (McpValues.IsArray(item.Value))
                        stagedArrayKeys.Add(item.Key);
                }

                foreach (string key in stagedArrayKeys)
                {
                    string mismatch = ArrayLengthMismatch(snap, staged, key);
                    if (mismatch == null) continue;
                    foreach (var cells in staged)
                    {
                        if (cells.Value.TryGetValue(key, out var cell))
                        {
                            report.Fail(cell.index, key, cells.Key, mismatch);
                            cells.Value.Remove(key);
                        }
                    }
                }

                foreach (var cells in staged)
                {
                    if (cells.Value.Count == 0) continue;
                    var langData = snap.GetLanguage(cells.Key);
                    Dictionary<string, object> updated = null;
                    var written = new List<(string key, int index)>();
                    foreach (var cell in cells.Value)
                    {
                        if (langData.TryGetValue(cell.Key, out object existing) && McpValues.AreEqual(existing, cell.Value.value))
                        {
                            report.Unchanged++;
                            continue;
                        }
                        updated ??= new Dictionary<string, object>(langData, StringComparer.Ordinal);
                        updated[cell.Key] = McpValues.Normalize(cell.Value.value);
                        written.Add((cell.Key, cell.Value.index));
                    }
                    if (updated == null) continue;
                    try
                    {
                        _io.SaveLanguage(cells.Key, updated);
                        report.Applied += written.Count;
                    }
                    catch (Exception ex)
                    {
                        foreach (var w in written)
                            report.Fail(w.index, w.key, cells.Key, "Save failed: " + ex.Message);
                    }
                }
            }
            report.SortErrors();
            return report;
        }

        /// <summary>Returns an error when the staged writes would leave <paramref name="key"/> with differing array lengths.</summary>
        private static string ArrayLengthMismatch(
            McpLocaleSnapshot snap,
            Dictionary<string, Dictionary<string, (object value, int index)>> staged,
            string key)
        {
            var lengths = new List<string>();
            int first = -1;
            bool mismatch = false;
            foreach (string lang in snap.Languages)
            {
                object value;
                if (staged.TryGetValue(lang, out var cells) && cells.TryGetValue(key, out var cell))
                    value = cell.value;
                else if (!snap.GetLanguage(lang).TryGetValue(key, out value))
                    continue;
                int length = McpValues.ArrayLength(value);
                if (length < 0) continue;
                if (first < 0) first = length;
                else if (length != first) mismatch = true;
                lengths.Add(lang + "=" + length);
            }
            if (!mismatch) return null;
            return $"Array length mismatch for '{key}' after this write ({string.Join(", ", lengths)}). " +
                "Array keys must have the same length in every language; to resize, set the key in all languages in one call.";
        }

        /// <summary>Creates keys in every language with one load and one save per language.</summary>
        public McpBatchReport AddKeys(IList<McpNewKey> items)
        {
            var report = new McpBatchReport();
            lock (WriteLock)
            {
                var snap = _io.Load();
                if (snap.LanguageCount == 0)
                {
                    for (int i = 0; i < items.Count; i++)
                        report.Fail(i, items[i].Key, null, "No languages exist yet. Use add_language first.");
                    return report;
                }

                var accepted = new List<(int index, string key, Dictionary<string, object> values, object emptyValue)>();
                var claimed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    string key = item.Key?.Trim();
                    string error = McpKeyRules.Validate(key);
                    if (error == null)
                    {
                        string existing = snap.FindKeyIgnoreCase(key);
                        if (existing != null)
                            error = string.Equals(existing, key, StringComparison.Ordinal)
                                ? $"Key '{key}' already exists."
                                : $"Key '{key}' already exists as '{existing}' (keys are unique case-insensitively).";
                        else if (claimed.TryGetValue(key, out int earlier))
                            error = $"Duplicate of item {earlier} (keys are unique case-insensitively).";
                    }
                    if (error == null)
                    {
                        error = ResolveNewKeyValues(snap, item, out var resolved, out object emptyValue);
                        if (error == null)
                        {
                            claimed[key] = i;
                            accepted.Add((i, key, resolved, emptyValue));
                            continue;
                        }
                    }
                    report.Fail(i, key, null, error);
                }

                if (accepted.Count > 0)
                {
                    var failedLanguages = new List<string>();
                    foreach (string lang in snap.Languages)
                    {
                        var updated = new Dictionary<string, object>(snap.GetLanguage(lang), StringComparer.Ordinal);
                        foreach (var a in accepted)
                        {
                            updated[a.key] = a.values.TryGetValue(lang, out object v)
                                ? McpValues.Normalize(v)
                                : McpValues.Normalize(a.emptyValue);
                        }
                        try
                        {
                            _io.SaveLanguage(lang, updated);
                        }
                        catch (Exception ex)
                        {
                            failedLanguages.Add(lang + " (" + ex.Message + ")");
                        }
                    }
                    if (failedLanguages.Count == 0)
                    {
                        report.Applied = accepted.Count;
                    }
                    else
                    {
                        string message = "Save failed for " + string.Join(", ", failedLanguages) +
                            ". The key may exist in the other languages; run validate.";
                        foreach (var a in accepted)
                            report.Fail(a.index, a.key, null, message);
                    }
                }
            }
            report.SortErrors();
            return report;
        }

        private static string ResolveNewKeyValues(
            McpLocaleSnapshot snap, McpNewKey item,
            out Dictionary<string, object> resolved, out object emptyValue)
        {
            resolved = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            emptyValue = null;
            string type = item.Type;
            if (type != null && type != "string" && type != "array")
                return "'type' must be 'string' or 'array'.";

            bool? isArray = type == null ? (bool?)null : type == "array";
            int length = -1;
            if (item.Values != null)
            {
                foreach (var kvp in item.Values)
                {
                    string lang = snap.ResolveLanguage(kvp.Key);
                    if (lang == null)
                        return UnknownLanguageMessage(snap, kvp.Key);
                    object value = kvp.Value;
                    bool valueIsArray = McpValues.IsArray(value);
                    if (isArray == null) isArray = valueIsArray;
                    else if (isArray.Value != valueIsArray)
                        return $"Value for '{lang}' must be {(isArray.Value ? "an array of strings" : "a string")} (all values of a key share one type).";
                    if (valueIsArray)
                    {
                        int n = McpValues.ArrayLength(value);
                        if (length < 0) length = n;
                        else if (n != length)
                            return "All array values must have the same length.";
                    }
                    resolved[lang] = value;
                }
            }
            if (isArray == true)
            {
                if (length < 0) length = 1;
                emptyValue = McpValues.EmptyArray(length);
            }
            else
            {
                emptyValue = string.Empty;
            }
            return null;
        }

        /// <summary>Renames a key in every language, preserving entry order. Returns an error message or null.</summary>
        public string RenameKey(string oldKey, string newKey)
        {
            lock (WriteLock)
            {
                var snap = _io.Load();
                if (!snap.HasKey(oldKey))
                    return UnknownKeyMessage(snap, oldKey);
                string error = McpKeyRules.Validate(newKey);
                if (error != null) return error;
                if (string.Equals(oldKey, newKey, StringComparison.Ordinal))
                    return null;
                string conflict = snap.FindKeyIgnoreCase(newKey);
                if (conflict != null && !string.Equals(conflict, oldKey, StringComparison.Ordinal))
                    return $"Key '{newKey}' already exists{(conflict == newKey ? "" : " as '" + conflict + "'")} (keys are unique case-insensitively).";

                foreach (string lang in snap.Languages)
                {
                    var data = snap.GetLanguage(lang);
                    if (!data.ContainsKey(oldKey)) continue;
                    var updated = new Dictionary<string, object>(data.Count, StringComparer.Ordinal);
                    foreach (var entry in data)
                        updated[string.Equals(entry.Key, oldKey, StringComparison.Ordinal) ? newKey : entry.Key] = entry.Value;
                    _io.SaveLanguage(lang, updated);
                }
                return null;
            }
        }

        /// <summary>Deletes keys from every language with one save per language.</summary>
        public McpBatchReport DeleteKeys(IList<string> keys)
        {
            var report = new McpBatchReport();
            lock (WriteLock)
            {
                var snap = _io.Load();
                var toDelete = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < keys.Count; i++)
                {
                    string key = keys[i];
                    if (!snap.HasKey(key))
                        report.Fail(i, key, null, UnknownKeyMessage(snap, key));
                    else
                        toDelete.Add(key);
                }
                if (toDelete.Count > 0)
                {
                    try
                    {
                        foreach (string lang in snap.Languages)
                        {
                            var data = snap.GetLanguage(lang);
                            Dictionary<string, object> updated = null;
                            foreach (string key in toDelete)
                            {
                                if (!data.ContainsKey(key)) continue;
                                updated ??= new Dictionary<string, object>(data, StringComparer.Ordinal);
                                updated.Remove(key);
                            }
                            if (updated != null)
                                _io.SaveLanguage(lang, updated);
                        }
                        report.Applied = toDelete.Count;
                    }
                    catch (Exception ex)
                    {
                        for (int i = 0; i < keys.Count; i++)
                        {
                            if (toDelete.Contains(keys[i]))
                                report.Fail(i, keys[i], null, "Save failed: " + ex.Message + " Some languages may still contain the key; run validate.");
                        }
                    }
                }
            }
            report.SortErrors();
            return report;
        }

        /// <summary>Adds a language with every known key created empty. Returns the canonical code.</summary>
        public string AddLanguage(string languageCode)
        {
            lock (WriteLock)
            {
                string code = _io.NormalizeLanguage(languageCode?.Trim());
                if (code == null)
                    throw new InvalidOperationException($"Unsupported language code '{languageCode}'. Use codes such as 'de', 'pt-br' or 'zh-hans'.");
                var snap = _io.Load();
                string existing = snap.ResolveLanguage(code);
                if (existing != null)
                    throw new InvalidOperationException($"Language '{existing}' already exists.");
                foreach (var issue in snap.FileIssues)
                {
                    if (string.Equals(issue.FileLanguage, code, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            $"'{issue.File}' exists but could not be loaded ({issue.Problem}). Fix or remove that file first; adding the language would overwrite it.");
                }
                var fresh = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (string key in snap.Keys)
                    fresh[key] = McpValues.EmptyLike(snap.ReferenceValue(key));
                _io.SaveLanguage(code, fresh);
                return code;
            }
        }

        /// <summary>Removes a language file. Returns the removed code.</summary>
        public string RemoveLanguage(string languageCode)
        {
            lock (WriteLock)
            {
                var snap = _io.Load();
                string actual = snap.ResolveLanguage(languageCode);
                if (actual == null)
                    throw new InvalidOperationException(UnknownLanguageMessage(snap, languageCode));
                if (snap.LanguageCount <= 1)
                    throw new InvalidOperationException("Cannot remove the last remaining language.");
                if (string.Equals(actual, _io.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"'{actual}' is the project default language. Change the default in Language Editor > Settings before removing it.");
                _io.DeleteLanguage(actual);
                return actual;
            }
        }

        public static string UnknownLanguageMessage(McpLocaleSnapshot snap, string code)
        {
            string available = snap.LanguageCount > 0 ? " Available: " + string.Join(", ", snap.Languages) + "." : " No languages exist yet.";
            return $"Unknown language '{code}'.{available} Use add_language to create one.";
        }

        public static string UnknownKeyMessage(McpLocaleSnapshot snap, string key)
        {
            var similar = SuggestSimilarKeys(snap, key, 3);
            string hint = similar.Count > 0 ? $" Did you mean: {string.Join(", ", similar)}?" : " Use add_keys to create it.";
            return $"Unknown key '{key}'.{hint}";
        }

        public static List<string> SuggestSimilarKeys(McpLocaleSnapshot snap, string key, int max)
        {
            var result = new List<string>(max);
            if (string.IsNullOrEmpty(key)) return result;
            var exact = new List<string>();
            var prefix = new List<string>();
            var fuzzy = new List<KeyValuePair<int, string>>();
            var contains = new List<string>();
            foreach (string existing in snap.Keys)
            {
                if (string.Equals(existing, key, StringComparison.OrdinalIgnoreCase))
                    exact.Add(existing);
                else if (existing.StartsWith(key, StringComparison.OrdinalIgnoreCase) ||
                         key.StartsWith(existing, StringComparison.OrdinalIgnoreCase))
                    prefix.Add(existing);
                else if (IsCloseTypo(existing, key, out int distance))
                    fuzzy.Add(new KeyValuePair<int, string>(distance, existing));
                else if (existing.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         key.IndexOf(existing, StringComparison.OrdinalIgnoreCase) >= 0)
                    contains.Add(existing);
            }
            fuzzy.Sort((x, y) =>
            {
                int c = x.Key.CompareTo(y.Key);
                return c != 0 ? c : string.Compare(x.Value, y.Value, StringComparison.Ordinal);
            });
            AddRanked(result, exact, max);
            AddRanked(result, prefix, max);
            foreach (var candidate in fuzzy)
            {
                if (result.Count >= max) return result;
                result.Add(candidate.Value);
            }
            AddRanked(result, contains, max);
            return result;
        }

        private static bool IsCloseTypo(string existing, string key, out int distance)
        {
            distance = int.MaxValue;
            if (key.Length < 3 || existing.Length < 3) return false;
            distance = EditDistanceCapped(existing, key, 2);
            return distance <= 2 && distance * 2 < key.Length;
        }

        private static int EditDistanceCapped(string a, string b, int cap)
        {
            int n = a.Length, m = b.Length;
            if (Math.Abs(n - m) > cap) return cap + 1;
            if (n > 64 || m > 64) return cap + 1;
            Span<int> prev = stackalloc int[65];
            Span<int> curr = stackalloc int[65];
            prev = prev.Slice(0, m + 1);
            curr = curr.Slice(0, m + 1);
            for (int j = 0; j <= m; j++) prev[j] = j;
            for (int i = 1; i <= n; i++)
            {
                curr[0] = i;
                int rowMin = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                    int v = prev[j] + 1;
                    int ins = curr[j - 1] + 1;
                    if (ins < v) v = ins;
                    int sub = prev[j - 1] + cost;
                    if (sub < v) v = sub;
                    curr[j] = v;
                    if (v < rowMin) rowMin = v;
                }
                if (rowMin > cap) return cap + 1;
                Span<int> tmp = prev;
                prev = curr;
                curr = tmp;
            }
            return prev[m];
        }

        private static void AddRanked(List<string> result, List<string> bucket, int max)
        {
            bucket.Sort(StringComparer.Ordinal);
            foreach (var candidate in bucket)
            {
                if (result.Count >= max) return;
                result.Add(candidate);
            }
        }
    }
}
