using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace KavrosSlayerHelper
{
    /// <summary>Borderless, per-pixel-alpha overlay window (WS_EX_LAYERED + UpdateLayeredWindow).
    /// Tool window, so it never shows in the taskbar or Alt+Tab. Subclasses paint into a bitmap at the
    /// current scale; fully transparent pixels let clicks fall through to whatever is underneath.</summary>
    internal abstract class LayeredWindow : Form
    {
        float scale = 1f;
        byte alpha = 255;
        bool clickThrough;
        Size pixelSize = new Size(1, 1);

        protected LayeredWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            // True so WinForms doesn't park us under a hidden owner window (see CreateParams); WS_EX_TOOLWINDOW
            // and the cleared WS_EX_APPWINDOW keep the panel out of the taskbar and Alt+Tab all the same.
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ControlBox = false;
            Text = "Kavros Slayer Helper";
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
        }

        protected virtual bool NoActivate { get { return true; } }

        /// <summary>WM_CLOSE arrived (raised on the UI thread, before any WinForms close handling).</summary>
        public event Action CloseRequested;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style = Native.WS_POPUP;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW;
                if (NoActivate) cp.ExStyle |= Native.WS_EX_NOACTIVATE;
                if (clickThrough) cp.ExStyle |= Native.WS_EX_TRANSPARENT;
                // No owner: WinForms parks a ShowInTaskbar=false form under a hidden, untitled owner window,
                // and Companion only sends WM_CLOSE to unowned windows, so its toggle never reached us (it
                // waited, then killed the process). WS_EX_TOOLWINDOW already keeps us out of the taskbar.
                cp.Parent = IntPtr.Zero;
                cp.ExStyle &= ~0x00040000;   // WS_EX_APPWINDOW
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // Clicks on a non-activating overlay never make it the active window (WM_MOUSEACTIVATE -> MA_NOACTIVATE),
        // so the game keeps the foreground. Linked apps like Clue Helper hide their panel when it loses it.
        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3, WM_CLOSE = 0x0010;
            if (m.Msg == WM_MOUSEACTIVATE && NoActivate) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
            // A close request (Companion's toggle, taskkill): the host decides; nothing waits on WinForms' close path.
            if (m.Msg == WM_CLOSE && CloseRequested != null) { CloseRequested(); m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
        }

        // Painting goes through UpdateLayeredWindow only.
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        public float OverlayScale { get { return scale; } }
        public Size PixelSize { get { return pixelSize; } }

        /// <summary>Size in device pixels for the given scale.</summary>
        protected abstract Size Measure(float s);
        protected abstract void Render(Graphics g, float s);

        public Size MeasureAt(float s) { return Measure(s); }

        public void SetScale(float s)
        {
            if (Math.Abs(s - scale) < 0.001f) return;
            scale = s;
            Present();
        }

        public void SetAlpha(int percent)
        {
            byte a = (byte)Math.Max(0, Math.Min(255, Math.Round(percent * 2.55)));
            if (a == alpha) return;
            alpha = a;
            Present();
        }

        public bool ClickThrough
        {
            get { return clickThrough; }
            set
            {
                if (clickThrough == value) return;
                clickThrough = value;
                if (IsHandleCreated) Native.SetExStyleFlag(Handle, Native.WS_EX_TRANSPARENT, value);
            }
        }

        /// <summary>Places the window with its top-left at <paramref name="topLeft"/> and redraws.</summary>
        public void PlaceAt(Point topLeft)
        {
            if (!IsHandleCreated) CreateHandle();
            Size want = Measure(scale);
            if (want != pixelSize || Location != topLeft)
            {
                pixelSize = want;
                Native.SetWindowPos(Handle, IntPtr.Zero, topLeft.X, topLeft.Y, want.Width, want.Height,
                    Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
            }
            Present();
        }

        /// <summary>Re-renders the bitmap and pushes it to the layered window.</summary>
        public void Present()
        {
            if (!IsHandleCreated) return;
            Size sz = Measure(scale);
            if (sz.Width < 1 || sz.Height < 1) return;
            pixelSize = sz;
            using (var bmp = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    Theme.Prepare(g);
                    Render(g, scale);
                }
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                IntPtr mem = Native.CreateCompatibleDC(screen);
                IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0));
                IntPtr old = Native.SelectObject(mem, hbmp);
                try
                {
                    var dst = new Native.POINT(Left, Top);
                    var size = new Native.SIZE(sz.Width, sz.Height);
                    var src = new Native.POINT(0, 0);
                    var blend = new Native.BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = alpha, AlphaFormat = Native.AC_SRC_ALPHA };
                    Native.UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, Native.ULW_ALPHA);
                }
                finally
                {
                    Native.SelectObject(mem, old);
                    Native.DeleteObject(hbmp);
                    Native.DeleteDC(mem);
                    Native.ReleaseDC(IntPtr.Zero, screen);
                }
            }
        }

        public void SetTopMost(bool topMost)
        {
            if (!IsHandleCreated) return;
            topMostNow = topMost;
            Native.SetWindowPos(Handle, topMost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        bool? topMostNow;

        /// <summary>Keeps the overlay directly above <paramref name="target"/> in the z-order without making it
        /// topmost: it covers the game, but any other app brought in front of the game also covers the overlay.
        /// Cheap enough to call on every poll (it does nothing when already in place).</summary>
        public void KeepJustAbove(IntPtr target)
        {
            if (!IsHandleCreated || target == IntPtr.Zero) return;
            bool targetTop = (Native.GetWindowLong(target, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0;
            if (targetTop) { if (topMostNow != true) SetTopMost(true); return; }     // a topmost (exclusive fullscreen) game: follow it there
            if (topMostNow != false) SetTopMost(false);
            IntPtr prev = Native.GetWindow(target, Native.GW_HWNDPREV);
            if (prev == Handle) return;
            // SetWindowPos puts us just below "after": the window that is just above the game now.
            IntPtr after = prev == IntPtr.Zero || (Native.GetWindowLong(prev, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0
                ? Native.HWND_TOP : prev;
            Native.SetWindowPos(Handle, after, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        /// <summary>Raises a non-topmost overlay above the game window without stealing focus.</summary>
        public void BringAboveClient()
        {
            if (!IsHandleCreated) return;
            Native.SetWindowPos(Handle, Native.HWND_TOP, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        public void ShowPassive()
        {
            if (!IsHandleCreated) CreateHandle();
            if (!Visible) Show();
        }

        public void HidePassive()
        {
            if (Visible) Hide();
        }

        // Scale a CSS pixel length.
        protected static float P(float v, float s) { return v * s; }
    }
}
