using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using KavrosSlayerHelper.Core;

namespace KavrosSlayerHelper
{
    internal sealed class StartupOptions
    {
        public string AttachTitle;
        public bool Verbose, Demo, NoOcr;
    }

    /// <summary>Hosts the panel: follows the game window, reads chat, applies what it reads, saves the state.
    /// There is no tray icon; the panel itself is the app (Companion's toggle starts and stops it).</summary>
    internal sealed class App : ApplicationContext
    {
        readonly Settings settings;
        SlayerData data;
        ChatParser parser;
        SlayerTracker tracker;
        readonly SlayerPanel panel;
        readonly ClientTracker client;
        readonly ChatReader reader;
        readonly Timer saveTimer = new Timer { Interval = 800 };
        readonly Timer statusTimer = new Timer { Interval = 1500 };
        readonly bool demo;
        FileSystemWatcher watcher;
        Timer reloadTimer;

        List<string> prevLines;
        readonly Dictionary<string, DateTime> seen = new Dictionary<string, DateTime>();
        bool startupGrace = true;
        bool everRead;
        string lastStatus = "";

        public App(StartupOptions opts)
        {
            settings = Settings.Load();
            demo = opts.Demo;
            data = LoadData();
            parser = NewParser(data);
            SlayerState state = demo ? DemoState() : SlayerState.Load(Settings.StatePath);
            tracker = new SlayerTracker(data, state);

            panel = new SlayerPanel { Compact = settings.Compact, Minimised = settings.Minimised };
            panel.Clicked += OnPanelClick;
            panel.Wheel += d => { if (tracker.Adjust(d)) Changed(); };
            panel.EditFinished += OnEditFinished;
            if (settings.HideFromCapture) panel.HandleCreated += (s, e) => ExcludeFromCapture(panel.Handle);

            client = new ClientTracker(() => settings, IsOwnWindow) { DebugTitle = opts.AttachTitle };
            client.Changed += Relayout;
            client.ForegroundChanged += OnForegroundChanged;

            reader = new ChatReader(line => parser.Parse(line) != null, OnChatReadWorker, settings.ChatRegion) { IntervalMs = settings.ScanMs };
            panel.Move += (s, e) => reader.SetExclude(panel.Visible ? panel.Bounds : Rectangle.Empty);
            panel.Resize += (s, e) => reader.SetExclude(panel.Visible ? panel.Bounds : Rectangle.Empty);
            panel.VisibleChanged += (s, e) => reader.SetExclude(panel.Visible ? panel.Bounds : Rectangle.Empty);
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveState(); };
            statusTimer.Tick += (s, e) => RefreshStatus();

            Refresh();
            client.Start();
            Relayout();
            if (!opts.NoOcr) reader.Start();
            statusTimer.Start();
            WatchOverride();
            Log.Write("started " + Program.Version + (data.OverrideLoaded ? " (with " + Settings.OverridePath + ")" : ""));
            if (data.OverrideError != null) Log.Write("override: " + data.OverrideError);
            foreach (string e in parser.Errors) Log.Write("chat pattern ignored: " + e);
        }

        // ---- data --------------------------------------------------------------------------------
        static SlayerData LoadData()
        {
            string json = Encoding.UTF8.GetString(Resources.Bytes("slayer.json") ?? Encoding.UTF8.GetBytes("{}"));
            return SlayerData.LoadFiles(json, Settings.OverridePath);
        }

        static ChatParser NewParser(SlayerData d) { return new ChatParser(d); }

        void WatchOverride()
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                reloadTimer = new Timer { Interval = 600 };
                reloadTimer.Tick += (s, e) =>
                {
                    reloadTimer.Stop();
                    data = LoadData();
                    parser = NewParser(data);
                    // The tracker keeps its state; point it at the new data.
                    tracker = new SlayerTracker(data, tracker.State);
                    Log.Write("override reloaded" + (data.OverrideError != null ? ": " + data.OverrideError : ""));
                    Refresh();
                };
                watcher = new FileSystemWatcher(Settings.Folder, "slayer.override.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
                FileSystemEventHandler h = (s, e) => panel.BeginInvoke((Action)(() => { reloadTimer.Stop(); reloadTimer.Start(); }));
                watcher.Changed += h; watcher.Created += h; watcher.Deleted += h;
                watcher.Renamed += (s, e) => panel.BeginInvoke((Action)(() => { reloadTimer.Stop(); reloadTimer.Start(); }));
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log.Write("override: cannot watch: " + ex.Message); }
        }

        static SlayerState DemoState()
        {
            return new SlayerState
            {
                Monster = "Abyssal demons", Master = "Kuradal", Assigned = 187, Left = 142, Streak = 47, Points = 1240,
                LastChat = "only 142 more to go", LastSource = "chat", Updated = DateTime.Now,
            };
        }

        // ---- chat ----------------------------------------------------------------------------------
        void OnChatReadWorker(ChatRead r)
        {
            try { panel.BeginInvoke((Action)(() => OnChatRead(r))); } catch (Exception) { }
        }

        void OnChatRead(ChatRead r)
        {
            DateTime now = DateTime.Now;
            foreach (string k in seen.Where(kv => (now - kv.Value).TotalMinutes > 15).Select(kv => kv.Key).ToList()) seen.Remove(k);

            List<string> fresh;
            if (r.Forced) fresh = r.Lines;                                  // Rescan: everything on screen
            else if (!everRead && tracker.State.HasTask) fresh = new List<string>();   // first look: baseline only
            else if (r.RegionChanged || prevLines == null)
                fresh = r.Lines.Where(l => !seen.ContainsKey(SlayerData.Norm(l))).ToList();
            else fresh = LineDiff.NewLines(prevLines, r.Lines);
            everRead = true;
            prevLines = r.Lines;

            bool changed = false;
            for (int i = 0; i < r.Lines.Count; i++)
            {
                string line = r.Lines[i];
                seen[SlayerData.Norm(line)] = now;
            }
            for (int i = 0; i < fresh.Count; i++)
            {
                string line = fresh[i];
                ChatEvent e = parser.Parse(line);
                if (e == null)
                {
                    // A long message wraps onto two lines: try it joined with the line above.
                    int at = r.Lines.IndexOf(line);
                    if (at > 0) e = parser.Parse(r.Lines[at - 1] + " " + line);
                }
                if (e == null) continue;
                Log.Write("chat: " + e + "  <- \"" + e.Line + "\"");
                if (tracker.Apply(e)) changed = true;
            }
            if (changed) Changed();
        }

        void RefreshStatus()
        {
            string st = reader.Status ?? "";
            if (st != lastStatus) { lastStatus = st; Refresh(); }
        }

        // ---- panel ----------------------------------------------------------------------------------
        void OnPanelClick(SlayerPanel.Hit h)
        {
            switch (h)
            {
                case SlayerPanel.Hit.Mode:
                    settings.Compact = !settings.Compact; panel.Compact = settings.Compact; settings.Save(); Relayout(); break;
                case SlayerPanel.Hit.Minimise:
                    settings.Minimised = !settings.Minimised; panel.Minimised = settings.Minimised; settings.Save(); Relayout(); break;
                case SlayerPanel.Hit.Rescan:
                    reader.Rescan(); lastStatus = "Reading chat\u2026"; Refresh(); break;
                case SlayerPanel.Hit.Clear:
                    if (tracker.Clear()) Changed(); break;
                case SlayerPanel.Hit.Minus: if (tracker.Adjust(-1)) Changed(); break;
                case SlayerPanel.Hit.Plus: if (tracker.Adjust(1)) Changed(); break;
            }
        }

        void OnEditFinished(int? value)
        {
            if (value.HasValue && tracker.SetLeft(value.Value)) Changed(); else Refresh();
            // Give the keyboard back to the game.
            if (client.State.Handle != IntPtr.Zero) Native.SetForegroundWindow(client.State.Handle);
        }

        void Changed()
        {
            Refresh();
            if (!demo) { saveTimer.Stop(); saveTimer.Start(); }
        }

        void SaveState()
        {
            try { tracker.State.Save(Settings.StatePath); }
            catch (Exception ex) { Log.Write("state: could not save: " + ex.Message); }
        }

        void Refresh()
        {
            string status = lastStatus;
            if (data.OverrideError != null) status = "Override file error: " + data.OverrideError;
            panel.SetView(PanelView.Build(tracker, status));
            Relayout();
        }

        bool IsOwnWindow(IntPtr h) { return panel.IsHandleCreated && h == panel.Handle; }

        void OnForegroundChanged()
        {
            IntPtr fg = Native.GetForegroundWindow();
            ClientState st = client.State;
            if (fg != IntPtr.Zero && !st.Active) startupGrace = false;
            // WS_EX_NOACTIVATE belt and braces: if the panel ends up in front without an edit, hand focus back.
            if (IsOwnWindow(fg) && !panel.Editing && st.Visible) Native.SetForegroundWindow(st.Handle);
            Relayout();
        }

        void Relayout()
        {
            ClientState st = client.State;
            if (st.Visible) reader.SetArea(GameArea(st), st.Active);
            else reader.SetArea(Rectangle.Empty, false);

            if (!st.Attached)
            {
                // No game: stay on screen (not on top of other apps) so the helper is never just "somewhere".
                float s0 = Native.DpiFor(IntPtr.Zero) / 96f;
                panel.SetScale(s0);
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                panel.PlaceAt(new Point(wa.Left + (int)Math.Round(12 * s0), wa.Top + (int)Math.Round(12 * s0)));
                panel.ShowPassive();
                panel.SetTopMost(false);
                return;
            }
            bool show = st.Visible && (st.Active || startupGrace || panel.Editing);
            if (!show) { panel.HidePassive(); return; }
            float s = st.Dpi / 96f;
            panel.SetScale(s);
            Rectangle area = GameArea(st);
            panel.PlaceAt(new Point(area.Left + (int)Math.Round(settings.OffsetX * s), area.Top + (int)Math.Round(settings.OffsetY * s)));
            panel.ShowPassive();
            panel.SetTopMost(true);
        }

        /// <summary>The game's render area: the client area, minus the Kavros client's own 32px title bar
        /// when the window is borderless (same rule as Companion's title-bar strip).</summary>
        Rectangle GameArea(ClientState st)
        {
            float s = st.Dpi / 96f;
            Rectangle f = st.Frame, c = st.Client;
            int nativeCaption = c.Top - f.Top;
            int bar;
            if (settings.TitleBarHeight > 0) bar = Math.Max(0, (int)Math.Round(settings.TitleBarHeight * s) - nativeCaption);
            else if (nativeCaption >= 16 * s) bar = 0;
            else bar = (int)Math.Round(32 * s);
            return new Rectangle(c.Left, c.Top + bar, c.Width, Math.Max(0, c.Height - bar));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        /// <summary>Optional (settings "hideFromCapture"): keeps the panel out of screen captures (Windows 10 2004+),
        /// so it never hides chat lines underneath it. Off by default because it also hides it from screenshots.</summary>
        static void ExcludeFromCapture(IntPtr h)
        {
            try { SetWindowDisplayAffinity(h, 0x11); } catch (Exception) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                reader.Dispose();
                client.Dispose();
                if (watcher != null) watcher.Dispose();
                if (!demo) SaveState();
            }
            base.Dispose(disposing);
        }
    }
}
