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
    /// <summary>Windows' built-in OCR (Windows.Media.Ocr), the approach Zenity's Clue Helper uses
    /// (NativeClueOcr.cs, MIT). Every WinRT call has a deadline; nothing leaves the PC.
    /// No WinRT type appears in this class's fields or signatures, so it loads (and reports
    /// "unavailable") on systems without Windows 10 text recognition, such as Wine.</summary>
    internal sealed class WinOcr
    {
        object engine;          // Windows.Media.Ocr.OcrEngine
        bool initialised;
        public string Unavailable;
        int maxDimension;

        /// <summary>The largest image side Windows OCR accepts (OcrEngine.MaxImageDimension, 2600 on current Windows).</summary>
        public int MaxDimension
        {
            get
            {
                if (maxDimension == 0)
                {
                    try { maxDimension = Ready ? (int)WinRtOcr.MaxDimension() : 2600; }
                    catch (Exception) { maxDimension = 2600; }
                    if (maxDimension <= 0) maxDimension = 2600;
                }
                return maxDimension;
            }
        }

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
                        if (engine == null) Unavailable = "Add English to Windows' languages so the counter can be read";
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
        public static uint MaxDimension() { return Windows.Media.Ocr.OcrEngine.MaxImageDimension; }

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
}
