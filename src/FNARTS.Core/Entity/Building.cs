using System;
using System.Collections.Generic;
using System.Numerics;

namespace FNARTS.Core
{
    /// <summary>
    /// A stationary entity that occupies one or more tiles.
    /// </summary>
    public class Building : Entity
    {
        public BuildingDef Definition { get; }
        public IsoCoord PlacementOrigin { get; set; }
        public int SizeX => Definition.SizeX;
        public int SizeY => Definition.SizeY;
        public int Height => Definition.Height;

        /// <summary>Unit training queue (sequential: first item is the one in progress).</summary>
        public Queue<ProductionItem> ProductionQueue { get; } = new();
        public ProductionItem CurrentProduction =>
            ProductionQueue.Count > 0 ? ProductionQueue.Peek() : null;
        public bool IsProducing => CurrentProduction != null;

        /// <summary>Texture dimensions match GenIsometricBox in ProceduralAssetProvider.
        /// texW = (E+N) * HALF_W,  texH = (E+N) * HALF_H + H * 2*TILE_H.</summary>
        public override Vector2 HitHalfExtent
        {
            get
            {
                float texW = (SizeX + SizeY) * CoordUtil.HALF_TILE_W;
                float texH = (SizeX + SizeY) * CoordUtil.HALF_TILE_H
                    + Height * CoordUtil.TILE_HEIGHT * 2f;
                return new Vector2(texW / 2f, texH / 2f);
            }
        }

        /// <summary>Precise hit test: world point must lie on one of the
        /// three visible faces (top, south wall, west wall).
        /// Solves the isometric projection inverse to recover
        /// building-local (gx, gy, hz), then tests each face.</summary>
        public override bool ContainsPoint(Vector2 worldPoint)
        {
            int E = SizeX, N = SizeY, H = Height;

            // Building (0,0,0) in world space — matches constructor.
            float ox = PlacementOrigin.X, oy = PlacementOrigin.Y;
            float originWx = (ox - oy + 1) * CoordUtil.HALF_TILE_W;
            float originWy = -(ox + oy) * CoordUtil.HALF_TILE_H + CoordUtil.TILE_HEIGHT;

            // Solve the isometric projection for local (gx, gy, hz).
            //   wx = originWx + (gx - gy) * HALF_W
            //   wy = originWy - (gx + gy) * HALF_H - hz * TILE_H
            // Let  Dx = (wx - originWx) / HALF_W = gx - gy
            //      Dy = (originWy - wy) / HALF_H = gx + gy + 2·hz
            // Then A = (Dx + Dy) / 2 = gx + hz
            //      B = (Dy - Dx) / 2 = gy + hz
            float Dx = (worldPoint.X - originWx) / CoordUtil.HALF_TILE_W;
            float Dy = (originWy - worldPoint.Y) / CoordUtil.HALF_TILE_H;
            float A = (Dx + Dy) * 0.5f;
            float B = (Dy - Dx) * 0.5f;

            // Top face (roof):    hz = H,  gx = A-H ∈ [0,E],  gy = B-H ∈ [0,N]
            float topGx = A - H;
            float topGy = B - H;
            if (topGx >= 0 && topGx <= E && topGy >= 0 && topGy <= N)
                return true;

            // South wall (gy = 0): hz = B,  gx = A-B ∈ [0,E]
            float sGx = A - B;
            if (B >= 0 && B <= H && sGx >= 0 && sGx <= E)
                return true;

            // West wall (gx = 0):  hz = A,  gy = B-A ∈ [0,N]
            float wGy = B - A;
            if (A >= 0 && A <= H && wGy >= 0 && wGy <= N)
                return true;

            return false;
        }

        public Building(BuildingDef definition, IsoCoord placementOrigin)
        {
            Definition = definition;
            PlacementOrigin = placementOrigin;
            WorldPosition = CoordUtil.BuildingWorldOrigin(
                placementOrigin, definition.SizeX, definition.SizeY);
        }

        /// <summary>Return all grid coordinates occupied by this building.
        /// Includes one extra row south (gy-1) for the south-wall footprint.</summary>
        public IsoCoord[] GetOccupiedTiles()
        {
            var tiles = new IsoCoord[SizeX * (SizeY + 1)];
            int i = 0;
            for (int x = 0; x < SizeX; x++)
            for (int y = -1; y < SizeY; y++)
                tiles[i++] = new IsoCoord(PlacementOrigin.X + x, PlacementOrigin.Y + y);
            return tiles;
        }

        /// <summary>Check if building occupies a given tile.
        /// Extends one tile south (gy-1) for the south-wall visual footprint.</summary>
        public bool OccupiesTile(IsoCoord coord)
        {
            return coord.X >= PlacementOrigin.X &&
                   coord.X < PlacementOrigin.X + SizeX &&
                   coord.Y >= PlacementOrigin.Y - 1 &&   // south wall
                   coord.Y < PlacementOrigin.Y + SizeY;
        }
    }
}
