using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNARTS.Core;
using FNARTS.Core.Config;
using FNARTS.Game;

namespace FNARTS.Editor
{
    /// <summary>
    /// Left-side panel UI for the terrain editor.
    /// Renders with BitmapFont (no external assets needed).
    /// </summary>
    public class EditorUI
    {
        public const int PANEL_W = 160;

        private BitmapFont _font;
        private readonly List<Button> _buttons = new();

        // Selected tool
        public EditorTool CurrentTool { get; set; } = EditorTool.Terrain;
        public TileType SelectedTerrain { get; set; } = TileType.Grass;
        public int SelectedBuildingIndex { get; set; } = -1;

        // Hover state for map overlay
        public IsoCoord? HoveredTile { get; set; }
        public bool IsMouseInMapArea { get; set; }

        public void LoadContent(BitmapFont font)
        {
            _font = font;

            // ── Terrain buttons ──
            _buttons.Add(new Button("Terrain", 10, 10, PANEL_W - 20, 20, () =>
            {
                CurrentTool = EditorTool.Terrain;
                SelectedBuildingIndex = -1;
            }));

            int y = 35;
            foreach (TileType type in Enum.GetValues<TileType>())
            {
                var captured = type;
                _buttons.Add(new Button($"  {type}", 20, y, PANEL_W - 30, 16, () =>
                {
                    CurrentTool = EditorTool.Terrain;
                    SelectedTerrain = captured;
                    SelectedBuildingIndex = -1;
                }));
                y += 18;
            }

            // ── Building section ──
            y += 10;
            _buttons.Add(new Button("Buildings", 10, y, PANEL_W - 20, 20, () =>
            {
                CurrentTool = EditorTool.Building;
            }));
            y += 22;

            // Read building defs from config
            var config = LoadConfig();
            for (int i = 0; i < config.Count; i++)
            {
                int idx = i;
                var def = config[i];
                _buttons.Add(new Button($"  {def.Name} ({def.SizeX}x{def.SizeY})",
                    20, y, PANEL_W - 30, 16, () =>
                {
                    CurrentTool = EditorTool.Building;
                    SelectedBuildingIndex = idx;
                    SelectedTerrain = TileType.Grass;
                }));
                y += 18;
            }

            // ── Operations ──
            y += 10;
            _buttons.Add(new Button("Save (Ctrl+S)", 10, y, PANEL_W - 20, 20,
                () => EditorGame.Instance.SaveMap()));
            y += 22;
            _buttons.Add(new Button("Load (Ctrl+O)", 10, y, PANEL_W - 20, 20,
                () => EditorGame.Instance.LoadMapDialog()));
        }

        /// <summary>Draw the left panel.</summary>
        public void DrawPanel(SpriteBatch sb)
        {
            // Panel background
            var bg = new Color(40, 40, 55);
            sb.Draw(EditorGame.Instance.Assets.WhitePixel,
                new Rectangle(0, 0, PANEL_W, EditorGame.Instance.GraphicsDevice.Viewport.Height),
                bg);

            // Buttons
            foreach (var btn in _buttons)
            {
                bool isSelected = IsButtonSelected(btn);
                var color = isSelected ? new Color(80, 100, 140) : new Color(50, 50, 65);
                sb.Draw(EditorGame.Instance.Assets.WhitePixel,
                    new Rectangle((int)btn.X, (int)btn.Y, (int)btn.W, (int)btn.H),
                    color);
                _font.DrawString(sb, btn.Label,
                    new Vector2(btn.X + 2, btn.Y + 2), Color.White);
            }

            // File info
            int y = EditorGame.Instance.GraphicsDevice.Viewport.Height - 40;
            string fileInfo = EditorGame.Instance.IsDirty ? "[Unsaved]" : "";
            _font.DrawString(sb, fileInfo, new Vector2(5, y), Color.Yellow);
        }

        /// <summary>Draw overlay on the map (hover highlight, building preview).</summary>
        public void DrawMapOverlay(SpriteBatch sb, Camera2D camera, TileRenderer tileRenderer)
        {
            if (!HoveredTile.HasValue || !IsMouseInMapArea)
                return;

            var h = HoveredTile.Value;

            if (CurrentTool == EditorTool.Terrain)
            {
                // Highlight hovered tile
                tileRenderer.DrawHighlights(sb, camera,
                    new[] { h }, new Color(255, 255, 255, 80));
            }
            else if (CurrentTool == EditorTool.Building && SelectedBuildingIndex >= 0)
            {
                // Show building preview
                var config = LoadConfig();
                if (SelectedBuildingIndex < config.Count)
                {
                    var def = config[SelectedBuildingIndex];
                    var tiles = new List<IsoCoord>();
                    for (int dx = 0; dx < def.SizeX; dx++)
                    for (int dy = 0; dy < def.SizeY; dy++)
                        tiles.Add(new IsoCoord(h.X + dx, h.Y + dy));

                    // Check if placement is valid (all tiles passable)
                    bool valid = true;
                    var map = EditorGame.Instance.Map;
                    foreach (var t in tiles)
                    {
                        if (!map.InBounds(t) || !map.IsPassable(t))
                        { valid = false; break; }
                    }

                    var color = valid
                        ? new Color(0, 255, 0, 100)
                        : new Color(255, 0, 0, 100);
                    tileRenderer.DrawHighlights(sb, camera, tiles, color);
                }
            }
        }

        /// <summary>Handle a mouse click on the panel. Returns true if handled.</summary>
        public bool HandlePanelClick(int mouseX, int mouseY)
        {
            if (mouseX > PANEL_W) return false;

            foreach (var btn in _buttons)
            {
                if (mouseX >= btn.X && mouseX <= btn.X + btn.W &&
                    mouseY >= btn.Y && mouseY <= btn.Y + btn.H)
                {
                    btn.Action?.Invoke();
                    return true;
                }
            }
            return false;
        }

        private bool IsButtonSelected(Button btn)
        {
            if (btn.Label.StartsWith("  ") && CurrentTool == EditorTool.Terrain)
            {
                // Check if this terrain button matches the selected terrain
                var label = btn.Label.Trim();
                foreach (TileType type in Enum.GetValues<TileType>())
                {
                    if (label == type.ToString())
                        return SelectedTerrain == type;
                }
            }
            if (btn.Label.StartsWith("  ") && CurrentTool == EditorTool.Building)
            {
                var config = LoadConfig();
                int idx = 0;
                foreach (var b in _buttons)
                {
                    if (b.Label.StartsWith("  ") && b.Label != btn.Label)
                        idx++;
                    else if (b == btn)
                        break;
                }
                // Adjust index: only count buttons after "Buildings" header
                int buildingStart = 0;
                for (int i = 0; i < _buttons.Count; i++)
                {
                    if (_buttons[i].Label == "Buildings")
                    { buildingStart = i + 1; break; }
                }
                int relIdx = 0;
                for (int i = buildingStart; i < _buttons.Count; i++)
                {
                    if (_buttons[i].Label.StartsWith("  "))
                    {
                        if (_buttons[i] == btn)
                            return SelectedBuildingIndex == relIdx;
                        relIdx++;
                    }
                }
            }
            return false;
        }

        private static List<BuildingDef> _cachedConfig;
        public static List<BuildingDef> LoadConfig()
        {
            if (_cachedConfig != null) return _cachedConfig;

            string dataDir = System.IO.Path.Combine(
                AppContext.BaseDirectory, "data");
            var config = ConfigLoader.Load(dataDir);
            _cachedConfig = new List<BuildingDef>(config.BuildingDefs.Values);
            return _cachedConfig;
        }

        private class Button
        {
            public string Label;
            public float X, Y, W, H;
            public Action Action;

            public Button(string label, float x, float y, float w, float h, Action action)
            {
                Label = label; X = x; Y = y; W = w; H = h; Action = action;
            }
        }
    }

    public enum EditorTool
    {
        Terrain,
        Building,
    }
}