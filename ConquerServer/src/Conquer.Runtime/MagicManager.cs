// Conquer.Runtime — Player magic, spellbook, cooldowns, and XP accumulation.
// Original: MapGroupKernel/Magic.h, Magic.cpp, MagicData.cpp, User.cpp (ProcessAutoAttack, ResetEnergyInc)

using Conquer.Data;

namespace Conquer.Runtime;

/// <summary>
/// Spell archetypes from original Conquer Online.
/// Original: MapGroupKernel/Magic.h MAGICSORT_*.
/// </summary>
public enum MagicSort : ushort
{
    Attack          = 1,  // Single-target damage
    Recruit         = 2,  // Healing / Cure
    Cross           = 3,  // Cross-pattern AoE
    Fan             = 4,  // Fan/Cone AoE
    Bomb            = 5,  // Point explosion AoE
    AttachStatus    = 6,  // Buff / status attachment
    DetachStatus    = 7,  // Debuff removal / cleanse
    Square          = 8,  // Square AoE
    JumpAttack      = 9,
    RandomTrans     = 10,
    DispatchXp      = 11,
    Collide         = 12,
    Line            = 14, // Line AoE
    AddMana         = 20, // Mana restoration
    DecLife         = 26, // Percentage life loss
}

/// <summary>
/// A learned spell instance in a player's spellbook.
/// Original: MapGroupKernel/MagicData.h CMagicData.
/// </summary>
public sealed class LearnedMagic
{
    public MagicModel Model { get; }
    public MagicTypeModel TypeData { get; private set; }

    public ushort Type => Model.Type;
    public byte Level
    {
        get => Model.Level;
        set => Model.Level = value;
    }
    public uint Exp
    {
        get => Model.Exp;
        set => Model.Exp = value;
    }

    public LearnedMagic(MagicModel model, MagicTypeModel typeData)
    {
        Model = model;
        TypeData = typeData;
    }

    /// <summary>
    /// Awards spell experience and levels up the spell if threshold is reached.
    /// Original: Magic.cpp AwardExp.
    /// </summary>
    public bool AddExp(uint amount, Func<ushort, byte, MagicTypeModel?> typeLookup, out bool leveledUp)
    {
        leveledUp = false;
        if (TypeData.NeedExp == 0)
            return false;

        Exp += amount;
        if (Exp >= TypeData.NeedExp)
        {
            byte nextLevel = (byte)(Level + 1);
            var nextType = typeLookup(Type, nextLevel);
            if (nextType != null)
            {
                Level = nextLevel;
                Exp -= TypeData.NeedExp;
                TypeData = nextType;
                leveledUp = true;
            }
        }
        return true;
    }
}

/// <summary>
/// Manages player learned spells, spell cooldowns, and XP accumulation.
/// Original: MapGroupKernel/Magic.cpp, User.cpp.
/// </summary>
public sealed class MagicManager
{
    private readonly Dictionary<ushort, LearnedMagic> _spells = [];
    private readonly Dictionary<ushort, DateTime> _cooldowns = [];

    public IReadOnlyCollection<LearnedMagic> Spells => _spells.Values;

    public byte Xp { get; private set; }
    public const byte MaxXp = 100;

    public DateTime LastXpTick { get; set; } = DateTime.UtcNow;

    public bool HasSpell(ushort magicType) => _spells.ContainsKey(magicType);

    public LearnedMagic? GetSpell(ushort magicType) => _spells.GetValueOrDefault(magicType);

    public void LearnSpell(MagicModel model, MagicTypeModel typeData)
    {
        _spells[model.Type] = new LearnedMagic(model, typeData);
    }

    public bool IsCoolingDown(ushort magicType, DateTime now)
    {
        if (_cooldowns.TryGetValue(magicType, out var expiry))
        {
            return now < expiry;
        }
        return false;
    }

    public void SetCooldown(ushort magicType, int delayMs, DateTime now)
    {
        if (delayMs > 0)
        {
            _cooldowns[magicType] = now.AddMilliseconds(delayMs);
        }
    }

    /// <summary>
    /// Accumulates XP points (e.g. periodically or on attacks) up to MaxXp (100).
    /// </summary>
    public bool AddXp(byte amount, out bool reachedFull)
    {
        reachedFull = false;
        if (Xp >= MaxXp)
            return false;

        Xp = (byte)Math.Min(MaxXp, Xp + amount);
        if (Xp >= MaxXp)
        {
            reachedFull = true;
        }
        return true;
    }

    /// <summary>
    /// Consumes all XP points when activating an XP skill (Superman, Cyclone).
    /// </summary>
    public void ResetXp()
    {
        Xp = 0;
    }
}
