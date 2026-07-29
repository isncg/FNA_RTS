using System;
using System.Collections.Generic;
using System.Numerics;

namespace FNARTS.Core.Movement
{
    /// <summary>
    /// Deprecated — use SteeringBehaviors.Separation instead.
    /// Kept for backward compat with existing test code during migration.
    /// </summary>
    [Obsolete("Use SteeringBehaviors.Separation instead.")]
    public static class SeparationBehavior
    {
        public const float SEPARATION_RADIUS = 44f;

        [Obsolete("Use SteeringBehaviors.Separation instead.")]
        public static Vector2 Compute(Vector2 self, IEnumerable<Vector2> others)
        {
            Vector2 separation = Vector2.Zero;
            int count = 0;

            foreach (var other in others)
            {
                float dist = Vector2.Distance(self, other);
                if (dist >= SEPARATION_RADIUS || dist < 0.01f)
                    continue;

                Vector2 away = Vector2.Normalize(self - other) / dist;
                separation += away;
                count++;
            }

            if (count == 0)
                return Vector2.Zero;

            separation /= count;
            return separation * SEPARATION_RADIUS * 0.8f;
        }
    }
}
