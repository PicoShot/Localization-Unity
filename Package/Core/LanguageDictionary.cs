using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PicoShot.Localization.Hashing;

namespace PicoShot.Localization
{
    public class LanguageDictionary
    {
        private string[] keys;
        private long[] keyHashes;
        private object[] values;

        public ReadOnlySpan<string> Keys => keys;
        public ReadOnlySpan<long> KeyHashes => keyHashes;
        public ReadOnlySpan<object> Values => values;
        public int Count { get; private set; }

        public LanguageDictionary(Dictionary<string, object> data)
        {
            Count = data.Count;
            keys = new string[Count];
            keyHashes = new long[Count];
            values = new object[Count];

            var temp = new (long hash, string key, object value)[Count];

            int i = 0;
            foreach (var entry in data)
            {
                string key = entry.Key;
                temp[i++] = (Hash64.Create(key?.ToLowerInvariant()), key, entry.Value);
            }

            Array.Sort(temp, (a, b) => a.hash.CompareTo(b.hash));

            for (int j = 0; j < Count; j++)
            {
                keyHashes[j] = temp[j].hash;
                keys[j] = temp[j].key;
                values[j] = temp[j].value switch
                {
                    null => null,
                    List<string> list => list,
                    string[] arr => new List<string>(arr),
                    _ => temp[j].value.ToString()
                };
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindIndexFromHash(long hash)
        {
            ReadOnlySpan<long> span = keyHashes.AsSpan();

            int low = 0;
            int high = span.Length - 1;

            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                long midValue = span[mid];

                if (midValue < hash)
                    low = mid + 1;
                else if (midValue > hash)
                    high = mid - 1;
                else
                    return mid;
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetKey(long keyHash, out string key)
        {
            int index = FindIndexFromHash(keyHash);

            if (index == -1)
            {
                key = null;
                return false;
            }

            key = keys[index];
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(long keyHash, out object value)
        {
            int index = FindIndexFromHash(keyHash);

            if (index == -1)
            {
                value = null;
                return false;
            }

            value = values[index];
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(string key, out object value) =>
            TryGetValue(Hash64.Create(key), out value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(long keyHash)
        {
            for (int i = 0; i < keyHashes.Length; i++)
            {
                if (keyHash != keyHashes[i])
                    continue;

                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(string key) =>
            ContainsKey(Hash64.Create(key));
    }
}