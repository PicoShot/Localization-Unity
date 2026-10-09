using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using PicoShot.Localization.Utils;
using static PicoShot.Localization.Bloc.BlocFormat;

namespace PicoShot.Localization.Bloc
{
    public unsafe static class BlocFormat2
    {
        public const int VERSION = 2;
        public const int HEADER_SIZE = 34;
        public const int FOOTER_SIZE = 4;

        public const int CONTENT_START = MAGIC_AND_VERSION_SIZE + HEADER_SIZE;

        public const int LANGUAGE_CODE_SIZE = 12;

        public static BlocFormatLayout FormatLayout = new(VERSION, Validate, Serialize, Deserialize, ReadInfo);
        public static bool Validate(BinaryReader reader, out string languageCode)
        {
            languageCode = null;

            if (reader.BaseStream.Length < (FILE_MIN_SIZE + HEADER_SIZE + FOOTER_SIZE))
                return false;

            var header = ReadHeader(reader);
            languageCode = ReadLanguageCode(ref header);

            if (header.contentSize > MaxContentSize)
                return false;

            int contentSize = (int)header.contentSize;
            var contentData = ArrayPool<byte>.Shared.Rent(contentSize);
            try
            {
                if (!ReadContent(reader, header, contentData, contentSize))
                    return false;

                uint storedCrc = ReadFooter(reader).contentCRC;
                return storedCrc == ComputeCrc32(contentData.AsSpan(0, contentSize));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(contentData);
            }
        }

        /// <summary>
        /// Reads only the header: format checks and the language code, without decompressing
        /// or checksumming the content. The full check happens when the file is loaded.
        /// </summary>
        public static bool ReadInfo(BinaryReader reader, out string languageCode)
        {
            languageCode = null;

            if (reader.BaseStream.Length < (FILE_MIN_SIZE + HEADER_SIZE + FOOTER_SIZE))
                return false;

            var header = ReadHeader(reader);
            languageCode = ReadLanguageCode(ref header);
            return header.contentSize <= MaxContentSize;
        }

        private const uint MaxContentSize = 100_000_000; // 100MB sanity check

        private static string ReadLanguageCode(ref Header header)
        {
            fixed (byte* code = header.languageCode)
            {
                int length = 0;
                while (length < LANGUAGE_CODE_SIZE && code[length] != 0) length++;
                return Encoding.ASCII.GetString(code, length);
            }
        }

        /// <summary>
        /// Reads (and decompresses if needed) exactly <paramref name="contentSize"/> bytes of content,
        /// then positions the reader on the footer.
        /// </summary>
        private static bool ReadContent(BinaryReader reader, Header header, byte[] buffer, int contentSize)
        {
            Stream baseStream = reader.BaseStream;
            long footerPosition = baseStream.Length - FOOTER_SIZE;
            int totalRead = 0;

            if ((header.flags & BlocFlags.IsCompressed) != 0)
            {
                using var contentSection = new SectionStream(baseStream, CONTENT_START, footerPosition - CONTENT_START);
                using var deflateStream = new DeflateStream(contentSection, CompressionMode.Decompress);

                while (totalRead < contentSize)
                {
                    int r = deflateStream.Read(buffer, totalRead, contentSize - totalRead);
                    if (r == 0)
                        break;

                    totalRead += r;
                }
            }
            else
            {
                baseStream.Position = CONTENT_START;
                while (totalRead < contentSize)
                {
                    int r = baseStream.Read(buffer, totalRead, contentSize - totalRead);
                    if (r == 0)
                        break;

                    totalRead += r;
                }
            }

            baseStream.Position = footerPosition;
            return totalRead == contentSize;
        }
        public static void Serialize(BinaryWriter writer, in IBlocEntry[] entries, string languageCode, CompressionLevel compressionLevel)
        {
            var header = new Header()
            {
                flags = compressionLevel != CompressionLevel.NoCompression ? BlocFlags.IsCompressed : 0,
            };

            if (string.IsNullOrEmpty(languageCode) || Encoding.ASCII.GetByteCount(languageCode) > LANGUAGE_CODE_SIZE)
                throw new ArgumentException($"Language code must be 1-{LANGUAGE_CODE_SIZE} ASCII characters: '{languageCode}'", nameof(languageCode));

            Encoding.ASCII.GetBytes(languageCode, new Span<byte>(header.languageCode, LANGUAGE_CODE_SIZE));

            Crc32 contentCrc = new Crc32();
            using (var contentStream = new MemoryStream())
            {
                using var contentWriter = new BinaryWriter(contentStream);

                contentStream.Position = MAGIC_AND_VERSION_SIZE + HEADER_SIZE;
                contentStream.SetLength(CONTENT_START);

                var stringPool = new Dictionary<string, int>();

                //Write entries
                header.entries.offset = (uint)contentStream.Position; //This offset start from after header
                header.entries.count = (uint)entries.Length;
                foreach (var kvp in entries)
                {
                    if (stringPool.TryGetValue(kvp.Key, out var fi))
                        contentWriter.Write((uint)fi);
                    else
                    {
                        contentWriter.Write((uint)stringPool.Count);
                        stringPool.Add(kvp.Key, stringPool.Count);
                    }

                    switch (kvp)
                    {
                        case StringEntry se:
                            if (stringPool.TryGetValue(se.Value, out var fi1))
                                contentWriter.Write((uint)fi1);
                            else
                            {
                                contentWriter.Write((uint)stringPool.Count);
                                stringPool.Add(se.Value, stringPool.Count);
                            }

                            break;
                        case ArrayEntry ae:
                            contentWriter.Write((uint)(0x80000000 | ae.Values.Length));
                            foreach (var val in ae.Values)
                            {
                                if (stringPool.TryGetValue(val, out var fi2))
                                    contentWriter.Write((uint)fi2);
                                else
                                {
                                    contentWriter.Write((uint)stringPool.Count);
                                    stringPool.Add(val, stringPool.Count);
                                }
                            }
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                }

                //Write strings
                header.strings.offset = (uint)contentStream.Position; //This offset start from after header
                header.strings.count = (uint)stringPool.Count;
                foreach (var str in stringPool)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(str.Key);
                    WriteVarInt(contentWriter, (uint)bytes.Length);
                    contentWriter.Write(bytes);
                }

                header.contentSize = (uint)contentStream.Position;

                //Write to main writer
                const int TEMPBUFFER_SIZE = 512;

                writer.BaseStream.Position = MAGIC_AND_VERSION_SIZE + HEADER_SIZE;
                contentStream.Position = 0;

                contentCrc.Reset();

                var tempBuffer = ArrayPool<byte>.Shared.Rent(TEMPBUFFER_SIZE);
                try
                {
                    if (compressionLevel != CompressionLevel.NoCompression)
                    {
                        using (var deflateStream = new DeflateStream(writer.BaseStream, compressionLevel, true))
                        {
                            int totalRead = 0;
                            while (totalRead < contentStream.Length)
                            {
                                int r = contentStream.Read(tempBuffer, 0, TEMPBUFFER_SIZE);
                                if (r == 0)
                                    break;

                                deflateStream.Write(tempBuffer, 0, r);
                                contentCrc.Append(tempBuffer.AsSpan(0, r));

                                totalRead += r;
                            }
                        }
                    }
                    else
                    {
                        int totalRead = 0;
                        while (totalRead < contentStream.Length)
                        {
                            int r = contentStream.Read(tempBuffer, 0, TEMPBUFFER_SIZE);
                            if (r == 0)
                                break;

                            writer.Write(tempBuffer, 0, r);
                            contentCrc.Append(tempBuffer.AsSpan(0, r));

                            totalRead += r;
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(tempBuffer);
                }
            }

            //Write footer
            WriteFooter(writer, new Footer()
            {
                contentCRC = contentCrc.GetCurrentHashAsUInt32()
            });

            //Write header, the reason I put the header last is because some values ??are not predictable.
            writer.BaseStream.Position = MAGIC_AND_VERSION_SIZE;
            WriteHeader(writer, header);
        }
        public static void Deserialize(BinaryReader reader, out IBlocEntry[] entries, out BlocInfo info)
        {
            info = new BlocInfo()
            {
                Version = VERSION
            };

            var header = ReadHeader(reader);
            info.LanguageCode = ReadLanguageCode(ref header);

            if (header.contentSize > MaxContentSize)
                throw new InvalidDataException("Invalid content size");

            int contentSize = (int)header.contentSize;
            var contentData = ArrayPool<byte>.Shared.Rent(contentSize);
            var contentSpan = contentData.AsSpan(0, contentSize);
            try
            {
                if (!ReadContent(reader, header, contentData, contentSize))
                    throw new InvalidDataException("File truncated");

                var footer = ReadFooter(reader);
                if (footer.contentCRC != ComputeCrc32(contentSpan))
                    throw new FileLoadException("File damaged (CRC mismatch)");

                int position = (int)header.strings.offset;
                var stringPool = new string[header.strings.count];
                for (int i = 0; i < stringPool.Length; i++)
                {
                    uint length = ReadVarInt(contentSpan, ref position);
                    if (length > (uint)(contentSize - position))
                        throw new InvalidDataException($"Invalid string length: {length}");

                    stringPool[i] = Encoding.UTF8.GetString(contentData, position, (int)length);
                    position += (int)length;
                }

                info.EntryCount = header.entries.count;

                position = (int)header.entries.offset;
                entries = new IBlocEntry[header.entries.count];
                for (int i = 0; i < entries.Length; i++)
                {
                    uint keyId = ReadUInt32(contentSpan, ref position);
                    uint valueRef = ReadUInt32(contentSpan, ref position);

                    string key = stringPool[keyId];

                    if ((valueRef & 0x80000000) != 0)
                    {
                        int count = (int)(valueRef & 0x7FFFFFFF);
                        var values = new string[count];

                        for (int j = 0; j < count; j++)
                        {
                            uint itemId = ReadUInt32(contentSpan, ref position);
                            values[j] = stringPool[itemId];
                        }

                        entries[i] = new ArrayEntry()
                        {
                            Key = key,
                            Values = values,
                        };
                    }
                    else
                    {
                        entries[i] = new StringEntry()
                        {
                            Key = key,
                            Value = stringPool[valueRef],
                        };
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(contentData);
            }
        }
        private static uint ReadUInt32(ReadOnlySpan<byte> data, ref int position)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position, 4));
            position += 4;
            return value;
        }

        private static uint ReadVarInt(ReadOnlySpan<byte> data, ref int position)
        {
            uint result = 0;
            int shift = 0;
            byte b;
            do
            {
                if (shift > 28)
                    throw new InvalidDataException("Invalid varint");

                b = data[position++];
                result |= (uint)(b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            return result;
        }

        public static Header ReadHeader(BinaryReader reader)
        {
            if (!EnsureLength(reader, HEADER_SIZE))
                throw new InvalidDataException("Size too low");

            var header = new Header();

            header.flags = (BlocFlags)reader.ReadUInt16();
            reader.Read(new Span<byte>(header.languageCode, LANGUAGE_CODE_SIZE));
            header.entries = new(reader.ReadUInt32(), reader.ReadUInt32());
            header.strings = new(reader.ReadUInt32(), reader.ReadUInt32());
            header.contentSize = reader.ReadUInt32();

            return header;
        }
        public static Footer ReadFooter(BinaryReader reader)
        {
            if (!EnsureLength(reader, FOOTER_SIZE))
                throw new InvalidDataException("Size too low");

            return new Footer()
            {
                contentCRC = reader.ReadUInt32()
            };
        }
        public static void WriteHeader(BinaryWriter writer, Header header)
        {
            writer.Write((ushort)header.flags);
            writer.Write(new Span<byte>(header.languageCode, LANGUAGE_CODE_SIZE));

            writer.Write(header.entries.offset);
            writer.Write(header.entries.count);

            writer.Write(header.strings.offset);
            writer.Write(header.strings.count);

            writer.Write(header.contentSize);
        }
        public static void WriteFooter(BinaryWriter writer, Footer footer)
        {
            writer.Write(footer.contentCRC);
        }

        public struct Header//34 byte
        {
            public BlocFlags flags;//Bit flags
            public fixed byte languageCode[LANGUAGE_CODE_SIZE];//ASCII encoded string
            public DataSpan entries;
            public DataSpan strings;
            public uint contentSize;//uncompressed content size
        }

        public struct Footer//4 byte
        {
            public uint contentCRC;//content checksum
        }

        public struct Entry//8* byte
        {
            public uint keyIndex;//key string index

            /// <summary>
            /// isArray: 0x80000000 != 0
            /// 
            /// String Entry:
            /// -value string index: 0xFFFFFFFF
            /// Array Entry:
            /// -value count: 0x7FFFFFFF
            /// </summary>
            public uint stringIndex;
            //uint[] stringIndices;//values string indices
        }

        [Flags]
        public enum BlocFlags : ushort
        {
            None = 0,
            IsCompressed = 1 << 0,
        }
    }

    public class Crc32
    {
        private uint _crc = 0xFFFFFFFF;

        public void Reset()
        {
            _crc = 0xFFFFFFFF;
        }

        public void Append(ReadOnlySpan<byte> data)
        {
            BlocFormat.ComputeCrc32(ref _crc, data);
        }

        public uint GetCurrentHashAsUInt32()
        {
            return ~_crc;
        }
    }
}
