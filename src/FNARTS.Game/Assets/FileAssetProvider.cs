using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNARTS.Core;

namespace FNARTS.Game
{
    /// <summary>
    /// Loads building and unit textures from PNG files on disk.
    /// Implements IAssetProvider so the rendering code needs no changes.
    ///
    /// Expected directory layout:
    ///   <baseDir>/buildings/<TextureId>.png     (isometric sprites)
    ///   <baseDir>/units/<unitDefId>.png         (unit sprites, optional)
    ///
    /// Falls back to ProceduralAssetProvider for any asset not found on disk.
    /// File-loaded textures are cached; fallback textures are NOT cached so
    /// that dropping a PNG into the directory takes effect on next restart.
    /// </summary>
    public class FileAssetProvider : IAssetProvider
    {
        private readonly GraphicsDevice _device;
        private readonly ProceduralAssetProvider _fallback;
        private readonly string _baseDir;

        // Only textures loaded from disk are stored here (for disposal).
        // Fallback textures are transparently returned from the procedural
        // provider and NOT cached so disk files can override mid-development.
        private readonly Dictionary<string, Texture2D> _fileTextures = new();

        public FileAssetProvider(GraphicsDevice device, string baseDir)
        {
            _device = device;
            _fallback = new ProceduralAssetProvider(device);
            _baseDir = baseDir ?? Path.Combine("data", "textures");

            if (!Directory.Exists(_baseDir))
                Directory.CreateDirectory(_baseDir);
        }

        /// <summary>
        /// Try to load a building texture from disk, falling back to procedural.
        /// Looks up: <baseDir>/buildings/<textureId>.png
        /// </summary>
        public Texture2D GetBuildingTexture(BuildingDef buildingDef)
        {
            string texId = !string.IsNullOrEmpty(buildingDef.TextureId)
                ? buildingDef.TextureId
                : $"gen_{buildingDef.SizeX}_{buildingDef.SizeY}_{buildingDef.Height}";

            // Check file cache first
            if (_fileTextures.TryGetValue(texId, out var cached))
                return cached;

            // Try disk
            string path = Path.Combine(_baseDir, "buildings", $"{texId}.png");
            if (File.Exists(path))
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    var tex = Texture2D.FromStream(_device, stream);
                    _fileTextures[texId] = tex;
                    GameLogger.Info($"Loaded building texture: {path} ({tex.Width}x{tex.Height})");
                    return tex;
                }
                catch (Exception ex)
                {
                    GameLogger.Warn($"Failed to load {path}: {ex.Message}");
                }
            }

            // Fallback — not cached here (ProceduralAssetProvider has its own cache)
            return _fallback.GetBuildingTexture(buildingDef);
        }

        /// <summary>
        /// Try to load a unit texture from disk.
        /// Looks up: <baseDir>/units/<unitDefId>.png
        /// </summary>
        public Texture2D GetUnitTexture(string unitDefId)
        {
            if (_fileTextures.TryGetValue(unitDefId, out var cached))
                return cached;

            string path = Path.Combine(_baseDir, "units", $"{unitDefId}.png");
            if (File.Exists(path))
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    var tex = Texture2D.FromStream(_device, stream);
                    _fileTextures[unitDefId] = tex;
                    GameLogger.Info($"Loaded unit texture: {path} ({tex.Width}x{tex.Height})");
                    return tex;
                }
                catch (Exception ex)
                {
                    GameLogger.Warn($"Failed to load {path}: {ex.Message}");
                }
            }

            return _fallback.GetUnitTexture(unitDefId);
        }

        // ── Delegated to ProceduralAssetProvider ──────────────────────

        public Texture2D TilesetTexture => _fallback.TilesetTexture;

        public Rectangle GetTileSourceRect(TileType type) => _fallback.GetTileSourceRect(type);

        public Texture2D SelectionHighlight => _fallback.SelectionHighlight;

        public Texture2D DiamondHighlight => _fallback.DiamondHighlight;

        public Texture2D WhitePixel => _fallback.WhitePixel;

        // ── IDisposable ────────────────────────────────────────────────

        public void Dispose()
        {
            foreach (var t in _fileTextures.Values)
                t?.Dispose();
            _fileTextures.Clear();
            _fallback?.Dispose();
        }
    }
}
