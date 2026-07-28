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
    }
}
