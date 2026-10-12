// Conquer.Shared — ID ranges ported from the original C++ source.
// Original: Share/define.h (ID ranges section)

namespace Conquer.Shared;

/// <summary>
/// Entity ID ranges from the original server. Used for collision-free dynamic id allocation across MapGroups.
/// Original: Share/define.h — see the ID range constants.
/// </summary>
public static class IdRanges
{
    // Scene IDs
    public const uint SceneIdFirst = 1;
    public const uint SceneIdLast = 299999;

    // System NPCs (fixed to database id)
    public const uint SysNpcIdFirst = 1;
    public const uint SysNpcIdLast = 99999;

    // Dynamic NPCs
    public const uint DynaNpcIdFirst = 100001;
    public const uint DynaNpcIdLast = 199999;

    // Monsters (NPC server range)
    public const uint MonsterIdFirst = 400001;
    public const uint MonsterIdLast = 499999;

    // Pets
    public const uint PetIdFirst = 500001;
    public const uint PetIdLast = 599999;

    // NPC server overall range
    public const uint NpcServerIdFirst = 400001;
    public const uint NpcServerIdLast = 699999;

    // Call pets (summoned)
    public const uint CallPetIdFirst = 700001;
    public const uint CallPetIdLast = 799999;

    // Traps
    public const uint TrapIdFirst = 900001;
    public const uint TrapIdLast = 999999;
    public const uint MagicTrapIdFirst = 900001;
    public const uint MagicTrapIdLast = 989999;
    public const uint SysTrapIdFirst = 990001;
    public const uint SysTrapIdLast = 999999;

    // Players
    public const uint PlayerIdFirst = 1000000;
    public const uint PlayerIdLast = 1999999999;

    // Eudemons
    public const uint EudemonIdFirst = 2000000000;
    public const uint EudemonIdLast = 3999999999;

    // Client-generated IDs
    public const uint ClientIdFirst = 4000000000;
}

/// <summary>
/// Object type flags. Original: Share/define.h enum { OBJ_NONE, ... }.
/// </summary>
[Flags]
public enum ObjectType : ushort
{
    None    = 0x1000,
    User    = 0x1001,
    Monster = 0x1002,
    Item    = 0x1004,
    Map     = 0x1008,
    Friend  = 0x1010,
    Npc     = 0x1040,
    MapItem = 0x1080,
    Syn     = 0x1100,
    Booth   = 0x1200,
    Trap    = 0x1400,
    Tutor   = 0x1800,
}

/// <summary>
/// Default login coordinates. Original: Share/define.h.
/// </summary>
public static class DefaultLogin
{
    public const int MapGroup = 0;
    public const int MapId = 1000;
    public const int PosX = 362;
    public const int PosY = 594;
}
