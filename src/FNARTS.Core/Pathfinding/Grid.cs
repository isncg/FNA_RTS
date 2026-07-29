using System;

namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// 描述一个矩形搜索区域。TopLeft 包含，BottomRight 不包含。
    /// 用于限定 GridPathGraph 的搜索范围。
    /// </summary>
    public readonly struct Grid
    {
        public readonly IsoCoord TopLeft;      // 包含
        public readonly IsoCoord BottomRight;  // 不包含

        public Grid(IsoCoord topLeft, IsoCoord bottomRight)
        {
            TopLeft = topLeft;
            BottomRight = bottomRight;
        }

        public int Width => BottomRight.X - TopLeft.X;
        public int Height => BottomRight.Y - TopLeft.Y;

        public bool Contains(IsoCoord cell) =>
            cell.X >= TopLeft.X && cell.X < BottomRight.X &&
            cell.Y >= TopLeft.Y && cell.Y < BottomRight.Y;

        /// <summary>
        /// 判断线段 [from, to] 是否穿过本区域（闭区间矩形，
        /// 单元格坐标范围 [TopLeft, BottomRight - 1]）。
        /// 用于分层寻路中判断能否直达更远处的抽象节点。
        /// </summary>
        public bool IntersectsLine(IsoCoord from, IsoCoord to)
        {
            float minX = TopLeft.X, maxX = BottomRight.X - 1;
            float minY = TopLeft.Y, maxY = BottomRight.Y - 1;

            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            float t0 = 0f, t1 = 1f;

            // Liang-Barsky slab clipping
            if (!ClipAxis(from.X, dx, minX, maxX, ref t0, ref t1)) return false;
            if (!ClipAxis(from.Y, dy, minY, maxY, ref t0, ref t1)) return false;
            return true;
        }

        private static bool ClipAxis(float p, float d, float min, float max,
            ref float t0, ref float t1)
        {
            if (MathF.Abs(d) < 1e-9f)
                return p >= min && p <= max;  // 平行于轴：起点必须在带内

            float tEnter = (min - p) / d;
            float tExit = (max - p) / d;
            if (tEnter > tExit)
                (tEnter, tExit) = (tExit, tEnter);

            t0 = MathF.Max(t0, tEnter);
            t1 = MathF.Min(t1, tExit);
            return t0 <= t1;
        }

        public override string ToString() => $"{TopLeft} -> {BottomRight}";
    }
}
