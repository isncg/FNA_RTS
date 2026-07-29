using System;
using System.Numerics;

namespace FNARTS.Core.Movement
{
    /// <summary>
    /// Pure static steering behavior functions for SC1-style unit movement.
    /// Each returns a desired-velocity vector; the caller computes
    ///   steering = desired - currentVelocity
    /// and applies it as a force.
    /// </summary>
    public static class SteeringBehaviors
    {
        /// <summary>
        /// Full-speed seek toward a target. Returns desired velocity
        /// (direction * maxSpeed).
        /// </summary>
        public static Vector2 Seek(Vector2 position, Vector2 target,
            Vector2 currentVelocity, float maxSpeed)
        {
            Vector2 desired = target - position;
            float lenSq = desired.LengthSquared();
            if (lenSq < 0.01f)
                return Vector2.Zero;
            return desired / MathF.Sqrt(lenSq) * maxSpeed;
        }

        /// <summary>
        /// Arrive: decelerate smoothly using the unit's actual deceleration.
        /// Uses the constant-acceleration braking formula d = v²/(2a),
        /// so desired speed = sqrt(2 * decel * distance).
        /// This avoids the jerkiness of a linear speed ramp.
        /// </summary>
        public static Vector2 Arrive(Vector2 position, Vector2 target,
            Vector2 currentVelocity, float maxSpeed, float deceleration,
            float slowingRadius)
        {
            Vector2 toTarget = target - position;
            float distance = toTarget.Length();

            if (distance < 0.5f)
                return Vector2.Zero;

            // Physics-based: speed needed to stop exactly at target
            float desiredSpeed = MathF.Sqrt(2f * deceleration * distance);
            float speed = MathF.Min(desiredSpeed, maxSpeed);

            return (toTarget / distance) * speed;
        }

        /// <summary>
        /// Separation: steer away from nearby entities to prevent overlap.
        /// Returns a steering-force vector (not a desired-velocity).
        /// </summary>
        public static Vector2 Separation(Vector2 position,
            Vector2[] neighborPositions, float radius, float maxForce)
        {
            Vector2 force = Vector2.Zero;
            int count = 0;

            foreach (var other in neighborPositions)
            {
                float dist = Vector2.Distance(position, other);
                if (dist >= radius || dist < 0.01f) continue;

                // Linear falloff: closer = stronger repulsion
                Vector2 away = Vector2.Normalize(position - other);
                float strength = (radius - dist) / radius * maxForce;
                force += away * strength;
                count++;
            }

            if (count == 0) return Vector2.Zero;
            return force / count;
        }

        /// <summary>
        /// Cohesion: steer toward the average position of neighbors.
        /// Returns a steering-force vector.
        /// </summary>
        public static Vector2 Cohesion(Vector2 position,
            Vector2[] neighborPositions, Vector2 currentVelocity,
            float maxSpeed, float maxForce)
        {
            if (neighborPositions.Length == 0) return Vector2.Zero;

            Vector2 center = Vector2.Zero;
            foreach (var n in neighborPositions) center += n;
            center /= neighborPositions.Length;

            Vector2 desired = center - position;
            if (desired.LengthSquared() < 1f) return Vector2.Zero;

            desired = Vector2.Normalize(desired) * maxSpeed;
            Vector2 steer = desired - currentVelocity;
            return ClampForce(steer, maxForce);
        }

        /// <summary>
        /// Alignment: match the average velocity of neighbors.
        /// Returns a steering-force vector.
        /// </summary>
        public static Vector2 Alignment(Vector2 currentVelocity,
            Vector2[] neighborVelocities, float maxSpeed, float maxForce)
        {
            if (neighborVelocities.Length == 0) return Vector2.Zero;

            Vector2 avgVelocity = Vector2.Zero;
            foreach (var v in neighborVelocities) avgVelocity += v;
            avgVelocity /= neighborVelocities.Length;

            Vector2 desired = Vector2.Normalize(avgVelocity) * maxSpeed;
            Vector2 steer = desired - currentVelocity;
            return ClampForce(steer, maxForce);
        }

        /// <summary>
        /// Clamp a steering-force vector to maxForce magnitude.
        /// </summary>
        public static Vector2 ClampForce(Vector2 force, float maxForce)
        {
            float lenSq = force.LengthSquared();
            if (lenSq > maxForce * maxForce)
                return force / MathF.Sqrt(lenSq) * maxForce;
            return force;
        }

        /// <summary>
        /// Clamp a velocity vector to maxSpeed magnitude.
        /// </summary>
        public static Vector2 ClampMagnitude(Vector2 v, float max)
        {
            float lenSq = v.LengthSquared();
            if (lenSq > max * max)
                return v / MathF.Sqrt(lenSq) * max;
            return v;
        }
    }
}
