// Conquer.Runtime — Monster entity.
// Original: MapGroupKernel/AINpc.h, AINpc.cpp CMonster

using Conquer.Shared;
using Conquer.Shared.Messages.Packets;

namespace Conquer.Runtime;

/// <summary>
/// A monster entity occupying a position in a GameMap within a MapGroup.
/// <para>
/// <b>Original:</b> MapGroupKernel/AINpc.h CMonster
/// </para>
/// </summary>
public class Monster : IWorldEntity
{
    public uint Id { get; }
    public string Name { get; }
    public uint TypeId { get; init; }
    public uint MapId { get; set; }
    public int PosX { get; set; }
    public int PosY { get; set; }
    public Direction Direction { get; set; } = Direction.South;
    public bool IsPlayer => false;

    public ushort Level { get; set; } = 1;
    public ushort Life { get; set; } = 100;
    public ushort MaxLife { get; set; } = 100;
    public ushort Mana { get; set; } = 0;
    public ushort MaxMana { get; set; } = 0;
    public uint LookFace { get; set; } = 1;

    // Combat stats (IWorldEntity)
    public int MinAtk { get; set; } = 10;
    public int MaxAtk { get; set; } = 20;
    public int Defense { get; set; } = 5;
    public int MagicDef { get; set; } = 0;
    public int AttackRange { get; set; } = CombatConstants.DefaultMeleeRange;
    public int AttackSpeedMs { get; set; } = CombatConstants.DefaultAttackSpeedMs;
    public uint TargetId { get; set; }
    public DateTime LastAttackTime { get; set; } = DateTime.MinValue;
    public bool IsAlive => Life > 0;

    /// <summary>Status effects and keep effect flags manager.</summary>
    public StatusManager Statuses { get; } = new();

    /// <summary>Average money (silver) dropped on death.</summary>
    public uint DropMoney { get; set; }

    /// <summary>Dormancy flag. If true, monster does not execute AI until awakened.</summary>
    public bool IsDormant { get; set; } = true;

    public Monster(uint id, string name, uint typeId = 0)
    {
        Id = id;
        Name = name;
        TypeId = typeId;
    }

    /// <summary>Create a MsgPlayer spawn packet for broadcasting to observers.</summary>
    public MsgPlayer CreateSpawnPacket()
    {
        return new MsgPlayer(
            Id,
            LookFace,
            (uint)(Statuses.Flags & (KeepEffectFlags)0xFFFFFFFF),
            0,
            0,
            0,
            MaxLife,
            Life,
            Level,
            (ushort)PosX,
            (ushort)PosY,
            0,
            (byte)Direction,
            100, // _ACTION_STANDBY
            Name);
    }
}
