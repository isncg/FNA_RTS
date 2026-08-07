# FNA_RTS 步兵单位开发文档

## 1. 概述

FNA_RTS 目前的移动体系是 **C&C2 风格的一格一单位**模型：所有单位（包括士兵、弓手等步兵类
单位）都整格占用，移动目的地永远是地格中心，由 `MovementSystem` 做 OpenRA 风格的
FromTile/ToTile 预订仲裁。

在 RA2 中，**步兵比载具占据的空间小**：载具整格独占，而步兵只占据地格内的一个
**子格（SubCell）**位置，一个地格最多可以同时容纳多名步兵。玩家把一队步兵点到同一
位置时，他们会在该格及相邻格内自然散开，而不是像载具那样必须排到不同的格子上。

本文档分两部分：

1. **调研**：RA2（SAGE 引擎前作 TS/RA2 系）步兵的寻路与停靠机制，以及开源重制
   OpenRA 的对应实现；
2. **设计**：在 FNA_RTS 现有 `MovementSystem` 仲裁框架上引入子格模型，让步兵获得
   RA2 式的共格与停靠行为，同时保持载具行为完全不变。

设计目标：

| 目标 | 说明 |
|------|------|
| 子格共格 | 一个地格最多容纳 4 名步兵（RA2 含中心共 5 槽，在 2.5D 视角下偏拥挤，故去掉中心槽） |
| 载具不受影响 | 载具继续整格独占，一格一单位规则对载具保持现状 |
| 复用现有仲裁 | 预订/等待/Nudge/重寻路阶梯逻辑不变，只扩展占用粒度 |
| 寻路层零改动 | 寻路图继续只看地形+建筑；占用判定全部在 MovementSystem |
| 确定性 | 子格选择算法确定性（按固定顺序扫描），为帧同步保留基础 |
| 自由流动 | 步兵在途互不阻挡，大群移动不拥堵；停靠秩序由槽位分配解决 |

---

## 2. RA2 步兵移动机制调研

### 2.1 地格与子格模型

- RA2 的移动与寻路建立在**地格（Cell）网格**上。载具（`VehicleType`）整格占用，
  一格只能有一个载具，与本项目现状一致。
- 步兵（`InfantryType`）比载具小，占用的是地格内的**子格（SubCell）**位置。引擎为每个地格
  预留了 **5 个子格槽位**（OpenRA 默认 `MapGrid.SubCellOffsets` 索引 1..5：中心 + 四角，
  见 §2.3），因此一格最多可堆叠 5 名步兵。
- 地图格式的 `[Infantry]` 段显式带有 `SUB_CELL` 字段
  （`INDEX=OWNER,ID,HEALTH,X,Y,SUB_CELL,MISSION,FACING,...`），预置步兵就是按
  子格摆放的，这是子格模型存在的直接证据（ModEnc: *Infantry (maps)*）。
- 步兵与载具使用不同的地形通行表：步兵 `SpeedType=Foot`，载具 `SpeedType=Track/Wheel`
  （ModEnc: *SpeedType*）；步兵使用 `Locomotor=Walk`（ModEnc: *Locomotor*）。

### 2.2 步兵寻路

- **寻路本身仍在地格粒度上进行**：A* 搜索空间是地格网格，不是子格。子格只影响
  "这个格子能不能进" 的占用判定，不改变搜索图的形状。
- 占用判定的类别规则（RA2 原生行为，OpenRA 亦如实重建）：
  - 目标格有建筑 → 不可进入；
  - 目标格有**载具**（整格占用者）→ 步兵不可进入；
  - 目标格已有步兵但**还有空闲子格** → 可以进入；
  - 反过来，载具把任何有占用者的格子视为阻挡（载具不与其他单位共格），
    唯一例外是碾压者（见 2.4）。
- OpenRA 实现细节（文件位置见 §8）：
  - `LocomotorInfo.SharesCell`（"Allow multiple (infantry) units in one cell"）区分共格单位；
    RA mod 的步兵 locomotor `foot` 配置 `SharesCell: true`（`mods/ra/rules/world.yaml`）；
  - `ActorMap` 按地格维护占用链表 `InfluenceNode { SubCell, Actor }`：整格占用者
    记 `FullCell`，步兵记 1..5；
  - 阻挡匹配规则（`ActorMap.AnyActorsAt`）：查询**某个具体子格**时，"该子格的占用者
    + 所有 FullCell 占用者"都会命中——这就是"载具阻挡步兵"；子格占用者不命中其他
    子格——这就是"步兵共格"；以 `FullCell` 查询（载具）时命中**所有**占用者——这就是
    "步兵阻挡载具"；
  - `FreeSubCell(cell, preferred)`：先尝试偏好子格，再按 1..5 顺序扫描取第一个空闲
    槽位，占满返回 `Invalid`；
  - 瞬态占用豁免（`checkTransient`）：正在**离开**该格的占用者（`IsLeavingCell`：
    FromCell==该格、ToCell!=该格且正在移动）不阻挡下一个进入者；
  - 预订时机：`Move` 活动每走一步前在 `PopPath` 中做"可进入 + 选子格"
    （`(nextCell, GetAvailableSubCell(nextCell, FromSubCell))`），再由
    `Mobile.SetLocation` 把 From/To 格 + 子格写入占用表；被挡时走
    NotifyBlocker → 等待（WaitAverage±WaitSpread）→ 重寻路阶梯，与本项目
    `MovementSystem` 已实现的仲裁阶梯同源。

### 2.3 步兵停靠（停靠点选择）

- 载具停靠在地格**中心**；步兵停靠在其所属**子格的世界坐标**：
  `Mobile.SetPosition` 的停靠点 = `CenterOfCell + MapGrid.SubCellOffsets[subCell]`。
  OpenRA 默认子格偏移表（1 格 = 1024 世界单位，`OpenRA.Game/Map/MapGrid.cs`）：

  | 索引 | 位置 | 偏移 (x, y) |
  |------|------|-------------|
  | 0 | FullCell（载具/整格） | (0, 0) |
  | 1 | 左上 | (-299, -256) |
  | 2 | 右上 | (256, -256) |
  | 3 | **中心（DefaultSubCell）** | (0, 0) |
  | 4 | 左下 | (-299, 256) |
  | 5 | 右下 | (256, 256) |

  即每格最多容纳 **5 名步兵**（索引 1..5），默认停靠中心子格（索引 3）。
  子格枚举（`TraitsInterfaces.cs`）：`FullCell=0, First=1, Any=254, Invalid=255`。
- **子格偏好**：预订时 `PopPath` 以**当前所在子格（FromSubCell）**为偏好值，步兵
  连续移动时尽量保持原槽位；新单位默认 `DefaultSubCell`（中心），地图预置可用
  `SubCellInit` 指定（对应 RA2 地图格式的 `SUB_CELL` 字段）。
- `Locomotor=Walk` 的到达判定是"与目标点距离 < 17 leptons"（1 格 = 256 leptons），
  即步兵以子格点为最终停靠点做小半径吸附（ModEnc: *Locomotor*）。
- 给多名步兵下同一个移动命令时：先到的步兵认领目标格的空闲子格；格子占满后，
  后来的步兵散到相邻格。OpenRA 的移动命令本身还带"目的地不可达时在半径内找
  最近可进入格"的兜底（`Move` 活动 nearEnough=8 格、`NearestMoveableCell` 环形
  搜索半径 1..10）。散开是占用仲裁的自然结果，不需要编队系统。
- **瞬态占用优化**：步兵在途中只向占用表登记 **(ToCell, ToSubCell)**
  （`Mobile.OccupiedCells`；源码注明这是 HACK，载具仍登记 From/To 两格）——
  步兵一开始离开，原格立即空出给后续步兵进入，避免前后脚互相等待。
- 被其他单位挤占时，空闲步兵会被推开（`INotifyBlockingMove` → `Nudge`，
  对应本项目已实现的 Nudge）。

### 2.4 步兵与载具的交互

- **载具阻挡步兵**：整格占用对子格占用者是硬阻挡（§2.2 的匹配规则）。
- **碾压（Crush）**：
  - RA mod 的载具 locomotor（`tracked` / `heavywheeled` / `heavytracked`）配置
    `Crushes: wall, infantry, mine, crate`，而步兵自己的 `foot` locomotor 只有
    `Crushes: mine, crate`——即载具可压步兵，步兵不压步兵；
  - 步兵在 `^Infantry` 默认规则中携带 `Crushable` trait
    （`mods/ra/rules/defaults.yaml`）；
  - 碾压判定发生在入格/移动完成时（`Mobile.EnteringCell` WarnCrush /
    `FinishedMoving` OnCrush），对象是 **(ToCell, ToSubCell)** 上的占用者——
    只压同子格的步兵（FullCell 占用者也会被命中）；
  - 寻路层面：格内占用者全部可碾压时视为可直接进入
    （`Locomotor.CanMoveFreelyInto`），碾压者不会为步兵绕路。
- 步兵在浅水中使用游泳帧序列、在桥上/悬崖上有专门的位置处理，这些属于渲染层
  细节，与本项目的寻路/停靠设计无关。

### 2.5 对本项目有借鉴价值的机制小结

| 机制 | RA2 行为 | 借鉴优先级 |
|------|----------|-----------|
| 5 子格槽位 | RA2 一格最多 5 名步兵；本项目取 4（去掉中心槽，见 §4.1） | P0 |
| 子格停靠点 | 步兵吸附到子格世界坐标而非地格中心 | P0 |
| 类别化占用判定 | 载具整格 / 步兵子格，互相的阻挡规则不同 | P0 |
| 寻路仍在地格粒度 | 子格只参与占用判定 | P0 |
| 同点命令自然散开 | 占满目标格后溢出到邻格 | P0（仲裁自然结果） |
| SpeedType=Foot 差异地形代价 | 步兵与载具地形代价表不同 | P1 |
| 碾压 Crush | 载具压死步兵 | P2 |

---

## 3. FNA_RTS 现状分析

与步兵相关的现状（详见 `src/FNARTS.Core/Movement/MovementSystem.cs`）：

- `Unit` 持有 `FromTile`/`ToTile` 双格占位（移动中同时占用两格），进入下一格前
  必须预订成功；`MovementSystem.Update` 在每帧 `Unit.Update` 之前仲裁。
- `CanEnterTile`：地形可通过 + 无建筑 + **无任何其他单位占用/预订**
  （唯一豁免：自己正在攻击的目标格）。这是"一格一单位"的直接来源。
- `_occupancy: Dictionary<IsoCoord, List<Unit>>` 每帧重建，List 已经是多占用者
  结构，扩展为子格模型不需要换数据结构。
- 移动目的地：`AssignUnitPath` 一律把 `MoveTarget` 设为 `IsoToWorldCenter(tile)`，
  终点吸附也只认地格中心（`Unit.Update` 的 isFinal 分支）。
- 步兵类单位（soldier、archer、scout、medic、worker）在数据里已存在，但没有任何
  步兵专属逻辑；`Unit.IsVehicle` 目前只影响 3D 渲染与移动中开火。
- `CoordUtil`：地格为 64×32 世界像素的 2:1 菱形，`IsoToWorld`/`IsoToWorldCenter`
  与 OpenRA 的 `CPos` 投影同构；子格偏移可以直接在连续网格空间定义后投影。
- 编队移动默认关闭（RA1 FormMove=false 语义：全员同一目标格，仲裁散开）。
  子格模型让这一语义对步兵更接近 RA2 原生体验。

**需要打破的现有约定**：目前约定 `MoveTarget` 永远是地格中心，不落在自由世界点上。
本方案有意放宽：**步兵的 MoveTarget 是子格世界点**（仍保证落在所属地格菱形内，
`WorldToIso` 往返不变式保持成立，`Unit.Update` 的终点格校验
`WorldToIso(MoveTarget) == Path[^1]` 因此无需修改）。载具保持地格中心语义。

---

## 4. 设计方案

### 4.1 子格模型

新增 `src/FNARTS.Core/Math/SubCell.cs`：

```csharp
namespace FNARTS.Core
{
    /// <summary>
    /// 地格内的子格槽位：四个菱形顶点方向共 4 个（N/E/S/W）。
    /// RA2 含中心共 5 槽，在 2.5D 视角下偏拥挤，故去掉中心槽；
    /// Center 保留为非槽位哨兵（瞬态溢出停靠标记/格心点）。
    /// 载具与建筑使用 FullCell（整格占用，不参与子格分配）。
    /// </summary>
    public enum SubCell
    {
        FullCell = -1,   // 整格占用（载具、建筑）
        Center = 0,
        North = 1,
        East = 2,
        South = 3,
        West = 4,
    }

    /// <summary>子格元数据（不含 FullCell；Center 不是步兵槽位）。</summary>
    public static class SubCellInfo
    {
        public const int Count = 4;

        /// <summary>首个步兵槽（确定性扫描起点）。</summary>
        public const SubCell First = SubCell.North;

        /// <summary>各子格在地格内的连续网格偏移（0..1 范围，
        /// 保证投影后仍落在地格菱形内，WorldToIso 往返不变）。</summary>
        private static readonly (float X, float Y)[] Offsets =
        {
            (0.50f, 0.50f),   // Center — 格心点，非步兵槽位
            (0.76f, 0.76f),   // North（菱形上顶点方向，网格 +X+Y）
            (0.76f, 0.24f),   // East（菱形右顶点方向，网格 +X）
            (0.24f, 0.24f),   // South（菱形下顶点方向）
            (0.24f, 0.76f),   // West（菱形左顶点方向，网格 +Y）
        };

        /// <summary>子格的世界坐标 = 地格原点 + 偏移的等距投影。</summary>
        public static Vector2 ToWorld(IsoCoord tile, SubCell sub)
        {
            var (fx, fy) = Offsets[(int)sub];
            return new Vector2(
                (tile.X + fx - tile.Y - fy) * CoordUtil.HALF_TILE_W,
                -(tile.X + fx + tile.Y + fy) * CoordUtil.HALF_TILE_H);
        }

        public static bool IsInfantrySlot(SubCell s)
            => s >= SubCell.North
            && (int)s < (int)SubCell.North + Count;
    }
}
```

要点：

- 槽位数取 **4**（四个菱形顶点）：RA2/OpenRA 含中心共 5 槽，实测在本项目
  2.5D 视角下同格 5 兵视觉上过于拥挤，故去掉中心槽；`Center` 保留为
  非槽位哨兵值（瞬态溢出停靠标记，`ToWorld` 时映射回格心）。偏移全部取在
  [0.2, 0.8] 区间内，确保子格点严格位于所属地格菱形内部（不会被
  `WorldToIso` 归到邻格）。
- 槽位布局参考 OpenRA `MapGrid.SubCellOffsets`（其原值不对称，为 −299/+256
  世界单位，1 格 = 1024），本项目简化为对称 0.24/0.76 偏移；由于 2:1 菱形的
  四个顶点在屏幕上呈上/右/下/左，槽位按网格方向命名 North/East/South/West；
  确定性扫描从 `First = North` 开始（OpenRA 默认停靠中心子格，本项目无中心槽，
  默认即首个空闲槽）。
- `FullCell` 复用为"非子格占用者"的哨兵值，载具 `SubCell == FullCell`
  （对应 OpenRA `SubCell.FullCell = 0` 的语义）。

### 4.2 数据模型变更

`UnitDef`（`src/FNARTS.Core/Data/UnitDef.cs`）新增：

```csharp
/// <summary>True = 步兵：占子格、可与其他步兵共格；
/// False = 载具行为：整格独占（现状）。</summary>
public bool IsInfantry { get; set; } = false;
```

`Unit`（`src/FNARTS.Core/Entity/Unit.cs`）新增：

```csharp
// ---- 子格占用（步兵）----
/// <summary>当前占据的子格。载具恒为 FullCell。</summary>
public SubCell SubCell { get; set; } = SubCell.FullCell;
/// <summary>预订进入 ToTile 时认领的子格。静止时等于 SubCell。</summary>
public SubCell ToSubCell { get; set; } = SubCell.FullCell;

// ---- 命令期停靠分配（步兵，自由流动模型，见 4.4）----
/// <summary>命令时分配的停靠格与槽位；在途单位借此计入目标格占用。
/// AssignedSubCell 为有效槽位时才有意义。</summary>
public IsoCoord AssignedTile { get; set; }
public SubCell AssignedSubCell { get; set; } = SubCell.FullCell;

public bool IsInfantry => Definition.IsInfantry;
```

配套修改：

- `ClearOrders()` 不清除子格（子格是占用状态，不是命令状态），但清除
  分配（`AssignedSubCell = FullCell`）。
- `AdvanceWaypoint` 到达终点时同步 `SubCell = ToSubCell`（与 `FromTile = ToTile`
  同处）。
- 初始 `SubCell` 由 `MovementSystem.SyncTiles` 在首次同步时分配（见 4.3）。

`ConfigLoader` 使用 camelCase 反序列化，无需改动加载代码；单位 JSON 增加
`"isInfantry": true` 即可（见 4.7）。

### 4.3 MovementSystem 自由流动仲裁

核心不变量（自由流动模型）：

> **载具整格独占、一格一单位且与步兵互斥；步兵在途互不阻挡（可对穿），
> 停靠秩序纯粹由槽位分配解决：每格停靠步兵 ≤ 4 且槽位互斥。**

**为什么自由流动**：最初的类别化方案保留了步兵互挡（目标格槽满即阻挡），
实测中阻挡阶梯（等待 → Nudge → 重寻路）在大群里密集触发、互相拉扯，
严重阻碍流畅移动。因此取消步兵间的一切阻挡，让整群自由流动；停靠秩序
完全交给命令期分配（§4.4），残余的槽位冲突在到达时用"同格换槽 /
邻格溢出"解决。载具保留完整的 OpenRA 式预订仲裁不变。

`CanEnterTile` 类别感知：

```csharp
public bool CanEnterTile(Unit unit, IsoCoord tile)
{
    if (!_terrain.IsTerrainPassable(tile)) return false;
    if (!_entities.IsAreaFree(tile, 1, 1)) return false;

    if (_occupancy.TryGetValue(tile, out var occupants))
    {
        foreach (var other in occupants)
        {
            if (other == unit) continue;
            // 步兵互不阻挡——大群自由流动，槽位冲突延迟到停靠时解决。
            if (unit.IsInfantry && other.IsInfantry) continue;
            if (other.Id == unit.AttackTargetId) continue; // 近战豁免
            return false;
        }
    }
    return true;
}
```

即：步兵被载具（含在途预订）与建筑硬阻挡，但穿越任何步兵都自由；
载具把任何占用者视为阻挡（现状）。

`FreeSubCell` 统计一个地格上的**全部认领**（扫描全局单位表而非单格
occupants），按槽位顺序确定性选取：

```csharp
private SubCell FreeSubCell(IsoCoord tile, SubCell preferred, Unit ignore)
{
    // 按 (int)SubCell 直接索引；索引 0（Center）不是槽位，永不标记。
    Span<bool> taken = stackalloc bool[
        (int)SubCellInfo.First + SubCellInfo.Count];
    foreach (var e in _entities.AllEntities)
    {
        if (e is not Unit u || !u.IsAlive || !u.TilesInitialized
            || u.IsAircraft || u == ignore)
            continue;

        if (!u.IsInfantry)
        {
            // 载具整格占用——停靠或预订均算。
            if (u.FromTile == tile || u.ToTile == tile)
                return SubCell.FullCell;
            continue;
        }

        SubCell s;
        if (u.FromTile == tile)
            s = u.SubCell;                  // 停靠 / 正在离开
        else if (u.ToTile == tile)
            s = u.ToSubCell;                // 最终预订
        else if (u.AssignedTile == tile
            && SubCellInfo.IsInfantrySlot(u.AssignedSubCell))
            s = u.AssignedSubCell;          // 命令期分配
        else
            continue;

        if (SubCellInfo.IsInfantrySlot(s))
            taken[(int)s] = true;
    }

    if (SubCellInfo.IsInfantrySlot(preferred) && !taken[(int)preferred])
        return preferred;
    for (int i = 0; i < SubCellInfo.Count; i++)
        if (!taken[(int)SubCellInfo.First + i])
            return SubCellInfo.First + i;
    return SubCell.FullCell;
}
```

认领统计层级：停靠（`FromTile`）> 最终预订（`ToTile`）> 命令期分配
（`AssignedTile`）；其余在途步兵**不产生任何槽位认领**——这是自由流动的
基础。`ignore` 参数区分两种调用语义：

- **预订期**（`Reserve` 停靠分支、`FindSpillTile`、`Repath`）：传入移动者
  自身——`Reserve` 先写 `ToTile` 再查询，若不排除自身会被自己刚写的
  预订影子挡住；
- **命令层**（`FreeSubCellFor`）：传 `null`——单位自己已停靠的槽位不能
  被重复分配给别人。

预订与占用同步：`Arbitrate` 调用 `Reserve(unit, nextTile, isFinal)`，
`isFinal = PathIndex == Path.Count - 1`：

- **在途**（`isFinal == false`）：步兵不认领槽位，仅 `ToSubCell = SubCell`
  携带当前槽位（保证航点到达时 SubCell 有效）；载具 `ToSubCell = FullCell`
  （现状不变）。
- **最终预订**（`isFinal == true`）：步兵正式认领槽位。`preferred` 取命令期
  分配（`AssignedTile == tile` 且槽位有效），否则取当前槽位。有空闲槽：
  写 `ToSubCell`、同步更新分配；`MoveTarget` 若落在该格则改写为新槽位点
  （**同格换槽**，几像素级微调，不触发重寻路）。无空闲槽（**到达冲突**，
  例如目标格在途中被载具占用）：`ToSubCell = Center` 瞬态停靠（Center 非槽位，
  仅作标记），调用
  `FindSpillTile` 把 `MoveTarget`/分配改写为最近有闲槽的邻格；单位照常
  走进原格，随后 `Arbitrate` 的"有 MoveTarget 无路径"分支自动向新
  MoveTarget 重寻路。
- 同帧可见性：`Reserve` 末尾立即 `AddOccupant`，且已写的 `ToTile/ToSubCell`
  会被后续 `FreeSubCell` 扫到，两个单位不可能认领同一槽位。

`FindSpillTile`（溢出搜索）：围绕原目的地 ring 1..5，跳过自身 `FromTile`，
要求 `CanEnterTile` + 有空闲槽 + 寻路可达三者同时满足。用于最终预订的
到达冲突与 `Repath` 的途中重定向（目的地在途中被占满，如载具停靠）。

同步修改：

- `HandleBlocked` 的等待/Nudge/重寻路阶梯保留现状，但步兵互挡不再发生，
  阶梯只对载具/建筑阻挡者触发。停靠步兵永远不会被步兵驱逐——溢出者
  总是自己离开（自由流动的自然结果，也覆盖了旧"防冲走"需求）。
- `SyncTiles` 首次同步：步兵优先取 `SlotAtWorld` 从世界坐标反推的槽位
  （预置单位恰好摆在槽位点上），但经 `FreeSubCell`（preferred = 反推槽位）
  校验不遮蔽已有认领——例如格心出生的兵不能抢走别人已占的顶点槽；
  不在槽位点则从 `First` 起认领；格满时退回位置反推槽位（下次移动自然
  校正）。初始化分支立即 `AddOccupant`，保证同一 pass 内同格出生的多个
  步兵抢到不同槽位。
- `RebuildOccupancy` / `AddOccupant` 结构不变（tile → List\<Unit\>）。
- 飞行器豁免不变。

### 4.4 停靠分配与目的地选择

**命令层**（`RTSGame.AssignUnitPath` → `AssignInfantryPath`）：

- 载具：维持现状，目的地 = 地格中心。
- 步兵：命令时为每名步兵确定 **(格, 槽位) 停靠分配**，记录在
  `Unit.AssignedTile` / `Unit.AssignedSubCell`；`MoveTarget` = 分配槽位的
  世界点，各自沿自己的路径前往各自的分配格（互不阻挡，见 4.3）。

```csharp
// AssignInfantryPath（步兵分支，简化）
var dest = FindInfantryDestination(unit, start, targetTile);
//   目标格有闲槽则用目标格；否则 ring 1..5 找"有闲槽且可达"的最近格。
var (destTile, path) = dest.Value;
var sub = _movement.FreeSubCellFor(unit, destTile);   // 命令层查询，计入所有认领
unit.AssignedTile = destTile;
unit.AssignedSubCell = sub;
unit.MoveTarget = SubCellInfo.ToWorld(destTile, sub);
unit.Path = path; unit.PathIndex = 0;
if (path == null) unit.SubCell = unit.ToSubCell = sub; // 同格内换位：直接提交
```

**批量命令紧凑铺开**：`ApplyPlayerCommand` 逐单位顺序分配，`FreeSubCell`
把先行单位的 `AssignedTile/AssignedSubCell` 计入目标格（在途即认领），
因此 N 名步兵同点命令会自动从目标格的 4 个槽位开始、逐环向外填满
（`FindInfantryDestination` ring 1..5）——无需等到达仲裁，不产生等待与
互相拉扯。

**冲突处理（到达时）**：命令期分配只是预测，途中可能被抢（其他步兵的
最终预订先到、载具停靠）。最终预订的 `FreeSubCell` 重新决定：

1. **同格换槽**：preferred（分配槽）被占则取同格另一空闲槽，`MoveTarget`
   格内微调，不重寻路；
2. **格满溢出**：`FindSpillTile` 溢到最近有闲槽的邻格（4.3）；
3. **永不驱逐**：已停靠者保住自己的格与槽位，溢出者永远是后来者。

**已知取舍**：溢出后单位的 `SubCell` 字段保持旧值直到下次命令（占用
重新统计无影响；仅在"溢出 + 另有两兵竞争同槽 + 期间无人再下命令"的罕见
叠加下可能出现瞬时视觉重叠，接受）。

`Unit.Update` 的 isFinal 判定（`WorldToIso(MoveTarget) == Path[^1]`）对
子格点依然成立（偏移在格内），无需修改。

**编队（GroupMovement，当前休眠）**：启用后，步兵编队的槽位可以从"每单位一格"
升级为"每格最多 4 单位"的紧凑布局（槽位 = 格 + 子格）。本期不改，仅在文档中
记录接口预留。

### 4.5 出生点

`RTSGame.OnUnitSpawned`：

- `FindFreeAdjacentTile` 的"空闲"判据扩展：对步兵而言，"有空闲子格的地格"即为
  可用（载具仍要求整格空闲）。兵营一次训练一个单位，实际几乎总能选到 0 占用的
  格子。
- 出生位置：载具保持 `IsoToWorldCenter`；步兵放到所选子格点
  （`SubCellInfo.ToWorld`），`Unit.SubCell`/`ToSubCell` 直接初始化为该槽位，
  `TilesInitialized = true`，避免出生瞬间参与仲裁竞争。
- 首次同步的槽位反推：`MovementSystem.SyncTiles` 用 `SlotAtWorld` 从世界坐标
  精确匹配槽位点（场景预置/脚本出生的步兵直接沿用摆位），并经由 `FreeSubCell`
  校验不遮蔽已有认领；不在槽位点上才从首个空闲槽认领（见 4.3）。

### 4.6 与现有系统的交互

| 系统 | 影响 | 处理 |
|------|------|------|
| 寻路图（PathfindingFacade 等） | 无 | 占用判定全在 MovementSystem，寻路层零改动 |
| 战斗（CombatSystem） | 无逻辑改动 | 近战豁免（攻击目标格可进入）对步兵同样生效 |
| 移动中开火（MoveWhileAttacking） | 仅载具 | 步兵不受影响 |
| 飞行器 | 无 | 继续豁免仲裁 |
| 渲染（EntityRenderer） | 步兵本来就是 2D sprite 路径 | 世界坐标即子格点，同格多兵自然错开；深度排序沿用现有 grid-depth，无需改 |
| 选择（SelectionSystem） | 同格多兵可被各自点选 | 按世界坐标命中，子格错开后无歧义，无需改 |
| 卡死检测（StuckTracking） | 无 | 阈值与现状一致 |
| 测试工具 | `AssertFromTilesUnique` 对步兵失效 | 改为自由流动不变量 `AssertOccupancyValid`（见 §6） |

### 4.7 数据配置

`data/units/` 下的步兵类 JSON 增加字段（示例，soldier.json）：

```json
{
  "id": "soldier",
  ...
  "isInfantry": true,
  "collisionRadius": 8.0
}
```

- `isInfantry: true`：soldier、archer、medic、worker。scout 是飞行器
  （`isAircraft`），本就豁免仲裁，不标记。
- tank 保持 `isInfantry` 缺省（false）。
- 步兵 `collisionRadius` 从 16 减到约 8（视觉与点选半径；移动占用完全由子格
  决定，不受该字段影响）。

---

## 5. 文件清单与实现优先级

| 文件 | 变更 | 优先级 |
|------|------|--------|
| `src/FNARTS.Core/Math/SubCell.cs` | 新增：SubCell 枚举 + 偏移 + ToWorld | P0 |
| `src/FNARTS.Core/Data/UnitDef.cs` | +`IsInfantry` | P0 |
| `src/FNARTS.Core/Entity/Unit.cs` | +`SubCell`/`ToSubCell`/`IsInfantry`/`AssignedTile`/`AssignedSubCell`；`AdvanceWaypoint` 同步子格；`ClearOrders` 清分配 | P0 |
| `src/FNARTS.Core/Movement/MovementSystem.cs` | 自由流动 `CanEnterTile`、认领统计 `FreeSubCell`、`Reserve` 最终预订/溢出、`FindSpillTile`、`SyncTiles` 兜底 | P0 |
| `src/FNARTS.Game/RTSGame.cs` | 步兵停靠分配（`AssignInfantryPath`/`FindInfantryDestination`）、出生点选子格 | P0 |
| `data/units/*.json` | `isInfantry`、步兵碰撞半径 | P0 |
| `tests/FNARTS.Core.Tests/Movement/MovementSystemInfantryTests.cs` | 新增测试集（§6） | P0 |
| `tests/FNARTS.Core.Tests/Movement/MovementSystemTests.cs` | 现有断言适配（FromTile 唯一性改为按类别） | P0 |
| MovementSystem 暴露 `FreeSubCellFor(unit, tile)` 查询 API | P0（命令层选位用） | P0 |
| SpeedType=Foot 差异地形代价（TerrainCostProvider 按单位类别） | P1 |
| 碾压 Crush（UnitDef.Crushes / Crushable，载具入格压死步兵） | P2 |
| GroupMovement 紧凑步兵布局（格+子格槽位） | P2（随编队开关一起启用） |

P0 完成后的行为基线：载具一切照旧；步兵在途互不阻挡、自由流动，4 人共格、
停靠子格点，同点命令命令期紧凑铺开（每格 4 槽逐环填充），到达冲突同格换槽/
邻格溢出，被载具与建筑正确阻挡。

---

## 6. 测试计划

新增 `MovementSystemInfantryTests.cs`：

| 测试 | 验证点 |
|------|--------|
| Infantry_CanShareTile_UpToFour | 4 名步兵同时预订同一格，槽位互斥，各自停靠在自己的槽位点 |
| Infantry_FifthUnit_SpillsInsteadOfWaiting | 第 5 名步兵不被满格阻挡：走进后溢出到最近有闲槽的邻格 |
| Infantry_TransitThroughCrowdedTile_FreeFlow | 穿越满格直接流过，不阻挡、不 Nudge，无人被驱逐 |
| Infantry_HeadOn_PassThroughEachOther | 两名步兵对穿互换位置，无阻挡无等待 |
| Infantry_DockedOccupants_NotEvictedByLatecomers | 已停靠步兵保住格与槽位，后来者全部溢出 |
| FreeSubCell_Deterministic_AndSkipsTaken | 相同状态下多次查询返回同一槽位；跳过已占槽位 |
| FreeSubCell_InTransitCountsReservedSlot | 在途步兵的最终预订按 ToSubCell 计入目标格 |
| FreeSubCell_CountsCommandTimeAssignments | 在途步兵按命令期分配（AssignedTile）计入目标格 |
| Infantry_BlockedByStandingVehicle / _InTransit | 载具（静止或已预订）硬阻挡步兵 |
| Vehicle_BlockedByInfantry | 格内有步兵，载具不可进入（现状回归） |
| MeleeException_StillWorks | 攻击目标格豁免在自由流动规则下依然生效 |
| Infantry_StopAtSubCellPoint | 到达后 WorldPosition == 子格世界点，WorldToIso 往返不变 |
| Infantry_SameOrder_SpreadsAcrossTiles | 8 名步兵同点命令：目标格 ≤4，其余铺开邻格，无槽位重叠 |
| MixedGroup_OccupancyInvariantHolds | 载具+步兵混编穿越：自由流动不变量全程成立 |
| Spawn_SameTileInfantry_ClaimDistinctSlots | 同格出生多兵首次同步即认领互斥槽位 |

现有测试适配：

- `AssertFromTilesUnique` 改为自由流动不变量 `AssertOccupancyValid`：载具整格
  独占（From 与 To）；**已停止**步兵（无路径、无 MoveTarget）每格 ≤4 且槽位
  互斥；在途步兵不产生槽位认领（在途预订只携带自身槽位）——仅**最终停靠
  预订**（ToTile == 路径末航点）不得遮蔽已停靠槽位（瞬态溢出停靠
  `ToSubCell == Center` 豁免）；步兵与载具不共格。
- `GroupMovementTests` 的"目的地互不相同"断言仅对载具编队生效（步兵编队随
  GroupMovement 休眠暂不启用）。

回归：`./run_tests.sh` 全绿。

---

## 7. 后续扩展（不在本期范围）

- **碾压（Crush）**：`UnitDef.Crushes`（载具）与 `UnitDef.Crushable`（步兵）；
  载具 `CanEnterTile` 对 Crushable 占用者放行，进入后触发击杀；步兵 Nudge 时
  优先避开敌方碾压载具。对应 RA2 `Crusher=yes` + `MovementZone=Crusher`。
- **差异地形代价**：`TerrainCostProvider` 增加单位类别维度（Foot vs Track），
  步兵可穿越载具受限的地形。
- **朝向动画**：RA2 步兵 8 朝向帧序列；本项目的 sprite 渲染接入朝向选择。
- **驻军（Garrison）**：步兵进入建筑，占用从地图移除。

---

## 8. 参考资料

- ModEnc — *Infantry (maps)*：`[Infantry]` 段的 `SUB_CELL` 字段（子格模型的直接证据）
- ModEnc — *Locomotor*：`Walk` 机兵到达判定（< 17 leptons）、`SpeedType=Foot` 默认值
- ModEnc — *SpeedType*：Foot / Track / Wheel 地形代价分类
- ModEnc — *MovementZone*：碾压者（Crusher）寻路语义
- OpenRA 源码（本机 `~/dev/OpenRA`，重建 RA1/RA2 行为的权威参考）：
  - `OpenRA.Game/Traits/TraitsInterfaces.cs` — `SubCell` 枚举（FullCell=0, First=1, Any, Invalid）
  - `OpenRA.Game/Map/MapGrid.cs` — `SubCellOffsets`（5 槽位：中心+四角）、`DefaultSubCell`
  - `OpenRA.Mods.Common/Traits/World/ActorMap.cs` — 占用链表 `InfluenceNode`、
    `FreeSubCell`/`AnyActorsAt` 子格匹配规则、checkTransient 豁免
  - `OpenRA.Mods.Common/Traits/World/Locomotor.cs` — `SharesCell`、`Crushes`、
    `CanMoveFreelyInto`/`GetAvailableSubCell` 阻挡判定
  - `OpenRA.Mods.Common/Traits/Mobile.cs` — `FromSubCell`/`ToSubCell`、
    `OccupiedCells`（在途步兵只登记 ToCell 的 HACK）、Nudge/IsBlocking、子格停靠
  - `OpenRA.Mods.Common/Activities/Move/Move.cs` — `PopPath` 预订阶梯、
    子格偏好（FromSubCell）、nearEnough/NearestMoveableCell 兜底
  - `mods/ra/rules/world.yaml`、`mods/ra/rules/defaults.yaml` — `foot`/`tracked`
    locomotor 配置（SharesCell、Crushes）、`^Infantry` 的 Crushable
- 本项目文档：`docs/PATHFINDING_REDESIGN.md`（§9.4 单位进出格仲裁，本方案的基础）
