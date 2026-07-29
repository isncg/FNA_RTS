using System.Numerics;
using Xunit;
using FNARTS.Core.Movement;
using FNARTS.Core.Pathfinding;
using FNARTS.Core.Tests.Pathfinding;

namespace FNARTS.Core.Tests.Movement
{
    /// <summary>
    /// RA1/RA2-style group movement: slot offsets are a snapshot of each
    /// unit's tile position relative to the group's bounding-box centre,
    /// taken at order time. No shape templates, no in-march maintenance.
    /// </summary>
    public class GroupMovementTests
    {
        private static Unit MakeUnit(Vector2 pos, float speed = 100f)
        {
            return new Unit(new UnitDef { MoveSpeed = speed })
            {
                WorldPosition = pos,
                IsAlive = true
            };
        }

        private static Unit MakeUnitOnTile(int x, int y, float speed = 100f)
            => MakeUnit(CoordUtil.IsoToWorldCenter(new IsoCoord(x, y)), speed);

        [Fact]
        public void Constructor_SelectsLeaderAsClosestToAnchor()
        {
            // Tiles (10,10), (15,10), (20,10) → anchor (15,10)
            var units = new[]
            {
                MakeUnitOnTile(10, 10),
                MakeUnitOnTile(15, 10),
                MakeUnitOnTile(20, 10),
            };
            var gm = new GroupMovement(units);

            Assert.Equal(1, gm.LeaderIndex);
        }

        [Fact]
        public void Constructor_OffsetsAreRelativeToBboxCentre()
        {
            // Tiles (10,10), (11,10), (10,12)
            // bbox centre = ((10+11)/2, (10+12)/2) = (10,11)
            var units = new[]
            {
                MakeUnitOnTile(10, 10),
                MakeUnitOnTile(11, 10),
                MakeUnitOnTile(10, 12),
            };
            var gm = new GroupMovement(units);

            Assert.Equal(new IsoCoord(0, -1), gm.SlotOffsets[0]);
            Assert.Equal(new IsoCoord(1, -1), gm.SlotOffsets[1]);
            Assert.Equal(new IsoCoord(0, 1), gm.SlotOffsets[2]);

            // Distinct offsets ⇒ one unit per tile at the destination
            var seen = new System.Collections.Generic.HashSet<IsoCoord>(gm.SlotOffsets);
            Assert.Equal(units.Length, seen.Count);
        }

        [Fact]
        public void Constructor_SpreadOutGroup_KeepsFullSpread()
        {
            // RA2 behaviour: a scattered group stays scattered.
            // 20 tiles apart → offsets ±10, no clamping of the spread.
            var units = new[]
            {
                MakeUnitOnTile(10, 10),
                MakeUnitOnTile(30, 10),
            };
            var gm = new GroupMovement(units);

            Assert.Equal(new IsoCoord(-10, 0), gm.SlotOffsets[0]);
            Assert.Equal(new IsoCoord(10, 0), gm.SlotOffsets[1]);
        }

        [Fact]
        public void Update_AdvancesFormationCenter_AlongPath()
        {
            var units = new[]
            {
                MakeUnit(new Vector2(0, 0)),
                MakeUnit(new Vector2(40, 0)),
            };
            var gm = new GroupMovement(units);

            var initialCenter = gm.FormationCenter;
            gm.FormationPath = new System.Collections.Generic.List<IsoCoord>
            {
                new(5, 0), new(10, 0)
            };

            gm.Update(1f, 100f); // 1 second at 100 px/s
            Assert.NotEqual(initialCenter, gm.FormationCenter);
        }

        [Fact]
        public void Update_ReassignsLeaderToFurthestAhead()
        {
            var units = new[]
            {
                MakeUnit(new Vector2(0, 0), 80f),   // slow, behind
                MakeUnit(new Vector2(80, 0), 160f), // fast, ahead
            };
            var gm = new GroupMovement(units);

            // Set MoveTargets — fast unit is much closer to destination
            units[0].MoveTarget = new Vector2(200, 0); // dist ≈ 200
            units[1].MoveTarget = new Vector2(100, 0); // dist ≈ 20

            gm.Update(1f / 60f, 100f);

            // Fast unit (index 1) should now be leader
            Assert.Equal(1, gm.LeaderIndex);
        }

        [Fact]
        public void Update_DoesNotOverwriteUnitMoveTargets()
        {
            var units = new[]
            {
                MakeUnit(new Vector2(0, 0)),
                MakeUnit(new Vector2(40, 0)),
            };
            var gm = new GroupMovement(units);

            // Set explicit MoveTargets on units (simulating IssueMoveOrder)
            units[0].MoveTarget = new Vector2(100, 0);
            units[1].MoveTarget = new Vector2(200, 0);

            gm.FormationPath = new System.Collections.Generic.List<IsoCoord>
            {
                new(5, 0), new(10, 0)
            };

            gm.Update(1f / 60f, 100f);

            // MoveTargets should NOT be overwritten — units navigate independently
            Assert.Equal(new Vector2(100, 0), units[0].MoveTarget);
            Assert.Equal(new Vector2(200, 0), units[1].MoveTarget);
        }

        [Fact]
        public void AllArrived_True_WhenAllStopped()
        {
            var units = new[]
            {
                MakeUnit(new Vector2(0, 0)),
                MakeUnit(new Vector2(40, 0)),
            };
            var gm = new GroupMovement(units);

            // Units have no orders — they should be considered arrived
            foreach (var u in units) { u.MoveTarget = null; u.Path = null; u.Velocity = Vector2.Zero; }
            Assert.True(gm.AllArrived);
        }

        [Fact]
        public void AllArrived_False_WhenSomeMoving()
        {
            var units = new[]
            {
                MakeUnit(new Vector2(0, 0)),
                MakeUnit(new Vector2(40, 0)),
            };
            var gm = new GroupMovement(units);

            units[0].MoveTarget = new Vector2(100, 0); // still moving
            Assert.False(gm.AllArrived);
        }

        [Fact]
        public void IssueMoveOrder_SnapsDestinationsToDistinctTileCentres()
        {
            // Units start on tiles near (10,10)
            var units = new[]
            {
                MakeUnitOnTile(10, 10),
                MakeUnitOnTile(11, 10),
                MakeUnitOnTile(10, 11),
                MakeUnitOnTile(11, 11),
            };
            var gm = new GroupMovement(units);

            var map = PathTestUtil.OpenMap(60, 60);
            var pathfinder = new PathfindingFacade(PathTestUtil.TerrainFor(map));

            // Click with jitter inside tile (30,30) — must snap to the tile
            var clickPos = CoordUtil.IsoToWorldCenter(new IsoCoord(30, 30))
                         + new Vector2(7f, -3f);
            gm.IssueMoveOrder(clickPos, pathfinder);

            Assert.Equal(new IsoCoord(30, 30), gm.TargetTile);
            Assert.Equal(CoordUtil.IsoToWorldCenter(new IsoCoord(30, 30)),
                gm.TargetPosition);

            var destTiles = new System.Collections.Generic.HashSet<IsoCoord>();
            for (int i = 0; i < units.Length; i++)
            {
                var mt = units[i].MoveTarget;
                Assert.True(mt.HasValue, $"unit {i} got no destination");

                // Destination is exactly the centre of its slot tile
                var expectedTile = gm.TargetTile + gm.SlotOffsets[i];
                Assert.Equal(expectedTile, CoordUtil.WorldToIso(mt.Value));
                Assert.Equal(CoordUtil.IsoToWorldCenter(expectedTile), mt.Value);

                // One unit per tile (C&C2 style)
                Assert.True(destTiles.Add(expectedTile),
                    $"unit {i} shares dest tile {expectedTile}");

                // Path ends on the destination tile
                Assert.NotNull(units[i].Path);
                Assert.Equal(expectedTile, units[i].Path![^1]);
            }
        }

        [Fact]
        public void IssueMoveOrder_PreservesRelativeLayout_AtDestination()
        {
            // Line layout (10,10), (11,10), (12,10) moved to (30,30):
            // arrival tiles must reproduce the exact same shape.
            var units = new[]
            {
                MakeUnitOnTile(10, 10),
                MakeUnitOnTile(11, 10),
                MakeUnitOnTile(12, 10),
            };
            var gm = new GroupMovement(units);

            var map = PathTestUtil.OpenMap(60, 60);
            var pathfinder = new PathfindingFacade(PathTestUtil.TerrainFor(map));
            gm.IssueMoveOrder(CoordUtil.IsoToWorldCenter(new IsoCoord(30, 30)),
                pathfinder);

            var dest = new IsoCoord[units.Length];
            for (int i = 0; i < units.Length; i++)
                dest[i] = CoordUtil.WorldToIso(units[i].MoveTarget!.Value);

            // Pairwise deltas identical to the starting layout
            Assert.Equal(dest[1] - dest[0], new IsoCoord(1, 0));
            Assert.Equal(dest[2] - dest[1], new IsoCoord(1, 0));
        }

        [Fact]
        public void IssueMoveOrder_ClampsSlotsAtMapEdge()
        {
            // Group with offsets ±3 ordered against the map corner —
            // RA1 Bound() compresses out-of-map slots onto the border.
            var units = new[]
            {
                MakeUnitOnTile(30, 30),
                MakeUnitOnTile(33, 30),
                MakeUnitOnTile(27, 30),
                MakeUnitOnTile(30, 33),
                MakeUnitOnTile(30, 27),
            };
            var gm = new GroupMovement(units);   // offsets ±3 in both axes

            var map = PathTestUtil.OpenMap(60, 60);
            var pathfinder = new PathfindingFacade(PathTestUtil.TerrainFor(map));

            // Click at the corner tile (0,0) — several slots fall off-map
            gm.IssueMoveOrder(CoordUtil.IsoToWorldCenter(new IsoCoord(0, 0)),
                pathfinder);

            for (int i = 0; i < units.Length; i++)
            {
                var mt = units[i].MoveTarget;
                Assert.True(mt.HasValue, $"unit {i} got no destination");
                var tile = CoordUtil.WorldToIso(mt.Value);
                Assert.InRange(tile.X, 0, 59);
                Assert.InRange(tile.Y, 0, 59);
            }
        }

        [Fact]
        public void IssueMoveOrder_BlockedSlotTile_FallsBackNearby()
        {
            var units = new[] { MakeUnitOnTile(10, 10) };
            var gm = new GroupMovement(units);

            var map = PathTestUtil.OpenMap(60, 60);
            // Block the exact target tile — unit must land on a neighbour
            var blocked = new System.Collections.Generic.HashSet<IsoCoord>
            {
                new(30, 30)
            };
            var pathfinder = new PathfindingFacade(
                PathTestUtil.TerrainFor(map, blocked));

            gm.IssueMoveOrder(CoordUtil.IsoToWorldCenter(new IsoCoord(30, 30)),
                pathfinder);

            var dest = CoordUtil.WorldToIso(units[0].MoveTarget!.Value);
            Assert.NotEqual(new IsoCoord(30, 30), dest);
            Assert.True(System.Math.Abs(dest.X - 30) <= 3
                     && System.Math.Abs(dest.Y - 30) <= 3,
                $"fallback tile {dest} outside search rings");
        }
    }
}
