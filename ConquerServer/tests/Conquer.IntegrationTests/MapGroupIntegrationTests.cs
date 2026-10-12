// Conquer.IntegrationTests — Integration tests for MapGroupLoop, Movement, and Broadcast.

using System.Diagnostics.Metrics;
using Conquer.Data;
using Conquer.Net;
using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages;
using Conquer.Shared.Messages.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Conquer.IntegrationTests;

public class MapGroupIntegrationTests
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
    public async Task MapGroupLoop_WalkPacket_MovesPlayerAndBroadcastsToObserver()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(
            groupIndex: 0,
            totalGroups: 1,
            NullLogger<MapGroupLoop>.Instance,
            meterFactory,
            clock);

        // Setup Map
        var mapModel = new MapModel { Id = 1002, Name = "TwinCity" };
        var grid = new CellMap(100, 100, defaultWalkable: true);
        var map = new GameMap(mapModel, grid);
        mapGroup.AddMap(map);

        // Add 2 Players
        var moverSocket = new MockSocket();
        var mover = new Player(10001, "Mover", 1) { Socket = moverSocket, SocketId = 101 };
        mapGroup.AddPlayer(mover, 1002, 50, 50);

        var observerSocket = new MockSocket();
        var observer = new Player(10002, "Observer", 2) { Socket = observerSocket, SocketId = 102 };
        mapGroup.AddPlayer(observer, 1002, 52, 52);

        observerSocket.SentPackets.Clear();

        // Start MapGroupLoop
        mapGroup.Start();

        // Encode MsgWalk: Mover walks South
        var writer = new PacketWriter();
        byte[] walkPayload;
        try
        {
            var walk = new MsgWalk(mover.Id, Direction.South);
            walk.Encode(ref writer);
            walkPayload = writer.AsSpan().ToArray();
        }
        finally
        {
            writer.Dispose();
        }

        // Post MsgWalk as KernelClientMsg from socket 101
        byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(walkPayload.Length);
        walkPayload.CopyTo(rented, 0);

        var msg = InternalMessage.WithPayload(
            InternalMessageId.KernelClientMsg,
            sourceId: 101,
            secondaryId: (int)PacketType.Walk,
            rentedBuffer: rented,
            length: walkPayload.Length);

        mapGroup.Post(msg);

        // Wait brief moment for loop to process message
        await Task.Delay(150);

        // Verify Mover position changed to (50, 51)
        Assert.Equal(50, mover.PosX);
        Assert.Equal(51, mover.PosY);
        Assert.Equal(Direction.South, mover.Direction);

        // Verify Observer received broadcast MsgWalk
        Assert.NotEmpty(observerSocket.SentPackets);
        var received = MsgWalk.Decode(observerSocket.SentPackets.Last());
        Assert.Equal(mover.Id, received.EntityId);
        Assert.Equal(Direction.South, received.Direction);

        // Cleanup
        await mapGroup.StopAsync();
    }

    [Fact]
    public async Task MapGroupLoop_JumpPacket_MovesPlayerWhenWithinRange()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(
            groupIndex: 0,
            totalGroups: 1,
            NullLogger<MapGroupLoop>.Instance,
            meterFactory,
            clock);

        var mapModel = new MapModel { Id = 1002, Name = "TwinCity" };
        var grid = new CellMap(100, 100, defaultWalkable: true);
        var map = new GameMap(mapModel, grid);
        mapGroup.AddMap(map);

        var jumperSocket = new MockSocket();
        var jumper = new Player(10001, "Jumper", 1) { Socket = jumperSocket, SocketId = 201 };
        mapGroup.AddPlayer(jumper, 1002, 50, 50);

        mapGroup.Start();

        // 1. Legal jump: jump 10 cells to (60, 55) (Chebyshev distance = 10 < 18)
        var writer = new PacketWriter();
        byte[] jumpPayload;
        try
        {
            var jump = MsgAction.CreateJump(jumper.Id, 50, 50, 60, 55);
            jump.Encode(ref writer);
            jumpPayload = writer.AsSpan().ToArray();
        }
        finally
        {
            writer.Dispose();
        }

        byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(jumpPayload.Length);
        jumpPayload.CopyTo(rented, 0);

        mapGroup.Post(InternalMessage.WithPayload(
            InternalMessageId.KernelClientMsg,
            sourceId: 201,
            secondaryId: (int)PacketType.Action,
            rentedBuffer: rented,
            length: jumpPayload.Length));

        await Task.Delay(150);

        Assert.Equal(60, jumper.PosX);
        Assert.Equal(55, jumper.PosY);

        // 2. Illegal jump: jump 25 cells to (85, 55) (Chebyshev distance = 25 >= 18)
        var writer2 = new PacketWriter();
        byte[] illegalJumpPayload;
        try
        {
            var illegalJump = MsgAction.CreateJump(jumper.Id, 60, 55, 85, 55);
            illegalJump.Encode(ref writer2);
            illegalJumpPayload = writer2.AsSpan().ToArray();
        }
        finally
        {
            writer2.Dispose();
        }

        byte[] rented2 = System.Buffers.ArrayPool<byte>.Shared.Rent(illegalJumpPayload.Length);
        illegalJumpPayload.CopyTo(rented2, 0);

        mapGroup.Post(InternalMessage.WithPayload(
            InternalMessageId.KernelClientMsg,
            sourceId: 201,
            secondaryId: (int)PacketType.Action,
            rentedBuffer: rented2,
            length: illegalJumpPayload.Length));

        await Task.Delay(150);

        // Position should remain unchanged at (60, 55)
        Assert.Equal(60, jumper.PosX);
        Assert.Equal(55, jumper.PosY);

        await mapGroup.StopAsync();
    }
}
