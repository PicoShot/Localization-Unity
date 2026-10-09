using System;

namespace PicoShot.Localization.Data
{
    /// <summary>
    /// CLDR plural categories, in the order BLOC v3 stores them.
    /// </summary>
    public enum PluralCategory : byte
    {
        Zero = 0,
        One = 1,
        Two = 2,
        Few = 3,
        Many = 4,
        Other = 5
    }

    /// <summary>
    /// A translation with one text per CLDR plural category (BLOC v3 and later).
    /// Every language uses <see cref="PluralCategory.Other"/>, so it is required; the rest are optional.
    /// </summary>
    [Serializable]
    public sealed class PluralValue
    {
        public const int CategoryCount = 6;

        private readonly string[] _forms = new string[CategoryCount];

        public string this[PluralCategory category]
        {
            get => _forms[(int)category];
            set => _forms[(int)category] = value;
        }

        public string Zero { get => _forms[0]; set => _forms[0] = value; }
        public string One { get => _forms[1]; set => _forms[1] = value; }
        public string Two { get => _forms[2]; set => _forms[2] = value; }
        public string Few { get => _forms[3]; set => _forms[3] = value; }
        public string Many { get => _forms[4]; set => _forms[4] = value; }
        public string Other { get => _forms[5]; set => _forms[5] = value; }

        /// <summary>Bit i is set when category i has a text.</summary>
        public byte Mask
        {
            get
            {
                int mask = 0;
                for (int i = 0; i < CategoryCount; i++)
                    if (_forms[i] != null) mask |= 1 << i;
                return (byte)mask;
            }
        }

        public bool Has(PluralCategory category) => _forms[(int)category] != null;

        public override string ToString() => Other ?? string.Empty;
    }
}
