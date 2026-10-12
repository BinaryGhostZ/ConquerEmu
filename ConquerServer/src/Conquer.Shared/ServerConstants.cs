// Conquer.Shared — Port IDs and broadcast constants.
// Original: Share/define.h — MSGPORT_*, BCAST_NPCID, SOCKET_NONE, PROCESS_NONE

namespace Conquer.Shared;

/// <summary>
/// Port (thread/loop) identifiers. Original: Share/define.h.
/// </summary>
public static class PortId
{
    /// <summary>Shell (GUI/console output). Original: MSGPORT_SHELL.</summary>
    public const int Shell = 0;

    /// <summary>Socket kernel (all network I/O). Original: MSGPORT_SOCKET.</summary>
    public const int Socket = 1;

    /// <summary>World kernel (login/logout, user list, syndicates). Original: MSGPORT_WORLD.</summary>
    public const int World = 2;

    /// <summary>First MapGroup port. Original: MSGPORT_MAPGROUP_FIRST. Groups are at ports 3..3+N-1.</summary>
    public const int MapGroupFirst = 3;

    /// <summary>Get the port for a MapGroup by its index (0-based).</summary>
    public static int MapGroup(int groupIndex) => MapGroupFirst + groupIndex;
}

/// <summary>
/// Sentinel values. Original: Share/define.h.
/// </summary>
public static class Sentinel
{
    /// <summary>No socket assigned. Original: SOCKET_NONE = -1.</summary>
    public const int SocketNone = -1;

    /// <summary>No process/loop assigned. Original: PROCESS_NONE = -1.</summary>
    public const int ProcessNone = -1;

    /// <summary>Invalid room id. Original: INVALID_ROOMID = -1.</summary>
    public const int InvalidRoomId = -1;

    /// <summary>Broadcast NPC id — means "send to all NPCs". Original: BCAST_NPCID = 0.</summary>
    public const int BroadcastNpcId = 0;
}

/// <summary>
/// Chat/talk message attributes. Original: Share/networkdef.h _TXTATR_* constants.
/// </summary>
public enum TalkAttribute : ushort
{
    // Original: _TXTATR_NORMAL = 2000
    Normal      = 2000,
    Private     = 2001,
    Action      = 2002,
    Team        = 2003,
    Syndicate   = 2004,
    System      = 2005,
    Family      = 2006,
    Talk        = 2007,
    Yelp        = 2008,
    FriendTalk  = 2009,
    Global      = 2010,
    Gm          = 2011,
    Whisper     = 2012,
    Ghost       = 2013,
    Serve       = 2014,

    Register    = 2100,
    Entrance    = 2101,
    Shop        = 2102,
    PetTalk     = 2103,
    CryOut      = 2104,
    WebPage     = 2105,
    NewMessage  = 2106,
    Task        = 2107,
    SynWarFirst = 2108,
    SynWarNext  = 2109,
    LeaveWord   = 2110,
    SynAnnounce = 2111,
    MessageBox  = 2112,
    Reject      = 2113,
    SynTenet    = 2114,

    MsgTrade    = 2201,
    MsgFriend   = 2202,
    MsgTeam     = 2203,
    MsgSyn      = 2204,
    MsgOther    = 2205,
    MsgSystem   = 2206,

    DialogBegin = 2500,
    DialogEnd   = 2600,
}

/// <summary>
/// Text display style flags. Original: Share/networkdef.h _TXT_* constants.
/// </summary>
[Flags]
public enum TalkStyle : ushort
{
    Normal  = 0x0000,
    Scroll  = 0x0001,
    Flash   = 0x0002,
    Blast   = 0x0008,
}
