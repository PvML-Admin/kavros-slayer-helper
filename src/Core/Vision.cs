using System;
using System.Collections.Generic;
using System.Linq;

namespace KavrosSlayerHelper.Core
{
    /// <summary>A plain 24-bit RGB image (row-major, 3 bytes per pixel, no padding). The app fills it from a
    /// screen capture; the tests fill it from PNG fixtures. Keeps the detectors free of System.Drawing.</summary>
    public sealed class RgbImage
    {
        public readonly int W, H;
        public readonly byte[] Px;

        public RgbImage(int w, int h, byte[] px = null)
        {
            W = w; H = h;
            Px = px ?? new byte[w * h * 3];
            if (Px.Length < w * h * 3) throw new ArgumentException("pixel buffer too small");
        }

        public int R(int x, int y) { return Px[(y * W + x) * 3]; }
        public int G(int x, int y) { return Px[(y * W + x) * 3 + 1]; }
        public int B(int x, int y) { return Px[(y * W + x) * 3 + 2]; }

        public RgbImage Crop(Box r)
        {
            r = r.Intersect(new Box(0, 0, W, H));
            var o = new RgbImage(Math.Max(1, r.W), Math.Max(1, r.H));
            for (int y = 0; y < r.H; y++)
                Buffer.BlockCopy(Px, ((r.Y + y) * W + r.X) * 3, o.Px, y * o.W * 3, r.W * 3);
            return o;
        }
    }

    /// <summary>Integer rectangle (System.Drawing.Rectangle without the dependency).</summary>
    public struct Box : IEquatable<Box>
    {
        public int X, Y, W, H;
        public Box(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
        public int Right { get { return X + W; } }
        public int Bottom { get { return Y + H; } }
        public bool IsEmpty { get { return W <= 0 || H <= 0; } }
        public static readonly Box Empty = new Box();

        public Box Intersect(Box o)
        {
            int l = Math.Max(X, o.X), t = Math.Max(Y, o.Y), r = Math.Min(Right, o.Right), b = Math.Min(Bottom, o.Bottom);
            return r > l && b > t ? new Box(l, t, r - l, b - t) : Empty;
        }
        public Box Inflate(int dx, int dy) { return new Box(X - dx, Y - dy, W + 2 * dx, H + 2 * dy); }
        public Box Union(Box o)
        {
            if (IsEmpty) return o; if (o.IsEmpty) return this;
            int l = Math.Min(X, o.X), t = Math.Min(Y, o.Y), r = Math.Max(Right, o.Right), b = Math.Max(Bottom, o.Bottom);
            return new Box(l, t, r - l, b - t);
        }
        public bool Contains(int x, int y) { return x >= X && y >= Y && x < Right && y < Bottom; }
        public bool Equals(Box o) { return X == o.X && Y == o.Y && W == o.W && H == o.H; }
        public override bool Equals(object o) { return o is Box && Equals((Box)o); }
        public override int GetHashCode() { return X * 73856093 ^ Y * 19349663 ^ W * 83492791 ^ H; }
        public static bool operator ==(Box a, Box b) { return a.Equals(b); }
        public static bool operator !=(Box a, Box b) { return !a.Equals(b); }
        public override string ToString() { return "{" + X + "," + Y + " " + W + "x" + H + "}"; }
    }

    /// <summary>Colour cues for the two RS3 widgets the helper reads. Thresholds are deliberately loose: the
    /// finder only proposes regions, OCR and the parsers decide.</summary>
    public static class Colours
    {
        /// <summary>The slayer counter's yellow (number, task name, values).</summary>
        public static bool Yellow(int r, int g, int b)
        {
            return r >= 170 && g >= 140 && b <= 110 && r - b >= 100 && g * 10 >= r * 6 && Math.Abs(r - g) <= 90;
        }

        /// <summary>The dialogue box parchment (light tan).</summary>
        public static bool Parchment(int r, int g, int b)
        {
            int lum = (r * 3 + g * 6 + b) / 10;
            return lum >= 135 && lum <= 245 && r >= g && g >= b - 6 && r - b >= 22 && r - b <= 120 && r - g <= 55;
        }

        public static int Luma(int r, int g, int b) { return (r * 3 + g * 6 + b) / 10; }
    }

    /// <summary>Finds the RS3 slayer counter and the NPC dialogue box anywhere in a capture of the game.
    /// Works on a coarse cell grid so a full 1080p frame costs a few milliseconds.</summary>
    public static class WidgetFinder
    {
        // ---- dialogue (parchment) ------------------------------------------------------------------
        /// <summary>The largest roughly rectangular parchment area big enough to be a dialogue box, or Empty.</summary>
        public static Box FindDialogue(RgbImage img, int cell = 8)
        {
            int cw = img.W / cell, ch = img.H / cell;
            if (cw < 4 || ch < 4) return Box.Empty;
            var on = new bool[cw * ch];
            for (int cy = 0; cy < ch; cy++)
                for (int cx = 0; cx < cw; cx++)
                {
                    int hit = 0, n = 0;
                    for (int y = cy * cell + 1; y < cy * cell + cell; y += 2)
                        for (int x = cx * cell + 1; x < cx * cell + cell; x += 2)
                        {
                            int i = (y * img.W + x) * 3;
                            n++;
                            if (Colours.Parchment(img.Px[i], img.Px[i + 1], img.Px[i + 2])) hit++;
                        }
                    on[cy * cw + cx] = hit * 100 >= n * 55;
                }
            Box best = Box.Empty; int bestCells = 0;
            foreach (var comp in Components(on, cw, ch))
            {
                Box b = comp.Key; int count = comp.Value;
                int pw = b.W * cell, ph = b.H * cell;
                if (pw < 180 || ph < 56) continue;
                if (pw > ph * 12 || ph > pw) continue;                 // dialogue boxes are wide
                if (count * 100 < b.W * b.H * 55) continue;            // mostly filled (text and portrait leave holes)
                if (count > bestCells) { bestCells = count; best = b; }
            }
            if (best.IsEmpty) return best;
            return new Box(best.X * cell, best.Y * cell, best.W * cell, best.H * cell).Intersect(new Box(0, 0, img.W, img.H));
        }

        // ---- slayer counter (yellow text on a dark box) ----------------------------------------------
        /// <summary>Regions that look like the slayer counter, best first (see <see cref="FindCounters"/>).</summary>
        public static List<Box> FindCounterCandidates(RgbImage img, int cell = 4)
        {
            var list = new List<Box>();
            foreach (CounterCandidate c in FindCounters(img, cell)) if (list.Count < 6) list.Add(c.Region);
            return list;
        }

        /// <summary>Stacks of yellow text rows on a dark box, scored by how much they look like the RS3 slayer
        /// counter: the number and task centred over each other, then two rows whose yellow values share a
        /// right edge with white labels ("Slayer points:", "Task streak:") to their left.</summary>
        public static List<CounterCandidate> FindCounters(RgbImage img, int cell = 4)
        {
            int cw = img.W / cell, ch = img.H / cell;
            var result = new List<CounterCandidate>();
            if (cw < 4 || ch < 4) return result;
            var on = new bool[cw * ch];
            for (int cy = 0; cy < ch; cy++)
                for (int cx = 0; cx < cw; cx++)
                {
                    int yellow = 0, dark = 0, other = 0;
                    for (int y = cy * cell; y < cy * cell + cell; y++)
                        for (int x = cx * cell; x < cx * cell + cell; x++)
                        {
                            int i = (y * img.W + x) * 3;
                            int r = img.Px[i], g = img.Px[i + 1], b = img.Px[i + 2];
                            if (Colours.Yellow(r, g, b)) yellow++;
                            else if (Colours.Luma(r, g, b) < 95) dark++;
                            else other++;
                        }
                    on[cy * cw + cx] = yellow >= 1 && dark >= other;   // yellow on dark, not on parchment or sky
                }
            // Glyph cells -> text blobs (bridge the gaps between letters with a horizontal dilation).
            var grown = new bool[cw * ch];
            for (int cy = 0; cy < ch; cy++)
                for (int cx = 0; cx < cw; cx++)
                    if (on[cy * cw + cx])
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int x = cx + dx;
                            if (x >= 0 && x < cw) grown[cy * cw + x] = true;
                        }
            var blobs = new List<Box>();
            foreach (var comp in Components(grown, cw, ch))
            {
                Box b = comp.Key;
                if (b.H < 2 || b.W < 2 || b.H > 20) continue;          // 8..80 px tall text rows
                blobs.Add(Tight(img, new Box(b.X * cell, b.Y * cell, b.W * cell, b.H * cell)));
            }
            blobs.RemoveAll(b => b.IsEmpty || b.H < 5);
            // Group blobs into stacks: rows close below each other and overlapping horizontally (or right-aligned values).
            blobs.Sort((a, b) => a.Y.CompareTo(b.Y));
            var used = new bool[blobs.Count];
            for (int i = 0; i < blobs.Count; i++)
            {
                if (used[i]) continue;
                var stack = new List<Box> { blobs[i] };
                used[i] = true;
                Box all = blobs[i];
                for (int j = i + 1; j < blobs.Count; j++)
                {
                    if (used[j]) continue;
                    Box b = blobs[j], last = stack[stack.Count - 1];
                    int gap = b.Y - last.Bottom;
                    if (gap > Math.Max(last.H, b.H) * 2 + 6) { if (b.Y > all.Bottom + 120) break; continue; }
                    bool overlap = b.X < all.Right + 60 && b.Right > all.X - 60;
                    if (!overlap) continue;
                    stack.Add(b); used[j] = true; all = all.Union(b);
                }
                if (stack.Count < 2) continue;
                var c = new CounterCandidate { Rows = stack, Text = all };
                double score = stack.Count >= 3 ? 2 : 1;
                if (stack.Count == 4) score += 1;
                else if (stack.Count > 5) score -= 1;
                // Number and task centred over each other.
                if (Math.Abs((stack[0].X + stack[0].Right) - (stack[1].X + stack[1].Right)) / 2 <= 6 + all.W / 10) score += 1;
                // The last two rows: values sharing a right edge, white labels to their left.
                if (stack.Count >= 3)
                {
                    Box v1 = stack[stack.Count - 2], v2 = stack[stack.Count - 1];
                    if (Math.Abs(v1.Right - v2.Right) <= 6) score += 1;
                    int labels = 0;
                    foreach (Box v in new[] { v1, v2 })
                        if (WhitePixels(img, new Box(Math.Max(0, v.X - Math.Max(60, all.W)), v.Y, Math.Max(0, v.X - 3 - Math.Max(0, v.X - Math.Max(60, all.W))), v.H)) >= 12) labels++;
                    score += labels;
                    c.Labels = labels;
                }
                if (all.W > 400 || all.H > 260) score -= 3;
                c.Score = score;
                if (score <= 1) continue;
                // Labels ("Slayer points:") are white, to the left of the yellow values: pad for them.
                int padL = Math.Max(60, all.W * 2 / 3), padR = Math.Max(20, all.W / 4);
                c.Region = new Box(all.X - padL, all.Y - 8, all.W + padL + padR, all.H + 16).Intersect(new Box(0, 0, img.W, img.H));
                result.Add(c);
            }
            result.Sort((a, b) => b.Score.CompareTo(a.Score));
            return result;
        }

        /// <summary>The minimised slayer counter: one short yellow number on a small dark box, the slayer skull
        /// icon over its top-right corner. The icon is required (ability-bar and buff timers are also yellow
        /// numbers on dark squares). Best first.</summary>
        public static List<CounterCandidate> FindMinimisedCounters(RgbImage img, int cell = 4)
        {
            int cw = img.W / cell, ch = img.H / cell;
            var result = new List<CounterCandidate>();
            if (cw < 4 || ch < 4) return result;
            var on = new bool[cw * ch];
            for (int cy = 0; cy < ch; cy++)
                for (int cx = 0; cx < cw; cx++)
                {
                    int yellow = 0;
                    for (int y = cy * cell; y < cy * cell + cell && yellow == 0; y++)
                        for (int x = cx * cell; x < cx * cell + cell; x++)
                        {
                            int i = (y * img.W + x) * 3;
                            if (Colours.Yellow(img.Px[i], img.Px[i + 1], img.Px[i + 2])) { yellow++; break; }
                        }
                    on[cy * cw + cx] = yellow > 0;
                }
            var grown = new bool[cw * ch];
            for (int cy = 0; cy < ch; cy++)
                for (int cx = 0; cx < cw; cx++)
                    if (on[cy * cw + cx])
                        for (int dx = -1; dx <= 1; dx++) { int x = cx + dx; if (x >= 0 && x < cw) grown[cy * cw + x] = true; }
            foreach (var comp in Components(grown, cw, ch))
            {
                Box cb = comp.Key;
                if (cb.H > 10 || cb.W > 30) continue;
                Box t = Tight(img, new Box(cb.X * cell, cb.Y * cell, cb.W * cell, cb.H * cell));
                if (t.IsEmpty || t.H < 7 || t.H > 40 || t.W < 4 || t.W > t.H * 5) continue;
                List<Box> glyphs = DigitReader.Glyphs(img, t.Inflate(1, 1));
                if (glyphs.Count < 1 || glyphs.Count > 5) continue;
                if (glyphs.Any(g => g.W > g.H * 1.2)) continue;                  // digits are taller than wide
                // A dark box around the number.
                int pad = Math.Max(4, t.H / 2);
                Box ring = t.Inflate(pad, pad / 2 + 1);
                if (DarkShare(img, ring, t) < 0.75) continue;
                // The skull icon: light, unsaturated pixels up and to the right of the number.
                Box iconArea = new Box(t.Right, t.Y - t.H * 2, t.H * 4, t.H * 3);
                int bone = BonePixels(img, iconArea);
                if (bone < Math.Max(8, t.H)) continue;
                var c = new CounterCandidate { Text = t, Minimised = true, Score = 2 + Math.Min(1.0, bone / (double)(t.H * 4)) };
                c.Rows.Add(t);
                c.Region = new Box(t.X - t.H * 2, t.Y - t.H, t.W + t.H * 4, t.H * 3).Intersect(new Box(0, 0, img.W, img.H));
                result.Add(c);
            }
            result.Sort((a, b) => b.Score.CompareTo(a.Score));
            return result;
        }

        static double DarkShare(RgbImage img, Box ring, Box hole)
        {
            ring = ring.Intersect(new Box(0, 0, img.W, img.H));
            int dark = 0, n = 0;
            for (int y = ring.Y; y < ring.Bottom; y++)
                for (int x = ring.X; x < ring.Right; x++)
                {
                    if (hole.Contains(x, y)) continue;
                    int i = (y * img.W + x) * 3;
                    n++;
                    if (Colours.Luma(img.Px[i], img.Px[i + 1], img.Px[i + 2]) < 70) dark++;
                }
            return n == 0 ? 0 : dark / (double)n;
        }

        static int BonePixels(RgbImage img, Box r)
        {
            r = r.Intersect(new Box(0, 0, img.W, img.H));
            int n = 0;
            for (int y = r.Y; y < r.Bottom; y++)
                for (int x = r.X; x < r.Right; x++)
                {
                    int i = (y * img.W + x) * 3;
                    int R = img.Px[i], G = img.Px[i + 1], B = img.Px[i + 2];
                    int mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B));
                    if (mx >= 150 && mx - mn <= 70 && !Colours.Yellow(R, G, B)) n++;
                }
            return n;
        }

        /// <summary>The bounding box of the yellow pixels inside <paramref name="r"/>.</summary>
        static Box Tight(RgbImage img, Box r)
        {
            r = r.Intersect(new Box(0, 0, img.W, img.H));
            int l = int.MaxValue, t = int.MaxValue, rr = -1, bb = -1;
            for (int y = r.Y; y < r.Bottom; y++)
                for (int x = r.X; x < r.Right; x++)
                {
                    int i = (y * img.W + x) * 3;
                    if (!Colours.Yellow(img.Px[i], img.Px[i + 1], img.Px[i + 2])) continue;
                    if (x < l) l = x; if (x > rr) rr = x; if (y < t) t = y; if (y > bb) bb = y;
                }
            return rr < 0 ? Box.Empty : new Box(l, t, rr - l + 1, bb - t + 1);
        }

        static int WhitePixels(RgbImage img, Box r)
        {
            r = r.Intersect(new Box(0, 0, img.W, img.H));
            int n = 0;
            for (int y = r.Y; y < r.Bottom; y++)
                for (int x = r.X; x < r.Right; x++)
                {
                    int i = (y * img.W + x) * 3;
                    int R = img.Px[i], G = img.Px[i + 1], B = img.Px[i + 2];
                    if (R >= 170 && G >= 170 && B >= 160 && Math.Abs(R - B) <= 45) n++;
                }
            return n;
        }

        /// <summary>Cheap change detector: a hash of every 3rd pixel in the region.</summary>
        public static long Fingerprint(RgbImage img, Box r)
        {
            r = r.Intersect(new Box(0, 0, img.W, img.H));
            unchecked
            {
                long h = 1469598103934665603;
                for (int y = r.Y; y < r.Bottom; y += 2)
                    for (int x = r.X; x < r.Right; x += 3)
                    {
                        int i = (y * img.W + x) * 3;
                        // Drop the low bits so capture noise and translucency shimmer don't count as a change.
                        h = (h ^ ((img.Px[i] >> 3) | (img.Px[i + 1] >> 3 << 5) | (img.Px[i + 2] >> 3 << 10))) * 1099511628211;
                    }
                return h;
            }
        }

        static IEnumerable<KeyValuePair<Box, int>> Components(bool[] on, int w, int h)
        {
            var seen = new bool[on.Length];
            var stack = new Stack<int>();
            for (int start = 0; start < on.Length; start++)
            {
                if (!on[start] || seen[start]) continue;
                int l = int.MaxValue, t = int.MaxValue, r = -1, b = -1, n = 0;
                stack.Push(start); seen[start] = true;
                while (stack.Count > 0)
                {
                    int p = stack.Pop();
                    int x = p % w, y = p / w;
                    n++;
                    if (x < l) l = x; if (x > r) r = x; if (y < t) t = y; if (y > b) b = y;
                    if (x > 0 && on[p - 1] && !seen[p - 1]) { seen[p - 1] = true; stack.Push(p - 1); }
                    if (x < w - 1 && on[p + 1] && !seen[p + 1]) { seen[p + 1] = true; stack.Push(p + 1); }
                    if (y > 0 && on[p - w] && !seen[p - w]) { seen[p - w] = true; stack.Push(p - w); }
                    if (y < h - 1 && on[p + w] && !seen[p + w]) { seen[p + w] = true; stack.Push(p + w); }
                }
                yield return new KeyValuePair<Box, int>(new Box(l, t, r - l + 1, b - t + 1), n);
            }
        }
    }

    public sealed class CounterCandidate
    {
        public Box Region;               // padded region to OCR
        public Box Text;                 // the yellow text
        public List<Box> Rows = new List<Box>();
        public int Labels;               // white labels found left of the last two rows
        public bool Minimised;           // the minimised counter (just the number)
        public double Score;
        public override string ToString() { return Region + " score=" + Score + " rows=" + Rows.Count + " labels=" + Labels; }
    }

    /// <summary>Reads the counter's big yellow number by template matching, so a kill usually needs no OCR.
    /// The font isn't shipped: templates are learned from OCR reads of the same widget (digits seen once
    /// are then recognised in a fraction of a millisecond).</summary>
    public sealed class DigitReader
    {
        const int TW = 10, TH = 14;
        readonly Dictionary<char, float[]> templates = new Dictionary<char, float[]>();
        public int Known { get { return templates.Count; } }
        public double MaxDiff = 0.13;

        /// <summary>Glyph boxes (left to right) of yellow text inside <paramref name="row"/>.</summary>
        public static List<Box> Glyphs(RgbImage img, Box row)
        {
            row = row.Intersect(new Box(0, 0, img.W, img.H));
            var cols = new int[row.W];
            int top = int.MaxValue, bottom = -1;
            for (int x = 0; x < row.W; x++)
                for (int y = 0; y < row.H; y++)
                {
                    int i = ((row.Y + y) * img.W + row.X + x) * 3;
                    if (Colours.Yellow(img.Px[i], img.Px[i + 1], img.Px[i + 2])) { cols[x]++; if (y < top) top = y; if (y > bottom) bottom = y; }
                }
            var list = new List<Box>();
            int sx = -1;
            for (int x = 0; x <= row.W; x++)
            {
                bool ink = x < row.W && cols[x] > 0;
                if (ink && sx < 0) sx = x;
                else if (!ink && sx >= 0)
                {
                    // Vertical extent of this glyph.
                    int gt = int.MaxValue, gb = -1;
                    for (int gx = sx; gx < x; gx++)
                        for (int y = 0; y < row.H; y++)
                        {
                            int i = ((row.Y + y) * img.W + row.X + gx) * 3;
                            if (Colours.Yellow(img.Px[i], img.Px[i + 1], img.Px[i + 2])) { if (y < gt) gt = y; if (y > gb) gb = y; }
                        }
                    int gh = gb - gt + 1;
                    // Commas and specks are short: skip them (the number has no separators we need).
                    if (gb >= 0 && gh * 2 >= (bottom - top + 1)) list.Add(new Box(row.X + sx, row.Y + gt, x - sx, gh));
                    sx = -1;
                }
            }
            return list;
        }

        static float[] Shape(RgbImage img, Box g)
        {
            var v = new float[TW * TH];
            for (int ty = 0; ty < TH; ty++)
                for (int tx = 0; tx < TW; tx++)
                {
                    int x0 = g.X + tx * g.W / TW, x1 = Math.Max(x0 + 1, g.X + (tx + 1) * g.W / TW);
                    int y0 = g.Y + ty * g.H / TH, y1 = Math.Max(y0 + 1, g.Y + (ty + 1) * g.H / TH);
                    int hit = 0, n = 0;
                    for (int y = y0; y < y1 && y < img.H; y++)
                        for (int x = x0; x < x1 && x < img.W; x++)
                        {
                            int i = (y * img.W + x) * 3; n++;
                            if (Colours.Yellow(img.Px[i], img.Px[i + 1], img.Px[i + 2])) hit++;
                        }
                    v[ty * TW + tx] = n == 0 ? 0 : hit / (float)n;
                }
            return v;
        }

        /// <summary>Learns the digits in <paramref name="text"/> (what OCR read) from the glyphs in the row.</summary>
        public bool Learn(RgbImage img, Box row, string text)
        {
            var digits = new List<char>();
            foreach (char c in text ?? "") if (c >= '0' && c <= '9') digits.Add(c);
            List<Box> gl = Glyphs(img, row);
            if (digits.Count == 0 || gl.Count != digits.Count) return false;
            for (int i = 0; i < gl.Count; i++)
            {
                float[] s = Shape(img, gl[i]);
                float[] old;
                if (templates.TryGetValue(digits[i], out old))
                    for (int k = 0; k < s.Length; k++) old[k] = old[k] * 0.7f + s[k] * 0.3f;
                else templates[digits[i]] = s;
            }
            return true;
        }

        /// <summary>The number in the row, or -1 if any glyph isn't a confident match.</summary>
        public int Read(RgbImage img, Box row)
        {
            if (templates.Count == 0) return -1;
            List<Box> gl = Glyphs(img, row);
            if (gl.Count == 0 || gl.Count > 6) return -1;
            int n = 0;
            foreach (Box g in gl)
            {
                float[] s = Shape(img, g);
                char best = '?'; double bestD = double.MaxValue, second = double.MaxValue;
                foreach (var kv in templates)
                {
                    double d = 0;
                    for (int k = 0; k < s.Length; k++) d += Math.Abs(s[k] - kv.Value[k]);
                    d /= s.Length;
                    if (d < bestD) { second = bestD; bestD = d; best = kv.Key; } else if (d < second) second = d;
                }
                if (bestD > MaxDiff || (second < double.MaxValue && second - bestD < 0.02)) return -1;
                n = n * 10 + (best - '0');
            }
            return n;
        }
    }
}
