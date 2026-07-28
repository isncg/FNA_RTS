using Xunit;

namespace FNARTS.Core.Tests.Map
{
    public class TileMapTests
    {
        [Fact]
        public void Constructor_CreatesGrid_AllGrass()
        {
            var map = new TileMap(10, 8);
            Assert.Equal(10, map.Width);
            Assert.Equal(8, map.Height);
            for (int x = 0; x < 10; x++)
            for (int y = 0; y < 8; y++)
                Assert.Equal(TileType.Grass, map.GetTile(x, y).Type);
        }

        [Fact]
        public void GetTile_OutOfBounds_ReturnsDefault()
        {
            var map = new TileMap(5, 5);
            var tile = map.GetTile(10, 10);
            Assert.Equal(TileType.Grass, tile.Type);
        }

        [Fact]
        public void SetTile_ThenGet_ReturnsSetValue()
        {
            var map = new TileMap(10, 10);
            map.SetTile(3, 5, new Tile(TileType.Water));
            Assert.Equal(TileType.Water, map.GetTile(3, 5).Type);
        }

        [Fact]
        public void SetTile_OutOfBounds_DoesNotThrow()
        {
            var map = new TileMap(5, 5);
            map.SetTile(10, 10, new Tile(TileType.Water));
            // Should not throw; just silently ignored
        }

        [Fact]
        public void SetTile_ByCoord_Works()
        {
            var map = new TileMap(10, 10);
            map.SetTile(new IsoCoord(3, 5), new Tile(TileType.Cliff));
            Assert.Equal(TileType.Cliff, map.GetTile(new IsoCoord(3, 5)).Type);
        }

        [Fact]
        public void IsPassable_Grass_ReturnsTrue()
        {
            var map = new TileMap(5, 5);
            Assert.True(map.IsPassable(new IsoCoord(2, 2)));
        }

        [Fact]
        public void IsPassable_Water_ReturnsFalse()
        {
            var map = new TileMap(5, 5);
            map.SetTile(2, 2, new Tile(TileType.Water));
            Assert.False(map.IsPassable(new IsoCoord(2, 2)));
        }

        [Fact]
        public void IsPassable_Impassable_ReturnsFalse()
        {
            var map = new TileMap(5, 5);
            map.SetTile(2, 2, new Tile(TileType.Impassable));
            Assert.False(map.IsPassable(new IsoCoord(2, 2)));
        }

        [Fact]
        public void IsPassable_Cliff_ReturnsFalse()
        {
            // Cliff is now impassable (used as map boundary terrain).
            var map = new TileMap(5, 5);
            map.SetTile(2, 2, new Tile(TileType.Cliff));
            Assert.False(map.IsPassable(new IsoCoord(2, 2)));
        }

        [Fact]
        public void IsPassable_OutOfBounds_ReturnsFalse()
        {
            var map = new TileMap(5, 5);
            Assert.False(map.IsPassable(new IsoCoord(10, 10)));
        }

        [Fact]
        public void InBounds_Inside_ReturnsTrue()
        {
            var map = new TileMap(10, 10);
            Assert.True(map.InBounds(0, 0));
            Assert.True(map.InBounds(9, 9));
            Assert.True(map.InBounds(5, 5));
        }

        [Fact]
        public void InBounds_Outside_ReturnsFalse()
        {
            var map = new TileMap(10, 10);
            Assert.False(map.InBounds(-1, 5));
            Assert.False(map.InBounds(5, -1));
            Assert.False(map.InBounds(10, 5));
            Assert.False(map.InBounds(5, 10));
        }

        [Fact]
        public void Fill_ChangesAllTiles()
        {
            var map = new TileMap(3, 3);
            map.Fill(TileType.Water);
            for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
                Assert.Equal(TileType.Water, map.GetTile(x, y).Type);
        }
    }
}
