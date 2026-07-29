using System;
using System.Collections.Generic;
using System.Numerics;
using FNARTS.Core.Pathfinding;

namespace FNARTS.Core.Movement
{
    /// <summary>
    /// Manages a group of units that move together while preserving
    /// their relative layout (RA1/RA2 style). Slot offsets are a one-time
    /// snapshot of each unit's tile position relative to the group's
    /// bounding-box centre, taken at order time — a spread-out group stays
    /// spread out, a tight group stays tight. There is no in-march
    /// formation maintenance: every unit pathfinds to its own slot tile
    /// and congestion is resolved by MovementSystem arbitration.
    /// </summary>
    public class GroupMovement
    {
        /// <summary>Index of the "leader" — the unit closest to the
        /// bounding-box centre at order time. Rendering highlight only;
        /// re-assigned each Update to the furthest-ahead unit.</summary>
        public int LeaderIndex { get; private set; }

        /// <summary>All units in this formation group.</summary>
        public Unit[] Units { get; }

        /// <summary>Grid-space slot offsets relative to the target tile,
        /// per unit (SlotOffsets[i] is unit i's slot). Snapshot of each
        /// unit's tile position relative to the bounding-box centre at
        /// order time (RA1 Adjust_Dest semantics), so every slot lands on
        /// a distinct tile — one unit per tile (C&amp;C2 style).</summary>
        public IsoCoord[] SlotOffsets { get; }

        /// <summary>Path for the formation centre.</summary>
        public List<IsoCoord>? FormationPath { get; set; }

        /// <summary>Current waypoint index in FormationPath.</summary>
        private int _pathIndex;

        /// <summary>Target tile of the formation centre (snapped).</summary>
        public IsoCoord TargetTile { get; private set; }

        /// <summary>Target world position for the formation centre
        /// (centre of TargetTile).</summary>
        public Vector2 TargetPosition { get; private set; }

        /// <summary>Current formation centre. Moves independently along the
        /// path — NOT derived from any unit's physics position.</summary>
        public Vector2 FormationCenter { get; private set; }

        /// <summary>Attack target for the entire group (attack-move).</summary>
        public uint? GroupAttackTargetId { get; set; }

        private const float WAYPOINT_RADIUS = 4f;
        private const float ARRIVE_RADIUS = 2f;

        /// <summary>Ring-search radius when a slot tile is unreachable.</summary>
        private const int SLOT_FALLBACK_RINGS = 3;

        /// <summary>
        /// Create a new group movement. Slot offsets are each unit's tile
        /// position relative to the group's bounding-box centre (RA1
        /// Toggle_Formation semantics). No shape choice — the current
        /// layout IS the formation.
        /// </summary>
        public GroupMovement(Unit[] units)
        {
            Units = units;

            int n = units.Length;
            var tiles = new IsoCoord[n];
            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            for (int i = 0; i < n; i++)
            {
                tiles[i] = CoordUtil.WorldToIso(units[i].WorldPosition);
                if (tiles[i].X < minX) minX = tiles[i].X;
                if (tiles[i].X > maxX) maxX = tiles[i].X;
                if (tiles[i].Y < minY) minY = tiles[i].Y;
                if (tiles[i].Y > maxY) maxY = tiles[i].Y;
            }

            // Anchor = bounding-box centre (RA1: (maxx-minx)/2 + minx).
            var anchor = new IsoCoord((minX + maxX) / 2, (minY + maxY) / 2);

            SlotOffsets = new IsoCoord[n];
            for (int i = 0; i < n; i++)
                SlotOffsets[i] = new IsoCoord(
                    tiles[i].X - anchor.X, tiles[i].Y - anchor.Y);

            // Formation centre sits on the anchor tile
            FormationCenter = CoordUtil.IsoToWorldCenter(anchor);

            // Leader = unit closest to the anchor (render highlight only)
            int bestIdx = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float d = Vector2.DistanceSquared(
                    units[i].WorldPosition, FormationCenter);
                if (d < bestDist) { bestDist = d; bestIdx = i; }
            }
            LeaderIndex = bestIdx;
        }

        /// <summary>
        /// Reassign leader to the unit with the shortest remaining distance
        /// to its target, then advance the FormationCenter along its path.
        /// Call every frame.
        /// </summary>
        /// <param name="dt">Frame delta time.</param>
        /// <param name="groupSpeed">Speed at which the formation centre moves.</param>
        public void Update(float dt, float groupSpeed)
        {
            // ── Dynamic leader: the unit furthest ahead (shortest remaining
            //     distance) leads so the formation doesn't stall behind a
            //     slow or stuck unit ──
            int bestIdx = LeaderIndex;
            float bestDist = float.MaxValue;
            for (int i = 0; i < Units.Length; i++)
            {
                if (!Units[i].IsAlive) continue;
                float d = Units[i].MoveTarget.HasValue
                    ? Vector2.Distance(Units[i].WorldPosition,
                        Units[i].MoveTarget.Value)
                    : 0f;
                if (d < bestDist) { bestDist = d; bestIdx = i; }
            }
            LeaderIndex = bestIdx;

            // Advance formation centre along the path (for debug / visualisation)
            if (FormationPath != null && _pathIndex < FormationPath.Count)
            {
                var wpCenter = CoordUtil.IsoToWorldCenter(FormationPath[_pathIndex]);
                Vector2 toWp = wpCenter - FormationCenter;
                float dist = toWp.Length();

                if (dist < WAYPOINT_RADIUS)
                {
                    FormationCenter = wpCenter;
                    _pathIndex++;
                }
                else
                {
                    Vector2 dir = toWp / dist;
                    float step = groupSpeed * dt;
                    if (step >= dist)
                        FormationCenter = wpCenter;
                    else
                        FormationCenter += dir * step;
                }
            }
            else if (FormationPath != null && _pathIndex >= FormationPath.Count)
            {
                Vector2 toTarget = TargetPosition - FormationCenter;
                float dist = toTarget.Length();
                if (dist < ARRIVE_RADIUS)
                    FormationCenter = TargetPosition;
                else
                {
                    float step = groupSpeed * dt;
                    FormationCenter += Vector2.Normalize(toTarget)
                        * MathF.Min(step, dist);
                }
            }
        }

        /// <summary>
        /// Issue a move order to the entire group.
        /// Every unit pathfinds to its own formation-slot TILE — C&amp;C2
        /// style one unit per tile, each destination snapped to a tile
        /// centre. Slot tiles are clamped to the map edge (RA1 Bound()),
        /// so a group ordered past the border compresses along it.
        /// </summary>
        /// <param name="targetWorldPos">World-space destination (snapped to its tile) for the formation centre.</param>
        /// <param name="pathfinder">The A* pathfinder (also supplies map bounds).</param>
        public void IssueMoveOrder(Vector2 targetWorldPos, PathfindingFacade pathfinder)
        {
            TargetTile = ClampToMap(CoordUtil.WorldToIso(targetWorldPos),
                pathfinder.MapWidth, pathfinder.MapHeight);
            TargetPosition = CoordUtil.IsoToWorldCenter(TargetTile);

            // Compute min speed — all units march at the slowest unit's pace
            float minSpeed = float.MaxValue;
            for (int i = 0; i < Units.Length; i++)
            {
                if (Units[i].IsAlive && Units[i].Definition.MoveSpeed < minSpeed)
                    minSpeed = Units[i].Definition.MoveSpeed;
            }
            if (minSpeed == float.MaxValue) minSpeed = 100f;

            // Formation centre path: from the anchor tile to target tile
            var centerStart = CoordUtil.WorldToIso(FormationCenter);
            FormationPath = pathfinder.FindPath(centerStart, TargetTile);
            _pathIndex = 0;

            // Each unit pathfinds to its own slot tile
            for (int i = 0; i < Units.Length; i++)
            {
                var unit = Units[i];
                IsoCoord slotTile = ClampToMap(
                    TargetTile + SlotOffsets[i],
                    pathfinder.MapWidth, pathfinder.MapHeight);

                unit.ClearOrders();
                unit.ForcedMoveSpeed = minSpeed;
                unit.AttackTargetId = GroupAttackTargetId;

                var start = CoordUtil.WorldToIso(unit.WorldPosition);
                var path = FindSlotPath(start, slotTile, pathfinder, out var destTile);

                if (destTile.HasValue)
                {
                    // Destination is always a tile centre — never a free
                    // world point — so units end exactly on grid positions.
                    unit.MoveTarget = CoordUtil.IsoToWorldCenter(destTile.Value);
                    unit.Path = path.Count > 0 ? path : null; // empty = same tile
                    unit.PathIndex = 0;
                    unit.ResetStuckTracking();
                }
                // else: boxed in — no reachable slot, unit stays put.
            }
        }

        /// <summary>Clamp a tile coordinate into the map rectangle
        /// (RA1 Adjust_Dest's Bound()).</summary>
        private static IsoCoord ClampToMap(IsoCoord tile, int w, int h)
            => new IsoCoord(
                Math.Clamp(tile.X, 0, w - 1),
                Math.Clamp(tile.Y, 0, h - 1));

        /// <summary>
        /// Find a path to the ideal slot tile; if unreachable (blocked,
        /// out of bounds), search nearby tiles in expanding rings.
        /// Returns null with destTile null when nothing reachable exists.
        /// </summary>
        private static List<IsoCoord>? FindSlotPath(IsoCoord start,
            IsoCoord ideal, PathfindingFacade pathfinder,
            out IsoCoord? destTile)
        {
            destTile = null;
            if (start == ideal)
            {
                destTile = ideal; // already on the slot tile: snap to centre
                return new List<IsoCoord>();
            }

            var path = pathfinder.FindPath(start, ideal);
            if (path.Count > 0)
            {
                destTile = ideal;
                return path;
            }

            for (int ring = 1; ring <= SLOT_FALLBACK_RINGS; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                for (int dy = -ring; dy <= ring; dy++)
                {
                    if (Math.Abs(dx) < ring && Math.Abs(dy) < ring)
                        continue; // inner ring already checked
                    var candidate = new IsoCoord(ideal.X + dx, ideal.Y + dy);
                    if (candidate == start) continue;
                    var fallbackPath = pathfinder.FindPath(start, candidate);
                    if (fallbackPath.Count > 0)
                    {
                        destTile = candidate;
                        return fallbackPath;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Whether every unit in the group has arrived at its destination.
        /// </summary>
        public bool AllArrived
        {
            get
            {
                foreach (var u in Units)
                    if (u.IsAlive && u.IsMoving) return false;
                return true;
            }
        }
    }
}
