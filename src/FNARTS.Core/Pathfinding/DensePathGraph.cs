using System;
using System.Collections.Generic;

namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// 密集网格路径图的抽象基类。
    /// 负责邻居枚举、方向裁剪、对角线代价计算、自定义代价叠加。
    /// 派生类提供 CellInfo 的存储方式。
    /// </summary>
    abstract class DensePathGraph : IPathGraph
    {
        // 车道偏移代价（减少单位面对面移动的视觉碰撞）
        private const int LaneBiasCost = 1;

        private readonly TerrainCostProvider _terrain;
        private readonly Func<IsoCoord, int> _customCost;
        private readonly bool _laneBias;
        private readonly bool _inReverse;

        // 按来向裁剪的邻居集合。排除那些"经由前驱节点到达必然更便宜"的邻居：
        // 对于水平/垂直来向，集合是前方 3 格；对于对角来向，是前方 3 格加两侧 2 格。
        // 这成立是因为当前格与前驱格都能到达的任意格子，从任一侧可达则另一侧也可达。
        //
        // 8 个方向索引: index = dy * 3 + dx + 4
        // 枚举: TL=0, T=1, TR=2, L=3, Center=4, R=5, BL=6, B=7, BR=8
        private static readonly IsoCoord[][] DirectedNeighbors =
        {
            new[] { C(-1, -1), C(0, -1), C(1, -1), C(-1, 0), C(-1, 1) },  // TL
            new[] { C(-1, -1), C(0, -1), C(1, -1) },                      // T
            new[] { C(-1, -1), C(0, -1), C(1, -1), C(1, 0), C(1, 1) },    // TR
            new[] { C(-1, -1), C(-1, 0), C(-1, 1) },                      // L
            AllDirections(),                                              // Center
            new[] { C(1, -1), C(1, 0), C(1, 1) },                         // R
            new[] { C(-1, -1), C(-1, 0), C(-1, 1), C(0, 1), C(1, 1) },    // BL
            new[] { C(-1, 1), C(0, 1), C(1, 1) },                         // B
            new[] { C(1, -1), C(1, 0), C(-1, 1), C(0, 1), C(1, 1) },      // BR
        };

        protected DensePathGraph(TerrainCostProvider terrain,
            Func<IsoCoord, int> customCost, bool laneBias, bool inReverse)
        {
            _terrain = terrain;
            _customCost = customCost;
            _laneBias = laneBias;
            _inReverse = inReverse;
        }

        public abstract CellInfo this[IsoCoord node] { get; set; }

        /// <summary>
        /// 派生类重写以限制有效邻居范围（如 GridPathGraph 限制在 Grid 内）。
        /// </summary>
        protected virtual bool IsValidNeighbor(IsoCoord neighbor) => true;

        public List<GraphConnection> GetConnections(IsoCoord position,
            Func<IsoCoord, bool> targetPredicate)
        {
            var info = this[position];
            var previousNode = info.PreviousNode;

            // 根据来向选择裁剪后的邻居集合
            int dx = position.X - previousNode.X;
            int dy = position.Y - previousNode.Y;
            int index = Math.Clamp(dy * 3 + dx + 4, 0, DirectedNeighbors.Length - 1);
            var directions = DirectedNeighbors[index];

            var validNeighbors = new List<GraphConnection>(directions.Length);
            for (int i = 0; i < directions.Length; i++)
            {
                var dir = directions[i];
                var neighbor = new IsoCoord(position.X + dir.X, position.Y + dir.Y);
                if (!IsValidNeighbor(neighbor))
                    continue;

                var pathCost = GetPathCostToNode(position, neighbor, dir, targetPredicate);
                if (pathCost != PathCost.InvalidPath &&
                    this[neighbor].Status != CellStatus.Closed)
                    validNeighbors.Add(new GraphConnection(neighbor, pathCost));
            }

            return validNeighbors;
        }

        private int GetPathCostToNode(IsoCoord src, IsoCoord dest,
            IsoCoord direction, Func<IsoCoord, bool> targetPredicate)
        {
            var movementCost = _terrain.MovementCost(src, dest);

            // 反向搜索时允许进入不可达的目标位置：
            // 反转后这实际上是源点，允许从不可达源点向外移动。
            if (movementCost == PathCost.UnreachableCell &&
                _inReverse && targetPredicate(dest))
                movementCost = 0;

            if (movementCost != PathCost.UnreachableCell)
                return CalculateCellPathCost(dest, direction, movementCost);

            return PathCost.InvalidPath;
        }

        private int CalculateCellPathCost(IsoCoord neighbor,
            IsoCoord direction, short movementCost)
        {
            // 对角线移动代价 = movementCost × √2
            int cellCost = direction.X * direction.Y != 0
                ? MultiplyBySqrtTwo(movementCost)
                : movementCost;

            // 自定义代价叠加（如威胁区域惩罚）
            if (_customCost != null)
            {
                int customCellCost = _customCost(neighbor);
                if (customCellCost == PathCost.InvalidPath)
                    return PathCost.InvalidPath;
                cellCost += customCellCost;
            }

            // 车道偏移：给特定方向的移动加微小代价，减少面对面的视觉碰撞
            if (_laneBias)
            {
                int ux = (neighbor.X + (_inReverse ? 1 : 0)) & 1;
                int uy = (neighbor.Y + (_inReverse ? 1 : 0)) & 1;

                if ((ux == 0 && direction.Y < 0) || (ux == 1 && direction.Y > 0))
                    cellCost += LaneBiasCost;
                if ((uy == 0 && direction.X < 0) || (uy == 1 && direction.X > 0))
                    cellCost += LaneBiasCost;
            }

            return cellCost;
        }

        /// <summary>整数乘以 √2 的定点近似：value × 1414 / 1000。</summary>
        internal static int MultiplyBySqrtTwo(int value)
            => value * 1414 / 1000;

        protected virtual void Dispose(bool disposing) { }

        public void Dispose() => Dispose(true);

        private static IsoCoord C(int x, int y) => new(x, y);

        private static IsoCoord[] AllDirections() => new[]
        {
            C(-1, -1), C(0, -1), C(1, -1),
            C(-1, 0),           C(1, 0),
            C(-1, 1),  C(0, 1), C(1, 1),
        };
    }
}
