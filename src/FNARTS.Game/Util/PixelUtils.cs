using System;
using Microsoft.Xna.Framework;

namespace FNARTS.Game
{
    /// <summary>
    /// Pixel-buffer utilities shared across CPU-side compositing code.
    /// </summary>
    public static class PixelUtils
    {
        /// <summary>Bresenham line draw into a flat colour array.</summary>
        public static void DrawLine(Color[] data, int w, int h,
            int x0, int y0, int x1, int y1, Color color)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                if ((uint)x0 < (uint)w && (uint)y0 < (uint)h)
                    data[y0 * w + x0] = color;
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }
}
