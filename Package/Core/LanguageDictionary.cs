using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using PicoShot.Localization.Bloc;
using PicoShot.Localization.Hashing;
using UnityEngine;

namespace PicoShot.Localization
{
    /// <summary>
    /// The translations of one language, looked up by key hash.
    /// Values loaded from a BLOC v3 file stay encoded until first read, then are cached.
    /// A value is a string or a List&lt;string&gt; (arrays).
    /// </summary>
    public class LanguageDictionary
    {
        private readonly LanguageKeyIndex index;
        private readonly object[] values;

        // Set only for BLOC v3 files: the encoded data and the cache of decoded strings by id.
        private readonly BlocData3 bloc;
        private readonly string[] strings;

        /// <summary>Key names by slot, in the same order as <see cref="KeyHashes"/>.</summary>
        public ReadOnlySpan<string> Keys => index.Names;

        /// <summary>Key hashes by slot.</summary>
        public ReadOnlySpan<long> KeyHashes => index.Hashes;

        /// <summary>
        /// Values by slot. Decodes every value; untranslated keys of a v3 file are null.
        /// </summary>
        public ReadOnlySpan<object> Values
        {
            get
            {
                if (bloc != null)
                {
                    for (int slot = 0; slot < values.Length; slot++)
                        GetValue(slot);
                }
                return values;
            }
        }

        public int Count => index.Count;

        public LanguageDictionary(Dictionary<string, object> data)
        {
            var keys = new string[data.Count];
            var hashes = new long[data.Count];
            var items = new object[data.Count];

            int i = 0;
            foreach (var entry in data)
            {
                keys[i] = entry.Key;
                hashes[i] = Hash64.CreateIgnoreCase(entry.Key);
                items[i] = entry.Value switch
                {
                    null => null,
                    List<string> list => list,
                    string[] arr => new List<string>(arr),
                    var other => other.ToString()
                };
                i++;
            }

            index = BuildIndex(hashes, keys, ref items, i);
            values = items;
        }

        internal LanguageDictionary(IBlocEntry[] entries)
        {
            var keys = new string[entries.Length];
            var hashes = new long[entries.Length];
            var items = new object[entries.Length];

            int count = 0;
            foreach (var entry in entries)
            {
                switch (entry)
                {
                    case StringEntry stringEntry:
                        items[count] = stringEntry.Value;
                        break;
                    case ArrayEntry arrayEntry:
                        items[count] = new List<string>(arrayEntry.Values);
                        break;
                    case PluralEntry pluralEntry:
                        items[count] = pluralEntry.Value?.ToString() ?? string.Empty;
                        break;
                    default:
                        // MissingEntry: not stored, so lookups fall back to the default language.
                        continue;
                }

                keys[count] = entry.Key;
                hashes[count] = Hash64.CreateIgnoreCase(entry.Key);
                count++;
            }

            index = BuildIndex(hashes, keys, ref items, count);
            values = items;
        }

        private LanguageDictionary(LanguageKeyIndex index, BlocData3 bloc)
        {
            this.index = index;
            this.bloc = bloc;
            values = new object[bloc.EntryCount];
            strings = new string[bloc.StringCount];
        }

        /// <summary>
        /// Loads a locale file of any BLOC version. A v3 file whose key set matches
        /// <paramref name="shareIndexWith"/> (normally the default language) reuses its key index.
        /// </summary>
        internal static LanguageDictionary Load(byte[] file, LanguageDictionary shareIndexWith = null)
        {
            if (IsVersion3(file))
            {
                var data = BlocData3.Parse(file, deep: false);
                var sharedIndex = shareIndexWith?.index;
                var keyIndex = sharedIndex != null && sharedIndex.CanServe(data)
                    ? sharedIndex
                    : LanguageKeyIndex.FromBloc(data);
                return new LanguageDictionary(keyIndex, data);
            }

            using var stream = new MemoryStream(file, false);
            return new LanguageDictionary(BlocFormat.DeserializeEntries(stream, out _));
        }

        private static bool IsVersion3(byte[] file)
        {
            if (file.Length < BlocFormat.MAGIC_AND_VERSION_SIZE)
                return false;

            for (int i = 0; i < BlocFormat.MAGIC_SIZE; i++)
            {
                if (file[i] != BlocFormat.MAGIC[i])
                    return false;
            }

            return (file[4] | (file[5] << 8)) == BlocFormat3.VERSION;
        }

        private static LanguageKeyIndex BuildIndex(long[] hashes, string[] keys, ref object[] items, int count)
        {
            hashes = Trim(hashes, count);
            keys = Trim(keys, count);
            items = Trim(items, count);
            if (LanguageKeyIndex.TryFromNames(hashes, keys, out var index))
                return index;

            // Rare: keys differing only by case. Drop the duplicates, then build again.
            count = RemoveDuplicates(hashes, keys, items, count);
            hashes = Trim(hashes, count);
            keys = Trim(keys, count);
            items = Trim(items, count);
            LanguageKeyIndex.TryFromNames(hashes, keys, out index);
            return index;
        }

        /// <summary>
        /// Keeps the first of several keys with the same hash (keys are case-insensitive) and warns.
        /// </summary>
        private static int RemoveDuplicates(long[] hashes, string[] keys, object[] items, int count)
        {
            var seen = new Dictionary<long, int>(count);
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                if (seen.TryGetValue(hashes[i], out int first))
                {
                    Debug.LogWarning($"[LanguageDictionary] Key '{keys[i]}' conflicts with '{keys[first]}' " +
                                     "(keys are case-insensitive). The duplicate is ignored; rename one of them.");
                    continue;
                }

                seen.Add(hashes[i], written);
                hashes[written] = hashes[i];
                keys[written] = keys[i];
                items[written] = items[i];
                written++;
            }
            return written;
        }

        private static T[] Trim<T>(T[] array, int count)
        {
            if (array.Length != count)
                Array.Resize(ref array, count);
            return array;
        }

        /// <summary>Value of a slot, decoding it on first use. Null when untranslated.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object GetValue(int slot)
        {
            return values[slot] ?? (bloc != null ? Decode(slot) : null);
        }

        private object Decode(int slot)
        {
            object value;
            switch (bloc.Kinds[slot])
            {
                case BlocData3.KIND_STRING:
                    value = GetString(bloc.Refs[slot]);
                    break;
                case BlocData3.KIND_ARRAY:
                    {
                        int start = bloc.Refs[slot];
                        int count = bloc.Items[start];
                        var list = new List<string>(count);
                        for (int i = 0; i < count; i++)
                            list.Add(GetString(bloc.Items[start + 1 + i]));
                        value = list;
                        break;
                    }
                case BlocData3.KIND_PLURAL:
                    {
                        // No plural API yet: use the Other form, which is always present and stored last.
                        int start = bloc.Refs[slot];
                        int formCount = CountBits(bloc.Items[start]);
                        value = GetString(bloc.Items[start + formCount]);
                        break;
                    }
                default:
                    return null;
            }

            values[slot] = value;
            return value;
        }

        private string GetString(int id)
        {
            return strings[id] ??= bloc.GetString(id);
        }

        private static int CountBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetKey(long keyHash, out string key)
        {
            int slot = index.Find(keyHash);
            if (slot < 0)
            {
                key = null;
                return false;
            }

            key = index.Names[slot];
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(long keyHash, out object value)
        {
            int slot = index.Find(keyHash);
            if (slot < 0)
            {
                value = null;
                return false;
            }

            if (bloc == null)
            {
                value = values[slot];
                return true;
            }

            value = GetValue(slot);
            return value != null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(string key, out object value) =>
            TryGetValue(Hash64.CreateIgnoreCase(key), out value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(long keyHash)
        {
            int slot = index.Find(keyHash);
            return slot >= 0 && (bloc == null || bloc.Kinds[slot] != BlocData3.KIND_MISSING);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(string key) =>
            ContainsKey(Hash64.CreateIgnoreCase(key));
    }
}
