using System.Collections.Generic;
using System.Linq;
using Xunit;
using FNARTS.Core.Pathfinding;

namespace FNARTS.Core.Tests.Pathfinding
{
    /// <summary>PathSearch A* 搜索引擎测试。</summary>
    public class PathSearchTests
    {
        private static PathSearch MakeSearch(TerrainCostProvider terrain,
            IsoCoord source, IsoCoord target,
            int weight = 100, Grid? grid = null, bool laneBias = false)
            => PathSearch.ToTargetCell(
                new CellInfoLayerPool(terrain.MapWidth, terrain.MapHeight),
                terrain, new[] { source }, target, weight,
                laneBias: laneBias, grid: grid);

        // ── FindPath ──

        [Fact]
        public void FindPath_StraightLine_Optimal()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            using var search = MakeSearch(terrain, new(0, 0), new(10, 0));

            var path = search.FindPath();  // 反向：target → source

            Assert.Equal(11, path.Count);
            Assert.Equal(new IsoCoord(10, 0), path.First());
            Assert.Equal(new IsoCoord(0, 0), path.Last());
            Assert.Equal(10 * PathTestUtil.StraightCost, PathTestUtil.CostOf(path));
        }

        [Fact]
        public void FindPath_Diagonal_Optimal()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            using var search = MakeSearch(terrain, new(0, 0), new(10, 10));

            var path = search.FindPath();

            Assert.Equal(11, path.Count);  // 10 步对角线
            Assert.Equal(10 * PathTestUtil.DiagonalCost, PathTestUtil.CostOf(path));
        }

        [Fact]
        public void FindPath_ObstacleDetour()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            // 竖直墙 x=10, y∈[0,15]：必须从上方绕行
            var wall = new HashSet<IsoCoord>();
            for (int y = 0; y <= 15; y++)
                wall.Add(new IsoCoord(10, y));
            terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20), wall);

            using var search = MakeSearch(terrain, new(5, 5), new(15, 5));
            var path = search.FindPath();

            Assert.NotEmpty(path);
            Assert.True(PathTestUtil.PathIsValid(path, wall));
            Assert.Equal(new IsoCoord(15, 5), path.First());
            Assert.Equal(new IsoCoord(5, 5), path.Last());
            // 绕行必然比直线远
            Assert.True(PathTestUtil.CostOf(path) > 10 * PathTestUtil.StraightCost);
        }

        [Fact]
        public void FindPath_NoPath_EmptyList()
        {
            var wall = new HashSet<IsoCoord>();
            // 完全封闭目标 (10,10)
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                wall.Add(new IsoCoord(10 + dx, 10 + dy));
            }
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20), wall);

            using var search = MakeSearch(terrain, new(0, 0), new(10, 10));
            Assert.Empty(search.FindPath());
        }

        [Fact]
        public void FindPath_StartEqualsEnd_SingleCell()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            using var search = MakeSearch(terrain, new(5, 5), new(5, 5));

            var path = search.FindPath();

            Assert.Single(path);
            Assert.Equal(new IsoCoord(5, 5), path[0]);
        }

        [Fact]
        public void FindPath_OutOfBounds_EmptyList()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            using var search = MakeSearch(terrain, new(-1, 0), new(10, 10));
            Assert.Empty(search.FindPath());
        }

        [Fact]
        public void FindPath_DiagonalCutCorner_NotAllowed()
        {
            // 水域墙角：(1,0) 和 (0,1) 不可通行，
            // 从 (0,0) 到 (1,1) 不允许对角线切角穿过。
            var map = PathTestUtil.OpenMap(5, 5);
            map.SetTile(1, 0, new Tile(TileType.Water));
            map.SetTile(0, 1, new Tile(TileType.Water));
            var terrain = PathTestUtil.TerrainFor(map);

            using var search = MakeSearch(terrain, new(0, 0), new(1, 1));
            Assert.Empty(search.FindPath());
        }

        // ── ExpandToTarget / ExpandAll ──

        [Fact]
        public void ExpandToTarget_ReturnsTrue_WhenPathExists()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            using var search = MakeSearch(terrain, new(0, 0), new(8, 3));
            Assert.True(search.ExpandToTarget());
        }

        [Fact]
        public void ExpandToTarget_ReturnsFalse_WhenNoPath()
        {
            var wall = new HashSet<IsoCoord>();
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                wall.Add(new IsoCoord(10 + dx, 10 + dy));
            }
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20), wall);

            using var search = MakeSearch(terrain, new(0, 0), new(10, 10));
            Assert.False(search.ExpandToTarget());
        }

        [Fact]
        public void ExpandAll_VisitsAllReachable()
        {
            // 20x20 地图，(10,10) 处有障碍：可达格 = 400 - 1
            var wall = new HashSet<IsoCoord> { new(10, 10) };
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20), wall);

            using var search = MakeSearch(terrain, new(0, 0), new(0, 0));
            var visited = search.ExpandAll();

            Assert.Equal(20 * 20 - 1, visited.Count);
            Assert.DoesNotContain(new IsoCoord(10, 10), visited);
        }

        // ── 双向搜索 ──

        [Fact]
        public void FindBidiPath_MatchesUnidirectional_Cost()
        {
            var wall = new HashSet<IsoCoord>();
            for (int y = 2; y <= 17; y++)
                wall.Add(new IsoCoord(10, y));
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20), wall);

            int uniCost;
            using (var search = MakeSearch(terrain, new(3, 10), new(17, 10)))
                uniCost = PathTestUtil.CostOf(search.FindPath());

            var pool = new CellInfoLayerPool(terrain.MapWidth, terrain.MapHeight);
            using var fwd = PathSearch.ToTargetCell(pool, terrain,
                new[] { new IsoCoord(3, 10) }, new(17, 10), 100);
            using var rev = PathSearch.ToTargetCell(pool, terrain,
                new[] { new IsoCoord(17, 10) }, new(3, 10), 100, inReverse: true);
            var bidiPath = PathSearch.FindBidiPath(fwd, rev);

            Assert.NotEmpty(bidiPath);
            Assert.True(PathTestUtil.PathIsValid(bidiPath, wall));
            // 双向路径不可能优于最优解；交汇策略可能带来轻微次优
            int bidiCost = PathTestUtil.CostOf(bidiPath);
            Assert.InRange(bidiCost, uniCost, uniCost + 30);
        }

        [Fact]
        public void FindBidiPath_NoPath_EmptyList()
        {
            var wall = new HashSet<IsoCoord>();
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                wall.Add(new IsoCoord(10 + dx, 10 + dy));
            }
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20), wall);

            var pool = new CellInfoLayerPool(terrain.MapWidth, terrain.MapHeight);
            using var fwd = PathSearch.ToTargetCell(pool, terrain,
                new[] { new IsoCoord(0, 0) }, new(10, 10), 100);
            using var rev = PathSearch.ToTargetCell(pool, terrain,
                new[] { new IsoCoord(10, 10) }, new(0, 0), 100, inReverse: true);

            Assert.Empty(PathSearch.FindBidiPath(fwd, rev));
        }

        // ── 启发式权重 / Grid 搜索 ──

        [Fact]
        public void HeuristicWeight_Over100_StillFindsValidPath()
        {
            var wall = new HashSet<IsoCoord>();
            for (int y = 2; y <= 17; y++)
                wall.Add(new IsoCoord(10, y));
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20), wall);

            using var search = MakeSearch(terrain, new(3, 10), new(17, 10), weight: 150);
            var path = search.FindPath();

            Assert.NotEmpty(path);
            Assert.True(PathTestUtil.PathIsValid(path, wall));
        }

        [Fact]
        public void GridSearch_StaysInsideGrid()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(30, 30));
            var grid = new Grid(new IsoCoord(0, 0), new IsoCoord(15, 15));

            using var search = MakeSearch(terrain, new(2, 2), new(12, 12), grid: grid);
            var path = search.FindPath();

            Assert.NotEmpty(path);
            Assert.All(path, c => Assert.True(grid.Contains(c)));
        }

        [Fact]
        public void GridSearch_TargetOutsideGrid_EmptyList()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(30, 30));
            var grid = new Grid(new IsoCoord(0, 0), new IsoCoord(10, 10));

            using var search = MakeSearch(terrain, new(2, 2), new(20, 20), grid: grid);
            Assert.Empty(search.FindPath());
        }

        // ── 启发式函数 ──

        [Fact]
        public void DefaultCostEstimator_OctileDistance()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            var estimator = PathSearch.DefaultCostEstimator(terrain);

            // 纯直线
            Assert.Equal(10 * PathTestUtil.StraightCost,
                estimator(new IsoCoord(0, 0), new IsoCoord(10, 0)));
            // 纯对角线
            Assert.Equal(5 * PathTestUtil.DiagonalCost,
                estimator(new IsoCoord(0, 0), new IsoCoord(5, 5)));
            // 混合：(3,1) = 2 直 + 1 对角 = 20 + 14
            Assert.Equal(2 * PathTestUtil.StraightCost + PathTestUtil.DiagonalCost,
                estimator(new IsoCoord(0, 0), new IsoCoord(3, 1)));
        }
    }
}
