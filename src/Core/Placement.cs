using System;

namespace KavrosSlayerHelper.Core
{
    /// <summary>Where the panel goes: the user's dragged position, kept fully inside the bounds
    /// (the game area on its monitor, or the monitor itself when no game is open).</summary>
    public static class Placement
    {
        /// <summary>Top-left that keeps a panel of size <paramref name="w"/> x <paramref name="h"/> inside <paramref name="bounds"/>.
        /// A panel bigger than the bounds sticks to their top-left.</summary>
        public static void Clamp(int x, int y, int w, int h, Box bounds, out int cx, out int cy)
        {
            cx = Math.Max(bounds.X, Math.Min(x, bounds.Right - w));
            cy = Math.Max(bounds.Y, Math.Min(y, bounds.Bottom - h));
        }

        /// <summary>The offset (in 100 % px) from the game area's top-left for a panel dragged to (x, y).</summary>
        public static void ToOffset(int x, int y, Box area, float scale, out int ox, out int oy)
        {
            if (scale <= 0) scale = 1;
            ox = Math.Max(0, (int)Math.Round((x - area.X) / scale));
            oy = Math.Max(0, (int)Math.Round((y - area.Y) / scale));
        }
    }
}
