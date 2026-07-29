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
    /// OpenRA-style tile occupancy arbitration tests: reservations,
    /// nudging idle blockers, waits, repaths and deadlock breaking.
    /// </summary>
    public class MovementSystemTests
    {
        private const float DT = 0.05f;

        private static (MovementSystem sys, EntityManager em,
            PathfindingFacade pf) Setup(int w = 16, int h = 16,
            HashSet<IsoCoord> blocked = null)
        {
            var map = PathTestUtil.OpenMap(w, h);
            var em = new EntityManager();
            var terrain = PathTestUtil.TerrainFor(map, blocked);
            var pf = new PathfindingFacade(terrain);
            var sys = new MovementSystem(pf, em, terrain);
            return (sys, em, pf);
        }

        private static Unit Spawn(EntityManager em, IsoCoord tile,
            float speed = 128f, int faction = 0)
        {
            var u = new Unit(new UnitDef { MoveSpeed = speed })
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

        /// <summary>No tile may ever be the FromTile of two units.</summary>
        private static void AssertFromTilesUnique(EntityManager em)
        {
            var seen = new HashSet<IsoCoord>();
            foreach (var e in em.AllEntities)
            {
                if (e is Unit u && u.TilesInitialized)
                    Assert.True(seen.Add(u.FromTile),
                        $"Two units share FromTile {u.FromTile}");
            }
        }

        [Fact]
        public void Reserve_FirstComeFirstServed_SecondUnitWaits()
        {
            var (sys, em, pf) = Setup();
            var a = Spawn(em, new IsoCoord(5, 5));
            var b = Spawn(em, new IsoCoord(7, 5));
            OrderTo(a, new IsoCoord(6, 5), pf);
            OrderTo(b, new IsoCoord(6, 5), pf);

            sys.Update(DT);

            // A (added first) reserves the contested tile; B must wait.
            Assert.Equal(new IsoCoord(6, 5), a.ToTile);
            Assert.Equal(new IsoCoord(7, 5), b.ToTile);
            Assert.True(b.WaitTimer > 0f);
        }

        [Fact]
        public void BlockedByIdleFriendly_NudgesBlockerAside()
        {
            var (sys, em, pf) = Setup();
            var a = Spawn(em, new IsoCoord(5, 5));
            var b = Spawn(em, new IsoCoord(6, 5)); // idle blocker
            OrderTo(a, new IsoCoord(7, 5), pf);

            sys.Update(DT);

            // A waits; the idle blocker gets a one-tile nudge order.
            Assert.Equal(new IsoCoord(5, 5), a.ToTile);
            Assert.True(a.WaitTimer > 0f);
            Assert.NotNull(b.Path);
            Assert.Single(b.Path);
            Assert.True(b.MoveTarget.HasValue);
            Assert.NotEqual(new IsoCoord(6, 5), b.Path[0]);
        }

        [Fact]
        public void BlockedByIdleBlocker_MoverEventuallyArrives()
        {
            var (sys, em, pf) = Setup();
            var a = Spawn(em, new IsoCoord(4, 5));
            var b = Spawn(em, new IsoCoord(6, 5)); // idle, on the path
            OrderTo(a, new IsoCoord(8, 5), pf);

            bool arrived = Simulate(sys, em,
                () => !a.IsMoving && TileOf(a) == new IsoCoord(8, 5),
                maxSteps: 4000, perFrame: () => AssertFromTilesUnique(em));

            Assert.True(arrived, "Mover never reached its destination");
            Assert.NotEqual(new IsoCoord(6, 5), TileOf(b));
        }

        [Fact]
        public void BlockedByEnemyUnit_DoesNotNudge_StillResolves()
        {
            var (sys, em, pf) = Setup();
            var a = Spawn(em, new IsoCoord(5, 5), faction: 0);
            var b = Spawn(em, new IsoCoord(6, 5), faction: 1);
            OrderTo(a, new IsoCoord(7, 5), pf);

            sys.Update(DT);

            // Enemy blockers are never nudged — they just block.
            Assert.Null(b.Path);
            Assert.False(b.IsBlocking);
            Assert.True(a.WaitTimer > 0f);
        }

        [Fact]
        public void BlockedByBuilding_RepathsAroundIt()
        {
            // Building-aware pathfinding (as in the game) + real building
            // for the occupancy check.
            var blocked = new HashSet<IsoCoord> { new(6, 5) };
            var (sys, em, pf) = Setup(blocked: blocked);
            var building = new Building(
                new BuildingDef { SizeX = 1, SizeY = 1 }, new IsoCoord(6, 5));
            em.AddEntity(building);

            var a = Spawn(em, new IsoCoord(5, 5));
            // Stale straight-line path straight through the building.
            a.MoveTarget = CoordUtil.IsoToWorldCenter(new IsoCoord(7, 5));
            a.Path = new List<IsoCoord> { new(6, 5), new(7, 5) };
            a.PathIndex = 0;

            sys.Update(DT);

            // Repath detours around the building footprint.
            Assert.NotNull(a.Path);
            Assert.DoesNotContain(new IsoCoord(6, 5), a.Path);
        }

        [Fact]
        public void HeadOnCollision_BothUnitsArrive()
        {
            var (sys, em, pf) = Setup();
            var a = Spawn(em, new IsoCoord(4, 6));
            var b = Spawn(em, new IsoCoord(8, 6));
            OrderTo(a, new IsoCoord(8, 6), pf);
            OrderTo(b, new IsoCoord(4, 6), pf);

            bool bothArrived = Simulate(sys, em,
                () => !a.IsMoving && !b.IsMoving
                    && TileOf(a) == new IsoCoord(8, 6)
                    && TileOf(b) == new IsoCoord(4, 6),
                maxSteps: 6000, perFrame: () => AssertFromTilesUnique(em));

            Assert.True(bothArrived,
                $"Head-on deadlock not resolved: A@{TileOf(a)} B@{TileOf(b)}");
        }

        [Fact]
        public void Aircraft_BypassOccupancyArbitration()
        {
            var (sys, em, pf) = Setup();
            var plane = new Unit(new UnitDef { MoveSpeed = 128f, IsAircraft = true })
            {
                WorldPosition = CoordUtil.IsoToWorldCenter(new IsoCoord(5, 5)),
            };
            em.AddEntity(plane);
            var b = Spawn(em, new IsoCoord(6, 5)); // ground unit underneath

            plane.Path = new List<IsoCoord> { new(6, 5), new(7, 5) };
            plane.MoveTarget = CoordUtil.IsoToWorldCenter(new IsoCoord(7, 5));
            plane.PathIndex = 0;

            bool arrived = Simulate(sys, em,
                () => !plane.IsMoving && TileOf(plane) == new IsoCoord(7, 5),
                maxSteps: 1000);

            Assert.True(arrived, "Aircraft should fly over ground occupancy");
            // Ground unit was not displaced by the aircraft.
            Assert.Equal(new IsoCoord(6, 5), TileOf(b));
        }

        [Fact]
        public void GroupMarch_SlotsNeverCollide()
        {
            // Four units marching through a 1-tile-wide bottleneck must
            // serialize, never share a tile.
            var map = PathTestUtil.OpenMap(16, 16);
            var walls = new HashSet<IsoCoord>();
            for (int y = 0; y < 16; y++)
            {
                if (y != 8) walls.Add(new IsoCoord(8, y));
            }
            var em = new EntityManager();
            var terrain = PathTestUtil.TerrainFor(map, walls);
            var pf = new PathfindingFacade(terrain);
            var sys = new MovementSystem(pf, em, terrain);

            var units = new List<Unit>();
            for (int i = 0; i < 4; i++)
            {
                var u = Spawn(em, new IsoCoord(4 + i, 4));
                OrderTo(u, new IsoCoord(12, 8 + i % 2), pf);
                units.Add(u);
            }

            bool allArrived = Simulate(sys, em,
                () =>
                {
                    foreach (var u in units)
                        if (u.IsMoving || TileOf(u).X < 9)
                            return false;
                    return true;
                },
                maxSteps: 8000, perFrame: () => AssertFromTilesUnique(em));

            Assert.True(allArrived, "Group failed to cross the bottleneck");
        }
    }
}
