// Conquer.NpcServer — Map partition and dormancy engine.
// Original: my_server/NpcKernel/GameMap.cpp

using Conquer.Runtime;
using Conquer.Shared;

namespace Conquer.NpcServer;

/// <summary>
/// Map copy maintained by NpcServer with 18x18-cell dormancy grid.
/// Original: my_server/NpcKernel/GameMap.cpp.
/// </summary>
public sealed class NpcWorldMap
{
    public uint MapId { get; }
    public int Width { get; }
    public int Height { get; }

    private readonly NpcBlock[,] _blocks;
    private readonly int _blockCountX;
    private readonly int _blockCountY;

    public NpcWorldMap(uint mapId, int width = 1000, int height = 1000)
    {
        MapId = mapId;
        Width = width;
        Height = height;

        _blockCountX = Math.Max(1, (width + TimingConstants.CellsPerBlock - 1) / TimingConstants.CellsPerBlock);
        _blockCountY = Math.Max(1, (height + TimingConstants.CellsPerBlock - 1) / TimingConstants.CellsPerBlock);
        _blocks = new NpcBlock[_blockCountX, _blockCountY];

        for (int x = 0; x < _blockCountX; x++)
        {
            for (int y = 0; y < _blockCountY; y++)
            {
                _blocks[x, y] = new NpcBlock(x, y);
            }
        }
    }

    public int ToBlockX(int x) => Math.Clamp(x / TimingConstants.CellsPerBlock, 0, _blockCountX - 1);
    public int ToBlockY(int y) => Math.Clamp(y / TimingConstants.CellsPerBlock, 0, _blockCountY - 1);

    public NpcBlock GetBlock(int x, int y) => _blocks[ToBlockX(x), ToBlockY(y)];

    /// <summary>
    /// Gets the up to 9 blocks surrounding the block containing (x, y).
    /// </summary>
    public List<NpcBlock> GetBlocksIn9Grid(int x, int y)
    {
        int bx = ToBlockX(x);
        int by = ToBlockY(y);
        List<NpcBlock> result = new(9);

        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = bx + dx;
            if (nx < 0 || nx >= _blockCountX)
                continue;

            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = by + dy;
                if (ny < 0 || ny >= _blockCountY)
                    continue;

                result.Add(_blocks[nx, ny]);
            }
        }
        return result;
    }

    /// <summary>
    /// Checks if a block can be marked dormant (no players in 9 blocks around it).
    /// Original: GameMap.cpp:169 CGameMap::SetDormancy.
    /// </summary>
    public bool SetDormancy(int posX, int posY)
    {
        NpcBlock currentBlock = GetBlock(posX, posY);
        if (currentBlock.IsDormant)
            return true;

        var blocks9 = GetBlocksIn9Grid(posX, posY);
        foreach (var block in blocks9)
        {
            if (block.Users.Count > 0)
                return false;
        }

        currentBlock.IsDormant = true;
        return true;
    }

    /// <summary>
    /// Wakes up monsters across the 9 blocks around (posX, posY).
    /// Original: GameMap.cpp:390 CGameMap::CallWakeMonster.
    /// </summary>
    public void CallWakeMonster(int posX, int posY, Action<uint> wakeNpcAction)
    {
        var blocks9 = GetBlocksIn9Grid(posX, posY);
        foreach (var block in blocks9)
        {
            if (block.IsDormant)
            {
                block.IsDormant = false;
                foreach (var npcId in block.Npcs)
                {
                    wakeNpcAction(npcId);
                }
            }
        }
    }

    public void EnterUser(NpcUser user, Action<uint> wakeNpcAction)
    {
        GetBlock(user.PosX, user.PosY).Users.Add(user.Id);
        CallWakeMonster(user.PosX, user.PosY, wakeNpcAction);
    }

    public void LeaveUser(NpcUser user)
    {
        GetBlock(user.PosX, user.PosY).Users.Remove(user.Id);
    }

    public void MoveUser(NpcUser user, ushort newX, ushort newY, Action<uint> wakeNpcAction)
    {
        int oldBx = ToBlockX(user.PosX);
        int oldBy = ToBlockY(user.PosY);
        int newBx = ToBlockX(newX);
        int newBy = ToBlockY(newY);

        if (oldBx != newBx || oldBy != newBy)
        {
            _blocks[oldBx, oldBy].Users.Remove(user.Id);
            _blocks[newBx, newBy].Users.Add(user.Id);
        }

        user.PosX = newX;
        user.PosY = newY;
        CallWakeMonster(newX, newY, wakeNpcAction);
    }

    public void EnterNpc(uint npcId, int x, int y)
    {
        GetBlock(x, y).Npcs.Add(npcId);
    }

    public void LeaveNpc(uint npcId, int x, int y)
    {
        GetBlock(x, y).Npcs.Remove(npcId);
    }

    public void MoveNpc(uint npcId, int oldX, int oldY, int newX, int newY)
    {
        int oldBx = ToBlockX(oldX);
        int oldBy = ToBlockY(oldY);
        int newBx = ToBlockX(newX);
        int newBy = ToBlockY(newY);

        if (oldBx != newBx || oldBy != newBy)
        {
            _blocks[oldBx, oldBy].Npcs.Remove(npcId);
            _blocks[newBx, newBy].Npcs.Add(npcId);
        }
    }

    public bool IsValidPoint(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    public bool IsStandEnable(int x, int y) => IsValidPoint(x, y);

    public bool IsMoveEnable(int x, int y, Direction dir)
    {
        var (dx, dy) = DirectionOffsets.GetOffset(dir);
        return IsValidPoint(x + dx, y + dy);
    }
}
