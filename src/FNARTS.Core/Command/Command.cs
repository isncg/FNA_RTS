namespace FNARTS.Core
{
    public enum CommandType
    {
        Move,
        Build
    }

    /// <summary>Base class for all game commands.</summary>
    public abstract class Command
    {
        public abstract CommandType Type { get; }
    }
}
