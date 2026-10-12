// Conquer.Runtime — Player entity for MapGroup.
// Original: MapGroupKernel/User.h, User.cpp CUser

using Conquer.Net;
using Conquer.Shared;
using Conquer.Shared.Messages.Packets;

namespace Conquer.Runtime;

/// <summary>
/// A player character active within a MapGroup.
/// <para><b>Original:</b> MapGroupKernel/User.h CUser</para>
/// </summary>
public class Player : IWorldEntity
{
    public uint Id { get; }
    public string Name { get; }
    public uint AccountId { get; init; }
    public uint MapId { get; set; }
    public int PosX { get; set; }
    public int PosY { get; set; }
    public Direction Direction { get; set; } = Direction.South;
    public bool IsPlayer => true;

    public ushort Level { get; set; } = 1;
    public byte Profession { get; set; } = 10;
    public byte Sex { get; set; } = 1;
    public uint Money { get; set; }
    public ushort Life { get; set; } = 100;
    public ushort MaxLife { get; set; } = 100;
    public ushort Mana { get; set; } = 100;
    public ushort MaxMana { get; set; } = 100;
    public uint LookFace { get; set; } = 1;
    public ushort Hair { get; set; } = 10;
    public byte Pose { get; set; } = 100; // Original: _ACTION_STANDBY = 100
    public uint Status { get; set; }
    public uint SynIdRank { get; set; }
    public uint ArmorType { get; set; }
    public uint WeaponRType { get; set; }

    // Inventory, Equipment, Status, and Magic
    public InventoryPackage Package { get; } = new();
    public EquipmentManager Equipment { get; } = new();
    public StatusManager Statuses { get; } = new();
    public MagicManager Magics { get; } = new();

    // Base attributes before equipment bonuses
    public int BaseMinAtk { get; set; } = 10;
    public int BaseMaxAtk { get; set; } = 20;
    public int BaseDefense { get; set; } = 5;
    public ushort BaseMaxLife { get; set; } = 100;
    public ushort BaseMaxMana { get; set; } = 100;
    public int BaseMagicDef { get; set; } = 0;

    // Combat attributes (IWorldEntity)
    public int MinAtk { get; set; } = 10;
    public int MaxAtk { get; set; } = 20;
    public int Defense { get; set; } = 5;
    public int MagicDef { get; set; } = 0;
    public int AttackRange { get; set; } = CombatConstants.DefaultMeleeRange;
    public int AttackSpeedMs { get; set; } = CombatConstants.DefaultAttackSpeedMs;
    public uint TargetId { get; set; }
    public DateTime LastAttackTime { get; set; } = DateTime.MinValue;
    public bool IsAlive => Life > 0;

    /// <summary>
    /// Recalculates effective combat attributes from base stats plus equipped item bonuses.
    /// Original: User.cpp CUser::RecalcAttr.
    /// </summary>
    public void RecalculateStats()
    {
        MinAtk = BaseMinAtk + Equipment.BonusMinAtk;
        MaxAtk = BaseMaxAtk + Equipment.BonusMaxAtk;
        Defense = BaseDefense + Equipment.BonusDefense;
        MagicDef = BaseMagicDef;
        MaxLife = (ushort)(BaseMaxLife + Equipment.BonusLife);
        Life = Math.Min(Life, MaxLife);
        MaxMana = BaseMaxMana;
        Mana = Math.Min(Mana, MaxMana);
        AttackRange = Equipment.GetAttackRange();
        AttackSpeedMs = Equipment.GetAttackSpeedMs();

        if (Statuses.HasStatus(StatusEffectType.Attack) || Statuses.HasStatus(StatusEffectType.SuperAtk))
        {
            MinAtk = (int)(MinAtk * 1.3);
            MaxAtk = (int)(MaxAtk * 1.3);
        }

        if (Statuses.HasStatus(StatusEffectType.Defense1) || Statuses.HasStatus(StatusEffectType.Shield) || Statuses.HasStatus(StatusEffectType.SuperDef))
        {
            Defense = (int)(Defense * 1.3);
        }

        if (Statuses.HasStatus(StatusEffectType.MagicDefense))
        {
            MagicDef = (int)(MagicDef * 1.3);
        }

        if (Statuses.HasStatus(StatusEffectType.AtkSpeed))
        {
            AttackSpeedMs = Math.Max(200, (int)(AttackSpeedMs * 0.7));
        }

        var armor = Equipment.GetEquip(ItemPosition.Armor);
        ArmorType = armor?.Type ?? 0;

        var weaponR = Equipment.GetEquip(ItemPosition.WeaponR);
        WeaponRType = weaponR?.Type ?? 0;

        Status = (uint)(Statuses.Flags & (KeepEffectFlags)0xFFFFFFFF);
    }

    // PK and crime state
    public int PkPoints { get; set; }
    public DateTime? CrimeExpireTime { get; set; }
    public bool IsEvil => PkPoints >= CombatConstants.PkValueRedName || (CrimeExpireTime.HasValue && CrimeExpireTime.Value > DateTime.UtcNow);

    /// <summary>Network socket ID for routing.</summary>
    public int SocketId { get; set; }

    /// <summary>Network socket for client I/O. Null if disconnected.</summary>
    public IGameSocket? Socket { get; set; }

    /// <summary>Timestamp of last client heartbeat packet received (MsgTick).</summary>
    public DateTime LastClientHeartbeat { get; set; }

    /// <summary>Timestamp of last server heartbeat packet sent (MsgTick).</summary>
    public DateTime LastServerHeartbeat { get; set; }

    public Player(uint id, string name, uint accountId)
    {
        Id = id;
        Name = name;
        AccountId = accountId;
        LastClientHeartbeat = DateTime.UtcNow;
        LastServerHeartbeat = DateTime.UtcNow;
    }

    /// <summary>Send a packet to the player's client socket.</summary>
    public void Send(ReadOnlySpan<byte> packet)
    {
        Socket?.Send(packet);
    }

    /// <summary>Disconnect the player socket.</summary>
    public void Kick(string reason)
    {
        Socket?.Disconnect();
    }

    /// <summary>Create a MsgPlayer spawn packet for broadcasting to observers.</summary>
    public MsgPlayer CreateSpawnPacket()
    {
        return new MsgPlayer(
            Id,
            LookFace,
            Status,
            SynIdRank,
            ArmorType,
            WeaponRType,
            MaxLife,
            Life,
            Level,
            (ushort)PosX,
            (ushort)PosY,
            Hair,
            (byte)Direction,
            Pose,
            Name);
    }
}
