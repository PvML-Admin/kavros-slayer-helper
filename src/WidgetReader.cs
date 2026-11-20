using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using KavrosSlayerHelper.Core;

namespace KavrosSlayerHelper
{
    internal sealed class WidgetRead
    {
        public CounterRead Counter;           // a reading of the slayer counter, or null
        public string CounterBy = "";         // how the number was read: digits, digits+ocr, ocr x2, ocr
        public List<TextLine> Dialogue;       // OCR lines of a dialogue box that just appeared or changed
        public bool DialogueGone;             // no dialogue box on screen any more
        public bool Forced;
    }

    /// <summary>One full capture and what the reader made of it, for "Save debug capture".</summary>
    internal sealed class DebugFrame
    {
        public RgbImage Full;
        public Rectangle Area;
        public Box Locked, Dialogue;
        public bool LockedMinimised;
        public List<CounterCandidate> Expanded, Minimised;
        public readonly List<string> Notes = new List<string>();
        public readonly DateTime Time = DateTime.Now;
    }

    /// <summary>Reads the RS3 slayer counter and the slayer master's dialogue box on a background thread.
    /// The counter is found once by colour (yellow text stacked on a dark box) and confirmed by OCR, then only
    /// its small region is captured; OCR runs only when its pixels change, and a kill is usually read by
    /// digit templates without OCR. The whole game is captured every other second to spot a dialogue box
    /// (parchment), which is OCR'd once per appearance.</summary>
    internal sealed class WidgetReader : IDisposable
    {
        readonly WinOcr ocr = new WinOcr();
        readonly Action<WidgetRead> deliver;          // called on the worker thread; the host marshals
        readonly Func<bool> needConfirm;              // the host wants the counter read again even if unchanged
        readonly Thread thread;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        volatile bool stop, rescan;
        readonly object gate = new object();
        Rectangle area;                               // game render area, screen px
        bool active;
        Rectangle exclude;                            // the panel, screen px
        readonly double[] fixedCounter;               // optional settings.counterRegion (fractions of the area)

        Box counter;                                  // locked counter region, relative to the area
        long counterFp, counterRestFp, dialogueFp;
        int counterMisses, searches, tick, lastLeft = -1;
        Box numberRow;                                // the big number inside the counter region (region px)
        bool lockedMinimised;                         // the locked counter is the minimised form (just the number)
        volatile DebugFrame lastFrame;                // the last full capture and what was found in it
        readonly DigitReader digits = NewDigits();

        static DigitReader NewDigits() { var d = new DigitReader(); DigitFont.SeedInto(d); return d; }

        public int IntervalMs = 1000;
        public volatile string Status = "Looking for the slayer counter\u2026";

        public WidgetReader(Action<WidgetRead> deliver, Func<bool> needConfirm, double[] fixedCounter)
        {
            this.deliver = deliver;
            this.needConfirm = needConfirm ?? (() => false);
            this.fixedCounter = fixedCounter;
            thread = new Thread(Loop) { IsBackground = true, Name = "widget reader" };
            thread.SetApartmentState(ApartmentState.MTA);
        }

        public void Start() { thread.Start(); }

        public void SetArea(Rectangle gameArea, bool gameActive)
        {
            lock (gate)
            {
                if (gameArea.Size != area.Size) { counter = Box.Empty; counterFp = 0; }   // resized: find it again
                area = gameArea;
                active = gameActive;
            }
            if (gameActive) wake.Set();
        }

        public void SetExclude(Rectangle r) { lock (gate) exclude = r; }

        public void Rescan()
        {
            lock (gate) { counter = Box.Empty; counterFp = 0; dialogueFp = 0; lockedMinimised = false; }
            rescan = true;
            wake.Set();
        }

        void Loop()
        {
            while (!stop)
            {
                wake.WaitOne(IntervalMs);
                if (stop) return;
                Rectangle a; bool act;
                lock (gate) { a = area; act = active; }
                bool forced = rescan;
                // Only while the game (or a Kavros helper) is in front: otherwise another app may cover it.
                if (a.Width < 100 || a.Height < 100 || (!act && !forced)) continue;
                rescan = false;
                try { ScanOnce(a, forced); }
                catch (Exception ex)
                {
                    if (stop) return;
                    Status = "Could not read the screen (" + ex.GetType().Name + ")";
                    Log.Write("reader: scan failed: " + ex);
                    Thread.Sleep(2000);
                }
            }
        }

        void ScanOnce(Rectangle a, bool forced)
        {
            if (!ocr.Ready) { Status = ocr.Unavailable ?? "Windows text recognition is unavailable"; Thread.Sleep(5000); return; }
            tick++;
            var read = new WidgetRead { Forced = forced };
            Box locked; Rectangle ex;
            lock (gate) { locked = counter; ex = exclude; }
            Box areaBox = new Box(0, 0, a.Width, a.Height);
            if (locked.IsEmpty && fixedCounter != null)
                locked = new Box((int)(a.Width * fixedCounter[0]), (int)(a.Height * fixedCounter[1]), (int)(a.Width * fixedCounter[2]), (int)(a.Height * fixedCounter[3])).Intersect(areaBox);
            Box exRel = ex.Width > 0 ? new Box(ex.X - a.X, ex.Y - a.Y, ex.Width, ex.Height) : Box.Empty;

            bool fullLook = forced || locked.IsEmpty || tick % 2 == 0;
            RgbImage full = fullLook ? CaptureRgb(a, areaBox) : null;
            if (full != null && !exRel.IsEmpty) Blank(full, exRel);
            DebugFrame dbg = full != null ? new DebugFrame { Full = full, Area = a, Locked = locked, LockedMinimised = lockedMinimised } : null;

            // ---- counter ----
            if (!locked.IsEmpty)
            {
                RgbImage img = full != null ? full.Crop(locked) : CaptureRgb(a, locked);
                if (full == null && !exRel.IsEmpty) Blank(img, new Box(exRel.X - locked.X, exRel.Y - locked.Y, exRel.W, exRel.H));
                ReadCounter(img, locked, forced, read);
                if (dbg != null) dbg.Notes.Add("locked counter " + locked + (lockedMinimised ? " (minimised)" : "") + " -> " + (read.Counter != null ? read.Counter.ToString() : "no new read"));
                // Locked on the minimised counter: now and then look for the expanded one (task, points, streak).
                if (full != null && lockedMinimised && tick % 6 == 0) SearchExpanded(full, read, dbg);
                // Unreadable twice in a row (minimised, expanded or moved): look around straight away.
                else if (full != null && read.Counter == null && counterMisses >= 2 && fixedCounter == null) SearchCounter(full, read, dbg);
            }
            else if (full != null)
            {
                SearchCounter(full, read, dbg);
            }

            // ---- dialogue ----
            if (full != null)
            {
                Box d = WidgetFinder.FindDialogue(full);
                if (d.IsEmpty)
                {
                    if (dialogueFp != 0) { dialogueFp = 0; read.DialogueGone = true; }
                }
                else
                {
                    long fp = WidgetFinder.Fingerprint(full, d);
                    if (fp != dialogueFp || forced)
                    {
                        dialogueFp = fp;
                        read.Dialogue = OcrDialogue(full, d);
                        if (Log.Verbose) Log.Write("dialogue at " + d + ": " + string.Join(" | ", read.Dialogue.Select(l => l.Text)));
                        if (dbg != null) dbg.Notes.Add("dialogue at " + d + ": " + string.Join(" | ", read.Dialogue.Select(l => l.Text)));
                    }
                }
            }
            if (dbg != null) { dbg.Dialogue = WidgetFinder.FindDialogue(full); lastFrame = dbg; }
            if (read.Counter != null || read.Dialogue != null || read.DialogueGone || forced) deliver(read);
        }

        void ReadCounter(RgbImage img, Box region, bool forced, WidgetRead read)
        {
            Box all = new Box(0, 0, img.W, img.H);
            long fp = WidgetFinder.Fingerprint(img, all);
            // Unchanged pixels: nothing to do, unless the host is confirming a change (it wants the same
            // reading on consecutive frames before adopting it).
            if (fp == counterFp && !forced && !needConfirm()) return;
            Box row = NumberRow(img);
            int byDigits = row.IsEmpty ? -1 : digits.Read(img, row);
            // Only the number changed (a kill) and the digit shapes are sure of it: no OCR at all.
            if (!forced && byDigits >= 0 && lastLeft >= 0 && !numberRow.IsEmpty && row.Equals(numberRow)
                && WidgetFinder.Fingerprint(img, Rest(img, row)) == counterRestFp)
            {
                counterFp = fp;
                counterMisses = 0;
                lastLeft = byDigits;
                read.Counter = new CounterRead { Left = byDigits, Task = lastTask, Points = lastPoints, Streak = lastStreak, Minimised = lastMinimised };
                read.CounterBy = "digits";
                return;
            }
            CounterRead r = CounterParser.Parse(OcrLines(img, all, 3f, false));
            string by = FixNumber(img, row, byDigits, ref r);
            if (r.Valid)
            {
                counterFp = fp;
                counterMisses = 0;
                Status = "";
                Remember(r, img, row);
                read.Counter = r;
                read.CounterBy = by;
            }
            else if (++counterMisses >= 6 && fixedCounter == null)
            {
                // Closed or moved: look for it again (the panel keeps the last values meanwhile).
                lock (gate) counter = Box.Empty;
                counterMisses = 0;
                counterFp = 0;
                Status = "Looking for the slayer counter\u2026";
                Log.Write("counter: lost at " + region);
            }
        }

        /// <summary>Settles the kills-left number of an OCR reading: the digit shapes (from the real counter
        /// font) win when they are sure; otherwise the number is OCR'd again on its own and must agree with the
        /// whole-box read and the glyph count (then its shapes are learned); otherwise the reading gets no
        /// number. Returns how it was read.</summary>
        string FixNumber(RgbImage img, Box row, int byDigits, ref CounterRead r)
        {
            if (row.IsEmpty) return "ocr";
            if (byDigits >= 0)
            {
                if (r.Left != byDigits && r.Left >= 0) Log.Write("counter: OCR read " + r.Left + ", digits read " + byDigits + " (using digits)");
                if (!r.Valid && lastLeft >= 0) r = new CounterRead { Task = lastTask, Points = lastPoints, Streak = lastStreak, Minimised = lastMinimised };
                r.Left = byDigits;
                return "digits+ocr";
            }
            // A digit the shapes don't know yet (8 isn't in any screenshot).
            int lone = OcrNumber(img, row);
            int glyphs = DigitReader.GlyphCount(img, row);
            if (lone >= 0 && lone == r.Left && lone.ToString().Length == glyphs)
            {
                digits.Learn(img, row, lone.ToString());
                return "ocr x2";
            }
            if (r.Left >= 0) Log.Write("counter: number unsure (box OCR " + r.Left + ", number OCR " + lone + ", " + glyphs + " glyphs): skipped");
            r.Left = -1;
            return "unsure";
        }

        /// <summary>The big number's row inside a counter crop, found from the layout (expanded: the top row
        /// of the stack; minimised: the lone number), padded for the digit reader. Empty when unclear.</summary>
        static Box NumberRow(RgbImage img)
        {
            List<CounterCandidate> ex = WidgetFinder.FindCounters(img);
            if (ex.Count > 0 && ex[0].Rows.Count >= 3 && ex[0].Score >= 4) return ex[0].Rows[0].Inflate(2, 2).Intersect(new Box(0, 0, img.W, img.H));
            List<CounterCandidate> mi = WidgetFinder.FindMinimisedCounters(img);
            if (mi.Count > 0) return mi[0].Text.Inflate(2, 2).Intersect(new Box(0, 0, img.W, img.H));
            return Box.Empty;
        }

        static Box Rest(RgbImage img, Box row)
        {
            return new Box(0, row.Bottom, img.W, Math.Max(1, img.H - row.Bottom));
        }

        /// <summary>OCR of the number row alone: yellow ink drawn black on white, scaled up and padded
        /// (Windows OCR reads an isolated, high-contrast number far more reliably). -1 if not a number.</summary>
        int OcrNumber(RgbImage img, Box row)
        {
            row = row.Intersect(new Box(0, 0, img.W, img.H));
            if (row.IsEmpty || stop) return -1;
            const int k = 4, pad = 24;
            var clean = new RgbImage(row.W * k + pad * 2, row.H * k + pad * 2);
            for (int i = 0; i < clean.Px.Length; i++) clean.Px[i] = 255;
            for (int y = 0; y < row.H; y++)
                for (int x = 0; x < row.W; x++)
                {
                    int i = ((row.Y + y) * img.W + row.X + x) * 3;
                    if (!Colours.Yellow(img.Px[i], img.Px[i + 1], img.Px[i + 2])) continue;
                    for (int dy = 0; dy < k; dy++)
                        for (int dx = 0; dx < k; dx++)
                        {
                            int o = ((pad + y * k + dy) * clean.W + pad + x * k + dx) * 3;
                            clean.Px[o] = clean.Px[o + 1] = clean.Px[o + 2] = 0;
                        }
                }
            using (Bitmap b = ToBitmap(clean, new Box(0, 0, clean.W, clean.H)))
            {
                string text = string.Join(" ", ocr.Read(b).Select(kv => kv.Key));
                string t = ChatParser.Clean(text).Replace(",", "").Replace(".", "").Replace(" ", "");
                t = t.Replace('O', '0').Replace('o', '0').Replace('l', '1').Replace('I', '1').Replace('|', '1').Replace('S', '5');
                int n;
                return t.Length > 0 && t.Length <= 5 && t.All(char.IsDigit) && int.TryParse(t, out n) ? n : -1;
            }
        }

        string lastTask = ""; int lastPoints = -1, lastStreak = -1; bool lastMinimised;

        void Remember(CounterRead r, RgbImage img, Box row)
        {
            lastLeft = r.Left; lastTask = r.Task; lastPoints = r.Points; lastStreak = r.Streak; lastMinimised = r.Minimised;
            numberRow = row;
            counterRestFp = row.IsEmpty ? 0 : WidgetFinder.Fingerprint(img, Rest(img, row));
        }

        int candidateTurn;

        /// <summary>Looks for the expanded counter (yellow number and task over white labels) by colour.</summary>
        bool SearchExpanded(RgbImage full, WidgetRead read, DebugFrame dbg)
        {
            List<CounterCandidate> cands = WidgetFinder.FindCounters(full);
            if (dbg != null) dbg.Expanded = cands;
            // OCR the best two, plus one more of the rest in turn (a busy scene can outscore the counter).
            var order = cands.Take(2).ToList();
            if (cands.Count > 2) order.Add(cands[2 + (candidateTurn++ % (cands.Count - 2))]);
            foreach (CounterCandidate c in order)
            {
                RgbImage img = full.Crop(c.Region);
                CounterRead r = CounterParser.Parse(OcrLines(img, new Box(0, 0, img.W, img.H), 3f, false));
                if (dbg != null) dbg.Notes.Add("expanded candidate " + c.Region + " score " + c.Score.ToString("0.00") + " -> " + r + (r.Valid ? "" : " (invalid)"));
                if (!r.Valid || r.Minimised) continue;
                Lock(c.Region, full, img, r, read, "colour");
                return true;
            }
            return false;
        }

        /// <summary>Looks for the minimised counter (one yellow number, skull icon at its top-right).</summary>
        bool SearchMinimised(RgbImage full, WidgetRead read, DebugFrame dbg)
        {
            List<CounterCandidate> cands = WidgetFinder.FindMinimisedCounters(full);
            if (dbg != null) dbg.Minimised = cands;
            foreach (CounterCandidate c in cands.Take(2))
            {
                RgbImage img = full.Crop(c.Region);
                CounterRead r = CounterParser.Parse(OcrLines(img, new Box(0, 0, img.W, img.H), 3f, false));
                if (dbg != null) dbg.Notes.Add("minimised candidate " + c.Region + " score " + c.Score.ToString("0.00") + " -> " + r + (r.Valid ? "" : " (invalid)"));
                if (!r.Valid || !r.Minimised) continue;
                Lock(c.Region, full, img, r, read, "colour (minimised)");
                return true;
            }
            return false;
        }

        void SearchCounter(RgbImage full, WidgetRead read, DebugFrame dbg)
        {
            searches++;
            if (SearchExpanded(full, read, dbg)) return;
            if (SearchMinimised(full, read, dbg)) return;
            // Fallback every 5th look: OCR the whole game for the counter's labels.
            if (searches % 5 != 0) { Status = "Looking for the slayer counter\u2026"; return; }
            List<TextLine> lines = OcrLines(full, new Box(0, 0, full.W, full.H), 1.5f, false);
            TextLine label = lines.FirstOrDefault(l => Regex.IsMatch(l.Text, @"slayer\s*p[o0]i?nts|task\s*str", RegexOptions.IgnoreCase));
            if (dbg != null) dbg.Notes.Add("label search: " + (label == null ? "no counter labels in " + lines.Count + " lines" : "\"" + label.Text + "\""));
            if (label == null) { Status = "Looking for the slayer counter\u2026"; return; }
            float lh = Math.Max(8, label.H);
            var box = new Box((int)(label.X - lh * 3), (int)(label.Y - lh * 6), (int)(label.W + lh * 6), (int)(lh * 10)).Intersect(new Box(0, 0, full.W, full.H));
            RgbImage cimg = full.Crop(box);
            CounterRead cr = CounterParser.Parse(OcrLines(cimg, new Box(0, 0, cimg.W, cimg.H), 3f, false));
            if (cr.Valid && !cr.Minimised) Lock(box, full, cimg, cr, read, "labels");
            else Status = "Looking for the slayer counter\u2026";
        }

        void Lock(Box c, RgbImage full, RgbImage img, CounterRead r, WidgetRead read, string how)
        {
            lock (gate) counter = c;
            lockedMinimised = r.Minimised;
            counterFp = WidgetFinder.Fingerprint(img, new Box(0, 0, img.W, img.H));
            counterMisses = 0;
            Status = "";
            Box row = NumberRow(img);
            read.CounterBy = FixNumber(img, row, row.IsEmpty ? -1 : digits.Read(img, row), ref r);
            if (!r.Valid) { Log.Write("counter: found by " + how + " at " + c + " but the number is unsure"); counterFp = 0; return; }
            Remember(r, img, row);
            read.Counter = r;
            Log.Write("counter: found by " + how + " at " + c + " (" + r + ")");
        }

        List<TextLine> OcrDialogue(RgbImage full, Box d)
        {
            RgbImage img = full.Crop(d);
            List<TextLine> lines = OcrLines(img, new Box(0, 0, img.W, img.H), 2f, true);
            // The portrait on the left reads as noise: drop lines that end before the text column starts.
            var texty = lines.Where(l => l.Text.Count(char.IsLetter) >= 12).ToList();
            if (texty.Count > 0)
            {
                float textLeft = texty.Min(l => l.X);
                lines = lines.Where(l => l.X + l.W > textLeft + 4 && l.Text.Count(char.IsLetter) >= 2).ToList();
            }
            return lines;
        }

        /// <summary>OCR lines of a region of <paramref name="img"/>, in image pixels.</summary>
        List<TextLine> OcrLines(RgbImage img, Box region, float scale, bool darkOnLight)
        {
            var result = new List<TextLine>();
            if (stop) return result;
            region = region.Intersect(new Box(0, 0, img.W, img.H));
            if (region.IsEmpty) return result;
            // Windows OCR refuses images larger than its limit (about 2600 px a side).
            int maxDim = ocr.MaxDimension;
            if (maxDim > 0 && Math.Max(region.W, region.H) * scale > maxDim) scale = maxDim / (float)Math.Max(region.W, region.H);
            using (Bitmap src = ToBitmap(img, region))
            using (Bitmap prepared = darkOnLight ? PrepareDarkOnLight(src, scale) : PrepareLightOnDark(src, scale))
            {
                foreach (var kv in ocr.Read(prepared))
                {
                    RectangleF b = kv.Value;
                    result.Add(new TextLine(kv.Key, region.X + b.X / scale, region.Y + b.Y / scale, b.Width / scale, b.Height / scale));
                }
            }
            return result;
        }

        // ---- pixels ----------------------------------------------------------------------------------
        static RgbImage CaptureRgb(Rectangle a, Box r)
        {
            using (var bmp = new Bitmap(r.W, r.H, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(new Point(a.X + r.X, a.Y + r.Y), Point.Empty, new Size(r.W, r.H), CopyPixelOperation.SourceCopy);
                return FromBitmap(bmp);
            }
        }

        public static RgbImage FromBitmap(Bitmap bmp)
        {
            var img = new RgbImage(bmp.Width, bmp.Height);
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[d.Stride];
                for (int y = 0; y < bmp.Height; y++)
                {
                    Marshal.Copy(d.Scan0 + y * d.Stride, row, 0, d.Stride);
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        int s = x * 3, t = (y * bmp.Width + x) * 3;
                        img.Px[t] = row[s + 2]; img.Px[t + 1] = row[s + 1]; img.Px[t + 2] = row[s];   // BGR -> RGB
                    }
                }
            }
            finally { bmp.UnlockBits(d); }
            return img;
        }

        static Bitmap ToBitmap(RgbImage img, Box r)
        {
            r = r.Intersect(new Box(0, 0, img.W, img.H));
            var bmp = new Bitmap(Math.Max(1, r.W), Math.Max(1, r.H), PixelFormat.Format24bppRgb);
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[d.Stride];
                for (int y = 0; y < r.H; y++)
                {
                    for (int x = 0; x < r.W; x++)
                    {
                        int s = ((r.Y + y) * img.W + r.X + x) * 3, t = x * 3;
                        row[t] = img.Px[s + 2]; row[t + 1] = img.Px[s + 1]; row[t + 2] = img.Px[s];
                    }
                    Marshal.Copy(row, 0, d.Scan0 + y * d.Stride, d.Stride);
                }
            }
            finally { bmp.UnlockBits(d); }
            return bmp;
        }

        static void Blank(RgbImage img, Box r)
        {
            r = r.Inflate(4, 4).Intersect(new Box(0, 0, img.W, img.H));
            for (int y = r.Y; y < r.Bottom; y++) Array.Clear(img.Px, (y * img.W + r.X) * 3, r.W * 3);
        }

        static Bitmap Upscale(Bitmap src, float scale)
        {
            int w = Math.Max(1, (int)(src.Width * scale)), h = Math.Max(1, (int)(src.Height * scale));
            var big = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(big))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h));
            }
            return big;
        }

        /// <summary>Counter: yellow and white text on a dark, see-through box -> dark text on white
        /// (brightness = strongest channel, stretched above the background level, inverted).</summary>
        public static Bitmap PrepareLightOnDark(Bitmap src, float scale)
        {
            Bitmap big = Upscale(src, scale);
            Map(big, (r, g, b) => Math.Max(r, Math.Max(g, b)), 55, true);
            return big;
        }

        /// <summary>Dialogue: dark-brown text and the gold speaker name on parchment. The blue channel
        /// separates both from the parchment best (parchment is light in blue, gold and brown are not).</summary>
        public static Bitmap PrepareDarkOnLight(Bitmap src, float scale)
        {
            Bitmap big = Upscale(src, scale);
            Map(big, (r, g, b) => b, 70, false);
            return big;
        }

        static void Map(Bitmap big, Func<int, int, int, int> channel, int percentile, bool textIsBright)
        {
            int w = big.Width, h = big.Height;
            BitmapData d = big.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            try
            {
                int stride = d.Stride;
                var px = new byte[stride * h];
                Marshal.Copy(d.Scan0, px, 0, px.Length);
                var hist = new int[256];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * stride + x * 3;
                        int v = channel(px[i + 2], px[i + 1], px[i]);
                        px[i] = (byte)v;
                        hist[v]++;
                    }
                int total = w * h, acc = 0, bg = 0;
                for (int v = 0; v < 256; v++) { acc += hist[v]; if (acc >= total * percentile / 100) { bg = v; break; } }
                int lo, hi;
                if (textIsBright) { lo = Math.Min(230, bg + 28); hi = Math.Min(255, lo + 90); }
                else { hi = Math.Max(30, bg - 20); lo = Math.Max(0, hi - 80); }
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * stride + x * 3;
                        int v = px[i];
                        int t = v <= lo ? 0 : v >= hi ? 255 : (v - lo) * 255 / Math.Max(1, hi - lo);
                        byte o = textIsBright ? (byte)(255 - t) : (byte)t;
                        px[i] = o; px[i + 1] = o; px[i + 2] = o;
                    }
                Marshal.Copy(px, 0, d.Scan0, px.Length);
            }
            finally { big.UnlockBits(d); }
        }

        /// <summary>Writes the last full capture, the counter crop and a text report of what was detected to
        /// <paramref name="dir"/>. Returns the report's path, or null when nothing has been captured yet.</summary>
        public string SaveDebug(string dir, string extra)
        {
            DebugFrame f = lastFrame;
            if (f == null || f.Full == null) return null;
            System.IO.Directory.CreateDirectory(dir);
            string stamp = f.Time.ToString("yyyyMMdd-HHmmss");
            string png = System.IO.Path.Combine(dir, "capture-" + stamp + ".png");
            using (Bitmap b = ToBitmap(f.Full, new Box(0, 0, f.Full.W, f.Full.H))) b.Save(png, ImageFormat.Png);
            if (!f.Locked.IsEmpty)
                using (Bitmap b = ToBitmap(f.Full, f.Locked)) b.Save(System.IO.Path.Combine(dir, "counter-" + stamp + ".png"), ImageFormat.Png);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Kavros Slayer Helper " + Program.Version + " debug capture " + f.Time.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("game area (screen px): " + f.Area + "  capture " + f.Full.W + "x" + f.Full.H);
            sb.AppendLine("status: " + Status);
            sb.AppendLine("locked counter: " + (f.Locked.IsEmpty ? "none" : f.Locked + (f.LockedMinimised ? " (minimised)" : "")));
            sb.AppendLine("dialogue box: " + (f.Dialogue.IsEmpty ? "none" : f.Dialogue.ToString()));
            List<CounterCandidate> ex = f.Expanded ?? WidgetFinder.FindCounters(f.Full);
            sb.AppendLine("expanded counter candidates (" + ex.Count + "):");
            foreach (var c in ex.Take(10)) sb.AppendLine("  " + c.Region + " text " + c.Text + " rows " + c.Rows.Count + " labels " + c.Labels + " score " + c.Score.ToString("0.00"));
            List<CounterCandidate> mi = f.Minimised ?? WidgetFinder.FindMinimisedCounters(f.Full);
            sb.AppendLine("minimised counter candidates (" + mi.Count + "):");
            foreach (var c in mi.Take(10)) sb.AppendLine("  " + c.Region + " number " + c.Text + " score " + c.Score.ToString("0.00"));
            sb.AppendLine("this capture:");
            foreach (string n in f.Notes) sb.AppendLine("  " + n);
            if (!string.IsNullOrEmpty(extra)) { sb.AppendLine(); sb.AppendLine(extra); }
            string txt = System.IO.Path.Combine(dir, "capture-" + stamp + ".txt");
            System.IO.File.WriteAllText(txt, sb.ToString());
            return txt;
        }

        public void Dispose()
        {
            stop = true;
            wake.Set();
        }
    }
}
