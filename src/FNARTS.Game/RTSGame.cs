using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNARTS.Core;
using FNARTS.Core.Config;
using FNARTS.Core.Movement;
using FNARTS.Core.Pathfinding;
using FNARTS.Core.Production;

namespace FNARTS.Game
{
    public class RTSGame : Microsoft.Xna.Framework.Game
    {
        public static RTSGame Instance { get; private set; }

        private GraphicsDeviceManager _gdm;
        private SpriteBatch _sb;
        private bool _headless;

        private GameState _state = GameState.Loading;

        private TileMap _map;
        private EntityManager _entities;
        private SelectionSystem _selection;
        private CommandSystem _commands;

        private Camera2D _camera;
        private RTSInput _input;
        private DebugOverlay _debugOverlay;

        private TileRenderer _tileRenderer;
        private EntityRenderer _entityRenderer;
        private SelectionRenderer _selectionRenderer;
        private CommandPanel _commandPanel;
        private Minimap _minimap;

        private IAssetProvider _assets;

        private string _mapName;
        private int _frameCount;

        private GameConfig _config;
        private ProductionSystem _productionSystem;
        private Pathfinder _pathfinder;
        private readonly HashSet<IsoCoord> _reservedTiles = new();

        // Placement mode
        private List<BuildingDef> _availableBuildings;
        private int _placementIndex;
        private BuildingDef _placementDef;
        private bool _placementActive;
        private IsoCoord _placementGrid;         // mouse-snapped grid position
        private bool _placementValid;            // current position validity
        private KeyboardState _prevKb;

        public RTSGame(bool headless, bool debugRender, string mapName)
        {
            Instance = this;
            _headless = headless;
            _mapName = mapName;

            _gdm = new GraphicsDeviceManager(this);
            _gdm.PreferredBackBufferWidth = 1280;
            _gdm.PreferredBackBufferHeight = 720;
            _gdm.SynchronizeWithVerticalRetrace = false;
            IsFixedTimeStep = true;
            TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
            IsMouseVisible = true;
            Window.Title = "FNA RTS — Phase 1 MVP";
        }

        protected override void Initialize()
        {
            GameLogger.Info("Initializing RTSGame...");

            _input = new RTSInput();
            // Camera viewport excludes the command panel.
            _camera = new Camera2D(_gdm.PreferredBackBufferWidth,
                _gdm.PreferredBackBufferHeight - CommandPanel.PANEL_H);

            _debugOverlay = new DebugOverlay();
            _debugOverlay.Enabled = true;
            _debugOverlay.Initialize(GraphicsDevice);

            // ── Data directory (used by config & keybindings) ──────────
            string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
            if (!Directory.Exists(dataDir))
            {
                // Fallback: running from project root (dotnet run)
                dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
            }

            // Load keybindings (falls back to hardcoded defaults if file missing)
            string kbPath = Path.Combine(dataDir, "config", "keybindings.json");
            if (File.Exists(kbPath))
                _input.LoadBindings(kbPath);

            // ── Load data-driven config ──────────────────────────────────
            _config = ConfigLoader.Load(dataDir);

            // Build available placement list from config.json placementOrder
            _availableBuildings = new List<BuildingDef>();
            foreach (var id in _config.PlacementOrder)
            {
                var def = _config.GetBuilding(id);
                if (def != null)
                    _availableBuildings.Add(def);
            }

            // ── Production system ─────────────────────────────────────
            _productionSystem = new ProductionSystem();
            _placementIndex = 1; // start on Outpost (2×2×1)
            _placementDef = _availableBuildings[_placementIndex];

            _prevKb = Keyboard.GetState();

            _minimap = new Minimap(GraphicsDevice);
            CreateTestMap();
            _minimap.SetMap(_map, MAP_CENTER, MAP_CENTER, MAP_RADIUS);
            _minimap.SetPlayableArea(InPlayableDiamond);
            _entities = new EntityManager();
            _selection = new SelectionSystem();
            _commands = new CommandSystem();

            // Pathfinder: inject passability via delegates (Core has no TileMap ref)
            _pathfinder = new Pathfinder
            {
                MapWidth = _map.Width,
                MapHeight = _map.Height,
                IsPassable = coord =>
                    _map.InBounds(coord) &&
                    _map.IsPassable(coord) &&
                    _entities.IsAreaFree(coord, 1, 1) &&
                    !_reservedTiles.Contains(coord),
            };

            CreateTestEntities();

            // Camera bounds: playable diamond |gx-cx|+|gy-cy| ≤ R.
            // IsoToWorld gives the tile top-left corner; the diamond
            // extends +TILE_WIDTH right and +TILE_HEIGHT down from there.
            int R = MAP_RADIUS;
            int cx = MAP_CENTER, cy = MAP_CENTER;
            float playMinWx = (cx - cy - R) * CoordUtil.HALF_TILE_W;   // -640
            float playMaxWx = (cx - cy + R) * CoordUtil.HALF_TILE_W
                            + CoordUtil.TILE_WIDTH;                     // +64 → 704
            float playMinWy = -(cx + cy + R) * CoordUtil.HALF_TILE_H;  // -1120
            float playMaxWy = -(cx + cy - R) * CoordUtil.HALF_TILE_H
                            + CoordUtil.TILE_HEIGHT;                    // +32 → -448
            _camera.WorldBoundMin = new Vector2(playMinWx, playMinWy);
            _camera.WorldBoundMax = new Vector2(playMaxWx, playMaxWy);
            // Centre camera on the bounding box of the playable diamond.
            _camera.Position = new Vector2(
                (playMinWx + playMaxWx) / 2f,
                (playMinWy + playMaxWy) / 2f);
            _camera.RebuildMatrices();

            _state = GameState.Playing;
            GameLogger.Info("RTSGame initialized.");
            base.Initialize();
        }

        // Grid-space diamond (|gx-cx| + |gy-cy| ≤ R) projects to a
        // screen-space RECTANGLE.  This is the core C&amp;C2 design.
        private const int MAP_CENTER = 25;   // grid centre of playable diamond
        private const int MAP_RADIUS = 20;    // |gx-cx| + |gy-cy| ≤ R
        private const int MAP_SIZE   = 51;    // full grid (0..50)

        // Stuck detection
        private const float STUCK_MOVE_THRESHOLD = 3f;   // pixels moved to reset stuck timer
        private const float STUCK_TIMEOUT = 2f;           // seconds before recompute
        private const int MAX_STUCK_RECOMPUTES = 3;       // max recomputes per move order

        private static bool InPlayableDiamond(int gx, int gy)
        {
            return Math.Abs(gx - MAP_CENTER) + Math.Abs(gy - MAP_CENTER) <= MAP_RADIUS;
        }

        private void CreateTestMap()
        {
            _map = new TileMap(MAP_SIZE, MAP_SIZE);

            // Default: cliff boundary everywhere
            for (int x = 0; x < MAP_SIZE; x++)
            for (int y = 0; y < MAP_SIZE; y++)
                _map.SetTile(x, y, new Tile(TileType.Cliff));

            // Fill the playable diamond with grass
            for (int x = 0; x < MAP_SIZE; x++)
            for (int y = 0; y < MAP_SIZE; y++)
                if (InPlayableDiamond(x, y))
                    _map.SetTile(x, y, new Tile(TileType.Grass));

            // Place original terrain features, shifted to centre of diamond.
            // Old 20×20 map centred near (10,10) → shift by (15,15).
            int Sx = MAP_CENTER - 10;  // shift x
            int Sy = MAP_CENTER - 10;  // shift y

            // Central water pool
            for (int x = 8; x <= 12; x++)
            for (int y = 8; y <= 12; y++)
                _map.SetTile(x + Sx, y + Sy, new Tile(TileType.Water));

            // Scattered impassable rocks
            _map.SetTile(Sx + 3,  Sy + 5,  new Tile(TileType.Impassable));
            _map.SetTile(Sx + 15, Sy + 10, new Tile(TileType.Impassable));
            _map.SetTile(Sx + 10, Sy + 3,  new Tile(TileType.Impassable));

            // East cliff wall (inside the diamond, near the NE edge)
            for (int y = 0; y < 20; y++)
            {
                int gx = Sx + 19, gy = Sy + y;
                if (InPlayableDiamond(gx, gy))
                    _map.SetTile(gx, gy, new Tile(TileType.Cliff));
            }

            GameLogger.Info($"Created {_map.Width}x{_map.Height} map " +
                $"(diamond |gx-{MAP_CENTER}|+|gy-{MAP_CENTER}|≤{MAP_RADIUS})");
        }

        private void CreateTestEntities()
        {
            int S = MAP_CENTER - 10;

            // ── Gallery of buildings: max 4×4, |E−N| ≤ 1 ──────────────
            var specs = new (string label, int gx, int gy, int E, int N, int H)[]
            {
                ("1×1×1",  1,  1,  1, 1, 1),
                ("2×1×1",  4,  2,  2, 1, 1),
                ("1×2×1",  8,  2,  1, 2, 1),
                ("2×2×1",  11, 2,  2, 2, 1),
                ("3×2×2",  1,  5,  3, 2, 2),
                ("2×3×2",  6,  5,  2, 3, 2),
                ("3×3×1",  10, 5,  3, 3, 1),
                ("4×3×2",  1,  9,  4, 3, 2),
                ("3×4×3",  7,  9,  3, 4, 3),
                ("4×4×2",  14, 9,  4, 4, 2),
            };

            foreach (var (label, gx, gy, E, N, H) in specs)
            {
                var def = new BuildingDef
                {
                    Id = $"bld_{E}x{N}x{H}",
                    Name = $"{label} {(H > 1 ? $"{H}fl" : "")}",
                    SizeX = E, SizeY = N, Height = H,
                    TextureId = $"gen_{E}_{N}_{H}",
                };
                var b = new Building(def, new IsoCoord(gx + S, gy + S));
                _entities.AddEntity(b);
            }

            // A few workers
            var workerDef = _config.GetUnit("worker");
            for (int i = 0; i < 5; i++)
            {
                var unit = new Unit(workerDef);
                unit.WorldPosition = CoordUtil.IsoToWorldCenter(
                    new IsoCoord(S + 3 + i, S + 1));
                _entities.AddEntity(unit);
            }

            GameLogger.Info($"Created {_entities.AllEntities.Count} entities " +
                $"({specs.Length} buildings, 5 units)");
        }

        protected override void LoadContent()
        {
            _sb = new SpriteBatch(GraphicsDevice);
            _assets = new ProceduralAssetProvider(GraphicsDevice);
            _tileRenderer = new TileRenderer(_map, _assets);
            _entityRenderer = new EntityRenderer(_assets);
            _selectionRenderer = new SelectionRenderer(_assets);
            _commandPanel = new CommandPanel(GraphicsDevice);
            GameLogger.Info("Content loaded");
        }

        protected override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _frameCount++;

            if (_headless && _frameCount > 10) Exit();

            _input.Update();

            if (_state == GameState.Playing)
                UpdatePlaying(dt);
            else if (_state == GameState.Paused && _input.EscapePressed)
                _state = GameState.Playing;

            base.Update(gameTime);
        }

        private bool CanPlaceBuilding(BuildingDef def, IsoCoord origin)
        {
            for (int x = 0; x < def.SizeX; x++)
            for (int y = 0; y < def.SizeY; y++)
            {
                var c = new IsoCoord(origin.X + x, origin.Y + y);
                if (!_map.InBounds(c) || !_map.IsPassable(c))
                    return false;
            }
            return _entities.IsAreaFree(origin, def.SizeX, def.SizeY);
        }

        private void UpdatePlaying(float dt)
        {
            // Camera pan (always active, even during placement)
            var panDir = _input.PanDirection;
            if (panDir != Vector2.Zero)
            {
                panDir.Normalize();
                _camera.Position += panDir * _camera.PanSpeed / _camera.Zoom * dt;
            }

            // Zoom
            if (_input.ScrollDelta != 0)
            {
                float oldZoom = _camera.Zoom;
                float delta = _input.ScrollDelta > 0 ? _camera.ZoomSpeed : -_camera.ZoomSpeed;
                _camera.Zoom = MathHelper.Clamp(_camera.Zoom + delta,
                    _camera.MinZoom, _camera.MaxZoom);
                if (_camera.Zoom != oldZoom)
                {
                    var before = _camera.ScreenToWorld(_input.MouseScreenPos);
                    _camera.RebuildMatrices();
                    var after = _camera.ScreenToWorld(_input.MouseScreenPos);
                    _camera.Position += before - after;
                    _camera.ClampToBounds();
                }
            }
            _camera.ClampToBounds();
            _camera.RebuildMatrices();

            // ── Placement mode toggle ────────────────────────────────
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.B) && _prevKb.IsKeyUp(Keys.B))
            {
                _placementActive = !_placementActive;
                if (_placementActive)
                {
                    _selection.ClearSelection();
                    UpdatePlacementHover();
                    GameLogger.Info($"Placement mode: [{_placementIndex + 1}/{_availableBuildings.Count}] " +
                        $"{_placementDef.Name} ({_placementDef.SizeX}×{_placementDef.SizeY}×{_placementDef.Height}) — " +
                        "Tab/1-6: switch type, LClick: place, RCancel/Esc: cancel");
                }
                else
                {
                    GameLogger.Info("Placement mode cancelled");
                }
            }

            // ── Placement mode ───────────────────────────────────────
            if (_placementActive)
            {
                UpdatePlacementHover();

                // Building type selection
                bool typeChanged = false;
                int count = _availableBuildings.Count;

                // Tab = next, Shift+Tab = previous
                if (kb.IsKeyDown(Keys.Tab) && _prevKb.IsKeyUp(Keys.Tab))
                {
                    if (_input.ShiftHeld)
                        _placementIndex = (_placementIndex - 1 + count) % count;
                    else
                        _placementIndex = (_placementIndex + 1) % count;
                    typeChanged = true;
                }
                // Number keys 1-9 for direct selection (1-based)
                for (int i = 0; i < count && i < 9; i++)
                {
                    var key = Keys.D1 + i;
                    if (kb.IsKeyDown(key) && _prevKb.IsKeyUp(key))
                    {
                        _placementIndex = i;
                        typeChanged = true;
                        break;
                    }
                }

                if (typeChanged)
                {
                    _placementDef = _availableBuildings[_placementIndex];
                    UpdatePlacementHover(); // re-validate at new size
                    GameLogger.Info($"Placement: [{_placementIndex + 1}/{count}] {_placementDef.Name} " +
                        $"({_placementDef.SizeX}×{_placementDef.SizeY}×{_placementDef.Height})");
                }

                if (_input.LeftClicked && _placementValid)
                {
                    var b = new Building(_placementDef, _placementGrid);
                    _entities.AddEntity(b);
                    GameLogger.Info($"Placed {_placementDef.Name} at " +
                        $"({_placementGrid.X},{_placementGrid.Y})");
                    _placementActive = false;
                }
                else if (_input.RightClicked || _input.EscapePressed)
                {
                    _placementActive = false;
                }

                _prevKb = kb;
                return; // skip normal input while placing
            }

            // ── Panel clicks (production buttons take priority) ────────
            int panelY = _gdm.PreferredBackBufferHeight - CommandPanel.PANEL_H;
            bool mouseInPanel = _input.MouseScreenPos.Y >= panelY;
            if (_input.LeftClicked && mouseInPanel)
            {
                var unitId = _commandPanel.HandleClick(_input.MouseScreenPos);
                if (unitId != null && _config.UnitDefs.TryGetValue(unitId, out var ud))
                {
                    // Find the currently selected building to enqueue on
                    var selBld = GetSelectedBuilding();
                    if (selBld != null)
                        _productionSystem.Enqueue(selBld, unitId, ud.BuildTime);
                }
            }

            // ── Normal mode: selection ───────────────────────────────
            if (_input.LeftClicked && !mouseInPanel)
            {
                var worldPosN = _camera.ScreenToWorld(_input.MouseScreenPos).ToNumerics();
                var entity = _entities.QueryPoint(worldPosN);
                if (entity != null)
                {
                    _selection.Select(entity, _input.ShiftHeld);
                }
                else
                {
                    _selection.ClearSelection();
                    _selection.BeginDrag(_input.MouseScreenPos.ToNumerics());
                }
            }
            else if (_selection.DragActive &&
                     Mouse.GetState().LeftButton == ButtonState.Pressed)
            {
                _selection.UpdateDrag(_input.MouseScreenPos.ToNumerics());
            }
            else if (_selection.DragActive)
            {
                var selected = _selection.EndDrag(
                    _input.MouseScreenPos.ToNumerics(), _entities,
                    p => _camera.ScreenToWorld(p.ToXna()).ToNumerics());
                _selection.SelectMultiple(selected, _input.ShiftHeld);
            }

            // Right-click command — A* pathfinding + formation (Phase 2)
            if (_input.RightClicked)
            {
                var worldPosN = _camera.ScreenToWorld(_input.MouseScreenPos).ToNumerics();
                var cmd = _commands.ProcessRightClick(worldPosN, _entities, _selection);

                // Collect selected units
                var selectedUnits = new System.Collections.Generic.List<Unit>();
                foreach (var id in _selection.SelectedEntityIds)
                {
                    if (_entities.GetEntity(id) is Unit u)
                        selectedUnits.Add(u);
                }

                if (selectedUnits.Count == 1)
                {
                    // Single unit — use click point directly
                    AssignUnitPath(selectedUnits[0], cmd.TargetWorldPosition);
                }
                else if (selectedUnits.Count > 1)
                {
                    // ── Multi-unit formation: greedy closest-assignment with
                    //     tile reservation (back-to-front, C&C2 style) ──────
                    var centerTile = CoordUtil.WorldToIso(cmd.TargetWorldPosition);
                    var tiles = FormationPosition.Compute(centerTile, selectedUnits.Count);

                    // group centroid in grid coords (for back-to-front ordering)
                    float cx = 0, cy = 0;
                    foreach (var u in selectedUnits)
                    {
                        var gc = CoordUtil.WorldToIso(u.WorldPosition);
                        cx += gc.X; cy += gc.Y;
                    }
                    cx /= selectedUnits.Count; cy /= selectedUnits.Count;

                    // Sort tiles: farthest from centroid first so "back-row"
                    // units pathfind while "front-row" tiles are still passable.
                    Array.Sort(tiles, (a, b) =>
                    {
                        float da = (a.X - cx) * (a.X - cx) + (a.Y - cy) * (a.Y - cy);
                        float db = (b.X - cx) * (b.X - cx) + (b.Y - cy) * (b.Y - cy);
                        return db.CompareTo(da); // descending
                    });

                    var assigned = new bool[selectedUnits.Count];
                    _reservedTiles.Clear();

                    for (int ti = 0; ti < tiles.Length; ti++)
                    {
                        var tile = tiles[ti];

                        // Find closest unassigned unit (octile distance)
                        int bestIdx = -1;
                        int bestDist = int.MaxValue;
                        for (int ui = 0; ui < selectedUnits.Count; ui++)
                        {
                            if (assigned[ui]) continue;
                            var upos = CoordUtil.WorldToIso(selectedUnits[ui].WorldPosition);
                            int d = Pathfinder.OctileDistance(upos, tile);
                            if (d < bestDist) { bestDist = d; bestIdx = ui; }
                        }

                        if (bestIdx < 0) break; // shouldn't happen

                        var unit = selectedUnits[bestIdx];
                        assigned[bestIdx] = true;

                        var start = CoordUtil.WorldToIso(unit.WorldPosition);
                        var path = TryFindPath(start, tile);

                        if (path != null)
                        {
                            unit.MoveTarget = CoordUtil.IsoToVisualCenter(tile);
                            unit.Path = path;
                            unit.PathIndex = 0;
                            unit.ResetStuckTracking();
                            _reservedTiles.Add(tile);
                        }
                        else if (start == tile)
                        {
                            unit.MoveTarget = CoordUtil.IsoToVisualCenter(tile);
                            unit.Path = null;
                            unit.PathIndex = 0;
                            unit.ResetStuckTracking();
                            _reservedTiles.Add(tile);
                        }
                        else
                        {
                            // Fallback: ring search (up to 5 tiles)
                            IsoCoord? fallback = null;
                            List<IsoCoord> fallbackPath = null;
                            for (int ring = 1; ring <= 5; ring++)
                            {
                                for (int dx = -ring; dx <= ring; dx++)
                                for (int dy = -ring; dy <= ring; dy++)
                                {
                                    if (Math.Abs(dx) < ring && Math.Abs(dy) < ring)
                                        continue;
                                    var ft = new IsoCoord(tile.X + dx, tile.Y + dy);
                                    var fp = TryFindPath(start, ft);
                                    if (fp != null)
                                    {
                                        fallback = ft;
                                        fallbackPath = fp;
                                        goto fallbackFound;
                                    }
                                }
                            }
                            fallbackFound:
                            if (fallback.HasValue && fallbackPath != null)
                            {
                                unit.MoveTarget = CoordUtil.IsoToVisualCenter(fallback.Value);
                                unit.Path = fallbackPath;
                                unit.PathIndex = 0;
                                unit.ResetStuckTracking();
                                _reservedTiles.Add(fallback.Value);
                            }
                            // else: completely boxed in — unit stays put
                        }
                    }

                    _reservedTiles.Clear();
                }
            }

            // Pause
            if (_input.EscapePressed)
                _state = GameState.Paused;

            // Production system tick
            _productionSystem.Update(dt, _entities, OnUnitSpawned);

            // Entity updates + stuck detection
            foreach (var e in _entities.AllEntities)
            {
                if (e is Unit unit)
                {
                    unit.Update(dt);

                    // Stuck detection: if unit has a path but isn't making progress,
                    // recompute after a timeout (max 3 recomputes per move order).
                    if (unit.IsMoving && unit.Path != null)
                    {
                        float moved = System.Numerics.Vector2.Distance(
                            unit.WorldPosition, unit.LastStuckCheckPos);
                        if (moved < STUCK_MOVE_THRESHOLD)
                        {
                            unit.StuckTimer += dt;
                            if (unit.StuckTimer > STUCK_TIMEOUT
                                && unit.StuckRecomputeCount < MAX_STUCK_RECOMPUTES)
                            {
                                RecomputeStuckPath(unit);
                            }
                        }
                        else
                        {
                            unit.StuckTimer = 0f;
                            unit.LastStuckCheckPos = unit.WorldPosition;
                        }
                    }
                    else
                    {
                        unit.StuckTimer = 0f;
                    }
                }
            }

            // Separation — push nearby units apart to prevent overlap
            ApplySeparation(dt);

            // Debug toggle
            if (kb.IsKeyDown(Keys.F3) && _prevKb.IsKeyUp(Keys.F3))
                _debugOverlay.Enabled = !_debugOverlay.Enabled;

            _prevKb = kb;
        }

        private void UpdatePlacementHover()
        {
            var worldPos = _camera.ScreenToWorld(_input.MouseScreenPos).ToNumerics();
            _placementGrid = CoordUtil.WorldToIso(worldPos);
            _placementValid = CanPlaceBuilding(_placementDef, _placementGrid);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(20, 20, 30));

            if (_state == GameState.Playing || _state == GameState.Paused)
            {
                _tileRenderer.Draw(_sb, _camera);

                // Placement tile highlights (green/red diamonds under ghost)
                if (_placementActive)
                {
                    var def = _placementDef;
                    var tiles = new System.Collections.Generic.List<IsoCoord>();
                    for (int x = 0; x < def.SizeX; x++)
                    for (int y = 0; y < def.SizeY; y++)
                        tiles.Add(new IsoCoord(_placementGrid.X + x, _placementGrid.Y + y));
                    var tint = _placementValid
                        ? new Color(0, 255, 0, 120)   // green, semi-transparent
                        : new Color(255, 60, 60, 120); // red, semi-transparent
                    _tileRenderer.DrawHighlights(_sb, _camera, tiles, tint);
                }

                _entityRenderer.Draw(_sb, _camera, _entities, _selection);

                // Placement ghost
                if (_placementActive)
                {
                    _entityRenderer.DrawGhost(_sb, _camera,
                        _placementDef, _placementGrid, _placementValid);
                }

                if (_selection.DragActive)
                {
                    _selectionRenderer.DrawDragRect(_sb,
                        _selection.DragStart.ToXna(),
                        _selection.DragEnd.ToXna());
                }
            }

            if (_debugOverlay.Enabled)
            {
                _debugOverlay.Fps = 1f / (float)gameTime.ElapsedGameTime.TotalSeconds;
                _debugOverlay.FrameTimeMs = gameTime.ElapsedGameTime.TotalMilliseconds;
                _debugOverlay.EntityCount = _entities.AllEntities.Count;
                _debugOverlay.CameraPos = _camera.Position;
                _debugOverlay.CameraZoom = _camera.Zoom;
                _debugOverlay.PlacementActive = _placementActive;
                _debugOverlay.PlacementInfo = _placementActive
                    ? $"{_placementDef?.Name} {_placementDef?.SizeX}x{_placementDef?.SizeY} grid=({_placementGrid.X},{_placementGrid.Y}) valid={_placementValid}"
                    : "";
                _debugOverlay.Draw();
            }

            // ── Command panel (bottom bar) ─────────────────────────
            _minimap.Render(_entities, _selection, _camera);
            _commandPanel.ViewportW = _gdm.PreferredBackBufferWidth;
            _commandPanel.ViewportH = _gdm.PreferredBackBufferHeight;
            _commandPanel.Selection = _selection;
            _commandPanel.Entities = _entities;
            _commandPanel.PlacementActive = _placementActive;
            _commandPanel.PlacementName = _placementDef?.Name ?? "";
            _commandPanel.PlacementIndex = _placementIndex;
            _commandPanel.PlacementCount = _availableBuildings?.Count ?? 0;
            _commandPanel.MinimapTexture = _minimap.Texture;
            _commandPanel.SelectedBuilding = GetSelectedBuilding();
            _commandPanel.UnitDefs = _config.UnitDefs;
            _commandPanel.Draw(_sb);

            base.Draw(gameTime);
        }

        /// <summary>
        /// Return the selected building if exactly one building is selected.
        /// </summary>
        private Building GetSelectedBuilding()
        {
            if (_placementActive) return null;
            if (_selection == null || _selection.SelectedEntityIds.Count != 1) return null;
            uint id = 0;
            foreach (var i in _selection.SelectedEntityIds) { id = i; break; }
            var e = _entities.GetEntity(id);
            return e as Building;
        }

        /// <summary>
        /// Called by ProductionSystem when a unit finishes training.
        /// Finds a free adjacent tile and spawns the unit.
        /// </summary>
        private void OnUnitSpawned(Building building, string unitDefId)
        {
            if (!_config.UnitDefs.TryGetValue(unitDefId, out var ud)) return;

            var spawnTile = FindFreeAdjacentTile(building);
            if (spawnTile == null) return;

            var unit = new Unit(ud);
            unit.WorldPosition = CoordUtil.IsoToWorldCenter(spawnTile.Value);
            _entities.AddEntity(unit);
            GameLogger.Info($"Unit spawned: {ud.Name} from {building.Definition.Name}");
        }

        /// <summary>
        /// Search expanding rings around a building for a free, passable 1x1 tile.
        /// Returns null if no free tile found within a reasonable radius.
        /// </summary>
        private IsoCoord? FindFreeAdjacentTile(Building building)
        {
            int bx = building.PlacementOrigin.X;
            int by = building.PlacementOrigin.Y;
            int sx = building.SizeX, sy = building.SizeY;

            // Search rings out to 5 tiles beyond the footprint
            for (int ring = 0; ring <= 5; ring++)
            {
                // Top and bottom edges of the ring
                for (int dx = -ring; dx <= sx - 1 + ring; dx++)
                {
                    if (TrySpawnTile(bx + dx, by - ring)) return new IsoCoord(bx + dx, by - ring);
                    if (TrySpawnTile(bx + dx, by + sy - 1 + ring)) return new IsoCoord(bx + dx, by + sy - 1 + ring);
                }
                // Left and right edges (skip corners already checked)
                for (int dy = -ring + 1; dy <= sy - 1 + ring - 1; dy++)
                {
                    if (TrySpawnTile(bx - ring, by + dy)) return new IsoCoord(bx - ring, by + dy);
                    if (TrySpawnTile(bx + sx - 1 + ring, by + dy)) return new IsoCoord(bx + sx - 1 + ring, by + dy);
                }
            }
            return null;
        }

        private bool TrySpawnTile(int gx, int gy)
        {
            if (!_map.InBounds(gx, gy)) return false;
            if (!_map.IsPassable(new IsoCoord(gx, gy))) return false;
            return _entities.IsAreaFree(new IsoCoord(gx, gy), 1, 1);
        }

        /// <summary>
        /// Compute and assign a path for a single unit to a world-space target.
        /// </summary>
        private void AssignUnitPath(Unit unit, System.Numerics.Vector2 targetWorldPos)
        {
            var start = CoordUtil.WorldToIso(unit.WorldPosition);
            var end = CoordUtil.WorldToIso(targetWorldPos);
            var path = _pathfinder.FindPath(start, end);

            if (path.Count > 0)
            {
                unit.MoveTarget = targetWorldPos;
                unit.Path = path;
                unit.PathIndex = 0;
                unit.ResetStuckTracking();
            }
            else if (start == end)
            {
                // Same tile — straight-line is safe (no intermediate obstacles).
                unit.MoveTarget = targetWorldPos;
                unit.Path = null;
                unit.PathIndex = 0;
                unit.ResetStuckTracking();
            }
            // else: unreachable (inside building, blocked, etc.) — don't move.
        }

        /// <summary>
        /// Compute and assign a path for a single unit to a specific tile centre
        /// (C&amp;C2 vehicle-style: one unit per tile, snapped to the tile centre).
        /// If the ideal tile is unreachable, searches nearby for the closest
        /// reachable tile (up to 3 tiles away).
        /// </summary>
        private void AssignUnitPath(Unit unit, IsoCoord targetTile)
        {
            var start = CoordUtil.WorldToIso(unit.WorldPosition);
            var path = TryFindPath(start, targetTile);

            if (path != null)
            {
                unit.MoveTarget = CoordUtil.IsoToVisualCenter(targetTile);
                unit.Path = path;
                unit.PathIndex = 0;
                unit.ResetStuckTracking();
                return;
            }

            if (start == targetTile)
            {
                unit.MoveTarget = CoordUtil.IsoToVisualCenter(targetTile);
                unit.Path = null;
                unit.PathIndex = 0;
                unit.ResetStuckTracking();
                return;
            }

            // Fallback: search expanding rings for a reachable tile
            for (int ring = 1; ring <= 5; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                for (int dy = -ring; dy <= ring; dy++)
                {
                    if (Math.Abs(dx) < ring && Math.Abs(dy) < ring)
                        continue; // inner ring, already checked

                    var fallbackTile = new IsoCoord(targetTile.X + dx, targetTile.Y + dy);
                    path = TryFindPath(start, fallbackTile);
                    if (path != null)
                    {
                        unit.MoveTarget = CoordUtil.IsoToVisualCenter(fallbackTile);
                        unit.Path = path;
                        unit.PathIndex = 0;
                        unit.ResetStuckTracking();
                        return;
                    }
                }
            }
            // Completely boxed in — don't move.
        }

        /// <summary>Try to find a path. Returns null if unreachable.</summary>
        private System.Collections.Generic.List<IsoCoord> TryFindPath(
            IsoCoord start, IsoCoord end)
        {
            var path = _pathfinder.FindPath(start, end);
            return path.Count > 0 ? path : null;
        }

        /// <summary>
        /// Recompute a path for a stuck unit.  Clears the current path,
        /// re-pathfinds from the current position to the original target,
        /// and falls back to a ring search if the ideal tile is unreachable.
        /// </summary>
        private void RecomputeStuckPath(Unit unit)
        {
            unit.StuckTimer = 0f;
            unit.StuckRecomputeCount++;

            var start = CoordUtil.WorldToIso(unit.WorldPosition);

            // Determine the target tile from the current MoveTarget.
            var targetTile = CoordUtil.WorldToIso(unit.MoveTarget ?? unit.WorldPosition);

            unit.Path = null;
            unit.PathIndex = 0;

            if (start == targetTile)
            {
                unit.MoveTarget = CoordUtil.IsoToVisualCenter(targetTile);
                return;
            }

            var path = TryFindPath(start, targetTile);
            if (path != null)
            {
                unit.Path = path;
                unit.PathIndex = 0;
                unit.LastStuckCheckPos = unit.WorldPosition;
                unit.StuckTimer = 0f;
                return;
            }

            // Fallback: ring search for an alternative tile (up to 4 rings —
            // fewer than the initial 5-ring search since we've already tried once).
            for (int ring = 1; ring <= 4; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                for (int dy = -ring; dy <= ring; dy++)
                {
                    if (Math.Abs(dx) < ring && Math.Abs(dy) < ring)
                        continue;
                    var ft = new IsoCoord(targetTile.X + dx, targetTile.Y + dy);
                    var fp = TryFindPath(start, ft);
                    if (fp != null)
                    {
                        unit.MoveTarget = CoordUtil.IsoToVisualCenter(ft);
                        unit.Path = fp;
                        unit.PathIndex = 0;
                        unit.LastStuckCheckPos = unit.WorldPosition;
                        unit.StuckTimer = 0f;
                        return;
                    }
                }
            }
            // No alternative found — unit stays stuck (max recomputes will prevent
            // further attempts for this move order).
        }

        /// <summary>
        /// Apply local separation force to all alive units so they don't overlap.
        /// </summary>
        private void ApplySeparation(float dt)
        {
            // Collect ALL alive unit positions (stationary units act as repellers).
            var allPositions = new System.Collections.Generic.List<System.Numerics.Vector2>();
            var movingUnits = new System.Collections.Generic.List<Unit>();
            foreach (var e in _entities.AllEntities)
            {
                if (e is Unit u && u.IsAlive)
                {
                    allPositions.Add(u.WorldPosition);
                    if (u.IsMoving)
                        movingUnits.Add(u);
                }
            }

            if (allPositions.Count < 2 || movingUnits.Count == 0)
                return;

            // Only apply separation TO moving units — stationary units hold their tile.
            // Moving units are repelled from ALL nearby units (moving or stationary).
            foreach (var unit in movingUnits)
            {
                var separation = SeparationBehavior.Compute(
                    unit.WorldPosition, allPositions);
                unit.WorldPosition += separation * dt * 60f;  // normalise to 1/60s step
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _assets?.Dispose();
                _tileRenderer?.Dispose();
                _entityRenderer?.Dispose();
                _debugOverlay?.Dispose();
                _commandPanel?.Dispose();
                _minimap?.Dispose();
                _sb?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
