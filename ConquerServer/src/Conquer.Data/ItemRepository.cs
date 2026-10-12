// Conquer.Data — Item, ItemType, and Shop repositories and table mappings.
// Original: cq_item, cq_itemtype, and cq_goods tables.

using System.Data.Common;
using Dapper;
using MySqlConnector;

namespace Conquer.Data;

/// <summary>
/// Data model for the cq_item database table.
/// </summary>
public sealed class ItemModel
{
    public uint Id { get; set; }
    public uint Type { get; set; }
    public uint OwnerId { get; set; }
    public uint PlayerId { get; set; }
    public ushort Amount { get; set; } = 100;
    public ushort AmountLimit { get; set; } = 100;
    public byte Ident { get; set; }
    public byte Position { get; set; }
    public byte Gem1 { get; set; }
    public byte Gem2 { get; set; }
    public byte Magic1 { get; set; }
    public byte Magic2 { get; set; }
    public byte Magic3 { get; set; }
    public int Data { get; set; }
}

/// <summary>
/// Data model for the cq_itemtype database table.
/// </summary>
public sealed class ItemTypeModel
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte ReqProfession { get; set; }
    public byte Level { get; set; }
    public byte ReqLevel { get; set; }
    public byte ReqSex { get; set; }
    public ushort ReqForce { get; set; }
    public ushort ReqSpeed { get; set; }
    public ushort ReqHealth { get; set; }
    public ushort ReqSoul { get; set; }
    public byte Monopoly { get; set; }
    public ushort Weight { get; set; } = 100;
    public uint Price { get; set; }
    public uint IdAction { get; set; }
    public ushort AttackMax { get; set; }
    public ushort AttackMin { get; set; }
    public ushort Defense { get; set; }
    public ushort MagicAtkMin { get; set; }
    public ushort MagicAtkMax { get; set; }
    public ushort MagicDef { get; set; }
    public ushort AddSoul { get; set; }
    public short Life { get; set; }
    public short Mana { get; set; }
    public ushort Amount { get; set; } = 1;
    public ushort AmountLimit { get; set; } = 1;
    public byte Ident { get; set; }
    public byte Gem1 { get; set; }
    public byte Gem2 { get; set; }
    public byte Magic1 { get; set; }
    public byte Magic2 { get; set; }
    public byte Magic3 { get; set; }
    public ushort AtkRange { get; set; } = 1;
    public ushort AtkSpeed { get; set; } = 800;
}

/// <summary>
/// Data model for the cq_goods database table (NPC Shops).
/// </summary>
public sealed class ShopGoodsModel
{
    public uint Id { get; set; }
    public uint OwnerId { get; set; }
    public uint ItemType { get; set; }
}

public interface IItemRepository
{
    Task<ItemModel?> GetItemByIdAsync(uint id);
    Task<IReadOnlyList<ItemModel>> GetItemsByOwnerAsync(uint ownerId);
    Task<uint> InsertItemAsync(ItemModel item);
    Task<bool> UpdateItemAsync(ItemModel item);
    Task<bool> DeleteItemAsync(uint id);
}

public sealed class ItemRepository : IItemRepository
{
    private readonly string _connectionString;

    public ItemRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private DbConnection CreateConnection() => new MySqlConnection(_connectionString);

    public async Task<ItemModel?> GetItemByIdAsync(uint id)
    {
        using var conn = CreateConnection();
        const string sql = @"
            SELECT id AS Id, type AS Type, owner_id AS OwnerId, player_id AS PlayerId,
                   amount AS Amount, amount_limit AS AmountLimit, ident AS Ident,
                   position AS Position, gem1 AS Gem1, gem2 AS Gem2,
                   magic1 AS Magic1, magic2 AS Magic2, magic3 AS Magic3, data AS Data
            FROM cq_item
            WHERE id = @Id;";

        return await conn.QuerySingleOrDefaultAsync<ItemModel>(sql, new { Id = id });
    }

    public async Task<IReadOnlyList<ItemModel>> GetItemsByOwnerAsync(uint ownerId)
    {
        using var conn = CreateConnection();
        const string sql = @"
            SELECT id AS Id, type AS Type, owner_id AS OwnerId, player_id AS PlayerId,
                   amount AS Amount, amount_limit AS AmountLimit, ident AS Ident,
                   position AS Position, gem1 AS Gem1, gem2 AS Gem2,
                   magic1 AS Magic1, magic2 AS Magic2, magic3 AS Magic3, data AS Data
            FROM cq_item
            WHERE owner_id = @OwnerId;";

        var results = await conn.QueryAsync<ItemModel>(sql, new { OwnerId = ownerId });
        return results.AsList();
    }

    public async Task<uint> InsertItemAsync(ItemModel item)
    {
        using var conn = CreateConnection();
        const string sql = @"
            INSERT INTO cq_item (type, owner_id, player_id, amount, amount_limit, ident, position, gem1, gem2, magic1, magic2, magic3, data)
            VALUES (@Type, @OwnerId, @PlayerId, @Amount, @AmountLimit, @Ident, @Position, @Gem1, @Gem2, @Magic1, @Magic2, @Magic3, @Data);
            SELECT LAST_INSERT_ID();";

        uint id = await conn.ExecuteScalarAsync<uint>(sql, item);
        item.Id = id;
        return id;
    }

    public async Task<bool> UpdateItemAsync(ItemModel item)
    {
        using var conn = CreateConnection();
        const string sql = @"
            UPDATE cq_item
            SET amount = @Amount, amount_limit = @AmountLimit, position = @Position,
                ident = @Ident, gem1 = @Gem1, gem2 = @Gem2,
                magic1 = @Magic1, magic2 = @Magic2, magic3 = @Magic3, data = @Data
            WHERE id = @Id;";

        int rows = await conn.ExecuteAsync(sql, item);
        return rows > 0;
    }

    public async Task<bool> DeleteItemAsync(uint id)
    {
        using var conn = CreateConnection();
        const string sql = "DELETE FROM cq_item WHERE id = @Id;";
        int rows = await conn.ExecuteAsync(sql, new { Id = id });
        return rows > 0;
    }
}

public interface IItemTypeRepository
{
    Task<ItemTypeModel?> GetItemTypeByIdAsync(uint id);
    Task<IReadOnlyList<ItemTypeModel>> GetAllItemTypesAsync();
}

public sealed class ItemTypeRepository : IItemTypeRepository
{
    private readonly string _connectionString;

    public ItemTypeRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private DbConnection CreateConnection() => new MySqlConnection(_connectionString);

    public async Task<ItemTypeModel?> GetItemTypeByIdAsync(uint id)
    {
        using var conn = CreateConnection();
        const string sql = @"
            SELECT id AS Id, name AS Name, req_profession AS ReqProfession, level AS Level,
                   req_level AS ReqLevel, req_sex AS ReqSex, req_force AS ReqForce,
                   req_speed AS ReqSpeed, req_health AS ReqHealth, req_soul AS ReqSoul,
                   monopoly AS Monopoly, weight AS Weight, price AS Price, id_action AS IdAction,
                   attack_max AS AttackMax, attack_min AS AttackMin, defense AS Defense,
                   magicatk_min AS MagicAtkMin, magicatk_max AS MagicAtkMax, magic_def AS MagicDef,
                   add_soul AS AddSoul, life AS Life, mana AS Mana, amount AS Amount,
                   amount_limit AS AmountLimit, ident AS Ident, gem1 AS Gem1, gem2 AS Gem2,
                   magic1 AS Magic1, magic2 AS Magic2, magic3 AS Magic3,
                   atk_range AS AtkRange, atk_speed AS AtkSpeed
            FROM cq_itemtype
            WHERE id = @Id;";

        return await conn.QuerySingleOrDefaultAsync<ItemTypeModel>(sql, new { Id = id });
    }

    public async Task<IReadOnlyList<ItemTypeModel>> GetAllItemTypesAsync()
    {
        using var conn = CreateConnection();
        const string sql = @"
            SELECT id AS Id, name AS Name, req_profession AS ReqProfession, level AS Level,
                   req_level AS ReqLevel, req_sex AS ReqSex, req_force AS ReqForce,
                   req_speed AS ReqSpeed, req_health AS ReqHealth, req_soul AS ReqSoul,
                   monopoly AS Monopoly, weight AS Weight, price AS Price, id_action AS IdAction,
                   attack_max AS AttackMax, attack_min AS AttackMin, defense AS Defense,
                   magicatk_min AS MagicAtkMin, magicatk_max AS MagicAtkMax, magic_def AS MagicDef,
                   add_soul AS AddSoul, life AS Life, mana AS Mana, amount AS Amount,
                   amount_limit AS AmountLimit, ident AS Ident, gem1 AS Gem1, gem2 AS Gem2,
                   magic1 AS Magic1, magic2 AS Magic2, magic3 AS Magic3,
                   atk_range AS AtkRange, atk_speed AS AtkSpeed
            FROM cq_itemtype;";

        var results = await conn.QueryAsync<ItemTypeModel>(sql);
        return results.AsList();
    }
}

public interface IShopRepository
{
    Task<IReadOnlyList<ShopGoodsModel>> GetShopGoodsByNpcIdAsync(uint npcId);
}

public sealed class ShopRepository : IShopRepository
{
    private readonly string _connectionString;

    public ShopRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private DbConnection CreateConnection() => new MySqlConnection(_connectionString);

    public async Task<IReadOnlyList<ShopGoodsModel>> GetShopGoodsByNpcIdAsync(uint npcId)
    {
        using var conn = CreateConnection();
        const string sql = @"
            SELECT id AS Id, ownerid AS OwnerId, itemtype AS ItemType
            FROM cq_goods
            WHERE ownerid = @NpcId;";

        var results = await conn.QueryAsync<ShopGoodsModel>(sql, new { NpcId = npcId });
        return results.AsList();
    }
}
