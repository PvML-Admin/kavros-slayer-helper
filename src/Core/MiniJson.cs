using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KavrosSlayerHelper.Core
{
    /// <summary>Small JSON reader/writer. Objects become <see cref="JObj"/> (ordered, case-sensitive keys),
    /// arrays List&lt;object&gt;, numbers double, plus string, bool and null.</summary>
    public sealed class JObj : List<KeyValuePair<string, object>>
    {
        public object this[string key]
        {
            get { int i = IndexOf(key); return i < 0 ? null : base[i].Value; }
            set { int i = IndexOf(key); if (i < 0) Add(new KeyValuePair<string, object>(key, value)); else base[i] = new KeyValuePair<string, object>(key, value); }
        }

        public int IndexOf(string key)
        {
            for (int i = 0; i < Count; i++) if (base[i].Key == key) return i;
            return -1;
        }

        public bool Has(string key) { return IndexOf(key) >= 0; }
        public void Remove(string key) { int i = IndexOf(key); if (i >= 0) RemoveAt(i); }
        public IEnumerable<string> Keys { get { foreach (var kv in this) yield return kv.Key; } }

        public string Str(string key, string fallback = "") { var v = this[key]; return v == null ? fallback : Convert.ToString(v, CultureInfo.InvariantCulture); }
        public int Int(string key, int fallback = 0)
        {
            object v = this[key];
            if (v is double) return (int)Math.Round((double)v);
            int r; if (v is string && int.TryParse((string)v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r)) return r;
            return fallback;
        }
        public bool Bool(string key, bool fallback = false) { object v = this[key]; return v is bool ? (bool)v : fallback; }
        public JObj Obj(string key) { return this[key] as JObj; }
        public List<object> Arr(string key) { return this[key] as List<object>; }
    }

    public static class MiniJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("no JSON");
            int i = 0;
            if (text.Length > 0 && text[0] == '\uFEFF') i = 1;
            object v = Value(text, ref i);
            Ws(text, ref i);
            if (i != text.Length) throw Err(text, i, "trailing text");
            return v;
        }

        static FormatException Err(string s, int i, string what)
        {
            int line = 1, col = 1;
            for (int k = 0; k < i && k < s.Length; k++) { if (s[k] == '\n') { line++; col = 1; } else col++; }
            return new FormatException(what + " at line " + line + ", column " + col);
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                // Comments are allowed in hand-edited override files.
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*') { int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? s.Length : e + 2; continue; }
                break;
            }
        }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw Err(s, i, "unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var o = new JObj(); i++;
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return o; }
                while (true)
                {
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == '}') { i++; return o; }   // trailing comma
                    if (i >= s.Length || s[i] != '"') throw Err(s, i, "expected a \"key\"");
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw Err(s, i, "expected ':'");
                    i++;
                    o[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return o; }
                    throw Err(s, i, "expected ',' or '}'");
                }
            }
            if (c == '[')
            {
                var a = new List<object>(); i++;
                while (true)
                {
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    a.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    throw Err(s, i, "expected ',' or ']'");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (Match(s, ref i, "true")) return true;
            if (Match(s, ref i, "false")) return false;
            if (Match(s, ref i, "null")) return null;
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double d;
            if (i > st && double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            throw Err(s, st, "unexpected character");
        }

        static bool Match(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
            i += word.Length; return true;
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw Err(s, i, "bad \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw Err(s, i, "unterminated string");
        }

        // ---- writer ----
        public static string Write(object v, bool indent = true)
        {
            var sb = new StringBuilder();
            Write(sb, v, indent ? 0 : -1);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v, int depth)
        {
            string nl = depth >= 0 ? "\n" + new string(' ', (depth + 1) * 2) : "";
            string end = depth >= 0 ? "\n" + new string(' ', depth * 2) : "";
            int next = depth >= 0 ? depth + 1 : -1;
            if (v == null) { sb.Append("null"); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is string) { Quote(sb, (string)v); return; }
            if (v is JObj)
            {
                var o = (JObj)v;
                if (o.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                for (int k = 0; k < o.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append(nl); Quote(sb, o[k].Key); sb.Append(depth >= 0 ? ": " : ":"); Write(sb, o[k].Value, next);
                }
                sb.Append(end).Append('}');
                return;
            }
            if (v is System.Collections.IList)
            {
                var a = (System.Collections.IList)v;
                if (a.Count == 0) { sb.Append("[]"); return; }
                sb.Append('[');
                for (int k = 0; k < a.Count; k++) { if (k > 0) sb.Append(','); sb.Append(nl); Write(sb, a[k], next); }
                sb.Append(end).Append(']');
                return;
            }
            if (v is double || v is float || v is int || v is long)
            {
                sb.Append(Convert.ToDouble(v).ToString("R", CultureInfo.InvariantCulture)); return;
            }
            Quote(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        static void Quote(StringBuilder sb, string s)
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
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
