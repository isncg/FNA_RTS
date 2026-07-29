using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FNARTS.Core
{
    /// <summary>
    /// Manages all game entities with spatial indexing for fast queries.
    /// </summary>
    public class EntityManager
    {
        private readonly Dictionary<uint, Entity> _entities = new();
        private readonly Dictionary<IsoCoord, List<Entity>> _spatialIndex = new();
        private readonly List<Entity> _allEntities = new();

        public IReadOnlyList<Entity> AllEntities => _allEntities;

        public void AddEntity(Entity entity)
        {
            _entities[entity.Id] = entity;
            _allEntities.Add(entity);
            IndexEntity(entity);
        }

        public void RemoveEntity(uint id)
        {
            if (!_entities.TryGetValue(id, out var entity)) return;
            UnindexEntity(entity);
            _entities.Remove(id);
            _allEntities.Remove(entity);
        }

        public Entity GetEntity(uint id)
        {
            _entities.TryGetValue(id, out var entity);
            return entity;
        }

        /// <summary>Query all entities whose world position falls within a world-space rectangle.</summary>
        public IEnumerable<Entity> QueryRect(Vector2 min, Vector2 max)
        {
            // Simple linear scan (spatial index optimization deferred)
            foreach (var e in _allEntities)
            {
                if (!e.IsAlive) continue;
                if (e.WorldPosition.X >= min.X && e.WorldPosition.X <= max.X &&
                    e.WorldPosition.Y >= min.Y && e.WorldPosition.Y <= max.Y)
                    yield return e;
            }
        }

        /// <summary>Query the topmost entity at a world point (for click selection).</summary>
        public Entity QueryPoint(Vector2 worldPoint)
        {
            Entity best = null;
            float bestY = float.MinValue;
            foreach (var e in _allEntities)
            {
                if (!e.IsAlive) continue;
                // Broad phase: bounding-box check
                var half = e.HitHalfExtent;
                if (worldPoint.X < e.WorldPosition.X - half.X ||
                    worldPoint.X > e.WorldPosition.X + half.X ||
                    worldPoint.Y < e.WorldPosition.Y - half.Y ||
                    worldPoint.Y > e.WorldPosition.Y + half.Y)
                    continue;
                // Narrow phase: per-entity precise test (e.g. face test for buildings)
                if (!e.ContainsPoint(worldPoint))
                    continue;
                // Pick the one with highest Y (closest to camera in isometric)
                if (e.WorldPosition.Y > bestY)
                {
                    bestY = e.WorldPosition.Y;
                    best = e;
                }
            }
            return best;
        }

        /// <summary>Check whether every tile in a rectangular area is free
        /// of alive buildings (for placement validation).</summary>
        public bool IsAreaFree(IsoCoord origin, int sizeX, int sizeY)
        {
            foreach (var e in _allEntities)
            {
                if (e is not Building b || !e.IsAlive) continue;
                for (int x = 0; x < sizeX; x++)
                for (int y = 0; y < sizeY; y++)
                    if (b.OccupiesTile(new IsoCoord(origin.X + x, origin.Y + y)))
                        return false;
            }
            return true;
        }

        private void IndexEntity(Entity entity)
        {
            var coord = CoordUtil.WorldToIso(entity.WorldPosition);
            if (!_spatialIndex.TryGetValue(coord, out var list))
                _spatialIndex[coord] = list = new List<Entity>();
            list.Add(entity);
        }

        /// <summary>Get all alive entities belonging to a faction.</summary>
        public IEnumerable<Entity> GetFactionEntities(int faction)
        {
            return _allEntities.Where(e => e.IsAlive && e.Faction == faction);
        }

        /// <summary>Get all alive entities NOT belonging to a faction (enemies).</summary>
        public IEnumerable<Entity> GetEnemyEntities(int faction)
        {
            return _allEntities.Where(e => e.IsAlive && e.Faction != faction);
        }

        /// <summary>
        /// Remove all dead entities from the manager (index + flat list).
        /// Call after processing deaths (e.g. in CombatSystem's onDeath callback).
        /// </summary>
        public void RemoveDead()
        {
            var dead = _allEntities.Where(e => !e.IsAlive).ToList();
            foreach (var e in dead)
            {
                UnindexEntity(e);
                _entities.Remove(e.Id);
                _allEntities.Remove(e);
            }
        }

        private void UnindexEntity(Entity entity)
        {
            var coord = CoordUtil.WorldToIso(entity.WorldPosition);
            if (_spatialIndex.TryGetValue(coord, out var list))
                list.Remove(entity);
        }
    }
}
