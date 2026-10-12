// Conquer.Runtime — 2D tile cell grid for map collision and passability.
// Original: MapGroupKernel/GameMap.h, GameMap.cpp, MapData/

namespace Conquer.Runtime;

/// <summary>
/// 2D tile grid representing terrain walkability and boundaries.
/// <para><b>Original:</b> GameMap.cpp IsMoveEnable, IsStandEnable, GetFloorMask.</para>
/// </summary>
public sealed class CellMap
{
    private readonly byte[] _mask;

    public int Width { get; }
    public int Height { get; }

    public CellMap(int width, int height, bool defaultWalkable = true)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException($"Invalid map dimensions: {width}x{height}");

        Width = width;
        Height = height;
        _mask = new byte[width * height];
        if (!defaultWalkable)
        {
            Array.Fill(_mask, (byte)1); // 1 = blocked
        }
    }

    /// <summary>Check if coordinates fall inside the map boundary.</summary>
    public bool IsValid(int x, int y)
    {
        return (uint)x < (uint)Width && (uint)y < (uint)Height;
    }

    /// <summary>
    /// Check if a cell is walkable (in bounds and mask == 0).
    /// <para>Original: GameMap.cpp IsMoveEnable(x, y) &amp;&amp; GetFloorMask(x, y) == 0</para>
    /// </summary>
    public bool IsWalkable(int x, int y)
    {
        if (!IsValid(x, y))
            return false;
        return _mask[y * Width + x] == 0;
    }

    /// <summary>Set whether a cell is walkable.</summary>
    public void SetWalkable(int x, int y, bool walkable)
    {
        if (!IsValid(x, y))
            return;
        _mask[y * Width + x] = walkable ? (byte)0 : (byte)1;
    }

    /// <summary>Set an entire rectangular region as blocked or walkable.</summary>
    public void SetRegionWalkable(int startX, int startY, int width, int height, bool walkable)
    {
        byte val = walkable ? (byte)0 : (byte)1;
        for (int y = startY; y < startY + height; y++)
        {
            for (int x = startX; x < startX + width; x++)
            {
                if (IsValid(x, y))
                    _mask[y * Width + x] = val;
            }
        }
    }
}
