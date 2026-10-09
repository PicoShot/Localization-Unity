using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using static PicoShot.Localization.Bloc.BlocFormat;

namespace PicoShot.Localization.Bloc
{
    public unsafe static class BlocFormat1
    {
        public const int VERSION = 1;
        public const int HEADER_SIZE = 26;
        public const int FOOTER_SIZE = 4;

        public const int LANGUAGE_CODE_SIZE = 12;

        public static BlocFormatLayout FormatLayout = new(VERSION, Validate, Serialize, Deserialize, ReadInfo);
        public static bool Validate(BinaryReader reader, out string languageCode)
        {
            languageCode = null;

            if (reader.BaseStream.Length < (FILE_MIN_SIZE + HEADER_SIZE + 4))
                return false;

            var header = ReadHeader(reader);
            languageCode = ReadLanguageCode(ref header);

            if ((header.flags & BlocFlags.IsCompressed) != 0)
                return true;

            int dataSize = (int)(reader.BaseStream.Length - reader.BaseStream.Position - 4);
            if (dataSize < 0)
                return false;

            var data = reader.ReadBytes(dataSize);

            uint storedCrc = reader.ReadUInt32();
            uint computedCrc = ComputeCrc32(data);

            return IsCrcMatch(storedCrc, computedCrc);
        }

        /// <summary>
        /// Reads only the header: format checks and the language code.
        /// </summary>
        public static bool ReadInfo(BinaryReader reader, out string languageCode)
        {
            languageCode = null;

            if (reader.BaseStream.Length < (FILE_MIN_SIZE + HEADER_SIZE + 4))
                return false;

            var header = ReadHeader(reader);
            languageCode = ReadLanguageCode(ref header);
            return true;
        }

        private static bool IsCrcMatch(uint storedCrc, uint computedCrc)
        {
            return storedCrc == computedCrc || storedCrc == ~computedCrc;
        }

        private static string ReadLanguageCode(ref Header header)
        {
            fixed (byte* code = header.languageCode)
            {
                int length = 0;
                while (length < LANGUAGE_CODE_SIZE && code[length] != 0) length++;
                return Encoding.ASCII.GetString(code, length);
            }
        }
        public static void Serialize(BinaryWriter writer, in IBlocEntry[] entries, string languageCode, CompressionLevel compressionLevel)
        {
            var header = new Header()
            {
                flags = (compressionLevel != CompressionLevel.NoCompression ? BlocFlags.IsCompressed : 0),
            };

            Encoding.ASCII.GetBytes(languageCode, new Span<byte>(header.languageCode, LANGUAGE_CODE_SIZE));

            using (var contentStream = new MemoryStream())
            {
                using var contentWriter = new BinaryWriter(contentStream);

                contentStream.Position = MAGIC_AND_VERSION_SIZE + HEADER_SIZE;
                contentStream.SetLength(MAGIC_AND_VERSION_SIZE + HEADER_SIZE);

                var stringPool = new Dictionary<string, int>();
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

                header.stringPoolOffset = (uint)contentStream.Position;

                foreach (var str in stringPool)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(str.Key);
                    WriteVarInt(contentWriter, (uint)bytes.Length);
                    contentWriter.Write(bytes);
                }

                header.entryCount = (uint)entries.Length;
                header.stringCount = (uint)stringPool.Count;

                writer.BaseStream.Position = MAGIC_AND_VERSION_SIZE + HEADER_SIZE;
                const int TEMPBUFFER_SIZE = 512;
                if (compressionLevel != CompressionLevel.NoCompression)
                {
                    contentWriter.Write(0u);//none crc32, wrong but required

                    //Write content header
                    contentStream.Position = 0;
                    contentWriter.Write(MAGIC);
                    contentWriter.Write((ushort)VERSION);
                    WriteHeader(contentWriter, header);

                    using var deflateStream = new DeflateStream(writer.BaseStream, compressionLevel, true);

                    contentStream.Position = 0;
                    contentStream.WriteTo(deflateStream);

                    header.stringPoolOffset = (uint)contentStream.Length;
                }
                else
                {
                    contentStream.Position = 0;

                    uint crc = 0xFFFFFFFF;
                    var tempBuffer = ArrayPool<byte>.Shared.Rent(TEMPBUFFER_SIZE);

                    try
                    {
                        int totalRead = 0;
                        while (totalRead < contentStream.Length)
                        {
                            int r = contentStream.Read(tempBuffer, 0, TEMPBUFFER_SIZE);
                            if (r == 0)
                                break;

                            writer.Write(tempBuffer, 0, r);
                            ComputeCrc32(ref crc, tempBuffer[..r]);
                            totalRead += r;
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(tempBuffer);
                    }

                    writer.Write(crc);
                }
            }

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

            bool isCompressed = (header.flags & BlocFlags.IsCompressed) != 0;
            byte[] uncompressedData = null;
            if (isCompressed)
            {
                int uncompressedSize = (int)header.stringPoolOffset;
                int compressedDataLength = (int)reader.BaseStream.Length - (MAGIC_AND_VERSION_SIZE + HEADER_SIZE);

                if (uncompressedSize < 0 || uncompressedSize > 100_000_000) // 100MB sanity check
                    throw new InvalidDataException("Invalid uncompressed size");

                using var deflateStream = new DeflateStream(reader.BaseStream, CompressionMode.Decompress);
                uncompressedData = new byte[uncompressedSize];

                int totalRead = 0;
                while (totalRead < uncompressedSize)
                {
                    int r = deflateStream.Read(uncompressedData, totalRead, uncompressedSize - totalRead);

                    if (r == 0)
                        break;

                    totalRead += r;
                }
            }
            else
            {
                int dataSize = (int)reader.BaseStream.Length - 32 - 4;
                uncompressedData = reader.ReadBytes(dataSize);

                var storedCrc = reader.ReadUInt32();
                uint contentCrc = ComputeCrc32(uncompressedData);

                if (!IsCrcMatch(storedCrc, contentCrc))
                    throw new FileLoadException("File damaged (CRC mismatch)");
            }

            using var contentStream = new MemoryStream(uncompressedData);
            using var contentReader = new BinaryReader(contentStream);

            uint entryCount;
            uint stringCount;
            uint stringPoolOffset;

            if (isCompressed)
            {
                contentStream.Position = 20;
                entryCount = contentReader.ReadUInt32();
                stringCount = contentReader.ReadUInt32();
                stringPoolOffset = contentReader.ReadUInt32();
            }
            else
            {
                entryCount = header.entryCount;
                stringCount = header.stringCount;
                stringPoolOffset = header.stringPoolOffset;
            }

            contentStream.Position = stringPoolOffset;
            var stringPool = ReadStringPool(contentReader, stringCount);
            contentStream.Position = 32;

            info.EntryCount = entryCount;

            entries = new IBlocEntry[entryCount];
            for (int i = 0; i < entryCount; i++)
            {
                uint keyId = contentReader.ReadUInt32();
                uint valueRef = contentReader.ReadUInt32();

                string key = stringPool[keyId];

                if ((valueRef & 0x80000000) != 0)
                {
                    int count = (int)(valueRef & 0x7FFFFFFF);
                    var values = new string[count];

                    for (int j = 0; j < count; j++)
                    {
                        uint itemId = contentReader.ReadUInt32();
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
                    uint valIndex = valueRef & 0xFFFFFFFF;

                    entries[i] = new StringEntry()
                    {
                        Key = key,
                        Value = stringPool[valIndex],
                    };
                }
            }
        }

        private static string[] ReadStringPool(BinaryReader reader, uint count)
        {
            var pool = new string[count];

            for (int i = 0; i < count; i++)
            {
                uint length = ReadVarInt(reader);
                if (length > reader.BaseStream.Length - reader.BaseStream.Position)
                    throw new InvalidDataException($"Invalid string length: {length}");

                byte[] bytes = reader.ReadBytes((int)length);
                pool[i] = Encoding.UTF8.GetString(bytes);
            }

            return pool;
        }

        public static Header ReadHeader(BinaryReader reader)
        {
            if (!EnsureLength(reader, HEADER_SIZE))
                throw new InvalidDataException("Size too low");

            var header = new Header();

            header.flags = (BlocFlags)reader.ReadUInt16();
            reader.Read(new Span<byte>(header.languageCode, LANGUAGE_CODE_SIZE));
            header.entryCount = reader.ReadUInt32();
            header.stringCount = reader.ReadUInt32();
            header.stringPoolOffset = reader.ReadUInt32();

            return header;
        }
        public static void WriteHeader(BinaryWriter writer, Header header)
        {
            writer.Write((ushort)header.flags);
            writer.Write(new Span<byte>(header.languageCode, LANGUAGE_CODE_SIZE));
            writer.Write(header.entryCount);
            writer.Write(header.stringCount);
            writer.Write(header.stringPoolOffset);
        }

        public struct Header//26 byte
        {
            public BlocFlags flags;//Bit flags
            public fixed byte languageCode[LANGUAGE_CODE_SIZE];//ASCII encoded string
            public uint entryCount;
            public uint stringCount;
            public uint stringPoolOffset;
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

        [Flags]
        public enum BlocFlags : ushort
        {
            None = 0,
            IsCompressed = 1 << 0,
        }
    }
}
