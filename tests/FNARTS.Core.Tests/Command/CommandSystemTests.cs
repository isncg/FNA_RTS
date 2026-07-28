using System.Numerics;
using Xunit;

namespace FNARTS.Core.Tests.Command
{
    public class CommandSystemTests
    {
        private static Unit MakeUnit(float x, float y)
        {
            var def = new UnitDef { Id = "test", MoveSpeed = 100f };
            return new Unit(def) { WorldPosition = new Vector2(x, y) };
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
            Assert.Equal(target, cmd.TargetWorldPosition);
        }

        [Fact]
        public void ProcessRightClick_OnEntity_ReturnsMoveToEntity()
        {
            var cmdSys = new FNARTS.Core.CommandSystem();
            var mgr = new FNARTS.Core.EntityManager();
            var sel = new FNARTS.Core.SelectionSystem();
            var enemy = MakeUnit(200, 300);
            mgr.AddEntity(enemy);

            // Click on the enemy's position
            var target = new Vector2(200, 300);
            var cmd = cmdSys.ProcessRightClick(target, mgr, sel);

            Assert.NotNull(cmd);
            // Should target the entity's position
            Assert.Equal(enemy.WorldPosition, cmd.TargetWorldPosition);
        }

        [Fact]
        public void ExecuteCommands_DoesNotThrow()
        {
            var cmdSys = new FNARTS.Core.CommandSystem();
            var mgr = new FNARTS.Core.EntityManager();
            var map = new TileMap(10, 10);
            cmdSys.ExecuteCommands(mgr, map);
            // Should not throw (no-op in Phase 1)
        }
    }
}
