using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KavrosSlayerHelper.Core
{
    /// <summary>One OCR line with its box (any consistent pixel space).</summary>
    public sealed class TextLine
    {
        public string Text;
        public float X, Y, W, H;
        public TextLine() { }
        public TextLine(string text, float x, float y, float w, float h) { Text = text; X = x; Y = y; W = w; H = h; }
        public float Mid { get { return Y + H / 2f; } }
        public override string ToString() { return Text; }
    }

    /// <summary>What the RS3 slayer counter shows.</summary>
    public sealed class CounterRead
    {
        public int Left = -1, Points = -1, Streak = -1;
        public string Task = "";
        public TextLine NumberLine;          // the big number's OCR line (for digit learning)
        public bool Minimised;               // the minimised counter: just the kills left
        public bool Valid { get { return Left >= 0 && (Minimised || Points >= 0 || Streak >= 0 || Task.Length > 0); } }
        public bool SameValues(CounterRead o)
        {
            return o != null && Left == o.Left && Points == o.Points && Streak == o.Streak && string.Equals(Task, o.Task, StringComparison.OrdinalIgnoreCase);
        }
        public override string ToString() { return "left=" + Left + " task=" + Task + " points=" + Points + " streak=" + Streak + (Minimised ? " (minimised)" : ""); }
    }

    /// <summary>Parses the slayer counter widget:
    /// <code>
    ///        135          (kills left, big yellow number)
    ///      Dragons        (task, yellow)
    /// Slayer points: 6,257
    /// Task streak:     291
    /// </code></summary>
    public static class CounterParser
    {
        static readonly Regex PointsRe = new Regex(@"p\s*[o0]\s*[il1|!]?\s*[nm]\s*[t+]\s*s?\b[^0-9]{0,6}(?<n>[0-9OolI|][0-9OolI|,.\s]*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex StreakRe = new Regex(@"s\s*t\s*r\s*[e3c]\s*[a4]?\s*[kx]\b[^0-9]{0,6}(?<n>[0-9OolI|][0-9OolI|,.\s]*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Letters = new Regex(@"[A-Za-z]{3,}", RegexOptions.CultureInvariant);

        /// <summary>OCR lines that sit at the same height (a label and its value read apart) become one row.</summary>
        public static List<TextLine> Rows(IEnumerable<TextLine> lines)
        {
            var sorted = lines.Where(l => l != null && !string.IsNullOrWhiteSpace(l.Text)).OrderBy(l => l.Mid).ToList();
            var rows = new List<List<TextLine>>();
            foreach (TextLine l in sorted)
            {
                List<TextLine> last = rows.Count > 0 ? rows[rows.Count - 1] : null;
                if (last != null && Math.Abs(last.Average(x => x.Mid) - l.Mid) <= Math.Max(4f, Math.Min(last.Max(x => x.H), l.H) * 0.5f)) last.Add(l);
                else rows.Add(new List<TextLine> { l });
            }
            return rows.Select(r =>
            {
                var o = r.OrderBy(x => x.X).ToList();
                float x0 = o.Min(x => x.X), y0 = o.Min(x => x.Y), x1 = o.Max(x => x.X + x.W), y1 = o.Max(x => x.Y + x.H);
                return new TextLine(string.Join(" ", o.Select(x => x.Text.Trim())), x0, y0, x1 - x0, y1 - y0);
            }).ToList();
        }

        public static int Num(string s)
        {
            if (s == null) return -1;
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c >= '0' && c <= '9') sb.Append(c);
                else if (c == 'O' || c == 'o') sb.Append('0');
                else if (c == 'l' || c == 'I' || c == '|') sb.Append('1');
            }
            int n;
            return sb.Length > 0 && sb.Length <= 9 && int.TryParse(sb.ToString(), out n) ? n : -1;
        }

        public static CounterRead Parse(IEnumerable<TextLine> lines)
        {
            var r = new CounterRead();
            List<TextLine> rows = Rows(lines);
            int pointsRow = -1, streakRow = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                string t = ChatParser.Clean(rows[i].Text);
                Match m;
                if (r.Points < 0 && (m = PointsRe.Match(t)).Success)
                { int n = Num(m.Groups["n"].Value); if (n >= 0) { r.Points = n; pointsRow = i; continue; } }
                if (r.Streak < 0 && (m = StreakRe.Match(t)).Success)
                { int n = Num(m.Groups["n"].Value); if (n >= 0) { r.Streak = n; streakRow = i; } }
            }
            // The layout: number, task, points, streak. Work upwards from the labels when they were read, so
            // stray text above the counter can't pose as the number.
            int first = pointsRow >= 0 ? pointsRow : streakRow;
            int numberRow = -1;
            if (first > 0)
            {
                int taskRow = -1;
                for (int i = first - 1; i >= 0 && i >= first - 2; i--)
                {
                    string t = ChatParser.Clean(rows[i].Text);
                    TextLine nl;
                    int n = NumberToken(rows[i], lines, out nl);
                    if (n >= 0) { r.Left = n; r.NumberLine = nl; numberRow = i; break; }
                    if (taskRow < 0 && Letters.IsMatch(t) && !Regex.IsMatch(t, @"\d{2}")) { taskRow = i; r.Task = CleanTask(t); }
                }
            }
            else
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    string t = ChatParser.Clean(rows[i].Text);
                    if (r.Left < 0)
                    {
                        TextLine nl;
                        int n = NumberToken(rows[i], lines, out nl);
                        if (n >= 0) { r.Left = n; r.NumberLine = nl; numberRow = i; continue; }
                    }
                    else if (r.Task.Length == 0 && Letters.IsMatch(t) && !Regex.IsMatch(t, @"\d{2}")) { r.Task = CleanTask(t); break; }
                }
            }
            // Just a number (no task, points or streak): the minimised counter. The host only trusts it for a
            // task it already knows.
            // Any other line that reads as words means this is not the counter.
            if (r.Left >= 0 && r.Task.Length == 0 && r.Points < 0 && r.Streak < 0)
            {
                bool words = rows.Where((row, i) => i != numberRow).Any(row => Regex.IsMatch(ChatParser.Clean(row.Text), @"[A-Za-z]{3,}"));
                if (words) r.Left = -1; else r.Minimised = true;
            }
            // No separate number row: "135 Dragons" read as one line.
            if (r.Left < 0)
                foreach (TextLine row in rows)
                {
                    Match m = Regex.Match(ChatParser.Clean(row.Text), @"^(?<n>\d[\d,]*)\s+(?<t>[A-Za-z][A-Za-z' \-]+)$");
                    if (m.Success && !PointsRe.IsMatch(row.Text) && !StreakRe.IsMatch(row.Text)) { r.Left = Num(m.Groups["n"].Value); r.Task = CleanTask(m.Groups["t"].Value); r.NumberLine = row; break; }
                }
            return r;
        }

        static string CleanTask(string t)
        {
            // Digits inside a word are OCR slips for letters ("Dem0ns", "Abyssa1"), not separators.
            t = Regex.Replace(t, @"(?<=[A-Za-z])[0-9|]+|[0-9|]+(?=[A-Za-z])", mm => mm.Value.Replace('0', 'o').Replace('1', 'l').Replace('|', 'l').Replace('5', 's').Replace('8', 'B'));
            t = Regex.Replace(t, @"[^A-Za-z' \-]", " ");
            // Single letters are icon noise ("Soul devourers e"), not part of a task name.
            t = string.Join(" ", t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1 || w == "a"));
            return t.Trim();
        }

        static readonly Regex NumTok = new Regex(@"^[0-9OolI|][0-9OolI|,.]*$", RegexOptions.CultureInvariant);

        /// <summary>The single number in a row (tokens that are short non-letter junk are ignored), or -1.
        /// Words of more than two letters mean it's not the number row.</summary>
        static int NumberToken(TextLine row, IEnumerable<TextLine> lines, out TextLine numLine)
        {
            numLine = null;
            string t = ChatParser.Clean(row.Text);
            int found = -1, count = 0;
            foreach (string tok in t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string k = tok.Trim(',', '.', ':', ';');
                if (k.Length == 0) continue;
                if (NumTok.IsMatch(k) && Regex.IsMatch(k, @"\d")) { int n = Num(k); if (n >= 0 && n < 100000) { found = n; count++; } continue; }
                if (Regex.IsMatch(k, @"[A-Za-z]{3,}")) return -1;
            }
            if (count != 1) return -1;
            // Prefer the OCR line that holds just the number (a tighter box for digit learning).
            numLine = lines.FirstOrDefault(l => l != null && Math.Abs(l.Mid - row.Mid) <= Math.Max(4f, row.H * 0.5f) && Num(l.Text) == found && NumTok.IsMatch(ChatParser.Clean(l.Text).Trim(',', '.', ' '))) ?? row;
            return found;
        }
    }

    /// <summary>The NPC dialogue box: the speaker (gold caps at the top) and the text under it.</summary>
    public sealed class DialogueRead
    {
        public string Speaker = "";      // as read
        public string Master;            // a known slayer master, or null
        public string Text = "";         // body lines joined (line wraps undone)
        public ChatEvent Event;          // what the patterns made of the text, or null
    }

    public static class DialogueParser
    {
        static readonly Regex Continue = new Regex(@"^\s*(click here to continue|continue|click to continue)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static DialogueRead Parse(IEnumerable<TextLine> lines, ChatParser parser, SlayerData data)
        {
            var d = new DialogueRead();
            List<TextLine> rows = CounterParser.Rows(lines);
            var body = new List<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                string t = ChatParser.Clean(rows[i].Text);
                if (t.Length == 0 || Continue.IsMatch(t)) continue;
                if (d.Master == null && i <= 1)
                {
                    string letters = Regex.Replace(t, @"[^A-Za-z' ]", "").Trim();
                    SlayerMaster m = data.FindMaster(letters);
                    if (m != null) { d.Speaker = t; d.Master = m.Name; continue; }
                    // A heading in caps that isn't a master (another NPC): still not body text.
                    if (i == 0 && letters.Length >= 3 && letters == letters.ToUpperInvariant() && !t.Contains(" ") && rows.Count > 1) { d.Speaker = t; continue; }
                }
                body.Add(t);
            }
            d.Text = Regex.Replace(string.Join(" ", body), @"(\w)- (\w)", "$1$2");
            d.Text = Regex.Replace(d.Text, @"\s+", " ").Trim();
            if (parser != null && d.Text.Length > 0)
            {
                d.Event = parser.Parse(d.Text);
                if (d.Event != null && d.Master != null) d.Event.Master = d.Master;
            }
            return d;
        }
    }

    /// <summary>Only lets a counter reading through once it has been read the same way twice in a row
    /// (a single misread digit never reaches the panel). The very first reading passes straight away
    /// when nothing is known yet.</summary>
    /// <summary>Decides when a counter reading is adopted. A change must be read the same way on
    /// <see cref="Confirmations"/> consecutive frames. It must also be plausible: kills left only go down,
    /// by at most <see cref="MaxStep"/> between adopted readings, unless a new task shows up (a different
    /// task name, or none known yet). An implausible reading (247 read as 99, or a jump up to 260) is only
    /// adopted if it stays the same for <see cref="ImplausibleConfirmations"/> frames in a row, so a bad
    /// earlier value can still be corrected by the counter, which is the truth.</summary>
    public sealed class CounterFilter
    {
        public int Confirmations = 2;
        public int ImplausibleConfirmations = 8;
        public int MaxStep = 20;
        /// <summary>Task text as read -> the known task name, "" when it can't be placed.</summary>
        public Func<string, string> Resolve = t => t ?? "";

        CounterRead pending, accepted;
        int pendingCount;
        public bool NeedsConfirm { get { return pending != null; } }
        public CounterRead Accepted { get { return accepted; } }
        public int PendingCount { get { return pendingCount; } }

        string Task(CounterRead r) { return r.Minimised || string.IsNullOrEmpty(r.Task) ? "" : (Resolve(r.Task) ?? ""); }

        /// <summary>The values that matter, with unread points/streak/task filled from the adopted reading.</summary>
        string Key(CounterRead r)
        {
            int p = r.Points >= 0 ? r.Points : accepted != null ? accepted.Points : -1;
            int st = r.Streak >= 0 ? r.Streak : accepted != null ? accepted.Streak : -1;
            string t = Task(r);
            if (t.Length == 0 && accepted != null) t = Task(accepted);
            return r.Left + "|" + p + "|" + st + "|" + t.ToLowerInvariant();
        }

        public CounterRead Offer(CounterRead r, SlayerState state)
        {
            if (r == null || !r.Valid) return null;
            string k = Key(r);
            if (accepted != null && k == Key(accepted)) { pending = null; pendingCount = 0; return null; }
            if (pending != null && k == Key(pending)) pendingCount++;
            else { pending = r; pendingCount = 1; }
            int need = Plausible(r, state) ? Confirmations : ImplausibleConfirmations;
            if (pendingCount < need) return null;
            accepted = r; pending = null; pendingCount = 0;
            return r;
        }

        /// <summary>Kills left going down by a small step, the same count, or a new task.</summary>
        public bool Plausible(CounterRead r, SlayerState s)
        {
            if (s == null || !s.HasTask || s.Left < 0) return true;
            string task = Task(r);
            if (task.Length > 0 && !string.Equals(task, s.Monster, StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Complete && task.Length > 0) return true;           // a new task of the same kind after finishing one
            int step = s.Left - r.Left;
            return step >= 0 && step <= MaxStep;
        }

        public void Reset() { pending = null; accepted = null; pendingCount = 0; }
    }
}
