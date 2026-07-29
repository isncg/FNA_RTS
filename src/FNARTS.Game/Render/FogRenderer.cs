using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNARTS.Core;
using FNARTS.Core.Fog;

namespace FNARTS.Game
{
    /// <summary>
    /// Draws the fog-of-war overlay on the tile map.
    /// Uses the DiamondHighlight texture tinted black at different alpha levels.
    /// </summary>
    public class FogRenderer : IDisposable
    {
        private readonly IAssetProvider _assets;

        // Alpha values for the two fogged states.
        private static readonly Color UnexploredTint = new Color(0, 0, 0, 235);
        private static readonly Color ExploredTint   = new Color(0, 0, 0, 110);

        public FogRenderer(IAssetProvider assets)
        {
            _assets = assets;
        }

        /// <summary>
        /// Draw diamond-shaped fog overlays on every fogged tile that is
        /// currently on screen.  Visible tiles are skipped.
        /// </summary>
        public void Draw(SpriteBatch sb, Camera2D camera, TileMap map,
            FogOfWar fog)
        {
            // ── visible tile range (same sampling strategy as TileRenderer) ──
            int vw = camera.ViewportWidth;
            int vh = camera.ViewportHeight;
            int W = map.Width, H = map.Height;

            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;

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
                SampleScreenPoint(vw * t, 0);
                SampleScreenPoint(vw * t, vh);
                SampleScreenPoint(0, vh * t);
                SampleScreenPoint(vw, vh * t);
            }
            SampleScreenPoint(0, 0);
            SampleScreenPoint(vw, 0);
            SampleScreenPoint(0, vh);
            SampleScreenPoint(vw, vh);

            const int margin = 4;
            minX -= margin; maxX += margin;
            minY -= margin; maxY += margin;

            // ── draw fog diamonds ────────────────────────────────────────
            var tex = _assets.DiamondHighlight;
            float maxSum = W + H;

            sb.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullCounterClockwise, null,
                camera.ViewMatrix);

            for (int gy = minY; gy <= maxY; gy++)
            {
                for (int gx = minX; gx <= maxX; gx++)
                {
                    var coord = new IsoCoord(gx, gy);
                    if (!map.InBounds(coord)) continue;

                    var state = fog[gx, gy];
                    if (state == FogCell.Visible) continue;

                    Vector2 pos = CoordUtil.IsoToWorld(new IsoCoord(gx, gy)).ToXna();
                    // Slightly in front of terrain to avoid z-fighting.
                    float depth = MathHelper.Clamp(
                        (gx + gy + 0.15f) / maxSum, 0, 1);
                    Color tint = state == FogCell.Unexplored
                        ? UnexploredTint : ExploredTint;

                    sb.Draw(tex, pos, null, tint, 0f, Vector2.Zero, 1f,
                        SpriteEffects.None, depth);
                }
            }

            sb.End();
        }

        public void Dispose() { }
    }
}
