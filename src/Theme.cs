using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace KavrosSlayerHelper
{
    /// <summary>Kavros navy/gold palette and the embedded fonts. Sizes are CSS pixels at 96 DPI;
    /// callers multiply by the current scale.</summary>
    internal static class Theme
    {
        public static readonly Color Bg = Hex(0x0a1219);
        public static readonly Color BgDeep = Hex(0x070d13);
        public static readonly Color BgRaise = Hex(0x0f1a23);
        public static readonly Color BgPanel = Hex(0x13212c);
        public static readonly Color BgHover = Hex(0x182a37);
        public static readonly Color BgInput = Hex(0x0c161e);
        public static readonly Color Line = Hex(0x2a4660);
        public static readonly Color LineSoft = Color.FromArgb(41, 160, 196, 218);
        public static readonly Color Text = Hex(0xe3eef4);
        public static readonly Color TextSoft = Hex(0xa3b8c6);
        public static readonly Color TextFaint = Hex(0x8fa7b6);
        public static readonly Color Gold = Hex(0xe8c36a);
        public static readonly Color GoldEdge = Hex(0xc9a64e);
        public static readonly Color GoldInk = Hex(0x1a1408);
        public static readonly Color Mint = Hex(0x87dcb5);
        public static readonly Color Warn = Hex(0xecd49a);
        public static readonly Color Error = Hex(0xf0a08c);
        public static readonly Color ButtonLine = Hex(0x3d5a72);
        public static readonly Color Frame = Hex(0x24384a);
        public static readonly Color Pipe = Hex(0x5a7180);
        public static readonly Color TrackBg = Hex(0x1c2c38);
        public static readonly Color ToggleBg = Hex(0x152029);
        public static readonly Color StripBg = Color.FromArgb(140, 8, 14, 20);
        public static readonly Color StripBgHover = Color.FromArgb(200, 24, 42, 55);
        public static readonly Color StripBgPressed = Color.FromArgb(220, 19, 33, 44);
        // Companion button: the launcher's panel navy (--kv-bg-panel #13212c) and hover navy
        // (--kv-bg-hover #182a37) from kavros-launcher brand/theme.css, solid, with a gold edge.
        public static readonly Color ButtonNavy = Hex(0x13212c);
        public static readonly Color ButtonNavyHover = Hex(0x182a37);
        public static readonly Color ButtonNavyPressed = Hex(0x0f1a23);
        public static readonly Color GoldBright = Hex(0xf6d98c);
        public static readonly Color WidgetBg = Color.FromArgb(235, 10, 18, 25);
        public static readonly Color WidgetBgOpen = Color.FromArgb(245, 19, 33, 44);
        public static readonly Color GoldBorderOpen = Color.FromArgb(140, 232, 195, 106);

        static Color Hex(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff); }

        // ---- fonts -------------------------------------------------------------------------
        public enum Face { Display, Regular, Medium, SemiBold }

        static readonly List<PrivateFontCollection> collections = new List<PrivateFontCollection>();
        static readonly Dictionary<Face, FontFamily> families = new Dictionary<Face, FontFamily>();
        static readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();
        static bool loaded;

        static void EnsureFonts()
        {
            if (loaded) return;
            loaded = true;
            Load(Face.Display, "KavrosDisplay.ttf");
            Load(Face.Regular, "KavrosUI-Regular.ttf");
            Load(Face.Medium, "KavrosUI-Medium.ttf");
            Load(Face.SemiBold, "KavrosUI-SemiBold.ttf");
        }

        static void Load(Face face, string resource)
        {
            try
            {
                byte[] bytes = Resources.Bytes(resource);
                if (bytes == null) return;
                // The memory must outlive the collection, so it is never freed (process lifetime).
                IntPtr mem = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, mem, bytes.Length);
                var pfc = new PrivateFontCollection();
                pfc.AddMemoryFont(mem, bytes.Length);
                uint n = 0;
                try { Native.AddFontMemResourceEx(mem, (uint)bytes.Length, IntPtr.Zero, ref n); } catch (Exception) { }
                collections.Add(pfc);
                if (pfc.Families.Length > 0) families[face] = pfc.Families[0];
            }
            catch (Exception) { }
        }

        /// <summary>Font sized in device pixels.</summary>
        public static Font Font(Face face, float px)
        {
            px = (float)Math.Round(px * 4) / 4f;
            string key = face + ":" + px;
            Font f;
            if (fontCache.TryGetValue(key, out f)) return f;
            EnsureFonts();
            FontFamily fam;
            if (families.TryGetValue(face, out fam))
                f = new Font(fam, px, FontStyle.Regular, GraphicsUnit.Pixel);
            else if (face == Face.Display)
                f = new Font("Georgia", px, FontStyle.Bold, GraphicsUnit.Pixel);
            else
                f = new Font("Segoe UI", px, face == Face.Regular ? FontStyle.Regular : FontStyle.Bold, GraphicsUnit.Pixel);
            fontCache[key] = f;
            return f;
        }

        public static bool FontsEmbedded { get { EnsureFonts(); return families.Count == 4; } }

        // ---- images ------------------------------------------------------------------------
        static Bitmap crest, crestSmall;
        public static Bitmap Crest
        {
            get
            {
                if (crest == null) crest = LoadPng("crest.png") ?? new Bitmap(1, 1);
                return crest;
            }
        }

        /// <summary>The crest image to draw at <paramref name="px"/> device pixels: a pre-sharpened 32 px
        /// version for small sizes (strip, header at 100-150 %), the 128 px one above that.</summary>
        public static Bitmap CrestFor(float px)
        {
            if (px <= 32)
            {
                if (crestSmall == null) crestSmall = LoadPng("crest-32.png") ?? Crest;
                return crestSmall;
            }
            return Crest;
        }

        static readonly System.Collections.Generic.Dictionary<string, Bitmap> moduleIcons = new System.Collections.Generic.Dictionary<string, Bitmap>();

        /// <summary>A full-colour module icon (assets/icon-&lt;name&gt;-24/48.png) for drawing at <paramref name="px"/>
        /// device pixels: the 24 px cut at 100 %, the 48 px one when scaled up. Null if there is none.</summary>
        public static Bitmap ModuleIconFor(string name, float px)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string key = "icon-" + name + "-" + (px <= 28 ? 24 : 48) + ".png";
            Bitmap b;
            if (!moduleIcons.TryGetValue(key, out b)) { b = LoadPng(key); moduleIcons[key] = b; }
            return b;
        }

        static Bitmap LoadPng(string name)
        {
            byte[] b = Resources.Bytes(name);
            return b != null ? new Bitmap(new MemoryStream(b)) : null;
        }

        public static Icon AppIcon(bool greyed)
        {
            byte[] b = Resources.Bytes("KavrosSlayerHelper.ico");
            Icon baseIcon = b != null ? new Icon(new MemoryStream(b), 16, 16) : SystemIcons.Application;
            if (!greyed) return baseIcon;
            using (var src = baseIcon.ToBitmap())
            using (var bmp = new Bitmap(src.Width, src.Height))
            using (var g = Graphics.FromImage(bmp))
            {
                var m = new System.Drawing.Imaging.ColorMatrix(new[]
                {
                    new[] { .3f, .3f, .3f, 0f, 0f }, new[] { .59f, .59f, .59f, 0f, 0f }, new[] { .11f, .11f, .11f, 0f, 0f },
                    new[] { 0f, 0f, 0f, .55f, 0f }, new[] { 0f, 0f, 0f, 0f, 1f }
                });
                using (var ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    ia.SetColorMatrix(m);
                    g.DrawImage(src, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        // ---- drawing helpers ---------------------------------------------------------------
        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.CompositingQuality = CompositingQuality.HighQuality;
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            if (radius <= 0.5f) { p.AddRectangle(r); return p; }
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static readonly StringFormat typo = MakeFormat();
        static StringFormat MakeFormat()
        {
            var f = (StringFormat)StringFormat.GenericTypographic.Clone();
            f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            return f;
        }

        public static SizeF Measure(Graphics g, string text, Font font, float tracking = 0)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;
            if (Math.Abs(tracking) < 0.5f)
            {
                if (text.IndexOf(' ') < 0) return g.MeasureString(text, font, PointF.Empty, typo);
                string[] words = text.Split(' ');
                float total = 0;
                for (int i = 0; i < words.Length; i++)
                {
                    if (i > 0) total += SpaceWidth(g, font);
                    if (words[i].Length > 0) total += g.MeasureString(words[i], font, PointF.Empty, typo).Width;
                }
                return new SizeF(total, font.GetHeight(g));
            }
            float w = 0;
            foreach (char c in text) w += g.MeasureString(c.ToString(), font, PointF.Empty, typo).Width + tracking;
            return new SizeF(w - tracking, font.GetHeight(g));
        }

        /// <summary>Inter-word gap: the fonts' own space advance (Outfit 0.2 em, Cinzel 0.25 em), which GDI+
        /// typographic layout would otherwise squeeze to a pixel or two.</summary>
        static float SpaceWidth(Graphics g, Font font)
        {
            float own = g.MeasureString("a a", font, PointF.Empty, typo).Width - g.MeasureString("aa", font, PointF.Empty, typo).Width;
            return Math.Max(own, font.Size * (font.FontFamily.Name.IndexOf("Display", StringComparison.OrdinalIgnoreCase) >= 0 ? 0.25f : 0.205f));
        }

        /// <summary>Draws text with its line box vertically centred on <paramref name="midY"/>.</summary>
        public static void DrawText(Graphics g, string text, Font font, Color color, float x, float midY, float tracking = 0)
        {
            if (string.IsNullOrEmpty(text)) return;
            float h = font.GetHeight(g);
            float y = midY - h / 2f;
            using (var b = new SolidBrush(color))
            {
                if (Math.Abs(tracking) < 0.5f)
                {
                    if (text.IndexOf(' ') < 0) { g.DrawString(text, font, b, x, y, typo); return; }
                    // Word by word: GDI+ typographic layout squeezes spaces to a pixel or two at small sizes.
                    string[] words = text.Split(' ');
                    for (int i = 0; i < words.Length; i++)
                    {
                        if (i > 0) x += SpaceWidth(g, font);
                        if (words[i].Length == 0) continue;
                        g.DrawString(words[i], font, b, x, y, typo);
                        x += g.MeasureString(words[i], font, PointF.Empty, typo).Width;
                    }
                    return;
                }
                foreach (char c in text)
                {
                    string s = c.ToString();
                    g.DrawString(s, font, b, x, y, typo);
                    x += g.MeasureString(s, font, PointF.Empty, typo).Width + tracking;
                }
            }
        }

        public static void Fill(Graphics g, Color c, RectangleF r)
        {
            using (var b = new SolidBrush(c)) g.FillRectangle(b, r);
        }

        public static void Border(Graphics g, Color c, RectangleF r, float width)
        {
            using (var p = new Pen(c, width) { Alignment = PenAlignment.Inset }) g.DrawRectangle(p, r.X, r.Y, r.Width - width * 0f, r.Height - width * 0f);
        }

        /// <summary>A 1px inset frame plus the 2px gold top rule used on every Kavros widget.</summary>
        public static void WidgetFrame(Graphics g, RectangleF r, float s, Color fill, Color border)
        {
            Fill(g, fill, r);
            float w = Math.Max(1f, (float)Math.Round(s));
            using (var p = new Pen(border, w)) g.DrawRectangle(p, r.X + w / 2f, r.Y + w / 2f, r.Width - w, r.Height - w);
            Fill(g, Gold, new RectangleF(r.X, r.Y, r.Width, Math.Max(2f, (float)Math.Round(2 * s))));
        }
    }

    internal static class Resources
    {
        public static byte[] Bytes(string name)
        {
            using (Stream s = typeof(Resources).Assembly.GetManifestResourceStream("KavrosSlayerHelper.Assets." + name))
            {
                if (s == null) return null;
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}
