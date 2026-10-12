// Conquer.UnitTests — Tests for Map, Grid, Movement, and Spatial Partitioning.

using Conquer.Data;
using Conquer.Net;
using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages.Packets;
using Xunit;

namespace Conquer.UnitTests;

public class MapSystemTests
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
    public void MapGrid_BoundaryAndPassability_WorksCorrectly()
    {
        var grid = new CellMap(100, 100, defaultWalkable: true);

        Assert.True(grid.IsValid(0, 0));
        Assert.True(grid.IsValid(99, 99));
        Assert.False(grid.IsValid(-1, 0));
        Assert.False(grid.IsValid(100, 100));

        Assert.True(grid.IsWalkable(50, 50));
        grid.SetWalkable(50, 50, false);
        Assert.False(grid.IsWalkable(50, 50));

        // Block a region
        grid.SetRegionWalkable(10, 10, 5, 5, false);
        Assert.False(grid.IsWalkable(10, 10));
        Assert.False(grid.IsWalkable(14, 14));
        Assert.True(grid.IsWalkable(15, 15));
    }

    [Fact]
    public void MapCoordinates_DeltaAndDistance_MatchesOriginal()
    {
        // 8 directions
        Assert.Equal(0, MapCoordinates.DeltaX[(int)Direction.South]);
        Assert.Equal(1, MapCoordinates.DeltaY[(int)Direction.South]);

        Assert.Equal(-1, MapCoordinates.DeltaX[(int)Direction.SouthWest]);
        Assert.Equal(1, MapCoordinates.DeltaY[(int)Direction.SouthWest]);

        Assert.Equal(-1, MapCoordinates.DeltaX[(int)Direction.West]);
        Assert.Equal(0, MapCoordinates.DeltaY[(int)Direction.West]);

        Assert.Equal(0, MapCoordinates.DeltaX[(int)Direction.North]);
        Assert.Equal(-1, MapCoordinates.DeltaY[(int)Direction.North]);

        Assert.Equal(1, MapCoordinates.DeltaX[(int)Direction.East]);
        Assert.Equal(0, MapCoordinates.DeltaY[(int)Direction.East]);

        // Chebyshev distance
        Assert.Equal(5, MapCoordinates.ChebyshevDistance(10, 10, 15, 12));
        Assert.Equal(1, MapCoordinates.ChebyshevDistance(10, 10, 11, 11));

        // Block calculation
        Assert.Equal(0, MapCoordinates.ToBlock(0));
        Assert.Equal(0, MapCoordinates.ToBlock(17));
        Assert.Equal(1, MapCoordinates.ToBlock(18));
        Assert.Equal(2, MapCoordinates.ToBlock(36));
    }

    [Fact]
    public void GameBlock_EntityAndDormancy_TracksAccurately()
    {
        var block = new GameBlock(0, 0);
        Assert.True(block.IsDormant);
        Assert.Equal(0, block.PlayerCount);

        var player = new Player(10001, "Hero", 1);
        block.Add(player);

        Assert.False(block.IsDormant);
        Assert.Equal(1, block.PlayerCount);
        Assert.Contains(player, block.Entities);

        block.Remove(player);
        Assert.Equal(0, block.PlayerCount);
        Assert.DoesNotContain(player, block.Entities);
    }

    [Fact]
    public void Packets_MsgWalk_RoundTripSerialization()
    {
        var walk = new MsgWalk(10001, Direction.NorthEast, 1);

        var writer = new PacketWriter();
        try
        {
            walk.Encode(ref writer);
            var span = writer.AsSpan();

            var decoded = MsgWalk.Decode(span);
            Assert.Equal(10001u, decoded.EntityId);
            Assert.Equal(Direction.NorthEast, decoded.Direction);
            Assert.Equal((byte)1, decoded.Mode);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void Packets_MsgAction_Jump_RoundTripSerialization()
    {
        var jump = MsgAction.CreateJump(10001, 100, 100, 110, 110, (ushort)Direction.NorthEast, 12345);

        var writer = new PacketWriter();
        try
        {
            jump.Encode(ref writer);
            var span = writer.AsSpan();

            var decoded = MsgAction.Decode(span);
            Assert.Equal(12345u, decoded.Timestamp);
            Assert.Equal(10001u, decoded.EntityId);
            Assert.Equal((ushort)100, decoded.PosX);
            Assert.Equal((ushort)100, decoded.PosY);
            Assert.Equal(ActionType.Jump, decoded.Action);
            Assert.Equal((ushort)110, decoded.TargetPosX);
            Assert.Equal((ushort)110, decoded.TargetPosY);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void Packets_MsgTick_RoundTripSerialization()
    {
        var tick = new MsgTick(10001, 555, 999);

        var writer = new PacketWriter();
        try
        {
            tick.Encode(ref writer);
            var span = writer.AsSpan();

            var decoded = MsgTick.Decode(span);
            Assert.Equal(10001u, decoded.PlayerId);
            Assert.Equal(555u, decoded.Data);
            Assert.Equal(999u, decoded.CheckData);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void Packets_MsgPlayer_RoundTripSerialization()
    {
        var player = new MsgPlayer(
            EntityId: 10001,
            LookFace: 1,
            Status: 0,
            SynIdRank: 0,
            ArmorType: 130000,
            WeaponRType: 410000,
            MaxLife: 500,
            Life: 450,
            Level: 70,
            PosX: 120,
            PosY: 150,
            Hair: 310,
            Dir: (byte)Direction.SouthWest,
            Pose: 100,
            Name: "ConquerHero");

        var writer = new PacketWriter();
        try
        {
            player.Encode(ref writer);
            var span = writer.AsSpan();

            var decoded = MsgPlayer.Decode(span);
            Assert.Equal(10001u, decoded.EntityId);
            Assert.Equal(130000u, decoded.ArmorType);
            Assert.Equal(410000u, decoded.WeaponRType);
            Assert.Equal((ushort)500, decoded.MaxLife);
            Assert.Equal((ushort)450, decoded.Life);
            Assert.Equal((ushort)70, decoded.Level);
            Assert.Equal((ushort)120, decoded.PosX);
            Assert.Equal((ushort)150, decoded.PosY);
            Assert.Equal((byte)Direction.SouthWest, decoded.Dir);
            Assert.Equal("ConquerHero", decoded.Name);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void GameMap_EnterAndLeave_SpawnsAndRemovesCorrectly()
    {
        var mapModel = new MapModel { Id = 1002, Name = "TwinCity" };
        var grid = new CellMap(200, 200, defaultWalkable: true);
        var map = new GameMap(mapModel, grid);

        var player1Socket = new MockSocket();
        var player1 = new Player(10001, "Alice", 1) { Socket = player1Socket, SocketId = 1 };

        var player2Socket = new MockSocket();
        var player2 = new Player(10002, "Bob", 2) { Socket = player2Socket, SocketId = 2 };

        // Alice enters map at (10, 10)
        Assert.True(map.Enter(player1, 10, 10));
        Assert.Single(map.Entities);

        // Bob enters in the same block at (12, 12)
        Assert.True(map.Enter(player2, 12, 12));
        Assert.Equal(2, map.Entities.Count);

        // Alice should have received Bob's spawn packet
        Assert.NotEmpty(player1Socket.SentPackets);
        // Bob should have received Alice's spawn packet
        Assert.NotEmpty(player2Socket.SentPackets);

        // Bob leaves
        Assert.True(map.Leave(player2));
        Assert.Single(map.Entities);

        // Alice should have received Bob's disappear packet
        var lastPacket = player1Socket.SentPackets.Last();
        var action = MsgAction.Decode(lastPacket);
        Assert.Equal(ActionType.Disappear, action.Action);
        Assert.Equal(player2.Id, action.EntityId);
    }

    [Fact]
    public void GameMap_Movement_WithinAndAcrossBlocks_BroadcastsCorrectly()
    {
        var mapModel = new MapModel { Id = 1002, Name = "TwinCity" };
        var grid = new CellMap(200, 200, defaultWalkable: true);
        var map = new GameMap(mapModel, grid);

        var player1Socket = new MockSocket();
        var player1 = new Player(10001, "Mover", 1) { Socket = player1Socket, SocketId = 1 };

        var player2Socket = new MockSocket();
        var player2 = new Player(10002, "Observer", 2) { Socket = player2Socket, SocketId = 2 };

        // Place Mover at (10, 10) in block (0, 0)
        map.Enter(player1, 10, 10);
        // Place Observer at (15, 15) in block (0, 0)
        map.Enter(player2, 15, 15);

        player2Socket.SentPackets.Clear();

        // 1. Mover walks within same block to (10, 11)
        var walkWriter = new PacketWriter();
        try
        {
            var walk = new MsgWalk(player1.Id, Direction.South);
            walk.Encode(ref walkWriter);
            Assert.True(map.Move(player1, 10, 11, walkWriter.AsSpan()));
        }
        finally
        {
            walkWriter.Dispose();
        }

        // Observer should receive MsgWalk
        Assert.Single(player2Socket.SentPackets);
        var decodedWalk = MsgWalk.Decode(player2Socket.SentPackets[0]);
        Assert.Equal(player1.Id, decodedWalk.EntityId);
        Assert.Equal(Direction.South, decodedWalk.Direction);

        // 2. Cannot move into blocked cell
        grid.SetWalkable(10, 12, false);
        Assert.False(map.Move(player1, 10, 12));
    }

    [Fact]
    public void MapGroupLoop_Heartbeat_SendsServerTickAndKicksSilentClient()
    {
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(
            groupIndex: 0,
            totalGroups: 1,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MapGroupLoop>.Instance,
            meterFactory,
            TimeProvider.System);

        var mapModel = new MapModel { Id = 1002, Name = "TwinCity" };
        var grid = new CellMap(100, 100, defaultWalkable: true);
        var map = new GameMap(mapModel, grid);
        mapGroup.AddMap(map);

        var playerSocket = new MockSocket();
        var player = new Player(10001, "HeartbeatHero", 1) { Socket = playerSocket, SocketId = 555 };
        mapGroup.AddPlayer(player, 1002, 50, 50);

        playerSocket.SentPackets.Clear();

        // 1. Simulate 11 seconds passing since last server tick:
        player.LastServerHeartbeat = DateTime.UtcNow.AddSeconds(-11);
        player.LastClientHeartbeat = DateTime.UtcNow;

        mapGroup.TriggerTimerForTesting();

        // Player should receive server MsgTick
        Assert.NotEmpty(playerSocket.SentPackets);
        var tick = MsgTick.Decode(playerSocket.SentPackets.Last());
        Assert.Equal(player.Id, tick.PlayerId);

        // 2. Simulate 26 seconds passing without client response (silence > 25s):
        player.LastClientHeartbeat = DateTime.UtcNow.AddSeconds(-26);

        mapGroup.TriggerTimerForTesting();

        // Player should be kicked and disconnected
        Assert.True(playerSocket.Disconnected);
        Assert.Equal(0, mapGroup.PlayerCount);
    }

    private sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) => new(options.Name, options.Version);
        public void Dispose() { }
    }
}

