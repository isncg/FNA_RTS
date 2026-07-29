using System;
using System.Collections.Generic;
using System.Numerics;

namespace FNARTS.Core
{
    /// <summary>
    /// A mobile entity that can receive move and attack commands.
    /// Phase 2: waypoint following (A* pathfinding) replaces straight-line movement.
    /// </summary>
    public class Unit : Entity
    {
        public UnitDef Definition { get; }
        public float MoveSpeed { get; }

        // ---- movement ----
        public Vector2? MoveTarget { get; set; }           // final destination (world)
        public List<IsoCoord>? Path { get; set; }          // A* waypoints (grid coords)
        public int PathIndex { get; set; }                  // current waypoint index

        // ---- combat (Phase 2 step 3 wiring, fields defined now) ----
        public int CurrentHP { get; set; }
        public int MaxHP => Definition.HP;
        public int AttackDamage => Definition.AttackDamage;
        public float AttackRange => Definition.AttackRange;
        public float AttackCooldownTimer { get; set; }
        public uint? AttackTargetId { get; set; }
        public int Armor => Definition.Armor;
        public int HealAmount => Definition.HealAmount;
        public float HealRange => Definition.HealRange;
        public bool IsAircraft => Definition.IsAircraft;
        public bool CanHitAir => Definition.CanHitAir;

        // ---- stuck detection ----
        /// <summary>Seconds spent without meaningful progress while moving.</summary>
        public float StuckTimer { get; set; }
        /// <summary>World position at last stuck-timer reset.</summary>
        public System.Numerics.Vector2 LastStuckCheckPos { get; set; }
        /// <summary>Number of path recomputations for the current move order.</summary>
        public int StuckRecomputeCount { get; set; }

        // ---- state queries ----
        public bool IsAttacking => AttackTargetId.HasValue;
        public bool CanAttack => AttackCooldownTimer <= 0f;
        public bool IsMoving => (Path != null && PathIndex < Path.Count)
                                || MoveTarget.HasValue;

        public Unit(UnitDef definition)
        {
            Definition = definition;
            MoveSpeed = definition.MoveSpeed;
            CurrentHP = definition.HP;
            AttackCooldownTimer = 0f;
        }

        /// <summary>
        /// Per-frame update: follow waypoints, then fallback to straight-line
        /// movement.  Also ticks down the attack cooldown timer.
        /// </summary>
        public void Update(float dt)
        {
            if (!IsAlive) return;

            // Tick cooldown
            if (AttackCooldownTimer > 0f)
                AttackCooldownTimer -= dt;

            // Waypoint following
            if (Path != null && PathIndex < Path.Count)
            {
                // On the final waypoint, steer directly to MoveTarget so the
                // unit doesn't detour through the tile centre of the target cell.
                bool isFinal = PathIndex == Path.Count - 1 && MoveTarget.HasValue;
                Vector2 target = isFinal
                    ? MoveTarget.Value
                    : CoordUtil.IsoToWorldCenter(Path[PathIndex]);

                if (!MoveToward(target, dt))
                {
                    if (isFinal)
                    {
                        // Arrived at the actual destination
                        MoveTarget = null;
                        Path = null;
                        PathIndex = 0;
                    }
                    else
                    {
                        PathIndex++;   // reached intermediate waypoint, advance
                    }
                }
            }
            else if (MoveTarget.HasValue)
            {
                // Fallback: straight-line movement (no path / Phase 1 compat)
                if (!MoveToward(MoveTarget.Value, dt))
                {
                    MoveTarget = null;
                    Path = null;
                }
            }
        }

        /// <summary>
        /// Move toward a world-space target for dt seconds.
        /// Returns true if still moving, false if arrived (position snapped).
        /// </summary>
        private bool MoveToward(Vector2 target, float dt)
        {
            Vector2 toTarget = target - WorldPosition;
            float distance = toTarget.Length();

            if (distance < 2f)
            {
                WorldPosition = target;
                return false;
            }

            float step = MoveSpeed * dt;
            if (step >= distance)
            {
                WorldPosition = target;
                return false;
            }

            WorldPosition += Vector2.Normalize(toTarget) * step;
            return true;
        }

        /// <summary>Clear all movement and attack state.</summary>
        public void ClearOrders()
        {
            MoveTarget = null;
            Path = null;
            PathIndex = 0;
            AttackTargetId = null;
            ResetStuckTracking();
        }

        /// <summary>Reset stuck-detection state (call when new orders are issued).</summary>
        public void ResetStuckTracking()
        {
            StuckTimer = 0f;
            LastStuckCheckPos = WorldPosition;
            StuckRecomputeCount = 0;
        }
    }
}
