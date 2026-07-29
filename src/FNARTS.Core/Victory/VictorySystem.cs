using System.Linq;

namespace FNARTS.Core
{
    public enum VictoryState
    {
        Ongoing,
        PlayerWon,
        PlayerLost,
    }

    /// <summary>
    /// Elimination victory check.  Player wins when all enemy entities are
    /// dead; player loses when all friendly entities are dead.
    /// </summary>
    public class VictorySystem
    {
        /// <summary>
        /// Check victory conditions for a given player faction.
        /// Returns Ongoing while both sides have at least one alive entity.
        /// </summary>
        public VictoryState CheckVictory(EntityManager entities, int playerFaction)
        {
            bool playerAlive = entities.AllEntities.Any(
                e => e.IsAlive && e.Faction == playerFaction);
            bool enemyAlive = entities.AllEntities.Any(
                e => e.IsAlive && e.Faction != playerFaction);

            if (!playerAlive && !enemyAlive) return VictoryState.PlayerLost;
            if (!playerAlive) return VictoryState.PlayerLost;
            if (!enemyAlive) return VictoryState.PlayerWon;
            return VictoryState.Ongoing;
        }
    }
}
