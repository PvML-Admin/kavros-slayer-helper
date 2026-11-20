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

            // The filter: one misread never lands; the first reading with no state does.
            var f = new CounterFilter();
            var a = new CounterRead { Left = 135, Task = "Dragons", Points = 6257, Streak = 291 };
            Ok(f.Offer(a, false) != null, "first reading passes");
            var b = new CounterRead { Left = 184, Task = "Dragons", Points = 6257, Streak = 291 };
            Ok(f.Offer(b, true) == null && f.NeedsConfirm, "a change waits for a second read");
            var c = new CounterRead { Left = 134, Task = "Dragons", Points = 6257, Streak = 291 };
            Ok(f.Offer(c, true) == null, "a different second read waits again");
            Ok(f.Offer(c, true) != null && !f.NeedsConfirm, "confirmed");
            Ok(f.Offer(c, true) == null, "unchanged: nothing to apply");
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
    }
}
