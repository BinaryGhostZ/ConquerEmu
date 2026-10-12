// Conquer.Runtime — GameBlock spatial partition (18x18 cells).
// Original: MapGroupKernel/GameBlock.h, GameBlock.cpp CGameBlock

namespace Conquer.Runtime;

/// <summary>
/// Spatial partition block ("room") representing an 18x18 cell region on a GameMap.
/// <para><b>Original:</b> MapGroupKernel/GameBlock.h CGameBlock</para>
/// </summary>
public sealed class GameBlock
{
    private readonly HashSet<IWorldEntity> _entities = [];
    private readonly HashSet<uint> _groundItems = [];
    private int _playerCount;

    public int BlockX { get; }
    public int BlockY { get; }

    /// <summary>
    /// Dormancy flag: when no players are nearby for DORMANCY_SECS, monsters sleep.
    /// <para>Original: CGameBlock::m_bDormancy</para>
    /// </summary>
    public bool IsDormant { get; set; } = true;

    /// <summary>Number of players currently inside this block.</summary>
    public int PlayerCount => _playerCount;

    /// <summary>All entities currently in this block.</summary>
    public IEnumerable<IWorldEntity> Entities => _entities;

    /// <summary>IDs of ground items currently in this block.</summary>
    public IEnumerable<uint> GroundItemIds => _groundItems;

    public GameBlock(int blockX, int blockY)
    {
        BlockX = blockX;
        BlockY = blockY;
    }

    /// <summary>Add an entity to this block.</summary>
    public bool Add(IWorldEntity entity)
    {
        if (_entities.Add(entity))
        {
            if (entity.IsPlayer)
            {
                _playerCount++;
                IsDormant = false;
            }
            return true;
        }
        return false;
    }

    /// <summary>Remove an entity from this block.</summary>
    public bool Remove(IWorldEntity entity)
    {
        if (_entities.Remove(entity))
        {
            if (entity.IsPlayer)
            {
                _playerCount = Math.Max(0, _playerCount - 1);
            }
            return true;
        }
        return false;
    }

    public bool AddGroundItem(uint id) => _groundItems.Add(id);
    public bool RemoveGroundItem(uint id) => _groundItems.Remove(id);
}
