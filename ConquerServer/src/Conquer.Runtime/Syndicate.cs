// Conquer.Runtime — Syndicate (Guild) entity and manager.
// Original: MapGroupKernel/Syndicate.h, Syndicate.cpp CSyndicate

using Conquer.Shared.Messages.Packets;

namespace Conquer.Runtime;

/// <summary>
/// Syndicate rank hierarchy.
/// <para><b>Original:</b> Share/define.h SYNRANK_*</para>
/// </summary>
public enum SyndicateRank : ushort
{
    None             = 0,
    Member           = 10,
    TeamLeader       = 20,
    Manager          = 70,
    HonoraryManager  = 80,
    DeputyLeader     = 90,
    Leader           = 100
}

/// <summary>
/// An in-memory syndicate instance.
/// <para><b>Original:</b> MapGroupKernel/Syndicate.cpp CSyndicate</para>
/// </summary>
public sealed class Syndicate
{
    private readonly Dictionary<uint, ushort> _members = new();
    private readonly HashSet<uint> _allies = new();
    private readonly HashSet<uint> _enemies = new();

    public uint Id { get; }
    public string Name { get; }
    public uint LeaderId { get; private set; }
    public string LeaderName { get; private set; }
    public uint FealtyId { get; set; }
    public long Money { get; private set; }
    public byte Rank { get; set; } = 1;
    public byte Saint { get; set; } = 0;

    public IReadOnlyDictionary<uint, ushort> Members => _members;
    public IReadOnlySet<uint> Allies => _allies;
    public IReadOnlySet<uint> Enemies => _enemies;

    public Syndicate(uint id, string name, Player leader)
    {
        Id = id;
        Name = name;
        LeaderId = leader.Id;
        LeaderName = leader.Name;
        _members[leader.Id] = (ushort)SyndicateRank.Leader;
    }

    public Syndicate(uint id, string name, uint leaderId, string leaderName, long money)
    {
        Id = id;
        Name = name;
        LeaderId = leaderId;
        LeaderName = leaderName;
        Money = money;
        _members[leaderId] = (ushort)SyndicateRank.Leader;
    }

    public bool IsLeader(uint playerId) => LeaderId == playerId;

    public ushort GetMemberRank(uint playerId) => _members.GetValueOrDefault(playerId, (ushort)SyndicateRank.None);

    public void AddMember(uint playerId, ushort rank = (ushort)SyndicateRank.Member)
    {
        _members[playerId] = rank;
    }

    public bool RemoveMember(uint playerId)
    {
        if (playerId == LeaderId) return false;
        return _members.Remove(playerId);
    }

    public void SetRank(uint playerId, ushort rank)
    {
        if (_members.ContainsKey(playerId))
        {
            _members[playerId] = rank;
        }
    }

    public void SetLeader(uint newLeaderId, string newLeaderName)
    {
        if (_members.ContainsKey(newLeaderId))
        {
            _members[LeaderId] = (ushort)SyndicateRank.Member;
            LeaderId = newLeaderId;
            LeaderName = newLeaderName;
            _members[newLeaderId] = (ushort)SyndicateRank.Leader;
        }
    }

    public void Donate(uint amount)
    {
        Money += amount;
    }

    public void AddAlly(uint synId) => _allies.Add(synId);
    public void RemoveAlly(uint synId) => _allies.Remove(synId);
    public bool IsAlly(uint synId) => _allies.Contains(synId);

    public void AddEnemy(uint synId) => _enemies.Add(synId);
    public void RemoveEnemy(uint synId) => _enemies.Remove(synId);
    public bool IsEnemy(uint synId) => _enemies.Contains(synId);

    public MsgSynInfo CreateSynInfoPacket()
    {
        return MsgSynInfo.Create(
            synId: Id,
            fealtyId: FealtyId,
            fund: (uint)Math.Min(Money, uint.MaxValue),
            population: (uint)_members.Count,
            rank: Rank,
            saint: Saint,
            name: Name,
            leaderName: LeaderName);
    }
}

/// <summary>
/// In-memory syndicate manager.
/// </summary>
public sealed class SyndicateManager
{
    private readonly Dictionary<uint, Syndicate> _syndicatesById = new();
    private readonly Dictionary<string, Syndicate> _syndicatesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, uint> _playerToSyndicate = new();
    private uint _nextSynId = 1;

    public Syndicate? GetSyndicate(uint synId) => _syndicatesById.GetValueOrDefault(synId);

    public Syndicate? GetSyndicate(string name) => _syndicatesByName.GetValueOrDefault(name);

    public Syndicate? GetPlayerSyndicate(uint playerId)
    {
        if (_playerToSyndicate.TryGetValue(playerId, out uint synId))
        {
            return GetSyndicate(synId);
        }
        return null;
    }

    public Syndicate? CreateSyndicate(string name, Player leader)
    {
        if (string.IsNullOrWhiteSpace(name) || _syndicatesByName.ContainsKey(name))
        {
            return null;
        }

        if (_playerToSyndicate.ContainsKey(leader.Id))
        {
            return null;
        }

        uint synId = _nextSynId++;
        var syn = new Syndicate(synId, name, leader);
        _syndicatesById[synId] = syn;
        _syndicatesByName[name] = syn;
        _playerToSyndicate[leader.Id] = synId;
        leader.SynIdRank = synId | ((uint)SyndicateRank.Leader << 16);
        return syn;
    }

    public bool JoinSyndicate(uint synId, Player player)
    {
        var syn = GetSyndicate(synId);
        if (syn == null) return false;

        if (_playerToSyndicate.ContainsKey(player.Id))
        {
            return false;
        }

        syn.AddMember(player.Id, (ushort)SyndicateRank.Member);
        _playerToSyndicate[player.Id] = synId;
        player.SynIdRank = synId | ((uint)SyndicateRank.Member << 16);
        return true;
    }

    public bool LeaveSyndicate(Player player)
    {
        if (!_playerToSyndicate.TryGetValue(player.Id, out uint synId))
        {
            return false;
        }

        var syn = GetSyndicate(synId);
        if (syn == null) return false;

        // Leader cannot simply leave without demising first
        if (syn.IsLeader(player.Id))
        {
            return false;
        }

        syn.RemoveMember(player.Id);
        _playerToSyndicate.Remove(player.Id);
        player.SynIdRank = 0;
        return true;
    }

    public bool DonateMoney(Player player, uint amount)
    {
        if (amount == 0 || player.Money < amount) return false;

        var syn = GetPlayerSyndicate(player.Id);
        if (syn == null) return false;

        player.Money -= amount;
        syn.Donate(amount);
        return true;
    }

    public bool KickMember(uint synId, uint kickerId, uint targetId)
    {
        var syn = GetSyndicate(synId);
        if (syn == null) return false;

        ushort kickerRank = syn.GetMemberRank(kickerId);
        ushort targetRank = syn.GetMemberRank(targetId);

        // Kicker must be higher rank (e.g. Leader)
        if (kickerRank <= targetRank || kickerRank < (ushort)SyndicateRank.DeputyLeader)
        {
            return false;
        }

        if (syn.RemoveMember(targetId))
        {
            _playerToSyndicate.Remove(targetId);
            return true;
        }

        return false;
    }
}
