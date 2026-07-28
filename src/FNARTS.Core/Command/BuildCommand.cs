namespace FNARTS.Core
{
    /// <summary>Order a building to be constructed at the given grid position.</summary>
    public class BuildCommand : Command
    {
        public override CommandType Type => CommandType.Build;
        public BuildingDef BuildingType { get; }
        public IsoCoord PlacementOrigin { get; }

        public BuildCommand(BuildingDef type, IsoCoord placement)
        {
            BuildingType = type;
            PlacementOrigin = placement;
        }
    }
}
