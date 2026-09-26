using System;
using System.Collections.Generic;
using System.Linq;

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
                return all.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
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
                foreach (var lang in all.Values)
                {
                    foreach (var existingKey in lang.Keys)
                    {
                        if (string.Equals(existingKey, key, StringComparison.Ordinal))
                            goto Found;
                    }
                }
                return null;
            Found:
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var kvp in all)
                {
                    if (kvp.Value.TryGetValue(key, out object value))
                        result[kvp.Key] = CloneValue(value);
                }
                return result;
            }
        }

        public bool ContainsKey(string key)
        {
            lock (_lock)
            {
                var all = _io.LoadAll();
                return all.Values.Any(lang =>
                    lang.Keys.Any(k => string.Equals(k, key, StringComparison.Ordinal)));
            }
        }

        public void SetTranslation(string key, string languageCode, object value)
        {
            lock (_lock)
            {
                var all = _io.LoadAll();
                if (!all.TryGetValue(languageCode, out var langData))
                    throw new InvalidOperationException($"Unknown language '{languageCode}'. Use add_language first.");
                string actualKey = langData.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.Ordinal));
                if (actualKey == null)
                    throw new InvalidOperationException($"Unknown key '{key}'. Use add_key first.");
                langData[actualKey] = CloneValue(value);
                _io.SaveLanguage(languageCode, langData);
            }
        }

        public void AddKey(string key, bool isArray, string defaultText, string defaultLanguage)
        {
            lock (_lock)
            {
                var all = _io.LoadAll();
                foreach (var langData in all.Values)
                {
                    if (langData.Keys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException($"Key '{key}' already exists.");
                }
                foreach (var kvp in all)
                {
                    object value;
                    if (isArray)
                    {
                        value = string.Equals(kvp.Key, defaultLanguage, StringComparison.OrdinalIgnoreCase) && defaultText != null
                            ? new List<string> { defaultText }
                            : new List<string> { string.Empty };
                    }
                    else
                    {
                        value = string.Equals(kvp.Key, defaultLanguage, StringComparison.OrdinalIgnoreCase)
                            ? (defaultText ?? string.Empty)
                            : string.Empty;
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
                bool found = false;
                foreach (var langData in all.Values)
                {
                    if (langData.Keys.Any(k => !string.Equals(k, oldKey, StringComparison.Ordinal) &&
                                               string.Equals(k, newKey, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException($"Key '{newKey}' already exists.");
                }
                foreach (var kvp in all)
                {
                    string actualOld = kvp.Value.Keys.FirstOrDefault(k => string.Equals(k, oldKey, StringComparison.Ordinal));
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
                    string actual = kvp.Value.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.Ordinal));
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
                if (all.Keys.Any(k => string.Equals(k, languageCode, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Language '{languageCode}' already exists.");
                var reference = all.Values.FirstOrDefault();
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
                string actual = all.Keys.FirstOrDefault(k => string.Equals(k, languageCode, StringComparison.OrdinalIgnoreCase));
                if (actual == null) return false;
                if (all.Count <= 1)
                    throw new InvalidOperationException("Cannot remove the last remaining language.");
                _io.DeleteLanguage(actual);
                return true;
            }
        }

        internal static object CloneValue(object value)
        {
            if (value is List<string> list) return new List<string>(list);
            if (value is string[] arr) return new List<string>(arr);
            return value?.ToString() ?? string.Empty;
        }
    }
}
