// Conquer.Runtime — NPC Shop and vendor management.
// Original: MapGroupKernel/NpcShop.h and User_Item.cpp CUser::BuyItem, CUser::SellItem

using Conquer.Data;
using Conquer.Shared.Messages.Packets;

namespace Conquer.Runtime;

/// <summary>
/// NPC Shop and goods manager.
/// Original: MapGroupKernel/NpcShop.h and User_Item.cpp CUser::BuyItem, CUser::SellItem.
/// </summary>
public sealed class ShopManager
{
    private readonly Dictionary<uint, HashSet<uint>> _shopGoods = new();

    public void RegisterShop(uint npcId, IEnumerable<uint> goodsItemTypeIds)
    {
        if (!_shopGoods.TryGetValue(npcId, out var set))
        {
            set = [];
            _shopGoods[npcId] = set;
        }

        foreach (var id in goodsItemTypeIds)
        {
            set.Add(id);
        }
    }

    public bool IsSoldByShop(uint npcId, uint itemTypeId)
    {
        return _shopGoods.TryGetValue(npcId, out var goods) && goods.Contains(itemTypeId);
    }

    public IReadOnlyCollection<uint> GetShopGoods(uint npcId)
    {
        if (_shopGoods.TryGetValue(npcId, out var goods))
            return goods;

        return Array.Empty<uint>();
    }

    /// <summary>
    /// Buys an item from an NPC shop.
    /// Original: User_Item.cpp CUser::BuyItem.
    /// </summary>
    public bool BuyItem(
        uint npcId,
        uint itemTypeId,
        Player player,
        Func<uint, ItemTypeModel?> typeLookup,
        Func<uint> nextIdGenerator,
        out Item? item,
        out string reason)
    {
        item = null;

        if (!IsSoldByShop(npcId, itemTypeId))
        {
            reason = "Shop does not sell this item.";
            return false;
        }

        var typeData = typeLookup(itemTypeId);
        if (typeData == null)
        {
            reason = "Item type data not found.";
            return false;
        }

        if (player.Money < typeData.Price)
        {
            reason = "Not enough money to buy item.";
            return false;
        }

        if (player.Package.IsFull)
        {
            reason = "Backpack is full.";
            return false;
        }

        player.Money -= typeData.Price;
        uint newId = nextIdGenerator();
        item = new Item(newId, typeData, ownerId: player.Id, position: ItemPosition.Backpack);
        player.Package.TryAdd(item);

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Sells an item from player backpack to an NPC shop.
    /// Original: User_Item.cpp CUser::SellItem.
    /// </summary>
    public static bool SellItem(
        uint npcId,
        uint itemId,
        Player player,
        out uint moneyEarned,
        out string reason)
    {
        moneyEarned = 0;

        var item = player.Package.GetItem(itemId);
        if (item == null)
        {
            reason = "Item not found in backpack.";
            return false;
        }

        moneyEarned = item.GetSellPrice();
        player.Package.Remove(itemId, out _);
        player.Money += moneyEarned;

        reason = string.Empty;
        return true;
    }
}
