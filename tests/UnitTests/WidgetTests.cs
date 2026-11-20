using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KavrosSlayerHelper.Core;

namespace KavrosSlayerHelper.Tests
{
    static partial class Program
    {
        static TextLine L(string t, float y, float x = 10, float w = 120, float h = 14) { return new TextLine(t, x, y, w, h); }
        static string Fixture(string name) { return Path.Combine(AppContext.BaseDirectory, "fixtures", name); }

        // ---- the master's dialogue box ---------------------------------------------------------------
        static void DialogueTests()
        {
            var d = Data();
            var p = new ChatParser(d);
            // Exactly the user's popup: LANIAKEA, two wrapped lines, the continue button.
            var r = DialogueParser.Parse(new[] {
                L("LANIAKEA", 8, 200, 90, 16),
                L("You're doing okay, I suppose. Your new task is to", 40, 120, 380),
                L("kill 125 Dragons.", 62, 120, 120),
                L("Continue", 112, 420, 70) }, p, d);
            Eq("Laniakea", r.Master, "speaker is a master");
            Eq("You're doing okay, I suppose. Your new task is to kill 125 Dragons.", r.Text, "wrapped lines joined");
            Eq(ChatKind.Assign, r.Event?.Kind, "assignment");
            Eq(125, r.Event.Count, "amount"); Eq("Dragons", r.Event.Monster, "monster"); Eq("Laniakea", r.Event.Master, "event master");

            // OCR noise: curly quote, wrapped mid-sentence, number on its own line, title mixed case.
            r = DialogueParser.Parse(new[] { L("Laniakea", 8), L("You\u2019re doing okay, I suppose. Your new task is to kill", 40), L("125 Dragons .", 62) }, p, d);
            Eq(125, r.Event?.Count, "noisy wrap count"); Eq("Dragons", r.Event?.Monster, "noisy wrap monster");

            r = DialogueParser.Parse(new[] { L("KURADAL", 8), L("Your task is to kill 87 abyssal demons.", 40) }, p, d);
            Eq(ChatKind.Assign, r.Event?.Kind, "'Your task is to kill'"); Eq(87, r.Event.Count, "count 2"); Eq("Kuradal", r.Master, "Kuradal");

            r = DialogueParser.Parse(new[] { L("LANIAKEA", 8), L("You're still hunting Dragons; you need to kill 40 more Dragons.", 40) }, p, d);
            Eq(ChatKind.Progress, r.Event?.Kind, "'kill N more' is progress, not a new task"); Eq(40, r.Event?.Left, "kill N more");

            r = DialogueParser.Parse(new[] { L("HANS", 8), L("Hello there, adventurer.", 40) }, p, d);
            Ok(r.Event == null && r.Master == null, "other dialogue ignored");

            // Override patterns still work on the dialogue text.
            var custom = new ChatParser(Data(@"{ ""chat"": { ""assign+"": [""off you go: (?<count>\\d+) x (?<monster>[a-z ]+)""] } }"));
            r = DialogueParser.Parse(new[] { L("MANDRITH", 8), L("Off you go: 30 x black demons", 40) }, custom, d);
            Eq(30, r.Event?.Count, "override pattern on dialogue"); Eq("Mandrith", r.Event?.Master, "override master");

            // The tracker: Dragons from the popup resolves to the wiki task.
            var t = NewTracker(d);
            var e = DialogueParser.Parse(new[] { L("LANIAKEA", 8), L("Your new task is to kill 125 Dragons.", 40) }, p, d).Event;
            Ok(t.Apply(e, "popup"), "popup applied");
            Eq("Dragons", t.State.Monster, "category"); Eq(125, t.State.Assigned, "assigned"); Eq(125, t.State.Left, "left");
            Eq("Laniakea", t.State.Master, "master from the popup"); Eq("popup", t.State.LastSource, "source");
            Ok(t.Monster != null && t.Monster.Locations.Count > 0, "Dragons has wiki data");
            Ok(!t.Apply(e, "popup"), "same popup again changes nothing");
            var v = PanelView.Build(t, "");
            Ok(v.ChatLine.StartsWith("From Laniakea: \u201c"), "popup chat line: " + v.ChatLine);
            Eq("Dragons", d.FindMonster("dragon")?.Name, "singular resolves");
            Eq("Dragons", d.FindMonster("DRAGONS")?.Name, "caps resolve");
        }

        // ---- the slayer counter ---------------------------------------------------------------------
        static void CounterTests()
        {
            var r = CounterParser.Parse(new[] { L("135", 4, 60, 40, 24), L("Dragons", 34, 50, 60), L("Slayer points:", 60, 8, 90), L("6,257", 60, 130, 36), L("Task streak:", 80, 8, 80), L("291", 80, 140, 24) });
            Ok(r.Valid, "valid");
            Eq(135, r.Left, "left"); Eq("Dragons", r.Task, "task"); Eq(6257, r.Points, "points (label and value read apart)"); Eq(291, r.Streak, "streak");
            Ok(r.NumberLine != null && r.NumberLine.Text == "135", "number line kept for digit learning");

            r = CounterParser.Parse(new[] { L("135", 4), L("Dragons", 34), L("Slayer points: 6,257", 60), L("Task streak: 291", 80) });
            Eq(6257, r.Points, "one-line points"); Eq(291, r.Streak, "one-line streak");

            // OCR noise: O for 0, l for 1, a stray period, "polnts".
            r = CounterParser.Parse(new[] { L("l35", 4), L("Dragons", 34), L("Slayer polnts: 6,257", 60), L("Task streak : 29l.", 80) });
            Eq(135, r.Left, "l35"); Eq(6257, r.Points, "polnts"); Eq(291, r.Streak, "29l");

            r = CounterParser.Parse(new[] { L("Abyssal demons", 34), L("Slayer points: 1,240", 60) });
            Ok(!r.Valid, "no number: not a counter");
            r = CounterParser.Parse(new[] { L("135 Dragons", 4), L("Task streak: 291", 80) });
            Eq(135, r.Left, "number and task on one line"); Eq("Dragons", r.Task, "task on one line");
            r = CounterParser.Parse(new[] { L("Hello", 4), L("12", 20) });
            Ok(!r.Valid, "a lone number is not a counter");

            // The filter: a change must be read the same way on two frames in a row.
            var f = new CounterFilter();
            var none = new SlayerState();
            var a = new CounterRead { Left = 135, Task = "Dragons", Points = 6257, Streak = 291 };
            Ok(f.Offer(a, none) == null && f.NeedsConfirm, "first reading waits for a second frame");
            Ok(f.Offer(a, none) != null, "two frames agree: adopted");
            var st = new SlayerState { Monster = "Dragons", Left = 135, Assigned = 135 };
            var b = new CounterRead { Left = 134, Task = "Dragons", Points = 6257, Streak = 291 };
            var c = new CounterRead { Left = 133, Task = "Dragons", Points = 6257, Streak = 291 };
            Ok(f.Offer(b, st) == null && f.NeedsConfirm, "a change waits for a second read");
            Ok(f.Offer(c, st) == null, "a different second read waits again");
            Ok(f.Offer(c, st) != null && !f.NeedsConfirm, "confirmed");
            Ok(f.Offer(c, st) == null, "unchanged: nothing to apply");
        }

        static void CounterApplyTests()
        {
            var d = Data();
            var t = NewTracker(d);
            var r = new CounterRead { Left = 135, Task = "Dragons", Points = 6257, Streak = 291 };
            Ok(t.ApplyCounter(r), "counter applied");
            Eq("Dragons", t.State.Monster, "task"); Eq(135, t.State.Left, "left"); Eq(135, t.State.Assigned, "assigned = left when unknown");
            Eq(6257, t.State.Points, "points"); Eq(291, t.State.Streak, "streak"); Eq("counter", t.State.LastSource, "source");
            var v = PanelView.Build(t, "");
            Eq("291 tasks", v.StreakValue, "streak box"); Eq("6,257", v.PointsValue, "points box");
            Eq("Read from the slayer counter", v.ChatLine, "counter chat line");
            Ok(!t.ApplyCounter(r), "same reading: no change");

            // Assigned 125 by the popup, the counter says 135: trust the counter.
            t = NewTracker(d);
            t.Apply(new ChatParser(d).Parse("Your new task is to kill 125 Dragons."), "popup");
            Ok(t.ApplyCounter(r), "counter over popup");
            Eq(135, t.State.Left, "counter wins"); Eq(135, t.State.Assigned, "assigned grows to the counter");
            Ok(t.ApplyCounter(new CounterRead { Left = 120, Task = "Dragons", Points = 6257, Streak = 291 }), "kills");
            Eq(120, t.State.Left, "counting down"); Eq(135, t.State.Assigned, "assigned kept");
            Eq("15 killed", PanelView.Build(t, "").KilledText, "killed");

            // A garbled task name never replaces a known task.
            t.ApplyCounter(new CounterRead { Left = 119, Task = "Dxqvons", Points = 6257, Streak = 291 });
            Eq("Dragons", t.State.Monster, "misread task ignored"); Eq(119, t.State.Left, "count still applied");
            // Singular / different case resolves.
            t.ApplyCounter(new CounterRead { Left = 50, Task = "ABYSSAL DEMON", Points = 6275, Streak = 292 });
            Eq("Abyssal demons", t.State.Monster, "new task from the counter"); Eq(50, t.State.Assigned, "new task assigned");
            Ok(t.ApplyCounter(new CounterRead { Left = 0, Task = "Abyssal demons", Points = 6275, Streak = 292 }), "done");
            Ok(t.State.Complete, "zero left = complete");
            Eq("Task complete; return to a Slayer master", PanelView.Build(t, "").ChatLine, "complete line");

            var empty = PanelView.Build(NewTracker(d), "");
            Eq("Kills left are read from the slayer counter.", empty.ChatLine, "empty-state wording");
        }

        static void PlacementTests()
        {
            int x, y;
            var area = new Box(100, 50, 1280, 720);
            Placement.Clamp(500, 300, 320, 400, area, out x, out y); Eq(500, x, "inside x"); Eq(300, y, "inside y");
            Placement.Clamp(1300, 600, 320, 400, area, out x, out y); Eq(100 + 1280 - 320, x, "right edge"); Eq(50 + 720 - 400, y, "bottom edge");
            Placement.Clamp(-50, -50, 320, 400, area, out x, out y); Eq(100, x, "left edge"); Eq(50, y, "top edge");
            Placement.Clamp(500, 300, 2000, 900, area, out x, out y); Eq(100, x, "too wide sticks left"); Eq(50, y, "too tall sticks top");
            int ox, oy;
            Placement.ToOffset(400, 250, area, 1.5f, out ox, out oy); Eq(200, ox, "offset x at 150%"); Eq(133, oy, "offset y at 150%");
        }

        // ---- detection on fixture images -------------------------------------------------------------
        static void FinderTests()
        {
            RgbImage a = Png.Load(Fixture("frame-counter-dialogue.png"));
            var counter = new Box(1060, 120, 170, 112);
            var dialogue = new Box(360, 520, 560, 140);
            List<Box> c = WidgetFinder.FindCounterCandidates(a);
            Ok(c.Count >= 1, "counter candidates: " + c.Count);
            if (c.Count > 0)
            {
                Box got = c[0];
                Ok(got.Intersect(counter).W * got.Intersect(counter).H >= counter.W * counter.H * 0.75, "best candidate covers the counter: " + got);
                Ok(got.W < 600 && got.H < 260, "candidate is small: " + got);
            }
            Ok(!c.Any(q => q.Intersect(new Box(40, 640, 260, 22)).W > 0 && q.Intersect(counter).W == 0), "single yellow row (item name) is not a counter");
            Box d = WidgetFinder.FindDialogue(a);
            Ok(!d.IsEmpty, "dialogue found");
            Ok(d.Intersect(dialogue).W * d.Intersect(dialogue).H >= dialogue.W * dialogue.H * 0.8, "dialogue box located: " + d);
            Ok(d.W <= dialogue.W + 24 && d.H <= dialogue.H + 24, "dialogue box tight: " + d);

            RgbImage e = Png.Load(Fixture("frame-empty.png"));
            Ok(WidgetFinder.FindDialogue(e).IsEmpty, "no dialogue on an empty frame");
            Eq(0, WidgetFinder.FindCounterCandidates(e).Count, "no counter on an empty frame");

            var crop = a.Crop(counter);
            Eq(WidgetFinder.Fingerprint(a, counter), WidgetFinder.Fingerprint(crop, new Box(0, 0, crop.W, crop.H)), "fingerprint is position independent");
            RgbImage b = Png.Load(Fixture("frame-counter-131.png"));
            Ok(WidgetFinder.Fingerprint(a, counter) != WidgetFinder.Fingerprint(b, counter), "a kill changes the fingerprint");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 5; i++) { WidgetFinder.FindCounterCandidates(a); WidgetFinder.FindDialogue(a); }
            Console.WriteLine("  finder: " + (sw.ElapsedMilliseconds / 5) + " ms per 1280x720 frame (both widgets)");
        }

        static void DigitTests()
        {
            RgbImage a = Png.Load(Fixture("frame-counter-dialogue.png"));
            RgbImage b = Png.Load(Fixture("frame-counter-131.png"));
            RgbImage c = Png.Load(Fixture("frame-counter-134.png"));
            var row = new Box(1060, 120, 170, 38);   // the big number's row
            Eq(3, DigitReader.Glyphs(a, row).Count, "three glyphs in 135");
            var dr = new DigitReader();
            Eq(-1, dr.Read(a, row), "nothing learned: no read");
            Ok(dr.Learn(a, row, "135"), "learned from an OCR read of 135");
            Eq(135, dr.Read(a, row), "reads 135 back");
            Eq(131, dr.Read(b, row), "reads 131 (known digits)");
            Eq(-1, dr.Read(c, row), "134 has an unknown digit: falls back to OCR");
            Ok(!dr.Learn(a, row, "1350"), "mismatched glyph count is not learned");
        }

        /// <summary>The user's own screenshots, when copied to tests/fixtures/real/ (popup.png, counter.png):
        /// detection must find both widgets in them.</summary>
        static void RealFixtureTests()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "real");
            string popup = Path.Combine(dir, "popup.png"), counter = Path.Combine(dir, "counter.png");
            if (!File.Exists(popup) && !File.Exists(counter)) { Console.WriteLine("  real fixtures: none in tests/fixtures/real (skipped)"); return; }
            if (File.Exists(popup))
            {
                Box d = WidgetFinder.FindDialogue(Png.Load(popup));
                Ok(!d.IsEmpty, "real popup: dialogue found");
                Console.WriteLine("  real popup: dialogue at " + d);
            }
            if (File.Exists(counter))
            {
                var img = Png.Load(counter);
                List<Box> c = WidgetFinder.FindCounterCandidates(img);
                Ok(c.Count > 0, "real counter: candidate found");
                Console.WriteLine("  real counter: " + string.Join(", ", c) + " in " + img.W + "x" + img.H);
            }
        }

        // ---- the minimised counter ------------------------------------------------------------------
        static void MinimisedTests()
        {
            // The user's expanded counter as Windows OCR splits it (labels and values apart, skull junk).
            var x = CounterParser.Parse(new[] { L("249 @", 14, 40, 44, 22), L("Soul devourers", 40, 30, 110), L("Slayer points:", 62, 6, 90), L("6,257", 62, 150, 40), L("Task streak:", 80, 6, 80), L("291", 80, 165, 24) });
            Eq(249, x.Left, "real layout: left"); Eq("Soul devourers", x.Task, "real layout: task"); Eq(6257, x.Points, "real layout: points"); Eq(291, x.Streak, "real layout: streak");
            Ok(!x.Minimised, "real layout: expanded");
            var tt = NewTracker(Data());
            Ok(tt.ApplyCounter(x), "real layout applied"); Eq("Soul devourers", tt.State.Monster, "soul devourers in the data");

            // Parsing: a lone number (the skull icon may read as a stray symbol) is the minimised counter.
            var r = CounterParser.Parse(new[] { L("177", 6, 20, 30, 14) });
            Ok(r.Valid && r.Minimised, "lone number: minimised counter"); Eq(177, r.Left, "minimised left");
            Ok(r.NumberLine != null, "number line kept for digit learning");
            r = CounterParser.Parse(new[] { L("177 &", 6, 20, 40, 14) });
            Ok(r.Valid && r.Minimised && r.Left == 177, "icon junk beside the number");
            r = CounterParser.Parse(new[] { L("1,177", 6) });
            Eq(1177, r.Left, "minimised with a thousands comma");
            r = CounterParser.Parse(new[] { L("135", 4), L("Dragons", 34), L("Slayer points: 6,257", 60), L("Task streak: 291", 80) });
            Ok(!r.Minimised, "expanded counter is not minimised");
            r = CounterParser.Parse(new[] { L("Hello", 4), L("12", 20) });
            Ok(!r.Valid, "a number next to words is not the minimised counter");

            // Applying: keeps the task, points and streak; counts down; ignored without a task.
            var d = Data();
            var t = NewTracker(d);
            Ok(!t.ApplyCounter(new CounterRead { Left = 177, Minimised = true }), "no task: minimised ignored");
            Ok(!t.State.HasTask && t.State.Left < 0, "no task: nothing set");
            Ok(t.ApplyCounter(new CounterRead { Left = 249, Task = "Soul devourers", Points = 6257, Streak = 291 }), "expanded first");
            Ok(t.ApplyCounter(new CounterRead { Left = 177, Minimised = true }), "minimised applied");
            Eq("Soul devourers", t.State.Monster, "task kept"); Eq(177, t.State.Left, "kills left from the minimised counter");
            Eq(249, t.State.Assigned, "assigned kept"); Eq(6257, t.State.Points, "points kept"); Eq(291, t.State.Streak, "streak kept");
            Ok(t.ApplyCounter(new CounterRead { Left = 176, Minimised = true }), "minimised counts down");
            Eq(176, t.State.Left, "176 left"); Eq("73 killed", PanelView.Build(t, "").KilledText, "killed while minimised");
            Ok(!t.ApplyCounter(new CounterRead { Left = 176, Minimised = true }), "same minimised reading: no change");
            Ok(t.ApplyCounter(new CounterRead { Left = 175, Task = "Soul devourers", Points = 6257, Streak = 291 }), "expanded again");
            Eq(175, t.State.Left, "expanded continues the count");
            Ok(t.ApplyCounter(new CounterRead { Left = 0, Minimised = true }), "minimised zero");
            Ok(t.State.Complete, "zero on the minimised counter = complete");

            // The filter treats minimised reads like any other: a change waits for a second read.
            var f = new CounterFilter();
            var s0 = new SlayerState();
            Ok(f.Offer(new CounterRead { Left = 249, Task = "Soul devourers", Points = 6257, Streak = 291 }, s0) == null, "filter: first wait");
            Ok(f.Offer(new CounterRead { Left = 249, Task = "Soul devourers", Points = 6257, Streak = 291 }, s0) != null, "filter: confirmed");
            var s1 = new SlayerState { Monster = "Soul devourers", Left = 249, Assigned = 249 };
            Ok(f.Offer(new CounterRead { Left = 248, Minimised = true }, s1) == null, "filter: minimised waits");
            Ok(f.Offer(new CounterRead { Left = 248, Minimised = true }, s1) != null, "filter: minimised confirmed (task kept)");

            // Detection on the real minimised counter.
            string dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "real");
            string mini = Path.Combine(dir, "counter-minimised.png");
            if (!File.Exists(mini)) { Console.WriteLine("  minimised: no real fixture (skipped)"); return; }
            RgbImage img = Png.Load(mini);
            var c = WidgetFinder.FindMinimisedCounters(img);
            Ok(c.Count == 1, "real minimised counter: one candidate (got " + c.Count + ")");
            if (c.Count > 0)
            {
                Box n = c[0].Text;
                Console.WriteLine("  real minimised: number at " + n + ", region " + c[0].Region + " in " + img.W + "x" + img.H);
                Ok(n.X >= 25 && n.X <= 35 && n.Y >= 15 && n.Y <= 22 && n.W >= 18 && n.W <= 26 && n.H >= 8 && n.H <= 13, "real minimised: number box around 177");
                Eq(3, DigitReader.Glyphs(img, n.Inflate(1, 1)).Count, "real minimised: three digits");
                var dr = new DigitReader();
                Ok(dr.Learn(img, n.Inflate(2, 2), "177"), "real minimised: digits learned");
                Eq(177, dr.Read(img, n.Inflate(2, 2)), "real minimised: read back by template");
            }
            // Pasted into busy frames (with ability-bar and buff numbers on dark squares).
            for (int k = 1; k <= 3; k++)
            {
                string f2 = Fixture("real-minimised-" + k + ".png");
                if (!File.Exists(f2)) continue;
                int[] at = File.ReadAllText(Path.ChangeExtension(f2, ".txt")).Split(' ').Select(int.Parse).ToArray();
                var truth = new Box(at[0], at[1], at[2], at[3]);
                RgbImage fr = Png.Load(f2);
                var mc = WidgetFinder.FindMinimisedCounters(fr);
                Ok(mc.Count >= 1, "minimised frame " + k + ": found");
                if (mc.Count > 0)
                {
                    Ok(!mc[0].Text.Intersect(truth).IsEmpty, "minimised frame " + k + ": best candidate is the counter (" + mc[0].Text + " vs " + truth + ")");
                    Ok(mc[0].Region.Intersect(mc[0].Text).W == mc[0].Text.W, "minimised frame " + k + ": region holds the number");
                }
                Eq(1, mc.Count, "minimised frame " + k + ": no distractors");
            }
            // The expanded counter is not mistaken for the minimised one first.
            string exp = Path.Combine(dir, "counter.png");
            if (File.Exists(exp))
            {
                var ec = WidgetFinder.FindCounters(Png.Load(exp));
                Ok(ec.Count > 0, "expanded real counter still found");
            }
            // The real expanded counter pasted into busy frames: the best candidate is the counter.
            for (int k = 1; k <= 4; k++)
            {
                string f3 = Fixture("real-pasted-" + k + ".png");
                if (!File.Exists(f3)) continue;
                int[] at = File.ReadAllText(Path.ChangeExtension(f3, ".txt")).Split(' ').Select(int.Parse).ToArray();
                var truth = new Box(at[0], at[1], at[2], at[3]);
                var ec = WidgetFinder.FindCounters(Png.Load(f3));
                Ok(ec.Count > 0 && !ec[0].Region.Intersect(truth).IsEmpty, "expanded frame " + k + ": best candidate is the counter" + (ec.Count > 0 ? " (" + ec[0].Region + " vs " + truth + ")" : ""));
            }
        }

        // ---- 0.1.3 regressions from real screenshots -------------------------------------------------
        static void RegressionTests()
        {
            var d = Data();
            // "Abyssal Oerm": OCR read the D of Demons as O and lost the end; 0.1.2 showed the raw read.
            Eq("Abyssal demons", d.FindMonster("Abyssal Oerm")?.Name, "Abyssal Oerm -> Abyssal demons");
            Eq("Abyssal demons", d.FindMonster("Abyssal Oemons")?.Name, "O for D");
            Eq("Abyssal demons", d.FindMonster("Abyssal Dernons")?.Name, "rn for m");
            Eq("Abyssal demons", d.FindMonster("Abyssa1 Demons")?.Name, "1 for l");
            Eq("Abyssal demons", d.FindMonster("ABYSSAL DEMONS")?.Name, "caps");
            Eq("Abyssal demons", d.FindMonster("Abyssal Demon")?.Name, "singular");
            Eq("Soul devourers", d.FindMonster("Soul devourers")?.Name, "soul devourers");
            Eq("Soul devourers", d.FindMonster("Soul dev0urers")?.Name, "0 for o");
            Eq("Soul devourers", d.FindMonster("Soul devourer")?.Name, "soul devourer singular");
            Ok(d.FindMonster("Abyssal") == null, "ambiguous 'Abyssal' is not guessed");
            Ok(d.FindMonster("Oerm") == null, "'Oerm' alone is nothing");
            Ok(d.FindMonster("Hello world") == null, "unrelated words are nothing");
            Ok(d.FindMonster("Abyssal Oerm") != null && d.Monsters.Values.All(m => m.Name != "Abyssal Oerm"), "'Abyssal Oerm' isn't in the data");
            int self = 0;
            foreach (var m in d.Monsters.Values) if (d.FindMonster(m.Name) == m) self++;
            Eq(d.Monsters.Count, self, "every task name finds itself (no look-alike collisions)");

            // Digits inside a word are letters.
            var r = CounterParser.Parse(new[] { L("196", 14, 40, 24, 10), L("Abyssal Dem0ns", 30, 10, 90, 11), L("Slayer points:", 50, 0, 80), L("6,301", 50, 100, 28), L("Task streak:", 66, 0, 70), L("293", 66, 110, 17) });
            Eq("Abyssal Demons", r.Task, "Dem0ns cleaned");

            // The tracker never invents a task from an unmatched counter read, and repairs a saved misread.
            var t = NewTracker(d);
            Ok(!t.ApplyCounter(new CounterRead { Left = 196, Task = "Qwxz Vbnm", Points = 6301, Streak = 293 }) || !t.State.HasTask, "unknown counter task not used");
            Ok(!t.State.HasTask, "no task from an unknown name");
            Ok(t.ApplyCounter(new CounterRead { Left = 196, Task = "Abyssal Oerm", Points = 6301, Streak = 293 }), "misread resolves");
            Eq("Abyssal demons", t.State.Monster, "resolved to the data name");
            var saved = new SlayerState { Monster = "Abyssal Oerm", Left = 196, Assigned = 196, LastSource = "counter" };
            Eq("Abyssal demons", new SlayerTracker(d, saved).State.Monster, "saved misread repaired on load");
            var junk = new SlayerState { Monster = "Qwxz Vbnm", Left = 5, Assigned = 9, LastSource = "counter" };
            Ok(!new SlayerTracker(d, junk).State.HasTask, "saved unplaceable counter name dropped");

            // The real screenshots: the counter's rows (not the clock above it) and its number by digit shapes.
            string dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "real");
            var digits = new DigitReader(); DigitFont.SeedInto(digits);
            foreach (var (file, want, clockAbove) in new[] { ("counter.png", 249, false), ("screen-abyssal-196.png", 196, true), ("screen-abyssal-247.png", 247, true) })
            {
                string f = Path.Combine(dir, file);
                if (!File.Exists(f)) { Console.WriteLine("  " + file + ": missing (skipped)"); continue; }
                RgbImage img = Png.Load(f);
                var c = WidgetFinder.FindCounters(img);
                Ok(c.Count > 0, file + ": counter found");
                if (c.Count == 0) continue;
                Eq(4, c[0].Rows.Count, file + ": number, task, points, streak");
                if (clockAbove) Ok(c[0].Rows[0].Y > c[0].Rows[1].Y - 25, file + ": the clock above is not the number row");
                Eq(want, digits.Read(img, c[0].Rows[0].Inflate(2, 2)), file + ": number by digit shapes");
                Ok(WidgetFinder.FindMinimisedCounters(img).Count == 0, file + ": expanded values aren't a minimised counter");
            }
            string mini = Path.Combine(dir, "counter-minimised.png");
            if (File.Exists(mini))
            {
                RgbImage img = Png.Load(mini);
                Eq(177, digits.Read(img, WidgetFinder.FindMinimisedCounters(img)[0].Text.Inflate(2, 2)), "minimised 177 by digit shapes");
            }
            // The digit shapes never give a confident wrong number (each screenshot read with the others' shapes).
            foreach (var (file, want) in new[] { ("counter.png", 249), ("screen-abyssal-247.png", 247) })
            {
                string f = Path.Combine(dir, file);
                if (!File.Exists(f)) continue;
                RgbImage img = Png.Load(f);
                var c = WidgetFinder.FindCounters(img)[0];
                var other = new DigitReader();
                foreach (var (of, ow) in new[] { ("counter.png", "249"), ("screen-abyssal-196.png", "196"), ("screen-abyssal-247.png", "247") })
                {
                    if (of == file) continue;
                    RgbImage oi = Png.Load(Path.Combine(dir, of));
                    other.Learn(oi, WidgetFinder.FindCounters(oi)[0].Rows[0].Inflate(2, 2), ow);
                }
                RgbImage mi = Png.Load(mini);
                other.Learn(mi, WidgetFinder.FindMinimisedCounters(mi)[0].Text.Inflate(2, 2), "177");
                int got = other.Read(img, c.Rows[0].Inflate(2, 2));
                Ok(got == want || got == -1, file + ": unseen screenshot read right or not at all (got " + got + ")");
            }

            // 247 misread as 99 (and a jump to 260) right after a kill: held back; 247 lands.
            var fl = new CounterFilter { Resolve = x => d.FindMonster(x)?.Name ?? "" };
            var tr = NewTracker(d);
            Func<int, CounterRead> rd = n => new CounterRead { Left = n, Task = "Abyssal Demons", Points = 6301, Streak = 293 };
            void Feed(CounterRead x) { var ok = fl.Offer(x, tr.State); if (ok != null) tr.ApplyCounter(ok); }
            Feed(rd(249)); Feed(rd(249));
            Eq(249, tr.State.Left, "start 249"); Eq(249, tr.State.Assigned, "assigned 249");
            Feed(rd(248)); Feed(rd(248));
            Eq(248, tr.State.Left, "kill: 248");
            for (int k = 0; k < 5; k++) Feed(rd(99));
            Eq(248, tr.State.Left, "99 (a drop of 149) held back");
            for (int k = 0; k < 5; k++) Feed(rd(260));
            Eq(248, tr.State.Left, "260 (a jump up) held back"); Eq(249, tr.State.Assigned, "assigned not inflated to 260");
            Feed(rd(247)); Feed(rd(247));
            Eq(247, tr.State.Left, "247 adopted"); Eq("2 killed", PanelView.Build(tr, "").KilledText, "2 killed");
            Feed(rd(246)); Feed(rd(99)); Feed(rd(246));
            Eq(247, tr.State.Left, "frames must agree in a row"); Feed(rd(246));
            Eq(246, tr.State.Left, "two in a row: adopted");
            // A wrong panel count is still corrected by the counter if it keeps saying so.
            for (int k = 0; k < 10; k++) Feed(rd(300));
            Eq(246, tr.State.Left, "a count above the total is never adopted (not a new task)"); Eq(249, tr.State.Assigned, "total stays 249");
            for (int k = 0; k < 8; k++) Feed(rd(100));
            Eq(100, tr.State.Left, "a steady counter value below the total wins in the end");
            // A new task (different name) is adopted after two frames whatever the number.
            Feed(new CounterRead { Left = 120, Task = "Soul devourers", Points = 6315, Streak = 294 });
            Feed(new CounterRead { Left = 120, Task = "Soul devourers", Points = 6315, Streak = 294 });
            Eq("Soul devourers", tr.State.Monster, "new task"); Eq(120, tr.State.Left, "new task count");
        }

        // ---- 0.1.4: the clock above the counter, totals, points and streak ------------------------------
        static void ClockTests()
        {
            var d = Data();
            string dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "real");
            var big = new DigitReader(); DigitFont.SeedInto(big);
            var small = new DigitReader(); DigitFont.SeedSmallInto(small);
            string f = Path.Combine(dir, "screen-abyssal-173-clock.png");
            if (File.Exists(f))
            {
                RgbImage img = Png.Load(f);
                var clocks = WidgetFinder.FindClocks(img);
                Eq(1, clocks.Count, "173: the clock box is found");
                var c = WidgetFinder.FindCounters(img);
                Ok(c.Count > 0, "173: counter found");
                if (c.Count > 0 && clocks.Count > 0)
                {
                    Eq(4, c[0].Rows.Count, "173: count, task, points, streak");
                    Ok(c[0].Rows.All(r => r.Intersect(clocks[0]).IsEmpty), "173: no counter row is the clock");
                    Ok(clocks[0].Bottom <= c[0].Rows[0].Y, "173: the clock is above the count");
                    Ok(!WidgetFinder.IsClock(img, c[0].Rows[0]), "173: the count is not a clock");
                    Eq(173, big.Read(img, c[0].Rows[0].Inflate(2, 2)), "173 by digit shapes");
                    Eq(6411, small.Read(img, c[0].Rows[2].Inflate(2, 2)), "points 6,411 by digit shapes (comma skipped)");
                    Eq(298, small.Read(img, c[0].Rows[3].Inflate(2, 2)), "streak 298 by digit shapes");
                    Ok(WidgetFinder.FindMinimisedCounters(img).Count == 0, "173: the clock isn't a minimised counter");
                }
            }
            // Every screenshot with a clock: found, and never part of the counter.
            foreach (string file in new[] { "screen-abyssal-196.png", "screen-abyssal-247.png" })
            {
                RgbImage img = Png.Load(Path.Combine(dir, file));
                var clocks = WidgetFinder.FindClocks(img);
                Eq(1, clocks.Count, file + ": clock found");
                var c = WidgetFinder.FindCounters(img)[0];
                Ok(clocks.Count == 0 || c.Rows.All(r => r.Intersect(clocks[0]).IsEmpty), file + ": clock not in the counter");
            }
            foreach (string file in new[] { "counter.png", "counter-minimised.png" })
                Eq(0, WidgetFinder.FindClocks(Png.Load(Path.Combine(dir, file))).Count, file + ": no clock");

            // Parsing: a time is never the count.
            var r1 = CounterParser.Parse(new[] { L("12:34", 0, 90, 30, 10), L("173", 30, 40, 22, 10), L("Abyssal Demons", 45, 10, 90, 11), L("Slayer points:", 61, 0, 80), L("6,411", 61, 100, 28), L("Task streak:", 77, 0, 70), L("298", 77, 110, 19) });
            Eq(173, r1.Left, "clock row above: count still 173"); Eq(6411, r1.Points, "points"); Eq(298, r1.Streak, "streak");
            foreach (string clock in new[] { "12:34", "12.34", "12;34", "12:00" })
            {
                var r2 = CounterParser.Parse(new[] { L(clock, 0), L("Abyssal Demons", 45), L("Slayer points: 6,411", 61), L("Task streak: 298", 77) });
                Ok(r2.Left < 0, "count row missing: " + clock + " is not the count (got " + r2.Left + ")");
                Ok(!CounterParser.Parse(new[] { L(clock, 0) }).Valid, clock + " alone is not a minimised counter");
            }

            // Totals: only set by a new task, never past the count by more than a few, never past the data's max.
            var t = NewTracker(d);
            Ok(t.ApplyCounter(new CounterRead { Left = 249, Task = "Abyssal Demons", Points = 6301, Streak = 293 }), "task from the counter");
            Eq(249, t.State.Assigned, "total from the first read");
            t.ApplyCounter(new CounterRead { Left = 175, Task = "Abyssal Demons", Points = 6411, Streak = 298 });
            Eq(175, t.State.Left, "175 left");
            t.ApplyCounter(new CounterRead { Left = 1234, Task = "Abyssal Demons", Points = 6411, Streak = 298 });
            t.ApplyCounter(new CounterRead { Left = 1200, Task = "Abyssal Demons", Points = 6411, Streak = 298 });
            Eq(175, t.State.Left, "1234/1200 (over the data's max) ignored"); Eq(249, t.State.Assigned, "total not raised to 1,200");
            t.ApplyCounter(new CounterRead { Left = 260, Task = "Abyssal Demons", Points = 6411, Streak = 298 });
            Eq(175, t.State.Left, "a count above the total is held back"); Eq(249, t.State.Assigned, "total kept");
            t.ApplyCounter(new CounterRead { Left = 177, Task = "Abyssal Demons", Points = 6411, Streak = 298 });
            Eq(177, t.State.Left, "a couple back up (a misread corrected) is fine within the total");
            t.ApplyCounter(new CounterRead { Left = 173, Task = "Abyssal Demons", Points = 6411, Streak = 298 });
            Eq(173, t.State.Left, "173"); Eq("76 killed", PanelView.Build(t, "").KilledText, "76 killed");
            Ok(t.AmountLimit("Abyssal demons", "") < 1000, "abyssal demons limit under 1,000 (got " + t.AmountLimit("Abyssal demons", "") + ")");
            // The popup: a misread amount is unknown, not a total.
            var tp = NewTracker(d);
            tp.Apply(new ChatParser(d).Parse("Your new task is to kill 1234 Abyssal demons."), "popup");
            Eq(0, tp.State.Assigned, "popup amount over the max: unknown"); Eq(-1, tp.State.Left, "popup count unknown");
            Ok(tp.ApplyCounter(new CounterRead { Left = 249, Task = "Abyssal Demons", Points = 6301, Streak = 293 }), "first counter read after the popup");
            Eq(249, tp.State.Assigned, "the first counter read sets the total");
            // Repairing the saved 1,200.
            var saved = new SlayerState { Monster = "Abyssal demons", Left = 175, Assigned = 1200, LastSource = "counter", CounterSeen = true, Points = 6411, Streak = 298 };
            var tr = new SlayerTracker(d, saved);
            Eq(175, tr.State.Assigned, "saved 1,200 total repaired to the count"); Ok(!tr.State.CounterSeen, "next counter read may set the total");
            var ok = new SlayerState { Monster = "Abyssal demons", Left = 175, Assigned = 249, LastSource = "counter", CounterSeen = true };
            Eq(249, new SlayerTracker(d, ok).State.Assigned, "a sane total is left alone");
            var json = SlayerState.FromJson(MiniJson.Parse(MiniJson.Write(ok.ToJson())) as JObj);
            Ok(json.CounterSeen, "counterSeen saved");

            // The filter: points and streak only go up; task names must be stable.
            var fl = new CounterFilter { Resolve = x => d.FindMonster(x)?.Name ?? "" };
            var st = new SlayerState { Monster = "Abyssal demons", Left = 175, Assigned = 249, Points = 6411, Streak = 298, CounterSeen = true };
            Func<int, int, int, string, CounterRead> rd = (n, p, k, task) => new CounterRead { Left = n, Task = task, Points = p, Streak = k };
            Ok(fl.Plausible(rd(174, 6411, 298, "Abyssal Demons"), st), "a kill is plausible");
            Ok(!fl.Plausible(rd(174, 6301, 298, "Abyssal Demons"), st), "points going down is not");
            Ok(!fl.Plausible(rd(174, 6411, 290, "Abyssal Demons"), st), "streak going down is not");
            Ok(fl.Plausible(rd(174, 6420, 299, "Abyssal Demons"), st), "points and streak going up is");
            Ok(!fl.Plausible(rd(1234, 6411, 298, "Abyssal Demons"), st), "1234 is not");
            Ok(fl.Offer(rd(174, 6411, 298, "Abyssal Demons"), st) == null, "kill: first frame waits");
            Ok(fl.Offer(rd(174, 6411, 298, "Soul devourers"), st) == null, "task flickers: waits again");
            Ok(fl.Offer(rd(174, 6411, 298, "Abyssal Demons"), st) == null, "back: first frame again");
            Ok(fl.Offer(rd(174, 6411, 298, "Abyssal Demons"), st) != null, "two stable frames: adopted");
            var fl2 = new CounterFilter { Resolve = fl.Resolve };
            for (int k = 0; k < 5; k++) Ok(fl2.Offer(rd(175, 6301, 298, "Abyssal Demons"), st) == null, "points drop held " + k);
        }

        /// <summary>Prints DigitFont's shape table, learned from the real screenshots in tests/fixtures/real
        /// (big number rows for 1 2 4 6 7 9; points/streak values for the rest).
        /// Run: dotnet run -c Release --project tests/UnitTests -- --emit-digit-font</summary>
        static void EmitDigitFont()
        {
            DigitReader big, small;
            TrainDigitFont(out big, out small);
            Console.WriteLine("        // big");
            foreach (var kv in big.Export().OrderBy(k => k.Key))
                Console.WriteLine("            { '" + kv.Key + "', \"" + string.Concat(kv.Value.Select(v => (char)('a' + (int)Math.Round(v * 20)))) + "\" },");
            Console.WriteLine("        // small");
            foreach (var kv in small.Export().OrderBy(k => k.Key))
                Console.WriteLine("            { '" + kv.Key + "', \"" + string.Concat(kv.Value.Select(v => (char)('a' + (int)Math.Round(v * 20)))) + "\" },");
        }

        static readonly (string file, string[] nums)[] FontShots =
        {
            ("counter.png", new[] { "249", "6257", "291" }),
            ("screen-abyssal-196.png", new[] { "196", "6301", "293" }),
            ("screen-abyssal-247.png", new[] { "247", "6301", "293" }),
            ("screen-abyssal-173-clock.png", new[] { "173", "6411", "298" }),
        };

        /// <summary>Big = the kills-left number (plus small shapes for digits no screenshot shows big);
        /// small = the points and streak values.</summary>
        static void TrainDigitFont(out DigitReader big, out DigitReader small, string skip = null)
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "real");
            big = new DigitReader(); small = new DigitReader();
            foreach (var (file, nums) in FontShots)
            {
                if (file == skip) continue;
                RgbImage img = Png.Load(Path.Combine(dir, file));
                var c = WidgetFinder.FindCounters(img)[0];
                big.Learn(img, c.Rows[0].Inflate(2, 2), nums[0]);
                small.Learn(img, c.Rows[2].Inflate(2, 2), nums[1]);
                small.Learn(img, c.Rows[3].Inflate(2, 2), nums[2]);
            }
            if (skip != "counter-minimised.png")
            {
                RgbImage m = Png.Load(Path.Combine(dir, "counter-minimised.png"));
                big.Learn(m, WidgetFinder.FindMinimisedCounters(m)[0].Text.Inflate(2, 2), "177");
            }
            foreach (var kv in small.Export()) big.Seed(kv.Key, kv.Value);
        }

    }
}
