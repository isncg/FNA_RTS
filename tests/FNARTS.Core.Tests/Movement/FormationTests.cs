using Xunit;
using FNARTS.Core.Movement;

namespace FNARTS.Core.Tests.Movement
{
    public class FormationTests
    {
        [Fact]
        public void Compute_SingleUnit_ReturnsCenterTile()
        {
            var center = new IsoCoord(5, 5);
            var positions = FormationPosition.Compute(center, 1);

            Assert.Single(positions);
            Assert.Equal(center, positions[0]);
        }

        [Fact]
        public void Compute_ReturnsCorrectCount()
        {
            var positions = FormationPosition.Compute(new IsoCoord(0, 0), 5);
            Assert.Equal(5, positions.Length);
        }

        [Fact]
        public void Compute_FourUnits_Forms2x2Grid()
        {
            var center = new IsoCoord(10, 10);
            var positions = FormationPosition.Compute(center, 4);

            // 4 units → cols=2, rows=2, halfCols=0, halfRows=0
            // (0): col=0,row=0 → (10, 10)
            // (1): col=1,row=0 → (11, 10)
            // (2): col=0,row=1 → (10, 11)
            // (3): col=1,row=1 → (11, 11)
            Assert.Equal(new IsoCoord(10, 10), positions[0]);
            Assert.Equal(new IsoCoord(11, 10), positions[1]);
            Assert.Equal(new IsoCoord(10, 11), positions[2]);
            Assert.Equal(new IsoCoord(11, 11), positions[3]);
        }

        [Fact]
        public void Compute_SixUnits_Forms3x2Grid()
        {
            var center = new IsoCoord(5, 5);
            var positions = FormationPosition.Compute(center, 6);

            // 6 units → cols=3, rows=2, halfCols=1, halfRows=0
            // Row 0 (gy=5): (4,5), (5,5), (6,5)
            // Row 1 (gy=6): (4,6), (5,6), (6,6)
            Assert.Equal(6, positions.Length);
            Assert.Equal(new IsoCoord(4, 5), positions[0]);
            Assert.Equal(new IsoCoord(5, 5), positions[1]);
            Assert.Equal(new IsoCoord(6, 5), positions[2]);
            Assert.Equal(new IsoCoord(4, 6), positions[3]);
            Assert.Equal(new IsoCoord(5, 6), positions[4]);
            Assert.Equal(new IsoCoord(6, 6), positions[5]);
        }

        [Fact]
        public void Compute_AllTilesUnique()
        {
            var positions = FormationPosition.Compute(new IsoCoord(0, 0), 16);

            // All 16 tiles should be distinct
            var set = new System.Collections.Generic.HashSet<IsoCoord>();
            foreach (var p in positions)
                Assert.True(set.Add(p), $"Duplicate tile: {p}");
        }

        [Fact]
        public void Compute_ZeroCount_ReturnsEmpty()
        {
            var positions = FormationPosition.Compute(new IsoCoord(0, 0), 0);
            Assert.Empty(positions);
        }
    }
}
