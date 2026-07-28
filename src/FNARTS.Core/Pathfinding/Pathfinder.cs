using System;
using System.Collections.Generic;

namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// A* grid pathfinder with 8-direction movement and diagonal cut-corner detection.
    /// Pure C# — no FNA or GPU dependency. Passability is injected via delegate so
    /// the Core layer never references TileMap or EntityManager directly.
    /// </summary>
    public class Pathfinder
    {
        // ---- move costs ----
        private const int STRAIGHT_COST = 10;
        private const int DIAGONAL_COST = 14;   // ≈ 10 * sqrt(2)

        // ---- 8 compass directions: dx, dy, cost ----
        private static readonly (int dx, int dy, int cost)[] _directions =
        {
            ( 1,  0, STRAIGHT_COST),  // E
            (-1,  0, STRAIGHT_COST),  // W
            ( 0,  1, STRAIGHT_COST),  // S
            ( 0, -1, STRAIGHT_COST),  // N
            ( 1,  1, DIAGONAL_COST),  // SE
            (-1,  1, DIAGONAL_COST),  // SW
            ( 1, -1, DIAGONAL_COST),  // NE
            (-1, -1, DIAGONAL_COST),  // NW
        };

        /// <summary>
        /// Delegate that returns true if a grid cell is traversable.
        /// Set by the Game layer before use.
        /// </summary>
        public Func<IsoCoord, bool> IsPassable { get; set; } = _ => true;

        /// <summary>Map dimensions for bounds checking.</summary>
        public int MapWidth { get; set; }
        public int MapHeight { get; set; }

        /// <summary>
        /// Safety cap on search iterations (default 2500 — enough for 51×51 worst case).
        /// </summary>
        public int MaxIterations { get; set; } = 2500;

        /// <summary>
        /// Find the shortest path from start to end.
        /// Returns a list of grid coordinates (excludes start, includes end).
        /// Returns an empty list when no path exists.
        /// </summary>
        public List<IsoCoord> FindPath(IsoCoord start, IsoCoord end)
        {
            // ---- early rejection ----
            if (!InBounds(start) || !InBounds(end))
                return new List<IsoCoord>();

            if (!IsPassable(end))
                return new List<IsoCoord>();

            if (start == end)
                return new List<IsoCoord>();

            // ---- A* search ----
            var openSet = new PriorityQueue<IsoCoord, int>();
            var nodeMap = new Dictionary<IsoCoord, PathNode>();
            var closedSet = new HashSet<IsoCoord>();

            var startNode = new PathNode
            {
                Coord = start,
                GCost = 0,
                HCost = OctileDistance(start, end),
                Parent = start,
            };
            nodeMap[start] = startNode;
            openSet.Enqueue(start, startNode.FCost);

            int iterations = 0;

            while (openSet.Count > 0 && iterations < MaxIterations)
            {
                iterations++;
                var current = openSet.Dequeue();

                if (current == end)
                {
                    // Path found — reconstruct
                    return ReconstructPath(nodeMap, start, end);
                }

                if (closedSet.Contains(current))
                    continue;
                closedSet.Add(current);

                var curNode = nodeMap[current];

                foreach (var (dx, dy, cost) in _directions)
                {
                    var neighbor = new IsoCoord(current.X + dx, current.Y + dy);

                    if (closedSet.Contains(neighbor))
                        continue;

                    if (!InBounds(neighbor))
                        continue;

                    if (!IsPassable(neighbor))
                        continue;

                    // Diagonal cut-corner detection:
                    // moving (dx, dy) with |dx|=|dy|=1 requires both adjacent
                    // orthogonal cells to be passable.
                    if (dx != 0 && dy != 0)
                    {
                        if (!IsPassable(new IsoCoord(current.X + dx, current.Y)) ||
                            !IsPassable(new IsoCoord(current.X, current.Y + dy)))
                            continue;
                    }

                    int tentativeG = curNode.GCost + cost;

                    if (nodeMap.TryGetValue(neighbor, out var existing))
                    {
                        if (tentativeG >= existing.GCost)
                            continue;
                    }

                    var neighborNode = new PathNode
                    {
                        Coord = neighbor,
                        GCost = tentativeG,
                        HCost = OctileDistance(neighbor, end),
                        Parent = current,
                    };
                    nodeMap[neighbor] = neighborNode;
                    openSet.Enqueue(neighbor, neighborNode.FCost);
                }
            }

            // No path found (exhausted open set or hit iteration cap)
            return new List<IsoCoord>();
        }

        /// <summary>
        /// Octile distance heuristic.
        /// Admissible and consistent for 8-direction grids with cost 10/14.
        ///   h = 10 * max(|dx|,|dy|) + 4 * min(|dx|,|dy|)
        /// where 4 = 14 − 10 (the diagonal surcharge).
        /// </summary>
        public static int OctileDistance(IsoCoord a, IsoCoord b)
        {
            int dx = Math.Abs(a.X - b.X);
            int dy = Math.Abs(a.Y - b.Y);
            if (dx < dy)
                (dx, dy) = (dy, dx);  // ensure dx >= dy
            return STRAIGHT_COST * dx + (DIAGONAL_COST - STRAIGHT_COST) * dy;
        }

        private bool InBounds(IsoCoord c)
            => (uint)c.X < (uint)MapWidth && (uint)c.Y < (uint)MapHeight;

        /// <summary>
        /// Walk the Parent chain from end back to start and reverse.
        /// Returns the list excluding the start coordinate.
        /// </summary>
        private static List<IsoCoord> ReconstructPath(
            Dictionary<IsoCoord, PathNode> nodeMap,
            IsoCoord start, IsoCoord end)
        {
            var path = new List<IsoCoord>();
            var current = end;

            while (current != start)
            {
                path.Add(current);
                if (!nodeMap.TryGetValue(current, out var node))
                {
                    // Should never happen if search completed correctly
                    path.Reverse();
                    return path;
                }
                current = node.Parent;
            }

            path.Reverse();
            return path;
        }
    }
}
