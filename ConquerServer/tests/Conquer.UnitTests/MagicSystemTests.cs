// Conquer.UnitTests — Unit tests for Phase 7: Magic, status effects, and combat formulas.
// Tests packet framing, status managers, spellbook progression, and BattleEngine magic damage.

using Conquer.Data;
using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages;
using Conquer.Shared.Messages.Packets;

namespace Conquer.UnitTests;

public class MagicSystemTests
{
    private sealed class MockCombatEntity : IWorldEntity
    {
        public uint Id { get; init; }
        public string Name { get; init; } = "MockCombat";
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
        public int MinAtk { get; set; } = 10;
        public int MaxAtk { get; set; } = 20;
        public int Defense { get; set; } = 5;
        public int MagicDef { get; set; } = 0;
        public int AttackRange { get; set; } = 1;
        public int AttackSpeedMs { get; set; } = 1000;
        public uint TargetId { get; set; }
        public DateTime LastAttackTime { get; set; } = DateTime.MinValue;
        public StatusManager Statuses { get; } = new();
    }

    [Fact]
    public void MsgMagicEffect_SingleTarget_EncodeDecode_RoundTrip()
    {
        var original = MsgMagicEffect.CreateSingle(
            userId: 100001,
            magicType: 1000,
            level: 2,
            targetId: 200002,
            damage: 350,
            direction: 3);

        var writer = new PacketWriter();
        try
        {
            original.Encode(ref writer);
            var span = writer.AsSpan();

            var decoded = MsgMagicEffect.Decode(span);

            Assert.Equal(original.UserId, decoded.UserId);
            Assert.Equal(original.TargetId, decoded.TargetId);
            Assert.Equal(original.MagicType, decoded.MagicType);
            Assert.Equal(original.Level, decoded.Level);
            Assert.Equal(original.Direction, decoded.Direction);
            Assert.Single(decoded.Targets);
            Assert.Equal(200002u, decoded.Targets[0].RoleId);
            Assert.Equal(350u, decoded.Targets[0].Data);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void MsgMagicEffect_Area_EncodeDecode_RoundTrip()
    {
        var targets = new EffectRole[]
        {
            new(200001, 150),
            new(200002, 220),
            new(200003, 305)
        };

        var original = MsgMagicEffect.CreateArea(
            userId: 100001,
            magicType: 1005,
            level: 1,
            posX: 400,
            posY: 450,
            direction: 4,
            targets: targets);

        var writer = new PacketWriter();
        try
        {
            original.Encode(ref writer);
            var span = writer.AsSpan();

            var decoded = MsgMagicEffect.Decode(span);

            Assert.Equal(original.UserId, decoded.UserId);
            Assert.Equal(400, decoded.PosX);
            Assert.Equal(450, decoded.PosY);
            Assert.Equal(original.MagicType, decoded.MagicType);
            Assert.Equal(3, decoded.Targets.Length);
            Assert.Equal(200002u, decoded.Targets[1].RoleId);
            Assert.Equal(220u, decoded.Targets[1].Data);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void MsgMagicInfo_EncodeDecode_RoundTrip()
    {
        var original = MsgMagicInfo.Create(ownerId: 100001, magicType: 1001, level: 3, exp: 5400);

        var writer = new PacketWriter();
        try
        {
            original.Encode(ref writer);
            var span = writer.AsSpan();

            var decoded = MsgMagicInfo.Decode(span);

            Assert.Equal(original.OwnerId, decoded.OwnerId);
            Assert.Equal(original.MagicType, decoded.MagicType);
            Assert.Equal(original.Level, decoded.Level);
            Assert.Equal(original.Exp, decoded.Exp);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void MsgFlushExp_EncodeDecode_RoundTrip()
    {
        var original = MsgFlushExp.CreateMagicExp(magicType: 1002, exp: 1250);

        var writer = new PacketWriter();
        try
        {
            original.Encode(ref writer);
            var span = writer.AsSpan();

            var decoded = MsgFlushExp.Decode(span);

            Assert.Equal(original.MagicType, decoded.MagicType);
            Assert.Equal(original.Exp, decoded.Exp);
            Assert.Equal(original.Action, decoded.Action);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void MsgUserAttrib_LifeManaXpStatus_RoundTrip()
    {
        // 1. Life
        var msgLife = MsgUserAttrib.CreateLife(100001, 750);
        Assert.Single(msgLife.Attributes);
        Assert.Equal(UserAttributeType.Life, msgLife.Attributes[0].Type);
        Assert.Equal(750u, msgLife.Attributes[0].Data);

        // 2. Mana
        var msgMana = MsgUserAttrib.CreateMana(100001, 320);
        Assert.Equal(UserAttributeType.Mana, msgMana.Attributes[0].Type);
        Assert.Equal(320u, msgMana.Attributes[0].Data);

        // 3. XP
        var msgXp = MsgUserAttrib.CreateXp(100001, 85);
        Assert.Equal(UserAttributeType.Xp, msgXp.Attributes[0].Type);
        Assert.Equal(85u, msgXp.Attributes[0].Data);

        // 4. Status 64-bit flag split
        ulong flags = (ulong)KeepEffectFlags.Poison | (ulong)KeepEffectFlags.Fly;
        var msgStatus = MsgUserAttrib.CreateStatus(100001, flags);
        Assert.Equal(2, msgStatus.Attributes.Length);
        Assert.Equal(UserAttributeType.KeepEffect, msgStatus.Attributes[0].Type);
        Assert.Equal((uint)(flags & 0xFFFFFFFF), msgStatus.Attributes[0].Data);
        Assert.Equal(UserAttributeType.KeepEffect2, msgStatus.Attributes[1].Type);
        Assert.Equal((uint)(flags >> 32), msgStatus.Attributes[1].Data);

        var writer = new PacketWriter();
        try
        {
            msgStatus.Encode(ref writer);
            var decoded = MsgUserAttrib.Decode(writer.AsSpan());
            Assert.Equal(100001u, decoded.UserId);
            Assert.Equal(2, decoded.Attributes.Length);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void StatusManager_AttachAndDetach_UpdatesBitmask()
    {
        var manager = new StatusManager();
        Assert.Equal(KeepEffectFlags.Normal, manager.Flags);
        Assert.False(manager.HasStatus(StatusEffectType.Shield));

        // Attach Shield
        manager.AttachStatus(StatusEffectType.Shield, power: 30, durationSecs: 20);
        Assert.True(manager.HasStatus(StatusEffectType.Shield));
        Assert.True(manager.Flags.HasFlag(KeepEffectFlags.Defense1));

        // Attach Fly
        manager.AttachStatus(StatusEffectType.Fly, power: 0, durationSecs: 30);
        Assert.True(manager.HasStatus(StatusEffectType.Fly));
        Assert.True(manager.Flags.HasFlag(KeepEffectFlags.Fly));

        // Detach Shield
        manager.DetachStatus(StatusEffectType.Shield);
        Assert.False(manager.HasStatus(StatusEffectType.Shield));
        Assert.False(manager.Flags.HasFlag(KeepEffectFlags.Defense1));
        Assert.True(manager.Flags.HasFlag(KeepEffectFlags.Fly));
    }

    [Fact]
    public void StatusManager_OnTimer_ExpiresTimedEffects_AndTicksPoison()
    {
        var manager = new StatusManager();
        var now = DateTime.UtcNow;

        manager.AttachStatus(StatusEffectType.Poison, power: 15, durationSecs: 10, intervalMs: 2000, now: now);
        manager.AttachStatus(StatusEffectType.Attack, power: 50, durationSecs: 5, now: now);

        // Tick at now + 2.5s: poison ticks once, Attack not expired yet
        manager.OnTimer(now.AddSeconds(2.5), out var expired1, out var periodic1);
        Assert.Empty(expired1);
        Assert.Single(periodic1);
        Assert.Equal(15, periodic1[0]);

        // Tick at now + 6s: Attack status expires
        manager.OnTimer(now.AddSeconds(6), out var expired2, out _);
        Assert.Contains(StatusEffectType.Attack, expired2);
        Assert.False(manager.HasStatus(StatusEffectType.Attack));

        // Tick at now + 11s: Poison status expires
        manager.OnTimer(now.AddSeconds(11), out var expired3, out _);
        Assert.Contains(StatusEffectType.Poison, expired3);
        Assert.False(manager.HasStatus(StatusEffectType.Poison));
        Assert.Equal(KeepEffectFlags.Normal, manager.Flags);
    }

    [Fact]
    public void MagicManager_LearnSpell_Cooldown_AndXpProgression()
    {
        var manager = new MagicManager();
        var model = new MagicModel { Id = 1, OwnerId = 100001, Type = 1000, Level = 0, Exp = 0 };
        var typeData = new MagicTypeModel
        {
            Type = 1000,
            Level = 0,
            NeedExp = 500,
            Delay = 1500,
            UseMp = 20
        };

        manager.LearnSpell(model, typeData);
        Assert.True(manager.HasSpell(1000));

        var spell = manager.GetSpell(1000);
        Assert.NotNull(spell);

        // Cooldown check
        var now = DateTime.UtcNow;
        Assert.False(manager.IsCoolingDown(1000, now));
        manager.SetCooldown(1000, typeData.Delay, now);
        Assert.True(manager.IsCoolingDown(1000, now.AddMilliseconds(500)));
        Assert.False(manager.IsCoolingDown(1000, now.AddMilliseconds(1600)));

        // XP accumulation and reset
        Assert.Equal(0, manager.Xp);
        Assert.True(manager.AddXp(60, out bool reachedFull1));
        Assert.False(reachedFull1);
        Assert.Equal(60, manager.Xp);

        Assert.True(manager.AddXp(45, out bool reachedFull2));
        Assert.True(reachedFull2);
        Assert.Equal(100, manager.Xp);

        manager.ResetXp();
        Assert.Equal(0, manager.Xp);

        // Spell exp & level up
        var level1Type = new MagicTypeModel { Type = 1000, Level = 1, NeedExp = 1000, Delay = 1500 };
        bool added = spell.AddExp(550, (t, l) => l == 1 ? level1Type : null, out bool leveledUp);
        Assert.True(added);
        Assert.True(leveledUp);
        Assert.Equal(1, spell.Level);
        Assert.Equal(50u, spell.Exp);
    }

    [Fact]
    public void BattleEngine_CalculateMagicDamage_AppliesFormulasAndBuffs()
    {
        var attacker = new MockCombatEntity { Id = 1, Level = 50, IsPlayer = true };
        var monster = new MockCombatEntity { Id = 2, Level = 30, IsPlayer = false, MagicDef = 20 };

        // 1. Base magic damage with level advantage delta 20 (x3 multiplier)
        // rawAtk = 200, multiplier = 3.0 -> 600 - targetDef(20) = 580
        int baseDamage = BattleEngine.CalculateMagicDamage(attacker, monster, magicPower: 200);
        Assert.Equal(580, baseDamage);

        // 2. Superman buff on attacker: 10x damage against monsters
        attacker.Statuses.AttachStatus(StatusEffectType.Superman, power: 0, durationSecs: 20);
        // rawAtk = 2000, multiplier = 3.0 -> 6000 - targetDef(20) = 5980
        int supermanDamage = BattleEngine.CalculateMagicDamage(attacker, monster, magicPower: 200);
        Assert.Equal(5980, supermanDamage);
        attacker.Statuses.DetachStatus(StatusEffectType.Superman);

        // 3. Cyclone buff on attacker: 2x damage
        attacker.Statuses.AttachStatus(StatusEffectType.Cyclone, power: 0, durationSecs: 20);
        // rawAtk = 400, multiplier = 3.0 -> 1200 - targetDef(20) = 1180
        int cycloneDamage = BattleEngine.CalculateMagicDamage(attacker, monster, magicPower: 200);
        Assert.Equal(1180, cycloneDamage);
        attacker.Statuses.DetachStatus(StatusEffectType.Cyclone);

        // 4. Target defensive Shield: reduces damage by 30%
        monster.Statuses.AttachStatus(StatusEffectType.Shield, power: 0, durationSecs: 20);
        int shieldedDamage = BattleEngine.CalculateMagicDamage(attacker, monster, magicPower: 200);
        Assert.Equal((int)(580 * 0.7), shieldedDamage);
    }

    [Fact]
    public void BattleEngine_FlyingTarget_ImmuneToMeleePhysical()
    {
        var meleeAttacker = new MockCombatEntity { Id = 1, MinAtk = 50, MaxAtk = 50, AttackRange = 1 };
        var flyingTarget = new MockCombatEntity { Id = 2, Defense = 10 };

        flyingTarget.Statuses.AttachStatus(StatusEffectType.Fly, power: 0, durationSecs: 30);

        // Melee attacker cannot hit flying target
        int damage = BattleEngine.CalculateDamage(meleeAttacker, flyingTarget);
        Assert.Equal(0, damage);

        // Magic attacker CAN hit flying target
        int magicDmg = BattleEngine.CalculateMagicDamage(meleeAttacker, flyingTarget, magicPower: 100);
        Assert.True(magicDmg > 0);
    }
}
