using System.Numerics;

namespace FNARTS.Core
{
    /// <summary>Order selected units to move to a target world position.</summary>
    public class MoveCommand : Command
    {
        public override CommandType Type => CommandType.Move;
        public Vector2 TargetWorldPosition { get; }

        public MoveCommand(Vector2 target)
        {
            TargetWorldPosition = target;
        }
    }
}
