using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNARTS.Core;

namespace FNARTS.Game
{
    /// <summary>
    /// Isometric minimap (C&amp;C2 style). Renders the map diamond via per-pixel
    /// inverse projection, with entity dots and camera viewport trapezoid.
    /// CPU-side compositing — no RenderTarget switching.
    /// </summary>
    public class Minimap : IDisposable
    {
        private readonly GraphicsDevice _device;
        private Texture2D _outputTex;

        private TileMap _map;
        private Func<int, int, bool> _isPlayable; // predicate: (gx,gy) → in playable area?

        /// <summary>Pixels per half-tile-width in minimap space.</summary>
        private const int MINIMAP_SCALE = 3;

        // Precomputed constants (derived from MINIMAP_SCALE and CoordUtil).
        private static readonly float InvScale = 1f / MINIMAP_SCALE;
        private static readonly float InvHalfScale = 1f / (MINIMAP_SCALE * 0.5f);
        private static readonly float WorldToMinimapScale =
            (float)MINIMAP_SCALE / CoordUtil.HALF_TILE_W;       // = 3/32
        private static readonly float WorldToMinimapScaleY =
            (float)MINIMAP_SCALE / (2f * CoordUtil.HALF_TILE_H); // = 3/32 (same at 2:1 ratio)
        // Canvas margin for the tile-diamond extent the camera can reach.
        private static readonly int MarginX =
            CoordUtil.TILE_WIDTH  * MINIMAP_SCALE / (int)CoordUtil.HALF_TILE_W; // 6
        private static readonly int MarginY =
            CoordUtil.TILE_HEIGHT * MINIMAP_SCALE / (int)CoordUtil.HALF_TILE_W; // 3

        // Pre-allocated entity colours (avoid per-frame allocation).
        private static readonly Color BuildingColor = new(180, 180, 220);
        private static readonly Color UnitSelectedColor = new(100, 255, 100);
        private static readonly Color UnitColor = new(200, 200, 100);

        private int _pw, _ph;    // canvas pixel size
        private int _ox, _oy;    // pixel position of grid (0,0) centre

        public int PixelW => _pw;
        public int PixelH => _ph;
        public Texture2D Texture => _outputTex;

        private static readonly Color[] _tileColors;

        static Minimap()
        {
            _tileColors = new Color[Enum.GetValues<TileType>().Length];
            _tileColors[(int)TileType.Grass] = new Color(60, 120, 30);
            _tileColors[(int)TileType.Water] = new Color(40, 80, 200);
            _tileColors[(int)TileType.Cliff] = new Color(130, 130, 130);
            _tileColors[(int)TileType.Impassable] = new Color(160, 50, 50);
        }

        public Minimap(GraphicsDevice device) { _device = device; }

        public void SetPlayableArea(Func<int, int, bool> isPlayable)
        {
            _isPlayable = isPlayable;
        }

        public void SetMap(TileMap map, int playableCenterX, int playableCenterY, int playableRadius)
        {
            _map = map;
            int cx = playableCenterX, cy = playableCenterY, R = playableRadius;

            // Canvas sized to the playable diamond plus the tile-diamond
            // extent the camera can reach.  +1 fencepost margin so the
            // constant-size viewport frame fits at all edges.
            _pw = 2 * R * MINIMAP_SCALE + MarginX + 1;
            _ph =      R * MINIMAP_SCALE + MarginY + 1;
            _pw = Math.Max(1, _pw);
            _ph = Math.Max(1, _ph);

            // Origin: position so the playable diamond fills the canvas.
            // px = ox + (gx-gy)*MINIMAP_SCALE
            // py = oy - (gx+gy)*MINIMAP_SCALE*0.5  (north = top)
            _ox = (R + cy - cx) * MINIMAP_SCALE;
            _oy = (int)((cx + cy + R) * MINIMAP_SCALE * 0.5f);

            if (_outputTex == null || _outputTex.Width != _pw || _outputTex.Height != _ph)
            {
                _outputTex?.Dispose();
                _outputTex = new Texture2D(_device, _pw, _ph);
            }
        }

        public void Render(EntityManager entities, SelectionSystem selection,
            Camera2D camera)
        {
            if (_map == null || _outputTex == null) return;

            int W = _map.Width, H = _map.Height;
            var frame = new Color[_pw * _ph];

            // 1. Terrain: per-pixel inverse isometric
            var isPlayable = _isPlayable; // hoist null check out of hot loop
            for (int py = 0; py < _ph; py++)
            {
                for (int px = 0; px < _pw; px++)
                {
                    var (gxf, gyf) = PixelToGrid(px, py);
                    int gx = (int)MathF.Floor(gxf);
                    int gy = (int)MathF.Floor(gyf);

                    Color c;
                    if ((uint)gx < (uint)W && (uint)gy < (uint)H
                        && (isPlayable == null || isPlayable(gx, gy)))
                    {
                        var tile = _map.GetTile(gx, gy);
                        c = _tileColors[(int)tile.Type];
                    }
                    else
                    {
                        c = Color.Transparent;
                    }

                    frame[py * _pw + px] = c;
                }
            }

            // 2. Entity dots
            if (entities != null)
            {
                foreach (var e in entities.AllEntities)
                {
                    if (!e.IsAlive) continue;

                    if (e is Building b)
                    {
                        // Fill every tile the building occupies — each tile is an
                        // isometric diamond on the minimap, matching the terrain.
                        int bx = b.PlacementOrigin.X;
                        int by = b.PlacementOrigin.Y;
                        int sx = b.SizeX, sy = b.SizeY;

                        // Pixel-space bounding box of the building's tile footprint
                        int pxMin = _ox + (int)MathF.Floor((bx - (by + sy)) * MINIMAP_SCALE);
                        int pxMax = _ox + (int)MathF.Ceiling((bx + sx - by) * MINIMAP_SCALE);
                        int pyMin = _oy - (int)MathF.Ceiling((bx + sx + by + sy) * MINIMAP_SCALE * 0.5f);
                        int pyMax = _oy - (int)MathF.Floor((bx + by) * MINIMAP_SCALE * 0.5f);

                        pxMin = Math.Max(0, pxMin); pxMax = Math.Min(_pw - 1, pxMax);
                        pyMin = Math.Max(0, pyMin); pyMax = Math.Min(_ph - 1, pyMax);

                        for (int py = pyMin; py <= pyMax; py++)
                        {
                            for (int px = pxMin; px <= pxMax; px++)
                            {
                                var (gxf, gyf) = PixelToGrid(px, py);
                                if (gxf >= bx && gxf < bx + sx && gyf >= by && gyf < by + sy)
                                    frame[py * _pw + px] = BuildingColor;
                            }
                        }
                    }
                    else
                    {
                        var g = CoordUtil.WorldToIso(e.WorldPosition);
                        float egx = g.X + 0.5f;
                        float egy = g.Y + 0.5f;

                        int ex = _ox + (int)MathF.Floor((egx - egy) * MINIMAP_SCALE);
                        int ey = _oy - (int)MathF.Floor((egx + egy) * MINIMAP_SCALE * 0.5f);

                        Color dotColor = e switch
                        {
                            Unit => selection != null && selection.SelectedEntityIds.Contains(e.Id)
                                ? UnitSelectedColor : UnitColor,
                            _ => Color.Gray,
                        };

                        for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int tx = ex + dx, ty = ey + dy;
                            if ((uint)tx < (uint)_pw && (uint)ty < (uint)_ph)
                                frame[ty * _pw + tx] = dotColor;
                        }
                    }
                }
            }

            // 3. Camera viewport — analytic (constant size at given zoom).
            //    worldX → gx-gy → minimap px (scaleX), worldY → gx+gy → minimap py (scaleY).
            //    At the current 2:1 tile ratio scaleX == scaleY, but the axes are
            //    kept explicit for clarity.
            if (camera != null)
            {
                float frameW = camera.ViewportWidth  * WorldToMinimapScale / camera.Zoom;
                float frameH = camera.ViewportHeight * WorldToMinimapScaleY / camera.Zoom;

                float cx = _ox + camera.Position.X * WorldToMinimapScale;
                float cy = _oy + camera.Position.Y * WorldToMinimapScaleY;

                float halfW = frameW * 0.5f;
                float halfH = frameH * 0.5f;

                int xMin = Math.Max(0,          (int)MathF.Round(cx - halfW));
                int xMax = Math.Min(_pw - 1,    (int)MathF.Round(cx + halfW));
                int yMin = Math.Max(0,          (int)MathF.Round(cy - halfH));
                int yMax = Math.Min(_ph - 1,    (int)MathF.Round(cy + halfH));

                PixelUtils.DrawLine(frame, _pw, _ph, xMin, yMin, xMax, yMin, Color.White); // top
                PixelUtils.DrawLine(frame, _pw, _ph, xMax, yMin, xMax, yMax, Color.White); // right
                PixelUtils.DrawLine(frame, _pw, _ph, xMax, yMax, xMin, yMax, Color.White); // bottom
                PixelUtils.DrawLine(frame, _pw, _ph, xMin, yMax, xMin, yMin, Color.White); // left
            }

            _outputTex.SetData(frame);
        }

        /// <summary>Inverse isometric projection: minimap pixel → continuous grid.</summary>
        private (float gxf, float gyf) PixelToGrid(int px, int py)
        {
            float dx = px - _ox;
            float dy = _oy - py;
            float gxf = (dx * InvScale + dy * InvHalfScale) * 0.5f;
            float gyf = (dy * InvHalfScale - dx * InvScale) * 0.5f;
            return (gxf, gyf);
        }

        public void Dispose() { _outputTex?.Dispose(); }
    }
}
