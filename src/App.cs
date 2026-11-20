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

    /// <summary>Hosts the panel: follows the game window, reads the slayer counter and the master's dialogue,
    /// applies what it reads, saves the state. There is no tray icon; the panel itself is the app
    /// (Companion's toggle starts and stops it).</summary>
    internal sealed class App : ApplicationContext
    {
        readonly Settings settings;
        SlayerData data;
        ChatParser parser;
        SlayerTracker tracker;
        readonly SlayerPanel panel;
        readonly ClientTracker client;
        readonly WidgetReader reader;
        readonly CounterFilter counterFilter = new CounterFilter();
        readonly Timer saveTimer = new Timer { Interval = 800 };
        readonly Timer statusTimer = new Timer { Interval = 1500 };
        readonly bool demo;
        FileSystemWatcher watcher;
        Timer reloadTimer;
        volatile bool confirmWanted;
        bool exiting;

        string lastDialogue;            // the dialogue text already applied (until the box closes)
        string lastStatus = "";
        readonly Dictionary<uint, bool> helperPids = new Dictionary<uint, bool>();
        DateTime helperPidsAt = DateTime.MinValue;

        /// <summary>Kavros helper apps whose windows count as "the game" for staying visible.</summary>
        static readonly string[] HelperProcesses = { "KavrosCompanion", "KavrosClueHelper", "KavrosAfkWarden", "KavrosSlayerHelper", "KavrosOverlay" };

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
            panel.HeaderMenu += OnHeaderMenu;
            panel.Wheel += d => { if (tracker.Adjust(d)) Changed(); };
            panel.EditFinished += OnEditFinished;
            panel.DragMoved += OnDragMoved;
            panel.DragEnded += () => settings.Save();
            // Companion's toggle (or anything else) closing the window: vanish at once, then exit.
            panel.CloseRequested += () => ExitNow("close requested");
            panel.FormClosing += (s, e) => { if (!exiting) { e.Cancel = true; ExitNow("window closed"); } };
            if (settings.HideFromCapture) panel.HandleCreated += (s, e) => ExcludeFromCapture(panel.Handle);

            client = new ClientTracker(() => settings, IsOwnWindow) { DebugTitle = opts.AttachTitle, IsLinkedAppPid = IsHelperPid };
            client.Changed += Relayout;
            client.ForegroundChanged += OnForegroundChanged;
            client.Polled += KeepAboveGame;

            counterFilter.Resolve = t => { MonsterInfo m = data.FindMonster(t); return m != null ? m.Name : ""; };
            reader = new WidgetReader(OnReadWorker, () => confirmWanted, settings.CounterRegion) { IntervalMs = settings.ScanMs };
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
            foreach (string e in parser.Errors) Log.Write("dialogue pattern ignored: " + e);
        }

        /// <summary>Hides the panel first (so it disappears the moment Companion switches it off), then stops.</summary>
        void ExitNow(string why)
        {
            if (exiting) return;
            exiting = true;
            Log.Write("exit: " + why);
            try { panel.HidePassive(); } catch (Exception) { }
            reader.Dispose();              // the worker is a background thread: it never holds the exit up
            ExitThread();
        }

        bool IsHelperPid(uint pid)
        {
            if (pid == 0) return false;
            DateTime now = DateTime.UtcNow;
            if ((now - helperPidsAt).TotalSeconds > 60) { helperPids.Clear(); helperPidsAt = now; }
            bool yes;
            if (helperPids.TryGetValue(pid, out yes)) return yes;
            yes = false;
            try
            {
                using (var p = System.Diagnostics.Process.GetProcessById((int)pid))
                    yes = HelperProcesses.Any(n => string.Equals(p.ProcessName, n, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception) { }
            helperPids[pid] = yes;
            return yes;
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
                Monster = "Dragons", Master = "Laniakea", Assigned = 135, Left = 118, Streak = 291, Points = 6257,
                LastChat = "", LastSource = "counter", Updated = DateTime.Now,
            };
        }

        // ---- counter and dialogue -----------------------------------------------------------------
        void OnReadWorker(WidgetRead r)
        {
            if (exiting) return;
            try { panel.BeginInvoke((Action)(() => OnRead(r))); } catch (Exception) { }
        }

        void OnRead(WidgetRead r)
        {
            if (exiting) return;
            bool changed = false;
            if (r.DialogueGone) lastDialogue = null;
            if (r.Dialogue != null)
            {
                DialogueRead d = DialogueParser.Parse(r.Dialogue, parser, data);
                if (d.Event != null && (d.Text != lastDialogue || r.Forced))
                {
                    Log.Write("dialogue: " + d.Event + "  <- \"" + d.Text + "\"");
                    if (tracker.Apply(d.Event, "popup"))
                    {
                        changed = true;
                        // The counter (if open) is still the truth for the count: read it again.
                        counterFilter.Reset();
                    }
                }
                if (d.Text.Length > 0) lastDialogue = d.Text;
            }
            if (r.Counter != null)
            {
                // Every reading goes through the filter: the same value on consecutive frames, and only small
                // steps down unless a new task shows up. The panel never counts down by itself meanwhile.
                CounterRead ok = counterFilter.Offer(r.Counter, tracker.State);
                if (ok == null && counterFilter.NeedsConfirm && !counterFilter.Plausible(r.Counter, tracker.State) && counterFilter.PendingCount == 1)
                    Log.Write("counter: held back an unlikely reading (" + r.Counter + " by " + r.CounterBy + "; panel has " + tracker.State.Left + ")");
                if (ok != null)
                {
                    Log.Write("counter: " + ok + " (by " + r.CounterBy + ")");
                    if (tracker.ApplyCounter(ok)) changed = true;
                }
            }
            confirmWanted = counterFilter.NeedsConfirm;
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
                    reader.Rescan(); counterFilter.Reset(); lastDialogue = null; lastStatus = "Looking for the slayer counter\u2026"; Refresh(); break;
                case SlayerPanel.Hit.Clear:
                    if (tracker.Clear()) Changed(); break;
                case SlayerPanel.Hit.Minus: if (tracker.Adjust(-1)) Changed(); break;
                case SlayerPanel.Hit.Plus: if (tracker.Adjust(1)) Changed(); break;
            }
        }

        ContextMenuStrip headerMenu;

        /// <summary>Hidden right-click menu on the header: save what the reader sees, for bug reports.</summary>
        void OnHeaderMenu(Point at)
        {
            if (headerMenu == null)
            {
                headerMenu = new ContextMenuStrip { ShowImageMargin = false };
                headerMenu.Items.Add("Save debug capture", null, (o, e) => SaveDebugCapture());
                headerMenu.Items.Add("Open debug folder", null, (o, e) =>
                {
                    try { Directory.CreateDirectory(DebugDir); System.Diagnostics.Process.Start("explorer.exe", "\"" + DebugDir + "\""); }
                    catch (Exception ex) { Log.Write("debug: open folder failed: " + ex.Message); }
                });
            }
            headerMenu.Show(at);
        }

        static string DebugDir { get { return Path.Combine(Settings.Folder, "debug"); } }

        void SaveDebugCapture()
        {
            try
            {
                string state = "tracker state: " + MiniJson.Write(tracker.State.ToJson()) + "\nfilter accepted: " +
                    (counterFilter.Accepted != null ? counterFilter.Accepted.ToString() : "none");
                string path = reader.SaveDebug(DebugDir, state);
                if (path == null) { Log.Write("debug: nothing captured yet"); MessageBox.Show("Nothing captured yet. Open the game in front and try again.", "Kavros Slayer Helper"); return; }
                Log.Write("debug: saved " + path);
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception ex) { Log.Write("debug: save failed: " + ex); }
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
            // WS_EX_NOACTIVATE belt and braces: if the panel ends up in front without an edit, hand focus back.
            if (IsOwnWindow(fg) && !panel.Editing && !panel.Dragging && st.Visible) Native.SetForegroundWindow(st.Handle);
            Relayout();
        }

        /// <summary>Every poll: stay directly above the game in the z-order (other apps in front of the game
        /// stay in front of the panel; the panel never goes topmost over them).</summary>
        void KeepAboveGame()
        {
            ClientState st = client.State;
            if (st.Attached && st.Visible && panel.Visible) panel.KeepJustAbove(st.Handle);
        }

        void Relayout()
        {
            if (exiting) return;
            ClientState st = client.State;
            if (st.Visible) reader.SetArea(GameArea(st), st.Active);
            else reader.SetArea(Rectangle.Empty, false);

            if (!st.Attached)
            {
                // No game: stay on screen (not on top of other apps) so the helper is never just "somewhere".
                float s0 = Native.DpiFor(IntPtr.Zero) / 96f;
                panel.SetScale(s0);
                Size sz0 = panel.MeasureAt(s0);
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                int fx = settings.FreeX != int.MinValue ? settings.FreeX : wa.Left + (int)Math.Round(12 * s0);
                int fy = settings.FreeY != int.MinValue ? settings.FreeY : wa.Top + (int)Math.Round(12 * s0);
                Rectangle mon = Screen.FromPoint(new Point(fx + sz0.Width / 2, fy + 10)).WorkingArea;
                int cx, cy;
                Placement.Clamp(fx, fy, sz0.Width, sz0.Height, ToBox(mon), out cx, out cy);
                panel.PlaceAt(new Point(cx, cy));
                panel.ShowPassive();
                panel.SetTopMost(false);
                return;
            }
            // Visible whenever the game is on screen, focused or not (only minimised or closed hides it);
            // the z-order, not hiding, keeps it from covering other apps.
            if (!st.Visible) { if (!panel.Editing) panel.HidePassive(); return; }
            float s = st.Dpi / 96f;
            panel.SetScale(s);
            Size sz = panel.MeasureAt(s);
            Rectangle area = GameArea(st);
            int x = area.Left + (int)Math.Round(settings.OffsetX * s), y = area.Top + (int)Math.Round(settings.OffsetY * s);
            int px, py;
            Placement.Clamp(x, y, sz.Width, sz.Height, Bounds(area), out px, out py);
            panel.PlaceAt(new Point(px, py));
            bool wasVisible = panel.Visible;
            panel.ShowPassive();
            panel.KeepJustAbove(st.Handle);
            if (!wasVisible) panel.Present();
        }

        /// <summary>The panel dragged to <paramref name="p"/> (screen px): store it as an offset in the game
        /// (so it follows the client), or as a free position when no game is open.</summary>
        void OnDragMoved(Point p)
        {
            ClientState st = client.State;
            if (st.Attached && st.Visible)
            {
                float s = st.Dpi / 96f;
                Size sz = panel.MeasureAt(s);
                Rectangle area = GameArea(st);
                int cx, cy;
                Placement.Clamp(p.X, p.Y, sz.Width, sz.Height, Bounds(area), out cx, out cy);
                int ox, oy;
                Placement.ToOffset(cx, cy, ToBox(area), s, out ox, out oy);
                settings.OffsetX = ox; settings.OffsetY = oy;
            }
            else
            {
                Size sz = panel.PixelSize;
                Rectangle mon = Screen.FromPoint(new Point(p.X + sz.Width / 2, p.Y + 10)).WorkingArea;
                int cx, cy;
                Placement.Clamp(p.X, p.Y, sz.Width, sz.Height, ToBox(mon), out cx, out cy);
                settings.FreeX = cx; settings.FreeY = cy;
            }
            Relayout();
        }

        /// <summary>The game area, cut to the monitor's working area so the panel is never off screen.</summary>
        static Box Bounds(Rectangle area)
        {
            Rectangle wa = Screen.FromRectangle(area).WorkingArea;
            Rectangle r = Rectangle.Intersect(area, wa);
            return ToBox(r.Width > 50 && r.Height > 50 ? r : area);
        }

        static Box ToBox(Rectangle r) { return new Box(r.X, r.Y, r.Width, r.Height); }

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
                exiting = true;
                reader.Dispose();
                client.Dispose();
                if (watcher != null) watcher.Dispose();
                if (!demo) SaveState();
            }
            base.Dispose(disposing);
        }
    }
}
