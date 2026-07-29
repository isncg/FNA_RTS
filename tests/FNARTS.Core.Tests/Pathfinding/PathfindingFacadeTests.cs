using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;
using FNARTS.Core.Pathfinding;

namespace FNARTS.Core.Tests.Pathfinding
{
    /// <summary>PathfindingFacade 接口回归测试（对照旧 Pathfinder 的行为约定）。</summary>
    public class PathfindingFacadeTests
    {
        private static PathfindingFacade MakeFacade(TileMap map,
            HashSet<IsoCoord> blocked = null)
            => new(PathTestUtil.TerrainFor(map, blocked));

        // ── 基本行为回归 ──

        [Fact]
        public void FindPath_StraightLine_Optimal()
        {
            var facade = MakeFacade(PathTestUtil.OpenMap(30, 30));

            var path = facade.FindPath(new IsoCoord(0, 0), new IsoCoord(10, 0));

            Assert.Equal(10, path.Count);
            Assert.DoesNotContain(new IsoCoord(0, 0), path);  // 不含起点
            Assert.Equal(new IsoCoord(10, 0), path.Last());   // 含终点
            Assert.True(PathTestUtil.PathIsValid(path, null));
        }

        [Fact]
        public void FindPath_ObstacleDetour()
        {
            // 竖直墙 x=10, y∈[0,15]：必须绕行
            var wall = new HashSet<IsoCoord>();
            for (int y = 0; y <= 15; y++)
                wall.Add(new IsoCoord(10, y));
            var facade = MakeFacade(PathTestUtil.OpenMap(30, 30), wall);

            var path = facade.FindPath(new IsoCoord(5, 5), new IsoCoord(15, 5));

            Assert.NotEmpty(path);
            Assert.True(PathTestUtil.PathIsValid(path, wall));
            Assert.Equal(new IsoCoord(15, 5), path.Last());
            // 第一步必须与起点相邻
            Assert.True(System.Math.Abs(path[0].X - 5) <= 1 &&
                System.Math.Abs(path[0].Y - 5) <= 1);
            // 绕行必然比直线远
            Assert.True(PathTestUtil.CostOf(path) > 10 * PathTestUtil.StraightCost);
        }

        [Fact]
        public void FindPath_NoPath_EmptyList()
        {
            // 实体墙完全封闭目标
            var wall = new HashSet<IsoCoord>();
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                wall.Add(new IsoCoord(20 + dx, 20 + dy));
            }
            var facade = MakeFacade(PathTestUtil.OpenMap(30, 30), wall);

            Assert.Empty(facade.FindPath(new IsoCoord(2, 2), new IsoCoord(20, 20)));
        }

        [Fact]
        public void FindPath_StartEqualsEnd()
        {
            var facade = MakeFacade(PathTestUtil.OpenMap(20, 20));
            Assert.Empty(facade.FindPath(new IsoCoord(5, 5), new IsoCoord(5, 5)));
        }

        [Fact]
        public void FindPath_OutOfBounds()
        {
            var facade = MakeFacade(PathTestUtil.OpenMap(20, 20));
            Assert.Empty(facade.FindPath(new IsoCoord(-1, 0), new IsoCoord(10, 10)));
            Assert.Empty(facade.FindPath(new IsoCoord(0, 0), new IsoCoord(25, 10)));
        }

        [Fact]
        public void FindPath_UnpassableEnd()
        {
            var blocked = new HashSet<IsoCoord> { new(8, 6) };
            var facade = MakeFacade(PathTestUtil.OpenMap(20, 20), blocked);

            Assert.Empty(facade.FindPath(new IsoCoord(5, 5), new IsoCoord(8, 6)));
        }

        [Fact]
        public void FindPath_DiagonalCutCorner()
        {
            // (1,0) 和 (0,1) 是水域 → (0,0) 无法对角切角到 (1,1)
            var map = PathTestUtil.OpenMap(5, 5);
            map.SetTile(1, 0, new Tile(TileType.Water));
            map.SetTile(0, 1, new Tile(TileType.Water));
            var facade = MakeFacade(map);

            Assert.Empty(facade.FindPath(new IsoCoord(0, 0), new IsoCoord(1, 1)));
        }

        // ── 多起点 ──

        [Fact]
        public void FindPath_MultipleSources_PicksNearest()
        {
            var facade = MakeFacade(PathTestUtil.OpenMap(30, 30));
            var target = new IsoCoord(20, 20);
            var sources = new[]
            {
                new IsoCoord(2, 2),    // 远
                new IsoCoord(18, 19),  // 近
            };

            var path = facade.FindPath(sources, target);

            Assert.NotEmpty(path);
            Assert.Equal(target, path.Last());
            // 应选最近起点：2 步（1 对角 + 1 直线）
            Assert.Equal(2, path.Count);
        }

        // ── 确定性 ──

        [Fact]
        public void FindPath_Deterministic()
        {
            var wall = new HashSet<IsoCoord>();
            for (int y = 3; y <= 26; y++)
                wall.Add(new IsoCoord(15, y));
            var facade = MakeFacade(PathTestUtil.OpenMap(30, 30), wall);

            var first = facade.FindPath(new IsoCoord(5, 15), new IsoCoord(25, 15));
            for (int i = 0; i < 5; i++)
            {
                var again = facade.FindPath(new IsoCoord(5, 15), new IsoCoord(25, 15));
                Assert.True(first.SequenceEqual(again));
            }
        }

        // ── 性能 ──

        [Fact]
        public void FindPath_Performance_51x51()
        {
            // 最坏情况：目标被实体墙包围（域仍连通）→ 精细搜索全图无果
            var wall = new HashSet<IsoCoord>();
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                wall.Add(new IsoCoord(40 + dx, 40 + dy));
            }
            var facade = MakeFacade(PathTestUtil.OpenMap(51, 51), wall);

            // 预热后取平均
            facade.FindPath(new IsoCoord(0, 0), new IsoCoord(40, 40));
            var sw = Stopwatch.StartNew();
            const int runs = 20;
            for (int i = 0; i < runs; i++)
                facade.FindPath(new IsoCoord(0, 0), new IsoCoord(40, 40));
            sw.Stop();

            double avgMs = sw.Elapsed.TotalMilliseconds / runs;
            Assert.True(avgMs < 5, $"51×51 最坏情况平均 {avgMs:F3}ms，超过 5ms");
        }

        [Fact]
        public void FindPath_Performance_HPF_BetterThanSimple()
        {
            // 大地图 + 长地形墙绕行：分层搜索应快于全图简单 A*。
            // 注意：抽象图只感知地形，障碍必须用地形（水域）表达。
            var map = PathTestUtil.OpenMap(128, 128);
            for (int y = 0; y <= 120; y++)
                map.SetTile(64, y, new Tile(TileType.Water));
            var terrain = PathTestUtil.TerrainFor(map);
            var facade = new PathfindingFacade(terrain);

            var source = new IsoCoord(10, 64);
            var target = new IsoCoord(118, 64);

            // 预热
            facade.FindPath(source, target);
            using (var warmup = PathSearch.ToTargetCell(
                new CellInfoLayerPool(128, 128), terrain,
                new[] { source }, target, 100))
                warmup.FindPath();

            const int runs = 10;
            var hpfSw = Stopwatch.StartNew();
            for (int i = 0; i < runs; i++)
                facade.FindPath(source, target);
            hpfSw.Stop();

            var simpleSw = Stopwatch.StartNew();
            for (int i = 0; i < runs; i++)
            {
                using var search = PathSearch.ToTargetCell(
                    new CellInfoLayerPool(128, 128), terrain,
                    new[] { source }, target, 100);
                search.FindPath();
            }
            simpleSw.Stop();

            Assert.True(hpfSw.Elapsed < simpleSw.Elapsed,
                $"HPF {hpfSw.ElapsedMilliseconds}ms 未快于简单 A* {simpleSw.ElapsedMilliseconds}ms");
        }
    }
}
