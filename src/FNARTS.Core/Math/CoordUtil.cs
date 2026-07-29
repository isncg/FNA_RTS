using System;
using System.Numerics;

namespace FNARTS.Core
{
    /// <summary>
    /// Isometric coordinate transformation utilities.
    /// 2:1 dimetric projection: tiles are 64x32 pixels in world space.
    ///
    /// C&amp;C2 direction convention:
    ///   gx+1 → upper-right (East),  gy+1 → upper-left (North)
    ///   Map origin (0,0) at bottom of screen; tiles cascade upward.
    /// </summary>
    public static class CoordUtil
    {
        public const int TILE_WIDTH = 64;
        public const int TILE_HEIGHT = 32;
        public const float HALF_TILE_W = TILE_WIDTH / 2f;
        public const float HALF_TILE_H = TILE_HEIGHT / 2f;

        /// <summary>Grid coordinate to world position — the SOUTH vertex of the
        /// tile diamond (continuous grid point (gx, gy)).  This is the anchor
        /// of the world↔grid projection; see WorldToIso for the inverse.</summary>
        public static Vector2 IsoToWorld(IsoCoord coord)
        {
            return new Vector2(
                (coord.X - coord.Y) * HALF_TILE_W,
                -(coord.X + coord.Y) * HALF_TILE_H);
        }

        /// <summary>Grid coordinate to world position (tile center).
        /// Returns a point that round-trips through WorldToIso to the same tile.
        /// In continuous grid space this is (coord.X + 0.5, coord.Y + 0.5).</summary>
        public static Vector2 IsoToWorldCenter(IsoCoord coord)
        {
            // Grid center (gx+0.5, gy+0.5) in world space:
            // wx = ((gx+0.5) - (gy+0.5)) * halfW = (gx - gy) * halfW
            // wy = -((gx+0.5) + (gy+0.5)) * halfH = -(gx + gy + 1) * halfH
            return new Vector2(
                (coord.X - coord.Y) * HALF_TILE_W,
                -(coord.X + coord.Y + 1) * HALF_TILE_H);
        }

        /// <summary>World position to grid coordinate (floor).</summary>
        public static IsoCoord WorldToIso(Vector2 worldPos)
        {
            // Inverse of IsoToWorld:
            // wx = (gx - gy) * halfW  →  wx/halfW = gx - gy
            // wy = -(gx + gy) * halfH → -wy/halfH = gx + gy
            // gx = (wx/halfW - wy/halfH) / 2
            // gy = (-wy/halfH - wx/halfW) / 2
            float fx = (worldPos.X / HALF_TILE_W - worldPos.Y / HALF_TILE_H) / 2f;
            float fy = (-worldPos.Y / HALF_TILE_H - worldPos.X / HALF_TILE_W) / 2f;
            return new IsoCoord((int)MathF.Floor(fx), (int)MathF.Floor(fy));
        }

        /// <summary>World position to continuous grid coords (for depth computation).</summary>
        public static Vector2 WorldToIsoFloat(Vector2 worldPos)
        {
            return new Vector2(
                (worldPos.X / HALF_TILE_W - worldPos.Y / HALF_TILE_H) / 2f,
                (-worldPos.Y / HALF_TILE_H - worldPos.X / HALF_TILE_W) / 2f);
        }

        /// <summary>Compute layer depth for BackToFront sorting (0=front/near, 1=back/far).
        /// Higher (gx+gy) tiles are farther from viewer (higher on screen).</summary>
        public static float ComputeDepth(IsoCoord coord, int mapWidth, int mapHeight)
        {
            float maxSum = mapWidth + mapHeight;
            return (coord.X + coord.Y) / maxSum;
        }

        /// <summary>
        /// World-space anchor (sprite top-left) for drawing the 64×32 diamond
        /// sprite of a tile so that it covers exactly the logical diamond of
        /// that tile: drawn centre == IsoToWorldCenter, and WorldToIso of any
        /// point inside the sprite resolves back to the tile.  Used by the
        /// tile / fog / highlight renderers.
        /// </summary>
        public static Vector2 TileDrawOrigin(IsoCoord coord)
        {
            return IsoToWorld(coord) - new Vector2(HALF_TILE_W, TILE_HEIGHT);
        }

        /// <summary>
        /// World-space position for a building's anchor point — the centre of
        /// its footprint in continuous grid space (placement + size/2), which
        /// is where the sprite is drawn centred.  With the render plane
        /// aligned to the logical grid this is simply the projection of the
        /// footprint centre.
        /// </summary>
        public static Vector2 BuildingWorldOrigin(IsoCoord placement, int sizeX, int sizeY)
        {
            float cgx = placement.X + sizeX / 2f;
            float cgy = placement.Y + sizeY / 2f;
            return new Vector2(
                (cgx - cgy) * HALF_TILE_W,
                -(cgx + cgy) * HALF_TILE_H);
        }
    }
}
