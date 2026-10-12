// Conquer.IntegrationTests — NPC Server Integration Tests.
// OPUS §10 Phase 5 deliverables:
// 1. TCP framing roundtrip across loopback socket.
// 2. Spawn budget loop (MAXNPC_PER_ONTIMER = 20, MaxNpc cap).
// 3. Dormancy lifecycle (wake on player enter, sleep on exit/timeout).
// 4. Command validation by MapGroupLoop (reject invalid move/attack/dead monster).
// 5. End-to-end Combat loop between MapGroupLoop and NpcWorld.

using System.Net;
using Conquer.Data;
using Conquer.Net;
using Conquer.NpcServer;
using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages.Packets;
using Conquer.Shared.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Conquer.IntegrationTests;

public class NpcIntegrationTests
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

    [Fact]
    public async Task TcpNpcTransport_FramingRoundtrip_LoopbackSocket()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var listener = new TcpNpcListener(IPAddress.Loopback, 0);
        listener.Start();

        int port = listener.LocalEndpoint.Port;
        var client = new TcpNpcTransport("127.0.0.1", port, autoReconnect: false);

        var acceptTask = listener.AcceptAsync(cts.Token);
        await client.ConnectAsync(cts.Token);
        var serverTransport = await acceptTask;

        try
        {
            // Send client -> server
            byte[] clientPayload = [10, 20, 30, 40, 50];
            var clientPacket = new NpcPacket(500001, PacketType.Walk, clientPayload);
            await client.SendAsync(clientPacket, cts.Token);

            await using var serverEnumerator = serverTransport.ReceiveAsync(cts.Token).GetAsyncEnumerator();
            Assert.True(await serverEnumerator.MoveNextAsync());
            Assert.Equal(500001u, serverEnumerator.Current.NpcId);
            Assert.Equal(PacketType.Walk, serverEnumerator.Current.PacketType);
            Assert.Equal(clientPayload, serverEnumerator.Current.Payload.ToArray());

            // Send server -> client
            byte[] serverPayload = [99, 88, 77];
            var serverPacket = new NpcPacket(500002, PacketType.Interact, serverPayload);
            await serverTransport.SendAsync(serverPacket, cts.Token);

            await using var clientEnumerator = client.ReceiveAsync(cts.Token).GetAsyncEnumerator();
            Assert.True(await clientEnumerator.MoveNextAsync());
            Assert.Equal(500002u, clientEnumerator.Current.NpcId);
            Assert.Equal(PacketType.Interact, clientEnumerator.Current.PacketType);
            Assert.Equal(serverPayload, clientEnumerator.Current.Payload.ToArray());
        }
        finally
        {
            await client.CloseAsync();
            await serverTransport.CloseAsync();
        }
    }

    [Fact]
    public void NpcWorld_SpawnBudget_SpawnsMaxPerTick_CappedAtMax()
    {
        var world = new NpcWorld();
        world.AddMap(1002, 1000, 1000);

        var type = new NpcTypeData
        {
            TypeId = 1,
            Name = "Pheasant",
            Life = 50,
            ViewRange = 5
        };
        world.RegisterType(type);

        var gen = new NpcGenerator(
            id: 1,
            mapId: 1002,
            boundX: 50,
            boundY: 50,
            boundCX: 50,
            boundCY: 1,
            npcType: 1,
            grid: 1,
            restSecs: 1,
            maxPerGen: 20);

        world.AddGenerator(gen);

        // Tick 1: Spawns up to MaxPerGen = 20
        world.OnTimer(nowMs: 10000);
        Assert.Equal(20, gen.Amount);

        // Advance 1s to exceed RestSecs
        // Tick 2: C++ NpcManager adaptive step pacing decrements step to 19 after full budget exhaustion
        world.OnTimer(nowMs: 12000);
        Assert.Equal(39, gen.Amount);

        // Tick 3: Spawns remaining 11 -> capped at MaxNpc = 50
        world.OnTimer(nowMs: 14000);
        Assert.Equal(50, gen.Amount);

        // Tick 4: Generator is at max, should not spawn any more
        world.OnTimer(nowMs: 16000);
        Assert.Equal(50, gen.Amount);
    }

    [Fact]
    public void NpcWorld_DormancyLifecycle_WakesOnPlayerEnter_SleepsOnTimeout()
    {
        var world = new NpcWorld();
        world.AddMap(1002, 1000, 1000);

        var type = new NpcTypeData
        {
            TypeId = 1,
            Name = "Wolf",
            Life = 100,
            ViewRange = 5
        };
        world.RegisterType(type);

        var brain = new NpcBrain(400001, type, 1002, 50, 50);
        world.RegisterNpc(brain);

        // Set initial block to dormant
        var map = world.GetMap(1002)!;
        map.SetDormancy(50, 50);
        brain.ChangeMode(NpcMode.Dormancy);
        Assert.True(brain.IsDormant);

        // Player enters map at (50, 50) -> wakes monster
        var writer = new PacketWriter();
        try
        {
            var msgPlayer = new MsgPlayer(
                EntityId: 10001,
                LookFace: 1,
                Status: 0,
                SynIdRank: 0,
                ArmorType: 0,
                WeaponRType: 0,
                MaxLife: 100,
                Life: 100,
                Level: 1,
                PosX: 50,
                PosY: 50,
                Hair: 0,
                Dir: (byte)Direction.South,
                Pose: 0,
                Name: "Hero");
            msgPlayer.Encode(ref writer);
            world.ProcessGameServerPacket(new NpcPacket(1002, PacketType.Player, writer.AsSpan().ToArray()), nowMs: 10000);
        }
        finally
        {
            writer.Dispose();
        }

        Assert.False(brain.IsDormant);
        Assert.Equal(NpcMode.Idle, brain.ActMode);

        // Player leaves map
        var delWriter = new PacketWriter();
        try
        {
            var delRole = MsgAction.CreateDelRole(10001);
            delRole.Encode(ref delWriter);
            world.ProcessGameServerPacket(new NpcPacket(1002, PacketType.Action, delWriter.AsSpan().ToArray()), nowMs: 10000);
        }
        finally
        {
            delWriter.Dispose();
        }

        // Advance beyond 60s dormancy timeout
        world.OnTimer(nowMs: 75000);
        Assert.True(brain.IsDormant);
        Assert.Equal(NpcMode.Dormancy, brain.ActMode);
    }

    [Fact]
    public void MapGroupLoop_CommandValidation_RejectsInvalidBrainCommands()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(0, 1, NullLogger<MapGroupLoop>.Instance, meterFactory, clock);

        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var monster = new Monster(400001, "Pheasant", 1)
        {
            Life = 100,
            MaxLife = 100,
            MinAtk = 10,
            MaxAtk = 10,
            AttackRange = 1,
            AttackSpeedMs = 1000
        };
        mapGroup.AddMonster(monster, 1002, 50, 50);

        var targetSocket = new MockSocket();
        var target = new Player(10001, "Target", 1)
        {
            Socket = targetSocket,
            SocketId = 101,
            Life = 100,
            MaxLife = 100
        };
        mapGroup.AddPlayer(target, 1002, 60, 60); // 10 cells away, well outside attack range 1

        // 1. Invalid Move: moving to an unwalkable cell
        map.Grid.SetWalkable(51, 50, false); // Block East tile
        var walkEast = new MsgWalk(monster.Id, Direction.East, WalkMode.Walk);
        var walkEastWriter = new PacketWriter();
        try
        {
            walkEast.Encode(ref walkEastWriter);
            mapGroup.ReceiveNpcPacket(new NpcPacket(monster.Id, PacketType.Walk, walkEastWriter.AsSpan().ToArray()));
        }
        finally
        {
            walkEastWriter.Dispose();
        }

        // Monster must remain at (50, 50)
        Assert.Equal(50, monster.PosX);
        Assert.Equal(50, monster.PosY);

        // 2. Invalid Attack: target is at (60, 60), monster at (50, 50) (dist 10 > range 1)
        var invalidAttack = MsgInteract.CreateAttack(monster.Id, target.Id, (ushort)monster.PosX, (ushort)monster.PosY, 0);
        var atkWriter = new PacketWriter();
        try
        {
            invalidAttack.Encode(ref atkWriter);
            mapGroup.ReceiveNpcPacket(new NpcPacket(monster.Id, PacketType.Interact, atkWriter.AsSpan().ToArray()));
        }
        finally
        {
            atkWriter.Dispose();
        }

        // Target HP must be untouched
        Assert.Equal(100, target.Life);

        // 3. Dead Monster: set monster HP = 0
        monster.Life = 0;
        Assert.False(monster.IsAlive);

        map.Grid.SetWalkable(51, 50, true); // unblock East
        walkEastWriter = new PacketWriter();
        try
        {
            walkEast.Encode(ref walkEastWriter);
            mapGroup.ReceiveNpcPacket(new NpcPacket(monster.Id, PacketType.Walk, walkEastWriter.AsSpan().ToArray()));
        }
        finally
        {
            walkEastWriter.Dispose();
        }

        // Dead monster must not move
        Assert.Equal(50, monster.PosX);
        Assert.Equal(50, monster.PosY);
    }

    [Fact]
    public async Task EndToEnd_CombatAndAiLoop_ChannelTransport()
    {
        var (gameTransport, npcTransport) = ChannelNpcTransport.CreatePair();

        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(0, 1, NullLogger<MapGroupLoop>.Instance, meterFactory, clock)
        {
            NpcTransport = gameTransport
        };

        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var world = new NpcWorld(npcTransport);
        world.AddMap(1002, 100, 100);

        var type = new NpcTypeData
        {
            TypeId = 1,
            Name = "Guard",
            Life = 20,
            MinAtk = 5,
            MaxAtk = 5,
            AttackRange = 1,
            AttackSpeedMs = 500,
            ViewRange = 10
        };
        world.RegisterType(type);

        // Monster in MapGroup at (52, 50)
        var monster = new Monster(400001, "Guard", 1)
        {
            Life = 20,
            MaxLife = 20,
            MinAtk = 5,
            MaxAtk = 5,
            AttackRange = 1,
            AttackSpeedMs = 500
        };
        mapGroup.AddMonster(monster, 1002, 52, 50);

        // Brain in NpcWorld at (52, 50)
        var brain = new NpcBrain(400001, type, 1002, 52, 50);
        world.RegisterNpc(brain);

        // Player enters at (50, 50)
        var playerSocket = new MockSocket();
        var player = new Player(10001, "Hero", 1)
        {
            Socket = playerSocket,
            SocketId = 101,
            Life = 100,
            MaxLife = 100,
            MinAtk = 25,
            MaxAtk = 25,
            AttackRange = 1,
            AttackSpeedMs = 500
        };
        mapGroup.AddPlayer(player, 1002, 50, 50);

        // Read player enter packet sent from MapGroup to NpcWorld
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var npcEnumerator = npcTransport.ReceiveAsync(cts.Token).GetAsyncEnumerator();
        Assert.True(await npcEnumerator.MoveNextAsync());
        var playerEnterPkt = npcEnumerator.Current;
        world.ProcessGameServerPacket(playerEnterPkt, nowMs: 1000);

        // Monster brain detects player at (50, 50), distance 2 > AttackRange 1
        // Brain ticks and steps West towards (51, 50)
        world.OnTimer(nowMs: 1000);

        // Read monster walk packet sent from NpcWorld to MapGroup
        await using var gameEnumerator = gameTransport.ReceiveAsync(cts.Token).GetAsyncEnumerator();
        Assert.True(await gameEnumerator.MoveNextAsync());
        var movePkt = gameEnumerator.Current;
        Assert.Equal(PacketType.Walk, movePkt.PacketType);

        // MapGroup receives walk command and moves monster
        mapGroup.ReceiveNpcPacket(movePkt);
        Assert.Equal(51, monster.PosX);
        Assert.Equal(50, monster.PosY);

        // Brain updates internal pos and ticks again: now at (51, 50), distance 1 <= AttackRange 1
        // Brain attacks player
        brain.PosX = 51;
        brain.PosY = 50;
        world.OnTimer(nowMs: 1600);

        Assert.True(await gameEnumerator.MoveNextAsync());
        var attackPkt = gameEnumerator.Current;
        Assert.Equal(PacketType.Interact, attackPkt.PacketType);

        // MapGroup receives attack command from monster and executes damage
        mapGroup.ReceiveNpcPacket(attackPkt);
        Assert.True(player.Life < 100, "Player took damage from monster attack");

        // Player counter-attacks monster with lethal damage (25 dmg vs 20 hp)
        mapGroup.ExecuteAttack(player, monster);

        // Monster should be dead in MapGroup
        Assert.False(monster.IsAlive);
        Assert.Equal(0, monster.Life);

        // Read death kill notification sent to NpcWorld
        Assert.True(await npcEnumerator.MoveNextAsync());
        var killPkt = npcEnumerator.Current;
        Assert.Equal(PacketType.Interact, killPkt.PacketType);
        world.ProcessGameServerPacket(killPkt, nowMs: 2000);

        // Monster is dead in NpcWorld
        Assert.False(brain.IsAlive);
    }
}
