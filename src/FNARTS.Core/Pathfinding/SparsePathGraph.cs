using System;
using System.Collections.Generic;

namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// 稀疏路径图：使用 Dictionary 存储 CellInfo。
    /// 用于抽象图搜索（分层寻路的粗粒度层）。
    /// </summary>
    sealed class SparsePathGraph : IPathGraph
    {
        private readonly Func<IsoCoord, List<GraphConnection>> _edges;
        private readonly Dictionary<IsoCoord, CellInfo> _info;

        public SparsePathGraph(
            Func<IsoCoord, List<GraphConnection>> edges,
            int estimatedSize = 0)
        {
            _edges = edges;
            _info = new Dictionary<IsoCoord, CellInfo>(estimatedSize);
        }

        public List<GraphConnection> GetConnections(IsoCoord position,
            Func<IsoCoord, bool> targetPredicate)
            => _edges(position) ?? new List<GraphConnection>();

        public CellInfo this[IsoCoord pos]
        {
            get => _info.TryGetValue(pos, out var info) ? info : default;
            set => _info[pos] = value;
        }

        public void Dispose() { }
    }
}
