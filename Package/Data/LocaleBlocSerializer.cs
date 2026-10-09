using System;
using System.IO;
using System.IO.Compression;
using PicoShot.Localization.Bloc;

namespace PicoShot.Localization.Data
{
    /// <summary>
    /// BLOC (Binary Localization Container) format serializer.
    /// Optimized binary format with optional compression and string deduplication.
    /// </summary>
    internal static class LocaleBlocSerializer
    {
        /// <summary>
        /// Compression level for BLOC files. Set to Optimal for best compression,
        /// Fastest for quicker saves, or NoCompression to disable.
        /// </summary>
        public static CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

        /// <summary>
        /// Deserializes BLOC data from a file.
        /// </summary>
        public static LocaleData LoadFile(string path, out BlocInfo info)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("BLOC file not found", path);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return BlocFormat.Deserialize(stream, out info);
        }

        /// <summary>
        /// Saves locale data to a BLOC file.
        /// </summary>
        public static void SaveFile(string path, LocaleData data)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var tempFile = $"{path}.tmp";
            try
            {
                using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    BlocFormat.Serialize(data, stream, CompressionLevel);

                    stream.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(tempFile, path, $"{path}.bak");
                else
                    File.Move(tempFile, path);
            }
            catch (Exception)
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);

                throw;
            }
        }

        /// <summary>
        /// Reads the version and language code from a BLOC file header, without
        /// decompressing or checksumming the content.
        /// </summary>
        public static bool ReadFileInfo(string path, out ushort version, out string languageCode)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("BLOC file not found", path);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
            return BlocFormat.ReadInfo(stream, out version, out languageCode);
        }

        /// <summary>
        /// Validates a BLOC file by checking magic, version, and CRC32 checksum.
        /// Returns true if the file is valid and not corrupted.
        /// </summary>
        public static bool ValidateFile(string path, out ushort version, out string languageCode)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("BLOC file not found", path);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return BlocFormat.Validate(stream, out version, out languageCode, out _);
        }
    }
}
