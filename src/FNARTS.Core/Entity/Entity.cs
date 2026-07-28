using System.Numerics;

namespace FNARTS.Core
{
    /// <summary>Base class for all game entities (units, buildings).</summary>
    public abstract class Entity
    {
        public uint Id { get; }
        public Vector2 WorldPosition { get; set; }
        public int Faction { get; set; }
        public bool IsSelected { get; set; }
        public bool IsAlive { get; set; } = true;

        /// <summary>World-space half-extent for broad-phase click hit-testing.
        /// The sprite is drawn with origin at centre, so the bounding
        /// rectangle is [WorldPosition ± HitHalfExtent].</summary>
        public virtual Vector2 HitHalfExtent => new Vector2(16f, 16f);

        /// <summary>Precise hit test for a world-space point.
        /// Default: axis-aligned bounding-box check.  Override for
        /// tighter per-face / per-pixel tests.</summary>
        public virtual bool ContainsPoint(Vector2 worldPoint)
        {
            var half = HitHalfExtent;
            return worldPoint.X >= WorldPosition.X - half.X
                && worldPoint.X <= WorldPosition.X + half.X
                && worldPoint.Y >= WorldPosition.Y - half.Y
                && worldPoint.Y <= WorldPosition.Y + half.Y;
        }

        protected Entity()
        {
            Id = EntityIdGenerator.Next();
        }
    }
}
