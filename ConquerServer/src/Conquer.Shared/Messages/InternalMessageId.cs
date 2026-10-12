// Conquer.Shared — Internal (inter-thread) message IDs.
// Original: Share/protocol.h — KERNEL_*, MAPGROUP_*, WORLD_*, SOCKET_*, SHELL_*, DB_* enums.

namespace Conquer.Shared.Messages;

/// <summary>
/// Inter-thread message IDs used by the MessagePort system.
/// Original names preserved for traceability. Original: Share/protocol.h.
/// </summary>
public enum InternalMessageId
{
    // ---- Kernel messages (routed to the owning MapGroup) ----
    // Original: KERNEL_BASE = 1000
    KernelClientMsg         = 1001,
    KernelNpcMsg            = 1002,
    KernelCloseKernel       = 1003,
    KernelCreateSyndicate   = 1010,
    KernelDestroySyndicate  = 1011,
    KernelChangeSyndicate   = 1012,
    KernelCombineSyndicate  = 1013,
    KernelSupermanList      = 1014,
    KernelQuerySupermanList = 1015,

    // ---- MapGroup messages (between groups or from World/Socket to a group) ----
    // Original: MAPGROUP_BASE = 10000
    MapGroupLogin                   = 10001,
    MapGroupBroadcastMsg            = 10002,
    MapGroupSocketUserInfo          = 10003,
    MapGroupSendObjInfo             = 10004,
    MapGroupTransmitMsg             = 10005,
    MapGroupChangeMapGroup          = 10010,
    MapGroupCreateNewNpc            = 10011,
    MapGroupChangeTeam              = 10012,
    MapGroupRemoteCall              = 10013,
    MapGroupChangeNpc               = 10014,
    MapGroupDelTransNpc             = 10015,
    MapGroupDelAllMonster           = 10016,
    MapGroupSetMapSynId             = 10017,
    MapGroupLoadAllPet              = 10018,
    MapGroupRpc                     = 10019,
    MapGroupTransmitMsgForChatRoom  = 10020,

    // ---- World messages ----
    // Original: WORLD_BASE = 20000
    WorldShellTalk      = 20001,
    WorldSetProcessId   = 20002,
    WorldCloseSocket    = 20003,
    WorldUserNameMsg    = 20004,
    WorldUserIdMsg      = 20005,
    WorldLevelUp        = 20006,
    WorldQueryFee       = 20007,
    WorldSendTime       = 20008,
    WorldTransmitMsg    = 20009,

    // ---- Socket messages ----
    // Original: SOCKET_BASE = 30000
    SocketCloseSocket       = 30001,
    SocketSetProcessId      = 30002,
    SocketSendClientMsg     = 30003,
    SocketSendNpcMsg        = 30004,
    SocketSetNpcSocketId    = 30005,
    SocketSetNpcProcessId   = 30006,
    SocketChangeCode        = 30007,
    SocketBreakConnect      = 30008,
    SocketBreakAllConnect   = 30011,

    // ---- Shell messages ----
    // Original: SHELL_BASE = 40000
    ShellPrintText      = 40001,
    ShellKernelState    = 40002,

    // ---- DB messages ----
    // Original: DB_BASE = 50000
    DbQueryUserInfo     = 50001,
    DbMsgBaseInfo       = 50101,
    DbMsgFriends        = 50102,
    DbMsgEnemies        = 50103,
    DbMsgItems          = 50104,
    DbMsgWeaponSkill    = 50105,
    DbMsgMagics         = 50106,
    DbMsgSynAttr        = 50107,
}
