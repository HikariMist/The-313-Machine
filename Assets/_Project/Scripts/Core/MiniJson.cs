// Minimal, dependency-free JSON reader/writer used by the Core layer.
// Values map to: Dictionary<string, object>, List<object>, string, double, bool, null.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HikariForge.M313.Core
{
    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }

    public static class MiniJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new JsonException("JSON text is null");
            var p = new Parser(text);
            p.SkipWs();
            var v = p.ReadValue();
            p.SkipWs();
            if (!p.End) throw new JsonException($"Unexpected trailing data at {p.Pos}");
            return v;
        }

        public static string Serialize(object value, bool pretty = false)
        {
            var sb = new StringBuilder();
            Write(sb, value, pretty, 0);
            return sb.ToString();
        }

        private static void Indent(StringBuilder sb, int level)
        {
            sb.Append('\n');
            sb.Append(' ', level * 2);
        }

        private static void Write(StringBuilder sb, object v, bool pretty, int level)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> dict:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        if (pretty) Indent(sb, level + 1);
                        WriteString(sb, kv.Key);
                        sb.Append(pretty ? ": " : ":");
                        Write(sb, kv.Value, pretty, level + 1);
                    }
                    if (pretty && dict.Count > 0) Indent(sb, level);
                    sb.Append('}');
                    break;
                }
                case System.Collections.IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var item in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        if (pretty) Indent(sb, level + 1);
                        Write(sb, item, pretty, level + 1);
                    }
                    if (pretty && !first) Indent(sb, level);
                    sb.Append(']');
                    break;
                }
                default: WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture)); break;
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
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Parser
        {
            private readonly string _s;
            public int Pos;
            public Parser(string s) { _s = s; }
            public bool End => Pos >= _s.Length;

            public void SkipWs()
            {
                while (Pos < _s.Length)
                {
                    char c = _s[Pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') Pos++;
                    else break;
                }
            }

            private JsonException Error(string msg)
            {
                int line = 1;
                for (int i = 0; i < Pos && i < _s.Length; i++) if (_s[i] == '\n') line++;
                return new JsonException($"{msg} (line {line}, pos {Pos})");
            }

            public object ReadValue()
            {
                if (End) throw Error("Unexpected end of JSON");
                char c = _s[Pos];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error($"Unexpected character '{c}'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, Pos, word, 0, word.Length) != 0) throw Error($"Expected '{word}'");
                Pos += word.Length;
            }

            private Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>();
                Pos++;
                SkipWs();
                if (!End && _s[Pos] == '}') { Pos++; return d; }
                while (true)
                {
                    SkipWs();
                    if (End || _s[Pos] != '"') throw Error("Expected string key");
                    string key = ReadString();
                    SkipWs();
                    if (End || _s[Pos] != ':') throw Error("Expected ':'");
                    Pos++;
                    SkipWs();
                    d[key] = ReadValue();
                    SkipWs();
                    if (End) throw Error("Unterminated object");
                    if (_s[Pos] == ',') { Pos++; continue; }
                    if (_s[Pos] == '}') { Pos++; return d; }
                    throw Error("Expected ',' or '}'");
                }
            }

            private List<object> ReadArray()
            {
                var list = new List<object>();
                Pos++;
                SkipWs();
                if (!End && _s[Pos] == ']') { Pos++; return list; }
                while (true)
                {
                    SkipWs();
                    list.Add(ReadValue());
                    SkipWs();
                    if (End) throw Error("Unterminated array");
                    if (_s[Pos] == ',') { Pos++; continue; }
                    if (_s[Pos] == ']') { Pos++; return list; }
                    throw Error("Expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                Pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw Error("Unterminated string");
                    char c = _s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("Bad escape");
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
                            sb.Append((char)int.Parse(_s.Substring(Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            Pos += 4;
                            break;
                        default: throw Error($"Bad escape '\\{e}'");
                    }
                }
            }

            private object ReadNumber()
            {
                int start = Pos;
                if (_s[Pos] == '-') Pos++;
                while (!End && (char.IsDigit(_s[Pos]) || _s[Pos] == '.' || _s[Pos] == 'e' || _s[Pos] == 'E' || _s[Pos] == '+' || _s[Pos] == '-')) Pos++;
                string num = _s.Substring(start, Pos - start);
                if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw Error($"Bad number '{num}'");
                return d;
            }
        }
    }

    /// <summary>Typed accessors over parsed MiniJson objects.</summary>
    public static class J
    {
        public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;
        public static List<object> Arr(object o) => o as List<object> ?? new List<object>();

        public static object Get(Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? v : null;

        public static string Str(Dictionary<string, object> d, string key, string def = "") =>
            Get(d, key) is string s ? s : def;

        public static int Int(Dictionary<string, object> d, string key, int def = 0)
        {
            var v = Get(d, key);
            if (v is double dv) return (int)Math.Round(dv);
            if (v is int iv) return iv;
            if (v is long lv) return (int)lv;
            return def;
        }

        public static float Float(Dictionary<string, object> d, string key, float def = 0f)
        {
            var v = Get(d, key);
            if (v is double dv) return (float)dv;
            if (v is int iv) return iv;
            return def;
        }

        public static bool Bool(Dictionary<string, object> d, string key, bool def = false) =>
            Get(d, key) is bool b ? b : def;

        public static List<object> List(Dictionary<string, object> d, string key) => Arr(Get(d, key));

        public static List<string> Strings(Dictionary<string, object> d, string key)
        {
            var result = new List<string>();
            var v = Get(d, key);
            if (v is string single) { result.Add(single); return result; }
            foreach (var item in Arr(v)) if (item is string s) result.Add(s);
            return result;
        }
    }
}
