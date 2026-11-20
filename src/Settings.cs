using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KavrosSlayerHelper.Core;

namespace KavrosSlayerHelper
{
    /// <summary>App settings in %AppData%\KavrosSlayerHelper\settings.json. Missing values use the defaults below.</summary>
    internal sealed class Settings
    {
        public string ProcessName = "kavrosclient";
        public string WindowTitle = "";
        public string DebugAttachTitle = "";
        public bool Compact;
        public bool Minimised;
        public int OffsetX = 12;            // panel position inside the game, px at 100 % (set by dragging the panel)
        public int OffsetY = 12;            // below the title bar
        public int FreeX = int.MinValue;    // position when no game is open, screen px (set by dragging)
        public int FreeY = int.MinValue;
        public int TitleBarHeight;          // 0 = the window's own caption, or 32 for a borderless client
        public int ScanMs = 1000;           // counter / dialogue read interval
        public double[] CounterRegion;      // optional fixed slayer counter: x, y, w, h as fractions of the game area
        public bool HideFromCapture;

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KavrosSlayerHelper"); }
        }
        public static string FilePath { get { return Path.Combine(Folder, "settings.json"); } }
        public static string StatePath { get { return Path.Combine(Folder, "state.json"); } }
        public static string OverridePath { get { return Path.Combine(Folder, "slayer.override.json"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var o = MiniJson.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as JObj;
                if (o == null) return s;
                s.ProcessName = o.Str("processName", s.ProcessName);
                s.WindowTitle = o.Str("windowTitle", "");
                s.DebugAttachTitle = o.Str("debugAttachTitle", "");
                s.Compact = o.Bool("compact");
                s.Minimised = o.Bool("minimised");
                s.OffsetX = Clamp(o.Int("offsetX", 12), 0, 4000);
                s.OffsetY = Clamp(o.Int("offsetY", 12), 0, 4000);
                if (o.Has("freeX") && o.Has("freeY")) { s.FreeX = Clamp(o.Int("freeX", 0), -20000, 20000); s.FreeY = Clamp(o.Int("freeY", 0), -20000, 20000); }
                s.TitleBarHeight = Clamp(o.Int("titleBarHeight", 0), 0, 200);
                s.ScanMs = Clamp(o.Int("scanMs", 1000), 300, 10000);
                s.HideFromCapture = o.Bool("hideFromCapture");
                List<object> r = o.Arr("counterRegion");
                if (r != null && r.Count == 4 && r.TrueForAll(x => x is double))
                    s.CounterRegion = new[] { (double)r[0], (double)r[1], (double)r[2], (double)r[3] };
            }
            catch (Exception ex) { Log.Write("settings: could not read, using defaults: " + ex.Message); }
            if (string.IsNullOrWhiteSpace(s.ProcessName)) s.ProcessName = "kavrosclient";
            s.ProcessName = s.ProcessName.Trim();
            if (s.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s.ProcessName = s.ProcessName.Substring(0, s.ProcessName.Length - 4);
            return s;
        }

        static int Clamp(int v, int lo, int hi) { return Math.Max(lo, Math.Min(hi, v)); }

        public void Save()
        {
            try
            {
                var o = new JObj();
                o["processName"] = ProcessName; o["windowTitle"] = WindowTitle; o["debugAttachTitle"] = DebugAttachTitle;
                o["compact"] = Compact; o["minimised"] = Minimised;
                o["offsetX"] = (double)OffsetX; o["offsetY"] = (double)OffsetY; o["titleBarHeight"] = (double)TitleBarHeight;
                if (FreeX != int.MinValue) { o["freeX"] = (double)FreeX; o["freeY"] = (double)FreeY; }
                o["scanMs"] = (double)ScanMs;
                o["hideFromCapture"] = HideFromCapture;
                o["counterRegion"] = CounterRegion == null ? null : (object)new List<object> { CounterRegion[0], CounterRegion[1], CounterRegion[2], CounterRegion[3] };
                Directory.CreateDirectory(Folder);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, MiniJson.Write(o), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Write("settings: could not save: " + ex.Message); }
        }
    }

    internal static class Log
    {
        static readonly object gate = new object();
        public static bool Verbose;

        public static void Write(string line)
        {
            string text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + line;
            if (Verbose) Console.WriteLine(text);
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Settings.Folder);
                    string path = Path.Combine(Settings.Folder, "slayer-helper.log");
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                    File.AppendAllText(path, text + Environment.NewLine);
                }
            }
            catch (Exception) { }
        }
    }
}
