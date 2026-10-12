// Conquer.NpcServer — Monster generator / spawner.
// Original: my_server/NpcKernel/NpcGenerator.cpp

using Conquer.Shared.Messages.Packets;

namespace Conquer.NpcServer;

/// <summary>
/// Monster spawner corresponding to cq_generator.
/// Controls spawn frequency, boundary limits, and active entity budgets.
/// Original: my_server/NpcKernel/NpcGenerator.cpp CNpcGenerator.
/// </summary>
public sealed class NpcGenerator
{
    public uint Id { get; init; }
    public uint MapId { get; init; }
    public ushort BoundX { get; init; }
    public ushort BoundY { get; init; }
    public ushort BoundCX { get; init; }
    public ushort BoundCY { get; init; }
    public ushort Grid { get; init; } = 1;
    public int RestSecs { get; init; } = 10;
    public int MaxPerGen { get; init; } = 5;
    public ushort NpcType { get; init; }
    public ushort BornX { get; init; }
    public ushort BornY { get; init; }

    public int MaxNpc { get; }
    public int Amount { get; private set; }

    private int _currGen;
    private long _lastRestTime;
    private int _lastGenIndex;

    public NpcGenerator(
        uint id,
        uint mapId,
        ushort boundX,
        ushort boundY,
        ushort boundCX,
        ushort boundCY,
        ushort npcType,
        ushort grid = 1,
        int restSecs = 10,
        int maxPerGen = 5,
        ushort bornX = 0,
        ushort bornY = 0)
    {
        Id = id;
        MapId = mapId;
        BoundX = boundX;
        BoundY = boundY;
        BoundCX = boundCX;
        BoundCY = boundCY;
        NpcType = npcType;
        Grid = Math.Max((ushort)1, grid);
        RestSecs = restSecs;
        MaxPerGen = maxPerGen;
        BornX = bornX;
        BornY = bornY;

        int nGrid = Grid >= 1 ? Grid : 1;
        MaxNpc = Math.Max(1, (BoundCX / nGrid) * (BoundCY / nGrid));
        _currGen = MaxPerGen;
    }

    public (int X, int Y) GetCenter() =>
        (BoundX + BoundCX / 2, BoundY + BoundCY / 2);

    public bool IsInRegion(int x, int y) =>
        x >= BoundX && x < BoundX + BoundCX && y >= BoundY && y < BoundY + BoundCY;

    public bool IsTooFar(int x, int y, int range) =>
        !(x >= BoundX - range && x < BoundX + BoundCX + range &&
          y >= BoundY - range && y < BoundY + BoundCY + range);

    public (ushort X, ushort Y) FindGenPos(Random random)
    {
        if (BornX > 0 || BornY > 0)
            return (BornX, BornY);

        if (MaxNpc <= 1 || Grid <= 0)
        {
            ushort rx = (ushort)(BoundX + random.Next(Math.Max(1, (int)BoundCX)));
            ushort ry = (ushort)(BoundY + random.Next(Math.Max(1, (int)BoundCY)));
            return (rx, ry);
        }

        _lastGenIndex = (_lastGenIndex + 1) % MaxNpc;
        int nGridX = Math.Max(1, BoundCX / Grid);
        int cellX = _lastGenIndex % nGridX;
        int cellY = _lastGenIndex / nGridX;

        ushort x = (ushort)(BoundX + Grid * cellX + random.Next(Grid));
        ushort y = (ushort)(BoundY + Grid * cellY + random.Next(Grid));
        return (x, y);
    }

    /// <summary>
    /// Executes a generation step with a given spawn budget.
    /// Original: CNpcGenerator::Generator(int nNum).
    /// </summary>
    public int Generator(
        long nowMs,
        int spawnBudget,
        Func<uint> idGenerator,
        Action<MsgAiNpcInfo> onSpawn,
        Random? random = null)
    {
        random ??= Random.Shared;

        if (Amount >= MaxNpc)
            return 0;

        if (nowMs - _lastRestTime >= RestSecs * 1000L)
        {
            _lastRestTime = nowMs;
            _currGen = MaxPerGen;
        }

        if (_currGen <= 0)
            return 0;

        int spawned = 0;
        int toSpawn = Math.Min(spawnBudget, Math.Min(_currGen, MaxNpc - Amount));

        for (int i = 0; i < toSpawn; i++)
        {
            var (posX, posY) = FindGenPos(random);
            uint newNpcId = idGenerator();
            _currGen--;
            Amount++;
            spawned++;

            var info = new MsgAiNpcInfo(
                Action: AiNpcAction.CreateNew,
                NpcId: newNpcId,
                GenId: Id,
                NpcType: NpcType,
                MapId: MapId,
                PosX: posX,
                PosY: posY);

            onSpawn(info);

            if (_currGen <= 0 || Amount >= MaxNpc)
                break;
        }

        return spawned;
    }

    public void OnNpcDeath(uint npcId)
    {
        if (Amount > 0)
            Amount--;
    }
}
