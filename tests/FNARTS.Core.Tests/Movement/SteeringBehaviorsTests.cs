using System.Numerics;
using Xunit;
using FNARTS.Core.Movement;

namespace FNARTS.Core.Tests.Movement
{
    public class SteeringBehaviorsTests
    {
        [Fact]
        public void Seek_ReturnsNormalizedDirectionTimesMaxSpeed()
        {
            var pos = Vector2.Zero;
            var target = new Vector2(100, 0);
            var result = SteeringBehaviors.Seek(pos, target, Vector2.Zero, 100f);

            Assert.Equal(100f, result.Length(), 3);
            Assert.Equal(100f, result.X, 3);
            Assert.Equal(0f, result.Y, 3);
        }

        [Fact]
        public void Seek_AtTarget_ReturnsZero()
        {
            var pos = new Vector2(10, 10);
            var result = SteeringBehaviors.Seek(pos, pos, Vector2.Zero, 100f);
            Assert.Equal(Vector2.Zero, result);
        }

        [Fact]
        public void Arrive_Far_ReturnsMaxSpeed()
        {
            var pos = Vector2.Zero;
            var target = new Vector2(200, 0); // far beyond braking distance
            var result = SteeringBehaviors.Arrive(pos, target, Vector2.Zero,
                100f, 300f, 48f);
            // sqrt(2 * 300 * 200) = 346 → capped at maxSpeed
            Assert.Equal(100f, result.Length(), 3);
        }

        [Fact]
        public void Arrive_Close_Decelerates()
        {
            var pos = Vector2.Zero;
            // At 8 px with decel 300: sqrt(2*300*8) = sqrt(4800) ≈ 69.3
            var target = new Vector2(8, 0);
            var result = SteeringBehaviors.Arrive(pos, target, Vector2.Zero,
                100f, 300f, 48f);
            Assert.Equal(69.28f, result.Length(), 1);
            Assert.True(result.Length() < 100f, "Should decelerate when close");
        }

        [Fact]
        public void Arrive_AtTarget_ReturnsZero()
        {
            var pos = new Vector2(10, 10);
            var result = SteeringBehaviors.Arrive(pos, pos, Vector2.Zero,
                100f, 300f, 48f);
            Assert.Equal(Vector2.Zero, result);
        }

        [Fact]
        public void ClampForce_WithinLimit_Unchanged()
        {
            var force = new Vector2(10, 20);
            var result = SteeringBehaviors.ClampForce(force, 100f);
            Assert.Equal(force, result);
        }

        [Fact]
        public void ClampForce_ExceedsLimit_Clamped()
        {
            var force = new Vector2(300, 400); // length = 500
            var result = SteeringBehaviors.ClampForce(force, 400f);
            Assert.Equal(400f, result.Length(), 3);
            // Direction preserved
            Assert.Equal(0.6f, result.X / result.Length(), 3);
            Assert.Equal(0.8f, result.Y / result.Length(), 3);
        }

        [Fact]
        public void ClampMagnitude_ExceedsLimit_Clamped()
        {
            var v = new Vector2(30, 40); // length = 50
            var result = SteeringBehaviors.ClampMagnitude(v, 40f);
            Assert.Equal(40f, result.Length(), 3);
        }

        [Fact]
        public void Cohesion_NoNeighbors_ReturnsZero()
        {
            var result = SteeringBehaviors.Cohesion(Vector2.Zero,
                System.Array.Empty<Vector2>(), Vector2.Zero, 100f, 600f);
            Assert.Equal(Vector2.Zero, result);
        }

        [Fact]
        public void Alignment_NoNeighbors_ReturnsZero()
        {
            var result = SteeringBehaviors.Alignment(Vector2.Zero,
                System.Array.Empty<Vector2>(), 100f, 600f);
            Assert.Equal(Vector2.Zero, result);
        }
    }
}
