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
        private int _playerFaction;

        /// <summary>Player faction index for enemy/friendly detection.</summary>
        public int PlayerFaction
        {
            get => _playerFaction;
            set => _playerFaction = value;
        }

        /// <summary>
        /// Process a right-click into a command.
        /// Right-click enemy → AttackCommand, friendly/ground → MoveCommand.
        /// </summary>
        public Command? ProcessRightClick(Vector2 worldPos,
            EntityManager entities, SelectionSystem selection)
        {
            // Check if clicking on an entity
            var clicked = entities.QueryPoint(worldPos);

            if (clicked != null && clicked.IsAlive)
            {
                if (clicked.Faction != _playerFaction)
                {
                    // Enemy → attack
                    return new AttackCommand(clicked.Id, clicked.WorldPosition);
                }
                else
                {
                    // Friendly → move to its position
                    return new MoveCommand(clicked.WorldPosition);
                }
            }
            else
            {
                // Empty ground → move
                return new MoveCommand(worldPos);
            }
        }

        /// <summary>Execute pending commands. Called once per frame.</summary>
        public void ExecuteCommands(EntityManager entities, TileMap map)
        {
            // Commands are executed immediately in Phase 1-2 (no command queue).
            // This method exists for Phase 3 expansion (command queues, build queues).
        }
    }
}
