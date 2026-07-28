using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNARTS.Game
{
    /// <summary>
    /// Debug performance overlay showing FPS, frame time, entity count, etc.
    /// Toggle with F3 key. Now uses BitmapFont for real text rendering.
    /// </summary>
    public class DebugOverlay
    {
        public bool Enabled { get; set; }

        // Metrics
        public float Fps { get; set; }
        public double FrameTimeMs { get; set; }
        public int DrawCalls { get; set; }
        public int VisibleTiles { get; set; }
        public int EntityCount { get; set; }
        public long MemoryBytes { get; set; }
        public Vector2 CameraPos { get; set; }
        public float CameraZoom { get; set; }

        // Placement info (optional, shown when placement is active)
        public bool PlacementActive { get; set; }
        public string PlacementInfo { get; set; } = "";

        private BitmapFont _font;
        private SpriteBatch _sb;
        private Texture2D _bgTex;

        // Cached position for minimap to avoid GC pressure (Draw() is called per-frame)
        private Vector2 _pos = Vector2.Zero;

        public void Initialize(GraphicsDevice device)
        {
            _font = new BitmapFont(device);
            _sb = new SpriteBatch(device);
            _bgTex = new Texture2D(device, 1, 1);
            _bgTex.SetData(new[] { Color.White });
        }

        public void Draw()
        {
            if (!Enabled || _sb == null || _font == null) return;

            int lineH = BitmapFont.GLYPH_H + 1;
            int lines = PlacementActive ? 7 : 6;
            int panelW = 340;
            int panelH = lines * lineH + 10;

            _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            // Semi-transparent background
            _sb.Draw(_bgTex, new Rectangle(0, 0, panelW, panelH),
                new Color(0, 0, 0, 170));

            int y = 4;
            DrawLine(_sb, $"FPS: {Fps:F0}  Frame: {FrameTimeMs:F2} ms", ref y, Color.White);
            DrawLine(_sb, $"Entities: {EntityCount}  DrawCalls: {DrawCalls}", ref y,
                new Color(180, 180, 180));
            DrawLine(_sb, $"Camera: ({CameraPos.X:F0}, {CameraPos.Y:F0})  Zoom: {CameraZoom:F2}",
                ref y, new Color(180, 180, 180));
            DrawLine(_sb, $"Memory: {MemoryBytes / 1024} KB", ref y,
                new Color(140, 140, 160));

            if (PlacementActive)
            {
                DrawLine(_sb, $"Place: {PlacementInfo}", ref y, new Color(100, 255, 100));
            }

            DrawLine(_sb, "F3: toggle overlay | B: build | RClick: move", ref y,
                new Color(120, 120, 140));

            _sb.End();
        }

        private void DrawLine(SpriteBatch sb, string text, ref int y, Color color)
        {
            _pos.X = 6;
            _pos.Y = y;
            _font.DrawString(sb, text, _pos, color);
            y += BitmapFont.GLYPH_H + 1;
        }

        public void Dispose()
        {
            _font?.Dispose();
            _sb?.Dispose();
            _bgTex?.Dispose();
        }
    }
}
