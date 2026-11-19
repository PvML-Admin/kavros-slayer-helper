using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace KavrosSlayerHelper
{
    internal sealed class OcrLine
    {
        public string Text;
        public RectangleF Rect;   // screen pixels
    }

    internal sealed class ChatRead
    {
        public List<string> Lines = new List<string>();   // top to bottom
        public bool RegionChanged;                          // the chatbox was (re)found this read
        public bool Forced;                                 // a Rescan asked for it
    }

    /// <summary>Windows' built-in OCR (Windows.Media.Ocr), the approach Zenity's Clue Helper uses
    /// (NativeClueOcr.cs, MIT). Every WinRT call has a deadline; nothing leaves the PC.
    /// No WinRT type appears in this class's fields or signatures, so it loads (and reports
    /// "unavailable") on systems without Windows 10 text recognition, such as Wine.</summary>
    internal sealed class WinOcr
    {
        object engine;          // Windows.Media.Ocr.OcrEngine
        bool initialised;
        public string Unavailable;

        public bool Ready
        {
            get
            {
                if (!initialised)
                {
                    initialised = true;
                    try
                    {
                        engine = WinRtOcr.Create();
                        if (engine == null) Unavailable = "Add English to Windows' languages so chat can be read";
                    }
                    catch (Exception ex)
                    {
                        Unavailable = "Windows text recognition is unavailable here";
                        Log.Write("ocr: unavailable: " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
                return engine != null;
            }
        }

        /// <summary>Lines in the image, in image pixels.</summary>
        public List<KeyValuePair<string, RectangleF>> Read(Bitmap image)
        {
            if (!Ready) return new List<KeyValuePair<string, RectangleF>>();
            using (var bytes = new MemoryStream())
            {
                image.Save(bytes, ImageFormat.Png);
                return WinRtOcr.Recognize(engine, bytes.ToArray());
            }
        }
    }

    /// <summary>All WinRT calls, kept apart so the runtime only resolves the Windows contracts when OCR is used.</summary>
    static class WinRtOcr
    {
        static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                while (op.Status == Windows.Foundation.AsyncStatus.Started)
                {
                    if (timer.ElapsedMilliseconds > 3000) throw new TimeoutException("Windows text recognition timed out.");
                    Thread.Sleep(2);
                }
                return op.GetResults();
            }
            catch { op.Cancel(); throw; }
            finally { op.Close(); }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static object Create()
        {
            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
                ?? Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-GB"));
            if (engine == null) engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            return engine;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static List<KeyValuePair<string, RectangleF>> Recognize(object engineObj, byte[] png)
        {
            var engine = (Windows.Media.Ocr.OcrEngine)engineObj;
            var result = new List<KeyValuePair<string, RectangleF>>();
            using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
            {
                using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(png);
                    Wait<uint>(writer.StoreAsync());
                    Wait<bool>(writer.FlushAsync());
                    writer.DetachStream();
                }
                stream.Seek(0);
                var decoder = Wait(Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream));
                using (var bmp = Wait(decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied)))
                {
                    var ocr = Wait(engine.RecognizeAsync(bmp));
                    foreach (var line in ocr.Lines)
                    {
                        float l = float.MaxValue, t = float.MaxValue, r = float.MinValue, b = float.MinValue;
                        foreach (var w in line.Words)
                        {
                            var br = w.BoundingRect;
                            l = Math.Min(l, (float)br.X); t = Math.Min(t, (float)br.Y);
                            r = Math.Max(r, (float)(br.X + br.Width)); b = Math.Max(b, (float)(br.Y + br.Height));
                        }
                        if (r > l) result.Add(new KeyValuePair<string, RectangleF>(line.Text, RectangleF.FromLTRB(l, t, r, b)));
                    }
                }
            }
            return result;
        }
    }

    /// <summary>Reads the chatbox on a background thread: captures the game area, finds the chatbox,
    /// cleans the pixels up for OCR and hands the lines (top to bottom) to the UI thread.</summary>
    internal sealed class ChatReader : IDisposable
    {
        readonly WinOcr ocr = new WinOcr();
        readonly Func<string, bool> isSlayerLine;
        readonly Action<ChatRead> deliver;          // called on the worker thread; the host marshals
        readonly Thread thread;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        volatile bool stop, rescan;
        readonly object gate = new object();
        Rectangle area;                            // game render area, screen px
        bool active;
        double[] fixedRegion;
        Rectangle locked;                          // the chatbox, relative to the area's top-left
        int emptyReads, searchTurn;
        public int IntervalMs = 1000;
        public volatile string Status = "Looking for the chatbox\u2026";

        static readonly Regex StampRe = new Regex(@"^\s*\[\s*\d{1,2}\s*[:.]\s*\d{2}", RegexOptions.CultureInvariant);

        public ChatReader(Func<string, bool> isSlayerLine, Action<ChatRead> deliver, double[] fixedRegion)
        {
            this.isSlayerLine = isSlayerLine;
            this.deliver = deliver;
            this.fixedRegion = fixedRegion;
            thread = new Thread(Loop) { IsBackground = true, Name = "chat reader" };
            thread.SetApartmentState(ApartmentState.MTA);
        }

        public void Start() { thread.Start(); }

        /// <summary>Called by the UI whenever the game moves, or comes and goes from the foreground.</summary>
        public void SetArea(Rectangle gameArea, bool gameActive)
        {
            lock (gate)
            {
                if (gameArea.Size != area.Size && locked != Rectangle.Empty) locked = Rectangle.Empty;   // resized: find it again
                area = gameArea;
                active = gameActive;
            }
        }

        Rectangle exclude;

        /// <summary>The panel's own screen rectangle: text read inside it is ignored (the panel quotes chat).</summary>
        public void SetExclude(Rectangle r) { lock (gate) exclude = r; }

        public void Rescan()
        {
            rescan = true;
            lock (gate) locked = Rectangle.Empty;
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
                if (a.Width < 100 || a.Height < 100 || (!act && !forced)) continue;
                rescan = false;
                try { ScanOnce(a, forced); }
                catch (Exception ex)
                {
                    Status = "Could not read chat (" + ex.GetType().Name + ")";
                    Log.Write("chat: scan failed: " + ex);
                    Thread.Sleep(2000);
                }
            }
        }

        void ScanOnce(Rectangle a, bool forced)
        {
            if (!ocr.Ready) { Status = ocr.Unavailable ?? "Windows text recognition is unavailable"; Thread.Sleep(5000); return; }
            Rectangle region; float scale; bool searching = false;
            if (fixedRegion != null)
            {
                region = new Rectangle((int)(a.Width * fixedRegion[0]), (int)(a.Height * fixedRegion[1]), (int)(a.Width * fixedRegion[2]), (int)(a.Height * fixedRegion[3]));
                scale = 2f;
            }
            else
            {
                Rectangle l; lock (gate) l = locked;
                if (l != Rectangle.Empty) { region = l; scale = 2f; }
                else
                {
                    searching = true;
                    // Most layouts keep chat bottom-left; every third look (and on Rescan) covers the whole game.
                    bool whole = forced || (searchTurn++ % 3) == 2;
                    region = whole ? new Rectangle(0, 0, a.Width, a.Height) : new Rectangle(0, a.Height * 2 / 5, a.Width * 3 / 5, a.Height - a.Height * 2 / 5);
                    scale = whole ? 1.5f : 2f;
                }
            }
            region.Intersect(new Rectangle(0, 0, a.Width, a.Height));
            if (region.Width < 40 || region.Height < 20) { lock (gate) locked = Rectangle.Empty; return; }

            List<OcrLine> lines = ReadRegion(new Rectangle(a.X + region.X, a.Y + region.Y, region.Width, region.Height), scale);
            Rectangle ex; lock (gate) ex = exclude;
            if (ex.Width > 0) lines = lines.Where(x => !RectangleF.Inflate(ex, 4, 4).IntersectsWith(x.Rect)).ToList();
            var read = new ChatRead { Forced = forced };
            if (searching)
            {
                List<OcrLine> anchors = lines.Where(x => isSlayerLine(x.Text)).ToList();
                List<OcrLine> stamps = lines.Where(x => StampRe.IsMatch(x.Text)).ToList();
                if (anchors.Count == 0 && stamps.Count >= 3) anchors = stamps;
                if (anchors.Count == 0)
                {
                    Status = "Looking for the chatbox\u2026";
                    if (forced) deliver(read);
                    return;
                }
                Rectangle box = ChatBox(lines, anchors, a);
                lock (gate) locked = box;
                read.RegionChanged = true;
                var rel = new RectangleF(a.X + box.X, a.Y + box.Y, box.Width, box.Height);
                lines = lines.Where(x => rel.Contains(new PointF(x.Rect.X + 1, x.Rect.Y + x.Rect.Height / 2))).ToList();
                Log.Write("chat: chatbox at " + box + " (game " + a.Size + ")");
            }
            read.Lines = lines.OrderBy(x => x.Rect.Y).Select(x => x.Text).ToList();
            if (read.Lines.Count == 0)
            {
                if (++emptyReads >= 20 && fixedRegion == null) { lock (gate) locked = Rectangle.Empty; emptyReads = 0; Status = "Looking for the chatbox\u2026"; }
            }
            else { emptyReads = 0; Status = ""; }
            deliver(read);
        }

        /// <summary>The chatbox around the anchor lines: chat is left-aligned, so take every line that starts
        /// near the anchors' left edge, then pad. Relative to the game area.</summary>
        static Rectangle ChatBox(List<OcrLine> all, List<OcrLine> anchors, Rectangle a)
        {
            float left = anchors.Min(x => x.Rect.Left);
            float lineH = Math.Max(10, anchors.Average(x => x.Rect.Height));
            var col = all.Where(x => Math.Abs(x.Rect.Left - left) < lineH * 3).ToList();
            float top = col.Min(x => x.Rect.Top), bottom = col.Max(x => x.Rect.Bottom);
            float right = col.Max(x => x.Rect.Right);
            float anchorBottom = anchors.Max(x => x.Rect.Bottom);
            top = Math.Min(top, anchorBottom - lineH * 14);
            bottom = Math.Max(bottom, anchorBottom + lineH * 2.5f);
            right = Math.Max(right, left + 380);
            var r = Rectangle.FromLTRB((int)(left - lineH * 1.5f) - a.X, (int)top - a.Y, (int)(right + lineH * 4) - a.X, (int)bottom - a.Y);
            r.Intersect(new Rectangle(0, 0, a.Width, a.Height));
            return r;
        }

        List<OcrLine> ReadRegion(Rectangle screen, float scale)
        {
            using (Bitmap cap = Capture(screen))
            using (Bitmap prepared = Prepare(cap, scale))
            {
                var r = new List<OcrLine>();
                foreach (var kv in ocr.Read(prepared))
                {
                    RectangleF b = kv.Value;
                    r.Add(new OcrLine { Text = kv.Key, Rect = new RectangleF(screen.X + b.X / scale, screen.Y + b.Y / scale, b.Width / scale, b.Height / scale) });
                }
                return r;
            }
        }

        static Bitmap Capture(Rectangle r)
        {
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb);
            try
            {
                using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size, CopyPixelOperation.SourceCopy);
                return bmp;
            }
            catch { bmp.Dispose(); throw; }
        }

        /// <summary>Upscales, then turns coloured chat text on the dark, see-through chat background into dark
        /// text on white: brightness = the strongest colour channel, stretched, inverted. Coloured game
        /// messages (red, green, purple, gold) all come out as solid text this way.</summary>
        public static Bitmap Prepare(Bitmap src, float scale)
        {
            int w = Math.Max(1, (int)(src.Width * scale)), h = Math.Max(1, (int)(src.Height * scale));
            var big = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(big))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h));
            }
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
                        int v = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2]));
                        px[i] = (byte)v;
                        hist[v]++;
                    }
                // Background level: the 55th percentile; anything well above it is text.
                int total = w * h, acc = 0, bg = 0;
                for (int v = 0; v < 256; v++) { acc += hist[v]; if (acc >= total * 55 / 100) { bg = v; break; } }
                int lo = Math.Min(230, bg + 28), hi = Math.Min(255, lo + 90);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * stride + x * 3;
                        int v = px[i];
                        int t = v <= lo ? 0 : v >= hi ? 255 : (v - lo) * 255 / (hi - lo);
                        byte o = (byte)(255 - t);
                        px[i] = o; px[i + 1] = o; px[i + 2] = o;
                    }
                Marshal.Copy(px, 0, d.Scan0, px.Length);
            }
            finally { big.UnlockBits(d); }
            return big;
        }

        public void Dispose()
        {
            stop = true;
            wake.Set();
        }
    }
}
