using System;
using System.Collections.Generic;
using System.Text;

namespace PicoShot.Localization.Rtl
{
    public static class RtlTextHandler
    {
        private static readonly StringBuilder StringBuilder = new(1024);

        public static string Fix(string str)
        {
            return FixInternal(str, false, true, false);
        }

        internal static string Fix(string str, bool preserveOrder)
        {
            return FixInternal(str, false, true, preserveOrder);
        }

        /// <summary>
        /// Shapes RTL characters without changing their logical order. The resulting
        /// string can be measured by TextMeshPro and later passed to Reverse or
        /// ReverseMixed without reshaping it a second time.
        /// </summary>
        internal static string Shape(string str, bool supportMixedText, bool isMainRtl)
        {
            return supportMixedText
                ? FixMixed(str, isMainRtl, true)
                : Fix(str, true);
        }

        internal static string Reverse(string str)
        {
            return ProcessLines(str, FixerTool.ReverseLine);
        }

        internal static string ReverseMixed(string str, bool isMainRtl)
        {
            return FixMixedInternal(str, isMainRtl, false, true);
        }

        private static string FixInternal(string str, bool showTashkeel, bool useHinduNumbers, bool preserveOrder)
        {
            FixerTool.ShowTashkeel = showTashkeel;
            FixerTool.UseHinduNumbers = useHinduNumbers;

            if (str.Contains("\n") && !str.Contains(Environment.NewLine))
            {
                str = str.Replace("\n", Environment.NewLine);
            }

            if (!str.Contains(Environment.NewLine))
            {
                return FixerTool.FixLine(str, preserveOrder);
            }

            var stringSeparators = new[] { Environment.NewLine };
            var strSplit = str.Split(stringSeparators, StringSplitOptions.None);

            if (strSplit.Length <= 1)
            {
                return FixerTool.FixLine(str, preserveOrder);
            }

            StringBuilder.Clear();
            StringBuilder.EnsureCapacity(str.Length);

            StringBuilder.Append(FixerTool.FixLine(strSplit[0], preserveOrder));

            for (int i = 1; i < strSplit.Length; i++)
            {
                StringBuilder.Append(Environment.NewLine);
                StringBuilder.Append(FixerTool.FixLine(strSplit[i], preserveOrder));
            }

            return StringBuilder.ToString();
        }

        public static string Fix(string str, bool showTashkeel, bool combineTashkeel, bool useHinduNumbers)
        {
            FixerTool.CombineTashkeel = combineTashkeel;
            return FixInternal(str, showTashkeel, useHinduNumbers, false);
        }

        public static string FixMixed(string str, bool isMainRtl)
        {
            return FixMixed(str, isMainRtl, false);
        }

        internal static string FixMixed(string str, bool isMainRtl, bool preserveOrder)
        {
            return FixMixedInternal(str, isMainRtl, preserveOrder, false);
        }

        private static string FixMixedInternal(string str, bool isMainRtl, bool preserveOrder, bool alreadyShaped)
        {
            if (string.IsNullOrEmpty(str)) return str;

            var rawTokens = new List<TextToken>();
            CharDirection currentDir = CharDirection.Neutral;
            TextToken currentToken = null;

            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                CharDirection dir = GetCharDirection(c);

                if (currentToken == null)
                {
                    currentDir = dir;
                    currentToken = new TextToken { Direction = dir };
                    currentToken.Text.Append(c);
                    rawTokens.Add(currentToken);
                }
                else if (dir == currentDir || (dir == CharDirection.Neutral && currentDir == CharDirection.Neutral))
                {
                    currentToken.Text.Append(c);
                }
                else if (dir == CharDirection.Neutral)
                {
                    currentDir = dir;
                    currentToken = new TextToken { Direction = dir };
                    currentToken.Text.Append(c);
                    rawTokens.Add(currentToken);
                }
                else
                {
                    currentDir = dir;
                    currentToken = new TextToken { Direction = dir };
                    currentToken.Text.Append(c);
                    rawTokens.Add(currentToken);
                }
            }

            CharDirection mainDir = isMainRtl ? CharDirection.RTL : CharDirection.LTR;
            for (int i = 0; i < rawTokens.Count; i++)
            {
                if (rawTokens[i].Direction == CharDirection.Neutral)
                {
                    CharDirection prevDir = (i > 0) ? rawTokens[i - 1].Direction : CharDirection.Neutral;
                    CharDirection nextDir = (i < rawTokens.Count - 1) ? rawTokens[i + 1].Direction : CharDirection.Neutral;

                    if (prevDir == nextDir && prevDir != CharDirection.Neutral)
                        rawTokens[i].Direction = prevDir;
                    else
                        rawTokens[i].Direction = mainDir;
                }
            }

            var mergedTokens = new List<TextToken>();
            if (rawTokens.Count > 0)
            {
                var currentMerged = new TextToken { Direction = rawTokens[0].Direction };
                currentMerged.Text.Append(rawTokens[0].Text);
                mergedTokens.Add(currentMerged);

                for (int i = 1; i < rawTokens.Count; i++)
                {
                    if (rawTokens[i].Direction == currentMerged.Direction)
                    {
                        currentMerged.Text.Append(rawTokens[i].Text);
                    }
                    else
                    {
                        currentMerged = new TextToken { Direction = rawTokens[i].Direction };
                        currentMerged.Text.Append(rawTokens[i].Text);
                        mergedTokens.Add(currentMerged);
                    }
                }
            }

            var sb = new StringBuilder(str.Length);
            if (isMainRtl && !preserveOrder)
            {
                for (int i = mergedTokens.Count - 1; i >= 0; i--)
                {
                    sb.Append(ProcessToken(mergedTokens[i], preserveOrder, alreadyShaped));
                }
            }
            else
            {
                for (int i = 0; i < mergedTokens.Count; i++)
                {
                    sb.Append(ProcessToken(mergedTokens[i], preserveOrder, alreadyShaped));
                }
            }

            return sb.ToString();
        }

        private static string ProcessToken(TextToken token, bool preserveOrder, bool alreadyShaped)
        {
            if (token.Direction == CharDirection.RTL)
                return alreadyShaped
                    ? Reverse(token.Text.ToString())
                    : Fix(token.Text.ToString(), preserveOrder);

            // Convert numbers in LTR tokens if needed, without reversing
            var text = token.Text.ToString();
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsDigit(c) && c >= '0' && c <= '9')
                    sb.Append((char)FixerTool.HandleInduNumber(c, c));
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static string ProcessLines(string str, Func<string, string> processor)
        {
            if (string.IsNullOrEmpty(str)) return str;

            string normalized = str.Replace("\r\n", "\n").Replace('\r', '\n');
            if (normalized.IndexOf('\n') < 0)
                return processor(normalized);

            string[] lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
            var sb = new StringBuilder(normalized.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(processor(lines[i]));
            }
            return sb.ToString();
        }

        private enum CharDirection
        {
            LTR,
            RTL,
            Neutral
        }

        private class TextToken
        {
            public CharDirection Direction;
            public StringBuilder Text = new StringBuilder();
        }

        private static CharDirection GetCharDirection(char c)
        {
            if (c >= 0x0590 && c <= 0x08FF || 
                c >= 0xFB1D && c <= 0xFDFF || 
                c >= 0xFE70 && c <= 0xFEFF)
                return CharDirection.RTL;

            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || 
                (c >= 0x00C0 && c <= 0x00FF) || (c >= 0x0400 && c <= 0x04FF))
                return CharDirection.LTR;

            if (char.IsNumber(c))
                return CharDirection.LTR;

            return CharDirection.Neutral;
        }
    }


    internal enum IsolatedArabicLetters
    {
        Hamza = 0xFE80,
        Alef = 0xFE8D,
        AlefHamza = 0xFE83,
        WawHamza = 0xFE85,
        AlefMaksoor = 0xFE87,
        AlefMaksora = 0xFBFC,
        HamzaNabera = 0xFE89,
        Ba = 0xFE8F,
        Ta = 0xFE95,
        Tha2 = 0xFE99,
        Jeem = 0xFE9D,
        H7AA = 0xFEA1,
        Khaa2 = 0xFEA5,
        Dal = 0xFEA9,
        Thal = 0xFEAB,
        Ra2 = 0xFEAD,
        Zeen = 0xFEAF,
        Seen = 0xFEB1,
        Sheen = 0xFEB5,
        S9A = 0xFEB9,
        Dha = 0xFEBD,
        T6A = 0xFEC1,
        T6Ha = 0xFEC5,
        Ain = 0xFEC9,
        Gain = 0xFECD,
        Fa = 0xFED1,
        Gaf = 0xFED5,
        Kaf = 0xFED9,
        Lam = 0xFEDD,
        Meem = 0xFEE1,
        Noon = 0xFEE5,
        Ha = 0xFEE9,
        Waw = 0xFEED,
        Ya = 0xFEF1,
        AlefMad = 0xFE81,
        TaMarboota = 0xFE93,
        PersianPe = 0xFB56, // Persian (iranian) Letters;
        PersianChe = 0xFB7A,
        PersianZe = 0xFB8A,
        PersianGaf = 0xFB92,
        PersianGaf2 = 0xFB8E,
        PersianYeh = 0xFBFC,
    }

    internal enum GeneralArabicLetters
    {
        Hamza = 0x0621,
        Alef = 0x0627,
        AlefHamza = 0x0623,
        WawHamza = 0x0624,
        AlefMaksoor = 0x0625,
        AlefMagsora = 0x0649,
        HamzaNabera = 0x0626,
        Ba = 0x0628,
        Ta = 0x062A,
        Tha2 = 0x062B,
        Jeem = 0x062C,
        H7AA = 0x062D,
        Khaa2 = 0x062E,
        Dal = 0x062F,
        Thal = 0x0630,
        Ra2 = 0x0631,
        Zeen = 0x0632,
        Seen = 0x0633,
        Sheen = 0x0634,
        S9A = 0x0635,
        Dha = 0x0636,
        T6A = 0x0637,
        T6Ha = 0x0638,
        Ain = 0x0639,
        Gain = 0x063A,
        Fa = 0x0641,
        Gaf = 0x0642,
        Kaf = 0x0643,
        Lam = 0x0644,
        Meem = 0x0645,
        Noon = 0x0646,
        Ha = 0x0647,
        Waw = 0x0648,
        Ya = 0x064A,
        AlefMad = 0x0622,
        TaMarboota = 0x0629,
        PersianPe = 0x067E, // Persian (iranian) Letters;
        PersianChe = 0x0686,
        PersianZe = 0x0698,
        PersianGaf = 0x06AF,
        PersianGaf2 = 0x06A9,
        PersianYeh = 0x06CC,
    }

    internal struct ArabicMapping
    {
        public readonly int From;
        public readonly int To;

        public ArabicMapping(int from, int to)
        {
            From = from;
            To = to;
        }
    }

    internal class ArabicTable
    {
        private static readonly Dictionary<int, int> MappingDictionary = new();
        public static ArabicTable ArabicMapper { get; }

        private ArabicTable()
        {
            var mapList = new[]
            {
                new ArabicMapping((int)GeneralArabicLetters.Hamza, (int)IsolatedArabicLetters.Hamza),
                new ArabicMapping((int)GeneralArabicLetters.Alef, (int)IsolatedArabicLetters.Alef),
                new ArabicMapping((int)GeneralArabicLetters.AlefHamza, (int)IsolatedArabicLetters.AlefHamza),
                new ArabicMapping((int)GeneralArabicLetters.WawHamza, (int)IsolatedArabicLetters.WawHamza),
                new ArabicMapping((int)GeneralArabicLetters.AlefMaksoor, (int)IsolatedArabicLetters.AlefMaksoor),
                new ArabicMapping((int)GeneralArabicLetters.AlefMagsora, (int)IsolatedArabicLetters.AlefMaksora),
                new ArabicMapping((int)GeneralArabicLetters.HamzaNabera, (int)IsolatedArabicLetters.HamzaNabera),
                new ArabicMapping((int)GeneralArabicLetters.Ba, (int)IsolatedArabicLetters.Ba),
                new ArabicMapping((int)GeneralArabicLetters.Ta, (int)IsolatedArabicLetters.Ta),
                new ArabicMapping((int)GeneralArabicLetters.Tha2, (int)IsolatedArabicLetters.Tha2),
                new ArabicMapping((int)GeneralArabicLetters.Jeem, (int)IsolatedArabicLetters.Jeem),
                new ArabicMapping((int)GeneralArabicLetters.H7AA, (int)IsolatedArabicLetters.H7AA),
                new ArabicMapping((int)GeneralArabicLetters.Khaa2, (int)IsolatedArabicLetters.Khaa2),
                new ArabicMapping((int)GeneralArabicLetters.Dal, (int)IsolatedArabicLetters.Dal),
                new ArabicMapping((int)GeneralArabicLetters.Thal, (int)IsolatedArabicLetters.Thal),
                new ArabicMapping((int)GeneralArabicLetters.Ra2, (int)IsolatedArabicLetters.Ra2),
                new ArabicMapping((int)GeneralArabicLetters.Zeen, (int)IsolatedArabicLetters.Zeen),
                new ArabicMapping((int)GeneralArabicLetters.Seen, (int)IsolatedArabicLetters.Seen),
                new ArabicMapping((int)GeneralArabicLetters.Sheen, (int)IsolatedArabicLetters.Sheen),
                new ArabicMapping((int)GeneralArabicLetters.S9A, (int)IsolatedArabicLetters.S9A),
                new ArabicMapping((int)GeneralArabicLetters.Dha, (int)IsolatedArabicLetters.Dha),
                new ArabicMapping((int)GeneralArabicLetters.T6A, (int)IsolatedArabicLetters.T6A),
                new ArabicMapping((int)GeneralArabicLetters.T6Ha, (int)IsolatedArabicLetters.T6Ha),
                new ArabicMapping((int)GeneralArabicLetters.Ain, (int)IsolatedArabicLetters.Ain),
                new ArabicMapping((int)GeneralArabicLetters.Gain, (int)IsolatedArabicLetters.Gain),
                new ArabicMapping((int)GeneralArabicLetters.Fa, (int)IsolatedArabicLetters.Fa),
                new ArabicMapping((int)GeneralArabicLetters.Gaf, (int)IsolatedArabicLetters.Gaf),
                new ArabicMapping((int)GeneralArabicLetters.Kaf, (int)IsolatedArabicLetters.Kaf),
                new ArabicMapping((int)GeneralArabicLetters.Lam, (int)IsolatedArabicLetters.Lam),
                new ArabicMapping((int)GeneralArabicLetters.Meem, (int)IsolatedArabicLetters.Meem),
                new ArabicMapping((int)GeneralArabicLetters.Noon, (int)IsolatedArabicLetters.Noon),
                new ArabicMapping((int)GeneralArabicLetters.Ha, (int)IsolatedArabicLetters.Ha),
                new ArabicMapping((int)GeneralArabicLetters.Waw, (int)IsolatedArabicLetters.Waw),
                new ArabicMapping((int)GeneralArabicLetters.Ya, (int)IsolatedArabicLetters.Ya),
                new ArabicMapping((int)GeneralArabicLetters.AlefMad, (int)IsolatedArabicLetters.AlefMad),
                new ArabicMapping((int)GeneralArabicLetters.TaMarboota, (int)IsolatedArabicLetters.TaMarboota),
                new ArabicMapping((int)GeneralArabicLetters.PersianPe, (int)IsolatedArabicLetters.PersianPe),
                new ArabicMapping((int)GeneralArabicLetters.PersianChe, (int)IsolatedArabicLetters.PersianChe),
                new ArabicMapping((int)GeneralArabicLetters.PersianZe, (int)IsolatedArabicLetters.PersianZe),
                new ArabicMapping((int)GeneralArabicLetters.PersianGaf, (int)IsolatedArabicLetters.PersianGaf),
                new ArabicMapping((int)GeneralArabicLetters.PersianGaf2, (int)IsolatedArabicLetters.PersianGaf2),
                new ArabicMapping((int)GeneralArabicLetters.PersianYeh, (int)IsolatedArabicLetters.PersianYeh)
            };

            foreach (var mapping in mapList)
            {
                MappingDictionary[mapping.From] = mapping.To;
            }
        }

        static ArabicTable()
        {
            ArabicMapper = new ArabicTable();
        }

        internal static int Convert(int toBeConverted)
        {
            return MappingDictionary.GetValueOrDefault(toBeConverted, toBeConverted);
        }
    }


    internal class TashkeelLocation
    {
        public char Tashkeel;
        public readonly int Position;

        public TashkeelLocation(char tashkeel, int position)
        {
            Tashkeel = tashkeel;
            Position = position;
        }
    }

    internal static class FixerTool
    {
        internal static bool ShowTashkeel = true;
        internal static bool CombineTashkeel = true;
        internal static bool UseHinduNumbers;
        private static StringBuilder _internalStringBuilder;

        private static StringBuilder InternalStringBuilder => _internalStringBuilder ??= new StringBuilder(1024);

        private static List<TashkeelLocation> _tashkeelLocations;

        private static List<TashkeelLocation> TashkeelLocations =>
            _tashkeelLocations ??= new List<TashkeelLocation>(64);


        private static void RemoveTashkeel(ref string str, out List<TashkeelLocation> tashkeelLocation)
        {
            var tashkeelLocations = TashkeelLocations;
            tashkeelLocations.Clear();
            tashkeelLocation = tashkeelLocations;

            var lastSplitIndex = 0;
            InternalStringBuilder.Clear();
            InternalStringBuilder.EnsureCapacity(str.Length);

            var index = 0;

            for (var i = 0; i < str.Length; i++)
            {
                var currentChar = str[i];
                var shouldRemove = false;

                switch (currentChar)
                {
                    case (char)0x064B:
                        tashkeelLocations.Add(new TashkeelLocation((char)0x064B, i));
                        shouldRemove = true;
                        break;

                    // Tanween Damma
                    case (char)0x064C:
                        tashkeelLocations.Add(new TashkeelLocation((char)0x064C, i));
                        shouldRemove = true;
                        break;

                    // Tanween Kasra
                    case (char)0x064D:
                        tashkeelLocations.Add(new TashkeelLocation((char)0x064D, i));
                        shouldRemove = true;
                        break;

                    // Fatha
                    case (char)0x064E:
                        if (index > 0 && CombineTashkeel && tashkeelLocations[index - 1].Tashkeel == (char)0x0651)
                        {
                            tashkeelLocations[index - 1].Tashkeel = (char)0xFC60;
                            shouldRemove = true;
                            break;
                        }

                        tashkeelLocations.Add(new TashkeelLocation((char)0x064E, i));
                        shouldRemove = true;
                        break;

                    // Damma
                    case (char)0x064F:
                        if (index > 0 && CombineTashkeel && tashkeelLocations[index - 1].Tashkeel == (char)0x0651)
                        {
                            tashkeelLocations[index - 1].Tashkeel = (char)0xFC61;
                            shouldRemove = true;
                            break;
                        }

                        tashkeelLocations.Add(new TashkeelLocation((char)0x064F, i));
                        shouldRemove = true;
                        break;

                    // Kasra
                    case (char)0x0650:
                        if (index > 0 && CombineTashkeel && tashkeelLocations[index - 1].Tashkeel == (char)0x0651)
                        {
                            tashkeelLocations[index - 1].Tashkeel = (char)0xFC62;
                            shouldRemove = true;
                            break;
                        }

                        tashkeelLocations.Add(new TashkeelLocation((char)0x0650, i));
                        shouldRemove = true;
                        break;

                    // Shadda
                    case (char)0x0651:
                        if (index > 0 && CombineTashkeel)
                        {
                            if (tashkeelLocations[index - 1].Tashkeel == (char)0x064E)
                            {
                                tashkeelLocations[index - 1].Tashkeel = (char)0xFC60;
                                shouldRemove = true;
                                break;
                            }

                            if (tashkeelLocations[index - 1].Tashkeel == (char)0x064F)
                            {
                                tashkeelLocations[index - 1].Tashkeel = (char)0xFC61;
                                shouldRemove = true;
                                break;
                            }

                            if (tashkeelLocations[index - 1].Tashkeel == (char)0x0650)
                            {
                                tashkeelLocations[index - 1].Tashkeel = (char)0xFC62;
                                shouldRemove = true;
                                break;
                            }
                        }

                        tashkeelLocations.Add(new TashkeelLocation((char)0x0651, i));
                        shouldRemove = true;
                        break;

                    // Sukun
                    case (char)0x0652:
                        tashkeelLocations.Add(new TashkeelLocation((char)0x0652, i));
                        shouldRemove = true;
                        break;

                    // Maddah
                    case (char)0x0653:
                        tashkeelLocations.Add(new TashkeelLocation((char)0x0653, i));
                        shouldRemove = true;
                        break;

                    case (char)0xFC60:
                    case (char)0xFC61:
                    case (char)0xFC62:
                        shouldRemove = true;
                        break;
                }

                if (shouldRemove)
                {
                    if (i - lastSplitIndex > 0)
                    {
                        InternalStringBuilder.Append(str, lastSplitIndex, i - lastSplitIndex);
                    }

                    lastSplitIndex = i + 1;
                    if (currentChar != (char)0xFC60 && currentChar != (char)0xFC61 && currentChar != (char)0xFC62)
                    {
                        index++;
                    }
                }
            }

            if (lastSplitIndex < str.Length)
            {
                InternalStringBuilder.Append(str, lastSplitIndex, str.Length - lastSplitIndex);
            }

            if (lastSplitIndex > 0)
            {
                str = InternalStringBuilder.ToString();
            }
        }

        private static void ReturnTashkeel(ref char[] letters, List<TashkeelLocation> tashkeelLocation)
        {
            Array.Resize(ref letters, letters.Length + tashkeelLocation.Count);

            foreach (var tl in tashkeelLocation)
            {
                for (var j = letters.Length - 1; j > tl.Position; j--)
                {
                    letters[j] = letters[j - 1];
                }

                letters[tl.Position] = tl.Tashkeel;
            }
        }

        internal static string FixLine(string str, bool preserveOrder = false)
        {
            if (string.IsNullOrEmpty(str))
            {
                return str;
            }

            RemoveTashkeel(ref str, out var tashkeelLocation);

            var lettersOrigin = new char[str.Length];
            var lettersFinal = str.ToCharArray();

            for (var i = 0; i < str.Length; i++)
            {
                lettersOrigin[i] = (char)ArabicTable.Convert(str[i]);
            }

            for (var i = 0; i < lettersOrigin.Length; i++)
            {
                var skip = false;

                if (lettersOrigin[i] == (char)IsolatedArabicLetters.Lam && i < lettersOrigin.Length - 1)
                {
                    switch (lettersOrigin[i + 1])
                    {
                        case (char)IsolatedArabicLetters.AlefMaksoor:
                            lettersOrigin[i] = (char)0xFEF7;
                            lettersFinal[i + 1] = (char)0xFFFF;
                            skip = true;
                            break;
                        case (char)IsolatedArabicLetters.Alef:
                            lettersOrigin[i] = (char)0xFEF9;
                            lettersFinal[i + 1] = (char)0xFFFF;
                            skip = true;
                            break;
                        case (char)IsolatedArabicLetters.AlefHamza:
                            lettersOrigin[i] = (char)0xFEF5;
                            lettersFinal[i + 1] = (char)0xFFFF;
                            skip = true;
                            break;
                        case (char)IsolatedArabicLetters.AlefMad:
                            lettersOrigin[i] = (char)0xFEF3;
                            lettersFinal[i + 1] = (char)0xFFFF;
                            skip = true;
                            break;
                    }
                }

                if (!IsIgnoredCharacter(lettersOrigin[i]))
                {
                    if (IsMiddleLetter(lettersOrigin, i))
                        lettersFinal[i] = (char)(lettersOrigin[i] + 3);
                    else if (IsFinishingLetter(lettersOrigin, i))
                        lettersFinal[i] = (char)(lettersOrigin[i] + 1);
                    else if (IsLeadingLetter(lettersOrigin, i))
                        lettersFinal[i] = (char)(lettersOrigin[i] + 2);
                    else
                        lettersFinal[i] = lettersOrigin[i]; // Isolated form
                }

                if (skip)
                    i++;

                if (UseHinduNumbers)
                {
                    lettersFinal[i] = (char)HandleInduNumber(lettersOrigin[i], lettersFinal[i]);
                }
            }

            if (ShowTashkeel && tashkeelLocation.Count > 0)
                ReturnTashkeel(ref lettersFinal, tashkeelLocation);

            if (preserveOrder)
            {
                InternalStringBuilder.Clear();
                InternalStringBuilder.EnsureCapacity(lettersFinal.Length);
                for (var i = 0; i < lettersFinal.Length; i++)
                {
                    if (lettersFinal[i] != 0xFFFF)
                        InternalStringBuilder.Append(lettersFinal[i]);
                }
                return InternalStringBuilder.ToString();
            }

            return ReverseLetters(lettersFinal);
        }

        internal static string ReverseLine(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return ReverseLetters(str.ToCharArray());
        }

        private static string ReverseLetters(char[] lettersFinal)
        {
            InternalStringBuilder.Clear();
            InternalStringBuilder.EnsureCapacity(lettersFinal.Length);

            var numberList = new List<char>(16);

            for (var i = lettersFinal.Length - 1; i >= 0; i--)
            {
                if (char.IsPunctuation(lettersFinal[i]) && i > 0 && i < lettersFinal.Length - 1 &&
                    (char.IsPunctuation(lettersFinal[i - 1]) || char.IsPunctuation(lettersFinal[i + 1])))
                {
                    switch (lettersFinal[i])
                    {
                        case '(': InternalStringBuilder.Append(')'); break;
                        case ')': InternalStringBuilder.Append('('); break;
                        case '<': InternalStringBuilder.Append('>'); break;
                        case '>': InternalStringBuilder.Append('<'); break;
                        case '[': InternalStringBuilder.Append(']'); break;
                        case ']': InternalStringBuilder.Append('['); break;
                        default:
                            if (lettersFinal[i] != 0xFFFF)
                                InternalStringBuilder.Append(lettersFinal[i]);
                            break;
                    }
                }
                else if (lettersFinal[i] == ' ' && i > 0 && i < lettersFinal.Length - 1 &&
                         (IsLatinChar(lettersFinal[i - 1])) && (IsLatinChar(lettersFinal[i + 1])))
                {
                    AppendNumber(lettersFinal[i]);
                }
                else if (IsLatinChar(lettersFinal[i]) || char.IsSymbol(lettersFinal[i]) ||
                         char.IsPunctuation(lettersFinal[i]))
                {
                    switch (lettersFinal[i])
                    {
                        case '(': AppendNumber(')'); break;
                        case ')': AppendNumber('('); break;
                        case '<': AppendNumber('>'); break;
                        case '>': AppendNumber('<'); break;
                        case '[': InternalStringBuilder.Append(']'); break;
                        case ']': InternalStringBuilder.Append('['); break;
                        default: AppendNumber(lettersFinal[i]); break;
                    }
                }
                else if (char.IsSurrogate(lettersFinal[i]))
                {
                    AppendNumber(lettersFinal[i]);
                }
                else
                {
                    FlushNumbers();
                    if (lettersFinal[i] != 0xFFFF)
                        InternalStringBuilder.Append(lettersFinal[i]);
                }
            }

            FlushNumbers();

            return InternalStringBuilder.ToString();

            void AppendNumber(char value)
            {
                numberList.Add(value);
            }

            void FlushNumbers()
            {
                if (numberList.Count == 0) return;
                for (var j = 0; j < numberList.Count; j++)
                    InternalStringBuilder.Append(numberList[numberList.Count - 1 - j]);
                numberList.Clear();
            }

            bool IsLatinChar(char c)
            {
                return char.IsNumber(c) || char.IsLower(c) || char.IsUpper(c);
            }
        }

        internal static ushort HandleInduNumber(ushort letterOrigin, ushort letterFinal)
        {
            return letterOrigin switch
            {
                0x0030 => 0x0660,
                0x0031 => 0x0661,
                0x0032 => 0x0662,
                0x0033 => 0x0663,
                0x0034 => 0x0664,
                0x0035 => 0x0665,
                0x0036 => 0x0666,
                0x0037 => 0x0667,
                0x0038 => 0x0668,
                0x0039 => 0x0669,
                _ => letterFinal
            };
        }

        private static bool IsIgnoredCharacter(char ch)
        {
            var isPunctuation = char.IsPunctuation(ch);
            var isNumber = char.IsNumber(ch);
            var isLower = char.IsLower(ch);
            var isUpper = char.IsUpper(ch);
            var isSymbol = char.IsSymbol(ch);
            var isPersianCharacter = ch is (char)0xFB56 or (char)0xFB7A or (char)0xFB8A or (char)0xFB92 or (char)0xFB8E;
            var isPresentationFormB = ch is <= (char)0xFEFF and >= (char)0xFE70;
            var isAcceptableCharacter = isPresentationFormB || isPersianCharacter || ch == (char)0xFBFC;

            return isPunctuation ||
                   isNumber ||
                   isLower ||
                   isUpper ||
                   isSymbol ||
                   !isAcceptableCharacter ||
                   ch == 'a' || ch == '>' || ch == '<' || ch == (char)0x061B;
        }

        private static bool IsLeadingLetter(char[] letters, int index)
        {
            var lettersThatCannotBeBeforeALeadingLetter = index == 0
                                                          || letters[index - 1] == ' '
                                                          || letters[index - 1] == '*'
                                                          || letters[index - 1] == 'A'
                                                          || char.IsPunctuation(letters[index - 1])
                                                          || letters[index - 1] == '>'
                                                          || letters[index - 1] == '<'
                                                          || letters[index - 1] == (int)IsolatedArabicLetters.Alef
                                                          || letters[index - 1] == (int)IsolatedArabicLetters.Dal
                                                          || letters[index - 1] == (int)IsolatedArabicLetters.Thal
                                                          || letters[index - 1] == (int)IsolatedArabicLetters.Ra2
                                                          || letters[index - 1] == (int)IsolatedArabicLetters.Zeen
                                                          || letters[index - 1] == (int)IsolatedArabicLetters.PersianZe
                                                          || letters[index - 1] == (int)IsolatedArabicLetters.Waw
                                                          || letters[index - 1] == (int)IsolatedArabicLetters.AlefMad
                                                          || letters[index - 1] == (int)IsolatedArabicLetters.AlefHamza
                                                          || letters[index - 1] == (int)IsolatedArabicLetters.Hamza
                                                          || letters[index - 1] ==
                                                          (int)IsolatedArabicLetters.AlefMaksoor
                                                          || letters[index - 1] == (int)IsolatedArabicLetters.WawHamza;

            var lettersThatCannotBeALeadingLetter = letters[index] != ' '
                                                    && letters[index] != (int)IsolatedArabicLetters.Dal
                                                    && letters[index] != (int)IsolatedArabicLetters.Thal
                                                    && letters[index] != (int)IsolatedArabicLetters.Ra2
                                                    && letters[index] != (int)IsolatedArabicLetters.Zeen
                                                    && letters[index] != (int)IsolatedArabicLetters.PersianZe
                                                    && letters[index] != (int)IsolatedArabicLetters.Alef
                                                    && letters[index] != (int)IsolatedArabicLetters.AlefHamza
                                                    && letters[index] != (int)IsolatedArabicLetters.AlefMaksoor
                                                    && letters[index] != (int)IsolatedArabicLetters.AlefMad
                                                    && letters[index] != (int)IsolatedArabicLetters.WawHamza
                                                    && letters[index] != (int)IsolatedArabicLetters.Waw
                                                    && letters[index] != (int)IsolatedArabicLetters.Hamza;

            var lettersThatCannotBeAfterLeadingLetter = index < letters.Length - 1
                                                        && letters[index + 1] != ' '
                                                        && letters[index + 1] != '\n'
                                                        && letters[index + 1] != '\r'
                                                        && !char.IsPunctuation(letters[index + 1])
                                                        && !char.IsNumber(letters[index + 1])
                                                        && !char.IsSymbol(letters[index + 1])
                                                        && !char.IsLower(letters[index + 1])
                                                        && !char.IsUpper(letters[index + 1])
                                                        && letters[index + 1] != (int)IsolatedArabicLetters.Hamza;

            return lettersThatCannotBeBeforeALeadingLetter && lettersThatCannotBeALeadingLetter &&
                   lettersThatCannotBeAfterLeadingLetter;
        }

        private static bool IsFinishingLetter(char[] letters, int index)
        {
            var lettersThatCannotBeBeforeAFinishingLetter = index != 0 &&
                                                            letters[index - 1] != ' '
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.Dal
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.Thal
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.Ra2
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.Zeen
                                                            && letters[index - 1] !=
                                                            (int)IsolatedArabicLetters.PersianZe
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.Waw
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.Alef
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.AlefMad
                                                            && letters[index - 1] !=
                                                            (int)IsolatedArabicLetters.AlefHamza
                                                            && letters[index - 1] !=
                                                            (int)IsolatedArabicLetters.AlefMaksoor
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.WawHamza
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.Hamza
                                                            && !char.IsPunctuation(letters[index - 1])
                                                            && !char.IsSymbol(letters[index - 1])
                                                            && letters[index - 1] != '>'
                                                            && letters[index - 1] != '<';


            var lettersThatCannotBeFinishingLetters =
                letters[index] != ' ' && letters[index] != (int)IsolatedArabicLetters.Hamza;

            return lettersThatCannotBeBeforeAFinishingLetter && lettersThatCannotBeFinishingLetters;
        }

        private static bool IsMiddleLetter(char[] letters, int index)
        {
            var lettersThatCannotBeMiddleLetters = index != 0 &&
                                                   letters[index] != (int)IsolatedArabicLetters.Alef
                                                   && letters[index] != (int)IsolatedArabicLetters.Dal
                                                   && letters[index] != (int)IsolatedArabicLetters.Thal
                                                   && letters[index] != (int)IsolatedArabicLetters.Ra2
                                                   && letters[index] != (int)IsolatedArabicLetters.Zeen
                                                   && letters[index] != (int)IsolatedArabicLetters.PersianZe
                                                   && letters[index] != (int)IsolatedArabicLetters.Waw
                                                   && letters[index] != (int)IsolatedArabicLetters.AlefMad
                                                   && letters[index] != (int)IsolatedArabicLetters.AlefHamza
                                                   && letters[index] != (int)IsolatedArabicLetters.AlefMaksoor
                                                   && letters[index] != (int)IsolatedArabicLetters.WawHamza
                                                   && letters[index] != (int)IsolatedArabicLetters.Hamza;

            var lettersThatCannotBeBeforeMiddleCharacters = index != 0 &&
                                                            letters[index - 1] != (int)IsolatedArabicLetters.Alef
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.Dal
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.Thal
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.Ra2
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.Zeen
                                                            && letters[index - 1] !=
                                                            (int)IsolatedArabicLetters.PersianZe
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.Waw
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.AlefMad
                                                            && letters[index - 1] !=
                                                            (int)IsolatedArabicLetters.AlefHamza
                                                            && letters[index - 1] !=
                                                            (int)IsolatedArabicLetters.AlefMaksoor
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.WawHamza
                                                            && letters[index - 1] != (int)IsolatedArabicLetters.Hamza
                                                            && !char.IsPunctuation(letters[index - 1])
                                                            && letters[index - 1] != '>'
                                                            && letters[index - 1] != '<'
                                                            && letters[index - 1] != ' '
                                                            && letters[index - 1] != '*';

            var lettersThatCannotBeAfterMiddleCharacters = (index < letters.Length - 1) && (letters[index + 1] != ' '
                && letters[index + 1] != '\r'
                && letters[index + 1] != (int)IsolatedArabicLetters.Hamza
                && !char.IsNumber(letters[index + 1])
                && !char.IsSymbol(letters[index + 1])
                && !char.IsPunctuation(letters[index + 1]));

            return lettersThatCannotBeAfterMiddleCharacters &&
                   lettersThatCannotBeBeforeMiddleCharacters &&
                   lettersThatCannotBeMiddleLetters &&
                   !char.IsPunctuation(letters[index + 1]);
        }
    }
}
