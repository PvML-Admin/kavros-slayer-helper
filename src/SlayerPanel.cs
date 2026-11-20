using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using KavrosSlayerHelper.Core;

namespace KavrosSlayerHelper
{
    /// <summary>The Slayer helper panel (full, compact or minimised), drawn to match the approved mockups
    /// (slayer-full / slayer-compact). All lengths are CSS pixels at 100 % and multiplied by the scale.</summary>
    internal sealed class SlayerPanel : LayeredWindow
    {
        public enum Hit { None, Mode, Minimise, Rescan, Clear, Count, Minus, Plus }

        public const float FullWidth = 316, CompactWidth = 292, ShadowPad = 4;

        PanelView view = new PanelView();
        bool compact, minimised;
        Hit hover = Hit.None, pressed = Hit.None;
        bool hoverMonster;          // pointer over the monster block (shows the -/+ buttons)
        readonly Dictionary<Hit, RectangleF> hits = new Dictionary<Hit, RectangleF>();
        RectangleF monsterBlock;
        string editText;            // non-null while the count is being typed
        bool allowActivate;

        public event Action<Hit> Clicked;
        /// <summary>Right-click on the header (screen point): the hidden menu with "Save debug capture".</summary>
        public event Action<Point> HeaderMenu;
        public event Action<int> Wheel;             // +1 / -1 on the count
        public event Action<int?> EditFinished;     // the typed count, or null if cancelled
        public event Action EditStarted;
        /// <summary>Dragging by the header or any empty part: the wanted top-left (screen px), and the end of the drag.</summary>
        public event Action<Point> DragMoved;
        public event Action DragEnded;

        bool dragArmed, dragging;
        Point dragStartMouse, dragStartWindow;
        public bool Dragging { get { return dragging; } }

        public SlayerPanel()
        {
            Text = "Kavros Slayer Helper";
        }

        protected override bool NoActivate { get { return !allowActivate; } }

        public bool Compact { get { return compact; } set { if (compact != value) { compact = value; Present(); } } }
        public bool Minimised { get { return minimised; } set { if (minimised != value) { minimised = value; Present(); } } }
        public bool Editing { get { return editText != null; } }

        public void SetView(PanelView v) { view = v ?? new PanelView(); Present(); }

        // ---- fonts and text ---------------------------------------------------------------------
        // Ascent/descent from the fonts' hhea tables (Cinzel 976/372, Outfit 1000/260 per 1000 em),
        // so line boxes and baselines land where the browser put them in the mockup.
        const float CinzelA = 0.976f, CinzelD = 0.372f, OutfitA = 1.0f, OutfitD = 0.26f;

        struct TextStyle
        {
            public Theme.Face Face; public float Size, Tracking, LineHeight; public Color Color; public bool Italic, Upper, Tab;
            public float A { get { return Face == Theme.Face.Display ? CinzelA : OutfitA; } }
            public float D { get { return Face == Theme.Face.Display ? CinzelD : OutfitD; } }
            /// <summary>Distance from the top of the CSS line box to the baseline (CSS px).</summary>
            public float BaselineInBox { get { float lh = LineHeight > 0 ? LineHeight : (A + D) * Size; return (lh - (A + D) * Size) / 2f + A * Size; } }
            public float Box { get { return LineHeight > 0 ? LineHeight : (A + D) * Size; } }
        }

        static TextStyle St(Theme.Face face, float size, Color c, float lineHeight = 0, float trackingEm = 0, bool italic = false, bool upper = false)
        {
            return new TextStyle { Face = face, Size = size, Color = c, LineHeight = lineHeight, Tracking = trackingEm * size, Italic = italic, Upper = upper };
        }

        // The mockup's type scale (styles.css).
        static readonly TextStyle TitleSt = St(Theme.Face.Display, 11, Theme.Text, 11, 0.14f);
        static readonly TextStyle HeadBtnSt = St(Theme.Face.Medium, 11, Theme.TextSoft, 11, 0.01f);
        static readonly TextStyle KickerSt = St(Theme.Face.Display, 8.5f, Theme.TextFaint, 8.5f * 1.2f, 0.16f, false, true);
        static readonly TextStyle H1St = St(Theme.Face.Display, 15, Theme.Text, 18, 0.02f);
        static readonly TextStyle LeftSt = Tabular(St(Theme.Face.SemiBold, 18, Theme.Gold, 18, 0.01f));
        static readonly TextStyle OfSt = St(Theme.Face.Medium, 12, Theme.TextSoft);
        static readonly TextStyle BarLabelSt = Tabular(St(Theme.Face.Regular, 10.5f, Theme.TextFaint));
        static readonly TextStyle MetaSt = St(Theme.Face.Regular, 11.5f, Theme.TextSoft, 11.5f * 1.35f);
        static readonly TextStyle MetaBoldSt = St(Theme.Face.Medium, 11.5f, Theme.Text, 11.5f * 1.35f);
        static readonly TextStyle ChatSt = St(Theme.Face.Regular, 11, Theme.TextFaint, 11 * 1.35f, 0, true);
        static readonly TextStyle SecSt = St(Theme.Face.Display, 8.5f, Theme.Gold, 8.5f, 0.16f, false, true);
        static readonly TextStyle LocNameSt = St(Theme.Face.Medium, 12.5f, Theme.Text, 12.5f * 1.25f);
        static readonly TextStyle LocTagSt = St(Theme.Face.Medium, 10.5f, Theme.Gold, 0, 0.02f);
        static readonly TextStyle LocHintSt = St(Theme.Face.Regular, 11, Theme.TextFaint, 11 * 1.3f);
        static readonly TextStyle FactKSt = St(Theme.Face.Regular, 11, Theme.TextFaint);
        static readonly TextStyle FactVSt = St(Theme.Face.Medium, 12, Theme.Text, 12 * 1.3f);
        static readonly TextStyle StatKSt = St(Theme.Face.Display, 8.5f, Theme.Gold, 8.5f, 0.16f, false, true);
        static readonly TextStyle StatVSt = Tabular(St(Theme.Face.SemiBold, 15, Theme.Text, 15 * 1.15f));
        static readonly TextStyle StatSubSt = St(Theme.Face.Regular, 10.5f, Theme.TextFaint, 10.5f * 1.3f);
        static readonly TextStyle BtnSt = St(Theme.Face.Medium, 12, Theme.Text, 12, 0.01f);
        static readonly TextStyle BtnPrimarySt = St(Theme.Face.SemiBold, 12, Theme.GoldInk, 12, 0.01f);
        static readonly TextStyle FootSt = St(Theme.Face.Regular, 10, Theme.TextFaint, 10 * 1.35f, 0, true);
        static readonly TextStyle CNameSt = St(Theme.Face.Display, 13, Theme.Text, 0, 0.02f);
        static readonly TextStyle CLeftSt = Tabular(St(Theme.Face.SemiBold, 14, Theme.Gold));
        static readonly TextStyle CUnitSt = St(Theme.Face.Medium, 11, Theme.TextSoft);
        static readonly TextStyle CPlaceSt = St(Theme.Face.Regular, 10.5f, Theme.TextFaint);

        static TextStyle Tabular(TextStyle st) { st.Tab = true; return st; }
        const float TabFigure = 0.59f;   // Outfit's tabular figure advance (em), for font-variant-numeric: tabular-nums

        /// <summary>Splits text into runs; digit runs are laid out one figure per tabular cell.</summary>
        static IEnumerable<string> Runs(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                bool d = char.IsDigit(text[i]);
                int j = i + 1;
                while (j < text.Length && char.IsDigit(text[j]) == d) j++;
                yield return text.Substring(i, j - i);
                i = j;
            }
        }

        static readonly StringFormat Typo = MakeFormat();
        static StringFormat MakeFormat()
        {
            var f = (StringFormat)StringFormat.GenericTypographic.Clone();
            f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            return f;
        }

        static Font F(TextStyle st, float s) { return Theme.Font(st.Face, st.Size * s); }
        static string Tx(TextStyle st, string t) { return st.Upper ? (t ?? "").ToUpperInvariant() : (t ?? ""); }

        /// <summary>Width in device px.</summary>
        static float W(Graphics g, TextStyle st, string text, float s)
        {
            text = Tx(st, text);
            if (text.Length == 0) return 0;
            if (!st.Tab) return Theme.Measure(g, text, F(st, s), st.Tracking * s).Width;
            float w = 0;
            foreach (var run in Runs(text))
                w += char.IsDigit(run[0]) ? run.Length * (TabFigure * st.Size * s + st.Tracking * s)
                                          : Theme.Measure(g, run, F(st, s), st.Tracking * s).Width + st.Tracking * s;
            return w - st.Tracking * s;
        }

        /// <summary>Draws at a baseline (device px). Italic is slanted around the baseline, like a browser's
        /// synthetic oblique (Outfit has no italic face).</summary>
        static void Draw(Graphics g, TextStyle st, string text, float x, float baseline, float s, Color? color = null)
        {
            text = Tx(st, text);
            if (text.Length == 0) return;
            Font f = F(st, s);
            float top = baseline - st.A * st.Size * s;
            float h = f.GetHeight(g);
            if (!st.Italic)
            {
                // Theme.DrawText centres the font's line height on midY; give it the matching middle.
                if (!st.Tab) { Theme.DrawText(g, text, f, color ?? st.Color, x, top + h / 2f, st.Tracking * s); return; }
                float cell = TabFigure * st.Size * s;
                foreach (var run in Runs(text))
                {
                    if (char.IsDigit(run[0]))
                        foreach (char c in run)
                        {
                            string d = c.ToString();
                            float dw = Theme.Measure(g, d, f).Width;
                            Theme.DrawText(g, d, f, color ?? st.Color, x + (cell - dw) / 2f, top + h / 2f);
                            x += cell + st.Tracking * s;
                        }
                    else
                    {
                        Theme.DrawText(g, run, f, color ?? st.Color, x, top + h / 2f, st.Tracking * s);
                        x += Theme.Measure(g, run, f, st.Tracking * s).Width + st.Tracking * s;
                    }
                }
                return;
            }
            // Oblique: draw upright into a bitmap, then shear it onto the panel with a parallelogram
            // DrawImage (a sheared Graphics transform is ignored by some GDI+ text paths).
            const float k = 0.2f;
            float w = W(g, st, text, s);
            int pad = (int)Math.Ceiling(2 * s);
            int bw = (int)Math.Ceiling(w) + pad * 2, bh = (int)Math.Ceiling(h) + pad * 2;
            using (var bmp = new Bitmap(bw, bh, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (var bg = Graphics.FromImage(bmp))
                {
                    Theme.Prepare(bg);
                    bg.TextRenderingHint = TextRenderingHint.AntiAlias;
                    bg.Clear(Color.Transparent);
                    Theme.DrawText(bg, text, f, color ?? st.Color, pad, pad + h / 2f, st.Tracking * s);
                }
                float ox = x - pad, oy = top - pad;
                float by = baseline - oy;              // baseline inside the bitmap
                var pts = new[] {
                    new PointF(ox + k * by, oy),
                    new PointF(ox + k * by + bw, oy),
                    new PointF(ox - k * (bh - by), oy + bh) };
                g.DrawImage(bmp, pts, new RectangleF(0, 0, bw, bh), GraphicsUnit.Pixel);
            }
        }

        static string Fit(Graphics g, TextStyle st, string text, float maxW, float s)
        {
            if (string.IsNullOrEmpty(text) || W(g, st, text, s) <= maxW) return text;
            for (int n = text.Length - 1; n > 1; n--)
            {
                string t = text.Substring(0, n).TrimEnd(' ', ',', '\u00b7') + "\u2026";
                if (W(g, st, t, s) <= maxW) return t;
            }
            return "\u2026";
        }

        static float R(float v) { return (float)Math.Round(v); }

        // ---- measure / render -------------------------------------------------------------------
        float PanelWidth { get { return compact ? CompactWidth : FullWidth; } }

        protected override Size Measure(float s)
        {
            float h;
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            {
                Theme.Prepare(g);
                h = LayoutPanel(g, s, false);
            }
            return new Size((int)Math.Ceiling((PanelWidth + ShadowPad) * s), (int)Math.Ceiling(h * s + ShadowPad * s));
        }

        protected override void Render(Graphics g, float s) { LayoutPanel(g, s, true); }

        /// <summary>Lays the panel out (and draws it when <paramref name="draw"/>). Returns the height in CSS px.</summary>
        float LayoutPanel(Graphics g, float s, bool draw)
        {
            // No grid fitting: glyph advances and word gaps stay as designed (the browser mockup),
            // instead of being rounded to whole pixels at 100% scale.
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            if (draw) hits.Clear();
            float pw = PanelWidth;
            float height = minimised ? 1 + 36 + 1 + 1 : compact ? CompactHeight(g, s) : FullHeight(g, s);
            if (!draw) return height;

            var panel = new RectangleF(0, 0, R(pw * s), R(height * s));
            // Shadow (0 12px 28px rgba(0,0,0,.5)), approximated inside the window like the Companion hub.
            for (int i = 4; i >= 1; i--)
                using (var b = new SolidBrush(Color.FromArgb(18, 0, 0, 0)))
                    g.FillRectangle(b, panel.X + i * s * 0.6f, panel.Y + i * s, panel.Width, panel.Height);
            Theme.Fill(g, Theme.Bg, panel);

            DrawHeader(g, s, pw);
            if (!minimised)
            {
                if (compact) DrawCompact(g, s, pw); else DrawFull(g, s, pw);
            }
            // Frame on top: 1px border and the 2px gold rule, as on every Kavros widget.
            float bw = Math.Max(1f, R(s));
            using (var p = new Pen(Theme.Frame, bw)) g.DrawRectangle(p, panel.X + bw / 2f, panel.Y + bw / 2f, panel.Width - bw, panel.Height - bw);
            Theme.Fill(g, Theme.Gold, new RectangleF(panel.X + bw, panel.Y + bw, panel.Width - 2 * bw, Math.Max(2f, R(2 * s))));
            return height;
        }

        // Header: 36px (padding 7 12, 22px buttons) plus a 1px rule, inside the 1px frame.
        void DrawHeader(Graphics g, float s, float pw)
        {
            var head = new RectangleF(R(1 * s), R(1 * s), R((pw - 2) * s), R(36 * s));
            Theme.Fill(g, Theme.BgRaise, head);
            Theme.Fill(g, Theme.LineSoft, new RectangleF(head.X, head.Bottom, head.Width, Math.Max(1, R(s))));

            float right = pw - 1 - 12;
            // Minimise (or restore when minimised): 22 x 22.
            var min = new RectangleF(R((right - 22) * s), R(8 * s), R(22 * s), R(22 * s));
            hits[Hit.Minimise] = min;
            HeadButton(g, s, min, hover == Hit.Minimise);
            Color ic = hover == Hit.Minimise ? Theme.Gold : Theme.TextSoft;
            using (var p = new Pen(ic, 1.2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                float x0 = min.X + 6 * s, y0 = min.Y + 6 * s;
                // "–" minimises; when minimised it becomes "+" to expand again.
                g.DrawLine(p, x0 + 1.6f * s, y0 + 5 * s, x0 + 8.4f * s, y0 + 5 * s);
                if (minimised) g.DrawLine(p, x0 + 5 * s, y0 + 1.6f * s, x0 + 5 * s, y0 + 8.4f * s);
            }
            float titleRight = right - 22;
            if (!minimised)
            {
                string label = compact ? "Expand" : "Compact";
                float tw = W(g, HeadBtnSt, label, s) / s;
                float bwid = tw + 16 + 2;
                var mode = new RectangleF(R((right - 22 - 6 - bwid) * s), R(8 * s), R(bwid * s), R(22 * s));
                hits[Hit.Mode] = mode;
                bool hot = hover == Hit.Mode;
                HeadButton(g, s, mode, hot);
                float bl = mode.Y + (mode.Height - HeadBtnSt.Box * s) / 2f + HeadBtnSt.BaselineInBox * s;
                Draw(g, HeadBtnSt, label, mode.X + (mode.Width - tw * s) / 2f, bl, s, hot ? Theme.Gold : Theme.TextSoft);
                titleRight = right - 22 - 6 - bwid;
            }
            // Title: line-height 1 with 1px top padding, centred in the 22px row.
            float rowTop = 1 + 7, boxTop = rowTop + (22 - 12) / 2f + 1;
            string title = Fit(g, TitleSt, "SLAYER HELPER", (titleRight - 8 - 13) * s, s);
            Draw(g, TitleSt, title, R(13 * s), (boxTop + TitleSt.BaselineInBox) * s, s);
        }

        static void HeadButton(Graphics g, float s, RectangleF r, bool hot)
        {
            using (var path = Theme.Rounded(new RectangleF(r.X + 0.5f * s, r.Y + 0.5f * s, r.Width - 1 * s, r.Height - 1 * s), 3 * s))
            {
                using (var b = new SolidBrush(Theme.BgPanel)) g.FillPath(b, path);
                using (var p = new Pen(hot ? Theme.Gold : Theme.ButtonLine, Math.Max(1f, s))) g.DrawPath(p, path);
            }
        }

        // ---- full panel ------------------------------------------------------------------------------
        const float BodyTop = 1 + 36 + 1;   // frame + header + header rule

        float FullHeight(Graphics g, float s) { return FullLayout(g, s, false); }
        void DrawFull(Graphics g, float s, float pw) { FullLayout(g, s, true); }

        float FullLayout(Graphics g, float s, bool draw)
        {
            float x0 = 1 + 12, x1 = FullWidth - 1 - 12, w = x1 - x0;
            float y = BodyTop + 8;
            PanelView v = view;
            float blockTop = y;

            // Monster block (flex column, gap 2).
            Line(g, s, draw, KickerSt, v.Kicker, x0, ref y, w); y += 2;
            Line(g, s, draw, H1St, v.Title, x0, ref y, w);
            if (v.HasTask)
            {
                y += 2 + 2;
                CountRow(g, s, draw, x0, x1, ref y);
                y += 2 + 8;
                if (draw) Bar(g, s, x0, y, w, v.Progress);
                y += 4 + 2 + 4;
                float lb = y + BarLabelSt.BaselineInBox;
                if (draw)
                {
                    Draw(g, BarLabelSt, v.KilledText, x0 * s, lb * s, s);
                    if (!string.IsNullOrEmpty(v.PercentText)) Draw(g, BarLabelSt, v.PercentText, x1 * s - W(g, BarLabelSt, v.PercentText, s), lb * s, s);
                }
                y += BarLabelSt.Box + 2 + 5;
                float mb = y + MetaSt.BaselineInBox;
                if (draw)
                {
                    if (!string.IsNullOrEmpty(v.Master))
                    {
                        const string by = "Assigned by ";
                        Draw(g, MetaSt, by, x0 * s, mb * s, s);
                        Draw(g, MetaBoldSt, Fit(g, MetaBoldSt, v.Master, w * s - W(g, MetaSt, by, s), s), x0 * s + W(g, MetaSt, by, s), mb * s, s);
                    }
                    else Draw(g, MetaSt, v.Complete ? "Return to a Slayer master for a new task" : "Master not read yet", x0 * s, mb * s, s);
                }
                y += MetaSt.Box;
            }
            else
            {
                y += 2 + 5;
                float mb = y + MetaSt.BaselineInBox;
                if (draw) Draw(g, MetaSt, Fit(g, MetaSt, "Talk to a Slayer master or open the slayer counter.", w * s, s), x0 * s, mb * s, s);
                y += MetaSt.Box;
            }
            if (!string.IsNullOrEmpty(v.ChatLine))
            {
                y += 2 + 3;
                Line(g, s, draw, ChatSt, v.ChatLine, x0, ref y, w);
            }
            y += 6;
            monsterBlock = new RectangleF(x0 * s, blockTop * s, w * s, (y - blockTop) * s);

            if (v.HasTask)
            {
                Rule(g, s, draw, x0, w, ref y);
                Section(g, s, draw, "Locations", x0, ref y);
                if (v.Locations.Count == 0) Line(g, s, draw, LocHintSt, "No locations in the data yet", x0, ref y, w);
                for (int i = 0; i < v.Locations.Count; i++)
                {
                    if (i > 0) y += 6;
                    LocationView l = v.Locations[i];
                    // name | tag, baseline aligned; hint below (row gap 1).
                    float tagW = string.IsNullOrEmpty(l.Tag) ? 0 : Math.Min(W(g, LocTagSt, l.Tag, s) / s, w * 0.5f);
                    float nameRoom = w - (tagW > 0 ? tagW + 8 : 0);
                    float above = Math.Max(LocNameSt.BaselineInBox, LocTagSt.BaselineInBox);
                    float below = Math.Max(LocNameSt.Box - LocNameSt.BaselineInBox, LocTagSt.Box - LocTagSt.BaselineInBox);
                    if (draw)
                    {
                        Draw(g, LocNameSt, Fit(g, LocNameSt, l.Name, nameRoom * s, s), x0 * s, (y + above) * s, s);
                        if (tagW > 0)
                        {
                            string tag = Fit(g, LocTagSt, l.Tag, tagW * s, s);
                            Draw(g, LocTagSt, tag, x1 * s - W(g, LocTagSt, tag, s), (y + above) * s, s);
                        }
                    }
                    y += above + below;
                    if (!string.IsNullOrEmpty(l.Hint)) { y += 1; Line(g, s, draw, LocHintSt, l.Hint, x0, ref y, w); }
                }

                Rule(g, s, draw, x0, w, ref y);
                Section(g, s, draw, "Combat", x0, ref y);
                Fact(g, s, draw, "Weakness", v.Weakness, false, x0, w, ref y); y += 4;
                Fact(g, s, draw, "Style", v.Style, false, x0, w, ref y); y += 4;
                Fact(g, s, draw, "Gear", v.Gear, false, x0, w, ref y); y += 4;
                Fact(g, s, draw, "Special", v.Special, v.SpecialOk ? (bool?)true : (bool?)false, x0, w, ref y);
            }

            Rule(g, s, draw, x0, w, ref y);
            // Stats: two columns, gap 16.
            float colW = (w - 16) / 2f;
            float sy = y;
            Stat(g, s, draw, "Streak", v.StreakValue, v.StreakSub, x0, colW, ref sy);
            float sy2 = y;
            Stat(g, s, draw, "Points", v.PointsValue, v.PointsSub, x0 + colW + 16, colW, ref sy2);
            y = Math.Max(sy, sy2);

            // Buttons.
            y += 9;
            float bwid = (w - 8) / 2f;
            var rescan = new RectangleF(R(x0 * s), R(y * s), R(bwid * s), R(28 * s));
            var clear = new RectangleF(R((x0 + bwid + 8) * s), R(y * s), R((x1 - x0 - bwid - 8) * s), R(28 * s));
            if (draw)
            {
                hits[Hit.Rescan] = rescan; hits[Hit.Clear] = clear;
                Button(g, s, rescan, "Rescan", true, hover == Hit.Rescan);
                Button(g, s, clear, "Clear", false, hover == Hit.Clear);
            }
            y += 28 + 6;
            Line(g, s, draw, FootSt, v.Footnote, x0, ref y, w);
            y += 10 + 1;
            return y;
        }

        void CountRow(Graphics g, float s, bool draw, float x0, float x1, ref float y)
        {
            PanelView v = view;
            float above = Math.Max(LeftSt.BaselineInBox, OfSt.BaselineInBox);
            float below = Math.Max(LeftSt.Box - LeftSt.BaselineInBox, OfSt.Box - OfSt.BaselineInBox);
            float bl = y + above;
            string num = Editing ? (editText.Length == 0 ? " " : editText) : v.LeftText;
            float nw = W(g, LeftSt, num, s) / s;
            if (draw)
            {
                var countRect = new RectangleF((x0 - 2) * s, y * s, (nw + 4) * s, (above + below) * s);
                hits[Hit.Count] = countRect;
                bool hot = hover == Hit.Count || Editing;
                Draw(g, LeftSt, num, x0 * s, bl * s, s, hot && !Editing ? Theme.GoldBright : Theme.Gold);
                if (Editing)
                {
                    // Caret and a thin gold underline while typing.
                    Theme.Fill(g, Theme.Gold, new RectangleF(x0 * s + nw * s + 1 * s, (bl - 13) * s, Math.Max(1, R(1.2f * s)), 15 * s));
                    Theme.Fill(g, Color.FromArgb(150, 232, 195, 106), new RectangleF(x0 * s, (bl + 3) * s, (nw + 3) * s, Math.Max(1, R(s))));
                }
                Draw(g, OfSt, Editing ? "left \u00b7 Enter to save" : v.OfText, (x0 + nw + 6) * s, bl * s, s);

                // -/+ : small header-style buttons at the right, only while the pointer is over the task.
                if ((hoverMonster || Editing) && v.HasTask)
                {
                    float bs = 18;
                    var plus = new RectangleF(R((x1 - bs) * s), R((bl - 13.5f) * s), R(bs * s), R(bs * s));
                    var minus = new RectangleF(R((x1 - bs - 4 - bs) * s), plus.Y, plus.Width, plus.Height);
                    hits[Hit.Minus] = minus; hits[Hit.Plus] = plus;
                    foreach (var kv in new[] { new KeyValuePair<Hit, RectangleF>(Hit.Minus, minus), new KeyValuePair<Hit, RectangleF>(Hit.Plus, plus) })
                    {
                        bool h = hover == kv.Key;
                        HeadButton(g, s, kv.Value, h);
                        using (var p = new Pen(h ? Theme.Gold : Theme.TextSoft, 1.2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            float cx = kv.Value.X + kv.Value.Width / 2f, cy = kv.Value.Y + kv.Value.Height / 2f;
                            g.DrawLine(p, cx - 3.4f * s, cy, cx + 3.4f * s, cy);
                            if (kv.Key == Hit.Plus) g.DrawLine(p, cx, cy - 3.4f * s, cx, cy + 3.4f * s);
                        }
                    }
                }
            }
            y += above + below;
        }

        void Bar(Graphics g, float s, float x, float y, float w, float t)
        {
            var track = new RectangleF(R(x * s), R(y * s), R(w * s), Math.Max(2, R(4 * s)));
            using (var path = Theme.Rounded(track, 2 * s)) using (var b = new SolidBrush(Theme.TrackBg)) g.FillPath(b, path);
            if (t <= 0) return;
            var fill = new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * t), track.Height);
            using (var path = Theme.Rounded(fill, 2 * s)) using (var b = new SolidBrush(Theme.Gold)) g.FillPath(b, path);
        }

        void Line(Graphics g, float s, bool draw, TextStyle st, string text, float x, ref float y, float w)
        {
            if (draw) Draw(g, st, Fit(g, st, text, (w - (st.Italic ? 2 : 0)) * s, s), x * s, (y + st.BaselineInBox) * s, s);
            y += st.Box;
        }

        void Rule(Graphics g, float s, bool draw, float x0, float w, ref float y)
        {
            y += 7;
            if (draw) Theme.Fill(g, Theme.LineSoft, new RectangleF(R(x0 * s), R(y * s), R(w * s), Math.Max(1, R(s))));
            y += 1 + 7;
        }

        void Section(Graphics g, float s, bool draw, string label, float x0, ref float y)
        {
            if (draw) Draw(g, SecSt, label, x0 * s, (y + SecSt.BaselineInBox) * s, s);
            y += SecSt.Box + 6;
        }

        void Fact(Graphics g, float s, bool draw, string k, string value, bool? ok, float x0, float w, ref float y)
        {
            float above = Math.Max(FactKSt.BaselineInBox, FactVSt.BaselineInBox);
            float below = Math.Max(FactKSt.Box - FactKSt.BaselineInBox, FactVSt.Box - FactVSt.BaselineInBox);
            if (draw)
            {
                Draw(g, FactKSt, k, x0 * s, (y + above) * s, s);
                Color c = ok == true ? Theme.Mint : ok == false && k == "Special" ? Theme.Warn : Theme.Text;
                Draw(g, FactVSt, Fit(g, FactVSt, value, (w - 82) * s, s), (x0 + 82) * s, (y + above) * s, s, c);
            }
            y += above + below;
        }

        void Stat(Graphics g, float s, bool draw, string k, string value, string sub, float x, float w, ref float y)
        {
            if (draw) Draw(g, StatKSt, k, x * s, (y + StatKSt.BaselineInBox) * s, s);
            y += StatKSt.Box + 3;
            if (draw) Draw(g, StatVSt, Fit(g, StatVSt, value, w * s, s), x * s, (y + StatVSt.BaselineInBox) * s, s);
            y += StatVSt.Box + 2;
            if (draw) Draw(g, StatSubSt, Fit(g, StatSubSt, sub, w * s, s), x * s, (y + StatSubSt.BaselineInBox) * s, s);
            y += StatSubSt.Box;
        }

        void Button(Graphics g, float s, RectangleF r, string label, bool primary, bool hot)
        {
            bool down = hot && pressed != Hit.None;
            Color fill = primary ? (hot ? Theme.GoldBright : Theme.Gold) : (hot ? Theme.BgHover : Look.Btn);
            if (down) fill = primary ? Theme.GoldEdge : Theme.BgRaise;
            Color edge = primary ? Theme.GoldEdge : (hot ? Theme.Gold : Theme.ButtonLine);
            using (var path = Theme.Rounded(new RectangleF(r.X + 0.5f * s, r.Y + 0.5f * s, r.Width - 1 * s, r.Height - 1 * s), 3 * s))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(edge, Math.Max(1f, s))) g.DrawPath(p, path);
            }
            TextStyle st = primary ? BtnPrimarySt : BtnSt;
            float tw = W(g, st, label, s);
            float bl = r.Y + (r.Height - st.Box * s) / 2f + st.BaselineInBox * s;
            Draw(g, st, label, r.X + (r.Width - tw) / 2f, bl, s);
        }

        // ---- compact panel ---------------------------------------------------------------------------
        float CompactHeight(Graphics g, float s) { return CompactLayout(g, s, false); }
        void DrawCompact(Graphics g, float s, float pw) { CompactLayout(g, s, true); }

        float CompactLayout(Graphics g, float s, bool draw)
        {
            PanelView v = view;
            float x0 = 1 + 10, x1 = CompactWidth - 1 - 10, w = x1 - x0;
            float y = BodyTop + 8;
            float blockTop = y;
            // Row: name (ellipsis) | 142 | left, baseline aligned.
            float above = Math.Max(CNameSt.BaselineInBox, Math.Max(CLeftSt.BaselineInBox, CUnitSt.BaselineInBox));
            float below = Math.Max(CNameSt.Box - CNameSt.BaselineInBox, Math.Max(CLeftSt.Box - CLeftSt.BaselineInBox, CUnitSt.Box - CUnitSt.BaselineInBox));
            float bl = y + above;
            if (draw)
            {
                string unit = v.HasTask ? "left" : "";
                string num = !v.HasTask ? "" : Editing ? (editText.Length == 0 ? " " : editText) : v.LeftText;
                float uw = W(g, CUnitSt, unit, s) / s;
                float nw = W(g, CLeftSt, num, s) / s;
                float ux = x1 - uw;
                float nx = ux - (uw > 0 ? 4 : 0) - nw;
                Draw(g, CUnitSt, unit, ux * s, bl * s, s);
                if (num.Length > 0)
                {
                    hits[Hit.Count] = new RectangleF((nx - 2) * s, y * s, (nw + 4) * s, (above + below) * s);
                    Draw(g, CLeftSt, num, nx * s, bl * s, s, hover == Hit.Count && !Editing ? Theme.GoldBright : Theme.Gold);
                    if (Editing) Theme.Fill(g, Theme.Gold, new RectangleF(nx * s + nw * s + 1 * s, (bl - 11) * s, Math.Max(1, R(1.2f * s)), 12 * s));
                }
                float room = (num.Length > 0 ? nx - 8 : x1) - x0;
                Draw(g, CNameSt, Fit(g, CNameSt, v.Title, room * s, s), x0 * s, bl * s, s);
            }
            y += above + below + 4;
            // Sub row: bar (flex) | place, centred.
            float lh = CPlaceSt.Box;
            if (draw)
            {
                string place = v.HasTask ? v.CompactPlace : (string.IsNullOrEmpty(v.ChatLine) ? "" : v.ChatLine);
                float maxPlace = w * 0.62f;
                place = Fit(g, CPlaceSt, place, maxPlace * s, s);
                float pw = W(g, CPlaceSt, place, s) / s;
                float barW = w - (pw > 0 ? pw + 8 : 0);
                Bar(g, s, x0, y + (lh - 4) / 2f, barW, v.Progress);
                if (pw > 0) Draw(g, CPlaceSt, place, (x1 - pw) * s, (y + CPlaceSt.BaselineInBox) * s, s);
            }
            y += lh;
            monsterBlock = new RectangleF(x0 * s, blockTop * s, w * s, (y - blockTop) * s);
            y += 9 + 1;
            return y;
        }

        // ---- input ----------------------------------------------------------------------------------
        Hit HitAt(Point p)
        {
            foreach (var kv in hits) if (kv.Value.Contains(p)) return kv.Key;
            return Hit.None;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragArmed)
            {
                Point c = Control.MousePosition;
                int dx = c.X - dragStartMouse.X, dy = c.Y - dragStartMouse.Y;
                if (!dragging && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)) { dragging = true; Capture = true; }
                if (dragging)
                {
                    if (DragMoved != null) DragMoved(new Point(dragStartWindow.X + dx, dragStartWindow.Y + dy));
                    return;
                }
            }
            Hit h = HitAt(e.Location);
            bool overMonster = !compact && !minimised && RectangleF.Inflate(monsterBlock, 0, 2).Contains(e.Location);
            if (h != hover || overMonster != hoverMonster)
            {
                hover = h;
                hoverMonster = overMonster;
                Present();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (dragging) return;
            if (hover != Hit.None || hoverMonster) { hover = Hit.None; hoverMonster = false; Present(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right && e.Y < 38 * OverlayScale)
            {
                if (HeaderMenu != null) HeaderMenu(Control.MousePosition);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            pressed = HitAt(e.Location);
            if (pressed != Hit.None) { Present(); return; }
            // Not on a button: start a (possible) drag.
            dragArmed = true;
            dragging = false;
            dragStartMouse = Control.MousePosition;
            dragStartWindow = Location;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragArmed)
            {
                bool wasDragging = dragging;
                dragArmed = false; dragging = false;
                if (wasDragging) { Capture = false; if (DragEnded != null) DragEnded(); return; }
            }
            Hit h = HitAt(e.Location);
            Hit was = pressed;
            pressed = Hit.None;
            if (e.Button != MouseButtons.Left || h == Hit.None || h != was) { Present(); return; }
            if (Editing && h != Hit.Count) FinishEdit(true);
            if (h == Hit.Count) { if (!Editing) BeginEdit(); return; }
            if (Clicked != null) Clicked(h);
            Present();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (minimised || !view.HasTask || !view.CountKnown || Editing) return;
            if (!RectangleF.Inflate(monsterBlock, 0, 2).Contains(e.Location)) return;
            if (Wheel != null) Wheel(e.Delta > 0 ? 1 : -1);
        }

        // Typing a count is the one thing that needs the keyboard: the panel takes focus only after the
        // player clicks the number, and hands it back to the game when the edit ends.
        void BeginEdit()
        {
            if (!view.HasTask) return;
            editText = view.CountKnown ? view.LeftText.Replace(",", "") : "";
            allowActivate = true;
            Native.SetExStyleFlag(Handle, Native.WS_EX_NOACTIVATE, false);
            Native.SetForegroundWindow(Handle);
            Activate();
            if (EditStarted != null) EditStarted();
            Present();
        }

        public void FinishEdit(bool commit)
        {
            if (!Editing) return;
            int n;
            int? value = commit && int.TryParse(editText, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : (int?)null;
            editText = null;
            allowActivate = false;
            if (IsHandleCreated) Native.SetExStyleFlag(Handle, Native.WS_EX_NOACTIVATE, true);
            if (EditFinished != null) EditFinished(value);
            Present();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Editing)
            {
                Keys k = keyData & Keys.KeyCode;
                if (k == Keys.Enter) { FinishEdit(true); return true; }
                if (k == Keys.Escape) { FinishEdit(false); return true; }
                if (k == Keys.Back) { if (editText.Length > 0) editText = editText.Substring(0, editText.Length - 1); Present(); return true; }
                if (k == Keys.Up) { editText = Bump(editText, 1); Present(); return true; }
                if (k == Keys.Down) { editText = Bump(editText, -1); Present(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        static string Bump(string t, int d)
        {
            int n; int.TryParse(t, out n);
            return Math.Max(0, n + d).ToString(CultureInfo.InvariantCulture);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (!Editing) return;
            if (char.IsDigit(e.KeyChar) && editText.Length < 5) { editText += e.KeyChar; Present(); }
            e.Handled = true;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (dragging && !Capture) { dragArmed = false; dragging = false; if (DragEnded != null) DragEnded(); }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (Editing) FinishEdit(true);
        }

        /// <summary>Renders the panel to a bitmap without a window (tests, screenshots, --render).</summary>
        public Bitmap RenderToBitmap(float s)
        {
            Size sz = Measure(s);
            var bmp = new Bitmap(sz.Width, sz.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                Theme.Prepare(g);
                Render(g, s);
            }
            return bmp;
        }

        public void SetHoverForDemo(bool overTask) { hoverMonster = overTask; }
    }

    internal static class Look
    {
        public static readonly Color Btn = Color.FromArgb(255, 0x1a, 0x2a, 0x38);   // --btn
    }
}
