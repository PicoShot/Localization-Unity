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
        private readonly object _lock = new object();

        public McpLocalesStore(IMcpLocaleIO io)
        {
            _io = io ?? throw new ArgumentNullException(nameof(io));
        }

        public string DefaultLanguage => _io.DefaultLanguage;

        public List<string> ListLanguages()
        {
            lock (_lock)
            {
                var all = _io.LoadAll();
                var list = new List<string>(all.Keys);
                list.Sort(StringComparer.OrdinalIgnoreCase);
                return list;
            }
        }

        public Dictionary<string, Dictionary<string, object>> LoadAll()
        {
            lock (_lock)
            {
                return _io.LoadAll();
            }
        }

        public Dictionary<string, object> GetKey(string key)
        {
            lock (_lock)
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
            lock (_lock)
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
            lock (_lock)
            {
                var all = _io.LoadAll();
                if (!all.TryGetValue(languageCode, out var langData))
                    throw new InvalidOperationException($"Unknown language '{languageCode}'. Use add_language first.");
                string actualKey = FindKey(langData, key);
                if (actualKey == null)
                    throw new InvalidOperationException($"Unknown key '{key}'. Use add_key first.");
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
            lock (_lock)
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
                            $"Unknown language '{item.Lang}'. Use add_language first."));
                        continue;
                    }
                    var langData = all[actualLang];
                    string actualKey = FindKey(langData, item.Key);
                    if (actualKey == null)
                    {
                        outcomes.Add(new McpBatchOutcome(false, item.Key, item.Lang,
                            $"Unknown key '{item.Key}'. Use add_key first."));
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
            lock (_lock)
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
            lock (_lock)
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
            lock (_lock)
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
            lock (_lock)
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
            lock (_lock)
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

        internal static object CloneValue(object value)
        {
            if (value is List<string> list) return new List<string>(list);
            if (value is string[] arr) return new List<string>(arr);
            return value?.ToString() ?? string.Empty;
        }
    }
}
