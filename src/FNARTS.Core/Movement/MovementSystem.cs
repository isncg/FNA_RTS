using System;
using System.Collections.Generic;
using System.Numerics;
using FNARTS.Core.Pathfinding;

namespace FNARTS.Core.Movement
{
    /// <summary>
    /// Tile occupancy arbitration. Vehicles keep the C&amp;C2
    /// one-unit-per-tile rule with full reservation arbitration (reserve
    /// next tile before entering, wait/nudge/repath ladder when blocked).
    /// Infantry are free-flowing instead: they never block each other in
    /// transit and walk through one another. Their destinations are
    /// sub-cell slot assignments (tile + slot, recorded on
    /// <see cref="Unit.AssignedTile"/>/<see cref="Unit.AssignedSubCell"/>
    /// at command time); a slot is only physically claimed when the unit
    /// reserves the FINAL tile of its path. On an arrival conflict the
    /// unit takes another free slot on the same tile, or spills to the
    /// nearest tile with a free slot. Vehicles and buildings still block
    /// infantry; docked infantry still block vehicles. Aircraft are
    /// exempt — they fly over ground occupancy.
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
        /// footprint, and class-aware occupancy. Infantry pass freely
        /// through other infantry (docking is resolved by slot
        /// assignment, not by blocking); vehicles and buildings still
        /// block them. Vehicles are blocked by ANY occupant (one unit
        /// per tile). Melee exception: the tile of one's own attack
        /// target is allowed.
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
                    // Infantry never block each other — large groups flow
                    // freely; slot conflicts are resolved at docking time.
                    if (unit.IsInfantry && other.IsInfantry) continue;
                    if (other.Id == unit.AttackTargetId) continue;
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Command-layer query: the sub-cell slot a unit would claim on a
        /// tile right now. Returns <see cref="SubCell.FullCell"/> when the
        /// tile cannot host the unit (vehicle present / all slots taken).
        /// </summary>
        public SubCell FreeSubCellFor(Unit unit, IsoCoord tile)
            => FreeSubCell(tile, SubCellInfo.First, null);

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
        private void SyncTiles(Unit unit)
        {
            if (!unit.TilesInitialized)
            {
                var tile = CoordUtil.WorldToIso(unit.WorldPosition);
                unit.FromTile = unit.ToTile = tile;
                if (unit.IsInfantry)
                {
                    // Fresh spawn / teleport: prefer the slot the actual
                    // position sits on (scenario spawns pre-place units
                    // at slot points), but never shadow an existing claim
                    // — e.g. a centre spawn must not grab a vertex slot
                    // another unit already holds. Full tile: keep the
                    // position-derived slot anyway (resolves on the next
                    // move).
                    var atPoint = SlotAtWorld(tile, unit.WorldPosition);
                    var preferred = SubCellInfo.IsInfantrySlot(atPoint)
                        ? atPoint : SubCellInfo.First;
                    var sub = FreeSubCell(tile, preferred, unit);
                    if (!SubCellInfo.IsInfantrySlot(sub))
                        sub = preferred;
                    unit.SubCell = unit.ToSubCell = sub;
                }
                unit.TilesInitialized = true;
                // Register immediately so other units initialised later in
                // this same pass (same-tile spawns) already see us and
                // claim a different slot. RebuildOccupancy re-syncs the
                // map right after this pass.
                AddOccupant(unit, tile);
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
                Reserve(unit, nextTile,
                    unit.PathIndex == unit.Path.Count - 1);
                return;
            }

            HandleBlocked(unit, nextTile);
        }

        private void Reserve(Unit unit, IsoCoord tile, bool isFinal)
        {
            unit.ToTile = tile;
            unit.HasWaited = false;

            if (unit.IsInfantry)
            {
                if (isFinal)
                {
                    // Docking reservation: claim a slot, preferring the
                    // command-time assignment. Same-frame earlier
                    // reservations are visible via ToTile/ToSubCell, so
                    // two units can never claim the same slot here.
                    var preferred = unit.AssignedTile == tile
                        && SubCellInfo.IsInfantrySlot(unit.AssignedSubCell)
                        ? unit.AssignedSubCell : unit.SubCell;
                    var sub = FreeSubCell(tile, preferred, unit);
                    if (SubCellInfo.IsInfantrySlot(sub))
                    {
                        unit.ToSubCell = sub;
                        unit.AssignedTile = tile;
                        unit.AssignedSubCell = sub;
                        if (unit.MoveTarget.HasValue
                            && CoordUtil.WorldToIso(unit.MoveTarget.Value)
                                == tile)
                            unit.MoveTarget =
                                SubCellInfo.ToWorld(tile, sub);
                    }
                    else
                    {
                        // Arrival conflict: the tile filled up in transit
                        // (e.g. a vehicle parked on it). Walk in on a
                        // temporary centre dock and spill to the nearest
                        // tile with a free slot — resolved after arrival
                        // when Arbitrate repaths to the new MoveTarget.
                        unit.ToSubCell = SubCell.Center; // transient dock marker (not a slot)
                        var spill = FindSpillTile(unit, tile);
                        if (spill.HasValue)
                        {
                            var s2 = FreeSubCell(spill.Value,
                                SubCellInfo.First, unit);
                            if (!SubCellInfo.IsInfantrySlot(s2))
                                s2 = SubCellInfo.First;
                            unit.MoveTarget =
                                SubCellInfo.ToWorld(spill.Value, s2);
                            unit.AssignedTile = spill.Value;
                            unit.AssignedSubCell = s2;
                        }
                    }
                }
                else
                {
                    // Transit: infantry reserve no slots mid-path — they
                    // flow through each other. Carry the current slot so
                    // waypoint arrival keeps SubCell valid.
                    unit.ToSubCell = unit.SubCell;
                }
            }
            else
            {
                unit.ToSubCell = SubCell.FullCell;
            }

            // Live update: the occupancy map is rebuilt every frame, but
            // later units in this frame must already see the reservation.
            AddOccupant(unit, tile);
        }

        /// <summary>
        /// First free sub-cell slot on a tile in fixed slot order
        /// (deterministic). The preferred slot wins when free. Counts
        /// every claim on the tile: docked infantry by their slot,
        /// arriving infantry by their final-reservation slot, in-transit
        /// infantry by their command-time assignment, and vehicles
        /// (docked or reserved) make the whole tile unavailable.
        /// <paramref name="ignore"/> is exempt from its own claims —
        /// reservation-time queries pass the mover so its freshly written
        /// ToTile/ToSubCell cannot shadow itself; command-layer queries
        /// pass null so a unit's own slot is never double-assigned.
        /// </summary>
        private SubCell FreeSubCell(IsoCoord tile, SubCell preferred,
            Unit ignore)
        {
            // Index by (int)SubCell directly; index 0 (Center) is never
            // marked — it is not an infantry slot.
            Span<bool> taken = stackalloc bool[
                (int)SubCellInfo.First + SubCellInfo.Count];
            foreach (var e in _entities.AllEntities)
            {
                if (e is not Unit u || !u.IsAlive || !u.TilesInitialized
                    || u.IsAircraft || u == ignore)
                    continue;

                if (!u.IsInfantry)
                {
                    // Vehicles occupy the whole tile — docked or reserved.
                    if (u.FromTile == tile || u.ToTile == tile)
                        return SubCell.FullCell;
                    continue;
                }

                SubCell s;
                if (u.FromTile == tile)
                    s = u.SubCell;                  // docked / leaving
                else if (u.ToTile == tile)
                    s = u.ToSubCell;                // final reservation
                else if (u.AssignedTile == tile
                    && SubCellInfo.IsInfantrySlot(u.AssignedSubCell))
                    s = u.AssignedSubCell;          // command assignment
                else
                    continue;

                if (SubCellInfo.IsInfantrySlot(s))
                    taken[(int)s] = true;
            }

            if (SubCellInfo.IsInfantrySlot(preferred) && !taken[(int)preferred])
                return preferred;
            for (int i = 0; i < SubCellInfo.Count; i++)
                if (!taken[(int)SubCellInfo.First + i])
                    return SubCellInfo.First + i;
            return SubCell.FullCell;
        }

        /// <summary>
        /// Which slot point of a tile a world position sits on exactly;
        /// <see cref="SubCell.FullCell"/> when it matches none.
        /// </summary>
        private static SubCell SlotAtWorld(IsoCoord tile, Vector2 world)
        {
            for (int i = 0; i < SubCellInfo.Count; i++)
            {
                var slot = SubCellInfo.First + i;
                if (SubCellInfo.ToWorld(tile, slot) == world)
                    return slot;
            }
            return SubCell.FullCell;
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
            // (Infantry-vs-infantry blocks no longer occur — infantry
            // pass through each other — so this only fires for vehicle
            // and building blockers now.)
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

            // Infantry overflow: every slot on the destination tile is
            // taken — redirect to the nearest enterable tile with a free
            // slot (RA2-style spill; docked infantry are never evicted).
            if (unit.IsInfantry && unit.MoveTarget.HasValue
                && !SubCellInfo.IsInfantrySlot(
                    FreeSubCell(destTile, SubCellInfo.First, unit)))
            {
                var spill = FindSpillTile(unit, destTile);
                if (spill.HasValue)
                {
                    var slot = FreeSubCell(spill.Value, SubCellInfo.First, unit);
                    if (!SubCellInfo.IsInfantrySlot(slot))
                        slot = SubCellInfo.First;
                    unit.MoveTarget = SubCellInfo.ToWorld(spill.Value, slot);
                    destTile = spill.Value;
                }
            }

            var start = unit.FromTile;
            if (start == destTile)
            {
                // Already on the destination tile — snap and finish.
                unit.Path = null;
                unit.PathIndex = 0;
                if (unit.IsInfantry)
                {
                    // Infantry dock at their slot point, not the centre.
                    var sub = SubCellInfo.IsInfantrySlot(unit.SubCell)
                        ? unit.SubCell
                        : FreeSubCell(destTile, SubCellInfo.First, unit);
                    if (!SubCellInfo.IsInfantrySlot(sub))
                        sub = SubCellInfo.First;
                    unit.MoveTarget = SubCellInfo.ToWorld(destTile, sub);
                    unit.AssignedTile = destTile;
                    unit.AssignedSubCell = sub;
                }
                else
                {
                    unit.MoveTarget = CoordUtil.IsoToWorldCenter(destTile);
                }
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
        /// Nearest tile with a free sub-cell slot for an infantry mover,
        /// searching expanding rings (1..5) around a fully-booked tile.
        /// Returns null when no enterable, reachable tile has room.
        /// </summary>
        private IsoCoord? FindSpillTile(Unit unit, IsoCoord around)
        {
            for (int ring = 1; ring <= 5; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                for (int dy = -ring; dy <= ring; dy++)
                {
                    if (Math.Abs(dx) < ring && Math.Abs(dy) < ring)
                        continue; // inner ring, already checked

                    var tile = new IsoCoord(around.X + dx, around.Y + dy);
                    if (tile == unit.FromTile)
                        continue; // never spill onto our own tile
                    if (!CanEnterTile(unit, tile))
                        continue;
                    if (!SubCellInfo.IsInfantrySlot(
                        FreeSubCell(tile, SubCellInfo.First, unit)))
                        continue; // enterable but no free docking slot
                    if (tile != unit.FromTile
                        && _pathfinder.FindPath(unit.FromTile, tile).Count == 0)
                        continue;
                    return tile;
                }
            }
            return null;
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
