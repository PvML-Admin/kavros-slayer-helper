using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace KavrosSlayerHelper.Core
{
    /// <summary>The current task and the running totals. Saved as JSON in %AppData%\KavrosSlayerHelper\state.json.</summary>
    public sealed class SlayerState
    {
        public string Monster = "";      // slayer category ("Abyssal demons"); "" = no task
        public string Master = "";       // display name, "" = unknown
        public int Assigned;             // amount assigned, 0 = unknown
        public int Left = -1;            // kills left, -1 = unknown
        public bool Complete;
        public int Streak;
        public int Points;
        public int LastPoints = -1;      // points from the last completed task, -1 = unknown
        public string LastChat = "";     // what the panel says it read ("only 142 more to go")
        public string LastSource = "";   // "counter", "popup", "chat" (0.1.0) or "manual"
        public DateTime Updated;

        public bool HasTask { get { return !string.IsNullOrEmpty(Monster); } }
        public int Killed { get { return Assigned > 0 && Left >= 0 ? Math.Max(0, Assigned - Left) : -1; } }

        public JObj ToJson()
        {
            var o = new JObj();
            o["monster"] = Monster; o["master"] = Master; o["assigned"] = (double)Assigned; o["left"] = (double)Left;
            o["complete"] = Complete; o["streak"] = (double)Streak; o["points"] = (double)Points; o["lastPoints"] = (double)LastPoints;
            o["lastChat"] = LastChat; o["lastSource"] = LastSource;
            o["updated"] = Updated.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            return o;
        }

        public static SlayerState FromJson(JObj o)
        {
            var s = new SlayerState();
            if (o == null) return s;
            s.Monster = o.Str("monster"); s.Master = o.Str("master"); s.Assigned = Math.Max(0, o.Int("assigned"));
            s.Left = o.Int("left", -1); s.Complete = o.Bool("complete"); s.Streak = Math.Max(0, o.Int("streak"));
            s.Points = Math.Max(0, o.Int("points")); s.LastPoints = o.Int("lastPoints", -1);
            s.LastChat = o.Str("lastChat"); s.LastSource = o.Str("lastSource");
            DateTime t;
            if (DateTime.TryParse(o.Str("updated"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t)) s.Updated = t;
            return s;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, MiniJson.Write(ToJson()), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }

        public static SlayerState Load(string path)
        {
            try { if (File.Exists(path)) return FromJson(MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)) as JObj); }
            catch (Exception) { }
            return new SlayerState();
        }
    }

    /// <summary>Applies chat events and manual edits to the state. Every change is idempotent where the
    /// chat can repeat itself (the same line read twice must not count twice).</summary>
    public sealed class SlayerTracker
    {
        public readonly SlayerData Data;
        public SlayerState State;
        public Func<DateTime> Now = () => DateTime.Now;

        public SlayerTracker(SlayerData data, SlayerState state)
        {
            Data = data;
            State = state ?? new SlayerState();
        }

        public MonsterInfo Monster { get { return State.HasTask ? Data.FindMonster(State.Monster) : null; } }
        public SlayerMaster Master { get { return Data.FindMaster(State.Master); } }

        string Canonical(string raw)
        {
            MonsterInfo m = Data.FindMonster(raw);
            if (m != null) return m.Name;
            string t = (raw ?? "").Trim();
            return t.Length == 0 ? "" : char.ToUpperInvariant(t[0]) + t.Substring(1);
        }

        string GuessMaster(string monster, int amount, string speaker)
        {
            if (!string.IsNullOrEmpty(speaker)) return speaker;
            List<SlayerMaster> c = Data.MastersFor(monster, amount);
            // Keep the master we already know if they assign this monster.
            if (!string.IsNullOrEmpty(State.Master) && c.Any(m => m.Name == State.Master)) return State.Master;
            if (c.Count == 1) return c[0].Name;
            return "";
        }

        static string Quote(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length > 60) s = s.Substring(0, 59).TrimEnd() + "\u2026";
            return s;
        }

        /// <summary>Applies one parsed chat event. Returns true if anything changed.</summary>
        public bool Apply(ChatEvent e) { return Apply(e, "chat"); }

        /// <summary>Applies one parsed event; <paramref name="source"/> is "popup" for the master's dialogue.</summary>
        public bool Apply(ChatEvent e, string source)
        {
            if (e == null) return false;
            SlayerState s = State;
            switch (e.Kind)
            {
                case ChatKind.Assign:
                {
                    string name = Canonical(e.Monster);
                    if (name.Length == 0) return false;
                    int count = e.Count > 0 ? e.Count : 0;
                    // The same assignment read again (still on screen, or a Rescan) changes nothing.
                    if (s.HasTask && !s.Complete && s.Monster == name && (count == 0 || count == s.Assigned)) return false;
                    s.Monster = name;
                    s.Assigned = count;
                    s.Left = count > 0 ? count : -1;
                    s.Complete = false;
                    s.Master = GuessMaster(name, count, e.Master);
                    break;
                }
                case ChatKind.Progress:
                {
                    string name = string.IsNullOrEmpty(e.Monster) ? s.Monster : Canonical(e.Monster);
                    if (name.Length == 0) return false;
                    bool sameTask = s.HasTask && s.Monster == name;
                    if (!sameTask)
                    {
                        s.Monster = name;
                        s.Assigned = e.Left > 0 ? e.Left : 0;
                        s.Master = GuessMaster(name, 0, e.Master);
                    }
                    else if (!string.IsNullOrEmpty(e.Master)) s.Master = e.Master;
                    if (e.Left >= 0)
                    {
                        if (sameTask && s.Left == e.Left && !s.Complete) return false;
                        s.Left = e.Left;
                        if (s.Assigned > 0 && s.Left > s.Assigned) s.Assigned = s.Left;
                        if (s.Assigned == 0 && s.Left > 0) s.Assigned = s.Left;
                    }
                    else if (sameTask) return false;
                    s.Complete = s.Left == 0 && s.Complete;
                    break;
                }
                case ChatKind.Kill:
                {
                    if (!s.HasTask || s.Complete) return false;
                    if (e.Left >= 0) { if (s.Left == e.Left) return false; s.Left = e.Left; }
                    else if (s.Left > 0) s.Left--;
                    else return false;
                    if (s.Assigned > 0 && s.Left > s.Assigned) s.Assigned = s.Left;
                    break;
                }
                case ChatKind.Complete:
                {
                    if (e.Streak >= 0 && s.Complete && e.Streak == s.Streak) return false;   // the same message again
                    if (e.Streak < 0 && s.Complete) return false;
                    if (e.Streak >= 0 && e.Streak < s.Streak && s.Complete) return false;
                    s.Complete = true;
                    s.Left = 0;
                    s.Streak = e.Streak >= 0 ? e.Streak : s.Streak + 1;
                    int got = e.Points;
                    if (got < 0 && Master != null) got = Master.PointsFor(s.Streak);
                    s.LastPoints = got;
                    if (e.Total >= 0) s.Points = e.Total;
                    else if (got > 0) s.Points += got;
                    break;
                }
                default: return false;
            }
            s.LastChat = Quote(source == "popup" && !string.IsNullOrEmpty(e.Line) ? e.Line : e.Snippet);
            s.LastSource = source;
            s.Updated = Now();
            return true;
        }

        /// <summary>Applies a reading of the RS3 slayer counter, the source of truth for the count while it is
        /// on screen. The counter can show more than was assigned (extensions): the assigned amount grows to match.</summary>
        public bool ApplyCounter(CounterRead r)
        {
            if (r == null || !r.Valid) return false;
            SlayerState s = State;
            bool changed = false;
            string name = s.Monster ?? "";
            if (!string.IsNullOrEmpty(r.Task))
            {
                MonsterInfo m = Data.FindMonster(r.Task);
                // A task name OCR couldn't place never replaces a known task (it is more likely a misread).
                if (m != null) name = m.Name;
                else if (!s.HasTask) name = Canonical(r.Task);
            }
            if (name.Length == 0) name = s.Monster ?? "";
            if (name.Length > 0 && name != s.Monster)
            {
                s.Monster = name;
                s.Assigned = Math.Max(0, r.Left);
                s.Master = GuessMaster(name, 0, null);
                s.Complete = false;
                changed = true;
            }
            if (name.Length > 0 && r.Left >= 0 && r.Left != s.Left)
            {
                s.Left = r.Left;
                changed = true;
            }
            if (s.HasTask && s.Left >= 0 && s.Assigned < s.Left) { s.Assigned = s.Left; changed = true; }
            bool done = s.HasTask && s.Left == 0;
            if (done != s.Complete && s.HasTask) { s.Complete = done; changed = true; }
            if (r.Streak >= 0 && r.Streak != s.Streak) { s.Streak = r.Streak; changed = true; }
            if (r.Points >= 0 && r.Points != s.Points) { s.Points = r.Points; changed = true; }
            if (!changed && s.LastSource == "counter") return false;
            s.LastChat = "";
            s.LastSource = "counter";
            s.Updated = Now();
            return true;
        }

        // ---- manual edits (the panel's count controls) --------------------------------------------
        /// <summary>Sets the kills left by hand. If the assigned amount is unknown (or smaller), it becomes this.</summary>
        public bool SetLeft(int left)
        {
            SlayerState s = State;
            if (!s.HasTask || left < 0) return false;
            left = Math.Min(left, 99999);
            if (s.Left == left) return false;
            s.Left = left;
            if (s.Assigned == 0 || s.Assigned < left) s.Assigned = left;
            s.Complete = false;
            s.LastChat = "";
            s.LastSource = "manual";
            s.Updated = Now();
            return true;
        }

        /// <summary>One kill more (delta -1) or one fewer (+1).</summary>
        public bool Adjust(int delta)
        {
            SlayerState s = State;
            if (!s.HasTask) return false;
            if (s.Left < 0) return false;
            int next = Math.Max(0, s.Left + delta);
            if (next == s.Left) return false;
            s.Left = next;
            if (s.Assigned < next) s.Assigned = next;
            if (next > 0) s.Complete = false;
            s.LastChat = "";
            s.LastSource = "manual";
            s.Updated = Now();
            return true;
        }

        /// <summary>Clears the task (keeps the streak and points).</summary>
        public bool Clear()
        {
            SlayerState s = State;
            if (!s.HasTask && string.IsNullOrEmpty(s.LastChat)) return false;
            s.Monster = ""; s.Master = ""; s.Assigned = 0; s.Left = -1; s.Complete = false;
            s.LastChat = ""; s.LastSource = ""; s.Updated = Now();
            return true;
        }

        // ---- text for the panel ----------------------------------------------------------------
        public static string StreakHint(int streak)
        {
            int next10 = (streak / 10 + 1) * 10;
            int next50 = (streak / 50 + 1) * 50;
            if (next50 - streak <= 10) return (next50 - streak) + " to a 50-task bonus";
            return (next10 - streak) + " to a 10-task bonus";
        }
    }
}
