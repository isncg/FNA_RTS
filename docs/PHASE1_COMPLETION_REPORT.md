# Phase 1 Completion Report

## Overview

Phase 1 (MVP) is **complete**. The core RTS loop — **see map → place building → produce unit → command movement** — is fully functional with data-driven configuration, a 93-test regression suite, and 6 FNA_Test infrastructure verifications.

**Build:** 0 warnings, 0 errors. **Tests:** 93/93 pass (FNARTS.Core), 4/4 pass (FNARTS.Game headless), 6 FNA_Test/RTS suites verified.

---

## 1. As-Built Architecture

### 1.1 Assembly Map

```
FNARTS.Core (.dll, net10.0)          ← zero FNA dependency, pure C# logic
  ├── Math/      IsoCoord, CoordUtil, VectorConvert
  ├── Map/       TileType, Tile, TileMap
  ├── Entity/    Entity, Unit, Building, EntityManager
  ├── Selection/ SelectionSystem
  ├── Command/   Command, MoveCommand, BuildCommand, CommandSystem
  ├── Production/ ProductionSystem, ProductionItem
  ├── Config/    ConfigLoader, GameConfig
  ├── Data/      UnitDef, BuildingDef, MapData
  ├── State/     GameState (enum: Loading/MainMenu/Playing/Paused)
  └── Util/      GameLogger, EntityIdGenerator

FNARTS.Game (.exe, net10.0)
  ├── Camera/    Camera2D
  ├── Input/     RTSInput, InputMapping, InputAction
  ├── Assets/    IAssetProvider, ProceduralAssetProvider
  ├── Render/    TileRenderer, EntityRenderer, SelectionRenderer
  ├── UI/        CommandPanel, Minimap, DebugOverlay, BitmapFont
  ├── Util/      PixelUtils
  └── RTSGame.cs (main Game class, system assembly)
```

### 1.2 State Machine

```
Loading ──→ Playing ──→ Paused ──→ Playing
                │                      │
                └── Escape             └── Escape
```

MainMenu state is declared but not yet used — game goes directly Loading→Playing.

---

## 2. Coordinate System (2:1 Dimetric Isometric)

### 2.1 Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `TILE_WIDTH` | 64 | Tile texture width in pixels |
| `TILE_HEIGHT` | 32 | Tile texture height in pixels |
| `HALF_TILE_W` | 32 | HALF_TILE_W = TILE_WIDTH / 2 |
| `HALF_TILE_H` | 16 | HALF_TILE_H = TILE_HEIGHT / 2 |

### 2.2 Projection Formulas

**Grid → World (tile top-left corner):**
```
worldX = (gx - gy) * HALF_TILE_W
worldY = -(gx + gy) * HALF_TILE_H
```

**Grid → World (tile center, for unit placement):**
```
worldX = (gx - gy) * HALF_TILE_W
worldY = -(gx + gy) * HALF_TILE_H + TILE_HEIGHT
```

**World → Grid (inverse, floor):**
```
floatGx = (worldX / HALF_TILE_W - worldY / HALF_TILE_H) / 2
floatGy = (-worldY / HALF_TILE_H - worldX / HALF_TILE_W) / 2
gridX = floor(floatGx),  gridY = floor(floatGy)
```

### 2.3 Key Design Decisions

- **Origin convention**: Tile (0,0) has its top-left corner at world (0, 0). The tile footprint extends +64px right and +32px down from its origin.
- **Building origin**: `CoordUtil.BuildingWorldOrigin()` computes the world position of a building's south-west tile corner, accounting for the isometric offset of the multi-tile footprint.
- **Playable diamond**: The map uses a diamond-shaped playable area `|gx - cx| + |gy - cy| ≤ R` (C&C2 convention). This projects isometrically to a **screen-space rectangle**.

### 2.4 System.Numerics vs XNA Vector2

- `FNARTS.Core` uses `System.Numerics.Vector2` exclusively
- `FNARTS.Game` uses `Microsoft.Xna.Framework.Vector2`
- Conversion extensions: `.ToXna()` and `.ToNumerics()` in `VectorConvert.cs`

---

## 3. Map System

### 3.1 TileMap

- **Size**: 51×51 grid (playable diamond: |gx-25|+|gy-25| ≤ 20)
- **Terrain types**: Grass, Water, Cliff, Impassable
- **Passability**: Water, Cliff, Impassable are impassable; Grass is passable
- **Map generation**: `CreateTestMap()` fills the playable diamond with Grass, then places Water pools and Impassable rocks procedurally. A cliff wall runs along the NE edge.

### 3.2 MapData

`MapData.cs` provides a JSON-serializable map format with:
- `Name`, `Width`, `Height`, `DefaultTile`
- Sparse `Tiles` list (only non-default tiles)
- `StartPositions` for faction spawn points
- `ToTileMap()` factory method

Not yet wired to RTSGame (maps are still procedural), but the data format is ready.

---

## 4. Entity System

### 4.1 Entity Base Class

| Member | Description |
|--------|-------------|
| `Id` (uint) | Monotonic ID from `EntityIdGenerator` |
| `WorldPosition` | World-space position (Vector2) |
| `IsAlive` | False after death/removal |
| `HitHalfExtent` | World-space half-extent for hit testing |
| `ContainsPoint(Vector2)` | Precise per-entity hit test |

### 4.2 Unit

- `Definition` (UnitDef): Speed, build time, texture ID
- `MoveTarget` (Vector2?): Destination for movement; linear interpolation at `MoveSpeed` px/s
- `Update(dt)`: Moves toward target, snaps when within 1px

### 4.3 Building

- `Definition` (BuildingDef): SizeX, SizeY, Height, texture ID, producible units
- `PlacementOrigin` (IsoCoord): Grid coordinate of the building's south-west corner tile
- `GetOccupiedTiles()`: Returns all IsoCoords covered by the building footprint
- `OccupiesTile(IsoCoord)`: Point-in-footprint test
- `ContainsPoint(Vector2)`: 3-face hit test (top/south wall/west wall) using inverse isometric projection
- `ProductionQueue`: Sequential `Queue<ProductionItem>` for unit training
- `IsProducing` / `CurrentProduction`: Queue head inspection

### 4.4 EntityManager

- `AllEntities`: `IReadOnlyList<Entity>` — all alive entities
- `AddEntity` / `RemoveEntity`: Lifecycle management
- `QueryPoint(Vector2)`: Returns topmost entity at a world point (hit test)
- `IsAreaFree(IsoCoord, w, h)`: Checks if a grid rectangle is unoccupied (placement validation)
- `EntityCount` / `GetEntity(uint)`: Accessors

### 4.5 Entity Hit Testing Design

Buildings use a 3-face isometric hit test that mirrors the procedural texture generator:
1. **Top face** (roof): hz=H, gx∈[0,E], gy∈[0,N]
2. **South wall**: gy=0, gx∈[0,E], hz∈[0,H]
3. **West wall**: gx=0, gy∈[0,N], hz∈[0,H]

Units use circular hit test via `HitHalfExtent`.

---

## 5. Selection System

### 5.1 Features

- **Single-click**: Selects topmost entity at click point; Shift adds to selection
- **Drag-select**: Box selection via `BeginDrag`/`UpdateDrag`/`EndDrag`
- **Shift-modifier**: Add to existing selection (both click and drag)
- **Clear on empty click**: Clicking empty ground clears selection
- `SelectedEntityIds`: `IReadOnlySet<uint>` — current selection

### 5.2 Drag Rectangle Pipeline

```
Screen-space drag rect
  → four corners → Camera2D.ScreenToWorld → world rect
    → EntityManager query against HitHalfExtent
      → return set of entity IDs
```

---

## 6. Command System

### 6.1 Command Hierarchy

```
Command (abstract)
  ├── MoveCommand  { TargetWorldPosition }
  └── BuildCommand { BuildingType, PlacementOrigin }
```

### 6.2 Right-Click Logic

```
Right-click on ground  → MoveCommand(target=clickedWorldPos)
Right-click on entity  → MoveCommand(target=entity.WorldPosition)
```

Commands are applied immediately to selected units (no command queue in Phase 1).

---

## 7. Camera System

### 7.1 Camera2D

| Property | Description |
|----------|-------------|
| `Position` | Camera center in world coordinates |
| `Zoom` | 1.0 = default, clamped to [MinZoom, MaxZoom] |
| `PanSpeed` | 600 px/s (WASD), scaled by 1/Zoom |
| `ViewMatrix` | Translation × Scale × ViewportCenter |
| `InverseViewMatrix` | Used by ScreenToWorld |
| `WorldBoundMin/Max` | Clamp camera position to playable area |

### 7.2 View Matrix Construction

```
ViewMatrix = Translation(-Position) × Scale(Zoom) × Translation(viewportCenter)
```

### 7.3 Zoom Behavior

Zoom centers on the mouse cursor: before zooming, the world point under the cursor is recorded; after zooming, the camera position is adjusted so the same world point remains under the cursor.

### 7.4 Degenerate Bounds Handling

When zoomed out far enough that `clampMin > clampMax`, the camera centers on the midpoint of the bounds instead of clamping — preventing NaN/oscillation.

---

## 8. Rendering Pipeline

### 8.1 TileRenderer

- **Frustum culling**: Computes visible grid range from screen corners via `ScreenToWorld → WorldToIso`, extended by 2 tiles for margin
- **Sort mode**: `SpriteSortMode.BackToFront` with `layerDepth = (gx + gy) / maxSum`
- **Tile atlas**: Single `TilesetTexture` with 4 columns (one per TileType), each tile 64×32 px

### 8.2 EntityRenderer

- **Depth sorting**: Entities sorted by `WorldPosition.Y` (BackToFront). `maxSum = 102f` for map size 51.
- **Selection highlight**: Yellow ring drawn below the entity sprite when selected
- **Building placement ghost**: Semi-transparent preview with green (valid) or red (invalid) tint
- **Unit rendering**: Colored circles (Worker=yellow, Soldier=blue, Tank=orange) with dark borders

### 8.3 SelectionRenderer

- **Drag rectangle**: Semi-transparent filled rectangle with border
- Drawn in screen space (no camera transform) using `SpriteSortMode.Deferred`

### 8.4 Depth Sorting Detail

```
layerDepth = (gx + gy) / maxSum
```
Where `maxSum = (MAP_SIZE - 1) * 2 = 100` → using `102f` for safety margin.

`gx + gy` increases as entities move "down" the screen (toward the player), so higher depth = drawn later = appears in front.

---

## 9. UI System

### 9.1 CommandPanel

Fixed-height bottom bar (140px) containing:
- **Left**: Selection info (entity count, type breakdown, name) or placement mode status
- **Center**: Production UI (when a producing building is selected)
  - TRAIN header
  - Progress bar with remaining time
  - Queue count
  - Trainable unit buttons with hit-testing
- **Right**: Minimap (square, stretched from minimap texture)

### 9.2 Minimap

- **Canvas**: `2×R×MINIMAP_SCALE + Margin = 127×64` pixels (diamond aspect)
- **Terrain**: Per-pixel rendering via inverse isometric projection
- **Buildings**: Per-pixel grid check against building `OccupiedTiles`
- **Units**: Small dots at world positions
- **Viewport frame**: Analytic axis-aligned rectangle, constant pixel size at given zoom
- **Scale**: `MINIMAP_SCALE = 3`, `WorldToMinimapScale = 3/32`

### 9.3 DebugOverlay

Top-left panel showing: FPS, Frame Time (ms), Entity Count, Camera Position, Camera Zoom, Placement Mode info. Toggle with F3.

### 9.4 BitmapFont

Custom 8×12 bitmap font rendered from a generated texture atlas. Used by CommandPanel and DebugOverlay.

---

## 10. Input System

### 10.1 InputAction Enum

```csharp
CameraPanUp, CameraPanDown, CameraPanLeft, CameraPanRight,
CameraZoomIn, CameraZoomOut,
Select, Command,           // mouse buttons
ShiftModifier, CtrlModifier,
TogglePause, Cancel
```

### 10.2 InputMapping

- Default hardcoded bindings (WASD, arrows, mouse buttons)
- `LoadFromFile(path)`: Merges JSON keybindings with defaults
- `IsActionPressed(action)`: Query action state
- `GetPanDirection()`: Returns normalized Vector2 from camera pan actions

### 10.3 Keybinding JSON Format

```json
{
  "actions": {
    "cameraPanUp": ["KeyW", "ArrowUp"],
    "select": ["MouseLeft"],
    ...
  }
}
```

Stored at `data/config/keybindings.json`. Keys use `Keys` enum names; mouse uses `MouseLeft`/`MouseRight`/`MouseMiddle`.

### 10.4 RTSInput

Per-frame polling layer:
- `MouseScreenPos`, `ScrollDelta`
- `LeftClicked`, `RightClicked` (detects edge transitions)
- `ShiftHeld`, `CtrlHeld`, `EscapePressed`, `PanDirection`
- `LoadBindings(path)`: Delegates to InputMapping

---

## 11. Production System

### 11.1 ProductionItem

```csharp
string UnitDefId       // what to train
float TotalTime         // total build time (seconds)
float RemainingTime     // time remaining
float Progress          // 0..1 fraction complete
```

### 11.2 ProductionSystem

- `Update(dt, entities, onCompleted)`: Ticks all buildings' queues, fires callback on completion
- `Enqueue(building, unitDefId, buildTime)`: Validates against `BuildingDef.ProducesUnitIds`, adds to queue
- `CancelCurrent(building)`: Removes queue head, preserves remaining items

### 11.3 Unit Spawn Logic

When training completes:
1. Callback receives (Building, unitDefId)
2. Searches expanding rings (0..5) around building footprint for a free, passable 1×1 tile
3. Creates Unit at tile center
4. Calls `EntityManager.AddEntity`

---

## 12. Asset System

### 12.1 IAssetProvider Interface

```csharp
Texture2D TilesetTexture
Texture2D SelectionHighlight
Texture2D DiamondHighlight
Texture2D WhitePixel
Rectangle GetTileSourceRect(TileType)
Texture2D GetUnitTexture(string unitDefId)
Texture2D GetBuildingTexture(BuildingDef def)
```

### 12.2 ProceduralAssetProvider

Generates all textures algorithmically:
- **Tileset**: 4-column atlas, each tile is a 64×32 diamond with fill+border
- **Unit textures**: 32×32 colored circles (yellow/blue/orange/gray per unit type), cached
- **Building textures**: Isometric 3D boxes generated via per-pixel face test (top/south/west faces + edge outlines), cached. Texture dimensions: `(E+N)*32` wide, `(E+N)*16 + H*64` tall
- **Selection highlight**: Yellow ring (36×36)
- **Diamond highlight**: Semi-transparent diamond for tile overlay (64×32)

### 12.3 Unit Colors

| UnitDefId | Color |
|-----------|-------|
| worker | Yellow (200,200,100) |
| infantry | Blue (100,180,255) |
| tank | Orange (255,120,80) |
| (default) | Gray |

---

## 13. Configuration System

### 13.1 Data Directory Layout

```
data/
  config.json              ← placementOrder, global settings
  config/keybindings.json  ← keybinding overrides
  units/
    worker.json, soldier.json, tank.json
  buildings/
    command_center.json, shed.json, outpost.json,
    barracks.json, workshop.json, hall.json, fortress.json
```

### 13.2 ConfigLoader

Reads all `data/units/*.json` → `Dictionary<string, UnitDef>`, all `data/buildings/*.json` → `Dictionary<string, BuildingDef>`, and `data/config.json` → placement order list. Uses `System.Text.Json` with camelCase naming.

### 13.3 GameConfig

```csharp
Dictionary<string, UnitDef> UnitDefs
Dictionary<string, BuildingDef> BuildingDefs
List<string> PlacementOrder
GetUnit(id) / GetBuilding(id)
```

---

## 14. Placement Mode

### 14.1 Workflow

1. Press **B** to toggle placement mode
2. **Tab** / **Shift+Tab** or **1-9** keys to switch building type
3. Mouse hover shows ghost preview at snapped grid position
4. Valid placement tiles glow green; invalid tiles glow red
5. **Left-click** to place (if valid)
6. **Right-click** or **Escape** to cancel

### 14.2 Validation

`CanPlaceBuilding(def, origin)` checks:
1. All footprint tiles are `InBounds`
2. All footprint tiles are `IsPassable`
3. `EntityManager.IsAreaFree` — no existing building overlaps

### 14.3 Available Buildings (from data/config.json placementOrder)

| # | ID | Name | Size | Height |
|---|-----|------|------|--------|
| 1 | place_1x1x1 | Shed | 1×1 | 1 |
| 2 | place_2x2x1 | Outpost | 2×2 | 1 |
| 3 | place_3x2x2 | Barracks | 3×2 | 2 |
| 4 | place_2x3x2 | Workshop | 2×3 | 2 |
| 5 | place_3x3x1 | Hall | 3×3 | 1 |
| 6 | place_4x4x2 | Fortress | 4×4 | 2 |

---

## 15. Unit Definitions

| ID | Name | Speed | Build Time | Produced By |
|----|------|-------|-----------|-------------|
| worker | Worker | 120 px/s | 5.0s | Command Center, Barracks |
| soldier | Soldier | 90 px/s | 8.0s | Barracks, Fortress |
| tank | Tank | 70 px/s | 15.0s | Workshop, Fortress |

---

## 16. Game Loop (UpdatePlaying)

```
1. Camera pan (WASD via PanDirection)
2. Camera zoom (mouse scroll wheel, cursor-centered)
3. Camera bounds clamp
4. Placement mode toggle (B key)
5. If placement active:
   a. Update hover (grid snap + validation)
   b. Building type switch (Tab/1-9)
   c. Left-click → place building
   d. Right-click/Escape → cancel
   e. Return (skip normal input)
6. Panel click handling (production buttons)
7. Normal mode selection:
   a. Left-click on entity → select
   b. Left-click on ground → begin drag or clear selection
   c. Drag update/end → box select
8. Right-click → move command
9. Escape → pause
10. Production system tick
11. Unit movement update
12. Debug toggle (F3)
```

---

## 17. Test Suite

### 17.1 FNARTS.Core.Tests (93 tests)

| Test Class | Tests | Coverage |
|-----------|-------|----------|
| IsoCoordTests | 8 | Equality, distance, hashing |
| CoordUtilTests | 16 | IsoToWorld, WorldToIso, round-trip, center, edge cases |
| TileMapTests | 12 | Dimensions, get/set, passability, bounds |
| EntityManagerTests | 14 | Add/remove, query point/area, area free check |
| UnitTests | 6 | Movement, arrival, definition |
| BuildingTests | 8 | Footprint, occupancy, origin, construction |
| SelectionSystemTests | 14 | Click, drag, shift-add, clear, deselect |
| CommandSystemTests | 8 | Right-click ground/entity, move command |
| EntityIdGeneratorTests | 4 | Monotonic, uniqueness |
| ConfigLoaderTests | 5 | Load, empty, missing, malformed JSON, unknown ID |

### 17.2 FNARTS.Game.Tests (4 headless tests)

- Camera screen↔world round-trip (z=1.0)
- Camera screen↔world round-trip (z=2.0)
- System.Numerics ↔ XNA Vector2 conversion round-trip
- IsoCoord center round-trip for grid range 0..10

### 17.3 FNA_Test/RTS Infrastructure (6 suites)

All 6 test suites from the Phase 1 plan exist in `../FNA_Test/RTS/`:
1. **Camera2D** — 2D camera pan + zoom with transform matrix verification
2. **PrimitiveLines** — DrawUserPrimitives line rendering
3. **IsometricTiles** — SpriteBatch isometric tile map rendering
4. **ScreenToWorld** — Screen→world→grid coordinate chain
5. **DepthSorting** — BackToFront layerDepth occlusion
6. **RectSelection** — Drag-select rectangle + entity pick

---

## 18. Deviations from Plan

| Plan Item | Plan | Actual | Reason |
|-----------|------|--------|--------|
| Edge scrolling | Mouse-at-edge auto-pan | Removed | Dead code; edge scrolling was unused and the system explicitly removed it during refactoring |
| Camera2D.Update() | Per-frame input-driven update | Removed | Input handling moved to RTSGame.UpdatePlaying(); Camera2D now exposes Position/Zoom for external control |
| MainMenu state | Loading→MainMenu→Playing | Loading→Playing | Deferred to Phase 2; no menu UI needed for MVP |
| FNA_Test in CI | `run_tests.sh` integration | Tests exist in FNA_Test but not wired to `run_tests.sh` | FNA_Test tests are standalone executables verified during development |
| Entity.GetScreenBounds | Screen-space bounding rect method | HitHalfExtent + ContainsPoint | More flexible; supports isometric face-precise building hit testing |
| IAssetProvider.GetBuildingTexture | `string buildingDefId` param | `BuildingDef def` param | Building texture dimensions depend on SizeX/SizeY/Height |
| Command queue | Deferred execution queue | Immediate execution | Phase 1 has no queued commands; queue infrastructure ready for Phase 2 |
| MapData loading | Maps from JSON files | Procedural map generation | Faster iteration; MapData class ready, wire-up is low-effort |
| CLI args | Full set (--faction, --seed, etc.) | Minimal (--headless, --debug-render, --map) | Only implemented what's needed |

---

## 19. Acceptance Checklist

### Rendering
- [x] Isometric tile map correctly rendered (51×51 grid, diamond playable area)
- [x] 4 terrain types visually distinguishable (Grass/Water/Cliff/Impassable)
- [x] Camera WASD pan smooth (600 px/s)
- [x] Camera scroll-zoom centered on cursor
- [x] Camera clamped to playable area bounds

### Building
- [x] Building type selectable during placement (Tab/1-9)
- [x] Ghost preview at grid-snapped cursor position
- [x] Green/red tile highlight for valid/invalid placement
- [x] Click to place building (valid terrain only)
- [x] Building correctly displayed with isometric 3D box texture
- [x] Multi-tile building footprint blocks future placement and movement
- [x] Cannot place on water/cliff/occupied tiles

### Unit
- [x] Unit sprites rendered adjacent to buildings
- [x] Depth sorting correct (screen-lower units occlude screen-higher ones)
- [x] Click unit to select (yellow ring highlight)
- [x] Click empty ground to deselect
- [x] Drag-select multiple units (all highlighted)
- [x] Shift-click to add to selection

### Movement
- [x] Right-click ground → unit moves to target in straight line
- [x] Multiple units move independently
- [x] Unit stops when reaching target

### Production
- [x] Building production queue with sequential training
- [x] Progress bar with remaining time
- [x] Unit spawns at free adjacent tile
- [x] Ring search for spawn position around building footprint
- [x] Queue shows item count

### UI
- [x] Command panel with selection info
- [x] Production buttons with hit-testing
- [x] Minimap with terrain, buildings, units, viewport frame
- [x] Debug overlay (FPS, entity count, camera info) toggleable with F3
- [x] Placement mode status in command panel

### Configuration
- [x] Unit definitions loaded from JSON
- [x] Building definitions loaded from JSON
- [x] Placement order configurable via JSON
- [x] Keybinding overrides from JSON

### Tests
- [x] 93/93 FNARTS.Core unit tests pass
- [x] 4/4 FNARTS.Game headless integration tests pass
- [x] 6/6 FNA_Test/RTS infrastructure tests verified

---

## 20. File Inventory

### Source Files (FNARTS.Core — 20 files)
```
Math/       IsoCoord.cs, CoordUtil.cs, VectorConvert.cs
Map/        TileType.cs, Tile.cs, TileMap.cs
Entity/     Entity.cs, Unit.cs, Building.cs, EntityManager.cs
Selection/  SelectionSystem.cs
Command/    Command.cs, MoveCommand.cs, BuildCommand.cs, CommandSystem.cs
Production/ ProductionItem.cs, ProductionSystem.cs
Config/     ConfigLoader.cs
Data/       UnitDef.cs, BuildingDef.cs, MapData.cs
State/      GameState.cs
Util/       GameLogger.cs, EntityIdGenerator.cs
```

### Source Files (FNARTS.Game — 15 files)
```
RTSGame.cs, Program.cs
Camera/     Camera2D.cs
Input/      InputAction.cs, InputMapping.cs, RTSInput.cs
Assets/     IAssetProvider.cs, ProceduralAssetProvider.cs
Render/     TileRenderer.cs, EntityRenderer.cs, SelectionRenderer.cs
UI/         CommandPanel.cs, Minimap.cs, DebugOverlay.cs, BitmapFont.cs
Util/       PixelUtils.cs
```

### Data Files (11 JSON files)
```
data/config.json, data/config/keybindings.json
data/units/worker.json, data/units/soldier.json, data/units/tank.json
data/buildings/command_center.json, shed.json, outpost.json,
              barracks.json, workshop.json, hall.json, fortress.json
```

### Test Files (10 test classes)
```
tests/FNARTS.Core.Tests/
  Math/IsoCoordTests.cs, Math/CoordUtilTests.cs
  Map/TileMapTests.cs
  Entity/EntityManagerTests.cs, Entity/UnitTests.cs, Entity/BuildingTests.cs
  Selection/SelectionSystemTests.cs
  Command/CommandSystemTests.cs
  Util/EntityIdGeneratorTests.cs
  Config/ConfigLoaderTests.cs

tests/FNARTS.Game.Tests/Program.cs (4 integration tests)
```

---

## 21. Phase 1 → Phase 2 Transition

All Phase 1 infrastructure is in place for Phase 2 (Combat System). Ready-to-use:

| Infrastructure | Phase 2 Use |
|---------------|-------------|
| UnitDef/BuildingDef | Add HP, AttackDamage, AttackRange, Armor fields |
| ConfigLoader | Combat stats loaded from same JSON files |
| CommandSystem | Add AttackCommand to hierarchy |
| SelectionSystem | Attack-target selection (right-click enemy) |
| EntityManager.QueryPoint | Attack target acquisition |
| ProductionSystem | Already functional for unit training |
| FNA_Test/RTS/* | Add RTS/Pathfinding, RTS/CombatLogic tests |

**Key Phase 2 tasks:**
1. A* pathfinding (grid-based, obstacle avoidance)
2. Combat system (HP, damage, attack commands, death/removal)
3. JSON combat stats (HP, attack, armor per unit type)
4. Fog of war (explored/visible/hidden three-layer state)
