// Conquer.Data — Account models and repositories.

using System.Data.Common;
using Dapper;
using MySqlConnector;

namespace Conquer.Data;

public sealed class AccountModel
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int NetState { get; set; }
    public string Ip { get; set; } = string.Empty;
}

public interface IAccountRepository
{
    Task<AccountModel?> GetAccountByNameAsync(string name);
    Task UpdateNetStateAsync(uint accountId, int netState);
}

public sealed class AccountRepository : IAccountRepository
{
    private readonly string _connectionString;

    public AccountRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private DbConnection CreateConnection() => new MySqlConnection(_connectionString);

    public async Task<AccountModel?> GetAccountByNameAsync(string name)
    {
        using var conn = CreateConnection();
        // Typically original Conquer uses cq_account or accounts. We will assume cq_account.
        const string sql = @"
            SELECT id AS Id, name AS Name, password AS Password, net_state AS NetState, ip AS Ip
            FROM cq_account
            WHERE name = @Name LIMIT 1;";
            
        return await conn.QueryFirstOrDefaultAsync<AccountModel>(sql, new { Name = name });
    }

    public async Task UpdateNetStateAsync(uint accountId, int netState)
    {
        using var conn = CreateConnection();
        const string sql = @"
            UPDATE cq_account
            SET net_state = @NetState
            WHERE id = @Id;";
            
        await conn.ExecuteAsync(sql, new { Id = accountId, NetState = netState });
    }
}
