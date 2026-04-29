using System;
using System.Runtime.CompilerServices;

namespace PicoShot.Localization.Hashing
{
    public unsafe static class Hash64
    {
        private const ulong FNV_PRIME = 1099511628211UL;
        private const ulong FNV_OFFSET_BASIS = 14695981039346656037UL;

        public static long Create(ReadOnlySpan<char> buffer)
        {
            if (buffer.IsEmpty)
                return 0L;

            ulong hash = FNV_OFFSET_BASIS;

            foreach (char c in buffer)
            {
                byte b = (byte)c;

                hash ^= b;
                hash *= FNV_PRIME;
            }
            

            return (long)hash;
        }
        public static long Create(ReadOnlySpan<byte> buffer)
        {
            if (buffer.IsEmpty)
                return 0L;

            ulong hash = FNV_OFFSET_BASIS;

            foreach (byte b in buffer)
            {
                hash ^= b;
                hash *= FNV_PRIME;
            }

            return (long)hash;
        }

        public static long Create(sbyte value)
        {
            ulong hash = FNV_OFFSET_BASIS;

            hash ^= (byte)value;
            hash *= FNV_PRIME;

            return (long)hash;
        }
        public static long Create(byte value)
        {
            ulong hash = FNV_OFFSET_BASIS;

            hash ^= value;
            hash *= FNV_PRIME;

            return (long)hash;
        }

        public static long Create(short value)
        {
            ulong hash = FNV_OFFSET_BASIS;

            for (byte i = 0; i < sizeof(int); i++)
            {
                byte b = (byte)((value >> (i * 8)) & 0xFF);

                hash ^= b;
                hash *= FNV_PRIME;
            }

            return (long)hash;
        }
        public static long Create(ushort value)
        {
            ulong hash = FNV_OFFSET_BASIS;

            for (byte i = 0; i < sizeof(int); i++)
            {
                byte b = (byte)((value >> (i * 8)) & 0xFF);

                hash ^= b;
                hash *= FNV_PRIME;
            }

            return (long)hash;
        }

        public static long Create(int value)
        {
            ulong hash = FNV_OFFSET_BASIS;

            for (byte i = 0; i < sizeof(int); i++)
            {
                byte b = (byte)((value >> (i * 8)) & 0xFF);

                hash ^= b;
                hash *= FNV_PRIME;
            }

            return (long)hash;
        }
        public static long Create(uint value)
        {
            ulong hash = FNV_OFFSET_BASIS;

            for (byte i = 0; i < sizeof(int); i++)
            {
                byte b = (byte)((value >> (i * 8)) & 0xFF);

                hash ^= b;
                hash *= FNV_PRIME;
            }

            return (long)hash;
        }

        public static long CreateFromUnmanaged<T>(T value) where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            ulong hash = FNV_OFFSET_BASIS;

            byte* ptr = (byte*)&value;
            for (int i = 0; i < size; i++)
            {
                hash ^= ptr[i];
                hash *= FNV_PRIME;
            }

            return (long)hash;
        }
        public static long CreateFromUnmanaged<T>(ref T value) where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            ulong hash = FNV_OFFSET_BASIS;

            fixed (T* ptr = &value)
            {
                for (int i = 0; i < size; i++)
                {
                    hash ^= ((byte*)ptr)[i];
                    hash *= FNV_PRIME;
                }
            }

            return (long)hash;
        }
        public static long CreateFromUnmanaged<T>(T* value) where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            ulong hash = FNV_OFFSET_BASIS;

            byte* ptr = (byte*)value;
            for (int i = 0; i < size; i++)
            {
                hash ^= ptr[i];
                hash *= FNV_PRIME;
            }

            return (long)hash;
        }

        public static long Combine(long idA, long idB)
        {
            ulong hash = (ulong)idA;
            ulong val = (ulong)idB;

            for (int i = 0; i < 8; i++)
            {
                byte b = (byte)((val >> (i * 8)) & 0xFF);

                hash ^= b;
                hash *= FNV_PRIME;
            }

            return (long)hash;
        }
    }
}