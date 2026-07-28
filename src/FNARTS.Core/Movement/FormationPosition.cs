using System;
using System.Numerics;

namespace FNARTS.Core.Movement
{
    /// <summary>
    /// Computes target tiles for multi-unit formations in grid coordinates.
    /// C&amp;C2 style: each vehicle occupies its own tile, snapped to the tile centre.
    /// </summary>
    public static class FormationPosition
    {
        /// <summary>
        /// Compute formation target tiles around a centre tile.
        /// </summary>
        /// <param name="center">The grid tile under the click point.</param>
        /// <param name="count">Number of units in the selection.</param>
        /// <returns>Array of distinct grid tiles, one per unit.</returns>
        public static IsoCoord[] Compute(IsoCoord center, int count)
        {
            if (count <= 0)
                return Array.Empty<IsoCoord>();

            var positions = new IsoCoord[count];

            int cols = (int)MathF.Ceiling(MathF.Sqrt(count));
            int rows = (int)MathF.Ceiling((float)count / cols);

            // Use (n-1)/2 so that the grid is as centred as possible.
            // Odd count in a dimension → perfectly centred; even → centre
            // falls between two tiles (both are equally valid).
            int halfCols = (cols - 1) / 2;
            int halfRows = (rows - 1) / 2;

            for (int i = 0; i < count; i++)
            {
                int col = i % cols - halfCols;
                int row = i / cols - halfRows;
                positions[i] = new IsoCoord(center.X + col, center.Y + row);
            }

            return positions;
        }
    }
}
