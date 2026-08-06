using System;
using System.Collections.Generic;
using System.Numerics;

namespace FNARTS.Core
{
    /// <summary>
    /// A mobile entity that can receive move and attack commands.
    /// Phase 3: waypoint-following at constant speed with Velocity field
    /// for separation and steering-based separation integration.
    /// </summary>
    public class Unit : Entity
    {
        public UnitDef Definition { get; }
        public float MoveSpeed { get; }

        // ---- movement ----
        public Vector2? MoveTarget { get; set; }           // final destination (world)
        public List<IsoCoord>? Path { get; set; }          // A* waypoints (grid coords)
        public int PathIndex { get; set; }                  // current waypoint index
        public Vector2 Velocity { get; set; }                // current velocity (px/s)
        /// <summary>If set, overrides the unit's natural MoveSpeed.
        /// Used by GroupMovement to match all units to the slowest speed.</summary>
        public float? ForcedMoveSpeed { get; set; }

        // ---- combat ----
        public int CurrentHP { get; set; }
        public int MaxHP => Definition.HP;
        public int AttackDamage => Definition.AttackDamage;
        public float AttackRange => Definition.AttackRange;
        public float AttackCooldownTimer { get; set; }
        public uint? AttackTargetId { get; set; }
        /// <summary>Fire-on-the-move (vehicles only): set when a move order
        /// arrives while the vehicle is attacking. The vehicle keeps moving
        /// and firing while the target stays in range, and disengages
        /// (AttackTargetId cleared, no pursuit) once it leaves range.
        /// Non-vehicles ignore this flag.</summary>
        public bool MoveWhileAttacking { get; set; }
        public int Armor => Definition.Armor;
        public int HealAmount => Definition.HealAmount;
        public float HealRange => Definition.HealRange;
        public bool IsAircraft => Definition.IsAircraft;
        public bool CanHitAir => Definition.CanHitAir;

        // ---- stuck detection ----
        public float StuckTimer { get; set; }
        public Vector2 LastStuckCheckPos { get; set; }
        public int StuckRecomputeCount { get; set; }

        // ---- tile occupancy (OpenRA Mobile-style) ----
        /// <summary>Tile currently occupied (or being left while in transit).</summary>
        public IsoCoord FromTile { get; set; }
        /// <summary>Tile reserved for entry; equals FromTile when standing.</summary>
        public IsoCoord ToTile { get; set; }
        /// <summary>Whether FromTile/ToTile have been initialised from position.</summary>
        public bool TilesInitialized { get; set; }
        /// <summary>True while transitioning between two tiles.</summary>
        public bool IsMovingBetweenTiles => FromTile != ToTile;
        /// <summary>Set by MovementSystem when another unit wants our tile
        /// but we have orders and can't just nudge away.</summary>
        public bool IsBlocking { get; set; }
        /// <summary>Arbitration wait / repath cooldown remaining (seconds).</summary>
        public float WaitTimer { get; set; }
        /// <summary>Whether the wait phase already started for the current
        /// blocker (OpenRA hasWaited).</summary>
        public bool HasWaited { get; set; }

        // ---- 3D vehicle state ----
        /// <summary>True for units rendered as 3D vehicles (tank, etc.).</summary>
        public bool IsVehicle { get; set; }
        /// <summary>Body yaw angle (radians). 0 = East, positive = CCW.</summary>
        public float BodyRotation { get; set; }
        /// <summary>Turret yaw angle (radians, absolute). Tracks target when attacking.</summary>
        public float TurretRotation { get; set; }
        /// <summary>Turret yaw relative to the body (radians). Idle: eases to 0
        /// so the turret rides rigidly with the body while turning.</summary>
        public float TurretOffset { get; set; }
        /// <summary>3D world position (X=East, Y=North, Z=up).</summary>
        public Vector3 VehiclePosition3D { get; set; }

        // Turn rates (radians/second). The turret must slew far faster than
        // the hull so it can keep up with a continuously changing target bearing.
        private const float BodyTurnRate = 8f;
        private const float TurretTurnRate = 24f;

        // ---- state queries ----
        public bool IsAttacking => AttackTargetId.HasValue;
        public bool CanAttack => AttackCooldownTimer <= 0f;
        public bool IsMoving => (Path != null && PathIndex < Path.Count)
                                || MoveTarget.HasValue
                                || Velocity.LengthSquared() > 0.1f;

        /// <summary>Snap radius when reaching a waypoint or destination.</summary>
        private const float SNAP_RADIUS = 2f;

        public Unit(UnitDef definition)
        {
            Definition = definition;
            MoveSpeed = definition.MoveSpeed;
            CurrentHP = definition.HP;
            AttackCooldownTimer = 0f;
        }

        /// <summary>
        /// Per-frame update: follow waypoints at constant speed, hard-turn
        /// at corners.  Velocity is set directly to direction * speed
        /// so the separation system can read and modify it.
        /// </summary>
        public void Update(float dt)
        {
            if (!IsAlive) return;

            // Tick cooldown
            if (AttackCooldownTimer > 0f)
                AttackCooldownTimer -= dt;

            float speed = ForcedMoveSpeed ?? MoveSpeed;

            // ── Waypoint following ──
            if (Path != null && PathIndex < Path.Count)
            {
                // OpenRA-style reservation gate: a unit may only move into
                // a tile that the MovementSystem has reserved for it.
                // Uninitialised units (tests) bypass the gate entirely.
                if (TilesInitialized && ToTile != Path[PathIndex])
                {
                    Velocity = Vector2.Zero;
                    return;
                }

                bool isFinal = PathIndex == Path.Count - 1 && MoveTarget.HasValue
                    // Only snap to MoveTarget when the last waypoint IS the
                    // destination tile — detour paths (Nudge/StepAside) keep
                    // the original MoveTarget and must not teleport the unit.
                    && CoordUtil.WorldToIso(MoveTarget.Value) == Path[PathIndex];
                Vector2 target = isFinal
                    ? MoveTarget.Value
                    : CoordUtil.IsoToWorldCenter(Path[PathIndex]);

                Vector2 toTarget = target - WorldPosition;
                float dist = toTarget.Length();
                float step = speed * dt;

                if (dist < SNAP_RADIUS)
                {
                    AdvanceWaypoint(target, isFinal, speed);
                }
                else if (step >= dist)
                {
                    // Overshoot: snap to target and advance
                    WorldPosition = target;
                    AdvanceWaypoint(target, isFinal, speed);
                }
                else
                {
                    Vector2 dir = toTarget / dist;
                    Velocity = dir * speed;
                    WorldPosition += Velocity * dt;
                }
            }
            else if (MoveTarget.HasValue)
            {
                // ── No path: straight-line at constant speed ──
                Vector2 toTarget = MoveTarget.Value - WorldPosition;
                float dist = toTarget.Length();
                float step = speed * dt;

                if (dist < SNAP_RADIUS || step >= dist)
                {
                    WorldPosition = MoveTarget.Value;
                    MoveTarget = null;
                    Path = null;
                    Velocity = Vector2.Zero;
                }
                else
                {
                    Vector2 dir = toTarget / dist;
                    Velocity = dir * speed;
                    WorldPosition += Velocity * dt;
                }
            }
            else if (IsMovingBetweenTiles)
            {
                // ── Orders canceled mid-transition: finish entering ToTile ──
                Vector2 dest = CoordUtil.IsoToWorldCenter(ToTile);
                Vector2 toTarget = dest - WorldPosition;
                float dist = toTarget.Length();
                float step = speed * dt;

                if (dist < SNAP_RADIUS || step >= dist)
                {
                    WorldPosition = dest;
                    FromTile = ToTile;
                    Velocity = Vector2.Zero;
                }
                else
                {
                    Vector2 dir = toTarget / dist;
                    Velocity = dir * speed;
                    WorldPosition += Velocity * dt;
                }
            }
            else
            {
                // Idle: stop immediately
                Velocity = Vector2.Zero;
            }

            // ── 3D vehicle rotation update ──
            if (IsVehicle)
            {
                // Body rotation: align with movement direction.
                // VehicleRenderer rotates in grid space (local X=east, Y=north)
                // before the isometric projection, so the angle must be the
                // atan2 of the velocity expressed in grid coordinates — not the
                // raw 2D world-space angle.
                if (Velocity.LengthSquared() > 1f)
                {
                    var gridDir = CoordUtil.WorldToIsoFloat(Velocity);
                    float targetBody = MathF.Atan2(gridDir.Y, gridDir.X);
                    BodyRotation = LerpAngle(BodyRotation, targetBody, BodyTurnRate * dt);
                }

                // Turret control switches mode on attack state:
                if (AttackTargetId.HasValue)
                {
                    // Attacking: the Game layer steers TurretRotation (absolute)
                    // toward the target bearing every frame. Just mirror the
                    // current relative offset so idle re-centring resumes
                    // seamlessly when the target is lost.
                    TurretOffset = WrapAngle(TurretRotation - BodyRotation);
                }
                else
                {
                    // Idle: turret is driven by its offset relative to the
                    // body — body turns carry the turret along rigidly (no
                    // lag), while the offset eases to 0 and stays there.
                    TurretOffset = LerpAngle(TurretOffset, 0f, TurretTurnRate * dt);
                    TurretRotation = BodyRotation + TurretOffset;
                }

                // Sync 3D position from 2D world position
                VehiclePosition3D = new Vector3(WorldPosition.X, WorldPosition.Y, 0f);
            }
        }

        /// <summary>
        /// Handle waypoint arrival: advance to next waypoint or finish.
        /// </summary>
        private void AdvanceWaypoint(Vector2 target, bool isFinal, float speed)
        {
            if (isFinal)
            {
                WorldPosition = MoveTarget!.Value;
                MoveTarget = null;
                Path = null;
                PathIndex = 0;
                Velocity = Vector2.Zero;
            }
            else
            {
                PathIndex++;
                // Hard turn: velocity instantly points to next waypoint
                if (PathIndex < Path.Count)
                {
                    Vector2 toNext = CoordUtil.IsoToWorldCenter(Path[PathIndex]) - WorldPosition;
                    Velocity = toNext.LengthSquared() > 0.01f
                        ? Vector2.Normalize(toNext) * speed
                        : Vector2.Zero;
                }
                else
                {
                    Velocity = Vector2.Zero;
                }
            }

            // Arrived on ToTile: occupancy of the old tile is released.
            // OpenRA: IsBlocking resets whenever the location changes.
            FromTile = ToTile;
            IsBlocking = false;
            HasWaited = false;
        }

        /// <summary>Clear all movement and attack state.</summary>
        public void ClearOrders()
        {
            MoveTarget = null;
            Path = null;
            PathIndex = 0;
            AttackTargetId = null;
            MoveWhileAttacking = false;
            Velocity = Vector2.Zero;
            ForcedMoveSpeed = null;
            IsBlocking = false;
            HasWaited = false;
            WaitTimer = 0f;
            ResetStuckTracking();
        }

        /// <summary>Reset stuck-detection state (call when new orders are issued).</summary>
        public void ResetStuckTracking()
        {
            StuckTimer = 0f;
            LastStuckCheckPos = WorldPosition;
            StuckRecomputeCount = 0;
        }

        /// <summary>
        /// Smoothly interpolate current angle toward target angle,
        /// handling the -π/π wrap-around.
        /// </summary>
        private static float LerpAngle(float current, float target, float speed)
        {
            float diff = target - current;
            return current + WrapToStep(diff, speed);
        }

        /// <summary>Normalise an angle to the (-π, π] range.</summary>
        private static float WrapAngle(float angle)
        {
            while (angle > MathF.PI) angle -= MathF.PI * 2f;
            while (angle < -MathF.PI) angle += MathF.PI * 2f;
            return angle;
        }

        /// <summary>Clamp an angular difference to a maximum step size.</summary>
        private static float WrapToStep(float diff, float maxStep)
        {
            while (diff > MathF.PI) diff -= MathF.PI * 2f;
            while (diff < -MathF.PI) diff += MathF.PI * 2f;

            if (MathF.Abs(diff) <= maxStep)
                return diff;

            return MathF.Sign(diff) * maxStep;
        }
    }
}
