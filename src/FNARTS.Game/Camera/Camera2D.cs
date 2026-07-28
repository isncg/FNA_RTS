using Microsoft.Xna.Framework;

namespace FNARTS.Game
{
    /// <summary>
    /// 2D camera for isometric RTS. Provides ViewMatrix for SpriteBatch
    /// transformMatrix and ScreenToWorld for mouse picking.
    /// </summary>
    public class Camera2D
    {
        public float PanSpeed { get; set; } = 600f;
        public float ZoomSpeed { get; set; } = 0.1f;
        public float MinZoom { get; set; } = 0.25f;
        public float MaxZoom { get; set; } = 4.0f;

        public Vector2 Position { get; set; }
        public float Zoom { get; set; } = 1.0f;

        public Matrix ViewMatrix { get; private set; } = Matrix.Identity;
        public Matrix InverseViewMatrix { get; private set; } = Matrix.Identity;

        public Vector2? WorldBoundMin { get; set; }
        public Vector2? WorldBoundMax { get; set; }

        public int ViewportWidth => _viewportW;
        public int ViewportHeight => _viewportH;
        private int _viewportW, _viewportH;

        public Camera2D(int viewportWidth, int viewportHeight)
        {
            _viewportW = viewportWidth;
            _viewportH = viewportHeight;
        }

        public void Resize(int vw, int vh) { _viewportW = vw; _viewportH = vh; }

        public Vector2 ScreenToWorld(Vector2 screenPos)
            => Vector2.Transform(screenPos, InverseViewMatrix);

        public Vector2 WorldToScreen(Vector2 worldPos)
            => Vector2.Transform(worldPos, ViewMatrix);

        public void RebuildMatrices() => BuildMatrices();

        /// <summary>Apply bounds clamping to the current position (call after manual pan).</summary>
        public void ClampToBounds() => ClampPosition();

        private void ClampPosition()
        {
            if (WorldBoundMin.HasValue && WorldBoundMax.HasValue)
            {
                float hvw = _viewportW / (2f * Zoom);
                float hvh = _viewportH / (2f * Zoom);
                var min = WorldBoundMin.Value;
                var max = WorldBoundMax.Value;
                float clampMinX = min.X + hvw;
                float clampMaxX = max.X - hvw;
                float clampMinY = min.Y + hvh;
                float clampMaxY = max.Y - hvh;

                // When zoomed out past the point where the viewport fits
                // within the bounds, centre the camera on the bounds.
                Position = new Vector2(
                    clampMinX <= clampMaxX
                        ? MathHelper.Clamp(Position.X, clampMinX, clampMaxX)
                        : (min.X + max.X) * 0.5f,
                    clampMinY <= clampMaxY
                        ? MathHelper.Clamp(Position.Y, clampMinY, clampMaxY)
                        : (min.Y + max.Y) * 0.5f);
            }
        }

        private void BuildMatrices()
        {
            ViewMatrix =
                Matrix.CreateTranslation(-Position.X, -Position.Y, 0) *
                Matrix.CreateScale(Zoom, Zoom, 1f) *
                Matrix.CreateTranslation(_viewportW / 2f, _viewportH / 2f, 0);
            InverseViewMatrix = Matrix.Invert(ViewMatrix);
        }
    }
}
