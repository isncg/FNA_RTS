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
    }
}
