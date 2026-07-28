using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNARTS.Core;

namespace FNARTS.Game
{
    /// <summary>
    /// Abstraction over game assets. Rendering code depends on this interface,
    /// not on concrete texture sources. Swap Procedural/File/Hybrid implementations.
    /// </summary>
    public interface IAssetProvider : IDisposable
    {
        Texture2D TilesetTexture { get; }
        Rectangle GetTileSourceRect(TileType type);
        Texture2D GetUnitTexture(string unitDefId);
        Texture2D GetBuildingTexture(BuildingDef buildingDef);
        Texture2D SelectionHighlight { get; }
        Texture2D DiamondHighlight { get; }   // 64×32 semi-transparent diamond for tile overlays
        Texture2D WhitePixel { get; }
    }
}
