# FNA_RTS 开发计划

## 1. 项目概述

### 1.1 项目定位

基于 FNA HLSL 分支实现的 2.5D 即时战略（RTS）游戏基础框架。视觉风格与《命令与征服2》《帝国时代2》《星际争霸1》一致：**2.5D 等距固定视角 + 方格地图**。游戏方法包含建造、生产、指挥三大核心循环。

### 1.2 参考游戏

| 游戏 | 参考要点 |
|------|---------|
| 命令与征服2 (C&C: Tiberian Sun) | 2.5D 等距视角、建筑放置、单位生产队列 |
| 帝国时代2 (AoE2) | 方格地图、资源采集、科技树、阵营设计 |
| 星际争霸1 (SC1) | 帧同步网络、确定性逻辑、录像回放 |

### 1.3 技术栈

| 层级 | 技术 |
|------|------|
| 运行时 | .NET 10.0 (C#) |
| 图形库 | FNA (XNA 4.0 复刻, HLSL 分支) |
| 图形后端 | FNA3D_HLSL (Vulkan via SDL_GPU, DXC→SPIR-V) |
| 平台 | Linux (Vulkan 驱动) |
| 着色器 | HLSL SM 6.0 → DXC → SPIR-V → FEB |
| 测试 | FNA_Test 的 TestHarness 模式 (headless + 像素断言) |

### 1.4 仓库关系

```
FNA_RTS/                  ← 本项目 (RTS 游戏)
  ├── src/FNARTS.Core/    ← 纯逻辑库 (无渲染依赖, 可单元测试)
  ├── src/FNARTS.Game/    ← FNA 游戏宿主 (渲染, 输入, 音频)
  └── tests/              ← 渲染/集成测试

../FNA/                   ← FNA C# 库 (XNA 4.0)
../FNA/lib/FNA3D/         ← FNA3D_HLSL 原生图形库
../FNA_Test/              ← 框架验证测试 (RTS 基础设施测试将添加于此)
../FNA3D_HLSL_Test/       ← 原生渲染管线测试
```

---

## 2. 整体架构设计

### 2.1 分层架构

```
┌─────────────────────────────────────────────────┐
│                  UI Layer                        │
│  HUD (命令面板, 小地图, 资源栏)                    │
│  重用 ../FNA_Test/Gui/ 的 Widget 体系              │
├─────────────────────────────────────────────────┤
│                Game Layer                        │
│  游戏状态机, 场景管理, 游戏规则, 阵营数据            │
├─────────────────────────────────────────────────┤
│               Systems Layer                      │
│  ┌──────────┬──────────┬──────────┬───────────┐ │
│  │ 渲染系统  │ 输入系统  │ 选择系统  │ 指令系统   │ │
│  │MapRenderer│InputCtrl │Selection │CommandSys │ │
│  ├──────────┼──────────┼──────────┼───────────┤ │
│  │ 战斗系统  │ 生产系统  │ 寻路系统  │ 网络系统   │ │
│  │CombatSys │ProductSys│Pathfind  │NetSys     │ │
│  └──────────┴──────────┴──────────┴───────────┘ │
├─────────────────────────────────────────────────┤
│               Entity Layer                       │
│  实体管理器, Unit/ Building/ Projectile/ Resource  │
├─────────────────────────────────────────────────┤
│                Core Layer                        │
│  坐标变换, 网格地图, 空间索引, 数学工具, 数据定义    │
└─────────────────────────────────────────────────┘
```

### 2.2 DLL 拆分原则

| 程序集 | 依赖 | 职责 |
|--------|------|------|
| `FNARTS.Core` | 无 (纯 C#) | 游戏逻辑、数据结构、坐标数学、寻路、战斗计算 |
| `FNARTS.Game` | FNARTS.Core, FNA.Core | 渲染、输入、音频、FNA 生命周期 |
| `FNARTS.Tests` | FNARTS.Core, FNARTS.Game | 集成测试、渲染验证 |

**核心原则**：`FNARTS.Core` 不依赖 FNA，所有逻辑可脱离 GPU 进行单元测试。

### 2.3 核心系统一览

| 系统 | 所属层 | 职责 | 阶段 |
|------|-------|------|------|
| `Camera2D` | Game | 平移 (WASD/边缘滚动) + 缩放 (滚轮), 输出变换矩阵 | P1 |
| `TileMap` | Core | 网格地图数据, 地形类型, 可通过性 | P1 |
| `IsometricRenderer` | Game | SpriteBatch 等距瓦片渲染, 纹理图集管理 | P1 |
| `EntityManager` | Core | 实体生命周期, 空间索引 (网格哈希) | P1 |
| `InputController` | Game | 鼠标/键盘事件映射为游戏指令 | P1 |
| `SelectionSystem` | Core | 单击/框选, 选中集合管理 | P1 |
| `CommandSystem` | Core | 移动/攻击/建造/生产指令的生成与执行 | P1 |
| `ProductionSystem` | Core | 建筑生产队列, 单位训练计时 | P2 |
| `CombatSystem` | Core | 攻击判定, 伤害计算, HP/护甲 | P2 |
| `Pathfinding` | Core | A\* 网格寻路, 群体移动 | P2 |
| `FogOfWar` | Core | 战争迷雾, 视野系统 | P2 |
| `FactionSystem` | Core | 阵营定义, 科技树, 非对称平衡 | P3 |
| `NetSystem` | Core | 帧同步, 确定性逻辑, 输入广播 | P3 |
| `ReplaySystem` | Core | 指令录制与回放 (基于帧同步输入流) | P3 |
| `AISystem` | Core | AI 对手 (基于规则/有限状态机) | P3 |

### 2.4 游戏状态机

整个项目生命周期由顶层状态机管理。Phase 1 至少需要两个状态，后续阶段扩展。

```
状态机 (GameStateMachine):

  [启动] → Loading → MainMenu
                         ├─ NewGame → Playing
                         ├─ LoadReplay → Replaying (P3)
                         └─ Multiplayer → Lobby → Playing (P3)

  Playing:
    ├─ Pause → Paused → Playing
    ├─ GameOver → GameOverScreen → MainMenu
    └─ Quit → [退出]

  Phase 1 实现: Loading → MainMenu → Playing (↔ Paused)
  Phase 3 扩展: Lobby, Replaying
```

**状态与系统可见性**：

| 状态 | 活动系统 |
|------|---------|
| `Loading` | AssetProvider 初始化, 着色器编译 |
| `MainMenu` | UI (菜单按钮) |
| `Playing` | 全部游戏系统 (渲染, 输入, 实体, 指令) |
| `Paused` | 渲染 (冻结), UI (设置面板), 输入 (仅 UI) |

### 2.5 确定性更新基础

帧同步（Phase 3）要求所有客户端在相同输入下产生相同状态。Phase 1 必须为此打好地基，避免后期大规模重构。

**核心规则（从 Phase 1 开始强制）**：

1. **固定时间步长**：`Game.IsFixedTimeStep = true`，`TargetElapsedTime = 1/60s`。所有逻辑在 `Update()` 中以 `dt = 1/60f` 驱动，禁止变步长。

2. **Update/Draw 严格分离**：逻辑计算只在 `Update()` 中进行，`Draw()` 只做渲染。Draw 中的任何状态读取必须视为"最终展示"，不可反馈到 Update。

3. **逻辑层不接触 FNA 类型**：`FNARTS.Core` 零 FNA 依赖。`Vector2` 等数学类型在 Core 中使用 `System.Numerics.Vector2`（或自定 `FVector2` struct），避免与 FNA 的 `Microsoft.Xna.Framework.Vector2` 耦合。

4. **禁止逻辑依赖渲染状态**：不能在 Update 中读取 `GraphicsDevice` 状态做逻辑分支。

5. **确定性随机数**：使用 `System.Random` 并记录种子。所有随机数从同一个确定性 RNG 获取。

6. **排序确定迭代**：遍历集合前必须排序（`OrderBy`），保证不同客户端遍历顺序一致。

---

## 3. 坐标系统设计

### 3.1 等距投影 (2:1 Dimetric)

采用经典 2:1 等距投影（与 AoE2/SC1 相同）：

```
世界网格坐标 (wx, wy) → 屏幕像素坐标 (sx, sy):

  sx = (wx - wy) * (TILE_WIDTH / 2)
  sy = (wx + wy) * (TILE_HEIGHT / 2)

其中 TILE_WIDTH : TILE_HEIGHT = 2 : 1 (如 64×32 像素)
```

**网格坐标轴定义**：
- `wx` 向右下增加（屏幕 x 正向）
- `wy` 向右上增加（屏幕 x 正向，屏幕 y 负向）

### 3.2 屏幕拾取（逆变换）

```
屏幕像素 (sx, sy) → 世界坐标 (worldX, worldY):

  worldX = (sx / (TILE_WIDTH/2) + sy / (TILE_HEIGHT/2)) / 2
  worldY = (sy / (TILE_HEIGHT/2) - sx / (TILE_WIDTH/2)) / 2

再 floor 取整得到网格单元格 (wx, wy)
```

### 3.3 深度排序

等距视角中，"屏幕下方 = 离摄像机更近"，遮挡关系由世界 Y 坐标决定：

```
layerDepth = 1.0f - (worldY / mapHeight)
```

使用 `SpriteSortMode.BackToFront` + `layerDepth` 实现正确的绘制顺序。

### 3.4 摄像机变换矩阵

2D 摄像机通过平移和缩放构建视图矩阵，传入 `SpriteBatch.Begin(transformMatrix:)`：

```
ViewMatrix = Translation(-cameraPos) * Scale(zoom) * Translation(viewportCenter)
```

屏幕像素 → 世界坐标的拾取使用 `ViewMatrix` 的逆矩阵。

---

## 4. 第一阶段：MVP（基础可玩最小系统）

### 4.1 目标

实现单击基础可玩循环：**看到地图 → 放置建筑 → 生产单位 → 选择和指挥单位移动**。

### 4.2 功能范围

| 功能 | 描述 |
|------|------|
| 等距地图渲染 | 从瓦片图集渲染方格地图，支持地形类型（草地/水域/悬崖） |
| 摄像机控制 | WASD/方向键平移，鼠标滚轮缩放，边缘滚动 |
| 建筑系统 | 放置建筑到网格（需有效地形），建筑占用多格 |
| 单位系统 | 单位渲染、选择（单击/框选）、右键移动指令 |
| 基本 UI | 命令面板（显示选中单位信息），小地图 |

### 4.3 不在范围内

- 战斗系统（攻击、HP）
- 生产队列（建筑训练单位）
- 寻路（单位沿直线移动即可）
- 战争迷雾
- 网络
- AI

### 4.4 FNA_Test 前置依赖

MVP 需要的 FNA_Test 基础设施测试（在 FNA_Test 中先完成）：

| 测试项目 | 验证能力 | 优先级 |
|---------|---------|--------|
| `RTS/Camera2D` | 2D 摄像机平移/缩放，变换矩阵输出 | 必须 |
| `RTS/PrimitiveLines` | DrawUserPrimitives 线条渲染 (网格/选框) | 必须 |
| `RTS/IsometricTiles` | 等距瓦片地图 SpriteBatch 渲染 | 必须 |
| `RTS/ScreenToWorld` | 屏幕像素→世界→网格坐标变换 | 必须 |
| `RTS/DepthSorting` | 基于 Y 坐标的 SpriteBatch 深度排序 | 必须 |
| `RTS/RectSelection` | 拖拽框选单位 | 必须 |
| `RTS/Minimap` | RenderTarget2D 缩微小地图 | 可选 |

### 4.5 验收标准

1. 10×10 等距瓦片地图正确渲染，地形纹理清晰可辨
2. 摄像机在 800×600 视口内平滑平移和缩放
3. 单击网格放置建筑，建筑精灵正确显示在瓦片上
4. 单击选中单位（高亮指示），右键空地单位移动到目标位置
5. 框选多个单位，所有被框单位同时高亮
6. 所有测试在 headless 模式下通过（`dotnet run -- --headless`）

---

## 5. 第二阶段：战斗与兵种系统

### 5.1 目标

实现基础战斗循环和可配置的兵种体系结构。

### 5.2 功能范围

| 功能 | 描述 |
|------|------|
| 战斗系统 | 攻击指令，HP/护甲/伤害计算，单位死亡与移除 |
| 兵种体系 | 数据驱动的单位/建筑定义（JSON），属性：HP、速度、攻击力、攻击范围、攻击冷却 |
| 生产队列 | 建筑训练单位（指定类型和数量），训练计时 |
| 寻路 | A\* 网格寻路，避开建筑物和不可通过地形 |
| 群体移动 | 多单位同时移动，避免重叠（局部避障） |
| 战争迷雾 | 视野范围，已探索/可见/不可见三层状态 |

### 5.3 兵种体系设计

```
UnitDefinition
├── 基础属性: Id, Name, HP, MoveSpeed, BuildTime, Cost
├── 战斗属性: AttackDamage, AttackRange, AttackCooldown, Armor
├── 渲染属性: SpriteSheet, FrameSize, AnimFrames
├── 分类标签: Infantry/Vehicle/Aircraft, Light/Heavy
└── 生产前提: RequiredBuilding, TechLevel
```

### 5.4 FNA_Test 前置依赖

| 测试项目 | 验证能力 |
|---------|---------|
| `RTS/Pathfinding` | A\* 寻路正确性 |
| `RTS/FogOfWar` | 战争迷雾渲染 |
| `RTS/CombatLogic` | 战斗逻辑单元测试 |

### 5.5 验收标准

1. 单位接受攻击指令后移动到目标并开始攻击
2. 目标 HP 降至 0 后正确移除
3. JSON 定义的兵种属性在游戏中正确生效
4. 建筑生产队列按计时完成单位训练
5. A\* 寻路找到绕过障碍物的最短路径
6. 战争迷雾正确遮蔽未探索区域

---

## 6. 第三阶段：阵营平衡与帧同步网络

### 6.1 目标

实现非对称阵营体系和帧同步多人对战。

### 6.2 功能范围

| 功能 | 描述 |
|------|------|
| 阵营系统 | 2-3 个阵营，不同建筑/单位/科技树，非对称平衡 |
| 帧同步网络 | Lockstep 模式，所有客户端运行相同逻辑，只同步玩家输入 |
| 确定性逻辑 | 定点数替代浮点数，固定时间步长，消除所有不确定性源 |
| 录像回放 | 基于帧同步输入流录制，完整回放对局 |
| AI 对手 | 基于有限状态机的 AI（建造顺序、编队攻击） |
| 平衡性测试 | 自动批量模拟对战，统计胜率 |

### 6.3 帧同步架构

```
游戏循环 (固定 60Hz Tick):

Tick N:
  1. 收集本地玩家输入 (mouse/keyboard actions)
  2. 广播输入到所有客户端 (via UDP, reliable layer)
  3. 等待所有玩家输入就绪 (turn-based: N+delay 的输入)
  4. 确定性 Update(dt=1/60) — 所有客户端执行相同代码
  5. Render() — 客户端独立渲染 (允许差异, 不影响逻辑)
```

关键约束：
- 所有游戏逻辑使用**定点数** (fixed-point, 如 1/256 精度)，禁止 `float`/`double`
- `GameTime` 使用固定步长 `1/60s`，禁用变步长
- 禁止依赖渲染状态做逻辑判断
- 集合遍历顺序必须确定 (排序后遍历)

### 6.4 验收标准

1. 两个阵营各有独特的建筑/单位/科技路径
2. 自动模拟 1000 场对战，胜率在 45%-55% 之间
3. 两个客户端帧同步运行 30 分钟无不一致
4. 录像文件可完整回放对局，结果一致
5. AI 能执行基础建造顺序和编队攻击

---

## 7. 测试驱动开发策略

### 7.1 三层测试

```
┌──────────────────────────────────────┐
│         集成/渲染测试 (FNARTS.Tests)    │
│  FNA headless 模式, 像素断言           │
│  验证: "地图正确渲染了吗?"               │
├──────────────────────────────────────┤
│       逻辑单元测试 (FNARTS.Core)        │
│  xUnit / NUnit, 纯 C#, 无 GPU         │
│  验证: "寻路结果是最短路径吗?"           │
├──────────────────────────────────────┤
│    FNA 框架能力测试 (FNA_Test/RTS/)     │
│  验证: "SpriteBatch能正确做深度排序吗?"   │
│  在 FNA_Test 中先建立 → 本项目中使用      │
└──────────────────────────────────────┘
```

### 7.2 开发流程

```
新功能开发:
  1. 确认 FNA_Test 中是否有相关能力的测试
     ├─ 有 → 使用已验证的模式
     └─ 无 → 先在 FNA_Test 中添加测试, 验证 FNA 能力
  2. 在 FNARTS.Core 中实现纯逻辑 → 编写单元测试
  3. 在 FNARTS.Game 中实现渲染/输入 → 编写集成测试
  4. 在 headless 模式下运行所有测试
  5. 运行 FNA_Test/run_tests.sh 确认回归
```

### 7.3 测试模板

核心逻辑单元测试（FNARTS.Core，无 GPU 依赖）：

```csharp
[Fact]
public void IsometricToScreen_Origin_ReturnsCenter()
{
    var result = CoordUtil.IsoToScreen(0, 0, tileWidth: 64, tileHeight: 32);
    Assert.Equal(0, result.X);
    Assert.Equal(0, result.Y);
}
```

渲染集成测试（FNARTS.Tests，headless FNA）：

```csharp
TestHarness.Tick(this, 3, () =>
{
    var px = TestHarness.ReadBackbuffer(GraphicsDevice);
    int fails = 0;
    fails += TestHarness.AssertCoverage(px, clearColor, 0.3f, "tile-coverage");
    fails += TestHarness.AssertPixel(px, w, centerX, centerY, expected, 5, "center-tile");
    TestHarness.Report("TileMap_Render", fails);
});
```

### 7.4 CI 集成

```bash
# 1. FNARTS.Core 单元测试 (无 GPU, 极快)
dotnet test tests/FNARTS.Core.Tests/

# 2. FNA_Test RTS 基础设施回归 (headless, 需要 Vulkan 驱动)
cd ../FNA_Test && ./run_tests.sh

# 3. FNARTS.Game 集成测试 (headless)
dotnet run --project tests/FNARTS.Integration.Tests/ -- --headless
```

---

## 8. 里程碑与交付物

### M1: 基础渲染 (预估 2-3 周)

- [ ] FNA_Test 中完成 RTS/Camera2D, IsometricTiles, ScreenToWorld, DepthSorting 测试
- [ ] FNARTS.Core: 坐标变换、TileMap、Entity 基类
- [ ] FNARTS.Game: 等距地图渲染、摄像机、鼠标拾取
- [ ] 10×10 测试地图可交互浏览

### M2: MVP 可玩 (预估 3-4 周)

- [ ] FNA_Test 中完成 RectSelection, PrimitiveLines 测试
- [ ] FNARTS.Core: SelectionSystem, CommandSystem
- [ ] FNARTS.Game: 建筑放置、单位渲染、选择与移动
- [ ] 完整的"放置建筑→生产单位→移动单位"循环

### M3: 战斗系统 (预估 3-4 周)

- [ ] FNA_Test 中完成 Pathfinding 测试
- [ ] FNARTS.Core: CombatSystem, ProductionSystem, Pathfinding
- [ ] JSON 兵种数据定义（至少 2 个建筑类型 + 4 个单位类型）
- [ ] 基础战斗循环可玩

### M4: 阵营与网络 (预估 4-6 周)

- [ ] FNA_Test 中完成相关测试
- [ ] FNARTS.Core: FactionSystem, NetSystem, ReplaySystem
- [ ] 2 阵营非对称定义
- [ ] 双人帧同步对战可玩
- [ ] AI 对手可玩

### M5: 打磨与平衡 (持续)

- [ ] 1000 场自动模拟平衡测试
- [ ] 性能优化 (100+ 单位同屏 60fps)
- [ ] UI 完善 (生产面板、科技树面板、计分板)

---

## 9. 工程实践与质量保障

### 9.1 配置管理

所有可调参数从数据文件中加载，不硬编码在源码中。配置文件分层：

| 层级 | 格式 | 示例 | 位置 |
|------|------|------|------|
| 引擎配置 | JSON | 分辨率、全屏、音量、键位绑定 | `config/settings.json` |
| 游戏数据 | JSON | 单位定义、建筑定义、科技树 | `data/units/*.json` |
| 地图数据 | JSON | 地形网格、起始位置、资源分布 | `data/maps/*.json` |
| 调试选项 | 命令行参数 | `--headless`, `--skip-menu`, `--debug-render` | CLI args |

**键位绑定（输入动作映射）**：不直接硬编码 `Keys.A`，而是定义动作→按键映射表：

```json
{
  "actions": {
    "camera_pan_up":    ["KeyW", "ArrowUp"],
    "camera_pan_left":  ["KeyA", "ArrowLeft"],
    "camera_pan_down":  ["KeyS", "ArrowDown"],
    "camera_pan_right": ["KeyD", "ArrowRight"],
    "camera_zoom_in":   ["ScrollUp"],
    "camera_zoom_out":  ["ScrollDown"],
    "select":           ["MouseLeft"],
    "command":          ["MouseRight"],
    "shift_modifier":   ["ShiftLeft", "ShiftRight"],
    "ctrl_modifier":    ["ControlLeft", "ControlRight"]
  }
}
```

代码中使用 `InputAction.CameraPanUp` 而非 `Keys.W`，键位可随时重新绑定。

### 9.2 日志与诊断

使用结构化日志，分级输出：

| 级别 | 用途 | 示例 |
|------|------|------|
| `ERROR` | 致命错误，程序无法继续 | Vulkan 设备丢失、FEB 加载失败 |
| `WARN` | 非致命异常，需关注 | 纹理缺失回退到占位、帧时间超 33ms |
| `INFO` | 关键状态变更 | 游戏状态切换、地图加载完成 |
| `DEBUG` | 开发调试信息 | 实体创建/销毁、指令队列长度、选中集合变化 |
| `TRACE` | 每帧详细数据 | 渲染 Draw Call 数、可见瓦片数、FPS |

**实现**：`FNARTS.Core` 中使用 `Microsoft.Extensions.Logging` 的 `ILogger<T>` 接口（纯抽象，无平台依赖），`FNARTS.Game` 中桥接到 `Console` 或文件输出。

**关键诊断指标**（通过 ImGui 调试覆盖层显示）：

| 指标 | 说明 | 目标 |
|------|------|------|
| FPS | 帧率 | 60fps 稳定 |
| Frame Time | 单帧耗时 (ms) | Update < 5ms, Draw < 11ms |
| Draw Calls | SpriteBatch Flush 次数 | < 50/帧 |
| Visible Tiles | 视锥剔除后瓦片数 | < 500 |
| Entity Count | 活跃实体数 | 记录峰值 |
| Memory | 托管内存 (GC.GetTotalMemory) | 监控泄漏 |

### 9.3 错误处理策略

```
致命错误 (Fatal)        → 日志 + 弹窗 + 退出
│  Vulkan设备丢失、FNA初始化失败
│
可恢复错误 (Recoverable) → 日志 + 降级 + 继续
│  纹理加载失败 → 占位纹理
│  着色器编译失败 → FNA内置默认Effect
│  配置文件损坏 → 默认配置
│
逻辑错误 (Assertion)     → DEBUG模式断言 + 日志
│  实体在TileMap外移动
│  重复的Entity ID
│  选择系统中存在已销毁实体
│
静默降级 (Silent)        → 日志(TRACE) + 无用户感知
   地图边界外瓦片查询返回默认值
```

### 9.4 实体ID生成策略

Phase 1 使用单调递增计数器即可。但接口必须考虑 Phase 3 网络需求。

```csharp
// FNARTS.Core/Entity/EntityId.cs
public static class EntityIdGenerator
{
    // Phase 1: 简单递增
    // Phase 3: 基于 factionIndex + monotonicCounter 保证跨客户端唯一
    //   例: id = (factionIndex << 24) | (localCounter & 0xFFFFFF)
    private static uint _nextId = 1;

    public static uint Next() => _nextId++;

    // Phase 3 扩展点
    public static uint NextForFaction(int factionIndex)
        => (uint)((factionIndex << 24) | (_nextId++ & 0xFFFFFF));
}
```

---

## 10. 风险管理

| 风险 | 概率 | 影响 | 缓解策略 |
|------|------|------|---------|
| **FNA3D_HLSL 未实现 Uniform API**：着色器参数无法运行时更新 | 高 | 高 | Phase 1-2 使用烘焙默认值；Dynamic 参数用多 Technique 切换绕开；跟踪上游 FNA3D_HLSL 进度 |
| **Vulkan 驱动兼容性**：特定 GPU/驱动无法运行 | 中 | 高 | 支持 llvmpipe/lavapipe 软件渲染（CI 已验证）；记录已知兼容驱动列表 |
| **性能不达标**：200+ 单位 < 60fps | 中 | 中 | Phase 1 起每帧性能统计；SpriteBatch 批次数监控；必要时下沉到自定义 DrawUserPrimitives 批处理 |
| **帧同步不一致**：客户端状态偏差 | 中 | 高 | Phase 1 起固定步长、确定性随机数；Phase 2 引入状态哈希校验（每 N 帧比较 hash）；Phase 3 完整锁步测试 |
| **FNA 上游变更**：HLSL 分支与上游分裂 | 低 | 低 | 固定当前 commits；记录 UPSTREAM-DIFF.md；必要时手动 cherry-pick |
| **范围蔓延**：Phase 1 试图做太多 | 中 | 中 | 严格按"不在范围内"列表裁剪；每两周回顾功能边界 |
| **系统依赖变更**：SDL3/DXC/.NET SDK API 变更 | 低 | 中 | 固定版本号；记录在 CLAUDE.md 中；CI 环境版本锁定 |

---

## 11. 附录

### A. 关键参考文件

| 文件 | 内容 |
|------|------|
| `../FNA/lib/FNA3D/CLAUDE.md` | FNA3D_HLSL 架构与构建 |
| `../FNA_Test/CLAUDE.md` | 测试基础架构、HLSL 顶点约定 C1-C5 |
| `../FNA_Test/README.md` | 测试目标与方法 |
| `../FNA/docs/UPSTREAM-DIFF.md` | FNA HLSL 分支与上游差异 |
| `../FNA_Test/Common/TestHarness.cs` | Headless 测试工具 API |
| `../FNA_Test/Common/TextureGen.cs` | 程序化纹理生成 |
| `../FNA_Test/Gui/` | 可重用的 GUI Widget 库 |

### B. FNA 框架约束

- **仅 Vulkan**：无 D3D11/OpenGL/Metal 后端，SPIR-V 是唯一着色器格式
- **仅 Linux**：macOS/iOS 不支持
- **无运行时 Uniform 更新**：着色器参数必须在 FEB 构建时烘焙
- **顶点约定 C1-C5**：所有自定义 HLSL 着色器必须遵循
- **COLOR 字节序**：BGRA (XNA 约定)
- **DXC Location**：按 HLSL 参数声明顺序分配，非 usage×16+index

### C. 性能目标

| 指标 | 目标 |
|------|------|
| 最大同屏单位 | 200+ @ 60fps |
| 最大地图尺寸 | 256×256 格 |
| 帧同步客户端 | 8 人 |
| 网络延迟容忍 | ≤ 200ms (lockstep with delay) |
| 寻路响应时间 | < 1ms per unit (A\* on 256×256) |
