using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FNARTS.Core.Pathfinding;

namespace FNARTS.Core.Tests.Pathfinding
{
    /// <summary>DensePathGraph / GridPathGraph 图结构测试。</summary>
    public class DensePathGraphTests
    {
        private static GridPathGraph MakeGraph(TerrainCostProvider terrain,
            Grid grid, bool laneBias = false, bool inReverse = false)
            => new(terrain, null, laneBias, inReverse, grid);

        private static readonly Grid FullGrid =
            new(new IsoCoord(0, 0), new IsoCoord(20, 20));

        // ── GetConnections ──

        [Fact]
        public void GetConnections_OpenGrid_8Directions()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            var graph = MakeGraph(terrain, FullGrid);

            var center = new IsoCoord(10, 10);
            // 起点无前驱：PreviousNode == 自身 → 检查全部 8 方向
            graph[center] = new CellInfo(CellStatus.Open, 0, 0, center);

            var connections = graph.GetConnections(center, _ => false);

            Assert.Equal(8, connections.Count);
            Assert.All(connections, c => Assert.True(
                c.Cost == PathTestUtil.StraightCost ||
                c.Cost == PathTestUtil.DiagonalCost));
        }

        [Fact]
        public void GetConnections_DirectedNeighbors_ReducesChecks()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            var graph = MakeGraph(terrain, FullGrid);

            var center = new IsoCoord(10, 10);
            // 前驱在左边 → 向右移动：只检查前方 3 格
            graph[center] = new CellInfo(CellStatus.Open, 0, 0, new IsoCoord(9, 10));

            var connections = graph.GetConnections(center, _ => false);

            Assert.Equal(3, connections.Count);
            var destinations = connections.Select(c => c.Destination).ToHashSet();
            Assert.Contains(new IsoCoord(11, 9), destinations);
            Assert.Contains(new IsoCoord(11, 10), destinations);
            Assert.Contains(new IsoCoord(11, 11), destinations);
        }

        [Fact]
        public void GetConnections_ExcludesClosedNeighbors()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            var graph = MakeGraph(terrain, FullGrid);

            var center = new IsoCoord(10, 10);
            graph[center] = new CellInfo(CellStatus.Open, 0, 0, center);
            graph[new IsoCoord(11, 10)] = new CellInfo(CellStatus.Closed, 0, 0, center);

            var connections = graph.GetConnections(center, _ => false);

            Assert.Equal(7, connections.Count);
            Assert.DoesNotContain(connections,
                c => c.Destination == new IsoCoord(11, 10));
        }

        [Fact]
        public void GetConnections_BlockedDiagonal_NoCutCorner()
        {
            // (11,10) 是水域 → 不允许 (10,10) → (11,11) 的对角切角
            var map = PathTestUtil.OpenMap(20, 20);
            map.SetTile(11, 10, new Tile(TileType.Water));
            var terrain = PathTestUtil.TerrainFor(map);
            var graph = MakeGraph(terrain, FullGrid);

            var center = new IsoCoord(10, 10);
            graph[center] = new CellInfo(CellStatus.Open, 0, 0, center);

            var connections = graph.GetConnections(center, _ => false);

            Assert.DoesNotContain(connections,
                c => c.Destination == new IsoCoord(11, 11));
            // (11,10) 本身也不可进入
            Assert.DoesNotContain(connections,
                c => c.Destination == new IsoCoord(11, 10));
        }

        // ── 移动代价 ──

        [Fact]
        public void CellPathCost_Straight_EqualsTerrainCost()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            var graph = MakeGraph(terrain, FullGrid);

            var center = new IsoCoord(10, 10);
            graph[center] = new CellInfo(CellStatus.Open, 0, 0, center);

            var connections = graph.GetConnections(center, _ => false);
            var straight = connections.First(c => c.Destination == new IsoCoord(11, 10));

            Assert.Equal(PathTestUtil.StraightCost, straight.Cost);
        }

        [Fact]
        public void CellPathCost_Diagonal_ApproxSqrtTwo()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            var graph = MakeGraph(terrain, FullGrid);

            var center = new IsoCoord(10, 10);
            graph[center] = new CellInfo(CellStatus.Open, 0, 0, center);

            var connections = graph.GetConnections(center, _ => false);
            var diagonal = connections.First(c => c.Destination == new IsoCoord(11, 11));

            // 10 × √2 ≈ 14（定点近似 10 × 1414 / 1000）
            Assert.Equal(PathTestUtil.DiagonalCost, diagonal.Cost);
        }

        [Fact]
        public void LaneBias_AddsPenalty_OnSomeDirections()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            var noBias = MakeGraph(terrain, FullGrid, laneBias: false);
            var withBias = MakeGraph(terrain, FullGrid, laneBias: true);

            var center = new IsoCoord(10, 10);
            var info = new CellInfo(CellStatus.Open, 0, 0, center);
            noBias[center] = info;
            withBias[center] = info;

            var plain = noBias.GetConnections(center, _ => false);
            var biased = withBias.GetConnections(center, _ => false);

            // 至少部分方向带 +1 车道偏移代价，且偏移最多为 2
            int differences = 0;
            foreach (var b in biased)
            {
                var p = plain.First(x => x.Destination == b.Destination);
                int delta = b.Cost - p.Cost;
                Assert.InRange(delta, 0, 2);
                if (delta > 0)
                    differences++;
            }
            Assert.True(differences > 0);
        }

        [Fact]
        public void CustomCost_IsApplied()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            var penaltyCell = new IsoCoord(11, 10);
            var graph = new GridPathGraph(terrain,
                c => c == penaltyCell ? 50 : 0, false, false, FullGrid);

            var center = new IsoCoord(10, 10);
            graph[center] = new CellInfo(CellStatus.Open, 0, 0, center);

            var connections = graph.GetConnections(center, _ => false);
            var penalized = connections.First(c => c.Destination == penaltyCell);

            Assert.Equal(PathTestUtil.StraightCost + 50, penalized.Cost);
        }

        [Fact]
        public void CustomCost_InvalidPath_ExcludesCell()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            var excluded = new IsoCoord(11, 10);
            var graph = new GridPathGraph(terrain,
                c => c == excluded ? PathCost.InvalidPath : 0, false, false, FullGrid);

            var center = new IsoCoord(10, 10);
            graph[center] = new CellInfo(CellStatus.Open, 0, 0, center);

            var connections = graph.GetConnections(center, _ => false);

            Assert.DoesNotContain(connections, c => c.Destination == excluded);
        }

        // ── GridPathGraph 区域限制 ──

        [Fact]
        public void GridPathGraph_LimitsNeighborsToGrid()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            var grid = new Grid(new IsoCoord(5, 5), new IsoCoord(10, 10));
            var graph = MakeGraph(terrain, grid);

            // 角落 (5,5)：8 方向中只有 3 个在 Grid 内
            var corner = new IsoCoord(5, 5);
            graph[corner] = new CellInfo(CellStatus.Open, 0, 0, corner);

            var connections = graph.GetConnections(corner, _ => false);

            Assert.Equal(3, connections.Count);
            Assert.All(connections, c => Assert.True(grid.Contains(c.Destination)));
        }

        [Fact]
        public void GridPathGraph_IndexerRoundTrip()
        {
            var terrain = PathTestUtil.TerrainFor(PathTestUtil.OpenMap(20, 20));
            var grid = new Grid(new IsoCoord(3, 4), new IsoCoord(12, 15));
            var graph = MakeGraph(terrain, grid);

            var cell = new IsoCoord(7, 9);
            var info = new CellInfo(CellStatus.Closed, 42, 100, new IsoCoord(6, 9));
            graph[cell] = info;

            var read = graph[cell];
            Assert.Equal(CellStatus.Closed, read.Status);
            Assert.Equal(42, read.CostSoFar);
            Assert.Equal(100, read.EstimatedTotalCost);
            Assert.Equal(new IsoCoord(6, 9), read.PreviousNode);

            // 未写入的格子为 Unvisited
            Assert.Equal(CellStatus.Unvisited, graph[new IsoCoord(8, 9)].Status);
        }
    }
}
