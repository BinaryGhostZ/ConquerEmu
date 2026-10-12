// Conquer.Runtime — Combat calculation and battle engine.
// Original: MapGroupKernel/BattleSystem.h, BattleSystem.cpp CBattleSystem::CalcPower & CalcAttackPower
using Conquer.Shared;

namespace Conquer.Runtime;

/// <summary>
/// Authoritative combat calculation engine.
/// <para>
/// <b>Original:</b> MapGroupKernel/BattleSystem.cpp CBattleSystem::CalcPower, CalcAttackPower,
/// CalcDamageUser2Monster, CalcDamageMonster2User.
/// </para>
/// </summary>
public static class BattleEngine
{
    /// <summary>
    /// Calculate damage dealt by an attacker to a target entity.
    /// <para><b>Original:</b> MapGroupKernel/BattleSystem.cpp CBattleSystem::CalcAttackPower</para>
    /// </summary>
    /// <param name="attacker">Attacking entity.</param>
    /// <param name="target">Target entity being attacked.</param>
    /// <param name="random">Optional random instance for testing determinism.</param>
    /// <returns>Non-negative damage amount (minimum 1).</returns>
    public static int CalculateDamage(IWorldEntity attacker, IWorldEntity target, Random? random = null)
    {
        random ??= Random.Shared;

        // Flying target: melee non-flying attacker cannot hit (Original: BattleSystem.cpp)
        if (target.Statuses.Flags.HasFlag(KeepEffectFlags.Fly) &&
            !attacker.Statuses.Flags.HasFlag(KeepEffectFlags.Fly) &&
            attacker.AttackRange <= CombatConstants.DefaultMeleeRange)
        {
            return 0;
        }

        int minAtk = Math.Max(0, attacker.MinAtk);
        int maxAtk = Math.Max(minAtk, attacker.MaxAtk);
        int spread = maxAtk - minAtk;

        int rawAtk;
        if (spread == 0)
        {
            rawAtk = minAtk;
        }
        else
        {
            // Original: 50% chance to roll in the upper half [max - (spread/2), max],
            // 50% chance to roll in lower half [min, min + (spread/2)].
            int halfSpread = spread / 2;
            if (random.Next(100) < 50)
            {
                rawAtk = maxAtk - random.Next(halfSpread + 1);
            }
            else
            {
                rawAtk = minAtk + random.Next(halfSpread + 1);
            }
        }

        // Attacker status modifiers (Superman = 10x vs monster, 3x vs player; Cyclone = 2x)
        if (attacker.Statuses.HasStatus(StatusEffectType.Superman))
        {
            rawAtk = target.IsPlayer ? rawAtk * 3 : rawAtk * 10;
        }
        else if (attacker.Statuses.HasStatus(StatusEffectType.Cyclone))
        {
            rawAtk *= 2;
        }

        int targetDef = Math.Max(0, target.Defense);
        int damage = rawAtk - targetDef;

        // Level difference adjustments (Original: CalcDamageUser2Monster / CalcDamageMonster2User)
        if (attacker.IsPlayer && !target.IsPlayer)
        {
            int atkLev = Math.Min(120, (int)attacker.Level);
            int defLev = target.Level;
            int deltaLev = atkLev - defLev;

            if (deltaLev > 0)
            {
                // Level advantage bonus multiplier (BattleSystem.cpp:607-621)
                double multiplier = deltaLev switch
                {
                    >= 20 => 3.0,
                    >= 10 => 2.5,
                    >= 5  => 2.0,
                    _     => 1.5
                };
                damage = (int)(rawAtk * multiplier) - targetDef;
            }

            int minDamage = 1 + attacker.Level / 10;
            damage = Math.Max(minDamage, damage);
        }
        else if (!attacker.IsPlayer && target.IsPlayer)
        {
            int minDamage = 1;
            damage = Math.Max(minDamage, damage);
        }
        else
        {
            // PvP or baseline: clamp to minimum 1
            damage = Math.Max(1, damage);
        }

        // Target defensive status modifiers (Shield / Defense1 / SuperDef reduce damage by 30%)
        if (target.Statuses.HasStatus(StatusEffectType.Shield) ||
            target.Statuses.HasStatus(StatusEffectType.Defense1) ||
            target.Statuses.HasStatus(StatusEffectType.SuperDef))
        {
            damage = (int)(damage * 0.7);
        }

        return Math.Max(1, damage);
    }

    /// <summary>
    /// Calculate damage dealt by an attacker to a target entity using spell / magic power.
    /// <para>
    /// <b>Original:</b> MapGroupKernel/Magic.cpp CMagic::CalcMagicPower, BattleSystem.cpp CalcPower.
    /// </para>
    /// </summary>
    public static int CalculateMagicDamage(IWorldEntity attacker, IWorldEntity target, int magicPower, Random? random = null)
    {
        random ??= Random.Shared;

        int rawAtk = magicPower > 0 ? magicPower : (attacker.MinAtk + attacker.MaxAtk) / 2;

        // Attacker status modifiers
        if (attacker.Statuses.HasStatus(StatusEffectType.Superman))
        {
            rawAtk = target.IsPlayer ? rawAtk * 3 : rawAtk * 10;
        }
        else if (attacker.Statuses.HasStatus(StatusEffectType.Cyclone))
        {
            rawAtk *= 2;
        }

        int targetDef = Math.Max(0, target.MagicDef);
        int damage = rawAtk - targetDef;

        // Level difference adjustments
        if (attacker.IsPlayer && !target.IsPlayer)
        {
            int deltaLev = Math.Min(120, (int)attacker.Level) - target.Level;
            if (deltaLev > 0)
            {
                double multiplier = deltaLev switch
                {
                    >= 20 => 3.0,
                    >= 10 => 2.5,
                    >= 5  => 2.0,
                    _     => 1.5
                };
                damage = (int)(rawAtk * multiplier) - targetDef;
            }

            int minDamage = 1 + attacker.Level / 10;
            damage = Math.Max(minDamage, damage);
        }
        else if (!attacker.IsPlayer && target.IsPlayer)
        {
            damage = Math.Max(1, damage);
        }
        else
        {
            damage = Math.Max(1, damage);
        }

        // Target defensive status modifiers
        if (target.Statuses.HasStatus(StatusEffectType.Shield) ||
            target.Statuses.HasStatus(StatusEffectType.Defense1) ||
            target.Statuses.HasStatus(StatusEffectType.SuperDef))
        {
            damage = (int)(damage * 0.7);
        }

        return Math.Max(1, damage);
    }
}
