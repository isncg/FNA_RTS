namespace FNARTS.Core
{
    /// <summary>A single map tile.</summary>
    public struct Tile
    {
        public TileType Type;
        public bool IsPassable => Type != TileType.Water
                                && Type != TileType.Impassable
                                && Type != TileType.Cliff;

        public Tile(TileType type) { Type = type; }
    }
}
