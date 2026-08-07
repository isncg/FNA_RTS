using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using FNARTS.Core.Movement;
using FNARTS.Core.Pathfinding;
using FNARTS.Core.Tests.Pathfinding;

namespace FNARTS.Core.Tests.Movement
{
    /// <summary>
    /// Free-flowing infantry sub-cell arbitration tests: tile sharing
    /// (up to 4 slots), slot reservation, class-aware blocking between
    /// infantry and vehicles, and sub-cell docking. See
    /// docs/INFANTRY_DESIGN.md §6.
    /// </summary>
    public class MovementSystemInfantryTests
    {
        private const float DT = 0.05f;

        private static (MovementSystem sys, EntityManager em,
            PathfindingFacade pf) Setup(int w = 16, int h = 16)
        {
            var map = PathTestUtil.OpenMap(w, h);
            var em = new EntityManager();
            var terrain = PathTestUtil.TerrainFor(map);
            var pf = new PathfindingFacade(terrain);
            var sys = new MovementSystem(pf, em, terrain);
            return (sys, em, pf);
        }

        private static Unit Spawn(EntityManager em, IsoCoord tile,
            bool infantry, int faction = 0)
        {
            var u = new Unit(new UnitDef
            {
                MoveSpeed = 128f,
                IsInfantry = infantry,
            })
            {
                WorldPosition = CoordUtil.IsoToWorldCenter(tile),
                Faction = faction,
            };
            em.AddEntity(u);
            return u;
        }

        private static void OrderTo(Unit unit, IsoCoord tile,
            PathfindingFacade pf)
        {
            var start = CoordUtil.WorldToIso(unit.WorldPosition);
            var path = pf.FindPath(start, tile);
            unit.MoveTarget = CoordUtil.IsoToWorldCenter(tile);
            unit.Path = path.Count > 0 ? path : null;
            unit.PathIndex = 0;
        }

        /// <summary>One game frame: arbitration first, then unit movement.</summary>
        private static void Step(MovementSystem sys, EntityManager em)
        {
            sys.Update(DT);
            foreach (var e in em.AllEntities)
                if (e is Unit u)
                    u.Update(DT);
        }

        private static bool Simulate(MovementSystem sys, EntityManager em,
            Func<bool> done, int maxSteps = 2000,
            Action perFrame = null)
        {
            for (int i = 0; i < maxSteps; i++)
            {
                Step(sys, em);
                perFrame?.Invoke();
                if (done())
                    return true;
            }
            return false;
        }

        private static IsoCoord TileOf(Unit u)
            => CoordUtil.WorldToIso(u.WorldPosition);

        /// <summary>
        /// Free-flowing infantry occupancy invariant:
        /// - vehicles keep one-per-tile (From and To);
        /// - truly stopped infantry (no path, no MoveTarget): at most
        ///   SubCellInfo.Count per tile, unique slots, no vehicle on it;
        /// - infantry in motion make no slot claims — they flow through
        ///   each other, transit reservations merely carrying their own
        ///   slot — except the FINAL docking reservation (ToTile is the
        ///   last path waypoint), which must not shadow a stopped unit's
        ///   slot (barring the transient spill dock, ToSubCell == Centre
        ///   on a tile that filled up mid-transit).
        /// </summary>
        private static void AssertOccupancyValid(EntityManager em)
        {
            var vehicleTiles = new HashSet<IsoCoord>();
            var stoppedSlots = new HashSet<(IsoCoord, SubCell)>();
            var stoppedCount = new Dictionary<IsoCoord, int>();
            var reservations = new List<(Unit u, IsoCoord tile)>();

            foreach (var e in em.AllEntities)
            {
                if (e is not Unit u || !u.IsAlive || !u.TilesInitialized
                    || u.IsAircraft)
                    continue;

                if (!u.IsInfantry)
                {
                    Assert.True(vehicleTiles.Add(u.FromTile),
                        $"Two vehicles claim tile {u.FromTile}");
                    if (u.ToTile != u.FromTile)
                        Assert.True(vehicleTiles.Add(u.ToTile),
                            $"Two vehicles claim tile {u.ToTile}");
                    continue;
                }

                bool stopped = !u.IsMovingBetweenTiles
                    && u.Path == null && !u.MoveTarget.HasValue;
                if (stopped)
                {
                    Assert.False(vehicleTiles.Contains(u.FromTile),
                        $"Infantry shares tile {u.FromTile} with a vehicle");
                    stoppedCount.TryGetValue(u.FromTile, out int n);
                    stoppedCount[u.FromTile] = n + 1;
                    Assert.True(stoppedCount[u.FromTile] <= SubCellInfo.Count,
                        $"More than {SubCellInfo.Count} infantry on {u.FromTile}");
                    Assert.True(stoppedSlots.Add((u.FromTile, u.SubCell)),
                        $"Two infantry claim slot {u.SubCell} on {u.FromTile}");
                }
                else if (u.IsMovingBetweenTiles)
                {
                    reservations.Add((u, u.ToTile));
                }
            }

            foreach (var (u, tile) in reservations)
            {
                Assert.False(vehicleTiles.Contains(tile),
                    $"Infantry reserved vehicle tile {tile}");
                if (u.ToSubCell == SubCell.Center)
                    continue; // transient spill dock — allowed
                // Only the final docking reservation claims a slot; a
                // transit reservation just carries the unit's own slot.
                bool finalDocking = u.Path != null
                    && u.PathIndex < u.Path.Count
                    && tile == u.Path[u.Path.Count - 1];
                if (!finalDocking)
                    continue;
                Assert.True(stoppedSlots.Add((tile, u.ToSubCell)),
                    $"Reservation shadows slot {u.ToSubCell} on {tile}");
            }
        }

        // ── sharing & slots ──────────────────────────────────────────

        [Fact]
        public void Infantry_CanShareTile_UpToFour()
        {
            var (sys, em, pf) = Setup();
            var dest = new IsoCoord(8, 8);
            // All four start adjacent to dest so the single arbitration
            // frame reserves the destination tile itself.
            var starts = new[]
            {
                new IsoCoord(7, 7), new IsoCoord(8, 7),
                new IsoCoord(7, 8), new IsoCoord(9, 8),
            };
            var units = new List<Unit>();
            for (int i = 0; i < SubCellInfo.Count; i++)
            {
                var u = Spawn(em, starts[i], infantry: true);
                units.Add(u);
                OrderTo(u, dest, pf);
            }

            sys.Update(DT);

            // All four reserve the same tile, each a distinct slot.
            var slots = new HashSet<SubCell>();
            foreach (var u in units)
            {
                Assert.Equal(dest, u.ToTile);
                Assert.True(SubCellInfo.IsInfantrySlot(u.ToSubCell),
                    $"Infantry reserved non-infantry slot {u.ToSubCell}");
                Assert.True(slots.Add(u.ToSubCell),
                    $"Slot {u.ToSubCell} reserved twice");
            }

            var dump0 = new System.Text.StringBuilder();
            foreach (var u in units)
                dump0.AppendLine($"toSub={u.ToSubCell} to={u.ToTile} mt={u.MoveTarget} assigned={u.AssignedTile}/{u.AssignedSubCell}");
            bool allArrived = Simulate(sys, em,
                () =>
                {
                    foreach (var u in units)
                        if (u.IsMoving || TileOf(u) != dest)
                            return false;
                    return true;
                },
                maxSteps: 6000, perFrame: () => AssertOccupancyValid(em));
            var dump = new System.Text.StringBuilder();
            foreach (var u in units)
                dump.AppendLine($"pos={TileOf(u)} sub={u.SubCell} toSub={u.ToSubCell} " +
                    $"from={u.FromTile} to={u.ToTile} moving={u.IsMoving} " +
                    $"wait={u.WaitTimer:F2} path={(u.Path == null ? -1 : u.Path.Count)} " +
                    $"idx={u.PathIndex} mt={u.MoveTarget}");
            Assert.True(allArrived, $"Not all infantry reached the shared tile\nafter reserve:\n{dump0}\nafter sim:\n{dump}");

            // Docked: four distinct slots, positions on their slot points.
            slots.Clear();
            foreach (var u in units)
            {
                Assert.True(slots.Add(u.SubCell));
                Assert.Equal(SubCellInfo.ToWorld(dest, u.SubCell),
                    u.WorldPosition);
            }
        }

        [Fact]
        public void Infantry_FifthUnit_SpillsInsteadOfWaiting()
        {
            // Free-flowing model: a fully-booked tile does not block the
            // mover — it walks in and spills to the nearest tile with a
            // free slot, leaving the occupants untouched.
            var (sys, em, pf) = Setup();
            var dest = new IsoCoord(8, 8);

            var occupants = new List<Unit>();
            for (int i = 0; i < SubCellInfo.Count; i++)
            {
                var occ = Spawn(em, new IsoCoord(8, 8), infantry: true,
                    faction: 1);
                occ.WorldPosition =
                    SubCellInfo.ToWorld(dest, SubCellInfo.First + i);
                occupants.Add(occ);
            }
            sys.Update(DT); // initialise their tiles + slots

            var fifth = Spawn(em, new IsoCoord(8, 7), infantry: true);
            OrderTo(fifth, dest, pf);

            bool settled = Simulate(sys, em, () => !fifth.IsMoving,
                maxSteps: 6000, perFrame: () => AssertOccupancyValid(em));
            Assert.True(settled, "Fifth unit never settled");

            Assert.NotEqual(dest, TileOf(fifth)); // spilled to a neighbour
            foreach (var occ in occupants)
            {
                Assert.Equal(dest, TileOf(occ));  // untouched
                Assert.False(occ.IsMoving);
            }
        }

        [Fact]
        public void FreeSubCell_Deterministic_AndSkipsTaken()
        {
            var (sys, em, pf) = Setup();
            var tile = new IsoCoord(8, 8);
            var a = Spawn(em, tile, infantry: true);
            sys.Update(DT); // a claims the first free slot (North)

            var b = Spawn(em, new IsoCoord(3, 3), infantry: true);
            var first = sys.FreeSubCellFor(b, tile);
            var second = sys.FreeSubCellFor(b, tile);

            Assert.Equal(first, second);
            Assert.NotEqual(a.SubCell, first); // North taken by a
        }

        [Fact]
        public void FreeSubCell_InTransitCountsReservedSlot()
        {
            var (sys, em, pf) = Setup();
            var dest = new IsoCoord(8, 8);
            var a = Spawn(em, new IsoCoord(7, 8), infantry: true);
            OrderTo(a, dest, pf);
            sys.Update(DT); // a reserves dest + a slot

            Assert.Equal(dest, a.ToTile);
            var b = Spawn(em, new IsoCoord(3, 3), infantry: true);
            Assert.NotEqual(a.ToSubCell, sys.FreeSubCellFor(b, dest));
        }

        [Fact]
        public void FreeSubCell_CountsCommandTimeAssignments()
        {
            // In-transit infantry count against a tile via their
            // command-time assignment even before they reserve anything.
            var (sys, em, pf) = Setup();
            var dest = new IsoCoord(8, 8);
            for (int i = 0; i < 2; i++)
            {
                var u = Spawn(em, new IsoCoord(2, 2 + i), infantry: true);
                u.AssignedTile = dest;
                u.AssignedSubCell = SubCellInfo.First + i; // North, East
            }
            sys.Update(DT); // initialise tiles; assignments survive

            var probe = Spawn(em, new IsoCoord(3, 3), infantry: true);
            Assert.Equal(SubCell.South, sys.FreeSubCellFor(probe, dest));
        }

        // ── class-aware blocking ─────────────────────────────────────

        [Fact]
        public void Infantry_BlockedByStandingVehicle()
        {
            var (sys, em, pf) = Setup();
            var dest = new IsoCoord(8, 8);
            // Enemy vehicle: never nudged, just blocks.
            Spawn(em, dest, infantry: false, faction: 1);
            sys.Update(DT);

            var inf = Spawn(em, new IsoCoord(8, 7), infantry: true);
            OrderTo(inf, dest, pf);
            sys.Update(DT);

            Assert.Equal(new IsoCoord(8, 7), inf.ToTile);
            Assert.True(inf.WaitTimer > 0f);
        }

        [Fact]
        public void Infantry_BlockedByVehicleInTransit()
        {
            var (sys, em, pf) = Setup();
            var dest = new IsoCoord(8, 8);
            var vehicle = Spawn(em, new IsoCoord(10, 8), infantry: false,
                faction: 1);
            OrderTo(vehicle, dest, pf);
            // Drive until the vehicle has reserved the destination tile.
            for (int i = 0; i < 400 && vehicle.ToTile != dest; i++)
                Step(sys, em);
            Assert.Equal(dest, vehicle.ToTile);

            var inf = Spawn(em, new IsoCoord(8, 7), infantry: true);
            OrderTo(inf, dest, pf);
            Step(sys, em); // infantry hits the vehicle's reservation

            Assert.Equal(dest, vehicle.ToTile);
            Assert.Equal(new IsoCoord(8, 7), inf.ToTile);
            Assert.True(inf.WaitTimer > 0f);
        }

        [Fact]
        public void Vehicle_BlockedByInfantry()
        {
            var (sys, em, pf) = Setup();
            var dest = new IsoCoord(8, 8);
            Spawn(em, dest, infantry: true, faction: 1); // enemy infantry
            sys.Update(DT);

            var vehicle = Spawn(em, new IsoCoord(9, 8), infantry: false);
            OrderTo(vehicle, dest, pf);
            sys.Update(DT);

            Assert.Equal(new IsoCoord(9, 8), vehicle.ToTile);
            Assert.True(vehicle.WaitTimer > 0f);
        }

        [Fact]
        public void MeleeException_StillWorks()
        {
            var (sys, em, pf) = Setup();
            var dest = new IsoCoord(8, 8);
            var enemy = Spawn(em, dest, infantry: true, faction: 1);
            sys.Update(DT);

            var attacker = Spawn(em, new IsoCoord(8, 7), infantry: true);
            attacker.AttackTargetId = enemy.Id;
            OrderTo(attacker, dest, pf);
            sys.Update(DT);

            // The attack target's tile is enterable despite occupancy.
            Assert.Equal(dest, attacker.ToTile);
        }

        [Fact]
        public void Infantry_HeadOn_PassThroughEachOther()
        {
            // Two infantry ordered through each other's tiles swap places
            // without blocking or waiting.
            var (sys, em, pf) = Setup();
            var a = Spawn(em, new IsoCoord(6, 8), infantry: true);
            var b = Spawn(em, new IsoCoord(10, 8), infantry: true);
            OrderTo(a, new IsoCoord(10, 8), pf);
            OrderTo(b, new IsoCoord(6, 8), pf);

            bool swapped = Simulate(sys, em,
                () => !a.IsMoving && !b.IsMoving,
                maxSteps: 4000, perFrame: () => AssertOccupancyValid(em));
            Assert.True(swapped, "Head-on movers never settled");
            Assert.Equal(new IsoCoord(10, 8), TileOf(a));
            Assert.Equal(new IsoCoord(6, 8), TileOf(b));
        }

        // ── docking & arrival ────────────────────────────────────────

        [Fact]
        public void Infantry_StopAtSubCellPoint()
        {
            var (sys, em, pf) = Setup();
            var dest = new IsoCoord(10, 9);
            var inf = Spawn(em, new IsoCoord(4, 4), infantry: true);
            OrderTo(inf, dest, pf);

            bool arrived = Simulate(sys, em,
                () => !inf.IsMoving && TileOf(inf) == dest,
                maxSteps: 4000, perFrame: () => AssertOccupancyValid(em));

            Assert.True(arrived, "Infantry never reached its destination");
            Assert.True(SubCellInfo.IsInfantrySlot(inf.SubCell));
            Assert.Equal(SubCellInfo.ToWorld(dest, inf.SubCell),
                inf.WorldPosition);
            // Round-trip invariant: the slot point stays inside its tile.
            Assert.Equal(dest, CoordUtil.WorldToIso(inf.WorldPosition));
        }

        [Fact]
        public void Infantry_SameOrder_SpreadsAcrossTiles()
        {
            var (sys, em, pf) = Setup();
            var dest = new IsoCoord(10, 8);
            var units = new List<Unit>();
            for (int i = 0; i < 8; i++)
            {
                var u = Spawn(em, new IsoCoord(2 + i, 3), infantry: true);
                units.Add(u);
                OrderTo(u, dest, pf);
            }

            bool allStopped = Simulate(sys, em,
                () =>
                {
                    foreach (var u in units)
                        if (u.IsMoving)
                            return false;
                    return true;
                },
                maxSteps: 8000, perFrame: () => AssertOccupancyValid(em));
            Assert.True(allStopped, "Group never settled");

            // Exactly five fit on the destination tile; the rest spread
            // onto nearby tiles — and nobody overlaps a slot.
            int onDest = 0;
            foreach (var u in units)
                if (TileOf(u) == dest)
                    onDest++;
            Assert.True(onDest <= SubCellInfo.Count,
                $"{onDest} infantry stacked on one tile");
            Assert.True(onDest >= 3,
                "Expected most infantry to reach the destination tile");
            AssertOccupancyValid(em);
        }

        [Fact]
        public void MixedGroup_OccupancyInvariantHolds()
        {
            // Vehicles and infantry crossing the same area: vehicles keep
            // one-per-tile, infantry share, and the two never mix tiles.
            var (sys, em, pf) = Setup();
            var units = new List<Unit>();
            for (int i = 0; i < 3; i++)
            {
                var v = Spawn(em, new IsoCoord(2, 3 + i), infantry: false);
                units.Add(v);
                OrderTo(v, new IsoCoord(12, 10 - i), pf);
            }
            for (int i = 0; i < 6; i++)
            {
                var inf = Spawn(em, new IsoCoord(3 + i, 12), infantry: true);
                units.Add(inf);
                OrderTo(inf, new IsoCoord(11, 3), pf);
            }

            bool allArrived = Simulate(sys, em,
                () =>
                {
                    foreach (var u in units)
                        if (u.IsMoving)
                            return false;
                    return true;
                },
                maxSteps: 8000, perFrame: () => AssertOccupancyValid(em));
            Assert.True(allArrived, "Mixed group never settled");
            AssertOccupancyValid(em);
        }

        [Fact]
        public void Infantry_DockedOccupants_NotEvictedByLatecomers()
        {
            // Four infantry already docked on dest; three latecomers with
            // the same order must spill to nearby tiles instead of
            // displacing the arrivals.
            var (sys, em, pf) = Setup();
            var dest = new IsoCoord(8, 8);

            var docked = new List<Unit>();
            for (int i = 0; i < SubCellInfo.Count; i++)
            {
                var slot = SubCellInfo.First + i;
                var u = new Unit(new UnitDef { MoveSpeed = 128f, IsInfantry = true })
                {
                    WorldPosition = SubCellInfo.ToWorld(dest, slot),
                    FromTile = dest,
                    ToTile = dest,
                    SubCell = slot,
                    ToSubCell = slot,
                    TilesInitialized = true,
                };
                em.AddEntity(u);
                docked.Add(u);
            }
            sys.Update(DT);

            var late = new List<Unit>();
            for (int i = 0; i < 3; i++)
            {
                var u = Spawn(em, new IsoCoord(7 + i, 3), infantry: true);
                late.Add(u);
                OrderTo(u, dest, pf);
            }

            bool settled = Simulate(sys, em,
                () =>
                {
                    foreach (var u in late)
                        if (u.IsMoving)
                            return false;
                    return true;
                },
                maxSteps: 8000, perFrame: () => AssertOccupancyValid(em));
            Assert.True(settled, "Latecomers never settled");

            // The early arrivals kept their tile and their slots.
            foreach (var u in docked)
            {
                Assert.Equal(dest, TileOf(u));
                Assert.False(u.IsMoving);
            }
            // Latecomers spilled elsewhere — dest stayed fully booked by
            // the original four.
            foreach (var u in late)
                Assert.NotEqual(dest, TileOf(u));
        }

        [Fact]
        public void Infantry_TransitThroughCrowdedTile_FreeFlow()
        {
            // A mover passing THROUGH a fully-booked tile flows straight
            // through — no blocking, no nudging; docked occupants stay put.
            var (sys, em, pf) = Setup();
            var choke = new IsoCoord(8, 8);

            var occupants = new List<Unit>();
            for (int i = 0; i < SubCellInfo.Count; i++)
            {
                var slot = SubCellInfo.First + i;
                var u = new Unit(new UnitDef
                {
                    MoveSpeed = 128f, IsInfantry = true,
                })
                {
                    WorldPosition = SubCellInfo.ToWorld(choke, slot),
                    FromTile = choke,
                    ToTile = choke,
                    SubCell = slot,
                    ToSubCell = slot,
                    TilesInitialized = true,
                };
                em.AddEntity(u);
                occupants.Add(u);
            }
            sys.Update(DT);

            var mover = Spawn(em, new IsoCoord(4, 8), infantry: true);
            OrderTo(mover, new IsoCoord(12, 8), pf);

            bool crossed = Simulate(sys, em,
                () => !mover.IsMoving && TileOf(mover) == new IsoCoord(12, 8),
                maxSteps: 8000, perFrame: () => AssertOccupancyValid(em));
            Assert.True(crossed, "Mover never crossed the crowded tile");
            foreach (var u in occupants)
            {
                Assert.Equal(choke, TileOf(u)); // nobody was displaced
                Assert.False(u.IsMoving);
            }
        }

        // ── spawn initialisation ─────────────────────────────────────

        [Fact]
        public void Spawn_SameTileInfantry_ClaimDistinctSlots()
        {
            var (sys, em, pf) = Setup();
            var tile = new IsoCoord(8, 8);
            var a = Spawn(em, tile, infantry: true);
            var b = Spawn(em, tile, infantry: true);
            b.WorldPosition = SubCellInfo.ToWorld(tile, SubCell.North);

            sys.Update(DT);

            Assert.True(a.TilesInitialized && b.TilesInitialized);
            Assert.True(SubCellInfo.IsInfantrySlot(a.SubCell));
            Assert.True(SubCellInfo.IsInfantrySlot(b.SubCell));
            Assert.NotEqual(a.SubCell, b.SubCell);
            AssertOccupancyValid(em);
        }
    }
}
