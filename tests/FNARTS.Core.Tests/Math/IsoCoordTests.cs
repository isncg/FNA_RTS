using System.Numerics;
using Xunit;

namespace FNARTS.Core.Tests.Math
{
    public class IsoCoordTests
    {
        [Fact]
        public void Default_IsZero()
        {
            var c = default(IsoCoord);
            Assert.Equal(0, c.X);
            Assert.Equal(0, c.Y);
            Assert.Equal(IsoCoord.Zero, c);
        }

        [Fact]
        public void Constructor_SetsValues()
        {
            var c = new IsoCoord(3, 7);
            Assert.Equal(3, c.X);
            Assert.Equal(7, c.Y);
        }

        [Fact]
        public void Equality_SameCoords_AreEqual()
        {
            var a = new IsoCoord(5, 10);
            var b = new IsoCoord(5, 10);
            Assert.True(a == b);
            Assert.True(a.Equals(b));
            Assert.False(a != b);
        }

        [Fact]
        public void Equality_DifferentCoords_AreNotEqual()
        {
            var a = new IsoCoord(5, 10);
            var b = new IsoCoord(5, 11);
            Assert.False(a == b);
            Assert.True(a != b);
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_Object_Works()
        {
            var a = new IsoCoord(1, 2);
            object b = new IsoCoord(1, 2);
            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Equals_Null_ReturnsFalse()
        {
            Assert.False(new IsoCoord(1, 2).Equals(null));
        }

        [Fact]
        public void Equals_DifferentType_ReturnsFalse()
        {
            Assert.False(new IsoCoord(1, 2).Equals("not an IsoCoord"));
        }

        [Fact]
        public void GetHashCode_SameCoords_SameHash()
        {
            var a = new IsoCoord(5, 10);
            var b = new IsoCoord(5, 10);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Addition_Operator()
        {
            var a = new IsoCoord(3, 4);
            var b = new IsoCoord(1, 2);
            var result = a + b;
            Assert.Equal(new IsoCoord(4, 6), result);
        }

        [Fact]
        public void Subtraction_Operator()
        {
            var a = new IsoCoord(5, 8);
            var b = new IsoCoord(2, 3);
            var result = a - b;
            Assert.Equal(new IsoCoord(3, 5), result);
        }

        [Fact]
        public void Distance_SamePoint_ReturnsZero()
        {
            var a = new IsoCoord(3, 4);
            Assert.Equal(0f, IsoCoord.Distance(a, a));
        }

        [Fact]
        public void Distance_ComputesCorrectly()
        {
            var a = new IsoCoord(0, 0);
            var b = new IsoCoord(3, 4);
            float dist = IsoCoord.Distance(a, b);
            Assert.Equal(5f, dist, 3); // 3-4-5 triangle, float tolerance
        }

        [Fact]
        public void ToString_Formatting()
        {
            var c = new IsoCoord(3, 7);
            Assert.Equal("(3, 7)", c.ToString());
        }
    }
}
