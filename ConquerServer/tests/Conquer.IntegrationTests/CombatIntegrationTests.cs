// Conquer.IntegrationTests — Integration tests for Combat, Auto-Attack, Death, PK/Crime, and Drops.

using Conquer.Data;
using Conquer.Net;
using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages;
using Conquer.Shared.Messages.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Conquer.IntegrationTests;

public class CombatIntegrationTests
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
    public async Task Player_Attacks_Monster_DealsDamage_BroadcastsAttack()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(
            groupIndex: 0,
            totalGroups: 1,
            NullLogger<MapGroupLoop>.Instance,
            meterFactory,
            clock);

        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var attackerSocket = new MockSocket();
        var attacker = new Player(10001, "Hero", 1)
        {
            Socket = attackerSocket,
            SocketId = 101,
            MinAtk = 25,
            MaxAtk = 25,
            AttackRange = 1,
            AttackSpeedMs = 100
        };
        mapGroup.AddPlayer(attacker, 1002, 50, 50);

        var monster = new Monster(50001, "Pheasant", 1)
        {
            Life = 100,
            MaxLife = 100,
            Defense = 5
        };
        mapGroup.AddMonster(monster, 1002, 51, 50);

        var observerSocket = new MockSocket();
        var observer = new Player(10002, "Observer", 2)
        {
            Socket = observerSocket,
            SocketId = 102
        };
        mapGroup.AddPlayer(observer, 1002, 52, 50);

        observerSocket.SentPackets.Clear();
        mapGroup.Start();

        try
        {
            // Send MsgInteract(Attack) from attacker
            var attackMsg = MsgInteract.CreateAttack(attacker.Id, monster.Id, 50, 50, 0);
            var attackBytes = new byte[MsgInteract.PacketSize];
            attackMsg.Encode(attackBytes);

            var internalMsg = CreateClientMessage(101, PacketType.Interact, attackBytes);
            mapGroup.Post(internalMsg);

            // Wait for combat processing on MapGroupLoop thread
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (monster.Life == 100 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.True(monster.Life < 100, "Monster life should be reduced after attack");

            // Verify observer received MsgInteract attack broadcast
            bool receivedInteract = observerSocket.SentPackets.Any(p =>
                p.Length >= 4 && WireProtocol.ReadType(p) == PacketType.Interact);
            Assert.True(receivedInteract, "Observer should receive MsgInteract attack packet");
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }

    [Fact]
    public async Task Player_Attacks_InnocentPlayer_GainsCrimeStatus()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(
            groupIndex: 0,
            totalGroups: 1,
            NullLogger<MapGroupLoop>.Instance,
            meterFactory,
            clock);

        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var playerASocket = new MockSocket();
        var playerA = new Player(10001, "PlayerA", 1)
        {
            Socket = playerASocket,
            SocketId = 101,
            MinAtk = 10,
            MaxAtk = 10,
            AttackRange = 1,
            PkPoints = 0
        };
        mapGroup.AddPlayer(playerA, 1002, 50, 50);

        var playerBSocket = new MockSocket();
        var playerB = new Player(10002, "PlayerB", 2)
        {
            Socket = playerBSocket,
            SocketId = 102,
            Life = 200,
            MaxLife = 200,
            Defense = 5,
            PkPoints = 0
        };
        mapGroup.AddPlayer(playerB, 1002, 51, 50);

        Assert.False(playerA.IsEvil);
        Assert.False(playerB.IsEvil);

        mapGroup.Start();

        try
        {
            // Player A attacks Player B
            var attackMsg = MsgInteract.CreateAttack(playerA.Id, playerB.Id, 50, 50, 0);
            var attackBytes = new byte[MsgInteract.PacketSize];
            attackMsg.Encode(attackBytes);

            var internalMsg = CreateClientMessage(101, PacketType.Interact, attackBytes);
            mapGroup.Post(internalMsg);

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!playerA.IsEvil && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.True(playerA.IsEvil, "Attacker should have criminal status after attacking innocent player");
            Assert.NotNull(playerA.CrimeExpireTime);
            Assert.False(playerB.IsEvil, "Victim should remain non-evil");
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }

    [Fact]
    public async Task Player_Kills_InnocentPlayer_GainsPkPoints()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(
            groupIndex: 0,
            totalGroups: 1,
            NullLogger<MapGroupLoop>.Instance,
            meterFactory,
            clock);

        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var killerSocket = new MockSocket();
        var killer = new Player(10001, "Killer", 1)
        {
            Socket = killerSocket,
            SocketId = 101,
            MinAtk = 500, // Fatal attack
            MaxAtk = 500,
            AttackRange = 1,
            PkPoints = 0
        };
        mapGroup.AddPlayer(killer, 1002, 50, 50);

        var victimSocket = new MockSocket();
        var victim = new Player(10002, "Victim", 2)
        {
            Socket = victimSocket,
            SocketId = 102,
            Life = 50,
            MaxLife = 50,
            Defense = 0,
            PkPoints = 0
        };
        mapGroup.AddPlayer(victim, 1002, 51, 50);

        mapGroup.Start();

        try
        {
            var attackMsg = MsgInteract.CreateAttack(killer.Id, victim.Id, 50, 50, 0);
            var attackBytes = new byte[MsgInteract.PacketSize];
            attackMsg.Encode(attackBytes);

            var internalMsg = CreateClientMessage(101, PacketType.Interact, attackBytes);
            mapGroup.Post(internalMsg);

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (victim.Life > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.Equal(0, victim.Life);
            Assert.Equal(CombatConstants.PkValueOneKill, killer.PkPoints);
            Assert.True(killer.IsEvil);
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }

    [Fact]
    public async Task Monster_Death_DropsSilver_RemovesFromMap()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(
            groupIndex: 0,
            totalGroups: 1,
            NullLogger<MapGroupLoop>.Instance,
            meterFactory,
            clock);

        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var playerSocket = new MockSocket();
        var player = new Player(10001, "Hero", 1)
        {
            Socket = playerSocket,
            SocketId = 101,
            MinAtk = 300,
            MaxAtk = 300,
            AttackRange = 1
        };
        mapGroup.AddPlayer(player, 1002, 50, 50);

        var monster = new Monster(50001, "Pheasant", 1)
        {
            Life = 20,
            MaxLife = 20,
            Defense = 0,
            DropMoney = 250
        };
        mapGroup.AddMonster(monster, 1002, 51, 50);

        playerSocket.SentPackets.Clear();
        mapGroup.Start();

        try
        {
            var attackMsg = MsgInteract.CreateAttack(player.Id, monster.Id, 50, 50, 0);
            var attackBytes = new byte[MsgInteract.PacketSize];
            attackMsg.Encode(attackBytes);

            var internalMsg = CreateClientMessage(101, PacketType.Interact, attackBytes);
            mapGroup.Post(internalMsg);

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (mapGroup.MonsterCount > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            // Monster removed from map
            Assert.Equal(0, mapGroup.MonsterCount);
            Assert.Null(mapGroup.GetMonster(50001));

            // Verify MsgMapItem drop packet was broadcasted
            bool receivedDrop = playerSocket.SentPackets.Any(p =>
                p.Length >= 4 && WireProtocol.ReadType(p) == PacketType.MapItem);
            Assert.True(receivedDrop, "Player should receive MsgMapItem silver drop packet on monster death");
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }

    [Fact]
    public void SingleOwner_Prevents_DoubleKill_RaceCondition()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(
            groupIndex: 0,
            totalGroups: 1,
            NullLogger<MapGroupLoop>.Instance,
            meterFactory,
            clock);

        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var attacker1 = new Player(10001, "Attacker1", 1) { MinAtk = 50, MaxAtk = 50, AttackRange = 1 };
        var attacker2 = new Player(10002, "Attacker2", 2) { MinAtk = 50, MaxAtk = 50, AttackRange = 1 };
        var victim = new Player(10003, "Victim", 3) { Life = 30, MaxLife = 30, Defense = 0 };

        mapGroup.AddPlayer(attacker1, 1002, 50, 50);
        mapGroup.AddPlayer(attacker2, 1002, 52, 50);
        mapGroup.AddPlayer(victim, 1002, 51, 50);

        // First attack kills victim
        mapGroup.ExecuteAttack(attacker1, victim);
        Assert.Equal(0, victim.Life);
        Assert.Equal(CombatConstants.PkValueOneKill, attacker1.PkPoints);

        // Second attack attempts to strike victim who is now dead
        mapGroup.ExecuteAttack(attacker2, victim);
        // Attacker 2 must NOT receive a kill or PK points
        Assert.Equal(0, attacker2.PkPoints);
        Assert.Equal(0, victim.Life);
    }
}
