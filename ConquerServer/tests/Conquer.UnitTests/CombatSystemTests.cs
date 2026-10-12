// Conquer.UnitTests — Tests for Combat, MsgInteract, BattleEngine, and PK/Crime constants.

using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages.Packets;
using Xunit;

namespace Conquer.UnitTests;

public class CombatSystemTests
{
    private sealed class MockEntity : IWorldEntity
    {
        public uint Id { get; init; }
        public string Name { get; init; } = "Mock";
        public uint MapId { get; set; } = 1002;
        public int PosX { get; set; } = 100;
        public int PosY { get; set; } = 100;
        public Direction Direction { get; set; } = Direction.South;
        public bool IsPlayer { get; init; }

        public ushort Level { get; set; } = 1;
        public ushort Life { get; set; } = 100;
        public ushort MaxLife { get; set; } = 100;
        public ushort Mana { get; set; } = 100;
        public ushort MaxMana { get; set; } = 100;
        public int MagicDef { get; set; } = 0;
        public StatusManager Statuses { get; } = new();
        public int MinAtk { get; set; } = 10;
        public int MaxAtk { get; set; } = 20;
        public int Defense { get; set; } = 5;
        public int AttackRange { get; set; } = 1;
        public int AttackSpeedMs { get; set; } = 1000;
        public uint TargetId { get; set; }
        public DateTime LastAttackTime { get; set; } = DateTime.MinValue;
    }

    [Fact]
    public void MsgInteract_EncodeDecode_RoundTrip()
    {
        var original = MsgInteract.CreateAttack(
            senderId: 100001,
            targetId: 200002,
            posX: 420,
            posY: 380,
            damage: 157,
            timestamp: 998877);

        var writer = new PacketWriter();
        try
        {
            original.Encode(ref writer);
            var span = writer.AsSpan();

            var decoded = MsgInteract.Decode(span);

            Assert.Equal(original.Timestamp, decoded.Timestamp);
            Assert.Equal(original.SenderId, decoded.SenderId);
            Assert.Equal(original.TargetId, decoded.TargetId);
            Assert.Equal(original.PosX, decoded.PosX);
            Assert.Equal(original.PosY, decoded.PosY);
            Assert.Equal(original.Type, decoded.Type);
            Assert.Equal(original.Data, decoded.Data);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void MsgInteract_MagicAttack_EncodesMagicTypeAndLevel()
    {
        var msg = MsgInteract.CreateMagicAttack(
            senderId: 1001,
            targetId: 2002,
            posX: 100,
            posY: 200,
            magicType: 1000,
            magicLevel: 4,
            timestamp: 12345);

        Assert.Equal(InteractType.MagicAttack, msg.Type);
        Assert.Equal(1000, msg.MagicType);
        Assert.Equal(4, msg.MagicLevel);
    }

    [Fact]
    public void MsgInteract_CreateKill_ValidFields()
    {
        var msg = MsgInteract.CreateKill(
            killerId: 500,
            targetId: 600,
            posX: 150,
            posY: 250,
            dieWay: (uint)DieMode.Normal,
            timestamp: 777);

        Assert.Equal(InteractType.Kill, msg.Type);
        Assert.Equal(500u, msg.SenderId);
        Assert.Equal(600u, msg.TargetId);
        Assert.Equal((uint)DieMode.Normal, msg.Data);
    }

    [Fact]
    public void MsgMapItem_EncodeDecode_RoundTrip()
    {
        var original = MsgMapItem.CreateDrop(
            itemId: 40001,
            itemType: 1090020, // Silver bag
            posX: 300,
            posY: 350);

        var writer = new PacketWriter();
        try
        {
            original.Encode(ref writer);
            var span = writer.AsSpan();

            var decoded = MsgMapItem.Decode(span);

            Assert.Equal(original.ItemId, decoded.ItemId);
            Assert.Equal(original.ItemType, decoded.ItemType);
            Assert.Equal(original.PosX, decoded.PosX);
            Assert.Equal(original.PosY, decoded.PosY);
            Assert.Equal(original.Action, decoded.Action);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void BattleEngine_MinMaxAtkRoll_StaysWithinRange()
    {
        var attacker = new MockEntity { MinAtk = 50, MaxAtk = 80, Level = 10, IsPlayer = true };
        var target = new MockEntity { Defense = 0, Level = 10, IsPlayer = true };

        var rng = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            int damage = BattleEngine.CalculateDamage(attacker, target, rng);
            Assert.InRange(damage, 50, 80);
        }
    }

    [Fact]
    public void BattleEngine_DefenseReducesDamage()
    {
        var attacker = new MockEntity { MinAtk = 100, MaxAtk = 100, Level = 10, IsPlayer = true };
        var target = new MockEntity { Defense = 35, Level = 10, IsPlayer = true };

        int damage = BattleEngine.CalculateDamage(attacker, target);
        Assert.Equal(65, damage);
    }

    [Fact]
    public void BattleEngine_DamageFloor_IsAtLeastOne()
    {
        var attacker = new MockEntity { MinAtk = 10, MaxAtk = 15, Level = 1, IsPlayer = true };
        var target = new MockEntity { Defense = 999, Level = 1, IsPlayer = true };

        int damage = BattleEngine.CalculateDamage(attacker, target);
        Assert.Equal(1, damage);
    }

    [Fact]
    public void BattleEngine_LevelAdvantage_IncreasesDamage()
    {
        // Player lev 50 vs Monster lev 20 (delta 30 >= 20 -> 3.0x multiplier)
        var player = new MockEntity { MinAtk = 40, MaxAtk = 40, Level = 50, IsPlayer = true };
        var monster = new MockEntity { Defense = 10, Level = 20, IsPlayer = false };

        int damage = BattleEngine.CalculateDamage(player, monster);
        // rawAtk = 40 * 3.0 = 120, minus def 10 = 110
        Assert.Equal(110, damage);
    }

    [Fact]
    public void CombatConstants_FidelityValues_MatchOriginal()
    {
        Assert.Equal(20, CombatConstants.PkValueOneKill);
        Assert.Equal(21, CombatConstants.PkValueRedName);
        Assert.Equal(101, CombatConstants.PkValueBlackName);
        Assert.Equal(30, CombatConstants.CrimeKeepSecs);
        Assert.Equal(1000, CombatConstants.DefaultAttackSpeedMs);
        Assert.Equal(1, CombatConstants.DefaultMeleeRange);
    }
}
