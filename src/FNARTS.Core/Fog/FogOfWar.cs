using System;

namespace FNARTS.Core.Fog
{
    /// <summary>
    /// Fog-of-war system tracks per-cell visibility for a single player.
    /// Three-state model: Unexplored → Explored → Visible.
    ///
    /// Pure C#, zero FNA dependency.  Designed for single-player Phase 2.5;
    /// Phase 3 networking will extend this to track per-faction visibility.
    /// </summary>
    public class FogOfWar
    {
        private readonly FogCell[,] _cells;
        private readonly int _width, _height;

        public int Width => _width;
        public int Height => _height;

        public FogOfWar(int width, int height)
        {
            _width = width;
            _height = height;
            _cells = new FogCell[width, height];
            // All cells default to Unexplored (0).
        }

        /// <summary>Indexer — returns Unexplored for out-of-bounds queries.</summary>
        public FogCell this[int x, int y]
            => InBounds(x, y) ? _cells[x, y] : FogCell.Unexplored;

        public FogCell this[IsoCoord c] => this[c.X, c.Y];

        public bool IsVisible(IsoCoord c) => this[c] == FogCell.Visible;
        public bool IsExplored(IsoCoord c) => this[c] == FogCell.Explored;
        public bool IsUnexplored(IsoCoord c) => this[c] == FogCell.Unexplored;

        /// <summary>Reveal every cell (debug / no-fog mode).</summary>
        public void RevealAll()
        {
            for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
                _cells[x, y] = FogCell.Visible;
        }

        /// <summary>Force a rectangular area to Visible.</summary>
        public void RevealRect(int minX, int minY, int maxX, int maxY)
        {
            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
                if (InBounds(x, y))
                    _cells[x, y] = FogCell.Visible;
        }

        /// <summary>
        /// Per-frame update: degrade all Visible → Explored, then re-reveal
        /// tiles within vision range of each alive friendly entity.
        /// </summary>
        public void Update(EntityManager entities, int playerFaction)
        {
            // Step 1 — degrade: Visible → Explored
            for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
                if (_cells[x, y] == FogCell.Visible)
                    _cells[x, y] = FogCell.Explored;

            // Step 2 — re-reveal around each friendly entity
            foreach (var e in entities.AllEntities)
            {
                if (!e.IsAlive || e.Faction != playerFaction)
                    continue;

                int range = e switch
                {
                    Unit u => u.Definition.VisionRange,
                    Building b => b.Definition.VisionRange,
                    _ => 0,
                };

                if (range <= 0) continue;

                IsoCoord center;
                if (e is Building bld)
                    center = new IsoCoord(
                        bld.PlacementOrigin.X + bld.SizeX / 2,
                        bld.PlacementOrigin.Y + bld.SizeY / 2);
                else
                    center = CoordUtil.WorldToIso(e.WorldPosition);
                RevealDiamond(center, range);
            }
        }

        /// <summary>
        /// Mark all cells within Manhattan distance ≤ range of centre as Visible.
        /// This creates a diamond-shaped vision area matching the isometric grid.
        /// </summary>
        private void RevealDiamond(IsoCoord center, int range)
        {
            for (int dx = -range; dx <= range; dx++)
            {
                int maxDy = range - Math.Abs(dx);
                for (int dy = -maxDy; dy <= maxDy; dy++)
                {
                    int x = center.X + dx;
                    int y = center.Y + dy;
                    if (InBounds(x, y))
                        _cells[x, y] = FogCell.Visible;
                }
            }
        }

        private bool InBounds(int x, int y)
            => (uint)x < (uint)_width && (uint)y < (uint)_height;
    }
}
