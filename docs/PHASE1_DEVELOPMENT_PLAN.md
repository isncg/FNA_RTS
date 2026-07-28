# FNA_RTS 第一阶段开发文档：MVP 基础可玩系统

## 1. 阶段目标

实现一个**单击基础可玩的最小 RTS 系统**：地图渲染 → 建筑放置 → 单位创建 → 选择和指挥单位移动。

验证核心渲染管线和交互模式在 FNA 框架下的可行性，为后续战斗系统与网络同步打下基础。

---

## 2. 前置工作：FNA_Test 基础设施测试

在编写 RTS 代码之前，需要在 FNA_Test 中验证 FNA 框架对 RTS 所需渲染能力（等距瓦片、2D 摄像机、深度排序、框选等）的支持。

### 2.1 测试项目列表（按依赖顺序）

```
RTS/Camera2D/           ← 基础：2D 摄像机 (平移 + 缩放)
RTS/PrimitiveLines/     ← 基础：DrawUserPrimitives 线条渲染
RTS/IsometricTiles/     ← 核心：等距瓦片地图渲染
RTS/ScreenToWorld/      ← 核心：屏幕↔世界↔网格坐标变换
RTS/DepthSorting/       ← 核心：Y 轴深度排序
RTS/RectSelection/      ← 功能：拖拽框选
```

### 2.2 RTS/Camera2D — 2D 摄像机

**目标**：验证 SpriteBatch 的 `transformMatrix` 参数可实现 2D 摄像机的平移和缩放。

**Camera2D.cs 接口设计**：
```csharp
public class Camera2D
{
    public Vector2 Position { get; set; }    // 摄像机中心的世界坐标
    public float Zoom { get; set; }          // 1.0 = 默认, >1 放大
    public float MinZoom { get; set; }       // 最小缩放 (默认 0.25)
    public float MaxZoom { get; set; }       // 最大缩放 (默认 4.0)
    public Vector2 WorldBoundMin { get; set; } // 世界边界 (用于夹持)
    public Vector2 WorldBoundMax { get; set; }

    Matrix ViewMatrix { get; }              // 供 SpriteBatch.Begin(transformMatrix:)
    Matrix InverseViewMatrix { get; }       // 供屏幕→世界坐标变换
    void Update(InputState input, float dt); // WASD/边缘滚动/滚轮缩放
    Vector2 ScreenToWorld(Vector2 screenPos); // 屏幕像素→世界坐标
}
```

**测试要点**：
- WASD 和方向键平移摄像机
- 鼠标滚轮缩放（以鼠标位置为中心）
- 鼠标靠近窗口边缘时自动滚动
- 摄像机位置被 WorldBound 夹持
- headless 模式：固定输入 → 断言 ViewMatrix 数值正确

### 2.3 RTS/PrimitiveLines — 线条渲染

**目标**：验证 `GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList)` 可渲染调试网格、选框和路径线。

**测试要点**：
- 使用 `VertexPositionColor[]` 创建线条顶点
- `BasicEffect` + 正交投影渲染线条
- headless 模式：渲染已知位置的线条 → 像素断言线条存在

### 2.4 RTS/IsometricTiles — 等距瓦片渲染

**目标**：验证 SpriteBatch 可以正确的等距投影渲染瓦片地图。

**TileMap.cs 接口设计**：
```csharp
public class TileMap
{
    int Width { get; }
    int Height { get; }
    int TileWidth { get; }    // 瓦片纹理宽度 (如 64)
    int TileHeight { get; }   // 瓦片纹理高度 (如 32)

    TileType GetTile(int wx, int wy);
    bool IsPassable(int wx, int wy);
    Vector2 TileToScreen(int wx, int wy);  // 等距投影
}
```

**测试要点**：
- 程序化生成瓦片图集纹理
- 使用 `SpriteBatch.Draw(texture, position, sourceRect, color)` 按等距坐标渲染每个瓦片
- `SpriteBatch.Begin(sortMode: BackToFront, transformMatrix: camera.ViewMatrix)` — 摄像机 + 深度排序结合
- headless 模式：渲染 10×10 瓦片地图 → 断言覆盖率和特定像素颜色

### 2.5 RTS/ScreenToWorld — 坐标变换

**目标**：验证屏幕像素 → 世界坐标 → 网格坐标的变换链准确无误。

**测试要点**：
- 固定摄像机位置和缩放 → 将鼠标位置变换为世界坐标 → 再变换为网格坐标
- 往返测试：world → screen → world（误差 < 0.001）
- headless 模式：计算特定屏幕坐标对应的网格位置 → 断言等于预期值

### 2.6 RTS/DepthSorting — 深度排序

**目标**：验证 `SpriteSortMode.BackToFront` + `layerDepth` 能正确实现等距遮挡。

**关键算法**：
```csharp
// layerDepth: 0.0 = 最远(先画), 1.0 = 最近(后画)
// 等距视角中, worldY 越大 = 屏幕 Y 越大 = 离摄像机越近 = 应该后画
float ComputeDepth(float worldY, float mapHeight)
{
    return worldY / mapHeight;  // 归一化到 [0, 1]
}
```

**测试要点**：
- 渲染 20+ 个带透明度重叠精灵
- 精灵按世界 Y 坐标计算 layerDepth
- headless 模式：验证特定位置的精灵遮挡了另一个精灵（像素断言颜色属于"前面"的精灵）

### 2.7 RTS/RectSelection — 框选

**目标**：验证鼠标拖拽绘制选择矩形，并能将实体投影到屏幕判定是否在矩形内。

**测试要点**：
- 鼠标按下 → 拖拽 → 松开，绘制半透明选择矩形
- 松开时检测所有实体（已知屏幕位置）是否在矩形内
- 选中的实体改变渲染颜色
- headless 模式：注入已知拖拽坐标 → 断言选中了正确数量的实体

---

## 3. 程序化美术资源生成

### 3.1 设计原则

初期阶段使用**程序化生成的占位美术资源**。原因：

- 渲染框架和交互逻辑是 Phase 1 的核心目标，美术不是
- 占位资源可以完全自动化生成，零外部依赖
- 先确定每个资源需要的像素尺寸、图集布局、渲染参数，为正式美术提供规格
- 正式美术就绪后，只需替换纹理来源，渲染代码不变

### 3.2 资源与占位方案

| 资源类型 | 占位方案 | 纹理尺寸 | 说明 |
|---------|---------|---------|------|
| 地形瓦片 | 纯色菱形 + 边框 | 64×32 px | 草地=绿, 水域=蓝, 高地=灰, 不可通过=红 |
| 建筑精灵 | 纯色矩形 + 阵营色边框 | 依建筑尺寸 (如 3×3 建筑 = 192×96 px) | 底色灰, 阵营色边框 2px |
| 单位精灵 | 实心圆 + 阵营色填充 | 32×32 px | 点选判定用圆形 Bounding |
| 选择高亮 | 黄色圆环 | 36×36 px | 比单位精灵稍大, 渲染在单位上方 |
| UI 面板 | 纯色/渐变矩形 | 可变 | 复用 FNA_Test/Gui 的样式系统 |
| 小地图瓦片 | 1×1 px 颜色点 | 地图尺寸 | RenderTarget 上逐像素写入 |

### 3.3 架构：IAssetProvider

渲染代码不直接生成纹理，而是通过 `IAssetProvider` 接口获取。这样正式美术就绪时只需实现新的 Provider，渲染代码零改动。

```csharp
// FNARTS.Game/Assets/IAssetProvider.cs

public interface IAssetProvider : IDisposable
{
    // ---- 地形 ----
    Texture2D TilesetTexture { get; }             // 瓦片图集 (所有 TileType 在一张纹理上)
    Rectangle GetTileSourceRect(TileType type);    // 图集中每个 TileType 的位置

    // ---- 实体 ----
    Texture2D GetUnitTexture(string unitDefId);     // 单位精灵
    Texture2D GetBuildingTexture(string buildingDefId); // 建筑精灵 (可能占多格)

    // ---- 选择 ----
    Texture2D SelectionHighlight { get; }           // 选择高亮圆环
    Texture2D WhitePixel { get; }                   // 1×1 白色 (用于矩形填充/边框)
}
```

### 3.4 ProceduralAssetProvider 实现

```csharp
// FNARTS.Game/Assets/ProceduralAssetProvider.cs

public class ProceduralAssetProvider : IAssetProvider
{
    private readonly GraphicsDevice _device;

    // 瓦片图集: 4×4 排列, 每个 64×32 → 图集 256×128
    public const int TILE_TEX_W = 64;
    public const int TILE_TEX_H = 32;
    public const int TILESET_COLS = 4;

    public Texture2D TilesetTexture { get; }
    public Texture2D SelectionHighlight { get; }
    public Texture2D WhitePixel { get; }

    private readonly Dictionary<string, Texture2D> _unitCache = new();
    private readonly Dictionary<string, Texture2D> _buildingCache = new();

    public ProceduralAssetProvider(GraphicsDevice device)
    {
        _device = device;
        TilesetTexture = GenerateTileset();
        SelectionHighlight = GenerateSelectionHighlight();
        WhitePixel = GenerateWhitePixel();
    }

    // ---- 瓦片图集生成 ----

    Texture2D GenerateTileset()
    {
        // 图集布局: 每行列出一个 TileType 的瓦片
        // TileType 顺序: Grass(0), Water(1), Cliff(2), Impassable(3)
        int cols = TILESET_COLS;
        int rows = (Enum.GetValues<TileType>().Length + cols - 1) / cols;
        int atlasW = cols * TILE_TEX_W;
        int atlasH = rows * TILE_TEX_H;

        var data = new Color[atlasW * atlasH];
        // 先填透明
        Array.Fill(data, Color.Transparent);

        foreach (TileType type in Enum.GetValues<TileType>())
        {
            int col = (int)type % cols;
            int row = (int)type / cols;
            int ox = col * TILE_TEX_W;
            int oy = row * TILE_TEX_H;

            Color fill = TileTypeColor(type);
            Color border = Color.Lerp(fill, Color.Black, 0.3f);

            // 绘制 2:1 菱形
            DrawDiamond(data, atlasW, ox, oy, TILE_TEX_W, TILE_TEX_H, fill, border);
        }

        var tex = new Texture2D(_device, atlasW, atlasH);
        tex.SetData(data);
        return tex;
    }

    static void DrawDiamond(Color[] data, int stride,
        int ox, int oy, int tw, int th,
        Color fill, Color border)
    {
        int halfW = tw / 2;
        int halfH = th / 2;
        int centerX = ox + halfW;
        int centerY = oy + halfH;

        for (int py = 0; py < th; py++)
        for (int px = 0; px < tw; px++)
        {
            // 菱形的等距边界判定
            int dx = px - halfW;
            int dy = py - halfH;
            // 世界坐标下: 菱形边缘 = |dx/halfW| + |dy/halfH| <= 1
            float dist = MathF.Abs((float)dx / halfW) + MathF.Abs((float)dy / halfH);

            if (dist <= 1.02f)
            {
                Color c = dist > 0.85f ? border : fill;
                data[(oy + py) * stride + (ox + px)] = c;
            }
        }
    }

    static Color TileTypeColor(TileType type) => type switch
    {
        TileType.Grass => new Color(76, 153, 0),      // 草地绿
        TileType.Water => new Color(51, 102, 255),    // 水域蓝
        TileType.Cliff => new Color(160, 160, 160),   // 悬崖灰
        TileType.Impassable => new Color(180, 60, 60),// 不可通过红
        _ => Color.Magenta                            // 未定义 = 醒目品红
    };

    public Rectangle GetTileSourceRect(TileType type)
    {
        int col = (int)type % TILESET_COLS;
        int row = (int)type / TILESET_COLS;
        return new Rectangle(col * TILE_TEX_W, row * TILE_TEX_H, TILE_TEX_W, TILE_TEX_H);
    }

    // ---- 单位精灵生成 ----

    public Texture2D GetUnitTexture(string unitDefId)
    {
        if (_unitCache.TryGetValue(unitDefId, out var cached))
            return cached;

        int size = 32;
        var data = new Color[size * size];
        Array.Fill(data, Color.Transparent);

        float center = size / 2f;
        float radius = size / 2f - 2;

        // 根据单位类型选择占位颜色
        Color fill = unitDefId switch
        {
            "worker" => new Color(200, 200, 100),     // 黄色
            "infantry" => new Color(100, 180, 255),   // 蓝色
            "tank" => new Color(255, 120, 80),        // 橙色
            _ => Color.Gray
        };

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - center + 0.5f;
            float dy = y - center + 0.5f;
            float d = MathF.Sqrt(dx * dx + dy * dy);

            if (d <= radius)
            {
                // 实心圆 + 深色边缘
                data[y * size + x] = d > radius - 2
                    ? Color.Lerp(fill, Color.Black, 0.4f)
                    : fill;
            }
        }

        var tex = new Texture2D(_device, size, size);
        tex.SetData(data);
        _unitCache[unitDefId] = tex;
        return tex;
    }

    // ---- 建筑精灵生成 ----

    public Texture2D GetBuildingTexture(string buildingDefId)
    {
        if (_buildingCache.TryGetValue(buildingDefId, out var cached))
            return cached;

        // 占位: 纯色矩形 + 阵营色边框
        int w = 128, h = 96;  // 默认建筑尺寸
        var data = new Color[w * h];
        Color fill = new Color(120, 120, 120);  // 灰色
        Color border = new Color(255, 215, 0);  // 金色边框

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            bool isBorder = x < 2 || x >= w - 2 || y < 2 || y >= h - 2;
            data[y * w + x] = isBorder ? border : fill;
        }

        var tex = new Texture2D(_device, w, h);
        tex.SetData(data);
        _buildingCache[buildingDefId] = tex;
        return tex;
    }

    // ---- 选择高亮 ----

    Texture2D GenerateSelectionHighlight()
    {
        int size = 36;
        var data = new Color[size * size];
        float center = size / 2f;
        float outerR = size / 2f - 1;
        float innerR = outerR - 3;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = MathF.Sqrt((x-center+0.5f)*(x-center+0.5f) + (y-center+0.5f)*(y-center+0.5f));
            if (d >= innerR && d <= outerR)
                data[y * size + x] = new Color(255, 255, 0, 220);  // 黄色半透明
        }

        var tex = new Texture2D(_device, size, size);
        tex.SetData(data);
        return tex;
    }

    Texture2D GenerateWhitePixel()
    {
        var tex = new Texture2D(_device, 1, 1);
        tex.SetData(new[] { Color.White });
        return tex;
    }
}
```

### 3.5 从程序化到正式美术的切换路径

```
Phase 1 (当前):
  ProceduralAssetProvider → 程序化生成所有纹理
  验证内容：纹理尺寸、图集布局、渲染坐标

Phase 2 (正式美术就绪后):
  FileAssetProvider : IAssetProvider → 从文件加载 PNG/XNB
  替换方法：Game.LoadContent() 中 new FileAssetProvider() 代替 new ProceduralAssetProvider()
  渲染代码：零改动 (只依赖 IAssetProvider 接口)

过渡期：
  混合模式 — 已完成的正式资源用文件加载，未完成的用 ProceduralAssetProvider 兜底
  HybridAssetProvider : IAssetProvider
    → 优先查文件，文件不存在则用 Procedural 生成
```

### 3.6 瓦片图集布局约定

这是与美术沟通的关键规格文档，程序化生成时必须严格遵循：

```
图集尺寸: 256 × 128 px (4 列 × 最多 4 行)
每个瓦片: 64 × 32 px, 2:1 菱形

列 0 (x=0):    Grass       列 1 (x=64):   Water
列 2 (x=128):  Cliff       列 3 (x=192):  Impassable

未来扩展:
  行 1 (y=32):  更多地形类型...
  行 2 (y=64):  装饰层 (树木、岩石)...
  行 3 (y=96):  地表覆盖 (道路、矿脉)...
```

**菱形绘制规范**：
- 菱形占满 64×32 像素区域
- 顶部顶点在 (32, 0)
- 底部顶点在 (32, 31)
- 左顶点在 (0, 16)
- 右顶点在 (63, 16)
- 菱形边缘留 1-2px 用于接缝抗锯齿

---

## 4. 项目结构

```
FNA_RTS/
├── docs/
│   ├── DEVELOPMENT_PLAN.md            ← 整体开发路线 (已完成)
│   └── PHASE1_DEVELOPMENT_PLAN.md     ← 本文件
├── src/
│   ├── FNARTS.Core/
│   │   ├── FNARTS.Core.csproj         ← 目标: net10.0, 无 FNA 依赖
│   │   ├── Math/
│   │   │   ├── IsoCoord.cs            ← 网格坐标结构
│   │   │   └── CoordUtil.cs           ← 等距投影变换, 屏幕↔世界↔网格
│   │   ├── Map/
│   │   │   ├── TileType.cs            ← 地形枚举
│   │   │   ├── Tile.cs                ← 瓦片数据
│   │   │   └── TileMap.cs             ← 网格地图
│   │   ├── Entity/
│   │   │   ├── Entity.cs              ← 实体基类 (Id, Position, Faction)
│   │   │   ├── Unit.cs                ← 单位 (MoveSpeed, MoveTarget)
│   │   │   ├── Building.cs            ← 建筑 (SizeInTiles)
│   │   │   └── EntityManager.cs       ← 实体管理 + 空间查询
│   │   ├── Selection/
│   │   │   └── SelectionSystem.cs     ← 选择逻辑 (单击/框选, 选中集合)
│   │   ├── Command/
│   │   │   ├── Command.cs             ← 指令基类
│   │   │   ├── MoveCommand.cs         ← 移动指令
│   │   │   ├── BuildCommand.cs        ← 建造指令
│   │   │   └── CommandSystem.cs       ← 指令生成与执行
│   │   ├── State/
│   │   │   └── GameState.cs           ← 游戏状态枚举 (Loading/Menu/Playing/Paused)
│   │   ├── Data/
│   │   │   ├── UnitDef.cs             ← 单位数据定义
│   │   │   ├── BuildingDef.cs         ← 建筑数据定义
│   │   │   └── MapData.cs             ← 地图数据结构 (可序列化)
│   │   └── Util/
│   │       └── EntityIdGenerator.cs   ← 实体ID生成 (Phase 3 兼容预留)
│   │
│   └── FNARTS.Game/
│       ├── FNARTS.Game.csproj         ← 依赖: FNARTS.Core + FNA.Core
│       ├── Camera/
│       │   └── Camera2D.cs            ← 2D 摄像机
│       ├── Assets/
│       │   ├── IAssetProvider.cs      ← 资源提供者接口
│       │   └── ProceduralAssetProvider.cs ← 程序化纹理生成
│       ├── Render/
│       │   ├── TileRenderer.cs        ← 等距瓦片渲染
│       │   ├── EntityRenderer.cs      ← 实体精灵渲染 (含深度排序)
│       │   └── SelectionRenderer.cs   ← 选择高亮 + 框选矩形
│       ├── Input/
│       │   └── RTSInput.cs            ← 输入轮询与事件生成 + 动作映射
│       ├── UI/
│       │   ├── CommandPanel.cs        ← 命令面板 (选中单位信息)
│       │   ├── Minimap.cs             ← 小地图 (可选, 延后)
│       │   └── DebugOverlay.cs        ← 性能/调试信息覆盖层 (FPS, DrawCalls)
│       └── RTSGame.cs                 ← Game 主类, 组装所有系统
│
└── tests/
    ├── FNARTS.Core.Tests/
    │   ├── FNARTS.Core.Tests.csproj   ← xUnit, 引用 FNARTS.Core
    │   ├── CoordUtilTests.cs          ← 坐标变换测试
    │   ├── TileMapTests.cs            ← 地图测试
    │   ├── EntityManagerTests.cs      ← 实体管理测试
    │   ├── SelectionSystemTests.cs    ← 选择逻辑测试
    │   └── CommandSystemTests.cs      ← 指令系统测试
    │
    └── FNARTS.Game.Tests/
        ├── FNARTS.Game.Tests.csproj   ← 引用 FNARTS.Game + FNA_Test Common
        ├── Camera2DTests.cs           ← 摄像机变换测试 (headless)
        ├── TileRenderTests.cs         ← 瓦片渲染测试 (像素断言)
        ├── EntityRenderTests.cs       ← 实体渲染测试 (深度排序)
        └── SelectionRenderTests.cs    ← 框选渲染测试
```

---

## 5. 核心类详细设计

### 5.1 IsoCoord — 网格坐标

```csharp
// FNARTS.Core/Math/IsoCoord.cs
public struct IsoCoord : IEquatable<IsoCoord>
{
    public int X;  // 网格 X (向右下)
    public int Y;  // 网格 Y (向右上)

    public IsoCoord(int x, int y);
    public static IsoCoord Zero { get; }
    public static float Distance(IsoCoord a, IsoCoord b);
    // + 运算符、Equals、GetHashCode
}
```

### 5.2 CoordUtil — 坐标变换

```csharp
// FNARTS.Core/Math/CoordUtil.cs
public static class CoordUtil
{
    // 瓦片像素尺寸 (2:1 等距)
    public const int TILE_WIDTH = 64;
    public const int TILE_HEIGHT = 32;

    /// 网格坐标 → 瓦片左上角屏幕位置 (世界坐标)
    public static Vector2 IsoToWorld(IsoCoord coord);

    /// 世界坐标 → 网格坐标 (floor 取整)
    public static IsoCoord WorldToIso(Vector2 worldPos);

    /// 网格坐标 → 瓦片中心世界坐标
    public static Vector2 IsoToWorldCenter(IsoCoord coord);
}
```

**等距投影公式**（Tile 左上角）：
```
worldX = (coord.X - coord.Y) * (TILE_WIDTH / 2)
worldY = (coord.X + coord.Y) * (TILE_HEIGHT / 2)
```

**逆变换**（世界坐标 → 网格坐标）：
```
浮点网格X = (worldX / halfTileW + worldY / halfTileH) / 2
浮点网格Y = (worldY / halfTileH - worldX / halfTileW) / 2
最终网格 = (floor(gridX), floor(gridY))
```

**完整拾取链**（屏幕像素 → 网格）：
```
屏幕像素 → 应用 InverseViewMatrix → 世界坐标 → WorldToIso → 网格坐标
```

### 5.3 TileMap — 网格地图

```csharp
// FNARTS.Core/Map/TileMap.cs
public class TileMap
{
    public int Width { get; }
    public int Height { get; }

    public TileMap(int width, int height);
    public Tile GetTile(IsoCoord coord);
    public Tile GetTile(int wx, int wy);
    public void SetTile(IsoCoord coord, Tile tile);
    public bool IsPassable(IsoCoord coord);
    public bool InBounds(IsoCoord coord);
}
```

### 5.4 Entity — 实体

```csharp
// FNARTS.Core/Entity/Entity.cs
public abstract class Entity
{
    public uint Id { get; }                    // 全局唯一 ID (由 EntityIdGenerator 生成)
    public Vector2 WorldPosition { get; set; } // 世界坐标 (像素)
    public int Faction { get; set; }           // 所属阵营
    public bool IsSelected { get; set; }       // 选中状态

    protected Entity() { Id = EntityIdGenerator.Next(); }

    // 屏幕空间包围矩形 (用于拾取和框选)
    public Rectangle GetScreenBounds(Camera2D camera);
}
```

```csharp
// FNARTS.Core/Entity/Unit.cs
public class Unit : Entity
{
    public UnitDef Definition { get; }
    public float MoveSpeed { get; }            // 像素/秒
    public Vector2? MoveTarget { get; set; }   // 移动目标世界坐标

    public void Update(float dt);              // 向 MoveTarget 移动
}
```

```csharp
// FNARTS.Core/Entity/Building.cs
public class Building : Entity
{
    public BuildingDef Definition { get; }
    public IsoCoord PlacementOrigin { get; }   // 建筑占用的网格原点
    public Size PlacementSize { get; }         // 占用网格数 (如 3×3)

    public IsoCoord[] GetOccupiedTiles();      // 返回占用的所有网格坐标
}
```

### 5.5 EntityManager — 实体管理器

```csharp
// FNARTS.Core/Entity/EntityManager.cs
public class EntityManager
{
    // 空间索引：网格哈希 (IsoCoord → List<Entity>)
    // 加速屏幕区域查询

    public IReadOnlyList<Entity> AllEntities { get; }

    public void AddEntity(Entity entity);
    public void RemoveEntity(uint id);
    public Entity GetEntity(uint id);

    // 空间查询
    public IEnumerable<Entity> QueryRect(Rectangle worldRect);      // 世界矩形查询
    public IEnumerable<Entity> QueryScreenRect(Rectangle screenRect, // 屏幕矩形查询
                                                Camera2D camera);
    public Entity QueryPoint(Vector2 worldPoint);                    // 点查询 (单击选择)
    public Entity QueryScreenPoint(Vector2 screenPoint,
                                    Camera2D camera);
}
```

### 5.6 SelectionSystem — 选择系统

```csharp
// FNARTS.Core/Selection/SelectionSystem.cs
public class SelectionSystem
{
    public IReadOnlySet<uint> SelectedEntityIds { get; }
    public bool IsDragging { get; }
    public Rectangle? DragRect { get; }         // 当前拖拽矩形 (屏幕坐标)

    // 输入事件
    public void OnMouseDown(Vector2 screenPos);
    public void OnMouseDrag(Vector2 screenPos);
    public void OnMouseUp(Vector2 screenPos, EntityManager entities, Camera2D camera);

    // 结果查询
    public bool IsEntitySelected(uint entityId);
    public void ClearSelection();
}
```

**选择逻辑**：
- `OnMouseDown`：检测点击位置是否有实体 → 有则选中该实体（按住 Shift 追加选择），无则开始拖拽
- `OnMouseDrag`：更新拖拽矩形
- `OnMouseUp`：拖拽模式下，使用 `EntityManager.QueryScreenRect` 查询矩形内实体并选中

### 5.7 CommandSystem — 指令系统

```csharp
// FNARTS.Core/Command/Command.cs
public abstract class Command
{
    public CommandType Type { get; }
}

// FNARTS.Core/Command/MoveCommand.cs
public class MoveCommand : Command
{
    public Vector2 TargetWorldPosition { get; }
}

// FNARTS.Core/Command/BuildCommand.cs
public class BuildCommand : Command
{
    public BuildingDef BuildingType { get; }
    public IsoCoord PlacementOrigin { get; }
}
```

```csharp
// FNARTS.Core/Command/CommandSystem.cs
public class CommandSystem
{
    // 处理右键点击 → 生成指令
    public Command? ProcessRightClick(Vector2 screenPos, Camera2D camera,
                                       SelectionSystem selection, EntityManager entities);

    // 执行待处理的指令
    public void ExecuteCommands(EntityManager entities, TileMap map);
}
```

**右键指令生成规则**：
```
右键空地 → 移动指令（发给所有选中的单位）
右键单位 → 移动指令（移动到目标单位附近）
右键建筑 → 移动指令（移动到目标建筑附近）
```

### 5.8 Camera2D — 2D 摄像机（FNARTS.Game）

```csharp
// FNARTS.Game/Camera/Camera2D.cs
public class Camera2D
{
    public Vector2 Position { get; set; }
    public float Zoom { get; set; }            // 默认 1.0

    public Matrix ViewMatrix { get; }          // 每帧更新，供 SpriteBatch
    public Matrix InverseViewMatrix { get; }

    // 边界夹持
    public RectangleF? WorldBounds { get; set; }

    public Camera2D(int viewportWidth, int viewportHeight);

    // 每帧调用
    public void Update(RTSInput input, float dt);

    // 坐标变换 (使用 InverseViewMatrix)
    public Vector2 ScreenToWorld(Vector2 screenPos);

    // 当视口改变时更新
    public void Resize(int viewportWidth, int viewportHeight);
}
```

**视图矩阵构建**：
```
ViewMatrix = Matrix.CreateTranslation(-Position.X, -Position.Y, 0) *
             Matrix.CreateScale(Zoom, Zoom, 1) *
             Matrix.CreateTranslation(viewportWidth/2, viewportHeight/2, 0)
```

**摄像机平移速度**：
- WASD/方向键：400 像素/秒（基准，随缩放调整）
- 边缘滚动：300 像素/秒（鼠标距边缘 ≤ 20px 时触发）

### 5.9 TileRenderer — 瓦片渲染器

```csharp
// FNARTS.Game/Render/TileRenderer.cs
public class TileRenderer : IDisposable
{
    // 通过 IAssetProvider 获取瓦片图集，不自行生成
    public TileRenderer(TileMap map, IAssetProvider assets);

    // 每帧渲染
    public void Draw(SpriteBatch sb, Camera2D camera);
}
```

**渲染流程**：
```
1. 计算摄像机可见的瓦片范围 (视锥剔除)
   → visibleMin = WorldToIso(ScreenToWorld(0, 0))
   → visibleMax = WorldToIso(ScreenToWorld(viewportW, viewportH))
   → 扩展 1 格边界 (容纳边缘瓦片)

2. SpriteBatch.Begin(SpriteSortMode.BackToFront, transformMatrix: camera.ViewMatrix)

3. for wx = minX..maxX, wy = minY..maxY:
     position = CoordUtil.IsoToWorld(wx, wy)
     sourceRect = _assets.GetTileSourceRect(map.GetTile(wx,wy).Type)
     depth = (wy + 0.5f) / map.Height   // 瓦片中心 Y 归一化
     batch.Draw(_assets.TilesetTexture, position, sourceRect, Color.White,
                0, Vector2.Zero, 1, SpriteEffects.None, depth)

4. SpriteBatch.End()
```

**视锥剔除**：将屏幕四角通过 `camera.ScreenToWorld()` 转换为世界坐标，再通过 `CoordUtil.WorldToIso()` 转换为网格范围。剔除不可见瓦片，减少 Draw 调用。

### 5.10 EntityRenderer — 实体渲染器

```csharp
// FNARTS.Game/Render/EntityRenderer.cs
public class EntityRenderer : IDisposable
{
    // 通过 IAssetProvider 获取实体纹理
    public EntityRenderer(IAssetProvider assets);

    // 渲染所有实体，按 worldY 排序
    public void Draw(SpriteBatch sb, Camera2D camera, EntityManager entities,
                      SelectionSystem selection);
}
```

**渲染顺序**：
```
1. 收集所有实体的 (Entity, ScreenPosition, Depth) 三元组
2. 按 depth 排序 (worldY 越大越靠后画)
3. SpriteBatch.Begin(sortMode: Texture, transformMatrix: camera.ViewMatrix)
   // 注意：深度排序已在 CPU 侧完成，用 Texture sortMode 利用批处理
4. for each entity:
     if entity.IsSelected: 绘制选择高亮 (黄色边框或绿色调)
     绘制实体精灵
5. SpriteBatch.End()
```

### 5.11 SelectionRenderer — 选择渲染器

```csharp
// FNARTS.Game/Render/SelectionRenderer.cs
public class SelectionRenderer
{
    // 渲染框选矩形 (半透明填充 + 白色边框)
    public void DrawDragRect(SpriteBatch sb, Rectangle? dragRect);

    // 渲染选中实体的高亮圆环
    public void DrawSelectionHighlight(SpriteBatch sb, Entity entity);
}
```

**框选矩形**：使用 1×1 白色纹理 + `SpriteBatch.Draw(destRect, Color.Green * 0.2f)` 绘制填充，使用 `PrimitiveLines` 模式绘制边框。

### 5.12 RTSInput — 输入层

硬件键位通过 `InputMapping` 映射为逻辑动作，游戏代码只依赖 `InputAction` 枚举，不硬编码按键。

```csharp
// FNARTS.Game/Input/RTSInput.cs
public class RTSInput
{
    private InputMapping _mapping;

    public void LoadBindings(string path);       // 加载键位配置 JSON

    // 鼠标
    public Vector2 MouseScreenPos { get; }
    public int ScrollDelta { get; }              // 滚轮变化量

    // 动作查询 (通过 InputMapping, 而非直接读 Keys)
    public bool IsPressed(InputAction action);
    public bool IsHeld(InputAction action);
    public bool IsReleased(InputAction action);

    // 派生值
    public Vector2 PanDirection { get; }         // 由 CameraPan* 动作合成
    public bool ShiftHeld { get; }               // IsHeld(ShiftModifier)
    public bool CtrlHeld { get; }

    public void Update();                        // 每帧轮询 FNA 输入, 更新 Mapping 状态
}
```

### 5.13 RTSGame — 游戏主类

```csharp
// FNARTS.Game/RTSGame.cs
public class RTSGame : Game
{
    // ---- 状态 ----
    private GameState _state = GameState.Loading;

    // ---- 系统 ----
    private Camera2D _camera;
    private TileMap _map;
    private EntityManager _entities;
    private SelectionSystem _selection;
    private CommandSystem _commands;
    private InputMapping _input;
    private IAssetProvider _assets;

    // ---- 渲染器 ----
    private TileRenderer _tileRenderer;
    private EntityRenderer _entityRenderer;
    private SelectionRenderer _selectionRenderer;
    private DebugOverlay _debugOverlay;
    private SpriteBatch _spriteBatch;

    // ---- 生命周期 ----
    protected override void Initialize();
    protected override void LoadContent();
    protected override void Update(GameTime gameTime);
    protected override void Draw(GameTime gameTime);
}
```

**固定时间步长设置**（`Initialize()` 中）：
```csharp
IsFixedTimeStep = true;
TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);  // 60Hz 固定步长
graphics.SynchronizeWithVerticalRetrace = false;        // 测试时关闭, 正式环境可开启
```

**Update 循环**（按状态分发）：
```
1. _input.Update()                              // 轮询输入 (所有状态)

2. switch (_state):
   case Loading:
     if (_assets.Ready) → _state = MainMenu

   case MainMenu:
     处理菜单输入 → NewGame → 加载地图 → _state = Playing

   case Playing:
     if 按 Escape → _state = Paused; return
     _camera.Update(_input, dt)                 // 摄像机
     ProcessInput()                              // 游戏输入
       ├─ 右键 → _commands.ProcessRightClick()
       ├─ 左键按下 → _selection.OnMouseDown()
       ├─ 左键拖拽 → _selection.OnMouseDrag()
       └─ 左键释放 → _selection.OnMouseUp()
     _commands.ExecuteCommands(_entities, _map) // 执行指令
     foreach unit in _entities: unit.Update(dt)  // 单位移动

   case Paused:
     if 按 Escape → _state = Playing
     处理暂停菜单输入
```

**Draw 循环**（按状态分发）：
```
1. GraphicsDevice.Clear(Color.Black)

2. switch (_state):
   case Loading:  画进度条
   case MainMenu: 画 UI 菜单
   case Playing:
   case Paused:
     _tileRenderer.Draw(_spriteBatch, _camera)
     _entityRenderer.Draw(...)
     _selectionRenderer.DrawDragRect(...)

3. UI.Draw() (HUD / 菜单 / 暂停面板)

4. if _debugOverlay.Enabled: _debugOverlay.Draw(_spriteBatch, font)
```

### 5.14 GameState — 游戏状态机

Phase 1 实现 4 个状态（后续阶段扩展）：

```csharp
// FNARTS.Core/State/GameState.cs
public enum GameState
{
    Loading,    // 资源加载中 (AssetProvider 初始化, 着色器编译)
    MainMenu,   // 主菜单
    Playing,    // 游戏中 (所有系统活动)
    Paused      // 暂停 (逻辑冻结, 渲染继续, UI 可交互)
}
```

**状态转换规则**：

```
Loading → MainMenu           (资源加载完成)
MainMenu → Playing           (选择"新游戏")
Playing → Paused             (按 Escape)
Paused → Playing             (按 Escape 或"继续")
Playing → MainMenu           (退出游戏确认)
```

**状态影响**：`RTSGame.Update()` 中根据 `_state` 决定哪些系统执行。

| 系统 | Loading | MainMenu | Playing | Paused |
|------|---------|----------|---------|--------|
| AssetProvider | ● | | | |
| Camera2D | | | ● | |
| TileRenderer | | | ● | ● (静态) |
| EntityRenderer | | | ● | ● |
| Entity.Update() | | | ● | |
| RTSInput | | ● (菜单) | ● | ● (仅UI) |
| SelectionSystem | | | ● | |
| CommandSystem | | | ● | |
| UI | ● (进度条) | ● (菜单) | ● (HUD) | ● (设置) |

### 5.15 MapData — 地图数据格式

地图使用 JSON 文件定义，与 `TileMap` 类解耦。`TileMap` 从 `MapData` 构造。

```json
{
  "name": "Test Map 1",
  "width": 20,
  "height": 20,
  "tiles": [
    { "x": 0, "y": 0, "type": "Grass" },
    { "x": 5, "y": 5, "type": "Water" }
  ],
  "defaultTile": "Grass",
  "startPositions": [
    { "faction": 0, "x": 2, "y": 2 },
    { "faction": 1, "x": 17, "y": 17 }
  ]
}
```

```csharp
// FNARTS.Core/Data/MapData.cs
public class MapData
{
    public string Name { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string DefaultTile { get; set; }
    public List<TileEntry> Tiles { get; set; }       // 稀疏列表
    public List<StartPosition> StartPositions { get; set; }
}
```

### 5.16 输入动作映射

`RTSInput` 不直接暴露 `Keys.A`，而是通过动作枚举解耦：

```csharp
// FNARTS.Game/Input/InputAction.cs
public enum InputAction
{
    CameraPanUp, CameraPanDown, CameraPanLeft, CameraPanRight,
    CameraZoomIn, CameraZoomOut,
    Select, Command,                          // 鼠标左键/右键
    ShiftModifier, CtrlModifier,
    TogglePause, Cancel
}
```

```csharp
// FNARTS.Game/Input/InputMapping.cs
public class InputMapping
{
    public void LoadFromFile(string path);    // 从 JSON 加载键位绑定
    public bool IsActionPressed(InputAction action);
    public bool IsActionHeld(InputAction action);
    public Vector2 MousePosition { get; }
    public int ScrollDelta { get; }
    public Vector2 PanDirection { get; }
}
```

### 5.17 错误处理与日志

**日志接口（Core 层纯抽象，不依赖具体日志库）**：

```csharp
// FNARTS.Core/Util/GameLogger.cs
public static class GameLogger
{
    public static Action<string> Info  { get; set; } = _ => {};
    public static Action<string> Warn  { get; set; } = _ => {};
    public static Action<string> Error { get; set; } = _ => {};
    public static Action<string> Debug { get; set; } = _ => {};
}
```

`FNARTS.Game` 启动时将委托桥接到 `Console.WriteLine`。

**关键日志点**：实体创建/销毁、指令生成/执行、地图加载、FNA 初始化失败（ERROR 级别 + 驱动诊断建议）。

**异常处理模式**：
- 初始化阶段：Fast Fail（FNA 失败 → 日志+退出）
- 运行时：降级（纹理加载失败 → 占位纹理 + WARN 日志）

### 5.18 命令行参数

```
FNA_RTS.Game [选项]

游戏选项:
  --map <name>         指定启动地图 (默认: test_map1)
  --faction <id>       玩家阵营 (默认: 0)
  --skip-menu          跳过主菜单, 直接进入游戏

调试选项:
  --headless           无头模式 (自动退出, 用于 CI)
  --debug-render       显示性能覆盖层 (FPS, DrawCalls)
  --debug-selection    显示选择/碰撞边界框
  --log-level <level>  日志级别 (默认: Info)
  --seed <number>      随机种子 (默认: 时间戳)
```

### 5.19 调试覆盖层

```csharp
// FNARTS.Game/UI/DebugOverlay.cs
public class DebugOverlay
{
    public bool Enabled { get; set; }

    // 每帧更新指标
    public void UpdateMetrics(float fps, int drawCalls, int visibleTiles,
                               int entityCount, long memoryBytes);

    // 左上角绘制半透明诊断文字
    public void Draw(SpriteBatch sb, SpriteFont font);
}
```

**显示内容**：FPS, Frame Time (Update/Draw), Draw Calls, Visible Tiles, Entity Count, Memory, Camera pos+zoom

---

## 6. 测试计划

### 6.1 FNARTS.Core 单元测试（xUnit, 无 GPU）

#### CoordUtilTests

```
Test_IsoToWorld_Origin_ReturnsZero
  → IsoToWorld(0,0) = Vector2(0, 0)

Test_IsoToWorld_PositiveX_MovesRightDown
  → IsoToWorld(1,0).X > 0, IsoToWorld(1,0).Y > 0

Test_IsoToWorld_PositiveY_MovesRightUp
  → IsoToWorld(0,1).X > 0, IsoToWorld(0,1).Y < 0

Test_WorldToIso_RoundTrip
  → WorldToIso(IsoToWorld(x,y)) == (x,y) for all x,y in 0..10

Test_WorldToIso_Fractional_SnapsToFloor
  → WorldToIso(Vector2 near tile corner) == expected tile
```

#### TileMapTests

```
Test_CreateMap_CorrectDimensions
  → map.Width == 10, map.Height == 10

Test_GetSetTile_RoundTrip
  → SetTile(coord, Grass); GetTile(coord).Type == Grass

Test_IsPassable_Water_ReturnsFalse
  → TileType.Water → !IsPassable

Test_InBounds_Outside_ReturnsFalse
  → InBounds(IsoCoord(-1, 0)) == false
```

#### EntityManagerTests

```
Test_AddEntity_IncreasesCount
  → AddEntity(unit); AllEntities.Count == 1

Test_QueryPoint_ReturnsEntityAtPosition
  → AddEntity at (100,50); QueryPoint(100,50) returns entity

Test_QueryRect_ReturnsEntitiesInRect
  → Add 3 entities inside rect, 2 outside → QueryRect returns 3

Test_RemoveEntity_RemovesFromIndex
  → Add then Remove; QueryPoint returns null
```

#### SelectionSystemTests

```
Test_ClickEntity_SelectsIt
  → OnMouseDown at entity position; entity.IsSelected == true

Test_ClickEmpty_ClearsSelection
  → Select entity; click empty; selection is empty

Test_ShiftClick_AddsToSelection
  → Select entity1; Shift+click entity2; both selected

Test_DragRect_SelectsEntitiesInside
  → Drag from (0,0) to (100,100); entities inside selected

Test_DragRect_ExcludesOutside
  → Entity at (200,200) not selected by (0,0)→(100,100) drag
```

#### CommandSystemTests

```
Test_RightClickGround_IssuesMoveCommand
  → Select unit; ProcessRightClick at (50,50) → MoveCommand(50,50)

Test_UnitExecuteMove_MovesTowardTarget
  → MoveCommand(100,0); Execute; unit position changes toward target

Test_UnitReachesTarget_Stops
  → Unit at (0,0) with MoveTarget(0,2) speed 1; after 2 seconds, unit stops
```

### 6.2 FNARTS.Game 集成测试（headless FNA + 像素断言）

#### Camera2DTests

```
Test_DefaultViewMatrix_CenterProjectsToScreenCenter
  → Camera at (0,0), zoom 1.0; world point (0,0) transforms to screen center

Test_ZoomIn_ScalesWorldCoordinates
  → Zoom 2.0; world point (10,0) transforms to (center + 20, center)

Test_ScreenToWorld_RoundTrip
  → world → screen → world; error < 0.001

Test_Pan_ChangesViewMatrix
  → Pan (100,0); world point (100,0) now at screen center

Test_BoundsClamp_PreventsOverPan
  → Set bounds; pan beyond; position clamped
```

#### TileRenderTests

```
Test_RenderTileMap_HasCoverage
  → Render 10×10 tile map; pixel coverage > 30%

Test_RenderTileMap_CenterTileIsCorrectColor
  → Center tile is grass green; AssertPixel matches

Test_IsometricShape_VerifyCornerPositions
  → Top corner tile at expected screen position (pixel check)
```

#### EntityRenderTests

```
Test_RenderUnit_HasCoverage
  → Render unit at known position; verify unit pixel exists

Test_DepthSorting_CloseEntityOccludesFar
  → Unit A (worldY=10), Unit B (worldY=1), B behind A → A's pixels visible

Test_SelectedUnit_HasHighlight
  → Select unit; verify highlight color pixels present
```

#### SelectionRenderTests

```
Test_DragRect_RendersSemiTransparent
  → Drag active; verify pixel coverage of drag rect area
```

---

## 7. 实现顺序

### 第 1 步：FNA_Test 基础设施 (约 1 周)

按依赖顺序：
1. **RTS/Camera2D** — 摄像机测试 (无依赖)
2. **RTS/PrimitiveLines** — 线条渲染测试 (依赖 Camera2D)
3. **RTS/IsometricTiles** — 瓦片渲染测试 (依赖 Camera2D)
4. **RTS/ScreenToWorld** — 坐标变换测试 (依赖 Camera2D + IsometricTiles)
5. **RTS/DepthSorting** — 深度排序测试 (依赖 IsometricTiles)
6. **RTS/RectSelection** — 框选测试 (依赖 DepthSorting + PrimitiveLines)

### 第 2 步：FNARTS.Core (约 1-1.5 周)

1. `IsoCoord`, `CoordUtil` — 坐标类型与变换
2. `TileType`, `Tile`, `TileMap` — 地图数据
3. `Entity`, `Unit`, `Building`, `EntityManager` — 实体系统
4. `SelectionSystem` — 选择逻辑
5. `Command`, `MoveCommand`, `BuildCommand`, `CommandSystem` — 指令系统

### 第 3 步：FNARTS.Game (约 1-1.5 周)

1. `Camera2D` — 摄像机 (从 FNA_Test 已验证的实现复制)
2. `RTSInput` — 输入层
3. `TileRenderer` — 瓦片渲染 (从 FNA_Test 已验证的模式实现)
4. `EntityRenderer` — 实体渲染 (含深度排序)
5. `SelectionRenderer` — 选择渲染
6. `RTSGame` — 主 Game 类，组装系统

### 第 4 步：集成与测试 (约 1 周)

1. FNARTS.Game.Tests — 渲染集成测试
2. 所有测试在 headless 模式下通过
3. 交互模式下手动验证
4. 集成进 FNA_Test/run_tests.sh

---

## 8. 验收检查清单

### 渲染

- [ ] 10×10 等距瓦片地图正确渲染
- [ ] 至少 3 种地形类型视觉可区分 (草地/水域/高地)
- [ ] 摄像机 WASD 平移流畅
- [ ] 摄像机滚轮缩放以鼠标位置为中心
- [ ] 摄像机不超出地图边界

### 建筑

- [ ] 选择建筑类型后可在地图上预览放置位置
- [ ] 单击有效位置放置建筑
- [ ] 建筑放置后在地图上正确显示
- [ ] 建筑占用多格 (如 3×3) 时后续单位不可通过
- [ ] 不可在水域/已有建筑格放置

### 单位

- [ ] 单位精灵在建筑旁正确渲染
- [ ] 单位深度排序正确 (屏幕下方的单位遮挡上方的)
- [ ] 单击单位选中 (高亮指示)
- [ ] 单击空地取消选择
- [ ] 框选多个单位全部高亮

### 移动

- [ ] 选中单位后右键空地 → 单位向目标位置移动
- [ ] 多个单位同时移动互不影响
- [ ] 单位到达目标后停止

### 测试

- [ ] 所有 FNARTS.Core 单元测试通过
- [ ] 所有 FNARTS.Game 集成测试在 headless 模式下通过
- [ ] FNA_Test/run_tests.sh 所有现有测试回归通过
- [ ] FNA_Test/RTS/* 测试全部通过
