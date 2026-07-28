using System;

namespace FNARTS.Core
{
    /// <summary>
    /// 2D grid-based tile map. Stores tile data for the game world.
    /// </summary>
    public class TileMap
    {
        public int Width { get; }
        public int Height { get; }
        private readonly Tile[,] _tiles;
        private readonly Tile _defaultTile = new(TileType.Grass);

        public TileMap(int width, int height)
        {
            Width = width;
            Height = height;
            _tiles = new Tile[width, height];
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                _tiles[x, y] = _defaultTile;
        }

        public Tile GetTile(IsoCoord coord) => GetTile(coord.X, coord.Y);

        public Tile GetTile(int x, int y)
        {
            if (!InBounds(x, y)) return _defaultTile;
            return _tiles[x, y];
        }

        public void SetTile(IsoCoord coord, Tile tile) => SetTile(coord.X, coord.Y, tile);

        public void SetTile(int x, int y, Tile tile)
        {
            if (!InBounds(x, y)) return;
            _tiles[x, y] = tile;
        }

        public bool IsPassable(IsoCoord coord)
        {
            if (!InBounds(coord)) return false;
            return _tiles[coord.X, coord.Y].IsPassable;
        }

        public bool InBounds(IsoCoord coord) => InBounds(coord.X, coord.Y);

        public bool InBounds(int x, int y)
            => x >= 0 && x < Width && y >= 0 && y < Height;

        /// <summary>Fill entire map with a single tile type.</summary>
        public void Fill(TileType type)
        {
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                _tiles[x, y] = new Tile(type);
        }
    }
}
