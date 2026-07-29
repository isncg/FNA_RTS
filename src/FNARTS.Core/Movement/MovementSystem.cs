using System;
using System.Collections.Generic;
using System.Numerics;
using FNARTS.Core.Pathfinding;

namespace FNARTS.Core.Movement
{
    /// <summary>
    /// OpenRA-style tile occupancy arbitration (one unit per tile, C&amp;C2
    /// style). Units reserve their next tile before entering; entry is
    /// gated by <see cref="Unit.ToTile"/>. When the next tile is occupied:
    /// <list type="number">
    /// <item>idle friendly blockers get a Nudge (random adjacent tile),</item>
    /// <item>busy blockers are flagged <see cref="Unit.IsBlocking"/> so they
    ///       can step aside when stuck,</item>
    /// <item>the mover waits a random while, keeps waiting while occupants
    ///       are evacuating, then repaths (cooldown with jitter on failure).</item>
    /// </list>
    /// Mirrors OpenRA's Move.PopPath / Mobile / Nudge / MoveCooldownHelper.
    /// Aircraft are exempt — they fly over ground occupancy.
    /// </summary>
    public class MovementSystem
    {
        private readonly PathfindingFacade _pathfinder;
        private readonly EntityManager _entities;
        private readonly TerrainCostProvider _terrain;
        private readonly Dictionary<IsoCoord, List<Unit>> _occupancy = new();
        private readonly Random _random = new();

        // OpenRA: WaitAverage = 40 ticks, WaitSpread = 10 (40 ticks/s → 1s)
        private const float WAIT_AVERAGE = 1.0f;
        private const float WAIT_SPREAD = 0.25f;
        // Wait extension while occupants are already evacuating
        private const float EVACUATE_WAIT = 0.25f;
        // OpenRA MoveCooldownHelper: 20–31 ticks of jittered cooldown
        private const float COOLDOWN_MIN = 0.5f;
        private const float COOLDOWN_MAX = 0.8f;

        private static readonly List<Unit> EmptyList = new(0);

        private static readonly IsoCoord[] Directions =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
            new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
        };

        public MovementSystem(PathfindingFacade pathfinder,
            EntityManager entities, TerrainCostProvider terrain)
        {
            _pathfinder = pathfinder;
            _entities = entities;
            _terrain = terrain;
        }

        /// <summary>
        /// Run one arbitration step. Call BEFORE Unit.Update so that
        /// reservations are in place before units consume their paths.
        /// </summary>
        public void Update(float dt)
        {
            // Pass 1: initialise/sync tiles so freshly spawned units are
            // visible to the occupancy map before anyone arbitrates.
            foreach (var e in _entities.AllEntities)
            {
                if (e is Unit unit && unit.IsAlive && !unit.IsAircraft)
                    SyncTiles(unit);
            }

            RebuildOccupancy();

            // Pass 2: arbitration (reservations, waits, nudges, repaths).
            foreach (var e in _entities.AllEntities)
            {
                if (e is Unit unit && unit.IsAlive && !unit.IsAircraft)
                    Arbitrate(unit, dt);
            }
        }

        /// <summary>Units currently occupying a tile (FromTile or ToTile).</summary>
        public IReadOnlyList<Unit> GetUnitsAt(IsoCoord tile)
            => _occupancy.TryGetValue(tile, out var list) ? list : EmptyList;

        /// <summary>
        /// Whether a unit may enter a tile: terrain passable, no building
        /// footprint, and no other unit occupies or has reserved it.
        /// Melee exception: the tile of one's own attack target is allowed.
        /// </summary>
        public bool CanEnterTile(Unit unit, IsoCoord tile)
        {
            if (!_terrain.IsTerrainPassable(tile))
                return false;
            if (!_entities.IsAreaFree(tile, 1, 1))
                return false;

            if (_occupancy.TryGetValue(tile, out var occupants))
            {
                foreach (var other in occupants)
                {
                    if (other == unit) continue;
                    if (other.Id == unit.AttackTargetId) continue;
                    return false;
                }
            }
            return true;
        }

        // ── internals ─────────────────────────────────────────────────

        private void RebuildOccupancy()
        {
            _occupancy.Clear();
            foreach (var e in _entities.AllEntities)
            {
                if (e is Unit u && u.IsAlive && u.TilesInitialized)
                {
                    AddOccupant(u, u.FromTile);
                    if (u.ToTile != u.FromTile)
                        AddOccupant(u, u.ToTile);
                }
            }
        }

        private void AddOccupant(Unit unit, IsoCoord tile)
        {
            if (!_occupancy.TryGetValue(tile, out var list))
                _occupancy[tile] = list = new List<Unit>(2);
            list.Add(unit);
        }

        /// <summary>Initialise tiles from position on first sight; while
        /// standing, keep them in sync (self-heals spawns/teleports).</summary>
        private static void SyncTiles(Unit unit)
        {
            if (!unit.TilesInitialized)
            {
                unit.FromTile = unit.ToTile =
                    CoordUtil.WorldToIso(unit.WorldPosition);
                unit.TilesInitialized = true;
                return;
            }

            if (!unit.IsMovingBetweenTiles)
            {
                var actual = CoordUtil.WorldToIso(unit.WorldPosition);
                unit.FromTile = unit.ToTile = actual;
            }
        }

        private void Arbitrate(Unit unit, float dt)
        {
            // Waiting for a blocker to leave, or repath cooldown.
            if (unit.WaitTimer > 0f)
            {
                unit.WaitTimer -= dt;
                return;
            }

            // No waypoint pending.
            if (unit.Path == null || unit.PathIndex >= unit.Path.Count)
            {
                if (unit.MoveTarget.HasValue && !unit.IsMovingBetweenTiles)
                {
                    // Destination tile not reached and no path — path to it
                    // (covers StepAside detours that keep the old target).
                    var destTile =
                        CoordUtil.WorldToIso(unit.MoveTarget.Value);
                    if (unit.FromTile != destTile)
                    {
                        Repath(unit);
                        return;
                    }
                }

                // Idle unit blocking others: nudge it out of the way.
                if (unit.IsBlocking && !unit.MoveTarget.HasValue
                    && !unit.IsMovingBetweenTiles)
                    Nudge(unit);
                return;
            }

            var nextTile = unit.Path[unit.PathIndex];
            if (unit.ToTile == nextTile)
                return; // already reserved / entering

            if (CanEnterTile(unit, nextTile))
            {
                Reserve(unit, nextTile);
                return;
            }

            HandleBlocked(unit, nextTile);
        }

        private void Reserve(Unit unit, IsoCoord tile)
        {
            unit.ToTile = tile;
            unit.HasWaited = false;
            // Live update: the occupancy map is rebuilt every frame, but
            // later units in this frame must already see the reservation.
            AddOccupant(unit, tile);
        }

        /// <summary>OpenRA PopPath decision ladder for a blocked tile.</summary>
        private void HandleBlocked(Unit unit, IsoCoord nextTile)
        {
            // Immovable blocker (building) — no point waiting; repath.
            if (!_entities.IsAreaFree(nextTile, 1, 1))
            {
                Repath(unit);
                return;
            }

            // Notify the blockers (OpenRA NotifyBlocker): idle friendlies
            // step aside now; busy ones get flagged to yield when stuck.
            foreach (var blocker in GetUnitsAt(nextTile))
            {
                if (blocker == unit || blocker.Faction != unit.Faction)
                    continue;
                if (!blocker.IsAircraft && blocker.Path == null
                    && !blocker.MoveTarget.HasValue
                    && !blocker.IsMovingBetweenTiles)
                    Nudge(blocker);
                else
                    blocker.IsBlocking = true;
            }

            // Wait a random while to see if they leave (WaitAverage±spread).
            if (!unit.HasWaited)
            {
                unit.HasWaited = true;
                unit.WaitTimer = WAIT_AVERAGE
                    + ((float)_random.NextDouble() * 2f - 1f) * WAIT_SPREAD;
                return;
            }

            // Occupants already leaving — extend the wait instead of repathing.
            if (TileIsEvacuating(nextTile))
            {
                unit.WaitTimer = EVACUATE_WAIT;
                return;
            }

            // Deadlock breaker: if every occupant is itself blocked or
            // waiting, someone must yield — let us step aside.
            if (OccupantsAreBlockedOrWaiting(nextTile) && StepAside(unit))
                return;

            Repath(unit);
        }

        /// <summary>All occupants are in transit out of the tile.</summary>
        private bool TileIsEvacuating(IsoCoord tile)
        {
            if (!_occupancy.TryGetValue(tile, out var occupants)
                || occupants.Count == 0)
                return false;

            foreach (var u in occupants)
            {
                if (u.IsAircraft) continue;
                bool leaving = u.IsMovingBetweenTiles && u.FromTile == tile
                    && u.WaitTimer <= 0f;
                if (!leaving) return false;
            }
            return true;
        }

        private bool OccupantsAreBlockedOrWaiting(IsoCoord tile)
        {
            if (!_occupancy.TryGetValue(tile, out var occupants)
                || occupants.Count == 0)
                return false;

            foreach (var u in occupants)
            {
                if (u.IsAircraft) continue;
                if (!u.IsBlocking && u.WaitTimer <= 0f)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Repath from the current tile to the destination. On failure,
        /// back up if blocking others; otherwise apply a jittered cooldown
        /// (OpenRA MoveCooldownHelper) to avoid repath storms.
        /// </summary>
        private void Repath(Unit unit)
        {
            var destTile = unit.MoveTarget.HasValue
                ? CoordUtil.WorldToIso(unit.MoveTarget.Value)
                : unit.Path != null && unit.Path.Count > 0
                    ? unit.Path[unit.Path.Count - 1]
                    : unit.FromTile;

            var start = unit.FromTile;
            if (start == destTile)
            {
                // Already on the destination tile — snap and finish.
                unit.Path = null;
                unit.PathIndex = 0;
                unit.MoveTarget = CoordUtil.IsoToWorldCenter(destTile);
                unit.HasWaited = false;
                return;
            }

            var path = _pathfinder.FindPath(start, destTile);
            if (path.Count > 0)
            {
                // Repathing onto the same blocked tile gains nothing —
                // treat as failure (back up or cooldown instead).
                if (unit.Path != null && unit.PathIndex < unit.Path.Count
                    && path[0] == unit.Path[unit.PathIndex])
                {
                    if (unit.IsBlocking && StepAside(unit))
                        return;
                    unit.WaitTimer = COOLDOWN_MIN
                        + (float)_random.NextDouble()
                        * (COOLDOWN_MAX - COOLDOWN_MIN);
                    return;
                }

                unit.Path = path;
                unit.PathIndex = 0;
                unit.HasWaited = false; // restart wait cycle if blocked again
                return;
            }

            // No way around: if we are blocking others, back up one tile.
            if (unit.IsBlocking && StepAside(unit))
                return;

            unit.WaitTimer = COOLDOWN_MIN
                + (float)_random.NextDouble() * (COOLDOWN_MAX - COOLDOWN_MIN);
        }

        /// <summary>
        /// Move to a random enterable adjacent tile (OpenRA
        /// GetAdjacentEnterableCell). Keeps MoveTarget so units with real
        /// orders repath to it after the detour.
        /// </summary>
        private bool StepAside(Unit unit)
        {
            var candidates = new List<IsoCoord>(Directions.Length);
            foreach (var d in Directions)
            {
                var c = unit.FromTile + d;
                if (CanEnterTile(unit, c))
                    candidates.Add(c);
            }
            if (candidates.Count == 0)
                return false;

            var pick = candidates[_random.Next(candidates.Count)];
            unit.Path = new List<IsoCoord> { pick };
            unit.PathIndex = 0;
            unit.IsBlocking = false;
            unit.HasWaited = false;
            unit.WaitTimer = 0f;
            return true;
        }

        /// <summary>
        /// Nudge an idle blocker: step one tile aside AND adopt that tile
        /// as the new destination (OpenRA Nudge activity).
        /// </summary>
        private void Nudge(Unit unit)
        {
            if (!StepAside(unit))
                return;
            unit.MoveTarget = CoordUtil.IsoToWorldCenter(unit.Path[0]);
        }
    }
}
