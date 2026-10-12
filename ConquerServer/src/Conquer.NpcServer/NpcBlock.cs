// Conquer.NpcServer — Spatial partition block for dormancy and perception.
// Original: my_server/NpcKernel/GameBlock.h

namespace Conquer.NpcServer;

/// <summary>
/// 18x18-cell map block for dormancy management and spatial hashing.
/// Original: my_server/NpcKernel/GameBlock.h CGameBlock.
/// </summary>
public sealed class NpcBlock
{
    public int BlockX { get; }
    public int BlockY { get; }

    /// <summary>
    /// Gets or sets whether this block is dormant.
    /// Original: m_bDormancy.
    /// </summary>
    public bool IsDormant { get; set; } = true;

    /// <summary>Users currently standing in this 18x18 block.</summary>
    public HashSet<uint> Users { get; } = [];

    /// <summary>NPCs currently standing in this 18x18 block.</summary>
    public HashSet<uint> Npcs { get; } = [];

    public NpcBlock(int blockX, int blockY)
    {
        BlockX = blockX;
        BlockY = blockY;
    }
}
