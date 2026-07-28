using System.Collections.Generic;

namespace FNARTS.Core
{
    /// <summary>Data-driven building definition.</summary>
    public class BuildingDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int SizeX { get; set; } = 1;  // tiles wide (East, gx axis)
        public int SizeY { get; set; } = 1;  // tiles deep (North, gy axis)
        public int Height { get; set; } = 1; // tile-units tall
        public string TextureId { get; set; } = "";
        /// <summary>UnitDef IDs this building can train.</summary>
        public List<string> ProducesUnitIds { get; set; } = new();

        // Phase 2 combat properties
        public int HP { get; set; } = 300;
        public int Armor { get; set; } = 3;
        public int VisionRange { get; set; } = 2;      // Phase 3 fog of war
    }
}
