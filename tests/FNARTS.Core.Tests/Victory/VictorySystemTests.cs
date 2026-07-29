using Xunit;

namespace FNARTS.Core.Tests
{
    public class VictorySystemTests
    {
        private static Unit MakeUnit(float x, float y, int faction = 0)
        {
            var def = new UnitDef { Id = "test", MoveSpeed = 100f, HP = 100 };
            return new Unit(def) { WorldPosition = new System.Numerics.Vector2(x, y), Faction = faction };
        }

        [Fact]
        public void BothSidesAlive_ReturnsOngoing()
        {
            var mgr = new EntityManager();
            mgr.AddEntity(MakeUnit(0, 0, faction: 0));
            mgr.AddEntity(MakeUnit(10, 10, faction: 1));

            var sys = new VictorySystem();
            var result = sys.CheckVictory(mgr, 0);

            Assert.Equal(VictoryState.Ongoing, result);
        }

        [Fact]
        public void OnlyPlayerAlive_ReturnsPlayerWon()
        {
            var mgr = new EntityManager();
            mgr.AddEntity(MakeUnit(0, 0, faction: 0));
            // No enemy entities

            var sys = new VictorySystem();
            var result = sys.CheckVictory(mgr, 0);

            Assert.Equal(VictoryState.PlayerWon, result);
        }

        [Fact]
        public void OnlyEnemyAlive_ReturnsPlayerLost()
        {
            var mgr = new EntityManager();
            mgr.AddEntity(MakeUnit(10, 10, faction: 1));
            // No player entities

            var sys = new VictorySystem();
            var result = sys.CheckVictory(mgr, 0);

            Assert.Equal(VictoryState.PlayerLost, result);
        }

        [Fact]
        public void NoEntities_ReturnsPlayerLost()
        {
            var mgr = new EntityManager();

            var sys = new VictorySystem();
            var result = sys.CheckVictory(mgr, 0);

            Assert.Equal(VictoryState.PlayerLost, result);
        }

        [Fact]
        public void AllDead_ReturnsPlayerLost()
        {
            var mgr = new EntityManager();
            var unit = MakeUnit(0, 0, faction: 0);
            unit.IsAlive = false;
            mgr.AddEntity(unit);

            var sys = new VictorySystem();
            var result = sys.CheckVictory(mgr, 0);

            Assert.Equal(VictoryState.PlayerLost, result);
        }
    }
}
