# FNA_RTS 第二阶段开发文档：战斗与兵种系统

## 1. 阶段目标

在 Phase 1 MVP 基础上实现**完整战斗循环**：攻击指令发起 → A\* 寻路接近目标 → 进入射程 → 攻击判定 → HP/Armor/Damage 计算 → 单位死亡移除。同时实现**编队移动**基础避障，以及**战斗属性数据驱动**。

Phase 2 完成后，游戏具备"建造基地 → 训练军队 → 指挥作战 → 消灭敌人"的完整 RTS 体验闭环。

### 1.1 功能范围

| 功能 | 描述 |
|------|------|
| A\* 寻路 | 8 方向网格寻路，建筑/地形障碍规避，对角线切角检测（最优先） |
| 战斗系统 | 攻击指令，HP/护甲/伤害计算，攻击冷却，自动追击，死亡与移除 |
| 兵种体系完善 | JSON 数据驱动战斗属性（HP、攻击力、射程、冷却、护甲） |
| 右键敌我判定 | 右键敌方→攻击指令，右键友方/空地→移动指令 |
| 编队移动 | 多单位目标位置网格排列，局部转向避障防止重叠 |

### 1.2 不在范围内

- 战争迷雾 — Phase 3（纯渲染层，不参与战斗逻辑；与帧同步/多人更自然搭配）
- 远程投射物（箭矢/子弹飞行）— Phase 2 采用即时伤害判定
- 技能/特殊能力（治疗、隐形、AOE）— Phase 3
- 科技树升级（攻防升级）— Phase 3
- AI 对手 — Phase 3
- 网络/帧同步 — Phase 3
- 采集/经济系统 — Phase 3

### 1.3 调整说明

相比总体路线中的 Phase 2 定义，战争迷雾推迟至 Phase 3。原因：

1. **迷雾不参与战斗逻辑**：迷雾只影响渲染可见性——攻击、移动、寻路等核心判定不依赖迷雾状态。Phase 3（帧同步网络）中，每个客户端独立计算视野，与迷雾更自然搭配。
2. **保持战斗开发连续性**：去掉迷雾后，从寻路→战斗属性→战斗系统→RTSGame 集成形成一条连续流水线，中间不再被纯渲染功能打断。

---

## 2. 前置工作：FNA_Test 基础设施测试

Phase 2 需要在 FNA_Test 中验证 2 项新能力：

```
RTS/Pathfinding/        ← 基础：A* 寻路算法正确性与性能（最优先）
RTS/CombatLogic/        ← 核心：战斗逻辑纯单元测试（紧随其后）
```

### 2.1 RTS/Pathfinding — A\* 寻路正确性

**目标**：验证 A\* 算法在等距网格上找到最短路径，正确绕开障碍物，性能满足需求。

**Pathfinder.cs 接口设计**：
```csharp
public class Pathfinder
{
    // 网格可通过性回调 — Core 层不持有 TileMap 引用，通过委托注入
    public Func<IsoCoord, bool> IsPassable { get; set; }

    // 网格边界
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }

    // 最大搜索迭代数（安全上限，默认 2500）
    public int MaxIterations { get; set; }

    // 寻找最短路径 — 返回网格坐标序列（不含起点，含终点）
    // 无可达路径时返回空列表
    public List<IsoCoord> FindPath(IsoCoord start, IsoCoord end);
}
```

**测试要点**：
- 无障碍直线：路径步数 = 八方向最优步数（非曼哈顿距离）
- U 形障碍绕行：找到绕行路径，不穿墙
- 被完全封闭时返回空列表
- 起点=终点返回空列表
- 越界坐标返回空列表
- 对角线切角检测：不可沿对角线穿过两个不可通过格之间的缝隙
- 性能：51×51 网格最坏情况 < 5ms（纯 CPU 断言，headless 模式）
- 启发式函数对称性和三角不等式

### 2.2 RTS/CombatLogic — 战斗逻辑

**目标**：验证伤害计算、攻击冷却、HP 归零死亡等纯逻辑正确性，无渲染依赖。

**测试要点**：
- 攻击力 − 护甲 = 实际伤害（最小 1）
- HP 降至 0 以下时 `IsAlive = false`
- 攻击冷却期间 `CanAttack = false`
- 超出射程不能攻击（应先移动至射程内）
- 已死亡单位不能被选为攻击目标
- 攻击同一阵营目标按配置处理（默认禁止攻击友方）
- 冷却随时间递减至 0 后恢复攻击能力

---

## 3. 数据格式扩展

### 3.1 设计原则

所有新增战斗属性字段均有合理默认值，保证向后兼容 Phase 1 JSON 文件。`ConfigLoader` 使用 `System.Text.Json` 的 `PropertyNameCaseInsensitive = true`，缺失字段自动使用 C# 默认值，无需修改现有数据文件即可通过编译和测试。

### 3.2 UnitDef 扩展 (data/units/*.json)

```json
{
  "id": "soldier",
  "name": "Soldier",
  "moveSpeed": 90.0,
  "buildTime": 8.0,
  "textureId": "infantry",

  "hp": 100,
  "attackDamage": 15,
  "attackRange": 96.0,
  "attackCooldown": 1.0,
  "armor": 2,
  "visionRange": 5
}
```

**新增字段说明**：

| 字段 | 类型 | 默认值 | Phase | 说明 |
|------|------|--------|-------|------|
| `hp` | int | 50 | P2 | 生命值上限。HP 归零则单位死亡 |
| `attackDamage` | int | 5 | P2 | 每次攻击造成的基础伤害 |
| `attackRange` | float | 64.0 | P2 | 攻击射程（世界像素），目标中心距离判定 |
| `attackCooldown` | float | 1.0 | P2 | 攻击冷却时间（秒） |
| `armor` | int | 0 | P2 | 护甲值，直接抵扣伤害 |
| `visionRange` | int | 4 | P3 | 视野范围（网格格数），Phase 3 战争迷雾启用 |

**`visionRange` 在 Phase 2 已定义默认值但暂不生效**：字段已存在于数据定义中以备未来使用，Phase 3 实现战争迷雾时无需再次修改数据格式。

**三种单位的 Phase 2 战斗属性设计**：

| 单位 | HP | 攻击 | 射程 | 冷却 | 护甲 | 速度 | 定位 |
|------|-----|------|------|------|------|------|------|
| Worker | 60 | 3 | 48 | 1.5s | 0 | 120 | 采集/建造（低战斗能力） |
| Soldier | 100 | 15 | 96 | 1.0s | 2 | 90 | 基础步兵（均衡型） |
| Tank | 200 | 35 | 128 | 2.0s | 8 | 70 | 重型突击（高攻高防低攻速） |

**平衡性设计思路**：
- Soldier 克制 Worker（快速击杀），但对 Tank 伤害低（15−8=7 per hit）
- Tank 克制 Soldier（35−2=33 per hit，2 下击杀），但攻速慢
- Worker 被所有单位克制，主要用于建造和经济
- 后续 Phase 3 可添加更多兵种（医疗兵、远程炮兵、侦察兵等）

### 3.3 BuildingDef 扩展 (data/buildings/*.json)

```json
{
  "id": "place_3x2x2",
  "name": "Barracks",
  "sizeX": 3,
  "sizeY": 2,
  "height": 2,
  "textureId": "gen_3_2_2",
  "producesUnitIds": ["worker", "soldier"],

  "hp": 500,
  "armor": 5,
  "visionRange": 3
}
```

**新增字段说明**：

| 字段 | 类型 | 默认值 | Phase | 说明 |
|------|------|--------|-------|------|
| `hp` | int | 300 | P2 | 建筑生命值（建筑比单位耐久） |
| `armor` | int | 3 | P2 | 建筑护甲 |
| `visionRange` | int | 2 | P3 | 建筑视野，Phase 3 战争迷雾启用 |

**现有建筑的 Phase 2 战斗属性**：

| 建筑 | HP | 护甲 | 说明 |
|------|-----|------|------|
| Command Center | 1000 | 8 | 主基地，最耐久 |
| Shed | 200 | 1 | 最小建筑，最脆弱 |
| Outpost | 400 | 3 | 小型前哨 |
| Barracks | 500 | 5 | 中型兵营 |
| Workshop | 550 | 5 | 中型工厂 |
| Hall | 600 | 6 | 大型厅堂 |
| Fortress | 1500 | 12 | 巨型堡垒 |

### 3.4 代码修改清单

#### UnitDef.cs — 新增字段

```csharp
// FNARTS.Core/Data/UnitDef.cs

public class UnitDef
{
    // Phase 1 字段（保持不变）
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public float MoveSpeed { get; set; } = 100f;
    public float BuildTime { get; set; } = 5f;
    public string TextureId { get; set; } = "";

    // Phase 2 新增战斗属性
    public int HP { get; set; } = 50;
    public int AttackDamage { get; set; } = 5;
    public float AttackRange { get; set; } = 64f;
    public float AttackCooldown { get; set; } = 1.0f;
    public int Armor { get; set; } = 0;
    public int VisionRange { get; set; } = 4;     // Phase 3 启用
}
```

#### BuildingDef.cs — 新增字段

```csharp
// FNARTS.Core/Data/BuildingDef.cs

public class BuildingDef
{
    // Phase 1 字段（保持不变）
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int SizeX { get; set; } = 1;
    public int SizeY { get; set; } = 1;
    public int Height { get; set; } = 1;
    public string TextureId { get; set; } = "";
    public List<string> ProducesUnitIds { get; set; } = new();

    // Phase 2 新增
    public int HP { get; set; } = 300;
    public int Armor { get; set; } = 3;
    public int VisionRange { get; set; } = 2;      // Phase 3 启用
}
```

---

## 4. 核心类详细设计

### 4.1 Pathfinder — A\* 寻路器 (FNARTS.Core) 【实现优先级：最高】

寻路是 Phase 2 最先实现的系统。遵循"Core 层不依赖 FNA"原则，通过委托注入可通过性判定。

#### PathNode — 搜索节点

```csharp
// FNARTS.Core/Pathfinding/PathNode.cs

namespace FNARTS.Core
{
    /// <summary>A* 搜索节点，存储在优先队列和已访问字典中。</summary>
    internal struct PathNode
    {
        public IsoCoord Coord;     // 网格坐标
        public int GCost;          // 起点到当前的累积代价
        public int HCost;          // 启发式代价（到终点的估算）
        public int FCost => GCost + HCost;
        public IsoCoord Parent;    // 回溯路径用的父节点坐标
    }
}
```

#### Pathfinder — 寻路器

```csharp
// FNARTS.Core/Pathfinding/Pathfinder.cs

namespace FNARTS.Core
{
    /// <summary>
    /// A* 网格寻路器。纯 C# 实现，无 FNA 或 GPU 依赖。
    /// 可通过性通过委托注入，Core 层无需了解 TileMap 或 EntityManager。
    /// </summary>
    public class Pathfinder
    {
        /// <summary>判断指定网格格是否可通过。</summary>
        public Func<IsoCoord, bool> IsPassable { get; set; }

        /// <summary>地图尺寸（用于边界检查）。</summary>
        public int MapWidth { get; set; }
        public int MapHeight { get; set; }

        /// <summary>
        /// 最大搜索迭代数（安全上限，防止无限循环）。
        /// 默认 2500，对 51×51 搜索空间足够。
        /// </summary>
        public int MaxIterations { get; set; } = 2500;

        /// <summary>
        /// 寻找从 start 到 end 的最短路径。
        /// 返回网格坐标列表（不含起点，包含终点）。
        /// 无可达路径时返回空列表。
        /// </summary>
        public List<IsoCoord> FindPath(IsoCoord start, IsoCoord end);
    }
}
```

#### A\* 算法核心流程

```
FindPath(start, end):
  1. 快速拒绝：
     - 起点/终点越界 → 返回空列表
     - 终点不可通过 → 返回空列表
     - 起点 = 终点 → 返回空列表

  2. 初始化：
     openSet = MinHeap<PathNode>      ← 按 FCost 排序，同值按 HCost
     closedSet = HashSet<IsoCoord>
     nodeMap = Dictionary<IsoCoord, PathNode>  ← 快速查找/更新

  3. 将 start 加入 openSet（GCost=0, HCost=OctileDistance(start, end)）

  4. while openSet 非空 && iterations < MaxIterations:
       current = openSet.PopMin()       ← 取 F 值最小的节点
       if current.Coord == end → 回溯构造路径，返回

       将 current.Coord 加入 closedSet

       for each neighbor in GetNeighbors(current.Coord):
         if neighbor ∈ closedSet → skip
         if !IsPassable(neighbor) → skip
         if 对角线移动 → 切角检测（两个相邻正交格必须都可通过）

         tentativeG = current.GCost + MoveCost(current, neighbor)
         // 直线移动代价 = 10, 对角线代价 = 14 (≈10×√2)

         if neighbor ∉ openSet 或 tentativeG < existing.GCost:
           更新/创建 nodeMap[neighbor]
           Parent = current.Coord
           加入/更新 openSet

  5. 到达 MaxIterations → 返回空列表（无路径）
```

#### 启发式函数：Octile Distance

```
OctileDistance(a, b) = 10 × max(|dx|, |dy|) + 4 × min(|dx|, |dy|)

其中 dx = b.X − a.X, dy = b.Y − a.Y
直线移动代价基数 = 10，对角线代价 = 14 ≈ 10×√2
差值为 4 = 14 − 10（对角线比直线多出的代价）
```

此启发式函数在允许 8 方向移动的网格上是 **admissible**（不高估）且 **consistent** 的，保证 A\* 找到最优路径。

#### 邻接方向（等距网格 8 方向）

等距视角中，网格坐标轴定义如下：
- X+ (东)：屏幕右下方向
- Y+ (南)：屏幕右上方向

| 方向 | dX | dY | 代价 | 屏幕含义 |
|------|----|----|------|---------|
| 东 | +1 | 0 | 10 | 右下 |
| 西 | −1 | 0 | 10 | 左上 |
| 南 | 0 | +1 | 10 | 右上 |
| 北 | 0 | −1 | 10 | 左下 |
| 东南 | +1 | +1 | 14 | 纯右 |
| 西南 | −1 | +1 | 14 | 纯上 |
| 东北 | +1 | −1 | 14 | 纯下 |
| 西北 | −1 | −1 | 14 | 纯左 |

#### 切角检测

对角线移动 (dx, dy) 需要两个相邻正交格都可通过，防止单位从障碍物斜角"挤过"。

```
从 (gx, gy) 对角线移动到 (gx+dx, gy+dy) 时：
  必须 IsPassable(gx+dx, gy)   AND  IsPassable(gx, gy+dy)
  两者都可通过，才允许对角线移动
```

例如从 (0,0) 移动到 (1,1)：需要 (1,0) 和 (0,1) 都可通过。

#### 路径回溯

从终点节点沿 Parent 链回到起点，反转后返回（不含起点坐标）。

#### 可选优化（后期添加）

- **路径平滑**：如果三个连续航点共线（方向相同），移除中间点，减少不必要的转向
- **漏斗算法**：对航点序列做拉绳优化，使路径更贴合几何最短路径
- **分帧寻路**：大量单位同时寻路时，将寻路请求分散到多帧处理

---

### 4.2 Unit.cs 修改 — 战斗属性与航点跟随

Phase 1 的 `Unit` 使用直线移动到 `MoveTarget`。Phase 2 改为沿 A\* 航点列表移动，同时增加战斗状态管理。

```csharp
// FNARTS.Core/Entity/Unit.cs（修改后）

namespace FNARTS.Core
{
    /// <summary>可移动、可战斗的单位实体。</summary>
    public class Unit : Entity
    {
        public UnitDef Definition { get; }
        public float MoveSpeed { get; }

        // ---- 移动 ----
        public Vector2? MoveTarget { get; set; }         // 最终目标世界坐标
        public List<IsoCoord>? Path { get; set; }        // A* 航点列表（网格坐标）
        public int PathIndex { get; set; }                // 当前航点索引

        // ---- 战斗 ----
        public int CurrentHP { get; set; }
        public int MaxHP => Definition.HP;
        public int AttackDamage => Definition.AttackDamage;
        public float AttackRange => Definition.AttackRange;
        public float AttackCooldownTimer { get; set; }    // 攻击冷却计时器
        public uint? AttackTargetId { get; set; }          // 攻击目标实体 ID
        public int Armor => Definition.Armor;

        // ---- 状态 ----
        public bool IsAttacking => AttackTargetId.HasValue;
        public bool CanAttack => AttackCooldownTimer <= 0f;
        public bool IsMoving => (Path != null && PathIndex < Path.Count)
                                || MoveTarget.HasValue;

        public Unit(UnitDef definition)
        {
            Definition = definition;
            MoveSpeed = definition.MoveSpeed;
            CurrentHP = definition.HP;
            AttackCooldownTimer = 0f;
        }

        /// <summary>
        /// 每帧更新：沿航点移动 + 攻击冷却计时。
        /// </summary>
        public void Update(float dt)
        {
            if (!IsAlive) return;

            // 更新攻击冷却
            if (AttackCooldownTimer > 0f)
                AttackCooldownTimer -= dt;

            // 航点跟随移动
            if (Path != null && PathIndex < Path.Count)
            {
                Vector2 waypointWorld = CoordUtil.IsoToWorldCenter(Path[PathIndex]);
                if (!MoveToward(waypointWorld, dt))
                    PathIndex++;   // 到达当前航点，前进到下一个
            }
            else if (MoveTarget.HasValue)
            {
                // 无路径时的回退：直线移动（Phase 1 兼容）
                if (!MoveToward(MoveTarget.Value, dt))
                {
                    MoveTarget = null;
                    Path = null;
                }
            }
        }

        /// <summary>
        /// 向目标点移动 dt 秒。返回 true 表示仍在移动，false 表示已到达。
        /// </summary>
        private bool MoveToward(Vector2 target, float dt)
        {
            Vector2 toTarget = target - WorldPosition;
            float distance = toTarget.Length();

            if (distance < 2f)
            {
                WorldPosition = target;
                return false;
            }

            float step = MoveSpeed * dt;
            if (step >= distance)
            {
                WorldPosition = target;
                return false;
            }

            WorldPosition += Vector2.Normalize(toTarget) * step;
            return true;
        }

        /// <summary>清除移动和攻击状态。</summary>
        public void ClearOrders()
        {
            MoveTarget = null;
            Path = null;
            PathIndex = 0;
            AttackTargetId = null;
        }
    }
}
```

### 4.3 Building.cs 修改 — 战斗属性

```csharp
// FNARTS.Core/Entity/Building.cs（新增字段，其余保持不变）

public class Building : Entity
{
    // ... Phase 1 字段和逻辑保持不变 ...

    // Phase 2 新增
    public int CurrentHP { get; set; }
    public int MaxHP => Definition.HP;
    public int Armor => Definition.Armor;

    public Building(BuildingDef definition, IsoCoord placementOrigin)
    {
        Definition = definition;
        PlacementOrigin = placementOrigin;
        WorldPosition = CoordUtil.BuildingWorldOrigin(
            placementOrigin, definition.SizeX, definition.SizeY);
        CurrentHP = definition.HP;    // Phase 2 新增
    }
}
```

### 4.4 CombatSystem — 战斗系统 (FNARTS.Core)

战斗系统负责每帧处理所有攻击者的攻击判定和伤害结算。

```csharp
// FNARTS.Core/Combat/CombatSystem.cs

namespace FNARTS.Core
{
    /// <summary>
    /// 每帧处理战斗逻辑：射程判定、伤害计算、冷却管理、死亡清理。
    /// 纯逻辑，无 FNA 依赖。所有计算使用固定 dt = 1/60s。
    /// </summary>
    public class CombatSystem
    {
        private readonly List<uint> _deadEntities = new();
        private int _frameCounter = 0;

        /// <summary>
        /// 处理一帧战斗。
        /// </summary>
        /// <param name="entities">实体管理器，用于目标查询。</param>
        /// <param name="pathfinder">寻路器，用于自动追击。</param>
        /// <param name="onDeath">每个死亡实体的回调（在遍历后调用，避免集合修改异常）。</param>
        public void Update(float dt, EntityManager entities,
            Pathfinder pathfinder, Action<Entity> onDeath)
        {
            _deadEntities.Clear();
            _frameCounter++;

            // 按 ID 排序保证确定性遍历
            var aliveEntities = entities.AllEntities
                .Where(e => e.IsAlive)
                .OrderBy(e => e.Id);

            foreach (var entity in aliveEntities)
            {
                if (entity is Unit unit && unit.AttackTargetId.HasValue)
                    ProcessUnitCombat(unit, entities, pathfinder, dt);
                // 建筑目前不主动攻击，Phase 3 可添加防御性建筑自动攻击
            }

            // 触发死亡回调（在遍历完成后处理，避免修改正在遍历的集合）
            foreach (var id in _deadEntities)
            {
                var entity = entities.GetEntity(id);
                if (entity != null)
                    onDeath(entity);
            }
        }

        private void ProcessUnitCombat(Unit attacker, EntityManager entities,
            Pathfinder pathfinder, float dt)
        {
            var target = entities.GetEntity(attacker.AttackTargetId.Value);
            if (target == null || !target.IsAlive)
            {
                // 目标已消失/死亡 → 清除攻击状态
                attacker.AttackTargetId = null;
                return;
            }

            float distance = Vector2.Distance(
                attacker.WorldPosition, target.WorldPosition);

            if (distance <= attacker.AttackRange)
            {
                // 在射程内 → 尝试攻击
                if (attacker.CanAttack)
                {
                    ApplyDamage(attacker, target);
                    attacker.AttackCooldownTimer = attacker.Definition.AttackCooldown;

                    if (!target.IsAlive)
                        _deadEntities.Add(target.Id);
                }
                // 冷却中：原地等待
            }
            else
            {
                // 不在射程内 → 自动追击
                // 每 30 帧（0.5 秒）重新寻路，减少 CPU 开销
                if (_frameCounter % 30 == 0 || attacker.Path == null)
                {
                    var start = CoordUtil.WorldToIso(attacker.WorldPosition);
                    var end = CoordUtil.WorldToIso(target.WorldPosition);
                    attacker.Path = pathfinder.FindPath(start, end);
                    attacker.PathIndex = 0;
                }
                // Unit.Update 沿 Path 移动
            }
        }

        /// <summary>计算实际伤害（护甲抵扣，最小 1）。</summary>
        public static int CalculateDamage(int attackDamage, int armor)
            => Math.Max(1, attackDamage - armor);

        private void ApplyDamage(Unit attacker, Entity target)
        {
            int targetArmor = target switch
            {
                Unit u => u.Armor,
                Building b => b.Armor,
                _ => 0
            };

            int damage = CalculateDamage(attacker.AttackDamage, targetArmor);

            switch (target)
            {
                case Unit targetUnit:
                    targetUnit.CurrentHP -= damage;
                    if (targetUnit.CurrentHP <= 0)
                        targetUnit.IsAlive = false;
                    break;
                case Building targetBuilding:
                    targetBuilding.CurrentHP -= damage;
                    if (targetBuilding.CurrentHP <= 0)
                        targetBuilding.IsAlive = false;
                    break;
            }
        }
    }
}
```

### 4.5 AttackCommand — 攻击指令

```csharp
// FNARTS.Core/Command/Command.cs（修改）

public enum CommandType
{
    Move,
    Build,
    Attack     // Phase 2 新增
}
```

```csharp
// FNARTS.Core/Command/AttackCommand.cs（新文件）

namespace FNARTS.Core
{
    /// <summary>
    /// 命令选中单位攻击目标实体。单位会自动寻路至射程内然后开始攻击。
    /// </summary>
    public class AttackCommand : Command
    {
        public override CommandType Type => CommandType.Attack;

        /// <summary>目标实体 ID。</summary>
        public uint TargetEntityId { get; }

        /// <summary>目标的世界坐标（指令发出时的快照，用于初始寻路）。</summary>
        public Vector2 TargetWorldPosition { get; }

        public AttackCommand(uint targetEntityId, Vector2 targetWorldPosition)
        {
            TargetEntityId = targetEntityId;
            TargetWorldPosition = targetWorldPosition;
        }
    }
}
```

### 4.6 CommandSystem.cs 修改 — 右键敌我判定

```csharp
// FNARTS.Core/Command/CommandSystem.cs（修改 ProcessRightClick）

public class CommandSystem
{
    private int _playerFaction = 0;

    /// <summary>设置玩家阵营，用于敌我判定。</summary>
    public int PlayerFaction
    {
        get => _playerFaction;
        set => _playerFaction = value;
    }

    /// <summary>
    /// 处理右键点击生成指令。
    /// 右键敌方 → AttackCommand，右键友方/空地 → MoveCommand。
    /// </summary>
    public Command? ProcessRightClick(Vector2 worldPos,
        EntityManager entities, SelectionSystem selection)
    {
        // 先检测是否点击了实体
        var clicked = entities.QueryPoint(worldPos);

        if (clicked != null && clicked.IsAlive)
        {
            // 点击了实体：判断敌我
            if (clicked.Faction != _playerFaction)
            {
                // 敌方 → 攻击
                return new AttackCommand(clicked.Id, clicked.WorldPosition);
            }
            else
            {
                // 友方 → 移动到其位置
                return new MoveCommand(clicked.WorldPosition);
            }
        }
        else
        {
            // 空地 → 移动
            return new MoveCommand(worldPos);
        }
    }
}
```

### 4.7 EntityManager 扩展

```csharp
// FNARTS.Core/Entity/EntityManager.cs（新增方法）

/// <summary>获取指定阵营的所有存活实体。</summary>
public IEnumerable<Entity> GetFactionEntities(int faction)
{
    return _allEntities.Where(e => e.IsAlive && e.Faction == faction);
}

/// <summary>获取相对于指定阵营的所有敌方存活实体。</summary>
public IEnumerable<Entity> GetEnemyEntities(int faction)
{
    return _allEntities.Where(e => e.IsAlive && e.Faction != faction);
}

/// <summary>清理所有死亡实体（移除索引和集合项）。</summary>
public void RemoveDead()
{
    var dead = _allEntities.Where(e => !e.IsAlive).ToList();
    foreach (var e in dead)
    {
        UnindexEntity(e);
        _entities.Remove(e.Id);
        _allEntities.Remove(e);
    }
}
```

### 4.8 编队移动 — 编队排列 + 局部避障 (FNARTS.Core) 【可选打磨】

> **已实施（as-built）变更**：本节原方案（`FormationPosition` 固定方阵 + `SeparationBehavior` 局部避障）已按 RA1/RA2 官方行为重做，保留本节作为历史设计记录：
> - `FormationPosition`（Box/Line 方阵模板）→ 已删除，由 `GroupMovement` 取代（RA1 `Toggle_Formation` 语义：下单瞬间各单位相对包围盒中心的格子偏移快照，当前布局即队形，无行军中维护）
> - `SeparationBehavior` 软推挤 → 已移除，由 `MovementSystem` 取代（OpenRA `Mobile` 进出格仲裁：一格一单位、预订/等待/Nudge/重寻路/StepAside）
> - 编队移动**默认关闭**：多选移动 = RA1 `FormMove=false`（全员同一目标格聚集，仲裁散开）；`GroupMovement` 保留但休眠，待 Ctrl+编队与队形开关实现后启用

编队移动在战斗系统集成完成后作为打磨项实现。它不是核心战斗循环的必需部分（右键 → 寻路 → 攻击 → 死亡已经完整），但显著改善多单位操作的体验。

#### FormationPosition — 编队位置计算

```csharp
// FNARTS.Core/Movement/FormationPosition.cs

namespace FNARTS.Core
{
    /// <summary>
    /// 计算多单位编队移动时的各自目标位置。
    /// 单位排列在目标点周围的方阵中。
    /// </summary>
    public static class FormationPosition
    {
        /// <param name="target">编队中心目标位置（世界坐标）。</param>
        /// <param name="unitCount">编队中单位数量。</param>
        /// <param name="spacing">单位间隔（世界像素），默认 48。</param>
        public static Vector2[] Compute(Vector2 target, int unitCount,
            float spacing = 48f)
        {
            var positions = new Vector2[unitCount];
            int cols = (int)MathF.Ceiling(MathF.Sqrt(unitCount));
            int rows = (int)MathF.Ceiling((float)unitCount / cols);

            float offsetX = -(cols - 1) * spacing / 2f;
            float offsetY = -(rows - 1) * spacing / 2f;

            for (int i = 0; i < unitCount; i++)
            {
                int col = i % cols;
                int row = i / cols;
                positions[i] = target + new Vector2(
                    offsetX + col * spacing,
                    offsetY + row * spacing);
            }
            return positions;
        }
    }
}
```

#### SeparationBehavior — 局部避障

```csharp
// FNARTS.Core/Movement/SeparationBehavior.cs

namespace FNARTS.Core
{
    /// <summary>
    /// 基于转向的简单局部分离力，防止单位重叠。
    /// 不是完整的 RVO2 —— 只处理单位过于靠近时的排斥力。
    /// </summary>
    public static class SeparationBehavior
    {
        public const float SEPARATION_RADIUS = 24f;

        public static Vector2 Compute(Vector2 position,
            IEnumerable<Vector2> nearbyPositions)
        {
            Vector2 separation = Vector2.Zero;
            int count = 0;

            foreach (var otherPos in nearbyPositions)
            {
                float dist = Vector2.Distance(position, otherPos);
                if (dist < SEPARATION_RADIUS && dist > 0.01f)
                {
                    Vector2 away = position - otherPos;
                    away = Vector2.Normalize(away) / dist;
                    separation += away;
                    count++;
                }
            }

            if (count > 0)
                separation /= count;

            return separation * SEPARATION_RADIUS * 0.5f;
        }
    }
}
```

### 4.9 项目结构更新

```
FNARTS.Core/                           [修改]
├── Pathfinding/
│   ├── PathNode.cs                    ← 新增
│   └── Pathfinder.cs                  ← 新增
├── Combat/
│   └── CombatSystem.cs                ← 新增
├── Movement/
│   ├── FormationPosition.cs           ← 新增（可选打磨）
│   └── SeparationBehavior.cs          ← 新增（可选打磨）
├── Command/
│   ├── Command.cs                     ← 修改（Attack 枚举值）
│   └── AttackCommand.cs               ← 新增
├── Entity/
│   ├── Unit.cs                        ← 修改（战斗+航点）
│   ├── Building.cs                    ← 修改（HP/Armor）
│   └── EntityManager.cs               ← 修改（阵营查询+RemoveDead）
└── Data/
    ├── UnitDef.cs                     ← 修改（新字段，含 visionRange 预留）
    └── BuildingDef.cs                 ← 修改（新字段，含 visionRange 预留）

FNARTS.Game/                           [修改]
├── Input/
│   └── RTSInput.cs                    ← 修改（需暴露鼠标位置供 HUD 更新）
└── RTSGame.cs                         ← 修改（集成所有新系统）

tests/FNARTS.Core.Tests/               [新增]
├── Pathfinding/
│   └── PathfinderTests.cs             ← 新增
├── Combat/
│   └── CombatSystemTests.cs           ← 新增
├── Movement/
│   └── SeparationTests.cs             ← 新增（可选）
└── Command/
    └── CommandSystemTests.cs          ← 修改（攻击命令测试）

data/                                  [修改]
├── units/worker.json                  ← 修改（加战斗属性）
├── units/soldier.json                 ← 修改（加战斗属性）
├── units/tank.json                    ← 修改（加战斗属性）
└── buildings/*.json                   ← 全部修改（加 HP/Armor/visionRange 预留）

# 以下文件不再需要（战争迷雾推迟至 Phase 3）：
# FogOfWar/FogOfWarSystem.cs     → Phase 3
# Render/FogOfWarRenderer.cs     → Phase 3
# Camera/Camera2D.GetVisibleWorldRect() → Phase 3
```

---

## 5. RTSGame 游戏主循环修改

### 5.1 初始化新增系统

```csharp
// RTSGame.Initialize() 或 LoadContent() 中新增：

// 寻路器 — 绑定可通过性检查
_pathfinder = new Pathfinder
{
    MapWidth = _map.Width,
    MapHeight = _map.Height,
    IsPassable = coord =>
        _map.InBounds(coord) &&
        _map.IsPassable(coord) &&
        _entities.IsAreaFree(coord, 1, 1)  // 建筑占用格不可通过
};

// 战斗系统
_combatSystem = new CombatSystem();

// 设置玩家阵营（敌我判定）
_commands.PlayerFaction = 0;
```

### 5.2 UpdatePlaying 修改

```
现有循环                              Phase 2 循环
────────                              ────────────
1. Camera pan/zoom                    1. Camera pan/zoom（不变）
2. Placement mode                     2. Placement mode（不变）
3. Panel clicks                       3. Panel clicks（不变）
4. Selection                          4. Selection（不变）
5. Right-click → MoveCommand          5. Right-click → MoveCommand | AttackCommand
6. Escape → pause                     6. Escape → pause（不变）
7. ProductionSystem tick              7. ProductionSystem tick（不变）
8. Unit movement (straight)           8. CombatSystem update：
                                         - 攻击判定 + 自动追击寻路
                                         - 死亡回调：清理选择、清除攻击目标
                                       9. Unit movement（航点跟随）
                                       10. EntityManager.RemoveDead()
```

与初版路线图的区别：
- 去掉了 FogOfWar update（推迟至 Phase 3）
- 去掉了 Separation behavior 独立步骤（合并到 Unit movement 中，可选）
- 战斗处理和移动处理紧密衔接（先处理攻击再移动，同一帧内完成）

### 5.3 右键指令处理

```csharp
// RTSGame.UpdatePlaying() 中右键处理：

var cmd = _commands.ProcessRightClick(worldPosN, _entities, _selection);
if (cmd == null) return;

if (cmd is AttackCommand atkCmd)
{
    foreach (var id in _selection.SelectedEntityIds)
    {
        var entity = _entities.GetEntity(id);
        if (entity is Unit unit)
        {
            unit.AttackTargetId = atkCmd.TargetEntityId;
            unit.MoveTarget = atkCmd.TargetWorldPosition;

            var start = CoordUtil.WorldToIso(unit.WorldPosition);
            var end = CoordUtil.WorldToIso(atkCmd.TargetWorldPosition);
            unit.Path = _pathfinder.FindPath(start, end);
            unit.PathIndex = 0;
        }
    }
}
else if (cmd is MoveCommand moveCmd)
{
    foreach (var id in _selection.SelectedEntityIds)
    {
        var entity = _entities.GetEntity(id);
        if (entity is Unit unit)
        {
            unit.AttackTargetId = null;  // 移动指令清除攻击状态
            unit.MoveTarget = moveCmd.TargetWorldPosition;

            var start = CoordUtil.WorldToIso(unit.WorldPosition);
            var end = CoordUtil.WorldToIso(moveCmd.TargetWorldPosition);
            unit.Path = _pathfinder.FindPath(start, end);
            unit.PathIndex = 0;
        }
    }
}
```

### 5.4 死亡回调

```csharp
// CombatSystem 的 onDeath 回调委托：

private void OnEntityDeath(Entity entity)
{
    // 1. 清除所有以此为攻击目标的单位状态
    foreach (var e in _entities.AllEntities)
    {
        if (e is Unit u && u.AttackTargetId == entity.Id)
        {
            u.AttackTargetId = null;
            u.Path = null;
        }
    }

    // 2. 从选择中移除
    _selection.Deselect(entity);

    // 3. 从实体管理器中移除
    _entities.RemoveDead();

    // 4. 日志
    GameLogger.Info($"Entity died: {entity.GetType().Name}#{entity.Id}");
}
```

### 5.5 Draw 修改

Phase 2 绘制流水线不变（只增加了实体 HP 条的可选渲染，视情况决定）：

```csharp
// RTSGame.Draw() 中 Playing/Paused 状态：

// 1. _tileRenderer.Draw(_spriteBatch, _camera)     // 瓦片
// 2. _entityRenderer.Draw(...)                      // 实体
// 3. _selectionRenderer.DrawDragRect(...)            // 框选矩形
// 4. UI (HUD/CommandPanel/Minimap)                   // UI
```

---

## 6. 测试计划

### 6.1 FNARTS.Core 单元测试（xUnit, 无 GPU）

#### PathfinderTests [新增，最高优先级]

```
Test_FindPath_StraightLine_ReturnsOptimalPath
  → 无障碍时，路径步数为八方向最优步数

Test_FindPath_ObstacleDetour_FindsPathAround
  → U 形障碍时，找到绕行路径而非穿墙

Test_FindPath_NoPath_ReturnsEmptyList
  → 被完全封闭的终点，返回空列表

Test_FindPath_StartEqualsEnd_ReturnsEmpty
  → 起点 = 终点返回空

Test_FindPath_OutOfBoundsStart_ReturnsEmpty
  → 起点越界

Test_FindPath_OutOfBoundsEnd_ReturnsEmpty
  → 终点越界

Test_FindPath_UnpassableEnd_ReturnsEmpty
  → 终点不可通过（建筑/水域）

Test_FindPath_DiagonalCutCorner_Blocked
  → 切角检测：两个正交相邻格不通 → 对角线被拒绝

Test_FindPath_DiagonalCutCorner_Allowed
  → 两个正交相邻格都可通过 → 对角线允许

Test_FindPath_Performance_WorstCaseWithinBudget
  → 51×51 全可通过网格上最长路径 < 5ms

Test_FindPath_MultiplePaths_ConsistentCost
  → 多次寻路结果一致（确定性）

Test_OctileDistance_Symmetric
  → H(a, b) == H(b, a)

Test_OctileDistance_TriangleInequality
  → H(a, c) ≤ H(a, b) + H(b, c)
```

#### CombatSystemTests [新增]

```
Test_CalculateDamage_ArmorReduces
  → AttackDamage=10, Armor=3 → 造成伤害 = 7

Test_CalculateDamage_MinimumOne
  → AttackDamage=5, Armor=10 → 造成伤害 = 1

Test_UnitTakesDamage_HPDecreases
  → ApplyDamage(30) → CurrentHP 减少 30

Test_UnitDies_WhenHPZeroOrLess
  → CurrentHP ≤ 0 → IsAlive = false

Test_BuildingTakesDamage_FromUnit
  → 单位攻击建筑 → 建筑 HP 正确减少

Test_BuildingDies_WhenHPZero
  → 建筑 HP ≤ 0 → IsAlive = false

Test_AttackCooldown_BlocksAttack
  → 攻击后 AttackCooldownTimer > 0 → CanAttack = false

Test_AttackCooldown_DecreasesOverTime
  → dt 流逝 → AttackCooldownTimer 递减至 0 → CanAttack = true

Test_OutOfRange_NoDamageDealt
  → 距离 > AttackRange → 不造成伤害

Test_InRange_DealsDamage
  → 距离 ≤ AttackRange → 造成伤害

Test_DeadTarget_NotAttacked
  → 目标 IsAlive = false → 不造成伤害，AttackTargetId 被清除

Test_SameFaction_NotAttackedByDefault
  → 同阵营单位互不攻击
```

#### UnitTests [修改]

```
Test_UnitWithPath_FollowsWaypoints
  → 设置 3 个航点 → 逐帧 Update → 依次到达每个航点

Test_UnitArrivesAtFinalWaypoint_Stops
  → 到达最后一个航点后停止（PathIndex 到达 Path.Count）

Test_UnitAttackCommand_SetsAttackTarget
  → 发出 AttackCommand → AttackTargetId 被设置

Test_UnitClearOrders_ResetsState
  → ClearOrders() → MoveTarget, Path, AttackTargetId 均被清空
```

#### CommandSystemTests [修改]

```
Test_RightClick_EnemyUnit_ReturnsAttackCommand
  → 右键敌方单位 → 返回 AttackCommand

Test_RightClick_FriendlyUnit_ReturnsMoveCommand
  → 右键友方单位 → 返回 MoveCommand（非 AttackCommand）

Test_RightClick_Ground_ReturnsMoveCommand
  → 右键空地 → 返回 MoveCommand（Phase 1 行为不变）

Test_RightClick_DeadEnemy_ReturnsMoveCommand
  → 右键已死亡敌方 → 返回 MoveCommand（空地处理）
```

### 6.2 FNARTS.Game 集成测试（headless FNA + 像素断言）

Phase 2 集成测试保持 Phase 1 现有的 4 个测试通过。新增集成测试可根据需要添加：

#### CombatIntegrationTests [可选]

```
Test_CombatLoop_RenderVerifiesUnitDeath
  → 渲染两军交战 → 验证单位死亡后不再绘制
```

### 6.3 FNA_Test 基础设施测试

1. **RTS/Pathfinding** — A\* 算法正确性（需要创建 FNA_Test 测试子项目）
2. **RTS/CombatLogic** — 战斗逻辑单元测试（纯逻辑，headless 模式）

---

## 7. 实现顺序

重新设计实现顺序，核心原则：**寻路先行 → 战斗数据与逻辑连续推进 → 尽早端到端可玩 → 最后打磨**。

### 第 1 步：数据层扩展（约 0.5 天）

**依赖**：Phase 1 完成
**产出**：新字段 + JSON 数据更新 + 编译验证

1. 修改 `FNARTS.Core/Data/UnitDef.cs` — 新增 6 个字段（含 visionRange 预留）
2. 修改 `FNARTS.Core/Data/BuildingDef.cs` — 新增 3 个字段（含 visionRange 预留）
3. 更新 `data/units/*.json`（3 个文件）— 添加战斗属性
4. 更新 `data/buildings/*.json`（7 个文件）— 添加 HP/Armor
5. `dotnet build` + `dotnet test tests/FNARTS.Core.Tests/` — 验证 0 错误，93 测试保持通过

### 第 2 步：A\* 寻路系统 + 航点移动（约 3-4 天）【最优先】

**依赖**：CoordUtil, IsoCoord（已存在），对第 1 步无强依赖
**产出**：Pathfinder + Unit 航点移动 + 全面测试

1. `FNARTS.Core/Pathfinding/PathNode.cs`
2. `FNARTS.Core/Pathfinding/Pathfinder.cs` — A\* 算法 + 最小堆优先队列
3. 修改 `FNARTS.Core/Entity/Unit.cs` 的 `Update()` — 航点跟随替代直线移动
4. `tests/FNARTS.Core.Tests/Pathfinding/PathfinderTests.cs` — ~13 测试用例
5. 修改 `tests/FNARTS.Core.Tests/Entity/UnitTests.cs` — 航点跟随测试
6. 手动性能基准：51×51 网格最坏情况 < 5ms
7. **在 RTSGame 中先集成寻路**：右键移动指令使用寻路 → 验证单位绕开建筑移动（此时攻击尚未实现，Phase 1 移动行为升级）

> **设计理由**：寻路是战斗系统的基础（单位需要走近目标才能攻击），且寻路完全独立于战斗数据。先完成寻路 + 航点移动可以：
> - 立即让 Phase 1 的移动体验升级（绕开建筑而非直线穿过）
> - 为后续战斗的自动追击提供经过验证的寻路能力
> - 寻路测试不依赖任何战斗属性，可以独立验证

### 第 3 步：战斗属性 + 战斗系统（约 3-4 天）

**依赖**：第 1、2 步
**产出**：Unit/Building 战斗属性, CombatSystem, AttackCommand, 全面测试

1. 修改 `FNARTS.Core/Entity/Unit.cs` — 新增 CurrentHP, AttackTargetId, AttackCooldownTimer 等战斗状态
2. 修改 `FNARTS.Core/Entity/Building.cs` — 新增 CurrentHP
3. `FNARTS.Core/Combat/CombatSystem.cs`
4. `FNARTS.Core/Command/AttackCommand.cs`
5. 修改 `FNARTS.Core/Command/Command.cs` — CommandType.Attack
6. 修改 `FNARTS.Core/Command/CommandSystem.cs` — ProcessRightClick 敌我判定
7. `tests/FNARTS.Core.Tests/Combat/CombatSystemTests.cs` — ~12 测试
8. 修改 `tests/FNARTS.Core.Tests/Command/CommandSystemTests.cs` — ~3 新测试
9. 修改 `tests/FNARTS.Core.Tests/Entity/UnitTests.cs` — ~2 新测试

### 第 4 步：RTSGame 集成（约 2 天）

**依赖**：第 2、3 步
**产出**：完整战斗循环端到端可玩

1. 修改 `RTSGame.cs` — 初始化 Pathfinder, CombatSystem
2. 修改 `UpdatePlaying()` — 战斗处理、死亡回调、右键攻击指令
3. 修改 `EntityManager.cs` — RemoveDead, GetFactionEntities, GetEnemyEntities
4. 手动游戏测试：放置建筑 → 训练军队 → 右键敌方 → 寻路接近 → 攻击 → 死亡

> **关键里程碑**：此时完整的战斗循环（寻路→接近→攻击→死亡）已可玩。后续步骤为打磨。

### 第 5 步：编队移动打磨（约 1-1.5 天）

**依赖**：第 4 步
**产出**：多单位编队排列 + 局部避障（可选）

1. `FNARTS.Core/Movement/FormationPosition.cs`
2. `FNARTS.Core/Movement/SeparationBehavior.cs`
3. 在 `RTSGame.UpdatePlaying()` 中集成分离力
4. `tests/FNARTS.Core.Tests/Movement/SeparationTests.cs` — 可选

> **as-built**：第 5 步的实际产出为 `GroupMovement.cs`（RA1 式相对偏移编队）+ `MovementSystem.cs`（OpenRA 式进出格仲裁），详见 4.8 节说明；编队当前默认关闭（多选 = 聚集移动）。

> **如果时间紧张，编队移动可以推迟到 Phase 3。** 核心战斗循环（单单位攻击）在步骤 4 已经完整。

### 第 6 步：FNA_Test 基础设施 + 全面测试（约 1.5-2 天）

**依赖**：第 4 步
**产出**：全部测试通过，无回归

1. `FNA_Test/RTS/Pathfinding/` — 寻路测试
2. `FNA_Test/RTS/CombatLogic/` — 战斗逻辑测试
3. `dotnet test tests/FNARTS.Core.Tests/` — 全部通过（93 + 新增 ~30）
4. `dotnet run --project tests/FNARTS.Game.Tests/ -- --headless` — 全部通过
5. `dotnet build` — 0 错误 0 警告
6. 交互模式手动测试：编队移动 → 混战 → 建筑攻防
7. 性能测试：50+ 单位同屏战斗保持 60fps

---

## 8. 验收检查清单

### 寻路

- [ ] 右键移动时单位沿 A\* 路径绕过障碍物，而非直线穿过
- [ ] 单位不穿过建筑/水域/悬崖/不可通过地形
- [ ] 无路径时（被完全包围）单位原地不动
- [ ] 多单位同时寻路性能正常（单次寻路 < 1ms）
- [ ] 单位沿路径航点逐段移动，在航点处平滑转向

### 战斗

- [ ] 右键敌方单位 → 自动寻路接近 → 进入射程 → 开始攻击
- [ ] 攻击伤害 = AttackDamage − Armor（最小 1）
- [ ] HP 归零后单位/建筑正确移除（不再渲染、不再可选）
- [ ] 攻击冷却时间内不能再次攻击
- [ ] 目标死亡后攻击者停止攻击并原地待命
- [ ] 右键友方单位 → 移动到其位置（非攻击）
- [ ] 已死亡单位不能被选为攻击目标
- [ ] 移动指令清除当前攻击状态
- [ ] 建筑可以被攻击和摧毁

### 生产系统（Phase 1 回归）

- [ ] 建筑生产队列继续正常工作
- [ ] 训练出的单位具有正确的战斗属性（HP、攻击力等）
- [ ] 训练完成的单位在建筑旁正确生成

### 编队移动

- [ ] 选中多个单位右键移动 → 到达目标后不严重重叠
- [ ] 多个单位同时攻击同一目标时不堆叠
- [ ] 单位移动中保持合理间距（无推挤穿透）

### 数据驱动

- [ ] JSON 中定义的 HP/Attack/Armor 等战斗属性在游戏中正确生效
- [ ] Phase 1 JSON 文件缺少新字段时使用默认值（向后兼容）
- [ ] ConfigLoader 正确加载所有新增字段
- [ ] visionRange 字段已定义但 Phase 2 暂不使用（Phase 3 就绪）

### 测试

- [ ] 所有 FNARTS.Core 单元测试通过（93 + 新增 ~30）
- [ ] 所有 FNARTS.Game 集成测试在 headless 模式下通过
- [ ] `dotnet build` 0 错误 0 警告
- [ ] FNA_Test/RTS/Pathfinding, CombatLogic 测试通过
- [ ] 所有 Phase 1 测试回归通过

---

## 9. 技术风险与缓解

| 风险 | 概率 | 影响 | 缓解策略 |
|------|------|------|---------|
| **A\* 寻路性能**：51×51 网格上大批量寻路超过帧预算 | 低 | 中 | 使用最小堆优先队列；限制 MaxIterations；大批量寻路时分帧处理；追击每 30 帧才重新寻路 |
| **自动追击寻路频率**：移动目标每帧重新寻路浪费 CPU | 低 | 低 | 已设计为每 30 帧重新寻路一次；目标位置变化小于 1 格时不重新寻路 |
| **编队位置落在不可通过格**：编队目标位置可能指向建筑/水域 | 中 | 低 | 编队位置用于最终停靠，单位到达后会停在最近可通过位置；后期可预先验证编队位置可达性 |
| **死亡实体残留引用**：攻击者仍持有已死亡目标的 ID 导致空引用 | 低 | 高 | CombatSystem 在目标死亡时立即清除所有指向它的 AttackTargetId；onDeath 回调统一处理 |
| **向后兼容性破坏**：新增字段导致现有 JSON 反序列化失败 | 低 | 中 | 所有新字段均有 C# 默认值；`System.Text.Json` 序列化器自动跳过未定义字段 |
| **编队移动卡死**：分离力过大导致单位振荡 | 低 | 低 | 分离力乘以小系数（0.5）；只在距离 < SEPARATION_RADIUS 时生效；不参与寻路，只在移动向量上叠加 |

---

## 10. Phase 2 → Phase 3 过渡准备

Phase 2 必须继续遵循 Phase 1 建立的确定性设计原则，为 Phase 3 帧同步网络打好基础：

1. **所有逻辑在 Core 层运算**：`CombatSystem.Update()` 接收固定 `dt = 1/60f`，不依赖渲染状态。
2. **EntityId 保持不变**：已有的 `EntityIdGenerator` 为 Phase 3 预留了 `NextForFaction()` 扩展点。
3. **确定性遍历顺序**：`CombatSystem` 遍历实体前先 `.OrderBy(e => e.Id)` 保证跨客户端一致。
4. **寻路确定性**：A\* 使用确定性数据结构（MinHeap 同值按加入顺序）；`IsPassable` 委托返回纯逻辑结果。
5. **随机数预留**：如果将来需要暴击/伤害浮动等随机元素，在 Phase 3 中使用 `System.Random` + 同步种子。
6. **定点数评估**：当前使用浮点数进行移动和距离计算。Phase 3 需要评估是否切换到定点数（fixed-point, 1/256 精度）。寻路代价使用 `int` 已经是定点安全的。
7. **VisionRange 字段已定义**：`UnitDef.VisionRange` 和 `BuildingDef.VisionRange` 在 Phase 2 已定义默认值。Phase 3 实现战争迷雾时，数据格式无需变更。
8. **战争迷雾设计已就绪**：FogOfWarSystem + FogOfWarRenderer 的接口设计在本文档初版中已完成，Phase 3 可直接参考实现。

---

## 11. 附录

### A. 架构对比：Phase 1 vs Phase 2

| 维度 | Phase 1 | Phase 2 |
|------|---------|---------|
| 单位移动 | 直线朝向 MoveTarget | A\* 航点跟随 |
| 右键交互 | 全部 = 移动指令 | 敌方 = 攻击，友方/空地 = 移动 |
| 实体属性 | Id, Position, Faction | + CurrentHP, Armor |
| 指令类型 | Move, Build | + Attack |
| 多单位 | 各自独立 | 同目标格聚集 + OpenRA 式进出格仲裁（编队默认关闭） |
| 测试数量 | 93 Core + 4 Game | ~120 Core + 4 Game |
| 战争迷雾 | 无 | 推迟至 Phase 3 |

### B. 关键参考文件

| 文件 | 内容 |
|------|------|
| `docs/DEVELOPMENT_PLAN.md` | 整体开发路线（Phase 2 定义在第 5 节） |
| `docs/PHASE1_DEVELOPMENT_PLAN.md` | Phase 1 详细设计文档（本文档的参考模板） |
| `docs/PHASE1_COMPLETION_REPORT.md` | Phase 1 完成状态和 as-built 架构 |
| `src/FNARTS.Core/Entity/Unit.cs` | 当前 Unit 实现（Phase 2 主要修改对象） |
| `src/FNARTS.Core/Entity/Building.cs` | 当前 Building 实现（Phase 2 修改对象） |
| `src/FNARTS.Core/Command/CommandSystem.cs` | 当前指令系统（Phase 2 扩展攻击指令） |
| `src/FNARTS.Core/Data/UnitDef.cs` | 当前单位定义（Phase 2 扩展字段） |
| `src/FNARTS.Game/RTSGame.cs` | 游戏主循环（Phase 2 集成点） |
| `../FNA_Test/CLAUDE.md` | 测试基础架构、HLSL 顶点约定 |

### C. 关键常量

| 常量 | 值 | 说明 |
|------|-----|------|
| `TILE_WIDTH` | 64 | 瓦片纹理宽度（像素） |
| `TILE_HEIGHT` | 32 | 瓦片纹理高度（像素） |
| `FIXED_DT` | 1/60f | 固定逻辑帧步长（秒） |
| `PATHFIND_REPATH_INTERVAL` | 30 | 追击重新寻路间隔（帧数） |
| `SEPARATION_RADIUS` | 24f | 局部避障触发距离（已废弃：软推挤被 MovementSystem 仲裁取代） |
| `FORMATION_SPACING` | 48f | 编队单位间距（已废弃：固定方阵被相对偏移快照取代） |
| `MAP_DEFAULT_SIZE` | 51 | 默认地图尺寸（网格格数） |
| `MAX_PATH_ITERATIONS` | 2500 | A\* 最大搜索迭代数 |
