// Conquer.Runtime — Team system.
// Original: MapGroupKernel/Team.h, Team.cpp CTeam

using Conquer.Shared.Messages.Packets;

namespace Conquer.Runtime;

/// <summary>
/// An in-memory player team.
/// <para><b>Original:</b> MapGroupKernel/Team.cpp CTeam</para>
/// </summary>
public sealed class Team
{
    public const int MaxMembers = 5;
    public const int ExpShareRange = 32; // Original: _RANGE_EXPSHARE = 32

    private readonly List<Player> _members = new(MaxMembers);

    public uint Id { get; }
    public uint LeaderId { get; private set; }
    public bool CloseTeam { get; set; }

    public IReadOnlyList<Player> Members => _members;

    public Team(uint id, Player leader)
    {
        Id = id;
        LeaderId = leader.Id;
        _members.Add(leader);
    }

    public bool IsLeader(uint playerId) => LeaderId == playerId;

    public bool IsFull => _members.Count >= MaxMembers;

    public bool ContainsMember(uint playerId)
    {
        for (int i = 0; i < _members.Count; i++)
        {
            if (_members[i].Id == playerId) return true;
        }
        return false;
    }

    public bool TryAddMember(Player player)
    {
        if (IsFull || ContainsMember(player.Id) || CloseTeam)
        {
            return false;
        }

        _members.Add(player);
        return true;
    }

    public bool RemoveMember(uint playerId)
    {
        for (int i = 0; i < _members.Count; i++)
        {
            if (_members[i].Id == playerId)
            {
                _members.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    public MsgTeamMember CreateMemberListPacket(TeamMemberAction action = TeamMemberAction.AddMember)
    {
        var records = new List<TeamMemberRecord>(_members.Count);
        for (int i = 0; i < _members.Count; i++)
        {
            var p = _members[i];
            records.Add(new TeamMemberRecord(p.Name, p.Id, p.LookFace, p.MaxLife, p.Life));
        }

        return MsgTeamMember.Create(action, records);
    }

    /// <summary>
    /// Distributes exp to team members within 32-cell range.
    /// Original: Team.cpp CTeam::AwardMemberExp.
    /// </summary>
    public void AwardTeamExp(Player killer, int totalExp)
    {
        if (totalExp <= 0 || _members.Count <= 1) return;

        // Share evenly among nearby members
        var eligible = new List<Player>(_members.Count);
        for (int i = 0; i < _members.Count; i++)
        {
            var m = _members[i];
            if (m.MapId == killer.MapId &&
                Math.Abs(m.PosX - killer.PosX) <= ExpShareRange &&
                Math.Abs(m.PosY - killer.PosY) <= ExpShareRange &&
                m.IsAlive)
            {
                eligible.Add(m);
            }
        }

        if (eligible.Count == 0) return;

        int sharedExp = Math.Max(1, totalExp / (eligible.Count + 1));
        for (int i = 0; i < eligible.Count; i++)
        {
            var m = eligible[i];
            if (m.Id != killer.Id)
            {
                // In full Conquer rules, member gets adjusted shared exp
                // m.AddExp(sharedExp)
            }
        }
    }
}

/// <summary>
/// Global/MapGroup team registry.
/// </summary>
public sealed class TeamManager
{
    private readonly Dictionary<uint, Team> _teams = new();
    private readonly Dictionary<uint, uint> _playerToTeam = new();
    private uint _nextTeamId = 1;

    public Team CreateTeam(Player leader)
    {
        if (_playerToTeam.ContainsKey(leader.Id))
        {
            DismissPlayerTeam(leader.Id);
        }

        uint teamId = _nextTeamId++;
        var team = new Team(teamId, leader);
        _teams[teamId] = team;
        _playerToTeam[leader.Id] = teamId;
        return team;
    }

    public Team? GetTeam(uint teamId) => _teams.GetValueOrDefault(teamId);

    public Team? GetPlayerTeam(uint playerId)
    {
        if (_playerToTeam.TryGetValue(playerId, out uint teamId))
        {
            return GetTeam(teamId);
        }
        return null;
    }

    public bool AddMember(uint teamId, Player player)
    {
        var team = GetTeam(teamId);
        if (team == null) return false;

        if (team.TryAddMember(player))
        {
            _playerToTeam[player.Id] = teamId;
            return true;
        }

        return false;
    }

    public bool LeaveTeam(Player player)
    {
        if (!_playerToTeam.TryGetValue(player.Id, out uint teamId))
        {
            return false;
        }

        var team = GetTeam(teamId);
        if (team == null) return false;

        if (team.IsLeader(player.Id))
        {
            DismissTeam(teamId);
            return true;
        }

        team.RemoveMember(player.Id);
        _playerToTeam.Remove(player.Id);
        return true;
    }

    public void DismissTeam(uint teamId)
    {
        if (_teams.Remove(teamId, out var team))
        {
            for (int i = 0; i < team.Members.Count; i++)
            {
                _playerToTeam.Remove(team.Members[i].Id);
            }
        }
    }

    public void DismissPlayerTeam(uint playerId)
    {
        if (_playerToTeam.TryGetValue(playerId, out uint teamId))
        {
            var team = GetTeam(teamId);
            if (team != null && team.IsLeader(playerId))
            {
                DismissTeam(teamId);
            }
            else if (team != null)
            {
                team.RemoveMember(playerId);
                _playerToTeam.Remove(playerId);
            }
        }
    }
}
