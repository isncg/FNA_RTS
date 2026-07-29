using System;
using System.Collections.Generic;

namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// A* 搜索引擎。在任意 IPathGraph 上执行搜索，
    /// 支持可插拔启发式、启发式权重、单向/双向搜索与增量展开。
    /// </summary>
    public sealed class PathSearch : IDisposable
    {
        /// <summary>搜索过程的记录器接口（用于调试可视化）。</summary>
        public interface IRecorder
        {
            void Add(IsoCoord source, IsoCoord destination,
                int costSoFar, int estimatedRemainingCost);
        }

        /// <summary>无路径时返回的空列表。</summary>
        internal static readonly List<IsoCoord> NoPath = new(0);

        public IPathGraph Graph { get; }
        public Func<IsoCoord, bool> TargetPredicate { get; set; }

        private readonly TerrainCostProvider _terrain;
        private readonly Func<IsoCoord, bool, int> _heuristic;
        private readonly int _heuristicWeightPercentage;
        private readonly IRecorder _recorder;

        // (代价, 插入序号) 二元组保证同代价时按 FIFO 弹出 → 确定性行为
        private readonly PriorityQueue<GraphConnection, (int Cost, long Seq)> _openQueue = new();
        private long _sequence;

        // ── 工厂方法 ──

        /// <summary>
        /// 创建朝向目标格子的搜索（全地图或限定 Grid，自动选择图类型）。
        /// </summary>
        /// <param name="layerPool">全地图搜索用的 CellInfo 层池。</param>
        /// <param name="terrain">地形代价提供者。</param>
        /// <param name="sources">候选起点集合。</param>
        /// <param name="target">目标格子。</param>
        /// <param name="heuristicWeightPercentage">
        ///   启发式权重百分比。100 = 最短路径，&gt;100 = 允许次优但更快。
        /// </param>
        /// <param name="customCost">自定义格子代价（返回 InvalidPath 表示禁止）。</param>
        /// <param name="laneBias">是否启用车道偏移。</param>
        /// <param name="inReverse">是否反向搜索（源点实际是路径终点）。</param>
        /// <param name="heuristic">自定义启发式，null 时使用 Octile Distance。</param>
        /// <param name="grid">限定搜索区域，null 时使用全地图。</param>
        /// <param name="recorder">调试记录器。</param>
        public static PathSearch ToTargetCell(
            CellInfoLayerPool layerPool,
            TerrainCostProvider terrain,
            IEnumerable<IsoCoord> sources,
            IsoCoord target,
            int heuristicWeightPercentage,
            Func<IsoCoord, int> customCost = null,
            bool laneBias = true,
            bool inReverse = false,
            Func<IsoCoord, bool, int> heuristic = null,
            Grid? grid = null,
            IRecorder recorder = null)
        {
            IPathGraph graph;
            if (grid != null)
                graph = new GridPathGraph(terrain, customCost, laneBias, inReverse, grid.Value);
            else
                graph = new MapPathGraph(layerPool, terrain, customCost, laneBias, inReverse);

            heuristic ??= DefaultCostEstimator(terrain, target);
            var search = new PathSearch(graph, terrain, heuristic,
                heuristicWeightPercentage, loc => loc == target, recorder);

            AddInitialCells(terrain, sources, customCost, inReverse, grid, search);

            return search;
        }

        /// <summary>
        /// 在稀疏图（抽象图）上执行搜索。
        /// </summary>
        public static PathSearch ToTargetCellOverGraph(
            Func<IsoCoord, List<GraphConnection>> edges,
            TerrainCostProvider terrain,
            IsoCoord from, IsoCoord target,
            int estimatedSearchSize = 0,
            IRecorder recorder = null)
        {
            var graph = new SparsePathGraph(edges, estimatedSearchSize);
            var search = new PathSearch(graph, terrain,
                DefaultCostEstimator(terrain, target), 100,
                loc => loc == target, recorder);

            search.AddInitialCell(from, null);

            return search;
        }

        /// <summary>
        /// 判定格子是否是合法的寻路位置（在地图内且未被 customCost 排除）。
        /// 源点允许具有不可达的移动代价，因此不检查可进入性。
        /// </summary>
        private static bool CellAllowsMovement(TerrainCostProvider terrain,
            IsoCoord cell, Func<IsoCoord, int> customCost, Grid? grid)
        {
            if (!terrain.InBounds(cell))
                return false;
            if (grid != null && !grid.Value.Contains(cell))
                return false;
            return customCost == null || customCost(cell) != PathCost.InvalidPath;
        }

        private static void AddInitialCells(TerrainCostProvider terrain,
            IEnumerable<IsoCoord> sources, Func<IsoCoord, int> customCost,
            bool inReverse, Grid? grid, PathSearch search)
        {
            // 源点允许具有不可达的移动代价（如单位脚下被自己占据的格子）。
            // 但反向搜索时源点实际是目标，目标必须可进入（地形 + 动态障碍）。
            foreach (var source in sources)
            {
                if (CellAllowsMovement(terrain, source, customCost, grid) &&
                    (!inReverse || terrain.IsPassable(source)))
                    search.AddInitialCell(source, customCost);
            }
        }

        private PathSearch(IPathGraph graph, TerrainCostProvider terrain,
            Func<IsoCoord, bool, int> heuristic, int heuristicWeightPercentage,
            Func<IsoCoord, bool> targetPredicate, IRecorder recorder)
        {
            Graph = graph;
            _terrain = terrain;
            _heuristic = heuristic;
            _heuristicWeightPercentage = heuristicWeightPercentage;
            TargetPredicate = targetPredicate;
            _recorder = recorder;
        }

        private void AddInitialCell(IsoCoord location, Func<IsoCoord, int> customCost)
        {
            int initialCost = 0;
            if (customCost != null)
            {
                initialCost = customCost(location);
                if (initialCost == PathCost.InvalidPath)
                    return;
            }

            var heuristicCost = _heuristic(location, false);
            if (heuristicCost == PathCost.InvalidPath)
                return;

            int estimatedCost = heuristicCost * _heuristicWeightPercentage / 100;
            Graph[location] = new CellInfo(CellStatus.Open, initialCost,
                initialCost + estimatedCost, location);
            _openQueue.Enqueue(
                new GraphConnection(location, estimatedCost),
                (estimatedCost, _sequence++));
        }

        // ── 搜索操作 ──

        /// <summary>
        /// 判断是否还有可展开的节点。为 false 时不能再调用 Expand。
        /// </summary>
        private bool CanExpand()
        {
            // 同一格可能以更高代价再次入队；低代价先处理并将格子置为 Closed。
            // 弹出所有已 Closed 的条目，保证 Expand 只看到 Open 格子。
            CellStatus status;
            do
            {
                if (_openQueue.Count == 0)
                    return false;

                status = Graph[_openQueue.Peek().Destination].Status;
                if (status == CellStatus.Closed)
                    _openQueue.Dequeue();
            }
            while (status == CellStatus.Closed);

            return true;
        }

        /// <summary>
        /// 用 A* 分析开放队列中最有希望节点的邻居，返回该节点。
        /// </summary>
        private IsoCoord Expand()
        {
            var currentMinNode = _openQueue.Dequeue().Destination;

            var currentInfo = Graph[currentMinNode];
            Graph[currentMinNode] = new CellInfo(CellStatus.Closed,
                currentInfo.CostSoFar, currentInfo.EstimatedTotalCost,
                currentInfo.PreviousNode);

            foreach (var connection in Graph.GetConnections(currentMinNode, TargetPredicate))
            {
                int costSoFarToNeighbor = currentInfo.CostSoFar + connection.Cost;

                var neighbor = connection.Destination;
                var neighborInfo = Graph[neighbor];

                // 已有更好的路径到达该邻居：跳过
                if (neighborInfo.Status == CellStatus.Closed ||
                    (neighborInfo.Status == CellStatus.Open &&
                     costSoFarToNeighbor >= neighborInfo.CostSoFar))
                    continue;

                int estimatedRemainingCostToTarget;
                if (neighborInfo.Status == CellStatus.Open)
                {
                    // 重用之前计算的启发式值
                    estimatedRemainingCostToTarget =
                        neighborInfo.EstimatedTotalCost - neighborInfo.CostSoFar;
                }
                else
                {
                    // 启发式报告不可达时不考虑该邻居
                    var heuristicCost = _heuristic(neighbor, true);
                    if (heuristicCost == PathCost.InvalidPath)
                        continue;
                    estimatedRemainingCostToTarget =
                        heuristicCost * _heuristicWeightPercentage / 100;
                }

                _recorder?.Add(currentMinNode, neighbor,
                    costSoFarToNeighbor, estimatedRemainingCostToTarget);

                int estimatedTotalCostToTarget =
                    costSoFarToNeighbor + estimatedRemainingCostToTarget;
                Graph[neighbor] = new CellInfo(CellStatus.Open, costSoFarToNeighbor,
                    estimatedTotalCostToTarget, currentMinNode);
                _openQueue.Enqueue(
                    new GraphConnection(neighbor, estimatedTotalCostToTarget),
                    (estimatedTotalCostToTarget, _sequence++));
            }

            return currentMinNode;
        }

        /// <summary>展开搜索直到找到目标，返回是否找到。</summary>
        public bool ExpandToTarget()
        {
            while (CanExpand())
                if (TargetPredicate(Expand()))
                    return true;

            return false;
        }

        /// <summary>展开搜索覆盖整个可达空间，返回访问过的所有节点。</summary>
        public List<IsoCoord> ExpandAll()
        {
            var consideredCells = new List<IsoCoord>();
            while (CanExpand())
                consideredCells.Add(Expand());
            return consideredCells;
        }

        /// <summary>
        /// 展开搜索直到找到路径。
        /// 返回的路径是*反向*的：目标 → 起点（含两端）。
        /// </summary>
        public List<IsoCoord> FindPath()
        {
            while (CanExpand())
            {
                var p = Expand();
                if (TargetPredicate(p))
                    return MakePath(Graph, p);
            }

            return NoPath;
        }

        // 从目标回溯前驱构建路径。
        // 前驱等于自身的节点即源点。
        private static List<IsoCoord> MakePath(IPathGraph graph, IsoCoord destination)
        {
            var ret = new List<IsoCoord>();
            var currentNode = destination;

            while (graph[currentNode].PreviousNode != currentNode)
            {
                ret.Add(currentNode);
                currentNode = graph[currentNode].PreviousNode;
            }

            ret.Add(currentNode);
            return ret;
        }

        /// <summary>
        /// 双向搜索：交替展开两个搜索直到交汇。
        /// 返回从 first 的起点到 second 起点的完整路径（正向）。
        /// </summary>
        public static List<IsoCoord> FindBidiPath(PathSearch first, PathSearch second)
        {
            while (first.CanExpand() && second.CanExpand())
            {
                // 推进第一个搜索
                var p = first.Expand();
                var pInfo = second.Graph[p];
                if (pInfo.Status == CellStatus.Closed &&
                    pInfo.CostSoFar != PathCost.InvalidPath)
                    return MakeBidiPath(first, second, p);

                // 推进第二个搜索
                var q = second.Expand();
                var qInfo = first.Graph[q];
                if (qInfo.Status == CellStatus.Closed &&
                    qInfo.CostSoFar != PathCost.InvalidPath)
                    return MakeBidiPath(first, second, q);
            }

            return NoPath;
        }

        // 从交汇点分别回溯两个搜索的前驱链，拼接成完整路径。
        private static List<IsoCoord> MakeBidiPath(PathSearch first,
            PathSearch second, IsoCoord confluenceNode)
        {
            var ca = first.Graph;
            var cb = second.Graph;

            var ret = new List<IsoCoord>();

            var q = confluenceNode;
            var previous = ca[q].PreviousNode;
            while (previous != q)
            {
                ret.Add(q);
                q = previous;
                previous = ca[q].PreviousNode;
            }

            ret.Add(q);
            ret.Reverse();

            q = confluenceNode;
            previous = cb[q].PreviousNode;
            while (previous != q)
            {
                q = previous;
                previous = cb[q].PreviousNode;
                ret.Add(q);
            }

            return ret;
        }

        // ── 启发式 ──

        /// <summary>
        /// 默认启发式：Octile Distance。
        /// 基于最小地形代价计算，对 8 方向网格 admissible 且 consistent。
        /// </summary>
        public static Func<IsoCoord, IsoCoord, int> DefaultCostEstimator(
            TerrainCostProvider terrain)
        {
            // 使用最小可能地形代价作为基数，保证不高估
            const int minCellCost = 10;  // 当前只有 Grass = 10
            int diagonalCellCost = DensePathGraph.MultiplyBySqrtTwo(minCellCost);

            return (here, destination) =>
            {
                int dx = Math.Abs(here.X - destination.X);
                int dy = Math.Abs(here.Y - destination.Y);
                int straight = dx + dy;
                int diag = Math.Min(dx, dy);

                // h = minCost * straight + (diagCost - 2*minCost) * diag
                return minCellCost * straight + (diagonalCellCost - 2 * minCellCost) * diag;
            };
        }

        /// <summary>面向固定目标格的启发式包装。</summary>
        public static Func<IsoCoord, bool, int> DefaultCostEstimator(
            TerrainCostProvider terrain, IsoCoord destination)
        {
            var estimator = DefaultCostEstimator(terrain);
            return (here, _) => estimator(here, destination);
        }

        public void Dispose()
        {
            Graph.Dispose();
        }
    }
}
