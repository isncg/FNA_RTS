using System;
using System.Numerics;

namespace FNARTS.Core
{
    /// <summary>
    /// A mobile entity that can receive move commands.
    /// </summary>
    public class Unit : Entity
    {
        public UnitDef Definition { get; }
        public float MoveSpeed { get; }
        public Vector2? MoveTarget { get; set; }

        public Unit(UnitDef definition)
        {
            Definition = definition;
            MoveSpeed = definition.MoveSpeed;
        }

        /// <summary>Move toward MoveTarget at MoveSpeed pixels/sec. Call each frame.</summary>
        public void Update(float dt)
        {
            if (!MoveTarget.HasValue || !IsAlive) return;

            Vector2 target = MoveTarget.Value;
            Vector2 toTarget = target - WorldPosition;
            float distance = toTarget.Length();

            if (distance < 2f)
            {
                // Arrived
                WorldPosition = target;
                MoveTarget = null;
                return;
            }

            float step = MoveSpeed * dt;
            if (step >= distance)
            {
                WorldPosition = target;
                MoveTarget = null;
            }
            else
            {
                WorldPosition += Vector2.Normalize(toTarget) * step;
            }
        }
    }
}
