using System;
using PicoShot.Localization.Hashing;

namespace PicoShot.Localization
{
    [Serializable]
    public readonly struct Key : IEquatable<Key>, IEquatable<string>
    {
        public readonly string Value;
        public readonly long Hash;

        public bool IsEmpty => Hash == 0;

        private Key(string value)
        {
            Value = value;
            Hash = Hash64.CreateIgnoreCase(value);
        }

        private Key(long hash)
        {
            Value = null;
            Hash = hash;
        }

        public static Key FromKey(string key) => new(key);
        public static Key FromHash(long hash) => new(hash);


        public override int GetHashCode()
        {
            return Hash.GetHashCode();
        }
        public override bool Equals(object obj)
        {
            return obj switch
            {
                Key other => Equals(other),
                string text => Equals(text),
                _ => false
            };
        }
        public bool Equals(Key other)
        {
            return Hash == other.Hash;
        }
        public bool Equals(string other)
        {
            return Hash == Hash64.CreateIgnoreCase(other);
        }

        public override string ToString() => Value ?? Hash.ToString();

        public static bool operator ==(Key left, Key right) => left.Hash == right.Hash;
        public static bool operator !=(Key left, Key right) => left.Hash != right.Hash;

        public static implicit operator string(Key key) => key.Value ?? key.Hash.ToString();

        public static implicit operator Key(string value) => new(value);
        public static implicit operator Key(long value) => new(value);
    }
}
