using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNARTS.Core;

namespace FNARTS.Game
{
    /// <summary>
    /// Bottom command panel showing selected entity info and placement status.
    /// </summary>
    public class CommandPanel
    {
        private readonly BitmapFont _font;
        private readonly Texture2D _bgTex;
        private readonly Texture2D _whiteTex;

        public const int PANEL_H = 140;
        private const int PADDING = 8;

        // External state set by RTSGame each frame
        public int ViewportW { get; set; }
        public int ViewportH { get; set; }
        public SelectionSystem Selection { get; set; }
        public EntityManager Entities { get; set; }
        public bool PlacementActive { get; set; }
        public string PlacementName { get; set; } = "";
        public int PlacementIndex { get; set; }
        public int PlacementCount { get; set; }
        public Texture2D MinimapTexture { get; set; }

        // Production state (set when a single producing building is selected)
        public Building SelectedBuilding { get; set; }
        public Dictionary<string, UnitDef> UnitDefs { get; set; }

        // Hit-testing: production button screen-space rects (cleared each Draw)
        private readonly List<(Rectangle rect, string unitDefId)> _prodButtons = new();

        public CommandPanel(GraphicsDevice device)
        {
            _font = new BitmapFont(device);
            _bgTex = new Texture2D(device, 1, 1);
            _bgTex.SetData(new[] { Color.White });
            _whiteTex = new Texture2D(device, 1, 1);
            _whiteTex.SetData(new[] { Color.White });
        }

        public void Draw(SpriteBatch sb)
        {
            int w = ViewportW;
            int h = ViewportH;
            if (w <= 0 || h <= 0) return;

            int panelY = h - PANEL_H;

            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            // Panel background
            sb.Draw(_bgTex, new Rectangle(0, panelY, w, PANEL_H),
                new Color(20, 22, 28, 220));
            // Top border accent
            sb.Draw(_bgTex, new Rectangle(0, panelY, w, 2),
                new Color(100, 180, 255, 200));

            // ── Left: selection info ──────────────────────────────────
            int x = PADDING;
            int y = panelY + PADDING;

            if (PlacementActive)
            {
                DrawHeader(sb, "PLACEMENT MODE", ref x, ref y, new Color(100, 255, 100));
                x = PADDING;
                string typeStr = $"[{PlacementIndex + 1}/{PlacementCount}] {PlacementName}";
                DrawLine(sb, typeStr, ref x, ref y, new Color(200, 200, 200));
                x = PADDING;
                DrawLine(sb, "Tab/1-9:switch  LClick:place  RCancel/Esc:cancel", ref x, ref y,
                    new Color(140, 140, 160));
            }
            else if (Selection != null && Selection.SelectedEntityIds.Count > 0)
            {
                var ids = Selection.SelectedEntityIds;
                DrawHeader(sb, $"SELECTED: {ids.Count}", ref x, ref y, new Color(100, 255, 100));

                // Group by type
                int units = 0, buildings = 0;
                string lastName = "";
                foreach (var id in ids)
                {
                    var e = Entities?.GetEntity(id);
                    if (e is Unit) { units++; lastName = ((Unit)e).Definition.Name; }
                    else if (e is Building) { buildings++; lastName = ((Building)e).Definition.Name; }
                }

                x = PADDING;
                if (units > 0)
                {
                    DrawIcon(sb, x, y, new Color(200, 200, 100), 6);
                    DrawLine(sb, $" Units: {units}", ref x, ref y, new Color(200, 200, 100));
                    x = PADDING;
                }
                if (buildings > 0)
                {
                    DrawIcon(sb, x, y, new Color(140, 140, 180), 5);
                    DrawLine(sb, $" Buildings: {buildings}", ref x, ref y, new Color(140, 140, 180));
                    x = PADDING;
                }
                if (ids.Count == 1)
                {
                    DrawLine(sb, $"Name: {lastName}", ref x, ref y, new Color(180, 180, 180));
                }
                x = PADDING;
                DrawLine(sb, "Right-click: move   B: build mode", ref x, ref y,
                    new Color(140, 140, 160));
            }
            else
            {
                DrawHeader(sb, "NO SELECTION", ref x, ref y, new Color(140, 140, 160));
                x = PADDING;
                DrawLine(sb, "Left-click: select unit/building", ref x, ref y,
                    new Color(140, 140, 160));
                x = PADDING;
                DrawLine(sb, "Drag: box-select   B: build mode", ref x, ref y,
                    new Color(140, 140, 160));
            }

            // ── Centre: production buttons ─────────────────────────────
            _prodButtons.Clear();
            int mmSizeForProd = PANEL_H - PADDING * 2;
            int mmXForProd = w - PADDING - mmSizeForProd;
            if (!PlacementActive && SelectedBuilding != null && UnitDefs != null)
            {
                var prodIds = SelectedBuilding.Definition.ProducesUnitIds;
                if (prodIds != null && prodIds.Count > 0)
                {
                    int px = mmXForProd - 220; // left-align buttons, leave gap to minimap
                    int py = panelY + PADDING;

                    DrawHeader(sb, "TRAIN", ref px, ref py, new Color(100, 200, 255));
                    px = mmXForProd - 220;

                    // Current production progress
                    var cp = SelectedBuilding.CurrentProduction;
                    if (cp != null && UnitDefs.TryGetValue(cp.UnitDefId, out var cd))
                    {
                        string progText = $"{cd.Name} {cp.RemainingTime:F1}s / {cp.TotalTime:F1}s";
                        DrawLine(sb, progText, ref px, ref py, new Color(180, 200, 220));
                        px = mmXForProd - 220;
                        // Progress bar
                        int barW = 210;
                        int barH = 8;
                        int barX = px;
                        int barY = py;
                        sb.Draw(_bgTex, new Rectangle(barX, barY, barW, barH),
                            new Color(30, 34, 42, 220));
                        int fillW = (int)(barW * cp.Progress);
                        if (fillW > 0)
                            sb.Draw(_whiteTex, new Rectangle(barX, barY, fillW, barH),
                                new Color(80, 200, 120, 220));
                        py += barH + 4;
                        px = mmXForProd - 220;
                    }

                    // Queue count
                    if (SelectedBuilding.ProductionQueue.Count > 0)
                    {
                        DrawLine(sb, $"Queue: {SelectedBuilding.ProductionQueue.Count}",
                            ref px, ref py, new Color(160, 170, 190));
                        px = mmXForProd - 220;
                    }

                    // Buttons for trainable units
                    foreach (var uid in prodIds)
                    {
                        if (UnitDefs == null || !UnitDefs.TryGetValue(uid, out var ud))
                            continue;

                        int btnW = 210, btnH = 24;
                        var btnRect = new Rectangle(px, py, btnW, btnH);
                        _prodButtons.Add((btnRect, uid));

                        // Button background
                        sb.Draw(_bgTex, btnRect, new Color(40, 48, 60, 220));
                        // Button border
                        sb.Draw(_bgTex, new Rectangle(px, py, btnW, 1),
                            new Color(80, 100, 140, 200));
                        sb.Draw(_bgTex, new Rectangle(px, py + btnH - 1, btnW, 1),
                            new Color(80, 100, 140, 200));

                        string label = $"+ {ud.Name} ({ud.BuildTime:F1}s)";
                        _font.DrawString(sb, label,
                            new Vector2(px + 6, py + (btnH - BitmapFont.GLYPH_H) / 2),
                            new Color(200, 210, 230));
                        py += btnH + 3;
                    }
                }
            }

            // ── Right: minimap ────────────────────────────────────────
            int mmSize = PANEL_H - PADDING * 2;
            int mmX = w - PADDING - mmSize;
            int mmY = panelY + PADDING;

            if (MinimapTexture != null)
            {
                // Stretch minimap to fill the square frame
                sb.Draw(_bgTex, new Rectangle(mmX - 1, mmY - 1, mmSize + 2, mmSize + 2),
                    new Color(30, 32, 38, 220));
                sb.Draw(MinimapTexture,
                    new Rectangle(mmX, mmY, mmSize, mmSize), Color.White);
            }
            else
            {
                sb.Draw(_bgTex, new Rectangle(mmX, mmY, mmSize, mmSize),
                    new Color(40, 44, 52, 200));
                _font.DrawString(sb, "MINIMAP", new Vector2(mmX + 4, mmY + 4),
                    new Color(100, 100, 120));
            }

            // Border
            sb.Draw(_bgTex, new Rectangle(mmX - 1, mmY - 1, mmSize + 2, 1),
                new Color(100, 100, 120, 200));
            sb.Draw(_bgTex, new Rectangle(mmX - 1, mmY + mmSize, mmSize + 2, 1),
                new Color(100, 100, 120, 200));
            sb.Draw(_bgTex, new Rectangle(mmX - 1, mmY, 1, mmSize),
                new Color(100, 100, 120, 200));
            sb.Draw(_bgTex, new Rectangle(mmX + mmSize, mmY, 1, mmSize),
                new Color(100, 100, 120, 200));

            sb.End();
        }

        private void DrawHeader(SpriteBatch sb, string text, ref int x, ref int y, Color color)
        {
            _font.DrawString(sb, text, new Vector2(x, y), color);
            y += BitmapFont.GLYPH_H + 2;
        }

        private void DrawLine(SpriteBatch sb, string text, ref int x, ref int y, Color color)
        {
            _font.DrawString(sb, text, new Vector2(x, y), color);
            y += BitmapFont.GLYPH_H + 1;
        }

        private void DrawIcon(SpriteBatch sb, int x, int y, Color color, int radius)
        {
            sb.Draw(_whiteTex, new Rectangle(x, y + 3, radius * 2, radius * 2), color);
        }

        /// <summary>
        /// Handle a left-click on the panel.  If the click hits a production
        /// button, returns the UnitDefId to train.  Otherwise returns null.
        /// </summary>
        public string HandleClick(Vector2 mousePos)
        {
            int px = (int)mousePos.X;
            int py = (int)mousePos.Y;
            foreach (var (rect, unitDefId) in _prodButtons)
            {
                if (px >= rect.Left && px < rect.Right &&
                    py >= rect.Top && py < rect.Bottom)
                    return unitDefId;
            }
            return null;
        }

        public void Dispose()
        {
            _font?.Dispose();
            _bgTex?.Dispose();
            _whiteTex?.Dispose();
        }
    }
}
