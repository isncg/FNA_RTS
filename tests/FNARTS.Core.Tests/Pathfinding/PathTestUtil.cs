using System.Collections.Generic;
using FNARTS.Core.Pathfinding;

namespace FNARTS.Core.Tests.Pathfinding
{
    /// <summary>寻路测试共用工具。</summary>
    internal static class PathTestUtil
    {
        /// <summary>直线代价 10、对角线代价 14（Grass 地形，无 lane bias）。</summary>
        public const int StraightCost = 10;
        public const int DiagonalCost = 14;

        /// <summary>基于 TileMap 构造地形代价提供者。</summary>
        public static TerrainCostProvider TerrainFor(TileMap map,
            HashSet<IsoCoord> blocked = null)
            => TerrainCostProvider.CreateDefault(map.Width, map.Height,
                c => map.GetTile(c).Type,
                c => blocked != null && blocked.Contains(c));

        /// <summary>全草地地图。</summary>
        public static TileMap OpenMap(int width, int height)
            => new(width, height);

        /// <summary>
        /// 计算一条完整路径（含相邻点对）的移动代价总和。
        /// 假定全草地、无 lane bias。
        /// </summary>
        public static int CostOf(IReadOnlyList<IsoCoord> path)
        {
            int total = 0;
            for (int i = 1; i < path.Count; i++)
            {
                int dx = System.Math.Abs(path[i].X - path[i - 1].X);
                int dy = System.Math.Abs(path[i].Y - path[i - 1].Y);
                bool diagonal = dx != 0 && dy != 0;
                total += diagonal ? DiagonalCost : StraightCost;
            }
            return total;
        }

        /// <summary>检查路径每一步都是合法 8 方向移动且不含阻挡格。</summary>
        public static bool PathIsValid(IReadOnlyList<IsoCoord> path,
            HashSet<IsoCoord> blocked)
        {
            for (int i = 0; i < path.Count; i++)
            {
                if (blocked != null && blocked.Contains(path[i]))
                    return false;
                if (i == 0)
                    continue;
                int dx = System.Math.Abs(path[i].X - path[i - 1].X);
                int dy = System.Math.Abs(path[i].Y - path[i - 1].Y);
                if (dx > 1 || dy > 1 || (dx == 0 && dy == 0))
                    return false;
            }
            return true;
        }
    }
}
