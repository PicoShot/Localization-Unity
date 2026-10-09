using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace PicoShot.Localization.Editor.Data
{
    /// <summary>
    /// Per-project, per-user editor preferences stored in EditorUserSettings (UserSettings/ folder),
    /// so they never mix with the game's PlayerPrefs. Values saved by older versions in PlayerPrefs
    /// are moved over the first time they are read.
    /// </summary>
    internal static class ProjectPrefs
    {
        public static string GetString(string key, string fallback)
        {
            string value = EditorUserSettings.GetConfigValue(key);
            if (value != null)
                return value;

            if (PlayerPrefs.HasKey(key))
            {
                value = PlayerPrefs.GetString(key, fallback);
                Migrate(key, value);
                return value;
            }

            return fallback;
        }

        public static void SetString(string key, string value)
        {
            EditorUserSettings.SetConfigValue(key, value ?? "");
        }

        public static int GetInt(string key, int fallback)
        {
            string value = EditorUserSettings.GetConfigValue(key);
            if (value != null)
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;

            if (PlayerPrefs.HasKey(key))
            {
                int legacy = PlayerPrefs.GetInt(key, fallback);
                Migrate(key, legacy.ToString(CultureInfo.InvariantCulture));
                return legacy;
            }

            return fallback;
        }

        public static void SetInt(string key, int value)
        {
            EditorUserSettings.SetConfigValue(key, value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Migrate(string key, string value)
        {
            EditorUserSettings.SetConfigValue(key, value);
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
    }
}
