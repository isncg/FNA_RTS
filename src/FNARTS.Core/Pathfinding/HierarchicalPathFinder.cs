using System;
using System.Collections.Generic;
using System.Linq;

namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// 分层寻路器。维护地图的抽象图，提供高效的路径搜索。
    ///
    /// 抽象图通过将地图划分为 GridSize×GridSize 的小网格构建：
    /// 每个网格内的连通区域用一个抽象节点表示，
    /// 相邻网格之间的边界连通性构成抽象边。
    ///
    /// 搜索时先在抽象图上快速求出粗略路线，再用该路线引导
    /// 精细 A* 搜索的启发式，显著减少绕障时的无效展开。
    /// 抽象图只考虑地形（不含动态实体），动态障碍由精细搜索处理。
    /// </summary>
    public sealed class HierarchicalPathFinder
    {
        // 通过实验确定的最佳性能平衡点
        private const int GridSize = 10;
        private const int CloseGridDistance = 2;

        private static readonly IsoCoord[] Directions =
        {
            new(-1, -1), new(0, -1), new(1, -1),
            new(-1, 0),             new(1, 0),
            new(-1, 1),  new(0, 1), new(1, 1),
        };

        private readonly TerrainCostProvider _terrain;
        private readonly CellInfoLayerPool _layerPool;
        private readonly Func<IsoCoord, int> _customCost;
        private readonly Func<IsoCoord, IsoCoord, int> _costEstimator;

        // 地图被划分成的网格边界、数量
        private Grid _mapBounds;
        private int _gridXs;
        private int _gridYs;

        // 每个网格的抽象信息（按 GridIndex 索引）
        private GridInfo[] _gridInfos;

        // 抽象图：抽象节点 → 邻接列表。无连接的节点不在图中。
        private Dictionary<IsoCoord, List<GraphConnection>> _abstractGraph;

        // 抽象域：抽象节点 → 域编号。同域互相可达，异域不可达。
        private readonly Dictionary<IsoCoord, uint> _abstractDomains;

        // 脏网格索引：地形变化时标记需要重建的网格
        private readonly HashSet<int> _dirtyGridIndexes = new();

        /// <summary>
        /// 单个 Grid 的抽象信息：知道网格内的抽象节点，
        /// 能把本地格子映射到其所属的抽象节点。
        /// </summary>
        private readonly struct GridInfo
        {
            // 整个 Grid 只有一个连通区域时，直接存储该区域的抽象节点，
            // 免去 Dictionary 查找。
            private readonly IsoCoord? _singleAbstractCell;

            // 多个连通区域（被障碍分割）时，存储本地格子 → 抽象节点映射。
            private readonly Dictionary<IsoCoord, IsoCoord> _localCellToAbstractCell;

            public GridInfo(IsoCoord? singleAbstractCell,
                Dictionary<IsoCoord, IsoCoord> localCellToAbstractCell)
            {
                _singleAbstractCell = singleAbstractCell;
                _localCellToAbstractCell = localCellToAbstractCell;
            }

            /// <summary>
            /// 把本地格子映射到抽象节点。格子不可达时返回 null。
            /// hpf 传 null 时跳过可达性检查（调用者已检查过）。
            /// </summary>
            public IsoCoord? AbstractCellForLocalCell(IsoCoord localCell,
                HierarchicalPathFinder hpf)
            {
                if (_singleAbstractCell != null)
                {
                    // 网格内所有可达格子属于同一区域，但仍需排除不可达格子
                    if (hpf != null && !hpf.CellIsAccessible(localCell))
                        return null;
                    return _singleAbstractCell;
                }

                // 只有可达格子会被登记，无需再检查代价
                if (_localCellToAbstractCell.TryGetValue(localCell, out var abstractCell))
                    return abstractCell;
                return null;
            }

            public void CopyAbstractCellsInto(HashSet<IsoCoord> set)
            {
                if (_singleAbstractCell != null)
                    set.Add(_singleAbstractCell.Value);
                foreach (var cell in _localCellToAbstractCell.Values)
                    set.Add(cell);
            }
        }

        /// <summary>
        /// 带临时插入边的抽象图。不复制整个抽象图，
        /// 而是用一个补充字典记录变更。
        /// </summary>
        private sealed class AbstractGraphWithInsertedEdges
        {
            private readonly Dictionary<IsoCoord, List<GraphConnection>> _abstractEdges;
            private readonly Dictionary<IsoCoord, List<GraphConnection>> _changedEdges;

            public AbstractGraphWithInsertedEdges(
                Dictionary<IsoCoord, List<GraphConnection>> abstractEdges,
                IList<GraphEdge> sourceEdges,
                GraphEdge? targetEdge,
                Func<IsoCoord, IsoCoord, int> costEstimator)
            {
                _abstractEdges = abstractEdges;
                _changedEdges = new Dictionary<IsoCoord, List<GraphConnection>>(
                    sourceEdges.Count * 9 + (targetEdge != null ? 9 : 0));
                foreach (var sourceEdge in sourceEdges)
                    InsertConnections(sourceEdge.Source, sourceEdge.Destination, costEstimator);
                if (targetEdge != null)
                    InsertConnections(targetEdge.Value.Source, targetEdge.Value.Destination,
                        costEstimator);
            }

            private void InsertConnections(IsoCoord localCell, IsoCoord abstractCell,
                Func<IsoCoord, IsoCoord, int> costEstimator)
            {
                if (!_abstractEdges.TryGetValue(abstractCell, out var edges))
                    edges = new List<GraphConnection>();

                // localCell → 抽象节点的所有出边（代价重算为到 localCell 的距离）
                var localConnections = new List<GraphConnection>(edges.Count + 1);
                foreach (var e in edges)
                    localConnections.Add(new GraphConnection(e.Destination,
                        costEstimator(localCell, e.Destination)));
                localConnections.Add(new GraphConnection(abstractCell,
                    costEstimator(localCell, abstractCell)));
                _changedEdges[localCell] = localConnections;

                // 抽象节点 → localCell 的反向边
                var abstractConnections = _changedEdges.TryGetValue(abstractCell, out var existing)
                    ? existing
                    : new List<GraphConnection>(edges);
                abstractConnections.Add(new GraphConnection(localCell,
                    costEstimator(abstractCell, localCell)));
                _changedEdges[abstractCell] = abstractConnections;

                // 抽象节点的每个邻居也获得到 localCell 的边
                foreach (var conn in edges)
                {
                    List<GraphConnection> neighborConnections;
                    if (_changedEdges.TryGetValue(conn.Destination, out var existingNeighbor))
                        neighborConnections = existingNeighbor;
                    else if (_abstractEdges.TryGetValue(conn.Destination, out var neighborEdges))
                        neighborConnections = new List<GraphConnection>(neighborEdges);
                    else
                        neighborConnections = new List<GraphConnection>();

                    neighborConnections.Add(new GraphConnection(localCell,
                        costEstimator(conn.Destination, localCell)));
                    _changedEdges[conn.Destination] = neighborConnections;
                }
            }

            public List<GraphConnection> GetConnections(IsoCoord position)
            {
                if (_changedEdges.TryGetValue(position, out var changedEdge))
                    return changedEdge;
                if (_abstractEdges.TryGetValue(position, out var abstractEdge))
                    return abstractEdge;
                return new List<GraphConnection>();
            }
        }

        public HierarchicalPathFinder(TerrainCostProvider terrain,
            Func<IsoCoord, int> customCost = null)
        {
            _terrain = terrain;
            _layerPool = new CellInfoLayerPool(terrain.MapWidth, terrain.MapHeight);
            _customCost = customCost;
            _costEstimator = PathSearch.DefaultCostEstimator(terrain);

            BuildGrids();
            BuildCostTable();
            _abstractDomains = new Dictionary<IsoCoord, uint>(_gridXs * _gridYs);
            RebuildDomains();
        }

        // ── 公共 API ──

        /// <summary>
        /// 计算从 source 到 target 的路径（双向搜索）。
        /// 返回的路径是*反向*的：目标 → 起点（含两端），无路径时为空。
        /// </summary>
        public List<IsoCoord> FindPath(IsoCoord source, IsoCoord target,
            int heuristicWeightPercentage = 100, bool laneBias = true)
        {
            // 近距离优先在限定区域内搜索，避免抽象搜索的开销
            int dx = target.X - source.X, dy = target.Y - source.Y;
            if (dx * dx + dy * dy < GridSize * GridSize *
                CloseGridDistance * CloseGridDistance)
            {
                var gridToSearch = new Grid(
                    new IsoCoord(
                        Math.Min(source.X, target.X) - GridSize / 2,
                        Math.Min(source.Y, target.Y) - GridSize / 2),
                    new IsoCoord(
                        Math.Max(source.X, target.X) + GridSize / 2,
                        Math.Max(source.Y, target.Y) + GridSize / 2));

                // 近距离强制 100% 权重（最短路径）：
                // 玩家对短距离的次优路径更敏感，而限定区域开销可控。
                List<IsoCoord> localPath;
                using (var search = PathSearch.ToTargetCell(
                    _layerPool, _terrain, new[] { source }, target, 100,
                    _customCost, laneBias, false, null, gridToSearch))
                    localPath = search.FindPath();

                if (localPath.Count > 0)
                    return localPath;
            }

            RebuildDirtyGrids();

            // 目标格地形不可达 → 无路径
            var targetAbstractCell = AbstractCellForLocalCell(target);
            if (targetAbstractCell == null)
                return PathSearch.NoPath;

            // 起点格地形不可达时，仍可能经由相邻格出发（交给多起点重载处理）
            var sourceAbstractCell = AbstractCellForLocalCell(source);
            if (sourceAbstractCell == null)
                return FindPath(new[] { source }, target,
                    heuristicWeightPercentage, laneBias);

            // 不同域 → 不可达
            RebuildDomains();
            var targetDomain = _abstractDomains[targetAbstractCell.Value];
            var sourceDomain = _abstractDomains[sourceAbstractCell.Value];
            if (sourceDomain != targetDomain)
                return PathSearch.NoPath;

            var targetEdge = EdgeFromLocalToAbstract(target, targetAbstractCell.Value);
            var sourceEdge = EdgeFromLocalToAbstract(source, sourceAbstractCell.Value);

            // 插入的边视为双向
            var fullGraph = new AbstractGraphWithInsertedEdges(
                _abstractGraph,
                sourceEdge != null
                    ? new[] { sourceEdge.Value }
                    : Array.Empty<GraphEdge>(),
                targetEdge, _costEstimator);

            // 双向抽象搜索：正向结果引导反向精细搜索，反之亦然
            var estimatedSearchSize = (_abstractGraph.Count + 2) / 8;
            using (var forwardAbstractSearch = PathSearch.ToTargetCellOverGraph(
                fullGraph.GetConnections, _terrain, source, target, estimatedSearchSize))
            {
                if (!forwardAbstractSearch.ExpandToTarget())
                    return PathSearch.NoPath;

                using (var reverseAbstractSearch = PathSearch.ToTargetCellOverGraph(
                    fullGraph.GetConnections, _terrain, target, source, estimatedSearchSize))
                {
                    reverseAbstractSearch.ExpandToTarget();

                    using (var fromSrc = PathSearch.ToTargetCell(
                        _layerPool, _terrain, new[] { source }, target,
                        heuristicWeightPercentage, _customCost, laneBias, false,
                        Heuristic(reverseAbstractSearch, estimatedSearchSize, null, null)))
                    using (var fromDest = PathSearch.ToTargetCell(
                        _layerPool, _terrain, new[] { target }, source,
                        heuristicWeightPercentage, _customCost, laneBias, true,
                        Heuristic(forwardAbstractSearch, estimatedSearchSize, null, null)))
                        return PathSearch.FindBidiPath(fromDest, fromSrc);
                }
            }
        }

        /// <summary>
        /// 从多个候选起点寻找到 target 的路径（单向搜索）。
        /// 返回的路径是*反向*的：目标 → 起点（含两端），无路径时为空。
        /// </summary>
        public List<IsoCoord> FindPath(IReadOnlyCollection<IsoCoord> sources,
            IsoCoord target, int heuristicWeightPercentage = 100, bool laneBias = true)
        {
            if (!_mapBounds.Contains(target))
                return PathSearch.NoPath;

            RebuildDirtyGrids();

            var targetAbstractCell = AbstractCellForLocalCell(target);
            if (targetAbstractCell == null)
                return PathSearch.NoPath;

            RebuildDomains();
            var targetDomain = _abstractDomains[targetAbstractCell.Value];

            // 起点允许是不可达位置：此时改用其相邻可达格作为出发点
            var sourcesWithPathableNodes = new HashSet<IsoCoord>(sources.Count);
            var sourceEdges = new List<GraphEdge>(sources.Count);
            List<IsoCoord> unpathableNodes = null;
            foreach (var source in sources)
            {
                if (!_mapBounds.Contains(source))
                    continue;

                var sourceAbstractCell = AbstractCellForLocalCell(source);
                if (sourceAbstractCell != null)
                {
                    var sourceDomain = _abstractDomains[sourceAbstractCell.Value];
                    if (sourceDomain != targetDomain)
                        continue;

                    sourcesWithPathableNodes.Add(source);
                    var sourceEdge = EdgeFromLocalToAbstract(source, sourceAbstractCell.Value);
                    if (sourceEdge != null)
                        sourceEdges.Add(sourceEdge.Value);
                    continue;
                }

                // 起点不可达：检查相邻格
                foreach (var dir in Directions)
                {
                    var adjacentSource = source + dir;
                    if (!MovementAllowedBetweenCells(source, adjacentSource))
                        continue;

                    var adjacentSourceAbstractCell =
                        AbstractCellForLocalCell(adjacentSource);
                    if (adjacentSourceAbstractCell == null)
                        continue;

                    var adjacentSourceDomain =
                        _abstractDomains[adjacentSourceAbstractCell.Value];
                    if (adjacentSourceDomain != targetDomain)
                    {
                        // 该相邻格所在区域与目标断开，启发式中需排除
                        unpathableNodes ??= new List<IsoCoord>();
                        unpathableNodes.Add(adjacentSource);
                        continue;
                    }

                    sourcesWithPathableNodes.Add(source);
                    var sourceEdge = EdgeFromLocalToAbstract(adjacentSource,
                        adjacentSourceAbstractCell.Value);
                    if (sourceEdge != null)
                        sourceEdges.Add(sourceEdge.Value);
                }
            }

            if (sourcesWithPathableNodes.Count == 0)
                return PathSearch.NoPath;

            var targetEdge = EdgeFromLocalToAbstract(target, targetAbstractCell.Value);

            var fullGraph = new AbstractGraphWithInsertedEdges(
                _abstractGraph, sourceEdges, targetEdge, _costEstimator);

            // 反向抽象搜索为单向精细搜索提供启发式
            var estimatedSearchSize = (_abstractGraph.Count + 2) / 8;
            using (var reverseAbstractSearch = PathSearch.ToTargetCellOverGraph(
                fullGraph.GetConnections, _terrain, target, target, estimatedSearchSize))
            using (var fromSrc = PathSearch.ToTargetCell(
                _layerPool, _terrain, sourcesWithPathableNodes, target,
                heuristicWeightPercentage, _customCost, laneBias, false,
                Heuristic(reverseAbstractSearch, estimatedSearchSize,
                    sourcesWithPathableNodes, unpathableNodes)))
                return fromSrc.FindPath();
        }

        /// <summary>
        /// 快速判定两个位置之间是否存在路径（不实际计算路径）。
        /// 利用抽象域信息进行近似 O(1) 判定。
        /// 只考虑地形，不考虑动态实体。
        /// </summary>
        public bool PathExists(IsoCoord source, IsoCoord target)
        {
            if (!_mapBounds.Contains(source) || !_mapBounds.Contains(target))
                return false;

            RebuildDomains();

            var abstractTarget = AbstractCellForLocalCell(target);
            if (abstractTarget == null)
                return false;
            var targetDomain = _abstractDomains[abstractTarget.Value];

            // 起点可达时直接比较域
            var abstractSource = AbstractCellForLocalCell(source);
            if (abstractSource != null)
                return _abstractDomains[abstractSource.Value] == targetDomain;

            // 起点不可达时，检查相邻可达格是否与目标同域
            foreach (var dir in Directions)
            {
                var adjacentSource = source + dir;
                if (!MovementAllowedBetweenCells(source, adjacentSource))
                    continue;

                var abstractAdjacentSource = AbstractCellForLocalCell(adjacentSource);
                if (abstractAdjacentSource == null)
                    continue;

                if (_abstractDomains[abstractAdjacentSource.Value] == targetDomain)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 通知地形变化（建筑建造/摧毁、地形编辑等），
        /// 标记所属网格为脏，下次搜索前重建。
        /// </summary>
        public void NotifyTerrainChanged(IsoCoord cell)
        {
            if (!_mapBounds.Contains(cell))
                return;
            _dirtyGridIndexes.Add(GridIndex(cell));
        }

        // ── 抽象图构建 ──

        /// <summary>将地图划分为 GridSize×GridSize 的网格。</summary>
        private void BuildGrids()
        {
            _mapBounds = new Grid(IsoCoord.Zero,
                new IsoCoord(_terrain.MapWidth, _terrain.MapHeight));
            _gridXs = DivideRoundUp(_mapBounds.Width, GridSize);
            _gridYs = DivideRoundUp(_mapBounds.Height, GridSize);

            _gridInfos = new GridInfo[_gridXs * _gridYs];
            for (int gridX = _mapBounds.TopLeft.X; gridX < _mapBounds.BottomRight.X; gridX += GridSize)
                for (int gridY = _mapBounds.TopLeft.Y; gridY < _mapBounds.BottomRight.Y; gridY += GridSize)
                    _gridInfos[GridIndex(new IsoCoord(gridX, gridY))] =
                        BuildGrid(gridX, gridY);
        }

        /// <summary>
        /// 确定单个网格内的抽象节点。每个互相可达的格子集合创建一个抽象节点。
        /// 开阔地形通常只有一个区域；被不可通行地形分割时有多个。
        /// 同时记录本地格子到抽象节点的映射。
        /// </summary>
        private GridInfo BuildGrid(int gridX, int gridY)
        {
            IsoCoord? singleAbstractCell = null;
            var localCellToAbstractCell = new Dictionary<IsoCoord, IsoCoord>();

            var grid = GetGrid(new IsoCoord(gridX, gridY), _mapBounds);
            var accessibleCells = new HashSet<IsoCoord>(GridSize * GridSize);
            for (int y = gridY; y < grid.BottomRight.Y; y++)
            for (int x = gridX; x < grid.BottomRight.X; x++)
            {
                var cell = new IsoCoord(x, y);
                if (CellIsAccessible(cell))
                    accessibleCells.Add(cell);
            }

            // 从一个可达格 flood fill，发现一个连通区域；
            // 重复直到所有不相交区域都被发现。
            bool hasPopulatedAbstractCell = false;
            while (accessibleCells.Count > 0)
            {
                var src = MinCell(accessibleCells);
                var localCellsInRegion = FloodFillRegion(src, grid);
                var abstractCell = AbstractCellForLocalCells(localCellsInRegion);
                accessibleCells.ExceptWith(localCellsInRegion);

                // 只有一个区域时，整个网格用一个抽象节点表示，
                // 不需要保存本地格子到抽象节点的映射。
                if (!hasPopulatedAbstractCell && accessibleCells.Count == 0)
                    singleAbstractCell = abstractCell;
                else
                {
                    hasPopulatedAbstractCell = true;
                    foreach (var localCell in localCellsInRegion)
                        localCellToAbstractCell.Add(localCell, abstractCell);
                }
            }

            return new GridInfo(singleAbstractCell, localCellToAbstractCell);
        }

        /// <summary>
        /// 从 src 出发在 grid 范围内 flood fill，返回区域内所有格子。
        /// 连通性使用"两端格子的地形均可通行"的 8 邻接关系（对称且可传递），
        /// 而非含切角规则的移动判定：切角规则不是可传递关系，
        /// 用它划分区域会导致区域重叠、同一格子被分配到多个抽象节点。
        /// 这只会把被切角规则隔开的格子合并进同一区域（抽象图更粗），
        /// 不影响正确性：域判定和精细搜索仍基于真实移动规则。
        /// </summary>
        private List<IsoCoord> FloodFillRegion(IsoCoord src, Grid grid)
        {
            var region = new List<IsoCoord>(GridSize * GridSize);
            var seen = new HashSet<IsoCoord>(GridSize * GridSize) { src };
            var queue = new Queue<IsoCoord>();
            queue.Enqueue(src);

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                region.Add(cell);

                for (int i = 0; i < Directions.Length; i++)
                {
                    var neighbor = cell + Directions[i];
                    if (!grid.Contains(neighbor) || !seen.Add(neighbor))
                        continue;
                    if (_terrain.IsTerrainPassable(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            return region;
        }

        /// <summary>
        /// 为一个连通区域选择抽象节点：取区域包围盒中心最近的格子，
        /// 平局时取最左最上（确定性 tiebreak，与列表顺序无关）。
        /// </summary>
        private static IsoCoord AbstractCellForLocalCells(List<IsoCoord> cells)
        {
            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            foreach (var cell in cells)
            {
                minX = Math.Min(cell.X, minX);
                minY = Math.Min(cell.Y, minY);
                maxX = Math.Max(cell.X, maxX);
                maxY = Math.Max(cell.Y, maxY);
            }

            var desired = new IsoCoord(
                minX + (maxX - minX) / 2,
                minY + (maxY - minY) / 2);

            var abstractCell = desired;
            int distance = int.MaxValue;
            foreach (var cell in cells)
            {
                int cdx = cell.X - desired.X, cdy = cell.Y - desired.Y;
                int newDistance = cdx * cdx + cdy * cdy;
                if (distance > newDistance ||
                    (distance == newDistance && abstractCell.X > cell.X) ||
                    (distance == newDistance && abstractCell.X == cell.X &&
                     abstractCell.Y > cell.Y))
                {
                    distance = newDistance;
                    abstractCell = cell;
                }
            }

            return abstractCell;
        }

        /// <summary>构建抽象图的所有边。</summary>
        private void BuildCostTable()
        {
            _abstractGraph = new Dictionary<IsoCoord, List<GraphConnection>>(_gridXs * _gridYs);
            for (int gridX = _mapBounds.TopLeft.X; gridX < _mapBounds.BottomRight.X; gridX += GridSize)
                for (int gridY = _mapBounds.TopLeft.Y; gridY < _mapBounds.BottomRight.Y; gridY += GridSize)
                    foreach (var edges in GetAbstractEdgesForGrid(gridX, gridY))
                        _abstractGraph.Add(edges.Key, edges.Value);
        }

        /// <summary>
        /// 确定给定网格内部的抽象边，以及与相邻网格之间的抽象边。
        /// 沿网格四条边扫描，检查边界格能否进入相邻网格的 3 个候选格。
        /// </summary>
        private IEnumerable<KeyValuePair<IsoCoord, List<GraphConnection>>>
            GetAbstractEdgesForGrid(int gridX, int gridY)
        {
            var abstractEdges = new HashSet<(IsoCoord Src, IsoCoord Dst)>();

            void AddEdgesIfMovementAllowedBetweenCells(IsoCoord cell, IsoCoord candidateCell)
            {
                if (!MovementAllowedBetweenCells(cell, candidateCell))
                    return;

                var abstractCell = AbstractCellForLocalCellNoAccessibleCheck(cell);
                if (abstractCell == null)
                    return;

                var abstractCellAdjacent =
                    AbstractCellForLocalCellNoAccessibleCheck(candidateCell);
                if (abstractCellAdjacent == null)
                    return;

                abstractEdges.Add((abstractCell.Value, abstractCellAdjacent.Value));
            }

            void AddAbstractEdges(int xIncrement, int yIncrement,
                IsoCoord adjacentVec, int offsetX, int offsetY)
            {
                int startY = gridY + offsetY;
                int startX = gridX + offsetX;
                for (int y = startY; y < startY + GridSize; y += yIncrement)
                for (int x = startX; x < startX + GridSize; x += xIncrement)
                {
                    var cell = new IsoCoord(x, y);
                    if (!_mapBounds.Contains(cell) || !CellIsAccessible(cell))
                        continue;

                    var adjacentCell = cell + adjacentVec;
                    for (int i = -1; i <= 1; i++)
                    {
                        var candidateCell = adjacentCell +
                            new IsoCoord(i * adjacentVec.Y, i * adjacentVec.X);
                        AddEdgesIfMovementAllowedBetweenCells(cell, candidateCell);
                    }
                }
            }

            // 上、左、下、右四条边
            AddAbstractEdges(1, GridSize, new IsoCoord(0, -1), 0, 0);
            AddAbstractEdges(GridSize, 1, new IsoCoord(-1, 0), 0, 0);
            AddAbstractEdges(1, GridSize, new IsoCoord(0, 1), 0, GridSize - 1);
            AddAbstractEdges(GridSize, 1, new IsoCoord(1, 0), GridSize - 1, 0);

            return abstractEdges
                .GroupBy(edge => edge.Src)
                .Select(group => KeyValuePair.Create(
                    group.Key,
                    group.Select(edge => new GraphConnection(edge.Dst,
                        _costEstimator(edge.Src, edge.Dst))).ToList()));
        }

        private bool CellIsAccessible(IsoCoord cell)
            => _terrain.IsTerrainPassable(cell);

        private bool TerrainMovementAllowed(IsoCoord src, IsoCoord dst)
            => _terrain.TerrainMovementCost(src, dst) != PathCost.UnreachableCell;

        private bool MovementAllowedBetweenCells(IsoCoord src, IsoCoord dst)
            => TerrainMovementAllowed(src, dst);

        // ── 脏网格重建 ──

        /// <summary>重建所有脏网格的抽象信息。</summary>
        private void RebuildDirtyGrids()
        {
            if (_dirtyGridIndexes.Count == 0)
                return;

            // 域缓存失效：清空后下次访问时重建
            _abstractDomains.Clear();

            foreach (var gridIndex in _dirtyGridIndexes)
            {
                var oldGrid = _gridInfos[gridIndex];
                var gridTopLeft = GetGridTopLeft(gridIndex);
                _gridInfos[gridIndex] = BuildGrid(gridTopLeft.X, gridTopLeft.Y);
                RebuildCostTable(gridTopLeft.X, gridTopLeft.Y, oldGrid);
            }

            _dirtyGridIndexes.Clear();
        }

        /// <summary>
        /// 更新抽象图以反映某个网格的变化：
        /// 移除旧的抽象节点，重新计算本网格与相邻网格的边。
        /// </summary>
        private void RebuildCostTable(int gridX, int gridY, GridInfo oldGrid)
        {
            // 该网格的抽象节点可能已变化，先移除旧节点。
            // 这很重要：GetAbstractEdgesForGrid 只看*当前*网格，
            // 感知不到更新前就消失的节点。
            var abstractNodes = new HashSet<IsoCoord>();
            oldGrid.CopyAbstractCellsInto(abstractNodes);
            foreach (var oldAbstractNode in abstractNodes)
                _abstractGraph.Remove(oldAbstractNode);
            abstractNodes.Clear();

            // 重新添加本网格的边（旧节点已清除，这里都是新的）
            foreach (var edges in GetAbstractEdgesForGrid(gridX, gridY))
                _abstractGraph.Add(edges.Key, edges.Value);

            foreach (var direction in Directions)
            {
                var adjacentGrid = new IsoCoord(
                    gridX + GridSize * direction.X,
                    gridY + GridSize * direction.Y);
                if (!_mapBounds.Contains(adjacentGrid))
                    continue;

                // 相邻网格的抽象节点不变，但连接可能变了：
                // 更新连接，并记录哪些节点被更新过。
                _gridInfos[GridIndex(adjacentGrid)].CopyAbstractCellsInto(abstractNodes);
                foreach (var edges in GetAbstractEdgesForGrid(adjacentGrid.X, adjacentGrid.Y))
                {
                    _abstractGraph[edges.Key] = edges.Value;
                    abstractNodes.Remove(edges.Key);
                }

                // 剩余节点现在没有任何连接，从图中移除
                foreach (var unconnectedNode in abstractNodes)
                    _abstractGraph.Remove(unconnectedNode);
                abstractNodes.Clear();
            }
        }

        /// <summary>
        /// 重建抽象域（连通分量）：对抽象图 flood fill，
        /// 确定哪些抽象节点互相可达。
        /// 用于 PathExists 的 O(1) 查询和 FindPath 的快速失败。
        /// </summary>
        private void RebuildDomains()
        {
            // 先重建过期的抽象图
            RebuildDirtyGrids();

            // 域缓存为空表示已过期，需要重建
            if (_abstractDomains.Count != 0)
                return;

            List<GraphConnection> AbstractEdge(IsoCoord abstractCell)
                => _abstractGraph.TryGetValue(abstractCell, out var edge) ? edge : null;

            // 与 BuildGrid 相同的方式 flood fill，发现所有不相交的域
            uint domain = 0;
            var abstractCells = new HashSet<IsoCoord>(_abstractGraph.Count);
            foreach (var grid in _gridInfos)
                grid.CopyAbstractCellsInto(abstractCells);

            while (abstractCells.Count > 0)
            {
                var searchCell = MinCell(abstractCells);
                using (var search = PathSearch.ToTargetCellOverGraph(
                    AbstractEdge, _terrain, searchCell, searchCell,
                    _abstractGraph.Count / 8))
                {
                    var searched = search.ExpandAll();
                    foreach (var abstractCell in searched)
                        _abstractDomains.Add(abstractCell, domain);
                    abstractCells.ExceptWith(searched);
                }
                domain++;
            }
        }

        // ── 网格索引工具 ──

        private int GridIndex(IsoCoord cellInGrid)
            => (cellInGrid.Y - _mapBounds.TopLeft.Y) / GridSize * _gridXs +
               (cellInGrid.X - _mapBounds.TopLeft.X) / GridSize;

        private IsoCoord GetGridTopLeft(int gridIndex)
            => new(
                gridIndex % _gridXs * GridSize + _mapBounds.TopLeft.X,
                gridIndex / _gridXs * GridSize + _mapBounds.TopLeft.Y);

        private static IsoCoord GetGridTopLeft(IsoCoord cellInGrid, Grid mapBounds)
            => new(
                (cellInGrid.X - mapBounds.TopLeft.X) / GridSize * GridSize + mapBounds.TopLeft.X,
                (cellInGrid.Y - mapBounds.TopLeft.Y) / GridSize * GridSize + mapBounds.TopLeft.Y);

        private static Grid GetGrid(IsoCoord cellInGrid, Grid mapBounds)
        {
            var gridTopLeft = GetGridTopLeft(cellInGrid, mapBounds);
            int width = Math.Min(mapBounds.BottomRight.X - gridTopLeft.X, GridSize);
            int height = Math.Min(mapBounds.BottomRight.Y - gridTopLeft.Y, GridSize);
            return new Grid(gridTopLeft,
                new IsoCoord(gridTopLeft.X + width, gridTopLeft.Y + height));
        }

        private static int DivideRoundUp(int value, int divisor)
            => (value + divisor - 1) / divisor;

        /// <summary>确定性选择集合中的最小格子（先 Y 后 X）。</summary>
        private static IsoCoord MinCell(HashSet<IsoCoord> cells)
        {
            var min = default(IsoCoord);
            bool first = true;
            foreach (var cell in cells)
            {
                if (first || cell.Y < min.Y ||
                    (cell.Y == min.Y && cell.X < min.X))
                {
                    min = cell;
                    first = false;
                }
            }
            return min;
        }

        // ── 抽象节点映射 ──

        /// <summary>
        /// 把本地格子映射到抽象节点。不可达时返回 null。
        /// </summary>
        private IsoCoord? AbstractCellForLocalCell(IsoCoord localCell)
            => _gridInfos[GridIndex(localCell)].AbstractCellForLocalCell(localCell, this);

        /// <summary>
        /// 把本地格子映射到抽象节点（跳过可达性检查，调用者已确认）。
        /// </summary>
        private IsoCoord? AbstractCellForLocalCellNoAccessibleCheck(IsoCoord localCell)
            => _gridInfos[GridIndex(localCell)].AbstractCellForLocalCell(localCell, null);

        /// <summary>
        /// 创建本地格到抽象格的边。两格相同时返回 null（不需要边）。
        /// </summary>
        private static GraphEdge? EdgeFromLocalToAbstract(IsoCoord localCell,
            IsoCoord abstractCell)
        {
            if (localCell == abstractCell)
                return null;
            return new GraphEdge(localCell, abstractCell);
        }

        // ── 启发式 ──

        /// <summary>
        /// 用抽象搜索的结果为精细搜索提供启发式。
        /// 抽象搜索必须与精细搜索方向相反：
        /// 精细搜索 source→target 时，抽象搜索必须是 target→source。
        /// </summary>
        private Func<IsoCoord, bool, int> Heuristic(PathSearch abstractSearch,
            int estimatedSearchSize, HashSet<IsoCoord> sources,
            List<IsoCoord> unpathableNodes)
        {
            var nodeForCostLookup = new Dictionary<IsoCoord, IsoCoord>(estimatedSearchSize);
            var graph = (SparsePathGraph)abstractSearch.Graph;

            return (cell, knownAccessible) =>
            {
                // 不可达起点的相邻格可能属于与目标断开的区域，提前排除
                if (unpathableNodes != null && unpathableNodes.Contains(cell))
                    return PathCost.InvalidPath;

                // 搜索过程中经启发式检查的其他格子保证可达；
                // 作为初始起点的格子可能不可达，需做可达性检查。
                IsoCoord? maybeAbstractCell;
                if (knownAccessible)
                    maybeAbstractCell = AbstractCellForLocalCellNoAccessibleCheck(cell);
                else
                    maybeAbstractCell = AbstractCellForLocalCell(cell);

                if (maybeAbstractCell == null)
                {
                    // 起点不可达时，改用其相邻可达格
                    if (sources != null && sources.Contains(cell))
                    {
                        foreach (var dir in Directions)
                        {
                            var adjacentCell = cell + dir;
                            if (!MovementAllowedBetweenCells(cell, adjacentCell) ||
                                (unpathableNodes != null &&
                                 unpathableNodes.Contains(adjacentCell)))
                                continue;

                            maybeAbstractCell = AbstractCellForLocalCell(adjacentCell);
                            if (maybeAbstractCell != null)
                                break;
                        }
                    }

                    if (maybeAbstractCell == null)
                        return PathCost.InvalidPath;
                }

                var abstractCell = maybeAbstractCell.Value;
                var info = graph[abstractCell];

                // 还没有到达该抽象节点的路线时，增量展开抽象搜索
                if (info.Status != CellStatus.Closed)
                {
                    abstractSearch.TargetPredicate = c => c == abstractCell;
                    if (!abstractSearch.ExpandToTarget())
                        return PathCost.InvalidPath;
                    info = graph[abstractCell];
                }

                var abstractNode = info.PreviousNode;

                // 尝试使用路径上更远处可直达的抽象节点，获得更好的估算。
                // 结果较昂贵，缓存起来。
                if (!nodeForCostLookup.TryGetValue(abstractNode, out var abstractNodeForCost))
                {
                    abstractNodeForCost = AbstractNodeForCost(graph, abstractCell, abstractNode);
                    nodeForCostLookup.Add(abstractNode, abstractNodeForCost);
                }

                return graph[abstractNodeForCost].CostSoFar +
                    _costEstimator(cell, abstractNodeForCost);
            };
        }

        /// <summary>
        /// 找到抽象路径上更远处、且能不偏离抽象路径边界直达的节点。
        /// 直达该节点可以得到更好的代价估算，同时避免单位
        /// 为了"上高速"而绕路：只有抽象路径转向时才提前汇入。
        /// </summary>
        private IsoCoord AbstractNodeForCost(SparsePathGraph graph,
            IsoCoord abstractCell, IsoCoord abstractNode)
        {
            var abstractNodesAlongPath = new List<IsoCoord>();
            while (true)
            {
                var previousAbstractNode = graph[abstractNode].PreviousNode;

                // 整条抽象路径已走完，无法更远
                if (previousAbstractNode == abstractNode)
                    break;

                // 检查能否直达新节点，同时保持在已走过抽象路径的边界内：
                // 路径上每个节点的网格区域都必须与直达线段相交
                bool intersectsAllNodes = true;
                abstractNodesAlongPath.Add(abstractNode);
                foreach (var node in abstractNodesAlongPath)
                {
                    if (!GetGrid(node, _mapBounds)
                        .IntersectsLine(abstractCell, previousAbstractNode))
                    {
                        intersectsAllNodes = false;
                        break;
                    }
                }

                if (!intersectsAllNodes)
                    break;

                abstractNode = previousAbstractNode;
            }

            return abstractNode;
        }
    }
}
