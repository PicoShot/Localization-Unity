using System;
using System.Collections.Generic;

namespace PicoShot.Localization.Editor.Mcp
{
    /// <summary>
    /// File I/O abstraction for locale storage.
    /// </summary>
    public interface IMcpLocaleIO
    {
        Dictionary<string, Dictionary<string, object>> LoadAll();
        void SaveLanguage(string languageCode, Dictionary<string, object> keys);
        void DeleteLanguage(string languageCode);
        bool IsValidLanguage(string languageCode);
        string DefaultLanguage { get; }
    }

    /// <summary>One validated batch entry: key and lang are non-empty, value is string or List&lt;string&gt;.</summary>
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

    /// <summary>Per-item batch outcome. Error is null when Ok is true.</summary>
    public readonly struct McpBatchOutcome
    {
        public readonly bool Ok;
        public readonly string Key;
        public readonly string Lang;
        public readonly string Error;

        public McpBatchOutcome(bool ok, string key, string lang, string error)
        {
            Ok = ok;
            Key = key;
            Lang = lang;
            Error = error;
        }
    }

    /// <summary>
    /// Thread-safe facade over locale storage.
    /// </summary>
    public sealed class McpLocalesStore
    {
        private readonly IMcpLocaleIO _io;

        private static readonly object FileLock = new();

        public McpLocalesStore(IMcpLocaleIO io)
        {
            _io = io ?? throw new ArgumentNullException(nameof(io));
        }

        public string DefaultLanguage => _io.DefaultLanguage;

        public List<string> ListLanguages()
        {
            lock (FileLock)
            {
                var all = _io.LoadAll();
                var list = new List<string>(all.Keys);
                list.Sort(StringComparer.OrdinalIgnoreCase);
                return list;
            }
        }

        public Dictionary<string, Dictionary<string, object>> LoadAll()
        {
            lock (FileLock)
            {
                return _io.LoadAll();
            }
        }

        public Dictionary<string, object> GetKey(string key)
        {
            lock (FileLock)
            {
                var all = _io.LoadAll();
                Dictionary<string, object> result = null;
                foreach (var kvp in all)
                {
                    foreach (var existingKey in kvp.Value.Keys)
                    {
                        if (string.Equals(existingKey, key, StringComparison.Ordinal))
                        {
                            if (result == null)
                                result = new Dictionary<string, object>(StringComparer.Ordinal);
                            result[kvp.Key] = CloneValue(kvp.Value[existingKey]);
                            break;
                        }
                    }
                }
                return result;
            }
        }

        public bool ContainsKey(string key)
        {
            lock (FileLock)
            {
                var all = _io.LoadAll();
                foreach (var lang in all.Values)
                {
                    foreach (var existingKey in lang.Keys)
                    {
                        if (string.Equals(existingKey, key, StringComparison.Ordinal))
                            return true;
                    }
                }
                return false;
            }
        }

        public void SetTranslation(string key, string languageCode, object value)
        {
            lock (FileLock)
            {
                var all = _io.LoadAll();
                if (!all.TryGetValue(languageCode, out var langData))
                    throw new InvalidOperationException($"Unknown language '{languageCode}'.{AvailableSuffix(all)} Use add_language first.");
                string actualKey = FindKey(langData, key);
                if (actualKey == null)
                    throw new InvalidOperationException(UnknownKeyMessage(all, key));
                langData[actualKey] = CloneValue(value);
                _io.SaveLanguage(languageCode, langData);
            }
        }

        /// <summary>
        /// Applies a whole batch with a single load.
        /// </summary>
        public List<McpBatchOutcome> SetMany(IList<McpBatchItem> items)
        {
            var outcomes = new List<McpBatchOutcome>(items.Count);
            lock (FileLock)
            {
                var all = _io.LoadAll();
                var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    string actualLang = FindLanguage(all, item.Lang);
                    if (actualLang == null)
                    {
                        outcomes.Add(new McpBatchOutcome(false, item.Key, item.Lang,
                            $"Unknown language '{item.Lang}'.{AvailableSuffix(all)} Use add_language first."));
                        continue;
                    }
                    var langData = all[actualLang];
                    string actualKey = FindKey(langData, item.Key);
                    if (actualKey == null)
                    {
                        outcomes.Add(new McpBatchOutcome(false, item.Key, item.Lang,
                            UnknownKeyMessage(all, item.Key)));
                        continue;
                    }
                    langData[actualKey] = CloneValue(item.Value);
                    touched.Add(actualLang);
                    outcomes.Add(new McpBatchOutcome(true, item.Key, item.Lang, null));
                }

                foreach (string lang in touched)
                {
                    try
                    {
                        _io.SaveLanguage(lang, all[lang]);
                    }
                    catch (Exception ex)
                    {
                        for (int i = 0; i < items.Count; i++)
                        {
                            if (!outcomes[i].Ok) continue;
                            if (string.Equals(items[i].Lang, lang, StringComparison.OrdinalIgnoreCase))
                                outcomes[i] = new McpBatchOutcome(false, items[i].Key, items[i].Lang,
                                    $"Save failed: {ex.Message}");
                        }
                    }
                }
                return outcomes;
            }
        }

        public void AddKey(string key, bool isArray, string defaultText, string defaultLanguage)
        {
            lock (FileLock)
            {
                var all = _io.LoadAll();
                foreach (var langData in all.Values)
                {
                    if (ContainsKeyOrdinalIgnoreCase(langData, key))
                        throw new InvalidOperationException($"Key '{key}' already exists.");
                }
                foreach (var kvp in all)
                {
                    bool isDefault = string.Equals(kvp.Key, defaultLanguage, StringComparison.OrdinalIgnoreCase);
                    object value;
                    if (isArray)
                    {
                        value = isDefault && defaultText != null
                            ? new List<string> { defaultText }
                            : new List<string> { string.Empty };
                    }
                    else
                    {
                        value = isDefault ? (defaultText ?? string.Empty) : string.Empty;
                    }
                    var updated = new Dictionary<string, object>(kvp.Value, StringComparer.Ordinal)
                    {
                        [key] = value
                    };
                    _io.SaveLanguage(kvp.Key, updated);
                }
            }
        }

        public bool RenameKey(string oldKey, string newKey)
        {
            lock (FileLock)
            {
                var all = _io.LoadAll();
                foreach (var langData in all.Values)
                {
                    foreach (var k in langData.Keys)
                    {
                        if (!string.Equals(k, oldKey, StringComparison.Ordinal) &&
                            string.Equals(k, newKey, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Key '{newKey}' already exists.");
                    }
                }
                bool found = false;
                foreach (var kvp in all)
                {
                    string actualOld = FindKey(kvp.Value, oldKey);
                    if (actualOld == null) continue;
                    found = true;
                    var updated = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (var entry in kvp.Value)
                        updated[string.Equals(entry.Key, actualOld, StringComparison.Ordinal) ? newKey : entry.Key] = entry.Value;
                    _io.SaveLanguage(kvp.Key, updated);
                }
                return found;
            }
        }

        public bool DeleteKey(string key)
        {
            lock (FileLock)
            {
                var all = _io.LoadAll();
                bool found = false;
                foreach (var kvp in all)
                {
                    string actual = FindKey(kvp.Value, key);
                    if (actual == null) continue;
                    found = true;
                    var updated = new Dictionary<string, object>(kvp.Value, StringComparer.Ordinal);
                    updated.Remove(actual);
                    _io.SaveLanguage(kvp.Key, updated);
                }
                return found;
            }
        }

        public void AddLanguage(string languageCode)
        {
            lock (FileLock)
            {
                if (!_io.IsValidLanguage(languageCode))
                    throw new InvalidOperationException($"Unsupported language code '{languageCode}'.");
                var all = _io.LoadAll();
                foreach (var existing in all.Keys)
                {
                    if (string.Equals(existing, languageCode, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Language '{languageCode}' already exists.");
                }
                Dictionary<string, object> reference = null;
                foreach (var langData in all.Values)
                {
                    reference = langData;
                    break;
                }
                var fresh = new Dictionary<string, object>(StringComparer.Ordinal);
                if (reference != null)
                {
                    foreach (var entry in reference)
                    {
                        fresh[entry.Key] = entry.Value is List<string> list
                            ? (object)new List<string>(new string[list.Count])
                            : string.Empty;
                    }
                }
                _io.SaveLanguage(languageCode, fresh);
            }
        }

        public bool RemoveLanguage(string languageCode)
        {
            lock (FileLock)
            {
                var all = _io.LoadAll();
                string actual = FindLanguage(all, languageCode);
                if (actual == null) return false;
                if (all.Count <= 1)
                    throw new InvalidOperationException("Cannot remove the last remaining language.");
                _io.DeleteLanguage(actual);
                return true;
            }
        }

        private static string FindKey(Dictionary<string, object> langData, string key)
        {
            foreach (var existingKey in langData.Keys)
            {
                if (string.Equals(existingKey, key, StringComparison.Ordinal))
                    return existingKey;
            }
            return null;
        }

        private static bool ContainsKeyOrdinalIgnoreCase(Dictionary<string, object> langData, string key)
        {
            foreach (var existingKey in langData.Keys)
            {
                if (string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string FindLanguage(Dictionary<string, Dictionary<string, object>> all, string languageCode)
        {
            foreach (var existing in all.Keys)
            {
                if (string.Equals(existing, languageCode, StringComparison.OrdinalIgnoreCase))
                    return existing;
            }
            return null;
        }

        public List<string> SuggestSimilarKeys(string key, int maxSuggestions = 3)
        {
            lock (FileLock)
            {
                return FindSimilarKeys(_io.LoadAll(), key, maxSuggestions);
            }
        }

        private static string UnknownKeyMessage(Dictionary<string, Dictionary<string, object>> all, string key)
        {
            var similar = FindSimilarKeys(all, key, 3);
            string hint = similar.Count > 0 ? $" Did you mean: {string.Join(", ", similar)}?" : string.Empty;
            return $"Unknown key '{key}'. Use add_key first.{hint}";
        }

        private static List<string> FindSimilarKeys(Dictionary<string, Dictionary<string, object>> all, string key, int max)
        {
            var exact = new List<string>();
            var prefix = new List<string>();
            var fuzzy = new List<KeyValuePair<int, string>>();
            var contains = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var langData in all.Values)
            {
                foreach (var existing in langData.Keys)
                {
                    if (!seen.Add(existing)) continue;
                    if (string.Equals(existing, key, StringComparison.OrdinalIgnoreCase))
                        exact.Add(existing);
                    else if (existing.StartsWith(key, StringComparison.OrdinalIgnoreCase) ||
                             key.StartsWith(existing, StringComparison.OrdinalIgnoreCase))
                        prefix.Add(existing);
                    else if (IsCloseTypo(existing, key))
                        fuzzy.Add(new KeyValuePair<int, string>(EditDistanceCapped(existing, key, 2), existing));
                    else if (existing.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                             key.IndexOf(existing, StringComparison.OrdinalIgnoreCase) >= 0)
                        contains.Add(existing);
                }
            }
            fuzzy.Sort((x, y) =>
            {
                int c = x.Key.CompareTo(y.Key);
                return c != 0 ? c : string.Compare(x.Value, y.Value, StringComparison.Ordinal);
            });
            var result = new List<string>(max);
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

        private static bool IsCloseTypo(string existing, string key)
        {
            if (key.Length < 3 || existing.Length < 3) return false;
            int distance = EditDistanceCapped(existing, key, 2);
            return distance <= 2 && distance * 2 < key.Length;
        }

        private static int EditDistanceCapped(string a, string b, int cap)
        {
            int n = a.Length, m = b.Length;
            if (n == 0) return m;
            if (m == 0) return n;
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

        private static string AvailableSuffix(Dictionary<string, Dictionary<string, object>> all)
        {
            if (all.Count == 0) return string.Empty;
            var langs = new List<string>(all.Keys);
            langs.Sort(StringComparer.OrdinalIgnoreCase);
            return $" Available: {string.Join(", ", langs)}.";
        }

        internal static object CloneValue(object value)
        {
            if (value is List<string> list) return new List<string>(list);
            if (value is string[] arr) return new List<string>(arr);
            return value?.ToString() ?? string.Empty;
        }
    }
}
