// Conquer.NpcServer — Lightweight user replica for monster perception.
// Original: my_server/NpcKernel/User.h

namespace Conquer.NpcServer;

/// <summary>
/// Lightweight player model maintained in NpcServer from GameServer events.
/// Used for aggro targeting, proximity detection, and monster awakening.
/// </summary>
public sealed class NpcUser
{
    public uint Id { get; init; }
    public string Name { get; set; } = "";
    public uint MapId { get; set; }
    public ushort PosX { get; set; }
    public ushort PosY { get; set; }
    public int Life { get; set; } = 100;
    public bool IsAlive => Life > 0;
    public bool IsCrime { get; set; }
    public bool IsPker { get; set; }
    public bool IsVirtuous { get; set; }
    public bool IsLurker { get; set; }

    public NpcUser(uint id, uint mapId, ushort posX, ushort posY)
    {
        Id = id;
        MapId = mapId;
        PosX = posX;
        PosY = posY;
    }
}
