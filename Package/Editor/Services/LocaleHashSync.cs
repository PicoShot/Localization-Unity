using System;
using System.Collections.Generic;
using System.IO;
using PicoShot.Localization.Config;
using UnityEngine;

namespace PicoShot.Localization.Editor.Services
{
    /// <summary>
    /// Keeps the anti-tamper SHA256 hashes in the config in step with the locale files on disk.
    /// </summary>
    internal static class LocaleHashSync
    {
        /// <summary>
        /// Recomputes the hash of every locale file and removes hashes of files that no longer exist.
        /// Saves the config only when something changed. Must run on the main thread.
        /// </summary>
        /// <returns>True when the stored hashes changed.</returns>
        public static bool Sync(LocalizationConfig config, out int syncedCount, out int removedCount)
        {
            syncedCount = 0;
            removedCount = 0;

            string languagesPath = LocalizationManager.LanguagesPath;
            if (config == null || !Directory.Exists(languagesPath))
                return false;

            bool changed = false;
            var staleHashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in config.GetFileHashes())
                staleHashes.Add(entry.fileName);

            foreach (var file in Directory.GetFiles(languagesPath, "*" + LocalizationManager.FileExtension, SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(file);
                string hash = LocalizationManager.CalculateFileHash(file);
                staleHashes.Remove(fileName);
                syncedCount++;

                if (config.TryGetFileHash(fileName, out string existing) &&
                    string.Equals(existing, hash, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                config.SetFileHash(fileName, hash);
                changed = true;
            }

            foreach (var fileName in staleHashes)
            {
                config.RemoveFileHash(fileName);
                removedCount++;
                changed = true;
            }

            if (changed)
                LocalizationConfigProvider.SaveConfig();

            return changed;
        }

        /// <summary>
        /// Counts locale files whose stored hash is missing or different, plus stored hashes of deleted files.
        /// </summary>
        public static int CountOutOfSync(LocalizationConfig config)
        {
            string languagesPath = LocalizationManager.LanguagesPath;
            if (config == null || !Directory.Exists(languagesPath))
                return 0;

            int outOfSync = 0;
            var stored = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in config.GetFileHashes())
                stored.Add(entry.fileName);

            foreach (var file in Directory.GetFiles(languagesPath, "*" + LocalizationManager.FileExtension, SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(file);
                stored.Remove(fileName);

                if (!config.TryGetFileHash(fileName, out string existing) ||
                    !string.Equals(existing, LocalizationManager.CalculateFileHash(file), StringComparison.OrdinalIgnoreCase))
                    outOfSync++;
            }

            return outOfSync + stored.Count;
        }

        /// <summary>
        /// Syncs hashes when anti-tamper protection is enabled. Must run on the main thread.
        /// </summary>
        public static void SyncIfEnabled(string reason)
        {
            var config = LocalizationConfigProvider.Config;
            if (config == null || !config.IsAntiTamperEnabled)
                return;

            try
            {
                if (Sync(config, out int synced, out int removed))
                    Debug.Log($"[Localization] Anti-tamper hashes updated after {reason} ({synced} files, {removed} removed).");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Localization] Failed to update anti-tamper hashes after {reason}: {ex}");
            }
        }
    }
}
