// Conquer.Data — Magic and spell repository layer for cq_magic and cq_magictype.
// Original: MapGroupKernel/MagicData.h, MagicType.h

using System.Collections.Concurrent;

namespace Conquer.Data;

/// <summary>
/// Data model for cq_magic (learned player magic spells).
/// Original: MapGroupKernel/MagicData.h MAGICDATA_*.
/// </summary>
public sealed class MagicModel
{
    public uint Id { get; set; }
    public uint OwnerId { get; set; }
    public ushort Type { get; set; }
    public byte Level { get; set; }
    public uint Exp { get; set; }
    public byte Unlearn { get; set; }
    public byte OldLevel { get; set; }
}

/// <summary>
/// Data model for cq_magictype (archetype templates for magic spells).
/// Original: MapGroupKernel/MagicType.h MAGICTYPEDATA_*.
/// </summary>
public sealed class MagicTypeModel
{
    public uint Id { get; set; }
    public ushort Type { get; set; }
    public ushort Sort { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte Crime { get; set; }
    public byte Ground { get; set; }
    public byte Multi { get; set; }
    public byte Target { get; set; }
    public byte Level { get; set; }
    public ushort UseMp { get; set; }
    public int Power { get; set; }
    public ushort Intone { get; set; }
    public ushort Percent { get; set; } = 100;
    public ushort Step { get; set; }         // Duration (secs) or radius
    public ushort Range { get; set; }        // AoE radius
    public ushort Distance { get; set; }     // Cast distance (cells)
    public uint Status { get; set; }         // Status ID attached/detached
    public byte StatusChance { get; set; }   // Percent chance to attach status
    public ushort NeedProfMask { get; set; }
    public uint NeedExp { get; set; }
    public byte NeedLevel { get; set; }
    public byte UseXp { get; set; }          // 1 if requires XP
    public ushort WeaponSubtype { get; set; }
    public ushort Delay { get; set; } = 1000;// Cooldown in milliseconds
}

public interface IMagicRepository
{
    Task<List<MagicModel>> GetByOwnerIdAsync(uint ownerId);
    Task<MagicModel?> GetAsync(uint id);
    Task<bool> AddAsync(MagicModel magic);
    Task<bool> UpdateAsync(MagicModel magic);
    Task<bool> DeleteAsync(uint id);
}

public interface IMagicTypeRepository
{
    MagicTypeModel? Get(ushort type, byte level);
    Task<MagicTypeModel?> GetByTypeAndLevelAsync(ushort type, byte level);
    Task<List<MagicTypeModel>> GetAllAsync();
    void Register(MagicTypeModel model);
}

public sealed class MagicRepository : IMagicRepository
{
    private readonly ConcurrentDictionary<uint, MagicModel> _magics = new();

    public Task<List<MagicModel>> GetByOwnerIdAsync(uint ownerId)
    {
        var result = _magics.Values
            .Where(m => m.OwnerId == ownerId && m.Unlearn == 0)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<MagicModel?> GetAsync(uint id)
    {
        _magics.TryGetValue(id, out var magic);
        return Task.FromResult(magic);
    }

    public Task<bool> AddAsync(MagicModel magic)
    {
        return Task.FromResult(_magics.TryAdd(magic.Id, magic));
    }

    public Task<bool> UpdateAsync(MagicModel magic)
    {
        _magics[magic.Id] = magic;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(uint id)
    {
        return Task.FromResult(_magics.TryRemove(id, out _));
    }
}

public sealed class MagicTypeRepository : IMagicTypeRepository
{
    private readonly ConcurrentDictionary<(ushort Type, byte Level), MagicTypeModel> _templates = new();

    public MagicTypeModel? Get(ushort type, byte level)
    {
        _templates.TryGetValue((type, level), out var model);
        return model;
    }

    public Task<MagicTypeModel?> GetByTypeAndLevelAsync(ushort type, byte level)
    {
        _templates.TryGetValue((type, level), out var model);
        return Task.FromResult(model);
    }

    public Task<List<MagicTypeModel>> GetAllAsync()
    {
        return Task.FromResult(_templates.Values.ToList());
    }

    public void Register(MagicTypeModel model)
    {
        _templates[(model.Type, model.Level)] = model;
    }
}
