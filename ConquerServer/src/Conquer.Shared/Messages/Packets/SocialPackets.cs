// Conquer.Shared — Social packet models (Chat, Teams, Trading, Guilds, Friends).
// Original: MapGroupKernel/Network/MsgTalk.h CMsgTalk (_MSG_TALK = 1004)
//           MapGroupKernel/Network/MsgTeam.h CMsgTeam (_MSG_TEAM = 1023)
//           MapGroupKernel/Network/MsgTeamMember.h CMsgTeamMember (_MSG_TEAMMEMBER = 1026)
//           MapGroupKernel/Network/MsgTrade.h CMsgTrade (_MSG_TRADE = 1056)
//           MapGroupKernel/Network/MsgSyndicate.h CMsgSyndicate (_MSG_SYNDICATE = 1107)
//           MapGroupKernel/Network/MsgSynInfo.h CMsgSynInfo (_MSG_SYNINFO = 1114)
//           MapGroupKernel/Network/MsgFriend.h CMsgFriend (_MSG_FRIEND = 1019)

namespace Conquer.Shared.Messages.Packets;

#region Chat / MsgTalk (1004)

/// <summary>
/// Text attributes / chat channels.
/// <para><b>Original:</b> Share/networkdef.h _TXTATR_*</para>
/// </summary>
public enum ChatChannel : ushort
{
    Normal      = 2000,
    Private     = 2001,
    Action      = 2002,
    Team        = 2003,
    Syndicate   = 2004,
    System      = 2005,
    Family      = 2006,
    Talk        = 2007,
    Yelp        = 2008,
    Friend      = 2009,
    Global      = 2010,
    Gm          = 2011,
    Whisper     = 2012,
    Ghost       = 2013,
    Serve       = 2014,
    Register    = 2100,
    Entrance    = 2101,
    Shop        = 2102,
    MsgTrade    = 2201,
    MsgFriend   = 2202,
    MsgTeam     = 2203,
    MsgSyn      = 2204,
    MsgSystem   = 2206
}

/// <summary>
/// Chat message packet.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgTalk.h CMsgTalk (_MSG_TALK = 1004)</para>
/// Wire format: [len:2][type:2][dwWordsColor:4][unTxtAttribute:2][unTxtStyle:2][dwTime:4]
/// followed by 4 packed strings: Speaker, Hearer, Emotion, Words.
/// </summary>
public readonly record struct MsgTalk(
    uint WordsColor,
    ChatChannel Channel,
    ushort Style,
    uint Time,
    string Speaker,
    string Hearer,
    string Emotion,
    string Words)
{
    public const int BaseHeaderSize = 16;

    public static MsgTalk Create(string speaker, string hearer, string words, ChatChannel channel = ChatChannel.Normal, uint wordsColor = 0xFFFFFF)
    {
        return new MsgTalk(
            WordsColor: wordsColor,
            Channel: channel,
            Style: 0,
            Time: 0,
            Speaker: speaker,
            Hearer: hearer,
            Emotion: string.Empty,
            Words: words);
    }

    public static MsgTalk CreateSystem(string words, ChatChannel channel = ChatChannel.System)
    {
        return Create("SYSTEM", "ALLUSERS", words, channel);
    }

    public static MsgTalk CreateWhisper(string speaker, string targetUser, string words)
    {
        return Create(speaker, targetUser, words, ChatChannel.Whisper);
    }

    public static MsgTalk CreateTeam(string speaker, string words)
    {
        return Create(speaker, "Team", words, ChatChannel.Team);
    }

    public static MsgTalk CreateSyndicate(string speaker, string words)
    {
        return Create(speaker, "Guild", words, ChatChannel.Syndicate);
    }

    public static MsgTalk Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint wordsColor = reader.ReadUInt32();
        ushort channel = reader.ReadUInt16();
        ushort style = reader.ReadUInt16();
        uint time = reader.ReadUInt32();

        string speaker = string.Empty;
        string hearer = string.Empty;
        string emotion = string.Empty;
        string words = string.Empty;

        if (reader.Remaining > 0)
        {
            var strings = reader.ReadStringList();
            if (strings.Length > 0) speaker = strings[0];
            if (strings.Length > 1) hearer = strings[1];
            if (strings.Length > 2) emotion = strings[2];
            if (strings.Length > 3) words = strings[3];
        }

        return new MsgTalk(
            WordsColor: wordsColor,
            Channel: (ChatChannel)channel,
            Style: style,
            Time: time,
            Speaker: speaker,
            Hearer: hearer,
            Emotion: emotion,
            Words: words);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(WordsColor);
        writer.Write((ushort)Channel);
        writer.Write(Style);
        writer.Write(Time);
        writer.Write(Speaker ?? string.Empty, Hearer ?? string.Empty, Emotion ?? string.Empty, Words ?? string.Empty);
        writer.Finalize(PacketType.Talk);
    }
}

#endregion

#region Team / MsgTeam (1023) & MsgTeamMember (1026)

/// <summary>
/// Actions for MsgTeam.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgTeam.h _MSGTEAM_*</para>
/// </summary>
public enum TeamAction : ushort
{
    Create       = 0,
    ApplyJoin    = 1,
    Leave        = 2,
    AcceptInvite = 3,
    Invite       = 4,
    AgreeJoin    = 5,
    Dismiss      = 6,
    KickOut      = 7,
    CloseTeam    = 8,
    OpenTeam     = 9
}

/// <summary>
/// Team action packet.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgTeam.h CMsgTeam (_MSG_TEAM = 1023)</para>
/// Wire format: [len:2][type:2][unAction:2][pad:2][idPlayer:4] = 12 bytes.
/// </summary>
public readonly record struct MsgTeam(TeamAction Action, uint PlayerId)
{
    public const int PacketSize = 12;

    public static MsgTeam Create(TeamAction action, uint playerId = 0) => new(action, playerId);

    public static MsgTeam Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        ushort action = reader.ReadUInt16();
        reader.ReadUInt16(); // pad
        uint playerId = reader.ReadUInt32();
        return new MsgTeam((TeamAction)action, playerId);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write((ushort)Action);
        writer.Write((ushort)0); // pad
        writer.Write(PlayerId);
        writer.Finalize(PacketType.Team);
    }
}

/// <summary>
/// Actions for MsgTeamMember.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgTeamMember.h _MSG_TEAMMEMBER_*</para>
/// </summary>
public enum TeamMemberAction : byte
{
    AddMember     = 0,
    DropAddMember = 1
}

/// <summary>
/// Single team member info.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgTeamMember.h TeamMemberInfo (28 bytes)</para>
/// </summary>
public readonly record struct TeamMemberRecord(
    string Name,
    uint Id,
    uint LookFace,
    ushort MaxHp,
    ushort Hp)
{
    public const int RecordSize = 28;
}

/// <summary>
/// Team member list update packet.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgTeamMember.h CMsgTeamMember (_MSG_TEAMMEMBER = 1026)</para>
/// Wire format: [len:2][type:2][ucAction:1][ucAmount:1][pad:2] + ucAmount * [szName:16][id:4][dwLookFace:4][usMaxHp:2][usHp:2]
/// </summary>
public readonly record struct MsgTeamMember(
    TeamMemberAction Action,
    IReadOnlyList<TeamMemberRecord> Members)
{
    public const int BaseHeaderSize = 8;
    public const int MaxTeamMembers = 5;

    public static MsgTeamMember Create(TeamMemberAction action, IReadOnlyList<TeamMemberRecord> members) =>
        new(action, members);

    public static MsgTeamMember Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        byte action = reader.ReadUInt8();
        byte amount = reader.ReadUInt8();
        reader.ReadUInt16(); // pad

        var list = new List<TeamMemberRecord>(amount);
        for (int i = 0; i < amount && reader.Remaining >= TeamMemberRecord.RecordSize; i++)
        {
            string name = reader.ReadCString(16);
            uint id = reader.ReadUInt32();
            uint lookFace = reader.ReadUInt32();
            ushort maxHp = reader.ReadUInt16();
            ushort hp = reader.ReadUInt16();
            list.Add(new TeamMemberRecord(name, id, lookFace, maxHp, hp));
        }

        return new MsgTeamMember((TeamMemberAction)action, list);
    }

    public void Encode(ref PacketWriter writer)
    {
        byte count = (byte)Math.Min(Members.Count, MaxTeamMembers);
        writer.Write((byte)Action);
        writer.Write(count);
        writer.Write((ushort)0); // pad

        for (int i = 0; i < count; i++)
        {
            var m = Members[i];
            writer.Write(m.Name, 16);
            writer.Write(m.Id);
            writer.Write(m.LookFace);
            writer.Write(m.MaxHp);
            writer.Write(m.Hp);
        }

        writer.Finalize(PacketType.TeamMember);
    }
}

#endregion

#region Trade / MsgTrade (1056)

/// <summary>
/// Actions for MsgTrade.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgTrade.h _TRADE_*</para>
/// </summary>
public enum TradeAction : ushort
{
    None         = 0,
    Apply        = 1,
    Quit         = 2,
    Open         = 3,
    Success      = 4,
    False        = 5,
    AddItem      = 6,
    AddMoney     = 7,
    MoneyAll     = 8,
    SelfMoneyAll = 9,
    Ok           = 10,
    AddItemFail  = 11
}

/// <summary>
/// Trade action packet.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgTrade.h CMsgTrade (_MSG_TRADE = 1056)</para>
/// Wire format: [len:2][type:2][id/dwData:4][usAction:2][pad:2] = 12 bytes.
/// </summary>
public readonly record struct MsgTrade(uint Data, TradeAction Action)
{
    public const int PacketSize = 12;

    public static MsgTrade Create(TradeAction action, uint data = 0) => new(data, action);

    public static MsgTrade Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint data = reader.ReadUInt32();
        ushort action = reader.ReadUInt16();
        reader.ReadUInt16(); // pad
        return new MsgTrade(data, (TradeAction)action);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(Data);
        writer.Write((ushort)Action);
        writer.Write((ushort)0); // pad
        writer.Finalize(PacketType.Trade);
    }
}

#endregion

#region Guild / Syndicate: MsgSyndicate (1107) & MsgSynInfo (1114)

/// <summary>
/// Actions for MsgSyndicate.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgSyndicate.h APPLY_JOIN, etc.</para>
/// </summary>
public enum SyndicateAction : ushort
{
    None             = 0,
    ApplyJoin        = 1,
    InviteJoin       = 2,
    Leave            = 3,
    KickOutMember    = 4,
    QuerySynName     = 6,
    ClearAntagonize  = 10,
    DonateMoney      = 11,
    QuerySynAttr     = 12,
    SetSyn           = 14,
    SetWhiteSyn      = 17,
    SetBlackSyn      = 18,
    DestroySyn       = 19,
    SetMantle        = 20,
    ApplyAlly        = 21,
    ClearAlly        = 22,
    SetAntagonize    = 23,
    ClearAntagonize2 = 24,
    Demise           = 116
}

/// <summary>
/// Syndicate interaction packet.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgSyndicate.h CMsgSyndicate (_MSG_SYNDICATE = 1107)</para>
/// Wire format: [len:2][type:2][usAction:2][pad:2][idTarget:4][idFealty:4] + optional strings
/// </summary>
public readonly record struct MsgSyndicate(
    SyndicateAction Action,
    uint TargetId,
    uint FealtyId,
    string Text = "")
{
    public const int BaseHeaderSize = 16;

    public static MsgSyndicate Create(SyndicateAction action, uint targetId = 0, uint fealtyId = 0, string text = "") =>
        new(action, targetId, fealtyId, text);

    public static MsgSyndicate Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        ushort action = reader.ReadUInt16();
        reader.ReadUInt16(); // pad
        uint targetId = reader.ReadUInt32();
        uint fealtyId = reader.ReadUInt32();

        string text = string.Empty;
        if (reader.Remaining > 0)
        {
            var strings = reader.ReadStringList();
            if (strings.Length > 0) text = strings[0];
        }

        return new MsgSyndicate((SyndicateAction)action, targetId, fealtyId, text);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write((ushort)Action);
        writer.Write((ushort)0); // pad
        writer.Write(TargetId);
        writer.Write(FealtyId);

        if (!string.IsNullOrEmpty(Text))
        {
            writer.Write(Text);
        }

        writer.Finalize(PacketType.Syndicate);
    }
}

/// <summary>
/// Syndicate info packet.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgSynInfo.h CMsgSynInfo (_MSG_SYNINFO = 1114)</para>
/// Wire format: [len:2][type:2][idSyn:4][idFealty:4][dwFund:4][dwPopulation:4][ucRank:1][ucSaint:1][pad:2][szName:16][szLeader:16] = 52 bytes.
/// </summary>
public readonly record struct MsgSynInfo(
    uint SynId,
    uint FealtyId,
    uint Fund,
    uint Population,
    byte Rank,
    byte Saint,
    string Name,
    string LeaderName)
{
    public const int PacketSize = 56;

    public static MsgSynInfo Create(uint synId, uint fealtyId, uint fund, uint population, byte rank, byte saint, string name, string leaderName) =>
        new(synId, fealtyId, fund, population, rank, saint, name, leaderName);

    public static MsgSynInfo Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint synId = reader.ReadUInt32();
        uint fealtyId = reader.ReadUInt32();
        uint fund = reader.ReadUInt32();
        uint population = reader.ReadUInt32();
        byte rank = reader.ReadUInt8();
        byte saint = reader.ReadUInt8();
        reader.ReadUInt16(); // pad
        string name = reader.ReadCString(16);
        string leader = reader.ReadCString(16);

        return new MsgSynInfo(synId, fealtyId, fund, population, rank, saint, name, leader);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(SynId);
        writer.Write(FealtyId);
        writer.Write(Fund);
        writer.Write(Population);
        writer.Write(Rank);
        writer.Write(Saint);
        writer.Write((ushort)0); // pad
        writer.Write(Name, 16);
        writer.Write(LeaderName, 16);
        writer.Finalize(PacketType.SynInfo);
    }
}

#endregion

#region Friends / MsgFriend (1019)

/// <summary>
/// Actions for MsgFriend.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgFriend.h _FRIEND_*</para>
/// </summary>
public enum FriendAction : byte
{
    Apply   = 10,
    Accept  = 11,
    Online  = 12,
    Offline = 13,
    Break   = 14,
    GetInfo = 15
}

/// <summary>
/// Friend interaction packet.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgFriend.h CMsgFriend (_MSG_FRIEND = 1019)</para>
/// Wire format: [len:2][type:2][idFriend:4][dwLookFace:4][ucAction:1][ucOnline:1][ucLevel:1][pad:1][szName:16] = 32 bytes.
/// </summary>
public readonly record struct MsgFriend(
    uint FriendId,
    uint LookFace,
    FriendAction Action,
    bool IsOnline,
    byte Level,
    string Name)
{
    public const int PacketSize = 32;

    public static MsgFriend Create(FriendAction action, uint friendId, string name, bool isOnline = false, uint lookFace = 0, byte level = 0) =>
        new(friendId, lookFace, action, isOnline, level, name);

    public static MsgFriend Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint friendId = reader.ReadUInt32();
        uint lookFace = reader.ReadUInt32();
        byte action = reader.ReadUInt8();
        byte online = reader.ReadUInt8();
        byte level = reader.ReadUInt8();
        reader.ReadUInt8(); // pad
        string name = reader.ReadCString(16);

        return new MsgFriend(friendId, lookFace, (FriendAction)action, online != 0, level, name);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(FriendId);
        writer.Write(LookFace);
        writer.Write((byte)Action);
        writer.Write((byte)(IsOnline ? 1 : 0));
        writer.Write(Level);
        writer.Write((byte)0); // pad
        writer.Write(Name, 16);
        writer.Finalize(PacketType.Friend);
    }
}

#endregion
