using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNARTS.Core;

namespace FNARTS.Game
{
    /// <summary>
    /// Draws health bars above damaged entities.
    /// Rendered in screen space so bar size stays constant regardless of zoom.
    /// </summary>
    public class HpBarRenderer : IDisposable
    {
        private const int BAR_W = 30;
        private const int BAR_H = 4;

        private readonly IAssetProvider _assets;

        public HpBarRenderer(IAssetProvider assets)
        {
            _assets = assets;
        }

        /// <summary>
        /// Draw HP bars for all damaged alive entities.
        /// Full-health entities are skipped to reduce visual clutter.
        /// </summary>
        public void Draw(SpriteBatch sb, Camera2D camera, EntityManager entities)
        {
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            foreach (var e in entities.AllEntities)
            {
                if (!e.IsAlive) continue;

                int cur, max;
                float screenOffsetY;

                if (e is Unit u)
                {
                    if (u.CurrentHP >= u.MaxHP) continue;
                    cur = u.CurrentHP;
                    max = u.MaxHP;
                    // Unit sprite is 32×32, origin at centre → top edge at
                    // screenPos.Y - 16*zoom.  Place bar 6 px above that.
                    screenOffsetY = 16f * camera.Zoom + 8f;
                }
                else if (e is Building b)
                {
                    if (b.CurrentHP >= b.MaxHP) continue;
                    cur = b.CurrentHP;
                    max = b.MaxHP;
                    // Building sprites vary — use the world-space half-extent
                    // scaled by camera zoom to estimate the screen-space top.
                    screenOffsetY = b.HitHalfExtent.Y * camera.Zoom + 8f;
                }
                else continue;

                float frac = (float)cur / max;
                Vector2 screenPos = camera.WorldToScreen(e.WorldPosition.ToXna());

                int bx = (int)(screenPos.X - BAR_W / 2f);
                int by = (int)(screenPos.Y - screenOffsetY);

                // Background: dark grey, semi-transparent
                var bgRect = new Rectangle(bx, by, BAR_W, BAR_H);
                sb.Draw(_assets.WhitePixel, bgRect, new Color(20, 20, 20, 200));

                // Fill: green → yellow → red gradient
                int fillW = Math.Max(1, (int)(BAR_W * frac));
                Color fill = HpFractionToColor(frac);
                var fillRect = new Rectangle(bx, by, fillW, BAR_H);
                sb.Draw(_assets.WhitePixel, fillRect, fill);

                // 1px border using the fill colour darkened
                var borderColor = new Color(
                    Math.Max(0, fill.R - 60),
                    Math.Max(0, fill.G - 60),
                    Math.Max(0, fill.B - 60),
                    230);
                // Top edge
                sb.Draw(_assets.WhitePixel, new Rectangle(bx, by, BAR_W, 1), borderColor);
                // Bottom edge
                sb.Draw(_assets.WhitePixel, new Rectangle(bx, by + BAR_H - 1, BAR_W, 1), borderColor);
                // Left edge
                sb.Draw(_assets.WhitePixel, new Rectangle(bx, by, 1, BAR_H), borderColor);
                // Right edge
                sb.Draw(_assets.WhitePixel, new Rectangle(bx + BAR_W - 1, by, 1, BAR_H), borderColor);
            }

            sb.End();
        }

        /// <summary>
        /// Map HP fraction [0..1] to a green→yellow→red gradient.
        /// frac ≥ 0.5 : green → yellow
        /// frac &lt; 0.5 : yellow → red
        /// </summary>
        private static Color HpFractionToColor(float frac)
        {
            if (frac > 0.5f)
            {
                // Green (0,255,0) → Yellow (255,255,0)
                int r = (int)(510 * (1f - frac)); // 0 at frac=1.0, 255 at frac=0.5
                return new Color(r, 255, 0);
            }
            else
            {
                // Yellow (255,255,0) → Red (255,0,0)
                int g = (int)(510 * frac); // 0 at frac=0, 255 at frac=0.5
                return new Color(255, g, 0);
            }
        }

        public void Dispose() { }
    }
}
