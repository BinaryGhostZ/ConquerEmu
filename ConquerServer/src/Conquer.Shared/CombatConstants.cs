// Conquer.Shared — Combat, PK, and Crime constants.
// Original: MapGroupKernel/User.h, User.cpp, BattleSystem.h

namespace Conquer.Shared;

/// <summary>
/// Constants governing combat calculations, PK states, and auto-attack timing.
/// <para><b>Original:</b> MapGroupKernel/User.h, BattleSystem.h</para>
/// </summary>
public static class CombatConstants
{
    /// <summary>PK points gained per murder of a white-named (innocent) player. Original: PKVALUE_ONEKILL = 20.</summary>
    public const int PkValueOneKill = 20;

    /// <summary>PK threshold where player name turns red. Original: PKVALUE_REDNAME = 21.</summary>
    public const int PkValueRedName = 21;

    /// <summary>PK threshold where player name turns black. Original: PKVALUE_BLACKNAME = 101.</summary>
    public const int PkValueBlackName = 101;

    /// <summary>Duration (in seconds) of flashing blue criminal status after attacking an innocent. Original: CRIME_KEEP_SECS = 30.</summary>
    public const int CrimeKeepSecs = 30;

    /// <summary>Default melee weapon attack speed in milliseconds.</summary>
    public const int DefaultAttackSpeedMs = 1000;

    /// <summary>Default melee attack reach in cells (Chebyshev distance).</summary>
    public const int DefaultMeleeRange = 1;
}

/// <summary>
/// Death animation/type modes.
/// <para><b>Original:</b> MapGroupKernel/I_Role.h DIE_*</para>
/// </summary>
public enum DieMode : uint
{
    Normal = 1,
    CriticalHit = 2,
    Magic = 3,
    Floating = 4,
    ArrowShoot = 5
}

