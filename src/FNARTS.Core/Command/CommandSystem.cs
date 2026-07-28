using System;
using System.Collections.Generic;
using System.Numerics;

namespace FNARTS.Core
{
    /// <summary>
    /// Processes player input into commands and executes them against entities.
    /// Core-only: no input hardware dependency. Pass transformed coords in.
    /// </summary>
    public class CommandSystem
    {
        /// <summary>
        /// Generate a move command from a right-click.
        /// If clicking on a unit/building, move to its position.
        /// Otherwise move to the clicked world position.
        /// </summary>
        public MoveCommand ProcessRightClick(Vector2 worldPos,
            EntityManager entities, SelectionSystem selection)
        {
            // Check if clicking on an entity
            var clicked = entities.QueryPoint(worldPos);
            Vector2 target = clicked != null ? clicked.WorldPosition : worldPos;

            return new MoveCommand(target);
        }

        /// <summary>Execute pending commands. Called once per frame.</summary>
        public void ExecuteCommands(EntityManager entities, TileMap map)
        {
            // Commands are executed immediately in Phase 1 (no command queue)
            // The command is applied directly to selected units in ProcessRightClick.
            // This method exists for Phase 2 expansion (build queues, attack commands).
        }
    }
}
