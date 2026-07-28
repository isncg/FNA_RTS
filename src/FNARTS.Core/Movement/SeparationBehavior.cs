using System.Collections.Generic;
using System.Numerics;

namespace FNARTS.Core.Movement
{
    /// <summary>
    /// Simple steering-based local separation force.
    /// Prevents units from overlapping without full RVO2.
    /// </summary>
    public static class SeparationBehavior
    {
        /// <summary>Distance within which repulsion activates (world pixels).
        /// ~2/3 tile width — units maintain roughly one-tile spacing during movement.</summary>
        public const float SEPARATION_RADIUS = 44f;

        /// <summary>
        /// Compute the separation displacement for one unit given the
        /// positions of all nearby units.
        /// </summary>
        /// <param name="self">This unit's world position.</param>
        /// <param name="others">World positions of other units.</param>
        /// <returns>A world-space displacement to add to the unit's position.</returns>
        public static Vector2 Compute(Vector2 self, IEnumerable<Vector2> others)
        {
            Vector2 separation = Vector2.Zero;
            int count = 0;

            foreach (var other in others)
            {
                float dist = Vector2.Distance(self, other);
                if (dist >= SEPARATION_RADIUS || dist < 0.01f)
                    continue;

                // Repulsion direction: away from the other unit.
                // Magnitude is inversely proportional to distance (closer = stronger).
                Vector2 away = Vector2.Normalize(self - other) / dist;
                separation += away;
                count++;
            }

            if (count == 0)
                return Vector2.Zero;

            separation /= count;                           // average
            return separation * SEPARATION_RADIUS * 0.8f;  // scale to world pixels
        }
    }
}
