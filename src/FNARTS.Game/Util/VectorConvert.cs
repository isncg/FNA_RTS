using System;
using System.Runtime.CompilerServices;

namespace FNARTS.Game
{
    /// <summary>Conversion helpers between System.Numerics.Vector2 and XNA Vector2.</summary>
    public static class VectorConvert
    {
        public static Microsoft.Xna.Framework.Vector2 ToXna(this System.Numerics.Vector2 v)
            => new Microsoft.Xna.Framework.Vector2(v.X, v.Y);

        public static System.Numerics.Vector2 ToNumerics(this Microsoft.Xna.Framework.Vector2 v)
            => new System.Numerics.Vector2(v.X, v.Y);
    }
}
