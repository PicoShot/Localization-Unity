using System;
using PicoShot.Localization.Hashing;

namespace PicoShot.Localization
{
    [Serializable]
    public readonly struct Key : IEquatable<string>
    {
        public readonly string Value;
        public readonly long Hash;

        private Key(string value)
        {
            Value = value;
            Hash = Hash64.Create(value);
        }

        public static Key From(string key) => new(key);

        public override int GetHashCode()
        {
            return Hash.GetHashCode();
        }
        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            return obj.GetHashCode() == GetHashCode();
        }
        public bool Equals(string other)
        {
            return Value == other;
        }

        public static implicit operator string(Key key) => key.Value;
        public static implicit operator Key(string value) => new(value);
    }
}