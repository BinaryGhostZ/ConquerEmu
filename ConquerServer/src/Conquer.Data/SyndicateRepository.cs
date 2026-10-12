// Conquer.Data — Syndicate (Guild) models and repository.
// Original: MapGroupKernel/Syndicate.cpp, SynAttr.cpp, cq_syndicate, cq_synattr

using System.Data.Common;
using Dapper;
using MySqlConnector;

namespace Conquer.Data;

public sealed class SyndicateModel
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint LeaderId { get; set; }
    public string LeaderName { get; set; } = string.Empty;
    public uint FealtySyn { get; set; }
    public long Money { get; set; }
    public byte Rank { get; set; }
}

public sealed class SynAttrModel
{
    public uint Id { get; set; } // user id
    public uint SynId { get; set; }
    public ushort Rank { get; set; }
    public uint Proffer { get; set; }
}

public interface ISyndicateRepository
{
    Task<SyndicateModel?> GetByIdAsync(uint synId);
    Task<SyndicateModel?> GetByNameAsync(string name);
    Task<IReadOnlyList<SyndicateModel>> GetAllAsync();
    Task CreateAsync(SyndicateModel syn);
    Task UpdateMoneyAsync(uint synId, long money);
    Task UpdateLeaderAsync(uint synId, uint leaderId, string leaderName);
    Task DeleteAsync(uint synId);

    Task<SynAttrModel?> GetMemberAttrAsync(uint userId);
    Task SetMemberAttrAsync(SynAttrModel attr);
    Task RemoveMemberAttrAsync(uint userId);
}

public sealed class SyndicateRepository : ISyndicateRepository
{
    private readonly string _connectionString;

    public SyndicateRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private DbConnection CreateConnection() => new MySqlConnection(_connectionString);

    public async Task<SyndicateModel?> GetByIdAsync(uint synId)
    {
        using var conn = CreateConnection();
        const string sql = @"
            SELECT id AS Id, name AS Name, leader_id AS LeaderId, leader_name AS LeaderName,
                   fealty_syn AS FealtySyn, money AS Money, rank AS Rank
            FROM cq_syndicate
            WHERE id = @Id LIMIT 1;";
        return await conn.QueryFirstOrDefaultAsync<SyndicateModel>(sql, new { Id = synId });
    }

    public async Task<SyndicateModel?> GetByNameAsync(string name)
    {
        using var conn = CreateConnection();
        const string sql = @"
            SELECT id AS Id, name AS Name, leader_id AS LeaderId, leader_name AS LeaderName,
                   fealty_syn AS FealtySyn, money AS Money, rank AS Rank
            FROM cq_syndicate
            WHERE name = @Name LIMIT 1;";
        return await conn.QueryFirstOrDefaultAsync<SyndicateModel>(sql, new { Name = name });
    }

    public async Task<IReadOnlyList<SyndicateModel>> GetAllAsync()
    {
        using var conn = CreateConnection();
        const string sql = @"
            SELECT id AS Id, name AS Name, leader_id AS LeaderId, leader_name AS LeaderName,
                   fealty_syn AS FealtySyn, money AS Money, rank AS Rank
            FROM cq_syndicate;";
        var results = await conn.QueryAsync<SyndicateModel>(sql);
        return results.AsList();
    }

    public async Task CreateAsync(SyndicateModel syn)
    {
        using var conn = CreateConnection();
        const string sql = @"
            INSERT INTO cq_syndicate (id, name, leader_id, leader_name, fealty_syn, money, rank)
            VALUES (@Id, @Name, @LeaderId, @LeaderName, @FealtySyn, @Money, @Rank);";
        await conn.ExecuteAsync(sql, syn);
    }

    public async Task UpdateMoneyAsync(uint synId, long money)
    {
        using var conn = CreateConnection();
        const string sql = @"
            UPDATE cq_syndicate
            SET money = @Money
            WHERE id = @Id;";
        await conn.ExecuteAsync(sql, new { Id = synId, Money = money });
    }

    public async Task UpdateLeaderAsync(uint synId, uint leaderId, string leaderName)
    {
        using var conn = CreateConnection();
        const string sql = @"
            UPDATE cq_syndicate
            SET leader_id = @LeaderId, leader_name = @LeaderName
            WHERE id = @Id;";
        await conn.ExecuteAsync(sql, new { Id = synId, LeaderId = leaderId, LeaderName = leaderName });
    }

    public async Task DeleteAsync(uint synId)
    {
        using var conn = CreateConnection();
        const string sql = @"DELETE FROM cq_syndicate WHERE id = @Id;";
        await conn.ExecuteAsync(sql, new { Id = synId });
    }

    public async Task<SynAttrModel?> GetMemberAttrAsync(uint userId)
    {
        using var conn = CreateConnection();
        const string sql = @"
            SELECT id AS Id, syn_id AS SynId, rank AS Rank, proffer AS Proffer
            FROM cq_synattr
            WHERE id = @Id LIMIT 1;";
        return await conn.QueryFirstOrDefaultAsync<SynAttrModel>(sql, new { Id = userId });
    }

    public async Task SetMemberAttrAsync(SynAttrModel attr)
    {
        using var conn = CreateConnection();
        const string sql = @"
            INSERT INTO cq_synattr (id, syn_id, rank, proffer)
            VALUES (@Id, @SynId, @Rank, @Proffer)
            ON DUPLICATE KEY UPDATE syn_id = @SynId, rank = @Rank, proffer = @Proffer;";
        await conn.ExecuteAsync(sql, attr);
    }

    public async Task RemoveMemberAttrAsync(uint userId)
    {
        using var conn = CreateConnection();
        const string sql = @"DELETE FROM cq_synattr WHERE id = @Id;";
        await conn.ExecuteAsync(sql, new { Id = userId });
    }
}
