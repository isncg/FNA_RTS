using System.Collections.Generic;

namespace FNARTS.Core
{
    /// <summary>JSON-serializable map definition.</summary>
    public class MapData
    {
        public string Name { get; set; } = "Untitled";
        public int Width { get; set; } = 20;
        public int Height { get; set; } = 20;
        public string DefaultTile { get; set; } = "Grass";
        public List<TileEntry> Tiles { get; set; } = new();
        public List<StartPosition> StartPositions { get; set; } = new();

        public class TileEntry
        {
            public int X { get; set; }
            public int Y { get; set; }
            public string Type { get; set; } = "Grass";
        }

        public class StartPosition
        {
            public int Faction { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
        }

        /// <summary>Create a TileMap from this data definition.</summary>
        public TileMap ToTileMap()
        {
            var map = new TileMap(Width, Height);
            foreach (var entry in Tiles)
            {
                var type = entry.Type switch
                {
                    "Water" => TileType.Water,
                    "Cliff" => TileType.Cliff,
                    "Impassable" => TileType.Impassable,
                    _ => TileType.Grass
                };
                map.SetTile(entry.X, entry.Y, new Tile(type));
            }
            return map;
        }
    }
}
