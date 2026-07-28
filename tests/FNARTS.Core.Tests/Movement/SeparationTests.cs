using System.Numerics;
using Xunit;
using FNARTS.Core.Movement;

namespace FNARTS.Core.Tests.Movement
{
    public class SeparationTests
    {
        [Fact]
        public void Compute_NoNearbyUnits_ReturnsZero()
        {
            var self = Vector2.Zero;
            var others = new[] { new Vector2(100, 0), new Vector2(0, 100) };

            var result = SeparationBehavior.Compute(self, others);
            Assert.Equal(Vector2.Zero, result);
        }

        [Fact]
        public void Compute_SingleNearbyUnit_RepelsAway()
        {
            var self = new Vector2(10, 0);
            var others = new[] { Vector2.Zero }; // 10px away → within radius

            var result = SeparationBehavior.Compute(self, others);

            // Repulsion should be away from the other unit (positive X)
            Assert.True(result.X > 0, "Should repel away from the other unit");
            Assert.Equal(0f, result.Y, 3);
        }

        [Fact]
        public void Compute_TwoNearby_RepelsAwayFromBoth()
        {
            var self = Vector2.Zero;
            var others = new[] {
                new Vector2(10, 0),   // to the right
                new Vector2(0, 10),   // above
            };

            var result = SeparationBehavior.Compute(self, others);

            // Should be pushed left (away from +X) and down (away from +Y)
            Assert.True(result.X < 0, "Should repel left");
            Assert.True(result.Y < 0, "Should repel down");
        }

        [Fact]
        public void Compute_ExactlyAtRadius_NoEffect()
        {
            var self = Vector2.Zero;
            var others = new[] { new Vector2(SeparationBehavior.SEPARATION_RADIUS, 0) };

            var result = SeparationBehavior.Compute(self, others);
            Assert.Equal(Vector2.Zero, result);
        }

        [Fact]
        public void Compute_JustInsideRadius_HasEffect()
        {
            var self = Vector2.Zero;
            var others = new[] { new Vector2(SeparationBehavior.SEPARATION_RADIUS - 1f, 0) };

            var result = SeparationBehavior.Compute(self, others);

            // Should be non-zero (repelled away)
            Assert.NotEqual(Vector2.Zero, result);
            Assert.True(result.X < 0, "Should repel left (away from positive X)");
        }

        [Fact]
        public void Compute_SelfPosition_IsIgnored()
        {
            var self = new Vector2(5, 5);
            // Include self in the list — should be ignored (dist ≈ 0)
            var others = new[] { self, new Vector2(15, 5) };

            var result = SeparationBehavior.Compute(self, others);
            // Only (15,5) is within radius — repulsion away from it
            Assert.True(result.X < 0, "Should repel left (away from 15)");
        }

        [Fact]
        public void Compute_CloserUnit_StrongerRepulsion()
        {
            var self = Vector2.Zero;
            var far = new[] { new Vector2(20, 0) };
            var near = new[] { new Vector2(5, 0) };

            var farResult = SeparationBehavior.Compute(self, far);
            var nearResult = SeparationBehavior.Compute(self, near);

            // Near unit should produce stronger repulsion (inversely proportional)
            Assert.True(nearResult.Length() > farResult.Length(),
                "Closer unit should repel more strongly");
        }

        [Fact]
        public void Compute_EmptyList_ReturnsZero()
        {
            var result = SeparationBehavior.Compute(Vector2.Zero, System.Array.Empty<Vector2>());
            Assert.Equal(Vector2.Zero, result);
        }
    }
}
