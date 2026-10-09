using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PicoShot.Localization.Bloc;
using PicoShot.Localization.Hashing;
using UnityEngine;

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
            int count = data.Count;
            keys = new string[count];
            keyHashes = new long[count];
            values = new object[count];

            int i = 0;
            foreach (var entry in data)
            {
                keyHashes[i] = Hash64.CreateIgnoreCase(entry.Key);
                keys[i] = entry.Key;
                values[i] = entry.Value switch
                {
                    null => null,
                    List<string> list => list,
                    string[] arr => new List<string>(arr),
                    var other => other.ToString()
                };
                i++;
            }

            Count = SortAndRemoveDuplicates(keyHashes, keys, values, count);
            if (Count < count)
            {
                Array.Resize(ref keyHashes, Count);
                Array.Resize(ref keys, Count);
                Array.Resize(ref values, Count);
            }
        }

        internal LanguageDictionary(IBlocEntry[] entries)
        {
            int count = 0;
            keys = new string[entries.Length];
            keyHashes = new long[entries.Length];
            values = new object[entries.Length];

            foreach (var entry in entries)
            {
                switch (entry)
                {
                    case StringEntry stringEntry:
                        values[count] = stringEntry.Value;
                        break;
                    case ArrayEntry arrayEntry:
                        values[count] = new List<string>(arrayEntry.Values);
                        break;
                    default:
                        continue;
                }

                keys[count] = entry.Key;
                keyHashes[count] = Hash64.CreateIgnoreCase(entry.Key);
                count++;
            }

            Count = SortAndRemoveDuplicates(keyHashes, keys, values, count);
            if (Count < entries.Length)
            {
                Array.Resize(ref keyHashes, Count);
                Array.Resize(ref keys, Count);
                Array.Resize(ref values, Count);
            }
        }

        private static int SortAndRemoveDuplicates(long[] hashes, string[] keys, object[] values, int count)
        {
            if (count <= 1)
                return count;

            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            var sortKeys = new long[count];
            Array.Copy(hashes, sortKeys, count);
            Array.Sort(sortKeys, order);
            for (int start = 0; start < count;)
            {
                int end = start + 1;
                while (end < count && sortKeys[end] == sortKeys[start]) end++;
                if (end - start > 1) Array.Sort(order, start, end - start);
                start = end;
            }

            var sortedKeys = new string[count];
            var sortedValues = new object[count];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                int source = order[i];
                if (written > 0 && sortKeys[i] == hashes[written - 1])
                {
                    Debug.LogWarning($"[LanguageDictionary] Key '{keys[source]}' conflicts with '{sortedKeys[written - 1]}' " +
                                     "(keys are case-insensitive). The duplicate is ignored; rename one of them.");
                    continue;
                }

                hashes[written] = sortKeys[i];
                sortedKeys[written] = keys[source];
                sortedValues[written] = values[source];
                written++;
            }

            Array.Copy(sortedKeys, keys, written);
            Array.Copy(sortedValues, values, written);
            return written;
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
            TryGetValue(Hash64.CreateIgnoreCase(key), out value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(long keyHash)
        {
            return FindIndexFromHash(keyHash) >= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(string key) =>
            ContainsKey(Hash64.CreateIgnoreCase(key));
    }
}
