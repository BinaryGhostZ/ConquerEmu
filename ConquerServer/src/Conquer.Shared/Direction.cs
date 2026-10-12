// Conquer.Shared — Direction enum and spatial offset tables.
// Original: MapGroupKernel/I_Role.h _DELTA_X, _DELTA_Y

namespace Conquer.Shared;

/// <summary>
/// 8-way movement and facing direction.
/// <para>
/// <b>Original:</b> MapGroupKernel/I_Role.h (0..7, 8 is none).
/// </para>
/// </summary>
public enum Direction : byte
{
    South = 0,
    SouthWest = 1,
    West = 2,
    NorthWest = 3,
    North = 4,
    NorthEast = 5,
    East = 6,
    SouthEast = 7,
    None = 8
}

/// <summary>
/// Direction offsets and coordinate calculation helpers.
/// <para>
/// <b>Original:</b> MapGroupKernel/I_Role.h:
/// <code>
/// const int _DELTA_X[9] = { 0, -1, -1, -1, 0, 1, 1, 1, 0 };
/// const int _DELTA_Y[9] = { 1, 1, 0, -1, -1, -1, 0, 1, 0 };
/// </code>
/// </para>
/// </summary>
public static class MapCoordinates
{
    /// <summary>X offset for each of the 9 directions.</summary>
    public static readonly int[] DeltaX = [0, -1, -1, -1, 0, 1, 1, 1, 0];

    /// <summary>Y offset for each of the 9 directions.</summary>
    public static readonly int[] DeltaY = [1, 1, 0, -1, -1, -1, 0, 1, 0];

    /// <summary>
    /// Calculate Chebyshev distance (maximum coordinate delta) between two points.
    /// <para>Original: GameMap.h Distance(x1, y1, x2, y2) = max(abs(x1 - x2), abs(y1 - y2))</para>
    /// </summary>
    public static int ChebyshevDistance(int x1, int y1, int x2, int y2)
    {
        return Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
    }

    /// <summary>
    /// Calculate the block index for a given tile coordinate.
    /// <para>Original: GameMap.h Block(nPos) = nPos / CELLS_PER_BLOCK</para>
    /// </summary>
    public static int ToBlock(int coordinate)
    {
        return coordinate / TimingConstants.CellsPerBlock;
    }

    /// <summary>
    /// Computes 8-way facing direction from source point to destination point.
    /// Original: GameMap.h / I_Role.h.
    /// </summary>
    public static Direction GetDirection(int fromX, int fromY, int toX, int toY)
    {
        int dx = toX - fromX;
        int dy = toY - fromY;
        if (dx == 0 && dy == 0) return Direction.South;

        if (dx < 0)
        {
            if (dy > 0) return Direction.SouthWest;
            if (dy < 0) return Direction.NorthWest;
            return Direction.West;
        }
        else if (dx > 0)
        {
            if (dy > 0) return Direction.SouthEast;
            if (dy < 0) return Direction.NorthEast;
            return Direction.East;
        }
        else
        {
            return dy > 0 ? Direction.South : Direction.North;
        }
    }
}

/// <summary>
/// Offset helper for Direction.
/// </summary>
public static class DirectionOffsets
{
    public static (int Dx, int Dy) GetOffset(Direction dir)
    {
        int d = (int)dir;
        return (d >= 0 && d < MapCoordinates.DeltaX.Length)
            ? (MapCoordinates.DeltaX[d], MapCoordinates.DeltaY[d])
            : (0, 0);
    }
}
