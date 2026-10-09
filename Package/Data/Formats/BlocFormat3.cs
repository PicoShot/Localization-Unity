using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using PicoShot.Localization.Data;
using PicoShot.Localization.Hashing;

namespace PicoShot.Localization.Bloc
{
    /// <summary>
    /// BLOC version 3
    ///
    /// Layout: 48-byte header, a section directory (20 bytes per section), then the sections back to back.
    /// The DATA section holds, in order: ENTRIES, LENGTHS, TEXT and KEYS (sorted, front-coded key names).
    /// </summary>
    public static class BlocFormat3
    {
        public const int VERSION = 3;
        public const int HEADER_SIZE = 48;
        public const int SECTION_ENTRY_SIZE = 20;
        public const int LANGUAGE_TAG_SIZE = 16;
        public const int MAX_SECTIONS = 16;

        /// <summary>Largest accepted section, stored or decompressed (100 MB).</summary>
        public const int MAX_SECTION_SIZE = 100_000_000;

        public const uint TAG_DATA = 'D' | ('A' << 8) | ('T' << 16) | ('A' << 24);

        public const byte CODEC_NONE = 0;
        public const byte CODEC_DEFLATE = 1;

        // Header field offsets (from the start of the file).
        internal const int OFFSET_HEADER_SIZE = 0x06;
        internal const int OFFSET_FLAGS = 0x08;
        internal const int OFFSET_LANGUAGE = 0x0C;
        internal const int OFFSET_ENTRY_COUNT = 0x1C;
        internal const int OFFSET_KEY_SET_ID = 0x20;
        internal const int OFFSET_TEXT_ENCODING = 0x28;
        internal const int OFFSET_SECTION_COUNT = 0x29;
        internal const int OFFSET_WINDOW_BASE = 0x2A;
        internal const int OFFSET_HEADER_CHECK = 0x2C;

        /// <summary>Low 16 flag bits: a reader must refuse a file with a set bit it does not know.</summary>
        internal const uint REQUIRED_FLAGS_MASK = 0x0000FFFF;
        internal const uint KNOWN_REQUIRED_FLAGS = 0;

        // ENTRIES tag: low 2 bits are the kind.
        internal const int TAG_KIND_STRING = 0;
        internal const int TAG_KIND_ARRAY = 1;
        internal const int TAG_KIND_EXTENDED = 2;
        internal const int EXTENDED_MISSING = 0;
        internal const int EXTENDED_PLURAL = 1;

        internal const byte PLURAL_OTHER_BIT = 1 << (int)PluralCategory.Other;
        internal const byte PLURAL_VALID_BITS = (1 << PluralValue.CategoryCount) - 1;

        /// <summary>Most entries (and unique strings) a file may hold, so references fit a varint shifted by 2.</summary>
        internal const int MAX_ENTRIES = 1 << 28;

        public static BlocFormatLayout FormatLayout = new(VERSION, Validate, Serialize, Deserialize, ReadInfo);

        #region BlocFormat entry points

        /// <summary>
        /// Full check of every byte: checksums, structure, text encoding and key rules.
        /// Throws <see cref="InvalidDataException"/> describing the first problem.
        /// </summary>
        public static bool Validate(BinaryReader reader, out string languageCode)
        {
            byte[] file = ReadWholeFile(reader.BaseStream);
            var data = BlocData3.Parse(file, deep: true);
            languageCode = data.LanguageCode;
            return true;
        }

        /// <summary>
        /// Reads only the header and section directory, verified by the header checksum.
        /// </summary>
        public static bool ReadInfo(BinaryReader reader, out string languageCode)
        {
            languageCode = null;
            Stream stream = reader.BaseStream;
            if (stream.Length < HEADER_SIZE)
                return false;

            var prefix = new byte[HEADER_SIZE];
            stream.Position = 0;
            if (!ReadExactly(stream, prefix, 0, HEADER_SIZE))
                return false;

            int sectionCount = prefix[OFFSET_SECTION_COUNT];
            if (sectionCount == 0 || sectionCount > MAX_SECTIONS)
                return false;

            ushort headerSize = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(OFFSET_HEADER_SIZE));
            if (headerSize < HEADER_SIZE || headerSize > stream.Length)
                return false;

            int directoryEnd = headerSize + sectionCount * SECTION_ENTRY_SIZE;
            if (directoryEnd > stream.Length)
                return false;

            var head = new byte[directoryEnd];
            prefix.CopyTo(head, 0);
            if (!ReadExactly(stream, head, HEADER_SIZE, directoryEnd - HEADER_SIZE))
                return false;

            if (!Header.TryParse(head, stream.Length, out var header, out _))
                return false;

            languageCode = header.LanguageCode;
            return true;
        }

        public static void Serialize(BinaryWriter writer, in IBlocEntry[] entries, string languageCode, CompressionLevel compressionLevel)
        {
            byte[] file = Write(entries, languageCode, compressionLevel);
            // BlocFormat.Serialize already wrote the magic and version.
            writer.Write(file, BlocFormat.MAGIC_AND_VERSION_SIZE, file.Length - BlocFormat.MAGIC_AND_VERSION_SIZE);
        }

        public static void Deserialize(BinaryReader reader, out IBlocEntry[] entries, out BlocInfo info)
        {
            byte[] file = ReadWholeFile(reader.BaseStream);
            var data = BlocData3.Parse(file, deep: false);
            entries = data.ToEntries();
            info = new BlocInfo()
            {
                Version = VERSION,
                LanguageCode = data.LanguageCode,
                EntryCount = (uint)data.EntryCount
            };
        }

        #endregion

        #region Writing

        private sealed class OrdinalKeyComparer : IComparer<IBlocEntry>
        {
            public static readonly OrdinalKeyComparer Instance = new OrdinalKeyComparer();
            public int Compare(IBlocEntry x, IBlocEntry y) => string.CompareOrdinal(x.Key, y.Key);
        }

        /// <summary>
        /// Builds a complete v3 file. The output depends only on the entries, language and compression
        /// level: entries are sorted by key, so the same data always gives the same bytes.
        /// </summary>
        public static byte[] Write(IReadOnlyList<IBlocEntry> entries, string languageCode, CompressionLevel compressionLevel)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));

            byte[] languageTag = EncodeLanguageTag(languageCode);

            var sorted = new IBlocEntry[entries.Count];
            for (int i = 0; i < sorted.Length; i++)
            {
                var entry = entries[i] ?? throw new ArgumentException($"Entry {i} is null", nameof(entries));
                if (string.IsNullOrEmpty(entry.Key))
                    throw new ArgumentException($"Entry {i} has an empty key", nameof(entries));
                sorted[i] = entry;
            }

            if (sorted.Length > MAX_ENTRIES)
                throw new InvalidDataException($"Too many entries: {sorted.Length}");

            Array.Sort(sorted, OrdinalKeyComparer.Instance);
            CheckKeyCollisions(sorted);

            // ENTRIES, and the unique strings in order of first use.
            var entryBytes = new BlocByteBuffer(sorted.Length * 2);
            var stringIds = new Dictionary<string, int>(StringComparer.Ordinal);
            var strings = new List<string>();

            foreach (var entry in sorted)
            {
                switch (entry)
                {
                    case StringEntry stringEntry:
                        uint reference = AddString(stringEntry.Value, stringIds, strings);
                        entryBytes.WriteVarUInt(reference << 2 | TAG_KIND_STRING);
                        break;
                    case ArrayEntry arrayEntry:
                        var values = arrayEntry.Values ?? Array.Empty<string>();
                        entryBytes.WriteVarUInt((uint)values.Length << 2 | TAG_KIND_ARRAY);
                        foreach (string value in values)
                            entryBytes.WriteVarUInt(AddString(value, stringIds, strings));
                        break;
                    case MissingEntry _:
                        entryBytes.WriteVarUInt(EXTENDED_MISSING << 2 | TAG_KIND_EXTENDED);
                        break;
                    case PluralEntry pluralEntry:
                        var plural = pluralEntry.Value ?? throw new ArgumentException($"Key '{entry.Key}': plural value is null");
                        byte mask = plural.Mask;
                        if ((mask & PLURAL_OTHER_BIT) == 0)
                            throw new ArgumentException($"Key '{entry.Key}': a plural value needs the Other form");

                        entryBytes.WriteVarUInt(EXTENDED_PLURAL << 2 | TAG_KIND_EXTENDED);
                        entryBytes.WriteByte(mask);
                        for (int category = 0; category < PluralValue.CategoryCount; category++)
                        {
                            if ((mask & (1 << category)) != 0)
                                entryBytes.WriteVarUInt(AddString(plural[(PluralCategory)category], stringIds, strings));
                        }
                        break;
                    default:
                        throw new NotSupportedException($"Key '{entry.Key}': unsupported entry type {entry.GetType().Name}");
                }

                if (strings.Count > MAX_ENTRIES)
                    throw new InvalidDataException($"Too many unique strings: {strings.Count}");
            }

            // LENGTHS and TEXT.
            var encoding = BlocText.Choose(strings, out ushort windowBase);
            var lengthBytes = new BlocByteBuffer(strings.Count * 2);
            var textBytes = new BlocByteBuffer(strings.Count * 16);
            foreach (string text in strings)
            {
                int before = textBytes.Length;
                BlocText.Encode(text, encoding, windowBase, textBytes);
                lengthBytes.WriteVarUInt((uint)(textBytes.Length - before));
            }

            // KEYS: each key is (shared prefix length, suffix length, suffix bytes) against the previous key.
            var keyBytes = new BlocByteBuffer(sorted.Length * 8);
            byte[] previous = Array.Empty<byte>();
            foreach (var entry in sorted)
            {
                byte[] current = Encoding.UTF8.GetBytes(entry.Key);
                int shared = 0;
                int limit = Math.Min(previous.Length, current.Length);
                while (shared < limit && previous[shared] == current[shared])
                    shared++;

                keyBytes.WriteVarUInt((uint)shared);
                keyBytes.WriteVarUInt((uint)(current.Length - shared));
                keyBytes.Write(current.AsSpan(shared));
                previous = current;
            }

            long rawLength = (long)entryBytes.Length + lengthBytes.Length + textBytes.Length + keyBytes.Length;
            if (rawLength > MAX_SECTION_SIZE)
                throw new InvalidDataException($"Locale data too large: {rawLength} bytes");

            var raw = new byte[rawLength];
            int offset = 0;
            entryBytes.Span.CopyTo(raw.AsSpan(offset)); offset += entryBytes.Length;
            lengthBytes.Span.CopyTo(raw.AsSpan(offset)); offset += lengthBytes.Length;
            textBytes.Span.CopyTo(raw.AsSpan(offset)); offset += textBytes.Length;
            keyBytes.Span.CopyTo(raw.AsSpan(offset));

            ulong keySetId = XxHash.Hash64(keyBytes.Span);

            byte codec = CODEC_NONE;
            byte[] stored = raw;
            if (compressionLevel != CompressionLevel.NoCompression && raw.Length > 0)
            {
                byte[] compressed = Deflate(raw, compressionLevel);
                if (compressed.Length < raw.Length)
                {
                    codec = CODEC_DEFLATE;
                    stored = compressed;
                }
            }

            // Header, directory, then the DATA section.
            const int sectionCount = 1;
            int dataOffset = HEADER_SIZE + sectionCount * SECTION_ENTRY_SIZE;
            var file = new byte[dataOffset + stored.Length];
            var span = file.AsSpan();

            BlocFormat.MAGIC.CopyTo(span);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4), VERSION);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(OFFSET_HEADER_SIZE), HEADER_SIZE);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OFFSET_FLAGS), 0);
            languageTag.CopyTo(span.Slice(OFFSET_LANGUAGE));
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OFFSET_ENTRY_COUNT), (uint)sorted.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(OFFSET_KEY_SET_ID), keySetId);
            span[OFFSET_TEXT_ENCODING] = (byte)encoding;
            span[OFFSET_SECTION_COUNT] = sectionCount;
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(OFFSET_WINDOW_BASE), windowBase);

            var directory = span.Slice(HEADER_SIZE, SECTION_ENTRY_SIZE);
            BinaryPrimitives.WriteUInt32LittleEndian(directory, TAG_DATA);
            directory[4] = codec;
            BinaryPrimitives.WriteUInt32LittleEndian(directory.Slice(8), (uint)stored.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(directory.Slice(12), (uint)raw.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(directory.Slice(16), XxHash.Hash32(stored));

            stored.CopyTo(span.Slice(dataOffset));

            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OFFSET_HEADER_CHECK), ComputeHeaderCheck(span, HEADER_SIZE, sectionCount));
            return file;
        }

        /// <summary>
        /// Two keys with the same 64-bit lookup hash (normally keys differing only by case) cannot both be
        /// looked up, so they are refused here instead of one being dropped at runtime.
        /// </summary>
        private static void CheckKeyCollisions(IBlocEntry[] sorted)
        {
            var seen = new Dictionary<long, string>(sorted.Length);
            foreach (var entry in sorted)
            {
                long hash = Hash64.CreateIgnoreCase(entry.Key);
                if (seen.TryGetValue(hash, out string other))
                {
                    throw new InvalidDataException(string.Equals(other, entry.Key, StringComparison.Ordinal)
                        ? $"Duplicate key '{entry.Key}'"
                        : $"Keys '{other}' and '{entry.Key}' conflict (keys are case-insensitive). Rename one of them.");
                }
                seen.Add(hash, entry.Key);
            }
        }

        /// <summary>0 for a string not seen before, otherwise its id + 1.</summary>
        private static uint AddString(string value, Dictionary<string, int> ids, List<string> strings)
        {
            value ??= string.Empty;
            if (ids.TryGetValue(value, out int id))
                return (uint)id + 1;

            ids.Add(value, strings.Count);
            strings.Add(value);
            return 0;
        }

        private static byte[] EncodeLanguageTag(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode) || languageCode.Length > LANGUAGE_TAG_SIZE)
                throw new ArgumentException($"Language code must be 1-{LANGUAGE_TAG_SIZE} characters: '{languageCode}'", nameof(languageCode));

            var tag = new byte[LANGUAGE_TAG_SIZE];
            for (int i = 0; i < languageCode.Length; i++)
            {
                char c = languageCode[i];
                if (c <= ' ' || c > '~')
                    throw new ArgumentException($"Language code must be printable ASCII: '{languageCode}'", nameof(languageCode));
                tag[i] = (byte)c;
            }
            return tag;
        }

        private static byte[] Deflate(byte[] raw, CompressionLevel compressionLevel)
        {
            using var output = new MemoryStream(raw.Length / 2 + 64);
            using (var deflate = new DeflateStream(output, compressionLevel, true))
                deflate.Write(raw, 0, raw.Length);
            return output.ToArray();
        }

        #endregion

        #region Shared helpers

        /// <summary>
        /// XXH32 of the header up to the check field, followed by the section directory.
        /// </summary>
        internal static uint ComputeHeaderCheck(ReadOnlySpan<byte> file, int headerSize, int sectionCount)
        {
            int directorySize = sectionCount * SECTION_ENTRY_SIZE;
            int extraHeader = headerSize - HEADER_SIZE;
            var buffer = new byte[OFFSET_HEADER_CHECK + extraHeader + directorySize];
            file.Slice(0, OFFSET_HEADER_CHECK).CopyTo(buffer);
            file.Slice(HEADER_SIZE, extraHeader + directorySize).CopyTo(buffer.AsSpan(OFFSET_HEADER_CHECK));
            return XxHash.Hash32(buffer);
        }

        internal static byte[] ReadWholeFile(Stream stream)
        {
            if (stream.Length > HEADER_SIZE + MAX_SECTIONS * (SECTION_ENTRY_SIZE + (long)MAX_SECTION_SIZE))
                throw new InvalidDataException("File too large");

            var file = new byte[stream.Length];
            stream.Position = 0;
            if (!ReadExactly(stream, file, 0, file.Length))
                throw new InvalidDataException("File truncated");
            return file;
        }

        private static bool ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read <= 0)
                    return false;
                offset += read;
                count -= read;
            }
            return true;
        }

        #endregion

        #region Header

        internal struct Section
        {
            public uint Tag;
            public byte Codec;
            public int Offset;
            public int StoredSize;
            public int RawSize;
            public uint Checksum;

            /// <summary>PNG rule: an uppercase first letter means a reader must understand the section.</summary>
            public bool IsCritical => (Tag & 0xFF) >= 'A' && (Tag & 0xFF) <= 'Z';

            public string TagName => new string(new[] { (char)(Tag & 0xFF), (char)((Tag >> 8) & 0xFF), (char)((Tag >> 16) & 0xFF), (char)(Tag >> 24) });
        }

        internal struct Header
        {
            public int HeaderSize;
            public uint Flags;
            public string LanguageCode;
            public int EntryCount;
            public ulong KeySetId;
            public BlocTextEncoding TextEncoding;
            public ushort WindowBase;
            public Section[] Sections;

            /// <summary>
            /// Parses and checks the header and directory. <paramref name="file"/> needs at least the
            /// header and directory; <paramref name="fileLength"/> is the length of the whole file.
            /// </summary>
            public static bool TryParse(ReadOnlySpan<byte> file, long fileLength, out Header header, out string error)
            {
                header = default;
                error = null;

                if (file.Length < HEADER_SIZE)
                    return Fail("File too small", out error);

                if (!file.Slice(0, BlocFormat.MAGIC_SIZE).SequenceEqual(BlocFormat.MAGIC) ||
                    BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(4)) != VERSION)
                    return Fail("Not a BLOC v3 file", out error);

                header.HeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(OFFSET_HEADER_SIZE));
                int sectionCount = file[OFFSET_SECTION_COUNT];
                if (header.HeaderSize < HEADER_SIZE)
                    return Fail("Invalid header size", out error);
                if (sectionCount == 0 || sectionCount > MAX_SECTIONS)
                    return Fail("Invalid section count", out error);

                int directoryEnd = header.HeaderSize + sectionCount * SECTION_ENTRY_SIZE;
                if (file.Length < directoryEnd || fileLength < directoryEnd)
                    return Fail("File truncated (section directory)", out error);

                uint storedCheck = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(OFFSET_HEADER_CHECK));
                if (storedCheck != ComputeHeaderCheck(file, header.HeaderSize, sectionCount))
                    return Fail("File damaged (header checksum mismatch)", out error);

                header.Flags = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(OFFSET_FLAGS));
                if ((header.Flags & REQUIRED_FLAGS_MASK & ~KNOWN_REQUIRED_FLAGS) != 0)
                    return Fail($"Unsupported required flags 0x{header.Flags & REQUIRED_FLAGS_MASK:X4} (file written by a newer version)", out error);

                if (!TryParseLanguageTag(file.Slice(OFFSET_LANGUAGE, LANGUAGE_TAG_SIZE), out header.LanguageCode))
                    return Fail("Invalid language tag", out error);

                uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(OFFSET_ENTRY_COUNT));
                if (entryCount > MAX_ENTRIES)
                    return Fail("Invalid entry count", out error);
                header.EntryCount = (int)entryCount;

                header.KeySetId = BinaryPrimitives.ReadUInt64LittleEndian(file.Slice(OFFSET_KEY_SET_ID));
                header.TextEncoding = (BlocTextEncoding)file[OFFSET_TEXT_ENCODING];
                header.WindowBase = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(OFFSET_WINDOW_BASE));

                if (!BlocText.IsKnown(header.TextEncoding))
                    return Fail($"Unknown text encoding {(byte)header.TextEncoding}", out error);
                if (header.TextEncoding == BlocTextEncoding.Window8 ? !BlocText.IsValidWindowBase(header.WindowBase) : header.WindowBase != 0)
                    return Fail("Invalid text window", out error);

                header.Sections = new Section[sectionCount];
                long position = directoryEnd;
                bool hasData = false;
                for (int i = 0; i < sectionCount; i++)
                {
                    var entry = file.Slice(header.HeaderSize + i * SECTION_ENTRY_SIZE, SECTION_ENTRY_SIZE);
                    var section = new Section
                    {
                        Tag = BinaryPrimitives.ReadUInt32LittleEndian(entry),
                        Codec = entry[4],
                        Checksum = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(16))
                    };

                    uint storedSize = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(8));
                    uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(12));
                    if (storedSize > MAX_SECTION_SIZE || rawSize > MAX_SECTION_SIZE)
                        return Fail($"Section '{section.TagName}' too large", out error);
                    if (section.Codec == CODEC_NONE && storedSize != rawSize)
                        return Fail($"Section '{section.TagName}' size mismatch", out error);

                    section.Offset = (int)position;
                    section.StoredSize = (int)storedSize;
                    section.RawSize = (int)rawSize;
                    position += storedSize;

                    if (section.Tag == TAG_DATA)
                    {
                        if (hasData)
                            return Fail("More than one DATA section", out error);
                        hasData = true;
                    }
                    else if (section.IsCritical)
                    {
                        return Fail($"Unsupported required section '{section.TagName}' (file written by a newer version)", out error);
                    }

                    if (section.IsCritical && section.Codec != CODEC_NONE && section.Codec != CODEC_DEFLATE)
                        return Fail($"Section '{section.TagName}' uses unknown codec {section.Codec}", out error);

                    header.Sections[i] = section;
                }

                if (!hasData)
                    return Fail("Missing DATA section", out error);
                if (position != fileLength)
                    return Fail(position > fileLength ? "File truncated" : "Unexpected bytes after the last section", out error);

                return true;
            }

            private static bool TryParseLanguageTag(ReadOnlySpan<byte> tag, out string languageCode)
            {
                languageCode = null;
                int length = 0;
                while (length < tag.Length && tag[length] != 0)
                {
                    if (tag[length] <= ' ' || tag[length] > '~')
                        return false;
                    length++;
                }

                if (length == 0)
                    return false;

                for (int i = length; i < tag.Length; i++)
                    if (tag[i] != 0)
                        return false;

                var chars = new char[length];
                for (int i = 0; i < length; i++)
                    chars[i] = (char)tag[i];
                languageCode = new string(chars);
                return true;
            }

            private static bool Fail(string message, out string error)
            {
                error = message;
                return false;
            }
        }

        #endregion
    }

    /// <summary>
    /// A parsed BLOC v3 file. Values stay encoded in <see cref="Raw"/> and are decoded on demand.
    /// </summary>
    internal sealed class BlocData3
    {
        public const byte KIND_STRING = 0;
        public const byte KIND_ARRAY = 1;
        public const byte KIND_MISSING = 2;
        public const byte KIND_PLURAL = 3;

        public string LanguageCode;
        public ulong KeySetId;
        public int EntryCount;
        public BlocTextEncoding TextEncoding;
        public ushort WindowBase;

        /// <summary>The decompressed DATA section.</summary>
        public byte[] Raw;

        /// <summary>Per entry: one of the KIND_ constants.</summary>
        public byte[] Kinds;

        /// <summary>
        /// Per entry: the string id for a string; for an array or plural, the index in <see cref="Items"/>
        /// of its header (array: item count, plural: category mask) followed by its string ids.
        /// </summary>
        public int[] Refs;

        public int[] Items;

        /// <summary>Start of each string in <see cref="Raw"/>, plus one final end offset.</summary>
        public int[] StringOffsets;

        public int KeysStart;

        public int StringCount => StringOffsets.Length - 1;

        /// <summary>
        /// Parses a whole file. The fast mode checks checksums and structure; deep mode also decodes every
        /// string strictly and checks key order, key encoding and uniqueness.
        /// </summary>
        public static BlocData3 Parse(byte[] file, bool deep)
        {
            if (!BlocFormat3.Header.TryParse(file, file.Length, out var header, out string error))
                throw new InvalidDataException(error);

            var data = new BlocData3
            {
                LanguageCode = header.LanguageCode,
                KeySetId = header.KeySetId,
                EntryCount = header.EntryCount,
                TextEncoding = header.TextEncoding,
                WindowBase = header.WindowBase
            };

            foreach (var section in header.Sections)
            {
                if (section.Tag != BlocFormat3.TAG_DATA && !deep)
                    continue; // Optional section this reader does not use.

                if (XxHash.Hash32(new ReadOnlySpan<byte>(file, section.Offset, section.StoredSize)) != section.Checksum)
                    throw new InvalidDataException($"File damaged (section '{section.TagName}' checksum mismatch)");

                if (section.Tag == BlocFormat3.TAG_DATA)
                    data.Raw = Decompress(file, section);
            }

            data.ParseRaw(deep);
            return data;
        }

        private static byte[] Decompress(byte[] file, BlocFormat3.Section section)
        {
            var raw = new byte[section.RawSize];
            if (section.Codec == BlocFormat3.CODEC_NONE)
            {
                Buffer.BlockCopy(file, section.Offset, raw, 0, section.RawSize);
                return raw;
            }

            using var input = new MemoryStream(file, section.Offset, section.StoredSize, false);
            using var deflate = new DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);

            int total = 0;
            while (total < raw.Length)
            {
                int read = deflate.Read(raw, total, raw.Length - total);
                if (read <= 0)
                    throw new InvalidDataException("File damaged (DATA shorter than declared)");
                total += read;
            }

            var probe = new byte[1];
            if (deflate.Read(probe, 0, 1) != 0)
                throw new InvalidDataException("File damaged (DATA longer than declared)");

            return raw;
        }

        private void ParseRaw(bool deep)
        {
            byte[] raw = Raw;
            int position = 0;
            int count = EntryCount;

            // Every entry takes at least one byte, so this bounds the allocations below.
            if (count > raw.Length)
                throw new InvalidDataException("Invalid entry count");

            Kinds = new byte[count];
            Refs = new int[count];
            var items = new List<int>();
            int stringCount = 0;

            for (int i = 0; i < count; i++)
            {
                uint tag = ReadVarUInt(raw, ref position);
                uint payload = tag >> 2;
                switch ((int)(tag & 3))
                {
                    case BlocFormat3.TAG_KIND_STRING:
                        Kinds[i] = KIND_STRING;
                        Refs[i] = ResolveReference(payload, ref stringCount);
                        break;
                    case BlocFormat3.TAG_KIND_ARRAY:
                        if (payload > (uint)(raw.Length - position))
                            throw new InvalidDataException("Invalid array length");
                        Kinds[i] = KIND_ARRAY;
                        Refs[i] = items.Count;
                        items.Add((int)payload);
                        for (uint j = 0; j < payload; j++)
                            items.Add(ResolveReference(ReadVarUInt(raw, ref position), ref stringCount));
                        break;
                    case BlocFormat3.TAG_KIND_EXTENDED:
                        if (payload == BlocFormat3.EXTENDED_MISSING)
                        {
                            Kinds[i] = KIND_MISSING;
                            Refs[i] = -1;
                        }
                        else if (payload == BlocFormat3.EXTENDED_PLURAL)
                        {
                            if (position >= raw.Length)
                                throw new InvalidDataException("Truncated plural entry");
                            byte mask = raw[position++];
                            if ((mask & ~BlocFormat3.PLURAL_VALID_BITS) != 0 || (mask & BlocFormat3.PLURAL_OTHER_BIT) == 0)
                                throw new InvalidDataException("Invalid plural forms");
                            Kinds[i] = KIND_PLURAL;
                            Refs[i] = items.Count;
                            items.Add(mask);
                            for (int bit = 0; bit < PluralValue.CategoryCount; bit++)
                            {
                                if ((mask & (1 << bit)) != 0)
                                    items.Add(ResolveReference(ReadVarUInt(raw, ref position), ref stringCount));
                            }
                        }
                        else
                        {
                            throw new InvalidDataException($"Unknown entry type {payload}");
                        }
                        break;
                    default:
                        throw new InvalidDataException("Unknown entry kind");
                }
            }

            Items = items.ToArray();

            // LENGTHS, then TEXT right after it.
            var lengths = new int[stringCount];
            long textLength = 0;
            for (int i = 0; i < stringCount; i++)
            {
                uint length = ReadVarUInt(raw, ref position);
                if (length > (uint)raw.Length)
                    throw new InvalidDataException("Invalid string length");
                lengths[i] = (int)length;
                textLength += length;
            }

            if (textLength > raw.Length - position)
                throw new InvalidDataException("Text section truncated");

            StringOffsets = new int[stringCount + 1];
            int offset = position;
            for (int i = 0; i < stringCount; i++)
            {
                StringOffsets[i] = offset;
                offset += lengths[i];
                if (TextEncoding == BlocTextEncoding.Utf16 && (lengths[i] & 1) != 0)
                    throw new InvalidDataException("UTF-16 text has an odd byte length");
            }
            StringOffsets[stringCount] = offset;
            KeysStart = offset;

            if (XxHash.Hash64(new ReadOnlySpan<byte>(raw, KeysStart, raw.Length - KeysStart)) != KeySetId)
                throw new InvalidDataException("File damaged (key set id mismatch)");

            if (deep)
                CheckDeep();
        }

        /// <summary>
        /// Reference 0 introduces the next new string; reference n reuses string n - 1.
        /// </summary>
        private static int ResolveReference(uint reference, ref int stringCount)
        {
            if (reference == 0)
            {
                if (stringCount >= BlocFormat3.MAX_ENTRIES)
                    throw new InvalidDataException("Too many strings");
                return stringCount++;
            }

            if (reference - 1 >= (uint)stringCount)
                throw new InvalidDataException("String reference out of range");
            return (int)(reference - 1);
        }

        private void CheckDeep()
        {
            for (int i = 0; i < StringCount; i++)
                GetString(i, strict: true);

            var hashes = new long[EntryCount];
            var names = new string[EntryCount];
            ReadKeys(hashes, names, strict: true);

            for (int i = 1; i < names.Length; i++)
            {
                if (string.CompareOrdinal(names[i - 1], names[i]) >= 0)
                    throw new InvalidDataException($"Keys are not in order at '{names[i]}'");
            }

            var seen = new Dictionary<long, string>(hashes.Length);
            for (int i = 0; i < hashes.Length; i++)
            {
                if (seen.TryGetValue(hashes[i], out string other))
                    throw new InvalidDataException($"Keys '{other}' and '{names[i]}' conflict (keys are case-insensitive)");
                seen.Add(hashes[i], names[i]);
            }
        }

        public string GetString(int id, bool strict = false)
        {
            int start = StringOffsets[id];
            return BlocText.Decode(new ReadOnlySpan<byte>(Raw, start, StringOffsets[id + 1] - start), TextEncoding, WindowBase, strict);
        }

        /// <summary>
        /// Decodes the KEYS block, filling the lookup hash of every key (same as
        /// <see cref="Hash64.CreateIgnoreCase(ReadOnlySpan{char})"/>) and, when given, the key names.
        /// ASCII keys are hashed straight from the bytes, resuming from the hash state of the prefix
        /// shared with the previous key.
        /// </summary>
        public void ReadKeys(long[] hashes, string[] names, bool strict)
        {
            const ulong FNV_OFFSET_BASIS = 14695981039346656037UL;
            const ulong FNV_PRIME = 1099511628211UL;

            byte[] raw = Raw;
            int position = KeysStart;
            var key = new byte[64];
            var states = new ulong[65];
            states[0] = FNV_OFFSET_BASIS;
            int keyLength = 0;
            int validStates = 0; // states[0..validStates] are correct for the current key bytes

            for (int i = 0; i < EntryCount; i++)
            {
                uint shared = ReadVarUInt(raw, ref position);
                uint suffix = ReadVarUInt(raw, ref position);
                if (shared > (uint)keyLength || suffix > (uint)(raw.Length - position) || shared + suffix == 0)
                    throw new InvalidDataException("Invalid key");

                int length = (int)(shared + suffix);
                if (length > key.Length)
                {
                    Array.Resize(ref key, Math.Max(length, key.Length * 2));
                    Array.Resize(ref states, key.Length + 1);
                }

                Buffer.BlockCopy(raw, position, key, (int)shared, (int)suffix);
                position += (int)suffix;
                keyLength = length;
                validStates = Math.Min(validStates, (int)shared);

                while (validStates < length && key[validStates] < 0x80)
                {
                    byte b = key[validStates];
                    if (b >= 'A' && b <= 'Z')
                        b += 'a' - 'A';
                    states[validStates + 1] = (states[validStates] ^ b) * FNV_PRIME;
                    validStates++;
                }

                string name = null;
                if (names != null || validStates < length)
                    name = DecodeKey(key, length, strict);

                hashes[i] = validStates == length ? (long)states[length] : Hash64.CreateIgnoreCase(name);
                if (names != null)
                    names[i] = name;
            }

            if (position != raw.Length)
                throw new InvalidDataException("Unexpected bytes after the keys");
        }

        public string[] ReadKeyNames()
        {
            var names = new string[EntryCount];
            ReadKeys(new long[EntryCount], names, strict: false);
            return names;
        }

        private static string DecodeKey(byte[] key, int length, bool strict)
        {
            return BlocText.Decode(new ReadOnlySpan<byte>(key, 0, length), BlocTextEncoding.Utf8, 0, strict);
        }

        /// <summary>Converts to entries for the editor and tools (all strings decoded).</summary>
        public IBlocEntry[] ToEntries()
        {
            string[] names = ReadKeyNames();
            var strings = new string[StringCount];
            for (int i = 0; i < strings.Length; i++)
                strings[i] = GetString(i);

            var entries = new IBlocEntry[EntryCount];
            for (int i = 0; i < entries.Length; i++)
            {
                switch (Kinds[i])
                {
                    case KIND_STRING:
                        entries[i] = new StringEntry() { Key = names[i], Value = strings[Refs[i]] };
                        break;
                    case KIND_ARRAY:
                        {
                            int start = Refs[i];
                            var values = new string[Items[start]];
                            for (int j = 0; j < values.Length; j++)
                                values[j] = strings[Items[start + 1 + j]];
                            entries[i] = new ArrayEntry() { Key = names[i], Values = values };
                            break;
                        }
                    case KIND_PLURAL:
                        {
                            int start = Refs[i];
                            int mask = Items[start];
                            var plural = new PluralValue();
                            int next = start + 1;
                            for (int category = 0; category < PluralValue.CategoryCount; category++)
                            {
                                if ((mask & (1 << category)) != 0)
                                    plural[(PluralCategory)category] = strings[Items[next++]];
                            }
                            entries[i] = new PluralEntry() { Key = names[i], Value = plural };
                            break;
                        }
                    default:
                        entries[i] = new MissingEntry() { Key = names[i] };
                        break;
                }
            }
            return entries;
        }

        private static uint ReadVarUInt(byte[] data, ref int position)
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                if (position >= data.Length)
                    throw new InvalidDataException("Unexpected end of data");

                byte b = data[position++];
                if (shift == 28 && b > 0x0F)
                    throw new InvalidDataException("Invalid varint");

                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return result;
            }
            throw new InvalidDataException("Invalid varint");
        }
    }
}
