using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNARTS.Core;

namespace FNARTS.Game
{
    /// <summary>
    /// Renders the isometric tile map with frustum culling and depth sorting.
    /// Fills triangular border areas with dark boundary tiles (C&amp;C2 style).
    /// </summary>
    public class TileRenderer : IDisposable
    {
        private readonly TileMap _map;
        private readonly IAssetProvider _assets;
        private readonly Rectangle _boundarySrcRect;  // tileset rect for boundary fill

        public TileRenderer(TileMap map, IAssetProvider assets)
        {
            _map = map;
            _assets = assets;
            // Use the cliff tile from the tileset as the boundary/edge fill
            _boundarySrcRect = assets.GetTileSourceRect(TileType.Cliff);
        }

        /// <summary>Draw coloured diamond highlights on a set of grid tiles
        /// (e.g. placement preview).</summary>
        public void DrawHighlights(SpriteBatch sb, Camera2D camera,
            IEnumerable<IsoCoord> tiles, Color tint)
        {
            // Depth normaliser matching CoordUtil.ComputeDepth.
            // +0.3 bias prevents z-fighting with terrain tiles.
            float maxSum = _map.Width + _map.Height;
            var tex = _assets.DiamondHighlight;

            sb.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullCounterClockwise, null,
                camera.ViewMatrix);

            foreach (var c in tiles)
            {
                Vector2 pos = CoordUtil.IsoToWorld(c).ToXna();
                float depth = MathHelper.Clamp((c.X + c.Y + 0.3f) / maxSum, 0, 1);
                sb.Draw(tex, pos, null, tint, 0f, Vector2.Zero, 1f,
                    SpriteEffects.None, depth);
            }

            sb.End();
        }

        public void Draw(SpriteBatch sb, Camera2D camera)
        {
            int W = _map.Width, H = _map.Height;

            // Compute visible tile range: sample many points along the
            // screen perimeter and take the bounding grid extent, plus a
            // generous margin to cover partially-visible tile diamonds.
            int vw = camera.ViewportWidth;
            int vh = camera.ViewportHeight;

            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;

            // Sample grid + midpoints along each screen edge (20 samples total)
            void SampleScreenPoint(float sx, float sy)
            {
                var w = camera.ScreenToWorld(new Vector2(sx, sy));
                var g = CoordUtil.WorldToIso(w.ToNumerics());
                if (g.X < minX) minX = g.X; if (g.X > maxX) maxX = g.X;
                if (g.Y < minY) minY = g.Y; if (g.Y > maxY) maxY = g.Y;
            }

            for (int i = 0; i <= 4; i++)
            {
                float t = i / 4f;
                SampleScreenPoint(vw * t, 0);          // top edge
                SampleScreenPoint(vw * t, vh);          // bottom edge
                SampleScreenPoint(0, vh * t);           // left edge
                SampleScreenPoint(vw, vh * t);          // right edge
            }
            // Also sample the four corners (some are duplicates, fine)
            SampleScreenPoint(0, 0);
            SampleScreenPoint(vw, 0);
            SampleScreenPoint(0, vh);
            SampleScreenPoint(vw, vh);

            // Fixed generous margin: 2 tiles covers the half-diamond of
            // partially-visible tiles at any sane zoom level.
            const int margin = 4;
            minX -= margin;
            maxX += margin;
            minY -= margin;
            maxY += margin;

            sb.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullCounterClockwise, null,
                camera.ViewMatrix);

            for (int gy = minY; gy <= maxY; gy++)
            {
                for (int gx = minX; gx <= maxX; gx++)
                {
                    bool inBounds = (uint)gx < (uint)W && (uint)gy < (uint)H;
                    Vector2 pos = CoordUtil.IsoToWorld(new IsoCoord(gx, gy)).ToXna();

                    if (inBounds)
                    {
                        var tile = _map.GetTile(gx, gy);
                        Rectangle srcRect = _assets.GetTileSourceRect(tile.Type);
                        // Normalised depth: SW=front (0), NE=back (1)
                        float depth = MathHelper.Clamp(
                            (float)(gx + gy) / (W + H), 0, 1);
                        sb.Draw(_assets.TilesetTexture, pos, srcRect, Color.White,
                            0f, Vector2.Zero, 1f, SpriteEffects.None, depth);
                    }
                    else
                    {
                        // Out-of-bounds: render cliff tile from tileset at far depth
                        sb.Draw(_assets.TilesetTexture, pos, _boundarySrcRect,
                            new Color(80, 80, 90),  // slightly tinted for edge feel
                            0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);
                    }
                }
            }

            sb.End();
        }

        public void Dispose() { }
    }
}
