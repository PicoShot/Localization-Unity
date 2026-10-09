using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace PicoShot.Localization.Hashing
{
    /// <summary>
    /// xxHash32 and xxHash64 (seed 0), used by BLOC v3 for checksums and key set ids.
    /// </summary>
    public static class XxHash
    {
        private const uint P32_1 = 2654435761U;
        private const uint P32_2 = 2246822519U;
        private const uint P32_3 = 3266489917U;
        private const uint P32_4 = 668265263U;
        private const uint P32_5 = 374761393U;

        private const ulong P64_1 = 11400714785074694791UL;
        private const ulong P64_2 = 14029467366897019727UL;
        private const ulong P64_3 = 1609587929392839161UL;
        private const ulong P64_4 = 9650029242287828579UL;
        private const ulong P64_5 = 2870177450012600261UL;

        public static uint Hash32(ReadOnlySpan<byte> data)
        {
            int length = data.Length;
            int position = 0;
            uint hash;

            if (length >= 16)
            {
                uint v1 = unchecked(P32_1 + P32_2);
                uint v2 = P32_2;
                uint v3 = 0;
                uint v4 = unchecked(0 - P32_1);

                int limit = length - 16;
                do
                {
                    v1 = Round32(v1, BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position)));
                    v2 = Round32(v2, BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position + 4)));
                    v3 = Round32(v3, BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position + 8)));
                    v4 = Round32(v4, BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position + 12)));
                    position += 16;
                } while (position <= limit);

                hash = RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
            }
            else
            {
                hash = P32_5;
            }

            hash += (uint)length;

            while (position + 4 <= length)
            {
                hash += BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position)) * P32_3;
                hash = RotateLeft(hash, 17) * P32_4;
                position += 4;
            }

            while (position < length)
            {
                hash += data[position] * P32_5;
                hash = RotateLeft(hash, 11) * P32_1;
                position++;
            }

            hash ^= hash >> 15;
            hash *= P32_2;
            hash ^= hash >> 13;
            hash *= P32_3;
            hash ^= hash >> 16;
            return hash;
        }

        public static ulong Hash64(ReadOnlySpan<byte> data)
        {
            int length = data.Length;
            int position = 0;
            ulong hash;

            if (length >= 32)
            {
                ulong v1 = unchecked(P64_1 + P64_2);
                ulong v2 = P64_2;
                ulong v3 = 0;
                ulong v4 = unchecked(0 - P64_1);

                int limit = length - 32;
                do
                {
                    v1 = Round64(v1, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(position)));
                    v2 = Round64(v2, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(position + 8)));
                    v3 = Round64(v3, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(position + 16)));
                    v4 = Round64(v4, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(position + 24)));
                    position += 32;
                } while (position <= limit);

                hash = RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
                hash = MergeRound64(hash, v1);
                hash = MergeRound64(hash, v2);
                hash = MergeRound64(hash, v3);
                hash = MergeRound64(hash, v4);
            }
            else
            {
                hash = P64_5;
            }

            hash += (ulong)length;

            while (position + 8 <= length)
            {
                hash ^= Round64(0, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(position)));
                hash = RotateLeft(hash, 27) * P64_1 + P64_4;
                position += 8;
            }

            if (position + 4 <= length)
            {
                hash ^= BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position)) * P64_1;
                hash = RotateLeft(hash, 23) * P64_2 + P64_3;
                position += 4;
            }

            while (position < length)
            {
                hash ^= data[position] * P64_5;
                hash = RotateLeft(hash, 11) * P64_1;
                position++;
            }

            hash ^= hash >> 33;
            hash *= P64_2;
            hash ^= hash >> 29;
            hash *= P64_3;
            hash ^= hash >> 32;
            return hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Round32(uint accumulator, uint input)
        {
            accumulator += input * P32_2;
            return RotateLeft(accumulator, 13) * P32_1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Round64(ulong accumulator, ulong input)
        {
            accumulator += input * P64_2;
            return RotateLeft(accumulator, 31) * P64_1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong MergeRound64(ulong accumulator, ulong value)
        {
            accumulator ^= Round64(0, value);
            return accumulator * P64_1 + P64_4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint RotateLeft(uint value, int bits) => (value << bits) | (value >> (32 - bits));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft(ulong value, int bits) => (value << bits) | (value >> (64 - bits));
    }
}
