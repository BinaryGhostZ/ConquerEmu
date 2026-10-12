// Conquer.Data — Friend list models and repository.
// Original: MapGroupKernel/User.cpp, WorldKernel/MsgFriend.cpp, cq_friend

using System.Data.Common;
using Dapper;
using MySqlConnector;

namespace Conquer.Data;

public sealed class FriendModel
{
    public uint Id { get; set; }
    public uint UserId { get; set; }
    public uint FriendId { get; set; }
    public string FriendName { get; set; } = string.Empty;
}

public interface IFriendRepository
{
    Task<IReadOnlyList<FriendModel>> GetFriendsAsync(uint userId);
    Task AddFriendAsync(uint userId, uint friendId, string friendName);
    Task RemoveFriendAsync(uint userId, uint friendId);
}

public sealed class FriendRepository : IFriendRepository
{
    private readonly string _connectionString;

    public FriendRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private DbConnection CreateConnection() => new MySqlConnection(_connectionString);

    public async Task<IReadOnlyList<FriendModel>> GetFriendsAsync(uint userId)
    {
        using var conn = CreateConnection();
        const string sql = @"
            SELECT id AS Id, userid AS UserId, friend AS FriendId, friendname AS FriendName
            FROM cq_friend
            WHERE userid = @UserId;";
        var results = await conn.QueryAsync<FriendModel>(sql, new { UserId = userId });
        return results.AsList();
    }

    public async Task AddFriendAsync(uint userId, uint friendId, string friendName)
    {
        using var conn = CreateConnection();
        const string sql = @"
            INSERT INTO cq_friend (userid, friend, friendname)
            VALUES (@UserId, @FriendId, @FriendName);";
        await conn.ExecuteAsync(sql, new { UserId = userId, FriendId = friendId, FriendName = friendName });
    }

    public async Task RemoveFriendAsync(uint userId, uint friendId)
    {
        using var conn = CreateConnection();
        const string sql = @"
            DELETE FROM cq_friend
            WHERE userid = @UserId AND friend = @FriendId;";
        await conn.ExecuteAsync(sql, new { UserId = userId, FriendId = friendId });
    }
}
