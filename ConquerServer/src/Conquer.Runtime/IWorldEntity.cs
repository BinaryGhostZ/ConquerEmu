// Conquer.Runtime — World entity interface for players, monsters, and NPCs.
// Original: MapGroupKernel/I_Role.h IRole

using Conquer.Shared;

namespace Conquer.Runtime;

/// <summary>
/// Common interface for all dynamic entities that occupy a position on a GameMap.
/// <para><b>Original:</b> MapGroupKernel/I_Role.h IRole / IMapThing</para>
/// </summary>
public interface IWorldEntity
{
    /// <summary>Unique entity ID.</summary>
    uint Id { get; }

    /// <summary>Display name.</summary>
    string Name { get; }

    /// <summary>ID of the current map.</summary>
    uint MapId { get; set; }

    /// <summary>Current X coordinate.</summary>
    int PosX { get; set; }

    /// <summary>Current Y coordinate.</summary>
    int PosY { get; set; }

    /// <summary>Facing direction (0..7).</summary>
    Direction Direction { get; set; }

    /// <summary>True if this entity is a player controlled by a client socket.</summary>
    bool IsPlayer { get; }

    /// <summary>Character/monster level.</summary>
    ushort Level { get; set; }

    /// <summary>Current life / HP.</summary>
    ushort Life { get; set; }

    /// <summary>Maximum life / HP.</summary>
    ushort MaxLife { get; set; }

    /// <summary>Current mana / MP.</summary>
    ushort Mana { get; set; }

    /// <summary>Maximum mana / MP.</summary>
    ushort MaxMana { get; set; }

    /// <summary>Magic defense. Original: GetMagicDef().</summary>
    int MagicDef { get; set; }

    /// <summary>Status effects and keep effect flags manager.</summary>
    StatusManager Statuses { get; }

    /// <summary>Minimum physical attack power. Original: GetMinAtk().</summary>
    int MinAtk { get; set; }

    /// <summary>Maximum physical attack power. Original: GetMaxAtk().</summary>
    int MaxAtk { get; set; }

    /// <summary>Physical defense. Original: GetDefense().</summary>
    int Defense { get; set; }

    /// <summary>Attack reach in cells (Chebyshev distance). Melee default is 1.</summary>
    int AttackRange { get; set; }

    /// <summary>Weapon attack interval in milliseconds. Melee default is 1000ms.</summary>
    int AttackSpeedMs { get; set; }

    /// <summary>Current combat target entity ID (0 if none).</summary>
    uint TargetId { get; set; }

    /// <summary>Timestamp of the last attack performed.</summary>
    DateTime LastAttackTime { get; set; }

    /// <summary>True if entity has positive life.</summary>
    bool IsAlive => Life > 0;
}
