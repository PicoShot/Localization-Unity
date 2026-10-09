using System;
using System.IO;
using System.Runtime.CompilerServices;
using PicoShot.Localization.Bloc;

namespace PicoShot.Localization
{
    /// <summary>
    /// Maps key hashes to entry slots with an open-addressing table: O(1) lookups, built in O(n) without sorting.
    /// An index built from a BLOC v3 file can be shared by every language with the same key set id,
    /// so switching language does not rebuild it.
    /// </summary>
    internal sealed class LanguageKeyIndex
    {
        private const ulong FibonacciMultiplier = 0x9E3779B97F4A7C15UL;

        /// <summary>Key hash per slot.</summary>
        public readonly long[] Hashes;

        /// <summary>Key set id of the v3 file the index was built from; only valid when <see cref="IsShareable"/>.</summary>
        public readonly ulong KeySetId;
        public readonly bool IsShareable;

        private readonly int[] _table; // slot + 1, 0 = empty
        private readonly int _shift;
        private readonly BlocData3 _nameSource;
        private string[] _names;

        private LanguageKeyIndex(long[] hashes, int[] table, string[] names, BlocData3 nameSource, ulong keySetId, bool isShareable)
        {
            Hashes = hashes;
            _table = table;
            _shift = ShiftFor(table.Length);
            _names = names;
            _nameSource = nameSource;
            KeySetId = keySetId;
            IsShareable = isShareable;
        }

        /// <summary>Builds the lookup table, or returns null when two keys have the same hash.</summary>
        private static int[] BuildTable(long[] hashes)
        {
            int bits = 2;
            while ((1 << bits) < hashes.Length * 2L)
                bits++;

            var table = new int[1 << bits];
            int shift = ShiftFor(table.Length);
            int mask = table.Length - 1;

            for (int slot = 0; slot < hashes.Length; slot++)
            {
                int i = Bucket(hashes[slot], shift);
                while (table[i] != 0)
                {
                    if (hashes[table[i] - 1] == hashes[slot])
                        return null;
                    i = (i + 1) & mask;
                }
                table[i] = slot + 1;
            }

            return table;
        }

        private static int ShiftFor(int tableLength)
        {
            int bits = 0;
            while ((1 << bits) < tableLength)
                bits++;
            return 64 - bits;
        }

        /// <summary>Index over named keys; false when two keys have the same hash.</summary>
        public static bool TryFromNames(long[] hashes, string[] names, out LanguageKeyIndex index)
        {
            var table = BuildTable(hashes);
            index = table != null ? new LanguageKeyIndex(hashes, table, names, null, 0, false) : null;
            return index != null;
        }

        /// <summary>Index over the keys of a v3 file; names are decoded only if asked for.</summary>
        public static LanguageKeyIndex FromBloc(BlocData3 data)
        {
            var hashes = new long[data.EntryCount];
            data.ReadKeys(hashes, null, strict: false);
            var table = BuildTable(hashes) ?? throw new InvalidDataException("File damaged (duplicate key hashes)");
            return new LanguageKeyIndex(hashes, table, null, data, data.KeySetId, true);
        }

        public int Count => Hashes.Length;

        public string[] Names => _names ??= _nameSource?.ReadKeyNames() ?? new string[Hashes.Length];

        public bool CanServe(BlocData3 data)
        {
            return IsShareable && KeySetId == data.KeySetId && Hashes.Length == data.EntryCount;
        }

        /// <summary>Slot of <paramref name="hash"/>, or -1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Find(long hash)
        {
            int[] table = _table;
            int mask = table.Length - 1;
            int i = Bucket(hash, _shift);
            while (true)
            {
                int entry = table[i];
                if (entry == 0)
                    return -1;
                if (Hashes[entry - 1] == hash)
                    return entry - 1;
                i = (i + 1) & mask;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Bucket(long hash, int shift) => (int)(((ulong)hash * FibonacciMultiplier) >> shift);
    }
}
