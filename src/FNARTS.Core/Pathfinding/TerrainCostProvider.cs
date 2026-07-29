using System;

namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// 提供地形移动代价和可通过性判定。
    /// Core 层通过委托注入，不持有 TileMap 或 EntityManager 引用。
    /// </summary>
    public class TerrainCostProvider
    {
        /// <summary>
        /// 获取从 from 进入目标格子 to 的移动代价。
        /// 返回 PathCost.UnreachableCell 表示不可进入。
        /// 实现需包含对角线切角规则（对角线移动要求两个正交邻格可通过）。
        /// </summary>
        public Func<IsoCoord, IsoCoord, short> MovementCost { get; set; }

        /// <summary>
        /// 地形级别的移动代价（不含动态实体障碍），含对角线切角规则。
        /// 用于分层寻路的抽象图构建（抽象图只感知地形）。
        /// </summary>
        public Func<IsoCoord, IsoCoord, short> TerrainMovementCost { get; set; }

        /// <summary>
        /// 快速判定格子是否完全不可通行（地形级别）。
        /// 不考虑动态障碍物（单位/建筑），仅检查地形类型。
        /// </summary>
        public Func<IsoCoord, bool> IsTerrainPassable { get; set; }

        /// <summary>
        /// 判定格子是否可进入（地形 + 动态实体障碍）。
        /// 用于反向搜索的源点校验：反向搜索的源点实际是路径终点，
        /// 终点必须真正可进入。
        /// </summary>
        public Func<IsoCoord, bool> IsPassable { get; set; }

        /// <summary>地图宽度。</summary>
        public int MapWidth { get; set; }

        /// <summary>地图高度。</summary>
        public int MapHeight { get; set; }

        /// <summary>坐标是否在地图范围内。</summary>
        public bool InBounds(IsoCoord c)
            => (uint)c.X < (uint)MapWidth && (uint)c.Y < (uint)MapHeight;

        /// <summary>
        /// 创建默认的地形代价提供者。
        /// 默认地形代价表：Grass=10，Water/Cliff/Impassable 不可通过。
        /// 对角线移动要求两个正交邻格地形可通过（防止切角穿墙）。
        /// </summary>
        /// <param name="mapWidth">地图宽度。</param>
        /// <param name="mapHeight">地图高度。</param>
        /// <param name="getTileType">获取格子地形类型。</param>
        /// <param name="isBlockedByEntity">格子是否被动态实体（单位/建筑）阻挡。</param>
        public static TerrainCostProvider CreateDefault(int mapWidth, int mapHeight,
            Func<IsoCoord, TileType> getTileType,
            Func<IsoCoord, bool> isBlockedByEntity)
        {
            var provider = new TerrainCostProvider
            {
                MapWidth = mapWidth,
                MapHeight = mapHeight,
            };

            bool TerrainPassable(IsoCoord c)
            {
                if (!provider.InBounds(c))
                    return false;
                return getTileType(c) switch
                {
                    TileType.Grass => true,
                    _ => false,
                };
            }

            provider.IsTerrainPassable = TerrainPassable;
            provider.IsPassable = c => TerrainPassable(c) && !isBlockedByEntity(c);

            short TerrainOnlyCost(IsoCoord from, IsoCoord to)
            {
                if (!provider.InBounds(to))
                    return PathCost.UnreachableCell;

                var terrainCost = getTileType(to) switch
                {
                    TileType.Grass => (short)10,
                    _ => PathCost.UnreachableCell,
                };
                if (terrainCost == PathCost.UnreachableCell)
                    return PathCost.UnreachableCell;

                // 对角线切角检测：两个正交邻格必须地形可通过
                int dx = to.X - from.X, dy = to.Y - from.Y;
                if (dx != 0 && dy != 0)
                {
                    if (!TerrainPassable(new IsoCoord(to.X, from.Y)) ||
                        !TerrainPassable(new IsoCoord(from.X, to.Y)))
                        return PathCost.UnreachableCell;
                }

                return terrainCost;
            }

            provider.TerrainMovementCost = TerrainOnlyCost;

            provider.MovementCost = (from, to) =>
            {
                var terrainCost = TerrainOnlyCost(from, to);
                if (terrainCost == PathCost.UnreachableCell)
                    return PathCost.UnreachableCell;

                // 动态障碍物检查（建筑、单位）
                if (isBlockedByEntity(to))
                    return PathCost.UnreachableCell;

                return terrainCost;
            };

            return provider;
        }
    }
}
