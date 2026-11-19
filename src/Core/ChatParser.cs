using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KavrosSlayerHelper.Core
{
    public enum ChatKind { None, Assign, Progress, Complete, Kill }

    public sealed class ChatEvent
    {
        public ChatKind Kind;
        public string Monster;     // as written in chat (null if the message has none)
        public string Master;      // the speaker, when the line starts with "<Master>:"
        public int Count = -1;     // assigned amount
        public int Left = -1;      // kills left
        public int Streak = -1, Points = -1, Total = -1;
        public string Snippet;     // the matched part of the line, for the panel
        public string Line;

        public override string ToString()
        {
            return Kind + " monster=" + Monster + " count=" + Count + " left=" + Left + " streak=" + Streak + " points=" + Points + " total=" + Total + " master=" + Master;
        }
    }

    /// <summary>Matches chat lines against the configurable patterns (data "chat" section, .NET regex,
    /// case-insensitive). Named groups: monster, count, left, streak, points, total.</summary>
    public sealed class ChatParser
    {
        sealed class Rule { public ChatKind Kind; public Regex Re; }

        readonly List<Rule> rules = new List<Rule>();
        readonly SlayerData data;
        public readonly List<string> Errors = new List<string>();

        static readonly Regex Stamp = new Regex(@"^\s*\[?\s*\d{1,2}\s*[:.]\s*\d{2}(?:\s*[:.]\s*\d{2})?\s*\]?\s*", RegexOptions.CultureInvariant);
        static readonly Regex Speaker = new Regex(@"^\s*([A-Za-z][A-Za-z' ]{1,24}?)\s*:\s+", RegexOptions.CultureInvariant);
        static readonly Regex DigitLike = new Regex(@"(?<![A-Za-z])[0-9OolI|]*[0-9][0-9OolI|,.]*(?![A-Za-z])", RegexOptions.CultureInvariant);

        public ChatParser(SlayerData data)
        {
            this.data = data;
            Add(ChatKind.Complete, data.Chat.Complete);   // most specific first: completion lines also mention kills
            Add(ChatKind.Assign, data.Chat.Assign);
            Add(ChatKind.Progress, data.Chat.Progress);
            Add(ChatKind.Kill, data.Chat.Kill);
        }

        void Add(ChatKind kind, IEnumerable<string> patterns)
        {
            foreach (string p in patterns)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                try { rules.Add(new Rule { Kind = kind, Re = new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)) }); }
                catch (ArgumentException ex) { Errors.Add(kind + " pattern \"" + p + "\": " + ex.Message); }
            }
        }

        /// <summary>Cleans OCR text: straight quotes, single spaces, digit look-alikes inside numbers.</summary>
        public static string Clean(string line)
        {
            if (line == null) return "";
            var sb = new StringBuilder(line.Length);
            foreach (char ch in line)
            {
                char c = ch;
                if (c == '\u2019' || c == '\u2018' || c == '`' || c == '\u00b4') c = '\'';
                else if (c == '\u201c' || c == '\u201d') c = '"';
                else if (c == '\u00a0' || c == '\t') c = ' ';
                sb.Append(c);
            }
            string s = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            s = Regex.Replace(s, @"\s+([;:,.!?])", "$1");
            // "l42" or "1O5" in a number -> digits (only for runs that already contain a real digit).
            s = DigitLike.Replace(s, m => FixDigits(m.Value));
            return s;
        }

        static string FixDigits(string v)
        {
            var sb = new StringBuilder(v.Length);
            foreach (char c in v)
            {
                if (c == 'O' || c == 'o') sb.Append('0');
                else if (c == 'l' || c == 'I' || c == '|') sb.Append('1');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static int Number(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            var sb = new StringBuilder();
            foreach (char c in s) if (c >= '0' && c <= '9') sb.Append(c);
            int n;
            return sb.Length > 0 && sb.Length < 10 && int.TryParse(sb.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : -1;
        }

        public ChatEvent Parse(string rawLine)
        {
            string line = Clean(rawLine);
            if (line.Length < 6) return null;
            string body = Stamp.Replace(line, "", 1);
            string master = null;
            Match sp = Speaker.Match(body);
            if (sp.Success && data.FindMaster(sp.Groups[1].Value) != null)
            {
                master = data.FindMaster(sp.Groups[1].Value).Name;
            }
            foreach (Rule r in rules)
            {
                Match m;
                try { m = r.Re.Match(body); }
                catch (RegexMatchTimeoutException) { continue; }
                if (!m.Success) continue;
                var e = new ChatEvent { Kind = r.Kind, Master = master, Line = line, Snippet = m.Value.Trim() };
                Group g;
                if ((g = m.Groups["monster"]).Success) e.Monster = TrimMonster(g.Value);
                if ((g = m.Groups["count"]).Success) e.Count = Number(g.Value);
                if ((g = m.Groups["left"]).Success) e.Left = Number(g.Value);
                if ((g = m.Groups["streak"]).Success) e.Streak = Number(g.Value);
                if ((g = m.Groups["points"]).Success) e.Points = Number(g.Value);
                if ((g = m.Groups["total"]).Success) e.Total = Number(g.Value);
                if (r.Kind == ChatKind.Assign && string.IsNullOrEmpty(e.Monster)) continue;
                return e;
            }
            return null;
        }

        static string TrimMonster(string s)
        {
            s = s.Trim().Trim('.', ';', ':', ',', '!', '?', '"', '\'', ' ');
            s = Regex.Replace(s, @"\s+(?:good luck|now|please)$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^(?:the|some)\s+", "", RegexOptions.IgnoreCase);
            return s.Trim();
        }
    }
}
