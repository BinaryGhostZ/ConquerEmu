// Conquer.Runtime — Status effects and KeepEffect flags management.
// Original: MapGroupKernel/I_Role.h KEEPEFFECT_*, STATUS_*
//           MapGroupKernel/Status.h, Status.cpp

namespace Conquer.Runtime;

/// <summary>
/// 64-bit status effect flags broadcast to observers and synchronized with client.
/// Original: MapGroupKernel/I_Role.h KEEPEFFECT_*.
/// </summary>
[Flags]
public enum KeepEffectFlags : ulong
{
    Normal          = 0x00000000,
    TeamLeader      = 0x00000001,
    Die             = 0x00000002,
    Ghost           = 0x00000004,
    Disappearing    = 0x00000008,
    Crime           = 0x00000010, // Blue name flashing
    Red             = 0x00000020, // PK red name
    DeepRed         = 0x00000040, // PK black name
    SynCrime        = 0x00000080,
    Poison          = 0x00000100, // Poison green aura
    Hidden          = 0x00000200, // Invisibility
    Freeze          = 0x00000400, // Frozen/stunned
    SuperSoldier    = 0x00000800, // Superman aura
    Defense1        = 0x00002000, // Shield aura
    Attack          = 0x00010000, // Attack boost
    AtkSpeed        = 0x00020000, // Speed boost
    MagicDamage     = 0x00040000,
    MagicDefense    = 0x00080000,
    Reflect         = 0x00100000,
    ReflectMagic    = 0x00200000,
    Slowdown1       = 0x00400000,
    XpFull          = 0x0000004000000000, // Glowing XP indicator
    Fly             = 0x0000080000000000, // Flying
    Cyclone         = 0x0000000800000000, // Cyclone frenzy
}

/// <summary>
/// Status effect IDs matching cq_magictype status values.
/// Original: MapGroupKernel/I_Role.h STATUS_*.
/// </summary>
public enum StatusEffectType : uint
{
    Normal          = 0,
    Die             = 1,
    Crime           = 2,
    Poison          = 3,
    TeamLeader      = 4,
    PkValue         = 5,
    MagicDefense    = 10,
    Attack          = 12,
    Reflect         = 13,
    Hidden          = 14,
    AtkSpeed        = 16,
    Lurker          = 17,
    ReflectMagic    = 19,
    SuperDef        = 20,
    SuperAtk        = 21,
    Defense1        = 24,
    Freeze          = 27,
    Palsy           = 30,
    Fly             = 38,
    Cyclone         = 100,
    Superman        = 101,
    Shield          = 102,
    XpFull          = 103
}

/// <summary>
/// An active status effect on an entity.
/// </summary>
public sealed class StatusEffect
{
    public StatusEffectType Type { get; }
    public int Power { get; }
    public DateTime Expiration { get; set; }
    public int IntervalMs { get; }
    public DateTime NextTick { get; set; }

    public StatusEffect(StatusEffectType type, int power, DateTime expiration, int intervalMs = 0)
    {
        Type = type;
        Power = power;
        Expiration = expiration;
        IntervalMs = intervalMs;
        NextTick = intervalMs > 0 ? DateTime.UtcNow.AddMilliseconds(intervalMs) : DateTime.MaxValue;
    }
}

/// <summary>
/// Manages status effects, periodic ticks, and 64-bit KeepEffect flags for an entity.
/// Original: MapGroupKernel/Status.cpp, User.cpp AttachStatus, DetachStatus.
/// </summary>
public sealed class StatusManager
{
    private readonly Dictionary<StatusEffectType, StatusEffect> _statuses = [];

    public KeepEffectFlags Flags { get; private set; } = KeepEffectFlags.Normal;

    public IReadOnlyCollection<StatusEffect> ActiveStatuses => _statuses.Values;

    public bool HasStatus(StatusEffectType type) => _statuses.ContainsKey(type);

    public StatusEffect? GetStatus(StatusEffectType type) => _statuses.GetValueOrDefault(type);

    public bool AttachStatus(StatusEffectType type, int power, int durationSecs, int intervalMs = 0, DateTime? now = null)
    {
        var currentTime = now ?? DateTime.UtcNow;
        var expiration = durationSecs > 0 ? currentTime.AddSeconds(durationSecs) : DateTime.MaxValue;

        var effect = new StatusEffect(type, power, expiration, intervalMs);
        _statuses[type] = effect;

        ApplyFlag(type);
        return true;
    }

    public bool DetachStatus(StatusEffectType type)
    {
        if (_statuses.Remove(type))
        {
            RemoveFlag(type);
            return true;
        }
        return false;
    }

    private void ApplyFlag(StatusEffectType type)
    {
        Flags |= type switch
        {
            StatusEffectType.Crime => KeepEffectFlags.Crime,
            StatusEffectType.Poison => KeepEffectFlags.Poison,
            StatusEffectType.Freeze => KeepEffectFlags.Freeze,
            StatusEffectType.Hidden => KeepEffectFlags.Hidden,
            StatusEffectType.Attack => KeepEffectFlags.Attack,
            StatusEffectType.SuperAtk => KeepEffectFlags.Attack,
            StatusEffectType.Defense1 => KeepEffectFlags.Defense1,
            StatusEffectType.SuperDef => KeepEffectFlags.Defense1,
            StatusEffectType.Shield => KeepEffectFlags.Defense1,
            StatusEffectType.AtkSpeed => KeepEffectFlags.AtkSpeed,
            StatusEffectType.Superman => KeepEffectFlags.SuperSoldier,
            StatusEffectType.Cyclone => KeepEffectFlags.Cyclone,
            StatusEffectType.Fly => KeepEffectFlags.Fly,
            StatusEffectType.XpFull => KeepEffectFlags.XpFull,
            _ => KeepEffectFlags.Normal
        };
    }

    private void RemoveFlag(StatusEffectType type)
    {
        Flags &= ~(type switch
        {
            StatusEffectType.Crime => KeepEffectFlags.Crime,
            StatusEffectType.Poison => KeepEffectFlags.Poison,
            StatusEffectType.Freeze => KeepEffectFlags.Freeze,
            StatusEffectType.Hidden => KeepEffectFlags.Hidden,
            StatusEffectType.Attack => KeepEffectFlags.Attack,
            StatusEffectType.SuperAtk => KeepEffectFlags.Attack,
            StatusEffectType.Defense1 => KeepEffectFlags.Defense1,
            StatusEffectType.SuperDef => KeepEffectFlags.Defense1,
            StatusEffectType.Shield => KeepEffectFlags.Defense1,
            StatusEffectType.AtkSpeed => KeepEffectFlags.AtkSpeed,
            StatusEffectType.Superman => KeepEffectFlags.SuperSoldier,
            StatusEffectType.Cyclone => KeepEffectFlags.Cyclone,
            StatusEffectType.Fly => KeepEffectFlags.Fly,
            StatusEffectType.XpFull => KeepEffectFlags.XpFull,
            _ => KeepEffectFlags.Normal
        });
    }

    /// <summary>
    /// Processes timer ticks: expires timed effects and handles periodic triggers (e.g. poison).
    /// </summary>
    public void OnTimer(DateTime now, out List<StatusEffectType> expired, out List<int> periodicDamage)
    {
        expired = [];
        periodicDamage = [];

        foreach (var (type, effect) in _statuses)
        {
            if (now >= effect.Expiration)
            {
                expired.Add(type);
                continue;
            }

            if (effect.IntervalMs > 0 && now >= effect.NextTick)
            {
                if (type == StatusEffectType.Poison)
                {
                    periodicDamage.Add(effect.Power);
                }
                effect.NextTick = now.AddMilliseconds(effect.IntervalMs);
            }
        }

        foreach (var exp in expired)
        {
            DetachStatus(exp);
        }
    }
}
