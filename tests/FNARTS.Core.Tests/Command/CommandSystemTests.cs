using System.Numerics;
using Xunit;

namespace FNARTS.Core.Tests.Command
{
    public class CommandSystemTests
    {
        private static Unit MakeUnit(float x, float y, int faction = 0)
        {
            var def = new UnitDef { Id = "test", MoveSpeed = 100f };
            return new Unit(def) { WorldPosition = new Vector2(x, y), Faction = faction };
        }

        [Fact]
        public void ProcessRightClick_EmptyWorld_ReturnsMoveToWorldPos()
        {
            var cmdSys = new FNARTS.Core.CommandSystem();
            var mgr = new FNARTS.Core.EntityManager();
            var sel = new FNARTS.Core.SelectionSystem();

            var target = new Vector2(150, 300);
            var cmd = cmdSys.ProcessRightClick(target, mgr, sel);

            Assert.NotNull(cmd);
            Assert.Equal(CommandType.Move, cmd.Type);
            var moveCmd = Assert.IsType<MoveCommand>(cmd);
            Assert.Equal(target, moveCmd.TargetWorldPosition);
        }

        [Fact]
        public void ProcessRightClick_OnFriendly_ReturnsMoveToEntity()
        {
            var cmdSys = new FNARTS.Core.CommandSystem();
            var mgr = new FNARTS.Core.EntityManager();
            var sel = new FNARTS.Core.SelectionSystem();
            var friendly = MakeUnit(200, 300, faction: 0); // same faction as player
            mgr.AddEntity(friendly);

            // Click on the friendly's position
            var target = new Vector2(200, 300);
            var cmd = cmdSys.ProcessRightClick(target, mgr, sel);

            Assert.NotNull(cmd);
            Assert.Equal(CommandType.Move, cmd.Type);
            var moveCmd = Assert.IsType<MoveCommand>(cmd);
            Assert.Equal(friendly.WorldPosition, moveCmd.TargetWorldPosition);
        }

        [Fact]
        public void ProcessRightClick_OnEnemy_ReturnsAttackCommand()
        {
            var cmdSys = new FNARTS.Core.CommandSystem();
            var mgr = new FNARTS.Core.EntityManager();
            var sel = new FNARTS.Core.SelectionSystem();
            var enemy = MakeUnit(200, 300, faction: 1); // different faction from player (0)
            mgr.AddEntity(enemy);

            var target = new Vector2(200, 300);
            var cmd = cmdSys.ProcessRightClick(target, mgr, sel);

            Assert.NotNull(cmd);
            Assert.Equal(CommandType.Attack, cmd.Type);
            var atkCmd = Assert.IsType<AttackCommand>(cmd);
            Assert.Equal(enemy.Id, atkCmd.TargetEntityId);
            Assert.Equal(enemy.WorldPosition, atkCmd.TargetWorldPosition);
        }

        [Fact]
        public void ProcessRightClick_OnDeadEnemy_ReturnsMoveCommand()
        {
            var cmdSys = new FNARTS.Core.CommandSystem();
            var mgr = new FNARTS.Core.EntityManager();
            var sel = new FNARTS.Core.SelectionSystem();
            var deadEnemy = MakeUnit(200, 300, faction: 1);
            deadEnemy.IsAlive = false;
            mgr.AddEntity(deadEnemy);

            var target = new Vector2(200, 300);
            var cmd = cmdSys.ProcessRightClick(target, mgr, sel);

            // Dead enemy should be treated like empty ground → move command
            Assert.NotNull(cmd);
            Assert.Equal(CommandType.Move, cmd.Type);
            var moveCmd = Assert.IsType<MoveCommand>(cmd);
            Assert.Equal(target, moveCmd.TargetWorldPosition);
        }

        [Fact]
        public void ExecuteCommands_DoesNotThrow()
        {
            var cmdSys = new FNARTS.Core.CommandSystem();
            var mgr = new FNARTS.Core.EntityManager();
            var map = new TileMap(10, 10);
            cmdSys.ExecuteCommands(mgr, map);
            // Should not throw (no-op in Phase 1-2)
        }
    }
}
