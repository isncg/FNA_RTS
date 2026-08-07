using System.Numerics;

namespace FNARTS.Core
{
    /// <summary>
    /// Sub-cell slots inside a tile: the four diamond vertices
    /// (N/E/S/W = 4 slots — RA2's 5-slot centre-inclusive layout looked
    /// too cramped in this game's 2.5D view, so the centre slot was
    /// dropped). Center remains as a non-slot position: the transient
    /// spill-dock marker and a world point (tile centre). Vehicles and
    /// buildings use FullCell (whole-tile occupancy, no sub-cell
    /// allocation).
    /// </summary>
    public enum SubCell
    {
        FullCell = -1,
        Center = 0,
        North = 1,
        East = 2,
        South = 3,
        West = 4,
    }

    /// <summary>Sub-cell slot metadata: count, offsets and projection.</summary>
    public static class SubCellInfo
    {
        /// <summary>Infantry slots per tile (the four diamond vertices).
        /// </summary>
        public const int Count = 4;

        /// <summary>First infantry slot (deterministic scan start).</summary>
        public const SubCell First = SubCell.North;

        /// <summary>
        /// Continuous-grid offset of each slot inside its tile (0..1),
        /// indexed by (int)SubCell — index 0 (Center) is not an infantry
        /// slot, just the tile-centre point. All offsets stay strictly
        /// within [0.2, 0.8], so every slot point projects back to its
        /// owning tile through WorldToIso (round-trip invariant). Slots
        /// sit toward the four diamond vertices (N/E/S/W in grid space:
        /// +X=East vertex, +Y=North vertex).
        /// </summary>
        private static readonly (float X, float Y)[] Offsets =
        {
            (0.50f, 0.50f),   // Center — tile centre, not an infantry slot
            (0.76f, 0.76f),   // North (upper diamond vertex)
            (0.76f, 0.24f),   // East (right diamond vertex)
            (0.24f, 0.24f),   // South (lower diamond vertex)
            (0.24f, 0.76f),   // West (left diamond vertex)
        };

        /// <summary>True for infantry slots (North..West; excludes
        /// FullCell and Center).</summary>
        public static bool IsInfantrySlot(SubCell s)
            => s >= SubCell.North
            && (int)s < (int)SubCell.North + Count;

        /// <summary>World position of a slot: isometric projection of the
        /// tile origin plus the slot offset. FullCell and Center fall
        /// back to the tile centre.</summary>
        public static Vector2 ToWorld(IsoCoord tile, SubCell sub)
        {
            if (sub == SubCell.Center)
                return CoordUtil.IsoToWorldCenter(tile);
            if (!IsInfantrySlot(sub))
                return CoordUtil.IsoToWorldCenter(tile);
            var (fx, fy) = Offsets[(int)sub];
            return new Vector2(
                (tile.X + fx - tile.Y - fy) * CoordUtil.HALF_TILE_W,
                -(tile.X + fx + tile.Y + fy) * CoordUtil.HALF_TILE_H);
        }
    }
}
