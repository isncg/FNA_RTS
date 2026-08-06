using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using FNARTS.Core;
using FNARTS.Game;

namespace FNARTS.Editor
{
    /// <summary>
    /// Handles editor input: terrain painting, building placement,
    /// camera control, and keyboard shortcuts.
    /// </summary>
    public class EditorInput
    {
        private KeyboardState _prevKb;
        private MouseState _prevMouse;
        private bool _mouseHeld;

        public void Update(float dt)
        {
            var kb = Keyboard.GetState();
            var mouse = Mouse.GetState();
            var game = EditorGame.Instance;
            var ui = game.UI;
            var camera = game.Camera;
            var map = game.Map;

            // ── Camera pan (WASD / arrow keys) ────────────────────────
            float panSpeed = 500f / camera.Zoom;
            Vector2 pan = Vector2.Zero;
            if (kb.IsKeyDown(Keys.W) || kb.IsKeyDown(Keys.Up)) pan.Y -= panSpeed * dt;
            if (kb.IsKeyDown(Keys.S) || kb.IsKeyDown(Keys.Down)) pan.Y += panSpeed * dt;
            if (kb.IsKeyDown(Keys.A) || kb.IsKeyDown(Keys.Left)) pan.X -= panSpeed * dt;
            if (kb.IsKeyDown(Keys.D) || kb.IsKeyDown(Keys.Right)) pan.X += panSpeed * dt;
            if (pan != Vector2.Zero)
            {
                camera.Position += pan;
                camera.ClampToBounds();
            }

            // ── Zoom ──────────────────────────────────────────────────
            if (mouse.ScrollWheelValue != _prevMouse.ScrollWheelValue)
            {
                float delta = mouse.ScrollWheelValue > _prevMouse.ScrollWheelValue
                    ? 0.1f : -0.1f;
                camera.Zoom = MathHelper.Clamp(camera.Zoom + delta, 0.25f, 4f);
            }

            // ── Mouse position ────────────────────────────────────────
            bool inPanel = mouse.X < EditorUI.PANEL_W;
            ui.IsMouseInMapArea = !inPanel;

            // ── Hovered tile ──────────────────────────────────────────
            if (!inPanel)
            {
                var worldPos = camera.ScreenToWorld(new Vector2(mouse.X, mouse.Y));
                var grid = CoordUtil.WorldToIso(worldPos.ToNumerics());
                if (map.InBounds(grid))
                    ui.HoveredTile = grid;
                else
                    ui.HoveredTile = null;
            }
            else
            {
                ui.HoveredTile = null;
            }

            // ── Panel click ───────────────────────────────────────────
            bool leftClicked = mouse.LeftButton == ButtonState.Pressed
                && _prevMouse.LeftButton == ButtonState.Released;
            bool rightClicked = mouse.RightButton == ButtonState.Pressed
                && _prevMouse.RightButton == ButtonState.Released;

            if (leftClicked && inPanel)
            {
                ui.HandlePanelClick(mouse.X, mouse.Y);
            }

            // ── Map interaction ───────────────────────────────────────
            if (!inPanel && ui.HoveredTile.HasValue)
            {
                var tile = ui.HoveredTile.Value;

                if (leftClicked)
                {
                    _mouseHeld = true;
                    ApplyTool(tile);
                }

                if (_mouseHeld && mouse.LeftButton == ButtonState.Pressed)
                {
                    ApplyTool(tile);
                }

                if (rightClicked && ui.CurrentTool == EditorTool.Building)
                {
                    // Remove building at hovered tile
                    // (Building removal is handled by the game layer;
                    //  for now, mark tile as grass)
                    map.SetTile(tile.X, tile.Y, new Tile(TileType.Grass));
                    game.MarkDirty();
                }
            }

            if (mouse.LeftButton == ButtonState.Released)
            {
                _mouseHeld = false;
            }

            // ── Keyboard shortcuts ────────────────────────────────────
            // Terrain shortcuts: G=Grass, W=Water, C=Cliff, I=Impassable
            if (kb.IsKeyDown(Keys.G) && _prevKb.IsKeyUp(Keys.G))
            { ui.CurrentTool = EditorTool.Terrain; ui.SelectedTerrain = TileType.Grass; }
            if (kb.IsKeyDown(Keys.W) && _prevKb.IsKeyUp(Keys.W))
            { ui.CurrentTool = EditorTool.Terrain; ui.SelectedTerrain = TileType.Water; }
            if (kb.IsKeyDown(Keys.C) && _prevKb.IsKeyUp(Keys.C))
            { ui.CurrentTool = EditorTool.Terrain; ui.SelectedTerrain = TileType.Cliff; }
            if (kb.IsKeyDown(Keys.I) && _prevKb.IsKeyUp(Keys.I))
            { ui.CurrentTool = EditorTool.Terrain; ui.SelectedTerrain = TileType.Impassable; }

            // Save/Load: Ctrl+S, Ctrl+O
            bool ctrl = kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl);
            if (ctrl && kb.IsKeyDown(Keys.S) && _prevKb.IsKeyUp(Keys.S))
                game.SaveMap();
            if (ctrl && kb.IsKeyDown(Keys.O) && _prevKb.IsKeyUp(Keys.O))
                game.LoadMapDialog();

            _prevKb = kb;
            _prevMouse = mouse;
        }

        private void ApplyTool(IsoCoord tile)
        {
            var game = EditorGame.Instance;
            var ui = game.UI;
            var map = game.Map;

            if (ui.CurrentTool == EditorTool.Terrain)
            {
                map.SetTile(tile.X, tile.Y, new Tile(ui.SelectedTerrain));
                game.MarkDirty();
            }
            else if (ui.CurrentTool == EditorTool.Building && ui.SelectedBuildingIndex >= 0)
            {
                var config = EditorUI.LoadConfig();
                if (ui.SelectedBuildingIndex < config.Count)
                {
                    var def = config[ui.SelectedBuildingIndex];
                    // Place building: fill footprint with impassable terrain
                    for (int dx = 0; dx < def.SizeX; dx++)
                    for (int dy = 0; dy < def.SizeY; dy++)
                    {
                        var t = new IsoCoord(tile.X + dx, tile.Y + dy);
                        if (map.InBounds(t))
                            map.SetTile(t.X, t.Y, new Tile(TileType.Impassable));
                    }
                    game.MarkDirty();
                }
            }
        }
    }
}