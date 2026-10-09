using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PicoShot.Localization.Bloc
{
    /// <summary>
    /// How BLOC v3 stores the bytes of translated text.
    /// </summary>
    public enum BlocTextEncoding : byte
    {
        /// <summary>Standard UTF-8.</summary>
        Utf8 = 0,

        /// <summary>UTF-16 little-endian: smaller for CJK, and decoding is a plain copy.</summary>
        Utf16 = 1,

        /// <summary>
        /// One byte per character for text that is ASCII plus one alphabet:
        /// </summary>
        Window8 = 2
    }

    /// <summary>
    /// Encoding, decoding and choosing the text encoding of BLOC v3 files.
    /// </summary>
    internal static class BlocText
    {
        public const int WINDOW_SIZE = 127;
        public const byte WINDOW_ESCAPE = 0x80;
        public const byte WINDOW_FIRST = 0x81;

        private const int StackDecodeLimit = 512;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static bool IsKnown(BlocTextEncoding encoding) => encoding <= BlocTextEncoding.Window8;

        /// <summary>
        /// The window must stay inside the BMP and must not overlap the surrogate range.
        /// </summary>
        public static bool IsValidWindowBase(int windowBase)
        {
            if (windowBase < 0x80 || windowBase + WINDOW_SIZE - 1 > 0xFFFF)
                return false;

            return windowBase + WINDOW_SIZE - 1 < 0xD800 || windowBase > 0xDFFF;
        }

        #region Choosing

        /// <summary>
        /// Picks the encoding with the smallest text, which is also the size the runtime keeps in memory.
        /// Ties prefer UTF-8, then Window-8.
        /// </summary>
        public static BlocTextEncoding Choose(IReadOnlyList<string> strings, out ushort windowBase)
        {
            long utf8 = 0;
            long utf16 = 0;
            for (int i = 0; i < strings.Count; i++)
            {
                utf8 += Encoding.UTF8.GetByteCount(strings[i]);
                utf16 += strings[i].Length * 2L;
            }

            var best = BlocTextEncoding.Utf8;
            long bestSize = utf8;
            windowBase = 0;

            if (TryFindWindow(strings, out ushort window))
            {
                long size = 0;
                for (int i = 0; i < strings.Count; i++)
                    size += GetByteCount(strings[i], BlocTextEncoding.Window8, window);

                if (size < bestSize)
                {
                    best = BlocTextEncoding.Window8;
                    bestSize = size;
                    windowBase = window;
                }
            }

            if (utf16 < bestSize)
            {
                best = BlocTextEncoding.Utf16;
                windowBase = 0;
            }

            return best;
        }

        /// <summary>
        /// Finds the 127-character range that covers the most non-ASCII characters.
        /// </summary>
        private static bool TryFindWindow(IReadOnlyList<string> strings, out ushort windowBase)
        {
            windowBase = 0;
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < strings.Count; i++)
            {
                foreach (char c in strings[i])
                {
                    if (c < 0x80 || char.IsSurrogate(c))
                        continue;

                    counts.TryGetValue(c, out int count);
                    counts[c] = count + 1;
                }
            }

            if (counts.Count == 0)
                return false;

            var codePoints = new int[counts.Count];
            counts.Keys.CopyTo(codePoints, 0);
            Array.Sort(codePoints);

            int bestStart = 0;
            long bestCovered = -1;
            long covered = 0;
            int left = 0;
            for (int right = 0; right < codePoints.Length; right++)
            {
                covered += counts[codePoints[right]];
                while (codePoints[right] - codePoints[left] >= WINDOW_SIZE)
                {
                    covered -= counts[codePoints[left]];
                    left++;
                }

                if (covered > bestCovered)
                {
                    bestCovered = covered;
                    bestStart = codePoints[left];
                }
            }

            // Characters are never surrogates, so a window touching the surrogate range or the end of
            // the BMP can slide down and still cover the same characters.
            int start = bestStart;
            if (start < 0xD800)
                start = Math.Min(start, 0xD800 - WINDOW_SIZE);
            else
                start = Math.Min(start, 0x10000 - WINDOW_SIZE);

            if (!IsValidWindowBase(start))
                return false;

            windowBase = (ushort)start;
            return true;
        }

        #endregion

        #region Encoding

        public static int GetByteCount(string text, BlocTextEncoding encoding, ushort windowBase)
        {
            switch (encoding)
            {
                case BlocTextEncoding.Utf8:
                    return Encoding.UTF8.GetByteCount(text);
                case BlocTextEncoding.Utf16:
                    return text.Length * 2;
                case BlocTextEncoding.Window8:
                    int count = 0;
                    for (int i = 0; i < text.Length; i++)
                    {
                        char c = text[i];
                        if (c < 0x80 || IsInWindow(c, windowBase))
                        {
                            count++;
                            continue;
                        }

                        int charCount = ScalarCharCount(text, i);
                        count += 1 + Encoding.UTF8.GetByteCount(text.AsSpan(i, charCount));
                        i += charCount - 1;
                    }
                    return count;
                default:
                    throw new ArgumentOutOfRangeException(nameof(encoding));
            }
        }

        public static void Encode(string text, BlocTextEncoding encoding, ushort windowBase, BlocByteBuffer output)
        {
            switch (encoding)
            {
                case BlocTextEncoding.Utf8:
                    {
                        int length = Encoding.UTF8.GetByteCount(text);
                        Encoding.UTF8.GetBytes(text.AsSpan(), output.Reserve(length));
                        break;
                    }
                case BlocTextEncoding.Utf16:
                    {
                        var target = output.Reserve(text.Length * 2);
                        for (int i = 0; i < text.Length; i++)
                        {
                            char c = text[i];
                            target[i * 2] = (byte)c;
                            target[i * 2 + 1] = (byte)(c >> 8);
                        }
                        break;
                    }
                case BlocTextEncoding.Window8:
                    {
                        Span<byte> scalar = stackalloc byte[8];
                        for (int i = 0; i < text.Length; i++)
                        {
                            char c = text[i];
                            if (c < 0x80)
                            {
                                output.WriteByte((byte)c);
                                continue;
                            }

                            if (IsInWindow(c, windowBase))
                            {
                                output.WriteByte((byte)(WINDOW_FIRST + (c - windowBase)));
                                continue;
                            }

                            int charCount = ScalarCharCount(text, i);
                            int written = Encoding.UTF8.GetBytes(text.AsSpan(i, charCount), scalar);
                            output.WriteByte(WINDOW_ESCAPE);
                            output.Write(scalar.Slice(0, written));
                            i += charCount - 1;
                        }
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(encoding));
            }
        }

        private static bool IsInWindow(char c, ushort windowBase)
        {
            return windowBase != 0 && c >= windowBase && c < windowBase + WINDOW_SIZE;
        }

        /// <summary>2 for a valid surrogate pair at <paramref name="index"/>, otherwise 1.</summary>
        private static int ScalarCharCount(string text, int index)
        {
            return char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
        }

        #endregion

        #region Decoding

        /// <summary>
        /// Decodes one string. With <paramref name="strict"/>, invalid bytes throw
        /// <see cref="InvalidDataException"/>; otherwise they become U+FFFD.
        /// </summary>
        public static string Decode(ReadOnlySpan<byte> bytes, BlocTextEncoding encoding, ushort windowBase, bool strict)
        {
            if (bytes.IsEmpty)
                return string.Empty;

            switch (encoding)
            {
                case BlocTextEncoding.Utf8:
                    if (!strict)
                        return Encoding.UTF8.GetString(bytes);

                    try
                    {
                        return StrictUtf8.GetString(bytes);
                    }
                    catch (DecoderFallbackException ex)
                    {
                        throw new InvalidDataException("Invalid UTF-8 text", ex);
                    }
                case BlocTextEncoding.Utf16:
                    return DecodeUtf16(bytes);
                case BlocTextEncoding.Window8:
                    return DecodeWindow8(bytes, windowBase, strict);
                default:
                    throw new InvalidDataException($"Unknown text encoding {(byte)encoding}");
            }
        }

        private static string DecodeUtf16(ReadOnlySpan<byte> bytes)
        {
            if ((bytes.Length & 1) != 0)
                throw new InvalidDataException("UTF-16 text has an odd byte length");

            if (BitConverter.IsLittleEndian)
                return new string(MemoryMarshal.Cast<byte, char>(bytes));

            var chars = new char[bytes.Length / 2];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = (char)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
            return new string(chars);
        }

        private static string DecodeWindow8(ReadOnlySpan<byte> bytes, ushort windowBase, bool strict)
        {
            // Every encoded character takes at least as many bytes as it has UTF-16 chars.
            char[] rented = null;
            Span<char> chars = bytes.Length <= StackDecodeLimit
                ? stackalloc char[bytes.Length]
                : (rented = ArrayPool<char>.Shared.Rent(bytes.Length));

            try
            {
                int written = 0;
                int position = 0;
                while (position < bytes.Length)
                {
                    byte b = bytes[position];
                    if (b < 0x80)
                    {
                        chars[written++] = (char)b;
                        position++;
                    }
                    else if (b >= WINDOW_FIRST)
                    {
                        chars[written++] = (char)(windowBase + (b - WINDOW_FIRST));
                        position++;
                    }
                    else if (TryDecodeEscape(bytes, position + 1, out int scalar, out int length))
                    {
                        if (scalar >= 0x10000)
                        {
                            scalar -= 0x10000;
                            chars[written++] = (char)(0xD800 + (scalar >> 10));
                            chars[written++] = (char)(0xDC00 + (scalar & 0x3FF));
                        }
                        else
                        {
                            chars[written++] = (char)scalar;
                        }
                        position += 1 + length;
                    }
                    else
                    {
                        if (strict)
                            throw new InvalidDataException("Invalid escaped character in Window-8 text");

                        chars[written++] = '�';
                        position++;
                    }
                }

                return new string(chars.Slice(0, written));
            }
            finally
            {
                if (rented != null)
                    ArrayPool<char>.Shared.Return(rented);
            }
        }

        /// <summary>Decodes one well-formed UTF-8 scalar (2-4 bytes) at <paramref name="start"/>.</summary>
        private static bool TryDecodeEscape(ReadOnlySpan<byte> bytes, int start, out int scalar, out int length)
        {
            scalar = 0;
            length = 0;
            if (start >= bytes.Length)
                return false;

            int lead = bytes[start];
            int minimum;
            if (lead >= 0xC2 && lead <= 0xDF) { length = 2; scalar = lead & 0x1F; minimum = 0x80; }
            else if (lead >= 0xE0 && lead <= 0xEF) { length = 3; scalar = lead & 0x0F; minimum = 0x800; }
            else if (lead >= 0xF0 && lead <= 0xF4) { length = 4; scalar = lead & 0x07; minimum = 0x10000; }
            else return false;

            if (start + length > bytes.Length)
                return false;

            for (int i = 1; i < length; i++)
            {
                int next = bytes[start + i];
                if ((next & 0xC0) != 0x80)
                    return false;
                scalar = (scalar << 6) | (next & 0x3F);
            }

            if (scalar < minimum || scalar > 0x10FFFF || (scalar >= 0xD800 && scalar <= 0xDFFF))
                return false;

            return true;
        }

        #endregion
    }

    /// <summary>
    /// Growable byte buffer with varint writing, used to build BLOC v3 files.
    /// </summary>
    internal sealed class BlocByteBuffer
    {
        private byte[] _buffer;

        public int Length { get; private set; }

        public BlocByteBuffer(int capacity = 256)
        {
            _buffer = new byte[Math.Max(16, capacity)];
        }

        public ReadOnlySpan<byte> Span => new ReadOnlySpan<byte>(_buffer, 0, Length);

        public void WriteByte(byte value)
        {
            Reserve(1)[0] = value;
        }

        public void Write(ReadOnlySpan<byte> data)
        {
            data.CopyTo(Reserve(data.Length));
        }

        public void WriteVarUInt(uint value)
        {
            while (value >= 0x80)
            {
                WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            WriteByte((byte)value);
        }

        /// <summary>Appends <paramref name="count"/> bytes and returns them for writing.</summary>
        public Span<byte> Reserve(int count)
        {
            int required = Length + count;
            if (required > _buffer.Length)
            {
                int capacity = Math.Max(required, _buffer.Length * 2);
                Array.Resize(ref _buffer, capacity);
            }

            var span = new Span<byte>(_buffer, Length, count);
            Length = required;
            return span;
        }
    }
}
