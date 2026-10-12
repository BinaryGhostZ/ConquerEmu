// Conquer.Shared — Wire protocol packet types (client ↔ server).
// Original: Share/networkdef.h — all _MSG_* constants.

namespace Conquer.Shared;

/// <summary>
/// Client ↔ server packet type IDs. Original: Share/networkdef.h.
/// Values are exact from the source.
/// </summary>
public enum PacketType : ushort
{
    None            = 0,

    // General messages (_MSG_GENERAL = 1000)
    Register        = 1001,
    Login           = 1002,
    Logout          = 1003,
    Talk            = 1004,
    Walk            = 1005,
    UserInfo        = 1006,
    Attack          = 1007,  // _MSG_ATTACK
    ItemInfo        = 1008,
    Item            = 1009,
    Action          = 1010,
    Accident        = 1011,
    Tick            = 1012,
    Room            = 1013,
    Player          = 1014,
    Name            = 1015,
    Weather         = 1016,
    UserAttrib      = 1017,
    Role            = 1018,
    Friend          = 1019,
    Effect          = 1020,
    QuizGame        = 1021,
    Interact        = 1022,
    Team            = 1023,
    Allot           = 1024,
    WeaponSkill     = 1025,
    TeamMember      = 1026,
    GemEmbed        = 1027,

    Connect         = 1052,
    Trade           = 1056,
    BattleSystem    = 1057,

    // New info messages (_MSG_GENERAL + 101..114)
    MapItem         = 1101,
    Package         = 1102,
    MagicInfo       = 1103,
    FlushExp        = 1104,
    MagicEffect     = 1105,
    SynAttrInfo     = 1106,
    Syndicate       = 1107,
    ItemInfoEx      = 1108,
    NpcInfoEx       = 1109,
    MapInfo         = 1110,
    MessageBoard    = 1111,
    SynMemberInfo   = 1112,
    Dice            = 1113,
    SynInfo         = 1114,

    // Extended NPC / dialog messages (_MSG_GENERAL + 1030..)
    NpcInfo         = 2030,
    Npc             = 2031,
    Dialog          = 2032,
    FriendInfo      = 2033,
    Time            = 2034,
    PetInfo         = 2035,
    DataArray       = 2036,
    EudemonAttrib   = 2037,
    SchoolMember    = 2038,
    PlayerTask      = 2039,
    TaskList        = 2040,
    AnnounceList    = 2041,
    AnnounceInfo    = 2042,
    Auction         = 2043,
    ChatRoom        = 2044,
    ItemAttrib      = 2045,

    // Walk extended
    WalkEx          = 3005,

    // NPC server internal
    AiNpcInfo       = 20001,

    // Account server messages (_MSG_ACCOUNTSERVER = 1000)
    LoginAccount    = 1002,  // _MSG_LOGIN_A — same numeric value as Login, distinguished by context
    AccountA        = 1051,
    // Connect = 1052 (shared)
    FeeA            = 1200,
    QueryFeeA       = 1201,
}
