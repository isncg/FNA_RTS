using System.Numerics;

namespace FNARTS.Core
{
    /// <summary>
    /// Orders selected units to attack a target entity.
    /// Units auto-pathfind into range and then begin attacking.
    /// </summary>
    public class AttackCommand : Command
    {
        public override CommandType Type => CommandType.Attack;

        /// <summary>Target entity ID.</summary>
        public uint TargetEntityId { get; }

        /// <summary>
        /// Target world position at the moment the command was issued
        /// (used for initial pathfinding).
        /// </summary>
        public Vector2 TargetWorldPosition { get; }

        public AttackCommand(uint targetEntityId, Vector2 targetWorldPosition)
        {
            TargetEntityId = targetEntityId;
            TargetWorldPosition = targetWorldPosition;
        }
    }
}
