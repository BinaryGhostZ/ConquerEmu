// Conquer.Runtime — Ground item and drop representation.
// Original: MapGroupKernel/MapItem.h and MapItem.cpp CMapItem

namespace Conquer.Runtime;

/// <summary>
/// Ground item lying on a GameMap.
/// Original: MapGroupKernel/MapItem.h CMapItem.
/// </summary>
public sealed class GroundItem
{
    public uint Id { get; init; }
    public uint ItemType { get; init; }
    public int PosX { get; init; }
    public int PosY { get; init; }
    public uint Money { get; init; }
    public Item? Item { get; init; }
    public DateTime DropTime { get; init; } = DateTime.UtcNow;

    public bool IsMoney => Money > 0;
}
