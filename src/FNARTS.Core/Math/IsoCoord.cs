using System;
using System.Numerics;

namespace FNARTS.Core
{
    /// <summary>
    /// 2D isometric grid coordinate. X increases right-down, Y increases right-up.
    /// </summary>
    public struct IsoCoord : IEquatable<IsoCoord>
    {
        public int X;
        public int Y;

        public IsoCoord(int x, int y) { X = x; Y = y; }

        public static IsoCoord Zero => default;

        public static float Distance(IsoCoord a, IsoCoord b)
        {
            int dx = a.X - b.X;
            int dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        public static IsoCoord operator +(IsoCoord a, IsoCoord b)
            => new(a.X + b.X, a.Y + b.Y);

        public static IsoCoord operator -(IsoCoord a, IsoCoord b)
            => new(a.X - b.X, a.Y - b.Y);

        public static bool operator ==(IsoCoord a, IsoCoord b)
            => a.X == b.X && a.Y == b.Y;

        public static bool operator !=(IsoCoord a, IsoCoord b)
            => !(a == b);

        public bool Equals(IsoCoord other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is IsoCoord other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);

        public override string ToString() => $"({X}, {Y})";
    }
}
