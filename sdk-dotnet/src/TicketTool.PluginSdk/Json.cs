using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TicketTool.PluginSdk
{
    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }

    /// <summary>
    /// A small JSON reader and writer. System.Text.Json isn't part of
    /// netstandard2.1, and Oxide's Newtonsoft version varies by host, so the SDK
    /// carries its own. Parsed values are Dictionary&lt;string, object&gt;,
    /// List&lt;object&gt;, string, long, double, bool and null.
    /// </summary>
    public static class Json
    {
        // Deeper than any protocol message; stops a hostile reply from blowing the stack.
        private const int MaxDepth = 64;

        public static object Parse(string text)
        {
            if (text == null) throw new JsonException("No JSON text");
            var p = new Parser(text);
            p.SkipWhitespace();
            object value = p.ReadValue(0);
            p.SkipWhitespace();
            if (p.Pos != text.Length) throw p.Error("Unexpected trailing characters");
            return value;
        }

        public static Dictionary<string, object> ParseObject(string text)
        {
            var map = Parse(text) as Dictionary<string, object>;
            if (map == null) throw new JsonException("Expected a JSON object");
            return map;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, int depth)
        {
            if (depth > MaxDepth) throw new JsonException("Value nested too deeply");
            if (value == null)
            {
                sb.Append("null");
            }
            else if (value is string s)
            {
                WriteString(sb, s);
            }
            else if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
            }
            else if (value is double || value is float || value is decimal)
            {
                double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) throw new JsonException("JSON can't represent " + d);
                if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
                    sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                else
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            }
            else if (value is int || value is long || value is short || value is byte || value is uint
                     || value is ulong || value is ushort || value is sbyte)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            else if (value is IDictionary dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    WriteValue(sb, e.Value, depth + 1);
                }
                sb.Append('}');
            }
            else if (value is IEnumerable list)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item, depth + 1);
                }
                sb.Append(']');
            }
            else
            {
                throw new JsonException("Can't write " + value.GetType().FullName + " as JSON");
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
                        // U+2028/2029 are valid JSON but break some JavaScript consumers.
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Parser
        {
            private readonly string _s;
            internal int Pos;

            internal Parser(string s)
            {
                _s = s;
            }

            internal JsonException Error(string message)
            {
                return new JsonException(message + " at position " + Pos);
            }

            internal void SkipWhitespace()
            {
                while (Pos < _s.Length)
                {
                    char c = _s[Pos];
                    if (c == ' ' || c == '\n' || c == '\r' || c == '\t') Pos++;
                    else break;
                }
            }

            internal object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Error("Nested too deeply");
                if (Pos >= _s.Length) throw Error("Unexpected end of input");
                char c = _s[Pos];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, Pos, word, 0, word.Length) != 0) throw Error("Expected " + word);
                Pos += word.Length;
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var map = new Dictionary<string, object>();
                Pos++;
                SkipWhitespace();
                if (Pos < _s.Length && _s[Pos] == '}')
                {
                    Pos++;
                    return map;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (Pos >= _s.Length || _s[Pos] != '"') throw Error("Expected a property name");
                    string key = ReadString();
                    SkipWhitespace();
                    if (Pos >= _s.Length || _s[Pos] != ':') throw Error("Expected ':'");
                    Pos++;
                    SkipWhitespace();
                    map[key] = ReadValue(depth + 1);
                    SkipWhitespace();
                    if (Pos >= _s.Length) throw Error("Unterminated object");
                    char c = _s[Pos++];
                    if (c == '}') return map;
                    if (c != ',') throw Error("Expected ',' or '}'");
                }
            }

            private List<object> ReadArray(int depth)
            {
                var list = new List<object>();
                Pos++;
                SkipWhitespace();
                if (Pos < _s.Length && _s[Pos] == ']')
                {
                    Pos++;
                    return list;
                }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (Pos >= _s.Length) throw Error("Unterminated array");
                    char c = _s[Pos++];
                    if (c == ']') return list;
                    if (c != ',') throw Error("Expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                Pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (Pos >= _s.Length) throw Error("Unterminated string");
                    char c = _s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw Error("Control character in string");
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (Pos >= _s.Length) throw Error("Unterminated escape");
                    char e = _s[Pos++];
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
                            if (Pos + 4 > _s.Length) throw Error("Bad unicode escape");
                            int code;
                            if (!int.TryParse(_s.Substring(Pos, 4), NumberStyles.AllowHexSpecifier,
                                    CultureInfo.InvariantCulture, out code))
                                throw Error("Bad unicode escape");
                            sb.Append((char)code);
                            Pos += 4;
                            break;
                        default:
                            throw Error("Bad escape");
                    }
                }
            }

            private object ReadNumber()
            {
                int start = Pos;
                if (_s[Pos] == '-') Pos++;
                bool isFloat = false;
                while (Pos < _s.Length)
                {
                    char c = _s[Pos];
                    if (c >= '0' && c <= '9')
                    {
                        Pos++;
                    }
                    else if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        isFloat = true;
                        Pos++;
                    }
                    else
                    {
                        break;
                    }
                }
                string text = _s.Substring(start, Pos - start);
                if (!isFloat)
                {
                    long l;
                    if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l)) return l;
                }
                double d;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                throw Error("Bad number");
            }
        }
    }

    /// <summary>Lenient, typed reads from parsed JSON: a missing or mistyped field is null.</summary>
    public static class JsonFields
    {
        public static string String(IDictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v)) return null;
            return v as string;
        }

        public static long? Long(IDictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v) || v == null) return null;
            if (v is long l) return l;
            if (v is double d && d == Math.Floor(d) && Math.Abs(d) < 9e15) return (long)d;
            return null;
        }

        public static bool? Bool(IDictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v)) return null;
            if (v is bool b) return b;
            return null;
        }

        public static Dictionary<string, object> Object(IDictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v)) return null;
            return v as Dictionary<string, object>;
        }

        public static List<object> List(IDictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v)) return new List<object>();
            return v as List<object> ?? new List<object>();
        }
    }
}
