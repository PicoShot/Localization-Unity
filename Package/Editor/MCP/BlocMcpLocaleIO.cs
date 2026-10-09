using System;
using System.Collections.Generic;
using System.IO;
using PicoShot.Localization.Bloc;
using PicoShot.Localization.Data;

namespace PicoShot.Localization.Editor.Mcp
{
    /// <summary>
    /// BLOC-file implementation of <see cref="IMcpLocaleIO"/>.
    /// Parsed files are cached and re-read only when their size or write time changes,
    /// so edits made by the Language Editor or by hand are still picked up.
    /// </summary>
    public sealed class BlocMcpLocaleIO : IMcpLocaleIO
    {
        private const string Extension = ".bloc";

        private sealed class CachedFile
        {
            public long Length;
            public DateTime LastWriteUtc;
            public string FileName;
            public string FileLanguage;
            /// <summary>Header language code; null when the file failed to load.</summary>
            public string Code;
            public Dictionary<string, object> Keys;
            public string Problem;
        }

        private readonly string _localesDir;
        private readonly object _sync = new();
        private readonly Dictionary<string, CachedFile> _files = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _pathByCode = new(StringComparer.OrdinalIgnoreCase);
        private McpLocaleSnapshot _snapshot;

        public BlocMcpLocaleIO(string localesDir, string defaultLanguage)
        {
            if (localesDir == null) throw new ArgumentNullException(nameof(localesDir));
            _localesDir = Path.GetFullPath(localesDir);
            DefaultLanguage = string.IsNullOrEmpty(defaultLanguage) ? "en" : defaultLanguage;
        }

        public string DefaultLanguage { get; }

        public McpLocaleSnapshot Load()
        {
            lock (_sync)
            {
                bool changed = _snapshot == null;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                string[] paths = Array.Empty<string>();
                if (Directory.Exists(_localesDir))
                {
                    try
                    {
                        paths = Directory.GetFiles(_localesDir, "*" + Extension, SearchOption.TopDirectoryOnly);
                    }
                    catch (Exception)
                    {
                        // Directory temporarily unreadable: keep serving the last good state.
                        return _snapshot ?? McpLocaleSnapshot.Empty;
                    }
                }

                foreach (string rawPath in paths)
                {
                    if (!string.Equals(Path.GetExtension(rawPath), Extension, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string path = Path.GetFullPath(rawPath);
                    seen.Add(path);
                    long length;
                    DateTime lastWrite;
                    try
                    {
                        var info = new FileInfo(path);
                        length = info.Length;
                        lastWrite = info.LastWriteTimeUtc;
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (_files.TryGetValue(path, out var cached) && cached.Length == length && cached.LastWriteUtc == lastWrite)
                        continue;
                    _files[path] = ReadFile(path, length, lastWrite);
                    changed = true;
                }

                if (_files.Count != seen.Count)
                {
                    var stale = new List<string>();
                    foreach (string path in _files.Keys)
                    {
                        if (!seen.Contains(path)) stale.Add(path);
                    }
                    foreach (string path in stale) _files.Remove(path);
                    changed = true;
                }

                if (changed)
                    RebuildSnapshot();
                return _snapshot;
            }
        }

        private static CachedFile ReadFile(string path, long length, DateTime lastWrite)
        {
            var entry = new CachedFile
            {
                Length = length,
                LastWriteUtc = lastWrite,
                FileName = Path.GetFileName(path),
                FileLanguage = Path.GetFileNameWithoutExtension(path),
            };
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (!BlocFormat.Validate(stream, out _, out string languageCode, out Exception validationError))
                    {
                        entry.Problem = "Corrupt or unsupported BLOC file: " + (validationError?.Message ?? "validation failed") + ".";
                        return entry;
                    }
                    if (string.IsNullOrEmpty(languageCode))
                    {
                        entry.Problem = "The file header has no language code.";
                        return entry;
                    }
                    if (!LanguageDefinitions.IsValidLanguage(languageCode))
                    {
                        entry.Problem = $"Unsupported language code '{languageCode}' in the file header.";
                        return entry;
                    }
                    if (!string.Equals(entry.FileLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Problem = $"File name does not match the header language '{languageCode}'. Rename it to '{languageCode}{Extension}'.";
                        return entry;
                    }
                    stream.Position = 0;
                    LocaleData localeData = BlocFormat.Deserialize(stream, out _);
                    if (localeData?.Translations == null)
                    {
                        entry.Problem = "The file could not be deserialized.";
                        return entry;
                    }
                    var keys = new Dictionary<string, object>(localeData.Translations.Count, StringComparer.Ordinal);
                    foreach (var kvp in localeData.Translations)
                    {
                        if (string.IsNullOrEmpty(kvp.Key)) continue;
                        keys[kvp.Key] = McpValues.Normalize(kvp.Value);
                    }
                    entry.Code = languageCode;
                    entry.Keys = keys;
                }
            }
            catch (IOException ex)
            {
                // Likely transient (file locked mid-write): force a re-read on the next load.
                entry.Length = -1;
                entry.Problem = "The file could not be read: " + ex.Message;
            }
            catch (Exception ex)
            {
                entry.Problem = "The file could not be read: " + ex.Message;
            }
            return entry;
        }

        private void RebuildSnapshot()
        {
            var paths = new List<string>(_files.Keys);
            paths.Sort(StringComparer.Ordinal);
            var languages = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            var issues = new List<McpFileIssue>();
            _pathByCode.Clear();
            foreach (string path in paths)
            {
                var file = _files[path];
                if (file.Keys == null)
                {
                    issues.Add(new McpFileIssue(file.FileName, file.FileLanguage, file.Problem));
                    continue;
                }
                if (_pathByCode.TryGetValue(file.Code, out string owner))
                {
                    issues.Add(new McpFileIssue(file.FileName, file.FileLanguage,
                        $"Duplicate of language '{file.Code}' already loaded from '{Path.GetFileName(owner)}'; this file is ignored."));
                    continue;
                }
                languages[file.Code] = file.Keys;
                _pathByCode[file.Code] = path;
            }
            _snapshot = new McpLocaleSnapshot(languages, issues, DefaultLanguage);
        }

        public void SaveLanguage(string languageCode, Dictionary<string, object> keys)
        {
            lock (_sync)
            {
                if (!_pathByCode.TryGetValue(languageCode, out string path))
                {
                    path = Path.GetFullPath(Path.Combine(_localesDir, languageCode + Extension));
                    if (File.Exists(path))
                        throw new InvalidOperationException(
                            $"'{Path.GetFileName(path)}' exists but is not a loadable locale file; refusing to overwrite it. Fix or remove it first (see validate).");
                }

                var localeData = new LocaleData
                {
                    Version = BlocFormat.LatestVersion,
                    LanguageCode = languageCode,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Translations = keys,
                };
                LocaleBlocSerializer.SaveFile(path, localeData);

                var info = new FileInfo(path);
                _files[path] = new CachedFile
                {
                    Length = info.Length,
                    LastWriteUtc = info.LastWriteTimeUtc,
                    FileName = Path.GetFileName(path),
                    FileLanguage = Path.GetFileNameWithoutExtension(path),
                    Code = languageCode,
                    Keys = keys,
                };
                RebuildSnapshot();
            }
        }

        public void DeleteLanguage(string languageCode)
        {
            lock (_sync)
            {
                if (!_pathByCode.TryGetValue(languageCode, out string path))
                    return;
                if (File.Exists(path))
                    File.Delete(path);
                _files.Remove(path);
                RebuildSnapshot();
            }
        }

        public string NormalizeLanguage(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode)) return null;
            string candidate = languageCode.Replace('_', '-');
            foreach (string code in LanguageDefinitions.LanguageNames.Keys)
            {
                if (string.Equals(code, candidate, StringComparison.OrdinalIgnoreCase))
                    return code;
            }
            return null;
        }

        public string GetLanguageName(string languageCode) => LanguageDefinitions.GetDisplayName(languageCode);

        public bool IsRightToLeft(string languageCode) => LanguageDefinitions.IsRightToLeft(languageCode);
    }
}
