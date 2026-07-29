namespace FNARTS.Core
{
    /// <summary>Data-driven unit definition.</summary>
    public class UnitDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public float MoveSpeed { get; set; } = 100f;  // pixels/sec
        public float BuildTime { get; set; } = 5f;    // seconds to train
        public string TextureId { get; set; } = "";

        // Phase 2 combat properties
        public int HP { get; set; } = 50;
        public int AttackDamage { get; set; } = 5;
        public float AttackRange { get; set; } = 64f;
        public float AttackCooldown { get; set; } = 1.0f;
        public int Armor { get; set; } = 0;
        public int VisionRange { get; set; } = 4;     // Phase 3 fog of war

        // Phase 2.5 economy
        public int CostCredits { get; set; } = 0;

        // Phase 2.5 new unit types
        public int HealAmount { get; set; } = 0;              // >0 = this unit is a healer
        public float HealRange { get; set; } = 0f;
        public bool IsAircraft { get; set; } = false;
        public bool CanHitAir { get; set; } = false;

        // Phase 3 SC1-style steering parameters
        public float Acceleration { get; set; } = 400f;       // px/s^2
        public float Deceleration { get; set; } = 300f;       // px/s^2
        public float MaxForce { get; set; } = 600f;           // max steering force magnitude
        public float Mass { get; set; } = 1f;                 // for F=ma

        /// <summary>Collision radius in world pixels. Two units should stay
        /// at least radiusA + radiusB apart.</summary>
        public float CollisionRadius { get; set; } = 16f;
    }
}
