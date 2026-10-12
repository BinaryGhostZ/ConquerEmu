// Conquer.NpcServer — Monster AI action modes.
// Original: my_server/NpcKernel/Npc.h:198
// enum NPC_MODE { MODE_DORMANCY = 0, MODE_IDLE, MODE_WALK, MODE_GOTO, MODE_TALK, MODE_FOLLOW, MODE_ESCAPE, MODE_FORWARD, MODE_ATTACK };

namespace Conquer.NpcServer;

/// <summary>
/// NPC action and AI execution modes.
/// Original: my_server/NpcKernel/Npc.h:198 enum NPC_MODE.
/// </summary>
public enum NpcMode
{
    Dormancy = 0,
    Idle = 1,
    Walk = 2,
    Goto = 3,
    Talk = 4,
    Follow = 5,
    Escape = 6,
    Forward = 7,
    Attack = 8,
}
