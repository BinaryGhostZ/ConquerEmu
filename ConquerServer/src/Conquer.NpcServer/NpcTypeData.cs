// Conquer.NpcServer — Monster type template data.
// Original: my_server/NpcKernel/NpcType.h

namespace Conquer.NpcServer;

/// <summary>
/// Immutable template attributes for a monster archetype.
/// Original: my_server/NpcKernel/NpcType.h NPCTYPEDATA_* constants.
/// </summary>
public sealed class NpcTypeData
{
    public ushort TypeId { get; init; }
    public string Name { get; init; } = "";
    public int Life { get; init; } = 100;
    public int Mana { get; init; } = 0;
    public int MinAtk { get; init; } = 10;
    public int MaxAtk { get; init; } = 20;
    public int Defense { get; init; } = 5;
    public int ViewRange { get; init; } = 10;
    public int AttackRange { get; init; } = 1;
    public int AttackSpeedMs { get; init; } = 1000;
    public int MoveSpeedMs { get; init; } = 800;
    public int RunSpeedMs { get; init; } = 500;
    public bool AtkUserActive { get; init; } = true;
    public bool EscapeEnable { get; init; } = false;
    public bool IsGuard { get; init; } = false;
    public bool IsPkKiller { get; init; } = false;
}
