// Conquer.IntegrationTests — Integration tests for Phase 8: Social Systems (Chat, Teams, Trading, Guilds).
// Tests MapGroup message flow, packet dispatching, state isolation, and multi-player interactions.

using Conquer.Data;
using Conquer.Net;
using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages;
using Conquer.Shared.Messages.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Conquer.IntegrationTests;

public class SocialIntegrationTests
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

    private static (MapGroupLoop loop, GameMap map) CreateTestEnvironment()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var loop = new MapGroupLoop(0, 1, NullLogger<MapGroupLoop>.Instance, meterFactory, clock);

        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(200, 200, defaultWalkable: true));
        loop.AddMap(map);

        return (loop, map);
    }

    [Fact]
    public async Task Chat_LocalBroadcast_And_Whisper_Routing()
    {
        var (loop, map) = CreateTestEnvironment();

        var sock1 = new MockSocket();
        var p1 = new Player(1001, "SpeakerOne", 1) { SocketId = 101, Socket = sock1 };

        var sock2 = new MockSocket();
        var p2 = new Player(1002, "ListenerTwo", 2) { SocketId = 102, Socket = sock2 };

        loop.AddPlayer(p1, 1002, 100, 100);
        loop.AddPlayer(p2, 1002, 102, 102);

        sock1.SentPackets.Clear();
        sock2.SentPackets.Clear();
        loop.Start();

        try
        {
            // 1. Local talk broadcast
            var talkMsg = MsgTalk.Create("SpeakerOne", "ALLUSERS", "Greetings everyone!", ChatChannel.Normal);
            var writerTalk = new PacketWriter();
            try
            {
                talkMsg.Encode(ref writerTalk);
                loop.Post(CreateClientMessage(p1.SocketId, PacketType.Talk, writerTalk.AsSpan().ToArray()));
            }
            finally
            {
                writerTalk.Dispose();
            }

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while ((!sock1.SentPackets.Any(p => p.Length >= MsgTalk.BaseHeaderSize && (PacketType)BitConverter.ToUInt16(p, 2) == PacketType.Talk) ||
                    !sock2.SentPackets.Any(p => p.Length >= MsgTalk.BaseHeaderSize && (PacketType)BitConverter.ToUInt16(p, 2) == PacketType.Talk)) &&
                   DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            // Both players are within 18 cells, so both should have received the local broadcast
            Assert.Contains(sock1.SentPackets, p => p.Length >= MsgTalk.BaseHeaderSize && (PacketType)BitConverter.ToUInt16(p, 2) == PacketType.Talk);
            Assert.Contains(sock2.SentPackets, p => p.Length >= MsgTalk.BaseHeaderSize && (PacketType)BitConverter.ToUInt16(p, 2) == PacketType.Talk);

            // Clear packet buffers
            sock1.SentPackets.Clear();
            sock2.SentPackets.Clear();

            // 2. Whisper to Player 2
            var whisperMsg = MsgTalk.CreateWhisper("SpeakerOne", "ListenerTwo", "Secret whisper!");
            var writerWhisper = new PacketWriter();
            try
            {
                whisperMsg.Encode(ref writerWhisper);
                loop.Post(CreateClientMessage(p1.SocketId, PacketType.Talk, writerWhisper.AsSpan().ToArray()));
            }
            finally
            {
                writerWhisper.Dispose();
            }

            deadline = DateTime.UtcNow.AddSeconds(2);
            while (!sock2.SentPackets.Any(p => (PacketType)BitConverter.ToUInt16(p, 2) == PacketType.Talk) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            // Player 2 received whisper
            var p2Packet = sock2.SentPackets.FirstOrDefault(p => (PacketType)BitConverter.ToUInt16(p, 2) == PacketType.Talk);
            Assert.NotNull(p2Packet);
            var decodedP2 = MsgTalk.Decode(p2Packet);
            Assert.Equal("Secret whisper!", decodedP2.Words);
            Assert.Equal(ChatChannel.Whisper, decodedP2.Channel);

            // Player 1 received echo
            var p1Echo = sock1.SentPackets.FirstOrDefault(p => (PacketType)BitConverter.ToUInt16(p, 2) == PacketType.Talk);
            Assert.NotNull(p1Echo);
        }
        finally
        {
            await loop.StopAsync();
        }
    }

    [Fact]
    public async Task Team_Formation_Invite_And_ExpSharing()
    {
        var (loop, map) = CreateTestEnvironment();

        var sock1 = new MockSocket();
        var leader = new Player(2001, "TeamLead", 1) { SocketId = 101, Socket = sock1, MinAtk = 50, MaxAtk = 50 };

        var sock2 = new MockSocket();
        var member = new Player(2002, "TeamMate", 2) { SocketId = 102, Socket = sock2 };

        loop.AddPlayer(leader, 1002, 50, 50);
        loop.AddPlayer(member, 1002, 52, 52);

        var monster = new Monster(9001, "Pheasant", 1)
        {
            Level = 5,
            Life = 10,
            MaxLife = 50
        };
        loop.AddMonster(monster, 1002, 51, 50);

        sock1.SentPackets.Clear();
        sock2.SentPackets.Clear();
        loop.Start();

        try
        {
            // 1. Leader creates team
            var createMsg = MsgTeam.Create(TeamAction.Create);
            var writerCreate = new PacketWriter();
            try
            {
                createMsg.Encode(ref writerCreate);
                loop.Post(CreateClientMessage(leader.SocketId, PacketType.Team, writerCreate.AsSpan().ToArray()));
            }
            finally
            {
                writerCreate.Dispose();
            }

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (loop.TeamManager.GetPlayerTeam(leader.Id) == null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            var team = loop.TeamManager.GetPlayerTeam(leader.Id);
            Assert.NotNull(team);
            Assert.True(team.IsLeader(leader.Id));

            // 2. Member applies / joins team
            var applyMsg = MsgTeam.Create(TeamAction.AgreeJoin, member.Id);
            var writerAgree = new PacketWriter();
            try
            {
                applyMsg.Encode(ref writerAgree);
                loop.Post(CreateClientMessage(leader.SocketId, PacketType.Team, writerAgree.AsSpan().ToArray()));
            }
            finally
            {
                writerAgree.Dispose();
            }

            deadline = DateTime.UtcNow.AddSeconds(2);
            while (team.Members.Count < 2 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.Equal(2, team.Members.Count);
            Assert.True(team.ContainsMember(member.Id));

            // 3. Leader attacks monster -> kills it and triggers team exp sharing
            var attackMsg = MsgInteract.CreateAttack(leader.Id, monster.Id, 50, 50, 0);
            var atkBuffer = new byte[MsgInteract.PacketSize];
            attackMsg.Encode(atkBuffer);
            loop.Post(CreateClientMessage(leader.SocketId, PacketType.Interact, atkBuffer));

            deadline = DateTime.UtcNow.AddSeconds(2);
            while (monster.Life > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            // Team exp sharing executed without throwing and kept members intact
            Assert.Equal(2, team.Members.Count);
        }
        finally
        {
            await loop.StopAsync();
        }
    }

    [Fact]
    public async Task Trade_EndToEnd_AtomicExchange()
    {
        var (loop, map) = CreateTestEnvironment();

        var sock1 = new MockSocket();
        var p1 = new Player(3001, "Merchant1", 1) { SocketId = 101, Socket = sock1, Money = 10000 };

        var sock2 = new MockSocket();
        var p2 = new Player(3002, "Merchant2", 2) { SocketId = 102, Socket = sock2, Money = 50000 };

        var swordType = new ItemTypeModel { Id = 410015, Name = "Blade", Amount = 100, AmountLimit = 100 };
        var sword = new Item(777, swordType, p1.Id, ItemPosition.Backpack);
        p1.Package.TryAdd(sword);

        loop.AddPlayer(p1, 1002, 10, 10);
        loop.AddPlayer(p2, 1002, 12, 12);

        sock1.SentPackets.Clear();
        sock2.SentPackets.Clear();
        loop.Start();

        try
        {
            // 1. Mutual Trade Apply
            var apply1 = MsgTrade.Create(TradeAction.Apply, p2.Id);
            var writerA1 = new PacketWriter();
            try
            {
                apply1.Encode(ref writerA1);
                loop.Post(CreateClientMessage(p1.SocketId, PacketType.Trade, writerA1.AsSpan().ToArray()));
            }
            finally { writerA1.Dispose(); }

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!sock2.SentPackets.Any(p => (PacketType)BitConverter.ToUInt16(p, 2) == PacketType.Trade) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            var apply2 = MsgTrade.Create(TradeAction.Apply, p1.Id);
            var writerA2 = new PacketWriter();
            try
            {
                apply2.Encode(ref writerA2);
                loop.Post(CreateClientMessage(p2.SocketId, PacketType.Trade, writerA2.AsSpan().ToArray()));
            }
            finally { writerA2.Dispose(); }

            deadline = DateTime.UtcNow.AddSeconds(2);
            while (loop.TradeManager.GetSession(p1.Id) == null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            var session = loop.TradeManager.GetSession(p1.Id);
            Assert.NotNull(session);
            Assert.Same(session, loop.TradeManager.GetSession(p2.Id));

            // 2. Player 1 stages item (sword)
            var addItem = MsgTrade.Create(TradeAction.AddItem, sword.Id);
            var writerItem = new PacketWriter();
            try
            {
                addItem.Encode(ref writerItem);
                loop.Post(CreateClientMessage(p1.SocketId, PacketType.Trade, writerItem.AsSpan().ToArray()));
            }
            finally { writerItem.Dispose(); }

            deadline = DateTime.UtcNow.AddSeconds(2);
            while (session.GetStagedItems(p1.Id).Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
            Assert.Single(session.GetStagedItems(p1.Id));

            // 3. Player 2 stages silver (25,000)
            var addMoney = MsgTrade.Create(TradeAction.AddMoney, 25000);
            var writerMoney = new PacketWriter();
            try
            {
                addMoney.Encode(ref writerMoney);
                loop.Post(CreateClientMessage(p2.SocketId, PacketType.Trade, writerMoney.AsSpan().ToArray()));
            }
            finally { writerMoney.Dispose(); }

            deadline = DateTime.UtcNow.AddSeconds(2);
            while (session.GetStagedMoney(p2.Id) == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
            Assert.Equal(25000u, session.GetStagedMoney(p2.Id));

            // 4. Both Confirm (TradeAction.Ok)
            var ok1 = MsgTrade.Create(TradeAction.Ok);
            var writerOk1 = new PacketWriter();
            try
            {
                ok1.Encode(ref writerOk1);
                loop.Post(CreateClientMessage(p1.SocketId, PacketType.Trade, writerOk1.AsSpan().ToArray()));
            }
            finally { writerOk1.Dispose(); }

            var ok2 = MsgTrade.Create(TradeAction.Ok);
            var writerOk2 = new PacketWriter();
            try
            {
                ok2.Encode(ref writerOk2);
                loop.Post(CreateClientMessage(p2.SocketId, PacketType.Trade, writerOk2.AsSpan().ToArray()));
            }
            finally { writerOk2.Dispose(); }

            deadline = DateTime.UtcNow.AddSeconds(2);
            while (!session.IsCompleted && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            // Atomic swap completed
            Assert.True(session.IsCompleted);
            Assert.Null(loop.TradeManager.GetSession(p1.Id));

            // p1 received 25000 silver (10000 + 25000 = 35000)
            Assert.Equal(35000u, p1.Money);
            Assert.False(p1.Package.Contains(sword.Id));

            // p2 lost 25000 silver (50000 - 25000 = 25000) and received sword
            Assert.Equal(25000u, p2.Money);
            Assert.True(p2.Package.Contains(sword.Id));
        }
        finally
        {
            await loop.StopAsync();
        }
    }

    [Fact]
    public async Task Guild_Creation_And_Donation()
    {
        var (loop, map) = CreateTestEnvironment();

        var sock1 = new MockSocket();
        var leader = new Player(4001, "SynLeader", 1) { SocketId = 101, Socket = sock1, Money = 100000 };

        loop.AddPlayer(leader, 1002, 30, 30);
        sock1.SentPackets.Clear();
        loop.Start();

        try
        {
            // 1. Create Syndicate
            var syn = loop.SyndicateManager.CreateSyndicate("DragonOrder", leader);
            Assert.NotNull(syn);
            Assert.Equal("DragonOrder", syn.Name);
            Assert.True(syn.IsLeader(leader.Id));

            // 2. Leader donates 30,000 silver to guild fund
            var donateMsg = MsgSyndicate.Create(SyndicateAction.DonateMoney, targetId: 30000);
            var writerDonate = new PacketWriter();
            try
            {
                donateMsg.Encode(ref writerDonate);
                loop.Post(CreateClientMessage(leader.SocketId, PacketType.Syndicate, writerDonate.AsSpan().ToArray()));
            }
            finally { writerDonate.Dispose(); }

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (syn.Money == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.Equal(30000, syn.Money);
            Assert.Equal(70000u, leader.Money);

            // Leader received updated MsgSynInfo
            Assert.Contains(sock1.SentPackets, p => (PacketType)BitConverter.ToUInt16(p, 2) == PacketType.SynInfo);
        }
        finally
        {
            await loop.StopAsync();
        }
    }
}
