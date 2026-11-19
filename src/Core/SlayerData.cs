using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KavrosSlayerHelper.Core
{
    public sealed class TaskRange
    {
        public string Monster;
        public int Min, Max, Weight, Slayer, Combat;
        public string Unlock = "";
    }

    public sealed class SlayerMaster
    {
        public string Id, Name, Quest = "", Location = "";
        public List<string> Aliases = new List<string>();
        public int Combat, Slayer, PointsTask, Points10, Points50;
        public List<TaskRange> Tasks = new List<TaskRange>();

        public TaskRange TaskFor(string monster)
        {
            return Tasks.FirstOrDefault(t => string.Equals(t.Monster, monster, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Points for completing task number <paramref name="streak"/> in a row (after Smoking Kills).
        /// Turael/Spria and the first four tasks of a streak give none.</summary>
        public int PointsFor(int streak)
        {
            if (streak <= 0) return PointsTask;
            if (streak % 50 == 0) return Points50;
            if (streak % 10 == 0) return Points10;
            return PointsTask;
        }
    }

    public sealed class LocationInfo
    {
        public string Name = "", Requirements = "", Notes = "", Tag = "";
        public string Monster = "";     // set when the spawns are of another monster that counts for the task
        public int Spawns;
        public bool Recommended;
    }

    public sealed class MonsterInfo
    {
        public string Name = "", Weakness = "", Style = "", Gear = "", Special = "";
        public int Slayer, Combat;
        public List<string> Aliases = new List<string>();
        public List<string> Eligible = new List<string>();
        public List<LocationInfo> Locations = new List<LocationInfo>();

        /// <summary>Locations in display order: one marked "recommended" first; otherwise the first location of the
        /// task's own monster that has no requirement and is outside the Wilderness (the wiki lists the main
        /// spawn first), then the rest in data order.</summary>
        public List<LocationInfo> OrderedLocations()
        {
            var list = new List<LocationInfo>(Locations);
            if (list.Count == 0) return list;
            LocationInfo best = list.FirstOrDefault(l => l.Recommended)
                ?? list.FirstOrDefault(l => string.IsNullOrEmpty(l.Monster) && string.IsNullOrEmpty(l.Requirements) && l.Name.IndexOf("Wilderness", StringComparison.OrdinalIgnoreCase) < 0)
                ?? list.FirstOrDefault(l => string.IsNullOrEmpty(l.Requirements) && l.Name.IndexOf("Wilderness", StringComparison.OrdinalIgnoreCase) < 0)
                ?? list[0];
            list.Remove(best);
            list.Insert(0, best);
            return list;
        }
    }

    public sealed class ChatPatterns
    {
        public List<string> Assign = new List<string>(), Progress = new List<string>(), Complete = new List<string>(), Kill = new List<string>();
    }

    /// <summary>The slayer data: the embedded data/slayer.json with the user's override file merged over it.</summary>
    public sealed class SlayerData
    {
        public List<SlayerMaster> Masters = new List<SlayerMaster>();
        public Dictionary<string, MonsterInfo> Monsters = new Dictionary<string, MonsterInfo>(StringComparer.OrdinalIgnoreCase);
        public ChatPatterns Chat = new ChatPatterns();
        public string Source = "";
        public string OverrideError;      // set when the override file could not be read
        public bool OverrideLoaded;

        // ---- loading -----------------------------------------------------------------------
        public static SlayerData Load(string baseJson, string overrideJson)
        {
            var root = (JObj)MiniJson.Parse(baseJson);
            string err = null;
            bool loaded = false;
            if (!string.IsNullOrWhiteSpace(overrideJson))
            {
                try
                {
                    var o = MiniJson.Parse(overrideJson) as JObj;
                    if (o == null) throw new FormatException("the override file must be a JSON object { ... }");
                    Merge(root, o);
                    loaded = true;
                }
                catch (Exception ex) { err = ex.Message; }
            }
            SlayerData d = FromJson(root);
            d.OverrideError = err;
            d.OverrideLoaded = loaded;
            return d;
        }

        public static SlayerData LoadFiles(string baseJson, string overridePath)
        {
            string text = null;
            try { if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath)) text = File.ReadAllText(overridePath, Encoding.UTF8); }
            catch (Exception ex) { var d0 = Load(baseJson, null); d0.OverrideError = ex.Message; return d0; }
            return Load(baseJson, text);
        }

        /// <summary>Merges <paramref name="over"/> into <paramref name="target"/>:
        /// objects merge key by key (keys match ignoring case), a null value deletes the key,
        /// arrays and plain values replace, and a key ending in "+" appends to the array of that name.</summary>
        public static void Merge(JObj target, JObj over)
        {
            foreach (var kv in over)
            {
                string key = kv.Key;
                bool append = key.EndsWith("+", StringComparison.Ordinal) && key.Length > 1;
                if (append) key = key.Substring(0, key.Length - 1);
                string existing = target.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                if (kv.Value == null && !append) { if (existing != null) target.Remove(existing); continue; }
                if (append)
                {
                    var add = kv.Value as List<object> ?? new List<object> { kv.Value };
                    var cur = existing != null ? target[existing] as List<object> : null;
                    if (cur == null) { cur = new List<object>(); target[existing ?? key] = cur; }
                    // New entries go first, so user patterns win over the built-in ones.
                    cur.InsertRange(0, add.Select(Clone));
                    continue;
                }
                var src = kv.Value as JObj;
                var dst = existing != null ? target[existing] as JObj : null;
                if (src != null && dst != null) Merge(dst, src);
                else target[existing ?? key] = Clone(kv.Value);
            }
        }

        static object Clone(object v)
        {
            var o = v as JObj;
            if (o != null) { var c = new JObj(); foreach (var kv in o) c.Add(new KeyValuePair<string, object>(kv.Key, Clone(kv.Value))); return c; }
            var a = v as List<object>;
            if (a != null) return a.Select(Clone).ToList();
            return v;
        }

        static List<string> Strings(List<object> a)
        {
            var r = new List<string>();
            if (a == null) return r;
            foreach (object o in a) if (o != null) r.Add(Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture));
            return r;
        }

        static SlayerData FromJson(JObj root)
        {
            var d = new SlayerData { Source = root.Str("source") };
            JObj masters = root.Obj("masters") ?? new JObj();
            foreach (var kv in masters)
            {
                var m = kv.Value as JObj;
                if (m == null) continue;
                var sm = new SlayerMaster
                {
                    Id = kv.Key, Name = m.Str("name", kv.Key), Quest = m.Str("quest"), Location = m.Str("location"),
                    Combat = m.Int("combat"), Slayer = m.Int("slayer"), Aliases = Strings(m.Arr("aliases")),
                };
                JObj pts = m.Obj("points");
                if (pts != null) { sm.PointsTask = pts.Int("task"); sm.Points10 = pts.Int("every10"); sm.Points50 = pts.Int("every50"); }
                JObj tasks = m.Obj("tasks") ?? new JObj();
                foreach (var t in tasks)
                {
                    var tj = t.Value as JObj;
                    if (tj == null) continue;
                    sm.Tasks.Add(new TaskRange
                    {
                        Monster = t.Key, Min = tj.Int("min"), Max = tj.Int("max"), Weight = tj.Int("weight"),
                        Slayer = tj.Int("slayer"), Combat = tj.Int("combat"), Unlock = tj.Str("unlock"),
                    });
                }
                d.Masters.Add(sm);
            }
            JObj monsters = root.Obj("monsters") ?? new JObj();
            foreach (var kv in monsters)
            {
                var m = kv.Value as JObj;
                if (m == null) continue;
                var mi = new MonsterInfo
                {
                    Name = m.Str("name", kv.Key), Slayer = m.Int("slayer"), Combat = m.Int("combat"),
                    Weakness = m.Str("weakness"), Style = m.Str("style"), Gear = m.Str("gear"), Special = m.Str("special"),
                    Aliases = Strings(m.Arr("aliases")), Eligible = Strings(m.Arr("eligible")),
                };
                foreach (object lo in m.Arr("locations") ?? new List<object>())
                {
                    var l = lo as JObj;
                    if (l == null) { if (lo is string) mi.Locations.Add(new LocationInfo { Name = (string)lo }); continue; }
                    mi.Locations.Add(new LocationInfo
                    {
                        Name = l.Str("name"), Requirements = l.Str("requirements"), Notes = l.Str("notes"),
                        Tag = l.Str("tag"), Spawns = l.Int("spawns"), Recommended = l.Bool("recommended"), Monster = l.Str("monster"),
                    });
                }
                d.Monsters[kv.Key] = mi;
            }
            JObj chat = root.Obj("chat");
            if (chat != null)
            {
                d.Chat.Assign = Strings(chat.Arr("assign"));
                d.Chat.Progress = Strings(chat.Arr("progress"));
                d.Chat.Complete = Strings(chat.Arr("complete"));
                d.Chat.Kill = Strings(chat.Arr("kill"));
            }
            return d;
        }

        // ---- lookups ---------------------------------------------------------------------------
        public SlayerMaster FindMaster(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string n = Norm(name);
            foreach (SlayerMaster m in Masters)
            {
                if (Norm(m.Name) == n || Norm(m.Id) == n) return m;
                foreach (string a in m.Aliases) if (Norm(a) == n) return m;
            }
            return null;
        }

        /// <summary>Finds the slayer category for a monster name read from chat ("abyssal demons",
        /// "Abyssal demon", OCR slips such as "abyssai demons"). Null when nothing is close.</summary>
        public MonsterInfo FindMonster(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string n = Norm(text);
            if (n.StartsWith("the ")) n = n.Substring(4);
            string ns = Singular(n);
            MonsterInfo best = null;
            int bestScore = int.MaxValue;
            foreach (MonsterInfo m in Monsters.Values)
            {
                foreach (string cand in Names(m))
                {
                    string c = Norm(cand);
                    if (c.Length == 0) continue;
                    int score;
                    if (c == n) score = 0;
                    else if (Singular(c) == ns) score = 1;
                    else
                    {
                        int dist = Levenshtein(Singular(c), ns);
                        int allowed = Math.Max(1, ns.Length / 7);
                        if (dist > allowed) continue;
                        score = 10 + dist;
                    }
                    // Prefer the category's own name over an eligible monster of a broader category.
                    if (cand != m.Name) score += 2;
                    if (score < bestScore) { bestScore = score; best = m; }
                }
            }
            return best;
        }

        static IEnumerable<string> Names(MonsterInfo m)
        {
            yield return m.Name;
            foreach (string a in m.Aliases) yield return a;
        }

        /// <summary>Masters that can assign <paramref name="monster"/>, and whose amount range fits <paramref name="amount"/> (when known).</summary>
        public List<SlayerMaster> MastersFor(string monster, int amount)
        {
            var r = new List<SlayerMaster>();
            foreach (SlayerMaster m in Masters)
            {
                TaskRange t = m.TaskFor(monster);
                if (t == null) continue;
                if (amount > 0 && t.Max > 0 && (amount < t.Min || amount > t.Max)) continue;
                r.Add(m);
            }
            return r;
        }

        public static string Norm(string s)
        {
            var sb = new StringBuilder();
            bool space = false;
            foreach (char ch in (s ?? "").ToLowerInvariant())
            {
                char c = ch == '\u2019' ? '\'' : ch;
                if (char.IsLetterOrDigit(c) || c == '\'' || c == '-') { if (space && sb.Length > 0) sb.Append(' '); sb.Append(c); space = false; }
                else space = true;
            }
            return sb.ToString();
        }

        public static string Singular(string s)
        {
            if (s.EndsWith("ies") && s.Length > 4) return s.Substring(0, s.Length - 3) + "y";
            if (s.EndsWith("ves") && s.Length > 4) return s.Substring(0, s.Length - 3) + "f";
            if (s.EndsWith("ches") || s.EndsWith("shes") || s.EndsWith("sses") || s.EndsWith("xes")) return s.Substring(0, s.Length - 2);
            if (s.EndsWith("men")) return s.Substring(0, s.Length - 3) + "man";
            if (s.EndsWith("s") && !s.EndsWith("ss") && s.Length > 3) return s.Substring(0, s.Length - 1);
            return s;
        }

        public static int Levenshtein(string a, string b)
        {
            if (a == b) return 0;
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                var t = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }
    }
}
