using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace KavrosSlayerHelper
{
    /// <summary>Snapshot of the game window, all in physical screen pixels.</summary>
    internal struct ClientState
    {
        public IntPtr Handle;
        public bool Attached;         // window exists
        public bool Visible;          // exists, shown, not minimised, not cloaked
        public bool Foreground;       // the game window has focus
        public bool Active;           // the game (any of its windows) or our own strip/hub is the foreground window
        public Rectangle Frame;       // visible outer frame
        public Rectangle Client;      // client (render) area
        public int Dpi;
        public string Title;

        public bool SameLayout(ClientState o)
        {
            return Handle == o.Handle && Attached == o.Attached && Visible == o.Visible && Foreground == o.Foreground && Active == o.Active
                && Frame == o.Frame && Client == o.Client && Dpi == o.Dpi;
        }
    }

    /// <summary>Finds the Kavros client window and follows its position, size, minimised state and focus.
    /// Uses SetWinEventHook for immediate updates and a polling timer as a fallback.
    /// It only queries window state; it never sends anything to the client.</summary>
    internal sealed class ClientTracker : IDisposable
    {
        readonly Func<Settings> settings;
        readonly Func<IntPtr, bool> isOwnWindow;
        /// <summary>True for the process id of a running linked app (set by the host).</summary>
        public Func<uint, bool> IsLinkedAppPid;
        readonly Timer poll = new Timer();
        readonly Native.WinEventProc hookProc;   // kept in a field so the GC never collects it
        readonly List<IntPtr> hooks = new List<IntPtr>();
        IntPtr foregroundHook;
        uint hookedPid;
        int searchCountdown;

        public string DebugTitle;       // set from --attach or the DebugAttachTitle setting
        public ClientState State;
        public event Action Changed;
        public event Action ForegroundChanged;
        /// <summary>After every poll (200 ms), changed or not.</summary>
        public event Action Polled;

        public ClientTracker(Func<Settings> settings, Func<IntPtr, bool> isOwnWindow)
        {
            this.settings = settings;
            this.isOwnWindow = isOwnWindow;
            hookProc = OnWinEvent;
            poll.Interval = 200;
            poll.Tick += delegate { Refresh(false); if (Polled != null) Polled(); };
        }

        public void Start()
        {
            foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, hookProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT);
            poll.Start();
            Refresh(true);
        }

        public string DebugTitleInUse
        {
            get { return !string.IsNullOrEmpty(DebugTitle) ? DebugTitle : settings().DebugAttachTitle; }
        }

        /// <summary>Re-reads the window state. Searching for a new window is throttled to once a second.</summary>
        public void Refresh(bool forceSearch)
        {
            IntPtr h = State.Handle;
            if (h != IntPtr.Zero && !Native.IsWindow(h)) h = IntPtr.Zero;
            if (h == IntPtr.Zero)
            {
                if (forceSearch || --searchCountdown <= 0)
                {
                    searchCountdown = 5;
                    h = Find();
                }
            }
            var next = Read(h, State);
            bool changed = !next.SameLayout(State);
            bool handleChanged = next.Handle != State.Handle;
            State = next;
            if (handleChanged) Rehook();
            if (changed && Changed != null) Changed();
        }

        ClientState Read(IntPtr h, ClientState previous)
        {
            var s = new ClientState { Handle = h, Dpi = 96 };
            if (h == IntPtr.Zero) return s;
            s.Attached = true;
            s.Title = Native.WindowTitle(h);
            s.Visible = Native.IsWindowVisible(h) && !Native.IsIconic(h) && !Native.IsCloaked(h);
            IntPtr fg = Native.GetForegroundWindow();
            s.Foreground = fg == h;
            if (fg == IntPtr.Zero)
            {
                // Mid-switch there is briefly no foreground window: keep the last answer so nothing flickers.
                s.Active = previous.Handle == h && previous.Active;
            }
            else
            {
                uint fgPid, clientPid;
                Native.GetWindowThreadProcessId(fg, out fgPid);
                Native.GetWindowThreadProcessId(h, out clientPid);
                // Linked apps (Clue Helper's panel and settings) count as "the game" too: clicking into them
                // must not hide the strip.
                s.Active = fg == h || fgPid == clientPid || isOwnWindow(fg) || (IsLinkedAppPid != null && IsLinkedAppPid(fgPid));
            }
            if (s.Visible)
            {
                s.Frame = Native.FrameBounds(h);
                s.Client = Native.ClientBounds(h);
                s.Dpi = Native.DpiFor(h);
                if (s.Frame.Width < 50 || s.Frame.Height < 50) s.Visible = false;
            }
            return s;
        }

        IntPtr Find()
        {
            Settings cfg = settings();
            string debug = DebugTitleInUse;
            var pids = new HashSet<uint>();
            if (string.IsNullOrEmpty(debug))
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName(cfg.ProcessName)) { pids.Add((uint)p.Id); p.Dispose(); }
                }
                catch (Exception) { }
                if (pids.Count == 0) return IntPtr.Zero;
            }
            uint self = (uint)Process.GetCurrentProcess().Id;
            IntPtr best = IntPtr.Zero;
            long bestArea = -1;
            Native.EnumWindows(delegate (IntPtr hwnd, IntPtr l)
            {
                if (!Native.IsWindowVisible(hwnd) || Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero) return true;
                uint pid; Native.GetWindowThreadProcessId(hwnd, out pid);
                if (pid == self || isOwnWindow(hwnd)) return true;
                string title = Native.WindowTitle(hwnd);
                if (!string.IsNullOrEmpty(debug))
                {
                    if (title.IndexOf(debug, StringComparison.OrdinalIgnoreCase) < 0) return true;
                }
                else
                {
                    if (!pids.Contains(pid)) return true;
                    if (!string.IsNullOrEmpty(cfg.WindowTitle) && title.IndexOf(cfg.WindowTitle, StringComparison.OrdinalIgnoreCase) < 0) return true;
                }
                Rectangle r = Native.FrameBounds(hwnd);
                long area = Native.IsIconic(hwnd) ? 1 : (long)r.Width * r.Height;
                if (area > bestArea) { bestArea = area; best = hwnd; }
                return true;
            }, IntPtr.Zero);
            if (best != IntPtr.Zero) Log.Write("attached to window 0x" + best.ToString("X") + " \"" + Native.WindowTitle(best) + "\"");
            return best;
        }

        void Rehook()
        {
            foreach (IntPtr hk in hooks) Native.UnhookWinEvent(hk);
            hooks.Clear();
            hookedPid = 0;
            if (State.Handle == IntPtr.Zero) return;
            Native.GetWindowThreadProcessId(State.Handle, out hookedPid);
            uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
            hooks.Add(Native.SetWinEventHook(Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, hookProc, hookedPid, 0, flags));
            hooks.Add(Native.SetWinEventHook(Native.EVENT_OBJECT_DESTROY, Native.EVENT_OBJECT_HIDE, IntPtr.Zero, hookProc, hookedPid, 0, flags));
            hooks.Add(Native.SetWinEventHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, hookProc, hookedPid, 0, flags));
            hooks.RemoveAll(x => x == IntPtr.Zero);
        }

        void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (evt == Native.EVENT_SYSTEM_FOREGROUND)
            {
                Refresh(false);
                if (ForegroundChanged != null) ForegroundChanged();
                return;
            }
            if (hwnd != State.Handle || idObject != Native.OBJID_WINDOW) return;
            Refresh(false);
        }

        public void Reset()
        {
            State = new ClientState();
            Rehook();
            Refresh(true);
        }

        public void Dispose()
        {
            poll.Stop();
            poll.Dispose();
            foreach (IntPtr hk in hooks) Native.UnhookWinEvent(hk);
            hooks.Clear();
            if (foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(foregroundHook);
        }
    }
}
