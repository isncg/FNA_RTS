using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNARTS.Game
{
    public class SelectionRenderer
    {
        private readonly IAssetProvider _assets;

        public SelectionRenderer(IAssetProvider assets)
        {
            _assets = assets;
        }

        public void DrawDragRect(SpriteBatch sb, Vector2 start, Vector2 end)
        {
            int x = (int)System.Math.Min(start.X, end.X);
            int y = (int)System.Math.Min(start.Y, end.Y);
            int w = (int)System.Math.Abs(end.X - start.X);
            int h = (int)System.Math.Abs(end.Y - start.Y);
            if (w < 2 || h < 2) return;

            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            sb.Draw(_assets.WhitePixel, new Rectangle(x, y, w, h),
                new Color(0, 255, 0, 40));

            int bw = 1;
            sb.Draw(_assets.WhitePixel, new Rectangle(x, y, w, bw), Color.Lime);
            sb.Draw(_assets.WhitePixel, new Rectangle(x, y + h - bw, w, bw), Color.Lime);
            sb.Draw(_assets.WhitePixel, new Rectangle(x, y, bw, h), Color.Lime);
            sb.Draw(_assets.WhitePixel, new Rectangle(x + w - bw, y, bw, h), Color.Lime);

            sb.End();
        }

        public void DrawHighlights(SpriteBatch sb, Camera2D camera,
            FNARTS.Core.Entity entity)
        {
            if (!entity.IsSelected) return;

            Vector2 screenPos = camera.WorldToScreen(entity.WorldPosition.ToXna());
            Vector2 origin = new Vector2(
                _assets.SelectionHighlight.Width / 2f,
                _assets.SelectionHighlight.Height / 2f);

            sb.Draw(_assets.SelectionHighlight, screenPos, null, Color.White,
                0f, origin, 1f, SpriteEffects.None, 0);
        }
    }
}
