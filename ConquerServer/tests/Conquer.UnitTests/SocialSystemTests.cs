// Conquer.UnitTests — Unit tests for Phase 8: Social Systems (Chat, Teams, Trading, Guilds, Friends).
// Original: MsgTalk.h, MsgTeam.h, MsgTeamMember.h, MsgTrade.h, MsgSyndicate.h, MsgFriend.h, Team.cpp, SquareDeal.cpp, Syndicate.cpp

using Conquer.Data;
using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages.Packets;

namespace Conquer.UnitTests;

public class SocialSystemTests
{
    [Fact]
    public void MsgTalk_EncodeDecode_RoundTrip()
    {
        var original = new MsgTalk(
            WordsColor: 0x00FF00,
            Channel: ChatChannel.Whisper,
            Style: 1,
            Time: 123456,
            Speaker: "Hero",
            Hearer: "Friend",
            Emotion: "Smile",
            Words: "Hello, Conquer!");

        var writer = new PacketWriter();
        try
        {
            original.Encode(ref writer);
            var span = writer.AsSpan();

            var decoded = MsgTalk.Decode(span);

            Assert.Equal(original.WordsColor, decoded.WordsColor);
            Assert.Equal(original.Channel, decoded.Channel);
            Assert.Equal(original.Style, decoded.Style);
            Assert.Equal(original.Time, decoded.Time);
            Assert.Equal(original.Speaker, decoded.Speaker);
            Assert.Equal(original.Hearer, decoded.Hearer);
            Assert.Equal(original.Emotion, decoded.Emotion);
            Assert.Equal(original.Words, decoded.Words);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void MsgTeam_EncodeDecode_RoundTrip()
    {
        var original = MsgTeam.Create(TeamAction.ApplyJoin, playerId: 100055);

        var writer = new PacketWriter();
        try
        {
            original.Encode(ref writer);
            var span = writer.AsSpan();

            Assert.Equal(MsgTeam.PacketSize + WireProtocol.TrailerSize, span.Length);
            var decoded = MsgTeam.Decode(span);

            Assert.Equal(original.Action, decoded.Action);
            Assert.Equal(original.PlayerId, decoded.PlayerId);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void MsgTeamMember_EncodeDecode_RoundTrip()
    {
        var members = new List<TeamMemberRecord>
        {
            new("LeaderOne", 1001, 1001, 2000, 1850),
            new("WarriorTwo", 1002, 1002, 1500, 1500),
            new("TaoistThree", 1003, 1003, 1200, 800)
        };

        var original = MsgTeamMember.Create(TeamMemberAction.AddMember, members);

        var writer = new PacketWriter();
        try
        {
            original.Encode(ref writer);
            var span = writer.AsSpan();

            Assert.Equal(MsgTeamMember.BaseHeaderSize + 3 * TeamMemberRecord.RecordSize + WireProtocol.TrailerSize, span.Length);
            var decoded = MsgTeamMember.Decode(span);

            Assert.Equal(original.Action, decoded.Action);
            Assert.Equal(3, decoded.Members.Count);
            Assert.Equal("LeaderOne", decoded.Members[0].Name);
            Assert.Equal(1001u, decoded.Members[0].Id);
            Assert.Equal(2000, decoded.Members[0].MaxHp);
            Assert.Equal(1850, decoded.Members[0].Hp);

            Assert.Equal("WarriorTwo", decoded.Members[1].Name);
            Assert.Equal(1002u, decoded.Members[1].Id);

            Assert.Equal("TaoistThree", decoded.Members[2].Name);
            Assert.Equal(1003u, decoded.Members[2].Id);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void MsgTrade_EncodeDecode_RoundTrip()
    {
        var original = MsgTrade.Create(TradeAction.AddMoney, data: 500000);

        var writer = new PacketWriter();
        try
        {
            original.Encode(ref writer);
            var span = writer.AsSpan();

            Assert.Equal(MsgTrade.PacketSize + WireProtocol.TrailerSize, span.Length);
            var decoded = MsgTrade.Decode(span);

            Assert.Equal(original.Action, decoded.Action);
            Assert.Equal(original.Data, decoded.Data);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void MsgSyndicate_EncodeDecode_RoundTrip()
    {
        var original = MsgSyndicate.Create(
            SyndicateAction.ApplyJoin,
            targetId: 501,
            fealtyId: 0,
            text: "Please let me join!");

        var writer = new PacketWriter();
        try
        {
            original.Encode(ref writer);
            var span = writer.AsSpan();

            var decoded = MsgSyndicate.Decode(span);

            Assert.Equal(original.Action, decoded.Action);
            Assert.Equal(original.TargetId, decoded.TargetId);
            Assert.Equal(original.FealtyId, decoded.FealtyId);
            Assert.Equal(original.Text, decoded.Text);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void MsgSynInfo_EncodeDecode_RoundTrip()
    {
        var original = MsgSynInfo.Create(
            synId: 42,
            fealtyId: 0,
            fund: 10_000_000,
            population: 15,
            rank: 1,
            saint: 0,
            name: "DragonGuild",
            leaderName: "DragonMaster");

        var writer = new PacketWriter();
        try
        {
            original.Encode(ref writer);
            var span = writer.AsSpan();

            Assert.Equal(MsgSynInfo.PacketSize + WireProtocol.TrailerSize, span.Length);
            var decoded = MsgSynInfo.Decode(span);

            Assert.Equal(original.SynId, decoded.SynId);
            Assert.Equal(original.FealtyId, decoded.FealtyId);
            Assert.Equal(original.Fund, decoded.Fund);
            Assert.Equal(original.Population, decoded.Population);
            Assert.Equal("DragonGuild", decoded.Name);
            Assert.Equal("DragonMaster", decoded.LeaderName);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void MsgFriend_EncodeDecode_RoundTrip()
    {
        var original = MsgFriend.Create(
            FriendAction.Online,
            friendId: 8888,
            name: "BestFriend",
            isOnline: true,
            lookFace: 1002,
            level: 70);

        var writer = new PacketWriter();
        try
        {
            original.Encode(ref writer);
            var span = writer.AsSpan();

            Assert.Equal(MsgFriend.PacketSize + WireProtocol.TrailerSize, span.Length);
            var decoded = MsgFriend.Decode(span);

            Assert.Equal(original.FriendId, decoded.FriendId);
            Assert.Equal(original.Action, decoded.Action);
            Assert.True(decoded.IsOnline);
            Assert.Equal((byte)70, decoded.Level);
            Assert.Equal("BestFriend", decoded.Name);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void TeamManager_Lifecycle_And_Capacity()
    {
        var manager = new TeamManager();
        var leader = new Player(101, "Leader", 1);
        var p2 = new Player(102, "Member2", 2);
        var p3 = new Player(103, "Member3", 3);
        var p4 = new Player(104, "Member4", 4);
        var p5 = new Player(105, "Member5", 5);
        var p6 = new Player(106, "Member6", 6);

        var team = manager.CreateTeam(leader);
        Assert.NotNull(team);
        Assert.Single(team.Members);
        Assert.True(team.IsLeader(leader.Id));

        Assert.True(manager.AddMember(team.Id, p2));
        Assert.True(manager.AddMember(team.Id, p3));
        Assert.True(manager.AddMember(team.Id, p4));
        Assert.True(manager.AddMember(team.Id, p5));
        Assert.Equal(Team.MaxMembers, team.Members.Count);
        Assert.True(team.IsFull);

        // Cannot exceed max capacity of 5 members
        Assert.False(manager.AddMember(team.Id, p6));

        // Member leaves
        Assert.True(manager.LeaveTeam(p5));
        Assert.Equal(4, team.Members.Count);
        Assert.False(team.IsFull);

        // Leader leaves -> dismisses whole team
        Assert.True(manager.LeaveTeam(leader));
        Assert.Null(manager.GetTeam(team.Id));
        Assert.Null(manager.GetPlayerTeam(p2.Id));
    }

    [Fact]
    public void TradeSession_TwoPhaseCommit_AtomicSwap()
    {
        var p1 = new Player(1, "Trader1", 1) { PosX = 100, PosY = 100, MapId = 1002, Money = 10000 };
        var p2 = new Player(2, "Trader2", 2) { PosX = 102, PosY = 102, MapId = 1002, Money = 5000 };

        var itemType1 = new ItemTypeModel { Id = 410015, Name = "Blade", Amount = 100, AmountLimit = 100 };
        var itemType2 = new ItemTypeModel { Id = 130005, Name = "Armor", Amount = 100, AmountLimit = 100 };

        var item1 = new Item(501, itemType1, p1.Id, ItemPosition.Backpack);
        var item2 = new Item(502, itemType2, p2.Id, ItemPosition.Backpack);

        p1.Package.TryAdd(item1);
        p2.Package.TryAdd(item2);

        var session = new TradeSession(p1, p2);

        Assert.True(session.TryAddItem(p1, item1));
        Assert.True(session.TryAddMoney(p1, 2000));

        Assert.True(session.TryAddItem(p2, item2));
        Assert.True(session.TryAddMoney(p2, 1000));

        // Confirming
        Assert.False(session.BothConfirmed);
        session.Confirm(p1);
        Assert.False(session.BothConfirmed);
        session.Confirm(p2);
        Assert.True(session.BothConfirmed);

        // Commit atomic swap
        Assert.True(session.TryCommit());

        // Verify items were swapped
        Assert.False(p1.Package.Contains(501));
        Assert.True(p1.Package.Contains(502));

        Assert.False(p2.Package.Contains(502));
        Assert.True(p2.Package.Contains(501));

        // Verify silver was swapped:
        // p1 started 10000, gave 2000, received 1000 -> 9000
        // p2 started 5000, gave 1000, received 2000 -> 6000
        Assert.Equal(9000u, p1.Money);
        Assert.Equal(6000u, p2.Money);
    }

    [Fact]
    public void TradeSession_Abort_PreservesState()
    {
        var p1 = new Player(1, "Trader1", 1) { PosX = 100, PosY = 100, MapId = 1002, Money = 10000 };
        var p2 = new Player(2, "Trader2", 2) { PosX = 102, PosY = 102, MapId = 1002, Money = 5000 };

        var itemType1 = new ItemTypeModel { Id = 410015, Name = "Blade", Amount = 100, AmountLimit = 100 };
        var item1 = new Item(501, itemType1, p1.Id, ItemPosition.Backpack);
        p1.Package.TryAdd(item1);

        var session = new TradeSession(p1, p2);
        session.TryAddItem(p1, item1);
        session.TryAddMoney(p1, 5000);

        session.Abort();

        Assert.False(session.TryCommit());

        // Items and money untouched
        Assert.True(p1.Package.Contains(501));
        Assert.False(p2.Package.Contains(501));
        Assert.Equal(10000u, p1.Money);
        Assert.Equal(5000u, p2.Money);
    }

    [Fact]
    public void SyndicateManager_Ranks_And_Donations()
    {
        var manager = new SyndicateManager();
        var leader = new Player(201, "GuildLeader", 1) { Money = 50000 };
        var member = new Player(202, "GuildMember", 2) { Money = 20000 };

        var syn = manager.CreateSyndicate("Honor", leader);
        Assert.NotNull(syn);
        Assert.Equal("Honor", syn.Name);
        Assert.Equal(leader.Id, syn.LeaderId);

        Assert.True(manager.JoinSyndicate(syn.Id, member));
        Assert.Equal(2, syn.Members.Count);
        Assert.Equal((ushort)SyndicateRank.Member, syn.GetMemberRank(member.Id));

        // Donation
        Assert.True(manager.DonateMoney(member, 15000));
        Assert.Equal(15000, syn.Money);
        Assert.Equal(5000u, member.Money);

        // Leader can kick member
        Assert.True(manager.KickMember(syn.Id, leader.Id, member.Id));
        Assert.Single(syn.Members);
        Assert.Null(manager.GetPlayerSyndicate(member.Id));
    }

    [Fact]
    public void FriendManager_OnlineStatus_And_Relationships()
    {
        var manager = new FriendManager();
        uint user1 = 1001;
        uint user2 = 1002;

        Assert.True(manager.AddFriend(user1, user2, "Alice"));
        Assert.True(manager.IsFriend(user1, user2));
        Assert.False(manager.IsFriend(user2, user1)); // directed relationship

        var friends = manager.GetFriends(user1);
        Assert.Single(friends);
        Assert.Equal("Alice", friends[user2]);

        Assert.True(manager.RemoveFriend(user1, user2));
        Assert.False(manager.IsFriend(user1, user2));
    }
}
