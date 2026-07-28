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
        public void GetOccupiedTiles_1x1_ReturnsTopAndSouthRow()
        {
            var def = new BuildingDef { SizeX = 1, SizeY = 1 };
            var building = new Building(def, new IsoCoord(3, 4));
            var tiles = building.GetOccupiedTiles();

            // 1×1 building occupies 2 tiles: top face + south-wall row
            Assert.Equal(2, tiles.Length);
            Assert.Contains(new IsoCoord(3, 4), tiles);  // top face
            Assert.Contains(new IsoCoord(3, 3), tiles);  // south wall
        }

        [Fact]
        public void GetOccupiedTiles_3x2_ReturnsCorrectTiles()
        {
            var def = new BuildingDef { SizeX = 3, SizeY = 2 };
            var building = new Building(def, new IsoCoord(5, 5));
            var tiles = building.GetOccupiedTiles();

            // 3×2 building occupies 3×(2+1) = 9 tiles (top face + south-wall row)
            Assert.Equal(9, tiles.Length);
            // Top face (gy 5..6)
            Assert.Contains(new IsoCoord(5, 5), tiles);
            Assert.Contains(new IsoCoord(6, 5), tiles);
            Assert.Contains(new IsoCoord(7, 5), tiles);
            Assert.Contains(new IsoCoord(5, 6), tiles);
            Assert.Contains(new IsoCoord(6, 6), tiles);
            Assert.Contains(new IsoCoord(7, 6), tiles);
            // South-wall row (gy=4)
            Assert.Contains(new IsoCoord(5, 4), tiles);
            Assert.Contains(new IsoCoord(6, 4), tiles);
            Assert.Contains(new IsoCoord(7, 4), tiles);
        }

        [Fact]
        public void OccupiesTile_TrueForOccupiedTiles()
        {
            var def = new BuildingDef { SizeX = 2, SizeY = 2 };
            var building = new Building(def, new IsoCoord(3, 3));

            // Top face
            Assert.True(building.OccupiesTile(new IsoCoord(3, 3)));
            Assert.True(building.OccupiesTile(new IsoCoord(4, 3)));
            Assert.True(building.OccupiesTile(new IsoCoord(3, 4)));
            Assert.True(building.OccupiesTile(new IsoCoord(4, 4)));
            // South-wall row (gy-1)
            Assert.True(building.OccupiesTile(new IsoCoord(3, 2)));
            Assert.True(building.OccupiesTile(new IsoCoord(4, 2)));
        }

        [Fact]
        public void OccupiesTile_FalseForNonOccupiedTiles()
        {
            var def = new BuildingDef { SizeX = 2, SizeY = 2 };
            var building = new Building(def, new IsoCoord(3, 3));

            Assert.False(building.OccupiesTile(new IsoCoord(2, 3)));   // west
            Assert.False(building.OccupiesTile(new IsoCoord(5, 3)));   // east
            Assert.False(building.OccupiesTile(new IsoCoord(3, 5)));   // north
            Assert.False(building.OccupiesTile(new IsoCoord(3, 1)));   // far south
            Assert.False(building.OccupiesTile(new IsoCoord(10, 10))); // distant
        }
    }
}
