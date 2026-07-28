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
    }
}
