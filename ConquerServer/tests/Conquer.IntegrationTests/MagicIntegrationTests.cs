// Conquer.IntegrationTests — Integration tests for Phase 7: Magic casting, status effects, and XP skills.

using Conquer.Data;
using Conquer.Net;
using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages;
using Conquer.Shared.Messages.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Conquer.IntegrationTests;

public class MagicIntegrationTests
{
    private sealed class MockSocket : IGameSocket
    {
        public List<byte[]> SentPackets { get; } = [];
        public bool Disconnected { get; private set; }
        public int PendingSends => 0;

        public void Send(ReadOnlySpan<byte> packet)
        {
            SentPackets.Add(packet.ToArray());
        }

        public void SendBatch(ReadOnlySpan<byte> wholePackets)
        {
            SentPackets.Add(wholePackets.ToArray());
        }

        public void Disconnect()
        {
            Disconnected = true;
        }
    }

    private static InternalMessage CreateClientMessage(int socketId, PacketType type, byte[] payload)
    {
        byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(rented, 0);

        return InternalMessage.WithPayload(
            InternalMessageId.KernelClientMsg,
            sourceId: socketId,
            secondaryId: (int)type,
            rentedBuffer: rented,
            length: payload.Length);
    }

    [Fact]
    public async Task Player_CastsMagicAttack_SingleTarget_DeductsMp_DealsDamage_BroadcastsMagicEffect()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var magicRepo = new MagicTypeRepository();

        var thunderType = new MagicTypeModel
        {
            Id = 1,
            Type = 1000,
            Sort = (ushort)MagicSort.Attack,
            Name = "Thunder",
            Level = 0,
            Power = 150,
            UseMp = 25,
            Delay = 1000,
            Distance = 15
        };
        magicRepo.Register(thunderType);

        var mapGroup = new MapGroupLoop(
            groupIndex: 0,
            totalGroups: 1,
            NullLogger<MapGroupLoop>.Instance,
            meterFactory,
            clock,
            magicRepo);

        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var casterSocket = new MockSocket();
        var caster = new Player(10001, "MageHero", 1)
        {
            Socket = casterSocket,
            SocketId = 101,
            Level = 20,
            Mana = 100,
            MaxMana = 100
        };
        caster.Magics.LearnSpell(new MagicModel { Type = 1000, Level = 0 }, thunderType);
        mapGroup.AddPlayer(caster, 1002, 50, 50);

        var monster = new Monster(50001, "Pheasant", 1)
        {
            Level = 10,
            Life = 200,
            MaxLife = 200,
            MagicDef = 10
        };
        mapGroup.AddMonster(monster, 1002, 53, 50);

        var observerSocket = new MockSocket();
        var observer = new Player(10002, "Watcher", 2)
        {
            Socket = observerSocket,
            SocketId = 102
        };
        mapGroup.AddPlayer(observer, 1002, 51, 50);

        observerSocket.SentPackets.Clear();
        casterSocket.SentPackets.Clear();
        mapGroup.Start();

        try
        {
            // Cast Thunder (1000) at monster
            var magicMsg = MsgInteract.CreateMagicAttack(caster.Id, monster.Id, 50, 50, magicType: 1000, magicLevel: 0);
            var buffer = new byte[MsgInteract.PacketSize];
            magicMsg.Encode(buffer);

            mapGroup.Post(CreateClientMessage(101, PacketType.Interact, buffer));

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (monster.Life == 200 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            // Verify MP deducted
            Assert.Equal(75, caster.Mana);

            // Verify monster took magic damage
            Assert.True(monster.Life < 200, "Monster should have taken magic damage");

            // Verify observer received MsgMagicEffect
            bool receivedMagicEffect = observerSocket.SentPackets.Any(p =>
                p.Length >= 4 && System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(2, 2)) == (ushort)PacketType.MagicEffect);
            Assert.True(receivedMagicEffect, "Observer should receive MsgMagicEffect");
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }

    [Fact]
    public async Task Player_CastsCure_RestoresLife_BroadcastsMagicEffect()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var magicRepo = new MagicTypeRepository();

        var cureType = new MagicTypeModel
        {
            Id = 2,
            Type = 1005,
            Sort = (ushort)MagicSort.Recruit,
            Name = "Cure",
            Level = 0,
            Power = 60,
            UseMp = 15,
            Delay = 500,
            Distance = 10
        };
        magicRepo.Register(cureType);

        var mapGroup = new MapGroupLoop(0, 1, NullLogger<MapGroupLoop>.Instance, meterFactory, clock, magicRepo);
        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var casterSocket = new MockSocket();
        var caster = new Player(10001, "Healer", 1)
        {
            Socket = casterSocket,
            SocketId = 101,
            Life = 30,
            MaxLife = 100,
            Mana = 50,
            MaxMana = 100
        };
        caster.Magics.LearnSpell(new MagicModel { Type = 1005, Level = 0 }, cureType);
        mapGroup.AddPlayer(caster, 1002, 50, 50);

        mapGroup.Start();
        try
        {
            // Cast Cure on self
            var magicMsg = MsgInteract.CreateMagicAttack(caster.Id, caster.Id, 50, 50, magicType: 1005, magicLevel: 0);
            var buffer = new byte[MsgInteract.PacketSize];
            magicMsg.Encode(buffer);

            mapGroup.Post(CreateClientMessage(101, PacketType.Interact, buffer));

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (caster.Life == 30 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.Equal(90, caster.Life); // 30 + 60
            Assert.Equal(35, caster.Mana); // 50 - 15
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }

    [Fact]
    public async Task Player_CastsShieldBuff_AttachesDefense1Status_RecalculatesStats()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var magicRepo = new MagicTypeRepository();

        var shieldType = new MagicTypeModel
        {
            Id = 3,
            Type = 1020,
            Sort = (ushort)MagicSort.AttachStatus,
            Name = "Shield",
            Level = 0,
            Power = 30,
            Status = (uint)StatusEffectType.Shield,
            Step = 20,
            UseMp = 20,
            Delay = 1000
        };
        magicRepo.Register(shieldType);

        var mapGroup = new MapGroupLoop(0, 1, NullLogger<MapGroupLoop>.Instance, meterFactory, clock, magicRepo);
        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var playerSocket = new MockSocket();
        var player = new Player(10001, "Warrior", 1)
        {
            Socket = playerSocket,
            SocketId = 101,
            BaseDefense = 50,
            Defense = 50,
            Mana = 100
        };
        player.Magics.LearnSpell(new MagicModel { Type = 1020, Level = 0 }, shieldType);
        mapGroup.AddPlayer(player, 1002, 50, 50);

        mapGroup.Start();
        try
        {
            var magicMsg = MsgInteract.CreateMagicAttack(player.Id, player.Id, 50, 50, magicType: 1020, magicLevel: 0);
            var buffer = new byte[MsgInteract.PacketSize];
            magicMsg.Encode(buffer);

            mapGroup.Post(CreateClientMessage(101, PacketType.Interact, buffer));

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!player.Statuses.HasStatus(StatusEffectType.Shield) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.True(player.Statuses.HasStatus(StatusEffectType.Shield));
            Assert.True(player.Statuses.Flags.HasFlag(KeepEffectFlags.Defense1));
            Assert.True(player.Defense > 50, "Player defense should be boosted by Shield status");
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }

    [Fact]
    public async Task Player_CastsXpSuperman_ConsumesXp_AttachesSuperSoldierBuff()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var magicRepo = new MagicTypeRepository();

        var supermanType = new MagicTypeModel
        {
            Id = 4,
            Type = 1025,
            Sort = (ushort)MagicSort.AttachStatus,
            Name = "Superman",
            Level = 0,
            Power = 1000,
            Status = (uint)StatusEffectType.Superman,
            Step = 25,
            UseXp = 1,
            Delay = 2000
        };
        magicRepo.Register(supermanType);

        var mapGroup = new MapGroupLoop(0, 1, NullLogger<MapGroupLoop>.Instance, meterFactory, clock, magicRepo);
        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var playerSocket = new MockSocket();
        var player = new Player(10001, "Trojan", 1)
        {
            Socket = playerSocket,
            SocketId = 101
        };
        // Charge player XP to 100
        player.Magics.AddXp(100, out _);
        Assert.Equal(100, player.Magics.Xp);

        player.Magics.LearnSpell(new MagicModel { Type = 1025, Level = 0 }, supermanType);
        mapGroup.AddPlayer(player, 1002, 50, 50);

        mapGroup.Start();
        try
        {
            var magicMsg = MsgInteract.CreateMagicAttack(player.Id, player.Id, 50, 50, magicType: 1025, magicLevel: 0);
            var buffer = new byte[MsgInteract.PacketSize];
            magicMsg.Encode(buffer);

            mapGroup.Post(CreateClientMessage(101, PacketType.Interact, buffer));

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!player.Statuses.HasStatus(StatusEffectType.Superman) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.True(player.Statuses.HasStatus(StatusEffectType.Superman));
            Assert.True(player.Statuses.Flags.HasFlag(KeepEffectFlags.SuperSoldier));
            Assert.Equal(0, player.Magics.Xp); // XP consumed
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }

    [Fact]
    public async Task Player_CastsAoE_DamagesMonstersInRadius()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var magicRepo = new MagicTypeRepository();

        var fireCircleType = new MagicTypeModel
        {
            Id = 5,
            Type = 1010,
            Sort = (ushort)MagicSort.Bomb,
            Name = "FireCircle",
            Level = 0,
            Power = 120,
            Range = 5,
            UseMp = 30,
            Delay = 1000
        };
        magicRepo.Register(fireCircleType);

        var mapGroup = new MapGroupLoop(0, 1, NullLogger<MapGroupLoop>.Instance, meterFactory, clock, magicRepo);
        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var caster = new Player(10001, "FireMage", 1)
        {
            SocketId = 101,
            Level = 30,
            Mana = 100
        };
        caster.Magics.LearnSpell(new MagicModel { Type = 1010, Level = 0 }, fireCircleType);
        mapGroup.AddPlayer(caster, 1002, 50, 50);

        var monsterNear1 = new Monster(50001, "Bat1", 1) { Level = 15, Life = 200, MaxLife = 200 };
        var monsterNear2 = new Monster(50002, "Bat2", 1) { Level = 15, Life = 200, MaxLife = 200 };
        var monsterFar = new Monster(50003, "Bat3", 1) { Level = 15, Life = 200, MaxLife = 200 };

        mapGroup.AddMonster(monsterNear1, 1002, 52, 51); // distance 2 (inside range 5)
        mapGroup.AddMonster(monsterNear2, 1002, 53, 50); // distance 3 (inside range 5)
        mapGroup.AddMonster(monsterFar, 1002, 65, 65);   // distance 15 (outside range 5)

        mapGroup.Start();
        try
        {
            var magicMsg = MsgInteract.CreateMagicAttack(caster.Id, 0, 50, 50, magicType: 1010, magicLevel: 0);
            var buffer = new byte[MsgInteract.PacketSize];
            magicMsg.Encode(buffer);

            mapGroup.Post(CreateClientMessage(101, PacketType.Interact, buffer));

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (monsterNear1.Life == 200 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.True(monsterNear1.Life < 200, "Near monster 1 should take damage");
            Assert.True(monsterNear2.Life < 200, "Near monster 2 should take damage");
            Assert.Equal(200, monsterFar.Life); // Untouched
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }
}
