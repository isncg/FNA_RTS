using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNARTS.Core;

namespace FNARTS.Game
{
    /// <summary>
    /// Generates all placeholder textures procedurally. No external assets needed.
    /// Replaced by FileAssetProvider when real art is ready.
    /// </summary>
    public class ProceduralAssetProvider : IAssetProvider
    {
        private readonly GraphicsDevice _device;
        private readonly Dictionary<string, Texture2D> _unitCache = new();
        private readonly Dictionary<string, Texture2D> _buildingCache = new();

        // Isometric constants (reference CoordUtil.cs)

        public const int TILE_TEX_W = 64;
        public const int TILE_TEX_H = 32;
        public const int TILESET_COLS = 4;

        public Texture2D TilesetTexture { get; }
        public Texture2D SelectionHighlight { get; }
        public Texture2D DiamondHighlight { get; }
        public Texture2D WhitePixel { get; }

        public ProceduralAssetProvider(GraphicsDevice device)
        {
            _device = device;
            TilesetTexture = GenerateTileset();
            SelectionHighlight = GenerateSelectionHighlight();
            DiamondHighlight = GenerateDiamondHighlight();
            WhitePixel = GenerateWhitePixel();
        }

        public Rectangle GetTileSourceRect(TileType type)
        {
            int col = (int)type % TILESET_COLS;
            return new Rectangle(col * TILE_TEX_W, 0, TILE_TEX_W, TILE_TEX_H);
        }

        public Texture2D GetUnitTexture(string unitDefId)
        {
            if (_unitCache.TryGetValue(unitDefId, out var cached)) return cached;
            var tex = GenCircleTex(32, unitDefId switch
            {
                "worker" => new Color(200, 200, 100),
                "infantry" => new Color(100, 180, 255),
                "tank" => new Color(255, 120, 80),
                _ => Color.Gray
            });
            _unitCache[unitDefId] = tex;
            return tex;
        }

        public Texture2D GetBuildingTexture(BuildingDef def)
        {
            string key = $"{def.Id}_{def.SizeX}_{def.SizeY}_{def.Height}";
            if (_buildingCache.TryGetValue(key, out var cached)) return cached;

            int E = def.SizeX, N = def.SizeY, H = def.Height;
            var tex = GenIsometricBox(E, N, H);
            _buildingCache[key] = tex;
            return tex;
        }

        public void Dispose()
        {
            TilesetTexture?.Dispose();
            SelectionHighlight?.Dispose();
            DiamondHighlight?.Dispose();
            WhitePixel?.Dispose();
            foreach (var t in _unitCache.Values) t?.Dispose();
            foreach (var t in _buildingCache.Values) t?.Dispose();
        }

        // --- Procedural generation ---

        private Texture2D GenerateTileset()
        {
            int cols = TILESET_COLS;
            int rows = (Enum.GetValues<TileType>().Length + cols - 1) / cols;
            int atlasW = cols * TILE_TEX_W;
            int atlasH = rows * TILE_TEX_H;
            var data = new Color[atlasW * atlasH];
            Array.Fill(data, Color.Transparent);

            foreach (TileType type in Enum.GetValues<TileType>())
            {
                int col = (int)type % cols;
                int row = (int)type / cols;
                int ox = col * TILE_TEX_W;
                int oy = row * TILE_TEX_H;
                Color fill = type switch
                {
                    TileType.Grass => new Color(76, 153, 0),
                    TileType.Water => new Color(51, 102, 255),
                    TileType.Cliff => new Color(160, 160, 160),
                    TileType.Impassable => new Color(180, 60, 60),
                    _ => Color.Magenta
                };
                Color border = new Color((byte)(fill.R * 0.6f), (byte)(fill.G * 0.6f), (byte)(fill.B * 0.6f), 255);
                DrawDiamond(data, atlasW, ox, oy, TILE_TEX_W, TILE_TEX_H, fill, border);
            }

            var tex = new Texture2D(_device, atlasW, atlasH);
            tex.SetData(data);
            return tex;
        }

        private static void DrawDiamond(Color[] data, int stride, int ox, int oy,
            int tw, int th, Color fill, Color border)
        {
            float halfW = tw / 2f, halfH = th / 2f;
            for (int py = 0; py < th; py++)
            for (int px = 0; px < tw; px++)
            {
                float dx = px - halfW + 0.5f;
                float dy = py - halfH + 0.5f;
                float dist = MathF.Abs(dx / halfW) + MathF.Abs(dy / halfH);
                if (dist <= 1.02f)
                    data[(oy + py) * stride + (ox + px)] = dist > 0.85f ? border : fill;
            }
        }

        private Texture2D GenerateSelectionHighlight()
        {
            int size = 36;
            var data = new Color[size * size];
            float center = size / 2f, outerR = size / 2f - 1, innerR = outerR - 3;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center + 0.5f, dy = y - center + 0.5f;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d >= innerR && d <= outerR)
                    data[y * size + x] = new Color(255, 255, 0, 220);
            }
            var tex = new Texture2D(_device, size, size);
            tex.SetData(data);
            return tex;
        }

        private Texture2D GenerateDiamondHighlight()
        {
            // 64×32 semi-transparent white diamond — tinted at draw time.
            int tw = TILE_TEX_W, th = TILE_TEX_H;
            var data = new Color[tw * th];
            float halfW = tw / 2f, halfH = th / 2f;
            for (int py = 0; py < th; py++)
            for (int px = 0; px < tw; px++)
            {
                float dx = px - halfW + 0.5f, dy = py - halfH + 0.5f;
                float dist = MathF.Abs(dx / halfW) + MathF.Abs(dy / halfH);
                if (dist <= 0.98f)
                    data[py * tw + px] = new Color(255, 255, 255, 140);
            }
            var tex = new Texture2D(_device, tw, th);
            tex.SetData(data);
            return tex;
        }

        private Texture2D GenerateWhitePixel()
        {
            var tex = new Texture2D(_device, 1, 1);
            tex.SetData(new[] { Color.White });
            return tex;
        }

        private Texture2D GenCircleTex(int size, Color fill)
        {
            var data = new Color[size * size];
            float center = size / 2f, radius = size / 2f - 2;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center + 0.5f, dy = y - center + 0.5f;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d <= radius)
                    data[y * size + x] = d > radius - 2
                        ? new Color((byte)(fill.R * 0.6f), (byte)(fill.G * 0.6f), (byte)(fill.B * 0.6f), 255)
                        : fill;
            }
            var tex = new Texture2D(_device, size, size);
            tex.SetData(data);
            return tex;
        }

        // ── Isometric 3D box building generator ─────────────────────────
        // Port of tools/build_building_tex.py — C&C2 isometric projection.
        // gx+1 → upper-right (East), gy+1 → upper-left (North), hz → straight up.
        // Visible faces from player perspective (bottom of screen): top, south, west.

        private Texture2D GenIsometricBox(int E, int N, int H)
        {
            // Anchor = footprint centre in continuous grid space
            float cgx = E / 2f, cgy = N / 2f;
            float anchorWx = (cgx - cgy) * CoordUtil.HALF_TILE_W;
            float anchorWy = -(cgx + cgy) * CoordUtil.HALF_TILE_H;  // hz=0

            int texW = (E + N) * (int)CoordUtil.HALF_TILE_W;
            int texH = (E + N) * (int)CoordUtil.HALF_TILE_H + H * (int)(2 * CoordUtil.TILE_HEIGHT);
            float halfTW = texW / 2f, halfTH = texH / 2f;

            var data = new Color[texW * texH];
            var roofColor = new Color(160, 160, 176, 255);
            var wallColor = new Color(140, 140, 160, 255);
            var southColor = new Color((byte)(wallColor.R * 0.60f), (byte)(wallColor.G * 0.60f), (byte)(wallColor.B * 0.60f), 255);
            var westColor  = new Color((byte)(wallColor.R * 0.40f), (byte)(wallColor.G * 0.40f), (byte)(wallColor.B * 0.40f), 255);
            var edgeColor = new Color(64, 64, 80, 255);

            // Per-pixel face test
            for (int py = 0; py < texH; py++)
            {
                for (int px = 0; px < texW; px++)
                {
                    // World position of this pixel (relative to anchor)
                    float wx = anchorWx + (px - halfTW);
                    float wy = anchorWy + (py - halfTH);

                    // Ground-level grid coords at this screen position
                    float gxGround = (wx / CoordUtil.HALF_TILE_W - wy / CoordUtil.HALF_TILE_H) / 2f;
                    float gyGround = (-wy / CoordUtil.HALF_TILE_H - wx / CoordUtil.HALF_TILE_W) / 2f;

                    // Top face (roof): gx ∈ [0,E], gy ∈ [0,N], hz = H
                    float gxR = gxGround - H;
                    float gyR = gyGround - H;
                    if (gxR >= 0 && gxR <= E && gyR >= 0 && gyR <= N)
                    {
                        data[py * texW + px] = roofColor;
                        continue;
                    }

                    // South wall (lower-right face): gy = 0, gx ∈ [0,E], hz ∈ [0,H]
                    float hzS = gyGround;
                    float gxS = gxGround - hzS;
                    if (hzS >= 0 && hzS <= H && gxS >= 0 && gxS <= E)
                    {
                        data[py * texW + px] = southColor;
                        continue;
                    }

                    // West wall (lower-left face): gx = 0, gy ∈ [0,N], hz ∈ [0,H]
                    float hzW = gxGround;
                    float gyW = gyGround - hzW;
                    if (hzW >= 0 && hzW <= H && gyW >= 0 && gyW <= N)
                    {
                        data[py * texW + px] = westColor;
                        continue;
                    }

                    // Transparent (ground shows through)
                    data[py * texW + px] = Color.Transparent;
                }
            }

            // Edge outlines — draw perimeter of each face
            DrawFaceEdges(data, texW, texH, E, N, H, anchorWx, anchorWy, halfTW, halfTH, edgeColor);

            var tex = new Texture2D(_device, texW, texH);
            tex.SetData(data);
            return tex;
        }

        private static void DrawFaceEdges(Color[] data, int texW, int texH,
            int E, int N, int H, float anchorWx, float anchorWy,
            float halfTW, float halfTH, Color edgeColor)
        {
            // Three visible faces — each is a parallelogram (4 vertices in world space)
            var topVerts = new (float wx, float wy)[]
            {
                (0,                    -(0 + 0) * CoordUtil.HALF_TILE_H - H * CoordUtil.TILE_HEIGHT),          // (0,0,H)
                ((E - 0) * CoordUtil.HALF_TILE_W,     -(E + 0) * CoordUtil.HALF_TILE_H - H * CoordUtil.TILE_HEIGHT),          // (E,0,H)
                ((E - N) * CoordUtil.HALF_TILE_W,     -(E + N) * CoordUtil.HALF_TILE_H - H * CoordUtil.TILE_HEIGHT),          // (E,N,H)
                ((0 - N) * CoordUtil.HALF_TILE_W,     -(0 + N) * CoordUtil.HALF_TILE_H - H * CoordUtil.TILE_HEIGHT),          // (0,N,H)
            };
            var southVerts = new (float wx, float wy)[]
            {
                (0,                    0),                                        // (0,0,0)
                ((E - 0) * CoordUtil.HALF_TILE_W,     -(E + 0) * CoordUtil.HALF_TILE_H),                       // (E,0,0)
                ((E - 0) * CoordUtil.HALF_TILE_W,     -(E + 0) * CoordUtil.HALF_TILE_H - H * CoordUtil.TILE_HEIGHT),          // (E,0,H)
                (0,                    -H * CoordUtil.TILE_HEIGHT),                              // (0,0,H)
            };
            var westVerts = new (float wx, float wy)[]
            {
                (0,                    0),                                        // (0,0,0)
                (0,                    -H * CoordUtil.TILE_HEIGHT),                              // (0,0,H)
                ((0 - N) * CoordUtil.HALF_TILE_W,     -(0 + N) * CoordUtil.HALF_TILE_H - H * CoordUtil.TILE_HEIGHT),          // (0,N,H)
                ((0 - N) * CoordUtil.HALF_TILE_W,     -(0 + N) * CoordUtil.HALF_TILE_H),                       // (0,N,0)
            };
            var allFaces = new[] { topVerts, southVerts, westVerts };

            foreach (var face in allFaces)
            {
                for (int i = 0; i < face.Length; i++)
                {
                    var (wx0, wy0) = face[i];
                    var (wx1, wy1) = face[(i + 1) % face.Length];
                    // Convert world → texel
                    int tx0 = (int)(wx0 - anchorWx + halfTW + 0.5f);
                    int ty0 = (int)(wy0 - anchorWy + halfTH + 0.5f);
                    int tx1 = (int)(wx1 - anchorWx + halfTW + 0.5f);
                    int ty1 = (int)(wy1 - anchorWy + halfTH + 0.5f);
                    PixelUtils.DrawLine(data, texW, texH, tx0, ty0, tx1, ty1, edgeColor);
                }
            }
        }
    }
}
