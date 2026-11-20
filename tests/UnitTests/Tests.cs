using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KavrosSlayerHelper.Core;

namespace KavrosSlayerHelper.Tests
{
    static partial class Program
    {
        static int passed, failed;
        static void Ok(bool c, string what) { if (c) passed++; else { failed++; Console.WriteLine("FAIL: " + what); } }
        static void Eq<T>(T want, T got, string what)
        {
            if (EqualityComparer<T>.Default.Equals(want, got)) passed++;
            else { failed++; Console.WriteLine("FAIL: " + what + ": want <" + want + "> got <" + got + ">"); }
        }

        static string BaseJson;
        static SlayerData Data(string over = null) { return SlayerData.Load(BaseJson, over); }

        static int Main()
        {
            BaseJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "slayer.json"));
            Run("data", DataTests);
            Run("merge", MergeTests);
            Run("chat", ChatTests);
            Run("countdown", CountdownTests);
            Run("manual", ManualTests);
            Run("complete", CompleteTests);
            Run("state", StateTests);
            Run("linediff", LineDiffTests);
            Run("view", ViewTests);
            Run("dialogue", DialogueTests);
            Run("counter", CounterTests);
            Run("counter-apply", CounterApplyTests);
            Run("placement", PlacementTests);
            Run("finder", FinderTests);
            Run("digits", DigitTests);
            Run("real-fixtures", RealFixtureTests);
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        static void Run(string name, Action a)
        {
            try { a(); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL: " + name + " threw " + ex); }
        }

        // ---- shipped data ---------------------------------------------------------------------
        static void DataTests()
        {
            var d = Data();
            Eq(12, d.Masters.Count, "masters");
            Ok(d.Monsters.Count >= 150, "monster categories: " + d.Monsters.Count);
            foreach (var id in new[] { "Turael", "Jacquelyn", "Vannaka", "The Raptor", "Mazchna", "Chaeldar", "Sumona", "Duradel", "Kuradal", "Morvran", "Laniakea", "Mandrith" })
                Ok(d.FindMaster(id) != null, "master " + id);
            Eq("Turael", d.FindMaster("Spria")?.Name, "alias Spria");
            Eq("Duradel", d.FindMaster("lapalok")?.Name, "alias Lapalok");
            var kur = d.FindMaster("Kuradal");
            Eq(110, kur.Combat, "Kuradal combat"); Eq(75, kur.Slayer, "Kuradal slayer");
            Eq(18, kur.PointsTask, "Kuradal points");
            var t = kur.TaskFor("Abyssal demons");
            Ok(t != null && t.Min > 0 && t.Max >= t.Min && t.Weight > 0, "Kuradal abyssal demons range");
            // Every task points at a monster entry.
            foreach (var m in d.Masters) foreach (var tr in m.Tasks)
                Ok(d.FindMonster(tr.Monster) != null, "monster entry for " + m.Name + "/" + tr.Monster);
            var ad = d.FindMonster("Abyssal demons");
            Eq(85, ad.Slayer, "abyssal demon slayer level");
            Eq("Slash", ad.Weakness, "abyssal weakness");
            Ok(ad.Locations.Count >= 3, "abyssal locations");
            Eq("Kuradal's Dungeon", ad.OrderedLocations()[0].Name, "recommended abyssal location");
            Ok(d.Chat.Assign.Count > 0 && d.Chat.Progress.Count > 0 && d.Chat.Complete.Count > 0, "default chat patterns");
            Eq(0, d.Chat.Kill.Count, "no kill patterns by default");
        }

        static void MergeTests()
        {
            var d = Data(@"{
              // comments and trailing commas are fine
              ""masters"": { ""KURADAL"": { ""points"": { ""task"": 20 } }, ""turael"": null },
              ""monsters"": {
                ""Abyssal demons"": { ""locations+"": [ { ""name"": ""Kavros Abyss"", ""recommended"": true, ""tag"": ""Kavros"" } ],
                                     ""weakness"": ""Stab"" },
                ""Dust devils"": { ""locations"": [ { ""name"": ""Only here"" } ] },
                ""Kavros wurms"": { ""slayer"": 50, ""weakness"": ""Fire"" },
              },
              ""chat"": { ""kill+"": [ ""you have slain an? (?<monster>.+)"" ] },
            }");
            Ok(d.OverrideLoaded && d.OverrideError == null, "override loaded: " + d.OverrideError);
            Eq(20, d.FindMaster("Kuradal").PointsTask, "nested merge, case-insensitive key");
            Eq(110, d.FindMaster("Kuradal").Combat, "untouched sibling kept");
            Ok(d.FindMaster("Turael") == null, "null deletes");
            var ad = d.FindMonster("abyssal demon");
            Eq("Kavros Abyss", ad.Locations[0].Name, "+ prepends");
            Ok(ad.Locations.Count > 3, "+ keeps the rest");
            Eq("Kavros Abyss", ad.OrderedLocations()[0].Name, "recommended flag wins");
            Eq("Stab", ad.Weakness, "scalar replaced");
            Eq(1, d.FindMonster("Dust devils").Locations.Count, "arrays replace");
            Eq(50, d.FindMonster("Kavros wurms").Slayer, "new monster added");
            Eq(1, d.Chat.Kill.Count, "kill+ appends a pattern");
            Ok(d.Chat.Assign.Count > 3, "default assign kept");

            var bad = Data("{ not json");
            Ok(bad.OverrideError != null, "bad override reported");
            Eq(12, bad.Masters.Count, "bad override falls back to the base data");
        }

        // ---- chat parsing ------------------------------------------------------------------------
        static ChatEvent P(ChatParser p, string line) { return p.Parse(line); }

        static void ChatTests()
        {
            var d = Data();
            var p = new ChatParser(d);
            Eq(0, p.Errors.Count, "default patterns compile");

            var e = P(p, "Kuradal: Excellent, you're doing great. Your new task is to kill 187 abyssal demons.");
            Eq(ChatKind.Assign, e?.Kind, "Duradel/Kuradal assign");
            Eq(187, e.Count, "count"); Eq("abyssal demons", e.Monster, "monster"); Eq("Kuradal", e.Master, "speaker");

            e = P(p, "You're doing okay, I suppose. Your new task is to kill 45 iron dragons.");
            Eq(ChatKind.Assign, e?.Kind, "Laniakea assign"); Eq(45, e.Count, "count 2"); Eq("iron dragons", e.Monster, "monster 2");

            e = P(p, "We'll start you off hunting cows. You'll need to kill 18 of them.");
            Eq(ChatKind.Assign, e?.Kind, "Turael first task"); Eq(18, e.Count, "count 3"); Eq("cows", e.Monster, "monster 3");

            e = P(p, "You need to kill 22 crawling hands.");
            Eq(ChatKind.Assign, e?.Kind, "short assign"); Eq(22, e.Count, "count 4");

            e = P(p, "Your new task is to kill abyssal demons.");
            Eq(ChatKind.Assign, e?.Kind, "amount-less assign"); Eq(-1, e.Count, "no amount"); Eq("abyssal demons", e.Monster, "monster 5");

            e = P(p, "You're currently assigned to kill abyssal demons; only 142 more to go.");
            Eq(ChatKind.Progress, e?.Kind, "gem progress"); Eq(142, e.Left, "left"); Eq("abyssal demons", e.Monster, "progress monster");

            e = P(p, "You still need to kill 120 monsters to complete your current Slayer assignment.");
            Eq(ChatKind.Progress, e?.Kind, "bracelet progress"); Eq(120, e.Left, "bracelet left");

            e = P(p, "You're still hunting abyssal demons; come back when you've finished your task.");
            Eq(ChatKind.Progress, e?.Kind, "still hunting"); Eq(-1, e.Left, "still hunting has no count");

            e = P(p, "You've completed 47 tasks in a row and gain 18 points. Return to a Slayer Master.");
            Eq(ChatKind.Complete, e?.Kind, "complete"); Eq(47, e.Streak, "streak"); Eq(18, e.Points, "points");

            e = P(p, "You have completed 50 tasks in a row and received 270 slayer points; you now have a total of 1,510 points.");
            Eq(ChatKind.Complete, e?.Kind, "complete variant"); Eq(270, e.Points, "points 2"); Eq(1510, e.Total, "total");

            e = P(p, "You've completed your special assignment from the Raptor and receive 3000 slayer experience and 20 slayer points.");
            Eq(ChatKind.Complete, e?.Kind, "raptor special"); Eq(20, e.Points, "raptor points");

            e = P(p, "You need something new to hunt.");
            Eq(ChatKind.Complete, e?.Kind, "something new to hunt");

            // OCR noise: curly quotes, extra spaces, l/O in numbers, timestamp prefix.
            e = P(p, "[14:02]  You\u2019re currently assigned to kill  abyssal demons ;  only l4O more to go.");
            Eq(ChatKind.Progress, e?.Kind, "noisy progress"); Eq(140, e.Left, "noisy number");

            Ok(P(p, "Welcome to Kavros.") == null, "unrelated line");
            Ok(P(p, "Iron dragon: I'm going to kill you") == null, "chatter");

            var custom = new ChatParser(Data(@"{ ""chat"": { ""kill+"": [""you have slain an? (?<monster>[a-z ]+)"", ""(""] } }"));
            Eq(1, custom.Errors.Count, "a bad user pattern is reported, not fatal");
            Eq(ChatKind.Kill, custom.Parse("You have slain an abyssal demon.")?.Kind, "configured kill pattern");
        }

        static SlayerTracker NewTracker(SlayerData d) { return new SlayerTracker(d, new SlayerState()); }

        static void CountdownTests()
        {
            var d = Data(@"{ ""chat"": { ""kill+"": [""you have slain an? (?<monster>[a-z ]+)"", ""task kills left: (?<left>\\d+)""] } }");
            var p = new ChatParser(d); var t = NewTracker(d);
            Ok(t.Apply(p.Parse("Kuradal: Your new task is to kill 187 abyssal demons.")), "assign applies");
            Eq("Abyssal demons", t.State.Monster, "canonical name"); Eq(187, t.State.Assigned, "assigned"); Eq(187, t.State.Left, "left = assigned");
            Eq("Kuradal", t.State.Master, "master from speaker");
            Ok(!t.Apply(p.Parse("Kuradal: Your new task is to kill 187 abyssal demons.")), "same assignment twice is ignored");
            Ok(t.Apply(p.Parse("You have slain an abyssal demon.")), "kill counts down"); Eq(186, t.State.Left, "186");
            t.Apply(p.Parse("You have slain an abyssal demon.")); Eq(185, t.State.Left, "185");
            Ok(t.Apply(p.Parse("Task kills left: 150")), "kill with explicit count"); Eq(150, t.State.Left, "150");
            Ok(t.Apply(p.Parse("You're currently assigned to kill abyssal demons; only 142 more to go.")), "progress");
            Eq(142, t.State.Left, "142"); Eq(187, t.State.Assigned, "assigned kept");
            Ok(!t.Apply(p.Parse("You're currently assigned to kill abyssal demons; only 142 more to go.")), "same progress twice ignored");

            // No amount in the assignment: unknown until set by hand or by a progress line.
            var t2 = NewTracker(d);
            t2.Apply(p.Parse("Your new task is to kill abyssal demons."));
            Eq(0, t2.State.Assigned, "unknown amount"); Eq(-1, t2.State.Left, "unknown left");
            Ok(!t2.Apply(p.Parse("You have slain an abyssal demon.")), "kill with unknown left does nothing");
            t2.Apply(p.Parse("You still need to kill 120 monsters to complete your current Slayer assignment."));
            Eq(120, t2.State.Left, "bracelet sets left"); Eq(120, t2.State.Assigned, "and assigned when unknown");

            // Master guessed only when unique.
            var t3 = NewTracker(d);
            t3.Apply(p.Parse("We'll start you off hunting cows. You'll need to kill 18 of them."));
            Ok(t3.State.Master == "" || t3.State.Master == "Turael", "guess is unique or empty: " + t3.State.Master);
        }

        static void ManualTests()
        {
            var d = Data(); var p = new ChatParser(d); var t = NewTracker(d);
            Ok(!t.SetLeft(10), "no task: nothing to set");
            t.Apply(p.Parse("Your new task is to kill abyssal demons."));
            Ok(t.SetLeft(150), "set count"); Eq(150, t.State.Left, "150"); Eq(150, t.State.Assigned, "assigned from manual"); Eq("manual", t.State.LastSource, "source");
            Ok(t.Adjust(-1), "minus"); Eq(149, t.State.Left, "149"); Eq(150, t.State.Assigned, "assigned kept");
            Ok(t.Adjust(+1), "plus"); Eq(150, t.State.Left, "150 again");
            Ok(t.Adjust(+5), "plus over assigned"); Eq(155, t.State.Assigned, "assigned grows");
            t.SetLeft(1); t.Adjust(-1); Eq(0, t.State.Left, "down to 0");
            Ok(!t.Adjust(-1), "never below 0"); Eq(0, t.State.Left, "still 0");
            Ok(t.Clear(), "clear"); Ok(!t.State.HasTask, "cleared");
        }

        static void CompleteTests()
        {
            var d = Data(); var p = new ChatParser(d);
            var t = new SlayerTracker(d, new SlayerState { Streak = 46, Points = 1222 });
            t.Apply(p.Parse("Kuradal: Your new task is to kill 187 abyssal demons."));
            Ok(t.Apply(p.Parse("You've completed 47 tasks in a row and gain 18 points.")), "complete");
            Ok(t.State.Complete, "complete flag"); Eq(0, t.State.Left, "left 0"); Eq(47, t.State.Streak, "streak"); Eq(1240, t.State.Points, "points added");
            Ok(!t.Apply(p.Parse("You've completed 47 tasks in a row and gain 18 points.")), "same completion read again is ignored");
            Eq(1240, t.State.Points, "no double count");
            // Without a points number, the master's table is used (Kuradal 18; the 50th task 270).
            var t2 = new SlayerTracker(d, new SlayerState { Streak = 49, Points = 0, Master = "Kuradal", Monster = "Abyssal demons", Assigned = 10, Left = 1 });
            t2.Apply(p.Parse("You have completed your task."));
            Eq(50, t2.State.Streak, "streak +1"); Eq(270, t2.State.LastPoints, "50th task points"); Eq(270, t2.State.Points, "points");
            Eq(18, d.FindMaster("Kuradal").PointsFor(51), "normal task"); Eq(90, d.FindMaster("Kuradal").PointsFor(60), "10th task");
            // Total overrides.
            var t3 = new SlayerTracker(d, new SlayerState { Streak = 49, Points = 5, Monster = "Abyssal demons", Left = 3 });
            t3.Apply(p.Parse("You have completed 50 tasks in a row and received 270 slayer points; you now have a total of 1,510 points."));
            Eq(1510, t3.State.Points, "total wins");
            // A new assignment after completion starts a new task.
            Ok(t.Apply(p.Parse("Kuradal: Your new task is to kill 187 abyssal demons.")), "same monster again after completion = new task");
            Eq(187, t.State.Left, "fresh count");

            Eq("3 to a 50-task bonus", SlayerTracker.StreakHint(47), "hint 50");
            Eq("7 to a 10-task bonus", SlayerTracker.StreakHint(13), "hint 10");
        }

        static void StateTests()
        {
            var s = new SlayerState { Monster = "Abyssal demons", Master = "Kuradal", Assigned = 187, Left = 142, Streak = 47, Points = 1240, LastPoints = 18, LastChat = "only 142 more to go", LastSource = "chat" };
            string path = Path.Combine(Path.GetTempPath(), "ksh-state-test.json");
            s.Save(path);
            var r = SlayerState.Load(path);
            Eq("Abyssal demons", r.Monster, "monster"); Eq(142, r.Left, "left"); Eq(187, r.Assigned, "assigned"); Eq(47, r.Streak, "streak"); Eq(1240, r.Points, "points");
            File.WriteAllText(path, "garbage");
            var g = SlayerState.Load(path);
            Ok(g != null && !g.HasTask, "garbage state loads empty");
            File.Delete(path);
        }

        static void LineDiffTests()
        {
            var a = new List<string> { "one", "two", "three" };
            var b = new List<string> { "two", "three", "four", "five" };
            Ok(LineDiff.NewLines(a, b).SequenceEqual(new[] { "four", "five" }), "scroll by two");
            Eq(0, LineDiff.NewLines(a, a).Count, "no change");
            Ok(LineDiff.NewLines(new List<string>(), b).Count == 4, "first read: all");
            var noisy = new List<string> { "two", "thre3", "four" };
            Ok(LineDiff.NewLines(a, noisy).SequenceEqual(new[] { "four" }), "fuzzy overlap");
            var unrelated = new List<string> { "x1 aaaa", "y2 bbbb", "z3 cccc" };
            Eq(0, LineDiff.NewLines(a, unrelated).Count, "no overlap: fresh baseline");
        }

        static void ViewTests()
        {
            var d = Data();
            var t = new SlayerTracker(d, new SlayerState { Monster = "Abyssal demons", Master = "Kuradal", Assigned = 187, Left = 142, Streak = 47, Points = 1240, LastChat = "only 142 more to go", LastSource = "chat" });
            var v = PanelView.Build(t, "");
            Eq("Level 85 Slayer \u00b7 Combat 98", v.Kicker, "kicker");
            Eq("142", v.LeftText, "left"); Eq("left of 187", v.OfText, "of"); Eq("45 killed", v.KilledText, "killed"); Eq("24%", v.PercentText, "percent");
            Eq("Kuradal", v.Master, "master");
            Eq("Read from chat: \u201conly 142 more to go\u201d", v.ChatLine, "chat line");
            Eq("Recommended", v.Locations[0].Tag, "first location recommended");
            Eq("47 tasks", v.StreakValue, "streak"); Eq("1,240", v.PointsValue, "points"); Eq("Kuradal awards 18 per task", v.PointsSub, "points sub");
            Eq("None needed", v.Special, "special");
            t.State.Left = -1; t.State.Assigned = 0;
            v = PanelView.Build(t, "");
            Ok(v.OfText.Contains("click to set"), "unknown count invites a manual set: " + v.OfText);
            var empty = PanelView.Build(new SlayerTracker(d, new SlayerState()), "Reading chat\u2026");
            Ok(!empty.HasTask, "empty view");
        }
    }
}
