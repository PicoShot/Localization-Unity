using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

namespace PicoShot.Localization.Editor
{
    /// <summary>
    /// Build processor that ships the locales folder with the player.
    /// </summary>
    public class LocalesBuildProcessor : BuildPlayerProcessor, IPostprocessBuildWithReport
    {
        private const string StagingDirectory = "Temp/PicoShotLocalization/" + LocalizationManager.LanguagesDirectory;

        public override int callbackOrder => 0;

        /// <summary>
        /// Before build: add locales to StreamingAssets without copying them into the Assets folder.
        /// </summary>
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (IsStandalonePlatform(buildPlayerContext.BuildPlayerOptions.target))
                return;

            string stagingPath = Path.GetFullPath(StagingDirectory);
            if (CopyLocales(LocalizationManager.LanguagesPath, stagingPath, "StreamingAssets staging"))
            {
                buildPlayerContext.AddAdditionalPathToStreamingAssets(stagingPath, LocalizationManager.LanguagesDirectory);
            }
        }

        /// <summary>
        /// After build: copy locales next to the executable for Windows and Linux players.
        /// </summary>
        public void OnPostprocessBuild(BuildReport report)
        {
            if (IsStandalonePlatform(report.summary.platform))
            {
                CopyLocalesToBuild(report.summary.outputPath);
            }
        }

        [MenuItem("Tools/Localization/Copy Locales to Build")]
        private static void ManualCopyToBuild()
        {
            string buildPath = EditorUtility.OpenFilePanelWithFilters(
                "Select Built Executable",
                "",
                new[] { "Executable", "exe,x86_64,x86", "All files", "*" });

            if (string.IsNullOrEmpty(buildPath))
                return;

            CopyLocalesToBuild(buildPath);
        }

        private static void CopyLocalesToBuild(string executablePath)
        {
            if (string.IsNullOrEmpty(executablePath))
                return;

            string buildDirectory = Path.GetDirectoryName(executablePath);
            string sourcePath = LocalizationManager.LanguagesPath;
            string targetPath = Path.Combine(buildDirectory, LocalizationManager.LanguagesDirectory);

            CopyLocales(sourcePath, targetPath, "build");
        }

        private static bool CopyLocales(string sourcePath, string targetPath, string destinationName)
        {
            if (!Directory.Exists(sourcePath))
            {
                Debug.LogWarning($"[LocalesBuildProcessor] No locales folder found at: {sourcePath}");
                return false;
            }

            try
            {
                if (Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, true);
                }

                Directory.CreateDirectory(targetPath);

                var files = Directory.GetFiles(sourcePath, "*" + LocalizationManager.FileExtension, SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    string destFile = Path.Combine(targetPath, Path.GetFileName(file));
                    File.Copy(file, destFile, true);
                }

                Debug.Log($"[LocalesBuildProcessor] Copied {files.Length} locale files to {destinationName}: {targetPath}");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LocalesBuildProcessor] Failed to copy locales: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Platforms whose player reads locales from a folder next to the executable.
        /// Must match the runtime path selection in <see cref="LocalizationManager.LanguagesPath"/>.
        /// </summary>
        internal static bool IsStandalonePlatform(BuildTarget target)
        {
            return target == BuildTarget.StandaloneWindows ||
                   target == BuildTarget.StandaloneWindows64 ||
                   target == BuildTarget.StandaloneLinux64;
        }
    }
}
