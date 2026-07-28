namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// A* search node stored in the priority queue and visited dictionary.
    /// Internal — callers interact only with Pathfinder.FindPath().
    /// </summary>
    internal struct PathNode
    {
        public IsoCoord Coord;
        public int GCost;          // cumulative cost from start
        public int HCost;          // heuristic estimate to end
        public readonly int FCost => GCost + HCost;
        public IsoCoord Parent;    // for path reconstruction
    }
}
