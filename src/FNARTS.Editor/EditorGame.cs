using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNARTS.Core;
using FNARTS.Game;

namespace FNARTS.Editor
{
    /// <summary>
    /// Standalone FNA terrain editor for RTS maps.
    /// Supports terrain painting, building placement, and save/load.
    /// </summary>
    public class EditorGame : Microsoft.Xna.Framework.Game
    {
        private readonly GraphicsDeviceManager _gdm;
        private SpriteBatch _sb;
        private BitmapFont _font;
        private Camera2D _camera;
        private TileMap _map;
        private TileRenderer _tileRenderer;
        private IAssetProvider _assets;
        private EditorUI _editorUI;
        private EditorInput _editorInput;

        private const int UI_WIDTH = 160;
        private const int MAP_W = 51;
        private const int MAP_H = 51;

        private string _currentFilePath;
        private bool _dirty;

        public EditorGame()
        {
            Instance = this;
            _gdm = new GraphicsDeviceManager(this);
            _gdm.PreferredBackBufferWidth = 1280;
            _gdm.PreferredBackBufferHeight = 720;
            _gdm.SynchronizeWithVerticalRetrace = false;
            IsFixedTimeStep = true;
            TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
            IsMouseVisible = true;
            Window.Title = "FNA RTS — Terrain Editor";
        }

        public static EditorGame Instance { get; private set; }
        public TileMap Map => _map;
        public IAssetProvider Assets => _assets;
        public EditorUI UI => _editorUI;
        public EditorInput EditorInput => _editorInput;
        public Camera2D Camera => _camera;
        public bool IsDirty => _dirty;
        public void MarkDirty() { _dirty = true; Window.Title = "FNA RTS — Terrain Editor *"; }
        public void ClearDirty() { _dirty = false; Window.Title = "FNA RTS — Terrain Editor"; }

        protected override void Initialize()
        {
            // Camera covers the full viewport (UI is drawn on top)
            _camera = new Camera2D(
                _gdm.PreferredBackBufferWidth - UI_WIDTH,
                _gdm.PreferredBackBufferHeight);

            // Try to load map from command line, otherwise create a fresh one
            _currentFilePath = Program.MapPath;
            if (!string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
            {
                LoadMap(_currentFilePath);
            }
            else
            {
                CreateFreshMap();
            }

            _editorUI = new EditorUI();
            _editorInput = new EditorInput();

            base.Initialize();
        }

        private void CreateFreshMap()
        {
            _map = new TileMap(MAP_W, MAP_H);
            // Default: grass everywhere
            for (int x = 0; x < MAP_W; x++)
            for (int y = 0; y < MAP_H; y++)
                _map.SetTile(x, y, new Tile(TileType.Grass));
            _currentFilePath = null;
            ClearDirty();
        }

        private void LoadMap(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                var mapData = System.Text.Json.JsonSerializer.Deserialize<MapData>(json);
                if (mapData != null)
                {
                    _map = mapData.ToTileMap();
                    _currentFilePath = path;
                    ClearDirty();
                    GameLogger.Info($"Loaded map: {path} ({_map.Width}x{_map.Height})");
                    return;
                }
            }
            catch (Exception ex)
            {
                GameLogger.Warn($"Failed to load map: {ex.Message}");
            }
            CreateFreshMap();
        }

        public void SaveMap()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                // Generate a default filename
                _currentFilePath = Path.Combine(
                    AppContext.BaseDirectory, "data", "maps", "editor_map.json");
            }
            SaveMapAs(_currentFilePath);
        }

        public void SaveMapAs(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var mapData = new MapData
                {
                    Width = _map.Width,
                    Height = _map.Height,
                    Name = Path.GetFileNameWithoutExtension(path),
                    DefaultTile = "Grass",
                };

                for (int x = 0; x < _map.Width; x++)
                for (int y = 0; y < _map.Height; y++)
                {
                    var tile = _map.GetTile(x, y);
                    if (tile.Type != TileType.Grass) // skip default
                    {
                        mapData.Tiles.Add(new MapData.TileEntry
                        {
                            X = x, Y = y,
                            Type = tile.Type.ToString()
                        });
                    }
                }

                var json = System.Text.Json.JsonSerializer.Serialize(mapData,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                _currentFilePath = path;
                ClearDirty();
                GameLogger.Info($"Saved map: {path} ({mapData.Tiles.Count} non-default tiles)");
            }
            catch (Exception ex)
            {
                GameLogger.Warn($"Failed to save map: {ex.Message}");
            }
        }

        public void LoadMapDialog()
        {
            // Simple prompt: use a hardcoded path or ask user via command line
            // For now, cycle through a few known paths
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory, "data", "maps", "editor_map.json"),
                _currentFilePath,
            };
            foreach (var c in candidates)
            {
                if (!string.IsNullOrEmpty(c) && File.Exists(c) && c != _currentFilePath)
                {
                    LoadMap(c);
                    return;
                }
            }
            // If no other file, reload current
            if (!string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
                LoadMap(_currentFilePath);
        }

        protected override void LoadContent()
        {
            _sb = new SpriteBatch(GraphicsDevice);
            _font = new BitmapFont(GraphicsDevice);

            // Use FileAssetProvider with fallback to procedural
            string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
            _assets = new FileAssetProvider(GraphicsDevice, dataDir);

            _tileRenderer = new TileRenderer(_map, _assets);
            _editorUI.LoadContent(_font);

            // Centre camera on map
            float cx = _map.Width / 2f;
            float cy = _map.Height / 2f;
            _camera.Position = new Vector2(
                (cx - cy) * CoordUtil.HALF_TILE_W,
                -(cx + cy) * CoordUtil.HALF_TILE_H);
            _camera.RebuildMatrices();

            GameLogger.Info("Editor content loaded");
        }

        protected override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _editorInput.Update(dt);
            _camera.RebuildMatrices();
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(30, 30, 40));

            // ── Map area ──────────────────────────────────────────────
            _tileRenderer.Draw(_sb, _camera);
            _editorUI.DrawMapOverlay(_sb, _camera, _tileRenderer);

            // ── UI overlay (left panel, no camera transform) ──────────
            _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
            _editorUI.DrawPanel(_sb);
            _sb.End();

            base.Draw(gameTime);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _assets?.Dispose();
                _tileRenderer?.Dispose();
                _font?.Dispose();
                _sb?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}