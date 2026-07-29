# FNA_RTS 寻路系统重设计文档

## 1. 概述

本文档描述 FNA_RTS 寻路系统的完整重设计方案。旧系统是一个简单的 A* 实现（`Pathfinder` + `PathNode` + `PathSmoother`），存在以下问题：

- **无图抽象层**：搜索算法与网格代价计算硬耦合，无法复用于不同搜索空间
- **无地形代价**：只有可通过/不可通过两种状态，无法表达不同地形的移动代价差异
- **无分层寻路**：大型地图上绕障效率低，启发式函数无法感知全局地形障碍
- **无双向搜索**：长距离路径搜索开销大
- **无对象池**：每次寻路都分配大量临时对象，GC 压力高
- **路径平滑为独立模块**：与搜索过程脱耦

新方案参照 OpenRA 的寻路架构（`OpenRA.Mods.Common.Pathfinder`），提取其核心设计模式并适配到 FNA_RTS 的规模和架构约束中。

### 1.1 设计目标

| 目标 | 说明 |
|------|------|
| 图抽象分层 | 搜索算法 (`PathSearch`) 只依赖 `IPathGraph` 接口，不关心底层是网格还是抽象图 |
| 地形代价支持 | 不同 TileType 具有不同移动代价，支持单位类型对地形的差异化通行能力 |
| 分层寻路 (HPA*) | 将地图划分为粗粒度网格，构建抽象图，用抽象路径引导精细搜索的启发式 |
| 双向搜索 | 支持从起点和终点同时搜索，减少搜索空间 |
| 对象池化 | CellInfo 层使用对象池，避免高频 GC |
| 局部搜索优化 | 近距离路径使用限定区域的 GridPathGraph，减少搜索空间 |
| 确定性 | 所有数据结构保证确定性行为，为未来帧同步网络做准备 |
| Core 层纯净 | 不依赖 FNA，通过委托注入地形代价和可通过性 |

### 1.2 等距投影与寻路网格的关系（2.5D 场景适配）

FNA_RTS 是 2.5D 等距视角，屏幕上的东西南北是斜向的。**寻路算法不需要为此做任何适配**，原因如下：

- OpenRA 的寻路在 `CPos` **逻辑网格**空间运行，不是屏幕空间。OpenRA 自身支持 `MapGridType.RectangularIsometric`，其投影变换 `u=(x-y)/2, v=(x+y)/2` 与 FNA_RTS 的 `CoordUtil.IsoToWorld` 是同一数学变换。
- 因此 OpenRA 的 `CPos` 等价于 FNA_RTS 的 `IsoCoord`，算法天然兼容。
- 8 方向逻辑偏移投影到屏幕后变为斜向（(+1,0)=屏幕右上，(+1,-1)=屏幕正下），但这只影响渲染，不影响寻路。方向转换只发生在两个边界点：`WorldToIso`（输入）和 `IsoToWorldCenter`（输出航点），两者已存在于 CoordUtil。
- Octile 启发式与对角线切角检测在逻辑空间中依然正确，因为逻辑距离（1 格 / √2 格）不受投影影响。

**需要注意的两个适配点**：

1. **菱形可玩区域**：地图是菱形（`|gx-cx|+|gy-cy| ≤ R`）但存储在矩形数组中。菱形外的格子通过 `TerrainCostProvider` 的 passability 委托自然拒绝（与旧系统一致）。
2. **HPF 抽象网格边缘**：沿用 OpenRA `Grid.cs` 的设计取舍——抽象网格按矩形切分，落在菱形外（地图外）的格子因不可通过而不参与连通域计算。矩形 Grid 边缘平直，相邻网格逻辑简单（OpenRA 在 RectangularIsometric 下即如此处理，见 `HierarchicalPathFinder.GetCPosBounds`）。

### 1.3 OpenRA 架构参考摘要

OpenRA 寻路系统由以下核心组件构成：

| 组件 | 职责 |
|------|------|
| `CellInfo` | 搜索节点信息：状态(Unvisited/Open/Closed)、累积代价、估算总代价、前驱节点 |
| `IPathGraph` | 图抽象接口：获取邻接连接 + 读写 CellInfo |
| `DensePathGraph` | 密集网格图抽象基类：方向邻居优化、地形代价计算、Lane Bias |
| `GridPathGraph` | 限定区域的密集图（ flat array 存储 CellInfo）|
| `MapPathGraph` | 全地图密集图（池化 CellInfo 层）|
| `SparsePathGraph` | 稀疏图（Dictionary 存储，用于抽象图搜索）|
| `PathSearch` | A* 搜索引擎：启发式权重、双向搜索、增量展开 |
| `HierarchicalPathFinder` | 分层寻路：抽象图构建、域检测、启发式引导 |
| `CellInfoLayerPool` | CellInfo 层对象池 |
| `Grid` | 矩形区域描述，用于限定搜索范围 |

---

## 2. 架构总览

```
┌─────────────────────────────────────────────────────────────┐
│                    PathfindingFacade                         │
│         (对外统一接口，替代旧 Pathfinder 类)                   │
│                                                             │
│  FindPath(start, end, unitType)  → List<IsoCoord>           │
│  FindPath(sources, target, unitType) → List<IsoCoord>       │
│  PathExists(start, end, unitType) → bool                    │
└──────────────────┬──────────────────────────────────────────┘
                   │
    ┌──────────────┼──────────────────────┐
    │              ▼                      │
    │  ┌───────────────────────┐          │
    │  │ HierarchicalPathFinder│  ← 长距离 │
    │  │   (分层寻路)           │          │
    │  └───────────┬───────────┘          │
    │              │                      │
    │    ┌─────────┼──────────┐           │
    │    ▼         ▼          ▼           │
    │ ┌──────┐ ┌───────┐ ┌──────┐        │
    │ │Grid  │ │Map    │ │Sparse│        │
    │ │Path  │ │Path   │ │Path  │        │
    │ │Graph │ │Graph  │ │Graph │        │
    │ └──┬───┘ └──┬────┘ └──┬───┘        │
    │    │        │         │             │
    │    ▼        ▼         ▼             │
    │ ┌─────────────────────────────┐     │
    │ │       IPathGraph            │     │
    │ │  GetConnections / CellInfo  │     │
    │ └─────────────┬───────────────┘     │
    │               │                     │
    │    ┌──────────▼──────────┐          │
    │    │     PathSearch      │          │
    │    │  (A* 搜索引擎)       │          │
    │    │  - Unidirectional   │          │
    │    │  - Bidirectional    │          │
    │    │  - ExpandToTarget   │          │
    │    │  - ExpandAll        │          │
    │    └─────────────────────┘          │
    │                                     │
    │  ┌──────────────────────────┐       │
    │  │   TerrainCostProvider    │       │
    │  │  (地形代价 + 可通过性)     │       │
    │  └──────────────────────────┘       │
    └─────────────────────────────────────┘
```

---

## 3. 核心类型设计

### 3.1 CellStatus & CellInfo

**对应 OpenRA**: `CellInfo.cs`

```csharp
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
        public readonly IsoCoord PreviousNode;     // 路径回溯用的前驱节点

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
```

**设计要点**：
- `readonly struct` 保证不可变性，避免意外修改
- `default(CellInfo)` 的 Status = Unvisited（byte 默认 0），不需要显式初始化整个地图的 CellInfo 数组
- 相比旧 `PathNode`，增加了 `CellStatus` 状态机，让搜索算法可以更精确地控制节点生命周期

### 3.2 PathCost 常量 & GraphConnection

**对应 OpenRA**: `IPathGraph.cs` 中的 `PathGraph`, `GraphConnection`, `GraphEdge`

```csharp
namespace FNARTS.Core.Pathfinding
{
    /// <summary>路径图常量。</summary>
    public static class PathCost
    {
        /// <summary>无效路径的代价（int.MaxValue）。</summary>
        public const int InvalidPath = int.MaxValue;

        /// <summary>不可到达的移动代价（short.MaxValue）。</summary>
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
}
```

**设计要点**：
- 用 `GraphConnection` 替代旧系统中直接遍历方向数组的方式，让图结构对搜索算法透明
- `PathCost.InvalidPath` 和 `PathCost.UnreachableCell` 分离了"路径无效"和"单元格不可达"两个概念

### 3.3 IPathGraph — 图抽象接口

**对应 OpenRA**: `IPathGraph.cs`

```csharp
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
        /// <param name="targetPredicate">用于判定目标节点（反向搜索时允许进入不可达目标）。</param>
        List<GraphConnection> GetConnections(IsoCoord source,
            Func<IsoCoord, bool> targetPredicate);

        /// <summary>读写节点的搜索信息。</summary>
        CellInfo this[IsoCoord node] { get; set; }
    }
}
```

**设计要点**：
- 这是整个架构的核心抽象——搜索算法与图结构完全解耦
- `GetConnections` 返回 `List<GraphConnection>` 而非 `IEnumerable`，避免枚举器分配（性能关键路径）
- `IDisposable` 允许图在搜索结束后归还池化的资源

### 3.4 TerrainCostProvider — 地形代价提供者

**对应 OpenRA**: `Locomotor` 的 `MovementCostToEnterCell` 方法（简化版）

```csharp
namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// 提供地形移动代价和可通过性判定。
    /// Core 层通过委托注入，不持有 TileMap 或 EntityManager 引用。
    /// </summary>
    public class TerrainCostProvider
    {
        /// <summary>
        /// 获取进入目标格子的移动代价。
        /// 返回 PathCost.UnreachableCell 表示不可进入。
        /// </summary>
        public Func<IsoCoord, IsoCoord, short> MovementCost { get; set; }

        /// <summary>
        /// 快速判定格子是否完全不可通行（地形级别）。
        /// 不考虑动态障碍物（单位/建筑），仅检查地形类型。
        /// </summary>
        public Func<IsoCoord, bool> IsTerrainPassable { get; set; }
        
                /// <summary>
                /// 判定格子是否可进入（地形 + 动态实体障碍）。
                /// 用于反向搜索的源点校验：反向搜索的源点实际是路径终点，
                /// 终点必须真正可进入。
                /// </summary>
                public Func<IsoCoord, bool> IsPassable { get; set; }

        /// <summary>地图宽度。</summary>
        public int MapWidth { get; set; }

        /// <summary>地图高度。</summary>
        public int MapHeight { get; set; }

        /// <summary>
        /// 创建默认的地形代价提供者。
        /// 默认地形代价表：Grass=10, 其他不可通过。
        /// </summary>
        public static TerrainCostProvider CreateDefault(int mapWidth, int mapHeight,
            Func<IsoCoord, TileType> getTileType,
            Func<IsoCoord, bool> isBlockedByEntity)
        {
            var provider = new TerrainCostProvider
            {
                MapWidth = mapWidth,
                MapHeight = mapHeight,
            };

            provider.MovementCost = (from, to) =>
            {
                if (!InBounds(to, mapWidth, mapHeight))
                    return PathCost.UnreachableCell;

                var tileType = getTileType(to);
                short terrainCost = tileType switch
                {
                    TileType.Grass => 10,
                    TileType.Water => PathCost.UnreachableCell,
                    TileType.Cliff => PathCost.UnreachableCell,
                    TileType.Impassable => PathCost.UnreachableCell,
                    _ => PathCost.UnreachableCell
                };

                if (terrainCost == PathCost.UnreachableCell)
                    return PathCost.UnreachableCell;

                // 动态障碍物检查（建筑、单位）
                if (isBlockedByEntity(to))
                    return PathCost.UnreachableCell;

                return terrainCost;
            };

            provider.IsTerrainPassable = coord =>
            {
                if (!InBounds(coord, mapWidth, mapHeight))
                    return false;
                var tileType = getTileType(coord);
                return tileType == TileType.Grass;
            };

            return provider;
        }

        private static bool InBounds(IsoCoord c, int w, int h)
            => (uint)c.X < (uint)w && (uint)c.Y < (uint)h;
    }
}
```

**设计要点**：
- 替代旧系统的 `Func<IsoCoord, bool> IsPassable`，增加了移动代价概念
- `MovementCost(from, to)` 签名支持方向相关代价（如坡面、高度差）
- 未来可扩展为不同单位类型有不同地形通行能力（如飞行器忽略地形）

### 3.5 Grid — 搜索区域描述

**对应 OpenRA**: `Grid.cs`

```csharp
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

        public override string ToString() => $"{TopLeft} -> {BottomRight}";
    }
}
```

---

## 4. 图实现

### 4.1 DensePathGraph — 密集网格图基类

**对应 OpenRA**: `DensePathGraph.cs`

密集网格图的核心优化是 **方向邻居裁剪**（Directed Neighbors）：根据当前节点相对于前驱节点的移动方向，排除那些"通过前驱节点可达且更便宜"的邻居，减少无效检查。

```csharp
namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// 密集网格路径图的抽象基类。
    /// 负责邻居枚举、方向裁剪、对角线代价计算、自定义代价叠加。
    /// 派生类提供 CellInfo 的存储方式。
    /// </summary>
    abstract class DensePathGraph : IPathGraph
    {
        // 车道偏移代价（用于减少面对面碰撞的视觉问题）
        private const int LaneBiasCost = 1;

        private readonly TerrainCostProvider _terrain;
        private readonly Func<IsoCoord, int> _customCost;
        private readonly bool _laneBias;
        private readonly bool _inReverse;

        /// <summary>
        /// 根据移动方向裁剪的邻居集合。
        /// 例如从左向右移动时，不需要检查左边的邻居（前驱节点已经覆盖了它们）。
        /// 
        /// 8 个方向索引: (dy+1)*3 + (dx+1) - 1 = dy*3 + dx + 4
        /// 方向枚举: TL=0, T=1, TR=2, L=3, Center=4, R=5, BL=6, B=7, BR=8
        /// </summary>
        private static readonly IsoCoord[][] DirectedNeighbors = new[]
        {
            // TL (来向)：排除 BR 方向的前驱 → 检查除 BR 外的所有 + 额外
            new[] { C(-1,-1), C(0,-1), C(1,-1), C(-1,0), C(-1,1) },
            // T
            new[] { C(-1,-1), C(0,-1), C(1,-1) },
            // TR
            new[] { C(-1,-1), C(0,-1), C(1,-1), C(1,0), C(1,1) },
            // L
            new[] { C(-1,-1), C(-1,0), C(-1,1) },
            // Center (起点无前驱): 所有 8 方向
            AllDirections(),
            // R
            new[] { C(1,-1), C(1,0), C(1,1) },
            // BL
            new[] { C(-1,-1), C(-1,0), C(-1,1), C(0,1), C(1,1) },
            // B
            new[] { C(-1,1), C(0,1), C(1,1) },
            // BR
            new[] { C(1,-1), C(1,0), C(-1,1), C(0,1), C(1,1) },
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

            // 计算移动方向索引
            int dx = position.X - previousNode.X;
            int dy = position.Y - previousNode.Y;
            int index = dy * 3 + dx + 4;

            // 裁剪方向索引到合法范围
            index = Math.Clamp(index, 0, DirectedNeighbors.Length - 1);
            var directions = DirectedNeighbors[index];

            var result = new List<GraphConnection>(directions.Length);

            for (int i = 0; i < directions.Length; i++)
            {
                var dir = directions[i];
                var neighbor = new IsoCoord(position.X + dir.X, position.Y + dir.Y);

                if (!IsValidNeighbor(neighbor))
                    continue;

                var pathCost = GetPathCostToNode(position, neighbor, dir, targetPredicate);
                if (pathCost != PathCost.InvalidPath &&
                    this[neighbor].Status != CellStatus.Closed)
                {
                    result.Add(new GraphConnection(neighbor, pathCost));
                }
            }

            return result;
        }

        private int GetPathCostToNode(IsoCoord src, IsoCoord dest,
            IsoCoord direction, Func<IsoCoord, bool> targetPredicate)
        {
            var movementCost = _terrain.MovementCost(src, dest);

            // 反向搜索时允许进入不可达的目标位置
            if (movementCost == PathCost.UnreachableCell &&
                _inReverse && targetPredicate(dest))
                movementCost = 0;

            if (movementCost == PathCost.UnreachableCell)
                return PathCost.InvalidPath;

            return CalculateCellPathCost(dest, direction, movementCost);
        }

        private int CalculateCellPathCost(IsoCoord neighbor,
            IsoCoord direction, short movementCost)
        {
            // 对角线移动代价 = movementCost × √2
            int cellCost = direction.X * direction.Y != 0
                ? MultiplyBySqrtTwo(movementCost)
                : movementCost;

            // 自定义代价叠加（如威胁区域、建筑附近惩罚）
            if (_customCost != null)
            {
                int customCellCost = _customCost(neighbor);
                if (customCellCost == PathCost.InvalidPath)
                    return PathCost.InvalidPath;
                cellCost += customCellCost;
            }

            // 车道偏移：减少面对面移动的视觉碰撞
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

        /// <summary>整数乘以√2的近似：value × 1414 / 1000。</summary>
        private static int MultiplyBySqrtTwo(int value)
            => value * 1414 / 1000;

        protected virtual void Dispose(bool disposing) { }
        public void Dispose() => Dispose(true);

        private static IsoCoord C(int x, int y) => new(x, y);

        private static IsoCoord[] AllDirections() => new[]
        {
            C(-1,-1), C(0,-1), C(1,-1),
            C(-1, 0),          C(1, 0),
            C(-1, 1), C(0, 1), C(1, 1),
        };
    }
}
```

**方向邻居裁剪原理**：

```
前驱节点在左边 (→ 方向移动)：
  P = 前驱    C = 当前    ? = 需要检查的邻居

  ?  ?  ?
  P  C  ?     ← 左边3个邻居已由 P 覆盖，无需再检查
  ?  ?  ?

  只需检查前方3个: (C右上, C右, C右下)
```

这个优化将每个节点的平均邻居检查数从 8 降低到约 3-5，显著减少 `MovementCost` 调用次数。

### 4.2 GridPathGraph — 限定区域搜索图

**对应 OpenRA**: `GridPathGraph.cs`

```csharp
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
```

**使用场景**：当起点和终点距离较近时（例如 < 20 格），限定搜索区域为起点终点包围盒 + 边距，避免搜索整个地图。

### 4.3 MapPathGraph — 全地图搜索图

**对应 OpenRA**: `MapPathGraph.cs` + `CellInfoLayerPool.cs`

```csharp
namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// CellInfo 层对象池。避免每次全地图搜索时分配大块内存。
    /// 池中的 CellInfo[] 在归还时清零（利用 default(CellInfo) = Unvisited）。
    /// </summary>
    sealed class CellInfoLayerPool
    {
        private const int MaxPoolSize = 4;
        private readonly object _lock = new();
        private readonly Stack<CellInfo[]> _pool = new(MaxPoolSize);
        private readonly int _layerSize;

        public CellInfoLayerPool(int mapWidth, int mapHeight)
        {
            _layerSize = mapWidth * mapHeight;
        }

        public PooledLayer Get()
        {
            CellInfo[] layer;
            lock (_lock)
            {
                layer = _pool.Count > 0 ? _pool.Pop() : null;
            }

            layer ??= new CellInfo[_layerSize];
            Array.Clear(layer, 0, layer.Length);  // 重置为 Unvisited
            return new PooledLayer(this, layer);
        }

        private void Return(CellInfo[] layer)
        {
            lock (_lock)
            {
                if (_pool.Count < MaxPoolSize)
                    _pool.Push(layer);
            }
        }

        /// <summary>池化的 CellInfo 层，使用后自动归还。</summary>
        public sealed class PooledLayer : IDisposable
        {
            private CellInfoLayerPool _pool;
            public CellInfo[] Data { get; private set; }

            internal PooledLayer(CellInfoLayerPool pool, CellInfo[] data)
            {
                _pool = pool;
                Data = data;
            }

            public void Dispose()
            {
                if (_pool != null && Data != null)
                {
                    _pool.Return(Data);
                    Data = null;
                    _pool = null;
                }
            }
        }
    }

    /// <summary>
    /// 全地图范围的密集路径图。使用池化的 CellInfo 层。
    /// </summary>
    sealed class MapPathGraph : DensePathGraph
    {
        private readonly CellInfoLayerPool.PooledLayer _pooledLayer;
        private readonly int _mapWidth;

        public MapPathGraph(CellInfoLayerPool layerPool,
            TerrainCostProvider terrain,
            Func<IsoCoord, int> customCost, bool laneBias, bool inReverse)
            : base(terrain, customCost, laneBias, inReverse)
        {
            _pooledLayer = layerPool.Get();
            _mapWidth = terrain.MapWidth;
        }

        public override CellInfo this[IsoCoord pos]
        {
            get => _pooledLayer.Data[pos.Y * _mapWidth + pos.X];
            set => _pooledLayer.Data[pos.Y * _mapWidth + pos.X] = value;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _pooledLayer.Dispose();
            base.Dispose(disposing);
        }
    }
}
```

### 4.4 SparsePathGraph — 稀疏图

**对应 OpenRA**: `SparsePathGraph.cs`

```csharp
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
```

---

## 5. PathSearch — A* 搜索引擎

**对应 OpenRA**: `PathSearch.cs`

这是整个系统的核心搜索引擎，负责在任意 `IPathGraph` 上执行 A* 搜索。

### 5.1 核心 API

```csharp
namespace FNARTS.Core.Pathfinding
{
    public sealed class PathSearch : IDisposable
    {
        /// <summary>搜索过程的记录器接口（用于调试可视化）。</summary>
        public interface IRecorder
        {
            void Add(IsoCoord source, IsoCoord destination,
                int costSoFar, int estimatedRemainingCost);
        }

        public IPathGraph Graph { get; }
        public Func<IsoCoord, bool> TargetPredicate { get; set; }

        // ── 工厂方法 ──

        /// <summary>
        /// 创建朝向目标格子的搜索。
        /// </summary>
        /// <param name="graph">搜索图。</param>
        /// <param name="heuristic">启发式函数：(cell, knownAccessible) → 估算代价。</param>
        /// <param name="heuristicWeightPercentage">
        ///   启发式权重百分比。100 = 最短路径，>100 = 允许次优但更快。
        ///   例如 110 表示路径最长不超过最优路径的 110%。
        /// </param>
        /// <param name="targetPredicate">目标判定。</param>
        public static PathSearch Create(IPathGraph graph,
            Func<IsoCoord, bool, int> heuristic,
            int heuristicWeightPercentage,
            Func<IsoCoord, bool> targetPredicate,
            IRecorder recorder = null);

        /// <summary>
        /// 创建朝向目标格子的搜索，自动选择图类型（Grid 或 Map）。
        /// </summary>
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
            IRecorder recorder = null);

        /// <summary>
        /// 在稀疏图（抽象图）上执行搜索。
        /// </summary>
        public static PathSearch ToTargetCellOverGraph(
            Func<IsoCoord, List<GraphConnection>> edges,
            TerrainCostProvider terrain,
            IsoCoord from, IsoCoord target,
            int estimatedSearchSize = 0,
            IRecorder recorder = null);

        // ── 搜索操作 ──

        /// <summary>展开搜索直到找到目标，返回是否找到。</summary>
        public bool ExpandToTarget();

        /// <summary>展开搜索覆盖整个可达空间，返回访问过的所有节点。</summary>
        public List<IsoCoord> ExpandAll();

        /// <summary>展开搜索直到找到路径，返回路径（反向：目标→起点）。</summary>
        public List<IsoCoord> FindPath();

        /// <summary>
        /// 双向搜索：两个搜索交替展开直到交汇。
        /// 返回从 first 起点到 second 起点的完整路径。
        /// </summary>
        public static List<IsoCoord> FindBidiPath(
            PathSearch first, PathSearch second);

        // ── 启发式 ──

        /// <summary>
        /// 默认启发式：Octile Distance。
        /// 基于最小地形代价计算，保证 admissible。
        /// </summary>
        public static Func<IsoCoord, IsoCoord, int> DefaultCostEstimator(
            TerrainCostProvider terrain);

        public void Dispose();
    }
}
```

### 5.2 搜索核心逻辑

```
PathSearch 内部核心流程:

  openQueue = PriorityQueue<GraphConnection>  (按 EstimatedTotalCost 排序)

  AddInitialCell(location):
    h = heuristic(location, knownAccessible=false)
    Graph[location] = CellInfo(Open, initialCost, initialCost + h*weight/100, location)
    openQueue.Add(GraphConnection(location, h*weight/100))

  CanExpand():
    // 弹出已 Closed 的节点（低代价路径已处理过）
    while !openQueue.Empty && Graph[openQueue.Peek()].Status == Closed:
      openQueue.Pop()
    return !openQueue.Empty

  Expand():
    current = openQueue.Pop().Destination
    info = Graph[current]
    Graph[current] = CellInfo(Closed, info.CostSoFar, info.EstimatedTotalCost, info.PreviousNode)

    for each connection in Graph.GetConnections(current, TargetPredicate):
      costSoFarToNeighbor = info.CostSoFar + connection.Cost
      neighborInfo = Graph[connection.Destination]

      if neighborInfo.Status == Closed ||
         (neighborInfo.Status == Open && costSoFarToNeighbor >= neighborInfo.CostSoFar):
        continue  // 已有更好的路径

      if neighborInfo.Status == Open:
        // 重用之前计算的启发式值
        h = neighborInfo.EstimatedTotalCost - neighborInfo.CostSoFar
      else:
        h = heuristic(neighbor, knownAccessible=true) * weight / 100
        if h == InvalidPath: continue

      Graph[neighbor] = CellInfo(Open, costSoFarToNeighbor, costSoFarToNeighbor + h, current)
      openQueue.Add(GraphConnection(neighbor, costSoFarToNeighbor + h))

  FindPath():
    while CanExpand():
      p = Expand()
      if TargetPredicate(p): return MakePath(Graph, p)
    return empty  // 无路径

  MakePath(graph, destination):
    path = []
    current = destination
    while graph[current].PreviousNode != current:
      path.Add(current)
      current = graph[current].PreviousNode
    path.Add(current)
    return path  // 反向：destination → source

  FindBidiPath(first, second):
    while first.CanExpand() && second.CanExpand():
      p = first.Expand()
      if second.Graph[p].Status == Closed:
        return MakeBidiPath(first, second, p)  // 交汇！
      q = second.Expand()
      if first.Graph[q].Status == Closed:
        return MakeBidiPath(first, second, q)
    return empty
```

### 5.3 启发式函数

**对应 OpenRA**: `PathSearch.DefaultCostEstimator`

```csharp
/// <summary>
/// Octile Distance 启发式。
/// 对 8 方向网格是 admissible（不高估）且 consistent 的。
/// 
/// h(a,b) = minCost × (|dx| + |dy|) + (√2×minCost − 2×minCost) × min(|dx|,|dy|)
/// 
/// 简化为：
/// h = minCost × straight + (diagCost − 2×minCost) × diag
/// 其中 straight = |dx|+|dy|, diag = min(|dx|,|dy|)
/// </summary>
public static Func<IsoCoord, IsoCoord, int> DefaultCostEstimator(
    TerrainCostProvider terrain)
{
    // 使用最小可能地形代价作为基数，保证 admissible
    const int minCellCost = 10;  // 当前只有 Grass=10
    int diagCost = MultiplyBySqrtTwo(minCellCost);

    return (here, destination) =>
    {
        int dx = Math.Abs(here.X - destination.X);
        int dy = Math.Abs(here.Y - destination.Y);
        int straight = dx + dy;
        int diag = Math.Min(dx, dy);
        return minCellCost * straight + (diagCost - 2 * minCellCost) * diag;
    };
}
```

---

## 6. HierarchicalPathFinder — 分层寻路

**对应 OpenRA**: `HierarchicalPathFinder.cs`

分层寻路是新系统相比旧系统最大的架构升级。核心思想是将地图划分为粗粒度网格（Grid），构建抽象图（Abstract Graph），在抽象图上快速找到粗略路线，然后用该路线引导精细 A* 搜索的启发式函数。

### 6.1 核心概念

```
原始地图 (51×51)                抽象图 (每 10×10 一个 Grid)
┌─────────────────┐           ┌─────┐
│ ░░░░░░░░░░░░░░░ │           │  A──B──C  │
│ ░░████████░░░░░ │           │  │     │  │
│ ░░████████░░░░░ │    →      │  D  E──F  │
│ ░░░░░░░░░░░░░░░ │           │  │     │  │
│ ░░░░░░░░░░░░░░░ │           │  G──H──I  │
└─────────────────┘           └─────┘

每个 Grid 内部通过 flood fill 确定连通区域：
- 一个连通区域 = 一个抽象节点
- 相邻 Grid 的边界连通 = 抽象边
- flood fill 的连通性使用"两端格子地形均可通行"的 8 邻接关系，
  不含对角线切角规则：切角规则不是可传递关系，用它划分会导致
  区域重叠（同一格子出现在多个区域）。粗化的区域划分不影响
  正确性，域判定和精细搜索仍基于真实移动规则。

搜索流程：
1. 在抽象图上搜索 → 得到抽象路径 (A→B→C→F→I)
2. 精细搜索时，启发式 = "到下一个抽象节点的距离 + 抽象路径剩余代价"
3. 精细搜索被引导沿抽象路径方向探索，避免盲目展开
```

### 6.2 类设计

```csharp
namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// 分层寻路器。维护地图的抽象图，提供高效的路径搜索。
    /// 
    /// 抽象图通过将地图划分为 GridSize×GridSize 的小网格构建。
    /// 每个网格内的连通区域用一个抽象节点表示。
    /// 相邻网格之间的边界连通性构成抽象边。
    /// </summary>
    public sealed class HierarchicalPathFinder
    {
        // 通过实验确定的最佳性能平衡点
        private const int GridSize = 10;

        private readonly TerrainCostProvider _terrain;
        private readonly Func<IsoCoord, int> _customCost;

        // 地图被划分成的网格数量和边界
        private Grid _mapBounds;
        private int _gridXs;
        private int _gridYs;

        // 每个网格的抽象信息
        private GridInfo[] _gridInfos;

        // 抽象图：抽象节点 → 邻接列表
        private Dictionary<IsoCoord, List<GraphConnection>> _abstractGraph;

        // 抽象域：抽象节点 → 域编号
        // 同一域内的节点互相可达，不同域之间不可达
        private Dictionary<IsoCoord, uint> _abstractDomains;

        // 脏网格索引：地形变化时标记需要重建的网格
        private HashSet<int> _dirtyGridIndexes;

        /// <summary>
        /// 内部结构：每个 Grid 的抽象信息。
        /// </summary>
        private readonly struct GridInfo
        {
            /// <summary>
            /// 如果整个 Grid 只有一个连通区域，存储该区域的抽象节点。
            /// 查找时不需要 Dictionary 查找，更快。
            /// </summary>
            public readonly IsoCoord? SingleAbstractCell;

            /// <summary>
            /// 如果有多个连通区域（被障碍分割），存储每个本地格子到其抽象节点的映射。
            /// </summary>
            public readonly Dictionary<IsoCoord, IsoCoord> LocalCellToAbstractCell;
        }

        public HierarchicalPathFinder(TerrainCostProvider terrain,
            Func<IsoCoord, int> customCost = null) { ... }

        // ── 公共 API ──

        /// <summary>
        /// 计算从 source 到 target 的路径。
        /// 近距离使用局部搜索，远距离使用分层搜索。
        /// </summary>
        public List<IsoCoord> FindPath(IsoCoord source, IsoCoord target,
            int heuristicWeightPercentage = 100,
            bool laneBias = true);

        /// <summary>
        /// 计算从多个候选起点到 target 的路径。
        /// </summary>
        public List<IsoCoord> FindPath(
            IReadOnlyCollection<IsoCoord> sources, IsoCoord target,
            int heuristicWeightPercentage = 100,
            bool laneBias = true);

        /// <summary>
        /// 快速判定两个位置之间是否存在路径（不实际计算路径）。
        /// 利用抽象域信息进行 O(1) 判定。
        /// </summary>
        public bool PathExists(IsoCoord source, IsoCoord target);

        /// <summary>
        /// 通知地形变化，标记需要重建的网格。
        /// </summary>
        public void NotifyTerrainChanged(IsoCoord cell);

        // ── 抽象图构建 ──

        /// <summary>将地图划分为 GridSize×GridSize 的网格。</summary>
        private void BuildGrids();

        /// <summary>
        /// 在单个网格内执行 flood fill，确定连通区域，
        /// 为每个连通区域创建一个抽象节点。
        /// </summary>
        private GridInfo BuildGrid(int gridX, int gridY);

        /// <summary>构建抽象图的所有边。</summary>
        private void BuildCostTable();

        /// <summary>
        /// 检查相邻网格边界格子的连通性，
        /// 建立抽象节点之间的边。
        /// </summary>
        private IEnumerable<KeyValuePair<IsoCoord, List<GraphConnection>>>
            GetAbstractEdgesForGrid(int gridX, int gridY);

        /// <summary>重建脏网格的抽象信息。</summary>
        private void RebuildDirtyGrids();

        /// <summary>
        /// 重建抽象域（连通分量）。
        /// 通过 flood fill 抽象图确定哪些抽象节点互相可达。
        /// 用于 PathExists 的 O(1) 查询和 FindPath 的快速失败。
        /// </summary>
        private void RebuildDomains();

        // ── 启发式 ──

        /// <summary>
        /// 创建基于抽象路径的启发式函数。
        /// 精细搜索时，启发式 = 到下一个抽象节点的距离 + 抽象路径剩余代价。
        /// 这比纯 Octile Distance 更精确，搜索更少节点。
        /// </summary>
        private Func<IsoCoord, bool, int> Heuristic(
            PathSearch abstractSearch, int estimatedSearchSize);
    }
}
```

### 6.3 搜索策略选择

```
FindPath(source, target):
  distance² = (target - source).LengthSquared

  if distance² < GridSize² × 4:    // 近距离 (< 20 格)
    // 使用 GridPathGraph 在限定区域内搜索
    // 启发式权重 = 100%（保证最短路径）
    grid = 包围(source, target) + GridSize/2 边距
    return PathSearch.ToTargetCell(..., grid: grid, weight: 100)

  // 远距离：分层搜索
  RebuildDirtyGrids()
  RebuildDomains()

  // 快速失败：不同域 = 不可达
  if domain(source) != domain(target):
    return empty

  // 1. 在抽象图上做双向搜索
  abstractForward = PathSearch.ToTargetCellOverGraph(source → target)
  abstractReverse = PathSearch.ToTargetCellOverGraph(target → source)

  // 2. 用反向抽象搜索的结果作为正向精细搜索的启发式
  // 3. 用正向抽象搜索的结果作为反向精细搜索的启发式
  localForward = PathSearch.ToTargetCell(source → target,
    heuristic: Heuristic(abstractReverse))
  localReverse = PathSearch.ToTargetCell(target → source,
    heuristic: Heuristic(abstractForward))

  return PathSearch.FindBidiPath(localForward, localReverse)
```

### 6.4 地形变化处理

```
NotifyTerrainChanged(cell):
  gridIndex = CellToGridIndex(cell)
  _dirtyGridIndexes.Add(gridIndex)

RebuildDirtyGrids():
  if _dirtyGridIndexes.Empty: return

  _abstractDomains.Clear()  // 域缓存失效

  foreach gridIndex in _dirtyGridIndexes:
    oldGrid = _gridInfos[gridIndex]

    // 重新构建该网格的抽象节点
    _gridInfos[gridIndex] = BuildGrid(gridX, gridY)

    // 更新该网格及其相邻网格的抽象边
    RebuildCostTable(gridX, gridY, oldGrid)

  _dirtyGridIndexes.Clear()
```

---

## 7. PathfindingFacade — 对外统一接口

替代旧的 `Pathfinder` 类，为外部调用者提供简洁统一的 API。

```csharp
namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// 寻路系统对外统一接口。
    /// 内部根据距离自动选择局部搜索或分层搜索。
    /// </summary>
    public class PathfindingFacade
    {
        private readonly HierarchicalPathFinder _hpf;
        private readonly CellInfoLayerPool _layerPool;
        private readonly TerrainCostProvider _terrain;

        public PathfindingFacade(TerrainCostProvider terrain,
            Func<IsoCoord, int> customCost = null)
        {
            _terrain = terrain;
            _layerPool = new CellInfoLayerPool(terrain.MapWidth, terrain.MapHeight);
            _hpf = new HierarchicalPathFinder(terrain, customCost);
        }

        /// <summary>
        /// 寻找从 start 到 end 的最短路径。
        /// 返回网格坐标列表（不含起点，含终点）。
        /// 无可达路径时返回空列表。
        /// </summary>
        public List<IsoCoord> FindPath(IsoCoord start, IsoCoord end)
            => _hpf.FindPath(start, end);

        /// <summary>
        /// 从多个候选起点寻找到 target 的路径。
        /// </summary>
        public List<IsoCoord> FindPath(
            IReadOnlyCollection<IsoCoord> sources, IsoCoord target)
            => _hpf.FindPath(sources, target);

        /// <summary>
        /// 快速判定两个位置之间是否存在路径。
        /// </summary>
        public bool PathExists(IsoCoord source, IsoCoord target)
            => _hpf.PathExists(source, target);

        /// <summary>
        /// 通知地形变化（建筑建造/摧毁等）。
        /// </summary>
        public void NotifyTerrainChanged(IsoCoord cell)
            => _hpf.NotifyTerrainChanged(cell);

        // ── 向后兼容旧 API ──

        /// <summary>地图宽度。</summary>
        public int MapWidth => _terrain.MapWidth;

        /// <summary>地图高度。</summary>
        public int MapHeight => _terrain.MapHeight;
    }
}
```

---

## 8. 文件结构

```
FNARTS.Core/Pathfinding/
├── CellInfo.cs                 ← CellStatus + CellInfo
├── PathCost.cs                 ← PathCost 常量 + GraphConnection
├── IPathGraph.cs               ← IPathGraph 接口
├── TerrainCostProvider.cs      ← 地形代价提供者
├── Grid.cs                     ← 搜索区域描述
├── DensePathGraph.cs           ← 密集网格图基类
├── GridPathGraph.cs            ← 限定区域搜索图
├── MapPathGraph.cs             ← 全地图搜索图 + CellInfoLayerPool
├── SparsePathGraph.cs          ← 稀疏图（抽象图用）
├── PathSearch.cs               ← A* 搜索引擎
├── HierarchicalPathFinder.cs   ← 分层寻路器
└── PathfindingFacade.cs        ← 对外统一接口

tests/FNARTS.Core.Tests/Pathfinding/
├── PathSearchTests.cs          ← A* 搜索引擎测试
├── DensePathGraphTests.cs      ← 密集图测试
├── HierarchicalPathFinderTests.cs ← 分层寻路测试
└── PathfindingFacadeTests.cs   ← 对外接口测试（含旧用例回归）
```

---

## 9. 与现有代码的集成

### 9.1 RTSGame.cs 修改

```csharp
// 旧代码：
_pathfinder = new Pathfinder
{
    MapWidth = _map.Width,
    MapHeight = _map.Height,
    IsPassable = coord =>
        _map.InBounds(coord) &&
        _map.IsPassable(coord) &&
        _entities.IsAreaFree(coord, 1, 1),
};

// 新代码：
var terrainProvider = TerrainCostProvider.CreateDefault(
    _map.Width, _map.Height,
    getTileType: coord => _map.GetTile(coord).Type,
    isBlockedByEntity: coord => !_entities.IsAreaFree(coord, 1, 1));

_pathfinder = new PathfindingFacade(terrainProvider);
```

### 9.2 调用方式不变

```csharp
// 所有现有调用点无需修改：
var path = _pathfinder.FindPath(start, end);

// GroupMovement.cs:
FormationPath = pathfinder.FindPath(centerStart, centerEnd);
var path = pathfinder.FindPath(start, end);

// CombatSystem.cs:
pursuer.Path = pathfinder.FindPath(start, end);
```

### 9.3 地形变化通知（新增）

```csharp
// 建筑建造/摧毁时通知寻路系统：
_pathfinder.NotifyTerrainChanged(buildingCoord);
```

### 9.4 单位进出格仲裁（MovementSystem，寻路系统之外）

寻路图只对**地形 + 建筑**可见；单位之间的相互阻挡由 `Core/Movement/MovementSystem.cs` 在每帧 `Unit.Update` 之前仲裁（OpenRA `Mobile` 进出格模型的移植）：

- 单位持有 `FromTile`/`ToTile` 双格占位（移动中同时占用两格），进入下一格前必须**预订**成功
- 被挡时按 OpenRA `PopPath` 阶梯处理：通知空闲友军（Nudge 让路）→ 等待（约 1s，随机抖动）→ 疏散延长 → 重寻路（冷却 0.5–0.8s 抖动）→ StepAside 打破死锁
- 飞行器豁免仲裁；对正在攻击的目标可贴身（近战豁免）
- 编队移动**默认关闭**：多选移动 = RA1 `FormMove=false` 语义（全员同一目标格），由仲裁在目标周围散开；`GroupMovement`（RA1 式相对偏移编队）保留但休眠，待 Ctrl+编队/队形开关实现后启用

原软推挤分离（`ApplySteeringSeparation`）已移除，不再有基于像素距离的推挤力。

---

## 10. 测试计划

### 10.1 PathSearchTests — 搜索引擎

| 测试 | 验证点 |
|------|--------|
| FindPath_StraightLine_Optimal | 无障碍直线：步数 = 八方向最优 |
| FindPath_Diagonal_Optimal | 无障碍对角线：步数 = min(dx,dy) |
| FindPath_ObstacleDetour | U 形障碍绕行，不穿墙 |
| FindPath_NoPath_EmptyList | 完全封闭返回空列表 |
| FindPath_StartEqualsEnd | 起点=终点返回空列表 |
| FindPath_OutOfBounds | 越界坐标返回空列表 |
| FindPath_DiagonalCutCorner | 对角线切角检测 |
| ExpandToTarget_ReturnsTrue | 找到目标返回 true |
| ExpandToTarget_ReturnsFalse | 无路径返回 false |
| ExpandAll_VisitsAllReachable | 覆盖所有可达节点 |
| FindBidiPath_MatchesUnidirectional | 双向结果代价与单向一致 |
| HeuristicWeight_FasterButSuboptimal | 权重 >100% 更快但路径可能次优 |

### 10.2 DensePathGraphTests — 图结构

| 测试 | 验证点 |
|------|--------|
| GetConnections_OpenGrid_8Directions | 开放网格返回最多 8 个连接 |
| GetConnections_DirectedNeighbors | 方向裁剪减少邻居检查数 |
| GetConnections_BlockedDiagonal | 对角线移动需要两个正交格可通过 |
| CalculateCellPathCost_Straight | 直线移动代价 = terrainCost |
| CalculateCellPathCost_Diagonal | 对角线代价 ≈ terrainCost × √2 |
| LaneBias_AddsPenalty | 启用 laneBias 时特定方向有额外代价 |

### 10.3 HierarchicalPathFinderTests — 分层寻路

| 测试 | 验证点 |
|------|--------|
| FindPath_LongDistance_MatchesSimple | 长距离结果与简单 A* 代价一致 |
| FindPath_AroundLake_UsesAbstractGuidance | 绕湖路径的搜索节点数 < 简单 A* |
| PathExists_SameDomain_True | 同一域返回 true |
| PathExists_DifferentDomain_False | 不同域返回 false |
| PathExists_AfterTerrainChange_Updates | 地形变化后域信息正确更新 |
| NotifyTerrainChanged_RebuildsGrid | 标记脏网格后正确重建 |
| FindPath_CloseDistance_UsesLocalSearch | 近距离使用 GridPathGraph |

### 10.4 PathfindingFacadeTests — 接口回归

| 测试 | 验证点 |
|------|--------|
| FindPath_StraightLine_Optimal | (旧测试回归) |
| FindPath_ObstacleDetour | (旧测试回归) |
| FindPath_NoPath_EmptyList | (旧测试回归) |
| FindPath_StartEqualsEnd | (旧测试回归) |
| FindPath_OutOfBounds | (旧测试回归) |
| FindPath_UnpassableEnd | (旧测试回归) |
| FindPath_DiagonalCutCorner | (旧测试回归) |
| FindPath_Deterministic | 多次寻路结果一致 |
| FindPath_Performance_51x51 | 51×51 最坏情况 < 5ms |
| FindPath_Performance_HPF_BetterThanSimple | HPF 在大地图上比简单 A* 更快 |

---

## 11. 实现优先级

| 优先级 | 组件 | 说明 |
|--------|------|------|
| P0 | CellInfo, PathCost, GraphConnection | 基础类型，无依赖 |
| P0 | IPathGraph, Grid | 接口和基础结构 |
| P0 | TerrainCostProvider | 地形代价注入 |
| P0 | DensePathGraph, GridPathGraph | 密集图 + 局部搜索 |
| P0 | PathSearch | A* 搜索引擎 |
| P0 | PathfindingFacade | 对外接口（先用 GridPathGraph + MapPathGraph） |
| P1 | CellInfoLayerPool, MapPathGraph | 全地图搜索 + 对象池 |
| P1 | SparsePathGraph | 稀疏图 |
| P2 | HierarchicalPathFinder | 分层寻路 |

**P0 完成后即可替换旧系统并恢复所有现有功能**。P1/P2 为性能优化，可逐步迭代。

---

## 12. 与旧系统的对比

| 维度 | 旧系统 | 新系统 |
|------|--------|--------|
| 搜索算法 | 硬编码 A* | PathSearch 引擎 + IPathGraph 抽象 |
| 图结构 | 无（内联在搜索中）| IPathGraph → Dense/Grid/Map/Sparse 4 种实现 |
| 地形代价 | 只有 passable/impassable | 任意整数代价，支持地形类型差异化 |
| 搜索范围 | 始终全地图 | 近距离 GridPathGraph，远距离 MapPathGraph |
| 启发式 | 固定 Octile Distance | 可插拔，支持抽象路径引导的精确启发式 |
| 双向搜索 | 不支持 | PathSearch.FindBidiPath |
| 分层寻路 | 不支持 | HierarchicalPathFinder |
| 对象池 | 无 | CellInfoLayerPool |
| 方向邻居裁剪 | 无（总是检查 8 方向）| DensePathGraph.DirectedNeighbors |
| 地形变化 | 无感知 | NotifyTerrainChanged + 脏网格重建 |
| 路径存在性查询 | 需要完整搜索 | 抽象域 O(1) 查询 |
| Lane Bias | 无 | 支持，减少面对面碰撞 |
| 启发式权重 | 固定 100% | 可配置，允许次优路径换取速度 |
| 确定性 | 是 | 是（保持帧同步兼容） |
