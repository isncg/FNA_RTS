using System.Collections.Generic;
using System.Numerics;
using Xunit;
using FNARTS.Core.Combat;
using FNARTS.Core.Pathfinding;

namespace FNARTS.Core.Tests.Combat
{
    public class CombatSystemTests
    {
        private static Unit MakeUnit(float x, float y, UnitDef def = null)
        {
            def ??= new UnitDef { Id = "test", MoveSpeed = 100f, HP = 100,
                AttackDamage = 20, AttackRange = 96f, AttackCooldown = 1.0f, Armor = 5 };
            return new Unit(def) { WorldPosition = new Vector2(x, y) };
        }

        private static Building MakeBuilding(int gx, int gy,
            int sizeX = 1, int sizeY = 1, BuildingDef def = null)
        {
            def ??= new BuildingDef { Id = "test_bld", SizeX = sizeX, SizeY = sizeY,
                HP = 300, Armor = 10 };
            return new Building(def, new IsoCoord(gx, gy));
        }

        private static (EntityManager mgr, PathfindingFacade pf, List<FNARTS.Core.Entity> deaths)
            Setup()
        {
            var mgr = new EntityManager();
            var terrain = TerrainCostProvider.CreateDefault(51, 51,
                getTileType: _ => TileType.Grass,
                isBlockedByEntity: _ => false);
            var pf = new PathfindingFacade(terrain);
            var deaths = new List<FNARTS.Core.Entity>();
            return (mgr, pf, deaths);
        }

        // ── CalculateDamage ────────────────────────────────────────────

        [Fact]
        public void CalculateDamage_ArmorReduces()
        {
            int dmg = CombatSystem.CalculateDamage(20, 5);
            Assert.Equal(15, dmg);
        }

        [Fact]
        public void CalculateDamage_MinimumOne()
        {
            int dmg = CombatSystem.CalculateDamage(5, 10);
            Assert.Equal(1, dmg);
        }

        [Fact]
        public void CalculateDamage_ZeroArmor_FullDamage()
        {
            int dmg = CombatSystem.CalculateDamage(30, 0);
            Assert.Equal(30, dmg);
        }

        // ── Damage application ─────────────────────────────────────────

        [Fact]
        public void UnitTakesDamage_HpDecreases()
        {
            var unit = MakeUnit(0, 0);
            int hpBefore = unit.CurrentHP;

            // Apply damage by setting up combat
            var atkDef = new UnitDef { Id = "attacker", MoveSpeed = 100f, HP = 100,
                AttackDamage = 30, AttackRange = 200f, AttackCooldown = 0.01f, Armor = 0 };
            var attacker = new Unit(atkDef) { WorldPosition = new Vector2(10, 0) };
            var target = MakeUnit(10, 0);

            attacker.AttackTargetId = target.Id;

            var (mgr, pf, deaths) = Setup();
            mgr.AddEntity(attacker);
            mgr.AddEntity(target);

            var combat = new CombatSystem();
            combat.Update(1f / 60f, mgr, pf, e => deaths.Add(e));

            // target has 5 armor, attacker does 30 damage → 25 net
            Assert.Equal(hpBefore - 25, target.CurrentHP);
        }

        [Fact]
        public void UnitDies_WhenHpZero()
        {
            var atkDef = new UnitDef { Id = "attacker", MoveSpeed = 100f, HP = 100,
                AttackDamage = 200, AttackRange = 200f, AttackCooldown = 0.01f, Armor = 0 };
            var attacker = new Unit(atkDef) { WorldPosition = new Vector2(10, 0) };
            var target = MakeUnit(10, 0); // 100 HP, 5 armor → net 195 damage → dead

            attacker.AttackTargetId = target.Id;

            var (mgr, pf, deaths) = Setup();
            mgr.AddEntity(attacker);
            mgr.AddEntity(target);

            var combat = new CombatSystem();
            combat.Update(1f / 60f, mgr, pf, e => deaths.Add(e));

            Assert.False(target.IsAlive);
            Assert.Single(deaths);
            Assert.Same(target, deaths[0]);
        }

        [Fact]
        public void BuildingTakesDamage_FromUnit()
        {
            var atkDef = new UnitDef { Id = "attacker", MoveSpeed = 100f, HP = 100,
                AttackDamage = 50, AttackRange = 200f, AttackCooldown = 0.01f, Armor = 0 };
            var building = MakeBuilding(3, 0); // 300 HP, 10 armor
            // Place attacker right next to the building
            var attacker = new Unit(atkDef)
            {
                WorldPosition = building.WorldPosition + new Vector2(10, 0)
            };

            attacker.AttackTargetId = building.Id;

            var (mgr, pf, deaths) = Setup();
            mgr.AddEntity(attacker);
            mgr.AddEntity(building);

            var combat = new CombatSystem();
            combat.Update(1f / 60f, mgr, pf, e => deaths.Add(e));

            // 50 damage - 10 armor = 40 → 300 - 40 = 260
            Assert.Equal(260, building.CurrentHP);
            Assert.True(building.IsAlive);
        }

        [Fact]
        public void BuildingDies_WhenHpZero()
        {
            var atkDef = new UnitDef { Id = "attacker", MoveSpeed = 100f, HP = 100,
                AttackDamage = 500, AttackRange = 200f, AttackCooldown = 0.01f, Armor = 0 };
            var building = MakeBuilding(3, 0); // 300 HP, 10 armor
            var attacker = new Unit(atkDef)
            {
                WorldPosition = building.WorldPosition + new Vector2(10, 0)
            };

            attacker.AttackTargetId = building.Id;

            var (mgr, pf, deaths) = Setup();
            mgr.AddEntity(attacker);
            mgr.AddEntity(building);

            var combat = new CombatSystem();
            combat.Update(1f / 60f, mgr, pf, e => deaths.Add(e));

            Assert.False(building.IsAlive);
            Assert.Single(deaths);
            Assert.Same(building, deaths[0]);
        }

        // ── Attack cooldown ────────────────────────────────────────────

        [Fact]
        public void AttackCooldown_BlocksRepeatedAttack()
        {
            var atkDef = new UnitDef { Id = "attacker", MoveSpeed = 100f, HP = 100,
                AttackDamage = 10, AttackRange = 200f, AttackCooldown = 1.0f, Armor = 0 };
            var attacker = new Unit(atkDef) { WorldPosition = new Vector2(10, 0) };
            var target = MakeUnit(10, 0,
                new UnitDef { Id = "target", MoveSpeed = 50f, HP = 200, Armor = 0 });

            attacker.AttackTargetId = target.Id;

            var (mgr, pf, deaths) = Setup();
            mgr.AddEntity(attacker);
            mgr.AddEntity(target);

            var combat = new CombatSystem();

            // First frame: attack should hit
            combat.Update(1f / 60f, mgr, pf, e => deaths.Add(e));
            int hpAfterFirst = target.CurrentHP;
            Assert.True(attacker.AttackCooldownTimer > 0f);

            // Second frame: still on cooldown, no additional damage
            combat.Update(1f / 60f, mgr, pf, e => deaths.Add(e));
            Assert.Equal(hpAfterFirst, target.CurrentHP); // no change
        }

        [Fact]
        public void AttackCooldown_DecreasesOverTime()
        {
            var atkDef = new UnitDef { Id = "attacker", MoveSpeed = 100f, HP = 100,
                AttackDamage = 10, AttackRange = 200f, AttackCooldown = 0.5f, Armor = 0 };
            var attacker = new Unit(atkDef) { WorldPosition = new Vector2(10, 0) };
            var target = MakeUnit(10, 0,
                new UnitDef { Id = "target", MoveSpeed = 50f, HP = 500, Armor = 0 });

            attacker.AttackTargetId = target.Id;

            var (mgr, pf, deaths) = Setup();
            mgr.AddEntity(attacker);
            mgr.AddEntity(target);

            var combat = new CombatSystem();

            // Fire once — CombatSystem applies damage + sets cooldown
            combat.Update(1f / 60f, mgr, pf, e => deaths.Add(e));
            float cdAfterFirst = attacker.AttackCooldownTimer;
            Assert.True(cdAfterFirst > 0f);

            // Cooldown is ticked by Unit.Update (called separately in the game loop)
            attacker.Update(0.6f);
            Assert.True(attacker.AttackCooldownTimer <= 0f);
            Assert.True(attacker.CanAttack);
        }

        // ── Range checks ───────────────────────────────────────────────

        [Fact]
        public void OutOfRange_NoDamageDealt()
        {
            var atkDef = new UnitDef { Id = "attacker", MoveSpeed = 100f, HP = 100,
                AttackDamage = 50, AttackRange = 20f, AttackCooldown = 0.01f, Armor = 0 };
            var attacker = new Unit(atkDef) { WorldPosition = new Vector2(0, 0) };
            var target = MakeUnit(200, 0); // far away

            attacker.AttackTargetId = target.Id;

            var (mgr, pf, deaths) = Setup();
            mgr.AddEntity(attacker);
            mgr.AddEntity(target);

            var combat = new CombatSystem();
            combat.Update(1f / 60f, mgr, pf, e => deaths.Add(e));

            // No damage because out of range
            Assert.Equal(target.Definition.HP, target.CurrentHP);
            // Auto-pursuit should have been triggered (path set)
            Assert.NotNull(attacker.Path); // pathfinder should find a path
        }

        [Fact]
        public void InRange_DealsDamage()
        {
            var atkDef = new UnitDef { Id = "attacker", MoveSpeed = 100f, HP = 100,
                AttackDamage = 30, AttackRange = 200f, AttackCooldown = 0.01f, Armor = 0 };
            var attacker = new Unit(atkDef) { WorldPosition = new Vector2(10, 0) };
            var target = MakeUnit(10, 0); // very close, within range

            attacker.AttackTargetId = target.Id;

            var (mgr, pf, deaths) = Setup();
            mgr.AddEntity(attacker);
            mgr.AddEntity(target);

            var combat = new CombatSystem();
            combat.Update(1f / 60f, mgr, pf, e => deaths.Add(e));

            // 30 damage - 5 armor = 25 damage
            Assert.Equal(target.Definition.HP - 25, target.CurrentHP);
        }

        // ── Dead target handling ───────────────────────────────────────

        [Fact]
        public void DeadTarget_AttackTargetCleared()
        {
            var atkDef = new UnitDef { Id = "attacker", MoveSpeed = 100f, HP = 100,
                AttackDamage = 10, AttackRange = 200f, AttackCooldown = 0.01f, Armor = 0 };
            var attacker = new Unit(atkDef) { WorldPosition = new Vector2(10, 0) };
            var target = MakeUnit(10, 0);
            target.IsAlive = false; // already dead

            attacker.AttackTargetId = target.Id;

            var (mgr, pf, deaths) = Setup();
            mgr.AddEntity(attacker);
            mgr.AddEntity(target); // dead but still in manager

            var combat = new CombatSystem();
            combat.Update(1f / 60f, mgr, pf, e => deaths.Add(e));

            // Attacker should clear its target
            Assert.Null(attacker.AttackTargetId);
            Assert.Empty(deaths);
        }

        [Fact]
        public void NullTarget_AttackTargetCleared()
        {
            var atkDef = new UnitDef { Id = "attacker", MoveSpeed = 100f, HP = 100,
                AttackDamage = 10, AttackRange = 200f, AttackCooldown = 0.01f, Armor = 0 };
            var attacker = new Unit(atkDef) { WorldPosition = new Vector2(10, 0) };
            attacker.AttackTargetId = 999; // nonexistent ID

            var (mgr, pf, deaths) = Setup();
            mgr.AddEntity(attacker);

            var combat = new CombatSystem();
            combat.Update(1f / 60f, mgr, pf, e => deaths.Add(e));

            Assert.Null(attacker.AttackTargetId);
            Assert.Empty(deaths);
        }
    }
}
