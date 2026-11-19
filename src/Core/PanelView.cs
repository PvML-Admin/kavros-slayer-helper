using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KavrosSlayerHelper.Core
{
    public sealed class LocationView { public string Name = "", Tag = "", Hint = ""; }

    /// <summary>Everything the panel shows, as text, worked out from the state and the data
    /// (kept free of WinForms so the tests can check it).</summary>
    public sealed class PanelView
    {
        public bool HasTask, Complete, CountKnown, AmountKnown;
        public string Kicker = "", Title = "", LeftText = "", OfText = "", KilledText = "", PercentText = "";
        public float Progress;
        public string Master = "";          // "" = unknown
        public string ChatLine = "";
        public List<LocationView> Locations = new List<LocationView>();
        public string Weakness = "", Style = "", Gear = "", Special = "";
        public bool SpecialOk;
        public string StreakValue = "", StreakSub = "", PointsValue = "", PointsSub = "";
        public string CompactPlace = "";
        public string Footnote = "From the RuneScape Wiki. Kavros locations can be edited later.";

        static readonly CultureInfo Num = CultureInfo.GetCultureInfo("en-GB");

        public static string N(int n) { return n.ToString("#,0", Num); }

        public static PanelView Build(SlayerTracker t, string readerStatus)
        {
            SlayerState s = t.State;
            var v = new PanelView { HasTask = s.HasTask, Complete = s.Complete };
            SlayerMaster master = t.Master;
            v.StreakValue = s.Streak == 1 ? "1 task" : N(s.Streak) + " tasks";
            v.StreakSub = SlayerTracker.StreakHint(s.Streak);
            v.PointsValue = N(s.Points);
            if (master != null && master.PointsTask > 0) v.PointsSub = master.Name + " awards " + master.PointsTask + " per task";
            else if (master != null) v.PointsSub = master.Name + " gives no points";
            else v.PointsSub = s.LastPoints > 0 ? "Last task gave " + N(s.LastPoints) : "Points depend on the master";

            if (!s.HasTask)
            {
                v.Kicker = "No task";
                v.Title = "Waiting for a task";
                v.ChatLine = string.IsNullOrEmpty(readerStatus) ? "Get a task or check your enchanted gem." : readerStatus;
                return v;
            }

            MonsterInfo m = t.Monster;
            v.Title = s.Monster;
            var kick = new List<string>();
            if (m != null && m.Slayer > 0) kick.Add("Level " + m.Slayer + " Slayer");
            if (m != null && m.Combat > 0) kick.Add("Combat " + m.Combat);
            v.Kicker = kick.Count > 0 ? string.Join(" \u00b7 ", kick) : "Slayer task";

            v.CountKnown = s.Left >= 0;
            v.AmountKnown = s.Assigned > 0;
            v.LeftText = v.CountKnown ? N(s.Left) : "?";
            if (s.Complete) v.OfText = v.AmountKnown ? "left of " + N(s.Assigned) + " \u00b7 done" : "left \u00b7 done";
            else if (!v.CountKnown) v.OfText = "left \u00b7 click to set";
            else v.OfText = v.AmountKnown ? "left of " + N(s.Assigned) : "left";
            if (v.AmountKnown && v.CountKnown)
            {
                int killed = Math.Max(0, s.Assigned - s.Left);
                v.Progress = Math.Max(0f, Math.Min(1f, killed / (float)s.Assigned));
                v.KilledText = N(killed) + " killed";
                v.PercentText = ((int)Math.Floor(v.Progress * 100)).ToString(CultureInfo.InvariantCulture) + "%";
            }
            else { v.KilledText = "\u2013 killed"; v.PercentText = ""; }
            v.Master = s.Master ?? "";
            if (s.LastSource == "manual") v.ChatLine = "Count set by hand";
            else if (!string.IsNullOrEmpty(s.LastChat)) v.ChatLine = "Read from chat: \u201c" + s.LastChat + "\u201d";
            else v.ChatLine = string.IsNullOrEmpty(readerStatus) ? "" : readerStatus;
            if (s.Complete) v.ChatLine = string.IsNullOrEmpty(s.LastChat) ? "Task complete; return to a Slayer master" : v.ChatLine;

            if (m != null)
            {
                List<LocationInfo> locs = m.OrderedLocations();
                for (int i = 0; i < locs.Count && v.Locations.Count < 3; i++)
                {
                    LocationInfo l = locs[i];
                    var lv = new LocationView { Name = l.Name };
                    string req = ShortReq(l.Requirements);
                    if (!string.IsNullOrEmpty(l.Tag)) lv.Tag = l.Tag;
                    else if (i == 0) lv.Tag = "Recommended";
                    else lv.Tag = req;
                    var hint = new List<string>();
                    string spawns = l.Spawns > 0 ? (l.Spawns == 1 ? "1 spawn" : l.Spawns + " spawns") : "";
                    if (!string.IsNullOrEmpty(l.Monster)) spawns = (spawns.Length > 0 ? spawns + " of " : "") + l.Monster;
                    if (spawns.Length > 0) hint.Add(spawns);
                    if (!string.IsNullOrEmpty(l.Notes)) hint.Add(l.Notes);
                    if (!string.IsNullOrEmpty(req) && lv.Tag != req) hint.Add(l.Requirements);
                    lv.Hint = string.Join(" \u00b7 ", hint);
                    v.Locations.Add(lv);
                }
                v.Weakness = string.IsNullOrEmpty(m.Weakness) ? "None" : m.Weakness;
                v.Style = string.IsNullOrEmpty(m.Style) ? "Any style" : m.Style;
                v.Gear = string.IsNullOrEmpty(m.Gear) ? "Any combat style" : m.Gear;
                v.SpecialOk = string.IsNullOrEmpty(m.Special);
                v.Special = v.SpecialOk ? "None needed" : m.Special;
                if (v.Locations.Count > 0) v.CompactPlace = v.Locations[0].Name;
            }
            else
            {
                v.Weakness = "Unknown"; v.Style = "\u2013"; v.Gear = "\u2013"; v.Special = "Not in the slayer data"; v.SpecialOk = false;
            }
            if (string.IsNullOrEmpty(v.CompactPlace)) v.CompactPlace = v.Master.Length > 0 ? v.Master : "";
            return v;
        }

        /// <summary>A requirement short enough for the gold tag ("71 Agility"); the full text goes in the hint.</summary>
        public static string ShortReq(string r)
        {
            if (string.IsNullOrWhiteSpace(r)) return "";
            string first = r.Split(new[] { ',', ';', '(' }, 2)[0].Trim();
            if (first.Length > 26) return "Requirements";
            return first;
        }
    }
}
