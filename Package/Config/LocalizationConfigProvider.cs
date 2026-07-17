using UnityEngine;

namespace PicoShot.Localization.Config
{
    /// <summary>
    /// Provides access to localization configuration.
    /// Config is stored in Resources folder for runtime access.
    /// </summary>
    public static class LocalizationConfigProvider
    {
        public const string ConfigDirectory = "Assets/Resources/Localization";
        public const string ConfigAssetPath = ConfigDirectory + "/LocalizationConfig.asset";
        private const string ResourcesPath = "Localization/LocalizationConfig";

        private static LocalizationConfig _cachedConfig;

        /// <summary>
        /// Gets the localization configuration.
        /// Auto-creates default if not exists.
        /// </summary>
        public static LocalizationConfig Config
        {
            get
            {
                if (_cachedConfig == null)
                {
                    _cachedConfig = LoadOrCreateConfig();
                }
                return _cachedConfig;
            }
        }

        /// <summary>
        /// Invalidates the cached config.
        /// </summary>
        public static void InvalidateCache()
        {
            _cachedConfig = null;
        }

        private static LocalizationConfig LoadOrCreateConfig()
        {
            var config = Resources.Load<LocalizationConfig>(ResourcesPath);
            if (config != null)
            {
                return config;
            }

#if UNITY_EDITOR
            return CreateDefaultConfig();
#else
            // At runtime in builds, create a default instance if not found
            Debug.LogWarning("[LocalizationConfig] Config not found in Resources. Using defaults.");
            var defaultConfig = ScriptableObject.CreateInstance<LocalizationConfig>();
            return defaultConfig;
#endif
        }

#if UNITY_EDITOR

        /// <summary>
        /// Creates a default config file in Resources folder (Editor only).
        /// </summary>
        private static LocalizationConfig CreateDefaultConfig()
        {
            string directory = System.IO.Path.GetDirectoryName(ConfigAssetPath);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            if (!System.IO.File.Exists(ConfigAssetPath))
            {
                var config = ScriptableObject.CreateInstance<LocalizationConfig>();
                UnityEditor.AssetDatabase.CreateAsset(config, ConfigAssetPath);
                UnityEditor.AssetDatabase.SaveAssets();
                UnityEditor.AssetDatabase.Refresh();
                Debug.Log($"[LocalizationConfig] Created default config at: {ConfigAssetPath}");
                return config;
            }
            return UnityEditor.AssetDatabase.LoadAssetAtPath<LocalizationConfig>(ConfigAssetPath);
        }

        /// <summary>
        /// Saves the config to disk (Editor only).
        /// </summary>
        public static void SaveConfig()
        {
            if (_cachedConfig != null)
            {
                UnityEditor.EditorUtility.SetDirty(_cachedConfig);
                UnityEditor.AssetDatabase.SaveAssets();
            }
        }

#endif
    }
}
