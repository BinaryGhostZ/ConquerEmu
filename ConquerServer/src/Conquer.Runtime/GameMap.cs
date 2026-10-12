// Conquer.Runtime — GameMap: spatial partitioned map instance.
// Original: MapGroupKernel/GameMap.h, GameMap.cpp CGameMap
// OPUS §4.1: GameMap.Broadcast over the 3x3 cells (18-cell blocks)
// OPUS §4.2: Entity position changes ONLY through GameMap.Move

using Conquer.Data;
using Conquer.Shared;
using Conquer.Shared.Messages.Packets;

namespace Conquer.Runtime;

/// <summary>
/// A game map with 18x18 block spatial partitioning and collision grid.
/// <para><b>Original:</b> MapGroupKernel/GameMap.h CGameMap</para>
/// </summary>
public sealed class GameMap
{
    private readonly GameBlock[,] _blocks;
    private readonly Dictionary<uint, IWorldEntity> _entities = [];
    private readonly Dictionary<uint, GroundItem> _groundItems = [];

    public uint Id { get; }
    public string Name { get; }
    public MapModel Data { get; }
    public CellMap Grid { get; }

    public int WidthInBlocks { get; }
    public int HeightInBlocks { get; }

    public IReadOnlyDictionary<uint, IWorldEntity> Entities => _entities;
    public IReadOnlyDictionary<uint, GroundItem> GroundItems => _groundItems;

    public bool AddGroundItem(GroundItem groundItem)
    {
        _groundItems[groundItem.Id] = groundItem;
        int bx = Block(groundItem.PosX);
        int by = Block(groundItem.PosY);
        GetBlock(bx, by)?.AddGroundItem(groundItem.Id);
        return true;
    }

    public bool RemoveGroundItem(uint id, out GroundItem? groundItem)
    {
        if (_groundItems.Remove(id, out groundItem))
        {
            int bx = Block(groundItem.PosX);
            int by = Block(groundItem.PosY);
            GetBlock(bx, by)?.RemoveGroundItem(id);
            return true;
        }
        return false;
    }

    public GroundItem? GetGroundItem(uint id) => _groundItems.GetValueOrDefault(id);

    public GameMap(MapModel data, CellMap grid)
    {
        Id = data.Id;
        Name = string.IsNullOrEmpty(data.Name) ? $"Map_{data.Id}" : data.Name;
        Data = data;
        Grid = grid;

        // Original: GameMap.h GetWidthOfBlock / GetHeightOfBlock
        // (m_pMapData->GetMapWidth() - 1) / CELLS_PER_BLOCK + 1
        WidthInBlocks = (grid.Width - 1) / TimingConstants.CellsPerBlock + 1;
        HeightInBlocks = (grid.Height - 1) / TimingConstants.CellsPerBlock + 1;

        _blocks = new GameBlock[WidthInBlocks, HeightInBlocks];
        for (int x = 0; x < WidthInBlocks; x++)
        {
            for (int y = 0; y < HeightInBlocks; y++)
            {
                _blocks[x, y] = new GameBlock(x, y);
            }
        }
    }

    /// <summary>Convert tile coordinate to block coordinate.</summary>
    public static int Block(int pos) => MapCoordinates.ToBlock(pos);

    /// <summary>Get the block at (blockX, blockY), or null if out of range.</summary>
    public GameBlock? GetBlock(int blockX, int blockY)
    {
        if ((uint)blockX < (uint)WidthInBlocks && (uint)blockY < (uint)HeightInBlocks)
            return _blocks[blockX, blockY];
        return null;
    }

    /// <summary>
    /// Get the 3x3 block neighborhood around a block coordinate.
    /// <para>Original: FOR_9_BLOCKS macro in GameMap.h</para>
    /// </summary>
    public List<GameBlock> GetBlocksIn9Grid(int blockX, int blockY)
    {
        var list = new List<GameBlock>(9);
        int minX = Math.Max(0, blockX - 1);
        int maxX = Math.Min(WidthInBlocks - 1, blockX + 1);
        int minY = Math.Max(0, blockY - 1);
        int maxY = Math.Min(HeightInBlocks - 1, blockY + 1);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                list.Add(_blocks[x, y]);
            }
        }
        return list;
    }

    /// <summary>
    /// Add an entity to the map at (x, y). Broadcasts spawn to surrounding observers
    /// and sends existing entities in the 9-block view to arriving players.
    /// <para>Original: GameMap.cpp EnterRoom / User.cpp EnterMap</para>
    /// </summary>
    public bool Enter(IWorldEntity entity, int x, int y)
    {
        if (!Grid.IsWalkable(x, y))
            return false;

        entity.MapId = Id;
        entity.PosX = x;
        entity.PosY = y;

        int bx = Block(x);
        int by = Block(y);
        var block = GetBlock(bx, by);
        if (block == null)
            return false;

        block.Add(entity);
        _entities[entity.Id] = entity;

        var blocks = GetBlocksIn9Grid(bx, by);

        // 1. If entity is a Player, send all existing entities in 9 blocks to this player
        if (entity is Player enteringPlayer)
        {
            foreach (var b in blocks)
            {
                foreach (var other in b.Entities)
                {
                    if (other.Id == enteringPlayer.Id)
                        continue;

                    if (other is Player otherPlayer)
                    {
                        var spawn = otherPlayer.CreateSpawnPacket();
                        var writer = new PacketWriter();
                        try
                        {
                            spawn.Encode(ref writer);
                            enteringPlayer.Send(writer.AsSpan());
                        }
                        finally
                        {
                            writer.Dispose();
                        }
                    }
                    else if (other is Monster otherMonster)
                    {
                        var spawn = otherMonster.CreateSpawnPacket();
                        var writer = new PacketWriter();
                        try
                        {
                            spawn.Encode(ref writer);
                            enteringPlayer.Send(writer.AsSpan());
                        }
                        finally
                        {
                            writer.Dispose();
                        }
                    }
                }
            }
        }

        // 2. Broadcast entity spawn to observers in the 9 blocks
        if (entity is Player p)
        {
            var spawnPacket = p.CreateSpawnPacket();
            var writer = new PacketWriter();
            try
            {
                spawnPacket.Encode(ref writer);
                BroadcastSpan(blocks, writer.AsSpan(), excludeEntityId: p.Id);
            }
            finally
            {
                writer.Dispose();
            }
        }
        else if (entity is Monster m)
        {
            var spawnPacket = m.CreateSpawnPacket();
            var writer = new PacketWriter();
            try
            {
                spawnPacket.Encode(ref writer);
                BroadcastSpan(blocks, writer.AsSpan(), excludeEntityId: m.Id);
            }
            finally
            {
                writer.Dispose();
            }
        }

        return true;
    }

    /// <summary>
    /// Remove an entity from the map. Broadcasts disappear to surrounding observers.
    /// <para>Original: GameMap.cpp LeaveRoom</para>
    /// </summary>
    public bool Leave(IWorldEntity entity)
    {
        if (!_entities.Remove(entity.Id))
            return false;

        int bx = Block(entity.PosX);
        int by = Block(entity.PosY);
        var block = GetBlock(bx, by);
        block?.Remove(entity);

        // Broadcast disappear packet to 9 blocks
        var blocks = GetBlocksIn9Grid(bx, by);
        var disappear = MsgAction.CreateDisappear(entity.Id);
        var writer = new PacketWriter();
        try
        {
            disappear.Encode(ref writer);
            BroadcastSpan(blocks, writer.AsSpan(), excludeEntityId: entity.Id);
        }
        finally
        {
            writer.Dispose();
        }

        return true;
    }

    /// <summary>
    /// Move an entity to a new position.
    /// <para><b>CRITICAL RULE 5 (OPUS §4.2):</b> Entity position changes ONLY through GameMap.Move.</para>
    /// <para>Original: GameMap.cpp CGameMap::MoveTo</para>
    /// </summary>
    public bool Move(IWorldEntity entity, int newX, int newY, ReadOnlySpan<byte> movementPacket = default)
    {
        if (!Grid.IsWalkable(newX, newY))
            return false;

        int oldBx = Block(entity.PosX);
        int oldBy = Block(entity.PosY);
        int newBx = Block(newX);
        int newBy = Block(newY);

        if (oldBx == newBx && oldBy == newBy)
        {
            // Position changed within the same block
            entity.PosX = newX;
            entity.PosY = newY;

            if (!movementPacket.IsEmpty)
            {
                var blocks = GetBlocksIn9Grid(oldBx, oldBy);
                BroadcastSpan(blocks, movementPacket, excludeEntityId: entity.Id);
            }
            return true;
        }

        // Block changed: perform view set differential (entered, left, common)
        var oldBlocks = GetBlocksIn9Grid(oldBx, oldBy);
        var newBlocks = GetBlocksIn9Grid(newBx, newBy);

        GetBlock(oldBx, oldBy)?.Remove(entity);
        GetBlock(newBx, newBy)?.Add(entity);

        entity.PosX = newX;
        entity.PosY = newY;

        var oldSet = new HashSet<GameBlock>(oldBlocks);
        var newSet = new HashSet<GameBlock>(newBlocks);

        // Blocks that lost visibility of this entity
        var leftBlocks = oldBlocks.Where(b => !newSet.Contains(b)).ToList();
        // Blocks that gained visibility of this entity
        var enteredBlocks = newBlocks.Where(b => !oldSet.Contains(b)).ToList();
        // Blocks that maintained visibility
        var commonBlocks = oldBlocks.Where(b => newSet.Contains(b)).ToList();

        // 1. Send disappear to left blocks
        var disappear = MsgAction.CreateDisappear(entity.Id);
        var disWriter = new PacketWriter();
        try
        {
            disappear.Encode(ref disWriter);
            BroadcastSpan(leftBlocks, disWriter.AsSpan(), excludeEntityId: entity.Id);
        }
        finally
        {
            disWriter.Dispose();
        }

        // If moving entity is a Player, also tell them that entities in leftBlocks disappeared
        if (entity is Player movingPlayer)
        {
            foreach (var b in leftBlocks)
            {
                foreach (var other in b.Entities)
                {
                    if (other.Id == movingPlayer.Id) continue;
                    var otherDis = MsgAction.CreateDisappear(other.Id);
                    var w = new PacketWriter();
                    try
                    {
                        otherDis.Encode(ref w);
                        movingPlayer.Send(w.AsSpan());
                    }
                    finally
                    {
                        w.Dispose();
                    }
                }
            }
        }

        // 2. Send spawn to newly entered blocks
        if (entity is Player mover)
        {
            var spawn = mover.CreateSpawnPacket();
            var spawnWriter = new PacketWriter();
            try
            {
                spawn.Encode(ref spawnWriter);
                BroadcastSpan(enteredBlocks, spawnWriter.AsSpan(), excludeEntityId: mover.Id);
            }
            finally
            {
                spawnWriter.Dispose();
            }

            // Tell moving player about entities in enteredBlocks
            foreach (var b in enteredBlocks)
            {
                foreach (var other in b.Entities)
                {
                    if (other.Id == mover.Id) continue;
                    if (other is Player otherPlayer)
                    {
                        var otherSpawn = otherPlayer.CreateSpawnPacket();
                        var w = new PacketWriter();
                        try
                        {
                            otherSpawn.Encode(ref w);
                            mover.Send(w.AsSpan());
                        }
                        finally
                        {
                            w.Dispose();
                        }
                    }
                }
            }
        }

        // 3. Send movement packet to common blocks
        if (!movementPacket.IsEmpty)
        {
            BroadcastSpan(commonBlocks, movementPacket, excludeEntityId: entity.Id);
        }

        return true;
    }

    /// <summary>
    /// Broadcast a packet to all observing players within the 9-block grid around (x, y).
    /// <para>Original: GameMap.cpp BroadcastBlockMsg(nPosX, nPosY, pMsg)</para>
    /// </summary>
    public void Broadcast(int x, int y, ReadOnlySpan<byte> packet, uint excludeEntityId = 0)
    {
        var blocks = GetBlocksIn9Grid(Block(x), Block(y));
        BroadcastSpan(blocks, packet, excludeEntityId);
    }

    private static void BroadcastSpan(List<GameBlock> blocks, ReadOnlySpan<byte> packet, uint excludeEntityId = 0)
    {
        foreach (var block in blocks)
        {
            foreach (var entity in block.Entities)
            {
                if (entity is Player player && player.Id != excludeEntityId)
                {
                    player.Send(packet);
                }
            }
        }
    }
}
