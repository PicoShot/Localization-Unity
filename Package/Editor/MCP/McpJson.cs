using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PicoShot.Localization.Editor.Mcp
{
    /// <summary>
    /// Minimal JSON parser and serializer.
    /// </summary>
    public static class McpJson
    {
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var parser = new Parser(json);
            parser.SkipWhitespace();
            object value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.IsAtEnd)
                throw new FormatException("Unexpected trailing characters in JSON.");
            return value;
        }

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        public static Dictionary<string, object> AsObject(object value, string name)
        {
            if (value is Dictionary<string, object> dict) return dict;
            throw new FormatException($"'{name}' must be a JSON object.");
        }

        public static string GetString(Dictionary<string, object> obj, string key, string defaultValue = null)
        {
            if (obj.TryGetValue(key, out object value))
            {
                if (value == null) return defaultValue;
                if (value is string s) return s;
                throw new FormatException($"'{key}' must be a JSON string.");
            }
            return defaultValue;
        }

        public static string RequireString(Dictionary<string, object> obj, string key)
        {
            string value = GetString(obj, key);
            if (value == null)
                throw new FormatException($"Missing required parameter '{key}'.");
            return value;
        }

        public static bool GetBool(Dictionary<string, object> obj, string key, bool defaultValue = false)
        {
            if (obj.TryGetValue(key, out object value))
            {
                if (value is bool b) return b;
                throw new FormatException($"'{key}' must be a JSON boolean.");
            }
            return defaultValue;
        }

        public static int GetInt(Dictionary<string, object> obj, string key, int defaultValue = 0)
        {
            if (obj.TryGetValue(key, out object value))
            {
                if (value is long l) return (int)l;
                if (value is double d) return (int)d;
                throw new FormatException($"'{key}' must be a JSON number.");
            }
            return defaultValue;
        }

        public static List<object> GetArray(Dictionary<string, object> obj, string key)
        {
            if (obj.TryGetValue(key, out object value))
            {
                if (value is List<object> list) return list;
                throw new FormatException($"'{key}' must be a JSON array.");
            }
            return null;
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case double d:
                    if (double.IsInfinity(d) || double.IsNaN(d))
                        sb.Append("null");
                    else
                        sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case float f:
                    WriteValue(sb, (double)f);
                    break;
                case Dictionary<string, object> dict:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kvp in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, kvp.Key);
                        sb.Append(':');
                        WriteValue(sb, kvp.Value);
                    }
                    sb.Append('}');
                    break;
                case List<object> list:
                    sb.Append('[');
                    for (int j = 0; j < list.Count; j++)
                    {
                        if (j > 0) sb.Append(',');
                        WriteValue(sb, list[j]);
                    }
                    sb.Append(']');
                    break;
                case List<string> stringList:
                    sb.Append('[');
                    for (int j = 0; j < stringList.Count; j++)
                    {
                        if (j > 0) sb.Append(',');
                        WriteString(sb, stringList[j]);
                    }
                    sb.Append(']');
                    break;
                default:
                    WriteString(sb, value.ToString());
                    break;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Parser
        {
            private readonly string _json;
            private int _pos;

            public Parser(string json)
            {
                _json = json;
                _pos = 0;
            }

            public bool IsAtEnd => _pos >= _json.Length;

            public void SkipWhitespace()
            {
                while (_pos < _json.Length)
                {
                    char c = _json[_pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _pos++;
                    else break;
                }
            }

            public object ParseValue()
            {
                if (_pos >= _json.Length)
                    throw new FormatException("Unexpected end of JSON.");
                char c = _json[_pos];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ParseNumber();
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                _pos++; // {
                SkipWhitespace();
                if (_pos < _json.Length && _json[_pos] == '}')
                {
                    _pos++;
                    return dict;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (_pos >= _json.Length || _json[_pos] != '"')
                        throw new FormatException("Expected string key in JSON object.");
                    string key = ParseString();
                    SkipWhitespace();
                    if (_pos >= _json.Length || _json[_pos] != ':')
                        throw new FormatException("Expected ':' in JSON object.");
                    _pos++;
                    SkipWhitespace();
                    dict[key] = ParseValue();
                    SkipWhitespace();
                    if (_pos >= _json.Length)
                        throw new FormatException("Unexpected end of JSON object.");
                    char next = _json[_pos];
                    if (next == ',') { _pos++; continue; }
                    if (next == '}') { _pos++; return dict; }
                    throw new FormatException("Expected ',' or '}' in JSON object.");
                }
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                _pos++; // [
                SkipWhitespace();
                if (_pos < _json.Length && _json[_pos] == ']')
                {
                    _pos++;
                    return list;
                }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ParseValue());
                    SkipWhitespace();
                    if (_pos >= _json.Length)
                        throw new FormatException("Unexpected end of JSON array.");
                    char next = _json[_pos];
                    if (next == ',') { _pos++; continue; }
                    if (next == ']') { _pos++; return list; }
                    throw new FormatException("Expected ',' or ']' in JSON array.");
                }
            }

            private string ParseString()
            {
                var sb = new StringBuilder();
                _pos++; // opening quote
                while (true)
                {
                    if (_pos >= _json.Length)
                        throw new FormatException("Unterminated JSON string.");
                    char c = _json[_pos++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (_pos >= _json.Length)
                            throw new FormatException("Unterminated JSON escape.");
                        char e = _json[_pos++];
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
                                if (_pos + 4 > _json.Length)
                                    throw new FormatException("Invalid unicode escape in JSON.");
                                string hex = _json.Substring(_pos, 4);
                                _pos += 4;
                                if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                    throw new FormatException("Invalid unicode escape in JSON.");
                                // Surrogate pair support.
                                if (code >= 0xD800 && code <= 0xDBFF && _pos + 6 <= _json.Length &&
                                    _json[_pos] == '\\' && _json[_pos + 1] == 'u')
                                {
                                    string lowHex = _json.Substring(_pos + 2, 4);
                                    if (int.TryParse(lowHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int low) &&
                                        low >= 0xDC00 && low <= 0xDFFF)
                                    {
                                        _pos += 6;
                                        sb.Append(char.ConvertFromUtf32(0x10000 + ((code - 0xD800) << 10) + (low - 0xDC00)));
                                        break;
                                    }
                                }
                                sb.Append((char)code);
                                break;
                            default:
                                throw new FormatException($"Invalid JSON escape '\\{e}'.");
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            private object ParseNumber()
            {
                int start = _pos;
                if (_pos < _json.Length && _json[_pos] == '-') _pos++;
                while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
                bool isDouble = false;
                if (_pos < _json.Length && _json[_pos] == '.')
                {
                    isDouble = true;
                    _pos++;
                    while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
                }
                if (_pos < _json.Length && (_json[_pos] == 'e' || _json[_pos] == 'E'))
                {
                    isDouble = true;
                    _pos++;
                    if (_pos < _json.Length && (_json[_pos] == '+' || _json[_pos] == '-')) _pos++;
                    while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
                }
                string token = _json.Substring(start, _pos - start);
                if (!isDouble && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                    return l;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    return d;
                throw new FormatException($"Invalid JSON number '{token}'.");
            }

            private void Expect(string literal)
            {
                if (_pos + literal.Length > _json.Length || _json.Substring(_pos, literal.Length) != literal)
                    throw new FormatException($"Invalid JSON value, expected '{literal}'.");
                _pos += literal.Length;
            }
        }
    }
}
