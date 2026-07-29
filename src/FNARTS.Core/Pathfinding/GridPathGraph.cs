using System;

namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// 在指定 Grid 区域内的密集路径图。
    /// CellInfo 使用 flat array 存储，区域外的节点视为不可达。
    /// 用于近距离路径搜索（避免全地图搜索的开销）。
    /// </summary>
    sealed class GridPathGraph : DensePathGraph
    {
        private readonly CellInfo[] _infos;
        private readonly Grid _grid;

        public GridPathGraph(TerrainCostProvider terrain,
            Func<IsoCoord, int> customCost, bool laneBias,
            bool inReverse, Grid grid)
            : base(terrain, customCost, laneBias, inReverse)
        {
            _infos = new CellInfo[grid.Width * grid.Height];
            _grid = grid;
        }

        public Grid Grid => _grid;

        protected override bool IsValidNeighbor(IsoCoord neighbor)
            => _grid.Contains(neighbor);

        private int InfoIndex(IsoCoord pos) =>
            (pos.Y - _grid.TopLeft.Y) * _grid.Width +
            (pos.X - _grid.TopLeft.X);

        public override CellInfo this[IsoCoord pos]
        {
            get => _infos[InfoIndex(pos)];
            set => _infos[InfoIndex(pos)] = value;
        }
    }
}
