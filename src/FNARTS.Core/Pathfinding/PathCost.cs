namespace FNARTS.Core.Pathfinding
{
    /// <summary>路径图代价常量。</summary>
    public static class PathCost
    {
        /// <summary>无效路径的代价（启发式或边代价报告"不可用"时使用）。</summary>
        public const int InvalidPath = int.MaxValue;

        /// <summary>不可到达的移动代价（单元格无法进入时使用）。</summary>
        public const short UnreachableCell = short.MaxValue;
    }

    /// <summary>
    /// 图中的一条有向连接：目标节点 + 到达目标的代价。
    /// </summary>
    public readonly struct GraphConnection
    {
        public readonly IsoCoord Destination;
        public readonly int Cost;

        public GraphConnection(IsoCoord destination, int cost)
        {
            Destination = destination;
            Cost = cost;
        }

        public override string ToString() => $"-> {Destination} = {Cost}";
    }

    /// <summary>
    /// 图中的一条有向边：源节点 + 目标节点。
    /// 用于向抽象图中临时插入源/目标的局部连接。
    /// </summary>
    public readonly struct GraphEdge
    {
        public readonly IsoCoord Source;
        public readonly IsoCoord Destination;

        public GraphEdge(IsoCoord source, IsoCoord destination)
        {
            Source = source;
            Destination = destination;
        }

        public override string ToString() => $"{Source} -> {Destination}";
    }
}
