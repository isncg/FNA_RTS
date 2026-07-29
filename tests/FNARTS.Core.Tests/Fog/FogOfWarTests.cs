using System.Numerics;
using Xunit;
using FNARTS.Core.Fog;

namespace FNARTS.Core.Tests.Fog
{
    public class FogOfWarTests
    {
        private static Unit MakeUnit(float x, float y, int faction = 0, int vision = 4)
        {
            var def = new UnitDef
            {
                Id = "test_unit", MoveSpeed = 100f, HP = 100,
                VisionRange = vision,
            };
            return new Unit(def)
            {
                WorldPosition = new Vector2(x, y),
                Faction = faction,
            };
        }

        private static Building MakeBuilding(
            int gx, int gy, int sizeX, int sizeY, int faction = 0, int vision = 3)
        {
            var def = new BuildingDef
            {
                Id = "test_bld", SizeX = sizeX, SizeY = sizeY,
                HP = 300, VisionRange = vision,
            };
            return new Building(def, new IsoCoord(gx, gy)) { Faction = faction };
        }

        [Fact]
        public void NewFog_AllCellsUnexplored()
        {
            var fog = new FogOfWar(10, 10);
            for (int x = 0; x < 10; x++)
            for (int y = 0; y < 10; y++)
                Assert.Equal(FogCell.Unexplored, fog[x, y]);
        }

        [Fact]
        public void Update_SingleUnit_RevealsVisionDiamond()
        {
            var fog = new FogOfWar(20, 20);
            var mgr = new EntityManager();
            var unit = MakeUnit(0, 0, faction: 0, vision: 2);
            // Place unit at a known world position that maps to grid (5,5)
            unit.WorldPosition = CoordUtil.IsoToWorldCenter(new IsoCoord(5, 5));
            mgr.AddEntity(unit);

            fog.Update(mgr, 0);

            // Vision range 2 should reveal a diamond: centre (5,5),
            // (3,5) through (7,5) on X axis, etc.
            Assert.Equal(FogCell.Visible, fog[5, 5]);   // centre
            Assert.Equal(FogCell.Visible, fog[3, 5]);   // left edge
            Assert.Equal(FogCell.Visible, fog[7, 5]);   // right edge
            Assert.Equal(FogCell.Visible, fog[5, 3]);   // top edge
            Assert.Equal(FogCell.Visible, fog[5, 7]);   // bottom edge

            // Outside vision range
            Assert.Equal(FogCell.Unexplored, fog[2, 5]);
            Assert.Equal(FogCell.Unexplored, fog[8, 5]);
            Assert.Equal(FogCell.Unexplored, fog[5, 1]);
        }

        [Fact]
        public void Update_DegradesVisibleToExplored_WhenUnitMovesAway()
        {
            var fog = new FogOfWar(20, 20);
            var mgr = new EntityManager();
            var unit = MakeUnit(0, 0, faction: 0, vision: 2);
            unit.WorldPosition = CoordUtil.IsoToWorldCenter(new IsoCoord(5, 5));
            mgr.AddEntity(unit);

            // First update: reveal
            fog.Update(mgr, 0);
            Assert.Equal(FogCell.Visible, fog[5, 5]);

            // Move unit far away
            unit.WorldPosition = CoordUtil.IsoToWorldCenter(new IsoCoord(15, 15));

            // Second update: old area should degrade to Explored, new area should be Visible
            fog.Update(mgr, 0);
            Assert.Equal(FogCell.Explored, fog[5, 5]);     // degraded
            Assert.Equal(FogCell.Visible, fog[15, 15]);    // newly revealed
        }

        [Fact]
        public void Update_DeadEntity_DoesNotProvideVision()
        {
            var fog = new FogOfWar(20, 20);
            var mgr = new EntityManager();
            var unit = MakeUnit(0, 0, faction: 0, vision: 2);
            unit.WorldPosition = CoordUtil.IsoToWorldCenter(new IsoCoord(5, 5));
            unit.IsAlive = false;
            mgr.AddEntity(unit);

            fog.Update(mgr, 0);

            // Dead unit should not reveal anything
            Assert.Equal(FogCell.Unexplored, fog[5, 5]);
        }

        [Fact]
        public void Update_WrongFaction_DoesNotProvideVision()
        {
            var fog = new FogOfWar(20, 20);
            var mgr = new EntityManager();
            var enemyUnit = MakeUnit(0, 0, faction: 1, vision: 5);
            enemyUnit.WorldPosition = CoordUtil.IsoToWorldCenter(new IsoCoord(10, 10));
            mgr.AddEntity(enemyUnit);

            fog.Update(mgr, 0); // player faction = 0, enemy = faction 1

            Assert.Equal(FogCell.Unexplored, fog[10, 10]);
        }

        [Fact]
        public void Update_Building_ProvidesStaticVision()
        {
            var fog = new FogOfWar(20, 20);
            var mgr = new EntityManager();
            // Building at (4,4) size 2×2 → vision centre at (5,5).
            var bld = MakeBuilding(4, 4, 2, 2, faction: 0, vision: 1);
            mgr.AddEntity(bld);

            fog.Update(mgr, 0);

            // Centre and immediate neighbours (Manhattan ≤ 1 from (5,5))
            Assert.Equal(FogCell.Visible, fog[5, 5]);   // centre
            Assert.Equal(FogCell.Visible, fog[4, 5]);   // adjacent
            Assert.Equal(FogCell.Visible, fog[5, 4]);   // adjacent
            // Outside vision range
            Assert.Equal(FogCell.Unexplored, fog[3, 5]);
        }

        [Fact]
        public void Update_MultipleEntities_OverlappingVision()
        {
            var fog = new FogOfWar(20, 20);
            var mgr = new EntityManager();

            var u1 = MakeUnit(0, 0, faction: 0, vision: 1);
            u1.WorldPosition = CoordUtil.IsoToWorldCenter(new IsoCoord(3, 3));
            mgr.AddEntity(u1);

            var u2 = MakeUnit(0, 0, faction: 0, vision: 1);
            u2.WorldPosition = CoordUtil.IsoToWorldCenter(new IsoCoord(5, 5));
            mgr.AddEntity(u2);

            fog.Update(mgr, 0);

            // Both centres should be visible
            Assert.Equal(FogCell.Visible, fog[3, 3]);
            Assert.Equal(FogCell.Visible, fog[5, 5]);
            // Adjacent to (5,5) — visible
            Assert.Equal(FogCell.Visible, fog[4, 5]);
            // (4,4) is Manhattan 2 from (3,3) and 2 from (5,5) — outside range
            Assert.Equal(FogCell.Unexplored, fog[4, 4]);
        }

        [Fact]
        public void RevealAll_SetsAllCellsVisible()
        {
            var fog = new FogOfWar(10, 10);
            fog.RevealAll();

            for (int x = 0; x < 10; x++)
            for (int y = 0; y < 10; y++)
                Assert.Equal(FogCell.Visible, fog[x, y]);
        }

        [Fact]
        public void RevealRect_OnlyAffectsSpecifiedArea()
        {
            var fog = new FogOfWar(10, 10);
            fog.RevealRect(2, 2, 4, 4);

            // Inside rect: Visible
            for (int x = 2; x <= 4; x++)
            for (int y = 2; y <= 4; y++)
                Assert.Equal(FogCell.Visible, fog[x, y]);

            // Outside rect: still Unexplored
            Assert.Equal(FogCell.Unexplored, fog[1, 1]);
            Assert.Equal(FogCell.Unexplored, fog[5, 5]);
            Assert.Equal(FogCell.Unexplored, fog[0, 0]);
        }

        [Fact]
        public void OutOfBounds_ReturnsUnexplored()
        {
            var fog = new FogOfWar(10, 10);
            Assert.Equal(FogCell.Unexplored, fog[-1, 0]);
            Assert.Equal(FogCell.Unexplored, fog[0, -1]);
            Assert.Equal(FogCell.Unexplored, fog[10, 0]);
            Assert.Equal(FogCell.Unexplored, fog[0, 10]);
        }

        [Fact]
        public void ZeroVisionRange_RevealsNothing()
        {
            var fog = new FogOfWar(20, 20);
            var mgr = new EntityManager();
            var unit = MakeUnit(0, 0, faction: 0, vision: 0);
            unit.WorldPosition = CoordUtil.IsoToWorldCenter(new IsoCoord(5, 5));
            mgr.AddEntity(unit);

            fog.Update(mgr, 0);

            Assert.Equal(FogCell.Unexplored, fog[5, 5]);
        }
    }
}
