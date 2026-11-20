using System;
using System.IO;
using System.IO.Compression;
using KavrosSlayerHelper.Core;

namespace KavrosSlayerHelper.Tests
{
    /// <summary>Minimal PNG reader for the fixtures: 8-bit RGB / RGBA / grey / palette, not interlaced.</summary>
    static class Png
    {
        public static RgbImage Load(string path)
        {
            byte[] f = File.ReadAllBytes(path);
            int pos = 8, w = 0, h = 0, depth = 0, type = 0, interlace = 0;
            byte[] palette = null;
            var idat = new MemoryStream();
            while (pos < f.Length)
            {
                int len = (f[pos] << 24) | (f[pos + 1] << 16) | (f[pos + 2] << 8) | f[pos + 3];
                string kind = System.Text.Encoding.ASCII.GetString(f, pos + 4, 4);
                int d = pos + 8;
                if (kind == "IHDR")
                {
                    w = (f[d] << 24) | (f[d + 1] << 16) | (f[d + 2] << 8) | f[d + 3];
                    h = (f[d + 4] << 24) | (f[d + 5] << 16) | (f[d + 6] << 8) | f[d + 7];
                    depth = f[d + 8]; type = f[d + 9]; interlace = f[d + 12];
                }
                else if (kind == "PLTE") { palette = new byte[len]; Array.Copy(f, d, palette, 0, len); }
                else if (kind == "IDAT") idat.Write(f, d, len);
                else if (kind == "IEND") break;
                pos = d + len + 4;
            }
            if (depth != 8 || interlace != 0) throw new NotSupportedException("png: depth " + depth + " interlace " + interlace);
            int bpp = type == 2 ? 3 : type == 6 ? 4 : type == 0 ? 1 : type == 4 ? 2 : type == 3 ? 1 : throw new NotSupportedException("png type " + type);
            idat.Position = 0;
            byte[] raw;
            using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            using (var o = new MemoryStream()) { z.CopyTo(o); raw = o.ToArray(); }
            int stride = w * bpp;
            var cur = new byte[stride]; var prev = new byte[stride];
            var img = new RgbImage(w, h);
            int p = 0;
            for (int y = 0; y < h; y++)
            {
                byte filter = raw[p++];
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? cur[x - bpp] : 0, b = prev[x], c = x >= bpp ? prev[x - bpp] : 0;
                    int v = raw[p++];
                    switch (filter)
                    {
                        case 1: v += a; break;
                        case 2: v += b; break;
                        case 3: v += (a + b) / 2; break;
                        case 4: { int pp = a + b - c, pa = Math.Abs(pp - a), pb = Math.Abs(pp - b), pc = Math.Abs(pp - c); v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c; break; }
                    }
                    cur[x] = (byte)v;
                }
                for (int x = 0; x < w; x++)
                {
                    int t = (y * w + x) * 3;
                    if (type == 2 || type == 6) { img.Px[t] = cur[x * bpp]; img.Px[t + 1] = cur[x * bpp + 1]; img.Px[t + 2] = cur[x * bpp + 2]; }
                    else if (type == 3) { int i = cur[x] * 3; img.Px[t] = palette[i]; img.Px[t + 1] = palette[i + 1]; img.Px[t + 2] = palette[i + 2]; }
                    else { byte g = cur[x * bpp]; img.Px[t] = g; img.Px[t + 1] = g; img.Px[t + 2] = g; }
                }
                var sw = prev; prev = cur; cur = sw;
            }
            return img;
        }
    }
}
