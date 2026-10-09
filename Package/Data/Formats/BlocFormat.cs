using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PicoShot.Localization.Data;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace PicoShot.Localization.Bloc
{
    public static class BlocFormat
    {
        public const int MAGIC_SIZE = 4;
        public const int MAGIC_AND_VERSION_SIZE = MAGIC_SIZE + 2;
        public const int FILE_MIN_SIZE = MAGIC_SIZE + 2;
        public static readonly byte[] MAGIC = new byte[MAGIC_SIZE] { 0x42, 0x4C, 0x4F, 0x43 };

        public static BlocFormatLayout[] Versions;
        public static ushort LatestVersion;

        static BlocFormat()
        {
            Versions = new BlocFormatLayout[]
            {
                BlocFormat1.FormatLayout,
                BlocFormat2.FormatLayout
            };

            LatestVersion = 2;
        }

        public static void Serialize(LocaleData localeData, Stream stream, CompressionLevel compressionLevel)
        {
            if (!stream.CanWrite || !stream.CanSeek)
                throw new InvalidOperationException("Cant write to this stream");

            if (!FindFormatLayout((ushort)localeData.Version, out var formatLayout))
                throw new InvalidDataException($"Not found version:`{localeData.Version}` in defined formats");

            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write(MAGIC);
            writer.Write(formatLayout.version);

            var entries = new IBlocEntry[localeData.Translations.Count];
            int i = 0;
            foreach (var translation in localeData.Translations)
            {
                if (translation.Key == null)
                    continue;

                ref var entry = ref entries[i++];

                switch (translation.Value)
                {
                    case IEnumerable<string> values:
                        entry = new ArrayEntry()
                        {
                            Key = translation.Key,
                            Values = values.Select(v => v ?? string.Empty).ToArray()
                        };
                        break;
                    case string value:
                        entry = new StringEntry()
                        {
                            Key = translation.Key,
                            Value = value ?? string.Empty
                        };
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            formatLayout.serializer(writer, entries, localeData.LanguageCode, compressionLevel);
        }
        public static LocaleData Deserialize(Stream stream, out BlocInfo info)
        {
            info = default;

            if (!stream.CanRead)
                throw new InvalidOperationException("Cant read from this stream");

            if (stream.Length < FILE_MIN_SIZE)
                throw new InvalidDataException("File too small");

            using var reader = new BinaryReader(stream, Encoding.UTF8, true);

            if (!ValidateMagicAndVersion(reader, out var version))
                throw new InvalidDataException("Invalid BLOC magic");

            stream.Position = MAGIC_AND_VERSION_SIZE;

            if (!FindFormatLayout(version, out var formatLayout))
                throw new InvalidDataException($"Not found version:`{version}` in defined formats");

            formatLayout.deserializer(reader, out var entries, out info);
            var translations = new Dictionary<string, object>((int)info.EntryCount);

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];

                switch (entry)
                {
                    case StringEntry se:
                        translations.Add(se.Key, se.Value);
                        break;
                    case ArrayEntry ae:
                        translations.Add(ae.Key, ae.Values.ToList());
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            return new LocaleData()
            {
                Version = version,
                LanguageCode = info.LanguageCode,
                Translations = translations
            };
        }
        public static bool Validate(Stream stream, out ushort version, out string languageCode, out Exception exception)
        {
            exception = null;

            try
            {
                if (!stream.CanRead)
                    throw new InvalidOperationException("Cant read from this stream");

                if (stream.Length < FILE_MIN_SIZE)
                    throw new InvalidDataException("File too small");

                using var reader = new BinaryReader(stream, Encoding.UTF8, true);

                if (!ValidateMagicAndVersion(reader, out version))
                    throw new InvalidDataException("Invalid BLOC magic");

                stream.Position = MAGIC_SIZE + 2;

                if (!FindFormatLayout(version, out var formatLayout))
                    throw new InvalidDataException($"Not found version:`{version}` in defined formats");

                return formatLayout.validator(reader, out languageCode);
            }
            catch (Exception ex)
            {
                version = 0;
                languageCode = null;
                exception = ex;
                return false;
            }
        }

        public static bool ReadInfo(Stream stream, out ushort version, out string languageCode)
        {
            version = 0;
            languageCode = null;

            try
            {
                if (!stream.CanRead || stream.Length < FILE_MIN_SIZE)
                    return false;

                using var reader = new BinaryReader(stream, Encoding.UTF8, true);

                if (!ValidateMagicAndVersion(reader, out version))
                    return false;

                stream.Position = MAGIC_AND_VERSION_SIZE;

                if (!FindFormatLayout(version, out var formatLayout))
                    return false;

                return formatLayout.infoReader != null
                    ? formatLayout.infoReader(reader, out languageCode)
                    : formatLayout.validator(reader, out languageCode);
            }
            catch (Exception)
            {
                languageCode = null;
                return false;
            }
        }

        public static void Upgrade(Stream source, Stream destination, ushort destinationVersion, CompressionLevel compressionLevel)
        {
            if (!source.CanRead)
                throw new InvalidOperationException($"Cant read from {nameof(source)} stream");

            if (!destination.CanWrite || !destination.CanSeek)
                throw new InvalidOperationException($"Cant write to {nameof(destination)} stream");

            if (source.Length < FILE_MIN_SIZE)
                throw new InvalidDataException("File too small");

            if (!FindFormatLayout(destinationVersion, out var destFormat))
                throw new InvalidDataException($"Not found version:`{destinationVersion}` in defined formats");

            #region Read Source
            using var reader = new BinaryReader(source, Encoding.UTF8, true);

            if (!ValidateMagicAndVersion(reader, out var version))
                throw new InvalidDataException("Invalid BLOC magic");

            source.Position = MAGIC_AND_VERSION_SIZE;

            if (!FindFormatLayout(version, out var sourceFormat))
                throw new InvalidDataException($"Not found version:`{version}` in defined formats");

            sourceFormat.deserializer(reader, out var entries, out var info);
            #endregion

            #region Write Destination
            using var writer = new BinaryWriter(destination, Encoding.UTF8, true);
            writer.Write(MAGIC);
            writer.Write(destFormat.version);

            destFormat.serializer(writer, entries, info.LanguageCode, compressionLevel);
            #endregion
        }
        public static bool ValidateMagicAndVersion(BinaryReader reader, out ushort version)
        {
            Span<byte> magic = stackalloc byte[MAGIC_SIZE];
            reader.Read(magic);

            if (!magic.SequenceEqual(MAGIC))
            {
                version = 0;
                return false;
            }

            version = reader.ReadUInt16();
            return true;
        }
        public static bool FindFormatLayout(ushort version, out BlocFormatLayout formatLayout)
        {
            for (int i = 0; i < Versions.Length; i++)
                if (Versions[i].version == version)
                {
                    formatLayout = Versions[i];
                    return true;
                }

            formatLayout = default;
            return false;
        }

        #region Utilities
        private static readonly uint[] Crc32Table = CreateCrc32Table();

        private static uint[] CreateCrc32Table()
        {
            const uint polynomial = 0xEDB88320;
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ polynomial : crc >> 1;
                table[i] = crc;
            }
            return table;
        }

        public static uint ComputeCrc32(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFF;
            ComputeCrc32(ref crc, data);
            return ~crc;
        }
        public static void ComputeCrc32(ref uint crc, ReadOnlySpan<byte> data)
        {
            uint[] table = Crc32Table;
            uint value = crc;
            for (int i = 0; i < data.Length; i++)
                value = table[(byte)(value ^ data[i])] ^ (value >> 8);
            crc = value;
        }
        public static int GetVarIntSize(uint value)
        {
            if (value <= 0x7F) return 1;
            if (value <= 0x3FFF) return 2;
            if (value <= 0x1FFFFF) return 3;
            if (value <= 0xFFFFFFF) return 4;
            return 5;
        }
        public static void WriteVarInt(BinaryWriter writer, uint value)
        {
            while (value >= 0x80)
            {
                writer.Write((byte)(value | 0x80));
                value >>= 7;
            }
            writer.Write((byte)value);
        }
        public static uint ReadVarInt(BinaryReader reader)
        {
            uint result = 0;
            int shift = 0;
            byte b;
            do
            {
                b = reader.ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            return result;
        }
        public static bool EnsureLength(BinaryReader reader, int minLength)
        {
            return minLength <= reader.BaseStream.Length - reader.BaseStream.Position;
        }
        #endregion

        public delegate bool ValidateMethod(BinaryReader reader, out string languageCode);
        public delegate void SerializeMethod(BinaryWriter writer, in IBlocEntry[] entries, string languageCode, System.IO.Compression.CompressionLevel compressionLevel);
        public delegate void DeserializeMethod(BinaryReader reader, out IBlocEntry[] entries, out BlocInfo info);
    }

    public struct BlocFormatLayout
    {
        public ushort version;
        public BlocFormat.ValidateMethod validator;
        public BlocFormat.SerializeMethod serializer;
        public BlocFormat.DeserializeMethod deserializer;
        public BlocFormat.ValidateMethod infoReader;

        public BlocFormatLayout(ushort version, BlocFormat.ValidateMethod validator, BlocFormat.SerializeMethod serializer, BlocFormat.DeserializeMethod deserializer)
            : this(version, validator, serializer, deserializer, null)
        {
        }

        public BlocFormatLayout(ushort version, BlocFormat.ValidateMethod validator, BlocFormat.SerializeMethod serializer, BlocFormat.DeserializeMethod deserializer, BlocFormat.ValidateMethod infoReader)
        {
            this.version = version;
            this.validator = validator;
            this.serializer = serializer;
            this.deserializer = deserializer;
            this.infoReader = infoReader;
        }
    }

    public struct BlocInfo
    {
        public ushort Version;
        public string LanguageCode;
        public uint EntryCount;
    }

    public struct DataSpan//8 byte
    {
        public uint offset;
        public uint count;

        public DataSpan(uint offset, uint count)
        {
            this.offset = offset;
            this.count = count;
        }
    }

    public interface IBlocEntry
    {
        string Key { get; }
    }

    public struct StringEntry : IBlocEntry
    {
        public string Key { get; set; }
        public string Value;
    }

    public struct ArrayEntry : IBlocEntry
    {
        public string Key { get; set; }
        public string[] Values;
    }
}
