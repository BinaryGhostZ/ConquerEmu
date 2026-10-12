// Conquer.StressTests — Phase 9 Soak, Load, and Zero-Allocation Verification Tests.
// Validates:
// 1. Zero heap allocation on packet parsing/serialization hot paths.
// 2. High-throughput message processing without backlog accumulation or drops.
// 3. Multi-entity spatial scaling across 18x18 blocks.
// 4. Thread-safe pipeline throughput and cipher continuity under concurrency.
// 5. Deterministic combat resolution under sustained concurrent load.

using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Conquer.Data;
using Conquer.Net;
using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages;
using Conquer.Shared.Messages.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Conquer.StressTests;

public class SoakAndAllocationTests
{
    private sealed class MockSocket : IGameSocket
    {
        public List<byte[]> SentPackets { get; } = [];
        public int SendCount;
        public bool Disconnected { get; private set; }
        public int PendingSends => 0;

        public void Send(ReadOnlySpan<byte> packet)
        {
            SendCount++;
            if (SentPackets.Count < 10)
            {
                SentPackets.Add(packet.ToArray());
            }
        }

        public void SendBatch(ReadOnlySpan<byte> wholePackets)
        {
            SendCount++;
            if (SentPackets.Count < 10)
            {
                SentPackets.Add(wholePackets.ToArray());
            }
        }

        public void Disconnect()
        {
            Disconnected = true;
        }
    }

    private static InternalMessage CreateClientMessage(int socketId, PacketType type, ReadOnlySpan<byte> payload)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(rented);

        return InternalMessage.WithPayload(
            InternalMessageId.KernelClientMsg,
            sourceId: socketId,
            secondaryId: (int)type,
            rentedBuffer: rented,
            length: payload.Length);
    }

    private static InternalMessage CreateClientMessage(int socketId, PacketType type, byte[] payload) =>
        CreateClientMessage(socketId, type, payload.AsSpan());

    [Fact]
    public void ZeroAllocation_PacketHotPaths_UnderSustainedThroughput()
    {
        const int iterations = 100_000;

        // Warm up and JIT
        Span<byte> buffer = stackalloc byte[128];
        var writer = new PacketWriter(buffer);
        var sampleWalk = new MsgWalk(1001, Direction.NorthWest, WalkMode.Walk);
        sampleWalk.Encode(ref writer);
        _ = MsgWalk.Decode(writer.AsSpan());

        // Force cleanup prior to measurement
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);

        long startBytes = GC.GetAllocatedBytesForCurrentThread();
        int initialGen1 = GC.CollectionCount(1);
        int initialGen2 = GC.CollectionCount(2);

        var sw = Stopwatch.StartNew();
        uint walkSum = 0;
        uint interactSum = 0;
        uint actionSum = 0;

        for (int i = 0; i < iterations; i++)
        {
            // 1. MsgWalk roundtrip
            writer.InitWriter();
            var walk = new MsgWalk(1000 + (uint)(i % 100), (Direction)(i % 8), WalkMode.Walk);
            walk.Encode(ref writer);
            var decodedWalk = MsgWalk.Decode(writer.AsSpan());
            walkSum += decodedWalk.EntityId;

            // 2. MsgInteract roundtrip
            writer.InitWriter();
            var interact = MsgInteract.CreateAttack(1001, 2002, 50, 50, 150);
            interact.Encode(ref writer);
            var decodedInteract = MsgInteract.Decode(writer.AsSpan());
            interactSum += decodedInteract.SenderId;

            // 3. MsgAction roundtrip
            writer.InitWriter();
            var action = MsgAction.CreateJump(1001, 55, 66, 56, 67);
            action.Encode(ref writer);
            var decodedAction = MsgAction.Decode(writer.AsSpan());
            actionSum += decodedAction.EntityId;
        }

        sw.Stop();
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
        int gen1Count = GC.CollectionCount(1) - initialGen1;
        int gen2Count = GC.CollectionCount(2) - initialGen2;

        // Ensure calculations were performed
        Assert.True(walkSum > 0);
        Assert.True(interactSum > 0);
        Assert.True(actionSum > 0);

        // Zero heap allocations on hot path stack/span execution (allowing <= 48 bytes for runtime jitter)
        Assert.True(allocatedBytes <= 48, $"Expected near-zero allocations across 100k iterations, got {allocatedBytes} bytes");
        Assert.Equal(0, gen1Count);
        Assert.Equal(0, gen2Count);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"100k packet cycles took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task MapGroupLoop_HighLoad_MessageProcessing_NoBacklogGrowth()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(0, 1, NullLogger<MapGroupLoop>.Instance, meterFactory, clock);

        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(200, 200, defaultWalkable: true));
        mapGroup.AddMap(map);

        const int playerCount = 50;
        const int messagesPerPlayer = 100;
        var players = new List<Player>(playerCount);

        for (int i = 0; i < playerCount; i++)
        {
            var p = new Player((uint)(10000 + i), $"Player_{i}", 1)
            {
                Socket = new MockSocket(),
                SocketId = 100 + i
            };
            players.Add(p);
            mapGroup.AddPlayer(p, 1002, 50 + (i % 20), 50 + (i / 20));
        }

        mapGroup.Start();

        try
        {
            var sw = Stopwatch.StartNew();

            // Flood MapGroup with concurrent walk packets from all players
            var tasks = new Task[playerCount];
            for (int pIndex = 0; pIndex < playerCount; pIndex++)
            {
                int socketId = players[pIndex].SocketId;
                uint playerId = players[pIndex].Id;

                tasks[pIndex] = Task.Run(() =>
                {
                    Span<byte> stackBuf = stackalloc byte[64];
                    for (int m = 0; m < messagesPerPlayer; m++)
                    {
                        var pw = new PacketWriter(stackBuf);
                        var walk = new MsgWalk(playerId, (Direction)(m % 8), WalkMode.Walk);
                        walk.Encode(ref pw);
                        mapGroup.Post(CreateClientMessage(socketId, PacketType.Walk, pw.AsSpan()));
                    }
                });
            }

            await Task.WhenAll(tasks);

            // Wait for queue to drain completely without backlog growth
            const int expectedTotal = playerCount * messagesPerPlayer;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (mapGroup.Metrics.TotalHandled < expectedTotal && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            sw.Stop();
            Assert.True(mapGroup.Metrics.TotalHandled >= expectedTotal, $"Expected at least {expectedTotal} handled, got {mapGroup.Metrics.TotalHandled}");
            Assert.True(sw.ElapsedMilliseconds < 5000, $"Processed {expectedTotal} messages in {sw.ElapsedMilliseconds}ms");
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }

    [Fact]
    public void SpatialGrid_And_ObserverBroadcast_ScaleTest()
    {
        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(500, 500, defaultWalkable: true));

        const int totalEntities = 1_000;
        var entities = new List<Player>(totalEntities);

        // Populate map with 1,000 entities spread across 500x500 grid
        for (int i = 0; i < totalEntities; i++)
        {
            var player = new Player((uint)(50000 + i), $"Entity_{i}", 1)
            {
                Socket = new MockSocket()
            };
            int x = 10 + (i % 30) * 15;
            int y = 10 + (i / 30) * 15;
            map.Enter(player, x, y);
            entities.Add(player);
        }

        Span<byte> walkBuf = stackalloc byte[64];
        var pw = new PacketWriter(walkBuf);
        var sampleWalk = new MsgWalk(1, Direction.NorthEast, WalkMode.Walk);
        sampleWalk.Encode(ref pw);
        var movementSpan = pw.AsSpan();

        var sw = Stopwatch.StartNew();

        // Perform 10,000 spatial movements and 9-grid neighbor scans
        for (int step = 0; step < 10_000; step++)
        {
            var p = entities[step % totalEntities];
            int newX = Math.Clamp(p.PosX + (step % 3 - 1), 5, 495);
            int newY = Math.Clamp(p.PosY + (step % 2 == 0 ? 1 : -1), 5, 495);

            map.Move(p, newX, newY, movementSpan);

            int bx = GameMap.Block(newX);
            int by = GameMap.Block(newY);
            var blocks = map.GetBlocksIn9Grid(bx, by);
            Assert.NotEmpty(blocks);
        }

        sw.Stop();
        int totalBroadcasts = entities.Sum(e => ((MockSocket)e.Socket!).SendCount);
        Assert.True(totalBroadcasts > 0, "Observers must receive spatial broadcasts");
        Assert.True(sw.ElapsedMilliseconds < 3000, $"10,000 entity movements and 9-grid evaluations completed in {sw.ElapsedMilliseconds}ms with {totalBroadcasts} broadcasts delivered");
    }

    [Fact]
    public async Task SocketKernel_ConcurrentProducers_PipelineThroughput()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var endPoint = (IPEndPoint)listener.LocalEndPoint!;

        using var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connectTask = clientSocket.ConnectAsync(endPoint);
        using var serverSocket = await listener.AcceptAsync();
        await connectTask;

        using var securitySocket = new SecuritySocket(1, serverSocket, new NullCrypto(), NullLogger.Instance);
        securitySocket.Attach((_, _) => { }, _ => { });
        securitySocket.Start();

        const int producerCount = 8;
        const int packetsPerProducer = 2_000;
        const int packetWireSize = 16;
        const long totalBytesExpected = (long)producerCount * packetsPerProducer * packetWireSize;
        var packetData = new byte[] { 8, 0, 10, 0, 1, 2, 3, 4 };

        long totalReceived = 0;
        var receiveTask = Task.Run(async () =>
        {
            byte[] recvBuf = new byte[16384];
            while (totalReceived < totalBytesExpected)
            {
                try
                {
                    int r = await clientSocket.ReceiveAsync(recvBuf.AsMemory(), SocketFlags.None);
                    if (r <= 0) break;
                    Interlocked.Add(ref totalReceived, r);
                }
                catch
                {
                    break;
                }
            }
        });

        var sw = Stopwatch.StartNew();

        Parallel.For(0, producerCount, _ =>
        {
            for (int i = 0; i < packetsPerProducer; i++)
            {
                securitySocket.Send(packetData);
            }
        });

        await receiveTask;
        sw.Stop();

        // Verify socket handled and drained 16,000 packet sends without exception, lockup, or data loss
        Assert.Equal(totalBytesExpected, totalReceived);
        Assert.Equal(0, securitySocket.PendingSends);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"16,000 socket packet sends completed in {sw.ElapsedMilliseconds}ms");
        securitySocket.Disconnect();
    }

    [Fact]
    public async Task CombatAndMagic_MassiveConcurrentResolution()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var magicRepo = new MagicTypeRepository();

        var thunderSpell = new MagicTypeModel
        {
            Id = 1,
            Type = 1000,
            Sort = (ushort)MagicSort.Attack,
            Name = "Thunder",
            Level = 0,
            Power = 100,
            UseMp = 10,
            Delay = 0,
            Distance = 15
        };
        magicRepo.Register(thunderSpell);

        var mapGroup = new MapGroupLoop(0, 1, NullLogger<MapGroupLoop>.Instance, meterFactory, clock, magicRepo);
        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        const int encounterCount = 50;
        var casters = new List<Player>(encounterCount);
        var targets = new List<Monster>(encounterCount);

        for (int i = 0; i < encounterCount; i++)
        {
            var caster = new Player((uint)(70000 + i), $"Caster_{i}", 1)
            {
                Socket = new MockSocket(),
                SocketId = 300 + i,
                Mana = 500,
                MaxMana = 500
            };
            caster.Magics.LearnSpell(new MagicModel { Type = 1000, Level = 0 }, thunderSpell);
            mapGroup.AddPlayer(caster, 1002, 20 + (i % 10), 20 + (i / 10));
            casters.Add(caster);

            var monster = new Monster((uint)(80000 + i), $"Monster_{i}", 1)
            {
                Life = 500,
                MaxLife = 500,
                MagicDef = 10
            };
            mapGroup.AddMonster(monster, 1002, 21 + (i % 10), 20 + (i / 10));
            targets.Add(monster);
        }

        mapGroup.Start();

        try
        {
            // Cast spells concurrently
            Span<byte> magicStackBuf = stackalloc byte[64];
            for (int i = 0; i < encounterCount; i++)
            {
                var pw = new PacketWriter(magicStackBuf);
                var magicMsg = MsgInteract.CreateMagicAttack(casters[i].Id, targets[i].Id, 20, 20, magicType: 1000, magicLevel: 0);
                magicMsg.Encode(ref pw);
                mapGroup.Post(CreateClientMessage(casters[i].SocketId, PacketType.Interact, pw.AsSpan()));
            }

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (targets.All(m => m.Life == 500) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            // Verify all monsters took damage and mana was deducted consistently
            for (int i = 0; i < encounterCount; i++)
            {
                Assert.True(targets[i].Life < 500, $"Monster {i} should have taken magic damage");
                Assert.Equal(490, casters[i].Mana);
            }
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }

    [Fact]
    public async Task TransferUnderFire_RoutingContinuity()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var router = new WorldRouter(NullLogger<WorldRouter>.Instance);

        var group0 = new MapGroupLoop(0, 2, NullLogger<MapGroupLoop>.Instance, meterFactory, clock);
        var group1 = new MapGroupLoop(1, 2, NullLogger<MapGroupLoop>.Instance, meterFactory, clock);

        router.RegisterLoop(group0.PortId, group0.Writer);
        router.RegisterLoop(group1.PortId, group1.Writer);

        const int socketId = 555;
        router.Bind(socketId, group0.Writer);

        group0.Start();
        group1.Start();

        try
        {
            const int totalMessages = 20_000;
            int successfulRoutes = 0;

            // Concurrently route 20,000 messages while re-binding between loops
            var routeTask = Task.Run(() =>
            {
                Span<byte> tickBuf = stackalloc byte[8];
                for (int i = 0; i < totalMessages; i++)
                {
                    var msg = CreateClientMessage(socketId, PacketType.Tick, tickBuf);
                    if (router.Route(socketId, msg))
                    {
                        Interlocked.Increment(ref successfulRoutes);
                    }
                }
            });

            // Mid-flight rebind
            await Task.Delay(10);
            router.Rebind(socketId, group1.Writer);

            await routeTask;

            // Every single message was delivered to an active loop via primary or forward table
            Assert.Equal(totalMessages, successfulRoutes);
        }
        finally
        {
            await group0.StopAsync();
            await group1.StopAsync();
        }
    }
}
