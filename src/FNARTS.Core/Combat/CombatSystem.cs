using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FNARTS.Core.Pathfinding;

namespace FNARTS.Core.Combat
{
    /// <summary>
    /// Per-frame combat processor: range checks, damage application, cooldown
    /// management, auto-pursuit pathfinding, and death collection.
    /// Pure C# — no FNA or GPU dependency. All calculations use fixed dt = 1/60s.
    /// </summary>
    public class CombatSystem
    {
        private readonly List<uint> _deadEntities = new();
        private int _frameCounter;

        /// <summary>
        /// Process one frame of combat.
        /// </summary>
        /// <param name="dt">Fixed timestep (1/60 s).</param>
        /// <param name="entities">Entity manager for target lookups.</param>
        /// <param name="pathfinder">Pathfinder for auto-pursuit.</param>
        /// <param name="onDeath">Called once per dead entity after the traversal
        /// loop completes (safe to mutate the entity collection here).</param>
        public void Update(float dt, EntityManager entities,
            Pathfinder pathfinder, Action<Entity> onDeath)
        {
            _deadEntities.Clear();
            _frameCounter++;

            // Deterministic iteration: sort by ID so every client processes
            // combat in the same order.
            var aliveEntities = entities.AllEntities
                .Where(e => e.IsAlive)
                .OrderBy(e => e.Id);

            foreach (var entity in aliveEntities)
            {
                if (entity is not Unit unit) continue;

                // Healers auto-target nearby damaged allies
                if (unit.HealAmount > 0 && !unit.AttackTargetId.HasValue)
                    AutoTargetHeal(unit, entities);

                if (unit.AttackTargetId.HasValue)
                {
                    if (unit.HealAmount > 0)
                        ProcessHealer(unit, entities, pathfinder, dt);
                    else
                        ProcessUnitCombat(unit, entities, pathfinder, dt);
                }
                // Buildings do not auto-attack in Phase 2
            }

            // Fire death callbacks after the traversal loop completes so the
            // caller can safely mutate the entity collection.
            foreach (var id in _deadEntities)
            {
                var entity = entities.GetEntity(id);
                if (entity != null)
                    onDeath(entity);
            }
        }

        private void ProcessUnitCombat(Unit attacker, EntityManager entities,
            Pathfinder pathfinder, float dt)
        {
            var target = entities.GetEntity(attacker.AttackTargetId!.Value);
            if (target == null || !target.IsAlive)
            {
                attacker.AttackTargetId = null;
                return;
            }

            // CanHitAir check: ground-only units cannot target aircraft
            if (target is Unit targetUnit && targetUnit.IsAircraft
                && !attacker.CanHitAir)
            {
                attacker.AttackTargetId = null;
                return;
            }

            float dist = Vector2.Distance(attacker.WorldPosition, target.WorldPosition);

            if (dist <= attacker.AttackRange)
            {
                if (attacker.CanAttack)
                {
                    ApplyDamage(attacker, target);
                    attacker.AttackCooldownTimer = attacker.Definition.AttackCooldown;

                    if (!target.IsAlive)
                        _deadEntities.Add(target.Id);
                }
            }
            else
            {
                AutoPursuit(attacker, target, pathfinder);
            }
        }

        /// <summary>
        /// Healer tick: move toward and heal the target friendly unit.
        /// </summary>
        private void ProcessHealer(Unit healer, EntityManager entities,
            Pathfinder pathfinder, float dt)
        {
            var target = entities.GetEntity(healer.AttackTargetId!.Value);
            if (target == null || !target.IsAlive || target.Faction != healer.Faction)
            {
                healer.AttackTargetId = null;
                return;
            }

            if (target is not Unit targetUnit) { healer.AttackTargetId = null; return; }
            if (targetUnit.CurrentHP >= targetUnit.MaxHP) { healer.AttackTargetId = null; return; }

            float dist = Vector2.Distance(healer.WorldPosition, target.WorldPosition);

            if (dist <= healer.HealRange)
            {
                if (healer.CanAttack)
                {
                    targetUnit.CurrentHP = Math.Min(
                        targetUnit.MaxHP,
                        targetUnit.CurrentHP + healer.HealAmount);
                    healer.AttackCooldownTimer = healer.Definition.AttackCooldown;

                    if (targetUnit.CurrentHP >= targetUnit.MaxHP)
                        healer.AttackTargetId = null; // done healing
                }
            }
            else
            {
                AutoPursuit(healer, target, pathfinder);
            }
        }

        /// <summary>
        /// Auto-target the nearest damaged friendly unit within a reasonable
        /// search radius.
        /// </summary>
        private static void AutoTargetHeal(Unit healer, EntityManager entities)
        {
            Unit bestTarget = null;
            float bestDist = float.MaxValue;

            foreach (var e in entities.AllEntities)
            {
                if (!e.IsAlive || e.Faction != healer.Faction) continue;
                if (e is not Unit u) continue;
                if (u.Id == healer.Id) continue;
                if (u.CurrentHP >= u.MaxHP) continue; // already full

                float dist = Vector2.Distance(healer.WorldPosition, u.WorldPosition);
                if (dist < bestDist && dist < healer.HealRange * 3f) // search radius
                {
                    bestDist = dist;
                    bestTarget = u;
                }
            }

            if (bestTarget != null)
                healer.AttackTargetId = bestTarget.Id;
        }

        /// <summary>
        /// Auto-pursuit pathfinding (shared by attackers and healers).
        /// Re-paths every 30 frames (0.5 s) to reduce CPU overhead.
        /// </summary>
        private void AutoPursuit(Unit pursuer, Entity target, Pathfinder pathfinder)
        {
            if (_frameCounter % 30 == 0 || pursuer.Path == null)
            {
                var start = CoordUtil.WorldToIso(pursuer.WorldPosition);
                var end = CoordUtil.WorldToIso(target.WorldPosition);

                // Aircraft fly over obstacles — use octile distance only.
                if (pursuer.IsAircraft)
                {
                    // Straight-line in grid terms: just set MoveTarget directly.
                    pursuer.Path = pathfinder.FindPath(start, end);
                    if (pursuer.Path == null || pursuer.Path.Count == 0)
                    {
                        pursuer.MoveTarget = target.WorldPosition;
                        pursuer.Path = null;
                    }
                    pursuer.PathIndex = 0;
                }
                else
                {
                    pursuer.Path = pathfinder.FindPath(start, end);
                    pursuer.PathIndex = 0;
                }
            }
        }

        /// <summary>
        /// Compute actual damage: attackDamage − armor, minimum 1.
        /// </summary>
        public static int CalculateDamage(int attackDamage, int armor)
            => Math.Max(1, attackDamage - armor);

        private static void ApplyDamage(Unit attacker, Entity target)
        {
            int targetArmor = target switch
            {
                Unit u => u.Armor,
                Building b => b.Armor,
                _ => 0,
            };

            int damage = CalculateDamage(attacker.AttackDamage, targetArmor);

            switch (target)
            {
                case Unit targetUnit:
                    targetUnit.CurrentHP -= damage;
                    if (targetUnit.CurrentHP <= 0)
                        targetUnit.IsAlive = false;
                    // Auto-retaliate: if the victim doesn't already have a target,
                    // fight back against the attacker.
                    if (targetUnit.IsAlive && !targetUnit.AttackTargetId.HasValue)
                        targetUnit.AttackTargetId = attacker.Id;
                    break;
                case Building targetBuilding:
                    targetBuilding.CurrentHP -= damage;
                    if (targetBuilding.CurrentHP <= 0)
                        targetBuilding.IsAlive = false;
                    break;
            }
        }
    }
}
