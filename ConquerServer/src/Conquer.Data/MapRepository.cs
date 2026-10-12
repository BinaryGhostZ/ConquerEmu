// Conquer.Data — Map repository and cq_map table mapping.
// Original: MapGroupKernel/MapManager.cpp CMapManager::Create
//           SELECT * FROM cq_map WHERE (idxserver=-1 || idxserver=<serverId>) && mapgroup=<groupId>

using System.Data.Common;
using Dapper;
using MySqlConnector;

namespace Conquer.Data;

/// <summary>
/// Data model for the cq_map database table.
/// </summary>
public sealed class MapModel
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint MapDoc { get; set; }
    public uint Type { get; set; }
    public int Portal0X { get; set; }
    public int Portal0Y { get; set; }
    public uint OwnerId { get; set; }
    public int OwnerType { get; set; }
    public int ResourceLev { get; set; }
    public int MapGroup { get; set; }
    public int IdxServer { get; set; }
}

public interface IMapRepository
{
    Task<IReadOnlyList<MapModel>> GetMapsByGroupAsync(int mapGroup, int serverId = -1);
    Task<MapModel?> GetMapByIdAsync(uint id);
    Task<IReadOnlyList<MapModel>> GetAllMapsAsync();
}

public sealed class MapRepository : IMapRepository
{
    private readonly string _connectionString;

    public MapRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private DbConnection CreateConnection() => new MySqlConnection(_connectionString);

    public async Task<IReadOnlyList<MapModel>> GetMapsByGroupAsync(int mapGroup, int serverId = -1)
    {
        using var conn = CreateConnection();
        // Original: MapGroupKernel/MapManager.cpp line 107
        // SELECT * FROM cq_map WHERE (idxserver=-1 || idxserver=@ServerId) && mapgroup=@MapGroup
        const string sql = @"
            SELECT id AS Id, name AS Name, mapdoc AS MapDoc, type AS Type,
                   portal0_x AS Portal0X, portal0_y AS Portal0Y,
                   ownerid AS OwnerId, ownertype AS OwnerType,
                   resource_lev AS ResourceLev, mapgroup AS MapGroup, idxserver AS IdxServer
            FROM cq_map
            WHERE (idxserver = -1 OR idxserver = @ServerId) AND mapgroup = @MapGroup;";

        var results = await conn.QueryAsync<MapModel>(sql, new { ServerId = serverId, MapGroup = mapGroup });
        return results.AsList();
    }

    public async Task<MapModel?> GetMapByIdAsync(uint id)
    {
        using var conn = CreateConnection();
        const string sql = @"
            SELECT id AS Id, name AS Name, mapdoc AS MapDoc, type AS Type,
                   portal0_x AS Portal0X, portal0_y AS Portal0Y,
                   ownerid AS OwnerId, ownertype AS OwnerType,
                   resource_lev AS ResourceLev, mapgroup AS MapGroup, idxserver AS IdxServer
            FROM cq_map
            WHERE id = @Id LIMIT 1;";

        return await conn.QueryFirstOrDefaultAsync<MapModel>(sql, new { Id = id });
    }

    public async Task<IReadOnlyList<MapModel>> GetAllMapsAsync()
    {
        using var conn = CreateConnection();
        const string sql = @"
            SELECT id AS Id, name AS Name, mapdoc AS MapDoc, type AS Type,
                   portal0_x AS Portal0X, portal0_y AS Portal0Y,
                   ownerid AS OwnerId, ownertype AS OwnerType,
                   resource_lev AS ResourceLev, mapgroup AS MapGroup, idxserver AS IdxServer
            FROM cq_map;";

        var results = await conn.QueryAsync<MapModel>(sql);
        return results.AsList();
    }
}
