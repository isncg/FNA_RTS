using System;
using System.Collections.Generic;

namespace FNARTS.Core.Pathfinding
{
    /// <summary>
    /// CellInfo 层对象池。避免每次全地图搜索时分配大块内存。
    /// 池中的 CellInfo[] 在借出时清零（default(CellInfo) = Unvisited）。
    /// </summary>
    public sealed class CellInfoLayerPool
    {
        private const int MaxPoolSize = 4;
        private readonly Stack<CellInfo[]> _pool = new(MaxPoolSize);
        private readonly int _layerSize;

        public CellInfoLayerPool(int mapWidth, int mapHeight)
        {
            _layerSize = mapWidth * mapHeight;
        }

        public PooledLayer Get()
        {
            var layer = _pool.Count > 0 ? _pool.Pop() : new CellInfo[_layerSize];
            Array.Clear(layer, 0, layer.Length);  // 重置为 Unvisited
            return new PooledLayer(this, layer);
        }

        private void Return(CellInfo[] layer)
        {
            if (_pool.Count < MaxPoolSize)
                _pool.Push(layer);
        }

        /// <summary>池化的 CellInfo 层，使用后自动归还。</summary>
        public sealed class PooledLayer : IDisposable
        {
            private CellInfoLayerPool _pool;
            public CellInfo[] Data { get; private set; }

            internal PooledLayer(CellInfoLayerPool pool, CellInfo[] data)
            {
                _pool = pool;
                Data = data;
            }

            public void Dispose()
            {
                if (_pool != null && Data != null)
                {
                    _pool.Return(Data);
                    Data = null;
                    _pool = null;
                }
            }
        }
    }

    /// <summary>
    /// 全地图范围的密集路径图。使用池化的 CellInfo 层。
    /// </summary>
    sealed class MapPathGraph : DensePathGraph
    {
        private readonly CellInfoLayerPool.PooledLayer _pooledLayer;
        private readonly int _mapWidth;

        public MapPathGraph(CellInfoLayerPool layerPool,
            TerrainCostProvider terrain,
            Func<IsoCoord, int> customCost, bool laneBias, bool inReverse)
            : base(terrain, customCost, laneBias, inReverse)
        {
            _pooledLayer = layerPool.Get();
            _mapWidth = terrain.MapWidth;
        }

        public override CellInfo this[IsoCoord pos]
        {
            get => _pooledLayer.Data[pos.Y * _mapWidth + pos.X];
            set => _pooledLayer.Data[pos.Y * _mapWidth + pos.X] = value;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _pooledLayer.Dispose();
            base.Dispose(disposing);
        }
    }
}
