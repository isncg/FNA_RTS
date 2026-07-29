using System.Collections.Generic;
using System.Linq;
using Xunit;
using FNARTS.Core.Pathfinding;

namespace FNARTS.Core.Tests.Pathfinding
{
    /// <summary>HierarchicalPathFinder 分层寻路测试。</summary>
    public class HierarchicalPathFinderTests
    {
        private static HierarchicalPathFinder MakeHpf(TileMap map,
            HashSet<IsoCoord> blocked = null)
            => new(PathTestUtil.TerrainFor(map, blocked));

        /// <summary>简单 A*（全图搜索）的最优代价，作为基准。</summary>
        private static int SimpleCost(TerrainCostProvider terrain,
            IsoCoord source, IsoCoord target)
        {
            using var search = PathSearch.ToTargetCell(
                new CellInfoLayerPool(terrain.MapWidth, terrain.MapHeight),
                terrain, new[] { source }, target, 100, laneBias: false);
            return PathTestUtil.CostOf(search.FindPath());
        }

        // ── FindPath ──

        [Fact]
        public void FindPath_LongDistance_MatchesSimple()
        {
            var map = PathTestUtil.OpenMap(50, 50);
            var hpf = MakeHpf(map);
            var source = new IsoCoord(2, 2);
            var target = new IsoCoord(45, 40);  // 距离 > 20 → 走分层搜索

            var path = hpf.FindPath(source, target, 100, laneBias: false);

            Assert.NotEmpty(path);
            // 内部约定：反向路径（目标 → 起点，含两端）
            Assert.Equal(target, path.First());
            Assert.Equal(source, path.Last());
            Assert.True(PathTestUtil.PathIsValid(path, null));
            Assert.Equal(
                SimpleCost(PathTestUtil.TerrainFor(map), source, target),
                PathTestUtil.CostOf(path));
        }

        [Fact]
        public void FindPath_AroundLake_MatchesSimpleCost()
        {
            // 竖直"湖"（实体阻挡区）横亘中央，必须从上下绕行
            var lake = new HashSet<IsoCoord>();
            for (int x = 22; x <= 27; x++)
            for (int y = 12; y <= 38; y++)
                lake.Add(new IsoCoord(x, y));
            var map = PathTestUtil.OpenMap(50, 50);
            var terrain = PathTestUtil.TerrainFor(map, lake);
            var hpf = new HierarchicalPathFinder(terrain);

            var source = new IsoCoord(5, 25);
            var target = new IsoCoord(45, 25);

            var path = hpf.FindPath(source, target, 100, laneBias: false);

            Assert.NotEmpty(path);
            Assert.True(PathTestUtil.PathIsValid(path, lake));
            // 双向交汇可能带来轻微次优，代价不应优于最优解
            int simpleCost = SimpleCost(terrain, source, target);
            Assert.InRange(PathTestUtil.CostOf(path), simpleCost, simpleCost + 30);
        }

        [Fact]
        public void FindPath_CloseDistance_UsesLocalSearch()
        {
            var map = PathTestUtil.OpenMap(40, 40);
            var hpf = MakeHpf(map);
            var source = new IsoCoord(10, 10);
            var target = new IsoCoord(15, 13);  // 距离² = 34 < 400 → 局部搜索

            var path = hpf.FindPath(source, target, 100, laneBias: false);

            Assert.NotEmpty(path);
            Assert.Equal(target, path.First());
            Assert.Equal(source, path.Last());
            // 最优：3 对角 + 2 直线 = 3×14 + 2×10
            Assert.Equal(3 * PathTestUtil.DiagonalCost +
                2 * PathTestUtil.StraightCost, PathTestUtil.CostOf(path));
        }

        // ── PathExists ──

        [Fact]
        public void PathExists_SameDomain_True()
        {
            var hpf = MakeHpf(PathTestUtil.OpenMap(40, 40));
            Assert.True(hpf.PathExists(new IsoCoord(5, 5), new IsoCoord(30, 30)));
        }

        [Fact]
        public void PathExists_DifferentDomain_False()
        {
            // 水域墙横贯地图，将地图分为左右两个域
            var map = PathTestUtil.OpenMap(40, 40);
            for (int y = 0; y < 40; y++)
                map.SetTile(20, y, new Tile(TileType.Water));
            var hpf = MakeHpf(map);

            Assert.False(hpf.PathExists(new IsoCoord(5, 5), new IsoCoord(35, 5)));
            // 同侧仍然可达
            Assert.True(hpf.PathExists(new IsoCoord(5, 5), new IsoCoord(15, 30)));
        }

        [Fact]
        public void PathExists_AfterTerrainChange_Updates()
        {
            var map = PathTestUtil.OpenMap(40, 40);
            var hpf = MakeHpf(map);
            Assert.True(hpf.PathExists(new IsoCoord(5, 5), new IsoCoord(35, 5)));

            // 建造水域墙并通知地形变化
            for (int y = 0; y < 40; y++)
            {
                map.SetTile(20, y, new Tile(TileType.Water));
                hpf.NotifyTerrainChanged(new IsoCoord(20, y));
            }

            Assert.False(hpf.PathExists(new IsoCoord(5, 5), new IsoCoord(35, 5)));
            Assert.True(hpf.PathExists(new IsoCoord(5, 5), new IsoCoord(15, 30)));
        }

        [Fact]
        public void NotifyTerrainChanged_RebuildsGrid()
        {
            var map = PathTestUtil.OpenMap(40, 40);
            var hpf = MakeHpf(map);
            var source = new IsoCoord(5, 20);
            var target = new IsoCoord(35, 20);
            Assert.NotEmpty(hpf.FindPath(source, target, 100, laneBias: false));

            // 水域墙封死通道，重建后应无路径
            for (int y = 0; y < 40; y++)
            {
                map.SetTile(20, y, new Tile(TileType.Water));
                hpf.NotifyTerrainChanged(new IsoCoord(20, y));
            }

            Assert.Empty(hpf.FindPath(source, target, 100, laneBias: false));
        }
    }
}
