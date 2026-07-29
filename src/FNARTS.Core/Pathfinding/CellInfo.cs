namespace FNARTS.Core.Pathfinding
{
    /// <summary>搜索节点的三种状态。</summary>
    public enum CellStatus : byte
    {
        Unvisited,  // 未被访问（default(CellInfo) 即此状态）
        Open,       // 在开放集合中，待展开
        Closed      // 已展开，不再重新访问
    }

    /// <summary>
    /// 搜索节点的完整信息。
    /// default(CellInfo) 表示 Unvisited 状态，无需显式初始化。
    /// </summary>
    public readonly struct CellInfo
    {
        public readonly CellStatus Status;
        public readonly int CostSoFar;            // 起点到当前的累积代价 (g)
        public readonly int EstimatedTotalCost;   // f = g + h
        public readonly IsoCoord PreviousNode;    // 路径回溯用的前驱节点

        public CellInfo(CellStatus status, int costSoFar,
            int estimatedTotalCost, IsoCoord previousNode)
        {
            Status = status;
            CostSoFar = costSoFar;
            EstimatedTotalCost = estimatedTotalCost;
            PreviousNode = previousNode;
        }
    }
}
