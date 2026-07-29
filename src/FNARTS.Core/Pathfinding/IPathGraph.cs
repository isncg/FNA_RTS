using System;
using System.Collections.Generic;

namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// 寻路图的抽象接口。
    /// 搜索算法 (PathSearch) 只通过此接口与图交互，
    /// 不关心底层是密集网格还是稀疏抽象图。
    /// </summary>
    public interface IPathGraph : IDisposable
    {
        /// <summary>
        /// 获取从 source 出发的所有可达邻接连接及其代价。
        /// </summary>
        /// <param name="source">当前节点。</param>
        /// <param name="targetPredicate">
        /// 用于判定目标节点（反向搜索时允许进入不可达的目标）。
        /// </param>
        List<GraphConnection> GetConnections(IsoCoord source,
            Func<IsoCoord, bool> targetPredicate);

        /// <summary>读写节点的搜索信息。</summary>
        CellInfo this[IsoCoord node] { get; set; }
    }
}
