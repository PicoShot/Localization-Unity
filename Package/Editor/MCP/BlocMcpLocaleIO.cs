using System;
using System.Collections.Generic;
using System.IO;
using PicoShot.Localization.Bloc;
using PicoShot.Localization.Data;

namespace PicoShot.Localization.Editor.Mcp
{
    /// <summary>
    /// BLOC-file implementation of <see cref="IMcpLocaleIO"/>.
    /// </summary>
    public sealed class BlocMcpLocaleIO : IMcpLocaleIO
    {
        private readonly string _localesDir;

        public BlocMcpLocaleIO(string localesDir, string defaultLanguage)
        {
            _localesDir = localesDir ?? throw new ArgumentNullException(nameof(localesDir));
            DefaultLanguage = string.IsNullOrEmpty(defaultLanguage) ? "en" : defaultLanguage;
        }

        public string DefaultLanguage { get; }

        public Dictionary<string, Dictionary<string, object>> LoadAll()
        {
            var result = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(_localesDir))
                return result;

            string[] files;
            try
            {
                files = Directory.GetFiles(_localesDir, "*.bloc", SearchOption.TopDirectoryOnly);
            }
            catch (IOException)
            {
                return result;
            }

            foreach (string file in files)
            {
                try
                {
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (!BlocFormat.Validate(stream, out _, out string languageCode, out _))
                            continue;
                        if (string.IsNullOrEmpty(languageCode))
                            continue;
                        if (!LanguageDefinitions.IsValidLanguage(languageCode))
                            continue;
                        string fileNameLanguage = Path.GetFileNameWithoutExtension(file);
                        if (!string.Equals(fileNameLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
                            continue;
                        stream.Position = 0;
                        LocaleData localeData = BlocFormat.Deserialize(stream, out _);
                        if (localeData?.Translations == null)
                            continue;
                        var keys = new Dictionary<string, object>(StringComparer.Ordinal);
                        foreach (var entry in localeData.Translations)
                        {
                            if (string.IsNullOrEmpty(entry.Key))
                                continue;
                            keys[entry.Key] = McpLocalesStore.CloneValue(entry.Value);
                        }
                        result[languageCode] = keys;
                    }
                }
                catch (Exception)
                {
                    // Skip unreadable files; the validate tool reports them.
                }
            }
            return result;
        }

        public void SaveLanguage(string languageCode, Dictionary<string, object> keys)
        {
            if (!Directory.Exists(_localesDir))
                Directory.CreateDirectory(_localesDir);

            var localeData = new LocaleData
            {
                Version = BlocFormat.LatestVersion,
                LanguageCode = languageCode,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Translations = new Dictionary<string, object>(StringComparer.Ordinal),
            };
            foreach (var kvp in keys)
                localeData.Translations[kvp.Key] = McpLocalesStore.CloneValue(kvp.Value);

            string filePath = Path.Combine(_localesDir, languageCode + ".bloc");
            LocaleBlocSerializer.SaveFile(filePath, localeData);
        }

        public void DeleteLanguage(string languageCode)
        {
            string filePath = Path.Combine(_localesDir, languageCode + ".bloc");
            if (File.Exists(filePath))
                File.Delete(filePath);
        }

        public bool IsValidLanguage(string languageCode)
        {
            return LanguageDefinitions.IsValidLanguage(languageCode);
        }
    }
}
