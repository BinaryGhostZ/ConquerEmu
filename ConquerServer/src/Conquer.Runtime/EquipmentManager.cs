// Conquer.Runtime — Equipment slots, stat aggregation, and requirement verification.
// Original: MapGroupKernel/User_Item.cpp EquipItem, UnEquipItem, GetEquipItemPtr

using Conquer.Shared.Messages.Packets;

namespace Conquer.Runtime;

/// <summary>
/// Manages player equipment slots 1..11 and aggregates combat stat bonuses.
/// Original: MapGroupKernel/User_Item.cpp ITEMPOSITION_EQUIPBEGIN..ITEMPOSITION_EQUIPEND.
/// </summary>
public sealed class EquipmentManager
{
    private readonly Item?[] _slots = new Item?[12];

    public Item? GetEquip(ItemPosition position)
    {
        int index = (int)position;
        return (index >= 1 && index <= 11) ? _slots[index] : null;
    }

    public IEnumerable<Item> EquippedItems
    {
        get
        {
            for (int i = 1; i <= 11; i++)
            {
                if (_slots[i] != null)
                    yield return _slots[i]!;
            }
        }
    }

    public int BonusMinAtk => EquippedItems.Sum(item => (int)item.TypeData.AttackMin);
    public int BonusMaxAtk => EquippedItems.Sum(item => (int)item.TypeData.AttackMax);
    public int BonusDefense => EquippedItems.Sum(item => (int)item.TypeData.Defense);
    public int BonusMagicAtkMin => EquippedItems.Sum(item => (int)item.TypeData.MagicAtkMin);
    public int BonusMagicAtkMax => EquippedItems.Sum(item => (int)item.TypeData.MagicAtkMax);
    public int BonusMagicDef => EquippedItems.Sum(item => (int)item.TypeData.MagicDef);
    public int BonusLife => EquippedItems.Sum(item => (int)item.TypeData.Life);
    public int BonusMana => EquippedItems.Sum(item => (int)item.TypeData.Mana);

    public int GetAttackRange()
    {
        var weaponR = GetEquip(ItemPosition.WeaponR);
        if (weaponR != null && weaponR.TypeData.AtkRange > 0)
            return weaponR.TypeData.AtkRange;

        return 1; // Default melee range
    }

    public int GetAttackSpeedMs()
    {
        var weaponR = GetEquip(ItemPosition.WeaponR);
        if (weaponR != null && weaponR.TypeData.AtkSpeed > 0)
            return weaponR.TypeData.AtkSpeed;

        return 1000; // Default attack speed
    }

    public static bool CanEquip(Item item, Player player, out string reason)
    {
        if (!item.IsEquipment())
        {
            reason = "Item is not equipable equipment.";
            return false;
        }

        if (player.Level < item.TypeData.ReqLevel)
        {
            reason = $"Requires level {item.TypeData.ReqLevel}, player is level {player.Level}.";
            return false;
        }

        if (item.TypeData.ReqProfession > 0)
        {
            int reqClass = item.TypeData.ReqProfession / 10;
            int playerClass = player.Profession / 10;
            if (reqClass != playerClass || player.Profession < item.TypeData.ReqProfession)
            {
                reason = "Does not meet profession requirement.";
                return false;
            }
        }

        if (item.TypeData.ReqSex > 0 && player.Sex != item.TypeData.ReqSex)
        {
            reason = "Does not meet gender requirement.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool TryEquip(
        Item item,
        ItemPosition targetPos,
        Player player,
        InventoryPackage package,
        out Item? unequipped,
        out string reason)
    {
        unequipped = null;

        if (!CanEquip(item, player, out reason))
            return false;

        // Auto-resolve slot if target position is package or unspecified
        if (targetPos is ItemPosition.None or ItemPosition.Backpack)
        {
            bool hasWeaponR = _slots[(int)ItemPosition.WeaponR] != null;
            targetPos = item.ResolveEquipPosition(hasWeaponR);
            if (targetPos == ItemPosition.None)
            {
                reason = "Cannot resolve valid equipment slot.";
                return false;
            }
        }

        int slotIndex = (int)targetPos;
        if (slotIndex < 1 || slotIndex > 11)
        {
            reason = "Invalid equipment slot index.";
            return false;
        }

        // Remove incoming item from package
        package.Remove(item.Id, out _);

        // If slot currently has an equipped item, move it to unequipped / back to package
        var existing = _slots[slotIndex];
        if (existing != null)
        {
            existing.Position = ItemPosition.Backpack;
            package.TryAdd(existing);
            unequipped = existing;
        }

        // Place into slot
        item.Position = targetPos;
        _slots[slotIndex] = item;
        reason = string.Empty;
        return true;
    }

    public bool TryUnEquip(
        ItemPosition pos,
        InventoryPackage package,
        out Item? unequipped,
        out string reason)
    {
        unequipped = null;
        int slotIndex = (int)pos;
        if (slotIndex < 1 || slotIndex > 11)
        {
            reason = "Invalid equipment slot.";
            return false;
        }

        var item = _slots[slotIndex];
        if (item == null)
        {
            reason = "No item equipped in that slot.";
            return false;
        }

        if (package.IsFull)
        {
            reason = "Backpack is full.";
            return false;
        }

        _slots[slotIndex] = null;
        item.Position = ItemPosition.Backpack;
        package.TryAdd(item);
        unequipped = item;
        reason = string.Empty;
        return true;
    }
}
