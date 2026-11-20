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
        public bool CounterSeen;         // the counter has been read for this task (it may set the total once)
        public DateTime Updated;

        public bool HasTask { get { return !string.IsNullOrEmpty(Monster); } }
        public int Killed { get { return Assigned > 0 && Left >= 0 ? Math.Max(0, Assigned - Left) : -1; } }

        public JObj ToJson()
        {
            var o = new JObj();
            o["monster"] = Monster; o["master"] = Master; o["assigned"] = (double)Assigned; o["left"] = (double)Left;
            o["complete"] = Complete; o["streak"] = (double)Streak; o["points"] = (double)Points; o["lastPoints"] = (double)LastPoints;
            o["lastChat"] = LastChat; o["lastSource"] = LastSource; o["counterSeen"] = CounterSeen;
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
            s.LastChat = o.Str("lastChat"); s.LastSource = o.Str("lastSource"); s.CounterSeen = o.Bool("counterSeen");
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
            // A task saved under a name that isn't in the data (an OCR misread kept by 0.1.2): put the right
            // name back when it can be placed, otherwise drop a counter-made one.
            if (State.HasTask && !Data.Monsters.Values.Any(m => m.Name == State.Monster))
            {
                MonsterInfo m = Data.FindMonster(State.Monster);
                if (m != null) State.Monster = m.Name;
                else if (State.LastSource == "counter") { State.Monster = ""; State.Left = -1; State.Assigned = 0; State.Complete = false; }
            }
            // A bogus saved total (0.1.3 could take the clock's 12:00 as 1,200): back to the count, and the
            // next counter read may set it again.
            if (State.HasTask)
            {
                int limit = AmountLimit(State.Monster, State.Master);
                if (State.Left > limit) { State.Left = -1; State.CounterSeen = false; }
                if (State.Assigned > limit || (State.Assigned > 1000 && limit < 1000))
                {
                    State.Assigned = Math.Max(0, State.Left);
                    State.CounterSeen = false;
                }
            }
        }

        public MonsterInfo Monster { get { return State.HasTask ? Data.FindMonster(State.Monster) : null; } }
        public SlayerMaster Master { get { return Data.FindMaster(State.Master); } }

        /// <summary>The largest believable amount for a task: the master's maximum (any master's when unknown)
        /// plus half again and 20 for extensions; 1000 for a task no master in the data assigns.</summary>
        public int AmountLimit(string monster, string master)
        {
            int max = 0;
            SlayerMaster m = Data.FindMaster(master);
            TaskRange own = m != null ? m.TaskFor(monster) : null;
            if (own != null && own.Max > 0) max = own.Max;
            else foreach (SlayerMaster x in Data.Masters) { TaskRange t = x.TaskFor(monster); if (t != null && t.Max > max) max = t.Max; }
            return max > 0 ? max * 3 / 2 + 20 : 1000;
        }

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
                    if (count > AmountLimit(name, e.Master)) count = 0;          // a misread amount: unknown instead
                    // The same assignment read again (still on screen, or a Rescan) changes nothing.
                    if (s.HasTask && !s.Complete && s.Monster == name && (count == 0 || count == s.Assigned)) return false;
                    s.Monster = name;
                    s.Assigned = count;
                    s.Left = count > 0 ? count : -1;
                    s.Complete = false;
                    s.CounterSeen = false;
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
            if (r.Minimised && !s.HasTask) return false;     // a bare number means nothing without a task
            bool changed = false;
            string name = s.Monster ?? "";
            if (!string.IsNullOrEmpty(r.Task))
            {
                MonsterInfo m = Data.FindMonster(r.Task);
                // A task name OCR couldn't place in the data is never used: not to replace a known task, and not
                // to start one either (0.1.2 showed "Abyssal Oerm" for a misread "Abyssal Demons").
                if (m != null) name = m.Name;
            }
            if (name.Length == 0) name = s.Monster ?? "";
            int left = r.Left;
            // Beyond any master's amount for this task (plus room for extensions): a misread (the clock's
            // 12:34 read as 1234), never a count or a total.
            if (name.Length > 0 && left > AmountLimit(name, s.Master)) left = -1;
            bool newTask = name.Length > 0 && (name != s.Monster || (s.Complete && !r.Minimised && left > 0 && !string.IsNullOrEmpty(r.Task)));
            if (newTask)
            {
                if (left < 0) return false;                     // a new task needs its count
                bool renamed = name != s.Monster;
                s.Monster = name;
                s.Assigned = left;
                s.Left = left;
                if (renamed || string.IsNullOrEmpty(s.Master)) s.Master = GuessMaster(name, left, null);
                s.Complete = false;
                s.CounterSeen = true;
                changed = true;
            }
            else if (name.Length > 0 && left >= 0)
            {
                if (!s.CounterSeen)
                {
                    // The first counter read of this task sets the count, and the total if it's bigger (the
                    // counter beats the popup: extensions).
                    if (left != s.Left) { s.Left = left; changed = true; }
                    if (left > s.Assigned) { s.Assigned = left; changed = true; }
                    s.CounterSeen = true;
                }
                else if (left > s.Assigned && left > s.Left + 3)
                {
                    // Bigger than the total and not a new task: held back, never made the new total.
                }
                else
                {
                    if (left != s.Left) { s.Left = left; changed = true; }
                    if (left > s.Assigned) { s.Assigned = left; changed = true; }   // at most a few past the last count
                }
            }
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
            s.Monster = ""; s.Master = ""; s.Assigned = 0; s.Left = -1; s.Complete = false; s.CounterSeen = false;
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
