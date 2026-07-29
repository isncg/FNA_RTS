namespace FNARTS.Core.Fog
{
    /// <summary>Visibility state of a single map cell for one player.</summary>
    public enum FogCell : byte
    {
        /// <summary>Never seen — rendered as opaque black.</summary>
        Unexplored = 0,

        /// <summary>Previously visible but currently out of vision range —
        /// rendered as dimmed.</summary>
        Explored = 1,

        /// <summary>Currently in vision range of a friendly entity —
        /// rendered at full brightness.</summary>
        Visible = 2,
    }
}
