using System;
using System.Collections.Generic;

namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// 寻路系统对外统一接口，替代旧的 Pathfinder 类。
    /// 内部根据距离自动选择局部搜索或分层搜索。
    ///
    /// 路径约定（与旧系统一致）：返回的列表不含起点、含终点；
    /// 无可达路径时返回空列表。
    /// </summary>
    public class PathfindingFacade
    {
        private readonly HierarchicalPathFinder _hpf;
        private readonly TerrainCostProvider _terrain;

        public PathfindingFacade(TerrainCostProvider terrain,
            Func<IsoCoord, int> customCost = null)
        {
            _terrain = terrain;
            _hpf = new HierarchicalPathFinder(terrain, customCost);
        }

        /// <summary>
        /// 寻找从 start 到 end 的最短路径。
        /// 返回网格坐标列表（不含起点，含终点）。
        /// 无可达路径时返回空列表。
        /// </summary>
        public List<IsoCoord> FindPath(IsoCoord start, IsoCoord end)
        {
            if (!_terrain.InBounds(start) || !_terrain.InBounds(end))
                return new List<IsoCoord>();
            if (start == end)
                return new List<IsoCoord>();

            var reversed = _hpf.FindPath(start, end);
            return ToForwardPath(reversed);
        }

        /// <summary>
        /// 从多个候选起点寻找到 target 的路径，返回最短的一条。
        /// </summary>
        public List<IsoCoord> FindPath(IReadOnlyCollection<IsoCoord> sources,
            IsoCoord target)
        {
            if (!_terrain.InBounds(target) || sources.Count == 0)
                return new List<IsoCoord>();

            var reversed = _hpf.FindPath(sources, target);
            return ToForwardPath(reversed);
        }

        /// <summary>
        /// 快速判定两个位置之间是否存在路径（只考虑地形）。
        /// </summary>
        public bool PathExists(IsoCoord source, IsoCoord target)
            => _hpf.PathExists(source, target);

        /// <summary>
        /// 通知地形变化（建筑建造/摧毁等）。
        /// </summary>
        public void NotifyTerrainChanged(IsoCoord cell)
            => _hpf.NotifyTerrainChanged(cell);

        // 内部返回的路径是反向的（目标 → 起点，含两端）：
        // 反转后去掉起点，得到旧系统的约定格式。
        private static List<IsoCoord> ToForwardPath(List<IsoCoord> reversed)
        {
            if (reversed.Count == 0)
                return new List<IsoCoord>();

            reversed.Reverse();
            reversed.RemoveAt(0);  // 去掉起点
            return reversed;
        }

        // ── 向后兼容旧 API ──

        /// <summary>地图宽度。</summary>
        public int MapWidth => _terrain.MapWidth;

        /// <summary>地图高度。</summary>
        public int MapHeight => _terrain.MapHeight;
    }
}
