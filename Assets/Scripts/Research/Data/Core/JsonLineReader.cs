using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PlayCT.Research
{
    /// <summary>
    /// Reads back the JSON lines that <see cref="JsonLine"/> writes: one flat object per line with string, number, boolean,
    /// null, array and (rarely) nested object values. Numbers without a fraction or exponent become long, the others double;
    /// arrays become List of object; objects become a List of key/value pairs. It never modifies what it reads.
    /// </summary>
    public static class JsonLineReader
    {
        public static bool TryParseObject(string line, out List<KeyValuePair<string, object>> fields)
        {
            fields = null;
            if (string.IsNullOrWhiteSpace(line)) return false;
            try
            {
                var position = 0;
                SkipSpace(line, ref position);
                var result = ReadObject(line, ref position);
                SkipSpace(line, ref position);
                if (position != line.Length) return false;
                fields = result;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        static List<KeyValuePair<string, object>> ReadObject(string s, ref int i)
        {
            Expect(s, ref i, '{');
            var fields = new List<KeyValuePair<string, object>>();
            SkipSpace(s, ref i);
            if (Peek(s, i) == '}')
            {
                i++;
                return fields;
            }
            while (true)
            {
                SkipSpace(s, ref i);
                var key = ReadString(s, ref i);
                SkipSpace(s, ref i);
                Expect(s, ref i, ':');
                SkipSpace(s, ref i);
                fields.Add(new KeyValuePair<string, object>(key, ReadValue(s, ref i)));
                SkipSpace(s, ref i);
                var next = Peek(s, i);
                i++;
                if (next == '}') return fields;
                if (next != ',') throw new FormatException("Expected ',' or '}'.");
            }
        }

        static object ReadValue(string s, ref int i)
        {
            var c = Peek(s, i);
            switch (c)
            {
                case '"': return ReadString(s, ref i);
                case '{': return ReadObject(s, ref i);
                case '[': return ReadArray(s, ref i);
                case 't': ExpectWord(s, ref i, "true"); return true;
                case 'f': ExpectWord(s, ref i, "false"); return false;
                case 'n': ExpectWord(s, ref i, "null"); return null;
                default: return ReadNumber(s, ref i);
            }
        }

        static List<object> ReadArray(string s, ref int i)
        {
            Expect(s, ref i, '[');
            var items = new List<object>();
            SkipSpace(s, ref i);
            if (Peek(s, i) == ']')
            {
                i++;
                return items;
            }
            while (true)
            {
                SkipSpace(s, ref i);
                items.Add(ReadValue(s, ref i));
                SkipSpace(s, ref i);
                var next = Peek(s, i);
                i++;
                if (next == ']') return items;
                if (next != ',') throw new FormatException("Expected ',' or ']'.");
            }
        }

        static object ReadNumber(string s, ref int i)
        {
            var start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            var text = s.Substring(start, i - start);
            if (text.Length == 0) throw new FormatException("Unexpected character.");
            var integral = text.IndexOfAny(new[] { '.', 'e', 'E' }) < 0;
            if (integral && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return l;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
            throw new FormatException("Bad number.");
        }

        static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("Unterminated string.");
                var c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (i >= s.Length) throw new FormatException("Bad escape.");
                var e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            throw new FormatException("Bad unicode escape.");
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: throw new FormatException("Bad escape.");
                }
            }
        }

        static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static char Peek(string s, int i) => i < s.Length ? s[i] : throw new FormatException("Unexpected end.");

        static void Expect(string s, ref int i, char c)
        {
            if (Peek(s, i) != c) throw new FormatException($"Expected '{c}'.");
            i++;
        }

        static void ExpectWord(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new FormatException($"Expected '{word}'.");
            i += word.Length;
        }
    }
}
