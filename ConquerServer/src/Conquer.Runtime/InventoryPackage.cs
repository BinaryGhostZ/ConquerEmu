// Conquer.Runtime — Player backpack inventory management.
// Original: MapGroupKernel/UserPackage.h and UserPackage.cpp (_MAX_USERITEMSIZE = 40)

using Conquer.Shared.Messages.Packets;

namespace Conquer.Runtime;

/// <summary>
/// Player backpack containing up to 40 items.
/// Original: MapGroupKernel/UserPackage.h CUserPackage (_MAX_USERITEMSIZE = 40).
/// </summary>
public sealed class InventoryPackage
{
    public const int MaxCapacity = 40;

    private readonly Dictionary<uint, Item> _items = new(MaxCapacity);

    public int Count => _items.Count;
    public bool IsFull => _items.Count >= MaxCapacity;
    public IReadOnlyCollection<Item> Items => _items.Values;

    public bool Contains(uint itemId) => _items.ContainsKey(itemId);

    public Item? GetItem(uint itemId) => _items.GetValueOrDefault(itemId);

    public bool TryAdd(Item item)
    {
        if (IsFull)
            return false;

        item.Position = ItemPosition.Backpack;
        _items[item.Id] = item;
        return true;
    }

    public bool Remove(uint itemId) => Remove(itemId, out _);

    public bool Remove(uint itemId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Item? item)
    {
        return _items.Remove(itemId, out item);
    }

    public void Clear()
    {
        _items.Clear();
    }
}
