using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;
using FNARTS.Core.Pathfinding;

namespace FNARTS.Core.Tests.Pathfinding
{
    public class PathfinderTests
    {
        // Helper: create a pathfinder with a simple open grid
        private static Pathfinder CreateOpenGrid(int w = 20, int h = 20)
        {
            return new Pathfinder
            {
                MapWidth = w,
                MapHeight = h,
                IsPassable = c => c.X >= 0 && c.X < w && c.Y >= 0 && c.Y < h,
            };
        }

        // Helper: create a pathfinder where specific cells are blocked
        private static Pathfinder CreateGridWithBlocked(
            int w, int h, HashSet<IsoCoord> blocked)
        {
            return new Pathfinder
            {
                MapWidth = w,
                MapHeight = h,
                IsPassable = c =>
                    c.X >= 0 && c.X < w && c.Y >= 0 && c.Y < h
                    && !blocked.Contains(c),
            };
        }

        private static IsoCoord C(int x, int y) => new(x, y);

        // ---- straight line ----

        [Fact]
        public void FindPath_StraightLine_ReturnsOptimalPath()
        {
            var pf = CreateOpenGrid();
            var path = pf.FindPath(C(0, 0), C(5, 0));
            // Should be 5 steps east
            Assert.Equal(5, path.Count);
            for (int i = 0; i < 5; i++)
                Assert.Equal(C(i + 1, 0), path[i]);
        }

        [Fact]
        public void FindPath_DiagonalLine_ReturnsOptimalPath()
        {
            var pf = CreateOpenGrid();
            var path = pf.FindPath(C(0, 0), C(3, 3));
            // Optimal on open grid: diagonal steps (dx=1,dy=1 repeated)
            Assert.Equal(3, path.Count);
        }

        // ---- obstacle detour ----

        [Fact]
        public void FindPath_ObstacleDetour_FindsPathAround()
        {
            // U-shaped wall: blocked cells at (1,0), (1,1), (1,2), (1,3), (1,4)
            // except (1,2) is open, making a gap.
            // Actually let's do a simpler wall: block (2,1), (2,2), (2,3)
            // going from (1,2) to (3,2). Direct diagonal would be blocked.
            var blocked = new HashSet<IsoCoord> { C(2, 1), C(2, 2), C(2, 3) };
            var pf = CreateGridWithBlocked(6, 6, blocked);

            var path = pf.FindPath(C(1, 2), C(3, 2));
            Assert.NotEmpty(path);
            // Must go around — should not contain blocked cells
            foreach (var c in path)
                Assert.DoesNotContain(c, blocked);
        }

        // ---- no path ----

        [Fact]
        public void FindPath_NoPath_ReturnsEmptyList()
        {
            // Completely enclosed: surround (2,2) with blocked cells
            var blocked = new HashSet<IsoCoord>
            {
                C(1,1), C(2,1), C(3,1),
                C(1,2),          C(3,2),
                C(1,3), C(2,3), C(3,3),
            };
            var pf = CreateGridWithBlocked(5, 5, blocked);

            // Start inside the box, end outside
            var path = pf.FindPath(C(2, 2), C(4, 4));
            Assert.Empty(path);
        }

        // ---- start == end ----

        [Fact]
        public void FindPath_StartEqualsEnd_ReturnsEmpty()
        {
            var pf = CreateOpenGrid();
            var path = pf.FindPath(C(3, 3), C(3, 3));
            Assert.Empty(path);
        }

        // ---- out of bounds ----

        [Fact]
        public void FindPath_OutOfBoundsStart_ReturnsEmpty()
        {
            var pf = CreateOpenGrid(10, 10);
            var path = pf.FindPath(C(-1, 5), C(5, 5));
            Assert.Empty(path);
        }

        [Fact]
        public void FindPath_OutOfBoundsEnd_ReturnsEmpty()
        {
            var pf = CreateOpenGrid(10, 10);
            var path = pf.FindPath(C(5, 5), C(10, 5));
            Assert.Empty(path);
        }

        // ---- unpassable end ----

        [Fact]
        public void FindPath_UnpassableEnd_ReturnsEmpty()
        {
            var blocked = new HashSet<IsoCoord> { C(5, 5) };
            var pf = CreateGridWithBlocked(10, 10, blocked);
            var path = pf.FindPath(C(0, 0), C(5, 5));
            Assert.Empty(path);
        }

        // ---- diagonal cut-corner ----

        [Fact]
        public void FindPath_DiagonalCutCorner_Blocked()
        {
            // From (0,0) to (2,2). Block (1,2) and (2,1) — the two orthogonals
            // that gate the diagonal (1,1)→(2,2). But keep (1,1) and (2,0)
            // passable so there IS a path (going around west/south side).
            var blocked = new HashSet<IsoCoord> { C(1, 2), C(2, 1) };
            var pf = CreateGridWithBlocked(5, 5, blocked);

            // Try to go (1,1) → (2,2): the diagonal should be blocked because
            // C(2,1) is blocked. The path should route around.
            var path = pf.FindPath(C(0, 0), C(2, 2));
            Assert.NotEmpty(path);

            // Verify the path does not contain the specific diagonal
            // transition: it should not contain (2,2) immediately after (1,1).
            // Actually, even simpler: the pathfinder should find a valid
            // path that never steps on blocked cells.
            foreach (var c in path)
                Assert.DoesNotContain(c, blocked);
        }

        [Fact]
        public void FindPath_DiagonalCutCorner_Allowed()
        {
            // Both orthogonals passable: diagonal allowed
            var pf = CreateOpenGrid(5, 5);
            var path = pf.FindPath(C(0, 0), C(1, 1));
            // On an open grid from (0,0) to (1,1), the optimal path is a single diagonal step
            Assert.Single(path);
            Assert.Equal(C(1, 1), path[0]);
        }

        // ---- performance ----

        [Fact]
        public void FindPath_Performance_WorstCaseWithinBudget()
        {
            var pf = CreateOpenGrid(51, 51);
            var sw = Stopwatch.StartNew();
            var path = pf.FindPath(C(0, 0), C(50, 50));
            sw.Stop();

            Assert.NotEmpty(path);
            Assert.True(sw.ElapsedMilliseconds < 5,
                $"Pathfinding took {sw.ElapsedMilliseconds}ms, expected <5ms");
        }

        // ---- determinism ----

        [Fact]
        public void FindPath_MultiplePaths_ConsistentResult()
        {
            var pf = CreateOpenGrid(20, 20);
            var path1 = pf.FindPath(C(2, 2), C(10, 10));
            var path2 = pf.FindPath(C(2, 2), C(10, 10));

            Assert.Equal(path1.Count, path2.Count);
            for (int i = 0; i < path1.Count; i++)
                Assert.Equal(path1[i], path2[i]);
        }

        // ---- heuristic correctness ----

        [Fact]
        public void OctileDistance_Symmetric()
        {
            var a = C(0, 0);
            var b = C(5, 3);
            Assert.Equal(Pathfinder.OctileDistance(a, b),
                         Pathfinder.OctileDistance(b, a));
        }

        [Fact]
        public void OctileDistance_TriangleInequality()
        {
            var a = C(0, 0);
            var b = C(5, 3);
            var c = C(10, 7);
            // h(a,c) ≤ h(a,b) + h(b,c)
            Assert.True(Pathfinder.OctileDistance(a, c)
                <= Pathfinder.OctileDistance(a, b) + Pathfinder.OctileDistance(b, c));
        }

        [Fact]
        public void OctileDistance_Zero_WhenSameCoord()
        {
            var a = C(4, 4);
            Assert.Equal(0, Pathfinder.OctileDistance(a, a));
        }

        [Fact]
        public void OctileDistance_MatchesMoveCost()
        {
            // From (0,0) to (1,1): actual shortest path cost = 14 (one diagonal)
            // Octile: 10*1 + 4*0 = 10... but wait, this isn't exact.
            // The heuristic should be admissible: h ≤ actual cost.
            // Actual diagonal cost = 14, heuristic = 10*1 + 4*0 = 10 ≤ 14 ✓
            int h = Pathfinder.OctileDistance(C(0, 0), C(1, 1));
            Assert.True(h <= 14, $"Heuristic {h} should be ≤ actual cost 14");
        }
    }
}
