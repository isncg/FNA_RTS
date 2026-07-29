using System.Numerics;
using Xunit;

namespace FNARTS.Core.Tests.Math
{
    public class CoordUtilTests
    {
        [Fact]
        public void Constants_AreCorrect()
        {
            Assert.Equal(64, CoordUtil.TILE_WIDTH);
            Assert.Equal(32, CoordUtil.TILE_HEIGHT);
            Assert.Equal(32f, CoordUtil.HALF_TILE_W);
            Assert.Equal(16f, CoordUtil.HALF_TILE_H);
        }

        [Fact]
        public void IsoToWorld_Origin_ReturnsZero()
        {
            var result = CoordUtil.IsoToWorld(new IsoCoord(0, 0));
            Assert.Equal(Vector2.Zero, result);
        }

        [Fact]
        public void IsoToWorld_XAxis_MovesRightAndUp()
        {
            // C&C2: gx+1 → upper-right (East): (32, -16)
            var result = CoordUtil.IsoToWorld(new IsoCoord(1, 0));
            Assert.Equal(32f, result.X, 3);
            Assert.Equal(-16f, result.Y, 3);
        }

        [Fact]
        public void IsoToWorld_YAxis_MovesLeftAndUp()
        {
            // C&C2: gy+1 → upper-left (North): (-32, -16)
            var result = CoordUtil.IsoToWorld(new IsoCoord(0, 1));
            Assert.Equal(-32f, result.X, 3);
            Assert.Equal(-16f, result.Y, 3);
        }

        [Fact]
        public void IsoToWorldCenter_OffsetsFromSouthVertex()
        {
            var south = CoordUtil.IsoToWorld(new IsoCoord(5, 3));
            var center = CoordUtil.IsoToWorldCenter(new IsoCoord(5, 3));
            // Grid centre: same X as the south vertex, Y offset by -HALF_TILE_H (up)
            Assert.Equal(south.X, center.X, 3);
            Assert.Equal(south.Y - CoordUtil.HALF_TILE_H, center.Y, 3);
        }

        [Fact]
        public void WorldToIso_RoundTrip_Exact()
        {
            var center = CoordUtil.IsoToWorldCenter(new IsoCoord(5, 3));
            var back = CoordUtil.WorldToIso(center);
            Assert.Equal(new IsoCoord(5, 3), back);
        }

        [Fact]
        public void WorldToIso_RoundTrip_ManyPoints()
        {
            for (int x = 0; x < 10; x++)
            for (int y = 0; y < 10; y++)
            {
                var original = new IsoCoord(x, y);
                var center = CoordUtil.IsoToWorldCenter(original);
                var back = CoordUtil.WorldToIso(center);
                Assert.Equal(original, back);
            }
        }

        [Fact]
        public void WorldToIso_Origin_ReturnsZero()
        {
            var result = CoordUtil.WorldToIso(Vector2.Zero);
            Assert.Equal(new IsoCoord(0, 0), result);
        }

        [Fact]
        public void WorldToIso_NearEdge_FloorsCorrectly()
        {
            // Point near the boundary between (0,0) and (1,0) in continuous grid space.
            // Grid center of (0,0) is at (0.5, 0.5); grid center of (1,0) is at (1.5, 0.5).
            // A point at grid (0.99, 0.5) should floor to (0, 0).
            // World position: wx = (0.99-0.5)*32, wy = -(0.99+0.5)*16
            float wx = (0.99f - 0.5f) * CoordUtil.HALF_TILE_W;
            float wy = -(0.99f + 0.5f) * CoordUtil.HALF_TILE_H;
            var pos = new Vector2(wx, wy);
            var result = CoordUtil.WorldToIso(pos);
            Assert.Equal(new IsoCoord(0, 0), result);
        }

        [Fact]
        public void WorldToIsoFloat_ReturnsContinuousCoords()
        {
            var center = CoordUtil.IsoToWorldCenter(new IsoCoord(5, 3));
            var fc = CoordUtil.WorldToIsoFloat(center);
            // Grid center is at (gx+0.5, gy+0.5) in continuous space
            Assert.Equal(5.5f, fc.X, 3);
            Assert.Equal(3.5f, fc.Y, 3);
        }

        [Fact]
        public void TileDrawOrigin_SpriteCentre_IsTileCentre()
        {
            // The 64×32 diamond sprite drawn at TileDrawOrigin has its centre
            // at + (HALF_W, HALF_H); that must coincide with the logical tile
            // centre so rendering and the world↔grid projection align.
            for (int x = 0; x < 5; x++)
            for (int y = 0; y < 5; y++)
            {
                var c = new IsoCoord(x, y);
                var drawn = CoordUtil.TileDrawOrigin(c)
                    + new Vector2(CoordUtil.HALF_TILE_W, CoordUtil.HALF_TILE_H);
                Assert.Equal(CoordUtil.IsoToWorldCenter(c), drawn);
                Assert.Equal(c, CoordUtil.WorldToIso(drawn));
            }
        }

        [Fact]
        public void BuildingWorldOrigin_IsFootprintCentre()
        {
            // 2×2 at (5,5): footprint centre is continuous grid (6,6).
            var origin = CoordUtil.BuildingWorldOrigin(new IsoCoord(5, 5), 2, 2);
            Assert.Equal(0f, origin.X, 3);
            Assert.Equal(-12f * CoordUtil.HALF_TILE_H, origin.Y, 3);
        }

        [Fact]
        public void ComputeDepth_Origin_Near()
        {
            // C&C2 flipped: depth = (gx+gy) / maxSum
            // (0,0): 0/40 = 0.0 (front/near — drawn last)
            float depth = CoordUtil.ComputeDepth(new IsoCoord(0, 0), 20, 20);
            Assert.Equal(0.0f, depth, 3);
        }

        [Fact]
        public void ComputeDepth_FarCorner_Far()
        {
            // (19,19) on 20x20: 38/40 = 0.95 (back/far — drawn first)
            float depth = CoordUtil.ComputeDepth(new IsoCoord(19, 19), 20, 20);
            Assert.Equal(0.95f, depth, 3);
        }

        [Fact]
        public void ComputeDepth_Monotonic()
        {
            // Farther tiles (higher X+Y) get higher depth values (drawn earlier)
            float d1 = CoordUtil.ComputeDepth(new IsoCoord(0, 0), 20, 20);
            float d2 = CoordUtil.ComputeDepth(new IsoCoord(1, 1), 20, 20);
            float d3 = CoordUtil.ComputeDepth(new IsoCoord(10, 10), 20, 20);
            Assert.True(d1 < d2);
            Assert.True(d2 < d3);
        }
    }
}
