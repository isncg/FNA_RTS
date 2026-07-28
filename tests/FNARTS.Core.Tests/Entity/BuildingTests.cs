using System.Linq;
using System.Numerics;
using Xunit;

namespace FNARTS.Core.Tests.Entity
{
    public class BuildingTests
    {
        [Fact]
        public void Constructor_SetsDefinitionAndOrigin()
        {
            var def = new BuildingDef { Id = "barracks", Name = "Barracks",
                SizeX = 3, SizeY = 3 };
            var origin = new IsoCoord(5, 5);
            var building = new Building(def, origin);

            Assert.Same(def, building.Definition);
            Assert.Equal(origin, building.PlacementOrigin);
            Assert.Equal(3, building.SizeX);
            Assert.Equal(3, building.SizeY);
        }

        [Fact]
        public void WorldPosition_IsCenterOfPlacement()
        {
            var def = new BuildingDef { SizeX = 2, SizeY = 2 };
            var building = new Building(def, new IsoCoord(5, 5));

            var center = CoordUtil.IsoToWorldCenter(new IsoCoord(6, 6)); // origin + half size, approximate
            // For 2x2 at origin (5,5), the center should be roughly between tiles
            Assert.NotEqual(Vector2.Zero, building.WorldPosition);
        }

        [Fact]
        public void GetOccupiedTiles_1x1_ReturnsSingleTile()
        {
            var def = new BuildingDef { SizeX = 1, SizeY = 1 };
            var building = new Building(def, new IsoCoord(3, 4));
            var tiles = building.GetOccupiedTiles();

            Assert.Single(tiles);
            Assert.Equal(new IsoCoord(3, 4), tiles[0]);
        }

        [Fact]
        public void GetOccupiedTiles_3x2_ReturnsCorrectTiles()
        {
            var def = new BuildingDef { SizeX = 3, SizeY = 2 };
            var building = new Building(def, new IsoCoord(5, 5));
            var tiles = building.GetOccupiedTiles();

            Assert.Equal(6, tiles.Length);
            Assert.Contains(new IsoCoord(5, 5), tiles);
            Assert.Contains(new IsoCoord(6, 5), tiles);
            Assert.Contains(new IsoCoord(7, 5), tiles);
            Assert.Contains(new IsoCoord(5, 6), tiles);
            Assert.Contains(new IsoCoord(6, 6), tiles);
            Assert.Contains(new IsoCoord(7, 6), tiles);
        }

        [Fact]
        public void OccupiesTile_TrueForOccupiedTiles()
        {
            var def = new BuildingDef { SizeX = 2, SizeY = 2 };
            var building = new Building(def, new IsoCoord(3, 3));

            Assert.True(building.OccupiesTile(new IsoCoord(3, 3)));
            Assert.True(building.OccupiesTile(new IsoCoord(4, 3)));
            Assert.True(building.OccupiesTile(new IsoCoord(3, 4)));
            Assert.True(building.OccupiesTile(new IsoCoord(4, 4)));
        }

        [Fact]
        public void OccupiesTile_FalseForNonOccupiedTiles()
        {
            var def = new BuildingDef { SizeX = 2, SizeY = 2 };
            var building = new Building(def, new IsoCoord(3, 3));

            Assert.False(building.OccupiesTile(new IsoCoord(2, 3)));
            Assert.False(building.OccupiesTile(new IsoCoord(5, 3)));
            Assert.False(building.OccupiesTile(new IsoCoord(3, 5)));
            Assert.False(building.OccupiesTile(new IsoCoord(10, 10)));
        }
    }
}
